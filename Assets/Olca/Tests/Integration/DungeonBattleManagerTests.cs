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
        private DungeonBattleManager manager;
        private DungeonCheckpointSession session;
        private FailingStore store;
        private SeedRandom random;
        private void Create(bool failDeparture = false)
        {
            var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var deck = new Deck(new List<Card> { factory.CreateById("sword"), factory.CreateById("sword"), factory.CreateById("heal") });
            var defaults = DungeonCheckpointSession.CreateDefault(map, deck);
            store = new FailingStore(defaults.Definitions) { Fail = failDeparture };
            session = new DungeonCheckpointSession(defaults.Definitions, store); random = new SeedRandom();
            manager = new DungeonBattleManager(map, new PlayerCharacter(100), deck, random, new MockLogger(), checkpoints: session);
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
            manager = new DungeonBattleManager(Map(damage), new PlayerCharacter(100), Deck(), new MockRandomProvider(), new MockLogger(), checkpoints: session);
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
        public void 通常融合と未定義超越は既定マップの保存を阻害しない(string operation)
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
            if (operation == "fusion") Assert.IsTrue(result.CanExecute, result.Reason);
            else { Assert.IsFalse(result.CanExecute); StringAssert.Contains("未定義", result.Reason); }
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
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 十七msの待機は開始位相によらず二論理フレーム待つ(int phase)
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> {
                new Enemy("frame", "fixture", 100, ElementType.None, new Vector2Int(5, 2)) }, new Deck());
            var clock = new LogicalFrameClock(); clock.Accumulate(10000000);
            if (phase > 0) clock.Drain((f, ms) => battle.TickFrame(ms), phase);
            var method = typeof(BattleSystem).GetMethod("WaitForActionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var wait = (UniTask)method.Invoke(battle, new object[] { .017f, System.Threading.CancellationToken.None });
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.AreEqual(UniTaskStatus.Pending, wait.Status);
            clock.Drain((f, ms) => battle.TickFrame(ms), 1);
            Assert.AreEqual(UniTaskStatus.Succeeded, wait.Status);
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
            var wait = (UniTask)method.Invoke(battle, new object[] { .1f, System.Threading.CancellationToken.None });
            battle.Tick(50); battle.TickFrame(16); battle.TickFrame(17);
            Assert.AreEqual(UniTaskStatus.Pending, wait.Status);
            battle.TickFrame(17); Assert.AreEqual(UniTaskStatus.Succeeded, wait.Status);
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
            // 報酬を必ず得る検証用敵。最終ボスのセル種別・製品値は変更しない。
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
            var session=new DungeonCheckpointSession(defaults.Definitions,store);var random=new MockRandomProvider();
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
            IReadOnlyList<Card> clear=null;resumed.OnDungeonClearCompleted+=cards=>clear=cards;
            resumed.OnCardRewardPresented+=_=>resumed.SelectCardReward(null);
            foreach(var enemy in resumed.CurrentBattle.Enemies.ToArray())enemy.TakeDamage(enemy.MaxHp,ElementType.None);
            Assert.IsNotNull(clear);Assert.AreEqual(1,clear.Count);
            CollectionAssert.AreEqual(saved.ObtainedOrder,reopened.Read().ObtainedOrder);
            CollectionAssert.AreEqual(saved.DeckOrder,reopened.Read().DeckOrder);
            Assert.AreEqual(CheckpointBoundary.Terminal,reopened.Read().Boundary);
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
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
        private static DungeonBattleManager Model(Olca.Application.Dungeon.DungeonBattleController controller) => (DungeonBattleManager)Get(controller, "manager");
        private static Olca.Unity.FloorSequenceFileStore Store(Olca.Application.Dungeon.DungeonBattleController controller) => (Olca.Unity.FloorSequenceFileStore)Get(controller, "floorStore");
        private static DungeonConfig Empty(int cells) => new DungeonConfig("empty-" + cells, "終端fixture", cells,
            Enumerable.Range(0, cells).Select(i => new MapCell(i)).ToList());
        [SetUp] public void Setup() { directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-floor-controller-" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup()
        {
            foreach (var host in objects) if (host != null) UnityEngine.Object.DestroyImmediate(host);
            objects.Clear();
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
        }
        private Olca.Application.Dungeon.DungeonBattleController Start(DungeonConfig[] maps = null)
        {
            var host = new UnityEngine.GameObject("floor-controller-fixture"); host.SetActive(false); objects.Add(host);
            var controller = host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            controller.ConfigureCheckpointDirectory(directory); controller.ConfigureSimulationSeed(7);
            if (maps != null) {
                var deck = (Deck)controller.GetType().GetMethod("CreateInitialDeck", Hidden).Invoke(controller, null);
                definitions = new FloorSequenceDefinitions("controller-fixture", "v1", maps, deck);
            }
            controller.ConfigureFloorSequence(definitions);
            var view = host.AddComponent<Olca.Presentation.Field.GameFieldView>(); Set(controller, "gameFieldView", view);
            var hud = host.AddComponent<Olca.Presentation.Dungeon.DungeonHudView>(); Set(controller, "dungeonHudView", hud);
            var label = new UnityEngine.GameObject("progress", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Text));
            label.transform.SetParent(host.transform); Set(hud, "progressText", label.GetComponent<UnityEngine.UI.Text>());
            Call(controller, "Start"); return controller;
        }
        private static void Tick(Olca.Application.Dungeon.DungeonBattleController controller, LogicalInput input = null)
            => Call(controller, "TickLogicalFrame", 1L, 17, input ?? new LogicalInput());
        private static void ToBattle(Olca.Application.Dungeon.DungeonBattleController controller)
        {
            for (int i = 0; i < 120 && Model(controller).CurrentBattle == null; i++) Tick(controller);
            Assert.IsNotNull(Model(controller).CurrentBattle);
        }
        private static IReadOnlyList<Card> Win(Olca.Application.Dungeon.DungeonBattleController controller)
        {
            IReadOnlyList<Card> choices = null;
            var model = Model(controller); model.OnCardRewardPresented += cards => choices = cards;
            foreach (var enemy in model.CurrentBattle.Enemies.ToArray())
                if (!enemy.IsDead && !enemy.IsSummoned) enemy.TakeDamage(enemy.MaxHp, ElementType.None);
            return choices;
        }
        [TestCase(false)] [TestCase(true)]
        public void 終端保存失敗中は旧モデルとファイルを維持し再試行後だけ描画と入力を切り替える(bool jumping)
        {
            var controller = Start(new[] { Empty(3), DungeonConfig.CreateTestDungeon(8) });
            var old = Model(controller); old.Dungeon.Player.TakeDamage(37);
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
            StringAssert.Contains("2階", ((UnityEngine.UI.Text)Get(Get(controller, "dungeonHudView"), "progressText")).text);
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
