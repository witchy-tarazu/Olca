using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PanelEffectSystemTests
    {
        private PlayerCharacter player;
        private BattleField field;
        private PanelEffectSystem panelEffects;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            logger = new MockLogger();
            panelEffects = new PanelEffectSystem(field, logger);
        }

        [Test]
        public void Magmaパネルに移動すると即座にダメージを受ける()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            int initialHp = player.CurrentHp;

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.Less(player.CurrentHp, initialHp, "HPが減少するべき");
            Assert.AreEqual(initialHp - 5, player.CurrentHp, "5ダメージ受けるべき");
        }

        [Test]
        public void Magmaパネルはダメージ後に通常パネルに変化する()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos), "Magmaが通常パネルに変化するべき");
        }

        [Test]
        public void Magmaパネルから離れた後に再度戻っても通常パネルになっている()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            player.MoveTo(new Vector2Int(1, 0));

            // Act: Magmaに移動してダメージを受ける
            panelEffects.ApplyOnEnterEffect(player, magmaPos);
            player.MoveTo(magmaPos);
            int hpAfterFirstVisit = player.CurrentHp;

            // 別の位置に移動
            player.MoveTo(new Vector2Int(3, 0));

            // 元のMagma位置に戻る（既に通常パネルになっているはず）
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(95, hpAfterFirstVisit, "最初のダメージは発生する");
            Assert.AreEqual(95, player.CurrentHp, "通常パネルになっているので追加ダメージはない");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos), "パネルは通常のまま");
        }

        [Test]
        public void 複数のMagmaパネルに連続で移動するとそれぞれダメージを受ける()
        {
            // Arrange
            Vector2Int magmaPos1 = new Vector2Int(2, 0);
            Vector2Int magmaPos2 = new Vector2Int(3, 0);
            field.ChangePanelType(magmaPos1, PanelType.Magma);
            field.ChangePanelType(magmaPos2, PanelType.Magma);
            player.MoveTo(new Vector2Int(1, 0));
            int initialHp = player.CurrentHp;

            // Act: 最初のMagmaに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos1);
            player.MoveTo(magmaPos1);

            // 2つ目のMagmaに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos2);

            // Assert
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "10ダメージ（5×2回）受けるべき");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos1), "最初のMagmaは通常に");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos2), "2つ目のMagmaも通常に");
        }

        [Test]
        public void Crackedパネルに乗っても変化しない()
        {
            // Arrange
            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);

            // Act: Crackedパネルに移動
            panelEffects.ApplyOnEnterEffect(player, crackedPos);

            // Assert
            Assert.AreEqual(PanelType.Cracked, field.GetPanelType(crackedPos), "Crackedパネルのまま変化しないべき");
        }

        [Test]
        public void Crackedパネルから離れるとHoleに変化する()
        {
            // Arrange
            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);
            player.MoveTo(crackedPos);

            // Act: Crackedパネルから離れる
            panelEffects.ApplyOnLeaveEffect(player, crackedPos);

            // Assert
            Assert.AreEqual(PanelType.Hole, field.GetPanelType(crackedPos), "CrackedがHoleに変化するべき");
        }

        [Test]
        public void CrackedパネルでOnPanelEffectAppliedイベントが発火する()
        {
            // Arrange
            Vector2Int? effectPosition = null;
            PanelType? effectPanelType = null;
            panelEffects.OnPanelEffectApplied += (pos, type) =>
            {
                effectPosition = pos;
                effectPanelType = type;
            };

            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);
            player.MoveTo(crackedPos);

            // Act: Crackedパネルから離れる
            panelEffects.ApplyOnLeaveEffect(player, crackedPos);

            // Assert
            Assert.IsNotNull(effectPosition, "イベントが発火するべき");
            Assert.AreEqual(crackedPos, effectPosition.Value);
            Assert.AreEqual(PanelType.Cracked, effectPanelType.Value);
        }

        [Test]
        public void MagmaダメージでOnPanelEffectAppliedイベントが発火する()
        {
            // Arrange
            int eventCount = 0;
            panelEffects.OnPanelEffectApplied += (pos, type) =>
            {
                eventCount++;
            };

            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(1, eventCount, "移動時にイベントが1回発火するべき");
        }

        [Test]
        public void プレイヤーが死亡している場合は効果を適用しない()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            player.TakeDamage(100); // プレイヤーを倒す
            Assert.IsTrue(player.IsDead);

            int hpBeforeTick = player.CurrentHp;

            // Act: 死亡しているプレイヤーでMagmaパネルに移動を試みる
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(hpBeforeTick, player.CurrentHp, "死亡している場合はダメージを受けないべき");
            Assert.AreEqual(PanelType.Magma, field.GetPanelType(magmaPos), "パネルも変化しないべき");
        }

        [Test]
        public void 通常パネルでは何も起こらない()
        {
            // Arrange
            Vector2Int normalPos = new Vector2Int(2, 0);
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(normalPos));
            player.MoveTo(normalPos);
            int initialHp = player.CurrentHp;

            // Act
            panelEffects.ApplyOnEnterEffect(player, normalPos);
            panelEffects.Tick(deltaTimeMs: 500, player);

            // Assert
            Assert.AreEqual(initialHp, player.CurrentHp, "通常パネルではダメージを受けないべき");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(normalPos), "パネルタイプは変化しないべき");
        }
    }
}

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class BattleFloatTests
    {
        private PlayerCharacter player;
        private BattleField field;
        private PlayerMovementSystem movement;
        private System.Collections.Generic.List<Enemy> enemies;
        private MockInputProvider input;

        [SetUp]
        public void Setup()
        {
            player = new PlayerCharacter(100, new Vector2Int(1, 0));
            field = new BattleField();
            movement = new PlayerMovementSystem(player, field);
            enemies = new System.Collections.Generic.List<Enemy>();
            input = new MockInputProvider();
        }

        [Test]
        public void 穴上で解除しても滞在と退出ができ再進入はできない()
        {
            var hole = new Vector2Int(2, 0);
            field.ChangePanelType(hole, PanelType.Hole);
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            player.TerrainState.IsFloating = true;
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            player.TerrainState.Clear();
            movement.Tick(200, input, enemies);
            Assert.AreEqual(hole, player.GridPosition);
            Assert.AreEqual(100, player.CurrentHp);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            movement.Tick(200, input, enemies);
            Assert.IsFalse(movement.TryMove(new Vector2Int(-1, 0), enemies));
        }

        [Test]
        public void 浮遊しても障害物と相手と範囲外は越えられない()
        {
            player.TerrainState.IsFloating = true;
            var next = new Vector2Int(2, 0);
            field.ChangePanelType(next, PanelType.Hole);
            field.PlaceObstacle(next, new FieldObstacle("rock", 10));
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            field.RemoveObstacle(next);
            enemies.Add(new Enemy("e", "敵", 20, ElementType.None, next));
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsFalse(movement.TryMove(new Vector2Int(0, 1), enemies));
            player.MoveTo(new Vector2Int(0, 0));
            Assert.IsFalse(movement.TryMove(new Vector2Int(-1, 0), enemies));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void プレイヤーと敵で共通判定し個体間で状態を共有しない(bool floating)
        {
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            var other = new Enemy("other", "敵", 20, ElementType.None, new Vector2Int(5, 0));
            player.TerrainState.IsFloating = floating;
            enemy.TerrainState.IsFloating = floating;
            var hole = new Vector2Int(2, 0);
            field.ChangePanelType(hole, PanelType.Hole);
            Assert.AreEqual(floating, field.CanMoveTo(hole, player.TerrainState));
            Assert.AreEqual(floating, field.CanMoveTo(hole, enemy.TerrainState));
            Assert.AreEqual(!floating, enemy.TerrainState.ReceivesPanelEffects);
            Assert.IsFalse(other.TerrainState.IsFloating);
            field.PlaceObstacle(hole, new FieldObstacle("rock", 10));
            Assert.IsFalse(field.CanMoveTo(hole, enemy.TerrainState));
            Assert.IsFalse(field.CanMoveTo(new Vector2Int(6, 0), enemy.TerrainState));
        }

        [Test]
        public void 浮遊中は氷の滑りも草むら移動遅延も受けない()
        {
            player.TerrainState.IsFloating = true;
            field.ChangePanelType(player.GridPosition, PanelType.Grass);
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Ice);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.AreEqual(150, movement.CurrentCooldownMs);
            Assert.IsFalse(movement.IsSliding);
            movement.Tick(200, input, enemies);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
        }

        [Test]
        public void 滑り中に浮遊を付与すると滑りが止まり解除後は再び滑る()
        {
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Ice);
            field.ChangePanelType(new Vector2Int(3, 0), PanelType.Ice);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsTrue(movement.IsSliding);
            player.TerrainState.IsFloating = true;
            Assert.IsFalse(movement.IsSliding);
            movement.Tick(200, input, enemies);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            player.TerrainState.Clear();
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsTrue(movement.IsSliding);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void マグマダメージとヒビ破壊から浮遊者を除外する(bool floating)
        {
            var effects = new PanelEffectSystem(field);
            player.TerrainState.IsFloating = floating;
            field.ChangePanelType(player.GridPosition, PanelType.Magma);
            effects.ApplyOnEnterEffect(player, player.GridPosition);
            Assert.AreEqual(floating ? 100 : 95, player.CurrentHp);
            field.ChangePanelType(player.GridPosition, PanelType.Cracked);
            effects.ApplyOnLeaveEffect(player, player.GridPosition);
            Assert.AreEqual(floating ? PanelType.Cracked : PanelType.Hole, field.GetPanelType(player.GridPosition));
        }

        [Test]
        public void 浮遊は直接攻撃への無敵にはならない()
        {
            player.TerrainState.IsFloating = true;
            player.TakeDamage(10);
            Assert.AreEqual(90, player.CurrentHp);
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            enemy.TerrainState.IsFloating = true;
            enemy.TakeDamage(10, ElementType.Fire);
            Assert.AreEqual(10, enemy.CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 勝敗確定時に両者の浮遊を通知より先に解除する(bool defeat)
        {
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            enemies.Add(enemy);
            var deck = new Deck(new System.Collections.Generic.List<Card> { new Card("c", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト") });
            var battle = new BattleSystem(player, enemies, deck);
            player.TerrainState.IsFloating = enemy.TerrainState.IsFloating = true;
            bool notified = false;
            battle.OnBattleEnd += result => { notified = true; Assert.IsFalse(player.TerrainState.IsFloating); Assert.IsFalse(enemy.TerrainState.IsFloating); };
            if (defeat) player.TakeDamage(100); else enemy.TakeDamage(20, ElementType.None);
            Assert.IsTrue(notified);
        }

        [TestCase(false, 1)]
        [TestCase(true, 3)]
        public void 一時移動の経路も同じ穴判定を使う(bool floating, int expectedX)
        {
            player.TerrainState.IsFloating = floating;
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Hole);
            var effect = new Olca.Core.Logic.CardEffects.Effects.TemporaryMoveAttackEffect(new Vector2Int(1, 0), 2, 0, 0, MovePenetrationMode.Blocking);
            var calculate = effect.GetType().GetMethod("CalculateTargetPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (Vector2Int)calculate.Invoke(effect, new object[] { player.GridPosition, field, enemies, player.TerrainState });
            Assert.AreEqual(expectedX, result.X);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void Battleの通常入力更新から地形効果の除外へ接続される(bool floating)
        {
            var deck = new Deck(new System.Collections.Generic.List<Card> { new Card("c", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト") });
            var battle = new BattleSystem(player, enemies, deck);
            player.TerrainState.IsFloating = floating;
            battle.Field.ChangePanelType(player.GridPosition, PanelType.Cracked);
            battle.Field.ChangePanelType(new Vector2Int(2, 0), PanelType.Magma);
            input.Horizontal = 1;
            battle.Tick(200, input);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            Assert.AreEqual(floating ? 100 : 95, player.CurrentHp);
            Assert.AreEqual(floating ? PanelType.Cracked : PanelType.Hole, battle.Field.GetPanelType(new Vector2Int(1, 0)));
        }
    }
}
