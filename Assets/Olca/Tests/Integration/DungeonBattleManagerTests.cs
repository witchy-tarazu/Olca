using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Linq;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class DungeonBattleManagerTests
    {
        [Test]
        public void 検証保存ガードは専用領域だけ許可し模擬既定と領域外と未指定を無書込で拒否する()
        {
            string container = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-storage-guard-" + System.Guid.NewGuid().ToString("N"));
            string own = System.IO.Path.Combine(container, "own"), mockDefault = System.IO.Path.Combine(container, "mock-default");
            var guard = new Olca.Unity.ValidationStorageGuard(own, mockDefault);
            var io = new Olca.Unity.CheckpointFileIo(guard);
            try {
                foreach (string target in new[] { mockDefault, System.IO.Path.Combine(container,"own-other"), System.IO.Path.Combine(own,"..","outside") }) {
                    Assert.That(new Olca.Unity.AudioVolumeFileStore(target, io).TrySave(new Olca.Unity.AudioVolumeSettings(99),out _), Is.False);
                    Assert.That(new Olca.Unity.TransformationPresentationFileStore(target, io).TrySave(true,out _), Is.False);
                    Assert.That(new Olca.Unity.DialogueTextSettingsStore(target, io).TrySave(DialogueTextSpeed.Fast,out _), Is.False);
                    Assert.That(new Olca.Unity.KeyboardBindingFileStore(target, io).TrySave(Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile(),out _), Is.False);
                }
                Assert.That(System.IO.Directory.Exists(container), Is.False, "拒否先にはディレクトリさえ生成しない");
                Assert.Throws<System.IO.IOException>(() => guard.Require((string)null));
                if(System.IO.Path.DirectorySeparatorChar == '/')
                    Assert.Throws<System.IO.IOException>(() => guard.Require(System.IO.Path.Combine(container,"OWN","settings")));
                Assert.Throws<System.IO.IOException>(() => Olca.Unity.ValidationStorageGuard.RequireEditorStartup(null,null,null,true));
                Assert.Throws<System.IO.IOException>(() => new Olca.Unity.ValidationStorageGuard("",mockDefault));
                Assert.Throws<System.IO.IOException>(() => new Olca.Unity.ValidationStorageGuard(container,mockDefault));
                string settings = System.IO.Path.Combine(own,"settings");
                Assert.That(new Olca.Unity.AudioVolumeFileStore(settings,io).TrySave(new Olca.Unity.AudioVolumeSettings(99),out _), Is.True);
                // 再構築は静的な許可キャッシュを引き継がず、同じ規則で再検査する。
                var restartedIo = new Olca.Unity.CheckpointFileIo(new Olca.Unity.ValidationStorageGuard(own,mockDefault));
                var audio = new Olca.Unity.AudioVolumeFileStore(settings,restartedIo);
                Assert.That(audio.TryRead(out var value,out _), Is.True); Assert.That(value.Master, Is.EqualTo(99));
                Assert.That(audio.TrySave(new Olca.Unity.AudioVolumeSettings(98),out _), Is.True);
                byte[] temporary = { 1,2,3 }; string temp = System.IO.Path.Combine(own,"temp"); restartedIo.WriteAndFlush(temp,temporary);
                Assert.Throws<System.IO.IOException>(()=>restartedIo.MoveNew(temp,System.IO.Path.Combine(mockDefault,"moved")));
                Assert.Throws<System.IO.IOException>(()=>restartedIo.Replace(temp,audio.ActivePath,System.IO.Path.Combine(mockDefault,"backup")));
                Assert.That(System.IO.File.ReadAllBytes(temp), Is.EqualTo(temporary));
                Assert.That(System.IO.Directory.Exists(mockDefault), Is.False);
                Assert.That(audio.TryRead(out value,out _), Is.True); Assert.That(value.Master, Is.EqualTo(98));
            } finally { if(System.IO.Directory.Exists(container)) System.IO.Directory.Delete(container,true); }
        }

        [TestCase(30, false)] [TestCase(60, false)] [TestCase(120, false)]
        [TestCase(30, true)] [TestCase(60, true)] [TestCase(120, true)]
        public void 空中ダッシュ入力と空中接敵を通常探索と保存復帰で区別する(int fps, bool stall)
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var config = DungeonConfig.CreateTestDungeon(30);
                config.MapCells[3] = new MapCell(3, new Enemy("boss", "検証", 200, ElementType.None, new Vector2Int(3, 0)));
                var deck = CreateTestDeck();
                var checkpoints = DungeonCheckpointSession.CreateDefault(config, deck);
                var game = new DungeonBattleManager(config, new PlayerCharacter(100), deck,
                    new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
                var host = new UnityEngine.GameObject("dash-input-test"); host.SetActive(false);
                var controller = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = controller.GetType(); type.GetField("manager", flags).SetValue(controller, game);
                var handle = type.GetMethod("HandleInput", flags);
                ContactInfo contact = null; game.Dungeon.OnBattleStart += c => contact = c;
                var clock = new LogicalFrameClock();
                try
                {
                    System.Action<long, int> tick = (frame, ms) => {
                        // mode0: ジャンプ後にダッシュ要求、mode1: 地上ダッシュ後ジャンプ、mode2: 地上ダッシュ。
                        var input = new LogicalInput {
                            JumpButtonDown = mode == 0 ? frame == 1 : mode == 1 && frame == 2,
                            DashButtonDown = mode == 0 ? frame == 2 : frame == 1 };
                        handle.Invoke(controller, new object[] { input, ms });
                        if (mode == 0 && frame == 2) {
                            Assert.IsFalse(game.Dungeon.IsDashing);
                            Assert.AreEqual(-1f, type.GetField("dashStartPosition", flags).GetValue(controller));
                            Assert.AreEqual(0, type.GetField("dashCooldownRemaining", flags).GetValue(controller));
                        }
                        game.TickFrame(ms);
                    };
                    long total = LogicalFrameClock.WallTicksPerSecond, previous = 0;
                    if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
                    for (long render = 1; previous < total; render++) {
                        long now = System.Math.Min(total, (stall ? total / 2 : 0) + render * total / fps);
                        clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
                    }
                    while (clock.PendingFrames > 0) clock.Drain(tick, 3);
                    Assert.IsNotNull(contact); Assert.AreEqual(mode == 2, contact.IsDashContact);
                    Assert.AreEqual(mode == 2, contact.DashEffect.HasValue);
                    Assert.AreEqual(mode == 2 ? 185 : 200, game.CurrentBattle.Enemies[0].CurrentHp);
                    var saved = checkpoints.Store.Read();
                    Assert.AreEqual(mode == 2, saved.Battle.DashContact);
                    Assert.AreEqual(mode == 2, saved.Battle.DashEffect.HasValue);
                    Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(checkpoints, new MockRandomProvider(),
                        new MockLogger(), out var resumed, out var error), error);
                    Assert.IsTrue(game.SuspendForCheckpoint()); resumed.ActivateCheckpoint();
                    Assert.AreEqual(mode == 2 ? 185 : 200, resumed.CurrentBattle.Enemies[0].CurrentHp);
                    Assert.AreEqual(saved.Battle.GridOriginX, checkpoints.Store.Read().Battle.GridOriginX);
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
        }

        [Test]
        public void 空中で拒否されたダッシュは着地後へ予約せず新しい地上要求を受け付ける()
        {
            var movement = new PlayerMovement(); movement.Jump(); movement.StartDash();
            Assert.IsFalse(movement.IsDashing); movement.Update(500);
            Assert.IsFalse(movement.IsJumping); Assert.IsFalse(movement.IsDashing);
            Assert.AreEqual(5f, movement.CurrentPosition);
            movement.StartDash(); Assert.IsTrue(movement.IsDashing);
            movement.Update(100); Assert.AreEqual(7f, movement.CurrentPosition);
        }

        [TestCase(30, false)] [TestCase(60, false)] [TestCase(120, false)]
        [TestCase(30, true)] [TestCase(60, true)] [TestCase(120, true)]
        public void 距離式の終了前後でもダッシュジャンプは着地まで加速して回復を止める(int fps, bool stall)
        {
            foreach (float distance in new[] { 3f, 5f, 50f })
            {
                var config = DungeonConfig.CreateTestDungeon(200, 100, 100);
                var deck = CreateTestDeck(); var checkpoints = DungeonCheckpointSession.CreateDefault(config, deck);
                var game = new DungeonBattleManager(config, new PlayerCharacter(100), deck,
                    new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
                var host = new UnityEngine.GameObject("dash-landing-test"); host.SetActive(false);
                var controller = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = controller.GetType(); type.GetField("manager", flags).SetValue(controller, game);
                // 3はコード既定、5は実シーン、50は着地前に距離式終了しない検証専用値。
                type.GetField("dashDistanceCells", flags).SetValue(controller, distance);
                var handle = type.GetMethod("HandleInput", flags); var clock = new LogicalFrameClock();
                try
                {
                    System.Action<long, int> tick = (frame, ms) => {
                        handle.Invoke(controller, new object[] { new LogicalInput {
                            DashButtonDown = frame == 1 || frame == 20 || frame == 33 || frame == 38,
                            JumpButtonDown = frame == 2 }, ms });
                        game.TickFrame(ms);
                        if (frame == 16) Assert.AreEqual(1.5f, game.Dungeon.PlayerHeight);
                        if (frame == 30) {
                            Assert.IsTrue(game.Dungeon.IsDashJumping); Assert.IsTrue(game.Dungeon.IsJumping);
                            Assert.AreEqual(distance < 50 ? 6 : 0, type.GetField("dashCooldownRemaining", flags).GetValue(controller));
                        }
                        if (frame == 31) {
                            Assert.IsFalse(game.Dungeon.IsDashJumping); Assert.IsFalse(game.Dungeon.IsJumping);
                            Assert.IsFalse(game.Dungeon.IsDashing); Assert.AreEqual(0, game.Dungeon.PlayerHeight);
                            Assert.AreEqual(10.333f, game.Dungeon.PlayerPosition, .001f);
                        }
                        if (frame == 33) Assert.IsFalse(game.Dungeon.IsDashing, "回復中の押下で再開しない");
                        if (frame == 37) Assert.IsFalse(game.Dungeon.IsDashing, "不受理の押下を予約しない");
                        if (frame == 38) Assert.IsTrue(game.Dungeon.IsDashing, "回復後の新しい押下");
                    };
                    long total = LogicalFrameClock.WallTicksPerSecond, previous = 0;
                    if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
                    for (long render = 1; previous < total; render++) {
                        long now = System.Math.Min(total, (stall ? total / 2 : 0) + render * total / fps);
                        clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
                    }
                    while (clock.PendingFrames > 0) clock.Drain(tick, 3);
                    Assert.AreEqual(CheckpointBoundary.Departure, checkpoints.Store.Read().Boundary);
                    Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(checkpoints, new MockRandomProvider(), new MockLogger(), out var resumed, out var error), error);
                    Assert.IsTrue(game.SuspendForCheckpoint()); resumed.ActivateCheckpoint();
                    Assert.AreEqual(0f, resumed.Dungeon.PlayerPosition); Assert.IsFalse(resumed.Dungeon.IsDashJumping);
                    Assert.IsFalse(resumed.Dungeon.IsJumping); Assert.IsFalse(resumed.Dungeon.IsDashing);
                    type.GetField("manager", flags).SetValue(controller, resumed);
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void 単階層の新規出撃ではボス必須だが既存保存の再開条件は変更しない(bool existingSave)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-final-floor-" + System.Guid.NewGuid().ToString("N"));
            var config = new DungeonConfig("legacy-floor", "検証", 3,
                new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2) });
            var host = new UnityEngine.GameObject("final-floor-entry-test"); host.SetActive(false);
            var controller = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = controller.GetType(); controller.ConfigureDungeon(config); controller.ConfigureCheckpointDirectory(directory);
            controller.ConfigureGameplaySettingsDirectory(System.IO.Path.Combine(directory,"settings"));
            var deck = (Deck)type.GetMethod("CreateInitialDeck", flags).Invoke(controller, null);
            var definitions = DungeonCheckpointSession.CreateDefault(config, deck).Definitions;
            var files = new Olca.Unity.FileCheckpointStore(definitions, directory);
            Dictionary<string, byte[]> before = null;
            try
            {
                if (existingSave) {
                    var player = new PlayerCharacter(100); player.TakeDamage(27);
                    var legacy = new DungeonBattleManager(config, player, deck, new MockRandomProvider(), new MockLogger(),
                        checkpoints: new DungeonCheckpointSession(definitions, files));
                    Assert.IsFalse(legacy.Dungeon.IsCheckpointBlocked);
                    before = System.IO.Directory.GetFiles(directory).ToDictionary(f => f, System.IO.File.ReadAllBytes);
                    type.GetMethod("Start", flags).Invoke(controller, null);
                    Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
                    var resumed = (DungeonBattleManager)type.GetField("manager", flags).GetValue(controller);
                    Assert.AreEqual(73, resumed.Dungeon.Player.CurrentHp); Assert.AreEqual(GamePhase.Dungeon, resumed.CurrentPhase);
                    foreach (var file in before) CollectionAssert.AreEqual(file.Value, System.IO.File.ReadAllBytes(file.Key));
                }
                else {
                    var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("Start", flags).Invoke(controller, null));
                    Assert.IsInstanceOf<System.ArgumentException>(exception.InnerException);
                    StringAssert.Contains("最終階層にはボスが必要", exception.InnerException.Message);
                    Assert.IsNull(type.GetField("manager", flags).GetValue(controller));
                    Assert.IsFalse(files.HasExistingData, "不正な出撃から保存を生成しない");
                }
            }
            finally {
                UnityEngine.Object.DestroyImmediate(host);
                if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void 最終階層のボスは生存本体を必要とし検証で配置を変更しない(bool summoned)
        {
            var config = DungeonConfig.CreateTestDungeon(3); config.ValidateFinalFloorForDeparture();
            var boss = new Enemy("boss", "検証", 50, ElementType.None, new Vector2Int(2, 0), isSummoned: summoned);
            if (!summoned) boss.TakeDamage(50, ElementType.None);
            config.MapCells[2] = new MapCell(2, boss);
            Assert.Throws<System.ArgumentException>(() => config.ValidateFinalFloorForDeparture());
            Assert.AreSame(boss, config.MapCells[2].Enemy);
        }

        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0, 0f); // 報酬ありの遷移を検証するfixture
            mockLogger = new MockLogger();
        }

        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復")
            };

            return new Deck(cards);
        }

        [Test]
        public void ポーズ中の探索は時間と入力を受け付けず繰り返し再開しても状態を戻さない()
        {
            var manager = new DungeonBattleManager(DungeonConfig.CreateTestDungeon(30), new PlayerCharacter(100),
                CreateTestDeck(), mockRandom, mockLogger);
            manager.Tick(100); var position = manager.Dungeon.PlayerPosition;
            manager.SetUserPaused(true); manager.SetUserPaused(true);
            manager.Jump(); manager.StartDash(); manager.Tick(5000);
            Assert.AreEqual(position, manager.Dungeon.PlayerPosition); Assert.AreEqual(0, manager.Dungeon.PlayerHeight);
            Assert.IsFalse(manager.Dungeon.IsDashing);
            manager.SetUserPaused(false); manager.SetUserPaused(false); manager.Tick(100);
            Assert.Greater(manager.Dungeon.PlayerPosition, position);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 戦闘停止理由は解除順によらず最後の理由が消えるまで停止する(bool userFirst)
        {
            var config = DungeonConfig.CreateTestDungeon(10);
            var manager = new DungeonBattleManager(config, new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            var battle = manager.CurrentBattle; Assert.IsNotNull(battle);
            battle.Pause(); manager.SetUserPaused(true); manager.SetUserPaused(true);
            if (userFirst) manager.SetUserPaused(false); else battle.Resume();
            Assert.IsTrue(battle.IsPaused);
            if (userFirst) battle.Resume(); else manager.SetUserPaused(false);
            Assert.IsFalse(battle.IsPaused);
        }

        [Test]
        public void ユーザーポーズは戦闘の毒遅延ゲージ基本攻撃を停止し残り時間から再開する()
        {
            var config = DungeonConfig.CreateTestDungeon(10);
            var manager = new DungeonBattleManager(config, new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            var battle = manager.CurrentBattle; var enemy = battle.Enemies[0];
            var poison = new StatusEffect(StatusEffectType.Poison, 5000, 2); enemy.ApplyStatus(poison);
            int fired = 0; var delayed = new DelayedEffect(DelayedEffectType.Startup, 2000) { ExecuteAction = () => fired++ };
            battle.DelayedEffects.RegisterDelayedEffect(delayed);
            var gauge = battle.CustomGauge.CurrentGaugeMs; var hp = enemy.CurrentHp; var pos = battle.Player.GridPosition;
            manager.SetUserPaused(true); manager.Tick(6000); battle.Tick(6000); battle.PlayerBasicAttack(enemy);
            Assert.AreEqual(5000, poison.RemainingMs); Assert.AreEqual(2000, delayed.RemainingTimeMs);
            Assert.AreEqual(0, fired); Assert.AreEqual(hp, enemy.CurrentHp); Assert.AreEqual(pos, battle.Player.GridPosition);
            Assert.AreEqual(gauge, battle.CustomGauge.CurrentGaugeMs);
            manager.SetUserPaused(false); manager.Tick(1000);
            Assert.AreEqual(4000, poison.RemainingMs); Assert.AreEqual(1000, delayed.RemainingTimeMs); Assert.AreEqual(0, fired);
            manager.Tick(1000); Assert.AreEqual(1, fired);
        }

        [Test]
        public void カスタム画面をポーズ中に閉じてもユーザー停止を解除せず再開だけではカスタムを閉じない()
        {
            var manager = new DungeonBattleManager(DungeonConfig.CreateTestDungeon(10), new PlayerCharacter(100),
                CreateTestDeck(), mockRandom, mockLogger);
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            var battle = manager.CurrentBattle; battle.SetFirstStrike(); battle.OpenCustomScreen(mockRandom);
            manager.SetUserPaused(true); manager.SetUserPaused(false);
            Assert.IsTrue(battle.CustomScreen.IsOpen); Assert.IsTrue(battle.IsPaused);
            manager.SetUserPaused(true); battle.CustomScreen.Cancel();
            Assert.IsTrue(battle.IsPaused); manager.SetUserPaused(false); Assert.IsFalse(battle.IsPaused);
        }

        [Test]
        public void 報酬画面のポーズ再開は報酬を選択せず探索も再開しない()
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            cells[2] = new MapCell(2, new Enemy("slime", "敵", 15, ElementType.None, new Vector2Int(2, 0)));
            cells[11] = new MapCell(11, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(11, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("pause", "停止", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            manager.StartDash();
            for (int i = 0; i < 20 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            var position = manager.Dungeon.PlayerPosition;
            manager.SetUserPaused(true); manager.SelectCardReward(null); manager.ResumeAfterBattle(); manager.Tick(10000);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            manager.SetUserPaused(false); manager.Tick(10000);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase); Assert.AreEqual(position, manager.Dungeon.PlayerPosition);
            Assert.AreEqual(0, manager.Dungeon.GetObtainedCards().Count);
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        public void 同じ処理単位の両者死亡_通知順によらず敗北一回で報酬なし(int route, bool reverse)
        {
            var config = DungeonConfig.CreateTestDungeon(10);
            var boss = config.MapCells[9].Enemy;
            var player = new PlayerCharacter(100, attackPower: 200);
            // route1/2は反撃イベントをBattleSystemより先/後に購読する。
            System.Action counter = route == 1
                ? () => player.TakeDamage(1000)
                : () => boss.TakeDamage(1000, ElementType.None);
            if (route == 1 && !reverse) boss.OnDeath += counter;
            if (route == 2 && !reverse) player.OnDeath += counter;
            var manager = new DungeonBattleManager(config, player, CreateTestDeck(), mockRandom, mockLogger);
            int rewards = 0, clears = 0, failures = 0, ends = 0;
            manager.OnCardRewardPresented += _ => rewards++;
            manager.OnDungeonClearCompleted += _ => clears++;
            manager.OnGameOver += () => failures++;
            manager.OnBattleEnded += () => ends++;
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            var battle = manager.CurrentBattle;
            var outcomes = new List<BattleResult>();
            battle.OnBattleEnd += result => outcomes.Add(result);
            if (route == 1 && reverse) boss.OnDeath += counter;
            if (route == 2 && reverse) player.OnDeath += counter;
            if (route == 0)
            {
                System.Action first = () => player.TakeDamage(1000);
                System.Action second = () => boss.TakeDamage(1000, ElementType.None);
                if (reverse) { var swap = first; first = second; second = swap; }
                battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = first });
                battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = second });
                manager.Tick(1);
            }
            else if (route == 1) boss.TakeDamage(1000, ElementType.None);
            else if (route == 2) player.TakeDamage(1000);
            else if (route == 4)
            {
                battle.OnPlayerAttack += (_, __) => player.TakeDamage(1000);
                battle.PlayerBasicAttack(boss);
            }
            else
            {
                // 同じTick内の遅延ダメージと、毒通知に重なる通常攻撃を最後まで処理する。
                boss.OnDamaged += (_, __) => {
                    if (boss.CurrentHp == 1) boss.TakeDamage(1, ElementType.None);
                };
                boss.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 1000));
                battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1)
                    { ExecuteAction = () => player.TakeDamage(1000) });
                manager.Tick(1000);
            }
            Assert.IsTrue(player.IsDead);
            Assert.IsTrue(boss.IsDead);
            Assert.AreEqual(BattleState.Defeat, battle.State);
            Assert.AreEqual(GamePhase.GameOver, manager.CurrentPhase);
            CollectionAssert.AreEqual(new[] { BattleResult.Defeat }, outcomes);
            Assert.AreEqual(0, rewards);
            Assert.AreEqual(0, clears);
            Assert.AreEqual(1, failures);
            Assert.AreEqual(1, ends);
            Assert.AreEqual(0, manager.Dungeon.GetObtainedCards().Count);
            // 既に死亡した相手からの重複通知でも結果とイベント数は不変。
            if (route == 1) boss.OnDeath -= counter;
            if (route == 2) player.OnDeath -= counter;
            player.TakeDamage(1);
            boss.TakeDamage(1, ElementType.None);
            battle.Tick(1000);
            CollectionAssert.AreEqual(new[] { BattleResult.Defeat }, outcomes);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void 片方だけ死亡_既存結果を一度確定し後の別処理では上書きしない(bool playerDies, bool tick)
        {
            var player = new PlayerCharacter(100);
            var enemy = new Enemy("enemy", "敵", 100, ElementType.None, new Vector2Int(3, 0));
            var battle = new BattleSystem(player, new List<Enemy> { enemy }, CreateTestDeck());
            var results = new List<BattleResult>();
            battle.OnBattleEnd += result => results.Add(result);
            System.Action damage = playerDies ? () => player.TakeDamage(1000)
                : () => enemy.TakeDamage(1000, ElementType.None);
            if (tick)
            {
                battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = damage });
                battle.Tick(1);
            }
            else damage();
            var expected = playerDies ? BattleResult.Defeat : BattleResult.Victory;
            CollectionAssert.AreEqual(new[] { expected }, results);
            player.TakeDamage(1000);
            enemy.TakeDamage(1000, ElementType.None);
            battle.Tick(1);
            CollectionAssert.AreEqual(new[] { expected }, results);
            Assert.AreEqual(playerDies ? BattleState.Defeat : BattleState.Victory, battle.State);
        }

        [TestCase(1, false, false)]
        [TestCase(2, false, false)]
        [TestCase(1, true, false)]
        [TestCase(2, true, false)]
        [TestCase(1, true, true)]
        [TestCase(2, true, true)]
        public void ダッシュ初撃全滅_開始後に勝利と報酬を一度だけ通知する(int count, bool boss, bool select)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            for (int i = 0; i < count; i++) cells[2 + i] = new MapCell(2 + i,
                new Enemy(boss && i == 0 ? "boss" : "slime", "初撃対象", 15, ElementType.None, new Vector2Int(2 + i, 0)));
            if (!boss) cells[11] = new MapCell(11, new Enemy("boss", "遠方ボス", 200, ElementType.None, new Vector2Int(11, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("dash", "初撃全滅", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            var events = new List<string>();
            IReadOnlyList<Card> choices = null;
            manager.OnBattleStarted += (battle, info) => { events.Add("start"); Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase); };
            manager.Dungeon.OnBattleEnd += result => { Assert.AreEqual(BattleResult.Victory, result); events.Add("victory"); };
            manager.OnCardRewardPresented += cards => { choices = cards; events.Add("reward"); };
            manager.OnBattleEnded += () => events.Add("end");
            manager.OnDungeonClearCompleted += _ => events.Add("clear");
            manager.Dungeon.OnReturnToDungeon += () => events.Add("return");
            manager.StartDash();
            for (int i = 0; i < 20 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            CollectionAssert.AreEqual(new[] { "start", "victory", "reward", "end" }, events);
            Assert.IsNull(manager.CurrentBattle);
            manager.SelectCardReward(select ? choices[0] : null);
            manager.Tick(0);
            Assert.AreEqual(boss ? GamePhase.DungeonCleared : GamePhase.Dungeon, manager.CurrentPhase);
            CollectionAssert.AreEqual(new[] { "start", "victory", "reward", "end", boss ? "clear" : "return" }, events);
            Assert.AreEqual(select ? 1 : 0, manager.Dungeon.GetObtainedCards().Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ダッシュ初撃_一部生存は戦闘継続し開始時敗北は上書きしない(bool dieAtStart)
        {
            var weak = new Enemy("weak", "弱敵", 10, ElementType.None, new Vector2Int(2, 0));
            var strong = new Enemy("strong", "強敵", 100, ElementType.None, new Vector2Int(3, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, weak), new MapCell(3, strong) };
            var player = new PlayerCharacter(100);
            var manager = new DungeonBattleManager(new DungeonConfig("dash", "初撃境界", 4, cells),
                player, CreateTestDeck(), mockRandom, mockLogger);
            int rewards = 0, ends = 0, failures = 0;
            manager.OnCardRewardPresented += _ => rewards++;
            manager.OnBattleEnded += () => ends++;
            manager.OnGameOver += () => failures++;
            if (dieAtStart) manager.OnBattleStarted += (_, __) => player.TakeDamage(1000);
            manager.StartDash();
            for (int i = 0; i < 20 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(0, rewards);
            if (dieAtStart)
            {
                Assert.AreEqual(GamePhase.GameOver, manager.CurrentPhase);
                Assert.AreEqual(1, failures);
                Assert.AreEqual(1, ends);
                Assert.IsFalse(weak.IsDead, "開始通知中の敗北後には初撃を適用しない");
                Assert.AreEqual(100, strong.CurrentHp);
            }
            else
            {
                Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
                Assert.IsTrue(weak.IsDead);
                Assert.AreEqual(85, strong.CurrentHp);
                Assert.AreEqual(0, ends);
                strong.TakeDamage(1000, ElementType.None);
                Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
                Assert.AreEqual(1, ends);
                Assert.AreEqual(1, rewards);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void 最奥ボス勝利_回避敵が残っても報酬完了後に一度だけクリア(bool selectReward, bool nearbyNormal)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            var avoided = new Enemy("avoided", "回避敵", 20, ElementType.None, new Vector2Int(2, 0));
            var boss = new Enemy("boss", "最奥ボス", 200, ElementType.None, new Vector2Int(11, 0));
            cells[2] = new MapCell(2, avoided);
            cells[11] = new MapCell(11, boss);
            if (nearbyNormal) cells[10] = new MapCell(10,
                new Enemy("primary", "主敵", 20, ElementType.None, new Vector2Int(10, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "ボスクリア", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int clears = 0, returns = 0, rewards = 0;
            IReadOnlyList<Card> choices = null;
            manager.OnDungeonClearCompleted += _ => clears++;
            manager.Dungeon.OnReturnToDungeon += () => returns++;
            manager.OnCardRewardPresented += cards => { rewards++; choices = cards; };
            manager.Jump();
            // 新仕様ではボス巻込みは禁止。手前の通常敵も回避し、ボス自身へ直接接敵する。
            bool jumpedNearBoss = false;
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++)
            {
                if (nearbyNormal && !jumpedNearBoss && manager.Dungeon.PlayerPosition >= 8f)
                {
                    manager.Jump();
                    jumpedNearBoss = true;
                }
                manager.Tick(100);
            }
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            Assert.AreEqual(nearbyNormal ? 2 : 1, manager.CurrentBattle.Enemies.Count);
            foreach (var enemy in new List<Enemy>(manager.CurrentBattle.Enemies)) enemy.TakeDamage(1000, ElementType.None);
            manager.Tick(100);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            Assert.AreEqual(0, clears, "報酬完了前にはクリアしない");
            manager.SelectCardReward(selectReward ? choices[0] : null);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            Assert.IsTrue(manager.Dungeon.IsDungeonCleared);
            Assert.AreSame(avoided, manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.IsFalse(avoided.IsDead);
            Assert.IsNull(manager.Dungeon.Map.GetCell(11).Enemy);
            Assert.AreEqual(selectReward ? 1 : 0, manager.Dungeon.GetObtainedCards().Count);
            manager.ResumeAfterBattle();
            manager.Tick(1000);
            manager.Dungeon.OnCardSelectionCompleted(null);
            Assert.AreEqual(1, clears);
            Assert.AreEqual(1, rewards);
            Assert.AreEqual(0, returns);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 通常接敵_近隣ボスを残し通常巻込み戦闘の後でボスへ直接接敵する(bool dash)
        {
            var normal = new Enemy("normal", "主敵", 50, ElementType.None, new Vector2Int(2, 0));
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(3, 0));
            var extra = new Enemy("extra", "通常巻込み", 50, ElementType.None, new Vector2Int(4, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, normal), new MapCell(3, boss), new MapCell(4, extra) };
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "ボス除外", 5, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int starts = 0, clears = 0;
            ContactInfo contact = null;
            manager.OnBattleStarted += (_, info) => { starts++; contact = info; };
            manager.OnDungeonClearCompleted += _ => clears++;
            if (dash) manager.StartDash();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreSame(normal, contact.Enemy);
            CollectionAssert.AreEquivalent(new[] { normal, extra }, manager.CurrentBattle.Enemies);
            Assert.AreEqual(200, boss.CurrentHp, "初撃もボスへ適用しない");
            foreach (var enemy in new List<Enemy>(manager.CurrentBattle.Enemies)) enemy.TakeDamage(1000, ElementType.None);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.AreEqual(0, clears);
            Assert.IsNull(manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.IsNull(manager.Dungeon.Map.GetCell(4).Enemy);
            Assert.AreSame(boss, manager.Dungeon.Map.GetCell(3).Enemy);
            manager.StopDash();
            manager.ResumeAfterBattle();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(2, starts);
            Assert.AreSame(boss, contact.Enemy);
            CollectionAssert.AreEqual(new[] { boss }, manager.CurrentBattle.Enemies);
            boss.TakeDamage(1000, ElementType.None);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            Assert.AreEqual(1, clears);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 最奥ボス未撃破_通常敵や手前のボスの勝利ではクリアしない(bool earlierBoss)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            cells[2] = new MapCell(2, new Enemy(earlierBoss ? "boss" : "slime", "手前の敵", 20,
                ElementType.None, new Vector2Int(2, 0)));
            var boss = new Enemy("boss", "最奥ボス", 200, ElementType.None, new Vector2Int(11, 0));
            cells[11] = new MapCell(11, boss);
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "最奥未撃破", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int clears = 0;
            manager.OnDungeonClearCompleted += _ => clears++;
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            manager.CurrentBattle.Enemies[0].TakeDamage(1000, ElementType.None);
            manager.Tick(100);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsFalse(manager.Dungeon.IsDungeonCleared);
            Assert.AreSame(boss, manager.Dungeon.Map.GetCell(11).Enemy);
            Assert.IsFalse(boss.IsDead);
            Assert.AreEqual(0, clears);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void 勝利復帰_撃破参加敵を除去し再接敵せず生存敵を保持する(int count, bool includeDead)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 25; i++) cells.Add(new MapCell(i));
            var avoided = new Enemy("avoided", "回避敵", 20, ElementType.None, new Vector2Int(2, 0));
            var primary = new Enemy("primary", "主敵", 20, ElementType.None, new Vector2Int(10, 0));
            var extra = new Enemy("extra", "隣接巻込み敵", 20, ElementType.None, new Vector2Int(11, 0));
            var unrelated = new Enemy("unrelated", "遠方生存敵", 20, ElementType.None, new Vector2Int(20, 0));
            var dead = new Enemy("dead", "既に死亡した敵", 20, ElementType.None, new Vector2Int(12, 0));
            dead.TakeDamage(100, ElementType.None);
            cells[2] = new MapCell(2, avoided);
            cells[10] = new MapCell(10, primary);
            if (count == 2) cells[11] = new MapCell(11, extra);
            if (includeDead) cells[12] = new MapCell(12, dead);
            cells[20] = new MapCell(20, unrelated);
            var manager = new DungeonBattleManager(new DungeonConfig("roundtrip", "勝利復帰", 25, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int starts = 0, victories = 0, rewards = 0, returns = 0;
            manager.OnBattleStarted += (_, __) => starts++;
            manager.Dungeon.OnBattleEnd += result => { if (result == BattleResult.Victory) victories++; };
            manager.OnCardRewardPresented += _ => rewards++;
            manager.Dungeon.OnReturnToDungeon += () => returns++;
            manager.Jump(); // 最初の通常敵を実際に回避して通過する。
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            var participants = new List<Enemy>(manager.CurrentBattle.Enemies);
            Assert.AreEqual(count, participants.Count);
            Assert.IsFalse(participants.Contains(dead));
            foreach (var enemy in participants) enemy.TakeDamage(100, ElementType.None);
            manager.Tick(100);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            foreach (var enemy in participants)
                Assert.IsFalse(manager.Dungeon.Map.GetAllEnemies().Exists(entry => ReferenceEquals(entry.enemy, enemy)));
            manager.SelectCardReward(null);
            manager.ResumeAfterBattle();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon && manager.Dungeon.PlayerPosition < 14f; i++)
                manager.Tick(100);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.GreaterOrEqual(manager.Dungeon.PlayerPosition, 14f);
            Assert.AreSame(avoided, manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.AreSame(unrelated, manager.Dungeon.Map.GetCell(20).Enemy);
            Assert.IsFalse(avoided.IsDead);
            Assert.IsFalse(unrelated.IsDead);
            if (includeDead) Assert.AreSame(dead, manager.Dungeon.Map.GetCell(12).Enemy, "無関係の参照は一括削除しない");
            Assert.AreEqual(1, starts);
            Assert.AreEqual(1, victories);
            Assert.AreEqual(1, rewards);
            Assert.AreEqual(1, returns);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 接触判定_死亡敵を無視して次の生存敵に一度だけ接触する(bool dash)
        {
            var dead = new Enemy("dead", "死亡敵", 10, ElementType.None, new Vector2Int(2, 0));
            dead.TakeDamage(100, ElementType.None);
            var alive = new Enemy("alive", "生存敵", 10, ElementType.None, new Vector2Int(3, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, dead), new MapCell(3, alive), new MapCell(4) };
            var contact = new ContactSystem(new DungeonMap(new DungeonConfig("contact", "接触", 5, cells)));
            var movement = new PlayerMovement(1f);
            if (dash) movement.StartDash();
            int calls = 0;
            contact.OnEnemyContact += info => { calls++; Assert.AreSame(alive, info.Enemy); };
            contact.CheckContact(movement);
            contact.ResetContact();
            contact.CheckContact(movement);
            Assert.AreEqual(0, calls);
            movement.SetPosition(2f);
            contact.CheckContact(movement);
            contact.CheckContact(movement);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void 初期化時はダンジョンフェーズ()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            // Act
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsNotNull(manager.Dungeon);
            Assert.IsNull(manager.CurrentBattle);
        }

        [Test]
        public void Tick_ダンジョンフェーズ中はダンジョンが更新される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            float initialPosition = manager.Dungeon.PlayerPosition;

            // Act
            manager.Tick(deltaTimeMs: 1000);

            // Assert
            Assert.Greater(manager.Dungeon.PlayerPosition, initialPosition, "ダンジョンでプレイヤーが前進");
        }

        [Test]
        public void 敵と接敵するとバトルフェーズに遷移する()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            BattleSystem battleStarted = null;
            manager.OnBattleStarted += (battle, contactInfo) => battleStarted = battle;

            // Act: 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (battleStarted != null) break;
            }

            // Assert
            Assert.IsNotNull(battleStarted, "バトルが開始された");
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            Assert.IsNotNull(manager.CurrentBattle);
        }

        [Test]
        public void バトル勝利後ダンジョンに戻る()
        {
            // Arrange: index 3 に通常敵（スライム）、index 9 にボスを配置
            // CreateTestDungeon(10) はボスのみ（100%ドロップ）のため専用の構成を使用
            var cells = new List<MapCell>
            {
                new MapCell(0), new MapCell(1), new MapCell(2),
                new MapCell(3, new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))),
                new MapCell(4), new MapCell(5), new MapCell(6), new MapCell(7), new MapCell(8),
                new MapCell(9, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(9, 0))),
            };
            var config = new DungeonConfig("test_dungeon", "テストダンジョン", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // カードドロップなし（0.7f >= 0.5f）
            mockRandom.SetFixedValue(0.7f);

            // 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase, "バトル開始");

            // Act: バトルに勝利
            var battle = manager.CurrentBattle;
            var enemy = battle.Enemies[0];

            // 敵を倒す
            while (!enemy.IsDead)
            {
                enemy.TakeDamage(100, ElementType.None);
            }

            // Tick を進めてバトル終了を処理
            manager.Tick(deltaTimeMs: 100);

            // 無報酬なので選択待ちを挟まず復帰する。

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase, "ダンジョンフェーズに戻る");
            Assert.IsNull(manager.CurrentBattle, "バトルシステムが破棄される");
        }

        [Test]
        public void バトル敗北時ゲームオーバー()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            bool gameOverFired = false;
            manager.OnGameOver += () => gameOverFired = true;

            // 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase, "バトル開始");

            // Act: プレイヤーを倒す
            player.TakeDamage(200);

            // Tick を進めてバトル終了を処理
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.AreEqual(GamePhase.GameOver, manager.CurrentPhase);
            Assert.IsTrue(gameOverFired, "ゲームオーバーイベント発火");
        }

        [Test]
        public void ダッシュ接敵時に初撃ダメージが適用される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 20);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            ContactInfo receivedContactInfo = null;
            manager.OnBattleStarted += (battle, contactInfo) => receivedContactInfo = contactInfo;

            // Act: ダッシュしながら敵に接近
            manager.StartDash();
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (receivedContactInfo != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedContactInfo, "接敵した");
            Assert.IsTrue(receivedContactInfo.IsDashContact, "ダッシュ接敵");

            // 敵のHPが初撃ダメージ分減っている
            // ダッシュ効果がInitialDamageの場合、攻撃力の50% = 10ダメージ
            var enemy = receivedContactInfo.Enemy;
            int expectedDamage = player.AttackPower / 2;  // 20 / 2 = 10
            Assert.Less(enemy.CurrentHp, enemy.MaxHp, "敵がダメージを受けている");
        }

        [Test]
        public void Jump_ダンジョンフェーズ中のみ有効()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // Act: ダンジョンフェーズでジャンプ
            manager.Jump();
            manager.Tick(deltaTimeMs: 100);  // Tickを呼んでHeightを更新

            // Assert
            Assert.Greater(manager.Dungeon.PlayerHeight, 0f, "ジャンプした");

            // バトルフェーズに移行
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            // バトル中にジャンプしても何も起こらない（エラーにならない）
            manager.Jump();
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class CheckpointFlowTests
    {
        private sealed class SeedRandom : Olca.Core.Interfaces.IRandomProvider
        {
            public int Seed, Calls;
            public int Range(int min, int max) { Calls++; return System.Math.Max(min, System.Math.Min(Seed, max - 1)); }
            public float Range(float min, float max) { Calls++; return min; }
            public float Value => 0;
        }
        private sealed class FailingStore : ICheckpointStore
        {
            private readonly MemoryCheckpointStore memory;
            public bool Fail;
            public System.Action BeforeWrite;
            public FailingStore(CheckpointDefinitions definitions) { memory = new MemoryCheckpointStore(definitions); }
            public bool TryWrite(CheckpointDto dto, out string error)
            { BeforeWrite?.Invoke(); if (Fail) { error = "fixture保存失敗"; return false; } return memory.TryWrite(dto, out error); }
            public System.Action BeforeRead;
            public bool ReadUnavailable, ThrowRead;
            public CheckpointDto Read()
            {
                BeforeRead?.Invoke();
                if (ThrowRead) throw new System.IO.IOException("fixture読込例外");
                return ReadUnavailable ? null : memory.Read();
            }
        }
        [Test]
        public void 保存再試行中の待機と同期保存時間を論理時計へ加算しない()
        {
            Create(true);
            var go = new UnityEngine.GameObject("retry-clock-test"); go.SetActive(false);
            var controller = go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("manager", flags).SetValue(controller, manager);
            var loop = (Olca.Unity.UnityLogicalLoop)typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("logicalLoop", flags).GetValue(controller);
            long now = 0;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp", flags).SetValue(loop, (System.Func<long>)(() => now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed", flags).SetValue(loop, 0L);
            loop.Clock.Accumulate(10000000);
            try
            {
                now += System.Diagnostics.Stopwatch.Frequency * 2;
                store.BeforeWrite = () => now += System.Diagnostics.Stopwatch.Frequency * 2;
                Assert.IsFalse(controller.RetryMemoryCheckpoint());
                now += System.Diagnostics.Stopwatch.Frequency * 2;
                store.Fail = false; Assert.IsTrue(controller.RetryMemoryCheckpoint());
                loop.Observe(true);
                Assert.AreEqual(60, loop.Clock.PendingFrames, "保存待ち・保存処理中の時間は追加せず既存の負債を保持する");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void 復帰検証と初期化時間を除外し失敗再試行と購読と旧予約を保護する(bool throwRead,bool userPaused)
        {
            Create();Contact();
            var go=new UnityEngine.GameObject("restore-clock-test");go.SetActive(false);
            var controller=go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(Olca.Application.Dungeon.DungeonBattleController);
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            Set("manager",manager);Set("memoryCheckpoints",session);Set("randomProvider",new Olca.Unity.UnityRandomProvider(123));Set("logger",new Olca.Unity.UnityLogger());
            var overlay=new UnityEngine.GameObject("checkpoint-overlay");overlay.transform.SetParent(go.transform);Set("checkpointOverlay",overlay);
            T Ui<T>(string name) where T:UnityEngine.Component {var obj=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(T));obj.transform.SetParent(go.transform);return obj.GetComponent<T>();}
            Set("checkpointMessage",Ui<UnityEngine.UI.Text>("message"));Set("continueCheckpointButton",Ui<UnityEngine.UI.Button>("continue"));Set("retryCheckpointButton",Ui<UnityEngine.UI.Button>("retry"));
            var loop=(Olca.Unity.UnityLogicalLoop)type.GetField("logicalLoop",flags).GetValue(controller);long now=0;long frequency=System.Diagnostics.Stopwatch.Frequency;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(System.Func<long>)(()=>now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);
            var handlers=new Dictionary<string,string>{{"OnBattleStarted","OnBattleStarted"},{"OnBattleEnded","OnBattleEnded"},{"OnCardRewardPresented","OnCardRewardPresented"},{"OnGameOver","OnGameOver"},{"OnDungeonClearCompleted","OnDungeonCleared"}};
            foreach(var pair in handlers){var e=typeof(DungeonBattleManager).GetEvent(pair.Key);e.AddEventHandler(manager,System.Delegate.CreateDelegate(e.EventHandlerType,controller,type.GetMethod(pair.Value,flags)));}
            int Subscriptions(DungeonBattleManager m,string name)=>((System.Delegate)typeof(DungeonBattleManager).GetField(name,flags).GetValue(m))?.GetInvocationList().Count(d=>ReferenceEquals(d.Target,controller))??0;
            float oldScale=UnityEngine.Time.timeScale;UnityEngine.Time.timeScale=1;
            int activationCalls=0;UnityEngine.Application.LogCallback delay=(message,stack,level)=>{if(message=="[INFO] [DungeonBattleManager] バトル開始"){activationCalls++;now+=frequency/4;}};
            UnityEngine.Application.logMessageReceived+=delay;
            try {
                if(userPaused)controller.SetUserPaused(true);
                var oldClock=loop.Clock;loop.Clock.Accumulate(10000000);int oldCommands=0,oldInput=0;
                loop.AfterFrames(2,()=>oldCommands++);loop.QueueTimestampedCommand(0,()=>oldInput++);
                now=frequency/5;store.BeforeRead=()=>now+=frequency/4;store.ReadUnavailable=!throwRead;store.ThrowRead=throwRead;
                if(throwRead)Assert.Throws<System.IO.IOException>(()=>controller.TryRestoreMemoryCheckpoint(out _));
                else Assert.IsFalse(controller.TryRestoreMemoryCheckpoint(out _));
                Assert.AreSame(manager,type.GetField("manager",flags).GetValue(controller));Assert.IsFalse(manager.IsSuspended);Assert.AreSame(oldClock,loop.Clock);
                Assert.AreEqual(userPaused?60:72,loop.Clock.PendingFrames,"失敗前の負債は保持、読込時間は除外");
                foreach(var name in handlers.Keys)Assert.AreEqual(1,Subscriptions(manager,name));
                now+=frequency/10;loop.Observe(!userPaused);Assert.AreEqual(userPaused?60:78,loop.Clock.PendingFrames,"失敗後の実経過は保持");
                store.ReadUnavailable=false;store.ThrowRead=false;
                Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error),error);
                Assert.IsTrue(manager.IsSuspended);Assert.AreNotSame(oldClock,loop.Clock);Assert.AreEqual(0,loop.Clock.Frame);Assert.AreEqual(0,loop.Clock.PendingFrames);
                Assert.AreEqual(1,activationCalls);
                var restored=(DungeonBattleManager)type.GetField("manager",flags).GetValue(controller);
                foreach(var name in handlers.Keys){Assert.AreEqual(0,Subscriptions(manager,name));Assert.AreEqual(1,Subscriptions(restored,name));}
                now+=frequency/10;loop.PumpTimestamped(true,null,(frame,ms,input)=>{});Assert.AreEqual(6,loop.Clock.Frame);Assert.AreEqual(0,oldCommands);Assert.AreEqual(0,oldInput);
                Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out error),error);Assert.AreEqual(2,activationCalls);Assert.AreEqual(0,loop.Clock.PendingFrames);
                var again=(DungeonBattleManager)type.GetField("manager",flags).GetValue(controller);
                foreach(var name in handlers.Keys){Assert.AreEqual(0,Subscriptions(restored,name));Assert.AreEqual(1,Subscriptions(again,name));}
            } finally {UnityEngine.Application.logMessageReceived-=delay;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Time.timeScale=oldScale;
                Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();}
        }
        [TestCase(false)] [TestCase(true)]
        public void 撤退確認中の古い図鑑操作を拒否し取消後に同じ停止状態で図鑑へ戻れる(bool battle)
        {
            Create();if(battle)Contact();manager.SetUserPaused(true);
            var host=new UnityEngine.GameObject("retreat-modal-test");host.SetActive(false);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=controller.GetType();
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            object Get(string name)=>type.GetField(name,flags).GetValue(controller);
            void Call(string name)=>type.GetMethod(name,flags).Invoke(controller,null);
            var progress=new ClearReturnSession(new MemoryClearReturnStore(new ClearReturnProfile()));
            Set("manager",manager);Set("clearReturnSession",progress);
            try {
                Call("CreatePauseControls");var pause=(UnityEngine.GameObject)Get("pauseOverlay");pause.SetActive(true);
                var button=(UnityEngine.UI.Button)Get("cardCollectionButton");var oldOpen=button.onClick;
                long revision=progress.Snapshot.Revision;long frame=manager.CurrentBattle?.LogicalFrame??0;bool custom=manager.CurrentBattle?.CustomScreen.IsOpen??false;
                for(int repeat=0;repeat<2;repeat++){
                    Call("RequestRetreat");Assert.IsTrue(((UnityEngine.GameObject)Get("retreatConfirmation")).activeInHierarchy);
                    oldOpen.Invoke();Assert.IsFalse((bool)Get("cardCollectionOpen"));Assert.IsFalse(pause.activeSelf);Assert.AreEqual(revision,progress.Snapshot.Revision);
                    var cancel=((UnityEngine.GameObject)Get("retreatConfirmation")).GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="CancelRetreat");cancel.onClick.Invoke();
                    Assert.IsTrue(pause.activeSelf);oldOpen.Invoke();Assert.IsTrue((bool)Get("cardCollectionOpen"));Assert.IsFalse(pause.activeSelf);Call("CloseCardCollection");
                    revision=progress.Snapshot.Revision;Assert.IsTrue(pause.activeSelf);Assert.IsTrue(manager.IsUserPaused);Assert.AreEqual(frame,manager.CurrentBattle?.LogicalFrame??0);Assert.AreEqual(custom,manager.CurrentBattle?.CustomScreen.IsOpen??false);
                }
            } finally {var canvas=Get("pauseCanvas") as UnityEngine.GameObject;if(canvas!=null)UnityEngine.Object.DestroyImmediate(canvas);UnityEngine.Object.DestroyImmediate(host);}
        }
        private DungeonBattleManager manager;
        private DungeonCheckpointSession session;
        private FailingStore store;
        private SeedRandom random;
        private void Create(bool failDeparture = false, int? recovery = null)
        {
            var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
            if (recovery.HasValue) foreach (var index in Enumerable.Range(0, map.MapCells.Count).Where(i => map.MapCells[i].Enemy != null).ToArray()) {
                var e = map.MapCells[index].Enemy;
                map.MapCells[index] = new MapCell(index, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element,
                    e.GridPosition, poiseDefinition: new EnemyPoiseDefinition(30, 6, recovery)));
            }
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("sword"), factory.CreateById("heal") });
            var defaults = DungeonCheckpointSession.CreateDefault(map, deck);
            store = new FailingStore(defaults.Definitions) { Fail = failDeparture };
            session = new DungeonCheckpointSession(defaults.Definitions, store); random = new SeedRandom();
            manager = new DungeonBattleManager(map, new PlayerCharacter(100, poiseRecoveryDelayFrames: recovery), deck, random, new MockLogger(), checkpoints: session);
            if (!failDeparture)
            {
                // 製品値に触れず、既存抽選で報酬ありとなるfixtureのSeedを探す。
                for (int seed = 0; seed < 100; seed++)
                {
                    var dto = store.Read(); dto.Enemies[0].GridX = 4;
                    var e = new CheckpointBattleDto { ContactCell = 3, ParticipantCells = new[] { 3 }, HandLimit = 10 };
                    Assert.IsTrue(CheckpointBattlePlan.TryCreate(dto, e, defaults.Definitions, seed, null,
                        c => defaults.Definitions.CaptureKnownCard("reward", c).DefinitionKey, out var plan, out var error), error);
                    if (plan.Snapshot.Battle.RewardOffered) { random.Seed = seed; break; }
                }
            }
        }
        private void Contact(bool dash = false)
        {
            if (dash) manager.StartDash();
            for (int i = 0; i < 10 && manager.CurrentBattle == null && !manager.Dungeon.IsCheckpointBlocked; i++) manager.Tick(50);
        }
        private void Open()
        {
            manager.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            manager.CurrentBattle.OpenCustomScreen(random);
        }
        private void Win()
        {
            foreach (var enemy in manager.CurrentBattle.Enemies.ToArray())
                if (!enemy.IsDead && !enemy.IsSummoned) enemy.TakeDamage(enemy.MaxHp, ElementType.None);
        }
        private void Restore()
        {
            Assert.IsTrue(manager.SuspendForCheckpoint());
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session, random, new MockLogger(), out var restored, out var error), error);
            manager = restored; manager.ActivateCheckpoint();
        }

        [TestCase(false)][TestCase(true)]
        public void 自然回復定義は出撃戦闘報酬と再戦を通して保持し一時状態だけ解除する(bool restore)
        {
            Create(recovery: 6);
            Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked);
            Assert.AreEqual(6, store.Read().Player.PoiseRecoveryFrames);
            Contact(); Assert.IsNotNull(manager.CurrentBattle);
            Assert.AreEqual(CheckpointBoundary.BattleStart, store.Read().Boundary);
            if (restore) Restore();
            var player = manager.Dungeon.Player; var enemy = manager.CurrentBattle.Enemies[0];
            Assert.AreEqual(6, player.PoiseRecoveryDelayFrames); Assert.AreEqual(6, enemy.PoiseRecoveryDelayFrames);
            player.TakeDamage(1, poiseDamage: 7); enemy.TakeDamage(1, ElementType.None, poiseDamage: 7);
            Assert.AreEqual(6, player.PoiseRecoveryRemainingFrames); Assert.AreEqual(6, enemy.PoiseRecoveryRemainingFrames);
            var sequence = store.Read().Sequence;
            Win(); store.Fail = true; manager.SelectCardReward(null);
            Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked); Assert.AreEqual(sequence, store.Read().Sequence);
            Assert.AreEqual(6, player.PoiseRecoveryDelayFrames); Assert.AreEqual(0, player.PoiseRecoveryRemainingFrames);
            Assert.AreEqual(0, player.AccumulatedDamage); Assert.AreEqual(6, enemy.PoiseRecoveryDelayFrames);
            Assert.AreEqual(0, enemy.PoiseRecoveryRemainingFrames); Assert.AreEqual(0, enemy.AccumulatedPoiseDamage);
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(CheckpointBoundary.RewardCompleted, store.Read().Boundary);
            Assert.AreEqual(6, store.Read().Player.PoiseRecoveryFrames); Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked);
            Restore(); Assert.AreEqual(6, manager.Dungeon.Player.PoiseRecoveryDelayFrames);
            for (int f = 0; f < 300 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle); Assert.AreEqual(6, manager.CurrentBattle.Enemies[0].PoiseRecoveryDelayFrames);
            Assert.AreEqual(0, manager.Dungeon.Player.PoiseRecoveryRemainingFrames);
        }

        [Test]
        public void 出撃保存失敗は進行せず再試行で初めて探索開始する()
        {
            Create(true); manager.Tick(1000); Assert.AreEqual(0, manager.Dungeon.PlayerPosition); Assert.IsNull(store.Read());
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(CheckpointBoundary.Departure, store.Read().Boundary);
            manager.Tick(50); Assert.Greater(manager.Dungeon.PlayerPosition, 0);
        }

        [Test]
        public void 開始保存失敗では通知と初撃を行わず再試行でも再抽選しない()
        {
            Create(); int starts = 0;
            manager.OnBattleStarted += (b, c) => {
                starts++; Assert.AreEqual(CheckpointBoundary.BattleStart, store.Read().Boundary);
                Assert.AreEqual(50, b.Enemies[0].CurrentHp);
            };
            store.Fail = true; Contact(true);
            Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked); Assert.IsNull(manager.CurrentBattle);
            Assert.AreEqual(0, starts); Assert.AreEqual(50, manager.Dungeon.Map.GetCell(3).Enemy.CurrentHp);
            int calls = random.Calls; Assert.IsFalse(manager.RetryCheckpoint()); Assert.AreEqual(calls, random.Calls);
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(1, starts); Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
            Assert.IsFalse(manager.RetryCheckpoint()); Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
            Open(); Assert.AreEqual(calls, random.Calls); Assert.AreEqual(3, manager.CurrentBattle.CustomScreen.CurrentHand.Count);
        }

        [Test]
        public void 戦闘モデル破棄から同じ初回手札と初撃一回を復元し旧個体を拒否する()
        {
            Create(); Contact(true); Open();
            var old = manager; var oldCard = manager.CurrentBattle.CustomScreen.CurrentHand[0];
            var before = manager.CurrentBattle.CustomScreen.CurrentHand.Select(c => c.Id).ToArray();
            Restore(); Open();
            CollectionAssert.AreEqual(before, manager.CurrentBattle.CustomScreen.CurrentHand.Select(c => c.Id));
            Assert.IsFalse(manager.CurrentBattle.CustomScreen.TrySelectCard(oldCard));
            Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
            manager.ActivateCheckpoint(); Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
            old.Tick(1000); Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 報酬復元と保存再試行で二重取得せず撃破敵を再出現させない(bool skip)
        {
            Create(); Contact(); List<Card> oldChoices = null;
            manager.OnCardRewardPresented += choices => oldChoices = choices.ToList(); Win();
            Assert.NotNull(oldChoices); var expected = oldChoices.Select(c => c.Id).ToArray();
            Restore(); List<Card> choicesNow = null;
            manager.OnCardRewardPresented += choices => choicesNow = choices.ToList(); Win();
            CollectionAssert.AreEqual(expected, choicesNow.Select(c => c.Id));
            manager.SelectCardReward(oldChoices[0]); Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            int count = manager.Dungeon.GetCurrentDeck().TotalCount;
            store.Fail = true; manager.SelectCardReward(skip ? null : choicesNow[0]);
            manager.SelectCardReward(choicesNow[0]); Assert.AreEqual(count, manager.Dungeon.GetCurrentDeck().TotalCount);
            Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            manager.SelectCardReward(choicesNow[0]); manager.Dungeon.OnCardSelectionCompleted(null);
            Assert.AreEqual(count + (skip ? 0 : 1), manager.Dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(skip ? 0 : 1, manager.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(CheckpointBoundary.RewardCompleted, store.Read().Boundary);
            Restore(); Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsNull(manager.Dungeon.Map.GetCell(3).Enemy);
            Assert.AreEqual(count + (skip ? 0 : 1), manager.Dungeon.GetCurrentDeck().TotalCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 終端を保存してから通知し終端保存からの再開を拒否する(bool victory)
        {
            Create();
            manager.Dungeon.StartBattle(new ContactInfo { Enemy = manager.Dungeon.Map.GetCell(19).Enemy, EnemyCellIndex = 19 });
            bool terminal = false;
            manager.OnGameOver += () => { terminal = true; Assert.AreEqual(CheckpointBoundary.Terminal, store.Read().Boundary); };
            manager.OnDungeonClearCompleted += _ => { terminal = true; Assert.AreEqual(CheckpointBoundary.Terminal, store.Read().Boundary); };
            if (victory) { Win(); manager.SelectCardReward(null); }
            else manager.CurrentBattle.Player.TakeDamage(1000);
            Assert.IsTrue(terminal);
            Assert.IsFalse(DungeonBattleManager.TryRestoreCheckpoint(session, random, new MockLogger(), out var restored, out _));
            Assert.IsNull(restored);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), session.Definitions, out var terminalState, out var error), error);
            Assert.Throws<System.ArgumentException>(() => new DungeonBattleManager(terminalState.Map, terminalState.Player,
                terminalState.Deck, random, new MockLogger(), checkpoints: session, restoration: terminalState));
        }

        [TestCase(false)] [TestCase(true)]
        public void 最終ボス報酬の再開と終了保存の失敗再試行でもクリア通知と獲得は一度だけ(bool skip)
        {
            var config = DungeonConfig.CreateTestDungeon(4); config.ValidateFinalFloorForDeparture();
            var deck = new Deck(); var defaults = DungeonCheckpointSession.CreateDefault(config, deck);
            store = new FailingStore(defaults.Definitions); session = new DungeonCheckpointSession(defaults.Definitions, store);
            random = new SeedRandom(); manager = new DungeonBattleManager(config, new PlayerCharacter(100), deck, random, new MockLogger(), checkpoints: session);
            for (int i = 0; i < 60 && manager.CurrentBattle == null; i++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle); List<Card> choices = null;
            manager.OnCardRewardPresented += cards => choices = cards.ToList(); Win();
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase); Assert.IsNotNull(choices);
            var ids = choices.Select(c => c.Id).ToArray(); Restore();
            manager.OnCardRewardPresented += cards => choices = cards.ToList(); Win();
            CollectionAssert.AreEqual(ids, choices.Select(c => c.Id));
            int clears = 0; manager.OnDungeonClearCompleted += cards => {
                clears++; Assert.AreEqual(CheckpointBoundary.Terminal, store.Read().Boundary);
                manager.SelectCardReward(null); manager.Dungeon.OnCardSelectionCompleted(null);
                Assert.IsFalse(manager.RetryCheckpoint());
            };
            var old = store.Read(); store.Fail = true; manager.SelectCardReward(skip ? null : choices[0]);
            Assert.AreEqual(0, clears); Assert.AreEqual(0, manager.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(old.Sequence, store.Read().Sequence); Assert.IsFalse(manager.SuspendForCheckpoint());
            manager.TickFrame(17); manager.SelectCardReward(choices[0]); Assert.AreEqual(0, clears);
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(1, clears); Assert.AreEqual(skip ? 0 : 1, manager.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            manager.TickFrame(17); manager.SelectCardReward(null); Assert.AreEqual(1, clears);
            Assert.IsFalse(DungeonBattleManager.TryRestoreCheckpoint(session, random, new MockLogger(), out _, out _));
        }

        [Test]
        public void ボスなし固定マップの既存クリア条件も終端として保存する()
        {
            var config = new DungeonConfig("no-boss", "fixture", 2, new List<MapCell> {
                new MapCell(0), new MapCell(1, new Enemy("normal", "敵", 50, ElementType.None, new Vector2Int(1, 0))) });
            var deck = new Deck(); var checkpoints = DungeonCheckpointSession.CreateDefault(config, deck);
            manager = new DungeonBattleManager(config, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
            manager.Tick(10); Win();
            if (manager.CurrentPhase == GamePhase.CardReward) manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            Assert.AreEqual(CheckpointBoundary.Terminal, checkpoints.Store.Read().Boundary);
            Assert.IsFalse(checkpoints.TryRead(out _, out _));
        }

        [Test]
        public void 敗北の保存失敗中は旧戦闘へ戻れず再試行後も終端を維持する()
        {
            Create(); Contact(); int notifications = 0; manager.OnGameOver += () => notifications++;
            store.Fail = true; manager.CurrentBattle.Player.TakeDamage(1000);
            Assert.AreEqual(0, notifications); Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);
            Assert.IsFalse(DungeonBattleManager.TryRestoreCheckpoint(session, random, new MockLogger(), out _, out _));
            store.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(1, notifications); Assert.AreEqual(CheckpointBoundary.Terminal, store.Read().Boundary);
            Assert.IsFalse(DungeonBattleManager.TryRestoreCheckpoint(session, random, new MockLogger(), out _, out _));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 未登録カードと未対応敵状態を黙って保存から落とさない(bool status)
        {
            Create();
            if (status) manager.Dungeon.Map.GetCell(3).Enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000));
            else manager.Dungeon.GetCurrentDeck().AddCard(new Card("unknown", "不明", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 1, ""));
            Contact(); Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked); Assert.IsNull(manager.CurrentBattle);
            Assert.AreEqual(CheckpointBoundary.Departure, store.Read().Boundary);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class FileCheckpointTests
    {
        private string directory;
        private DungeonCheckpointSession session;
        private Olca.Unity.FileCheckpointStore store;
        private DungeonBattleManager manager;
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo
        {
            public string Fault;
            public override void WriteAndFlush(string path, byte[] bytes)
            {
                if (Fault == "disk") throw new System.IO.IOException("容量不足fixture");
                if (Fault == "permission") throw new System.UnauthorizedAccessException("権限fixture");
                base.WriteAndFlush(path, bytes);
                if (Fault == "corrupt") System.IO.File.WriteAllText(path, "broken");
            }
            public override void Replace(string temp, string dest, string backup)
            {
                if (Fault == "replace") throw new System.IO.IOException("置換失敗fixture");
                base.Replace(temp, dest, backup);
                if (Fault == "after") throw new System.IO.IOException("置換応答喪失fixture");
            }
        }
        private FaultIo io;
        [SetUp] public void Setup()
        {
            directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-checkpoint-test-" + System.Guid.NewGuid().ToString("N"));
            var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("heal") });
            var definitions = DungeonCheckpointSession.CreateDefault(map, deck).Definitions;
            io = new FaultIo(); store = new Olca.Unity.FileCheckpointStore(definitions, directory, io);
            session = new DungeonCheckpointSession(definitions, store);
            manager = new DungeonBattleManager(map, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: session);
            Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked, manager.Dungeon.CheckpointError);
        }
        [TearDown] public void Cleanup() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        private CheckpointDto Next() { var dto = store.Read(); dto.Sequence++; return dto; }
        [Test] public void 新規ストアで実ファイルを読めて旧世代バックアップを保持する()
        {
            var before = store.Read(); var next = Next();
            Assert.IsTrue(store.TryWrite(next, out var error), error);
            var reopened = new Olca.Unity.FileCheckpointStore(session.Definitions, directory);
            Assert.AreEqual(next.Sequence, reopened.Read().Sequence);
            Assert.IsTrue(reopened.TryReadBackup(out var backup, out error), error);
            Assert.AreEqual(before.Sequence, backup.Sequence);
            Assert.IsTrue(reopened.TryWrite(next, out error), error);
            Assert.IsFalse(reopened.TryWrite(before, out error));
            next.RunId = "another"; next.Sequence++;
            Assert.IsFalse(reopened.TryWrite(next, out error));
        }
        [TestCase("disk")][TestCase("permission")][TestCase("corrupt")][TestCase("replace")]
        public void 書込失敗は本体とバックアップを保持し同一計画で再試行できる(string fault)
        {
            Assert.IsTrue(store.TryWrite(Next(), out _));
            var active = System.IO.File.ReadAllBytes(store.ActivePath); var backup = System.IO.File.ReadAllBytes(store.BackupPath);
            var dto = Next(); io.Fault = fault;
            Assert.IsFalse(store.TryWrite(dto, out var error)); Assert.IsNotEmpty(error);
            CollectionAssert.AreEqual(active, System.IO.File.ReadAllBytes(store.ActivePath));
            CollectionAssert.AreEqual(backup, System.IO.File.ReadAllBytes(store.BackupPath));
            Assert.AreEqual(0, System.IO.Directory.GetFiles(directory, "*.tmp").Length);
            io.Fault = null; Assert.IsTrue(store.TryWrite(dto, out error), error);
            Assert.AreEqual(dto.Sequence, store.Read().Sequence);
        }
        [Test] public void 置換完了後の例外は実体照合で確定しバックアップを二重更新しない()
        {
            var old = store.Read(); var dto = Next(); io.Fault = "after";
            Assert.IsTrue(store.TryWrite(dto, out var error), error);
            Assert.IsTrue(store.TryWrite(dto, out error), error);
            Assert.IsTrue(store.TryReadBackup(out var backup, out error), error);
            Assert.AreEqual(old.Sequence, backup.Sequence);
        }
        [TestCase(false)][TestCase(true)]
        public void 破損と未知版は再開も書込も拒否し旧バックアップを保持する(bool unknown)
        {
            Assert.IsTrue(store.TryWrite(Next(), out _)); var valid = store.Read(); valid.Sequence++;
            var backup = System.IO.File.ReadAllBytes(store.BackupPath);
            var text = System.IO.File.ReadAllText(store.ActivePath);
            System.IO.File.WriteAllText(store.ActivePath, unknown ? text.Replace("\"Version\":1", "\"Version\":99") : "broken");
            var invalid = System.IO.File.ReadAllBytes(store.ActivePath);
            Assert.IsFalse(session.TryRead(out _, out var error)); Assert.IsNotEmpty(error);
            Assert.IsFalse(store.TryWrite(valid, out error));
            CollectionAssert.AreEqual(invalid, System.IO.File.ReadAllBytes(store.ActivePath));
            CollectionAssert.AreEqual(backup, System.IO.File.ReadAllBytes(store.BackupPath));
            Assert.IsFalse(manager.IsSuspended);
        }
        [TestCase(false)][TestCase(true)]
        public void 新規ストアと新規モデルで同じ戦闘開始の手札報酬と初撃へ復帰する(bool dash)
        {
            if (dash) manager.StartDash();
            for (int i = 0; i < 100 && manager.CurrentBattle == null; i++) manager.Tick(50);
            Assert.IsNotNull(manager.CurrentBattle, manager.Dungeon.CheckpointError);
            var before = store.Read(); Assert.AreEqual(CheckpointBoundary.BattleStart, before.Boundary);
            Assert.AreEqual(dash, before.Battle.DashContact);
            var reopened = new Olca.Unity.FileCheckpointStore(session.Definitions, directory);
            var newSession = new DungeonCheckpointSession(session.Definitions, reopened);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(newSession, new MockRandomProvider(), new MockLogger(), out var restored, out var error), error);
            Assert.IsTrue(manager.SuspendForCheckpoint()); restored.ActivateCheckpoint();
            CollectionAssert.AreEqual(before.Battle.FirstHand, newSession.Last.Battle.FirstHand);
            CollectionAssert.AreEqual(before.Battle.RewardCandidates, newSession.Last.Battle.RewardCandidates);
            Assert.AreEqual(manager.CurrentBattle.Enemies[0].CurrentHp, restored.CurrentBattle.Enemies[0].CurrentHp);
            Assert.AreNotSame(manager, restored); Assert.AreNotSame(store, reopened);
        }
        [Test] public void 戦闘開始保存失敗は旧出撃ファイルを保持し進行停止後に同じ計画を保存する()
        {
            var before = System.IO.File.ReadAllBytes(store.ActivePath); io.Fault = "disk";
            manager.StartDash(); for (int i = 0; i < 20 && !manager.Dungeon.IsCheckpointBlocked; i++) manager.Tick(50);
            Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked); Assert.IsNull(manager.CurrentBattle);
            CollectionAssert.AreEqual(before, System.IO.File.ReadAllBytes(store.ActivePath));
            float position = manager.Dungeon.PlayerPosition; manager.Tick(1000); Assert.AreEqual(position, manager.Dungeon.PlayerPosition);
            Assert.IsFalse(manager.RetryCheckpoint()); io.Fault = null; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(CheckpointBoundary.BattleStart, store.Read().Boundary);
            Assert.AreEqual(35, manager.CurrentBattle.Enemies[0].CurrentHp);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class ObstacleCheckpointTests
    {
        private string directory;
        private DungeonBattleManager manager;
        private DungeonCheckpointSession session;
        private Olca.Unity.FileCheckpointStore store;
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Fail;
            public override void WriteAndFlush(string path, byte[] bytes)
            { if (Fail) throw new System.IO.IOException("fixture容量不足"); base.WriteAndFlush(path, bytes); }
        }
        private FaultIo io;
        private DungeonConfig Map(int damage = 15)
        {
            var map = DungeonConfig.CreateTestDungeon(20, 30, 6);
            map.MapCells[2] = new MapCell(2, new Obstacle("spike", "トゲ", damage));
            map.MapCells[10] = new MapCell(10, new Obstacle("spike", "別のトゲ", 15));
            return map;
        }
        private Deck Deck() => new Deck(new List<Card> { new Olca.Application.Battle.SimpleCardFactory().CreateById("sword") });
        private CheckpointDefinitions Definitions(int damage = 15) => DungeonCheckpointSession.CreateDefault(Map(damage), Deck()).Definitions;
        private void Create(int damage = 15)
        {
            var defs = Definitions(damage); io = new FaultIo(); store = new Olca.Unity.FileCheckpointStore(defs, directory, io);
            session = new DungeonCheckpointSession(defs, store);
            manager = new DungeonBattleManager(Map(damage), new PlayerCharacter(100, poiseRecoveryDelayFrames: 180), Deck(), new MockRandomProvider(), new MockLogger(), checkpoints: session);
            Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked, manager.Dungeon.CheckpointError);
        }
        [SetUp] public void Setup() => directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-obstacle-checkpoint-" + System.Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        private void Contact()
        {
            for (int i = 0; i < 1000 && manager.CurrentBattle == null && !manager.Dungeon.IsCheckpointBlocked && manager.CurrentPhase != GamePhase.GameOver; i++) manager.Tick(50);
        }
        private DungeonBattleManager Restore(CheckpointDefinitions defs)
        {
            var reopened = new Olca.Unity.FileCheckpointStore(defs, directory);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(defs, reopened), new MockRandomProvider(), new MockLogger(), out var result, out var error), error);
            result.ActivateCheckpoint(); return result;
        }
        [Test] public void 大フレーム接敵の保存失敗でも接触位置で止まり旧保存と被弾回数を維持する()
        {
            Create(); var before = System.IO.File.ReadAllBytes(store.ActivePath); io.Fail = true;
            int hits = 0, starts = 0; manager.Dungeon.OnObstaclePenalty += _ => hits++; manager.Dungeon.OnBattleStart += _ => starts++;
            manager.Tick(3000); Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);
            Assert.AreEqual(5f, manager.Dungeon.PlayerPosition); Assert.AreEqual(85, manager.Dungeon.Player.CurrentHp);
            Assert.AreEqual(1, hits); Assert.AreEqual(0, starts); Assert.IsNull(manager.CurrentBattle);
            CollectionAssert.AreEqual(before, System.IO.File.ReadAllBytes(store.ActivePath));
            manager.Tick(3000); Assert.IsFalse(manager.RetryCheckpoint());
            Assert.AreEqual(1, hits); Assert.AreEqual(5f, manager.Dungeon.PlayerPosition);
            io.Fail = false; Assert.IsTrue(manager.RetryCheckpoint()); Assert.AreEqual(1, starts);
            Assert.AreEqual(5000, store.Read().PositionMilli); Assert.AreEqual(85, store.Read().Player.Hp);
            CollectionAssert.AreEqual(new[] { 2 }, store.Read().ProcessedObstacleCells);
            manager.Tick(3000); Assert.AreEqual(1, hits); Assert.AreEqual(1, starts);
        }
        [Test] public void 障害物通過後の戦闘開始を実ファイルから別定義別モデルへ復元する()
        {
            Create(); Contact(); Assert.IsNotNull(manager.CurrentBattle);
            var saved = store.Read(); Assert.AreEqual(2, saved.SchemaVersion); Assert.AreEqual(85, saved.Player.Hp);
            CollectionAssert.AreEqual(new[] { 2 }, saved.ProcessedObstacleCells);
            var restored = Restore(Definitions()); Assert.AreEqual(85, restored.Dungeon.Player.CurrentHp);
            var a = manager.Dungeon.Map.GetCell(2).Obstacle; var b = restored.Dungeon.Map.GetCell(2).Obstacle;
            Assert.AreNotSame(a, b); Assert.AreEqual(a.ObstacleId, b.ObstacleId); Assert.AreEqual(a.PenaltyValue, b.PenaltyValue);
            Assert.AreEqual(15, restored.Dungeon.Map.GetCell(10).Obstacle.PenaltyValue);
            restored.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); restored.CurrentBattle.OpenCustomScreen(new MockRandomProvider());
            Assert.AreEqual(saved.Battle.FirstHand.Length, restored.CurrentBattle.CustomScreen.CurrentHand.Count);
        }
        [TestCase(false)][TestCase(true)]
        public void 同じ障害物セル内で再開しても衝突済みと回避済みを再処理しない(bool avoided)
        {
            Create(); var dto = store.Read(); dto.Sequence++; dto.Boundary = CheckpointBoundary.RewardCompleted;
            dto.PositionMilli = 2200; dto.ProcessedObstacleCells = new[] { 2 }; dto.Player.Hp = avoided ? 100 : 85;
            Assert.IsTrue(store.TryWrite(dto, out var error), error);
            var restored = Restore(Definitions()); int penalties = 0; restored.Dungeon.OnObstaclePenalty += _ => penalties++;
            restored.Tick(0); Assert.AreEqual(dto.Player.Hp, restored.Dungeon.Player.CurrentHp); Assert.AreEqual(0, penalties);
        }
        [Test] public void 未通過の障害物は再開後に一度だけ既存ダメージを適用する()
        {
            Create(); var dto = store.Read(); dto.Sequence++; dto.Boundary = CheckpointBoundary.RewardCompleted;
            dto.PositionMilli = 10200; dto.ProcessedObstacleCells = new[] { 2 }; dto.Player.Hp = 85;
            Assert.IsTrue(store.TryWrite(dto, out var error), error);
            var restored = Restore(Definitions()); int penalties = 0; restored.Dungeon.OnObstaclePenalty += _ => penalties++;
            restored.Tick(0); restored.Tick(0); Assert.AreEqual(70, restored.Dungeon.Player.CurrentHp); Assert.AreEqual(1, penalties);
        }
        [TestCase("missing")][TestCase("duplicate")][TestCase("unknown")][TestCase("future")]
        public void 不整合な通過情報を拒否し旧ファイルを保全する(string kind)
        {
            Create(); Contact(); var bytes = System.IO.File.ReadAllBytes(store.ActivePath); var dto = store.Read(); dto.Sequence++;
            dto.ProcessedObstacleCells = kind == "missing" ? null : kind == "duplicate" ? new[] { 2, 2 } : kind == "unknown" ? new[] { 1 } : new[] { 10 };
            Assert.IsFalse(store.TryWrite(dto, out var error)); Assert.IsNotEmpty(error);
            CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
        }
        [Test] public void 同一IDでも障害物値が異なる定義では再開できず旧モデルも保存も変えない()
        {
            Create(); Contact(); var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
            var changed = Definitions(16); var read = new Olca.Unity.FileCheckpointStore(changed, directory);
            Assert.IsFalse(new DungeonCheckpointSession(changed, read).TryRead(out _, out var error)); Assert.IsNotEmpty(error);
            Assert.IsFalse(manager.IsSuspended); CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
        }
        [Test] public void 障害物による敗北は終端保存成功まで通知せず同一計画を再試行する()
        {
            Create(150); var bytes = System.IO.File.ReadAllBytes(store.ActivePath); io.Fail = true; int gameOvers = 0;
            manager.OnGameOver += () => gameOvers++; Contact(); Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);
            Assert.AreEqual(0, gameOvers); Assert.IsNull(manager.CurrentBattle);
            CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
            Assert.IsFalse(manager.RetryCheckpoint()); io.Fail = false; Assert.IsTrue(manager.RetryCheckpoint());
            Assert.AreEqual(1, gameOvers); Assert.AreEqual(CheckpointBoundary.Terminal, store.Read().Boundary);
            Assert.IsFalse(new DungeonCheckpointSession(Definitions(150), store).TryRead(out _, out _));
            manager.Tick(500); Assert.AreEqual(1, gameOvers);
        }
        [TestCase("status")][TestCase("negative-discard")][TestCase("air")]
        public void 対象外の障害物を黙って省略せず拒否する(string kind)
        {
            var map = Map(); map.MapCells[2] = kind == "status" ? new MapCell(2, new Obstacle("x", "x", "poison")) :
                kind == "negative-discard" ? new MapCell(2, Obstacle.CreateCardDiscardObstacle("x", "x", -1)) : new MapCell(2, new Obstacle("x", "x", 15), 1);
            Assert.Throws<System.ArgumentException>(() => DungeonCheckpointSession.CreateDefault(map, Deck()));
        }
        [TestCase("fusion")][TestCase("exceed")]
        public void 通常融合と登録済み超越は既定マップの保存を阻害しない(string operation)
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
            var deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("sword"), factory.CreateById("bomb") });
            var defaults = DungeonCheckpointSession.CreateDefault(map, deck);
            var game = new DungeonBattleManager(map, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(),
                exceedSystem: new ExceedSystem(factory), fusionSystem: new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(), factory), checkpoints: defaults);
            for (int i = 0; i < 100 && game.CurrentBattle == null; i++) game.Tick(50);
            var screen = game.CurrentBattle.CustomScreen;
            game.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); game.CurrentBattle.OpenCustomScreen(new MockRandomProvider());
            var materials = operation == "fusion" ? new[] { screen.CurrentHand.First(c => c.Id == "sword"), screen.CurrentHand.Single(c => c.Id == "bomb") } : screen.CurrentHand.Where(c => c.Id == "sword").ToArray();
            var result = screen.ExecuteCustomization(materials);
            Assert.IsTrue(result.CanExecute, result.Reason);
            if (operation == "exceed") Assert.AreEqual(45, result.ResultCard.GetFinalPower());
            game.OnCardRewardPresented += _ => game.Dungeon.OnCardSelectionCompleted(null);
            foreach (var enemy in game.CurrentBattle.Enemies.ToArray()) enemy.TakeDamage(enemy.MaxHp, ElementType.None);
            Assert.IsFalse(game.Dungeon.IsCheckpointBlocked, game.Dungeon.CheckpointError);
            Assert.AreEqual(CheckpointBoundary.RewardCompleted, defaults.Store.Read().Boundary);
            Assert.AreEqual(3, game.Dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(2, game.Dungeon.GetCurrentDeck().GetAllCards().Count(c => c.Id == "sword" && c.ExceedLevel == (ExceedLevel)0));
        }
        [Test] public void 障害物なしの保存は版1を維持し障害物状態の混入を拒否する()
        {
            var map = DungeonConfig.CreateTestDungeon(20, 30, 6); var defaults = DungeonCheckpointSession.CreateDefault(map, Deck());
            var saved = new Olca.Unity.FileCheckpointStore(defaults.Definitions, directory);
            new DungeonBattleManager(map, new PlayerCharacter(100), Deck(), new MockRandomProvider(), new MockLogger(), checkpoints: new DungeonCheckpointSession(defaults.Definitions, saved));
            var dto = saved.Read(); Assert.AreEqual(1, dto.SchemaVersion); Assert.IsNull(dto.ProcessedObstacleCells);
            dto.ProcessedObstacleCells = new[] { 2 }; dto.Sequence++;
            Assert.IsFalse(saved.TryWrite(dto, out _)); Assert.AreEqual(1, saved.Read().SchemaVersion);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class LogicalFrameClockTests
    {
        [TestCase(50,30)][TestCase(100,60)][TestCase(200,120)]
        public void デバッグ速度は時間供給だけを変え分割負荷が異なっても同じ二百四十ステップになる(int percent,int fps)
        {
            var clock=new LogicalFrameClock();Assert.IsTrue(clock.TrySetDebugSpeedPercent(percent));
            var simulation=new BattleSimulation(new BattleSimulationSettings(),123);var trace=new System.Text.StringBuilder();
            for(int render=0;clock.Frame<240&&render<2000;render++){
                clock.Accumulate(render==2?10000000:10000000/fps);
                long work=0;for(int i=0;i<render%30;i++)work+=i;System.GC.KeepAlive(work);
                clock.Drain((f,ms)=>trace.Append(simulation.Step(BattleSimulation.SampleCommand((int)f)).Canonical).Append('\n'),(int)System.Math.Min(7,240-clock.Frame));
            }
            Assert.AreEqual(240,clock.Frame);Assert.AreEqual(BattleSimulation.SampleTrace(),trace.ToString());
        }
        [Test]
        public void 速度切替は取得済み時間と端数を再換算せず極端値を拒否し等速へ戻せる()
        {
            var clock=new LogicalFrameClock();clock.Accumulate(10000000);Assert.IsTrue(clock.TrySetDebugSpeedPercent(200));Assert.AreEqual(60,clock.PendingFrames);
            clock.Accumulate(5000000);Assert.AreEqual(120,clock.PendingFrames);Assert.IsTrue(clock.TrySetDebugSpeedPercent(50));clock.Accumulate(1);
            double remainder=clock.Interpolation;
            foreach(int invalid in new[]{int.MinValue,-1,0,1,99,201,int.MaxValue}){Assert.IsFalse(clock.TrySetDebugSpeedPercent(invalid));Assert.AreEqual(50,clock.DebugSpeedPercent);Assert.AreEqual(remainder,clock.Interpolation);}
            Assert.AreEqual(0,clock.Drain((f,ms)=>Assert.Fail("blocked"),canAdvance:()=>false));Assert.AreEqual(120,clock.PendingFrames);
            Assert.IsTrue(clock.TrySetDebugSpeedPercent(100));clock.Drain((f,ms)=>{});Assert.AreEqual(120,clock.Frame);Assert.AreEqual(remainder,clock.Interpolation);
            clock.Accumulate(10000000);clock.Drain((f,ms)=>{});Assert.AreEqual(180,clock.Frame);
        }
        [TestCase(50)][TestCase(100)][TestCase(200)]
        public void 速度変更後もカスタムとユーザーポーズ中の戦闘時間と乱数を進めない(int percent)
        {
            var clock=new LogicalFrameClock();clock.TrySetDebugSpeedPercent(percent);
            var custom=TransformationBattleReplay.Create(weakness:false);long frame=custom.Battle.LogicalFrame,draws=custom.Random.Draws;
            clock.Accumulate(10000000);clock.Drain((f,ms)=>custom.Step(new BattleFrameCommand()));
            Assert.AreEqual(frame,custom.Battle.LogicalFrame);Assert.AreEqual(draws,custom.Random.Draws);
            var paused=new BattleSimulation(new BattleSimulationSettings(),123);paused.Step(new BattleFrameCommand(userPaused:true));frame=paused.Battle.LogicalFrame;draws=paused.Random.Draws;
            clock.Accumulate(10000000);clock.Drain((f,ms)=>paused.Step(new BattleFrameCommand()));
            Assert.AreEqual(frame,paused.Battle.LogicalFrame);Assert.AreEqual(draws,paused.Random.Draws);
        }
        [TestCase(50)][TestCase(100)][TestCase(200)]
        public void 時刻付き入力の割当は速度に従い会話等の非active期間は加算しない(int percent)
        {
            using(var loop=new Olca.Unity.UnityLogicalLoop()){
                long now=0;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(System.Func<long>)(()=>now));
                typeof(Olca.Unity.UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);loop.Clock.TrySetDebugSpeedPercent(percent);
                var presses=new List<long>();long frequency=System.Diagnostics.Stopwatch.Frequency;now=frequency*2;
                loop.PumpTimestamped(true,new[]{new Olca.Unity.TimestampedLogicalInput(frequency,new LogicalInput{DashButtonDown=true})},(f,ms,input)=>{if(input.DashButtonDown)presses.Add(f);});
                CollectionAssert.AreEqual(new long[]{60*percent/100+1},presses);long frame=loop.Clock.Frame;
                now+=frequency*20;loop.PumpTimestamped(false,null,(f,ms,input)=>Assert.Fail("inactive"));Assert.AreEqual(frame,loop.Clock.Frame);
                loop.PumpTimestamped(true,null,(f,ms,input)=>Assert.Fail("stopped wall time"));Assert.AreEqual(frame,loop.Clock.Frame);
                loop.StartNewTimeline();Assert.AreEqual(100,loop.Clock.DebugSpeedPercent);
            }
        }
        [Test]
        public void 開発コマンドは通常設定に保存せず厳密な候補とresetだけを受け付ける()
        {
            var host=new UnityEngine.GameObject("debug-command-disabled");host.SetActive(false);
            try{var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();string error;
                Assert.IsTrue(controller.TryExecuteDebugGameSpeedCommand("game-speed 2",out error));Assert.IsNull(error);Assert.AreEqual(200,controller.DebugGameSpeedPercent);
                foreach(string invalid in new[]{null,"game-speed 0","game-speed NaN","game-speed Infinity","game-speed 100000","game-speed 2 extra"}){Assert.IsFalse(controller.TryExecuteDebugGameSpeedCommand(invalid,out error));Assert.IsNotNull(error);Assert.AreEqual(200,controller.DebugGameSpeedPercent);}
                Assert.IsTrue(controller.TryExecuteDebugGameSpeedCommand("game-speed 0.5",out error));Assert.AreEqual(50,controller.DebugGameSpeedPercent);
                Assert.IsTrue(controller.TryExecuteDebugGameSpeedCommand("game-speed reset",out error));Assert.AreEqual(100,controller.DebugGameSpeedPercent);
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 十七msの待機は開始位相によらず二論理フレーム待つ(int phase)
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> {
                new Enemy("frame", "fixture", 100, ElementType.None, new Vector2Int(5, 2)) }, new Deck());
            var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            if (phase > 0) clock.Drain((f, ms) => battle.TickFrame(ms), phase);
            var method = typeof(BattleSystem).GetMethod("WaitForActionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var wait = (Olca.Core.Interfaces.FrameTask)method.Invoke(battle, new object[] { .017f, System.Threading.CancellationToken.None });
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.AreEqual(Olca.Core.Interfaces.FrameTaskStatus.Pending, wait.Status);
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.AreEqual(Olca.Core.Interfaces.FrameTaskStatus.Succeeded, wait.Status);
        }
        [Test]
        public void ゲージ速度小数部を毎フレーム切り捨てず一秒分蓄積する()
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> {
                new Enemy("frame", "fixture", 100, ElementType.None, new Vector2Int(5, 2)) }, new Deck(), customGaugeSpeed: 1.1f);
            var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            clock.Drain((f, ms) => battle.TickFrame(ms));
            Assert.AreEqual(1100, battle.CustomGauge.CurrentGaugeMs);
        }
        [Test]
        public void 探索の毎フレーム位置は六十分の一秒の累積値となる()
        {
            var game = new DungeonBattleManager(DungeonConfig.CreateTestDungeon(100, 30, 50),
                new PlayerCharacter(100), new Deck(), new SeededRandom(), new MockLogger());
            var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            clock.Drain((f, ms) => {
                game.TickFrame(ms);
                Assert.AreEqual((f * 10000 / 60) / 1000f, game.Dungeon.PlayerPosition, "frame=" + f);
            });
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 無敵スタン遅延効果も十七msを二論理フレーム保持する(int phase)
        {
            var enemy = new Enemy("frame", "fixture", 100, ElementType.None, new Vector2Int(5, 2));
            var player = new PlayerCharacter(100);
            var battle = new BattleSystem(player, new List<Enemy> { enemy }, new Deck());
            var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            if (phase > 0) clock.Drain((f, ms) => battle.TickFrame(ms), phase);
            player.SetInvincible(17); enemy.ApplyStun(17); int fired = 0;
            battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 17) { ExecuteAction = () => fired++ });
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.IsTrue(player.IsInvincible); Assert.IsTrue(enemy.IsStunned); Assert.AreEqual(0, fired);
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.IsFalse(player.IsInvincible); Assert.IsFalse(enemy.IsStunned); Assert.AreEqual(1, fired);
        }
        [Test]
        public void ジャンプは開始位相によらず三十フレームで着地する()
        {
            var game = new DungeonBattleManager(DungeonConfig.CreateTestDungeon(100, 30, 50),
                new PlayerCharacter(100), new Deck(), new SeededRandom(), new MockLogger());
            game.Jump(); var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            clock.Drain((f, ms) => game.TickFrame(ms), 29);
            Assert.Greater(game.Dungeon.PlayerHeight, 0);
            clock.Drain((f, ms) => game.TickFrame(ms), 1);
            Assert.AreEqual(0, game.Dungeon.PlayerHeight);
        }
        [Test]
        public void 旧ms更新と論理更新を混ぜても待機の経過時間を失わない()
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> {
                new Enemy("frame", "fixture", 100, ElementType.None, new Vector2Int(5, 2)) }, new Deck());
            var method = typeof(BattleSystem).GetMethod("WaitForActionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var wait = (Olca.Core.Interfaces.FrameTask)method.Invoke(battle, new object[] { .1f, System.Threading.CancellationToken.None });
            battle.Tick(50); battle.TickFrame(16); battle.TickFrame(17);
            Assert.AreEqual(Olca.Core.Interfaces.FrameTaskStatus.Pending, wait.Status);
            battle.TickFrame(17); Assert.AreEqual(Olca.Core.Interfaces.FrameTaskStatus.Succeeded, wait.Status);
        }
        private static List<string> TimestampedTrace(int fps, bool stall)
        {
            var loop = new Olca.Unity.UnityLogicalLoop(); long now = 0;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp", flags).SetValue(loop, (System.Func<long>)(() => now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed", flags).SetValue(loop, 0L);
            long At(int ms) => System.Diagnostics.Stopwatch.Frequency * ms / 1000;
            var events = new List<Olca.Unity.TimestampedLogicalInput>();
            void Add(int ms, LogicalInput value) { value.HasInputSample = true; events.Add(new Olca.Unity.TimestampedLogicalInput(At(ms), value)); }
            Add(10, new LogicalInput { DashButtonDown = true, DashButton = true, AnyKey = true });
            Add(20, new LogicalInput());
            Add(35, new LogicalInput { BasicAttackButton = true, AnyKey = true });
            Add(410, new LogicalInput());
            Add(450, new LogicalInput { Horizontal = 1, HorizontalDown = 1, AnyKey = true });
            Add(750, new LogicalInput());
            Add(900, new LogicalInput { JumpButtonDown = true, CardUseButtonDown = true, AnyKey = true });
            Add(905, new LogicalInput());
            // 停止中の押下は復帰後へ持ち越さない。復帰時点で押し続けているキーは離すまで遮断する。
            Add(1500, new LogicalInput { DashButtonDown = true, AnyKey = true });
            Add(2000, new LogicalInput { BasicAttackButton = true, AnyKey = true });
            Add(2200, new LogicalInput());
            Add(2250, new LogicalInput { BasicAttackButton = true, AnyKey = true });
            Add(2600, new LogicalInput());
            var trace = new List<string>(); var random = new SeededRandom(); int cursor = 0, dash = 0, card = 0;
            long moved = 0; var rolls = new List<int>();
            void Pump(bool active)
            {
                var batch = new List<Olca.Unity.TimestampedLogicalInput>();
                while (cursor < events.Count && events[cursor].Timestamp <= now) batch.Add(events[cursor++]);
                // 演出の乱数消費量を描画回数ごとに変える。
                for (int j = 0; j < fps; j++) { var ignored = UnityEngine.Random.value; }
                loop.PumpTimestamped(active, batch, (f, ms, input) => {
                    bool blocked = Olca.Unity.GameplayPause.InputBlocked;
                    if (!blocked) {
                        if (input.DashButtonDown) { dash++; rolls.Add(random.Range(0, 10000)); }
                        if (input.CardUseButtonDown) { card++; rolls.Add(random.Range(0, 10000)); }
                        moved += (long)input.Horizontal;
                    }
                    trace.Add(f+":"+blocked+":"+input.DashButtonDown+":"+input.BasicAttackButtonHoldTime+":"+input.Horizontal+":"+moved+":"+random.Calls);
                });
            }
            void Segment(int start, int end)
            {
                if (stall) { now = At(end); Pump(true); return; }
                for (int n = 1; now < At(end); n++) { now = System.Math.Min(At(end), At(start) + System.Diagnostics.Stopwatch.Frequency * n / fps); Pump(true); }
            }
            try {
                Segment(0,1000); loop.Observe(true); loop.ReleaseInput(); Olca.Unity.GameplayPause.SetPaused(true);
                now = At(1999); Pump(false);
                now = At(2000); loop.Observe(false); loop.ReleaseInput(); Olca.Unity.GameplayPause.SetPaused(false);
                Segment(2000,3000);
                Assert.AreEqual(0,loop.LateInputEvents); Assert.AreEqual(120,loop.Clock.Frame); Assert.AreEqual(1,dash); Assert.AreEqual(1,card);
                Assert.AreEqual(2,random.Calls); Assert.AreEqual(18,moved);
                StringAssert.Contains(":"+(22f/60)+":",trace[23]);
                return trace.Concat(rolls.Select(v=>"rng="+v)).ToList();
            } finally {
                loop.Dispose(); Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }
        [TestCase(30,false)][TestCase(120,false)][TestCase(60,true)]
        public void 同じイベント時刻列なら停止中の押下解放と移動チャージ復帰と乱数列が一致する(int fps,bool stall)
        { CollectionAssert.AreEqual(TimestampedTrace(60,false),TimestampedTrace(fps,stall)); }
        [Test]
        public void 同一論理フレームの押下解放を翌フレームへ遅らせない()
        {
            var queue = new LogicalInputQueue();
            queue.Enqueue(1,new LogicalInput { DashButtonDown=true,DashButton=true });
            queue.Enqueue(1,new LogicalInput());
            var first=queue.Take(1); Assert.IsTrue(first.DashButtonDown); Assert.IsFalse(first.DashButton);
            Assert.IsFalse(queue.Take(2).DashButtonDown);
        }
        [TestCase("battle", "CardPrimary", "Z")]
        [TestCase("exploration", "JumpPrimary", "J")]
        [TestCase("exploration", "DashPrimary", "K")]
        [TestCase("battle", "CustomPrimary", "L")]
        [TestCase("battle", "LeftPrimary", "J")]
        public void 個別スロットは破棄なら元のまま適用なら実イベントを変換する(string context, string slot, string key)
        {
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var provider = new Olca.Unity.UnityInputProvider();
            try {
                provider.StartEventCapture();
                var original = provider.KeyboardProfile;
                var edit = original.BeginEdit();
                Assert.IsTrue(edit.TryAssign(("keyboard",context,slot),key,out _));
                Assert.AreSame(original,provider.KeyboardProfile);
                edit.Discard(); Assert.AreEqual(original.Bindings[("keyboard",context,slot)],edit.Draft[("keyboard",context,slot)]);
                Assert.IsTrue(edit.TryAssign(("keyboard",context,slot),key,out _));
                Olca.Unity.GameplayPause.SetPaused(false);
                Assert.IsFalse(provider.TryApplyKeyboardProfile(edit.Apply(),out _));
                Olca.Unity.GameplayPause.SetPaused(true);
                Assert.IsTrue(provider.TryApplyKeyboardProfile(edit.Apply(),out var error),error);
                Assert.AreEqual("RightShift",provider.KeyboardProfile.Bindings[("keyboard","exploration","DashSecondary")]);
                Assert.AreEqual("A",provider.KeyboardProfile.Bindings[("keyboard","battle","LeftSecondary")]);
                if(slot=="CardPrimary") Assert.AreEqual("Space",provider.KeyboardProfile.Bindings[("keyboard","battle","AttackPrimary")]);
                provider.CaptureTimestamped();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(System.Enum.Parse<UnityEngine.InputSystem.Key>(key)));
                UnityEngine.InputSystem.InputSystem.Update();
                var input=provider.CaptureTimestamped().Last().Input;
                if(slot=="CardPrimary") { Assert.IsTrue(input.CardUseButtonDown); Assert.IsFalse(input.BasicAttackButton); Assert.IsFalse(input.JumpButtonDown); }
                if(slot=="JumpPrimary") { Assert.IsTrue(input.JumpButtonDown); Assert.IsFalse(input.CardUseButtonDown); }
                if(slot=="DashPrimary") Assert.IsTrue(input.DashButtonDown&&input.DashButton);
                if(slot=="CustomPrimary") Assert.IsTrue(input.CustomScreenButtonDown);
                if(slot=="LeftPrimary") Assert.AreEqual(-1,input.Horizontal);
            } finally { provider.Dispose(); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame(); }
        }
        [TestCase(UnityEngine.InputSystem.Key.Enter)][TestCase(UnityEngine.InputSystem.Key.NumpadEnter)]
        public void 確定キーは同一更新の押下解放でもイベント時刻を維持する(UnityEngine.InputSystem.Key key)
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var provider=new Olca.Unity.UnityInputProvider();
            try { provider.StartEventCapture();provider.CaptureTimestamped();double time=UnityEngine.Time.realtimeSinceStartupAsDouble;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(key),time-.02);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(),time-.01);UnityEngine.InputSystem.InputSystem.Update();
                var events=provider.CaptureTimestamped();var confirm=events.Single(e=>e.Input.MenuConfirmButtonDown);var release=events.First(e=>e.Timestamp>confirm.Timestamp);
                Assert.AreEqual(.01,(release.Timestamp-confirm.Timestamp)/(double)System.Diagnostics.Stopwatch.Frequency,.00001);
                var queue=new LogicalInputQueue();foreach(var e in events)queue.Enqueue(1,e.Input);Assert.IsTrue(queue.Take(1).MenuConfirmButtonDown);Assert.IsFalse(queue.Take(2).MenuConfirmButtonDown);
            } finally {provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}
        }
        [TestCase(UnityEngine.InputSystem.Key.Escape)]
        public void 取消キーは同一更新の押下解放でもイベント時刻を維持する(UnityEngine.InputSystem.Key key)
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var provider=new Olca.Unity.UnityInputProvider();
            try { provider.StartEventCapture();provider.CaptureTimestamped();double time=UnityEngine.Time.realtimeSinceStartupAsDouble;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(key),time-.02);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(),time-.01);UnityEngine.InputSystem.InputSystem.Update();
                var events=provider.CaptureTimestamped();var confirm=events.Single(e=>e.Input.MenuCancelButtonDown);var release=events.First(e=>e.Timestamp>confirm.Timestamp);
                Assert.AreEqual(.01,(release.Timestamp-confirm.Timestamp)/(double)System.Diagnostics.Stopwatch.Frequency,.00001);
                var queue=new LogicalInputQueue();foreach(var e in events)queue.Enqueue(1,e.Input);Assert.IsTrue(queue.Take(1).MenuCancelButtonDown);Assert.IsFalse(queue.Take(2).MenuCancelButtonDown);
            } finally {provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}
        }
        [Test]
        public void クリック解放時刻を使いポーズ境界で未実行UIコマンドを破棄する()
        {
            var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();var provider=new Olca.Unity.UnityInputProvider();
            try {provider.StartEventCapture();double time=UnityEngine.Time.realtimeSinceStartupAsDouble;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left),time-.02);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState(),time-.01);UnityEngine.InputSystem.InputSystem.Update();
                long timestamp=provider.PointerClickTimestamp;Assert.Less(timestamp,System.Diagnostics.Stopwatch.GetTimestamp()-System.Diagnostics.Stopwatch.Frequency/200);
                var loop=new Olca.Unity.UnityLogicalLoop();int called=0;loop.QueueTimestampedCommand(timestamp,()=>called++);loop.ReleaseInput();loop.PumpTimestamped(true,null,(f,ms,input)=>{});Assert.AreEqual(0,called);loop.Dispose();
            } finally {provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);}
        }
        [Test]
        public void 操作説明は適用済み割当だけを表示し破棄と未変更スロットを維持する()
        {
            var profile=Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();
            string Format(Olca.Core.Domain.InputBindingProfile p,GamePhase phase) => Olca.Presentation.Field.GameFieldView.FormatGameplayInstructions(p,phase);
            var original=Format(profile,GamePhase.Dungeon);
            StringAssert.Contains("Space: ジャンプ",original); StringAssert.Contains("LeftShift / RightShift: ダッシュ",original);
            var edit=profile.BeginEdit(); Assert.IsTrue(edit.TryAssign(("keyboard","exploration","JumpPrimary"),"J",out _));
            Assert.AreEqual(original,Format(profile,GamePhase.Dungeon)); edit.Discard(); Assert.AreEqual(original,Format(edit.Apply(),GamePhase.Dungeon));
            Assert.IsTrue(edit.TryAssign(("keyboard","exploration","JumpPrimary"),"J",out _));profile=edit.Apply();
            StringAssert.Contains("J: ジャンプ",Format(profile,GamePhase.Dungeon));StringAssert.DoesNotContain("Space",Format(profile,GamePhase.Dungeon));
            StringAssert.Contains("LeftShift / RightShift",Format(profile,GamePhase.Dungeon));
            var battle=Format(profile,GamePhase.Battle);
            foreach(var pair in profile.Bindings) if(pair.Key.Context=="battle") StringAssert.Contains(pair.Value,battle);
            edit=profile.BeginEdit();Assert.IsTrue(edit.TryAssign(("keyboard","battle","CardPrimary"),"Z",out _));
            battle=Format(edit.Apply(),GamePhase.Battle);StringAssert.Contains("Z: カード使用",battle);StringAssert.Contains("Space: 固有攻撃",battle);
            Assert.IsTrue(edit.TryAssign(("keyboard","exploration","DashPrimary"),null,out _));
            StringAssert.Contains("未割当 / RightShift",Format(edit.Apply(),GamePhase.Dungeon));
            Assert.AreEqual(string.Empty,Format(profile,GamePhase.CardReward));
        }
        [TestCase("not-a-key")][TestCase("1")][TestCase("None")]
        public void 不正キーは現在の実入力割当を変更しない(string key)
        {
            var provider=new Olca.Unity.UnityInputProvider(); var original=provider.KeyboardProfile;
            var edit=original.BeginEdit(); Assert.IsTrue(edit.TryAssign(("keyboard","battle","CardPrimary"),key,out _));
            try { Olca.Unity.GameplayPause.SetPaused(true); Assert.IsFalse(provider.TryApplyKeyboardProfile(edit.Apply(),out _)); Assert.AreSame(original,provider.KeyboardProfile); }
            finally { provider.Dispose(); Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false); Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame(); }
        }
        [Test]
        public void InputSystemは同じ更新内の押下解放を生成時刻付きで両方採取する()
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var provider=new Olca.Unity.UnityInputProvider();
            try {
                provider.StartEventCapture(); provider.CaptureTimestamped();
                double time=UnityEngine.Time.realtimeSinceStartupAsDouble;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftShift),time-0.02);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(),time-0.01);
                UnityEngine.InputSystem.InputSystem.Update();
                var events=provider.CaptureTimestamped();
                Assert.AreEqual(1,events.Count(e=>e.Input.DashButtonDown));
                var press=events.Single(e=>e.Input.DashButtonDown);
                var release=events.First(e=>e.Timestamp>press.Timestamp&&!e.Input.DashButton);
                Assert.AreEqual(.01,(release.Timestamp-press.Timestamp)/(double)System.Diagnostics.Stopwatch.Frequency,.00001);
            } finally { provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); }
        }
        [Test]
        public void 演出乱数を挟んでも実デッキの抽選順とゲーム乱数呼出数は変わらない()
        {
            string Draw(bool animate)
            {
                var factory = new Olca.Application.Battle.SimpleCardFactory();
                var random = new SeededRandom();
                var deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("bomb"), factory.CreateById("heal") });
                var results = new List<string>();
                for(int i=0;i<3;i++) {
                    if(animate) for(int j=0;j<97;j++){var ignored=UnityEngine.Random.value;}
                    results.Add(deck.DrawCards(1,random).Single().Id);
                }
                return string.Join(",",results)+":"+random.Calls;
            }
            Assert.AreEqual(Draw(false),Draw(true));
        }
        [Test]
        public void スティックの片軸差分イベントも既存の生軸値として採取する()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            var provider=new Olca.Unity.UnityInputProvider();
            try {
                provider.StartEventCapture();provider.CaptureTimestamped();
                UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(pad.leftStick.x,.7f);
                UnityEngine.InputSystem.InputSystem.Update();
                Assert.IsTrue(provider.CaptureTimestamped().Any(e=>System.Math.Abs(e.Input.Horizontal-.7f)<.00001f && !e.Input.AnyKey));
            } finally {provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test]
        public void 遅着イベントは処理済みフレームへ戻さず未来イベントは時刻まで保持する()
        {
            var loop=new Olca.Unity.UnityLogicalLoop();long now=0;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(System.Func<long>)(()=>now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);
            long frequency=System.Diagnostics.Stopwatch.Frequency;var presses=new List<long>();
            System.Action<long,int,LogicalInput> tick=(f,ms,input)=>{if(input.DashButtonDown)presses.Add(f);};
            now=frequency/10;loop.PumpTimestamped(true,null,tick);Assert.AreEqual(6,loop.Clock.Frame);
            loop.PumpTimestamped(true,new[]{new Olca.Unity.TimestampedLogicalInput(frequency/20,new LogicalInput{DashButtonDown=true}),
                new Olca.Unity.TimestampedLogicalInput(frequency/5,new LogicalInput{DashButtonDown=true})},tick);
            Assert.IsEmpty(presses);Assert.AreEqual(1,loop.LateInputEvents);
            now=frequency*3/10;loop.PumpTimestamped(true,null,tick);
            CollectionAssert.AreEqual(new long[]{7,13},presses);loop.Dispose();
        }
        private sealed class SeededRandom : Olca.Core.Interfaces.IRandomProvider
        {
            private readonly System.Random random = new System.Random(12345);
            public int Calls;
            public int Range(int min, int max) { Calls++; return random.Next(min, max); }
            public float Range(float min, float max) { Calls++; return min + (float)random.NextDouble() * (max - min); }
            public float Value { get { Calls++; return (float)random.NextDouble(); } }
        }
        private static IEnumerable<long> RenderFrames(int fps, bool stall)
        {
            const long total = 100000000;
            long previous = 0;
            if (stall) { previous = 23000000; yield return previous; }
            for (long frame = 1; previous < total; frame++)
            {
                long now = System.Math.Min(total, (stall ? 23000000 : 0) + frame * LogicalFrameClock.WallTicksPerSecond / fps);
                yield return now - previous; previous = now;
            }
        }
        private static List<string> BattleTrace(int fps, bool stall, int budget)
        {
            var clock = new LogicalFrameClock(); var input = new LogicalInputQueue(); var random = new SeededRandom();
            var enemy = new Enemy("trace", "fixture", 10000, ElementType.None, new Vector2Int(3, 0));
            var battle = new BattleSystem(new PlayerCharacter(10000), new List<Enemy> { enemy }, new Deck(), randomProvider: random, logger: new MockLogger());
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 7));
            var card = new Card("trace", "fixture", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "fixture", startupTime: .2f, recoveryTime: .3f);
            battle.CardUsage.EnqueueCards(new List<Card> { card, card.Clone() });
            input.Enqueue(3, new LogicalInput { CardUseButtonDown = true });
            input.Enqueue(70, new LogicalInput { Horizontal = 1, BasicAttackButton = true });
            input.Enqueue(110, new LogicalInput());
            input.Enqueue(150, new LogicalInput { CardUseButtonDown = true });
            var trace = new List<string>();
            System.Action<long, int> tick = (frame, ms) => {
                var value = input.Take(frame);
                if (value.CardUseButtonDown) battle.CardUsage.TryUseCurrentCardAsync().Forget();
                battle.TickFrame(ms, value);
                trace.Add(frame+":"+battle.Player.CurrentHp+":"+enemy.CurrentHp+":"+enemy.GridPosition+":"+battle.Player.GridPosition+":"+battle.CardUsage.RemainingCount+":"+(battle.CardUsage.InFlightCard!=null)+":"+enemy.ActiveStatus?.RemainingMs+":"+random.Calls+":"+value.BasicAttackButtonHoldTime);
            };
            foreach (long elapsed in RenderFrames(fps, stall)) { clock.Accumulate(elapsed); clock.Drain(tick, budget); }
            while (clock.PendingFrames > 0) clock.Drain(tick, budget);
            Assert.AreEqual(600, clock.Frame); Assert.IsTrue(battle.CardUsage.IsEmpty, "描画Yieldなしで効果と後隙が完了する");
            return trace;
        }
        [TestCase(30, false, 240)][TestCase(120, false, 240)][TestCase(60, true, 7)][TestCase(30, true, 7)]
        public void 同じ入力列と乱数で描画速度や停止に関係なく毎論理フレームの戦闘状態が一致する(int fps, bool stall, int budget)
        { CollectionAssert.AreEqual(BattleTrace(60, false, 240), BattleTrace(fps, stall, budget)); }
        [Test] public void 六十フレームで正確に一秒となり処理上限でも未処理時間を捨てない()
        {
            var clock = new LogicalFrameClock(); long ms = 0; clock.Accumulate(LogicalFrameClock.WallTicksPerSecond);
            Assert.AreEqual(7, clock.Drain((_, dt) => ms += dt, 7)); Assert.AreEqual(53, clock.PendingFrames);
            while(clock.PendingFrames > 0) clock.Drain((_, dt) => ms += dt, 7);
            Assert.AreEqual(60, clock.Frame); Assert.AreEqual(1000, ms);
        }
        [Test] public void 入力は論理フレームに一度だけ消費し保持時間も描画と独立する()
        {
            var queue = new LogicalInputQueue(); queue.Enqueue(2, new LogicalInput { JumpButtonDown = true, BasicAttackButton = true });
            Assert.IsFalse(queue.Take(1).JumpButtonDown); Assert.IsTrue(queue.Take(2).JumpButtonDown);
            var third = queue.Take(3); Assert.IsFalse(third.JumpButtonDown); Assert.AreEqual(2f/60, third.BasicAttackButtonHoldTime, .00001f);
            queue.Release(); Assert.IsFalse(queue.Take(4).BasicAttackButton);
        }
        [Test] public void ポーズは獲得済みの未処理フレームを保持し再入で二重更新しない()
        {
            var clock = new LogicalFrameClock(); clock.Accumulate(1000000); int ticks = 0;
            clock.Drain((_, ms) => ticks++, canAdvance: () => false); Assert.AreEqual(6, clock.PendingFrames);
            clock.Drain((_, ms) => { ticks++; Assert.AreEqual(0, clock.Drain((f,d)=>ticks++)); });
            Assert.AreEqual(6, ticks); Assert.AreEqual(0, clock.PendingFrames);
        }
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void 探索の移動接触障害物も描画分割に依存しない(int fps)
        {
            var config = DungeonConfig.CreateTestDungeon(100, 30, 50);
            config.MapCells[47] = new MapCell(47, new Obstacle("fixed", "fixture", 15));
            var game = new DungeonBattleManager(config, new PlayerCharacter(100), new Deck(), new SeededRandom(), new MockLogger());
            var clock = new LogicalFrameClock(); var input = new LogicalInputQueue(); input.Enqueue(1, new LogicalInput { DashButtonDown = true });
            int contacts = 0; game.OnBattleStarted += (b, c) => { contacts++; b.Pause(); };
            foreach(long elapsed in RenderFrames(fps, true))
            {
                clock.Accumulate(elapsed);
                clock.Drain((frame, ms) => { var value=input.Take(frame); if(value.DashButtonDown)game.StartDash(); game.TickFrame(ms); });
            }
            Assert.AreEqual(600, clock.Frame); Assert.AreEqual(1, contacts);
            Assert.AreEqual(48.5f, game.Dungeon.PlayerPosition); Assert.AreEqual(85, game.Dungeon.Player.CurrentHp);
        }
        [Test] public void 毒の期限と最終ダメージが同じ論理フレームでも最後の量を失わない()
        {
            var enemy = new Enemy("poison", "fixture", 100, ElementType.None, new Vector2Int(3,0));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,1000,7));
            var clock = new LogicalFrameClock(); int damage=0, last=0;
            clock.Accumulate(10000000); clock.Drain((frame,ms)=>{last=enemy.TickStatusFrame();damage+=last;});
            Assert.AreEqual(7,damage); Assert.Greater(last,0); Assert.IsNull(enemy.ActiveStatus);
        }
        [Test] public void 描画用乱数を使ってもゲーム専用seedの列は変わらない()
        {
            var first = new Olca.Unity.UnityRandomProvider(10); var second = new Olca.Unity.UnityRandomProvider(10);
            for(int i=0;i<100;i++){var ignored=UnityEngine.Random.value; Assert.AreEqual(first.Range(0,10000),second.Range(0,10000));}
        }
        [Test] public void 複数論理更新でもボタン押下を繰り返さず描画更新なしでも押下を保管する()
        {
            var loop = new Olca.Unity.UnityLogicalLoop();int presses=0;
            System.Action<long,int,LogicalInput> tick=(f,ms,input)=>{if(input.JumpButtonDown)presses++;};
            loop.Advance(10000,new LogicalInput{JumpButtonDown=true},tick);
            loop.Advance(9990000,new LogicalInput(),tick);
            Assert.AreEqual(1,presses);Assert.AreEqual(60,loop.Clock.Frame);
        }
        [Test] public void 耐性適用時の端数フレームを切り捨て終了フレームの毒量を処理する()
        {
            var status = new StatusEffect(StatusEffectType.Poison, 550, 60, durationResistancePercent:25);
            Assert.AreEqual(24,status.RemainingFrames); // 33 * 0.75 = 24.75 → 24
            var enemy=new Enemy("resistance","fixture",100,ElementType.None,new Vector2Int(3,0));enemy.ApplyStatus(status);
            for(int i=0;i<23;i++)Assert.AreEqual(1,enemy.TickStatusFrame());
            Assert.AreEqual(1,enemy.ActiveStatus.RemainingFrames);Assert.AreEqual(1,enemy.TickStatusFrame());Assert.IsNull(enemy.ActiveStatus);
            Assert.AreEqual(0,enemy.TickStatusFrame());
        }
        [Test] public void 保存読込による攻略置換では旧攻略の入力と予約を新攻略へ適用しない()
        {
            var loop = new Olca.Unity.UnityLogicalLoop(); int oldCommands=0,presses=0;
            loop.AfterFrames(2,()=>oldCommands++);
            loop.Advance(10000,new LogicalInput{JumpButtonDown=true},(f,ms,input)=>{});
            loop.StartNewTimeline();loop.Advance(10000000,null,(f,ms,input)=>{if(input.JumpButtonDown)presses++;});
            Assert.AreEqual(0,oldCommands);Assert.AreEqual(0,presses);Assert.AreEqual(60,loop.Clock.Frame);
        }
        [TestCase(.2f,12)][TestCase(.4f,24)][TestCase(.5f,30)][TestCase(.1001f,7)]
        public void 既存float秒の表現誤差で余計な一フレームを加えない(float seconds,int expected)
        { Assert.AreEqual(expected,LogicalFrameClock.DurationFrames(seconds)); }
        [Test] public void カスタム停止中は保持入力のタメ時間を進めず復帰後に続ける()
        {
            var input=new LogicalInputQueue();input.Enqueue(1,new LogicalInput{BasicAttackButton=true});
            float before=input.Take(1).BasicAttackButtonHoldTime;
            Assert.AreEqual(before,input.Take(2,false).BasicAttackButtonHoldTime);
            Assert.AreEqual(before,input.Take(3,false).BasicAttackButtonHoldTime);
            Assert.AreEqual(2f/60,input.Take(4).BasicAttackButtonHoldTime,.000001f);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class CardDiscardCheckpointTests
    {
        private string directory;
        private sealed class FailIo : Olca.Unity.CheckpointFileIo
        {
            public bool Fail;
            public override void WriteAndFlush(string path, byte[] bytes)
            { if (Fail) throw new System.IO.IOException("fixture保存失敗"); base.WriteAndFlush(path, bytes); }
        }
        private DungeonConfig Map(int discard)
        {
            var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
            // 通常敵の報酬は下記の固定計画seedで得る。部分一致IDをボス扱いしない。
            map.MapCells[3] = new MapCell(3, new Enemy("boss_fixture", "fixture", 50, ElementType.None, new Vector2Int(3,0)));
            map.MapCells[10] = new MapCell(10, Obstacle.CreateCardDiscardObstacle("discard-fixture", "fixture", discard));
            return map;
        }
        private Deck Deck()
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            return new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("sword"), factory.CreateById("heal") });
        }
        [SetUp] public void Setup() => directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-discard-checkpoint-"+System.Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory,true); }
        private static void ReachBattle(DungeonBattleManager manager)
        {
            for(int f=0;f<600 && manager.CurrentBattle==null && !manager.Dungeon.IsCheckpointBlocked;f++) manager.TickFrame(17);
        }
        [TestCase(1,false)][TestCase(5,false)][TestCase(1,true)]
        public void 破棄後の保存復帰で個体順序と獲得履歴を保持しクリア候補から消さない(int discard,bool failSave)
        {
            var defaults=DungeonCheckpointSession.CreateDefault(Map(discard),Deck());var io=new FailIo();
            var store=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory,io);
            var session=new DungeonCheckpointSession(defaults.Definitions,store);var random=new MockRandomProvider(fixedIntValue:1);
            var manager=new DungeonBattleManager(Map(discard),new PlayerCharacter(100),Deck(),random,new MockLogger(),checkpoints:session);
            IReadOnlyList<Card> choices=null;manager.OnCardRewardPresented+=value=>choices=value;
            ReachBattle(manager);Assert.IsNotNull(manager.CurrentBattle);
            foreach(var enemy in manager.CurrentBattle.Enemies.ToArray())enemy.TakeDamage(enemy.MaxHp,ElementType.None);
            Assert.IsNotNull(choices);manager.SelectCardReward(choices[0]);
            var before=store.Read();var originalBytes=System.IO.File.ReadAllBytes(store.ActivePath);
            Assert.AreEqual(4,before.DeckOrder.Length);Assert.AreEqual(1,before.ObtainedOrder.Length);
            Assert.AreEqual(4,before.DeckOrder.Distinct().Count());
            string acquiredId=before.ObtainedOrder.Single();Assert.AreEqual(acquiredId,before.DeckOrder.Last());
            var beforeCards=manager.Dungeon.GetCurrentDeck().GetAllCards().ToArray();Card[] afterDiscard=null;
            int hits=0;manager.Dungeon.OnObstaclePenalty+=_=>{hits++;afterDiscard=manager.Dungeon.GetCurrentDeck().GetAllCards().ToArray();};
            random.SetFixedValue(int.MaxValue);io.Fail=failSave;manager.ResumeAfterBattle();ReachBattle(manager);
            int expected=System.Math.Max(0,4-discard);Assert.AreEqual(expected,manager.Dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(1,hits);Assert.AreEqual(1,manager.Dungeon.GetObtainedCards().Count);
            CollectionAssert.AreEqual(beforeCards.Take(expected),afterDiscard,"破棄自体は残カードの順序を変えない");
            CheckpointDto pending=null;
            if(failSave) {
                Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);CollectionAssert.AreEqual(originalBytes,System.IO.File.ReadAllBytes(store.ActivePath));
                Assert.IsFalse(manager.RetryCheckpoint());manager.TickFrame(17);Assert.AreEqual(1,hits);
                pending=((CheckpointDto)typeof(DungeonSystem).GetField("pendingCheckpoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(manager.Dungeon)).Copy();
                io.Fail=false;Assert.IsTrue(manager.RetryCheckpoint());Assert.AreEqual(1,hits);
            }
            var saved=store.Read();Assert.AreEqual(3,saved.SchemaVersion);
            // 次の戦闘開始時の既存シャッフルは維持し、その確定順を保存・復元する。
            CollectionAssert.AreEquivalent(before.DeckOrder.Take(expected),saved.DeckOrder);
            if(pending!=null){CollectionAssert.AreEqual(pending.DeckOrder,saved.DeckOrder);CollectionAssert.AreEqual(pending.Battle.FirstHand,saved.Battle.FirstHand);}
            CollectionAssert.AreEqual(before.ObtainedOrder,saved.ObtainedOrder);
            Assert.IsFalse(saved.DeckOrder.Contains(acquiredId));Assert.IsTrue(saved.Cards.Any(c=>c.InstanceId==acquiredId));
            CollectionAssert.AreEqual(new[]{10},saved.ProcessedObstacleCells);
            var reopened=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);
            var resumedSession=new DungeonCheckpointSession(defaults.Definitions,reopened);
            Assert.IsTrue(resumedSession.TryRead(out var model,out var error),error);
            Assert.AreSame(model.Cards[acquiredId],model.Obtained.Single());
            CollectionAssert.AreEqual(saved.DeckOrder.Select(id=>model.Cards[id]),model.Deck.GetAllCards());
            if(expected>=2){var swords=model.Deck.GetAllCards().Where(c=>c.Id=="sword").ToArray();Assert.AreEqual(2,swords.Length);Assert.AreNotSame(swords[0],swords[1]);}
            Assert.AreEqual(ObstaclePenaltyType.CardDiscard,model.Map.MapCells[10].Obstacle.PenaltyType);
            Assert.AreEqual(discard,model.Map.MapCells[10].Obstacle.PenaltyValue);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(resumedSession,new MockRandomProvider(),new MockLogger(),out var resumed,out error),error);
            Assert.IsTrue(manager.SuspendForCheckpoint());resumed.ActivateCheckpoint();
            Assert.IsFalse(resumed.TryValidateCarryHomeSelection(model.Obtained,out _,out _));
            IReadOnlyList<Card> clear=null;IReadOnlyList<Card> validated=null;
            resumed.OnDungeonClearCompleted+=cards=>{
                clear=cards;
                Assert.IsTrue(resumed.TryValidateCarryHomeSelection(cards,out validated,out var selectionError),selectionError);
            };
            resumed.OnCardRewardPresented+=_=>resumed.SelectCardReward(null);
            foreach(var enemy in resumed.CurrentBattle.Enemies.ToArray())enemy.TakeDamage(enemy.MaxHp,ElementType.None);
            Assert.IsNotNull(clear);Assert.AreEqual(1,clear.Count);
            CollectionAssert.AreEqual(saved.ObtainedOrder,reopened.Read().ObtainedOrder);
            CollectionAssert.AreEqual(saved.DeckOrder,reopened.Read().DeckOrder);
            Assert.AreEqual(CheckpointBoundary.Terminal,reopened.Read().Boundary);
            Assert.AreSame(clear[0],validated[0]);
            var terminalBytes=System.IO.File.ReadAllBytes(reopened.ActivePath);
            var deckAfter=resumed.Dungeon.GetCurrentDeck().GetAllCards().ToArray();
            Assert.IsFalse(resumed.TryValidateCarryHomeSelection(new[]{beforeCards.Last()},out _,out _),"復帰前の同じ取得個体は再利用しない");
            Assert.IsFalse(resumed.TryValidateCarryHomeSelection(new[]{clear[0],clear[0]},out _,out _));
            resumed.SetUserPaused(true);Assert.IsFalse(resumed.TryValidateCarryHomeSelection(clear,out _,out _));
            resumed.SetUserPaused(false);Assert.IsTrue(resumed.TryValidateCarryHomeSelection(clear,out _,out error),error);
            CollectionAssert.AreEqual(deckAfter,resumed.Dungeon.GetCurrentDeck().GetAllCards());
            CollectionAssert.AreEqual(clear,resumed.Dungeon.GetObtainedCards());
            CollectionAssert.AreEqual(terminalBytes,System.IO.File.ReadAllBytes(reopened.ActivePath));
            Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(), defaults.Definitions, out var terminal, out error), error);
            var returnFlow = new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory, "progress")));
            Assert.IsTrue(returnFlow.Begin(terminal, "discard-route", new ClearReturnSettings()), returnFlow.Error);
            Assert.IsTrue(returnFlow.Toggle(0)); Assert.IsTrue(returnFlow.ConfirmSelection()); Assert.IsTrue(returnFlow.ConfirmReturn());
            Assert.AreEqual(clear[0].Id, returnFlow.Snapshot.Owned.Single().Id); Assert.AreEqual(1, returnFlow.Snapshot.Owned.Single().Count);
            CollectionAssert.AreEqual(terminalBytes, System.IO.File.ReadAllBytes(reopened.ActivePath));
        }
        [Test]
        public void 通過済み破棄セル上で再開しても二重破棄せず不正参照と旧版を拒否する()
        {
            var defaults=DungeonCheckpointSession.CreateDefault(Map(1),Deck());var store=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);
            var manager=new DungeonBattleManager(Map(1),new PlayerCharacter(100),Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:new DungeonCheckpointSession(defaults.Definitions,store));
            var dto=store.Read();dto.Sequence++;dto.Boundary=CheckpointBoundary.RewardCompleted;dto.PositionMilli=10000;dto.ProcessedObstacleCells=new[]{10};
            Assert.IsTrue(store.TryWrite(dto,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
            var session=new DungeonCheckpointSession(defaults.Definitions,store);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var resumed,out error),error);
            resumed.ActivateCheckpoint();int hits=0;resumed.Dungeon.OnObstaclePenalty+=_=>hits++;
            resumed.Tick(0);resumed.Tick(0);Assert.AreEqual(0,hits);Assert.AreEqual(3,resumed.Dungeon.GetCurrentDeck().TotalCount);
            foreach(string corruption in new[]{"version","deck","history","processed"}) {
                var invalid=dto.Copy();invalid.Sequence++;
                if(corruption=="version")invalid.SchemaVersion=2;
                if(corruption=="deck")invalid.DeckOrder=new[]{dto.DeckOrder[0],dto.DeckOrder[0]};
                if(corruption=="history")invalid.ObtainedOrder=new[]{"missing-instance"};
                if(corruption=="processed")invalid.ProcessedObstacleCells=new[]{10,10};
                Assert.IsFalse(store.TryWrite(invalid,out error),corruption);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
            }
            var changed=DungeonCheckpointSession.CreateDefault(Map(2),Deck()).Definitions;
            Assert.IsFalse(new DungeonCheckpointSession(changed,new Olca.Unity.FileCheckpointStore(changed,directory)).TryRead(out _,out error));
            CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class InputBindingEditTests
    {
        private static readonly (string,string,string) Confirm=("keyboard","menu","confirm"), Cancel=("keyboard","menu","cancel"), Optional=("keyboard","menu","detail");
        private static Dictionary<(string,string,string),string> Bindings() => new Dictionary<(string,string,string),string> {
            [Confirm]="key-a",[Cancel]="key-b",[Optional]=null,
            [("keyboard","exploration","jump")]="key-a",[("controller","menu","confirm")]="key-a"
        };
        private static InputBindingProfile Profile() => new InputBindingProfile(Bindings(),new[]{Confirm,Cancel});
        [Test]
        public void 同一場面だけ相互交換し他場面と他デバイスには影響しない()
        {
            var profile=Profile();var edit=profile.BeginEdit();
            Assert.IsTrue(edit.TryAssign(Confirm,"key-b",out var error),error);
            Assert.AreEqual("key-b",edit.Draft[Confirm]);Assert.AreEqual("key-a",edit.Draft[Cancel]);
            Assert.AreEqual("key-a",edit.Draft[("keyboard","exploration","jump")]);
            Assert.AreEqual("key-a",edit.Draft[("controller","menu","confirm")]);
            Assert.AreEqual("key-a",profile.Bindings[Confirm]);Assert.AreEqual("key-b",profile.Bindings[Cancel]);
        }
        [TestCase(false)][TestCase(true)]
        public void 必須操作の直接解除と交換による間接解除を原子的に拒否する(bool indirect)
        {
            var edit=Profile().BeginEdit();var before=edit.Draft.ToArray();
            Assert.IsFalse(edit.TryAssign(indirect?Optional:Confirm,indirect?"key-a":null,out var error));
            Assert.IsNotEmpty(error);CollectionAssert.AreEquivalent(before,edit.Draft.ToArray());
        }
        [Test]
        public void 任意操作は未割当にでき未割当同士の存在も許可する()
        {
            var bindings=Bindings();bindings[("keyboard","menu","help")]=null;
            var edit=new InputBindingProfile(bindings,new[]{Confirm,Cancel}).BeginEdit();
            Assert.IsTrue(edit.TryAssign(Optional,"key-c",out _));
            Assert.IsTrue(edit.TryAssign(("keyboard","menu","help"),"key-c",out _));
            Assert.IsNull(edit.Draft[Optional]);Assert.AreEqual("key-c",edit.Draft[("keyboard","menu","help")]);
            Assert.IsTrue(edit.TryAssign(("keyboard","menu","help"),null,out _));
        }
        [Test]
        public void 適用前の破棄と適用後の破棄はそれぞれ確定済み設定へ戻る()
        {
            var original=Profile();var edit=original.BeginEdit();
            Assert.IsTrue(edit.TryAssign(Confirm,"key-b",out _));edit.Discard();
            Assert.AreEqual("key-a",edit.Draft[Confirm]);
            Assert.IsTrue(edit.TryAssign(Confirm,"key-b",out _));var applied=edit.Apply();
            Assert.AreEqual("key-b",applied.Bindings[Confirm]);Assert.AreEqual("key-a",original.Bindings[Confirm]);
            Assert.IsTrue(edit.TryAssign(Confirm,"key-c",out _));edit.Discard();
            Assert.AreEqual("key-b",edit.Draft[Confirm]);Assert.AreEqual("key-b",applied.Bindings[Confirm]);
            Assert.IsTrue(edit.TryAssign(Confirm,"key-b",out _));Assert.AreEqual("key-a",edit.Draft[Cancel]);
        }
        [TestCase("unknown")][TestCase("empty")][TestCase("space")]
        public void 未定義操作と不正な割当文字列を原子的に拒否する(string kind)
        {
            var edit=Profile().BeginEdit();var before=edit.Draft.ToArray();
            Assert.IsFalse(edit.TryAssign(kind=="unknown"?("keyboard","menu","unknown"):Confirm,kind=="empty"?"":kind=="space"?" ":"key-c",out _));
            CollectionAssert.AreEquivalent(before,edit.Draft.ToArray());
        }
        [TestCase("duplicate")][TestCase("missing")][TestCase("unbound")][TestCase("identity")]
        public void 不正な初期設定を黙って補完せず拒否する(string kind)
        {
            var bindings=Bindings();
            if(kind=="duplicate")bindings[Cancel]="key-a";
            if(kind=="missing")bindings.Remove(Confirm);
            if(kind=="unbound")bindings[Confirm]=null;
            if(kind=="identity")bindings[("","menu","unknown")]="key-c";
            Assert.Throws<System.ArgumentException>(()=>new InputBindingProfile(bindings,new[]{Confirm,Cancel}));
        }
        [Test]
        public void 入力辞書と必須一覧と編集スナップショットの変更を原本へ漏らさない()
        {
            var bindings=Bindings();var required=new List<(string,string,string)>{Confirm,Cancel};
            var profile=new InputBindingProfile(bindings,required);bindings.Clear();required.Clear();
            var edit=profile.BeginEdit();var snapshot=edit.Draft;
            Assert.IsFalse(edit.TryAssign(Confirm,null,out _));Assert.IsTrue(edit.TryAssign(Confirm,"key-b",out _));
            Assert.AreEqual("key-a",snapshot[Confirm]);Assert.AreEqual("key-a",profile.Bindings[Confirm]);
            Assert.Throws<System.NotSupportedException>(()=>((IDictionary<(string,string,string),string>)profile.Bindings)[Confirm]="changed");
            Assert.Throws<System.NotSupportedException>(()=>((IDictionary<(string,string,string),string>)snapshot)[Confirm]="changed");
        }
        [Test]
        public void 並行した編集は互いへ漏れず採用する設定を呼出し側が選べる()
        {
            var profile=Profile();var a=profile.BeginEdit();var b=profile.BeginEdit();
            Assert.IsTrue(a.TryAssign(Confirm,"key-b",out _));var committed=a.Apply();
            Assert.AreEqual("key-a",b.Draft[Confirm]);Assert.AreEqual("key-b",committed.Bindings[Confirm]);
            Assert.IsTrue(b.TryAssign(("controller","menu","confirm"),"button-c",out _));
            var controllerEdit=b.Apply();Assert.AreEqual("key-a",controllerEdit.Bindings[Confirm]);
            Assert.AreEqual("key-a",committed.Bindings[("controller","menu","confirm")]);
        }
    }
}


namespace Olca.Tests.Integration
{
    [TestFixture]
    public class ContinuousHoldBoundaryTests
    {
        [TestCase(true)][TestCase(false)]
        public void 同じ論理フレームの解放再押下で以前の保持時間を引き継がない(bool advancing)
        {
            var queue=new LogicalInputQueue();queue.Enqueue(1,new LogicalInput{BasicAttackButton=true});
            LogicalInput value=null;for(int f=1;f<=30;f++)value=queue.Take(f);
            Assert.AreEqual(.5f,value.BasicAttackButtonHoldTime,.000001f);
            queue.Enqueue(31,new LogicalInput{BasicAttackButton=false});
            queue.Enqueue(31,new LogicalInput{BasicAttackButton=true,CardUseButtonDown=true});
            value=queue.Take(31,advancing);
            Assert.IsTrue(value.BasicAttackButton);Assert.IsTrue(value.CardUseButtonDown);
            Assert.AreEqual(advancing?1f/60:0f,value.BasicAttackButtonHoldTime,.000001f);
            value=queue.Take(32);Assert.IsFalse(value.CardUseButtonDown);
            Assert.AreEqual(advancing?2f/60:1f/60,value.BasicAttackButtonHoldTime,.000001f);
            queue.Enqueue(33,new LogicalInput{BasicAttackButton=false});
            queue.Enqueue(33,new LogicalInput{BasicAttackButton=true});
            queue.Enqueue(33,new LogicalInput{BasicAttackButton=false});
            value=queue.Take(33);Assert.IsFalse(value.BasicAttackButton);Assert.AreEqual(0,value.BasicAttackButtonHoldTime);
        }
        [TestCase(30)][TestCase(60)][TestCase(120)][TestCase(0)]
        public void 生成時刻が同じフレームに入る解放再押下を描画間隔に関係なく反映する(int fps)
        {
            var loop=new Olca.Unity.UnityLogicalLoop();long now=0;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(System.Func<long>)(()=>now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);
            long frequency=System.Diagnostics.Stopwatch.Frequency;
            var events=new[]{
                new Olca.Unity.TimestampedLogicalInput(frequency*10/1000,new LogicalInput{BasicAttackButton=true}),
                new Olca.Unity.TimestampedLogicalInput(frequency*510/1000,new LogicalInput{BasicAttackButton=false}),
                new Olca.Unity.TimestampedLogicalInput(frequency*511/1000,new LogicalInput{BasicAttackButton=true})};
            var holds=new Dictionary<long,float>();bool submitted=false;
            try {
                for(int render=1;now<frequency*600/1000;render++) {
                    now=fps==0?frequency*600/1000:System.Math.Min(frequency*600/1000,frequency*render/fps);
                    loop.PumpTimestamped(true,submitted?null:events,(f,ms,input)=>holds[f]=input.BasicAttackButtonHoldTime);submitted=true;
                }
                Assert.AreEqual(36,loop.Clock.Frame);Assert.AreEqual(0,loop.LateInputEvents);
                Assert.AreEqual(.5f,holds[30],.000001f);Assert.AreEqual(1f/60,holds[31],.000001f);
                Assert.AreEqual(2f/60,holds[32],.000001f);Assert.AreEqual(6f/60,holds[36],.000001f);
            } finally {loop.Dispose();}
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class FloorSequenceFoundationTests
    {
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo
        {
            public string Fault;
            public System.Action BeforeReplace;
            public override void Replace(string temporary, string destination, string backup)
            {
                BeforeReplace?.Invoke();
                if (Fault == "before") throw new System.IO.IOException("階層保存失敗fixture");
                base.Replace(temporary, destination, backup);
                if (Fault == "after") throw new System.IO.IOException("保存応答喪失fixture");
            }
        }
        private string directory;
        private DungeonConfig[] maps;
        private Deck deck;
        private FloorSequenceDefinitions definitions;
        private CheckpointDto departure;
        private FaultIo io;
        private Olca.Unity.FloorSequenceFileStore store;
        private FloorSequenceSession run;
        [SetUp] public void Setup()
        {
            directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-floors-" + System.Guid.NewGuid().ToString("N"));
            // 階層数・配置はテスト専用。製品シーンへ注入しない。
            maps = new[] { DungeonConfig.CreateTestDungeon(4), DungeonConfig.CreateTestDungeon(5), DungeonConfig.CreateTestDungeon(6) };
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("heal") });
            definitions = new FloorSequenceDefinitions("fixture-route", "v1", maps, deck);
            var legacy = DungeonCheckpointSession.CreateDefault(maps[0], deck);
            new DungeonBattleManager(maps[0], new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: legacy);
            departure = legacy.Store.Read(); io = new FaultIo();
            store = new Olca.Unity.FloorSequenceFileStore(definitions, directory, io);
            run = new FloorSequenceSession(definitions, store);
        }
        [TearDown] public void Cleanup() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        private static CheckpointDto Complete(CheckpointDto previous)
        {
            var next = previous.Copy(); next.Sequence++; next.Boundary = CheckpointBoundary.Terminal;
            foreach (var enemy in next.Enemies) enemy.Hp = 0;
            return next;
        }
        [Test]
        public void 自然回復定義は通常階層終端の保存とファイル再起動を越えて保持する()
        {
            foreach (var map in maps) foreach (var index in Enumerable.Range(0, map.MapCells.Count).Where(i => map.MapCells[i].Enemy != null).ToArray()) {
                var e = map.MapCells[index].Enemy;
                map.MapCells[index] = new MapCell(index, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element,
                    e.GridPosition, poiseDefinition: new EnemyPoiseDefinition(30, 6, 9)));
            }
            definitions = new FloorSequenceDefinitions("fixture-route", "v1", maps, deck);
            store = new Olca.Unity.FloorSequenceFileStore(definitions, directory);
            run = new FloorSequenceSession(definitions, store);
            var checkpoints = definitions.CreateCheckpointSession(0, run);
            var player = new PlayerCharacter(100, poiseRecoveryDelayFrames: 6);
            var manager = new DungeonBattleManager(definitions.CreateInitialMap(0), player, deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, hasNextFloor: true);
            manager.OnCardRewardPresented += _ => manager.SelectCardReward(null);
            for (int f = 0; f < 600 && !manager.Dungeon.IsFloorCompleted; f++) {
                manager.TickFrame(17);
                var battle = manager.CurrentBattle;
                if (battle == null) continue;
                player.TakeDamage(1, poiseDamage: 7);
                foreach (var enemy in battle.Enemies.ToArray()) if (!enemy.IsDead) enemy.TakeDamage(enemy.MaxHp, ElementType.None);
            }
            Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked); Assert.IsTrue(manager.Dungeon.IsFloorCompleted);
            Assert.AreEqual(1, run.Current.FloorIndex); Assert.AreEqual(6, run.Current.State.Player.PoiseRecoveryDelayFrames);
            Assert.AreEqual(0, run.Current.State.Player.PoiseRecoveryRemainingFrames);
            var reopened = new FloorSequenceSession(definitions, new Olca.Unity.FloorSequenceFileStore(definitions, directory));
            Assert.IsTrue(reopened.TryLoad(out var error), error);
            Assert.AreEqual(6, reopened.Current.State.Player.PoiseRecoveryDelayFrames);
            Assert.IsTrue(reopened.Current.State.Enemies.Values.All(e => e.PoiseRecoveryDelayFrames == 9 && e.PoiseRecoveryRemainingFrames == 0));
        }

        [TestCase("empty")] [TestCase("null")] [TestCase("future-cell")] [TestCase("final-boss")]
        public void 現在階層の保存前に階層列全体を検証する(string fault)
        {
            var bad = maps.ToArray();
            if (fault == "empty") bad = new DungeonConfig[0];
            if (fault == "null") bad[1] = null;
            if (fault == "future-cell") bad[1].MapCells[0] = null;
            if (fault == "final-boss") bad[2] = new DungeonConfig("no-boss", "検証", 1, new List<MapCell> { new MapCell(0) });
            Assert.Throws<System.ArgumentException>(() => new FloorSequenceDefinitions("fixture-route", "v1", bad, deck));
            Assert.IsFalse(store.HasExistingData); Assert.IsNull(run.Current);
        }
        [TestCase(-1)] [TestCase(3)]
        public void 範囲外の現在階層を復元も保存もしない(int index)
        {
            Assert.IsTrue(run.TryStart(departure, out var error), error); var previous = System.IO.File.ReadAllBytes(store.ActivePath);
            var bad = run.Current.Snapshot; bad.FloorIndex = index;
            Assert.IsFalse(definitions.TryRestore(bad, out _, out _)); Assert.IsFalse(store.TryWrite(bad, out _));
            CollectionAssert.AreEqual(previous, System.IO.File.ReadAllBytes(store.ActivePath));
        }
        [TestCase("hash")] [TestCase("card")] [TestCase("version")] [TestCase("wrong-map")]
        public void 未解決または異なる定義の階層保存を拒否する(string fault)
        {
            Assert.IsTrue(run.TryStart(departure, out var error), error); var bad = run.Current.Snapshot;
            if (fault == "hash") bad.RouteHash = "unknown";
            if (fault == "card") bad.State.Cards[0].DefinitionKey = "unknown";
            if (fault == "version") bad.FormatVersion = 99;
            if (fault == "wrong-map") bad.FloorIndex = 1;
            Assert.IsFalse(definitions.TryRestore(bad, out _, out _));
            Assert.IsFalse(store.TryWrite(bad, out _)); Assert.AreEqual(0, run.Current.FloorIndex);
        }
        [TestCase("before")] [TestCase("after")] [TestCase(null)]
        public void 次階層開始保存の成功前に現モデルを交換せず再試行と再入でも二重遷移しない(string fault)
        {
            Assert.IsTrue(run.TryStart(departure, out var error), error);
            var old = run.Current; var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
            var completed = Complete(old.State.Snapshot); completed.Player.Hp = 37; completed.Player.FireResistance = -200;
            var acquired = completed.Cards[0].Copy(); acquired.InstanceId = "acquired";
            var removed = completed.Cards[1].Copy(); removed.InstanceId = "removed";
            completed.Cards = completed.Cards.Concat(new[] { acquired, removed }).ToArray();
            completed.DeckOrder = completed.DeckOrder.Concat(new[] { acquired.InstanceId }).ToArray();
            completed.ObtainedOrder = new[] { acquired.InstanceId, removed.InstanceId };
            io.Fault = fault; int attempts = 0;
            io.BeforeReplace = () => {
                attempts++; Assert.AreSame(old, run.Current);
                Assert.IsFalse(run.TryTransferToNextFloor(0, completed, out _));
                Assert.IsFalse(run.Retry(out _)); Assert.IsFalse(run.TryLoad(out _));
            };
            bool success = run.TryTransferToNextFloor(0, completed, out error);
            if (fault == "before") {
                Assert.IsFalse(success); Assert.IsTrue(run.HasPendingSave); Assert.AreSame(old, run.Current);
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
                completed.Player.Hp = 1; completed.DeckOrder = new string[0]; // 再試行の固定候補を変えない。
                io.Fault = null; Assert.IsTrue(run.Retry(out error), error);
            }
            else Assert.IsTrue(success, error);
            Assert.AreEqual(fault == "before" ? 2 : 1, attempts);
            Assert.IsFalse(run.HasPendingSave); Assert.AreEqual(1, run.Current.FloorIndex);
            Assert.AreEqual(37, run.Current.State.Player.CurrentHp); Assert.AreEqual(-200, run.Current.State.Player.Resistances.Fire);
            Assert.AreEqual(3, run.Current.State.Deck.TotalCount); Assert.AreEqual(2, run.Current.State.Obtained.Count);
            Assert.AreSame(run.Current.State.Cards["acquired"], run.Current.State.Obtained[0]);
            Assert.IsTrue(run.Current.State.Deck.GetAllCards().Contains(run.Current.State.Obtained[0]));
            Assert.IsFalse(run.Current.State.Deck.GetAllCards().Contains(run.Current.State.Obtained[1]));
            Assert.AreEqual(0, run.Current.Snapshot.State.PositionMilli); Assert.AreEqual(CheckpointBoundary.Departure, run.Current.State.Snapshot.Boundary);
            Assert.IsTrue(run.Current.State.Enemies.Values.All(e => e.CurrentHp == e.MaxHp));
            Assert.IsFalse(run.TryTransferToNextFloor(0, completed, out _)); Assert.IsFalse(run.Retry(out _));
            io.BeforeReplace = null;
            var reopened = new FloorSequenceSession(definitions, new Olca.Unity.FloorSequenceFileStore(definitions, directory));
            Assert.IsTrue(reopened.TryLoad(out error), error); Assert.AreEqual(1, reopened.Current.FloorIndex);
            Assert.AreEqual(37, reopened.Current.State.Player.CurrentHp);
            Assert.IsTrue(reopened.TryTransferToNextFloor(1, Complete(reopened.Current.State.Snapshot), out error), error);
            Assert.AreEqual(2, reopened.Current.FloorIndex);
            Assert.IsFalse(reopened.TryTransferToNextFloor(1, Complete(reopened.Current.State.Snapshot), out _));
            Assert.IsFalse(reopened.TryTransferToNextFloor(2, Complete(reopened.Current.State.Snapshot), out _));
        }
        [Test]
        public void 階層列と単階層のファイルは独立し旧保存を変更しない()
        {
            var legacyDefinitions = DungeonCheckpointSession.CreateDefault(maps[0], deck).Definitions;
            var legacy = new Olca.Unity.FileCheckpointStore(legacyDefinitions, directory);
            Assert.IsTrue(legacy.TryWrite(departure, out var error), error); var bytes = System.IO.File.ReadAllBytes(legacy.ActivePath);
            Assert.IsTrue(run.TryStart(departure, out error), error);
            var boundary = departure.Copy(); boundary.Sequence++; boundary.Boundary = CheckpointBoundary.RewardCompleted; boundary.Player.Hp = 61;
            Assert.IsTrue(run.TrySaveCurrent(0, boundary, out error), error);
            Assert.IsFalse(run.TrySaveCurrent(0, boundary, out _));
            Assert.IsTrue(run.TryLoad(out error), error); Assert.AreEqual(61, run.Current.State.Player.CurrentHp);
            CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(legacy.ActivePath));
            Assert.IsTrue(CheckpointCodec.TryRestore(legacy.Read(), legacyDefinitions, out var restored, out error), error);
            Assert.AreEqual(100, restored.Player.CurrentHp);
        }
        [Test]
        public void 単階層列も保存できて最終階層の先へは進まない()
        {
            var single = new FloorSequenceDefinitions("single", "v1", new[] { maps[0] }, deck);
            var singleStore = new Olca.Unity.FloorSequenceFileStore(single, directory);
            var session = new FloorSequenceSession(single, singleStore);
            Assert.IsTrue(session.TryStart(departure, out var error), error);
            Assert.IsFalse(session.TryTransferToNextFloor(0, Complete(departure), out _));
            Assert.IsTrue(session.TryLoad(out error), error); Assert.AreEqual(0, session.Current.FloorIndex);
        }
        [Test]
        public void ボスなし中間階層の提供を許可するが敗北状態は次階層へ運ばない()
        {
            var first = new DungeonConfig("empty-middle", "検証", 2, new List<MapCell> { new MapCell(0), new MapCell(1) });
            var route = new FloorSequenceDefinitions("route", "v1", new[] { first, maps[2] }, deck);
            var legacy = DungeonCheckpointSession.CreateDefault(first, deck);
            new DungeonBattleManager(first, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: legacy);
            var session = new FloorSequenceSession(route, new Olca.Unity.FloorSequenceFileStore(route, directory));
            Assert.IsTrue(session.TryStart(legacy.Store.Read(), out var error), error);
            var completed = Complete(legacy.Store.Read()); completed.Player.Hp = 0;
            Assert.IsFalse(session.TryTransferToNextFloor(0, completed, out _)); Assert.AreEqual(0, session.Current.FloorIndex);
            completed.Player.Hp = 100;
            Assert.IsTrue(session.TryTransferToNextFloor(0, completed, out error), error);
            Assert.AreEqual(1, session.Current.FloorIndex);
        }
        [Test]
        public void 破損した階層保存を読み直しても現モデルを捨てず上書きしない()
        {
            Assert.IsTrue(run.TryStart(departure, out var error), error); var current = run.Current;
            System.IO.File.WriteAllText(store.ActivePath, "broken-fixture");
            Assert.IsFalse(run.TryLoad(out error)); Assert.IsNotEmpty(error); Assert.AreSame(current, run.Current);
            Assert.IsFalse(run.TryTransferToNextFloor(0, Complete(departure), out error));
            Assert.AreSame(current, run.Current); Assert.AreEqual("broken-fixture", System.IO.File.ReadAllText(store.ActivePath));
        }
        [Test]
        public void 定義後の入力マップ変更で階層列を改変できず将来階層の変更もhashへ反映する()
        {
            var hash = definitions.RouteHash; maps[1].MapCells[0] = null;
            Assert.IsTrue(run.TryStart(departure, out var error), error);
            Assert.IsTrue(run.TryTransferToNextFloor(0, Complete(departure), out error), error);
            Assert.AreEqual(hash, run.Current.Snapshot.RouteHash); Assert.IsNotNull(run.Current.State.Map.MapCells[0]);
            var alternate = new FloorSequenceDefinitions("fixture-route", "v1", new[] { maps[0], DungeonConfig.CreateTestDungeon(7), maps[2] }, deck);
            Assert.AreNotEqual(hash, alternate.RouteHash); Assert.IsFalse(alternate.TryRestore(run.Current.Snapshot, out _, out _));
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class FloorSequenceControllerTests
    {
        private const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private readonly List<UnityEngine.GameObject> objects = new List<UnityEngine.GameObject>();
        private string directory;
        private FloorSequenceDefinitions definitions;
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Fail;
            public System.Action BeforeReplace;
            public override void Replace(string temporary, string destination, string backup)
            {
                BeforeReplace?.Invoke();
                if (Fail) throw new System.IO.IOException("Controller階層保存失敗fixture");
                base.Replace(temporary, destination, backup);
            }
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) {var type=target.GetType();System.Reflection.FieldInfo field=null;while(type!=null&&field==null){field=type.GetField(name,Hidden);type=type.BaseType;}if(field==null)throw new System.MissingFieldException(name);field.SetValue(target,value);}
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
        private static DungeonBattleManager Model(Olca.Application.Dungeon.DungeonBattleController controller) => (DungeonBattleManager)Get(controller, "manager");
        private static Olca.Unity.FloorSequenceFileStore Store(Olca.Application.Dungeon.DungeonBattleController controller) => (Olca.Unity.FloorSequenceFileStore)Get(controller, "floorStore");
        private static DungeonConfig Empty(int cells) => new DungeonConfig("empty-" + cells, "終端fixture", cells,
            Enumerable.Range(0, cells).Select(i => new MapCell(i)).ToList());
        [SetUp] public void Setup() { definitions = null; directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-floor-controller-" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup()
        {
            foreach (var host in objects) if (host != null) {
                // 非アクティブなfixtureのStartを手動実行したため、Unity任せではOnDestroyが届かない。
                var controller=host.GetComponent<Olca.Application.Dungeon.DungeonBattleController>();
                if(controller!=null) {
                    ((Olca.Unity.UnityInputProvider)Get(controller,"inputProvider"))?.Dispose();
                    Call(controller,"CancelExplorationResume");
                    Call(controller,"ApplyUserPauseImmediate",false);
                    ((Olca.Unity.UnityLogicalLoop)Get(controller,"explorationResumeLoop")).Dispose();
                }
                UnityEngine.Object.DestroyImmediate(host);
            }
            objects.Clear();
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
        }
        private Olca.Application.Dungeon.DungeonBattleController Start(DungeonConfig[] maps = null, int? recoveryFrames = null, System.Action<Olca.Application.Dungeon.DungeonBattleController> configure = null, bool production = false)
        {
            var host = new UnityEngine.GameObject("floor-controller-fixture"); host.SetActive(false); objects.Add(host);
            var controller = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            if (recoveryFrames.HasValue) Set(controller, "playerPoiseRecoveryFrames", recoveryFrames.Value);
            configure?.Invoke(controller);
            controller.ConfigureCheckpointDirectory(directory); controller.ConfigureGameplaySettingsDirectory(System.IO.Path.Combine(directory,"settings")); controller.ConfigureSimulationSeed(7);
            if (maps != null) {
                var deck = (Deck)controller.GetType().GetMethod("CreateInitialDeck", Hidden).Invoke(controller, null);
                definitions = new FloorSequenceDefinitions("controller-fixture", "v1", maps, deck);
            }
            if (definitions != null) controller.ConfigureFloorSequence(definitions);
            // 既存の単マップ試験は明示設定で従来の直接起動契約を検証する。選択画面は別の通常UI試験へ分離。
            if (!production && definitions == null && Get(controller,"configuredDungeon") == null && Get(controller,"dungeonChoices") == null &&
                (!System.IO.Directory.Exists(directory) || !System.IO.Directory.EnumerateFiles(directory,"*.json").Any()))
                controller.ConfigureDungeon((DungeonConfig)controller.GetType().GetMethod("CreateDefaultDungeonWithoutVisits",Hidden).Invoke(controller,new object[]{false}));
            var view = host.AddComponent<Olca.Presentation.Field.GameFieldView>(); Set(controller, "gameFieldView", view);
            var hud = host.AddComponent<Olca.Presentation.Dungeon.DungeonHudView>(); Set(controller, "dungeonHudView", hud);
            Call(controller, "Start"); return controller;
        }
        private static void Tick(Olca.Application.Dungeon.DungeonBattleController controller, LogicalInput input = null)
            => Call(controller, "TickLogicalFrame", 1L, 17, input ?? new LogicalInput());
        [Test]
        public void 停止地点なしでアイテム報酬だけある帰還進行を再起動しても保存依存を満たす()
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> configure = controller => {
                Set(controller, "dungeonCellCount", 6); Set(controller, "enemySpacing", 1); Set(controller, "firstEnemyCell", 2);
                var original = (DungeonConfig)controller.GetType().GetMethod("CreateDefaultDungeonWithoutTerrain", Hidden).Invoke(controller, new object[] { false });
                var map = new DungeonConfig(original.DungeonId, original.DungeonName, original.TotalCellCount, original.MapCells, original.InitialDeckSize,
                    null, original.StrongEnemyCells, original.ItemRewards, original.ItemRewardChance);
                Assert.AreEqual(0, map.Visits.Count); Assert.Greater(map.ItemRewards.Count, 0);
                controller.ConfigureDungeon(map);
            };
            var controller = Start(configure: configure); ToBattle(controller);
            controller.SetUserPaused(true); KeyboardButton(controller, "RetreatButton"); KeyboardButton(controller, "ConfirmRetreat");
            Assert.AreEqual(RunReturnOutcome.Retreated, controller.PersistentProgress.Flow.Outcome);
            var progress = (Olca.Unity.ClearReturnFileStore)Get(controller, "clearReturnStore"); var before = System.IO.File.ReadAllBytes(progress.ActivePath);
            var restarted = Start(configure: configure);
            Assert.IsNotNull(Model(restarted)); Assert.IsFalse(Model(restarted).Dungeon.IsCheckpointBlocked);
            Assert.AreEqual(ClearReturnStage.Returned, restarted.PersistentProgress.Flow.Stage);
            CollectionAssert.AreEqual(before, System.IO.File.ReadAllBytes(progress.ActivePath));
        }

        [TestCase(false)] [TestCase(true)]
        public void 敵定義元は通常Controllerの単階層と複数階層で保存復帰へ再供給される(bool floors)
        {
            int generated=0;var instances=new List<IEnemyAIAction>();
            var map=DungeonConfig.CreateTestDungeon(20,30,3);
            var entries=map.MapCells.Where(x=>x.Enemy!=null).Select(x=>x.Enemy.EnemyId).Distinct().ToDictionary(id=>id,id=>(System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>)((enemy,cards)=>{
                Assert.IsNotNull(cards);generated++;var action=new EnemyMovementAction(EnemyMovementKind.Stay,0);instances.Add(action);
                return new EnemyAIRegistration(new[]{new EnemyActionPattern(71,new[]{action},EnemyPatternEndMode.Stop)},71);
            }));
            var registry=new EnemyAIRegistry(entries);
            var c=Start(maps:floors?new[]{map,DungeonConfig.CreateTestDungeon(20,30,3)}:null,configure:host=>{host.ConfigureEnemyAIProvider(registry);if(!floors)host.ConfigureDungeon(map);});
            ToBattle(c);var before=Model(c).CurrentBattle;Assert.Greater(generated,0);int count=generated;
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreNotSame(before,Model(c).CurrentBattle);
            Assert.AreEqual(count*2,generated);Assert.AreEqual(generated,instances.Distinct().Count());
            Assert.IsTrue(Model(c).CurrentBattle.CustomScreen.IsOpen);Assert.AreEqual(0,Model(c).CurrentBattle.LogicalFrame);
        }

        private static void FinishExplorationCountdown(Olca.Application.Dungeon.DungeonBattleController controller)
        {
            if (!controller.IsExplorationResumeCounting) return;
            Assert.IsTrue(controller.IsUserPaused);
            for (int n = 0; n < 89; n++) Call(controller, "AdvanceExplorationResumeFrame");
            Assert.IsTrue(controller.IsUserPaused); Assert.AreEqual(1, controller.ExplorationResumeRemainingFrames);
            Call(controller, "AdvanceExplorationResumeFrame");
            Assert.IsFalse(controller.IsExplorationResumeCounting);
        }
        [Test] public void 探索再開は九十フレームまで探索と入力を停止し連続再開で延長しない()
        {
            var c=Start();var m=Model(c);float x=m.Dungeon.PlayerPosition;float scale=UnityEngine.Time.timeScale;
            c.SetUserPaused(true);c.SetUserPaused(false);
            Assert.IsTrue(c.IsExplorationResumeCounting);Assert.AreEqual(90,c.ExplorationResumeRemainingFrames);
            c.SetUserPaused(false);Assert.AreEqual(90,c.ExplorationResumeRemainingFrames);
            for(int n=0;n<30;n++)Tick(c,new LogicalInput{Horizontal=1,JumpButtonDown=true});
            Assert.AreEqual(x,m.Dungeon.PlayerPosition);Assert.IsTrue(Olca.Unity.GameplayPause.InputBlocked);Assert.AreEqual(0,UnityEngine.Time.timeScale);
            FinishExplorationCountdown(c);Assert.IsFalse(c.IsUserPaused);Assert.AreEqual(scale,UnityEngine.Time.timeScale);
        }
        [TestCase(1)] [TestCase(30)] [TestCase(89)]
        public void 探索カウント中の再停止は残りを捨て次の明示再開で全長を使う(int frames)
        {
            var c=Start();c.SetUserPaused(true);c.SetUserPaused(false);
            for(int n=0;n<frames;n++)Call(c,"AdvanceExplorationResumeFrame");
            c.SetUserPaused(true);Assert.IsFalse(c.IsExplorationResumeCounting);Assert.IsTrue(c.IsUserPaused);
            Call(c,"UpdateExplorationResume");Assert.IsTrue(c.IsUserPaused);
            c.SetUserPaused(false);Assert.AreEqual(90,c.ExplorationResumeRemainingFrames);FinishExplorationCountdown(c);Assert.IsFalse(c.IsUserPaused);
        }
        [Test] public void 探索カウント中の非アクティブ復帰は停止を保ち保存復帰で旧期限を持ち越さない()
        {
            var c=Start();c.SetUserPaused(true);c.SetUserPaused(false);Call(c,"AdvanceExplorationResumeFrame");
            Call(c,"OnApplicationPause",true);Assert.IsFalse(c.IsExplorationResumeCounting);
            Call(c,"OnApplicationPause",false);Call(c,"UpdateExplorationResume");Assert.IsTrue(c.IsUserPaused);
            c.SetUserPaused(false);Assert.AreEqual(90,c.ExplorationResumeRemainingFrames);
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.IsFalse(c.IsExplorationResumeCounting);
            var paused=c.IsUserPaused;for(int n=0;n<100;n++)Call(c,"AdvanceExplorationResumeFrame");Assert.AreEqual(paused,c.IsUserPaused);
        }
        [Test] public void 探索カウントの無効化は解除せず次の再開で全長を使う()
        {
            var c=Start();c.SetUserPaused(true);c.SetUserPaused(false);Call(c,"AdvanceExplorationResumeFrame");
            c.enabled=false;Call(c,"OnDisable");Assert.IsFalse(c.IsExplorationResumeCounting);Assert.IsTrue(c.IsUserPaused);
            c.enabled=true;c.SetUserPaused(false);Assert.AreEqual(90,c.ExplorationResumeRemainingFrames);FinishExplorationCountdown(c);
        }

        [TestCase(false)] [TestCase(true)]
        public void パッド切断は探索とカスタムを停止し再接続なしの再開操作を許す(bool battle)
        {
            var c=Start();if(battle)ToBattle(c);
            var m=Model(c);float scale=UnityEngine.Time.timeScale;
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try {
                UnityEngine.InputSystem.InputSystem.DisableDevice(pad);Assert.IsFalse(c.IsUserPaused,"無効化は物理切断ではない");
                UnityEngine.InputSystem.InputSystem.EnableDevice(pad);Call(c,"OnApplicationFocus",false);Assert.IsFalse(c.IsUserPaused);
                float x=m.Dungeon.PlayerPosition;long frame=m.CurrentBattle?.LogicalFrame??0;
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                Assert.IsTrue(c.IsUserPaused);Assert.AreEqual(0,UnityEngine.Time.timeScale);
                for(int n=0;n<30;n++)Tick(c,new LogicalInput{Horizontal=1});
                Assert.AreEqual(x,m.Dungeon.PlayerPosition);Assert.AreEqual(frame,m.CurrentBattle?.LogicalFrame??0);
                var replacement=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                try {Assert.IsTrue(c.IsUserPaused,"再接続で自動再開しない");}
                finally {UnityEngine.InputSystem.InputSystem.RemoveDevice(replacement);}
                KeyboardButton(c,"ResumeButton");if(!battle)FinishExplorationCountdown(c);Assert.IsFalse(c.IsUserPaused);Assert.AreEqual(scale,UnityEngine.Time.timeScale);
            } finally {if(pad.added)UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);c.SetUserPaused(false);UnityEngine.Time.timeScale=scale;}
        }
        [TestCase(true)] [TestCase(false)]
        public void 勝敗演出中のパッド切断は演出完了までメニューを遅延し一度だけ停止する(bool victory)
        {
            var c=Start();ToBattle(c);var m=Model(c);
            var callback=(System.Action<BattleResult>)System.Delegate.CreateDelegate(typeof(System.Action<BattleResult>),c,c.GetType().GetMethod("OnBattleEndPresentationRequested",Hidden));
            m.OnBattleEndPresentationRequested-=callback;
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try {
                if(victory)foreach(var e in m.CurrentBattle.Enemies.ToArray())e.TakeDamage(100000,ElementType.None);else m.CurrentBattle.Player.TakeDamage(100000);
                Assert.IsTrue(m.IsBattleEndPresentationPending);
                c.SetUserPaused(true);
                Assert.IsFalse(c.IsUserPaused);Assert.IsFalse(((UnityEngine.GameObject)Get(c,"pauseOverlay")).activeSelf);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                Assert.IsFalse(c.IsUserPaused);Assert.IsFalse(((UnityEngine.GameObject)Get(c,"pauseOverlay")).activeSelf);
                Assert.IsTrue(m.CompleteBattleEndPresentation());Assert.IsTrue(c.IsUserPaused);
                Assert.IsFalse(m.CompleteBattleEndPresentation());KeyboardButton(c,"ResumeButton");Assert.IsFalse(c.IsUserPaused);
                Assert.IsNull(Get(c,"automaticPauseOwner"));
            } finally {if(pad.added)UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);c.SetUserPaused(false);}
        }

        [TestCase(false)] [TestCase(true)]
        public void アプリ休止は探索とカスタムを停止し復帰やフォーカス通知では再開しない(bool battle)
        {
            var c=Start();if(battle)ToBattle(c);var m=Model(c);float scale=UnityEngine.Time.timeScale;
            Call(c,"OnApplicationPause",false);Call(c,"OnApplicationFocus",false);Call(c,"OnApplicationFocus",true);
            Assert.IsFalse(c.IsUserPaused,"PCのフォーカスだけでは停止しない");
            float x=m.Dungeon.PlayerPosition;long frame=m.CurrentBattle?.LogicalFrame??0;
            Call(c,"OnApplicationPause",true);Call(c,"OnApplicationPause",true);
            Assert.IsTrue(c.IsUserPaused);Assert.AreEqual(0,UnityEngine.Time.timeScale);
            for(int n=0;n<30;n++)Tick(c,new LogicalInput{Horizontal=1});
            Assert.AreEqual(x,m.Dungeon.PlayerPosition);Assert.AreEqual(frame,m.CurrentBattle?.LogicalFrame??0);
            Call(c,"OnApplicationPause",false);Call(c,"OnApplicationFocus",true);
            Assert.IsTrue(c.IsUserPaused);KeyboardButton(c,"ResumeButton");if(!battle)FinishExplorationCountdown(c);
            Assert.IsFalse(c.IsUserPaused);Assert.AreEqual(scale,UnityEngine.Time.timeScale);
        }
        [Test] public void 手動停止と切断停止はアプリ復帰に解除されず停止前速度を保持する()
        {
            var c=Start();float scale=UnityEngine.Time.timeScale;c.SetUserPaused(true);
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try {
                Call(c,"OnApplicationPause",true);UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                Call(c,"OnApplicationPause",false);Assert.IsTrue(c.IsUserPaused);
                KeyboardButton(c,"ResumeButton");FinishExplorationCountdown(c);Assert.AreEqual(scale,UnityEngine.Time.timeScale);
            } finally {if(pad.added)UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test] public void 初期化前の休止要求は初期化後も保持し攻略置換でも解除しない()
        {
            var c=Start(configure:host=>Call(host,"OnApplicationPause",true));Assert.IsTrue(c.IsUserPaused);
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.IsTrue(c.IsUserPaused);
            Call(c,"OnApplicationPause",false);Assert.IsTrue(c.IsUserPaused);
            KeyboardButton(c,"ResumeButton");FinishExplorationCountdown(c);Assert.IsFalse(c.IsUserPaused);
        }
        [TestCase(true)] [TestCase(false)]
        public void 勝敗演出中の休止と復帰は完了後の停止を一度だけ引き継ぐ(bool victory)
        {
            var c=Start();ToBattle(c);var m=Model(c);
            var callback=(System.Action<BattleResult>)System.Delegate.CreateDelegate(typeof(System.Action<BattleResult>),c,c.GetType().GetMethod("OnBattleEndPresentationRequested",Hidden));
            m.OnBattleEndPresentationRequested-=callback;
            if(victory)foreach(var e in m.CurrentBattle.Enemies.ToArray())e.TakeDamage(100000,ElementType.None);else m.CurrentBattle.Player.TakeDamage(100000);
            Assert.IsTrue(m.IsBattleEndPresentationPending);
            Call(c,"OnApplicationPause",true);Call(c,"OnApplicationPause",false);
            Assert.IsFalse(c.IsUserPaused);Assert.IsFalse(((UnityEngine.GameObject)Get(c,"pauseOverlay")).activeSelf);
            Assert.IsTrue(m.CompleteBattleEndPresentation());Assert.IsTrue(c.IsUserPaused);
            Assert.IsFalse(m.CompleteBattleEndPresentation());Assert.IsNull(Get(c,"automaticPauseOwner"));
            KeyboardButton(c,"ResumeButton");Assert.IsFalse(c.IsUserPaused);
        }

        [TestCase(false)] [TestCase(true)]
        public void 休止復帰と明示再開は暗転とヒットストップの残時間を消さない(bool hitstop)
        {
            var c=Start();ToBattle(c);var b=Model(c).CurrentBattle;
            if(b.CustomScreen.IsOpen)b.CustomScreen.Cancel();
            // 既存の短い停止時間をfixture内にのみ指定する。製品カード値は追加しない。
            int resolved=0;
            if(hitstop)b.RunLogicalFrame(()=>b.Enemies.First().TakeDamage(1,ElementType.None,hitStopFrames:6));
            else Assert.IsTrue(b.TryStartDarkeningEffect(Olca.Core.Logic.Battle.DarkeningSide.Opponent,0,6,()=>resolved++));
            int remaining=b.HitStopRemainingFrames;long frame=b.LogicalFrame;
            Call(c,"OnApplicationPause",true);for(int n=0;n<20;n++)Tick(c);
            Call(c,"OnApplicationPause",false);for(int n=0;n<20;n++)Tick(c);
            Assert.IsTrue(c.IsUserPaused);Assert.AreEqual(frame,b.LogicalFrame);Assert.AreEqual(remaining,b.HitStopRemainingFrames);Assert.AreEqual(0,resolved);
            KeyboardButton(c,"ResumeButton");Assert.IsFalse(c.IsUserPaused);
            Assert.AreEqual(hitstop,b.IsHitStopped);Assert.AreEqual(!hitstop,b.IsDarkening);
            for(int n=0;n<6;n++)Tick(c);
            Assert.IsFalse(b.IsHitStopped);Assert.IsFalse(b.IsDarkening);Assert.AreEqual(hitstop?0:1,resolved);
        }

        [TestCase(false)] [TestCase(true)]
        public void 撤退確定は開閉途中の旧バトル表示を取消して帰還の背面操作を残さない(bool closing)
        {
            var c=Start();ToBattle(c);
            var root=new UnityEngine.GameObject("retreat-battle-view",typeof(UnityEngine.RectTransform));root.transform.SetParent(c.transform,false);
            var custom=new UnityEngine.GameObject("CustomScreenContainer",typeof(UnityEngine.RectTransform));custom.transform.SetParent(root.transform,false);
            var view=root.AddComponent<Olca.Presentation.Battle.BattleView>();
            var cts=new System.Threading.CancellationTokenSource();var token=cts.Token;
            Set(view,"customScreenContainer",(UnityEngine.RectTransform)custom.transform);
            Set(view,"animationCts",cts);Set(view,"isCustomScreenOpen",!closing);Set(view,"gaugeResetting",closing);
            Set(c,"battleView",view);
            c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");
            Assert.AreEqual(RunReturnOutcome.Retreated,c.PersistentProgress.Flow.Outcome);
            Assert.IsFalse(root.activeSelf);Assert.IsTrue(token.IsCancellationRequested);
            Assert.IsNull(Get(view,"animationCts"));Assert.IsFalse((bool)Get(view,"isCustomScreenOpen"));Assert.IsFalse((bool)Get(view,"gaugeResetting"));
            Assert.IsFalse((bool)Get(c,"battleFieldReady"));
            long revision=c.PersistentProgress.Revision;Call(c,"ConfirmRetreat");Assert.AreEqual(revision,c.PersistentProgress.Revision);
        }

        [Test] public void 会話は多重起動とゲーム入力を遮断し閉鎖通知は一回だけで保存しない(){var c=Start();var m=Model(c);var before=System.IO.Directory.GetFiles(directory,"*.json").ToDictionary(p=>p,p=>System.IO.File.ReadAllBytes(p));int ended=0;var script=new DialogueScript("external",new[]{new DialogueLine("検証")});Assert.IsTrue(c.TryStartDialogue(script,r=>ended++));Assert.IsFalse(c.TryStartDialogue(script));float x=m.Dungeon.PlayerPosition;for(int n=0;n<30;n++)Tick(c,new LogicalInput{Horizontal=1,DashButtonDown=true});Assert.AreEqual(x,m.Dungeon.PlayerPosition);Assert.IsTrue(Olca.Unity.GameplayPause.InputBlocked);c.SetUserPaused(true);c.CurrentDialogue.AdvanceFrame();Assert.AreEqual(0,c.CurrentDialogue.VisibleElements);c.SetUserPaused(false);Assert.IsTrue(Olca.Unity.GameplayPause.InputBlocked);c.CurrentDialogue.Decide();c.CurrentDialogue.Decide();Call(c,"UpdateDialogue");Call(c,"UpdateDialogue");Assert.IsNull(c.CurrentDialogue);Assert.AreEqual(1,ended);foreach(var pair in before)CollectionAssert.AreEqual(pair.Value,System.IO.File.ReadAllBytes(pair.Key));}
        [Test] public void 会話中のチェックポイント再開は会話とバックログを復元せず既存保存を保持する(){var c=Start();int ended=0;DialogueEndReason reason=DialogueEndReason.Completed;Assert.IsTrue(c.TryStartDialogue(new DialogueScript("external",new[]{new DialogueLine("復元しない文章")}),r=>{ended++;reason=r;}));var old=c.CurrentDialogue;old.Decide();c.SetUserPaused(true);Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.IsNull(c.CurrentDialogue);Assert.AreEqual(1,ended);Assert.AreEqual(DialogueEndReason.Interrupted,reason);Assert.IsEmpty(old.Backlog);}
        private static void ToBattle(Olca.Application.Dungeon.DungeonBattleController controller)
        {
            for (int i = 0; i < 120 && Model(controller).CurrentBattle == null; i++) Tick(controller);
            Assert.IsNotNull(Model(controller).CurrentBattle);
        }
        [Test]
        public void 出撃スタイルの保存コピーは独立し登録値変更で既存攻略を変更しない()
        {
            var c=StartStyles(true);KeyboardButton(c,"SelectDepartureStyle");KeyboardButton(c,"StyleChoice_1");KeyboardButton(c,"ConfirmStyleSelection");KeyboardButton(c,"ConfirmDungeonSelection");
            var plan=c.PersistentProgress.NextDeparture;var dto=plan.Start.Copy();dto.Player.Style.NormalPercent=1;
            Assert.AreEqual(150,c.PersistentProgress.NextDeparture.Start.Player.Style.NormalPercent);
            Set(c,"departureStyles",new[]{DepartureStyleDefinition.Basic,new DepartureStyleDefinition("fixture-fire","更新後fixture",null,new TransformationAttackDefinition(ElementType.Water,50,100,120))});
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
            Assert.AreEqual(ElementType.Fire,Model(c).Dungeon.Player.ActiveElement);Assert.AreEqual(150,Model(c).Dungeon.Player.DepartureStyle.Attack.NormalPowerPercent);
            Assert.AreEqual(plan.Start.RunId,c.PersistentProgress.NextDeparture.Start.RunId);
        }
        [Test]
        public void 出撃スタイルの火属性はマグマで被害を受けず変身属性は別に優先する()
        {
            var p=new PlayerCharacter(100);p.SetDepartureStyle(StyleFixtures()[1]);
            var field=new BattleField();var panels=new PanelEffectSystem(field,new MockLogger());field.ChangePanelType(p.GridPosition,PanelType.Magma);
            panels.ApplyOnEnterEffect(p,p.GridPosition);Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(PanelType.Magma,field.GetPanelType(p.GridPosition));
            Assert.AreEqual(ElementType.Fire,p.ActiveElement);
        }
        [Test]
        public void トークン交換所の通算制限は帰還しても保持し更新制限だけを戻す()
        {
            var c=StartSelection();ExchangeProfile(c,30);var session=(ClearReturnSession)Get(c,"clearReturnSession");
            var card=new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            var products=new[]{new TokenExchangeProduct("life",card,3,1,ExchangeLimit.Lifetime,1),new TokenExchangeProduct("round",card,3,1,ExchangeLimit.PerRefresh,1)};
            Assert.IsTrue(session.EnsureExchange(products,new TokenExchangeProduct[0],0,new MockRandomProvider()));
            string old=session.Snapshot.ExchangeStock.RoundId;
            Assert.IsTrue(session.PurchaseExchange(System.Guid.NewGuid().ToString("N"),old,"life"));Assert.IsTrue(session.PurchaseExchange(System.Guid.NewGuid().ToString("N"),old,"round"));
            var malformed=session.Snapshot;malformed.ExchangeTotals[0].Count++;Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(malformed));
            malformed=session.Snapshot;malformed.ExchangeStock.Offers[0].Purchased=0;Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(malformed));
            KeyboardButton(c,"ConfirmDungeonSelection");ToBattle(c);c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");
            Assert.IsNull(c.PersistentProgress.ExchangeStock);Assert.IsTrue(session.EnsureExchange(products,new TokenExchangeProduct[0],0,new MockRandomProvider()));
            var next=session.Snapshot;Assert.IsFalse(ClearReturnSession.CanExchange(next,next.ExchangeStock.Offers[0]));Assert.IsTrue(ClearReturnSession.CanExchange(next,next.ExchangeStock.Offers[1]));
            Assert.AreEqual(24,next.Tokens);Assert.AreEqual(2,next.Owned.Single().Count);
        }
        [Test]
        public void 標準旧保存で配置追加のない短いマップは同じhashの新旧カード候補を完全検証して選ぶ()
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> config=c=>{Set(c,"dungeonCellCount",5);Set(c,"firstEnemyCell",10);};
            var c=Start(production:true,configure:config);var map=(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonWithoutVisits",Hidden).Invoke(c,new object[]{false});
            var deck=(Deck)c.GetType().GetMethod("CreateDepartureDeck",Hidden).Invoke(c,new object[]{true});var old=DungeonCheckpointSession.CreateDefault(map,deck);
            new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:old);
            Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).PrepareNextDeparture(old.Store.Read(),old.Definitions.MapHash));
            c=Start(production:true,configure:config);Assert.IsFalse((bool)Get(c,"clearReturnVisible"));Assert.AreEqual(0,Model(c).Dungeon.Map.Visits.Count);
            Assert.AreEqual(old.Store.Read().RunId,((DungeonCheckpointSession)Get(c,"memoryCheckpoints")).Store.Read().RunId);
        }
        [Test]
        public void 通常新規開始だけ20枚と同一所持を一度保存し再起動と出撃へ接続する()
        {
            var c=StartStandard();var p=c.PersistentProgress;
            Assert.AreEqual(20,p.Owned.Sum(o=>o.Count));Assert.AreEqual(5,p.Owned.Length);
            Assert.IsTrue(p.Owned.All(o=>o.Count==4));Assert.AreEqual(1,p.SavedDecks.Length);
            Assert.AreEqual(20,p.SavedDecks[0].Cards.Length);Assert.AreEqual(p.SavedDecks[0].Id,p.EquippedDeckId);
            var store=new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
            UnityEngine.Object.DestroyImmediate(c.gameObject);c=StartStandard();
            CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));Assert.AreEqual(20,c.PersistentProgress.Owned.Sum(o=>o.Count));
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.AreEqual(20,c.PersistentProgress.NextDeparture.Start.DeckOrder.Length);
            Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void 空の旧進行ファイルは新規配布せず従来6枚出撃を維持する()
        {
            var store=new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress"));
            Assert.IsTrue(store.TryWrite(0,new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N")},out var error),error);
            var bytes=System.IO.File.ReadAllBytes(store.ActivePath);var c=StartStandard();
            Assert.AreEqual(0,c.PersistentProgress.Owned.Length);Assert.AreEqual(0,c.PersistentProgress.SavedDecks.Length);
            CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.AreEqual(6,c.PersistentProgress.NextDeparture.Start.DeckOrder.Length);
        }

        [Test]
        public void 進行なしでも旧チェックポイントがあれば配布せず6枚を再開する()
        {
            var c=Start(production:true,configure:host=>{
                var map=(DungeonConfig)host.GetType().GetMethod("CreateDefaultDungeon",Hidden).Invoke(host,new object[]{false});
                var deck=(Deck)host.GetType().GetMethod("CreateInitialDeck",Hidden).Invoke(host,null);
                var defs=DungeonCheckpointSession.CreateDefault(map,deck).Definitions;
                var store=new Olca.Unity.FileCheckpointStore(defs,directory);
                new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),
                    checkpoints:new DungeonCheckpointSession(defs,store));
            });
            Assert.AreEqual(0,c.PersistentProgress.Owned.Length);Assert.IsTrue((bool)Get(c,"awaitingCheckpointChoice"));
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreEqual(6,Model(c).Dungeon.GetCurrentDeck().TotalCount);
        }

        private sealed class StarterFaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Fail=true;
            public override void MoveNew(string temporary,string destination){if(Fail)throw new System.IO.IOException("starter fixture");base.MoveNew(temporary,destination);}
        }
        [Test]
        public void 新規配布保存失敗は出撃を止め再試行成功後に20枚だけを装備する()
        {
            var io=new StarterFaultIo();var c=Start(production:true,configure:host=>Set(host,"clearReturnStore",
                new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress"),io)));
            Assert.AreEqual(0,c.PersistentProgress.Owned.Length);Assert.IsNull(c.PersistentProgress.NextDeparture);
            Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).HasPendingSave);
            Assert.IsFalse(SelectionButton(c,"ConfirmDungeonSelection").interactable);
            io.Fail=false;Call(c,"RetryClearReturn");Assert.IsTrue(SelectionButton(c,"ConfirmDungeonSelection").interactable);
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.AreEqual(20,c.PersistentProgress.NextDeparture.Start.DeckOrder.Length);
            Assert.AreEqual(20,c.PersistentProgress.Owned.Sum(o=>o.Count));
        }

        [TestCase(false)][TestCase(true)]
        public void 図鑑の融合成功は通常Controllerから保存し失敗時だけ履歴再試行を表示する(bool fail)
        {
            float oldScale=UnityEngine.Time.timeScale;
            try {
            var c=Start();ToBattle(c);var cs=Model(c).CurrentBattle.CustomScreen;
            if(!cs.IsOpen){Model(c).CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);Model(c).CurrentBattle.OpenCustomScreen(new MockRandomProvider());}
            var sword=cs.CurrentHand.First(x=>x.Id=="sword");var bomb=cs.CurrentHand.First(x=>x.Id=="bomb");
            var io=new StarterFaultIo{Fail=fail};Set(Get(c,"clearReturnStore"),"io",io);
            Assert.IsTrue(cs.ExecuteFusion(new[]{sword,bomb}).IsSuccess);
            Assert.AreEqual(fail,(bool)Get(c,"cardCollectionOpen"));
            if(fail){Assert.IsTrue(c.IsUserPaused);Assert.IsFalse(SelectionButton(c,"CloseCardCollection").interactable);io.Fail=false;KeyboardButton(c,"RetryCardCollection");KeyboardButton(c,"CloseCardCollection");}
            Assert.AreEqual(1,c.PersistentProgress.RevealedRecipes.Length);Assert.IsEmpty(c.PersistentProgress.Owned);
            Assert.IsEmpty(c.PersistentProgress.DiscoveredCards);Assert.IsTrue(cs.TryUndoCustomization());Assert.AreEqual(1,c.PersistentProgress.RevealedRecipes.Length);
            } finally {
                UnityEngine.Time.timeScale=oldScale;Olca.Unity.GameplayPause.SetPaused(false);
                Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [Test]
        public void 旧帰還済み進行だけを再開しても仮の探索を保存せず帰還編成画面を表示する()
        {
            var parent=directory;var c=Start(configure:x=>x.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5,30,4)));ToBattle(c);Model(c).SelectCardReward(Win(c)[0]);KeyboardButton(c,"ConfirmCarryHome");KeyboardButton(c,"ConfirmReturn");
            var source=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var bytes=System.IO.File.ReadAllBytes(source.ActivePath);var snapshot=c.PersistentProgress;
            try {directory=System.IO.Path.Combine(parent,"return-only");System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory,"progress"));System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,"progress","progress.json"),bytes);
                c=Start(configure:x=>x.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5,30,4)));
                Assert.IsTrue((bool)Get(c,"clearReturnVisible"));Assert.IsTrue(SelectionButton(c,"SavedDeckMenu").interactable);Assert.IsNull(c.PersistentProgress.NextDeparture);
                CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(((Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore")).ActivePath));Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(directory,"active.json")));
                Assert.AreEqual(snapshot.Owned.Sum(o=>o.Count),c.PersistentProgress.Owned.Sum(o=>o.Count));
            } finally {directory=parent;}
        }
        private Olca.Application.Dungeon.DungeonBattleController StartStandard() => Start(production:true,configure:c=>{Set(c,"dungeonCellCount",100);Set(c,"firstEnemyCell",50);Set(c,"enemySpacing",30);});
        [Test]
        public void 標準製品マップは初戦後の空セルだけにショップと既存回復を配置し初期資源を増やさない()
        {
            var c=StartStandard();KeyboardButton(c,"ConfirmDungeonSelection");var d=Model(c).Dungeon;
            CollectionAssert.AreEqual(new[]{52,54},d.Map.Visits.Select(v=>v.Cell));Assert.IsTrue(d.Map.Visits[0].IsShop);Assert.IsFalse(d.Map.Visits[1].IsShop);
            Assert.AreEqual(3,d.Map.Visits[0].Slots);Assert.AreEqual(1,d.Map.Visits[0].Options.Count(o=>o.RemoveCount==1));Assert.IsTrue(d.Map.Visits[0].Options.All(o=>o.Price==10));Assert.AreEqual(20,d.Map.Visits[1].Options.Single().Heal);
            CollectionAssert.AreEqual(new[]{50},d.Map.StrongEnemyCells);
            Assert.AreEqual(100,d.Map.ItemRewardChance);Assert.AreEqual(1,d.Map.ItemRewards.Count);
            Assert.AreEqual(1,d.Map.ItemRewards[0].Weight);Assert.AreEqual(20,d.Map.ItemRewards[0].Item.Heal);
            Assert.AreEqual(20,d.Map.GetCell(51).Item.Heal);Assert.AreEqual(50,d.Map.GetCell(50).Enemy.MaxHp);Assert.AreEqual(100,d.Player.CurrentHp);Assert.AreEqual(0,d.Currency);Assert.AreEqual(0,c.PersistentProgress.Tokens);
            var start=c.PersistentProgress.NextDeparture.Start;Assert.AreEqual(2,start.Visits.Length);Assert.IsTrue(start.Visits.All(v=>!v.Closed));
        }
        [Test]
        public void 製品の氷は既存回復後の空セル二つだけで旧配置と初期資源を変えず出撃保存する()
        {
            var c=StartStandard();var old=(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonWithoutTerrain",Hidden).Invoke(c,new object[]{false});KeyboardButton(c,"ConfirmDungeonSelection");var d=Model(c).Dungeon;
            CollectionAssert.AreEqual(new[]{56,57},d.Map.ExplorationTerrain.Select(t=>t.Cell));Assert.IsTrue(d.Map.ExplorationTerrain.All(t=>t.Type==PanelType.Ice&&t.Heal==0&&t.Damage==0));Assert.IsTrue(d.Map.ExplorationTerrain.All(t=>d.Map.GetCell(t.Cell).Type==CellType.Empty&&!d.Map.Visits.Any(v=>v.Cell==t.Cell)));
            CollectionAssert.AreEqual(old.StrongEnemyCells,d.Map.StrongEnemyCells);CollectionAssert.AreEqual(old.Visits.Select(v=>v.Cell),d.Map.Visits.Select(v=>v.Cell));Assert.AreEqual(old.MapCells[50].Enemy.MaxHp,d.Map.GetCell(50).Enemy.MaxHp);Assert.AreEqual(old.MapCells[51].Item.Heal,d.Map.GetCell(51).Item.Heal);Assert.AreEqual(100,d.Player.CurrentHp);Assert.AreEqual(0,d.Currency);Assert.AreEqual(0,c.PersistentProgress.Tokens);Assert.AreEqual(5,c.PersistentProgress.NextDeparture.Start.SchemaVersion);
        }
        [TestCase(false,false,0)][TestCase(false,true,0)][TestCase(true,false,0)][TestCase(true,true,0)]
        [TestCase(false,false,1)][TestCase(false,true,1)][TestCase(false,false,2)][TestCase(false,true,2)]
        [TestCase(false,false,3)][TestCase(false,true,3)]
        [TestCase(false,false,4)][TestCase(false,true,4)]
        public void 標準旧保存と旧出撃計画は旧マップで再開し帰還後の次回だけ新配置を使う(bool legacy,bool existingFile,int oldVisits)
        {
            // 旧進行の試験で新規スターター配布を混ぜない。
            var legacyProgress=new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress"));
            Assert.IsTrue(legacyProgress.TryWrite(0,new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N")},out var legacyError),legacyError);
            var c=StartStandard();var map=oldVisits==4?(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonWithoutTerrain",Hidden).Invoke(c,new object[]{false}):oldVisits==3?(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonWithoutItemRewards",Hidden).Invoke(c,new object[]{false}):oldVisits>0?(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonVisits",Hidden).Invoke(c,new object[]{false,oldVisits==1}):(DungeonConfig)c.GetType().GetMethod("CreateDefaultDungeonWithoutVisits",Hidden).Invoke(c,new object[]{legacy});
            var deck=(Deck)c.GetType().GetMethod("CreateDepartureDeck",Hidden).Invoke(c,new object[]{legacy});var memory=DungeonCheckpointSession.CreateDefault(map,deck);
            new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:memory);var old=memory.Store.Read();
            var progress=(ClearReturnSession)Get(c,"clearReturnSession");Assert.IsTrue(progress.PrepareNextDeparture(old,memory.Definitions.MapHash),progress.Error);
            var folder=System.IO.Path.Combine(directory,"runs",old.RunId);var file=new Olca.Unity.FileCheckpointStore(memory.Definitions,folder);
            if(existingFile)Assert.IsTrue(file.TryWrite(old,out var error),error);
            c=StartStandard();if((bool)Get(c,"awaitingCheckpointChoice"))Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
            Assert.AreEqual(old.RunId,c.PersistentProgress.NextDeparture.Start.RunId);Assert.AreEqual(oldVisits>0?2:0,Model(c).Dungeon.Map.Visits.Count);if(oldVisits>0)Assert.AreEqual(oldVisits==1?2:3,Model(c).Dungeon.Map.Visits[0].Slots);if(oldVisits>=3)Assert.AreEqual(20,Model(c).Dungeon.Map.GetCell(51).Item.Heal);else Assert.IsNull(Model(c).Dungeon.Map.GetCell(51).Item);
            Assert.AreEqual(oldVisits==4?1:0,Model(c).Dungeon.Map.StrongEnemyCells.Count);Assert.AreEqual(oldVisits==4?1:0,Model(c).Dungeon.Map.ItemRewards.Count);Assert.AreEqual(0,Model(c).Dungeon.Map.ExplorationTerrain.Count);
            var bytes=System.IO.File.ReadAllBytes(file.ActivePath);c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");KeyboardButton(c,"NextDeparture");
            CollectionAssert.AreEqual(new[]{52,54},Model(c).Dungeon.Map.Visits.Select(v=>v.Cell));Assert.AreNotEqual(old.RunId,c.PersistentProgress.NextDeparture.Start.RunId);CollectionAssert.AreEqual(new[]{56,57},Model(c).Dungeon.Map.ExplorationTerrain.Select(t=>t.Cell));
            CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));Assert.AreEqual(0,c.PersistentProgress.Tokens);
        }
        private void ExchangeProfile(Olca.Application.Dungeon.DungeonBattleController c,long tokens,OwnedCardCount[] owned=null)
        {
            var p=c.PersistentProgress;long revision=p.Revision;p.Tokens=tokens;if(owned!=null)p.Owned=owned;
            p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(((Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore")).TryWrite(revision,p,out var error),error);
            Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).Reload());
        }
        [Test]
        public void トークン交換所の通常商品表示取消資源不足は通貨所持や保存済み在庫を変更しない()
        {
            var c=StartSelection();KeyboardButton(c,"OpenTokenExchange");var p=c.PersistentProgress;
            Assert.AreEqual(3,p.ExchangeStock.Offers.Length);Assert.AreEqual(2,p.ExchangeStock.Offers.Count(o=>o.Random));
            Assert.AreEqual(3,p.ExchangeStock.Offers.Select(o=>o.Id).Distinct().Count());
            Assert.IsTrue(p.ExchangeStock.Offers.All(o=>o.Price==o.OverflowTokens*3));
            KeyboardButton(c,"ExchangeOffer_card-sword");Assert.IsFalse(SelectionButton(c,"ConfirmTokenExchange").interactable);
            Call(c,"StartNextDeparture");Assert.IsNull(c.PersistentProgress.NextDeparture);
            KeyboardButton(c,"CancelTokenExchange");KeyboardButton(c,"CloseTokenExchange");KeyboardButton(c,"OpenTokenExchange");
            Assert.AreEqual(p.Revision,c.PersistentProgress.Revision);Assert.AreEqual(0,c.PersistentProgress.Tokens);Assert.IsEmpty(c.PersistentProgress.Owned);
            CollectionAssert.AreEqual(p.ExchangeStock.Offers.Select(o=>o.Id),c.PersistentProgress.ExchangeStock.Offers.Select(o=>o.Id));
        }
        [TestCase(null)] [TestCase("before")] [TestCase("after")]
        public void トークン交換所は支払所持領収書を同時保存し失敗再試行再起動と重複要求で二重交換しない(string fault)
        {
            var c=StartSelection();ExchangeProfile(c,10);KeyboardButton(c,"OpenTokenExchange");var before=c.PersistentProgress;
            var io=new KeyboardFaultIo{Fault=fault};Set(Get(c,"clearReturnStore"),"io",io);
            KeyboardButton(c,"ExchangeOffer_card-sword");string request=(string)Get(c,"exchangeRequestId");string round=before.ExchangeStock.RoundId;
            KeyboardButton(c,"ConfirmTokenExchange");
            if(fault=="before"){
                Assert.AreEqual(10,c.PersistentProgress.Tokens);Assert.IsEmpty(c.PersistentProgress.Owned);
                Assert.IsFalse(SelectionButton(c,"CloseTokenExchange").interactable);
                var crashed=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory));Assert.AreEqual(10,crashed.Snapshot.Tokens);
                io.Fault=null;KeyboardButton(c,"RetryTokenExchange");
            }
            var after=c.PersistentProgress;Assert.AreEqual(7,after.Tokens);Assert.AreEqual(1,after.Owned.Single().Count);Assert.AreEqual(1,after.ExchangeReceipts.Length);
            var session=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory));
            Assert.IsTrue(session.PurchaseExchange(request,round,"card-sword"),session.Error);Assert.AreEqual(after.Revision,session.Snapshot.Revision);
            Assert.IsFalse(session.PurchaseExchange(request,round,"card-bomb"));Assert.AreEqual(7,session.Snapshot.Tokens);
            Assert.AreEqual(1,session.Snapshot.Owned.Single().Count);Assert.AreEqual(1,session.Snapshot.ExchangeTotals.Single().Count);
            KeyboardButton(c,"ConfirmTokenExchange");Assert.AreEqual(after.Revision,c.PersistentProgress.Revision);
            var copy=after.Copy();copy.ExchangeStock.Offers[0].Price=99;Assert.AreEqual(3,c.PersistentProgress.ExchangeStock.Offers[0].Price);
        }
        [Test]
        public void トークン交換所の在庫保存失敗も同じ抽選を保持し再開時に再抽選しない()
        {
            var c=StartSelection();var io=new KeyboardFaultIo{Fault="before"};Set(Get(c,"clearReturnStore"),"io",io);
            KeyboardButton(c,"OpenTokenExchange");Assert.IsNull(c.PersistentProgress.ExchangeStock);Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).HasPendingSave);
            io.Fault=null;KeyboardButton(c,"RetryTokenExchange");var p=c.PersistentProgress;Assert.AreEqual(3,p.ExchangeStock.Offers.Length);
            var session=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory));
            Assert.IsTrue(session.EnsureExchange(null,null,-1,null),"既存ラウンドを再抽選/現在定義で置換しない");
            Assert.AreEqual(p.Revision,session.Snapshot.Revision);CollectionAssert.AreEqual(p.ExchangeStock.Offers.Select(o=>o.Id),session.Snapshot.ExchangeStock.Offers.Select(o=>o.Id));
        }
        [Test]
        public void トークン交換所の所持上限は超過変換して必ず純減し任意売却は追加しない()
        {
            var c=StartSelection();ExchangeProfile(c,10,new[]{new OwnedCardCount{Id="sword",Name="ソード",Rarity=CardRarity.Normal,Level=ExceedLevel.Normal,Count=4}});
            KeyboardButton(c,"OpenTokenExchange");KeyboardButton(c,"ExchangeOffer_card-sword");KeyboardButton(c,"ConfirmTokenExchange");
            Assert.AreEqual(8,c.PersistentProgress.Tokens);Assert.AreEqual(4,c.PersistentProgress.Owned.Single().Count);
            Assert.AreEqual(3,c.PersistentProgress.ExchangeReceipts.Single().Paid);Assert.AreEqual(1,c.PersistentProgress.ExchangeReceipts.Single().OverflowTokens);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void トークン交換所のランダム在庫は全帰還で更新し中断再開や失敗保存では更新しない(int outcome)
        {
            var c=StartSelection();ExchangeProfile(c,30);KeyboardButton(c,"OpenTokenExchange");var old=c.PersistentProgress.ExchangeStock;
            var random=old.Offers.First(o=>o.Random);KeyboardButton(c,"ExchangeOffer_"+random.Id);KeyboardButton(c,"ConfirmTokenExchange");
            Assert.IsFalse(SelectionButton(c,"ExchangeOffer_"+random.Id).interactable);
            KeyboardButton(c,"CloseTokenExchange");KeyboardButton(c,"ConfirmDungeonSelection");ToBattle(c);
            if(outcome==0){Win(c);Model(c).SelectCardReward(null);KeyboardButton(c,"ConfirmCarryHome");
                var io=new KeyboardFaultIo{Fault="before"};Set(Get(c,"clearReturnStore"),"io",io);KeyboardButton(c,"ConfirmReturn");
                Assert.IsNotNull(c.PersistentProgress.ExchangeStock);io.Fault=null;KeyboardButton(c,"RetryClearReturn");}
            else if(outcome==1){c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");}
            else Model(c).CurrentBattle.Player.TakeDamage(100000,affectsPoise:false);
            Assert.AreEqual(ClearReturnStage.Returned,c.PersistentProgress.Flow.Stage);Assert.IsNull(c.PersistentProgress.ExchangeStock);
            KeyboardButton(c,"OpenTokenExchange");var next=c.PersistentProgress;Assert.AreEqual(next.Flow.RunId,next.ExchangeStock.RoundId);Assert.AreNotEqual(old.RoundId,next.ExchangeStock.RoundId);
            Assert.IsTrue(next.ExchangeStock.Offers.All(o=>o.Purchased==0));Assert.AreEqual(1,next.ExchangeTotals.Single().Count);
            Assert.AreEqual(27,next.Tokens); // ダンジョン通貨とは別の永続資源
        }
        [TestCase(ExchangeLimit.Lifetime)] [TestCase(ExchangeLimit.PerRefresh)]
        public void トークン交換所の商品別制限と不正価格空プール旧省略を検証する(ExchangeLimit limit)
        {
            var c=StartSelection();ExchangeProfile(c,10);var session=(ClearReturnSession)Get(c,"clearReturnSession");
            var card=new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            Assert.Throws<System.ArgumentException>(()=>new TokenExchangeProduct("bad",card,1,1));
            Assert.Throws<System.ArgumentException>(()=>new TokenExchangeProduct("bad",card,3,1,limit,0));
            var product=new TokenExchangeProduct("limited",card,3,1,limit,1);
            Assert.IsFalse(session.EnsureExchange(new[]{product},new[]{product},1,new MockRandomProvider()));
            Assert.IsTrue(session.EnsureExchange(new[]{product},new TokenExchangeProduct[0],0,new MockRandomProvider()),session.Error);
            string round=session.Snapshot.ExchangeStock.RoundId;Assert.IsTrue(session.PurchaseExchange(System.Guid.NewGuid().ToString("N"),round,"limited"));
            Assert.IsFalse(session.PurchaseExchange(System.Guid.NewGuid().ToString("N"),round,"limited"));Assert.AreEqual(7,session.Snapshot.Tokens);
            var old=new ClearReturnProfile();ClearReturnProfile.Validate(old);Assert.IsNull(old.ExchangeStock);Assert.IsNull(old.ExchangeReceipts);
            var invalid=session.Snapshot;invalid.ExchangeStock.Offers[0].Price=0;Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(invalid));
        }
        private static DepartureStyleDefinition[] StyleFixtures() => new[] {
            DepartureStyleDefinition.Basic,
            new DepartureStyleDefinition("fixture-fire", "既存攻撃の外部設定fixture", "selection-a", new TransformationAttackDefinition(ElementType.Fire, 150, 250, 30, 12)),
            new DepartureStyleDefinition("fixture-water", "未解放fixture", "selection-b", new TransformationAttackDefinition(ElementType.Water)) };
        private Olca.Application.Dungeon.DungeonBattleController StartStyles(bool unlocked = false)
        {
            var c = StartSelection();
            // 試験用登録だけを差し込む。製品のスタイル追加ではない。
            Set(c, "departureStyles", StyleFixtures());
            if (unlocked) {
                var p = c.PersistentProgress; long revision = p.Revision;
                p.ClearedDungeons = new[] { "selection-a" }; p.Revision++; p.CommitId = System.Guid.NewGuid().ToString("N");
                Assert.IsTrue(((Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore")).TryWrite(revision,p,out var error),error);
                Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).Reload());
            }
            return c;
        }
        [TestCase(false)][TestCase(true)]
        public void 出撃先からスタイルと交換へ進んで取消しても選択先と保存を保持する(bool returned)
        {
            var c=StartStyles(true);
            if(returned){KeyboardButton(c,"ConfirmDungeonSelection");ToBattle(c);c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");Assert.IsFalse((bool)Get(c,"initialHub"));}
            KeyboardButton(c,"OpenTokenExchange");KeyboardButton(c,"CloseTokenExchange");if(returned)KeyboardButton(c,"SelectDungeon");KeyboardButton(c,"DungeonChoice_1");
            var store=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var before=System.IO.File.ReadAllBytes(store.ActivePath);string style=c.PersistentProgress.SelectedStyleId;
            for(int n=0;n<2;n++){
                KeyboardButton(c,"SelectDepartureStyle");Assert.IsTrue((bool)Get(c,"styleSelectionOpen"));KeyboardButton(c,"StyleChoice_1");KeyboardButton(c,"CancelStyleSelection");Assert.IsFalse((bool)Get(c,"styleSelectionOpen"));Assert.AreEqual(1,Get(c,"selectedDungeon"));Assert.IsTrue(SelectionButton(c,"DungeonChoice_1").gameObject.activeInHierarchy);Assert.AreEqual(style,c.PersistentProgress.SelectedStyleId);
                KeyboardButton(c,"OpenTokenExchange");Assert.IsTrue((bool)Get(c,"exchangeOpen"));KeyboardButton(c,"ExchangeOffer_card-sword");KeyboardButton(c,"CancelTokenExchange");KeyboardButton(c,"CloseTokenExchange");Assert.IsFalse((bool)Get(c,"exchangeOpen"));Assert.AreEqual(1,Get(c,"selectedDungeon"));Assert.IsTrue(SelectionButton(c,"DungeonChoice_1").gameObject.activeInHierarchy);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(store.ActivePath));
            }
        }
        [Test]
        public void 出撃スタイルの初期値未解放表示と取消は出撃や保存を行わない()
        {
            var c=StartStyles(); long revision=c.PersistentProgress.Revision;
            KeyboardButton(c,"SelectDepartureStyle"); Assert.IsTrue(SelectionButton(c,"StyleChoice_0").interactable);
            Assert.IsFalse(SelectionButton(c,"StyleChoice_1").interactable); Assert.IsFalse(SelectionButton(c,"StyleChoice_2").interactable);
            KeyboardButton(c,"StyleChoice_1"); Assert.AreEqual(0,Get(c,"styleDraft"));
            KeyboardButton(c,"CancelStyleSelection"); Assert.AreEqual(revision,c.PersistentProgress.Revision); Assert.IsNull(c.PersistentProgress.NextDeparture);
            KeyboardButton(c,"SelectDepartureStyle"); KeyboardButton(c,"ConfirmStyleSelection");
            Assert.AreEqual(revision,c.PersistentProgress.Revision); Assert.IsNull(c.PersistentProgress.SelectedStyleId);
            Assert.IsTrue(SelectionButton(c,"DungeonChoice_0").interactable);
        }
        [TestCase(null)] [TestCase("before")] [TestCase("after")]
        public void 出撃スタイルは解放履歴から選択保存し失敗時に旧値を保ち同一候補を再試行する(string fault)
        {
            var c=StartStyles(true); var io=new KeyboardFaultIo{Fault=fault};
            Set(Get(c,"clearReturnStore"),"io",io); long revision=c.PersistentProgress.Revision;
            KeyboardButton(c,"SelectDepartureStyle"); Assert.IsTrue(SelectionButton(c,"StyleChoice_1").interactable);
            Assert.IsFalse(SelectionButton(c,"StyleChoice_2").interactable);
            KeyboardButton(c,"StyleChoice_1"); KeyboardButton(c,"CancelStyleSelection"); Assert.IsNull(c.PersistentProgress.SelectedStyleId);
            KeyboardButton(c,"SelectDepartureStyle"); KeyboardButton(c,"StyleChoice_1"); KeyboardButton(c,"ConfirmStyleSelection");
            if(fault=="before") {
                Assert.IsNull(c.PersistentProgress.SelectedStyleId); Assert.AreEqual(revision,c.PersistentProgress.Revision);
                Assert.IsFalse(SelectionButton(c,"StyleChoice_0").interactable);
                Call(c,"StartNextDeparture"); Assert.IsNull(c.PersistentProgress.NextDeparture);
                var old=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory)); Assert.IsNull(old.Snapshot.SelectedStyleId);
                io.Fault=null; KeyboardButton(c,"RetryStyleDeparture");
            }
            Assert.AreEqual("fixture-fire",c.PersistentProgress.SelectedStyleId); Assert.AreEqual(revision+1,c.PersistentProgress.Revision);
            var reopened=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(c.ProgressDirectory));
            Assert.AreEqual("fixture-fire",reopened.Snapshot.SelectedStyleId); Assert.IsEmpty(reopened.Snapshot.Receipts);
            KeyboardButton(c,"DungeonChoice_1"); KeyboardButton(c,"ConfirmDungeonSelection");
            Assert.AreEqual("selection-b",((FloorSequenceDefinitions)Get(c,"floorDefinitions")).RouteId);
            var style=Model(c).Dungeon.Player.DepartureStyle; Assert.AreEqual("fixture-fire",style.Id);
            Assert.AreEqual(ElementType.Fire,Model(c).Dungeon.Player.ActiveElement);
            Assert.Throws<System.InvalidOperationException>(()=>Model(c).Dungeon.Player.SetDepartureStyle(DepartureStyleDefinition.Basic));
            string run=c.PersistentProgress.NextDeparture.Start.RunId;
            Assert.AreEqual("fixture-fire",c.PersistentProgress.NextDeparture.Start.Player.Style.Id);
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error); Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);
            Assert.AreEqual(150,Model(c).Dungeon.Player.DepartureStyle.Attack.NormalPowerPercent);
            Call(c,"OpenStyleSelection"); Assert.IsFalse((bool)Get(c,"styleSelectionOpen"));
        }
        [Test]
        public void 出撃スタイルの旧保存省略は基本へ復元し不正な攻撃定義を拒否する()
        {
            var c=StartSelection();KeyboardButton(c,"ConfirmDungeonSelection"); var dto=c.PersistentProgress.NextDeparture.Start;
            Assert.IsNull(dto.Player.Style); Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
            Assert.AreEqual(DepartureStyleDefinition.BasicId,Model(c).Dungeon.Player.DepartureStyle.Id);
            var style=StyleFixtures()[1].Capture();style.ChargeFrames=0;dto.Player.Style=style;
            Assert.IsFalse(((FloorSequenceDefinitions)Get(c,"floorDefinitions")).TryRestore(new FloorSequenceCheckpointDto{
                RouteId="selection-a",Revision="v1",RouteHash=((FloorSequenceDefinitions)Get(c,"floorDefinitions")).RouteHash,FloorIndex=0,State=dto},out _,out error));
            style=StyleFixtures()[1].Capture();style.Id="basic";Assert.Throws<System.ArgumentException>(()=>style.Restore());
            Assert.Throws<System.InvalidOperationException>(()=>c.ConfigureDepartureStyles(StyleFixtures()));
        }
        [Test]
        public void 出撃スタイルは通常固有攻撃とタメへ反映し変身終了時に戻る()
        {
            var c=StartStyles(true); KeyboardButton(c,"SelectDepartureStyle");KeyboardButton(c,"StyleChoice_1");KeyboardButton(c,"ConfirmStyleSelection");KeyboardButton(c,"ConfirmDungeonSelection");ToBattle(c);
            var battle=Model(c).CurrentBattle; var enemy=battle.Enemies[0];enemy.ApplyStun(100000);
            battle.Player.MoveTo(new Vector2Int(enemy.GridPosition.X-1,enemy.GridPosition.Y));
            battle.Resume(BattlePauseReason.CustomScreen);
            var input=new Olca.Application.Battle.BasicAttackInputState();
            input.Tick(battle,new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true},60,12);
            input.Tick(battle,new LogicalInput{BasicAttackButtonUp=true},60,12);
            Assert.AreEqual(185,enemy.CurrentHp); // レベル共通攻撃10の150%
            for(int i=0;i<40;i++)battle.TickFrame(17); input.Reset();
            for(int i=0;i<30;i++)input.Tick(battle,new LogicalInput{BasicAttackButtonDown=i==0,BasicAttackButton=true},60,12);
            input.Tick(battle,new LogicalInput{BasicAttackButtonUp=true},60,12);
            Assert.AreEqual(160,enemy.CurrentHp); // スタイル固有の30フレーム/250%
            var form=new TransformationDefenseDefinition("fixture-form",ElementResistances.Neutral,ElementType.Earth,attack:new TransformationAttackDefinition(ElementType.Water,80));
            battle.Transformations.Configure(new[]{form});if(!battle.CustomScreen.IsOpen)battle.CustomScreen.Open(System.Array.Empty<Card>(),new MockRandomProvider());Assert.IsTrue(battle.Transformations.TryPreview("fixture-form"));
            battle.CustomScreen.Confirm();Assert.AreEqual(ElementType.Water,battle.ActivePlayerElement);
            battle.Player.EndTransformationDefense();Assert.AreEqual(ElementType.Fire,battle.ActivePlayerElement);Assert.AreEqual("fixture-fire",battle.Player.DepartureStyle.Id);
        }
        private Olca.Application.Dungeon.DungeonBattleController StartSelection(bool debug = false)
        {
            definitions = null;
            return Start(configure: c => {
                var deck = (Deck)c.GetType().GetMethod("CreateInitialDeck",Hidden).Invoke(c,null);
                var maps = new[] { DungeonConfig.CreateTestDungeon(5,30,4) };
                c.ConfigureDungeonSelection(new[] {
                    new FloorSequenceDefinitions("selection-a","v1",maps,deck),
                    new FloorSequenceDefinitions("selection-b","v1",maps,deck),
                    new FloorSequenceDefinitions("selection-c","v1",maps,deck) }, debug);
            });
        }
        private static UnityEngine.UI.Button SelectionButton(Olca.Application.Dungeon.DungeonBattleController c,string name)
            => ((UnityEngine.GameObject)Get(c,"pauseCanvas")).GetComponentsInChildren<UnityEngine.UI.Button>(false).Single(b=>b.name==name);
        [Test]
        public void 出撃前は未解放名を隠して解放条件と既存マップ情報を示し保存しない()
        {
            var c=StartSelection();long revision=c.PersistentProgress.Revision;
            var first=SelectionButton(c,"DungeonChoice_0").GetComponentInChildren<UnityEngine.UI.Text>().text;
            var locked=SelectionButton(c,"DungeonChoice_1").GetComponentInChildren<UnityEngine.UI.Text>().text;
            StringAssert.Contains("ミクリア",first);StringAssert.Contains("？？？",locked);StringAssert.DoesNotContain("selection-b",locked);StringAssert.Contains("ヒトつマエのダンジョンをクリア",locked);
            var help=((UnityEngine.GameObject)Get(c,"pauseCanvas")).GetComponentsInChildren<UnityEngine.UI.Text>(false).Single(t=>t.name=="DungeonSelectionHelp").text;
            StringAssert.Contains("テキゾクセイ: ムゾクセイ",help);StringAssert.Contains("チケイ: ノーマル",help);
            KeyboardButton(c,"DungeonChoice_1");Assert.AreEqual(0,Get(c,"selectedDungeon"));Assert.AreEqual(revision,c.PersistentProgress.Revision);Assert.IsNull(c.PersistentProgress.NextDeparture);
        }
        [Test]
        public void クリア表示は永続履歴だけを読みデバッグ全開放や再読込で履歴を捏造しない()
        {
            var c=StartSelection(true);
            Assert.IsTrue(SelectionButton(c,"DungeonChoice_2").interactable);
            StringAssert.Contains("ミクリア",SelectionButton(c,"DungeonChoice_2").GetComponentInChildren<UnityEngine.UI.Text>().text);Assert.IsEmpty(c.PersistentProgress.ClearedDungeons);
            var profile=c.PersistentProgress;long revision=profile.Revision;profile.ClearedDungeons=new[]{"selection-a"};profile.Revision++;profile.CommitId=System.Guid.NewGuid().ToString("N");
            Assert.IsTrue(((Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore")).TryWrite(revision,profile,out var error),error);
            c=StartSelection();
            StringAssert.Contains("クリアズみ",SelectionButton(c,"DungeonChoice_0").GetComponentInChildren<UnityEngine.UI.Text>().text);
            StringAssert.Contains("ミクリア",SelectionButton(c,"DungeonChoice_1").GetComponentInChildren<UnityEngine.UI.Text>().text);
            StringAssert.Contains("？？？",SelectionButton(c,"DungeonChoice_2").GetComponentInChildren<UnityEngine.UI.Text>().text);
            CollectionAssert.AreEqual(new[]{"selection-a"},c.PersistentProgress.ClearedDungeons);
        }
        [Test]
        public void ダンジョンの初回選択取消と未解放拒否は保存せず同じ選択先で出撃再開する()
        {
            var c=StartSelection();Assert.IsTrue((bool)Get(c,"initialHub"));Assert.IsTrue(SelectionButton(c,"DungeonChoice_0").interactable);
            Assert.IsFalse(SelectionButton(c,"DungeonChoice_1").interactable);Assert.IsFalse(SelectionButton(c,"DungeonChoice_2").interactable);
            Assert.IsNull(c.PersistentProgress.NextDeparture);Assert.IsEmpty(c.PersistentProgress.Receipts);Assert.IsFalse(Store(c).HasExistingData);
            KeyboardButton(c,"DungeonChoice_1");Assert.AreEqual(0,Get(c,"selectedDungeon"));
            KeyboardButton(c,"CancelDungeonSelection");Assert.IsNull(c.PersistentProgress.NextDeparture);
            KeyboardButton(c,"ConfirmDungeonSelection");var start=c.PersistentProgress.NextDeparture.Start.Copy();Assert.IsFalse((bool)Get(c,"initialHub"));Assert.AreEqual("selection-a",Store(c).Read().RouteId);
            Assert.AreEqual(NextDepartureData.InitialParent,c.PersistentProgress.NextDeparture.ParentRunId);Assert.IsEmpty(c.PersistentProgress.Receipts);
            c=StartSelection();Assert.AreEqual(start.RunId,c.PersistentProgress.NextDeparture.Start.RunId);Assert.AreEqual("selection-a",Store(c).Read().RouteId);
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreEqual(start.RunId,Store(c).Read().State.RunId);
        }
        [Test]
        public void 保存済みクリアだけで次を解放し取消と再選択と再読込で既存成長を保つ()
        {
            var c=StartSelection();KeyboardButton(c,"ConfirmDungeonSelection");ToBattle(c);Model(c).SelectCardReward(Win(c)[0]);
            KeyboardButton(c,"ConfirmCarryHome");KeyboardButton(c,"ConfirmReturn");
            CollectionAssert.AreEqual(new[]{"selection-a"},c.PersistentProgress.ClearedDungeons);var experience=c.PersistentProgress.Experience;
            KeyboardButton(c,"SelectDungeon");Assert.IsTrue(SelectionButton(c,"DungeonChoice_1").interactable);Assert.IsFalse(SelectionButton(c,"DungeonChoice_2").interactable);
            KeyboardButton(c,"DungeonChoice_1");KeyboardButton(c,"CancelDungeonSelection");Assert.AreEqual(0,Get(c,"selectedDungeon"));
            KeyboardButton(c,"SelectDungeon");KeyboardButton(c,"DungeonChoice_1");KeyboardButton(c,"ConfirmDungeonSelection");Assert.AreEqual(1,Get(c,"selectedDungeon"));
            KeyboardButton(c,"NextDeparture");var run=c.PersistentProgress.NextDeparture.Start.RunId;Assert.AreEqual("selection-b",Store(c).Read().RouteId);
            Assert.AreEqual(experience,c.PersistentProgress.Experience);Assert.AreEqual(1,c.PersistentProgress.Receipts.Length);
            c=StartSelection();Assert.AreEqual(1,Get(c,"selectedDungeon"));Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
            Assert.AreEqual("selection-b",Store(c).Read().RouteId);Assert.AreEqual(run,Store(c).Read().State.RunId);
        }
        [Test]
        public void デバッグ全解放はクリア履歴を捏造せず選択を保存して通常設定で再開する()
        {
            var c=StartSelection(true);Assert.IsTrue(SelectionButton(c,"DungeonChoice_2").interactable);KeyboardButton(c,"DungeonChoice_2");
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.IsEmpty(c.PersistentProgress.ClearedDungeons);Assert.AreEqual("selection-c",Store(c).Read().RouteId);
            var run=c.PersistentProgress.NextDeparture.Start.RunId;c=StartSelection();Assert.AreEqual(2,Get(c,"selectedDungeon"));
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreEqual(run,Store(c).Read().State.RunId);Assert.IsEmpty(c.PersistentProgress.ClearedDungeons);
        }
        [TestCase("progress")] [TestCase("checkpoint")]
        public void 初回出撃の保存失敗は同じ計画を保持し再試行でも二重出撃しない(string fault)
        {
            var c=StartSelection(true);KeyboardButton(c,"DungeonChoice_1");
            var io=new KeyboardFaultIo{Fault="before"};
            if(fault=="progress")Set(Get(c,"clearReturnStore"),"io",io);else Set(c,"nextDepartureIo",io);
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.IsTrue((bool)Get(c,"initialHub"));Assert.IsFalse(Store(c).HasExistingData);
            Assert.AreEqual(fault=="progress",((ClearReturnSession)Get(c,"clearReturnSession")).HasPendingSave);
            var planned=c.PersistentProgress.NextDeparture?.Start.RunId;io.Fault=null;KeyboardButton(c,"RetryDungeonDeparture");
            Assert.IsFalse((bool)Get(c,"initialHub"));Assert.AreEqual("selection-b",Store(c).Read().RouteId);
            var run=c.PersistentProgress.NextDeparture.Start.RunId;if(planned!=null)Assert.AreEqual(planned,run);
            Call(c,"StartNextDeparture");Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);Assert.IsEmpty(c.PersistentProgress.Receipts);
        }

        [Test]
        public void 初回の進行保存完了後エラーも同じ計画を使い受領履歴を増やさない()
        {
            var c=StartSelection(true);KeyboardButton(c,"DungeonChoice_1");Set(Get(c,"clearReturnStore"),"io",new KeyboardFaultIo{Fault="after"});
            KeyboardButton(c,"ConfirmDungeonSelection");Assert.IsFalse((bool)Get(c,"initialHub"));var run=c.PersistentProgress.NextDeparture.Start.RunId;
            Assert.AreEqual("selection-b",Store(c).Read().RouteId);Assert.IsEmpty(c.PersistentProgress.Receipts);
            c=StartSelection();Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
        }
        [Test]
        public void 保存した選択先の定義変更は別ダンジョンへ振替えず既存ファイルを保持する()
        {
            var c=StartSelection(true);KeyboardButton(c,"DungeonChoice_1");KeyboardButton(c,"ConfirmDungeonSelection");var run=c.PersistentProgress.NextDeparture.Start.RunId;
            var before=System.IO.Directory.GetFiles(directory,"*.json",System.IO.SearchOption.AllDirectories).ToDictionary(p=>p,System.IO.File.ReadAllBytes);
            definitions=null;c=Start(configure: next=>{
                var deck=(Deck)next.GetType().GetMethod("CreateInitialDeck",Hidden).Invoke(next,null);
                next.ConfigureDungeonSelection(new[]{new FloorSequenceDefinitions("selection-a","v1",new[]{DungeonConfig.CreateTestDungeon(5,30,4)},deck),
                    new FloorSequenceDefinitions("selection-b","changed",new[]{DungeonConfig.CreateTestDungeon(5,30,4)},deck)});
            });
            Assert.IsFalse(c.TryRestoreMemoryCheckpoint(out _));Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);
            foreach(var pair in before)CollectionAssert.AreEqual(pair.Value,System.IO.File.ReadAllBytes(pair.Key));
        }

        [Test]
        public void 通貨画面中のポーズは前面に表示し停止中の受取を拒否する()
        {
            var controller = Start();
            var currency = (UnityEngine.GameObject)Get(controller, "currencyOverlay");
            currency.SetActive(true); currency.transform.SetAsLastSibling();
            var choice = new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>(); Set(controller, "currencyChoice", choice);
            controller.SetUserPaused(true);
            var pause = (UnityEngine.GameObject)Get(controller, "pauseOverlay");
            Assert.IsTrue(pause.activeSelf); Assert.Greater(pause.transform.GetSiblingIndex(), currency.transform.GetSiblingIndex());
            var button = currency.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "ReceiveCurrencyButton");
            button.onClick.Invoke(); Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending, choice.Task.Status);
            controller.SetUserPaused(false); Assert.IsFalse(pause.activeSelf); Assert.IsTrue(currency.activeSelf);
            choice.TrySetResult(false); currency.SetActive(false);
        }

        private static IReadOnlyList<Card> Win(Olca.Application.Dungeon.DungeonBattleController controller)
        {
            IReadOnlyList<Card> choices = null;
            var model = Model(controller); model.OnCardRewardPresented += cards => choices = cards;
            // このfixtureは階層/帰還を検証するため、別枠の通貨を一度だけ受領する。
            // GUI入力の検証ではなく、Coreの報酬判断を明示する試験操作。
            System.Action<long> receiveCurrency = null;
            receiveCurrency = amount => { model.Dungeon.OnCurrencyRewardOffered -= receiveCurrency; model.SelectCurrencyReward(true); };
            model.Dungeon.OnCurrencyRewardOffered += receiveCurrency;
            foreach (var enemy in model.CurrentBattle.Enemies.ToArray())
                if (!enemy.IsDead && !enemy.IsSummoned) enemy.TakeDamage(enemy.MaxHp, ElementType.None);
            return choices;
        }
        [TestCase(null, 180)] [TestCase(60, 60)]
        public void 通常出撃の自然回復値は保存復帰後も有効で期限に蓄積を一括回復する(int? configured, int expected)
        {
            var controller = Start(new[] { DungeonConfig.CreateTestDungeon(20, 30, 2) }, configured);
            Assert.AreEqual(expected, Model(controller).Dungeon.Player.PoiseRecoveryDefinitionFrames);
            ToBattle(controller);
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle;
            var player = battle.Player;
            Assert.AreEqual(expected, player.PoiseRecoveryDefinitionFrames);
            foreach (var enemy in battle.Enemies) enemy.ApplyStun(60000);
            battle.Resume(BattlePauseReason.CustomScreen);
            battle.RunLogicalFrame(() => { player.TakeDamage(1, poiseDamage: 7); battle.TickFrame(17); });
            Assert.AreEqual(expected, player.PoiseRecoveryRemainingFrames);
            controller.SetUserPaused(true);
            for (int i = 0; i < 20; i++) Tick(controller);
            Assert.AreEqual(expected, player.PoiseRecoveryRemainingFrames);
            controller.SetUserPaused(false);
            for (int i = 0; i < expected - 1; i++) battle.TickFrame(17);
            Assert.AreEqual(7, player.AccumulatedDamage);
            Assert.AreEqual(1, player.PoiseRecoveryRemainingFrames);
            battle.TickFrame(17);
            Assert.AreEqual(0, player.AccumulatedDamage);
            Assert.AreEqual(0, player.PoiseRecoveryRemainingFrames);
        }

        private Olca.Application.Dungeon.DungeonBattleController StartCombatDefaults(int stagger, int recovery, int invincibilityMs = 600)
            => Start(configure: controller => {
                Set(controller, "dungeonCellCount", 8); Set(controller, "firstEnemyCell", 2);
                Set(controller, "battleStaggerFrames", stagger); Set(controller, "enemyPoiseRecoveryFrames", recovery);
                Set(controller, "commonHitInvincibilityMs", invincibilityMs);
            });

        [TestCase(18)] [TestCase(6)]
        public void 既定出撃と保存復帰のプレイヤーは通常AIの累積削りで共通時間のけぞり入力を遮断する(int stagger)
        {
            var controller = StartCombatDefaults(stagger, 180);
            var map = Model(controller).Dungeon.Map;
            Assert.AreEqual(30, map.GetCell(2).Enemy.PoiseDefinition.Maximum);
            Assert.AreEqual(60, map.GetCell(7).Enemy.PoiseDefinition.Maximum);
            Assert.AreEqual(stagger, map.GetCell(7).Enemy.PoiseDefinition.StaggerFrames);
            ToBattle(controller);
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle; var player = battle.Player; var enemy = battle.Enemies[0];
            Assert.AreEqual(stagger, player.StaggerDurationFrames);
            enemy.MoveTo(new Vector2Int(player.GridPosition.X + 1, player.GridPosition.Y));
            battle.Resume(BattlePauseReason.CustomScreen);
            int notices = 0; player.OnStaggered += () => notices++;
            for (int f = 0; f < 400 && !player.IsStaggered; f++) battle.TickFrame(17);
            Assert.IsTrue(player.IsStaggered, "通常登録の隣接攻撃3発で累積30に到達");
            Assert.AreEqual(70, player.CurrentHp); Assert.AreEqual(1, notices);
            Assert.AreEqual(stagger, player.StaggerRemainingFrames); Assert.AreEqual(0, player.AccumulatedDamage);
            var card = new Olca.Application.Battle.SimpleCardFactory().CreateById("heal");
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            Assert.IsFalse(battle.CardUsage.TryUseCurrentCard()); Assert.AreEqual(1, battle.CardUsage.RemainingCount);
            int hp = enemy.CurrentHp; battle.PlayerBasicAttack(enemy); Assert.AreEqual(hp, enemy.CurrentHp);
            var position = player.GridPosition;
            controller.SetUserPaused(true); for (int f = 0; f < 20; f++) Tick(controller);
            Assert.AreEqual(stagger, player.StaggerRemainingFrames); controller.SetUserPaused(false);
            for (int f = 0; f < stagger; f++) {
                battle.TickFrame(17, new LogicalInput { Horizontal = -1 });
                Assert.AreEqual(position, player.GridPosition); Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);
            }
            Assert.IsFalse(player.IsStaggered);
            battle.TickFrame(17, new LogicalInput { Horizontal = -1 });
            Assert.IsTrue(battle.PlayerMovement.IsNormalMoving, "終了後の新しい入力を受け付ける");
        }

        [TestCase(18, 180)] [TestCase(6, 60)]
        public void 既定敵は通常カードの独立削りでのけぞり自然回復し定義が保存復帰する(int stagger, int recovery)
        {
            var controller = StartCombatDefaults(stagger, recovery); ToBattle(controller);
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle; var enemy = battle.Enemies[0]; var player = battle.Player;
            Assert.AreEqual(30, enemy.PoiseDefinition.Maximum); Assert.AreEqual(stagger, enemy.PoiseDefinition.StaggerFrames);
            Assert.AreEqual(recovery, enemy.PoiseRecoveryDelayFrames);
            player.SetInvincible(100000); enemy.MoveTo(new Vector2Int(player.GridPosition.X + 1, player.GridPosition.Y));
            battle.Resume(BattlePauseReason.CustomScreen);
            var card = new Card("poise-fixture", "検証ソード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 1, "HP1とは独立の削り7", poiseDamage: 7);
            System.Action hit = () => {
                battle.CardUsage.EnqueueCards(new List<Card> { card.Clone() });
                battle.RunLogicalFrame(() => Assert.IsTrue(battle.CardUsage.TryUseCurrentCard()));
            };
            int notices = 0; enemy.OnStaggered += () => notices++;
            for (int i = 0; i < 4; i++) hit();
            Assert.AreEqual(46, enemy.CurrentHp); Assert.AreEqual(28, enemy.AccumulatedPoiseDamage);
            hit(); Assert.AreEqual(45, enemy.CurrentHp); Assert.AreEqual(1, notices);
            Assert.AreEqual(stagger, enemy.StaggerRemainingFrames); Assert.AreEqual(0, enemy.AccumulatedPoiseDamage);
            battle.Pause(BattlePauseReason.User); for (int f = 0; f < 20; f++) battle.TickFrame(17);
            Assert.AreEqual(stagger, enemy.StaggerRemainingFrames); battle.Resume(BattlePauseReason.User);
            for (int f = 0; f < stagger; f++) battle.TickFrame(17);
            Assert.IsFalse(enemy.IsStaggered);
            hit(); Assert.AreEqual(7, enemy.AccumulatedPoiseDamage);
            for (int f = 0; f < recovery - 1; f++) battle.TickFrame(17);
            Assert.AreEqual(7, enemy.AccumulatedPoiseDamage); battle.TickFrame(17);
            Assert.AreEqual(0, enemy.AccumulatedPoiseDamage); Assert.AreEqual(1, notices);
        }

        [TestCase(0, false, true)] [TestCase(180, false, true)] [TestCase(180, true, true)] [TestCase(180, false, false)]
        public void 旧カードと旧敵定義の保存は既知の組合せで再開し別配置や新値の遡及を許さない(int recovery, bool differentLayout, bool legacyMap)
        {
            var host = new UnityEngine.GameObject("legacy-default-deck"); host.SetActive(false); objects.Add(host);
            var factoryController = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var deck = (Deck)factoryController.GetType().GetMethod("CreateDepartureDeck", Hidden).Invoke(factoryController, new object[] { true });
            var map = DungeonConfig.CreateTestDungeon(8, 30, differentLayout ? 3 : 2,
                enemyPoise: legacyMap ? null : new EnemyPoiseDefinition(30, 18, 180),
                bossPoise: legacyMap ? null : new EnemyPoiseDefinition(60, 18, 180));
            var legacyDefinitions = DungeonCheckpointSession.CreateDefault(map, deck).Definitions;
            var file = new Olca.Unity.FileCheckpointStore(legacyDefinitions, directory);
            var session = new DungeonCheckpointSession(legacyDefinitions, file);
            var legacy = new DungeonBattleManager(map, new PlayerCharacter(100, poiseRecoveryDelayFrames: recovery == 0 ? (int?)null : recovery),
                deck, new MockRandomProvider(), new MockLogger(), checkpoints: session);
            for (int f = 0; f < 120 && legacy.CurrentBattle == null; f++) legacy.TickFrame(17);
            Assert.IsNotNull(legacy.CurrentBattle);
            var bytes = System.IO.File.ReadAllBytes(file.ActivePath);
            var controller = StartCombatDefaults(18, 180);
            CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(file.ActivePath));
            if (differentLayout) {
                Assert.IsFalse(controller.TryRestoreMemoryCheckpoint(out _));
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(file.ActivePath)); return;
            }
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle;
            Assert.AreEqual(0, battle.Player.StaggerDurationFrames);
            Assert.AreEqual(recovery == 0 ? (int?)null : recovery, battle.Player.PoiseRecoveryDelayFrames);
            if (legacyMap) Assert.IsNull(battle.Enemies[0].PoiseDefinition);
            else Assert.AreEqual(30, battle.Enemies[0].PoiseDefinition.Maximum);
            foreach (var card in Model(controller).Dungeon.GetCurrentDeck().GetAllCards().Where(c => c.Category == CardCategory.Attack))
                Assert.IsNull(card.PoiseDamage, "旧カードのHP連動を新しい仮値へ書き換えない");
            CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(file.ActivePath));
        }

        [TestCase(600, 36)] [TestCase(400, 24)]
        public void 通常出撃の共通被弾無敵は保存復帰後もAIと基本攻撃で次フレームから発生しポーズ中停止する(int milliseconds, int frames)
        {
            var controller = StartCombatDefaults(18, 180, milliseconds); ToBattle(controller);
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle; var player = battle.Player; var enemy = battle.Enemies[0];
            Assert.AreEqual(milliseconds, battle.DefaultHitInvincibilityMs);
            Assert.IsFalse(player.IsInvincible); Assert.IsFalse(enemy.IsInvincible);
            Assert.AreEqual(0, new Olca.Application.Battle.SimpleCardFactory().CreateById("sword").HitInvincibilityMs);
            enemy.MoveTo(new Vector2Int(player.GridPosition.X + 1, player.GridPosition.Y));
            battle.Resume(BattlePauseReason.CustomScreen);
            battle.RunLogicalFrame(() => {
                battle.PlayerBasicAttack(enemy); battle.PlayerBasicAttack(enemy);
                Assert.IsFalse(enemy.IsInvincible, "新しい無敵は同フレームの2発目を妨げない");
                battle.TickFrame(17);
            });
            Assert.AreEqual(30, enemy.CurrentHp); Assert.AreEqual(90, player.CurrentHp);
            Assert.IsTrue(enemy.IsInvincible); Assert.IsTrue(player.IsInvincible);
            Assert.AreEqual(milliseconds, player.InvincibleDurationMs);
            Assert.IsFalse(player.TryTakeDamage(7, poiseDamage: 7)); Assert.AreEqual(10, player.AccumulatedDamage);
            battle.PlayerBasicAttack(enemy); Assert.AreEqual(30, enemy.CurrentHp);
            var card = new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard()); Assert.AreEqual(30, enemy.CurrentHp, "発生設定0のカードも既存無敵を貫通しない");
            controller.SetUserPaused(true); for (int f = 0; f < 20; f++) Tick(controller);
            Assert.AreEqual(milliseconds, player.InvincibleDurationMs); controller.SetUserPaused(false);
            for (int f = 0; f < frames - 1; f++) battle.TickFrame(17);
            Assert.IsTrue(player.IsInvincible); Assert.IsTrue(enemy.IsInvincible);
            battle.TickFrame(17); Assert.IsFalse(player.IsInvincible); Assert.IsFalse(enemy.IsInvincible);
            battle.PlayerBasicAttack(enemy); Assert.AreEqual(20, enemy.CurrentHp); Assert.IsTrue(enemy.IsInvincible);
            Assert.Throws<System.InvalidOperationException>(() => battle.ConfigureDefaultHitInvincibility(1000));
            Assert.AreEqual(milliseconds, battle.DefaultHitInvincibilityMs);
        }

        [TestCase(false, false, 5, true)] [TestCase(true, false, 0, false)] [TestCase(false, true, 0, false)]
        public void 通常移動の中間で受理したマグマだけ共通無敵を発生し削りを増やさない(bool barrier, bool floating, int loss, bool invincible)
        {
            var controller = StartCombatDefaults(18, 180); ToBattle(controller);
            var battle = Model(controller).CurrentBattle; var player = battle.Player;
            foreach (var enemy in battle.Enemies) enemy.ApplyStun(100000);
            battle.Resume(BattlePauseReason.CustomScreen);
            if (barrier) player.SetBarrier(15); player.TerrainState.IsFloating = floating;
            var target = new Vector2Int(player.GridPosition.X - 1, player.GridPosition.Y);
            battle.Field.ChangePanelType(target, PanelType.Magma);
            for (int f = 0; f < 4; f++) battle.TickFrame(17, new LogicalInput { Horizontal = -1 });
            Assert.AreEqual(100, player.CurrentHp); Assert.IsFalse(player.IsInvincible);
            battle.TickFrame(17, new LogicalInput { Horizontal = -1 });
            Assert.AreEqual(target, player.GridPosition); Assert.AreEqual(100 - loss, player.CurrentHp);
            Assert.AreEqual(invincible, player.IsInvincible); Assert.AreEqual(0, player.AccumulatedDamage);
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(target));
            if (invincible) {
                int remaining = player.InvincibleDurationMs;
                battle.Field.ChangePanelType(target, PanelType.Magma);
                battle.RunLogicalFrame(() => battle.PanelEffects.ApplyOnEnterEffect(player, target));
                Assert.AreEqual(95, player.CurrentHp); Assert.AreEqual(remaining, player.InvincibleDurationMs);
                Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(target));
            }
        }

        [TestCase("sword", 30, 15)] [TestCase("bomb", 40, 20)] [TestCase("fire_sword", 60, 30)]
        public void 通常カタログの攻撃カードは威力と独立した仮の削りでボスを攻撃し超越でも削りを保持する(string id, int power, int impact)
        {
            var controller = Start(configure: c => { Set(c, "dungeonCellCount", 8); Set(c, "firstEnemyCell", 7); });
            ToBattle(controller); Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle; var enemy = battle.Enemies[0];
            var factory = (Olca.Application.Battle.SimpleCardFactory)controller.GetType().GetMethod("CreateCardFactory", Hidden).Invoke(controller, null);
            Card card;
            if (id == "fire_sword") {
                var fused = new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(), factory)
                    .Execute(new[] { factory.CreateById("sword"), factory.CreateById("bomb") });
                Assert.IsTrue(fused.IsSuccess, fused.ErrorMessage); card = fused.ResultCard;
            } else card = Model(controller).Dungeon.GetCurrentDeck().GetAllCards().First(c => c.Id == id);
            Assert.AreEqual(impact, card.PoiseDamage); Assert.AreEqual(power, card.Power);
            var exceeded = factory.CreateExceeded(card, ExceedLevel.Lv1);
            Assert.AreEqual((int)(power * 1.5f), exceeded.Power); Assert.AreEqual(impact, exceeded.PoiseDamage);
            Assert.AreEqual(impact, card.Clone().PoiseDamage); Assert.AreEqual(0, card.HitInvincibilityMs);
            enemy.MoveTo(new Vector2Int(battle.Player.GridPosition.X + (id == "bomb" ? 3 : 1), battle.Player.GridPosition.Y));
            battle.Player.SetInvincible(100000); battle.Resume(BattlePauseReason.CustomScreen);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            battle.RunLogicalFrame(() => Assert.IsTrue(battle.CardUsage.TryUseCurrentCard()));
            if(id == "bomb") {
                Assert.AreEqual(200,enemy.CurrentHp);
                for(int f=0;f<90;f++)battle.TickFrame(17);
            }
            Assert.AreEqual(200 - power, enemy.CurrentHp); Assert.AreEqual(impact, enemy.AccumulatedPoiseDamage);
        }

        [TestCase(0)] [TestCase(9)]
        public void 出撃設定の削り調整は初期カードと保存復帰と通常使用へ一貫して届く(int impact)
        {
            var controller = Start(configure: c => { Set(c, "dungeonCellCount", 8); Set(c, "firstEnemyCell", 7); Set(c, "swordPoiseDamage", impact); });
            ToBattle(controller); Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var error), error);
            var battle = Model(controller).CurrentBattle; var enemy = battle.Enemies[0];
            var card = Model(controller).Dungeon.GetCurrentDeck().GetAllCards().First(c => c.Id == "sword");
            Assert.AreEqual(impact, card.PoiseDamage);
            enemy.MoveTo(new Vector2Int(battle.Player.GridPosition.X + 1, battle.Player.GridPosition.Y));
            battle.Player.SetInvincible(100000); battle.Resume(BattlePauseReason.CustomScreen);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            battle.RunLogicalFrame(() => Assert.IsTrue(battle.CardUsage.TryUseCurrentCard()));
            Assert.AreEqual(170, enemy.CurrentHp); Assert.AreEqual(impact, enemy.AccumulatedPoiseDamage);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Olca.Application.Battle.CardPoiseSettings(sword: -1));
        }

        [TestCase("keyboard")][TestCase("gamepad")][TestCase("audio")]
        public void 設定画面中は図鑑入口を隠し破棄して戻ると停止状態のまま再表示する(string kind)
        {
            var c=Start(new[]{Empty(3),DungeonConfig.CreateTestDungeon(8)});var model=Model(c);var bytes=System.IO.File.ReadAllBytes(Store(c).ActivePath);
            var profile=c.GameplayKeyboardProfile;var volumes=c.GameplayAudioVolumes;
            c.gameObject.SetActive(true);
            try
            {
                c.SetUserPaused(true);
                var entry=(UnityEngine.UI.Button)Get(c,"cardCollectionButton");
                for(int i=0;i<2;i++)
                {
                    Call(c,"UpdateViews");Assert.IsTrue(entry.gameObject.activeSelf);
                    KeyboardButton(c,kind=="keyboard"?"KeyboardSettingsButton":kind=="gamepad"?"GamepadSettingsButton":"AudioSettingsButton");
                    Call(c,"UpdateViews");Assert.IsFalse(entry.gameObject.activeSelf,"設定編集中は別画面への入口を前面へ出さない");
                    if(kind=="audio")Call(c,"CloseAudioSettings");
                    else ((Olca.Presentation.Field.GameplayKeyboardSettingsView)Get(c,kind=="keyboard"?"keyboardSettingsView":"gamepadSettingsView")).DiscardAndClose();
                    Call(c,"UpdateViews");Assert.IsTrue(entry.gameObject.activeSelf);
                    Assert.IsTrue(c.IsUserPaused);Assert.AreSame(model,Model(c));
                    Assert.AreSame(profile,c.GameplayKeyboardProfile);Assert.AreEqual(volumes,c.GameplayAudioVolumes);
                    CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(Store(c).ActivePath));
                }
            }
            finally {c.gameObject.SetActive(false);}
        }

        [TestCase(1920f,1080f)][TestCase(1662.77f,1247.08f)][TestCase(2199.64f,942.70f)]
        public void 拠点の補助操作は画面比率が変わっても図鑑と重ならず基準位置を保つ(float width,float height)
        {
            var c=Start();var viewport=new UnityEngine.GameObject("HubButtonViewport",typeof(UnityEngine.RectTransform));var rect=(UnityEngine.RectTransform)viewport.transform;rect.sizeDelta=new UnityEngine.Vector2(width,height);
            try{
                var names=new[]{"CardCollectionButton","OpenCustomizationEditor","OpenCustomizationUnlocks","OpenCustomizationAcquisitions","ManualSaveButton","NewGameButton"};var areas=new System.Collections.Generic.List<UnityEngine.Rect>();
                foreach(var name in names){var button=((UnityEngine.GameObject)Get(c,"pauseCanvas")).GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name==name);var r=(UnityEngine.RectTransform)button.transform;r.SetParent(rect,false);var corners=new UnityEngine.Vector3[4];r.GetWorldCorners(corners);var points=corners.Select(p=>rect.InverseTransformPoint(p)).ToArray();var area=UnityEngine.Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));Assert.GreaterOrEqual(area.xMin,rect.rect.xMin);Assert.LessOrEqual(area.xMax,rect.rect.xMax);Assert.GreaterOrEqual(area.yMin,rect.rect.yMin);Assert.LessOrEqual(area.yMax,rect.rect.yMax);foreach(var other in areas)Assert.IsFalse(area.Overlaps(other),name);areas.Add(area);if(width==1920&&name=="OpenCustomizationEditor"){Assert.AreEqual(650,area.center.x,.1);Assert.AreEqual(380,area.center.y,.1);}}
            }finally{UnityEngine.Object.DestroyImmediate(viewport);}
        }

        [Test]
        public void 報酬だけは背景のデッキ枚数を隠し報酬終了後に既存表示へ戻す()
        {
            var c=Start();var hud=(Olca.Presentation.Dungeon.DungeonHudView)Get(c,"dungeonHudView");var host=new UnityEngine.GameObject("BackgroundDeckCount",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));host.transform.SetParent(c.transform,false);var text=host.GetComponent<UnityEngine.UI.Text>();text.font=UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");var font=text.font;Set(hud,"deckCountText",text);
            Call(c,"UpdateViews");Assert.IsTrue(host.activeSelf);ToBattle(c);Call(c,"UpdateViews");Assert.IsTrue(host.activeSelf);
            var choices=Win(c);Call(c,"UpdateViews");Assert.IsFalse(host.activeSelf);Model(c).SelectCardReward(choices[0]);Call(c,"UpdateViews");Assert.IsTrue(host.activeSelf);Assert.AreSame(font,text.font);
        }

        private static void KeyboardButton(Olca.Application.Dungeon.DungeonBattleController controller, string name)
        {
            var canvas = (UnityEngine.GameObject)Get(controller, "pauseCanvas");
            canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == name && !b.GetComponentsInParent<UnityEngine.Transform>(true).Any(t=>t.name=="GamepadSettingsPanel")).onClick.Invoke();
        }
        [TestCase(false)] [TestCase(true)]
        public void ポーズのキー設定画面で実キーを交換し保存失敗は未適用のまま再試行と破棄と確認付き初期化ができる(bool failFirst)
        {
            var controller = Start(new[] { Empty(3), DungeonConfig.CreateTestDungeon(8) });
            var saveBytes = System.IO.File.ReadAllBytes(Store(controller).ActivePath);
            controller.SetUserPaused(true); KeyboardButton(controller, "KeyboardSettingsButton");
            var view = (Olca.Presentation.Field.GameplayKeyboardSettingsView)Get(controller, "keyboardSettingsView");
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            void Send(params UnityEngine.InputSystem.Key[] keys) {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));
                UnityEngine.InputSystem.InputSystem.Update(); Call(view, "Update");
            }
            string Draft(string name) => view.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == name).GetComponentInChildren<UnityEngine.UI.Text>().text;
            try {
                KeyboardButton(controller, "Clear_exploration_DashSecondary");
                Assert.AreEqual("未割当", Draft("Bind_exploration_DashSecondary"));
                KeyboardButton(controller, "Bind_battle_AttackPrimary"); Send();
                Send(UnityEngine.InputSystem.Key.J, UnityEngine.InputSystem.Key.K);
                Assert.AreEqual("Z", Draft("Bind_battle_AttackPrimary"), "複数キーを勝手な優先順で採用しない");
                Send(); Send(UnityEngine.InputSystem.Key.Space); Send();
                Assert.AreEqual("Space", Draft("Bind_battle_AttackPrimary")); Assert.AreEqual("Z", Draft("Bind_battle_CardPrimary"));
                Assert.AreEqual("Z", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
                var settings = (Olca.Unity.KeyboardBindingFileStore)Get(controller, "keyboardSettings");
                var io = new KeyboardFaultIo { Fault = failFirst ? "before" : null }; Set(settings, "io", io);
                KeyboardButton(controller, "SaveKeyboardSettings");
                if (failFirst) {
                    Assert.AreEqual("Z", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
                    Assert.IsFalse(System.IO.File.Exists(settings.ActivePath));
                    Assert.IsTrue(view.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("保存できませんでした")));
                    KeyboardButton(controller, "CloseKeyboardSettings"); KeyboardButton(controller, "KeyboardSettingsButton");
                    Assert.AreEqual("Z", Draft("Bind_battle_AttackPrimary"), "失敗後の破棄は未保存候補でなく適用済み設定へ戻る");
                    KeyboardButton(controller, "Bind_battle_AttackPrimary"); Send(); Send(UnityEngine.InputSystem.Key.Space); Send();
                    io.Fault = null; KeyboardButton(controller, "SaveKeyboardSettings");
                }
                Assert.AreEqual("Space", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
                Assert.AreEqual("Z", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "CardPrimary")]);
                var bytes = System.IO.File.ReadAllBytes(settings.ActivePath);
                KeyboardButton(controller, "ResetKeyboardSettings"); KeyboardButton(controller, "CancelKeyboardReset");
                Assert.AreEqual("Space", Draft("Bind_battle_AttackPrimary"));
                KeyboardButton(controller, "ResetKeyboardSettings"); KeyboardButton(controller, "ConfirmKeyboardReset");
                Assert.AreEqual("Z", Draft("Bind_battle_AttackPrimary"));
                Assert.AreEqual("Space", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
                KeyboardButton(controller, "CloseKeyboardSettings"); KeyboardButton(controller, "KeyboardSettingsButton");
                Assert.AreEqual("Space", Draft("Bind_battle_AttackPrimary")); CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(settings.ActivePath));
                KeyboardButton(controller, "ResetKeyboardSettings"); KeyboardButton(controller, "ConfirmKeyboardReset"); KeyboardButton(controller, "SaveKeyboardSettings");
                Assert.AreEqual("Z", controller.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
                controller.SetUserPaused(false); Assert.IsFalse(view.gameObject.activeSelf);
                CollectionAssert.AreEqual(saveBytes, System.IO.File.ReadAllBytes(Store(controller).ActivePath));
                var restarted = Start(); Assert.AreEqual("Z", restarted.GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")]);
            } finally {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); controller.SetUserPaused(false);
                Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void 最終報酬後に持ち帰り選択と経験値と帰還を実ボタンで確定し各段階を再起動できる(bool multipleFloors)
        {
            var final = DungeonConfig.CreateTestDungeon(5, 30, 4);
            var controller = Start(multipleFloors ? new[] { Empty(2), final } : new[] { final });
            ToBattle(controller); Assert.IsNull(controller.PersistentProgress.Flow, "途中階層では永続付与しない");
            var choices = Win(controller); Assert.IsNotNull(choices); Assert.IsNull(controller.PersistentProgress.Flow, "最終報酬完了前は付与しない");
            Model(controller).SelectCardReward(choices[0]);
            Assert.AreEqual(ClearReturnStage.Choosing, controller.PersistentProgress.Flow.Stage); Assert.AreEqual(0, controller.PersistentProgress.Experience);
            var gameplay = System.IO.File.ReadAllBytes(Store(controller).ActivePath);
            KeyboardButton(controller, "CarryCard_0");
            controller = Start(); Assert.AreEqual(ClearReturnStage.Choosing, controller.PersistentProgress.Flow.Stage);
            CollectionAssert.AreEqual(new[] { 0 }, controller.PersistentProgress.Flow.Selected);
            KeyboardButton(controller, "ConfirmCarryHome"); Assert.AreEqual(100, controller.PersistentProgress.Experience);
            Call(controller, "ConfirmCarryCards"); Assert.AreEqual(100, controller.PersistentProgress.Experience);
            controller = Start(); Assert.AreEqual(ClearReturnStage.Awarded, controller.PersistentProgress.Flow.Stage);
            KeyboardButton(controller, "ConfirmReturn"); Assert.AreEqual(105, controller.ReturnedPlayer.CurrentHp);
            Assert.AreEqual(11, controller.ReturnedPlayer.AttackPower); Assert.AreEqual(1, controller.PersistentProgress.Owned.Single().Count);
            controller = Start(); Assert.AreEqual(ClearReturnStage.Returned, controller.PersistentProgress.Flow.Stage);
            Assert.AreEqual(100, controller.PersistentProgress.Experience); Assert.IsFalse(controller.TryRestoreMemoryCheckpoint(out _));
            CollectionAssert.AreEqual(gameplay, System.IO.File.ReadAllBytes(Store(controller).ActivePath));
        }
        [Test]
        public void 最終報酬保存失敗時は進行を付与せず保存成功後だけ帰還選択へ進む()
        {
            var controller = Start(new[] { DungeonConfig.CreateTestDungeon(5, 30, 4) }); ToBattle(controller);
            var choices = Win(controller); var gameFile = Store(controller); var original = System.IO.File.ReadAllBytes(gameFile.ActivePath);
            var io = new FaultIo { Fail = true }; Set(gameFile, "io", io);
            Model(controller).SelectCardReward(choices[0]); Assert.IsTrue(Model(controller).Dungeon.IsCheckpointBlocked);
            Assert.IsNull(controller.PersistentProgress.Flow); Assert.AreEqual(0, controller.PersistentProgress.Experience);
            CollectionAssert.AreEqual(original, System.IO.File.ReadAllBytes(gameFile.ActivePath));
            io.Fail = false; Assert.IsTrue(controller.RetryMemoryCheckpoint());
            Assert.AreEqual(ClearReturnStage.Choosing, controller.PersistentProgress.Flow.Stage);
            KeyboardButton(controller, "ConfirmCarryHome"); KeyboardButton(controller, "ConfirmReturn");
            Assert.AreEqual(0, controller.PersistentProgress.Owned.Length); Assert.AreEqual(100, controller.PersistentProgress.Experience);
        }

        [TestCase("keyboard")][TestCase("overlay")][TestCase("both")][TestCase("none")]
        public void 先に破棄されたポーズUIを解除時に呼ばず時間とモデルと保存を維持する(string destroyed)
        {
            float oldScale=UnityEngine.Time.timeScale;
            try {
                var c=Start();var model=Model(c);var session=(DungeonCheckpointSession)Get(c,"memoryCheckpoints");
                var file=(Olca.Unity.FileCheckpointStore)session.Store;var saved=System.IO.File.ReadAllBytes(file.ActivePath);
                var keyboard=(Olca.Presentation.Field.GameplayKeyboardSettingsView)Get(c,"keyboardSettingsView");
                UnityEngine.Time.timeScale=.75f;c.SetUserPaused(true);KeyboardButton(c,"KeyboardSettingsButton");
                if(destroyed=="keyboard"||destroyed=="both")UnityEngine.Object.DestroyImmediate(keyboard.gameObject);
                if(destroyed=="overlay"||destroyed=="both")UnityEngine.Object.DestroyImmediate((UnityEngine.GameObject)Get(c,"pauseOverlay"));
                Assert.DoesNotThrow(()=>c.SetUserPaused(false));Assert.IsTrue(c.IsExplorationResumeCounting);FinishExplorationCountdown(c);Assert.IsFalse(model.IsUserPaused);Assert.IsFalse(Olca.Unity.GameplayPause.IsPaused);
                Assert.AreEqual(.75f,UnityEngine.Time.timeScale);Assert.AreSame(model,Model(c));
                if(keyboard!=null)Assert.IsFalse(keyboard.gameObject.activeSelf,"通常の解除は従来どおり設定画面を閉じる");
                CollectionAssert.AreEqual(saved,System.IO.File.ReadAllBytes(file.ActivePath));
            } finally {
                UnityEngine.Time.timeScale=oldScale;Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 全ポーズUIの先行破棄後もController終了は例外なく解除し重複終了で保存を変えない(bool paused)
        {
            float oldScale=UnityEngine.Time.timeScale;
            try {
                var c=Start();var model=Model(c);var session=(DungeonCheckpointSession)Get(c,"memoryCheckpoints");
                var file=(Olca.Unity.FileCheckpointStore)session.Store;var saved=System.IO.File.ReadAllBytes(file.ActivePath);
                UnityEngine.Time.timeScale=.75f;if(paused)c.SetUserPaused(true);
                UnityEngine.Object.DestroyImmediate((UnityEngine.GameObject)Get(c,"pauseCanvas"));
                Assert.DoesNotThrow(()=>Call(c,"OnDestroy"));Assert.DoesNotThrow(()=>Call(c,"OnDestroy"));
                Assert.IsFalse(model.IsUserPaused);Assert.IsFalse(Olca.Unity.GameplayPause.IsPaused);Assert.AreEqual(.75f,UnityEngine.Time.timeScale);
                CollectionAssert.AreEqual(saved,System.IO.File.ReadAllBytes(file.ActivePath));
            } finally {
                UnityEngine.Time.timeScale=oldScale;Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void 実キーZと保存したリマップJの短押しは成長後の攻撃値を通常Controllerへ渡す(bool transformed)
        {
            var progress = new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress"));
            Assert.IsTrue(progress.TryWrite(0,new ClearReturnProfile { Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),Experience=100,Level=2 },out var error),error);
            var c=StartCombatDefaults(18,180);ToBattle(c);var battle=Model(c).CurrentBattle;
            battle.CustomScreen.Close();battle.Resume(BattlePauseReason.CustomScreen);battle.Player.SetInvincible(60000);
            var target=battle.Enemies[0];target.MoveTo(new Vector2Int(battle.Player.GridPosition.X+1,battle.Player.GridPosition.Y));
            Assert.AreEqual(11,battle.Player.AttackPower);
            if(transformed){battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("remap",ElementResistances.Neutral,ElementType.Water,attack:new TransformationAttackDefinition(ElementType.Fire,chargedPowerPercent:250,chargeFrames:6))});battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());battle.Transformations.TryPreview("remap");battle.CustomScreen.Confirm();battle.CustomScreen.Close();}
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var provider=(Olca.Unity.UnityInputProvider)Get(c,"inputProvider");
            void Tap(UnityEngine.InputSystem.Key key) {
                var queue=new LogicalInputQueue();provider.CaptureTimestamped();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(key));UnityEngine.InputSystem.InputSystem.Update();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();
                foreach(var e in provider.CaptureTimestamped())queue.Enqueue(1,e.Input);
                Tick(c,queue.Take(1));
            }
            try {
                int hp=target.CurrentHp;Tap(UnityEngine.InputSystem.Key.Z);Assert.AreEqual(hp-11,target.CurrentHp);
                for(int i=0;i<40;i++)Tick(c);
                c.SetUserPaused(true);var edit=c.GameplayKeyboardProfile.BeginEdit();Assert.IsTrue(edit.TryAssign(("keyboard","battle","AttackPrimary"),"J",out error),error);
                Assert.IsTrue(c.TryApplyGameplayKeyboardProfile(edit.Apply(),out error),error);c.SetUserPaused(false);
                Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                hp=target.CurrentHp;Tap(UnityEngine.InputSystem.Key.Z);Assert.AreEqual(hp,target.CurrentHp);Tap(UnityEngine.InputSystem.Key.J);Assert.AreEqual(hp-11,target.CurrentHp);
                for(int f=0;f<40;f++)Tick(c);
                var heldQueue=new LogicalInputQueue();provider.CaptureTimestamped();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.J));UnityEngine.InputSystem.InputSystem.Update();
                foreach(var e in provider.CaptureTimestamped())heldQueue.Enqueue(1,e.Input);
                hp=target.CurrentHp;for(int f=1;f<=60;f++)Tick(c,heldQueue.Take(f));
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();
                foreach(var e in provider.CaptureTimestamped())heldQueue.Enqueue(61,e.Input);
                Tick(c,heldQueue.Take(61));Assert.AreEqual(hp-(transformed?27:22),target.CurrentHp,"保存済みJの保持/解放を通常Controllerでタメ分岐する");
                Call(c,"RefreshBasicAttackHud");var hud=(Olca.Presentation.Dungeon.BasicAttackHud)Get(c,"basicAttackHud");
                Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t=>t.text.Contains("[J]")));
                c=StartCombatDefaults(18,180);Assert.AreEqual("J",c.GameplayKeyboardProfile.Bindings[("keyboard","battle","AttackPrimary")]);
                Assert.AreEqual(100,c.PersistentProgress.Experience);Assert.AreEqual(2,c.PersistentProgress.Level);
            } finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame(); }
        }

        private Olca.Application.Dungeon.DungeonBattleController SavedDeckFixture()
        {
            var c=Start(new[]{DungeonConfig.CreateTestDungeon(5,30,4)});ToBattle(c);Model(c).SelectCardReward(Win(c)[0]);KeyboardButton(c,"ConfirmCarryHome");KeyboardButton(c,"ConfirmReturn");SeedOwnedDeparture(c);return c;
        }
        private static SavedDeckCard[] TwentySavedCards()=>new[]{"sword","heal","shield","fire","ice"}.SelectMany(id=>Enumerable.Range(0,4).Select(_=>new SavedDeckCard{Id=id,Level=ExceedLevel.Normal})).ToArray();
        private static void SavedName(Olca.Application.Dungeon.DungeonBattleController c,string name)
        {((UnityEngine.GameObject)Get(c,"pauseCanvas")).GetComponentInChildren<UnityEngine.UI.InputField>(true).text=name;}
        [Test]
        public void 保存デッキの空保存と編成と複製と装備と確認付き削除を実UIから接続し選択二十枚で再開する()
        {
            var c=SavedDeckFixture();var owned=c.PersistentProgress.Owned.Select(o=>o.Count).ToArray();var receipts=c.PersistentProgress.Receipts.Length;
            KeyboardButton(c,"SavedDeckMenu");KeyboardButton(c,"NewSavedDeck");SavedName(c,"出撃A");KeyboardButton(c,"SaveNamedDeck");
            var a=c.PersistentProgress.SavedDecks.Single().Id;Assert.IsEmpty(c.PersistentProgress.SavedDecks[0].Cards);Assert.IsNull(c.PersistentProgress.EquippedDeckId);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"EquipDeck_"+a);Assert.IsNull(c.PersistentProgress.EquippedDeckId);
            KeyboardButton(c,"EditDeck_"+a);for(int row=0;row<5;row++)for(int n=0;n<4;n++)KeyboardButton(c,"AddDepartureCard_"+row);
            KeyboardButton(c,"SaveNamedDeck");KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"CloneDeck_"+a);
            var b=c.PersistentProgress.SavedDecks.Single(d=>d.Id!=a).Id;Assert.AreEqual(2,c.PersistentProgress.SavedDecks.Length);KeyboardButton(c,"EquipDeck_"+a);
            KeyboardButton(c,"EditDeck_"+a);KeyboardButton(c,"RemoveDepartureCard_0");var file=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var bytes=System.IO.File.ReadAllBytes(file.ActivePath);
            KeyboardButton(c,"SaveNamedDeck");CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));Assert.AreEqual(20,c.PersistentProgress.SavedDecks.Single(d=>d.Id==a).Cards.Length);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"KeepEditingDeparture");Assert.AreEqual(19,((List<Card>)Get(c,"ownedDepartureDraft")).Count);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"DiscardDepartureDraft");
            KeyboardButton(c,"EditDeck_"+b);KeyboardButton(c,"AddDepartureCard_0");KeyboardButton(c,"SaveNamedDeck");Assert.AreEqual(21,c.PersistentProgress.SavedDecks.Single(d=>d.Id==b).Cards.Length);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"EquipDeck_"+b);Assert.AreEqual(a,c.PersistentProgress.EquippedDeckId);
            KeyboardButton(c,"EditDeck_"+b);KeyboardButton(c,"RemoveDepartureCard_0");KeyboardButton(c,"SaveNamedDeck");KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"EquipDeck_"+b);
            Assert.IsFalse(((ClearReturnSession)Get(c,"clearReturnSession")).DeleteDeck(b));Assert.AreEqual(b,c.PersistentProgress.EquippedDeckId);
            KeyboardButton(c,"DeleteDeck_"+a);KeyboardButton(c,"CancelDeleteDeck");Assert.AreEqual(2,c.PersistentProgress.SavedDecks.Length);
            KeyboardButton(c,"DeleteDeck_"+a);KeyboardButton(c,"ConfirmDeleteDeck");Assert.AreEqual(b,c.PersistentProgress.SavedDecks.Single().Id);
            CollectionAssert.AreEqual(owned,c.PersistentProgress.Owned.Select(o=>o.Count));Assert.AreEqual(receipts,c.PersistentProgress.Receipts.Length);
            KeyboardButton(c,"ReloadSavedDecks");Assert.AreEqual(b,c.PersistentProgress.EquippedDeckId);Assert.AreEqual(20,c.PersistentProgress.SavedDecks.Single().Cards.Length);
            var snapshot=c.PersistentProgress;snapshot.SavedDecks[0].Cards[0].Id="mutation";Assert.AreNotEqual("mutation",c.PersistentProgress.SavedDecks[0].Cards[0].Id);
            c=Start();Assert.AreEqual(b,c.PersistentProgress.EquippedDeckId);Assert.AreEqual(20,c.PersistentProgress.SavedDecks.Single().Cards.Length);
            KeyboardButton(c,"NextDeparture");Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().TotalCount);Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().GetAllCards().Distinct().Count());
            var plan=c.PersistentProgress.NextDeparture;Assert.AreEqual(20,plan.Start.DeckOrder.Length);
            var session=(ClearReturnSession)Get(c,"clearReturnSession");Assert.IsFalse(session.SaveDeck(b,"変更",TwentySavedCards()));Assert.IsFalse(session.EquipDeck(b));Assert.IsFalse(session.DeleteDeck(b));
            c=Start();Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreEqual(plan.Start.RunId,c.PersistentProgress.NextDeparture.Start.RunId);Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().TotalCount);
            CollectionAssert.AreEqual(owned,c.PersistentProgress.Owned.Select(o=>o.Count));Assert.AreEqual(100,c.PersistentProgress.Experience);
        }
        [TestCase("save","before")][TestCase("save","after")][TestCase("equip","before")][TestCase("equip","after")][TestCase("delete","before")][TestCase("delete","after")]
        public void 保存装備削除の失敗とOS完了後エラーを同じ計画で再試行し所持を増やさない(string operation,string fault)
        {
            var c=SavedDeckFixture();var session=(ClearReturnSession)Get(c,"clearReturnSession");string a=System.Guid.NewGuid().ToString("N"),b=System.Guid.NewGuid().ToString("N");
            Assert.IsTrue(session.SaveDeck(a,"A",TwentySavedCards()));Assert.IsTrue(session.SaveDeck(b,"B",TwentySavedCards()));Assert.IsTrue(session.EquipDeck(a));
            var store=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var before=System.IO.File.ReadAllBytes(store.ActivePath);var revision=c.PersistentProgress.Revision;
            var io=new KeyboardFaultIo{Fault=fault};Set(store,"io",io);
            bool result=operation=="save"?session.SaveDeck(b,"B空",new SavedDeckCard[0]):operation=="equip"?session.EquipDeck(b):session.DeleteDeck(b);
            Assert.AreEqual(fault=="after",result);
            if(fault=="before"){Assert.IsTrue(session.HasPendingSave);Assert.AreEqual(revision,c.PersistentProgress.Revision);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(store.ActivePath));io.Fault=null;Assert.IsTrue(session.Retry(),session.Error);}
            Assert.AreEqual(revision+1,c.PersistentProgress.Revision);Assert.AreEqual(20,c.PersistentProgress.Owned.Sum(o=>o.Count));Assert.AreEqual(100,c.PersistentProgress.Experience);Assert.AreEqual(1,c.PersistentProgress.Receipts.Length);
            c=Start();Assert.AreEqual(operation=="equip"?b:a,c.PersistentProgress.EquippedDeckId);
            Assert.AreEqual(operation=="delete"?1:2,c.PersistentProgress.SavedDecks.Length);if(operation=="save")Assert.IsEmpty(c.PersistentProgress.SavedDecks.Single(d=>d.Id==b).Cards);
        }
        [Test]
        public void 非装備は共通制限違反を保存でき未所持の新規登録と不正な装備参照を拒否する()
        {
            var c=SavedDeckFixture();var session=(ClearReturnSession)Get(c,"clearReturnSession");var id=System.Guid.NewGuid().ToString("N");
            Assert.IsTrue(session.SaveDeck(id,"上限超過",Enumerable.Range(0,20).Select(_=>new SavedDeckCard{Id="sword",Level=ExceedLevel.Normal}).ToArray()));Assert.IsFalse(session.EquipDeck(id));
            var before=c.PersistentProgress.Revision;Assert.IsFalse(session.SaveDeck(System.Guid.NewGuid().ToString("N"),"未所持",new[]{new SavedDeckCard{Id="unknown",Level=ExceedLevel.Normal}}));Assert.AreEqual(before,c.PersistentProgress.Revision);
            var broken=c.PersistentProgress;broken.EquippedDeckId=System.Guid.NewGuid().ToString("N");Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(broken));
            broken=c.PersistentProgress;broken.SavedDecks=broken.SavedDecks.Concat(new[]{broken.SavedDecks[0].Copy()}).ToArray();Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(broken));
            Assert.AreEqual(20,c.PersistentProgress.Owned.Sum(o=>o.Count));
        }

        private void SeedPriorProgressForFailure()
        {
            var p=new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),Experience=100,Level=2,Tokens=17,
                Owned=new[]{new OwnedCardCount{Id="heal",Name="heal_card",Rarity=CardRarity.Normal,Level=ExceedLevel.Normal,Count=2}},
                ClearedDungeons=new[]{"prior-clear"},Receipts=new[]{new ClearReceipt{RunId="prior-run",Fingerprint="prior-result",Experience=100}}};
            Assert.IsTrue(new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress")).TryWrite(0,p,out var error),error);
        }
        private static void ReachNonClearBattle(Olca.Application.Dungeon.DungeonBattleController c)
        {for(int n=0;n<1200&&Model(c).CurrentBattle==null;n++)Tick(c);Assert.IsNotNull(Model(c).CurrentBattle);}
        [TestCase(false, RunReturnOutcome.Defeated, "none")]
        [TestCase(false, RunReturnOutcome.Defeated, "before")]
        [TestCase(false, RunReturnOutcome.Defeated, "after")]
        [TestCase(false, RunReturnOutcome.Retreated, "none")]
        [TestCase(false, RunReturnOutcome.Retreated, "before")]
        [TestCase(false, RunReturnOutcome.Retreated, "after")]
        [TestCase(true, RunReturnOutcome.Defeated, "none")]
        [TestCase(true, RunReturnOutcome.Defeated, "before")]
        [TestCase(true, RunReturnOutcome.Defeated, "after")]
        [TestCase(true, RunReturnOutcome.Retreated, "none")]
        [TestCase(true, RunReturnOutcome.Retreated, "before")]
        [TestCase(true, RunReturnOutcome.Retreated, "after")]
        [TestCase(false, RunReturnOutcome.Retreated, "child")]
        [TestCase(true, RunReturnOutcome.Defeated, "child")]
        public void 敗北と撤退は獲得を失い既存所持成長を保ち保存再試行と再起動から一度だけ次回出撃する(bool route,RunReturnOutcome outcome,string fault)
        {
            SeedPriorProgressForFailure();var map=DungeonConfig.CreateTestDungeon(14,5,2);
            System.Action<Olca.Application.Dungeon.DungeonBattleController> setup=x=>x.ConfigureDungeon(DungeonConfig.CreateTestDungeon(14,5,2));
            var c=route?Start(new[]{map,DungeonConfig.CreateTestDungeon(5,30,4)}):Start(configure:setup);
            ReachNonClearBattle(c);var choices=Win(c);Assert.IsNotNull(choices);Model(c).SelectCardReward(choices[0]);
            Assert.AreEqual(1,Model(c).Dungeon.GetObtainedCards().Count);Assert.AreEqual(7,Model(c).Dungeon.GetCurrentDeck().TotalCount);
            Assert.Greater(Model(c).Dungeon.Currency, 0);
            var progress=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var before=System.IO.File.ReadAllBytes(progress.ActivePath);
            var io=new KeyboardFaultIo{Fault=fault=="before"||fault=="after"?fault:null};Set(progress,"io",io);
            if(outcome==RunReturnOutcome.Defeated){Model(c).ResumeAfterBattle();ReachNonClearBattle(c);Model(c).CurrentBattle.Player.TakeDamage(99999);}
            else{c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"CancelRetreat");Assert.IsTrue(c.IsUserPaused);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(progress.ActivePath));KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");}
            if(fault=="before"){
                Assert.IsNull(c.PersistentProgress.Flow);Assert.AreEqual(1,c.PersistentProgress.Receipts.Length);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(progress.ActivePath));
                Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).HasPendingSave);io.Fault=null;KeyboardButton(c,"RetryClearReturn");
            }
            Assert.AreEqual(0, Model(c).Dungeon.Currency);
            var ended=c.PersistentProgress.Flow;Assert.IsNotNull(ended,((ClearReturnSession)Get(c,"clearReturnSession")).Error);Assert.AreEqual(outcome,ended.Outcome);
            Assert.AreEqual(ClearReturnStage.Returned,ended.Stage);Assert.IsEmpty(ended.Candidates);Assert.IsEmpty(ended.Selected);Assert.AreEqual(0,ended.ExperienceAward);Assert.AreEqual(0,ended.TokenAward);
            Assert.AreEqual(100,c.PersistentProgress.Experience);Assert.AreEqual(2,c.PersistentProgress.Level);Assert.AreEqual(17,c.PersistentProgress.Tokens);Assert.AreEqual(2,c.PersistentProgress.Owned.Single().Count);
            CollectionAssert.AreEqual(new[]{"prior-clear"},c.PersistentProgress.ClearedDungeons);Assert.AreEqual(2,c.PersistentProgress.Receipts.Length);
            Assert.AreEqual(105,c.ReturnedPlayer.CurrentHp);Assert.AreEqual(11,c.ReturnedPlayer.AttackPower);Assert.IsFalse(c.ReturnedPlayer.IsInvincible);Assert.IsFalse(c.ReturnedPlayer.IsStaggered);
            Assert.IsFalse(c.TryRestoreMemoryCheckpoint(out _));Call(c,"EndRunWithoutRewards",outcome);Assert.AreEqual(2,c.PersistentProgress.Receipts.Length);
            var oldPath=route?Store(c).ActivePath:((Olca.Unity.FileCheckpointStore)((DungeonCheckpointSession)Get(c,"memoryCheckpoints")).Store).ActivePath;
            var closedBytes=System.IO.File.ReadAllBytes(oldPath);
            c=route?Start():Start(configure:setup);Assert.AreEqual(outcome,c.PersistentProgress.Flow.Outcome);Assert.IsTrue((bool)Get(c,"clearReturnVisible"));Assert.IsFalse(c.TryRestoreMemoryCheckpoint(out _));
            var childIo=new KeyboardFaultIo{Fault=fault=="child"?"before":null};Set(c,"nextDepartureIo",childIo);KeyboardButton(c,"NextDeparture");
            string run=c.PersistentProgress.NextDeparture.Start.RunId;Assert.AreNotEqual(ended.RunId,run);
            if(fault=="child"){
                c=Start(configure:x=>{if(!route)setup(x);Set(x,"nextDepartureIo",childIo);});childIo.Fault=null;KeyboardButton(c,"RetryClearReturn");Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);
            }
            Assert.AreEqual(0, Model(c).Dungeon.Currency);
            Assert.AreEqual(6,Model(c).Dungeon.GetCurrentDeck().TotalCount);Assert.IsEmpty(Model(c).Dungeon.GetObtainedCards());Assert.AreEqual(105,Model(c).Dungeon.Player.CurrentHp);
            Assert.AreEqual(2,c.PersistentProgress.Receipts.Length);Assert.AreEqual(2,c.PersistentProgress.Owned.Single().Count);Assert.AreEqual(100,c.PersistentProgress.Experience);
            c=route?Start():Start(configure:setup);Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);Assert.AreEqual(run,c.PersistentProgress.NextDeparture.Start.RunId);
            CollectionAssert.AreEqual(closedBytes,System.IO.File.ReadAllBytes(oldPath));
        }
        [TestCase(false)] [TestCase(true)]
        public void 未保存の終了依頼を再起動しても敗北はTerminalから復旧し撤退は未確定の旧保存から再依頼できる(bool defeat)
        {
            SeedPriorProgressForFailure();var map=DungeonConfig.CreateTestDungeon(14,5,2);System.Action<Olca.Application.Dungeon.DungeonBattleController> setup=x=>x.ConfigureDungeon(DungeonConfig.CreateTestDungeon(14,5,2));
            var c=Start(configure:setup);ReachNonClearBattle(c);Set(Get(c,"clearReturnStore"),"io",new KeyboardFaultIo{Fault="before"});
            if(defeat)Model(c).CurrentBattle.Player.TakeDamage(99999);else{c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");}
            Assert.IsNull(c.PersistentProgress.Flow);c=Start(configure:setup);
            if(!defeat){Assert.IsNull(c.PersistentProgress.Flow);Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");}
            Assert.AreEqual(defeat?RunReturnOutcome.Defeated:RunReturnOutcome.Retreated,c.PersistentProgress.Flow.Outcome);Assert.AreEqual(2,c.PersistentProgress.Receipts.Length);Assert.AreEqual(100,c.PersistentProgress.Experience);
        }

        // 所持を十分に集めた状態のfixture。製品スターターの付与ではない。
        private void SeedOwnedDeparture(Olca.Application.Dungeon.DungeonBattleController c)
        {
            var store=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var p=c.PersistentProgress;long revision=p.Revision;
            p.Owned=new[]{"sword","heal","shield","fire","ice"}.Select(id=>{var card=CardRewardSystem.CreateFallbackCard(id,CardRarity.Normal);
                return new OwnedCardCount{Id=card.Id,Name=card.Name,Rarity=card.Rarity,Level=card.ExceedLevel,Count=4};}).ToArray();
            p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(revision,p,out var error),error);
            Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).Reload());
        }
        [TestCase(false,"none")][TestCase(true,"none")][TestCase(false,"progress")][TestCase(true,"checkpoint")]
        public void 所持二十枚の編成から通常出撃し保存失敗と再開でも選択枚数と旧受領を維持する(bool route,string fault)
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> setup=c=>c.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5,30,4));
            var c=route?Start(new[]{Empty(2),DungeonConfig.CreateTestDungeon(5,30,4)}):Start(configure:setup);
            ToBattle(c);Model(c).SelectCardReward(Win(c)[0]);KeyboardButton(c,"ConfirmCarryHome");KeyboardButton(c,"ConfirmReturn");SeedOwnedDeparture(c);
            var oldPath=route?Store(c).ActivePath:((Olca.Unity.FileCheckpointStore)((DungeonCheckpointSession)Get(c,"memoryCheckpoints")).Store).ActivePath;
            var oldBytes=System.IO.File.ReadAllBytes(oldPath);var progress=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");
            var before=System.IO.File.ReadAllBytes(progress.ActivePath);var oldModel=Model(c);
            KeyboardButton(c,"EditOwnedDeparture");KeyboardButton(c,"AddDepartureCard_0");
            Call(c,"ConfirmOwnedDeparture");Assert.IsNull(c.PersistentProgress.NextDeparture);Assert.AreSame(oldModel,Model(c));
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"KeepEditingDeparture");
            Assert.AreEqual(1,((List<Card>)Get(c,"ownedDepartureDraft")).Count);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"DiscardDepartureDraft");KeyboardButton(c,"EditOwnedDeparture");
            Assert.AreEqual(0,((List<Card>)Get(c,"ownedDepartureDraft")).Count);
            for(int row=0;row<5;row++)for(int count=0;count<5;count++)Call(c,"ChangeOwnedDeparture",row,1);
            var draft=((List<Card>)Get(c,"ownedDepartureDraft")).ToArray();Assert.AreEqual(20,draft.Length);Assert.IsTrue(draft.GroupBy(x=>x.Id).All(g=>g.Count()==4));
            CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(progress.ActivePath));
            var progressIo=new KeyboardFaultIo{Fault=fault=="progress"?"before":null};Set(progress,"io",progressIo);
            var checkpointIo=new KeyboardFaultIo{Fault=fault=="checkpoint"?"before":null};Set(c,"nextDepartureIo",checkpointIo);
            KeyboardButton(c,"ConfirmOwnedDeparture");
            if(fault=="progress"){Assert.AreSame(oldModel,Model(c));Assert.IsNull(c.PersistentProgress.NextDeparture);progressIo.Fault=null;KeyboardButton(c,"RetryClearReturn");}
            if(fault=="checkpoint"){Assert.AreSame(oldModel,Model(c));var id=c.PersistentProgress.NextDeparture.Start.RunId;
                c=Start(configure:x=>{if(!route)setup(x);Set(x,"nextDepartureIo",checkpointIo);});checkpointIo.Fault=null;KeyboardButton(c,"RetryClearReturn");Assert.AreEqual(id,c.PersistentProgress.NextDeparture.Start.RunId);}
            var plan=c.PersistentProgress.NextDeparture;Assert.AreEqual(20,plan.Start.DeckOrder.Length);
            CollectionAssert.AreEqual(draft.Select(x=>x.Id),Model(c).Dungeon.GetCurrentDeck().GetAllCards().Select(x=>x.Id));
            Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().GetAllCards().Distinct().Count());Assert.IsTrue(c.PersistentProgress.Owned.All(o=>o.Count==4));
            Assert.AreEqual(100,c.PersistentProgress.Experience);Assert.AreEqual(1,c.PersistentProgress.Receipts.Length);Assert.AreEqual(0,Model(c).Dungeon.GetObtainedCards().Count);
            c=route?Start():Start(configure:setup);Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error),error);
            Assert.AreEqual(plan.Start.RunId,c.PersistentProgress.NextDeparture.Start.RunId);Assert.AreEqual(20,Model(c).Dungeon.GetCurrentDeck().TotalCount);
            c.SetUserPaused(true);KeyboardButton(c,"OwnedCardsButton");Call(c,"OpenOwnedDeparture");Assert.IsFalse((bool)Get(c,"ownedDepartureEditing"));c.SetUserPaused(false);
            CollectionAssert.AreEqual(oldBytes,System.IO.File.ReadAllBytes(oldPath));
        }
        [Test]
        public void 未解決の所持を編成せず未所持を候補に足さず空所持でも既存出撃を保つ()
        {
            var c=Start(new[]{DungeonConfig.CreateTestDungeon(5,30,4)});ToBattle(c);Model(c).SelectCardReward(Win(c)[0]);KeyboardButton(c,"ConfirmCarryHome");KeyboardButton(c,"ConfirmReturn");
            KeyboardButton(c,"EditOwnedDeparture");Assert.AreEqual(0,((Card[])Get(c,"ownedDepartureOptions")).Length);KeyboardButton(c,"CloseOwnedDeparture");
            var store=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var p=c.PersistentProgress;long revision=p.Revision;
            p.Owned=new[]{new OwnedCardCount{Id="unresolved",Name="未知",Rarity=CardRarity.Normal,Level=ExceedLevel.Normal,Count=4}};
            p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(revision,p,out var error),error);Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).Reload());
            KeyboardButton(c,"EditOwnedDeparture");Assert.AreEqual(1,((Card[])Get(c,"ownedDepartureOptions")).Length);Assert.IsNull(((Card[])Get(c,"ownedDepartureOptions"))[0]);
            Call(c,"ChangeOwnedDeparture",0,1);Assert.AreEqual(0,((List<Card>)Get(c,"ownedDepartureDraft")).Count);Call(c,"ConfirmOwnedDeparture");Assert.IsNull(c.PersistentProgress.NextDeparture);
            KeyboardButton(c,"CloseOwnedDeparture");KeyboardButton(c,"NextDeparture");Assert.AreEqual(6,Model(c).Dungeon.GetCurrentDeck().TotalCount);
        }

        [TestCase(false)] [TestCase(true)]
        public void 帰還から次回出撃と再クリアでも所持と成長を保ち取得履歴と報酬を分離する(bool route)
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> setup = c => c.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5, 30, 4));
            var c = route ? Start(new[] { Empty(2), DungeonConfig.CreateTestDungeon(5, 30, 4) }) : Start(configure: setup);
            ToBattle(c); Model(c).SelectCardReward(Win(c)[0]); KeyboardButton(c, "CarryCard_0");
            KeyboardButton(c, "ConfirmCarryHome"); KeyboardButton(c, "ConfirmReturn");
            var oldPath = route ? Store(c).ActivePath : ((Olca.Unity.FileCheckpointStore)((DungeonCheckpointSession)Get(c, "memoryCheckpoints")).Store).ActivePath;
            var oldBytes = System.IO.File.ReadAllBytes(oldPath); string parent = c.PersistentProgress.Flow.RunId;
            var initialIds = ((Deck)c.GetType().GetMethod("CreateInitialDeck", Hidden).Invoke(c, null)).GetAllCards().Select(x => x.Id).ToArray();
            KeyboardButton(c, "NextDeparture");
            var plan = c.PersistentProgress.NextDeparture; Assert.IsNotNull(plan, (string)Get(c, "clearReturnError")); Assert.AreEqual(parent, plan.ParentRunId);
            Assert.AreNotEqual(parent, plan.Start.RunId); Assert.AreEqual(CheckpointBoundary.Departure, plan.Start.Boundary);
            Assert.AreEqual(105, Model(c).Dungeon.Player.CurrentHp); Assert.AreEqual(11, Model(c).Dungeon.Player.AttackPower);
            Assert.IsFalse(Model(c).Dungeon.Player.IsInvincible); Assert.AreEqual(0, Model(c).Dungeon.GetObtainedCards().Count);
            CollectionAssert.AreEqual(initialIds, Model(c).Dungeon.GetCurrentDeck().GetAllCards().Select(x => x.Id));
            Assert.AreEqual(100, c.PersistentProgress.Experience); Assert.AreEqual(1, c.PersistentProgress.Owned.Single().Count);
            var nextDir = c.ActiveCheckpointDirectory; Assert.AreNotEqual(directory, nextDir);
            Call(c, "StartNextDeparture"); Assert.AreEqual(plan.Start.RunId, c.PersistentProgress.NextDeparture.Start.RunId, "再入で新規攻略を作らない");
            c.SetUserPaused(true); KeyboardButton(c, "OwnedCardsButton");
            Assert.IsFalse(((UnityEngine.GameObject)Get(c,"pauseOverlay")).activeSelf, "透過一覧の背後へメニューを重ねない");
            var view = (Olca.Presentation.Dungeon.ClearReturnView)Get(c, "clearReturnView");
            Assert.IsTrue(view.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("1/4マイ")));
            Assert.IsFalse(view.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b => b.name == "NextDeparture" && b.gameObject.activeSelf));
            KeyboardButton(c, "CloseOwnedCards"); Assert.IsTrue(c.IsUserPaused); Assert.IsTrue(((UnityEngine.GameObject)Get(c,"pauseOverlay")).activeSelf); c.SetUserPaused(false);
            c = route ? Start() : Start(configure: setup);
            Assert.AreEqual(nextDir, c.ActiveCheckpointDirectory); Assert.AreEqual(100, c.PersistentProgress.Experience);
            Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error), error); Assert.AreEqual(105, Model(c).Dungeon.Player.CurrentHp);
            ToBattle(c); Model(c).SelectCardReward(Win(c)[0]);
            Assert.AreEqual(ClearReturnStage.Choosing, c.PersistentProgress.Flow.Stage); Assert.AreEqual(50, c.PersistentProgress.Flow.ExperienceAward);
            Assert.AreEqual(1, c.PersistentProgress.Flow.Candidates.Length, "前回の取得履歴を持ち込まない");
            KeyboardButton(c, "CarryCard_0"); KeyboardButton(c, "ConfirmCarryHome"); KeyboardButton(c, "ConfirmReturn");
            Assert.AreEqual(150, c.PersistentProgress.Experience); Assert.AreEqual(2, c.PersistentProgress.Owned.Single().Count); Assert.AreEqual(2, c.PersistentProgress.Receipts.Length);
            KeyboardButton(c, "NextDeparture"); Assert.AreNotEqual(nextDir, c.ActiveCheckpointDirectory);
            CollectionAssert.AreEqual(oldBytes, System.IO.File.ReadAllBytes(oldPath));
        }
        [TestCase(false, "progress")] [TestCase(true, "progress")]
        [TestCase(false, "checkpoint")] [TestCase(true, "checkpoint")]
        [TestCase(false, "after")] [TestCase(true, "after")]
        public void 次回出撃の進行保存と攻略保存の失敗および中断を同じ計画で復旧する(bool route, string fault)
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> setup = c => c.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5, 30, 4));
            var c = route ? Start(new[] { Empty(2), DungeonConfig.CreateTestDungeon(5, 30, 4) }) : Start(configure: setup);
            ToBattle(c); Model(c).SelectCardReward(Win(c)[0]); KeyboardButton(c, "CarryCard_0"); KeyboardButton(c, "ConfirmCarryHome"); KeyboardButton(c, "ConfirmReturn");
            var oldModel = Model(c); var oldPath = route ? Store(c).ActivePath : ((Olca.Unity.FileCheckpointStore)((DungeonCheckpointSession)Get(c, "memoryCheckpoints")).Store).ActivePath;
            var oldBytes = System.IO.File.ReadAllBytes(oldPath);
            var progressIo = new KeyboardFaultIo { Fault = fault == "progress" ? "before" : null };
            Set(Get(c, "clearReturnStore"), "io", progressIo);
            var nextIo = new KeyboardFaultIo { Fault = fault == "checkpoint" ? "before" : fault == "after" ? "after" : null }; Set(c, "nextDepartureIo", nextIo);
            KeyboardButton(c, "NextDeparture");
            if (fault == "progress") {
                Assert.AreSame(oldModel, Model(c)); Assert.IsNull(c.PersistentProgress.NextDeparture);
                Assert.IsTrue(((ClearReturnSession)Get(c, "clearReturnSession")).HasPendingSave);
                progressIo.Fault = null; KeyboardButton(c, "RetryClearReturn");
            } else if (fault == "checkpoint") {
                Assert.AreSame(oldModel, Model(c)); var frozenId = c.PersistentProgress.NextDeparture.Start.RunId;
                // 進行文書だけ確定して終了しても、旧攻略や受領を戻さず同じ新規攻略を再試行する。
                System.Action<Olca.Application.Dungeon.DungeonBattleController> restartSetup = x => { if (!route) setup(x); Set(x, "nextDepartureIo", nextIo); };
                c = Start(configure: restartSetup); Assert.AreEqual(frozenId, c.PersistentProgress.NextDeparture.Start.RunId);
                nextIo.Fault = null; KeyboardButton(c, "RetryClearReturn"); Assert.AreEqual(frozenId, c.PersistentProgress.NextDeparture.Start.RunId);
            }
            Assert.AreEqual(GamePhase.Dungeon, Model(c).CurrentPhase); Assert.AreEqual(105, Model(c).Dungeon.Player.CurrentHp);
            Assert.AreEqual(100, c.PersistentProgress.Experience); Assert.AreEqual(1, c.PersistentProgress.Receipts.Length); Assert.AreEqual(1, c.PersistentProgress.Owned.Single().Count);
            CollectionAssert.AreEqual(oldBytes, System.IO.File.ReadAllBytes(oldPath));
            var nextId = c.PersistentProgress.NextDeparture.Start.RunId;
            c = route ? Start() : Start(configure: setup); Assert.IsTrue(c.TryRestoreMemoryCheckpoint(out var error), error);
            Assert.AreEqual(nextId, ((DungeonCheckpointSession)Get(c, "memoryCheckpoints")).RunId); Assert.AreEqual(100, c.PersistentProgress.Experience);
        }
        [TestCase(false)] [TestCase(true)]
        public void 次回攻略の保存が破損していたら再生成や旧クリアへの巻戻しをせず停止する(bool route)
        {
            System.Action<Olca.Application.Dungeon.DungeonBattleController> setup = c => c.ConfigureDungeon(DungeonConfig.CreateTestDungeon(5, 30, 4));
            var c = route ? Start(new[] { Empty(2), DungeonConfig.CreateTestDungeon(5, 30, 4) }) : Start(configure: setup);
            ToBattle(c); Model(c).SelectCardReward(Win(c)[0]); KeyboardButton(c, "ConfirmCarryHome"); KeyboardButton(c, "ConfirmReturn");
            Set(c, "nextDepartureIo", new KeyboardFaultIo { Fault = "before" }); KeyboardButton(c, "NextDeparture");
            var planId = c.PersistentProgress.NextDeparture.Start.RunId;
            string brokenPath = System.IO.Path.Combine(c.ActiveCheckpointDirectory, route ? "floors.json" : "active.json");
            // 実際の階層保存名を使う。
            if (route) brokenPath = new Olca.Unity.FloorSequenceFileStore(definitions, c.ActiveCheckpointDirectory).ActivePath;
            System.IO.File.WriteAllText(brokenPath, "broken-next-run");
            c = route ? Start() : Start(configure: setup);
            Assert.IsFalse(c.TryRestoreMemoryCheckpoint(out var error)); Assert.IsNotEmpty(error);
            Assert.AreEqual(planId, c.PersistentProgress.NextDeparture.Start.RunId); Assert.AreEqual(100, c.PersistentProgress.Experience);
            Assert.AreEqual(1, c.PersistentProgress.Receipts.Length); Assert.AreEqual("broken-next-run", System.IO.File.ReadAllText(brokenPath));
        }
        [Test]
        public void 旧定義の成功Terminalは一度帰還し次回出撃だけ現在の定義と成長を使う()
        {
            var host = new UnityEngine.GameObject("legacy-next-deck"); host.SetActive(false); objects.Add(host);
            var template = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var deck = (Deck)template.GetType().GetMethod("CreateDepartureDeck", Hidden).Invoke(template, new object[] { true });
            var map = DungeonConfig.CreateTestDungeon(8, 30, 2);
            var defs = DungeonCheckpointSession.CreateDefault(map, deck).Definitions;
            var file = new Olca.Unity.FileCheckpointStore(defs, directory);
            var legacy = new DungeonBattleManager(map, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: new DungeonCheckpointSession(defs, file));
            legacy.OnCardRewardPresented += _ => legacy.SelectCardReward(null);
            for (int i = 0; i < 600 && legacy.CurrentPhase != GamePhase.DungeonCleared; i++) {
                legacy.TickFrame(17); if (legacy.CurrentBattle != null) foreach (var e in legacy.CurrentBattle.Enemies.ToArray()) if (!e.IsDead) e.TakeDamage(e.MaxHp, ElementType.None);
                legacy.ResumeAfterBattle();
            }
            Assert.AreEqual(GamePhase.DungeonCleared, legacy.CurrentPhase); var terminal = System.IO.File.ReadAllBytes(file.ActivePath);
            var c = StartCombatDefaults(18, 180); Assert.AreEqual(ClearReturnStage.Choosing, c.PersistentProgress.Flow.Stage);
            KeyboardButton(c, "ConfirmCarryHome"); KeyboardButton(c, "ConfirmReturn"); KeyboardButton(c, "NextDeparture");
            Assert.IsNotNull(c.PersistentProgress.NextDeparture, (string)Get(c, "clearReturnError"));
            Assert.AreEqual(GamePhase.Dungeon, Model(c).CurrentPhase); Assert.AreEqual(105, Model(c).Dungeon.Player.MaxHp);
            Assert.AreEqual(18, Model(c).Dungeon.Player.StaggerDurationFrames); Assert.AreEqual(100, c.PersistentProgress.Experience);
            Assert.AreEqual(15, Model(c).Dungeon.GetCurrentDeck().GetAllCards().First(x => x.Id == "sword").PoiseDamage);
            CollectionAssert.AreEqual(terminal, System.IO.File.ReadAllBytes(file.ActivePath));
        }
        [Test]
        public void 旧進行に帰還がなくても攻略中の所持参照は保存と進行を変えない()
        {
            var c = Start(); var game = Model(c); var progress = c.PersistentProgress.Revision;
            c.SetUserPaused(true); KeyboardButton(c, "OwnedCardsButton");
            Assert.IsTrue(((Olca.Presentation.Dungeon.ClearReturnView)Get(c, "clearReturnView")).gameObject.activeSelf);
            KeyboardButton(c, "CloseOwnedCards"); Assert.AreSame(game, Model(c)); Assert.AreEqual(progress, c.PersistentProgress.Revision);
            c.SetUserPaused(false);
        }

        private sealed class KeyboardFaultIo : Olca.Unity.CheckpointFileIo
        {
            public string Fault;
            public override void MoveNew(string temporary, string destination)
            {
                if (Fault == "before") throw new System.IO.IOException("fixture");
                base.MoveNew(temporary, destination);
                if (Fault == "after") throw new System.IO.IOException("fixture");
            }
            public override void Replace(string temporary,string destination,string backup)
            {
                if(Fault=="before")throw new System.IO.IOException("fixture");
                base.Replace(temporary,destination,backup);
                if(Fault=="after")throw new System.IO.IOException("fixture");
            }
        }

        [TestCase(null)][TestCase("before")][TestCase("after")]
        public void キー保存適用は失敗時に旧割当を維持し再起動後の実入力とガイドへ復元する(string fault)
        {
            var controller=Start(new[]{Empty(3),DungeonConfig.CreateTestDungeon(8)});
            var provider=(Olca.Unity.UnityInputProvider)Get(controller,"inputProvider");
            Olca.Unity.UnityInputProvider restoredProvider=null;
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            try{
                var gameplayBytes=System.IO.File.ReadAllBytes(Store(controller).ActivePath);
                var settings=(Olca.Unity.KeyboardBindingFileStore)Get(controller,"keyboardSettings");
                Assert.IsFalse(System.IO.File.Exists(settings.ActivePath));
                var original=controller.GameplayKeyboardProfile;var edit=original.BeginEdit();
                Assert.IsTrue(edit.TryAssign(("keyboard","exploration","JumpPrimary"),"J",out _));edit.Discard();
                Assert.AreEqual("Space",edit.Draft[("keyboard","exploration","JumpPrimary")]);Assert.IsFalse(System.IO.File.Exists(settings.ActivePath));
                controller.SetUserPaused(true);Assert.IsTrue(controller.TryApplyGameplayKeyboardProfile(original,out var error),error);
                var before=System.IO.File.ReadAllBytes(settings.ActivePath);
                Assert.IsTrue(edit.TryAssign(("keyboard","exploration","JumpPrimary"),"J",out _));var candidate=edit.Apply();
                var io=new KeyboardFaultIo{Fault=fault};Set(settings,"io",io);
                bool saved=controller.TryApplyGameplayKeyboardProfile(candidate,out error);
                if(fault=="before"){
                    Assert.IsFalse(saved);Assert.AreSame(original,controller.GameplayKeyboardProfile);
                    CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(settings.ActivePath));
                    io.Fault=null;Assert.IsTrue(controller.TryApplyGameplayKeyboardProfile(candidate,out error),error);
                }else Assert.IsTrue(saved,error);
                Assert.AreEqual("J",controller.GameplayKeyboardProfile.Bindings[("keyboard","exploration","JumpPrimary")]);
                StringAssert.Contains("J",Olca.Presentation.Field.GameFieldView.FormatGameplayInstructions(controller.GameplayKeyboardProfile,GamePhase.Dungeon));
                CollectionAssert.AreEqual(gameplayBytes,System.IO.File.ReadAllBytes(Store(controller).ActivePath));
                controller.SetUserPaused(false);provider.Dispose();
                var restarted=Start();restoredProvider=(Olca.Unity.UnityInputProvider)Get(restarted,"inputProvider");
                Assert.IsNull(restarted.GameplayKeyboardSettingsError);
                Assert.AreEqual("J",restarted.GameplayKeyboardProfile.Bindings[("keyboard","exploration","JumpPrimary")]);
                Assert.AreEqual("RightShift",restarted.GameplayKeyboardProfile.Bindings[("keyboard","exploration","DashSecondary")]);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();
                restoredProvider.CaptureTimestamped();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.J));UnityEngine.InputSystem.InputSystem.Update();
                Assert.IsTrue(restoredProvider.CaptureTimestamped().Last().Input.JumpButtonDown);
                CollectionAssert.AreEqual(gameplayBytes,System.IO.File.ReadAllBytes(Store(restarted).ActivePath));
            }finally{
                provider.Dispose();restoredProvider?.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                controller.SetUserPaused(false);Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase("broken")][TestCase("version")][TestCase("device")][TestCase("key")]
        public void 破損未知版と異なるデバイスのキー設定は初期操作で起動し上書きしない(string fault)
        {
            var settings=new Olca.Unity.KeyboardBindingFileStore(System.IO.Path.Combine(directory,"settings"));
            Assert.IsTrue(settings.TryRead(out var defaults,out _));Assert.IsFalse(System.IO.Directory.Exists(settings.DirectoryPath));
            Assert.IsTrue(settings.TrySave(defaults,out _));var text=System.IO.File.ReadAllText(settings.ActivePath);
            text=fault=="broken"?"{broken":fault=="version"?text.Replace("\"Version\":1","\"Version\":99"):
                fault=="device"?text.Replace("keyboard","controller"):text.Replace("LeftShift","not-a-key");
            System.IO.File.WriteAllText(settings.ActivePath,text);
            var controller=Start(new[]{Empty(3),DungeonConfig.CreateTestDungeon(8)});
            try{
                Assert.IsNotEmpty(controller.GameplayKeyboardSettingsError);
                Assert.AreEqual("Space",controller.GameplayKeyboardProfile.Bindings[("keyboard","exploration","JumpPrimary")]);
                controller.SetUserPaused(true);Assert.IsFalse(controller.TryApplyGameplayKeyboardProfile(defaults,out _));
                Assert.AreEqual(text,System.IO.File.ReadAllText(settings.ActivePath));
            }finally{
                controller.SetUserPaused(false);((Olca.Unity.UnityInputProvider)Get(controller,"inputProvider")).Dispose();
                Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void 終端保存失敗中は旧モデルとファイルを維持し再試行後だけ描画と入力を切り替える(bool jumping)
        {
            var controller = Start(new[] { Empty(3), DungeonConfig.CreateTestDungeon(8) });
            // 階層引継ぎ用のHP初期条件。戦闘外の強靭度削りは作らない。
            var old = Model(controller); old.Dungeon.Player.TakeDamage(37, affectsPoise: false);
            int clears = 0, transfers = 0; old.OnDungeonClearCompleted += _ => clears++;
            old.OnFloorCompleted += () => { transfers++; Assert.AreEqual(1, Store(controller).Read().FloorIndex); };
            var io = new FaultIo { Fail = true }; Set(Store(controller), "io", io);
            var before = System.IO.File.ReadAllBytes(Store(controller).ActivePath);
            if (jumping) { Tick(controller, new LogicalInput { DashButtonDown = true }); Tick(controller, new LogicalInput { JumpButtonDown = true }); }
            for (int i = 0; i < 40 && !old.Dungeon.IsCheckpointBlocked; i++) Tick(controller);
            Assert.IsTrue(old.Dungeon.IsCheckpointBlocked); Assert.AreSame(old, Model(controller));
            Assert.AreEqual(0, controller.CurrentFloorIndex); Assert.AreEqual(0, transfers); Assert.AreEqual(0, clears);
            var position = old.Dungeon.PlayerPosition;
            for (int i = 0; i < 4; i++) Tick(controller, new LogicalInput { DashButtonDown = true, JumpButtonDown = true });
            Assert.AreEqual(position, old.Dungeon.PlayerPosition);
            Assert.IsFalse(controller.TryRestoreMemoryCheckpoint(out _)); Assert.IsFalse(controller.RetryMemoryCheckpoint());
            CollectionAssert.AreEqual(before, System.IO.File.ReadAllBytes(Store(controller).ActivePath));
            io.Fail = false;
            io.BeforeReplace = () => { Assert.IsFalse(controller.RetryMemoryCheckpoint()); Assert.AreSame(old, Model(controller)); };
            Assert.IsTrue(controller.RetryMemoryCheckpoint());
            var next = Model(controller);
            Assert.AreNotSame(old, next); Assert.IsTrue(old.IsSuspended); Assert.AreEqual(1, transfers); Assert.AreEqual(0, clears);
            Assert.AreEqual(1, controller.CurrentFloorIndex); Assert.AreEqual(63, next.Dungeon.Player.CurrentHp);
            Assert.AreEqual(0, next.Dungeon.PlayerPosition); Assert.IsFalse(next.Dungeon.IsJumping); Assert.IsFalse(next.Dungeon.IsDashing);
            Assert.AreSame(next.Dungeon, Get(Get(controller, "gameFieldView"), "dungeonSystem"));
            Assert.IsTrue((bool)Get(Get(controller, "gameFieldView"), "playerPositionUpdateEnabled"));
            StringAssert.Contains("2カイ", ((UnityEngine.UI.Text)Get(Get(controller, "dungeonHudView"), "progressText")).text);
            Assert.IsFalse(controller.RetryMemoryCheckpoint()); old.SelectCardReward(null); old.TickFrame(17);
            Assert.AreEqual(1, transfers); Assert.AreEqual(1, Store(controller).Read().FloorIndex);
            Tick(controller, new LogicalInput { DashButtonDown = true }); Assert.IsTrue(next.Dungeon.IsDashing);
            Tick(controller, new LogicalInput { JumpButtonDown = true }); Assert.IsTrue(next.Dungeon.IsJumping);
            // 新規Controllerは旧階層ではなく、保存成功済みの次階層の開始を復元する。
            var restarted = Start(); Assert.IsTrue(restarted.TryRestoreMemoryCheckpoint(out var error), error);
            Assert.AreEqual(1, restarted.CurrentFloorIndex); Assert.AreEqual(0, Model(restarted).Dungeon.PlayerPosition);
            Assert.AreEqual(63, Model(restarted).Dungeon.Player.CurrentHp);
            Assert.AreEqual(CheckpointBoundary.Departure, Store(restarted).Read().State.Boundary);
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(directory, "active.json")));
        }
        [TestCase(false)] [TestCase(true)]
        public void 中間ボス報酬完了後だけ進み最終階層だけクリアを通知する(bool skip)
        {
            var controller = Start(new[] { DungeonConfig.CreateTestDungeon(4), DungeonConfig.CreateTestDungeon(5) });
            ToBattle(controller); var choices = Win(controller); Assert.IsNotNull(choices);
            var old = Model(controller); Assert.AreEqual(GamePhase.CardReward, old.CurrentPhase);
            Assert.AreEqual(0, controller.CurrentFloorIndex); int clears = 0, transfers = 0;
            old.OnDungeonClearCompleted += _ => clears++; old.OnFloorCompleted += () => transfers++;
            var io = new FaultIo { Fail = true }; Set(Store(controller), "io", io);
            old.SelectCardReward(skip ? null : choices[0]);
            Assert.IsTrue(old.Dungeon.IsCheckpointBlocked); Assert.AreEqual(0, transfers);
            Assert.AreEqual(CheckpointBoundary.BattleStart, Store(controller).Read().State.Boundary);
            io.Fail = false; Assert.IsTrue(controller.RetryMemoryCheckpoint());
            Assert.AreEqual(1, transfers); Assert.AreEqual(0, clears); Assert.AreEqual(1, controller.CurrentFloorIndex);
            var next = Model(controller); Assert.AreEqual(skip ? 0 : 1, next.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(10, next.Dungeon.Currency); Assert.AreEqual(10, Store(controller).Read().State.Currency);
            Assert.AreEqual(6 + (skip ? 0 : 1), next.Dungeon.GetCurrentDeck().TotalCount);
            old.SelectCardReward(choices[0]); Assert.AreEqual(1, transfers);
            ToBattle(controller);
            Assert.AreEqual(1, Store(controller).Read().FloorIndex);
            Assert.AreEqual(CheckpointBoundary.BattleStart, Store(controller).Read().State.Boundary);
            Assert.IsTrue(controller.TryRestoreMemoryCheckpoint(out var restoreError), restoreError);
            next = Model(controller); Assert.AreEqual(1, controller.CurrentFloorIndex);
            Assert.AreEqual(GamePhase.Battle, next.CurrentPhase);
            var finalChoices = Win(controller); Assert.IsNotNull(finalChoices);
            next.OnDungeonClearCompleted += _ => clears++;
            next.SelectCardReward(null); Tick(controller);
            Assert.AreEqual(GamePhase.DungeonCleared, next.CurrentPhase); Assert.AreEqual(1, clears);
            Assert.AreEqual(0, next.Dungeon.Currency); Assert.AreEqual(0, Store(controller).Read().State.Currency);
            Assert.AreEqual(CheckpointBoundary.Terminal, Store(controller).Read().State.Boundary);
            Assert.AreEqual(1, Store(controller).Read().FloorIndex);
            next.SelectCardReward(finalChoices[0]); Tick(controller); Assert.AreEqual(1, clears);
            var restarted = Start(); Assert.IsFalse(restarted.TryRestoreMemoryCheckpoint(out _));
        }
        [Test]
        public void ボスなし階層は途中の全敵撃破では移動せず終端で進む()
        {
            var first = Empty(20); first.MapCells[2] = new MapCell(2, new Enemy("normal", "途中敵", 50, ElementType.None, new Vector2Int(2, 0)));
            var controller = Start(new[] { first, DungeonConfig.CreateTestDungeon(5) });
            ToBattle(controller); Win(controller);
            if (Model(controller).CurrentPhase == GamePhase.CardReward) Model(controller).SelectCardReward(null);
            Assert.AreEqual(0, controller.CurrentFloorIndex); Assert.AreEqual(GamePhase.Dungeon, Model(controller).CurrentPhase);
            Assert.AreEqual(CheckpointBoundary.RewardCompleted, Store(controller).Read().State.Boundary);
            Model(controller).ResumeAfterBattle();
            for (int i = 0; i < 160 && controller.CurrentFloorIndex == 0; i++) Tick(controller);
            Assert.AreEqual(1, controller.CurrentFloorIndex);
        }
        [Test]
        public void 階層交換時に旧時計の追いつきフレームと予約入力を次階層へ流さない()
        {
            var controller = Start(new[] { Empty(2), Empty(3), DungeonConfig.CreateTestDungeon(5) });
            var loop = (Olca.Unity.UnityLogicalLoop)Get(controller, "logicalLoop");
            loop.Advance(100000000, null, (frame, ms, input) => Call(controller, "TickLogicalFrame", frame, ms, input));
            Assert.AreEqual(1, controller.CurrentFloorIndex); Assert.AreEqual(0, Model(controller).Dungeon.PlayerPosition);
            Assert.AreEqual(0, loop.Clock.Frame); Assert.AreEqual(0, loop.Clock.PendingFrames);
            for (int i = 0; i < 25 && controller.CurrentFloorIndex == 1; i++) Tick(controller);
            Assert.AreEqual(2, controller.CurrentFloorIndex);
            var restarted = Start(); Assert.IsTrue(restarted.TryRestoreMemoryCheckpoint(out var error), error);
            Assert.AreEqual(2, restarted.CurrentFloorIndex);
        }
        [Test]
        public void 実シーンの未割当進行度欄も階層列でだけ一度生成し同じHUDで更新する()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/DungeonScene.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var hud = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Olca.Presentation.Dungeon.DungeonHudView>(true)).Single();
                Assert.IsNull(Get(hud, "progressText"));
                hud.Refresh(new Olca.Presentation.Dungeon.DungeonHudData(100, 100, 0, 6, false, false, 6, 0));
                Assert.IsNull(Get(hud, "progressText"), "既定単階層の表示を増やさない");
                hud.Refresh(new Olca.Presentation.Dungeon.DungeonHudData(100, 100, 1, 6, false, false, 6, 0, 1));
                var label = (UnityEngine.UI.Text)Get(hud, "progressText");
                Assert.IsNotNull(label); Assert.AreEqual("1カイ  1 / 6", label.text);
                Assert.IsFalse(label.raycastTarget); Assert.AreSame(hud.transform, label.transform.parent);
                Assert.AreSame(((UnityEngine.UI.Text)Get(hud, "deckCountText")).font, label.font);
                hud.Refresh(new Olca.Presentation.Dungeon.DungeonHudData(63, 100, 0, 20, false, false, 7, 1, 2));
                Assert.AreSame(label, Get(hud, "progressText")); Assert.AreEqual("2カイ  0 / 20", label.text);
                Assert.AreEqual(1, hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Count(t => t.name == "FloorProgressText"));
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }
        [Test]
        public void 中間階層での敗北は次階層へ進めず終了保存から再開しない()
        {
            var controller = Start(new[] { DungeonConfig.CreateTestDungeon(4), DungeonConfig.CreateTestDungeon(5) });
            ToBattle(controller); var model = Model(controller); int transitions = 0;
            model.OnFloorCompleted += () => transitions++;
            model.CurrentBattle.Player.TakeDamage(100); Tick(controller);
            Assert.AreEqual(GamePhase.GameOver, model.CurrentPhase); Assert.AreEqual(0, transitions);
            Assert.AreEqual(0, controller.CurrentFloorIndex); Assert.AreEqual(0, Store(controller).Read().State.Player.Hp);
            Assert.AreEqual(CheckpointBoundary.Terminal, Store(controller).Read().State.Boundary);
            var restarted = Start(); Assert.IsFalse(restarted.TryRestoreMemoryCheckpoint(out _));
        }
        [Test]
        public void 外部指定が一階層だけならボス報酬後にそのまま最終クリアする()
        {
            var controller = Start(new[] { DungeonConfig.CreateTestDungeon(4) });
            ToBattle(controller); Win(controller); int clears = 0;
            Model(controller).OnDungeonClearCompleted += _ => clears++;
            Model(controller).SelectCardReward(null); Tick(controller);
            Assert.AreEqual(1, clears); Assert.AreEqual(0, controller.CurrentFloorIndex);
            Assert.AreEqual(GamePhase.DungeonCleared, Model(controller).CurrentPhase);
            Assert.AreEqual(CheckpointBoundary.Terminal, Store(controller).Read().State.Boundary);
        }
        [Test]
        public void 階層定義が異なる再開は保存と表示中モデルを変更しない()
        {
            var controller = Start(new[] { Empty(2), DungeonConfig.CreateTestDungeon(5) });
            var before = System.IO.File.ReadAllBytes(Store(controller).ActivePath);
            var restarted = Start(new[] { Empty(2), DungeonConfig.CreateTestDungeon(6) });
            var old = Model(restarted);
            Assert.IsFalse(restarted.TryRestoreMemoryCheckpoint(out _)); Assert.AreSame(old, Model(restarted));
            CollectionAssert.AreEqual(before, System.IO.File.ReadAllBytes(Store(controller).ActivePath));
        }
    }
}


namespace Olca.Tests.Integration
{
    [TestFixture]
    public class CarryHomeSelectionRuleTests
    {
        private static Card Card(string id, ExceedLevel level=ExceedLevel.Normal)
            => new Card(id,id,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,10,"fixture",exceedLevel:level);

        [TestCase(0)][TestCase(1)][TestCase(3)][TestCase(4)]
        public void 選択上限を検証し入力リストや獲得履歴を変更しない(int count)
        {
            var obtained=Enumerable.Range(0,4).Select(i=>Card("fixture-"+i)).ToList();
            var selected=obtained.Take(count).ToList();
            bool accepted=CarryHomeSelectionRules.TryValidate(obtained,selected,out var result,out var error);
            Assert.AreEqual(count<=3,accepted);Assert.AreEqual(4,obtained.Count);Assert.AreEqual(count,selected.Count);
            if(accepted){Assert.IsNull(error);CollectionAssert.AreEqual(selected,result);selected.Clear();Assert.AreEqual(count,result.Count);}
            else{Assert.IsNull(result);Assert.IsNotEmpty(error);}
        }
        [TestCase(false)][TestCase(true)]
        public void 同一カードの重複は同じ個体でも別個体でも拒否する(bool sameInstance)
        {
            var a=Card("same");var b=sameInstance?a:Card("same");
            Assert.IsFalse(CarryHomeSelectionRules.TryValidate(new[]{a,b},new[]{a,b},out var result,out _));Assert.IsNull(result);
        }
        [Test] public void 同じシリーズの別名とレベル違いは別カードとして選べる()
        {
            var cards=new[]{Card("same"),Card("same",ExceedLevel.Lv1),Card("other",ExceedLevel.Lv2)};
            Assert.IsTrue(CarryHomeSelectionRules.TryValidate(cards,cards,out var result,out var error),error);
            CollectionAssert.AreEqual(cards,result);
        }
        [TestCase(ExceedLevel.Lv3)][TestCase((ExceedLevel)99)]
        public void 超越専用と未知のレベルを拒否する(ExceedLevel level)
        {
            var card=Card("same",level);Assert.IsFalse(CarryHomeSelectionRules.TryValidate(new[]{card},new[]{card},out _,out _));
        }
        [Test] public void 履歴にない持込個体とnullを拒否し未選択候補は変更しない()
        {
            var obtained=new[]{Card("same"),Card("other")};
            Assert.IsFalse(CarryHomeSelectionRules.TryValidate(obtained,new[]{Card("same")},out _,out _));
            Assert.IsFalse(CarryHomeSelectionRules.TryValidate(obtained,new Card[]{null},out _,out _));
            Assert.IsFalse(CarryHomeSelectionRules.TryValidate(obtained,null,out _,out _));
            Assert.IsFalse(CarryHomeSelectionRules.TryValidate(null,new Card[0],out _,out _));
            Assert.IsTrue(CarryHomeSelectionRules.TryValidate(obtained,new[]{obtained[0]},out _,out _));Assert.AreEqual(2,obtained.Length);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class ClearReturnPersistenceTests
    {
        private sealed class ProgressActionFaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Before,After;
            public override void Replace(string temporary,string destination,string backup){if(Before)throw new System.IO.IOException("before");base.Replace(temporary,destination,backup);if(After)throw new System.IO.IOException("after");}
        }
        [Test]
        public void 手動保存は同じ進行だけを再保存し旧形式と設定と攻略ファイルを変更しない()
        {
            var session=new ClearReturnSession(Store());var starter=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            Assert.IsTrue(session.InitializeStarter(starter));var id=session.Snapshot.EquippedDeckId;
            var checkpoint=System.IO.Path.Combine(directory,"active.json");var settings=System.IO.Path.Combine(directory,"keyboard.json");
            System.IO.File.WriteAllText(checkpoint,"checkpoint-bytes");System.IO.File.WriteAllText(settings,"settings-bytes");
            long revision=session.Snapshot.Revision;Assert.IsTrue(session.ManualSave(),session.Error);
            var restored=new ClearReturnSession(Store());Assert.AreEqual(revision+1,restored.Snapshot.Revision);Assert.AreEqual(id,restored.Snapshot.EquippedDeckId);
            Assert.AreEqual(20,restored.Snapshot.Owned.Sum(o=>o.Count));Assert.IsNull(restored.Snapshot.CheckpointGeneration);
            Assert.AreEqual("checkpoint-bytes",System.IO.File.ReadAllText(checkpoint));Assert.AreEqual("settings-bytes",System.IO.File.ReadAllText(settings));
        }
        [TestCase(false)] [TestCase(true)]
        public void 新規開始は確定した同じ計画を原子的に保存し旧進行を保全して二重配布しない(bool after)
        {
            var io=new ProgressActionFaultIo();var session=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(directory,io));var starter=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            Assert.IsTrue(session.InitializeStarter(starter));Assert.IsTrue(session.Begin(Terminal(Card("old")),"old-dungeon",new ClearReturnSettings()));
            Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());
            var before=System.IO.File.ReadAllBytes(Store().ActivePath);var revision=session.Snapshot.Revision;
            var settings=System.IO.Path.Combine(directory,"settings.json");var checkpoint=System.IO.Path.Combine(directory,"active.json");
            System.IO.File.WriteAllText(settings,"keep settings");System.IO.File.WriteAllText(checkpoint,"keep old checkpoint");
            io.Before=!after;io.After=after;bool ok=session.StartNewGame(revision,starter);Assert.AreEqual(after,ok);
            if(!after){CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(Store().ActivePath));Assert.IsTrue(session.HasPendingSave);Assert.IsFalse(session.StartNewGame(revision,starter));io.Before=false;Assert.IsTrue(session.Retry(),session.Error);}
            Assert.IsFalse(session.StartNewGame(revision,starter),"古い確認の二重決定を拒否");
            var current=new ClearReturnSession(Store()).Snapshot;Assert.AreEqual(1,current.Level);Assert.AreEqual(0,current.Experience);Assert.AreEqual(0,current.Tokens);Assert.IsNull(current.Flow);Assert.IsNull(current.NextDeparture);
            Assert.IsEmpty(current.Receipts);Assert.IsEmpty(current.ClearedDungeons);Assert.AreEqual(20,current.Owned.Sum(o=>o.Count));Assert.AreEqual(1,current.SavedDecks.Length);
            Assert.IsTrue(System.Guid.TryParseExact(current.CheckpointGeneration,"N",out _));
            Assert.AreEqual("keep settings",System.IO.File.ReadAllText(settings));Assert.AreEqual("keep old checkpoint",System.IO.File.ReadAllText(checkpoint));
            var archives=System.IO.Directory.GetFiles(System.IO.Path.Combine(directory,"new-game-backups"),"*.json");Assert.AreEqual(1,archives.Length);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(archives[0]));
            Assert.IsTrue(session.ManualSave());CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(archives[0]));
        }
        [Test]
        public void 確認後の更新と不正スターターと不正保存世代は新規開始で上書きしない()
        {
            var session=new ClearReturnSession(Store());var starter=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();Assert.IsTrue(session.InitializeStarter(starter));
            long old=session.Snapshot.Revision;Assert.IsTrue(session.ManualSave());var bytes=System.IO.File.ReadAllBytes(Store().ActivePath);
            Assert.IsFalse(session.StartNewGame(old,starter));Assert.IsFalse(session.StartNewGame(session.Snapshot.Revision,starter.Take(6).ToArray()));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(Store().ActivePath));
            var bad=session.Snapshot;bad.CheckpointGeneration="../old";Assert.Throws<System.ArgumentException>(()=>ClearReturnProfile.Validate(bad));
        }
        [Test]
        public void 手動保存の障害は旧進行を維持して同じ内容を再試行できる()
        {
            var io=new FaultIo();var session=new ClearReturnSession(Store(io));Assert.IsTrue(session.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()));
            var before=System.IO.File.ReadAllBytes(Store().ActivePath);io.Before=true;Assert.IsFalse(session.ManualSave());CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(Store().ActivePath));
            Assert.IsFalse(session.ManualSave());io.Before=false;Assert.IsTrue(session.Retry());Assert.AreEqual(20,new ClearReturnSession(Store()).Snapshot.Owned.Sum(o=>o.Count));
        }
        [System.Runtime.Serialization.DataContract]
        private sealed class LegacyEnvelope
        {
            [System.Runtime.Serialization.DataMember] public int Version;
            [System.Runtime.Serialization.DataMember] public string Payload;
            [System.Runtime.Serialization.DataMember] public string Sha256;
        }
        [Test]
        public void 終了種別フィールドのない旧版進行は成功帰還として読み既存バイト列を書き換えない()
        {
            var session=new ClearReturnSession(Store());Assert.IsTrue(session.Begin(Terminal(Card("old")),"legacy",new ClearReturnSettings()));Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());
            var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(LegacyEnvelope));LegacyEnvelope envelope;
            using(var stream=new System.IO.MemoryStream(System.IO.File.ReadAllBytes(Store().ActivePath)))envelope=(LegacyEnvelope)serializer.ReadObject(stream);
            envelope.Payload=envelope.Payload.Replace("\"Outcome\":0,","").Replace("\"SavedDecks\":[],","").Replace("\"EquippedDeckId\":null,","");StringAssert.DoesNotContain("Outcome",envelope.Payload);StringAssert.DoesNotContain("SavedDecks",envelope.Payload);
            using(var sha=System.Security.Cryptography.SHA256.Create())envelope.Sha256=System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(envelope.Payload))).Replace("-","");
            using(var stream=new System.IO.MemoryStream()){serializer.WriteObject(stream,envelope);System.IO.File.WriteAllBytes(Store().ActivePath,stream.ToArray());}
            var old=System.IO.File.ReadAllBytes(Store().ActivePath);var restored=new ClearReturnSession(Store());Assert.IsTrue(restored.Ready,restored.Error);Assert.AreEqual(RunReturnOutcome.Cleared,restored.Snapshot.Flow.Outcome);Assert.AreEqual(RunReturnOutcome.Cleared,restored.Snapshot.Receipts.Single().Outcome);CollectionAssert.AreEqual(old,System.IO.File.ReadAllBytes(Store().ActivePath));
        }

        private string directory;
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Before, After;
            public System.Action During;
            public override void MoveNew(string temporary, string destination) {
                During?.Invoke(); if (Before) throw new System.IO.IOException("before"); base.MoveNew(temporary, destination); if (After) throw new System.IO.IOException("after");
            }
            public override void Replace(string temporary, string destination, string backup) {
                During?.Invoke(); if (Before) throw new System.IO.IOException("before"); base.Replace(temporary, destination, backup); if (After) throw new System.IO.IOException("after");
            }
        }
        [SetUp] public void Setup() => directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-return-" + System.Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        private static Card Card(string id, CardRarity rarity = CardRarity.Normal, ExceedLevel level = ExceedLevel.Normal)
            => new Card(id, id, 1, rarity, CardCategory.Attack, ElementType.None, CardSeries.Sword, 1, "", exceedLevel: level);
        // 選択規則用の取得履歴fixture。通常報酬/破棄とControllerの経路は別の統合ケースで通す。
        private static RestoredCheckpoint Terminal(params Card[] history)
        {
            var map = DungeonConfig.CreateTestDungeon(8, 30, 2); var deck = new Deck(history.Select(c => c.Clone()).ToList());
            var session = DungeonCheckpointSession.CreateDefault(map, deck);
            var game = new DungeonBattleManager(map, new PlayerCharacter(100), deck, new MockRandomProvider(), new MockLogger(), checkpoints: session);
            var rewards = (CardRewardSystem)typeof(DungeonSystem).GetField("cardRewardSystem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game.Dungeon);
            foreach (var card in history) rewards.SelectCard(card);
            game.OnCardRewardPresented += _ => game.SelectCardReward(null);
            for (int f = 0; f < 500 && game.CurrentPhase != GamePhase.DungeonCleared; f++) {
                game.TickFrame(17);
                if (game.CurrentBattle != null) foreach (var e in game.CurrentBattle.Enemies.ToArray()) if (!e.IsDead) e.TakeDamage(e.MaxHp, ElementType.None);
                game.ResumeAfterBattle();
            }
            Assert.AreEqual(GamePhase.DungeonCleared, game.CurrentPhase);
            Assert.IsTrue(CheckpointCodec.TryRestore(session.Store.Read(), session.Definitions, out var result, out var error), error); return result;
        }
        [TestCase(false)][TestCase(true)]
        public void スターターは保存失敗と書込後例外でも同じ20枚を一度だけ確定する(bool after)
        {
            var io=new FaultIo{Before=!after,After=after};var store=Store(io);var session=new ClearReturnSession(store);
            var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            bool ok=session.InitializeStarter(cards);Assert.AreEqual(after,ok);
            if(!after){Assert.AreEqual(0,session.Snapshot.Owned.Length);Assert.IsTrue(session.HasPendingSave);io.Before=false;Assert.IsTrue(session.Retry());}
            Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));var id=session.Snapshot.EquippedDeckId;
            session=new ClearReturnSession(Store());Assert.IsFalse(session.InitializeStarter(cards));
            Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));Assert.AreEqual(id,session.Snapshot.EquippedDeckId);
            Assert.AreEqual(1,session.Snapshot.SavedDecks.Length);
        }

        [Test]
        public void スターター配布後の複数デッキ保存切替と再開は所持を増殖しない()
        {
            var session=new ClearReturnSession(Store());var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            Assert.IsTrue(session.InitializeStarter(cards));var original=session.Snapshot.SavedDecks.Single();
            Assert.IsTrue(session.Begin(Terminal(),"fixture",new ClearReturnSettings()));Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());
            var id=System.Guid.NewGuid().ToString("N");Assert.IsTrue(session.SaveDeck(id,"複製",original.Cards));Assert.IsTrue(session.EquipDeck(id));
            Assert.AreEqual(2,session.Snapshot.SavedDecks.Length);Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));
            session=new ClearReturnSession(Store());Assert.AreEqual(id,session.Snapshot.EquippedDeckId);Assert.IsFalse(session.InitializeStarter(cards));
            Assert.IsTrue(session.DeleteDeck(original.Id));Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));
        }

        private static CollectionRecipe[] CollectionRecipes()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();
            CollectionRecipe Recipe(string id,string a,string b)=>new CollectionRecipe(id,new[]{a,b},"fire_sword",
                materialCards:new[]{f.CreateById(a),f.CreateById(b)},resultCard:f.CreateById("fire_sword"));
            return new[]{Recipe("one","sword","bomb"),Recipe("special","shield","heal")};
        }
        [Test]
        public void 融合公開は結果詳細だけを許可し未知素材と未入手カード本体は隠す()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var recipes=CollectionRecipes();var session=new ClearReturnSession(Store(),recipes);
            Assert.IsTrue(CollectionDisplay.RecipeRows(session.Snapshot,recipes).All(r=>r.Result==null));
            Assert.IsTrue(session.RecordFusion("one"));var p=session.Snapshot;var rows=CollectionDisplay.RecipeRows(p,recipes);
            StringAssert.Contains("？？？ ＋ ？？？ → ファイアソード",rows[0].Text);
            Assert.AreEqual(60,rows[0].Result.Power);StringAssert.Contains("威力 60",rows[0].Performance);
            StringAssert.Contains("（？？？＋？？？の融合）",rows[0].Performance);
            Assert.IsNull(rows[1].Result);Assert.AreEqual("？？？　未公開レシピ",rows[1].Text);
            Assert.IsEmpty(p.Owned);Assert.IsEmpty(p.DiscoveredCards);Assert.AreEqual("？？？　未入手",CollectionDisplay.CardText(p,f.CreateById("fire_sword")));
            rows[0].Result.Modifier.BonusDamage=999;Assert.AreEqual(60,rows[0].Result.GetFinalPower());
            var resumed=new ClearReturnSession(Store(),recipes);Assert.IsTrue(resumed.EnsureCollection());
            Assert.AreEqual(60,CollectionDisplay.RecipeRows(resumed.Snapshot,recipes)[0].Result.Power);Assert.IsEmpty(resumed.Snapshot.DiscoveredCards);
        }
        [Test]
        public void 同ID同名別性能の入手履歴や融合結果では別定義レシピを公開しない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var actual=f.CreateById("fire_sword");
            var other=new Card(actual.Id,actual.Name,actual.Cost,actual.Rarity,actual.Category,actual.Element,actual.Series,999,actual.Description);
            var recipe=new CollectionRecipe("other",new[]{"sword","bomb"},other.Id,resultCard:other);
            var catalog=f.CollectionCards.Concat(new[]{other}).ToArray();
            var known=new DiscoveredCard{Id=actual.Id,Name=actual.Name,Level=actual.ExceedLevel,Rarity=actual.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(actual)};
            var p=new ClearReturnProfile{DiscoveredCards=new[]{known}};
            Assert.IsFalse(recipe.IsResultKnown(p,catalog));Assert.IsNull(CollectionDisplay.RecipeRows(p,new[]{recipe},catalog)[0].Result);
            Assert.IsFalse(CollectionRecipes()[0].Matches(new[]{f.CreateById("sword"),f.CreateById("bomb")},other));
            known.DefinitionHash=null;Assert.IsFalse(recipe.IsResultKnown(p,catalog));
            var materialRecipe=new CollectionRecipe("material",new[]{actual.Id,"bomb"},"sword",materialCards:new[]{other,f.CreateById("bomb")},resultCard:f.CreateById("sword"));
            p.RevealedRecipes=new[]{"material"};Assert.AreEqual("？？？",materialRecipe.MaterialName(p,0,catalog));
        }
        [Test]
        public void 結果定義のない旧レシピは公開履歴だけから性能を推測しない()
        {
            var old=new CollectionRecipe("legacy",new[]{"a","b"},"fire_sword");var p=new ClearReturnProfile{RevealedRecipes=new[]{"legacy"}};
            Assert.IsNull(CollectionDisplay.RecipeRows(p,new[]{old})[0].Result);
            Assert.AreEqual("公開済み：？？？ ＋ ？？？ → ？？？\n結果の詳細定義は未特定です。",CollectionDisplay.RecipeText(p,old));
            Assert.IsNull(CollectionDisplay.RecipeRows(p,new[]{old})[0].Performance);
            Assert.AreEqual("？？？　未公開レシピ",CollectionDisplay.RecipeText(new ClearReturnProfile(),old));
        }
        [Test]
        public void 図鑑はスターターを登録し未入手のカード性能とレシピ素材を公開しない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var session=new ClearReturnSession(Store(),CollectionRecipes());
            Assert.IsTrue(session.InitializeStarter(f.CreateStarterDeck().GetAllCards()));var p=session.Snapshot;
            Assert.AreEqual(5,p.DiscoveredCards.Length);Assert.IsEmpty(p.RevealedRecipes);
            Assert.AreEqual("？？？　未入手",CollectionDisplay.CardText(p,f.CreateById("fire_sword")));
            Assert.AreEqual("？？？　未公開レシピ",CollectionDisplay.RecipeText(p,CollectionRecipes()[0]));
            Assert.IsFalse(f.CollectionCards.Any(c=>c.ExceedLevel==ExceedLevel.Lv3));
        }
        [TestCase(false)][TestCase(true)]
        public void 図鑑は攻略取得や破棄履歴だけでは登録せず持帰り確定時だけ全レシピを公開する(bool carry)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var session=new ClearReturnSession(Store(),CollectionRecipes());
            Assert.IsTrue(session.Begin(Terminal(f.CreateById("fire_sword")),"collection",new ClearReturnSettings()));
            Assert.IsEmpty(session.Snapshot.DiscoveredCards);Assert.IsEmpty(session.Snapshot.RevealedRecipes);
            if(carry)Assert.IsTrue(session.Toggle(0));Assert.IsTrue(session.ConfirmSelection());
            Assert.AreEqual(carry?1:0,session.Snapshot.DiscoveredCards.Length);Assert.AreEqual(carry?2:0,session.Snapshot.RevealedRecipes.Length);
            if(carry){var text=CollectionDisplay.RecipeText(session.Snapshot,CollectionRecipes()[0]);StringAssert.Contains("？？？ ＋ ？？？",text);StringAssert.Contains("ファイアソード",text);}
        }
        [Test]
        public void 図鑑の融合実行取消と保存失敗再試行はカード入手と独立する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var io=new FaultIo{Before=true};var session=new ClearReturnSession(Store(io),CollectionRecipes());
            var sword=f.CreateById("sword");var bomb=f.CreateById("bomb");var deck=new Deck(new List<Card>{sword,bomb});
            var cs=new CustomScreenSystem(deck,fusionSystem:new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(),f));
            int events=0;cs.OnFusionExecuted+=(input,output)=>{events++;Assert.IsTrue(CollectionRecipes()[0].Matches(input,output));Assert.IsFalse(session.RecordFusion("one"));};
            cs.Open(new Card[0],new MockRandomProvider());Assert.IsTrue(cs.PreviewCustomization(new[]{sword,bomb}).CanExecute);Assert.AreEqual(0,events);
            Assert.IsTrue(cs.ExecuteFusion(new[]{sword,bomb}).IsSuccess);Assert.AreEqual(1,events);Assert.IsTrue(session.HasPendingSave);
            Assert.IsEmpty(session.Snapshot.RevealedRecipes??new string[0]);Assert.IsTrue(cs.TryUndoCustomization());Assert.AreEqual(2,cs.CurrentHand.Count);
            io.Before=false;Assert.IsTrue(session.Retry());Assert.AreEqual("one",session.Snapshot.RevealedRecipes.Single());Assert.IsEmpty(session.Snapshot.Owned);
            Assert.IsEmpty(session.Snapshot.DiscoveredCards);StringAssert.Contains("？？？ ＋ ？？？ → ファイアソード",CollectionDisplay.RecipeText(session.Snapshot,CollectionRecipes()[0]));
            Assert.AreEqual(60,CollectionDisplay.RecipeRows(session.Snapshot,CollectionRecipes())[0].Result.Power);
            session=new ClearReturnSession(Store(),CollectionRecipes());Assert.IsTrue(session.EnsureCollection());Assert.AreEqual("one",session.Snapshot.RevealedRecipes.Single());
        }
        [TestCase(false)][TestCase(true)]
        public void 図鑑の旧所持移行は資源不変で保存失敗を再試行し所持ゼロでも履歴を維持する(bool fail)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var card=f.CreateById("fire_sword");var store=Store();
            var old=new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),Tokens=17,
                Owned=new[]{new OwnedCardCount{Id=card.Id,Name=card.Name,Level=card.ExceedLevel,Rarity=card.Rarity,Count=1}}};
            Assert.IsTrue(store.TryWrite(0,old,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
            var io=new FaultIo{Before=fail};var session=new ClearReturnSession(Store(io),CollectionRecipes());Assert.AreEqual(!fail,session.EnsureCollection());
            if(fail){CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));Assert.AreEqual(17,session.Snapshot.Tokens);io.Before=false;Assert.IsTrue(session.Retry());}
            Assert.AreEqual(17,session.Snapshot.Tokens);Assert.AreEqual(1,session.Snapshot.Owned.Single().Count);Assert.AreEqual(2,session.Snapshot.RevealedRecipes.Length);
            var revision=session.Snapshot.Revision;Assert.IsTrue(session.EnsureCollection());Assert.AreEqual(revision,session.Snapshot.Revision);
            var p=session.Snapshot;p.Owned=new OwnedCardCount[0];p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(revision,p,out error),error);
            session=new ClearReturnSession(Store(),CollectionRecipes());Assert.IsTrue(session.EnsureCollection());Assert.AreEqual("ファイアソード",CollectionDisplay.Name(session.Snapshot,"fire_sword"));Assert.AreEqual(17,session.Snapshot.Tokens);
        }
        [Test]
        public void 図鑑の購入登録は支払と同じ保存で確定し再受領では増えない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var store=Store();Assert.IsTrue(store.TryWrite(0,new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),Tokens=10},out var error),error);
            var session=new ClearReturnSession(store,CollectionRecipes());Assert.IsTrue(session.EnsureExchange(new[]{new TokenExchangeProduct("fire",f.CreateById("fire_sword"),3,1)},new TokenExchangeProduct[0],0,new MockRandomProvider()));
            var round=session.Snapshot.ExchangeStock.RoundId;var request=System.Guid.NewGuid().ToString("N");Assert.IsTrue(session.PurchaseExchange(request,round,"fire"));
            Assert.AreEqual(7,session.Snapshot.Tokens);Assert.AreEqual(1,session.Snapshot.DiscoveredCards.Length);Assert.AreEqual(2,session.Snapshot.RevealedRecipes.Length);
            Assert.IsTrue(session.PurchaseExchange(request,round,"fire"));Assert.AreEqual(7,session.Snapshot.Tokens);Assert.AreEqual(1,session.Snapshot.Owned.Single().Count);
        }
        [Test]
        public void 図鑑統合は既存18定義を列挙し個体複製やショップ同一定義で枠を増やさない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var defs=f.CollectionCards;
            Assert.AreEqual(18,defs.Count);Assert.AreEqual(10,CardRewardSystem.FallbackDefinitions.Count);
            Assert.AreEqual(18,defs.Select(CollectionDisplay.DefinitionHash).Distinct().Count());
            var session=new ClearReturnSession(Store(),new Olca.Application.Battle.SimpleFusionRecipeProvider(f).CollectionRecipes,defs);
            Assert.IsTrue(session.InitializeStarter(f.CreateStarterDeck().GetAllCards()));
            Assert.AreEqual(18,CollectionDisplay.CardRows(session.Snapshot,defs.Concat(defs.Select(c=>c.Clone())).ToArray()).Length);
            var normal=CardRewardSystem.CreateFallbackCard("sword",CardRarity.Normal);var limited=CardRewardSystem.CreateFallbackCard("sword",CardRarity.Limited);
            Assert.AreEqual("？？？　未入手",CollectionDisplay.CardText(session.Snapshot,normal,defs));
            Assert.AreEqual("？？？　未入手",CollectionDisplay.CardText(session.Snapshot,limited,defs));
            Assert.IsFalse(CollectionDisplay.MetadataKnown(new ClearReturnProfile {DiscoveredCards=new[]{new DiscoveredCard{Id="sword",Name=normal.Name,Rarity=normal.Rarity,Level=normal.ExceedLevel}}},f.CreateById("sword")));
        }
        [TestCase(CardRarity.Normal)][TestCase(CardRarity.Limited)]
        public void 図鑑統合は実際に持帰った報酬定義だけを公開し同ID別レギュレーションを隠す(CardRarity rarity)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var card=CardRewardSystem.CreateFallbackCard("heal",rarity);
            var session=new ClearReturnSession(Store(),new Olca.Application.Battle.SimpleFusionRecipeProvider(f).CollectionRecipes,f.CollectionCards);
            Assert.IsTrue(session.Begin(Terminal(card),"catalog",new ClearReturnSettings()));Assert.IsTrue(session.Toggle(0));Assert.IsTrue(session.ConfirmSelection());
            Assert.IsTrue(CollectionDisplay.IsKnown(session.Snapshot,card,f.CollectionCards));
            Assert.IsFalse(CollectionDisplay.IsKnown(session.Snapshot,f.CreateById("heal"),f.CollectionCards));
            var other=CardRewardSystem.CreateFallbackCard("heal",rarity==CardRarity.Normal?CardRarity.Limited:CardRarity.Normal);
            Assert.IsFalse(CollectionDisplay.IsKnown(session.Snapshot,other,f.CollectionCards));
            Assert.AreEqual(CollectionDisplay.DefinitionHash(card),session.Snapshot.DiscoveredCards.Single().DefinitionHash);
            session=new ClearReturnSession(Store(),new Olca.Application.Battle.SimpleFusionRecipeProvider(f).CollectionRecipes,f.CollectionCards);
            Assert.IsTrue(session.EnsureCollection());Assert.IsTrue(CollectionDisplay.IsKnown(session.Snapshot,card,f.CollectionCards));
        }
        [Test]
        public void 図鑑統合は同名異性能の旧履歴を推測公開せず実入手した定義だけを識別する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var a=f.CreateById("sword");var b=new Card(a.Id,a.Name,a.Cost,a.Rarity,a.Category,a.Element,a.Series,99,a.Description);
            var defs=new[]{a,b};var p=new ClearReturnProfile{DiscoveredCards=new[]{new DiscoveredCard{Id=a.Id,Name=a.Name,Level=a.ExceedLevel,Rarity=a.Rarity}}};
            Assert.IsFalse(CollectionDisplay.IsKnown(p,a,defs));Assert.IsFalse(CollectionDisplay.IsKnown(p,b,defs));
            Assert.AreEqual(3,CollectionDisplay.CardRows(p,defs).Length);
            p.DiscoveredCards[0].DefinitionHash=CollectionDisplay.DefinitionHash(a);Assert.IsTrue(CollectionDisplay.IsKnown(p,a,defs));Assert.IsFalse(CollectionDisplay.IsKnown(p,b,defs));
            p.DiscoveredCards=p.DiscoveredCards.Concat(new[]{new DiscoveredCard{Id=b.Id,Name=b.Name,Level=b.ExceedLevel,Rarity=b.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(b)}}).ToArray();
            Assert.DoesNotThrow(()=>ClearReturnProfile.Validate(p));Assert.AreEqual(2,CollectionDisplay.CardRows(p,defs.Concat(defs).ToArray()).Length);
        }
        [Test]
        public void 図鑑統合は旧履歴の一意な定義だけを補完し繰返しや超過変換で別定義を公開しない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var store=Store();var old=new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),
                Owned=new[]{new OwnedCardCount{Id="heal",Name="ヒール",Rarity=CardRarity.Normal,Level=ExceedLevel.Normal,Count=4}},
                DiscoveredCards=new[]{new DiscoveredCard{Id="heal",Name="ヒール",Rarity=CardRarity.Normal,Level=ExceedLevel.Normal}}};
            Assert.IsTrue(store.TryWrite(0,old,out var error),error);var session=new ClearReturnSession(store,new Olca.Application.Battle.SimpleFusionRecipeProvider(f).CollectionRecipes,f.CollectionCards);
            Assert.IsTrue(session.EnsureCollection());Assert.IsNotNull(session.Snapshot.DiscoveredCards.Single().DefinitionHash);var revision=session.Snapshot.Revision;
            Assert.IsTrue(session.EnsureCollection());Assert.AreEqual(revision,session.Snapshot.Revision);
            var fallback=CardRewardSystem.CreateFallbackCard("heal",CardRarity.Normal);Assert.IsTrue(session.Begin(Terminal(fallback),"overflow",new ClearReturnSettings()));
            Assert.IsTrue(session.Toggle(0));Assert.IsTrue(session.ConfirmSelection());Assert.AreEqual(2,session.Snapshot.Owned.Length);Assert.AreEqual(4,session.Snapshot.Owned.Single(o=>o.Name=="ヒール").Count);Assert.AreEqual(1,session.Snapshot.Owned.Single(o=>o.Name=="heal_card").Count);Assert.AreEqual(0,session.Snapshot.Tokens);
            Assert.IsTrue(CollectionDisplay.IsKnown(session.Snapshot,fallback,f.CollectionCards));
        }
        [TestCase(false)] [TestCase(true)]
        public void hash欄省略の旧図鑑封筒は一意候補だけ正規化し再読込と再確認で増分保存しない(bool ambiguous)
        {
            var a=new Card("shared","おなじナマエ",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,17,"カクニンA");var b=new Card(a.Id,a.Name,a.Cost,a.Rarity,a.Category,a.Element,a.Series,71,"ヒミツB");var initial=new ClearReturnProfile{Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),DiscoveredCards=new[]{new DiscoveredCard{Id=a.Id,Name=a.Name,Level=a.ExceedLevel,Rarity=a.Rarity}}};
            string raw=System.Text.Encoding.UTF8.GetString(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(initial));StringAssert.Contains("\"DefinitionHash\":null",raw);raw=raw.Replace("\"DefinitionHash\":null,","").Replace(",\"DefinitionHash\":null","");StringAssert.DoesNotContain("DefinitionHash",raw);var payload=System.Text.Encoding.UTF8.GetBytes(raw);string before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(payload);string envelope="{\"Version\":1,\"Payload\":"+System.Text.Encoding.UTF8.GetString(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(raw))+",\"Sha256\":\""+before+"\"}";var loaded=Olca.Core.Logic.Dungeon.PortableCheckpointJson.DecodeProgress(System.Text.Encoding.UTF8.GetBytes(envelope));Assert.IsNull(loaded.DiscoveredCards.Single().DefinitionHash);
            var store=new Olca.Core.Logic.Dungeon.MemoryClearReturnStore(loaded);var catalog=ambiguous?new[]{a,b}:new[]{a};var session=new ClearReturnSession(store,definitions:catalog);Assert.IsTrue(session.EnsureCollection(),session.Error);Assert.AreEqual(ambiguous?1:2,session.Snapshot.Revision);Assert.AreEqual(ambiguous?null:CollectionDisplay.DefinitionHash(a),session.Snapshot.DiscoveredCards.Single().DefinitionHash);Assert.AreEqual(a.Name,session.Snapshot.DiscoveredCards.Single().Name);string saved=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Snapshot));long revision=session.Snapshot.Revision;
            Assert.IsTrue(session.EnsureCollection());Assert.AreEqual(revision,session.Snapshot.Revision);Assert.AreEqual(saved,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Snapshot)));var resumed=new ClearReturnSession(store,definitions:catalog);Assert.IsTrue(resumed.EnsureCollection());Assert.AreEqual(revision,resumed.Snapshot.Revision);var roundtrip=Olca.Core.Logic.Dungeon.PortableCheckpointJson.DecodeProgress(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(resumed.Snapshot));Assert.AreEqual(saved,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(roundtrip)));Assert.AreEqual(!ambiguous,CollectionDisplay.IsKnown(roundtrip,a,catalog));if(ambiguous)Assert.IsFalse(CollectionDisplay.IsKnown(roundtrip,b,catalog));Assert.IsNull(loaded.DiscoveredCards.Single().DefinitionHash);Assert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(payload));
        }
        [Test]
        public void 図鑑統合は現カタログから外れた同名旧定義の履歴を別定義の既知判定で消さない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var a=f.CreateById("sword");var old=new Card(a.Id,a.Name,a.Cost,a.Rarity,a.Category,a.Element,a.Series,99,a.Description);
            var p=new ClearReturnProfile{DiscoveredCards=new[]{
                new DiscoveredCard{Id=a.Id,Name=a.Name,Level=a.ExceedLevel,Rarity=a.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(a)},
                new DiscoveredCard{Id=old.Id,Name=old.Name,Level=old.ExceedLevel,Rarity=old.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(old)}}};
            var rows=CollectionDisplay.CardRows(p,new[]{a,a.Clone()});Assert.AreEqual(2,rows.Length);StringAssert.Contains("詳細定義未特定",rows[1]);
            Assert.IsTrue(CollectionDisplay.IsKnown(p,a,new[]{a}));
        }
        [Test]
        public void 定義別所持は同IDの回復と報酬攻撃を混ぜず旧デッキと新編成を保持する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var defs=f.CollectionCards;var store=Store();var session=new ClearReturnSession(store,new Olca.Application.Battle.SimpleFusionRecipeProvider(f).CollectionRecipes,defs);
            Assert.IsTrue(session.InitializeStarter(f.CreateStarterDeck().GetAllCards()));var original=session.Snapshot.SavedDecks.Single().Copy();
            var reward=CardRewardSystem.CreateFallbackCard("heal",CardRarity.Normal);Assert.IsTrue(session.Begin(Terminal(reward),"owned-def",new ClearReturnSettings()));Assert.IsTrue(session.Toggle(0));Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());
            Assert.AreEqual(21,session.Snapshot.Owned.Sum(o=>o.Count));Assert.AreEqual(0,session.Snapshot.Tokens);
            Assert.AreEqual(4,session.Snapshot.Owned.Single(o=>o.Name=="ヒール").Count);Assert.AreEqual(1,session.Snapshot.Owned.Single(o=>o.Name=="heal_card").Count);
            var slots=original.Cards.Select(c=>c.Copy()).ToArray();slots[System.Array.FindIndex(slots,c=>c.Id=="heal")].DefinitionKey=CollectionDisplay.DefinitionHash(reward);
            var id=System.Guid.NewGuid().ToString("N");Assert.IsTrue(session.SaveDeck(id,"報酬混在",slots),session.Error);Assert.IsTrue(session.EquipDeck(id),session.Error);
            CollectionAssert.AreEqual(original.Cards.Select(c=>c.DefinitionKey),session.Snapshot.SavedDecks.Single(d=>d.Id==original.Id).Cards.Select(c=>c.DefinitionKey));
            var p=session.Snapshot;long revision=p.Revision;p.Owned=p.Owned.Reverse().ToArray();p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(revision,p,out var error),error);
            session=new ClearReturnSession(store,definitions:defs);var saved=session.Snapshot.SavedDecks.Single(d=>d.Id==id);var actual=saved.Cards.Select(slot=>defs.Single(c=>CollectionDisplay.DefinitionHash(c)==slot.DefinitionKey)).ToArray();
            Assert.IsTrue(OwnedDefinition.TryCreate(session.Snapshot,actual,saved.Cards,out var deck,out error),error);
            Assert.AreEqual(3,deck.GetAllCards().Count(c=>c.Name=="ヒール"));Assert.AreEqual(1,deck.GetAllCards().Count(c=>c.Name=="heal_card"));Assert.AreEqual(20,deck.GetAllCards().Distinct().Count());
            slots[System.Array.FindIndex(slots,c=>c.Id=="heal"&&c.DefinitionKey!=CollectionDisplay.DefinitionHash(reward))].DefinitionKey=CollectionDisplay.DefinitionHash(reward);
            Assert.IsFalse(SavedDepartureDeck.TryValidateEquipped(session.Snapshot,new SavedDepartureDeck{Cards=slots},out error));
        }
        [Test]
        public void 定義別所持は同名異性能と上位段階を別スロットで解決し別定義の差替えを拒否する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var a=f.CreateById("sword");var b=new Card(a.Id,a.Name,a.Cost,a.Rarity,a.Category,a.Element,a.Series,99,a.Description);var upper=f.CreateExceeded(a,ExceedLevel.Lv1);
            var defs=f.CollectionCards.Concat(new[]{b}).ToArray();var store=Store();var session=new ClearReturnSession(store,definitions:defs);Assert.IsTrue(session.InitializeStarter(f.CreateStarterDeck().GetAllCards()));
            var p=session.Snapshot;long rev=p.Revision;p.Owned=p.Owned.Concat(new[]{b,upper}.Select(c=>new OwnedCardCount{Id=c.Id,Name=c.Name,Rarity=c.Rarity,Level=c.ExceedLevel,Count=1,DefinitionHash=CollectionDisplay.DefinitionHash(c)})).ToArray();p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(rev,p,out var error),error);Assert.IsTrue(session.Reload());
            var slots=p.SavedDecks.Single().Cards.Select(c=>c.Copy()).ToArray();var indices=slots.Select((c,n)=>new{c,n}).Where(x=>x.c.Id=="sword").Select(x=>x.n).ToArray();slots[indices[0]].DefinitionKey=CollectionDisplay.DefinitionHash(b);slots[indices[1]]=new SavedDeckCard{Id=upper.Id,Level=upper.ExceedLevel,DefinitionKey=CollectionDisplay.DefinitionHash(upper)};
            Assert.IsTrue(SavedDepartureDeck.TryValidateEquipped(session.Snapshot,new SavedDepartureDeck{Cards=slots},out error),error);
            var cards=slots.Select(slot=>defs.Single(c=>CollectionDisplay.DefinitionHash(c)==slot.DefinitionKey)).ToArray();Assert.IsTrue(OwnedDefinition.TryCreate(session.Snapshot,cards,slots,out var deck,out error),error);Assert.AreEqual(1,deck.GetAllCards().Count(c=>c.Power==99));Assert.AreEqual(1,deck.GetAllCards().Count(c=>c.ExceedLevel==ExceedLevel.Lv1));
            cards[indices[0]]=a;Assert.IsFalse(OwnedDefinition.TryCreate(session.Snapshot,cards,slots,out _,out error));Assert.AreEqual("ソード★　Normal",OwnedDefinition.DisplayName(p.Owned.Single(o=>o.Level==ExceedLevel.Lv1)));
        }
        [Test]
        public void 定義別所持の旧20枚は一意補完と新別定義の受領を同時保存し失敗再試行でも枚数不変()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var store=Store();var seed=new ClearReturnSession(store);Assert.IsTrue(seed.InitializeStarter(f.CreateStarterDeck().GetAllCards()));var p=seed.Snapshot;long rev=p.Revision;
            foreach(var o in p.Owned)o.DefinitionHash=null;foreach(var slot in p.SavedDecks.Single().Cards)slot.DefinitionKey=null;p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(rev,p,out var error),error);string deckId=p.EquippedDeckId;
            var io=new FaultIo();var session=new ClearReturnSession(Store(io),definitions:f.CollectionCards);var reward=CardRewardSystem.CreateFallbackCard("heal",CardRarity.Normal);Assert.IsTrue(session.Begin(Terminal(reward),"legacy-owned",new ClearReturnSettings()));Assert.IsTrue(session.Toggle(0));
            var before=System.IO.File.ReadAllBytes(store.ActivePath);io.Before=true;Assert.IsFalse(session.ConfirmSelection());Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(store.ActivePath));
            io.Before=false;Assert.IsTrue(session.Retry());Assert.AreEqual(21,session.Snapshot.Owned.Sum(o=>o.Count));Assert.AreEqual(deckId,session.Snapshot.EquippedDeckId);Assert.IsTrue(session.Snapshot.SavedDecks.Single().Cards.All(slot=>slot.DefinitionKey!=null));
            Assert.IsTrue(SavedDepartureDeck.TryValidateEquipped(session.Snapshot,session.Snapshot.SavedDecks.Single(),out error),error);Assert.IsFalse(session.HasPendingSave);
        }
        [Test]
        public void 定義別所持は曖昧な旧行を分割せず旧スロットを安定キーへ固定する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var a=f.CreateById("sword");var b=new Card(a.Id,a.Name,a.Cost,a.Rarity,a.Category,a.Element,a.Series,99,a.Description);var defs=f.CollectionCards.Concat(new[]{b}).ToArray();
            var store=Store();var session=new ClearReturnSession(store);Assert.IsTrue(session.InitializeStarter(f.CreateStarterDeck().GetAllCards()));var p=session.Snapshot;long rev=p.Revision;foreach(var o in p.Owned)o.DefinitionHash=null;foreach(var c in p.SavedDecks[0].Cards)c.DefinitionKey=null;p.Revision++;p.CommitId=System.Guid.NewGuid().ToString("N");Assert.IsTrue(store.TryWrite(rev,p,out var error),error);
            session=new ClearReturnSession(store,definitions:defs);Assert.IsTrue(session.EnsureOwnedDefinitions());Assert.IsTrue(session.EnsureCollection());var legacy=session.Snapshot.Owned.Single(o=>o.Id=="sword");Assert.IsNull(legacy.DefinitionHash);Assert.AreEqual(4,legacy.Count);
            Assert.IsTrue(session.Snapshot.SavedDecks[0].Cards.Where(c=>c.Id=="sword").All(c=>c.DefinitionKey==OwnedDefinition.Key(legacy)));Assert.AreEqual(20,session.Snapshot.Owned.Sum(o=>o.Count));
            session=new ClearReturnSession(store,definitions:defs);Assert.AreEqual(4,OwnedDefinition.Resolve(session.Snapshot,session.Snapshot.SavedDecks[0].Cards.First(c=>c.Id=="sword")).Count);
        }
        private Olca.Unity.ClearReturnFileStore Store(FaultIo io = null) => new Olca.Unity.ClearReturnFileStore(directory, io);
        [Test]
        public void 複数レベルで節目を越える経験値は保存再試行と再開で二重成長せず帰還後だけ反映する()
        {
            var io=new FaultIo();var source=Terminal();var flow=new ClearReturnSession(Store(io));Assert.IsTrue(flow.Begin(source,"growth-fixture",new ClearReturnSettings{FirstClearExperience=900}));io.Before=true;Assert.IsFalse(flow.ConfirmSelection());Assert.AreEqual(1,flow.Snapshot.Level);Assert.AreEqual(0,flow.Snapshot.Experience);Assert.IsNull(source.Player.AttackGrowth);
            io.Before=false;Assert.IsTrue(flow.Retry());flow=new ClearReturnSession(Store());Assert.AreEqual(10,flow.Snapshot.Level);Assert.AreEqual(900,flow.Snapshot.Experience);Assert.IsTrue(flow.ConfirmReturn());Assert.AreEqual(19,flow.ReturnedPlayer.AttackPower);Assert.AreEqual(48,flow.ReturnedPlayer.AttackGrowth.ChargeFrames(60));Assert.AreEqual(10,flow.ReturnedPlayer.AttackGrowth.IntervalFrames(12));
            flow=new ClearReturnSession(Store());Assert.AreEqual(900,flow.Snapshot.Experience);Assert.AreEqual(10,flow.ReturnedPlayer.AttackGrowth.Level);flow.ConfirmReturn();Assert.AreEqual(900,flow.Snapshot.Experience);Assert.AreEqual(48,flow.ReturnedPlayer.AttackGrowth.ChargeFrames(60));
        }
        [TestCase("begin")] [TestCase("toggle")] [TestCase("award")] [TestCase("return")]
        public void 各保存段階の失敗は現状態とファイルを保ち同一計画を再試行して一度だけ受領する(string step)
        {
            var io = new FaultIo(); var store = Store(io); var flow = new ClearReturnSession(store); var source = Terminal(Card("a"));
            if (step != "begin") Assert.IsTrue(flow.Begin(source, "dungeon", new ClearReturnSettings()), flow.Error);
            if (step == "award" || step == "return") Assert.IsTrue(flow.Toggle(0), flow.Error);
            if (step == "return") Assert.IsTrue(flow.ConfirmSelection(), flow.Error);
            var before = flow.Snapshot;
            var bytes = System.IO.File.Exists(store.ActivePath) ? System.IO.File.ReadAllBytes(store.ActivePath) : null;
            io.Before = true;
            bool saved = step == "begin" ? flow.Begin(source, "dungeon", new ClearReturnSettings()) : step == "toggle" ? flow.Toggle(0) : step == "award" ? flow.ConfirmSelection() : flow.ConfirmReturn();
            Assert.IsFalse(saved); Assert.IsTrue(flow.HasPendingSave); Assert.AreEqual(before.Revision, flow.Snapshot.Revision);
            Assert.AreEqual(before.Experience, flow.Snapshot.Experience); Assert.AreEqual(before.Owned.Length, flow.Snapshot.Owned.Length);
            if (bytes == null) Assert.IsFalse(System.IO.File.Exists(store.ActivePath)); else CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
            var interrupted = new ClearReturnSession(Store()); Assert.AreEqual(before.Revision, interrupted.Snapshot.Revision);
            Assert.AreEqual(before.Experience, interrupted.Snapshot.Experience);
            Assert.IsFalse(flow.Toggle(0), "未保存計画に別の操作を混ぜない");
            io.Before = false; io.During = () => Assert.IsFalse(flow.ConfirmSelection(), "保存コールバックの再入を拒否する");
            Assert.IsTrue(flow.Retry(), flow.Error); io.During = null;
            if (step == "begin") Assert.IsTrue(flow.Toggle(0));
            Assert.IsTrue(flow.ConfirmSelection(), flow.Error); Assert.IsTrue(flow.ConfirmReturn(), flow.Error);
            var committed = System.IO.File.ReadAllBytes(store.ActivePath);
            Assert.IsTrue(flow.Begin(source, "dungeon", new ClearReturnSettings())); Assert.IsTrue(flow.ConfirmSelection()); Assert.IsTrue(flow.ConfirmReturn());
            CollectionAssert.AreEqual(committed, System.IO.File.ReadAllBytes(store.ActivePath));
            var restarted = new ClearReturnSession(Store());
            Assert.AreEqual(100, restarted.Snapshot.Experience); Assert.AreEqual(2, restarted.Snapshot.Level);
            Assert.AreEqual(1, restarted.Snapshot.Owned.Single().Count); Assert.AreEqual(1, restarted.Snapshot.Receipts.Length);
            Assert.AreEqual(105, restarted.ReturnedPlayer.CurrentHp); Assert.AreEqual(11, restarted.ReturnedPlayer.AttackPower);
            Assert.IsFalse(restarted.ReturnedPlayer.IsInvincible); Assert.IsFalse(restarted.ReturnedPlayer.IsActionPrevented);
        }
        [TestCase(false)] [TestCase(true)]
        public void 初回移動と既存置換の直後にOSエラーが返っても確定内容を照合して二重計上しない(bool existing)
        {
            var io = new FaultIo(); var flow = new ClearReturnSession(Store(io)); var source = Terminal(Card("a"));
            if (existing) Assert.IsTrue(flow.Begin(source, "dungeon", new ClearReturnSettings()));
            io.After = true;
            Assert.IsTrue(existing ? flow.Toggle(0) : flow.Begin(source, "dungeon", new ClearReturnSettings()), flow.Error);
            io.After = false; if (!existing) Assert.IsTrue(flow.Toggle(0));
            io.After = true; Assert.IsTrue(flow.ConfirmSelection(), flow.Error); Assert.IsTrue(flow.ConfirmReturn(), flow.Error);
            Assert.AreEqual(100, new ClearReturnSession(Store()).Snapshot.Experience);
            Assert.AreEqual(1, new ClearReturnSession(Store()).Snapshot.Owned.Single().Count);
        }
        [Test]
        public void 選択途中と経験値反映後と帰還後を再開し候補個体と制限と受領結果を保持する()
        {
            var source = Terminal(Card("a"), Card("a"), Card("forbidden", level: ExceedLevel.Lv3), Card("b"), Card("c"), Card("d"));
            var original = source.Snapshot; var flow = new ClearReturnSession(Store());
            Assert.IsTrue(flow.Begin(source, "dungeon", new ClearReturnSettings())); Assert.IsTrue(flow.Toggle(0));
            flow = new ClearReturnSession(Store()); CollectionAssert.AreEqual(new[] { 0 }, flow.Snapshot.Flow.Selected);
            CollectionAssert.AreEqual(original.ObtainedOrder, flow.Snapshot.Flow.Candidates.Select(c => c.InstanceId));
            Assert.IsFalse(flow.Toggle(1), "同一カードを2枚選べない"); Assert.IsFalse(flow.Toggle(2), "★★★は不可");
            Assert.IsTrue(flow.Toggle(3)); Assert.IsTrue(flow.Toggle(4)); Assert.IsFalse(flow.Toggle(5), "4枚目は不可");
            Assert.IsTrue(flow.ConfirmSelection(), flow.Error); flow = new ClearReturnSession(Store());
            Assert.AreEqual(ClearReturnStage.Awarded, flow.Snapshot.Flow.Stage); Assert.AreEqual(100, flow.Snapshot.Experience); Assert.IsNull(flow.ReturnedPlayer);
            Assert.IsTrue(flow.ConfirmSelection()); Assert.AreEqual(3, flow.Snapshot.Owned.Length);
            Assert.IsTrue(flow.ConfirmReturn()); flow = new ClearReturnSession(Store()); Assert.AreEqual(ClearReturnStage.Returned, flow.Snapshot.Flow.Stage);
            CollectionAssert.AreEqual(original.DeckOrder, source.Snapshot.DeckOrder); CollectionAssert.AreEqual(original.ObtainedOrder, source.Snapshot.ObtainedOrder);
        }
        [TestCase(CardRarity.Normal, ExceedLevel.Normal, 1)] [TestCase(CardRarity.Limited, ExceedLevel.Lv1, 4)] [TestCase(CardRarity.Ultimate, ExceedLevel.Lv2, 9)]
        public void 再クリア経験値と所持4枚上限とレギュレーション段階別超過変換を確定する(CardRarity rarity, ExceedLevel level, int tokens)
        {
            var flow = new ClearReturnSession(Store()); RestoredCheckpoint first = null;
            for (int run = 0; run < 5; run++) {
                var source = Terminal(Card("a", rarity, level)); if (first == null) first = source;
                Assert.IsTrue(flow.Begin(source, "dungeon", new ClearReturnSettings()), flow.Error);
                Assert.AreEqual(run == 0 ? 100 : 50, flow.Snapshot.Flow.ExperienceAward);
                Assert.IsTrue(flow.Toggle(0)); Assert.IsTrue(flow.ConfirmSelection()); Assert.IsTrue(flow.ConfirmReturn());
                flow = new ClearReturnSession(Store());
            }
            Assert.AreEqual(300, flow.Snapshot.Experience); Assert.AreEqual(4, flow.Snapshot.Level);
            Assert.AreEqual(4, flow.Snapshot.Owned.Single().Count); Assert.AreEqual(tokens, flow.Snapshot.Tokens);
            Assert.AreEqual(5, flow.Snapshot.Receipts.Length); Assert.IsTrue(flow.Begin(first, "dungeon", new ClearReturnSettings()));
            Assert.AreEqual(300, flow.Snapshot.Experience); Assert.AreEqual(tokens, flow.Snapshot.Tokens);
        }
        [Test]
        public void ゼロ枚でも確定でき別保存世代と破損データは上書きせず拒否する()
        {
            var a = new ClearReturnSession(Store()); Assert.IsTrue(a.Begin(Terminal(Card("a")), "dungeon", new ClearReturnSettings()));
            var stale = new ClearReturnSession(Store()); Assert.IsTrue(a.Toggle(0)); Assert.IsFalse(stale.ConfirmSelection());
            Assert.AreEqual(0, new ClearReturnSession(Store()).Snapshot.Experience);
            var fresh = new ClearReturnSession(Store()); Assert.IsTrue(fresh.Toggle(0)); Assert.IsTrue(fresh.ConfirmSelection()); Assert.IsTrue(fresh.ConfirmReturn());
            Assert.AreEqual(0, fresh.Snapshot.Owned.Length); Assert.AreEqual(100, fresh.Snapshot.Experience);
            System.IO.File.WriteAllText(Store().ActivePath, "broken"); var broken = System.IO.File.ReadAllBytes(Store().ActivePath);
            var blocked = new ClearReturnSession(Store()); Assert.IsFalse(blocked.Ready); Assert.IsFalse(blocked.ConfirmSelection());
            Assert.IsFalse(fresh.Begin(Terminal(Card("b")), "dungeon", new ClearReturnSettings()));
            CollectionAssert.AreEqual(broken, System.IO.File.ReadAllBytes(Store().ActivePath));
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class BasicAttackProvisionalTests
    {
        private Olca.Core.Logic.Battle.BattleSystem battle;
        private Olca.Application.Battle.BasicAttackInputState state;
        private Enemy target;
        private void SetupBattle(int x=2,int y=0) {
            target=new Enemy("target","target",200,ElementType.None,new Vector2Int(x,y));
            var p=new PlayerCharacter(500,new Vector2Int(1,0),attackPower:11,staggerDurationFrames:3);
            battle=new Olca.Core.Logic.Battle.BattleSystem(p,new List<Enemy>{target,new Enemy("keeper","keeper",200,ElementType.None,new Vector2Int(5,0))},new Deck());
            state=new Olca.Application.Battle.BasicAttackInputState();
        }
        private void Step(LogicalInput input=null)=>state.Tick(battle,input??new LogicalInput(),60,12);
        private static LogicalInput Tap(bool card=false)=>new LogicalInput{BasicAttackButtonDown=true,BasicAttackButtonUp=true,CardUseButtonDown=card};
        private void SetupTiming()
        { SetupBattle();battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(3,12,6));foreach(var e in battle.Enemies)e.ApplyStun(60000); }
        private void TimingStep(LogicalInput input=null)
        {
            var value=input??new LogicalInput();battle.RunLogicalFrame(()=>{
                Step(value);if(value.CardUseButtonDown)battle.CardUsage.TryUseCurrentCardAsync().Forget();battle.TickFrame(17,value);
            });
        }
        private static Card TimedCard(string id, CardModifier modifier=null)=>new Card(id,id,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,20,"検証",Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier);
        [Test]
        public void 固有攻撃の前隙で未発生かつ後隙で移動を止め窓以降だけ新前隙へ入る()
        {
            SetupTiming();TimingStep(Tap());Assert.AreEqual(200,target.CurrentHp);Assert.AreEqual(PlayerAttackPhase.Startup,battle.PlayerActionPhase);
            TimingStep();TimingStep();Assert.AreEqual(189,target.CurrentHp);Assert.AreEqual(PlayerAttackPhase.Recovery,battle.PlayerActionPhase);
            TimingStep(new LogicalInput{Horizontal=-1});Assert.AreEqual(1,battle.Player.GridPosition.X);Assert.IsFalse(battle.PlayerMovement.CanMove);
            for(int i=0;i<4;i++)TimingStep(Tap());Assert.AreEqual(189,target.CurrentHp);Assert.IsFalse(battle.CanCancelPlayerRecovery);
            TimingStep();Assert.IsTrue(battle.CanCancelPlayerRecovery);Assert.IsFalse(battle.TryPlayerBasicAttackForward(),"即時互換APIから動作を飛び越えない");TimingStep(Tap());Assert.AreEqual(PlayerAttackPhase.Startup,battle.PlayerActionPhase);
            Assert.AreEqual(189,target.CurrentHp);TimingStep();TimingStep();Assert.AreEqual(178,target.CurrentHp);
            for(int i=0;i<15;i++)TimingStep();Assert.IsFalse(battle.HasPlayerAction);Assert.AreEqual(178,target.CurrentHp,"拒否した連打を後から予約しない");
        }
        [Test]
        public void カード後隙から固有攻撃へ移ると旧カードを一度消費し新前隙を最初から数える()
        {
            SetupTiming();var c=TimedCard("a");battle.ConfigureCardAttackTiming("a",new PlayerAttackTiming(2,10,3));battle.CardUsage.EnqueueCards(new List<Card>{c,TimedCard("b")});
            int used=0;battle.CardUsage.OnCardUsed+=_=>used++;var task=battle.CardUsage.TryUseCurrentCardAsync().Preserve();
            TimingStep();Assert.AreEqual(200,target.CurrentHp);TimingStep();Assert.AreEqual(180,target.CurrentHp);Assert.AreEqual(PlayerAttackPhase.Recovery,battle.PlayerActionPhase);
            TimingStep(Tap());Assert.AreEqual(180,target.CurrentHp);Assert.AreEqual(0,used);TimingStep();TimingStep();Assert.IsTrue(battle.CanCancelPlayerRecovery);
            TimingStep(Tap());Assert.IsTrue(task.GetAwaiter().GetResult());Assert.AreEqual(1,used);Assert.IsNull(battle.CardUsage.InFlightCard);Assert.AreEqual(PlayerAttackPhase.Startup,battle.PlayerActionPhase);
            TimingStep();TimingStep();Assert.AreEqual(169,target.CurrentHp);Assert.AreEqual(1,battle.CardUsage.RemainingCount);Assert.AreEqual(1,used);
        }
        [Test]
        public void 固有後隙とカード後隙のキャンセル窓で同時入力はカードを優先する()
        {
            SetupTiming();foreach(var id in new[]{"a","b"})battle.ConfigureCardAttackTiming(id,new PlayerAttackTiming(2,10,3));battle.CardUsage.EnqueueCards(new List<Card>{TimedCard("a"),TimedCard("b")});
            TimingStep(Tap());TimingStep();TimingStep();for(int i=0;i<6;i++)TimingStep();Assert.IsTrue(battle.CanCancelPlayerRecovery);
            TimingStep(Tap(true));Assert.AreEqual(PlayerAttackPhase.Startup,battle.PlayerActionPhase);Assert.AreEqual("a",battle.CardUsage.InFlightCard.Id);TimingStep();Assert.AreEqual(169,target.CurrentHp);
            for(int i=0;i<3;i++)TimingStep();TimingStep(Tap(true));Assert.AreEqual("b",battle.CardUsage.InFlightCard.Id);Assert.AreEqual(PlayerAttackPhase.Startup,battle.PlayerActionPhase);TimingStep();Assert.AreEqual(149,target.CurrentHp);
            for(int i=0;i<12;i++)TimingStep();Assert.AreEqual(149,target.CurrentHp);Assert.IsFalse(battle.HasPlayerAction);
        }
        [Test]
        public void 前隙と後隙は停止復帰で二重経過せず麻痺や変身切替の旧前隙を発生させない()
        {
            SetupTiming();TimingStep(Tap());battle.Pause(BattlePauseReason.User);for(int i=0;i<10;i++)TimingStep();Assert.AreEqual(200,target.CurrentHp);
            battle.Resume(BattlePauseReason.User);TimingStep();Assert.AreEqual(200,target.CurrentHp);TimingStep();Assert.AreEqual(189,target.CurrentHp);
            battle.Pause(BattlePauseReason.CustomScreen);for(int i=0;i<10;i++)TimingStep();Assert.AreEqual(0,battle.RecoveryElapsedFrames);battle.Resume(BattlePauseReason.CustomScreen);
            for(int i=0;i<12;i++)TimingStep();Assert.IsFalse(battle.HasPlayerAction);
            SetupTiming();TimingStep(Tap());battle.Player.ApplyParalysis(3);for(int i=0;i<10;i++)TimingStep();Assert.AreEqual(200,target.CurrentHp);Assert.IsFalse(battle.HasPlayerAction);
            SetupTiming();battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("new",ElementResistances.Neutral,ElementType.Water,attack:new TransformationAttackDefinition(ElementType.Fire,timing:new PlayerAttackTiming(2,8,4)))});
            TimingStep(Tap());battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());battle.Transformations.TryPreview("new");battle.CustomScreen.Confirm();TimingStep();battle.CustomScreen.Close();for(int i=0;i<5;i++)TimingStep();Assert.AreEqual(200,target.CurrentHp);Assert.IsFalse(battle.HasPlayerAction);
        }
        [Test]
        public void 後隙キャンセルで発射済み投擲を消さず再実行もせず有効な窓だけを設定できる()
        {
            SetupTiming();var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.DelayedProjectile,Trigger=DerivativeTrigger.Immediate,DelayTime=.3f,TargetOffset=new Vector2Int(1,0)});
            var card=TimedCard("projectile",modifier);battle.ConfigureCardAttackTiming(card.Id,new PlayerAttackTiming(0,12,3));battle.CardUsage.EnqueueCards(new List<Card>{card});var task=battle.CardUsage.TryUseCurrentCardAsync().Preserve();
            Assert.AreEqual(1,battle.DelayedEffects.ActiveEffectCount);for(int i=0;i<3;i++)TimingStep();TimingStep(Tap());Assert.IsTrue(task.GetAwaiter().GetResult());Assert.AreEqual(1,battle.DelayedEffects.ActiveEffectCount);
            for(int i=0;i<20;i++)TimingStep();Assert.AreEqual(169,target.CurrentHp);Assert.AreEqual(0,battle.DelayedEffects.ActiveEffectCount);Assert.IsTrue(battle.CardUsage.IsEmpty);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new PlayerAttackTiming(0,6,7));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new PlayerAttackTiming(-1,6,0));
        }
        [Test]
        public void 前後隙とカード窓の通常入力トレースは描画三十六十百二十と追いつきで一致する()
        {
            List<string> Trace(int fps){SetupTiming();battle.ConfigureCardAttackTiming("a",new PlayerAttackTiming(2,10,3));battle.CardUsage.EnqueueCards(new List<Card>{TimedCard("a"),TimedCard("a")});var clock=new LogicalFrameClock();var trace=new List<string>();long wall=0;
                void Tick(long f,int ms){if(f==40)battle.Pause(BattlePauseReason.User);if(f==50)battle.Resume(BattlePauseReason.User);TimingStep(f==1||f==20?Tap():f==10||f==35?Tap(true):new LogicalInput());trace.Add(f+":"+target.CurrentHp+":"+battle.PlayerActionPhase+":"+battle.RecoveryElapsedFrames+":"+battle.CardUsage.RemainingCount+":"+state.ChargeFrames);}
                if(fps==0){clock.Accumulate(LogicalFrameClock.WallTicksPerSecond*3/2);clock.Drain(Tick);}else for(int i=1;clock.Frame<90;i++){long next=(long)i*LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}return trace.Take(90).ToList();}
            var expected=Trace(60);CollectionAssert.AreEqual(expected,Trace(30));CollectionAssert.AreEqual(expected,Trace(120));CollectionAssert.AreEqual(expected,Trace(0));
        }

        [Test]
        public void マスター個別時間は実カード受付へ入り外部戦闘上書きなしで窓から次動作へ移る()
        {
            SetupTiming();var master=new Olca.Core.Domain.Master.CardMaster{Id="data",Name="検証",Category=CardCategory.Attack,Series=SeriesType.Canon,Power=20,StartupTime=9,RecoveryTime=9,StartupFrames=3,RecoveryFrames=12,RecoveryCancelAfterFrames=6};
            var card=Card.FromMaster(master);battle.CardUsage.EnqueueCards(new List<Card>{card,card.Clone()});var task=battle.CardUsage.TryUseCurrentCardAsync().Preserve();TimingStep();TimingStep();Assert.AreEqual(200,target.CurrentHp);TimingStep();Assert.AreEqual(180,target.CurrentHp);
            for(int i=0;i<5;i++)TimingStep();Assert.IsFalse(battle.CanCancelPlayerRecovery);TimingStep();Assert.IsTrue(battle.CanCancelPlayerRecovery);TimingStep(Tap());Assert.IsTrue(task.GetAwaiter().GetResult());Assert.AreEqual(1,battle.CardUsage.RemainingCount);TimingStep();TimingStep();Assert.AreEqual(169,target.CurrentHp);
        }
        [Test]
        public void 取消開始だけ明示しても旧秒値をフレームへ丸め直さず即時指定は前後隙を省く()
        {
            SetupTiming();var card=new Card("seconds","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,20,"検証",startupTime:.011f,recoveryTime:.101f,recoveryCancelAfterFrames:6);battle.CardUsage.EnqueueCards(new List<Card>{card});var task=battle.CardUsage.TryUseCurrentCardAsync().Preserve();battle.Tick(10);Assert.AreEqual(200,target.CurrentHp);battle.Tick(1);Assert.AreEqual(180,target.CurrentHp);battle.Tick(100);Assert.IsTrue(battle.HasPlayerAction);battle.Tick(1);Assert.IsTrue(task.GetAwaiter().GetResult());Assert.IsFalse(battle.HasPlayerAction);
            SetupTiming();var instant=new Card("instant","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,20,"検証",isInstantEffect:true,startupFrames:3,recoveryFrames:12,recoveryCancelAfterFrames:6);battle.CardUsage.EnqueueCards(new List<Card>{instant});Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.AreEqual(180,target.CurrentHp);Assert.IsFalse(battle.HasPlayerAction);
        }

        private void SetupForm(bool confirm = true)
        {
            target=new Enemy("target","検証",200,ElementType.None,new Vector2Int(2,0),resistances:new ElementResistances(fire:50,water:50));
            battle=new Olca.Core.Logic.Battle.BattleSystem(new PlayerCharacter(500,new Vector2Int(1,0),attackPower:11),new List<Enemy>{target,new Enemy("keeper","検証",200,ElementType.None,new Vector2Int(5,0))},new Deck());
            state=new Olca.Application.Battle.BasicAttackInputState();
            battle.Transformations.Configure(new[]{
                new TransformationDefenseDefinition("fire",ElementResistances.Neutral,ElementType.Water,attack:new TransformationAttackDefinition(ElementType.Fire,150,250,3,4)),
                new TransformationDefenseDefinition("water",ElementResistances.Neutral,ElementType.Fire,attack:new TransformationAttackDefinition(ElementType.Water,100,300,4,6))});
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());
            if(confirm){battle.Transformations.TryPreview("fire");battle.CustomScreen.Confirm();battle.CustomScreen.Close();}
        }
        [Test]
        public void 変身攻撃はプレビューで未発動かつ確定後だけ固定属性と専用タメ閾値を使う()
        {
            SetupForm(false);var t=battle.Transformations;Assert.IsNull(t.ActiveAttack);t.TryPreview("fire");Assert.AreEqual(ElementType.Fire,t.PreviewAttack.Element);Step(Tap());Assert.AreEqual(200,target.CurrentHp);
            t.TryCancelPending();Assert.IsNull(t.PreviewAttack);Assert.IsNull(t.ActiveAttack);
            t.TryPreview("fire");battle.CustomScreen.Confirm();battle.CustomScreen.Close();Step(Tap());Assert.AreEqual(192,target.CurrentHp,"11×150%を整数化後に火耐性50を適用");Assert.AreEqual(4,state.CooldownFrames);
            for(int i=0;i<4;i++)Step();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            Step(new LogicalInput{BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true});Assert.AreEqual(3,state.ChargeFrames);Assert.AreEqual(192,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(179,target.CurrentHp,"専用250%を使い呼出元の通常2倍で代用しない");Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(179,target.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void 変身の専用タメも停止中は保持し取消プレビューで元性能へ戻り新押下だけ再開する(bool custom)
        {
            SetupForm();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true});
            if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());battle.Transformations.TryPreview("water");}
            else battle.Pause(BattlePauseReason.User);
            for(int f=0;f<10;f++)Step(new LogicalInput{BasicAttackButton=true});Assert.AreEqual(2,state.ChargeFrames);
            if(custom){battle.Transformations.TryCancelPending();battle.CustomScreen.Close();}else battle.Resume(BattlePauseReason.User);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200,target.CurrentHp);Assert.AreEqual(2,state.ChargeFrames);
            Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(187,target.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void タメ途中の確定切替と変身終了は旧量と解放を攻撃へ転用しない(bool end)
        {
            SetupForm();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true});
            if(end)battle.Player.EndTransformationDefense();else{
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());battle.Transformations.TryPreview("water");battle.CustomScreen.Confirm();Step(new LogicalInput{BasicAttackButton=true});battle.CustomScreen.Close();}
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(0,state.ChargeFrames);Assert.AreEqual(200,target.CurrentHp);Assert.IsFalse(state.IsCharging);
            Step(Tap());Assert.AreEqual(end?189:195,target.CurrentHp);if(end)Assert.IsNull(battle.Transformations.ActiveAttack);else Assert.AreEqual(ElementType.Water,battle.Transformations.ActiveAttack.Element);
        }
        [Test]
        public void 変身タメのカード優先と未設定互換は性能を二重適用せず大きな倍率も飽和する()
        {
            SetupForm();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true});Step(new LogicalInput{BasicAttackButton=true,CardUseButtonDown=true});
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200,target.CurrentHp);Assert.AreEqual(0,state.ChargeFrames);
            SetupBattle();Assert.IsTrue(battle.TryPlayerBasicAttackForward(int.MaxValue));Assert.IsTrue(target.IsDead,"既存倍率入口を百分率の上限で早期切捨てしない");
            var defaults=new TransformationAttackDefinition(ElementType.Fire);Assert.AreEqual(100,defaults.NormalPowerPercent);Assert.AreEqual(200,defaults.ChargedPowerPercent);Assert.AreEqual(60,defaults.ChargeFrames);Assert.AreEqual(12,defaults.IntervalFrames);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationAttackDefinition((ElementType)999));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationAttackDefinition(ElementType.Fire,chargeFrames:0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationAttackDefinition(ElementType.Fire,normalPowerPercent:-1));
        }

        [TestCase(2,0,189)][TestCase(3,0,200)][TestCase(0,0,200)][TestCase(2,1,200)]
        public void 前方一マス同じ列の敵だけに成長後の無属性攻撃を適用する(int x,int y,int hp)
        {SetupBattle(x,y);Step(Tap());Assert.AreEqual(hp,target.CurrentHp);}
        [Test]
        public void 同マスは先配置一体だけを選び無敵対象を別の敵へ付け替えない()
        {
            SetupBattle();var second=new Enemy("second","second",200,ElementType.None,target.GridPosition,isSummoned:true);battle.AddSummon(second,new List<Olca.Core.Logic.Battle.IEnemyAIAction>{new Olca.Core.Logic.Battle.BasicAttackAction()});
            target.SetInvincible(1000);Step(Tap());Assert.AreEqual(200,target.CurrentHp);Assert.AreEqual(200,second.CurrentHp);
        }
        [TestCase(1,1)][TestCase(59,1)][TestCase(60,2)][TestCase(120,2)]
        public void 解放時の論理タメ量で通常と二倍を一度だけ分岐する(int held,int multiplier)
        {
            SetupBattle();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            for(int i=1;i<held;i++)Step(new LogicalInput{BasicAttackButton=true});Assert.AreEqual(200,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200-11*multiplier,target.CurrentHp);
            for(int i=0;i<40;i++)Step();Assert.AreEqual(200-11*multiplier,target.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void ポーズとカスタムはタメを保持し復帰解放で暴発せず再押下で再開する(bool custom)
        {
            SetupBattle(); Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            for(int f=1;f<30;f++)Step(new LogicalInput{BasicAttackButton=true});
            if(custom){battle.CustomGauge.SetGaugeMs(Olca.Core.Logic.Battle.CustomGaugeSystem.MAX_GAUGE_MS);battle.OpenCustomScreen(new MockRandomProvider());}
            else battle.Pause(BattlePauseReason.User);
            for(int f=0;f<100;f++)Step(new LogicalInput{BasicAttackButton=true});
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(30,state.ChargeFrames);
            if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);
            Step(new LogicalInput{BasicAttackButtonUp=true});Step();Assert.AreEqual(200,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            for(int f=1;f<30;f++)Step(new LogicalInput{BasicAttackButton=true});
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(178,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(178,target.CurrentHp);
        }
        [Test]
        public void タメ中も通常移動しカード入力と明示取消で蓄積を破棄する()
        {
            SetupBattle();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            battle.TickFrame(17,new LogicalInput{Horizontal=-1});Assert.IsTrue(battle.PlayerMovement.IsNormalMoving);
            for(int f=0;f<20;f++){Step(new LogicalInput{BasicAttackButton=true});battle.TickFrame(17);}
            Assert.AreEqual(21,state.ChargeFrames);Assert.AreEqual(0,battle.Player.GridPosition.X);
            Step(new LogicalInput{BasicAttackButton=true,CardUseButtonDown=true});Assert.AreEqual(0,state.ChargeFrames);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});state.CancelCharge();
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200,target.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void ポインター保持は同じタメ状態へ入り取消と別指を攻撃へ転用しない(bool transformed)
        {
            SetupBattle();
            if(transformed){battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("touch",ElementResistances.Neutral,ElementType.Water,attack:new TransformationAttackDefinition(ElementType.Fire,chargedPowerPercent:300,chargeFrames:6))});battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());battle.Transformations.TryPreview("touch");battle.CustomScreen.Confirm();battle.CustomScreen.Close();}var o=new UnityEngine.GameObject("hold-input");
            try {
                var pointer=o.AddComponent<Olca.Presentation.Dungeon.BasicAttackTapButton>();
                bool down=false,up=false,held=false;int submits=0;
                pointer.InitializeHold(value=>{if(value)down=true;else up=true;held=value;},()=>{held=down=up=false;state.CancelCharge();},()=>submits++);
                var data=new UnityEngine.EventSystems.PointerEventData(null){pointerId=1,eligibleForClick=true};
                pointer.OnPointerDown(data);
                for(int f=0;f<60;f++){Step(new LogicalInput{BasicAttackButtonDown=down,BasicAttackButton=held});down=false;}
                var other=new UnityEngine.EventSystems.PointerEventData(null){pointerId=2,eligibleForClick=true};
                pointer.OnPointerUp(other);pointer.OnPointerClick(other);Assert.IsTrue(held);
                pointer.OnPointerUp(data);pointer.OnPointerClick(data);Step(new LogicalInput{BasicAttackButtonUp=up});
                Assert.AreEqual(transformed?167:178,target.CurrentHp);up=false;
                for(int f=0;f<12;f++)Step();
                pointer.OnPointerDown(data);Step(new LogicalInput{BasicAttackButtonDown=down,BasicAttackButton=held});
                data.eligibleForClick=false;pointer.OnPointerUp(data);pointer.OnPointerClick(data);Step();
                Assert.AreEqual(transformed?167:178,target.CurrentHp);Assert.AreEqual(0,state.ChargeFrames);
                pointer.OnSubmit(null);Assert.AreEqual(1,submits);
            }finally{UnityEngine.Object.DestroyImmediate(o);}
        }

        [Test]
        public void タメの論理トレースは描画三十六十百二十と追いつき処理で一致する()
        {
            List<string> Trace(int fps) {
                SetupBattle();var clock=new LogicalFrameClock();var trace=new List<string>();long wall=0;
                void Tick(long frame,int ms) {
                    if(frame==20)battle.Pause(BattlePauseReason.CustomScreen);
                    if(frame==30)battle.Resume(BattlePauseReason.CustomScreen);
                    Step(new LogicalInput{BasicAttackButtonDown=frame==1||frame==30,BasicAttackButton=frame<=70,BasicAttackButtonUp=frame==71});
                    trace.Add(frame+":"+state.ChargeFrames+":"+target.CurrentHp+":"+state.CooldownFrames);
                }
                if(fps==0){clock.Accumulate(LogicalFrameClock.WallTicksPerSecond*3/2);clock.Drain(Tick);}
                else for(int sample=1;clock.Frame<90;sample++){long next=(long)sample*LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}
                Assert.AreEqual(178,target.CurrentHp);return trace.Take(90).ToList();
            }
            var expected=Trace(60);CollectionAssert.AreEqual(expected,Trace(30));CollectionAssert.AreEqual(expected,Trace(120));CollectionAssert.AreEqual(expected,Trace(0));
        }

        [Test]
        public void 同一フレームの押下解放を保持し連打は十二フレーム未満を予約しない()
        {
            SetupBattle();var q=new LogicalInputQueue();q.Enqueue(1,new LogicalInput{BasicAttackButton=true,BasicAttackButtonDown=true});q.Enqueue(1,new LogicalInput{BasicAttackButtonUp=true});
            var tap=q.Take(1);Assert.IsTrue(tap.BasicAttackButtonDown&&tap.BasicAttackButtonUp);Step(tap);Assert.AreEqual(189,target.CurrentHp);
            Step(Tap());for(int i=0;i<10;i++)Step();Assert.AreEqual(189,target.CurrentHp);Step(Tap());Assert.AreEqual(178,target.CurrentHp);
            Assert.IsFalse(q.Take(2).BasicAttackButtonUp);
        }
        [Test]
        public void 画面ボタンも短いポインター解放だけ発動し長押しを通常攻撃へ転用しない()
        {
            var o=new UnityEngine.GameObject("tap-fixture");
            try {var tap=o.AddComponent<Olca.Presentation.Dungeon.BasicAttackTapButton>();long frame=0;int fired=0;tap.Initialize(()=>fired++,()=>frame,()=>30);
                tap.OnPointerDown(null);frame=2;tap.OnPointerUp(null);tap.Click();Assert.AreEqual(1,fired);
                tap.OnPointerDown(null);frame+=30;tap.OnPointerUp(null);tap.Click();Assert.AreEqual(1,fired);
                tap.Click();Assert.AreEqual(2,fired,"メニューSubmit/onClickは一つの短押し操作");
            }finally{UnityEngine.Object.DestroyImmediate(o);}
        }
        [Test]
        public void ポインターの短押しは描画追いつき前の論理フレーム番号ではなく生成時刻で測る()
        {
            var o=new UnityEngine.GameObject("timestamp-tap-fixture");try {
                var tap=o.AddComponent<Olca.Presentation.Dungeon.BasicAttackTapButton>();long stamp=1000;int fired=0;
                tap.Initialize(()=>fired++,()=>stamp,()=>30,1000);tap.OnPointerDown(null);
                stamp=1070;tap.OnPointerUp(null);tap.Click();Assert.AreEqual(1,fired);
                tap.OnPointerDown(null);stamp+=500;tap.OnPointerUp(null);tap.Click();Assert.AreEqual(1,fired);
            }finally{UnityEngine.Object.DestroyImmediate(o);}
        }
        [Test]
        public void カード同時入力を優先し攻撃が後から発火しない()
        {SetupBattle();Step(Tap(true));Assert.AreEqual(200,target.CurrentHp);Step();Assert.AreEqual(200,target.CurrentHp);Step(Tap());Assert.AreEqual(189,target.CurrentHp);}
        [TestCase("pause")][TestCase("custom")][TestCase("move")][TestCase("stagger")][TestCase("paralysis")]
        public void 拘束中の押下解放や拘束前の保持を復帰後に予約しない(string kind)
        {
            SetupBattle();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            if(kind=="pause")battle.Pause(BattlePauseReason.User);
            if(kind=="custom"){battle.CustomGauge.SetGaugeMs(Olca.Core.Logic.Battle.CustomGaugeSystem.MAX_GAUGE_MS);battle.OpenCustomScreen(new MockRandomProvider());}
            if(kind=="move")battle.TickFrame(17,new LogicalInput{Horizontal=-1});
            if(kind=="stagger"){battle.Player.TakeDamage(1,poiseDamage:30);}
            if(kind=="paralysis"){battle.Player.ApplyParalysis(3);battle.TickFrame(17);}
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(200,target.CurrentHp);
            if(kind=="pause")battle.Resume(BattlePauseReason.User);
            if(kind=="custom")battle.CustomScreen.Close();
            for(int i=0;i<20;i++){Step();battle.TickFrame(17);}
            Assert.AreEqual(200,target.CurrentHp);
            target.MoveTo(new Vector2Int(battle.Player.GridPosition.X+1,battle.Player.GridPosition.Y));Step(Tap());Assert.AreEqual(189,target.CurrentHp);
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class DungeonVisitFlowTests
    {
        private sealed class FaultStore : ICheckpointStore {
            public readonly MemoryCheckpointStore Inner;public bool Fail;public System.Action During;
            public FaultStore(CheckpointDefinitions definitions){Inner=new MemoryCheckpointStore(definitions);}
            public bool TryWrite(CheckpointDto d,out string error){During?.Invoke();if(Fail){error="fixture exit save failure";return false;}return Inner.TryWrite(d,out error);}
            public CheckpointDto Read()=>Inner.Read();
        }
        private sealed class ExitIo : Olca.Unity.CheckpointFileIo {
            public string Fault;public override void Replace(string temporary,string destination,string backup){if(Fault=="before")throw new System.IO.IOException("visit fixture before");base.Replace(temporary,destination,backup);if(Fault=="after")throw new System.IO.IOException("visit fixture after");}
        }
        [TestCase("before")][TestCase("after")]
        public void 停止地点の実ファイル退出保存は保存前後エラーと再開でも一回だけ取引する(string fault) {
            Start(true,out var original,out var memory);var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-visit-"+System.Guid.NewGuid().ToString("N"));
            try{var io=new ExitIo();var file=new Olca.Unity.FileCheckpointStore(original.Definitions,directory,io);Assert.IsTrue(file.TryWrite(memory.Read(),out var error),error);
                var session=new DungeonCheckpointSession(original.Definitions,file);Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var game,out error),error);game.Dungeon.ActivateCheckpoint();Enter(game);
                Assert.IsTrue(game.Dungeon.ChooseVisit(0));io.Fault=fault;bool saved=game.Dungeon.ExitVisit();
                if(fault=="before"){Assert.IsFalse(saved);Assert.AreEqual(20,file.Read().Currency);io.Fault=null;Assert.IsTrue(game.RetryCheckpoint());}else Assert.IsTrue(saved);
                var data=file.Read();Assert.AreEqual(10,data.Currency);Assert.AreEqual(1,data.ObtainedOrder.Length);Assert.AreEqual(2,data.DeckOrder.Length);Assert.IsTrue(data.Visits[0].Closed);
                Assert.IsTrue(CheckpointCodec.TryRestore(data,original.Definitions,out var restored,out error),error);Assert.AreEqual(10,restored.Snapshot.Currency);Assert.AreEqual(1,restored.Obtained.Count);
                Assert.IsFalse(game.Dungeon.ChooseVisit(0));Assert.IsFalse(game.Dungeon.ExitVisit());
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [Test]
        public void 停止地点なしの旧マップhashは維持し候補定義の変更と欠落だけを拒否する() {
            var legacy=DungeonConfig.CreateTestDungeon(5,30,4);legacy.MapCells[4]=new MapCell(4,new Enemy("boss","旧dash",200,ElementType.None,new Vector2Int(4,0)));var old=new CheckpointDefinitions("v1",legacy);Assert.AreEqual("FD6B09962EE734F318E3FC8905339460CBB37E720D5FAFD732E88CDE71665090",old.MapHash);
            var game=Start(true,out var session,out var store);var copy=store.Read();copy.Visits=null;Assert.IsFalse(CheckpointCodec.TryRestore(copy,session.Definitions,out _,out _));
            var same=session.Definitions.CreateInitialMap();Assert.AreEqual(1,same.Visits.Count);Assert.AreEqual(session.Definitions.MapHash,new CheckpointDefinitions("fixed-runtime-v1",same).MapHash);
        }
        private static DungeonBattleManager Removal(bool shop,int count,int size,bool decline,out DungeonCheckpointSession session,out FaultStore store,out Deck original,bool obtained=true)
        {
            var baseMap=DungeonConfig.CreateTestDungeon(8,100,7);
            var map=new DungeonConfig(baseMap.DungeonId,baseMap.DungeonName,8,baseMap.MapCells,visits:new[]{new DungeonVisitDefinition(2,shop,new[]{new DungeonVisitOption("remove",shop?10:0,removeCount:count)},1,decline)});
            original=new Deck(Enumerable.Range(0,size).Select(_=>new Olca.Application.Battle.SimpleCardFactory().CreateById("sword")).ToList());
            var seed=DungeonCheckpointSession.CreateDefault(map,original);new DungeonBattleManager(map,new PlayerCharacter(100),original,new MockRandomProvider(),new MockLogger(),checkpoints:seed);
            var dto=seed.Store.Read();dto.Currency=20;if(obtained&&size>0)dto.ObtainedOrder=new[]{dto.DeckOrder[0]};
            Assert.IsTrue(CheckpointCodec.TryRestore(dto,seed.Definitions,out var restored,out var error),error);
            store=new FaultStore(seed.Definitions);Assert.IsTrue(store.TryWrite(dto,out error),error);session=new DungeonCheckpointSession(seed.Definitions,store);
            return new DungeonBattleManager(restored.Map,restored.Player,restored.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:restored);
        }
        [TestCase(true)][TestCase(false)]
        public void 削除は選択個体だけを除き同名別個体と持込原本と取得履歴を保持する(bool shop)
        {
            var game=Removal(shop,1,3,true,out var session,out var store,out var original);Enter(game);var d=game.Dungeon;var cards=d.GetCurrentDeck().GetAllCards().ToArray();long seq=store.Read().Sequence;
            Assert.IsFalse(d.ChooseVisit(0));Assert.AreEqual(20,d.Currency);Assert.IsFalse(d.ChooseVisit(0,new[]{cards[0].Clone()}));
            Assert.IsTrue(d.ChooseVisit(0,new[]{cards[0]}));Assert.IsFalse(d.ChooseVisit(0,new[]{cards[1]}));
            CollectionAssert.AreEqual(cards.Skip(1),d.GetCurrentDeck().GetAllCards());Assert.AreSame(cards[0],d.GetObtainedCards().Single());Assert.AreEqual(3,original.TotalCount);
            Assert.AreEqual(shop?10:20,d.Currency);Assert.AreEqual(seq,store.Read().Sequence);Assert.IsTrue(d.ExitVisit());
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var state,out var error),error);
            Assert.AreEqual(2,state.Deck.TotalCount);Assert.AreEqual(1,state.Obtained.Count);Assert.IsFalse(state.Deck.GetAllCards().Contains(state.Obtained[0]));Assert.AreEqual(3,state.Snapshot.Cards.Length);
        }
        [TestCase(2)][TestCase(10)]
        public void 削除枚数は実在枚数へ丸め重複と不足選択を拒否しゼロ枚まで削除できる(int count)
        {
            var game=Removal(true,count,2,true,out _,out _,out _);Enter(game);var d=game.Dungeon;var cards=d.GetCurrentDeck().GetAllCards().ToArray();
            Assert.AreEqual(2,d.VisitRemovalCount(0));Assert.IsFalse(d.ChooseVisit(0,new[]{cards[0]}));Assert.IsFalse(d.ChooseVisit(0,new[]{cards[0],cards[0]}));Assert.AreEqual(20,d.Currency);
            d.GetCurrentDeck().DrawCards(2,new MockRandomProvider());Assert.IsTrue(d.ChooseVisit(0,d.GetCurrentDeck().GetAllCards()),"同じデッキの読取Viewを対象にしても列挙中に壊れない");Assert.AreEqual(0,d.GetCurrentDeck().TotalCount);Assert.AreEqual(0,d.GetCurrentDeck().RemainingCount);Assert.IsTrue(d.ExitVisit());
        }
        [TestCase(true)][TestCase(false)]
        public void 空デッキの削除は支払わず必須イベントも対象なしなら退出できる(bool shop)
        {
            var game=Removal(shop,1,0,shop,out _,out var store,out _);Enter(game);var d=game.Dungeon;
            Assert.IsFalse(d.CanChooseVisit(0));Assert.IsFalse(d.ChooseVisit(0,new Card[0]));Assert.AreEqual(20,d.Currency);Assert.IsTrue(d.CanExitVisit);Assert.IsTrue(d.ExitVisit());Assert.AreEqual(20,store.Read().Currency);
        }
        [Test]
        public void 辞退不可の削除イベントは未選択では退出せず確定後だけ退出する()
        {
            var game=Removal(false,1,2,false,out _,out _,out _);Enter(game);var d=game.Dungeon;Assert.IsFalse(d.CanExitVisit);Assert.IsFalse(d.ExitVisit());
            Assert.IsFalse(d.ChooseVisit(0));Assert.IsTrue(d.ChooseVisit(0,new[]{d.GetCurrentDeck().GetAllCards()[0]}));Assert.IsTrue(d.ExitVisit());
        }
        [Test]
        public void 削除の保存失敗は同じ削除結果を再試行し保存前再開は支払と削除を戻す()
        {
            var game=Removal(true,1,2,true,out var session,out var store,out _);Enter(game);var d=game.Dungeon;var target=d.GetObtainedCards().Single();Assert.IsTrue(d.ChooseVisit(0,new[]{target}));store.Fail=true;
            Assert.IsFalse(d.ExitVisit());Assert.IsTrue(d.IsCheckpointBlocked);Assert.IsFalse(d.ChooseVisit(0,new[]{d.GetCurrentDeck().GetAllCards()[0]}));
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var before,out var error),error);Assert.AreEqual(2,before.Deck.TotalCount);Assert.AreEqual(20,before.Snapshot.Currency);Assert.IsTrue(before.Deck.GetAllCards().Contains(before.Obtained.Single()));
            store.Fail=false;store.During=()=>Assert.IsFalse(d.ChooseVisit(0,new[]{target}));Assert.IsTrue(game.RetryCheckpoint());store.During=null;Assert.IsFalse(game.RetryCheckpoint());
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var after,out error),error);Assert.AreEqual(1,after.Deck.TotalCount);Assert.AreEqual(10,after.Snapshot.Currency);Assert.AreEqual(1,after.Obtained.Count);Assert.IsFalse(after.Deck.GetAllCards().Contains(after.Obtained.Single()));
        }
        [TestCase("before")][TestCase("after")]
        public void 削除の実ファイル保存前後エラーと再開で代金も個体も二重適用しない(string fault)
        {
            Removal(true,1,2,true,out var originalSession,out var seed,out _);var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-removal-"+System.Guid.NewGuid().ToString("N"));
            try{var io=new ExitIo();var disk=new Olca.Unity.FileCheckpointStore(originalSession.Definitions,directory,io);Assert.IsTrue(disk.TryWrite(seed.Read(),out var error),error);
                Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(originalSession.Definitions,disk),new MockRandomProvider(),new MockLogger(),out var game,out error),error);
                game.Dungeon.ActivateCheckpoint();Enter(game);var d=game.Dungeon;Assert.IsTrue(d.ChooseVisit(0,new[]{d.GetObtainedCards().Single()}));io.Fault=fault;bool saved=d.ExitVisit();if(!saved){Assert.IsTrue(d.IsCheckpointBlocked);io.Fault=null;Assert.IsTrue(game.RetryCheckpoint());}
                var reopened=new Olca.Unity.FileCheckpointStore(originalSession.Definitions,directory);Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(),originalSession.Definitions,out var state,out error),error);
                Assert.AreEqual(1,state.Deck.TotalCount);Assert.AreEqual(10,state.Snapshot.Currency);Assert.AreEqual(1,state.Obtained.Count);Assert.IsTrue(state.Snapshot.Visits[0].Closed);
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [Test]
        public void ショップで削除した取得個体は最終クリアの持帰り選択と永続獲得まで残る()
        {
            var game=Removal(true,2,2,true,out var session,out var store,out _);Enter(game);var d=game.Dungeon;Assert.IsTrue(d.ChooseVisit(0,d.GetCurrentDeck().GetAllCards().ToArray()));Assert.IsTrue(d.ExitVisit());
            for(int i=0;i<200&&game.CurrentBattle==null;i++)game.TickFrame(17);Assert.IsNotNull(game.CurrentBattle);
            foreach(var enemy in game.CurrentBattle.Enemies.ToArray())enemy.TakeDamage(enemy.MaxHp,ElementType.None);
            for(int i=0;i<10&&game.CurrentPhase==GamePhase.Battle;i++)game.TickFrame(17);
            if(game.CurrentPhase==GamePhase.CardReward)game.SelectCardReward(null);if(d.PendingCurrencyReward>0)game.SelectCurrencyReward(false);
            Assert.AreEqual(CheckpointBoundary.Terminal,store.Read().Boundary);Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var terminal,out var error),error);Assert.AreEqual(0,terminal.Deck.TotalCount);Assert.AreEqual(1,terminal.Obtained.Count);
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-removal-return-"+System.Guid.NewGuid().ToString("N"));
            try{var flow=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(directory));Assert.IsTrue(flow.Begin(terminal,"removal",new ClearReturnSettings()),flow.Error);Assert.AreEqual(1,flow.Snapshot.Flow.Candidates.Length);Assert.IsTrue(flow.Toggle(0));Assert.IsTrue(flow.ConfirmSelection());Assert.IsTrue(flow.ConfirmReturn());Assert.AreEqual(1,flow.Snapshot.Owned.Single().Count);}
            finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }

        private static DungeonConfig Map(bool shop) {
            var m=DungeonConfig.CreateTestDungeon(8,100,7);var factory=new Olca.Application.Battle.SimpleCardFactory();
            var options=shop?new[]{new DungeonVisitOption("sword",10,factory.CreateById("sword")),new DungeonVisitOption("bomb",10,factory.CreateById("bomb"))}:
                new[]{new DungeonVisitOption("existing-heal",0,heal:20)};
            return new DungeonConfig(m.DungeonId,m.DungeonName,m.TotalCellCount,m.MapCells,visits:new[]{new DungeonVisitDefinition(2,shop,options,options.Length)});
        }
        private static DungeonBattleManager Start(bool shop,out DungeonCheckpointSession session,out FaultStore store,bool funded=true) {
            var map=Map(shop);var deck=new Deck(new List<Card>{new Olca.Application.Battle.SimpleCardFactory().CreateById("sword")});
            var seed=DungeonCheckpointSession.CreateDefault(map,deck);new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:seed);
            var dto=seed.Store.Read();dto.Currency=funded?20:0;dto.Player.Hp=60;
            Assert.IsTrue(CheckpointCodec.TryRestore(dto,seed.Definitions,out var state,out var error),error);
            store=new FaultStore(seed.Definitions);Assert.IsTrue(store.TryWrite(dto,out error),error);session=new DungeonCheckpointSession(seed.Definitions,store);
            return new DungeonBattleManager(state.Map,state.Player,state.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:state);
        }
        private static void Enter(DungeonBattleManager game){for(int i=0;i<100&&game.Dungeon.ActiveVisit==null;i++)game.TickFrame(17);Assert.IsNotNull(game.Dungeon.ActiveVisit);}
        [TestCase(true)][TestCase(false)]
        public void 停止地点は通常探索で入り選択中の移動毒タイマーとダッシュを停止し退出保存する(bool shop) {
            var game=Start(shop,out var session,out var store);game.Dungeon.Player.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,10000,1),30);Enter(game);
            var d=game.Dungeon;float position=d.PlayerPosition;var poison=d.Player.CapturePoisons()[0];int hp=d.Player.CurrentHp;long seq=store.Read().Sequence;
            game.SetUserPaused(true);for(int i=0;i<100;i++)game.TickFrame(17);game.SetUserPaused(false);for(int i=0;i<100;i++)game.TickFrame(17);
            Assert.AreEqual(position,d.PlayerPosition);Assert.AreEqual(hp,d.Player.CurrentHp);Assert.AreEqual(poison.RemainingUnits,d.Player.CapturePoisons()[0].RemainingUnits);
            Assert.IsTrue(d.ChooseVisit(0));Assert.IsFalse(d.ChooseVisit(0));Assert.AreEqual(shop?10:20,d.Currency);Assert.AreEqual(shop?hp:System.Math.Min(100,hp+20),d.Player.CurrentHp);
            Assert.AreEqual(seq,store.Read().Sequence,"取引は退出まで未確定");Assert.IsTrue(d.ExitVisit());Assert.IsNull(d.ActiveVisit);Assert.AreEqual(seq+1,store.Read().Sequence);
            Assert.IsTrue(store.Read().Visits[0].Closed);Assert.IsFalse(d.ChooseVisit(0));
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,store),new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);
            restored.Dungeon.ActivateCheckpoint();for(int i=0;i<5;i++)restored.TickFrame(17);Assert.IsNull(restored.Dungeon.ActiveVisit);Assert.AreEqual(d.Currency,restored.Dungeon.Currency);
            Assert.AreEqual(d.GetCurrentDeck().TotalCount,restored.Dungeon.GetCurrentDeck().TotalCount);Assert.AreEqual(d.GetObtainedCards().Count,restored.Dungeon.GetObtainedCards().Count);
        }
        [TestCase(true)][TestCase(false)]
        public void 退出保存失敗は同じ候補と取引を再試行し再入を拒否し保存前再開はまとめて戻す(bool shop) {
            var game=Start(shop,out var session,out var store);Enter(game);var d=game.Dungeon;var before=store.Read();Assert.IsTrue(d.ChooseVisit(0));store.Fail=true;
            Assert.IsFalse(d.ExitVisit());Assert.IsTrue(d.IsCheckpointBlocked);Assert.IsFalse(d.ChooseVisit(0));Assert.AreEqual(before.Sequence,store.Read().Sequence);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var rolledBack,out var error),error);Assert.AreEqual(20,rolledBack.Snapshot.Currency);Assert.AreEqual(60,rolledBack.Player.CurrentHp);Assert.IsFalse(rolledBack.Snapshot.Visits[0].Purchased[0]);
            store.Fail=false;store.During=()=>Assert.IsFalse(d.ChooseVisit(0));Assert.IsTrue(game.RetryCheckpoint());store.During=null;
            Assert.IsFalse(game.RetryCheckpoint());Assert.IsNull(d.ActiveVisit);Assert.AreEqual(shop?10:20,store.Read().Currency);Assert.AreEqual(shop?1:0,store.Read().ObtainedOrder.Length);
        }
        [Test]
        public void ショップの不足選択取消相当退出と候補保存は所持を増やさず再抽選もしない() {
            var game=Start(true,out var session,out var store,false);Enter(game);var d=game.Dungeon;Assert.IsFalse(d.ChooseVisit(0));Assert.AreEqual(0,d.Currency);Assert.AreEqual(0,d.GetObtainedCards().Count);
            var options=store.Read().Visits[0].Options;Assert.IsTrue(d.ExitVisit());CollectionAssert.AreEqual(options,store.Read().Visits[0].Options);Assert.AreEqual(0,store.Read().ObtainedOrder.Length);
            var invalid=store.Read();invalid.Visits[0].Options[0]=99;Assert.IsFalse(CheckpointCodec.TryRestore(invalid,session.Definitions,out _,out _));
        }
        [Test]
        public void イベントは一回の確定のみで既存回復を適用し退出再進入で繰り返さない() {
            var game=Start(false,out var session,out var store);Enter(game);Assert.IsTrue(game.Dungeon.ChooseVisit(0));Assert.IsFalse(game.Dungeon.ChooseVisit(0));Assert.AreEqual(80,game.Dungeon.Player.CurrentHp);Assert.IsTrue(game.Dungeon.ExitVisit());
            Assert.AreEqual(80,store.Read().Player.Hp);Assert.AreEqual(20,store.Read().Currency);Assert.IsEmpty(store.Read().ObtainedOrder);
        }
    }
}

namespace Olca.Tests.Integration
{
    public class ItemBattleRewardTests
    {
        [Test]
        public void 強敵マークは固定一個で戦闘復帰と死亡および再初期化に追随する()
        {
            var go = new UnityEngine.GameObject("strong-marker-test");
            try {
                var view = go.AddComponent<Olca.Presentation.Field.WorldEnemyView>();
                var enemy = new Enemy("enemy", "既存敵", 50, ElementType.None, new Vector2Int(2,0));
                view.Initialize(enemy, 2f); view.SetStrongEnemy(true); view.SetStrongEnemy(true);
                var marker = go.transform.Find("StrongEnemyMarker");
                Assert.IsNotNull(marker); Assert.IsTrue(marker.gameObject.activeSelf); Assert.AreEqual(1, go.transform.childCount);
                var position = marker.localPosition; var size = marker.localScale;
                view.OnBattleStarted(null); Assert.IsFalse(marker.gameObject.activeSelf);
                view.OnBattleEnded(); Assert.IsTrue(marker.gameObject.activeSelf);
                Assert.AreEqual(position,marker.localPosition); Assert.AreEqual(size,marker.localScale);
                enemy.TakeDamage(50,ElementType.None); view.OnBattleEnded(); Assert.IsFalse(marker.gameObject.activeSelf);
                view.Initialize(new Enemy("normal","通常敵",50,ElementType.None,new Vector2Int(2,0)),2f);
                Assert.IsFalse(marker.gameObject.activeSelf);
            } finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private sealed class FaultStore:ICheckpointStore{
            public readonly MemoryCheckpointStore Inner;public bool Fail;public System.Action During;
            public FaultStore(CheckpointDefinitions defs){Inner=new MemoryCheckpointStore(defs);}
            public bool TryWrite(CheckpointDto dto,out string error){During?.Invoke();if(Fail){error="item reward save failure";return false;}return Inner.TryWrite(dto,out error);}
            public CheckpointDto Read()=>Inner.Read();
        }
        private sealed class FaultIo:Olca.Unity.CheckpointFileIo{
            public string Fault;public override void Replace(string temporary,string destination,string backup){if(Fault=="before")throw new System.IO.IOException("item before");base.Replace(temporary,destination,backup);if(Fault=="after")throw new System.IO.IOException("item after");}
        }
        private static DungeonBattleManager Start(string kind,out DungeonCheckpointSession session,out FaultStore store,int hp=60,int chance=100,int seed=0)
        {
            bool boss=kind.Contains("boss");var m=DungeonConfig.CreateTestDungeon(20,100,boss?100:2);if(kind=="late")m.MapCells[12]=new MapCell(12,new Enemy("late","既存強敵",50,ElementType.None,new Vector2Int(4,0)));
            var strong=kind=="strong"?new[]{2}:kind=="late"?new[]{12}:new int[0];
            var map=new DungeonConfig(m.DungeonId,m.DungeonName,20,m.MapCells,strongEnemyCells:strong,itemRewards:new[]{new ItemRewardEntry(new FieldItem("heal","ヒール",20))},itemRewardChance:chance);
            var deck=new Deck(new List<Card>{new Olca.Application.Battle.SimpleCardFactory().CreateById("sword")});var defaults=DungeonCheckpointSession.CreateDefault(map,deck);store=new FaultStore(defaults.Definitions);session=new DungeonCheckpointSession(defaults.Definitions,store);
            var player=new PlayerCharacter(100);if(hp<100)player.TakeDamage(100-hp);
            var game=new DungeonBattleManager(map,player,deck,new MockRandomProvider(seed),new MockLogger(),checkpoints:session,hasNextFloor:kind=="midboss");game.Dungeon.ConfigureCurrencyReward(10);
            for(int i=0;i<200&&game.CurrentBattle==null&&!game.Dungeon.IsCheckpointBlocked;i++)game.TickFrame(17);
            Assert.IsFalse(game.Dungeon.IsCheckpointBlocked,game.Dungeon.CheckpointError);Assert.IsNotNull(game.CurrentBattle);return game;
        }
        private static void Kill(DungeonBattleManager game){foreach(var enemy in game.CurrentBattle.Enemies.ToArray())if(!enemy.IsSummoned&&!enemy.IsDead)enemy.TakeDamage(enemy.MaxHp,ElementType.None);}
        private static Card CardChoice(DungeonBattleManager game,DungeonCheckpointSession session,FaultStore store,bool receive)
        {
            if(!store.Read().Battle.RewardOffered)return null;Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var state,out var error),error);
            // 通常ランタイムの同一候補を選ぶ。復元した別参照は使わない。
            var choices=(List<Card>)typeof(DungeonSystem).GetField("pendingRewardChoices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(game.Dungeon);var card=receive?choices[0]:null;game.SelectCardReward(card);return card;
        }
        [TestCase("normal",false)][TestCase("strong",true)][TestCase("midboss",true)][TestCase("finalboss",false)][TestCase("late",false)]
        public void 強敵と中間ボスだけで初期参加条件を満たす一枠を固定する(string kind,bool expected)
        {
            var game=Start(kind,out var session,out var store);var plan=store.Read();Assert.AreEqual(expected,plan.Battle.ItemRewardIndex.HasValue);
            if(kind=="late")game.Dungeon.Map.GetCell(12).Enemy.TakeDamage(50,ElementType.None);Kill(game);CardChoice(game,session,store,false);
            Assert.AreEqual(expected,game.Dungeon.PendingItemReward!=null);if(expected)game.SelectItemReward(false);if(game.Dungeon.PendingCurrencyReward>0)game.SelectCurrencyReward(false);
            Assert.IsNull(store.Read().Battle);Assert.AreEqual(60,store.Read().Player.Hp);
        }
        [TestCase(false,false,false)][TestCase(false,false,true)][TestCase(false,true,false)][TestCase(false,true,true)]
        [TestCase(true,false,false)][TestCase(true,false,true)][TestCase(true,true,false)][TestCase(true,true,true)]
        public void カードアイテム通貨の別枠は独立選択し全終了後に一回だけ保存する(bool card,bool item,bool currency)
        {
            var game=Start("midboss",out var session,out var store);var d=game.Dungeon;long seq=store.Read().Sequence;Kill(game);var selected=CardChoice(game,session,store,card);Assert.IsNotNull(d.PendingItemReward);
            game.SelectItemReward(item);Assert.AreEqual(item?80:60,d.Player.CurrentHp);game.SelectItemReward(!item);Assert.AreEqual(item?80:60,d.Player.CurrentHp);Assert.AreEqual(seq,store.Read().Sequence);
            Assert.AreEqual(10,d.PendingCurrencyReward);game.SelectCurrencyReward(currency);game.SelectCurrencyReward(!currency);
            Assert.AreEqual(seq+1,store.Read().Sequence);Assert.AreEqual(item?80:60,store.Read().Player.Hp);Assert.AreEqual(currency?10:0,store.Read().Currency);Assert.AreEqual(card?1:0,store.Read().ObtainedOrder.Length);
            if(card)Assert.AreSame(selected,d.GetObtainedCards().Single());
        }
        [Test]
        public void 満タン受取も一度で消費しポーズ中の受取と重複を拒否する()
        {
            var game=Start("strong",out var session,out var store,hp:100);Kill(game);CardChoice(game,session,store,false);game.SetUserPaused(true);game.SelectItemReward(true);Assert.IsNotNull(game.Dungeon.PendingItemReward);game.SetUserPaused(false);
            game.SelectItemReward(true);Assert.IsNull(game.Dungeon.PendingItemReward);Assert.AreEqual(100,game.Dungeon.Player.CurrentHp);game.SelectItemReward(true);game.SelectCurrencyReward(false);Assert.AreEqual(100,store.Read().Player.Hp);
        }
        [Test]
        public void 召喚撃破は報酬を提示せず初期強敵撃破後だけ一回提示する()
        {
            var game=Start("strong",out var session,out var store);int count=0;game.Dungeon.OnItemRewardOffered+=_=>count++;
            var summon=new Enemy("summon","既存召喚fixture",5,ElementType.None,new Vector2Int(5,0),isSummoned:true);game.CurrentBattle.AddSummon(summon,new List<Olca.Core.Logic.Battle.IEnemyAIAction>{new Olca.Core.Logic.Battle.BasicAttackAction(attackDamage:1,cooldown:10)});
            summon.TakeDamage(5,ElementType.None);Assert.AreEqual(0,count);Kill(game);CardChoice(game,session,store,false);Assert.AreEqual(1,count);game.SelectItemReward(false);game.SelectItemReward(true);Assert.AreEqual(1,count);game.SelectCurrencyReward(false);
        }
        [Test]
        public void 保存失敗再試行と戦闘開始からの再開で候補と回復を二重適用しない()
        {
            var game=Start("strong",out var session,out var store);var before=store.Read();Kill(game);CardChoice(game,session,store,false);game.SelectItemReward(true);Assert.AreEqual(80,game.Dungeon.Player.CurrentHp);store.Fail=true;game.SelectCurrencyReward(true);
            Assert.IsTrue(game.Dungeon.IsCheckpointBlocked);game.SelectItemReward(true);Assert.AreEqual(80,game.Dungeon.Player.CurrentHp);Assert.AreEqual(before.Sequence,store.Read().Sequence);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var rollback,out var error),error);Assert.AreEqual(60,rollback.Player.CurrentHp);Assert.AreEqual(before.Battle.ItemRewardIndex,rollback.Snapshot.Battle.ItemRewardIndex);
            store.Fail=false;store.During=()=>game.SelectItemReward(true);Assert.IsTrue(game.RetryCheckpoint());store.During=null;Assert.IsFalse(game.RetryCheckpoint());Assert.AreEqual(80,store.Read().Player.Hp);Assert.AreEqual(10,store.Read().Currency);
        }
        [TestCase("before")][TestCase("after")]
        public void 実ファイル保存前後エラーでも同じ結果を再試行し再起動で回復を増やさない(string fault)
        {
            Start("strong",out var original,out var memory);var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-item-reward-"+System.Guid.NewGuid().ToString("N"));
            try{var io=new FaultIo();var disk=new Olca.Unity.FileCheckpointStore(original.Definitions,path,io);Assert.IsTrue(disk.TryWrite(memory.Read(),out var error),error);
                Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(original.Definitions,disk),new MockRandomProvider(999),new MockLogger(),out var game,out error),error);game.ActivateCheckpoint();Kill(game);
                var choices=(List<Card>)typeof(DungeonSystem).GetField("pendingRewardChoices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(game.Dungeon);if(choices!=null)game.SelectCardReward(null);
                game.SelectItemReward(true);io.Fault=fault;game.SelectCurrencyReward(true);if(game.Dungeon.IsCheckpointBlocked){io.Fault=null;Assert.IsTrue(game.RetryCheckpoint());}
                var reopened=new Olca.Unity.FileCheckpointStore(original.Definitions,path);Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(),original.Definitions,out var result,out error),error);Assert.AreEqual(80,result.Player.CurrentHp);Assert.AreEqual(10,result.Snapshot.Currency);Assert.IsNull(result.Snapshot.Battle);
            }finally{if(System.IO.Directory.Exists(path))System.IO.Directory.Delete(path,true);}
        }
        [Test]
        public void 保存済み候補は異なる乱数で再抽選せず旧省略も報酬なしを保つ()
        {
            Start("strong",out var session,out var store);var saved=store.Read();Assert.IsTrue(saved.Battle.ItemRewardIndex.HasValue);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,store),new MockRandomProvider(999),new MockLogger(),out var restored,out var error),error);restored.ActivateCheckpoint();CollectionAssert.AreEqual(saved.Battle.FirstHand,store.Read().Battle.FirstHand);Assert.AreEqual(saved.Battle.ItemRewardIndex,store.Read().Battle.ItemRewardIndex);
            var old=saved.Copy();old.Sequence++;old.Battle.ItemRewardIndex=null;old.Battle.ItemRewardHasNextFloor=null;var oldStore=new MemoryCheckpointStore(session.Definitions);Assert.IsTrue(oldStore.TryWrite(old,out error),error);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,oldStore),new MockRandomProvider(999),new MockLogger(),out var legacy,out error),error);legacy.ActivateCheckpoint();Kill(legacy);if(old.Battle.RewardOffered)legacy.SelectCardReward(null);Assert.IsNull(legacy.Dungeon.PendingItemReward);Assert.AreEqual(60,legacy.Dungeon.Player.CurrentHp);
        }
        [TestCase(true)][TestCase(false)]
        public void 中間ボスの保存復帰は同じ階層条件だけを受け入れる(bool next)
        {
            Start("midboss",out var session,out var store);var before=store.Read();
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,store),new MockRandomProvider(999),new MockLogger(),out var game,out var error,hasNextFloor:next),error);game.ActivateCheckpoint();
            Assert.AreEqual(!next,game.Dungeon.IsCheckpointBlocked);Assert.AreEqual(before.Sequence,store.Read().Sequence);Assert.AreEqual(before.Battle.ItemRewardIndex,store.Read().Battle.ItemRewardIndex);
        }
        [Test]
        public void 空セルを強敵として新規定義に固定せず回復候補率も検証する()
        {
            var m=DungeonConfig.CreateTestDungeon(8,100,7);var invalid=new DungeonConfig(m.DungeonId,m.DungeonName,8,m.MapCells,strongEnemyCells:new[]{2});
            Assert.Throws<System.ArgumentException>(()=>DungeonCheckpointSession.CreateDefault(invalid,new Deck()));
            Assert.Throws<System.ArgumentException>(()=>new DungeonConfig(m.DungeonId,m.DungeonName,8,m.MapCells,itemRewardChance:101));
        }
        [TestCase(-1)][TestCase(99)]
        public void 不正候補添字と資格外候補を拒否する(int index)
        {
            Start("strong",out var session,out var store);var bad=store.Read();bad.Battle.ItemRewardIndex=index;Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));
            Start("finalboss",out session,out store);bad=store.Read();bad.Battle.ItemRewardIndex=0;Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));
        }
        [Test]
        public void 重みゼロ空プール率ゼロと不正値を区別する()
        {
            var item=new FieldItem("heal","ヒール",20);Assert.Throws<System.ArgumentException>(()=>new ItemRewardEntry(item,-1));Assert.Throws<System.ArgumentException>(()=>ItemRewardEntry.Validate(new[]{new ItemRewardEntry(item),new ItemRewardEntry(item)}));
            var other=new FieldItem("fixture-heal","回復fixture",10);Assert.Throws<System.ArgumentException>(()=>ItemRewardEntry.Validate(new[]{new ItemRewardEntry(item,int.MaxValue),new ItemRewardEntry(other,1)}));
            Assert.IsNull(ItemRewardEntry.Pick(new ItemRewardEntry[0],100,new MockRandomProvider()));Assert.IsNull(ItemRewardEntry.Pick(new[]{new ItemRewardEntry(item,0)},100,new MockRandomProvider()));Assert.IsNull(ItemRewardEntry.Pick(new[]{new ItemRewardEntry(item)},0,new MockRandomProvider()));
            var pool=new[]{new ItemRewardEntry(item,1),new ItemRewardEntry(other,3)};Assert.AreEqual(0,ItemRewardEntry.Pick(pool,100,new MockRandomProvider(0)));Assert.AreEqual(1,ItemRewardEntry.Pick(pool,100,new MockRandomProvider(1)));Assert.AreEqual(1,ItemRewardEntry.Pick(pool,100,new MockRandomProvider(3)));
        }
    }
}

namespace Olca.Tests.Integration
{
    public class AttackMilestoneTests
    {
        [TestCase(1,60,12)][TestCase(4,60,12)][TestCase(5,54,11)][TestCase(9,54,11)][TestCase(10,48,10)][TestCase(99,48,10)]
        public void 節目は共通レベルから一度だけ導出し複数レベルでも加算を重ねない(int level,int charge,int interval)
        {
            var g=AttackMilestoneGrowth.ForLevel(level);Assert.AreEqual(charge,g.ChargeFrames(60));Assert.AreEqual(interval,g.IntervalFrames(12));Assert.AreEqual(charge,g.ChargeFrames(60));Assert.AreEqual(1,g.ChargeFrames(1));Assert.AreEqual(1,g.IntervalFrames(1));
        }
        private static Olca.Core.Logic.Battle.BattleSystem Battle(int level,out Enemy enemy)
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0),attackPower:14);p.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(level));enemy=new Enemy("target","検証",500,ElementType.None,new Vector2Int(2,0));return new Olca.Core.Logic.Battle.BattleSystem(p,new List<Enemy>{enemy},new Deck());
        }
        [TestCase(9,false)] [TestCase(10,true)]
        public void 節目射程は通常入力の発生位置から前方を判定し多重発動しない(int level,bool hits)
        {
            var b=Battle(level,out var e);e.MoveTo(new Vector2Int(3,0));var state=new Olca.Application.Battle.BasicAttackInputState();state.Tick(b,new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true},60,12);state.Tick(b,new LogicalInput{BasicAttackButtonUp=true},60,12);Assert.AreEqual(hits?486:500,e.CurrentHp);state.Tick(b,new LogicalInput{BasicAttackButtonUp=true},60,12);Assert.AreEqual(hits?486:500,e.CurrentHp);Assert.AreEqual(hits?2:1,b.BasicAttackRange);
        }
        [Test]
        public void 通常入力は短縮したタメ閾値と間隔を使い二重解放しない()
        {
            var b=Battle(5,out var e);var state=new Olca.Application.Battle.BasicAttackInputState();
            for(int i=0;i<54;i++)b.RunLogicalFrame(()=>state.Tick(b,new LogicalInput{BasicAttackButtonDown=i==0,BasicAttackButton=true},60,12));
            Assert.AreEqual(54,state.ChargeFrames);b.RunLogicalFrame(()=>state.Tick(b,new LogicalInput{BasicAttackButtonUp=true},60,12));Assert.AreEqual(472,e.CurrentHp);Assert.AreEqual(11,state.CooldownFrames);state.Tick(b,new LogicalInput{BasicAttackButtonUp=true},60,12);Assert.AreEqual(472,e.CurrentHp);
        }
        [Test]
        public void スタイルは共通節目を使い変身の専用時間はプレビュー取消確定終了で壊さない()
        {
            var b=Battle(10,out _);b.Transformations.Configure(new[]{new TransformationDefenseDefinition("form",ElementResistances.Neutral,ElementType.Water,attack:new TransformationAttackDefinition(ElementType.Fire,chargeFrames:3,intervalFrames:4))});b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());Assert.IsTrue(b.Transformations.TryPreview("form"));Assert.AreEqual(48,b.ResolveBasicChargeFrames(60));Assert.IsTrue(b.Transformations.TryCancelPending());Assert.AreEqual(10,b.ResolveBasicIntervalFrames(12));b.Transformations.TryPreview("form");b.CustomScreen.Confirm();b.CustomScreen.Close();Assert.AreEqual(3,b.ResolveBasicChargeFrames(60));Assert.AreEqual(4,b.ResolveBasicIntervalFrames(12));Assert.AreEqual(1,b.BasicAttackRange);b.Player.EndTransformationDefense();Assert.AreEqual(48,b.ResolveBasicChargeFrames(60));Assert.AreEqual(10,b.ResolveBasicIntervalFrames(12));
        }
        [Test]
        public void 固有スタイルの元定義へ節目を適用し保存される元値を書き換えない()
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0));p.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(5));var attack=new TransformationAttackDefinition(ElementType.Earth,chargeFrames:30,intervalFrames:8);p.SetDepartureStyle(new DepartureStyleDefinition("fixture","検証",null,attack));var b=new Olca.Core.Logic.Battle.BattleSystem(p,new List<Enemy>{new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0))},new Deck());Assert.AreEqual(27,b.ResolveBasicChargeFrames(60));Assert.AreEqual(7,b.ResolveBasicIntervalFrames(12));Assert.AreEqual(30,attack.ChargeFrames);Assert.AreEqual(8,attack.IntervalFrames);
        }
        [Test]
        public void 新規保存は節目の採用値を固定し旧省略は成長済みHP攻撃から推測しない()
        {
            var map=DungeonConfig.CreateTestDungeon(8,100,7);var deck=new Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);var p=new PlayerCharacter(145,attackPower:19);p.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(10));new DungeonBattleManager(map,p,deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);var dto=session.Store.Read();Assert.AreEqual(80,dto.Player.AttackGrowth.ChargePercent);var copy=dto.Copy();copy.Player.AttackGrowth.ChargePercent=1;Assert.AreEqual(80,session.Store.Read().Player.AttackGrowth.ChargePercent);Assert.IsTrue(CheckpointCodec.TryRestore(dto,session.Definitions,out var restored,out var error),error);Assert.AreEqual(48,restored.Player.AttackGrowth.ChargeFrames(60));Assert.Throws<System.InvalidOperationException>(()=>p.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(20)));
            dto.Player.AttackGrowth=null;Assert.IsTrue(CheckpointCodec.TryRestore(dto,session.Definitions,out restored,out error),error);Assert.IsNull(restored.Player.AttackGrowth);var b=new Olca.Core.Logic.Battle.BattleSystem(restored.Player,new List<Enemy>{new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0))},new Deck());Assert.AreEqual(60,b.ResolveBasicChargeFrames(60));dto.Player.AttackGrowth=new AttackMilestoneDto{Level=10,ChargePercent=0,IntervalReduction=2};Assert.IsFalse(CheckpointCodec.TryRestore(dto,session.Definitions,out _,out _));
        }
    }
}

namespace Olca.Tests.Integration
{
    public class GamepadGameplayInputTests
    {
        private static void State(UnityEngine.InputSystem.Gamepad pad,params UnityEngine.InputSystem.LowLevel.GamepadButton[] held)
        {var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in held)state=state.WithButton(key);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();}
        [Test]
        public void 仮想パッドのゲームボタンは既存の論理入力へ一度だけ立上り保持解放を送る()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var provider=new Olca.Unity.UnityInputProvider();
            try{provider.StartEventCapture();provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West,UnityEngine.InputSystem.LowLevel.GamepadButton.South,UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder,UnityEngine.InputSystem.LowLevel.GamepadButton.North,UnityEngine.InputSystem.LowLevel.GamepadButton.LeftStick);
                var pressed=provider.CaptureTimestamped().Last().Input;Assert.IsTrue(pressed.BasicAttackButtonDown&&pressed.BasicAttackButton&&pressed.JumpButtonDown&&pressed.CardUseButtonDown&&pressed.CustomScreenButtonDown&&pressed.DashButtonDown&&pressed.DashButton);
                State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);var hold=provider.CaptureTimestamped().Last().Input;Assert.IsTrue(hold.BasicAttackButton);Assert.IsFalse(hold.BasicAttackButtonDown);State(pad);var release=provider.CaptureTimestamped().Last().Input;Assert.IsTrue(release.BasicAttackButtonUp);Assert.IsFalse(release.BasicAttackButton);Assert.IsEmpty(provider.CaptureTimestamped());
            }finally{provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test]
        public void キーとパッドの同じ攻撃は合成して片側解放で発射せず最後の解放だけ送る()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var provider=new Olca.Unity.UnityInputProvider();
            try{provider.StartEventCapture();provider.CaptureTimestamped();UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Z));UnityEngine.InputSystem.InputSystem.Update();Assert.IsTrue(provider.CaptureTimestamped().Last().Input.BasicAttackButtonDown);
                State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);var joined=provider.CaptureTimestamped().Last().Input;Assert.IsTrue(joined.BasicAttackButton);Assert.IsFalse(joined.BasicAttackButtonDown);UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();var switched=provider.CaptureTimestamped().Last().Input;Assert.IsTrue(switched.BasicAttackButton);Assert.IsFalse(switched.BasicAttackButtonUp);State(pad);Assert.IsTrue(provider.CaptureTimestamped().Last().Input.BasicAttackButtonUp);
            }finally{provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}
        }
        [Test]
        public void スナップショットと切断は押下を発生させず保持中の復帰を中立まで遮断する()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var provider=new Olca.Unity.UnityInputProvider();
            try{provider.StartEventCapture();provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);provider.CaptureTimestamped();provider.QueueSnapshot();var snapshot=provider.CaptureTimestamped().Last().Input;Assert.IsFalse(snapshot.BasicAttackButton||snapshot.BasicAttackButtonDown||snapshot.BasicAttackButtonUp);
                State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);Assert.IsFalse(provider.CaptureTimestamped().Last().Input.BasicAttackButton);State(pad);provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);Assert.IsTrue(provider.CaptureTimestamped().Last().Input.BasicAttackButtonDown);
                UnityEngine.InputSystem.InputSystem.DisableDevice(pad);var removed=provider.CaptureTimestamped().Last().Input;Assert.IsFalse(removed.BasicAttackButton||removed.BasicAttackButtonUp);UnityEngine.InputSystem.InputSystem.EnableDevice(pad);State(pad);provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);Assert.IsTrue(provider.CaptureTimestamped().Last().Input.BasicAttackButtonDown);
            }finally{provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test]
        public void 十字キーは移動軸へ入り中心復帰で残留せずキーボード再割当を変えない()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var provider=new Olca.Unity.UnityInputProvider();
            try{var profile=provider.KeyboardProfile;provider.StartEventCapture();provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp);var moved=provider.CaptureTimestamped().Last().Input;Assert.AreEqual(1,moved.Horizontal);Assert.AreEqual(1,moved.Vertical);Assert.AreEqual(1,moved.HorizontalDown);State(pad);var neutral=provider.CaptureTimestamped().Last().Input;Assert.AreEqual(0,neutral.Horizontal);Assert.AreEqual(0,neutral.Vertical);Assert.AreSame(profile,provider.KeyboardProfile);
            }finally{provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
    }
}

namespace Olca.Tests.Integration
{
    public class CustomGamepadInputTests
    {
        private static void State(UnityEngine.InputSystem.Gamepad pad,params UnityEngine.InputSystem.LowLevel.GamepadButton[] buttons)
        {var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in buttons)state=state.WithButton(key);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();}
        [Test]
        public void 無効場面と保持復帰を遮断し新しい押下だけを一回採取する()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();bool active=false;var input=new Olca.Unity.CustomGamepadInput(()=>active);
            try{State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);active=true;Assert.IsEmpty(input.Read());State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.IsEmpty(input.Read());State(pad);Assert.IsEmpty(input.Read());State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(Olca.Unity.CustomPadCommand.Decide,input.Read().Single().Command);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.IsEmpty(input.Read());active=false;input.Read();active=true;input.Read();State(pad);Assert.IsEmpty(input.Read());}
            finally{input.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test]
        public void 同時ボタンは確定を優先し方向の保持と同じ状態の再送で決定を重複しない()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var input=new Olca.Unity.CustomGamepadInput(()=>true);
            try{input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South,UnityEngine.InputSystem.LowLevel.GamepadButton.Start,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight);CollectionAssert.AreEqual(new[]{Olca.Unity.CustomPadCommand.Right,Olca.Unity.CustomPadCommand.Confirm},input.Read().Select(e=>e.Command));State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South,UnityEngine.InputSystem.LowLevel.GamepadButton.Start,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight);Assert.IsEmpty(input.Read());State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.East,UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(Olca.Unity.CustomPadCommand.Cancel,input.Read().Single().Command);}
            finally{input.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test]
        public void 切断やリセットは未処理入力と世代を破棄し別デバイスへ移動を残留させない()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var input=new Olca.Unity.CustomGamepadInput(()=>true);
            try{input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.North);var old=input.Read().Single();input.Reset();Assert.AreNotEqual(old.Epoch,input.Epoch);State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.West);UnityEngine.InputSystem.InputSystem.DisableDevice(pad);Assert.IsEmpty(input.Read());UnityEngine.InputSystem.InputSystem.EnableDevice(pad);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.RightStick);Assert.IsEmpty(input.Read(),"再接続直後の保持は中立まで無視");State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.RightStick);Assert.AreEqual(Olca.Unity.CustomPadCommand.Details,input.Read().Single().Command);State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.Select);Assert.AreEqual(Olca.Unity.CustomPadCommand.Transform,input.Read().Single().Command);}
            finally{input.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
    }
    public class MenuPadScopeTests
    {
        private static void State(UnityEngine.InputSystem.Gamepad pad,params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys)
        {var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in keys)state=state.WithButton(key);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();}
        [Test]
        public void スライダーは左右で調整し上下で移動して切替は一押し一回だけ行う()
        {
            var root=new UnityEngine.GameObject("Settings",typeof(UnityEngine.RectTransform));var sliderObject=new UnityEngine.GameObject("Volume",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Slider));sliderObject.transform.SetParent(root.transform);((UnityEngine.RectTransform)sliderObject.transform).anchoredPosition=new UnityEngine.Vector2(0,100);var slider=sliderObject.GetComponent<UnityEngine.UI.Slider>();slider.minValue=0;slider.maxValue=2;slider.wholeNumbers=true;slider.value=1;var toggleObject=new UnityEngine.GameObject("Switch",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Toggle));toggleObject.transform.SetParent(root.transform);var toggle=toggleObject.GetComponent<UnityEngine.UI.Toggle>();toggle.isOn=false;var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var menu=new Olca.Unity.MenuPadScope(()=>true);
            try{menu.Bind(root.transform,"Volume",()=>{});State(pad);menu.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft);menu.Tick();Assert.AreEqual(0,slider.value);State(pad);menu.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft);menu.Tick();Assert.AreEqual(0,slider.value);State(pad);menu.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight);menu.Tick();Assert.AreEqual(1,slider.value);State(pad);menu.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);menu.Tick();Assert.AreEqual("Switch",menu.FocusName);State(pad);menu.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);menu.Tick();Assert.IsTrue(toggle.isOn);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);menu.Tick();Assert.IsTrue(toggle.isOn);}
            finally{menu.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test]
        public void 同じボタンを再表示しても前回焦点より指定した安全な初期焦点を優先する()
        {
            var existing=UnityEngine.EventSystems.EventSystem.current;var previous=existing?.currentSelectedGameObject;var es=existing!=null?existing:new UnityEngine.GameObject("MenuEventSystem",typeof(UnityEngine.EventSystems.EventSystem)).GetComponent<UnityEngine.EventSystems.EventSystem>();var root=new UnityEngine.GameObject("Reward",typeof(UnityEngine.RectTransform));var receive=new UnityEngine.GameObject("Receive",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));receive.transform.SetParent(root.transform);var skip=new UnityEngine.GameObject("Skip",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));skip.transform.SetParent(root.transform);var menu=new Olca.Unity.MenuPadScope(()=>true);
            try{if(existing==null)typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(es,null);UnityEngine.EventSystems.EventSystem.current=es;es.SetSelectedGameObject(receive);menu.Bind(root.transform,"Skip",()=>{});Assert.AreEqual(skip,es.currentSelectedGameObject);es.SetSelectedGameObject(receive);root.SetActive(false);root.SetActive(true);menu.Bind(root.transform,"Skip",()=>{});Assert.AreEqual("Skip",menu.FocusName);Assert.AreEqual(skip,es.currentSelectedGameObject);}
            finally{menu.Dispose();es.SetSelectedGameObject(previous);UnityEngine.Object.DestroyImmediate(root);if(existing==null){typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(es,null);UnityEngine.Object.DestroyImmediate(es.gameObject);}}
        }
        [Test]
        public void 受領後の次報酬へ保持中の決定取消を持ち越さない()
        {
            var first=new UnityEngine.GameObject("CardReward",typeof(UnityEngine.RectTransform));var second=new UnityEngine.GameObject("CurrencyReward",typeof(UnityEngine.RectTransform));var a=new UnityEngine.GameObject("Receive",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));a.transform.SetParent(first.transform);var b=new UnityEngine.GameObject("Skip",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));b.transform.SetParent(second.transform);second.SetActive(false);var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var scope=new Olca.Unity.MenuPadScope(()=>true);int received=0,skipped=0;
            try{a.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>{received++;first.SetActive(false);second.SetActive(true);scope.Bind(second.transform,"Skip",()=>skipped++);});b.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>skipped++);scope.Bind(first.transform,"Receive",()=>{});State(pad);scope.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();Assert.AreEqual(1,received);Assert.AreEqual(0,skipped);State(pad);scope.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.East,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();Assert.AreEqual(1,skipped,"同時入力は取消だけ");}
            finally{scope.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);}
        }
        [Test]
        public void 再描画を伴う決定は保持で重複せず背面と切断復帰を遮断する()
        {
            var root=new UnityEngine.GameObject("Menu",typeof(UnityEngine.RectTransform));var obj=new UnityEngine.GameObject("Decide",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));obj.transform.SetParent(root.transform);var button=obj.GetComponent<UnityEngine.UI.Button>();var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();bool top=true;int count=0,cancel=0;var scope=new Olca.Unity.MenuPadScope(()=>top);
            try{button.onClick.AddListener(()=>{count++;scope.Bind(root.transform,"Decide",()=>cancel++);});scope.Bind(root.transform,"Decide",()=>cancel++);State(pad);scope.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();Assert.AreEqual(1,count);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();Assert.AreEqual(1,count);State(pad);scope.Tick();top=false;State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.East);scope.Tick();Assert.AreEqual(0,cancel);top=true;scope.Tick();State(pad);scope.Tick();UnityEngine.InputSystem.InputSystem.DisableDevice(pad);scope.Tick();UnityEngine.InputSystem.InputSystem.EnableDevice(pad);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.South);scope.Tick();Assert.AreEqual(1,count);State(pad);scope.Tick();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.East);scope.Tick();Assert.AreEqual(1,cancel);}
            finally{scope.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.Object.DestroyImmediate(root);}
        }
    }

}

namespace Olca.Tests.Integration {
 [NUnit.Framework.TestFixture] public class IndependentGamepadBindingTests {
  static void State(UnityEngine.InputSystem.Gamepad pad,params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in keys){if(key==UnityEngine.InputSystem.LowLevel.GamepadButton.LeftTrigger)state.leftTrigger=1;else if(key==UnityEngine.InputSystem.LowLevel.GamepadButton.RightTrigger)state.rightTrigger=1;else state=state.WithButton(key);}UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();}
  sealed class FaultIo:Olca.Unity.CheckpointFileIo {public override void MoveNew(string a,string b){throw new System.IO.IOException("own fixture write failure");}public override void Replace(string a,string b,string c){throw new System.IO.IOException("own fixture replace failure");}}
  [NUnit.Framework.Test] public void パッドの交換と必須条件はキーボードを変更せず独立保存再開できる(){string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-pad-"+System.Guid.NewGuid().ToString("N"));try{var kb=new Olca.Unity.KeyboardBindingFileStore(dir);NUnit.Framework.Assert.IsTrue(kb.TrySave(Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile(),out _));var original=System.IO.File.ReadAllBytes(kb.ActivePath);var defaults=Olca.Unity.UnityInputProvider.CreateDefaultGamepadProfile();NUnit.Framework.Assert.AreEqual("South",defaults.Bindings[("gamepad","exploration","JumpPrimary")]);NUnit.Framework.Assert.AreEqual("West",defaults.Bindings[("gamepad","battle","AttackPrimary")]);var edit=defaults.BeginEdit();var attack=("gamepad","battle","AttackPrimary");var card=("gamepad","battle","CardPrimary");NUnit.Framework.Assert.IsTrue(edit.TryAssign(attack,defaults.Bindings[card],out _));NUnit.Framework.Assert.AreEqual(defaults.Bindings[attack],edit.Preview().Bindings[card]);NUnit.Framework.Assert.IsFalse(edit.TryAssign(("gamepad","menu","Decide"),null,out _));NUnit.Framework.Assert.IsFalse(edit.TryAssign(("gamepad","battle","Pause"),null,out _));var store=new Olca.Unity.GamepadBindingFileStore(dir);NUnit.Framework.Assert.IsTrue(store.TrySave(edit.Preview(),out _));NUnit.Framework.Assert.IsTrue(new Olca.Unity.GamepadBindingFileStore(dir).TryRead(out var restored,out _));NUnit.Framework.Assert.AreEqual(defaults.Bindings[card],restored.Bindings[attack]);NUnit.Framework.CollectionAssert.AreEqual(original,System.IO.File.ReadAllBytes(kb.ActivePath));NUnit.Framework.Assert.IsFalse(new Olca.Unity.GamepadBindingFileStore(dir,new FaultIo()).TrySave(defaults,out _));NUnit.Framework.Assert.IsTrue(store.TryRead(out restored,out _));NUnit.Framework.Assert.AreEqual(defaults.Bindings[card],restored.Bindings[attack]);edit.Discard();NUnit.Framework.Assert.AreEqual(defaults.Bindings[attack],edit.Preview().Bindings[attack]);}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}}
  [NUnit.Framework.Test] public void 必須操作なしや不正ボタンを保存前に拒否する(){var defaults=Olca.Unity.UnityInputProvider.CreateDefaultGamepadProfile();var slots=defaults.Bindings.ToDictionary(p=>p.Key,p=>p.Value);slots[("gamepad","menu","Cancel")]=null;var missing=new Olca.Core.Domain.InputBindingProfile(slots,new (string,string,string)[0]);NUnit.Framework.Assert.IsFalse(Olca.Unity.UnityInputProvider.ValidateGamepadProfile(missing,out _));slots[("gamepad","menu","Cancel")]=defaults.Bindings[("gamepad","menu","Cancel")];slots[("gamepad","battle","AttackPrimary")]="999";NUnit.Framework.Assert.IsFalse(Olca.Unity.UnityInputProvider.ValidateGamepadProfile(new Olca.Core.Domain.InputBindingProfile(slots,new (string,string,string)[0]),out _));}
  [NUnit.Framework.Test] public void 再割当を通常イベントとメニューへ反映し保持と切断復帰で誤発動しない(){var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var provider=new Olca.Unity.UnityInputProvider();var input=new Olca.Unity.CustomGamepadInput(()=>true,context:"menu");try{var edit=Olca.Unity.UnityInputProvider.CreateDefaultGamepadProfile().BeginEdit();NUnit.Framework.Assert.IsTrue(edit.TryAssign(("gamepad","battle","AttackPrimary"),"RightTrigger",out _));NUnit.Framework.Assert.IsTrue(edit.TryAssign(("gamepad","menu","Decide"),"LeftTrigger",out _));NUnit.Framework.Assert.IsTrue(provider.TryRestoreGamepadProfile(edit.Preview(),out _));provider.GamepadContext=()=>"battle";provider.GamepadPauseAvailable=()=>true;provider.StartEventCapture();provider.CaptureTimestamped();input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.RightTrigger);NUnit.Framework.Assert.IsTrue(provider.CaptureTimestamped().Last().Input.BasicAttackButtonDown);State(pad);provider.CaptureTimestamped();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.Start);NUnit.Framework.Assert.IsTrue(provider.ConsumeGamepadPause());NUnit.Framework.Assert.IsFalse(provider.ConsumeGamepadPause());State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.Start);NUnit.Framework.Assert.IsFalse(provider.ConsumeGamepadPause());State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.LeftTrigger);NUnit.Framework.Assert.AreEqual(Olca.Unity.CustomPadCommand.Decide,input.Read().Single().Command);UnityEngine.InputSystem.InputSystem.DisableDevice(pad);input.Read();UnityEngine.InputSystem.InputSystem.EnableDevice(pad);State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.LeftTrigger);NUnit.Framework.Assert.IsEmpty(input.Read());State(pad);input.Read();State(pad,UnityEngine.InputSystem.LowLevel.GamepadButton.LeftTrigger);NUnit.Framework.Assert.AreEqual(Olca.Unity.CustomPadCommand.Decide,input.Read().Single().Command);}finally{input.Dispose();provider.Dispose();var reset=new Olca.Unity.UnityInputProvider();reset.TryRestoreGamepadProfile(Olca.Unity.UnityInputProvider.CreateDefaultGamepadProfile(),out _);reset.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}}
 }
}

namespace Olca.Tests.Integration
{
    public class BattleEvaluationTests
    {
        [TestCase(100,BattleEvaluationGrade.Excellent)] [TestCase(90,BattleEvaluationGrade.Excellent)]
        [TestCase(89,BattleEvaluationGrade.Great)] [TestCase(75,BattleEvaluationGrade.Great)]
        [TestCase(74,BattleEvaluationGrade.Good)] [TestCase(50,BattleEvaluationGrade.Good)]
        [TestCase(49,BattleEvaluationGrade.Fair)] [TestCase(0,BattleEvaluationGrade.Fair)]
        public void 暫定の評価境界は下限を含み四段階へ写像する(int points,BattleEvaluationGrade expected)
            =>Assert.AreEqual(expected,BattleEvaluationRules.ProvisionalNormal.GradeForPoints(points));

        [TestCase(0,100)] [TestCase(1800,100)] [TestCase(1801,99)]
        [TestCase(4500,75)] [TestCase(7200,50)] [TestCase(long.MaxValue,50)]
        public void 時間配点は論理フレームの両端と整数切捨てと上限を守る(long frames,int points)
            =>Assert.AreEqual(points,BattleEvaluationRules.ProvisionalNormal.Evaluate(frames,0,1,1).Points);

        [Test]
        public void のけぞりと同時撃破は独立配点で単体戦と最大組だけを評価する()
        {
            var r=BattleEvaluationRules.ProvisionalNormal;
            Assert.AreEqual(100,r.Evaluate(0,0,1,1).Points);
            Assert.AreEqual(80,r.Evaluate(0,0,1,3).Points);
            Assert.AreEqual(90,r.Evaluate(0,0,2,3).Points);
            Assert.AreEqual(100,r.Evaluate(0,0,3,3).Points);
            Assert.AreEqual(90,r.Evaluate(0,1,3,3).Points);
            Assert.AreEqual(70,r.Evaluate(0,int.MaxValue,int.MaxValue,3).Points);
        }
        [Test]
        public void 通常とボスの基準は独立して差替え可能で不正定義を拒否する()
        {
            Assert.AreEqual(66,BattleEvaluationRules.ProvisionalNormal.Evaluate(5400,0,1,1).Points);
            Assert.AreEqual(91,BattleEvaluationRules.ProvisionalBoss.Evaluate(5400,0,1,1,true).Points);
            var custom=new BattleEvaluationRules(100,0,0,1,2,1,99,98,97);
            Assert.AreEqual(100,custom.Evaluate(1,100,0,5).Points);Assert.AreEqual(0,custom.Evaluate(2,0,5,5).Points);
            Assert.Throws<System.ArgumentException>(()=>new BattleEvaluationRules(50,30,30,1,2,1,90,75,50));
            Assert.Throws<System.ArgumentException>(()=>new BattleEvaluationRules(50,30,20,2,2,1,90,75,50));
            Assert.Throws<System.ArgumentException>(()=>new BattleEvaluationRules(50,30,20,1,2,0,90,75,50));
            Assert.Throws<System.ArgumentException>(()=>new BattleEvaluationRules(50,30,20,1,2,1,75,90,50));
            Assert.Throws<System.ArgumentException>(()=>custom.Evaluate(-1,0,1,1));
            Assert.Throws<System.ArgumentException>(()=>custom.Evaluate(0,0,0,0));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void 評価は描画頻度と重複停止に依存せず召喚でも開始時分母と確定値を保持する(int fps)
        {
            var enemies=new List<Enemy>{new Enemy("a","テスト",10,ElementType.None,new Vector2Int(3,0)),new Enemy("b","テスト",10,ElementType.None,new Vector2Int(4,0))};
            var b=new BattleSystem(new PlayerCharacter(10000,new Vector2Int(0,0)),enemies,new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
            var rules=new BattleEvaluationRules(50,30,20,0,120,10,90,75,50);
            b.AddSummon(new Enemy("summon","テスト",10,ElementType.None,new Vector2Int(5,0),true),new List<IEnemyAIAction>{new BasicAttackAction(0,10000)});Assert.AreEqual(2,b.InitialEvaluationEnemyCount);
            b.Pause(BattlePauseReason.CustomScreen);b.Pause(BattlePauseReason.User);for(int i=0;i<60;i++)b.TickFrame(17);
            b.Resume(BattlePauseReason.User);for(int i=0;i<60;i++)b.TickFrame(17);Assert.AreEqual(0,b.LogicalFrame);b.Resume(BattlePauseReason.CustomScreen);
            Assert.IsTrue(b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,6,()=>{}));
            b.RunLogicalFrame(()=>enemies[0].TakeDamage(1,ElementType.None,hitStopFrames:6));for(int i=0;i<12;i++)b.TickFrame(17);Assert.AreEqual(0,b.LogicalFrame);
            var clock=new LogicalFrameClock();long previous=0;
            for(int render=1;render<=fps;render++){long now=render*LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(now-previous);previous=now;clock.Drain((_,ms)=>b.TickFrame(ms),3);}
            while(clock.PendingFrames>0)clock.Drain((_,ms)=>b.TickFrame(ms),3);
            Assert.AreEqual(60,b.LogicalFrame);
            b.RunLogicalFrame(()=>{foreach(var e in enemies.ToArray())e.TakeDamage(1000,ElementType.None);});
            var value=rules.Evaluate(b);Assert.AreEqual(75,value.Points);Assert.AreEqual(3,value.MaximumSimultaneousKills);Assert.AreEqual(2,value.InitialEnemyCount);
            for(int i=0;i<60;i++)b.TickFrame(17);Assert.AreEqual(value.LogicalFrames,b.LogicalFrame);Assert.AreEqual(75,value.Points);
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void 勝利評価は演出と報酬より先に一度確定し敗北は評価を作らない(bool boss,bool defeat)
        {
            var cells=Enumerable.Range(0,20).Select(i=>new MapCell(i)).ToList();
            var e=new Enemy(boss?"boss":"slime","テスト",50,ElementType.None,new Vector2Int(3,0));cells[3]=new MapCell(3,e);
            cells[19]=new MapCell(19,new Enemy("boss","テスト",500,ElementType.None,new Vector2Int(19,0)));
            var m=new DungeonBattleManager(new DungeonConfig("evaluation","テスト",20,cells),new PlayerCharacter(100),new Deck(new List<Card>()),new MockRandomProvider(),new MockLogger(),
                normalEvaluation:new BattleEvaluationRules(100,0,0,0,120,1,90,75,50),bossEvaluation:new BattleEvaluationRules(0,100,0,0,120,1,90,75,50));
            m.RequireBattleEndPresentation();m.Dungeon.StartBattle(new ContactInfo{Enemy=e,EnemyCellIndex=3});var b=m.CurrentBattle;
            Assert.IsNull(m.LastBattleEvaluation);b.RunLogicalFrame(()=>{e.TakeDamage(1000,ElementType.None);if(defeat)b.Player.TakeDamage(1000);});
            Assert.IsTrue(m.IsBattleEndPresentationPending);
            var snapshot=m.LastBattleEvaluation;if(defeat)Assert.IsNull(snapshot);else{Assert.AreEqual(100,snapshot.Points);Assert.AreEqual(boss,snapshot.IsBoss);}
            Assert.IsTrue(m.CompleteBattleEndPresentation());Assert.IsFalse(m.CompleteBattleEndPresentation());Assert.AreSame(snapshot,m.LastBattleEvaluation);
            b.TickFrame(17);Assert.AreSame(snapshot,m.LastBattleEvaluation);
        }
        [Test]
        public void 報酬サマリーは評価と確定論理時間だけをカナで表示する()
        {
            var value=BattleEvaluationRules.ProvisionalNormal.Evaluate(60,2,1,3);
            string text=Olca.Presentation.Dungeon.CardSelectionView.FormatBattleSummary(999,value);
            Assert.AreEqual("ヒョウカ: リョウ / ゲキハタイム: 00:01.00",text);
            Assert.IsFalse(text.Contains("60"));Assert.IsFalse(text.Contains("ノケゾリ"));Assert.IsFalse(text.Contains("ドウジ"));
            Assert.AreEqual("",Olca.Presentation.Dungeon.CardSelectionView.FormatBattleSummary(null));
        }
    }
}

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class EnemyAIDefinitionSupplyTests
    {
        private sealed class Probe : IEnemyAIAction
        {
            public int Calls;
            public bool CanExecute(AIContext context)=>true;
            public void Execute(AIContext context){Calls++;}
            public int GetCooldownMs()=>0;
            public float GetCooldown()=>0;
        }
        private static EnemyAIRegistry Registry(System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration> factory, params string[] ids)
            =>new EnemyAIRegistry(ids.ToDictionary(id=>id,id=>factory));
        private static EnemyAIRegistration Definition(Probe initial,Probe low)
            =>new EnemyAIRegistration(new[]{new EnemyActionPattern(7,new[]{initial},EnemyPatternEndMode.Stop),new EnemyActionPattern(8,new[]{low},EnemyPatternEndMode.Stop)},7,
                new[]{EnemyHpActionPattern.ForRegisteredPattern(5000,true,8),EnemyHpActionPattern.ForRegisteredPattern(10000,true,7)});
        private static void Contact(DungeonBattleManager manager)
        { for(int i=0;i<100&&manager.CurrentBattle==null;i++)manager.Tick(50);Assert.IsNotNull(manager.CurrentBattle); }
        private static void Run(BattleSystem battle)
        {if(battle.CustomScreen.IsOpen)battle.CustomScreen.Cancel();for(int i=0;i<3;i++)battle.TickFrame(17);}

        [TestCase(false)] [TestCase(true)]
        public void 通常接敵と戦闘開始保存復帰へ同じ定義元を再供給し状態と保存を共有しない(bool checkpoints)
        {
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var cards=new Olca.Application.Battle.SimpleCardFactory();
            var deck=cards.CreateStarterDeck();var session=checkpoints?DungeonCheckpointSession.CreateDefault(map,deck):null;
            var probes=new List<Probe>();var low=new List<Probe>();var owners=new List<Enemy>();
            var registry=Registry((enemy,database)=>{Assert.AreSame(cards,database);owners.Add(enemy);var a=new Probe();var b=new Probe();probes.Add(a);low.Add(b);return Definition(a,b);},map.MapCells.Where(c=>c.Enemy!=null).Select(c=>c.Enemy.EnemyId).Distinct().ToArray());
            var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,cardDatabase:cards,enemyAIProvider:registry);
            Contact(game);Assert.Greater(probes.Count,0);Assert.IsTrue(probes.All(x=>x.Calls==0));
            var battle=game.CurrentBattle;var count=probes.Count;var snapshot=session?.Store.Read();var before=snapshot==null?null:PortableCheckpointJson.Serialize(snapshot);
            Run(battle);Assert.IsTrue(probes.All(x=>x.Calls==1));Run(battle);Assert.IsTrue(probes.All(x=>x.Calls==1));
            battle.Enemies[0].TakeDamage((battle.Enemies[0].MaxHp+1)/2,ElementType.None);Run(battle);Assert.AreEqual(1,low[0].Calls);
            if(!checkpoints)return;
            Assert.AreEqual(before,PortableCheckpointJson.Serialize(session.Store.Read()),"AI実行で保存境界を変更しない");
            Assert.IsTrue(game.SuspendForCheckpoint());
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error,cardDatabase:cards,enemyAIProvider:registry),error);
            restored.ActivateCheckpoint();Assert.AreEqual(count*2,probes.Count);Assert.AreNotSame(owners[0],owners[count]);
            Assert.IsTrue(probes.Skip(count).All(x=>x.Calls==0));Run(restored.CurrentBattle);Assert.IsTrue(probes.Skip(count).All(x=>x.Calls==1));
            Assert.AreEqual(1,low[0].Calls);Assert.AreEqual(0,low[count].Calls,"開始保存にない途中HP/AI状態を復元しない");
            Assert.AreEqual(before,PortableCheckpointJson.Serialize(session.Store.Read()));
        }

        [Test]
        public void 同じ敵IDの複数個体にもfactoryを個別実行し定義状態を共有しない()
        {
            var calls=new List<Probe>();var registry=Registry((e,c)=>{var a=new Probe();calls.Add(a);return Definition(a,new Probe());},"same");
            var enemies=new List<Enemy>{new Enemy("same","テスト",30,ElementType.None,new Vector2Int(3,0)),new Enemy("same","テスト",30,ElementType.None,new Vector2Int(4,0))};
            var battle=new BattleSystem(new PlayerCharacter(100),enemies,new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck(),enemyAIProvider:registry);
            Run(battle);Assert.AreEqual(2,calls.Count);Assert.IsTrue(calls.All(x=>x.Calls==1));Assert.AreNotSame(calls[0],calls[1]);
        }

        [TestCase(false)] [TestCase(true)]
        public void 未登録敵と供給元なしは従来攻撃と保存形式を維持する(bool emptyRegistry)
        {
            var p=new PlayerCharacter(100);p.MoveTo(new Vector2Int(0,0));var enemy=new Enemy("legacy","テスト",30,ElementType.None,new Vector2Int(1,0));
            var battle=new BattleSystem(p,new List<Enemy>{enemy},new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck(),enemyAIProvider:emptyRegistry?Registry((e,c)=>throw new System.Exception("未登録を生成した"),"other"):null);
            Run(battle);Assert.AreEqual(90,p.CurrentHp);Assert.AreEqual(30,battle.Enemies[0].CurrentHp);
        }

        [Test]
        public void 通常バトルで登録カードと待ちと停止を使用し未登録敵だけ既定を使う()
        {
            var cards=new Olca.Application.Battle.SimpleCardFactory();var enemy=new Enemy("caster","テスト",30,ElementType.None,new Vector2Int(1,0));enemy.TakeDamage(15,ElementType.None);
            var registry=Registry((e,db)=>new EnemyAIRegistration(new[]{new EnemyActionPattern(1,new IEnemyAIAction[]{new EnemyCardAction(db.GetCardById("heal"),0,db)},EnemyPatternEndMode.Stop)},1),"caster");
            var battle=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{enemy},cards.CreateStarterDeck(),cardDatabase:cards,enemyAIProvider:registry);
            Run(battle);Assert.Greater(enemy.CurrentHp,15);Assert.AreEqual(100,battle.Player.CurrentHp);
        }

        [Test]
        public void 登録元は辞書の後続変更から保護し登録済みnull定義を黙って既定へ落とさない()
        {
            var entries=new Dictionary<string,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>>{{"enemy",(e,c)=>Definition(new Probe(),new Probe())}};
            var registry=new EnemyAIRegistry(entries);entries.Clear();Assert.IsNotNull(registry.Create(new Enemy("enemy","テスト",30,ElementType.None,new Vector2Int(1,0)),null));
            Assert.Throws<System.ArgumentException>(()=>new EnemyAIRegistry(new Dictionary<string,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>>{{"",(e,c)=>null}}));
            Assert.Throws<System.InvalidOperationException>(()=>Registry((e,c)=>null,"enemy").Create(new Enemy("enemy","テスト",30,ElementType.None,new Vector2Int(1,0)),null));
        }

        [Test]
        public void Controllerは起動前だけ定義元を受け取り攻略中の差替えを拒否する()
        {
            var host=new UnityEngine.GameObject("ai-definition-supply");host.SetActive(false);
            try{var c=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var provider=Registry((e,d)=>Definition(new Probe(),new Probe()),"slime");c.ConfigureEnemyAIProvider(provider);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                Assert.AreSame(provider,c.GetType().GetField("enemyAIProvider",flags).GetValue(c));
                var cards=new Olca.Application.Battle.SimpleCardFactory();var manager=new DungeonBattleManager(DungeonConfig.CreateTestDungeon(20),new PlayerCharacter(100),cards.CreateStarterDeck(),new MockRandomProvider(),new MockLogger());
                c.GetType().GetField("manager",flags).SetValue(c,manager);Assert.Throws<System.InvalidOperationException>(()=>c.ConfigureEnemyAIProvider(null));
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}

namespace Olca.Tests.Integration
{
 [TestFixture]
 public class EnemyAIEventDefinitionRestoreTests
 {
  [TestCase(false)] [TestCase(true)]
  public void 通常接敵と開始保存復帰で条件と召喚定義を新規生成し旧召喚状態を保存へ混入しない(bool beforeContact)
  {
   var map=DungeonConfig.CreateTestDungeon(20,30,3);var cards=new Olca.Application.Battle.SimpleCardFactory();var deck=cards.CreateStarterDeck();var session=DungeonCheckpointSession.CreateDefault(map,deck);int spawned=0;var rules=new List<object>();
   var entries=map.MapCells.Where(c=>c.Enemy!=null).Select(c=>c.Enemy.EnemyId).Distinct().ToDictionary(id=>id,id=>(System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>)((enemy,db)=>{
    var identity=new object();rules.Add(identity);
    var summon=new EnemySummonDefinition(context=>{spawned++;return new Enemy("child","コドモ",20,ElementType.None,new Vector2Int(4,0),isSummoned:true);},(e,c)=>new EnemyAIRegistration(new[]{new EnemyActionPattern(7,new[]{new EnemyMovementAction(EnemyMovementKind.Stay,0)},EnemyPatternEndMode.Stop)},7));
    var card=new Card("fixture-summon","ショウカン",1,CardRarity.Normal,CardCategory.Support,ElementType.None,CardSeries.Sword,0,"");
    return new EnemyAIRegistration(new[]{new EnemyActionPattern(1,new[]{new EnemyMovementAction(EnemyMovementKind.Stay,0)},EnemyPatternEndMode.Stop),new EnemyActionPattern(2,new IEnemyAIAction[]{new EnemyCardAction(card,0,db,summon)},EnemyPatternEndMode.Stop)},1,switchRule:(e,f)=>f.LogicalFrame>=2?2:(int?)null);
   }));
   var registry=new EnemyAIRegistry(entries);var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,cardDatabase:cards,enemyAIProvider:registry);
   if(beforeContact){Assert.IsTrue(game.SuspendForCheckpoint());Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out game,out var error,cardDatabase:cards,enemyAIProvider:registry),error);game.ActivateCheckpoint();}
   for(int i=0;i<100&&game.CurrentBattle==null;i++)game.Tick(50);Assert.IsNotNull(game.CurrentBattle);
   var saved=PortableCheckpointJson.Serialize(session.Store.Read());var old=game.CurrentBattle;old.CustomScreen.Cancel();for(int i=0;i<5;i++)old.TickFrame(17);
   Assert.AreEqual(1,spawned);var child=old.Enemies.First(e=>e.IsSummoned);Assert.IsTrue(old.TrySwitchRegisteredEnemyPattern(child,7));
   Assert.AreEqual(saved,PortableCheckpointJson.Serialize(session.Store.Read()));Assert.IsTrue(game.SuspendForCheckpoint());
   Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var resumed,out var restoreError,cardDatabase:cards,enemyAIProvider:registry),restoreError);resumed.ActivateCheckpoint();
   Assert.IsFalse(resumed.CurrentBattle.Enemies.Any(e=>e.IsSummoned));Assert.AreEqual(0,resumed.CurrentBattle.LogicalFrame);resumed.CurrentBattle.CustomScreen.Cancel();for(int i=0;i<5;i++)resumed.CurrentBattle.TickFrame(17);
   Assert.AreEqual(2,spawned);Assert.AreNotSame(child,resumed.CurrentBattle.Enemies.First(e=>e.IsSummoned));Assert.AreEqual(2,rules.Count);Assert.AreNotSame(rules[0],rules[1]);Assert.AreEqual(saved,PortableCheckpointJson.Serialize(session.Store.Read()));
  }
 }
}
