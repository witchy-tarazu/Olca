using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class DelayedProjectileTests
    {
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;
        private BattleSystem battle;
        private MockCardDatabase cardDatabase;

        [SetUp]
        public void Setup()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemies = new List<Enemy>
            {
                new Enemy("slime1", "スライム1", 100, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("slime2", "スライム2", 100, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("slime3", "スライム3", 100, ElementType.None, new Vector2Int(5, 0))
            };

            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "前方1マス攻撃")
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());

            cardDatabase = new MockCardDatabase();
        }

        [Test]
        public void 遅延投擲効果が登録される()
        {
            // Arrange: ボムカード（Power=0、爆風のみ）
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.5f,
                TargetOffset = new Vector2Int(3, 0), // 前方3マス
                CancelOnHole = true,
                Trigger = DerivativeTrigger.Immediate
            });

            var bomb = new Card("bomb", "ボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
                DamageTargetType.Single, modifier: modifier);

            // Act
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, bomb);
            context.CardDatabase = cardDatabase;

            effect.Execute(context);

            // Assert
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "遅延効果が1つ登録されるべき");
        }

        [Test]
        public void 投擲物が時間経過後に着弾する()
        {
            // Arrange: 爆風カード
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 20, "爆風",
                DamageTargetType.FrontTile);
            cardDatabase.AddCard(explosionCard);

            // ボムカード
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.0f,
                TargetOffset = new Vector2Int(1, 0), // 前方1マス = 位置(3,0)
                CancelOnHole = true,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.Immediate
            });

            var bomb = new Card("bomb", "ボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, bomb);
            context.CardDatabase = cardDatabase;

            // Act: カード使用
            effect.Execute(context);

            // Assert: 着弾前は敵はダメージを受けていない
            Assert.AreEqual(100, enemies[0].CurrentHp, "着弾前は敵1はダメージを受けないべき");
            Assert.AreEqual(100, enemies[1].CurrentHp, "着弾前は敵2はダメージを受けないべき");

            // Act: 0.5秒経過（まだ着弾しない）
            battle.DelayedEffects.Update(0.5f);

            // Assert: まだダメージなし
            Assert.AreEqual(100, enemies[0].CurrentHp, "0.5秒後も敵1はダメージを受けないべき");
            Assert.AreEqual(100, enemies[1].CurrentHp, "0.5秒後も敵2はダメージを受けないべき");

            // Act: さらに0.5秒経過（合計1.0秒で着弾）
            battle.DelayedEffects.Update(0.5f);

            // Assert: 着弾して爆風ダメージが発生
            // 着弾位置(3,0)から前方1マス(4,0)に爆風
            Assert.AreEqual(100, enemies[0].CurrentHp, "敵1(3,0)は爆風範囲外");
            Assert.AreEqual(80, enemies[1].CurrentHp, "敵2(4,0)が爆風で20ダメージを受けるべき");
            Assert.AreEqual(100, enemies[2].CurrentHp, "敵3(5,0)は爆風範囲外");
        }

        [Test]
        public void 投擲物が直接ダメージを与える()
        {
            // Arrange: ホウガン（Power>0、直接ダメージあり）
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.0f,
                TargetOffset = new Vector2Int(1, 0), // 前方1マス = 位置(3,0)
                CancelOnHole = true,
                Trigger = DerivativeTrigger.Immediate
            });

            var hougan = new Card("hougan", "ホウガン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 50, "砲丸投げ",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, hougan);
            context.CardDatabase = cardDatabase;

            // Act: カード使用
            effect.Execute(context);

            // Assert: 着弾前はダメージなし
            Assert.AreEqual(100, enemies[0].CurrentHp);

            // Act: 1.0秒経過で着弾
            battle.DelayedEffects.Update(1.0f);

            // Assert: 着弾地点(3,0)の敵1が直接ダメージを受ける
            Assert.AreEqual(50, enemies[0].CurrentHp, "敵1が直接ダメージ50を受けるべき");
            Assert.AreEqual(100, enemies[1].CurrentHp, "敵2は無傷");
            Assert.AreEqual(100, enemies[2].CurrentHp, "敵3は無傷");
        }

        [Test]
        public void 穴パネルに落下すると効果が無効化される()
        {
            // Arrange: ボムカード
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 20, "爆風",
                DamageTargetType.FrontTile);
            cardDatabase.AddCard(explosionCard);

            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.0f,
                TargetOffset = new Vector2Int(1, 0), // 位置(3,0)
                CancelOnHole = true,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.Immediate
            });

            var bomb = new Card("bomb", "ボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, bomb);
            context.CardDatabase = cardDatabase;

            // Act: カード使用
            effect.Execute(context);

            // 着弾前に着弾地点を穴パネルにする
            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Hole);

            // 1.0秒経過で着弾
            battle.DelayedEffects.Update(1.0f);

            // Assert: 穴パネルに落下して効果が無効化される
            Assert.AreEqual(100, enemies[0].CurrentHp, "穴に落ちて効果が無効化されるべき");
            Assert.AreEqual(100, enemies[1].CurrentHp, "爆風も発生しないべき");
        }

        [Test]
        public void 複数の投擲物を同時に管理できる()
        {
            // Arrange: 2つのボムカード
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 15, "爆風",
                DamageTargetType.FrontTile);
            cardDatabase.AddCard(explosionCard);

            var modifier1 = new CardModifier();
            modifier1.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.0f,
                TargetOffset = new Vector2Int(1, 0), // 位置(3,0)
                CancelOnHole = true,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.Immediate
            });

            var modifier2 = new CardModifier();
            modifier2.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.5f,
                TargetOffset = new Vector2Int(2, 0), // 位置(4,0)
                CancelOnHole = true,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.Immediate
            });

            var bomb1 = new Card("bomb1", "ボム1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾1",
                DamageTargetType.Single, modifier: modifier1);

            var bomb2 = new Card("bomb2", "ボム2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾2",
                DamageTargetType.Single, modifier: modifier2);

            // Act: 2つのボムを使用
            var effect1 = new ModifiableCardEffect(DamageTargetType.Single);
            var context1 = new BattleContext(battle, bomb1);
            context1.CardDatabase = cardDatabase;
            effect1.Execute(context1);

            var effect2 = new ModifiableCardEffect(DamageTargetType.Single);
            var context2 = new BattleContext(battle, bomb2);
            context2.CardDatabase = cardDatabase;
            effect2.Execute(context2);

            // Assert: 2つの遅延効果が登録される
            Assert.AreEqual(2, battle.DelayedEffects.ActiveEffectCount, "2つの遅延効果が登録されるべき");

            // Act: 1.0秒経過（1つ目が着弾）
            battle.DelayedEffects.Update(1.0f);

            // Assert: 1つ目のボムが着弾（位置3,0から前方1マス4,0に爆風）
            Assert.AreEqual(85, enemies[1].CurrentHp, "敵2が1つ目の爆風で15ダメージを受けるべき");
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "1つの遅延効果が残っているべき");

            // Act: さらに0.5秒経過（2つ目が着弾）
            battle.DelayedEffects.Update(0.5f);

            // Assert: 2つ目のボムが着弾（位置4,0から前方1マス5,0に爆風）
            Assert.AreEqual(85, enemies[2].CurrentHp, "敵3が2つ目の爆風で15ダメージを受けるべき");
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount, "すべての遅延効果が完了しているべき");
        }
    }
}
