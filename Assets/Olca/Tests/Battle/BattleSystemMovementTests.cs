using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class BattleSystemMovementTests
    {
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
