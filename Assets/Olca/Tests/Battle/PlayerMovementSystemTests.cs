using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PlayerMovementSystemTests
    {
        private PlayerCharacter player;
        private BattleField field;
        private PlayerMovementSystem movement;
        private MockInputProvider input;
        private MockLogger logger;
        private List<Enemy> enemies;

        [SetUp]
        public void SetUp()
        {
            // プレイヤーをグリッドの中央に配置
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            logger = new MockLogger();
            movement = new PlayerMovementSystem(player, field, logger);
            input = new MockInputProvider();
            enemies = new List<Enemy>();
        }

        [Test]
        public void 右移動できる()
        {
            // Arrange
            Vector2Int initialPos = player.GridPosition;

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsTrue(result, "右移動が成功するべき");
            Assert.AreEqual(new Vector2Int(initialPos.X + 1, initialPos.Y), player.GridPosition);
        }

        [Test]
        public void 左移動できる()
        {
            // Arrange
            Vector2Int initialPos = player.GridPosition;

            // Act
            bool result = movement.TryMove(new Vector2Int(-1, 0), enemies);

            // Assert
            Assert.IsTrue(result, "左移動が成功するべき");
            Assert.AreEqual(new Vector2Int(initialPos.X - 1, initialPos.Y), player.GridPosition);
        }

        [Test]
        public void 左端から左に移動できない()
        {
            // Arrange: プレイヤーを左端に配置
            player.MoveTo(new Vector2Int(0, 0));

            // Act
            bool result = movement.TryMove(new Vector2Int(-1, 0), enemies);

            // Assert
            Assert.IsFalse(result, "グリッド外への移動は失敗するべき");
            Assert.AreEqual(new Vector2Int(0, 0), player.GridPosition, "位置は変わらないべき");
        }

        [Test]
        public void 右端から右に移動できない()
        {
            // Arrange: プレイヤーを右端に配置
            player.MoveTo(new Vector2Int(5, 0));

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsFalse(result, "グリッド外への移動は失敗するべき");
            Assert.AreEqual(new Vector2Int(5, 0), player.GridPosition, "位置は変わらないべき");
        }

        [Test]
        public void 敵がいる位置に移動できない()
        {
            // Arrange
            Vector2Int enemyPos = new Vector2Int(3, 0);
            enemies.Add(new Enemy("slime", "スライム", 50, ElementType.None, enemyPos));
            player.MoveTo(new Vector2Int(2, 0));

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsFalse(result, "敵がいる位置への移動は失敗するべき");
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition, "位置は変わらないべき");
        }

        [Test]
        public void 死んだ敵は衝突判定されない()
        {
            // Arrange
            Vector2Int enemyPos = new Vector2Int(3, 0);
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, enemyPos);
            enemy.TakeDamage(50, ElementType.None); // 敵を倒す
            enemies.Add(enemy);
            player.MoveTo(new Vector2Int(2, 0));

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsTrue(result, "死んだ敵は衝突しないべき");
            Assert.AreEqual(new Vector2Int(3, 0), player.GridPosition);
        }

        [Test]
        public void Holeパネルに移動できない()
        {
            // Arrange
            Vector2Int holePos = new Vector2Int(3, 0);
            field.ChangePanelType(holePos, PanelType.Hole);
            player.MoveTo(new Vector2Int(2, 0));

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsFalse(result, "Holeパネルへの移動は失敗するべき");
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
        }

        [Test]
        public void Grassパネルから移動するとクールダウンが設定される()
        {
            // Arrange
            Vector2Int grassPos = new Vector2Int(2, 0);
            field.ChangePanelType(grassPos, PanelType.Grass);
            player.MoveTo(grassPos);

            // Act
            movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.Greater(movement.CurrentCooldown, 0f, "クールダウンが設定されるべき");
            Assert.IsFalse(movement.CanMove, "移動不可になるべき");
        }

        [Test]
        public void クールダウン中は移動できない()
        {
            // Arrange: Grassから移動してクールダウンを設定
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Grass);
            player.MoveTo(new Vector2Int(2, 0));
            movement.TryMove(new Vector2Int(1, 0), enemies);
            Vector2Int currentPos = player.GridPosition;

            // Act
            bool result = movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsFalse(result, "クールダウン中は移動できないべき");
            Assert.AreEqual(currentPos, player.GridPosition, "位置は変わらないべき");
        }

        [Test]
        public void クールダウンはTickで減少する()
        {
            // Arrange: クールダウンを設定
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Grass);
            player.MoveTo(new Vector2Int(2, 0));
            movement.TryMove(new Vector2Int(1, 0), enemies);
            float initialCooldown = movement.CurrentCooldown;

            // Act
            movement.Tick(deltaTimeMs: 100, input, enemies);

            // Assert
            Assert.Less(movement.CurrentCooldown, initialCooldown, "クールダウンが減少するべき");
        }

        [Test]
        public void Iceパネルに移動すると滑り状態になる()
        {
            // Arrange
            Vector2Int icePos = new Vector2Int(3, 0);
            field.ChangePanelType(icePos, PanelType.Ice);
            player.MoveTo(new Vector2Int(2, 0));

            // Act
            movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsTrue(movement.IsSliding, "滑り状態になるべき");
        }

        [Test]
        public void Ice滑りは壁で止まる()
        {
            // Arrange: 右端の手前にIceパネルを配置
            field.ChangePanelType(new Vector2Int(4, 0), PanelType.Ice);
            field.ChangePanelType(new Vector2Int(5, 0), PanelType.Ice);
            player.MoveTo(new Vector2Int(3, 0));

            // Act: 右に移動してIceに入る
            movement.TryMove(new Vector2Int(1, 0), enemies);
            // Tick で滑りを処理
            movement.Tick(deltaTimeMs: 100, input, enemies); // 4→5
            movement.Tick(deltaTimeMs: 100, input, enemies); // 5→壁でストップ

            // Assert
            Assert.AreEqual(new Vector2Int(5, 0), player.GridPosition, "右端で止まるべき");
            Assert.IsFalse(movement.IsSliding, "滑りが終了しているべき");
        }

        [Test]
        public void Ice滑りは敵で止まる()
        {
            // Arrange: Ice パネルと敵を配置
            field.ChangePanelType(new Vector2Int(3, 0), PanelType.Ice);
            field.ChangePanelType(new Vector2Int(4, 0), PanelType.Ice);
            enemies.Add(new Enemy("goblin", "ゴブリン", 50, ElementType.None, new Vector2Int(5, 0)));
            player.MoveTo(new Vector2Int(2, 0));

            // Act: 右に移動してIceに入る
            movement.TryMove(new Vector2Int(1, 0), enemies);
            // Tick で滑りを処理
            movement.Tick(deltaTimeMs: 100, input, enemies); // 3→4
            movement.Tick(deltaTimeMs: 100, input, enemies); // 4→敵でストップ

            // Assert
            Assert.AreEqual(new Vector2Int(4, 0), player.GridPosition, "敵の手前で止まるべき");
            Assert.IsFalse(movement.IsSliding, "滑りが終了しているべき");
        }

        [Test]
        public void Ice滑りは通常パネルで止まる()
        {
            // Arrange: Iceパネルを1マスだけ配置
            field.ChangePanelType(new Vector2Int(3, 0), PanelType.Ice);
            player.MoveTo(new Vector2Int(2, 0));

            // Act: 右に移動してIceに入る
            movement.TryMove(new Vector2Int(1, 0), enemies);
            // Tick で滑りを処理
            movement.Tick(deltaTimeMs: 100, input, enemies); // 3→4（通常パネル）でストップ

            // Assert
            Assert.AreEqual(new Vector2Int(4, 0), player.GridPosition, "通常パネルで止まるべき");
            Assert.IsFalse(movement.IsSliding, "滑りが終了しているべき");
        }

        [Test]
        public void Tickで入力から移動できる()
        {
            // Arrange
            input.SetHorizontal(1.0f); // 右入力
            Vector2Int initialPos = player.GridPosition;

            // Act
            movement.Tick(deltaTimeMs: 100, input, enemies);

            // Assert
            Assert.AreEqual(new Vector2Int(initialPos.X + 1, initialPos.Y), player.GridPosition);
        }

        [Test]
        public void OnMovedイベントが発火する()
        {
            // Arrange
            Vector2Int? movedNewPosition = null;
            Vector2Int? movedOldPosition = null;
            movement.OnMoved += (newPos, oldPos) =>
            {
                movedNewPosition = newPos;
                movedOldPosition = oldPos;
            };

            // Act
            movement.TryMove(new Vector2Int(1, 0), enemies);

            // Assert
            Assert.IsNotNull(movedNewPosition, "OnMovedイベントが発火するべき");
            Assert.AreEqual(new Vector2Int(3, 0), movedNewPosition.Value, "新しい位置が正しい");
            Assert.AreEqual(new Vector2Int(2, 0), movedOldPosition.Value, "古い位置が正しい");
        }

        [Test]
        public void OnMoveBlockedイベントが発火する()
        {
            // Arrange
            Vector2Int? blockedPosition = null;
            movement.OnMoveBlocked += (pos) => blockedPosition = pos;
            player.MoveTo(new Vector2Int(0, 0)); // 左端に配置

            // Act
            movement.TryMove(new Vector2Int(-1, 0), enemies); // 左端から左へ

            // Assert
            Assert.IsNotNull(blockedPosition, "OnMoveBlockedイベントが発火するべき");
        }
    }
}
