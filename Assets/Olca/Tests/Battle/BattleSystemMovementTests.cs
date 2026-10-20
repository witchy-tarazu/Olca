using NUnit.Framework;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class BattleSystemMovementTests
    {
        private static Enemy Occupant(string id, int x, bool summon = false)
            => new Enemy(id, id, 50, ElementType.None, new Vector2Int(x, 0), isSummoned: summon);

        [TestCase(false)]
        [TestCase(true)]
        public void 敵占有中の直接穴化は浮遊の有無にかかわらずヒビになる(bool floating)
        {
            var enemy = Occupant("target", 3); enemy.TerrainState.IsFloating = floating;
            var b = new BattleSystem(new PlayerCharacter(100, new Vector2Int(0, 0)), new List<Enemy> { enemy }, new Deck());
            b.Field.ChangePanelType(enemy.GridPosition, PanelType.Hole);
            Assert.IsTrue(b.Field.IsOccupied(enemy.GridPosition));
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(enemy.GridPosition));
            enemy.MoveTo(new Vector2Int(4, 0));
            Assert.AreEqual(floating ? PanelType.Cracked : PanelType.Hole, b.Field.GetPanelType(new Vector2Int(3, 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 同マスの最後の対象が移動または死亡した時だけヒビが壊れる(bool die)
        {
            var a = Occupant("a", 3); var b = Occupant("b", 3); var keeper = Occupant("keeper", 5);
            var battle = new BattleSystem(new PlayerCharacter(100, new Vector2Int(0, 0)), new List<Enemy> { a, b, keeper }, new Deck());
            var from = a.GridPosition; battle.Field.ChangePanelType(from, PanelType.Hole);
            int breaks = 0; battle.PanelEffects.OnPanelEffectApplied += (_, type) => { if (type == PanelType.Cracked) breaks++; };
            a.MoveTo(from); Assert.AreEqual(0, breaks, "同位置通知は離脱ではない");
            if (die) a.TakeDamage(50, ElementType.None); else a.MoveTo(new Vector2Int(4, 0));
            Assert.AreEqual(PanelType.Cracked, battle.Field.GetPanelType(from)); Assert.IsTrue(battle.Field.IsOccupied(from));
            if (die) b.TakeDamage(50, ElementType.None); else b.MoveTo(new Vector2Int(4, 0));
            Assert.AreEqual(PanelType.Hole, battle.Field.GetPanelType(from)); Assert.IsFalse(battle.Field.IsOccupied(from)); Assert.AreEqual(1, breaks);
            Assert.IsFalse(battle.IsGameOver);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 同マスの浮遊者も占有に数えるが最後の浮遊者自身はヒビを壊さない(bool floatLeavesFirst)
        {
            var ground = Occupant("ground", 3); var floating = Occupant("float", 3); floating.TerrainState.IsFloating = true;
            var b = new BattleSystem(new PlayerCharacter(100, new Vector2Int(0, 0)), new List<Enemy> { ground, floating }, new Deck());
            var from = ground.GridPosition; b.Field.ChangePanelType(from, PanelType.Cracked);
            (floatLeavesFirst ? floating : ground).MoveTo(new Vector2Int(4, 0));
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(from));
            (floatLeavesFirst ? ground : floating).MoveTo(new Vector2Int(4, 0));
            Assert.AreEqual(floatLeavesFirst ? PanelType.Hole : PanelType.Cracked, b.Field.GetPanelType(from));
        }

        [Test]
        public void 召喚登録の通知前に占有が反映され死亡後は再通知で地形を変更しない()
        {
            var b = new BattleSystem(new PlayerCharacter(100, new Vector2Int(0, 0)), new List<Enemy> { Occupant("keeper", 5) }, new Deck());
            var summon = Occupant("summon", 3, true); var from = summon.GridPosition;
            b.OnSummonAdded += e => { Assert.IsTrue(b.Field.IsOccupied(e.GridPosition)); b.Field.ChangePanelType(e.GridPosition, PanelType.Hole); };
            b.AddSummon(summon, new List<IEnemyAIAction> { new BasicAttackAction(10, 2f) });
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(from));
            summon.TakeDamage(50, ElementType.None); Assert.AreEqual(PanelType.Hole, b.Field.GetPanelType(from));
            b.Field.ChangePanelType(from, PanelType.Cracked); summon.MoveTo(new Vector2Int(4, 0)); summon.TakeDamage(1, ElementType.None);
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(from)); Assert.IsFalse(b.Field.IsOccupied(summon.GridPosition));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 勝敗通知前に占有と位置購読を解除して後続の移動でヒビを破壊しない(bool defeat)
        {
            var p = new PlayerCharacter(100, new Vector2Int(0, 0)); var e = Occupant("body", 5); var summon = Occupant("summon", 3, true);
            var b = new BattleSystem(p, new List<Enemy> { e }, new Deck());
            b.AddSummon(summon, new List<IEnemyAIAction> { new BasicAttackAction(10, 2f) });
            b.Field.SetProtectedPosition(new Vector2Int(2, 0));
            int externalMoves = 0; summon.OnPositionChanged += _ => externalMoves++;
            b.OnBattleEnd += result =>
            {
                for (int x = 0; x < 6; x++) Assert.IsFalse(b.Field.IsOccupied(new Vector2Int(x, 0)));
                Assert.IsFalse(b.Enemies.Contains(summon));
            };
            if (defeat) p.TakeDamage(100); else e.TakeDamage(50, ElementType.None);
            Assert.IsTrue(b.IsGameOver);
            b.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Cracked);
            b.Field.ChangePanelType(new Vector2Int(0, 0), PanelType.Cracked);
            summon.MoveTo(new Vector2Int(4, 0)); p.MoveTo(new Vector2Int(1, 0));
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(new Vector2Int(3, 0)));
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(new Vector2Int(0, 0))); Assert.AreEqual(1, externalMoves);
        }

        [Test]
        public void 永続穴の占有中も移動後も地形変更で上書きできない()
        {
            var hole = new Vector2Int(3, 0); var enemy = Occupant("float", 3); enemy.TerrainState.IsFloating = true;
            var b = new BattleSystem(new PlayerCharacter(100, new Vector2Int(0, 0)), new List<Enemy> { enemy }, new Deck(), initialPermanentHoles: new[] { hole });
            b.Field.ChangePanelType(hole, PanelType.Cracked); Assert.AreEqual(PanelType.Hole, b.Field.GetPanelType(hole));
            enemy.MoveTo(new Vector2Int(4, 0)); b.Field.ChangePanelType(hole, PanelType.Normal);
            Assert.IsTrue(b.Field.IsPermanentHole(hole)); Assert.AreEqual(PanelType.Hole, b.Field.GetPanelType(hole));
        }

        [Test]
        public void プレイヤーの直接位置変更も敵と共通の占有と離脱判定を使う()
        {
            var p = new PlayerCharacter(100, new Vector2Int(3, 0)); var e = Occupant("e", 3);
            var b = new BattleSystem(p, new List<Enemy> { e }, new Deck()); var from = p.GridPosition;
            b.Field.ChangePanelType(from, PanelType.Hole); p.MoveTo(new Vector2Int(2, 0));
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(from));
            e.MoveTo(new Vector2Int(4, 0)); Assert.AreEqual(PanelType.Hole, b.Field.GetPanelType(from));
        }

        [Test]
        public void 一時移動元の保護は共通占有判定でも維持する()
        {
            var p = new PlayerCharacter(100, new Vector2Int(0, 0)); var b = new BattleSystem(p, new List<Enemy> { Occupant("e", 5) }, new Deck());
            var origin = p.GridPosition; b.Field.SetProtectedPosition(origin); p.MoveTemporarily(new Vector2Int(2, 0));
            b.Field.ChangePanelType(origin, PanelType.Hole); b.Field.ChangePanelType(p.GridPosition, PanelType.Hole);
            Assert.IsTrue(b.Field.IsOccupied(origin)); Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(origin));
            p.ReturnToOriginalPosition(); b.Field.SetProtectedPosition(null);
            Assert.AreEqual(PanelType.Cracked, b.Field.GetPanelType(origin));
            Assert.AreEqual(PanelType.Hole, b.Field.GetPanelType(new Vector2Int(2, 0)));
        }

        private BattleSystem battle;
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;
        private MockInputProvider input;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemies = new List<Enemy>();
            deck = new Deck(new List<Card>
            {
                new Card("test1", "テストカード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト"),
            });
            input = new MockInputProvider();
            logger = new MockLogger();

            battle = new BattleSystem(player, enemies, deck, logger: logger);
        }

        [Test]
        public void Tickで入力を受け取って移動できる()
        {
            // Arrange
            input.SetHorizontal(1.0f); // 右入力
            Vector2Int initialPos = player.GridPosition;

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(new Vector2Int(initialPos.X + 1, initialPos.Y), player.GridPosition, "右に移動するべき");
        }

        [Test]
        public void Paused状態では移動しない()
        {
            // Arrange
            input.SetHorizontal(1.0f);
            battle.Pause();
            Vector2Int initialPos = player.GridPosition;

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(initialPos, player.GridPosition, "一時停止中は移動しないべき");
        }

        [Test]
        public void GameOver状態では移動しない()
        {
            // Arrange
            input.SetHorizontal(1.0f);
            player.TakeDamage(100); // プレイヤーを倒してGameOver
            Vector2Int initialPos = player.GridPosition;

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(initialPos, player.GridPosition, "GameOver時は移動しないべき");
        }

        [Test]
        public void Iceパネルに移動すると滑って最終位置に到達する()
        {
            // Arrange: Iceパネルを連続で配置
            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Ice);
            battle.Field.ChangePanelType(new Vector2Int(4, 0), PanelType.Ice);
            // 5は通常パネル

            input.SetHorizontal(1.0f);

            // Act: 右に移動（Ice に入る）
            battle.Tick(deltaTimeMs: 200, input);
            input.SetHorizontal(0f); // 入力を解除

            // Ice滑り処理
            battle.Tick(deltaTimeMs: 200, input); // 3→4
            battle.Tick(deltaTimeMs: 200, input); // 4→5（通常パネルで停止）

            // Assert
            Assert.AreEqual(new Vector2Int(5, 0), player.GridPosition, "Ice滑りで(5,0)まで到達するべき");
            Assert.IsFalse(battle.PlayerMovement.IsSliding, "滑りが終了しているべき");
        }

        [Test]
        public void Magmaに移動すると即座にダメージを受けてパネルが通常に変化する()
        {
            // Arrange
            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Magma);
            int initialHp = player.CurrentHp;
            input.SetHorizontal(1.0f);

            // Act: 右に移動してMagmaパネルに乗る
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(new Vector2Int(3, 0), player.GridPosition, "Magmaパネルに移動");
            Assert.Less(player.CurrentHp, initialHp, "Magmaダメージを受けるべき");
            Assert.AreEqual(initialHp - 5, player.CurrentHp, "5ダメージを受ける");
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(new Vector2Int(3, 0)), "Magmaが通常パネルに変化");
        }

        [Test]
        public void Crackedパネルに移動後_離れるとHoleに変化する()
        {
            // Arrange
            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Cracked);
            input.SetHorizontal(1.0f);

            // Act: Crackedパネルに移動
            battle.Tick(deltaTimeMs: 200, input);

            // Assert: Crackedパネルに乗った時点ではまだCracked
            Assert.AreEqual(new Vector2Int(3, 0), player.GridPosition, "Crackedに移動");
            Assert.AreEqual(PanelType.Cracked, battle.Field.GetPanelType(new Vector2Int(3, 0)), "まだCrackedパネル");

            // さらに右に移動
            battle.Tick(deltaTimeMs: 200, input);

            // Crackedから離れたのでHoleに変化しているべき
            Assert.AreEqual(new Vector2Int(4, 0), player.GridPosition, "右に移動成功");
            Assert.AreEqual(PanelType.Hole, battle.Field.GetPanelType(new Vector2Int(3, 0)), "離れた後にHoleに変化");
        }

        [Test]
        public void PlayerMovement経由でイベントが発火する()
        {
            // Arrange
            int moveCount = 0;
            battle.PlayerMovement.OnMoved += (newPos, oldPos) => moveCount++;

            input.SetHorizontal(1.0f);

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(1, moveCount, "OnMovedイベントが発火するべき");
        }

        [Test]
        public void PanelEffects経由でイベントが発火する()
        {
            // Arrange
            int effectCount = 0;
            battle.PanelEffects.OnPanelEffectApplied += (pos, type) => effectCount++;

            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Cracked);
            input.SetHorizontal(1.0f);

            // Act: Crackedに移動して、さらに離れる
            battle.Tick(deltaTimeMs: 200, input); // Crackedに移動（イベントなし）
            battle.Tick(deltaTimeMs: 200, input); // Crackedから離れる（イベント発火）

            // Assert
            Assert.AreEqual(1, effectCount, "Crackedから離れた時にOnPanelEffectAppliedイベントが発火するべき");
        }

        [Test]
        public void 入力がnullの場合は移動処理をスキップする()
        {
            // Arrange
            Vector2Int initialPos = player.GridPosition;

            // Act: input = null で Tick
            battle.Tick(deltaTimeMs: 200, null);

            // Assert
            Assert.AreEqual(initialPos, player.GridPosition, "入力がnullの場合は移動しないべき");
        }

        [Test]
        public void Grassパネルから移動後_クールダウン中は移動できない()
        {
            // Arrange
            battle.Field.ChangePanelType(new Vector2Int(2, 0), PanelType.Grass);
            player.MoveTo(new Vector2Int(2, 0));

            input.SetHorizontal(1.0f);

            // Act: Grassから移動
            battle.Tick(deltaTimeMs: 200, input);
            Vector2Int posAfterFirstMove = player.GridPosition;

            // さらに移動を試みる（クールダウン中）
            battle.Tick(deltaTimeMs: 10, input);

            // Assert
            Assert.AreEqual(new Vector2Int(3, 0), posAfterFirstMove, "最初の移動は成功");
            Assert.AreEqual(posAfterFirstMove, player.GridPosition, "クールダウン中は移動できないべき");
        }

        [Test]
        public void 敵との衝突で移動がブロックされる()
        {
            // Arrange
            enemies.Add(new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0)));
            input.SetHorizontal(1.0f);
            Vector2Int initialPos = player.GridPosition;

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.AreEqual(initialPos, player.GridPosition, "敵がいる位置には移動できないべき");
        }

        [Test]
        public void カスタムゲージと移動が同時に更新される()
        {
            // Arrange
            float initialGauge = battle.CustomGauge.CurrentGauge;
            input.SetHorizontal(1.0f);

            // Act
            battle.Tick(deltaTimeMs: 200, input);

            // Assert
            Assert.Greater(battle.CustomGauge.CurrentGauge, initialGauge, "カスタムゲージが更新されるべき");
            Assert.AreEqual(new Vector2Int(3, 0), player.GridPosition, "移動も成功するべき");
        }
    }
}

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class BattleSummonTests
    {
        private sealed class CallbackAction : IEnemyAIAction
        {
            public int Count;
            public System.Action Callback;
            public bool CanExecute(AIContext context) => true;
            public void Execute(AIContext context) { Count++; Callback?.Invoke(); }
            public int GetCooldownMs() => 1000;
            public float GetCooldown() => 1f;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 毒イベント内の召喚は即時登録して次Tickから更新し終了時に除去する(bool onDeath)
        {
            var main = Enemy(); var survivor = Enemy(); var summon = Enemy(true);
            var battle = Battle(new PlayerCharacter(100), main, survivor);
            var action = new CallbackAction(); int notifications = 0, ends = 0;
            var poison = new StatusEffect(StatusEffectType.Poison, 5000, 1);
            summon.ApplyStatus(poison);
            battle.OnSummonAdded += _ => notifications++;
            System.Action add = () => battle.AddSummon(summon, new List<IEnemyAIAction> { action });
            if (onDeath)
            {
                main.OnDeath += add;
                // 毒単独では死亡しないため、毒通知内で通常攻撃が重なった経路を検証する。
                main.OnDamaged += (_, __) => { if (main.CurrentHp == 1) main.TakeDamage(1, ElementType.None); };
            }
            else main.OnDamaged += (_, __) => { if (notifications == 0) add(); };
            main.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, onDeath ? 50 : 1));
            Assert.DoesNotThrow(() => battle.Tick(1000));
            CollectionAssert.AreEqual(new[] { main, survivor, summon }, battle.Enemies);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(1, AI(battle).AIBehaviors.Count(b => b.Enemy == summon));
            Assert.AreEqual(5000, poison.RemainingMs);
            Assert.AreEqual(0, action.Count, "追加前から経過したTickの時間を新規召喚へ渡さない");
            battle.Tick(1000);
            Assert.AreEqual(4000, poison.RemainingMs);
            Assert.AreEqual(1, action.Count);
            battle.OnBattleEnd += _ =>
            {
                ends++;
                Assert.IsFalse(battle.Enemies.Contains(summon));
                Assert.AreEqual(0, AI(battle).AIBehaviors.Count);
                NoBattleSubscriptions(summon, battle, "OnDeath", "OnDamageProcessingStarted", "OnDamageProcessingCompleted");
            };
            if (!main.IsDead) main.TakeDamage(50, ElementType.None);
            survivor.TakeDamage(50, ElementType.None);
            Assert.AreEqual(BattleState.Victory, battle.State);
            Assert.AreEqual(1, ends);
            Assert.AreEqual(1, notifications);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 遅延効果やAIからの追加召喚も次Tickまで行動しない(bool fromAI)
        {
            var battle = Battle(new PlayerCharacter(100), Enemy());
            var summon = Enemy(true); var action = new CallbackAction();
            System.Action add = () => battle.AddSummon(summon, new List<IEnemyAIAction> { action });
            if (fromAI)
            {
                var summoner = new CallbackAction();
                summoner.Callback = () => { summoner.Callback = null; add(); };
                battle.AddSummon(Enemy(true), new List<IEnemyAIAction> { summoner });
            }
            else battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = add });
            battle.Tick(1);
            Assert.AreEqual(0, action.Count);
            battle.Tick(1);
            Assert.AreEqual(1, action.Count);
        }

        private static Enemy Enemy(bool summon = false) => new Enemy("fixture", "試験対象", 50, ElementType.None, new Vector2Int(4, 0), isSummoned: summon);
        private static Card Card() => new Card("c", "試験", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "試験");
        private static BattleSystem Battle(PlayerCharacter player, params Enemy[] enemies) => new BattleSystem(player, new List<Enemy>(enemies), new Deck(new List<Card> { Card() }));
        private static List<IEnemyAIAction> Actions() => new List<IEnemyAIAction> { new BasicAttackAction(1, 1f) };
        private static EnemyAISystem AI(BattleSystem battle) => (EnemyAISystem)typeof(BattleSystem).GetField("enemyAI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(battle);
        private static void NoBattleSubscriptions(object owner, BattleSystem battle, params string[] fields)
        {
            foreach (var name in fields)
            {
                var value = (System.Delegate)owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(owner);
                if (value == null) continue;
                foreach (var action in value.GetInvocationList()) Assert.AreNotSame(battle, action.Target, name);
            }
        }

        [Test]
        public void 召喚物だけ撃破しても継続し本体全滅なら生存召喚物を残さず勝利する()
        {
            var main = Enemy(); var deadSummon = Enemy(true); var liveSummon = Enemy(true);
            var battle = Battle(new PlayerCharacter(100), main, deadSummon, liveSummon);
            int ends = 0; battle.OnBattleEnd += _ => ends++;
            deadSummon.TakeDamage(50, ElementType.None);
            Assert.IsFalse(battle.IsGameOver);
            Assert.AreEqual(2, AI(battle).AIBehaviors.Count);
            int summonDeaths = 0; liveSummon.OnDeath += () => summonDeaths++;
            main.TakeDamage(50, ElementType.None);
            Assert.AreEqual(BattleState.Victory, battle.State);
            Assert.AreEqual(1, ends);
            Assert.AreEqual(0, summonDeaths, "終了時破棄を撃破扱いにしない");
            Assert.AreEqual(50, liveSummon.CurrentHp);
            Assert.IsFalse(battle.Enemies.Contains(liveSummon));
            Assert.IsFalse(battle.Enemies.Contains(deadSummon));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 同マスの単体攻撃は本体と召喚物を区別せず配置順を使う(bool summonFirst)
        {
            var main = Enemy(); var summon = Enemy(true);
            var battle = summonFirst ? Battle(new PlayerCharacter(100), summon, main) : Battle(new PlayerCharacter(100), main, summon);
            new Olca.Core.Logic.CardEffects.Effects.DamageEffect(10).Execute(new Olca.Core.Logic.CardEffects.BattleContext(battle, Card()));
            Assert.AreEqual(summonFirst ? 50 : 40, main.CurrentHp);
            Assert.AreEqual(summonFirst ? 40 : 50, summon.CurrentHp);
        }

        [Test]
        public void 戦闘中の召喚を配置順末尾に登録しAIと通知を一回だけ追加する()
        {
            var main = Enemy(); var summon = Enemy(true);
            var battle = Battle(new PlayerCharacter(100), main);
            Enemy notified = null; int count = 0;
            battle.OnSummonAdded += e => { notified = e; count++; };
            battle.AddSummon(summon, Actions());
            CollectionAssert.AreEqual(new[] { main, summon }, battle.Enemies);
            Assert.AreSame(summon, notified); Assert.AreEqual(1, count);
            Assert.AreEqual(2, AI(battle).AIBehaviors.Count);
            Assert.Throws<System.ArgumentException>(() => battle.AddSummon(summon, Actions()));
            Assert.AreEqual(2, AI(battle).AIBehaviors.Count);
        }

        [Test]
        public void 不正な召喚指定やAI登録失敗は参加者と購読を増やさない()
        {
            var main = Enemy(); var summon = Enemy(true);
            var battle = Battle(new PlayerCharacter(100), main);
            Assert.Throws<System.ArgumentException>(() => battle.AddSummon(Enemy(), Actions()));
            Assert.Throws<System.ArgumentException>(() => battle.AddSummon(summon, null));
            Assert.AreEqual(1, battle.Enemies.Count);
            Assert.AreEqual(1, AI(battle).AIBehaviors.Count);
            NoBattleSubscriptions(summon, battle, "OnDamageProcessingStarted", "OnDamageProcessingCompleted", "OnDeath");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 勝敗通知より前に召喚物と障害物とAIと購読を片付ける(bool defeat)
        {
            var main = Enemy(); var summon = Enemy(true); var player = new PlayerCharacter(100);
            var battle = Battle(player, main);
            battle.AddSummon(summon, Actions());
            var pos = new Vector2Int(2, 0); var obstacle = new FieldObstacle("fixture", 10);
            battle.Field.PlaceObstacle(pos, obstacle);
            battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 10000) { ExecuteAction = () => Assert.Fail("終了後の遅延実行") });
            int ends = 0, externalDeaths = 0;
            System.Action external = () => externalDeaths++;
            summon.OnDeath += external;
            battle.OnBattleEnd += result =>
            {
                ends++;
                Assert.AreEqual(defeat ? BattleResult.Defeat : BattleResult.Victory, result);
                Assert.AreEqual(1, battle.Enemies.Count);
                Assert.AreEqual(0, AI(battle).AIBehaviors.Count);
                Assert.IsFalse(battle.Field.HasObstacle(pos));
                Assert.AreEqual(10, obstacle.Hp);
                Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
                NoBattleSubscriptions(player, battle, "OnDamageProcessingStarted", "OnDamageProcessingCompleted", "OnDeath", "OnStaggered");
                NoBattleSubscriptions(main, battle, "OnDamageProcessingStarted", "OnDamageProcessingCompleted", "OnDeath");
                NoBattleSubscriptions(summon, battle, "OnDamageProcessingStarted", "OnDamageProcessingCompleted", "OnDeath");
            };
            if (defeat) player.TakeDamage(100); else main.TakeDamage(50, ElementType.None);
            Assert.AreEqual(1, ends);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Assert.IsNull(typeof(Enemy).GetField("OnDeath", flags).GetValue(main));
            var remaining = (System.Delegate)typeof(Enemy).GetField("OnDeath", flags).GetValue(summon);
            CollectionAssert.AreEqual(new System.Delegate[] { external }, remaining.GetInvocationList());
            Assert.AreEqual(0, ((System.Collections.IDictionary)typeof(BattleSystem).GetField("enemyDeathHandlers", flags).GetValue(battle)).Count);
            Assert.Throws<System.InvalidOperationException>(() => battle.AddSummon(Enemy(true), Actions()));
            summon.TakeDamage(50, ElementType.None); battle.Tick(10000);
            Assert.AreEqual(1, externalDeaths, "外部の購読は削除しない");
            Assert.AreEqual(1, ends);
            Assert.AreEqual(0, (int)typeof(BattleSystem).GetField("resolutionDepth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(battle));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 召喚物が残っていても同じTick内の本体とプレイヤーHP0は敗北一回(bool reverse)
        {
            var main = Enemy(); var summon = Enemy(true); var player = new PlayerCharacter(100);
            var battle = Battle(player, main, summon); int ends = 0;
            battle.OnBattleEnd += result => { ends++; Assert.AreEqual(BattleResult.Defeat, result); };
            System.Action a = () => main.TakeDamage(50, ElementType.None), b = () => player.TakeDamage(100);
            battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = reverse ? a : b });
            battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Startup, 1) { ExecuteAction = reverse ? b : a });
            battle.Tick(1);
            Assert.AreEqual(BattleState.Defeat, battle.State); Assert.AreEqual(1, ends);
            Assert.IsFalse(battle.Enemies.Contains(summon));
        }
    }
}


