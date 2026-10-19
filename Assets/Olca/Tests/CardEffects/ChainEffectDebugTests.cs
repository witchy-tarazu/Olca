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
    public class ChainEffectDebugTests
    {
        [Test]
        public void ChainCard効果のトリガーを確認()
        {
            // Arrange
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.OnHit,
                EffectOrigin = ChainEffectOrigin.HitPosition
            });

            var spreadGun = new Card("spread_gun", "スプレッドガン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "本体攻撃+爆風",
                DamageTargetType.Single, modifier: modifier);

            // Assert
            Assert.AreEqual(1, spreadGun.Modifier.AdditionalEffects.Count);
            Assert.AreEqual(AdditionalEffectType.ChainCard, spreadGun.Modifier.AdditionalEffects[0].EffectType);
            Assert.AreEqual(DerivativeTrigger.OnHit, spreadGun.Modifier.AdditionalEffects[0].Trigger);
            Assert.AreEqual("explosion", spreadGun.Modifier.AdditionalEffects[0].ChainCardId);
            Assert.AreEqual(ChainEffectOrigin.HitPosition, spreadGun.Modifier.AdditionalEffects[0].EffectOrigin);
        }

        [Test]
        public void CardDatabaseが正しく動作する()
        {
            // Arrange
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 15, "爆風",
                DamageTargetType.FrontTile);

            var cardDatabase = new MockCardDatabase();
            cardDatabase.AddCard(explosionCard);

            // Act
            var retrieved = cardDatabase.GetCardById("explosion");

            // Assert
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("explosion", retrieved.Id);
            Assert.AreEqual(15, retrieved.Power);
            Assert.AreEqual(DamageTargetType.FrontTile, retrieved.TargetType);
        }

        [Test]
        public void TemporaryPlayerPositionが正しく機能する()
        {
            // Arrange
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemies = new List<Enemy>();
            var deck = new Deck(new List<Card>());
            var battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
            var card = new Card("test", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト");

            var context = new BattleContext(battle, card);

            // Act & Assert
            // デフォルトはプレイヤー位置
            Assert.AreEqual(new Vector2Int(2, 0), context.GetEffectivePlayerPosition());

            // TemporaryPlayerPositionを設定
            context.TemporaryPlayerPosition = new Vector2Int(5, 0);
            Assert.AreEqual(new Vector2Int(5, 0), context.GetEffectivePlayerPosition());

            // nullに戻す
            context.TemporaryPlayerPosition = null;
            Assert.AreEqual(new Vector2Int(2, 0), context.GetEffectivePlayerPosition());
        }

        [Test]
        public void 実際のChainCard実行をステップバイステップで確認()
        {
            // Arrange
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemies = new List<Enemy>
            {
                new Enemy("slime1", "スライム1", 100, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("slime2", "スライム2", 100, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("slime3", "スライム3", 100, ElementType.None, new Vector2Int(5, 0))
            };

            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "前方1マス攻撃")
            };
            var deck = new Deck(cards);
            var battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());

            // 爆風カード
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 15, "爆風",
                DamageTargetType.FrontTile);

            var cardDatabase = new MockCardDatabase();
            cardDatabase.AddCard(explosionCard);

            // スプレッドガンカード
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = "explosion",
                HitCount = 1,
                Trigger = DerivativeTrigger.OnHit,
                EffectOrigin = ChainEffectOrigin.HitPosition
            });

            var spreadGun = new Card("spread_gun", "スプレッドガン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 30, "本体攻撃+爆風",
                DamageTargetType.Single, modifier: modifier);

            // 確認1: モディファイアが正しく設定されているか
            Assert.AreEqual(1, spreadGun.Modifier.AdditionalEffects.Count, "モディファイアに効果が1つあるべき");
            Assert.AreEqual(AdditionalEffectType.ChainCard, spreadGun.Modifier.AdditionalEffects[0].EffectType, "ChainCard効果であるべき");
            Assert.AreEqual(DerivativeTrigger.OnHit, spreadGun.Modifier.AdditionalEffects[0].Trigger, "OnHitトリガーであるべき");

            // Act
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, spreadGun);
            context.CardDatabase = cardDatabase;

            effect.Execute(context);

            // Assert
            // 本体攻撃で1番目の敵が30ダメージを受けるべき
            Assert.AreEqual(70, enemies[0].CurrentHp, "1番目の敵が30ダメージを受けるべき");

            // 爆風で2番目の敵が15ダメージを受けるべき
            Assert.AreEqual(85, enemies[1].CurrentHp, "2番目の敵が爆風の15ダメージを受けるべき");

            // 3番目の敵は無傷
            Assert.AreEqual(100, enemies[2].CurrentHp, "3番目の敵はダメージを受けないべき");
        }
        [TestCase(ChainEffectOrigin.HitPosition, 3)]
        [TestCase(ChainEffectOrigin.HitPosition, 5)]
        [TestCase(ChainEffectOrigin.PlayerPosition, 3)]
        [TestCase(ChainEffectOrigin.PlayerPosition, 5)]
        public void 派生貫通は指定された発動位置より前だけを攻撃して燃焼する(ChainEffectOrigin origin, int hitX)
        {
            var player = new PlayerCharacter(100, new Vector2Int(1, 0));
            var primary = new Enemy("hit", "検証", 100, ElementType.None, new Vector2Int(hitX, 0));
            var forward = new Enemy("forward", "検証", 100, ElementType.None, new Vector2Int(4, 0));
            var second = new Enemy("second", "検証", 100, ElementType.None, new Vector2Int(4, 0));
            var battle = new BattleSystem(player, new List<Enemy> { primary, forward, second }, new Deck());
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                battle.Field.ChangePanelType(new Vector2Int(x, 0), PanelType.Grass);
            var penetration = new CardModifier();
            penetration.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Penetration, Trigger = DerivativeTrigger.Immediate });
            var child = new Card("pierce", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.None, 10, "検証", DamageTargetType.Single, modifier: penetration);
            var database = new MockCardDatabase(); database.AddCard(child);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = child.Id, Trigger = DerivativeTrigger.OnHit, EffectOrigin = origin });
            // 配置順でSingleの対象を確定し、指定位置から実際の派生カードを発動する。
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", DamageTargetType.Single, modifier: modifier);
            var context = new BattleContext(battle, parent) { CardDatabase = database };
            new ModifiableCardEffect(DamageTargetType.Single).Execute(context);

            int startX = origin == ChainEffectOrigin.HitPosition ? hitX : 1;
            Assert.AreEqual(startX < 4 ? 80 : 100, forward.CurrentHp);
            Assert.AreEqual(startX < 4 ? 80 : 100, second.CurrentHp);
            Assert.AreEqual(origin == ChainEffectOrigin.HitPosition ? 99 : 79, primary.CurrentHp);
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                Assert.AreEqual(x > startX ? PanelType.Normal : PanelType.Grass,
                    battle.Field.GetPanelType(new Vector2Int(x, 0)), "位置=" + x);
            Assert.AreEqual(new Vector2Int(1, 0), player.GridPosition);
            Assert.IsNull(context.TemporaryPlayerPosition, "派生の発動位置は親へ漏らさない");
        }

    }
}
