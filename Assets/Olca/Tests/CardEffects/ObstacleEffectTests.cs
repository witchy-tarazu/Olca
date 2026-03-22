using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    /// <summary>
    /// 障害物設置効果のテスト
    /// </summary>
    [TestFixture]
    public class ObstacleEffectTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private BattleField field;
        private Deck deck;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(4, 0))
            };
            var cards = new List<Card>
            {
                new Card("rock_cube", "ロックキューブ", 1, CardRarity.Normal, CardCategory.Field, ElementType.None, CardSeries.Canon, 0, "障害物設置"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
            field = battle.Field;
        }

        [Test]
        public void ObstacleEffectで指定位置に障害物が設置される()
        {
            // プレイヤー(X=2)の前方1マス(X=3)に設置
            var effect = new ObstacleEffect(new Vector2Int(1, 0), 50);
            var card = new Card("rock_cube", "ロックキューブ", 1, CardRarity.Normal, CardCategory.Field, ElementType.None, CardSeries.Canon, 0, "障害物");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            var obstacle = field.GetObstacle(new Vector2Int(3, 0));
            Assert.IsNotNull(obstacle, "障害物が設置されているはず");
            Assert.AreEqual(50, obstacle.Hp);
        }

        [Test]
        public void フィールド範囲外への設置は無視される()
        {
            // X=2から右に100マス（範囲外）
            var effect = new ObstacleEffect(new Vector2Int(100, 0), 50);
            var card = new Card("rock_cube", "ロックキューブ", 1, CardRarity.Normal, CardCategory.Field, ElementType.None, CardSeries.Canon, 0, "障害物");
            var context = new BattleContext(battle, card);

            Assert.DoesNotThrow(() => effect.Execute(context), "範囲外でも例外が出ないはず");
        }

        [Test]
        public void 既存障害物がある位置には設置できない()
        {
            var pos = new Vector2Int(3, 0);
            field.PlaceObstacle(pos, new FieldObstacle("rock", 50));

            var effect = new ObstacleEffect(new Vector2Int(1, 0), 30);
            var card = new Card("rock_cube", "ロックキューブ", 1, CardRarity.Normal, CardCategory.Field, ElementType.None, CardSeries.Canon, 0, "障害物");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            // 元の障害物(50HP)が残っているはず
            Assert.AreEqual(50, field.GetObstacle(pos).Hp, "元の障害物が上書きされないはず");
        }

        [Test]
        public void 障害物があるマスは移動不可になる()
        {
            var pos = new Vector2Int(3, 0);
            field.PlaceObstacle(pos, new FieldObstacle("rock", 50));

            Assert.IsFalse(field.CanMoveTo(pos), "障害物のあるマスは移動不可のはず");
        }

        [Test]
        public void FieldObstacleのHP0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new FieldObstacle("rock", 0));
        }

        [Test]
        public void FieldObstacleはTakeDamageでHPが減る()
        {
            var obstacle = new FieldObstacle("rock", 50);

            obstacle.TakeDamage(20);

            Assert.AreEqual(30, obstacle.Hp);
        }

        [Test]
        public void FieldObstacleはHP0になると破壊済みになる()
        {
            var obstacle = new FieldObstacle("rock", 20);

            obstacle.TakeDamage(20);

            Assert.IsTrue(obstacle.IsDestroyed);
        }
    }
}
