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
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
        private static DungeonBattleManager Model(Olca.Application.Dungeon.DungeonBattleController controller) => (DungeonBattleManager)Get(controller, "manager");
        private static Olca.Unity.FloorSequenceFileStore Store(Olca.Application.Dungeon.DungeonBattleController controller) => (Olca.Unity.FloorSequenceFileStore)Get(controller, "floorStore");
        private static DungeonConfig Empty(int cells) => new DungeonConfig("empty-" + cells, "終端fixture", cells,
            Enumerable.Range(0, cells).Select(i => new MapCell(i)).ToList());
        [SetUp] public void Setup() { definitions = null; directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-floor-controller-" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup()
        {
            foreach (var host in objects) if (host != null) UnityEngine.Object.DestroyImmediate(host);
            objects.Clear();
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
        }
        private Olca.Application.Dungeon.DungeonBattleController Start(DungeonConfig[] maps = null, int? recoveryFrames = null, System.Action<Olca.Application.Dungeon.DungeonBattleController> configure = null)
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
            var view = host.AddComponent<Olca.Presentation.Field.GameFieldView>(); Set(controller, "gameFieldView", view);
            var hud = host.AddComponent<Olca.Presentation.Dungeon.DungeonHudView>(); Set(controller, "dungeonHudView", hud);
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
            enemy.MoveTo(new Vector2Int(battle.Player.GridPosition.X + 1, battle.Player.GridPosition.Y));
            battle.Player.SetInvincible(100000); battle.Resume(BattlePauseReason.CustomScreen);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            battle.RunLogicalFrame(() => Assert.IsTrue(battle.CardUsage.TryUseCurrentCard()));
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

        private static void KeyboardButton(Olca.Application.Dungeon.DungeonBattleController controller, string name)
        {
            var canvas = (UnityEngine.GameObject)Get(controller, "pauseCanvas");
            canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == name).onClick.Invoke();
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
                Assert.DoesNotThrow(()=>c.SetUserPaused(false));Assert.IsFalse(model.IsUserPaused);Assert.IsFalse(Olca.Unity.GameplayPause.IsPaused);
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

        [Test]
        public void 実キーZと保存したリマップJの短押しは成長後の攻撃値を通常Controllerへ渡す()
        {
            var progress = new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress"));
            Assert.IsTrue(progress.TryWrite(0,new ClearReturnProfile { Revision=1,CommitId=System.Guid.NewGuid().ToString("N"),Experience=100,Level=2 },out var error),error);
            var c=StartCombatDefaults(18,180);ToBattle(c);var battle=Model(c).CurrentBattle;
            battle.CustomScreen.Close();battle.Resume(BattlePauseReason.CustomScreen);battle.Player.SetInvincible(60000);
            var target=battle.Enemies[0];target.MoveTo(new Vector2Int(battle.Player.GridPosition.X+1,battle.Player.GridPosition.Y));
            Assert.AreEqual(11,battle.Player.AttackPower);
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
            var progress=(Olca.Unity.ClearReturnFileStore)Get(c,"clearReturnStore");var before=System.IO.File.ReadAllBytes(progress.ActivePath);
            var io=new KeyboardFaultIo{Fault=fault=="before"||fault=="after"?fault:null};Set(progress,"io",io);
            if(outcome==RunReturnOutcome.Defeated){Model(c).ResumeAfterBattle();ReachNonClearBattle(c);Model(c).CurrentBattle.Player.TakeDamage(99999);}
            else{c.SetUserPaused(true);KeyboardButton(c,"RetreatButton");KeyboardButton(c,"CancelRetreat");Assert.IsTrue(c.IsUserPaused);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(progress.ActivePath));KeyboardButton(c,"RetreatButton");KeyboardButton(c,"ConfirmRetreat");}
            if(fault=="before"){
                Assert.IsNull(c.PersistentProgress.Flow);Assert.AreEqual(1,c.PersistentProgress.Receipts.Length);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(progress.ActivePath));
                Assert.IsTrue(((ClearReturnSession)Get(c,"clearReturnSession")).HasPendingSave);io.Fault=null;KeyboardButton(c,"RetryClearReturn");
            }
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
            Assert.IsTrue(view.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("1/4枚")));
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
                Assert.IsNotNull(label); Assert.AreEqual("1階  1 / 6", label.text);
                Assert.IsFalse(label.raycastTarget); Assert.AreSame(hud.transform, label.transform.parent);
                Assert.AreSame(((UnityEngine.UI.Text)Get(hud, "deckCountText")).font, label.font);
                hud.Refresh(new Olca.Presentation.Dungeon.DungeonHudData(63, 100, 0, 20, false, false, 7, 1, 2));
                Assert.AreSame(label, Get(hud, "progressText")); Assert.AreEqual("2階  0 / 20", label.text);
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
        private Olca.Unity.ClearReturnFileStore Store(FaultIo io = null) => new Olca.Unity.ClearReturnFileStore(directory, io);
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
        private void Step(LogicalInput input=null)=>state.Tick(battle,input??new LogicalInput(),30,12);
        private static LogicalInput Tap(bool card=false)=>new LogicalInput{BasicAttackButtonDown=true,BasicAttackButtonUp=true,CardUseButtonDown=card};
        [TestCase(2,0,189)][TestCase(3,0,200)][TestCase(0,0,200)][TestCase(2,1,200)]
        public void 前方一マス同じ列の敵だけに成長後の無属性攻撃を適用する(int x,int y,int hp)
        {SetupBattle(x,y);Step(Tap());Assert.AreEqual(hp,target.CurrentHp);}
        [Test]
        public void 同マスは先配置一体だけを選び無敵対象を別の敵へ付け替えない()
        {
            SetupBattle();var second=new Enemy("second","second",200,ElementType.None,target.GridPosition,isSummoned:true);battle.AddSummon(second,new List<Olca.Core.Logic.Battle.IEnemyAIAction>{new Olca.Core.Logic.Battle.BasicAttackAction()});
            target.SetInvincible(1000);Step(Tap());Assert.AreEqual(200,target.CurrentHp);Assert.AreEqual(200,second.CurrentHp);
        }
        [TestCase(1,true)][TestCase(29,true)][TestCase(30,false)]
        public void 短押しは解放で一度だけ発動し長押しはタメ代替の通常攻撃を出さない(int held,bool accepted)
        {
            SetupBattle();Step(new LogicalInput{BasicAttackButtonDown=true,BasicAttackButton=true});
            for(int i=1;i<held;i++)Step(new LogicalInput{BasicAttackButton=true});Assert.AreEqual(200,target.CurrentHp);
            Step(new LogicalInput{BasicAttackButtonUp=true});Assert.AreEqual(accepted?189:200,target.CurrentHp);
            for(int i=0;i<40;i++)Step();Assert.AreEqual(accepted?189:200,target.CurrentHp);
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