namespace Olca.Tests.Battle
{
    [TestFixture]
    public class NormalMoveMidpointTests
    {
        private static BattleSystem Create(int x=3)
        {
            var p=new PlayerCharacter(100,new Vector2Int(x,0),poise:1,staggerDurationFrames:3);
            var enemy=new Enemy("e","敵",100,ElementType.None,new Vector2Int(x==5?0:5,0));
            enemy.ApplyStun(60000);
            return new BattleSystem(p,new System.Collections.Generic.List<Enemy>{enemy},new Deck());
        }
        private static MockInputProvider Input(int direction)=>new MockInputProvider{Horizontal=direction};
        [TestCase(-1,false)][TestCase(1,false)][TestCase(-1,true)][TestCase(1,true)]
        public void 通常移動は中間で占有と当たり判定を切り替え全動作中は攻撃カードを受け付けない(int direction,bool grass)
        {
            var battle=Create();var p=battle.Player;var move=battle.PlayerMovement;
            battle.Enemies[0].MoveTo(new Vector2Int(direction>0?5:1,0));
            var attack=new BasicAttackAction(1,0);var context=new AIContext(battle.Enemies[0],p,battle.Field,new MockRandomProvider(),new MockLogger());
            var from=p.GridPosition;var to=new Vector2Int(3+direction,0);
            if(grass)battle.Field.ChangePanelType(from,PanelType.Grass);
            var card=new Card("c","攻撃",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,1,"fixture");
            battle.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{card});int changed=0;move.OnMoved+=(_,__)=>changed++;
            int total=grass?12:9,mid=NormalMoveTiming.Midpoint(total);
            for(int frame=1;frame<=total;frame++){
                battle.TickFrame(17,frame==1?Input(direction):null);
                Assert.AreEqual(frame<mid?from:to,p.GridPosition,"frame "+frame);
                Assert.AreEqual(frame>=mid,attack.CanExecute(context),"当たり判定も中間で切り替わる");
                Assert.AreEqual(frame<mid,battle.Field.IsOccupied(from));Assert.AreEqual(frame>=mid,battle.Field.IsOccupied(to));
                Assert.AreEqual(frame<total,move.IsNormalMoving);Assert.AreEqual(frame<mid?0:1,changed);
                if(frame<total){
                    int hp=battle.Enemies[0].CurrentHp;battle.PlayerBasicAttack(battle.Enemies[0]);Assert.AreEqual(hp,battle.Enemies[0].CurrentHp);
                    Assert.IsFalse(battle.CardUsage.TryUseCurrentCard());Assert.IsFalse(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());
                    Assert.IsFalse(move.TryMove(new Vector2Int(-direction,0),new System.Collections.Generic.List<Enemy>()));
                }
            }
            Assert.AreEqual(1,battle.CardUsage.RemainingCount);Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
        }
        [TestCase(-1,false,false)][TestCase(1,false,false)][TestCase(-1,true,false)][TestCase(1,true,false)]
        [TestCase(-1,false,true)][TestCase(1,false,true)][TestCase(-1,true,true)][TestCase(1,true,true)]
        public void 麻痺とのけぞりの中断位置は中間確定の前後で両方向とも維持する(int direction,bool committed,bool paralysis)
        {
            var battle=Create();int count=committed?5:4;
            for(int i=0;i<count;i++)battle.TickFrame(17,i==0?Input(direction):null);
            if(paralysis)battle.Player.ApplyParalysis(3);else battle.Player.TakeDamage(1,forceStagger:true,poiseDamage:1);
            battle.TickFrame(17);Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);
            Assert.AreEqual(new Vector2Int(committed?3+direction:3,0),battle.Player.GridPosition);
            for(int i=0;i<12;i++)battle.TickFrame(17);
            Assert.AreEqual(new Vector2Int(committed?3+direction:3,0),battle.Player.GridPosition);
        }
        [TestCase(BattlePauseReason.User)][TestCase(BattlePauseReason.CustomScreen)]
        public void ポーズは移動表示と占有の時計を止め動作中の反対入力を予約しない(BattlePauseReason reason)
        {
            var battle=Create();battle.TickFrame(17,Input(-1));float progress=battle.PlayerMovement.NormalMoveProgress;
            battle.Pause(reason);for(int i=0;i<12;i++)battle.TickFrame(17,Input(1));
            Assert.AreEqual(progress,battle.PlayerMovement.NormalMoveProgress);Assert.AreEqual(new Vector2Int(3,0),battle.Player.GridPosition);
            battle.Resume(reason);for(int i=0;i<8;i++)battle.TickFrame(17,Input(1));
            Assert.AreEqual(new Vector2Int(2,0),battle.Player.GridPosition);Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);
            battle.TickFrame(17);Assert.AreEqual(new Vector2Int(2,0),battle.Player.GridPosition);
        }
        [TestCase(false)][TestCase(true)]
        public void 中間直前に移動先が塞がれた場合は移動元に留める(bool hole)
        {
            var battle=Create();for(int i=0;i<4;i++)battle.TickFrame(17,i==0?Input(1):null);
            if(hole)battle.Field.ChangePanelType(new Vector2Int(4,0),PanelType.Hole);else battle.Enemies[0].MoveTo(new Vector2Int(4,0));
            int blocks=0;battle.PlayerMovement.OnMoveBlocked+=_=>blocks++;battle.TickFrame(17);
            Assert.AreEqual(new Vector2Int(3,0),battle.Player.GridPosition);Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);Assert.AreEqual(1,blocks);
        }
        [TestCase(0,-1)][TestCase(5,1)]
        public void 両端から盤外へは動作を開始しない(int x,int direction)
        {var b=Create(x);b.TickFrame(17,Input(direction));Assert.IsFalse(b.PlayerMovement.IsNormalMoving);Assert.AreEqual(new Vector2Int(x,0),b.Player.GridPosition);}
        [Test] public void ヒビの離脱とマグマ接触は開始時ではなく中間確定時だけ発生する()
        {
            var b=Create();b.Field.ChangePanelType(new Vector2Int(3,0),PanelType.Cracked);b.Field.ChangePanelType(new Vector2Int(4,0),PanelType.Magma);
            for(int i=0;i<4;i++)b.TickFrame(17,i==0?Input(1):null);
            Assert.AreEqual(100,b.Player.CurrentHp);Assert.AreEqual(PanelType.Cracked,b.Field.GetPanelType(new Vector2Int(3,0)));
            b.TickFrame(17);Assert.AreEqual(95,b.Player.CurrentHp);Assert.AreEqual(PanelType.Hole,b.Field.GetPanelType(new Vector2Int(3,0)));
            Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(new Vector2Int(4,0)));
        }
        [Test] public void 移動途中の中断再開は途中座標を保存せず既存の戦闘開始地点へ戻る()
        {
            var map=DungeonConfig.CreateTestDungeon(30,1,5);
            var deck=new Deck(new List<Card>{new Card("c","攻撃",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,10,"fixture")});
            var checkpoints=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);
            var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:checkpoints);
            for(int i=0;i<1000&&manager.CurrentBattle==null;i++)manager.TickFrame(17);
            var battle=manager.CurrentBattle;Assert.IsNotNull(battle);
            if(battle.CustomScreen.IsOpen){battle.CustomScreen.Confirm();battle.CustomScreen.Close();}
            var before=checkpoints.Store.Read();
            var enemies=battle.Enemies.ToList();var direction=new[]{-1,1}.First(d=>battle.Field.IsInBounds(new Vector2Int(battle.Player.GridPosition.X+d,0))&&
                !enemies.Any(e=>e.GridPosition==new Vector2Int(battle.Player.GridPosition.X+d,0)));
            Assert.IsTrue(battle.PlayerMovement.TryStartNormalMove(new Vector2Int(direction,0),enemies));
            var capture=typeof(Olca.Core.Logic.Dungeon.DungeonCheckpointSession).GetMethod("Capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var rejected=Assert.Throws<System.Reflection.TargetInvocationException>(()=>capture.Invoke(checkpoints,new object[]{manager.Dungeon,before.Boundary,null}));
            Assert.IsInstanceOf<System.ArgumentException>(rejected.InnerException);
            Assert.IsTrue(manager.SuspendForCheckpoint());Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,new MockRandomProvider(),new MockLogger(),out var resumed,out var error),error);
            resumed.ActivateCheckpoint();Assert.IsFalse(resumed.CurrentBattle.PlayerMovement.IsNormalMoving);
            Assert.AreEqual(new Vector2Int(before.Player.GridX,before.Player.GridY),resumed.CurrentBattle.Player.GridPosition);
            var after=checkpoints.Store.Read();Assert.AreEqual(before.SchemaVersion,after.SchemaVersion);Assert.AreEqual(before.Sequence,after.Sequence);
            CollectionAssert.AreEqual(before.DeckOrder,after.DeckOrder);CollectionAssert.AreEqual(before.ObtainedOrder,after.ObtainedOrder);
        }

        [TestCase(7,4)][TestCase(8,4)][TestCase(1,1)]
        public void 調整値の中間は奇数だけ後半へ切り上げる(int frames,int commit)
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0));var movement=new PlayerMovementSystem(p,new BattleField(),timing:new NormalMoveTiming(frames,frames));
            var enemies=new System.Collections.Generic.List<Enemy>();
            for(int i=1;i<=frames;i++){
                movement.Tick(17,i==1?Input(1):null,enemies,true);Assert.AreEqual(i<commit?1:2,p.GridPosition.X);
            }
            Assert.IsFalse(movement.IsNormalMoving);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new NormalMoveTiming(0,12));
        }
    }
}
