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
