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
                new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃", DamageTargetType.Single),
            };
            deck = new Deck(cards);
            logger = new MockLogger();
            battle = new BattleSystem(player, enemies, deck, logger: logger);
        }

        [TestCase("sword")][TestCase("fire_sword")]
        public void 製品ソードは遠方に飛ばず発動位置の隣接マスだけに命中する(string id)
        {
            var factory=new Olca.Application.Battle.SimpleCardFactory();
            var card=factory.CreateById(id);var originalTarget=card.TargetType;
            var effect=CardEffectFactory.CreateEffect(card);
            player.MoveTo(new Vector2Int(1,0));
            effect.Execute(new BattleContext(battle,card));
            Assert.AreEqual(50,enemies[0].CurrentHp);Assert.AreEqual(60,enemies[1].CurrentHp);
            player.MoveTo(new Vector2Int(2,0));effect.Execute(new BattleContext(battle,card));
            Assert.AreEqual(System.Math.Max(0,50-card.Power),enemies[0].CurrentHp);
            Assert.AreEqual(60,enemies[1].CurrentHp);Assert.AreEqual(originalTarget,card.TargetType);
        }
        [Test]
        public void ソードの明示範囲と通常射撃の既定値を上書きしない()
        {
            player.MoveTo(new Vector2Int(1,0));
            var explicitSword=new Card("sword","明示ソード",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,10,"明示2マス",DamageTargetType.FrontTwoTiles);
            CardEffectFactory.CreateEffect(explicitSword).Execute(new BattleContext(battle,explicitSword));Assert.AreEqual(40,enemies[0].CurrentHp);
            var cannon=new Card("cannon","射撃",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,10,"単体射撃");
            CardEffectFactory.CreateEffect(cannon).Execute(new BattleContext(battle,cannon));Assert.AreEqual(30,enemies[0].CurrentHp);
        }
        [TestCase(ExceedLevel.Lv1)][TestCase(ExceedLevel.Lv2)][TestCase(ExceedLevel.Lv3)]
        public void 超越ソードも既存IDで前方1マスを維持する(ExceedLevel level)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var card=f.CreateExceeded(f.CreateById("sword"),level);
            player.MoveTo(new Vector2Int(1,0));CardEffectFactory.CreateEffect(card).Execute(new BattleContext(battle,card));
            Assert.AreEqual(50,enemies[0].CurrentHp);
        }

        [Test]
        public void ModifiableCardEffectで単体攻撃できる()
        {
            // Arrange
            var cannonEffect = new ModifiableCardEffect(DamageTargetType.Single);
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
        public void ModifiableCardEffectで前方1マス攻撃できる()
        {
            // Arrange
            var swordEffect = new ModifiableCardEffect(DamageTargetType.FrontTile);
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
            var effect = new ModifiableCardEffect(DamageTargetType.FrontTile);
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
            var attackCard = new Card("test", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "攻撃", DamageTargetType.Single);

            // Act
            var effect = CardEffectFactory.CreateEffect(attackCard);

            // Assert
            Assert.IsNotNull(effect, "効果が生成されるべき");
            Assert.IsInstanceOf<ModifiableCardEffect>(effect, "Attack カテゴリなのでModifiableCardEffectが生成されるべき");
        }

        [Test]
        public void HealEffectでプレイヤーが回復する()
        {
            // Arrange
            player.TakeDamage(50); // 50ダメージを受ける
            int hpBeforeHeal = player.CurrentHp;
            var healEffect = new HealEffect(30);
            var healCard = new Card("heal", "回復", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 30, "HP回復", DamageTargetType.Single);
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
                new Card("card1", "カード1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "攻撃1", DamageTargetType.Single),
                new Card("card2", "カード2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 20, "攻撃2", DamageTargetType.Single)
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
