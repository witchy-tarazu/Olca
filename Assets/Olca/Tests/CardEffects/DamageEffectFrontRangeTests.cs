using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;

namespace Olca.Tests.CardEffects
{
    public class DamageEffectFrontRangeTests
    {
        private static Card Card() => new Card("fixture-front", "テスト", 1, CardRarity.Normal,
            CardCategory.Attack, ElementType.None, CardSeries.Sword, 7, "");
        [TestCase(1, 1)] [TestCase(1, 2)] [TestCase(1, 3)]
        [TestCase(2, 1)] [TestCase(2, 2)] [TestCase(2, 3)]
        [TestCase(3, 1)] [TestCase(3, 2)] [TestCase(3, 3)]
        public void 前方範囲は最初の敵ではなく使用者からの距離で判定する(int range, int distance)
        {
            var card = Card(); var p = new PlayerCharacter(100, new Vector2Int(0, 0));
            var far = new Enemy("far", "テスト", 100, ElementType.None, new Vector2Int(5, 0));
            var near = new Enemy("near", "テスト", 100, ElementType.None, new Vector2Int(distance, 0));
            var b = new BattleSystem(p, new List<Enemy>{far, near}, new Deck(new List<Card>{card}), logger:new MockLogger());
            new DamageEffect(7, (DamageTargetType)((int)DamageTargetType.FrontTile + range - 1)).Execute(new BattleContext(b,card));
            Assert.AreEqual(100, far.CurrentHp);
            Assert.AreEqual(distance <= range ? 93 : 100, near.CurrentHp);
        }
        [TestCase(1)] [TestCase(-1)]
        public void 一時発動位置と向きを使い同位置と後方を除く(int forward)
        {
            var card = Card(); var p = new PlayerCharacter(100, new Vector2Int(0, 0));
            var front = new Enemy("front", "テスト", 100, ElementType.None, new Vector2Int(2 + forward, 0));
            var same = new Enemy("same", "テスト", 100, ElementType.None, new Vector2Int(2, 0));
            var back = new Enemy("back", "テスト", 100, ElementType.None, new Vector2Int(2 - forward, 0));
            var b = new BattleSystem(p, new List<Enemy>{back,same,front},new Deck(new List<Card>{card}),logger:new MockLogger());
            var context = new BattleContext(b,card,forwardX:forward){TemporaryPlayerPosition=new Vector2Int(2,0)};
            new DamageEffect(7,DamageTargetType.FrontTile).Execute(context);
            Assert.AreEqual(93,front.CurrentHp);Assert.AreEqual(100,same.CurrentHp);Assert.AreEqual(100,back.CurrentHp);
        }
        [TestCase(1)] [TestCase(-1)]
        public void 敵使用も使用者起点を保ち味方へ当てない(int forward)
        {
            var card = Card();var p = new PlayerCharacter(100,new Vector2Int(2 + forward,0));
            var user = new Enemy("user","テスト",100,ElementType.None,new Vector2Int(2,0));
            var ally = new Enemy("ally","テスト",100,ElementType.None,new Vector2Int(2 + forward,0));
            var b = new BattleSystem(p,new List<Enemy>{user,ally},new Deck(new List<Card>{card}),logger:new MockLogger());
            new DamageEffect(7,DamageTargetType.FrontTile).Execute(new BattleContext(b,card,user,forward));
            Assert.AreEqual(93,p.CurrentHp);Assert.AreEqual(100,user.CurrentHp);Assert.AreEqual(100,ally.CurrentHp);
        }
    }
}
