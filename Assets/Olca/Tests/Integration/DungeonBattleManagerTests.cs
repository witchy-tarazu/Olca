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
                // 同じTick内の遅延ダメージと敵の毒更新を最後まで処理する。
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
            public FailingStore(CheckpointDefinitions definitions) { memory = new MemoryCheckpointStore(definitions); }
            public bool TryWrite(CheckpointDto dto, out string error)
            { if (Fail) { error = "fixture保存失敗"; return false; } return memory.TryWrite(dto, out error); }
            public CheckpointDto Read() => memory.Read();
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
        [TestCase("status")][TestCase("discard")][TestCase("air")]
        public void 対象外の障害物を黙って省略せず拒否する(string kind)
        {
            var map = Map(); map.MapCells[2] = kind == "status" ? new MapCell(2, new Obstacle("x", "x", "poison")) :
                kind == "discard" ? new MapCell(2, Obstacle.CreateCardDiscardObstacle("x", "x", 1)) : new MapCell(2, new Obstacle("x", "x", 15), 1);
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
