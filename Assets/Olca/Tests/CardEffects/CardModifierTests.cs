using NUnit.Framework;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System.Reflection;
using Olca.Application.Battle;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class CardModifierTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;

        [SetUp]
        public void SetUp()
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
        }

        [Test]
        public void アタック10でダメージが10増加する()
        {
            // Arrange
            var baseCard = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "攻撃");
            var attackPlus10 = new Card("atk+10", "アタック+10", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+10",
                modifier: CardModifier.CreateAttackBonus(10));

            // Act
            var tunedCard = baseCard.ApplyTuning(attackPlus10);

            // Assert
            Assert.AreEqual(90, tunedCard.GetFinalPower(), "威力が80+10=90になるべき");
        }

        [Test]
        public void 複数の調律効果を重ねられる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
            var attackPlus10 = new Card("atk+10", "アタック+10", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+10",
                modifier: CardModifier.CreateAttackBonus(10));
            var attackPlus30 = new Card("atk+30", "アタック+30", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+30",
                modifier: CardModifier.CreateAttackBonus(30));

            // Act
            var tunedCard = baseCard.ApplyTuning(attackPlus10).ApplyTuning(attackPlus30);

            // Assert
            Assert.AreEqual(80, tunedCard.GetFinalPower(), "威力が40+10+30=80になるべき");
        }

        [Test]
        public void スプレッド効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
            var spreadModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread);
            var spreadCard = new Card("spread", "スプレッド", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "スプレッド効果",
                modifier: spreadModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(spreadCard);
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            int enemy1HpBefore = enemies[0].CurrentHp;
            int enemy2HpBefore = enemies[1].CurrentHp;

            effect.Execute(context);

            // Assert
            // 着弾地点(3,0) + 前方(4,0) にダメージが入るべき
            Assert.Less(enemies[0].CurrentHp, enemy1HpBefore, "最前列の敵がダメージを受けるべき");
            Assert.Less(enemies[1].CurrentHp, enemy2HpBefore, "2番目の敵もスプレッド効果でダメージを受けるべき");
        }

        [Test]
        public void 貫通効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 30, "単体攻撃");
            var penetrationModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Penetration);
            var penetrationCard = new Card("penetration", "貫通", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "貫通効果",
                modifier: penetrationModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(penetrationCard);
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            effect.Execute(context);

            // Assert
            // 全ての敵が貫通ダメージを受けるべき
            Assert.AreEqual(70, enemies[0].CurrentHp, "1番目の敵が30ダメージを受けるべき");
            Assert.AreEqual(70, enemies[1].CurrentHp, "2番目の敵も貫通で30ダメージを受けるべき");
            Assert.AreEqual(70, enemies[2].CurrentHp, "3番目の敵も貫通で30ダメージを受けるべき");
        }

        [Test]
        public void 多段ヒット効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 15, "単体攻撃");
            var multiHitModifier = new CardModifier();
            multiHitModifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.MultiHit,
                HitCount = 4
            });
            var multiHitCard = new Card("multi", "多段ヒット", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "4ヒット",
                modifier: multiHitModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(multiHitCard);
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            effect.Execute(context);

            // Assert
            // 15ダメージ × 4ヒット = 60ダメージ
            Assert.AreEqual(40, enemies[0].CurrentHp, "15×4=60ダメージを受けるべき");
        }

        [Test]
        public void ソードにスプレッド効果を付与すると範囲が拡大する()
        {
            // Arrange: ソード（前方1マス攻撃）
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 50, "前方1マス");
            var spreadModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread);
            var spreadCard = new Card("spread", "スプレッド", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "スプレッド",
                modifier: spreadModifier);

            // Act: ソード + スプレッド
            var tunedSword = sword.ApplyTuning(spreadCard);
            var effect = new ModifiableCardEffect(DamageTargetType.FrontTile);
            var context = new BattleContext(battle, tunedSword);

            int enemy1HpBefore = enemies[0].CurrentHp;
            int enemy2HpBefore = enemies[1].CurrentHp;

            effect.Execute(context);

            // Assert
            // 前方1マス(3,0)に攻撃 → スプレッドで(2,0)(3,0)(4,0)に拡散
            Assert.Less(enemies[0].CurrentHp, enemy1HpBefore, "前方の敵がダメージを受けるべき");
            Assert.Less(enemies[1].CurrentHp, enemy2HpBefore, "スプレッド効果で2番目の敵もダメージを受けるべき");
        }

        [Test]
        public void Immediate効果で無条件にパネル変化できる()
        {
            // Arrange: Immediateトリガーで全体氷パネル化
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelIceAll,
                Trigger = DerivativeTrigger.Immediate
            });
            var iceCard = new Card("ice_age", "アイスエイジ", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Water, CardSeries.None, 0, "画面全体氷化",
                modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, iceCard);

            // Act
            effect.Execute(context);

            // Assert: 画面全体が氷パネルになるべき
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
            {
                for (int y = 0; y < BattleField.GRID_HEIGHT; y++)
                {
                    Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(new Vector2Int(x, y)),
                        $"位置({x},{y})が氷パネルになるべき");
                }
            }
        }

        [Test]
        public void OnHit効果でヒットした敵の位置がパネル変化する()
        {
            // Arrange: OnHitトリガーでヒット位置を氷パネル化
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelIce,
                Trigger = DerivativeTrigger.OnHit
            });
            var iceAttack = new Card("ice_strike", "アイスストライク", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Water, CardSeries.None, 30, "攻撃+氷化",
                modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, iceAttack);

            Vector2Int targetPos = enemies[0].GridPosition;

            // Act
            effect.Execute(context);

            // Assert
            Assert.AreEqual(70, enemies[0].CurrentHp, "敵が30ダメージを受けるべき");
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(targetPos), "ヒットした敵の位置が氷パネルになるべき");
        }

        [Test]
        public void OnDamage効果でダメージを与えた敵の位置がパネル変化する()
        {
            // Arrange: OnDamageトリガーでダメージ位置をマグマパネル化
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelMagma,
                Trigger = DerivativeTrigger.OnDamage
            });
            var fireAttack = new Card("fire_strike", "ファイアストライク", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 40, "攻撃+マグマ化",
                modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, fireAttack);

            Vector2Int targetPos = enemies[0].GridPosition;

            // Act
            effect.Execute(context);

            // Assert
            Assert.AreEqual(60, enemies[0].CurrentHp, "敵が40ダメージを受けるべき");
            Assert.AreEqual(PanelType.Magma, battle.Field.GetPanelType(targetPos), "ダメージを与えた敵の位置がマグマパネルになるべき");
        }

        [Test]
        public void AfterActionIfHit効果でヒットしていれば行動終了時に発動する()
        {
            // Arrange: 全体攻撃後、ヒットしていれば画面全体をマグマ化
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelMagmaAll,
                Trigger = DerivativeTrigger.AfterActionIfHit
            });
            var flameBomb = new Card("flame_bomb", "フレイムボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 20, "全体攻撃+マグマ化",
                DamageTargetType.All, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.All);
            var context = new BattleContext(battle, flameBomb);

            // Act
            effect.Execute(context);

            // Assert
            // 全敵が20ダメージを受ける
            Assert.AreEqual(80, enemies[0].CurrentHp, "敵1が20ダメージを受けるべき");
            Assert.AreEqual(80, enemies[1].CurrentHp, "敵2が20ダメージを受けるべき");
            Assert.AreEqual(80, enemies[2].CurrentHp, "敵3が20ダメージを受けるべき");

            // 行動終了時に画面全体がマグマパネルになるべき
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
            {
                for (int y = 0; y < BattleField.GRID_HEIGHT; y++)
                {
                    Assert.AreEqual(PanelType.Magma, battle.Field.GetPanelType(new Vector2Int(x, y)),
                        $"位置({x},{y})がマグマパネルになるべき");
                }
            }
        }

        [Test]
        public void AfterActionIfHit効果でヒットしていなければ発動しない()
        {
            // Arrange: 敵がいない位置に攻撃（ヒットしない）
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelMagmaAll,
                Trigger = DerivativeTrigger.AfterActionIfHit
            });

            // プレイヤーを敵がいない位置に移動
            player = new PlayerCharacter(100, new Vector2Int(0, 0));
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());

            var flameBomb = new Card("flame_bomb", "フレイムボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 20, "全体攻撃+マグマ化",
                DamageTargetType.FrontTile, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.FrontTile);
            var context = new BattleContext(battle, flameBomb);

            // Act
            effect.Execute(context);

            // Assert: ヒットしていないのでパネル変化は発動しないべき
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(new Vector2Int(0, 0)), "パネル変化は発動しないべき");
        }

        [Test]
        public void AfterActionIfDamaged効果でダメージを与えていれば行動終了時に発動する()
        {
            // Arrange: 多段ヒット後、ダメージを与えた敵の位置を氷パネル化
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.MultiHit,
                HitCount = 3,
                Trigger = DerivativeTrigger.Immediate
            });
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelIce,
                Trigger = DerivativeTrigger.AfterActionIfDamaged
            });

            var iceBurst = new Card("ice_burst", "アイスバースト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Water, CardSeries.None, 15, "3段攻撃+氷化",
                DamageTargetType.All, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.All);
            var context = new BattleContext(battle, iceBurst);

            // Act
            effect.Execute(context);

            // Assert
            // 全敵が15×3=45ダメージを受ける
            Assert.AreEqual(55, enemies[0].CurrentHp, "敵1が45ダメージを受けるべき");
            Assert.AreEqual(55, enemies[1].CurrentHp, "敵2が45ダメージを受けるべき");
            Assert.AreEqual(55, enemies[2].CurrentHp, "敵3が45ダメージを受けるべき");

            // ダメージを与えた敵の位置が氷パネルになるべき
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(enemies[0].GridPosition), "敵1の位置が氷パネルになるべき");
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(enemies[1].GridPosition), "敵2の位置が氷パネルになるべき");
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(enemies[2].GridPosition), "敵3の位置が氷パネルになるべき");
        }

        [Test]
        public void ChainCard効果で自分自身を再発動できる()
        {
            // Arrange: ChainCardIdが空文字列の場合、自分自身を再発動
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = "", // 空 = 自分自身
                HitCount = 1,
                Trigger = DerivativeTrigger.OnHit,
                EffectOrigin = ChainEffectOrigin.PlayerPosition  // プレイヤー位置から連続攻撃
            });

            var doubleSword = new Card("double_sword", "ダブルソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 25, "2回攻撃",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, doubleSword);

            // Act
            effect.Execute(context);

            // Assert: 25ダメージ × 2 = 50ダメージ
            Assert.AreEqual(50, enemies[0].CurrentHp, "2回攻撃で50ダメージを受けるべき");
        }

        [Test]
        public void ChainCard効果で別カードを発動できる()
        {
            // Arrange: チェイン用の爆風カードを準備
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 15, "爆風",
                DamageTargetType.FrontTile);

            // CardDatabaseのモックを作成
            var cardDatabase = new MockCardDatabase();
            cardDatabase.AddCard(explosionCard);

            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = "explosion",
                HitCount = 1,
                Trigger = DerivativeTrigger.OnHit,
                EffectOrigin = ChainEffectOrigin.HitPosition  // ヒット位置から爆風を発動
            });

            var spreadGun = new Card("spread_gun", "スプレッドガン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "本体攻撃+爆風",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, spreadGun);
            context.CardDatabase = cardDatabase;

            // Act
            effect.Execute(context);

            // Assert
            // 1番目の敵(3,0): 30（本体のみ）= 30ダメージ
            // 爆風はヒット位置(3,0)から前方1マス(4,0)に発動するため、1番目の敵自身は爆風を受けない
            Assert.AreEqual(70, enemies[0].CurrentHp, "1番目の敵が30ダメージを受けるべき");
            // 2番目の敵(4,0): 爆風は1番目の敵の位置(3,0)から前方1マス(4,0)なので、15ダメージ
            Assert.AreEqual(85, enemies[1].CurrentHp, "2番目の敵が爆風の15ダメージを受けるべき");
        }

        [Test]
        public void 全パネル変化効果が敵の位置に関わらず発動する()
        {
            // Arrange: 敵がいない状態でも全パネルを変化させる
            var emptyBattle = new BattleSystem(player, new List<Enemy>(), deck, logger: new MockLogger());

            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelGrassAll,
                Trigger = DerivativeTrigger.Immediate
            });

            var grassCard = new Card("grass_field", "グラスフィールド", 1, CardRarity.Normal, CardCategory.Field, ElementType.Earth, CardSeries.None, 0, "画面全体草化",
                modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(emptyBattle, grassCard);

            // Act
            effect.Execute(context);

            // Assert: 敵がいなくても全パネルが草に変化
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
            {
                for (int y = 0; y < BattleField.GRID_HEIGHT; y++)
                {
                    Assert.AreEqual(PanelType.Grass, emptyBattle.Field.GetPanelType(new Vector2Int(x, y)),
                        $"位置({x},{y})が草パネルになるべき（敵不在でも発動）");
                }
            }
        }

        [Test]
        public void 複数の派生効果を同時に設定できる()
        {
            // Arrange: 多段ヒット + 氷パネル化 + チェイン
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.MultiHit,
                HitCount = 2,
                Trigger = DerivativeTrigger.Immediate
            });
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.PanelIce,
                Trigger = DerivativeTrigger.OnDamage
            });

            var complexCard = new Card("complex", "複合攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Water, CardSeries.None, 20, "2段+氷化",
                DamageTargetType.Single, modifier: modifier);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, complexCard);

            Vector2Int targetPos = enemies[0].GridPosition;

            // Act
            effect.Execute(context);

            // Assert
            Assert.AreEqual(60, enemies[0].CurrentHp, "20×2=40ダメージを受けるべき");
            Assert.AreEqual(PanelType.Ice, battle.Field.GetPanelType(targetPos), "ダメージ後に氷パネルになるべき");
        }
    }
}

