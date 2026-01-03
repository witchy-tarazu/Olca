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
    }
}
