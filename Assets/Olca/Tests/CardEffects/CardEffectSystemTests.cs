using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Domain.Master;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class CardEffectSystemTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("goblin", "ゴブリン", 60, ElementType.None, new Vector2Int(4, 0))
            };

            var cards = new List<Card>
            {
                new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃", "CannonEffect"),
            };
            deck = new Deck(cards);
            logger = new MockLogger();
            battle = new BattleSystem(player, enemies, deck, logger: logger);
        }

        [Test]
        public void ModifiableDamageEffectで単体攻撃できる()
        {
            // Arrange
            var cannonEffect = new ModifiableDamageEffect(DamageTargetType.Single);
            var cannon = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
            var context = new BattleContext(battle, cannon);
            int initialHp = enemies[0].CurrentHp;

            // Act
            cannonEffect.Execute(context);

            // Assert
            Assert.Less(enemies[0].CurrentHp, initialHp, "敵のHPが減少するべき");
            Assert.AreEqual(initialHp - 40, enemies[0].CurrentHp, "40ダメージを与えるべき");
        }

        [Test]
        public void ModifiableDamageEffectで前方1マス攻撃できる()
        {
            // Arrange
            var swordEffect = new ModifiableDamageEffect(DamageTargetType.FrontTile);
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "前方1マス攻撃");
            var context = new BattleContext(battle, sword);
            int initialHp = enemies[0].CurrentHp; // 50

            // Act
            swordEffect.Execute(context);

            // Assert
            Assert.Less(enemies[0].CurrentHp, initialHp, "前方の敵のHPが減少するべき");
            Assert.AreEqual(0, enemies[0].CurrentHp, "80ダメージを受けてHPが0になるべき");
        }

        [Test]
        public void ModifierでパネルをIceに変化させられる()
        {
            // Arrange
            var icePanelModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.PanelIce);
            var icePanel = new Card("ice_panel", "アイスパネル", 1, CardRarity.Normal, CardCategory.Field, ElementType.Water, CardSeries.None, 0, "前方1マスをIceに",
                modifier: icePanelModifier);
            var effect = new ModifiableDamageEffect(DamageTargetType.FrontTile);
            var context = new BattleContext(battle, icePanel);
            Vector2Int targetPos = new Vector2Int(player.GridPosition.X + 1, player.GridPosition.Y);

            // Act
            effect.Execute(context);

            // Assert
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(targetPos), "前方のパネルがIceに変化するべき");
        }

        [Test]
        public void CardEffectFactoryでカードから効果を生成できる()
        {
            // Arrange
            var attackCard = new Card("test", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "攻撃", null);

            // Act
            var effect = CardEffectFactory.CreateEffect(attackCard);

            // Assert
            Assert.IsNotNull(effect, "効果が生成されるべき");
            Assert.IsInstanceOf<ModifiableDamageEffect>(effect, "Attack カテゴリなのでModifiableDamageEffectが生成されるべき");
        }

        [Test]
        public void HealEffectでプレイヤーが回復する()
        {
            // Arrange
            player.TakeDamage(50); // 50ダメージを受ける
            int hpBeforeHeal = player.CurrentHp;
            var healEffect = new HealEffect(30);
            var healCard = new Card("heal", "回復", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 30, "HP回復", "HealEffect");
            var context = new BattleContext(battle, healCard);

            // Act
            healEffect.Execute(context);

            // Assert
            Assert.Greater(player.CurrentHp, hpBeforeHeal, "HPが回復するべき");
            Assert.AreEqual(hpBeforeHeal + 30, player.CurrentHp, "30回復するべき");
        }

        [Test]
        public void CardUsageSystemで複数カードを順番に使用できる()
        {
            // Arrange
            var cards = new List<Card>
            {
                new Card("card1", "カード1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "攻撃1", null),
                new Card("card2", "カード2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 20, "攻撃2", null)
            };

            // Act
            battle.CardUsage.EnqueueCards(cards);
            Assert.AreEqual(2, battle.CardUsage.RemainingCount, "2枚のカードがキューにあるべき");

            battle.CardUsage.TryUseCurrentCard();
            Assert.AreEqual(1, battle.CardUsage.RemainingCount, "1枚目使用後は1枚残るべき");

            battle.CardUsage.TryUseCurrentCard();
            Assert.AreEqual(0, battle.CardUsage.RemainingCount, "2枚目使用後は0枚になるべき");
        }

        [Test]
        public void 敵が全滅したら生存敵リストが空になる()
        {
            // Arrange
            int enemy1Hp = enemies[0].CurrentHp;
            int enemy2Hp = enemies[1].CurrentHp;

            // Act: 全ての敵を倒す
            enemies[0].TakeDamage(enemy1Hp, ElementType.None);
            enemies[1].TakeDamage(enemy2Hp, ElementType.None);

            var aliveEnemies = battle.GetAliveEnemies();

            // Assert
            Assert.AreEqual(0, aliveEnemies.Count, "生存敵が0になるべき");
        }
    }
}