// MockCardDatabase の実装
namespace Olca.Tests.CardEffects
{
    using Olca.Core.Logic.CardEffects;

    public class MockCardDatabase : ICardDatabase
    {
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();

        public void AddCard(Card card)
        {
            cards[card.Id] = card;
        }

        public Card GetCardById(string cardId)
        {
            if (cards.TryGetValue(cardId, out var card))
            {
                return card;
            }
            return null;
        }
        private Card DetailedCard(ExceedLevel level)
        {
            var modifier = new CardModifier { BonusDamage = 7, DamageMultiplier = 1.5f, ElementOverride = ElementType.Water };
            modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.DelayedProjectile, Power = 19, Duration = 2.5f,
                HitCount = 3, Trigger = DerivativeTrigger.OnDamage, ChainCardId = "linked_card",
                EffectOrigin = ChainEffectOrigin.HitPosition, DelayTime = 0.75f,
                TargetOffset = new Vector2Int(2, 0), CancelOnHole = true });
            return new Card("grade_" + level, "段階" + level, 3, CardRarity.Limited, CardCategory.Attack,
                ElementType.Fire, CardSeries.Sword, 37, "定義保持", DamageTargetType.Single,
                "Ice", "Right", 2, modifier, 0.2f, 0.3f, MovePenetrationMode.Invincible,
                0.4f, 0.5f, true, true, level);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void Cloneは全定義と段階を保持し可変効果を独立させる(ExceedLevel level)
        {
            var original = DetailedCard(level);
            var clone = original.Clone();
            Assert.AreNotSame(original, clone);
            foreach (var property in typeof(Card).GetProperties())
            {
                if (property.Name == nameof(Card.Modifier)) continue;
                Assert.AreEqual(property.GetValue(original), property.GetValue(clone), property.Name);
            }
            Assert.AreEqual(original.GetFinalPower(), clone.GetFinalPower());
            Assert.AreEqual(original.GetFinalElement(), clone.GetFinalElement());
            Assert.AreNotSame(original.Modifier, clone.Modifier);
            Assert.AreNotSame(original.Modifier.AdditionalEffects, clone.Modifier.AdditionalEffects);
            var sourceEffect = original.Modifier.AdditionalEffects[0];
            var copyEffect = clone.Modifier.AdditionalEffects[0];
            Assert.AreNotSame(sourceEffect, copyEffect);
            foreach (var property in typeof(AdditionalEffect).GetProperties())
                Assert.AreEqual(property.GetValue(sourceEffect), property.GetValue(copyEffect), property.Name);
            clone.Modifier.BonusDamage = 99;clone.Modifier.DamageMultiplier = 3;
            clone.Modifier.ElementOverride = ElementType.Fire;
            copyEffect.Power = 999;copyEffect.TargetOffset = new Vector2Int(5, 0);copyEffect.ChainCardId = "changed";
            clone.Modifier.AdditionalEffects.Add(new AdditionalEffect());
            Assert.AreEqual(7, original.Modifier.BonusDamage);Assert.AreEqual(1.5f, original.Modifier.DamageMultiplier);
            Assert.AreEqual(ElementType.Water, original.Modifier.ElementOverride);
            Assert.AreEqual(19, sourceEffect.Power);Assert.AreEqual(new Vector2Int(2, 0), sourceEffect.TargetOffset);
            Assert.AreEqual("linked_card", sourceEffect.ChainCardId);Assert.AreEqual(1, original.Modifier.AdditionalEffects.Count);
            sourceEffect.Duration = 8;Assert.AreEqual(2.5f, copyEffect.Duration);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void Factory生成とデッキ手札の別個体がカタログや他カードを変更しない(ExceedLevel level)
        {
            var factory = new SimpleCardFactory();var definition = DetailedCard(level);
            var catalog = (Dictionary<string, Card>)typeof(SimpleCardFactory).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(factory);
            catalog[definition.Id] = definition; // 段階定義を持つfixture。上位定義生成仕様は変更しない。
            var a = factory.CreateById(definition.Id);var b = factory.CreateById(definition.Id);
            var copyDeck = new Deck(new List<Card> { a, b });
            var hand = copyDeck.DrawCards(2, new MockRandomProvider(0));
            hand[0].Modifier.AdditionalEffects[0].Power = 200;
            Assert.AreEqual(level, a.ExceedLevel);Assert.AreEqual(level, b.ExceedLevel);
            Assert.AreEqual(19, hand[1].Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(19, definition.Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(19, factory.CreateById(definition.Id).Modifier.AdditionalEffects[0].Power);
            Assert.AreSame(a, copyDeck.GetAllCards()[0], "Deck自体は参照を保持する既存契約");
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void 調律結果と戦闘使用コピーの効果は両素材や元カードと共有しない(ExceedLevel level)
        {
            var original = DetailedCard(level);
            var material = new Card("tune", "調律", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "素材", modifier: CardModifier.CreateAdditionalEffect(AdditionalEffectType.Poison, 3));
            var tuned = original.ApplyTuning(material);
            var used = tuned.WithPowerMultiplier(2);
            Assert.AreEqual(level, tuned.ExceedLevel);Assert.AreEqual(level, used.ExceedLevel);
            Assert.AreEqual(tuned.Id, used.Id);Assert.AreEqual(tuned.GetFinalPower() * 2, used.GetFinalPower());
            Assert.AreNotSame(original.Modifier.AdditionalEffects[0], tuned.Modifier.AdditionalEffects[0]);
            Assert.AreNotSame(material.Modifier.AdditionalEffects[0], tuned.Modifier.AdditionalEffects[1]);
            used.Modifier.AdditionalEffects[0].Power = 400;
            used.Modifier.AdditionalEffects[1].Power = 500;
            Assert.AreEqual(19, tuned.Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(3, tuned.Modifier.AdditionalEffects[1].Power);
            Assert.AreEqual(19, original.Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(3, material.Modifier.AdditionalEffects[0].Power);
        }


        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void 自己チェインコピーは威力とModifier以外の全定義を保持する(ExceedLevel level)
        {
            var source = DetailedCard(level);
            var snapshot = source.Clone();
            foreach (int percent in new[] { -1, 0, 50, 100, 150 })
            {
                var copy = source.CreateSelfChainCopy(percent);
                foreach (var property in typeof(Card).GetProperties())
                {
                    if (property.Name == nameof(Card.Modifier) || property.Name == nameof(Card.Power)) continue;
                    Assert.AreEqual(property.GetValue(source), property.GetValue(copy), property.Name);
                }
                Assert.AreEqual(percent > 0 ? source.Power * percent / 100 : source.Power, copy.Power);
                Assert.AreEqual(0, copy.Modifier.BonusDamage);
                Assert.AreEqual(1f, copy.Modifier.DamageMultiplier);
                Assert.IsNull(copy.Modifier.ElementOverride);
                Assert.IsEmpty(copy.Modifier.AdditionalEffects);
                Assert.AreNotSame(source.Modifier, copy.Modifier);
            }
            Assert.AreEqual(snapshot.Power, source.Power);
            Assert.AreEqual(snapshot.ExceedLevel, source.ExceedLevel);
            Assert.AreEqual(snapshot.Modifier.AdditionalEffects[0].Power, source.Modifier.AdditionalEffects[0].Power);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void 自己チェイン生成物の可変効果は元と別コピーへ漏れない(ExceedLevel level)
        {
            var source = DetailedCard(level);
            var first = source.CreateSelfChainCopy(100);
            var second = source.CreateSelfChainCopy(100);
            first.Modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Poison, Power = 7 });
            var clone = first.Clone();
            clone.Modifier.AdditionalEffects[0].Power = 99;
            first.Modifier.BonusDamage = 80;
            Assert.AreEqual(7, first.Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(19, source.Modifier.AdditionalEffects[0].Power);
            Assert.IsEmpty(second.Modifier.AdditionalEffects);
            Assert.AreEqual(0, second.Modifier.BonusDamage);
            Assert.AreEqual(level, clone.ExceedLevel);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public async Task Factoryから調律して自己チェインしても一枚だけ消費し元定義を変更しない(ExceedLevel level)
        {
            var factory = new SimpleCardFactory();
            var normal = factory.CreateById(SimpleCardFactory.Sword);
            var source = level == ExceedLevel.Normal ? normal : factory.CreateExceeded(normal, level);
            var material = new Card("chain_tune", "チェイン調律", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "検証素材");
            material.Modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.OnHit, ChainCardId = "", Power = 50, HitCount = 1,
                EffectOrigin = ChainEffectOrigin.PlayerPosition });
            var tuned = source.ApplyTuning(material);
            var target = new Enemy("target", "検証敵", 1000, ElementType.None, new Vector2Int(3, 0));
            var local = new BattleSystem(new PlayerCharacter(100, new Vector2Int(2, 0)),
                new List<Enemy> { target }, new Deck());
            var next = normal.Clone();
            local.CardUsage.EnqueueCards(new List<Card> { tuned, next });
            int used = 0, hits = 0; Card consumed = null;
            local.CardUsage.OnCardUsed += card => { used++; consumed = card; };
            target.OnDamaged += (_, __) => hits++;
            Assert.IsTrue(await local.CardUsage.TryUseCurrentCardAsync());
            Assert.AreEqual(1000 - source.Power - source.Power * 50 / 100, target.CurrentHp);
            Assert.AreEqual(2, hits);
            Assert.AreEqual(1, used);
            Assert.AreSame(tuned, consumed);
            Assert.AreEqual(level, consumed.ExceedLevel);
            Assert.AreEqual(source.Cost, consumed.Cost);
            Assert.AreEqual(level, tuned.CreateSelfChainCopy(50).ExceedLevel);
            CollectionAssert.AreEqual(new[] { next }, local.CardUsage.GetRemainingCards());
            Assert.IsEmpty(source.Modifier.AdditionalEffects);
            Assert.AreEqual(50, material.Modifier.AdditionalEffects[0].Power);
            Assert.AreNotSame(material.Modifier.AdditionalEffects[0], tuned.Modifier.AdditionalEffects[0]);
        }
    }
}
