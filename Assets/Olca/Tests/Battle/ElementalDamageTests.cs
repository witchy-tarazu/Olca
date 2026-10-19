using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    /// <summary>
    /// 属性ダメージ計算の統合テスト
    /// </summary>
    [TestFixture]
    public class ElementalDamageTests
    {
        private PlayerCharacter player;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(0, 0));
            logger = new MockLogger();
        }

        private BattleSystem CreateBattle(Enemy enemy)
        {
            var enemies = new List<Enemy> { enemy };
            var cards = new List<Card>
            {
                new Card("atk", "攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 10, "攻撃"),
            };
            return new BattleSystem(player, enemies, new Deck(cards), logger: logger);
        }


        [TestCase(-50, 1073741823)]
        [TestCase(int.MinValue, 50)]
        public void 負耐性の大ダメージを回復して再被弾しても強靭度累計が符号反転しない(int resistance,int damage)
        {
            var target = new PlayerCharacter(int.MaxValue, poise: int.MaxValue);
            target.SetElementResistances(new ElementResistances(fire: resistance));
            int staggered = 0;
            target.OnStaggered += () => staggered++;
            target.TakeDamage(damage, attackElement: ElementType.Fire);
            Assert.AreEqual(0, staggered);
            int received = int.MaxValue - target.CurrentHp;
            Assert.Greater(received, int.MaxValue / 2);
            target.Heal(received);
            target.TakeDamage(damage, attackElement: ElementType.Fire);
            Assert.AreEqual(1, staggered);
            Assert.AreEqual(0, target.AccumulatedDamage);
            Assert.Greater(target.CurrentHp, 0);
        }

        [TestCase(0, 7)][TestCase(50, 3)][TestCase(-50, 10)][TestCase(-100, 14)][TestCase(100, 0)][TestCase(-200, 21)]
        public void 属性と独立した耐性で最後に切り捨てる(int resistance, int expected)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Fire, Vector2Int.Zero,
                resistances: new ElementResistances(water: resistance));
            enemy.TakeDamage(7, ElementType.Water);
            Assert.AreEqual(100 - expected, enemy.CurrentHp);
            player.SetElementResistances(new ElementResistances(water: resistance));
            player.TakeDamage(7, attackElement: ElementType.Water);
            Assert.AreEqual(100 - expected, player.CurrentHp);
        }
        [Test]
        public void 自身属性から弱点を推測せず上限超過だけ拒否する()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Fire, Vector2Int.Zero);
            enemy.TakeDamage(10, ElementType.Water);Assert.AreEqual(90, enemy.CurrentHp);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ElementResistances(fire:101));
            Assert.DoesNotThrow(() => new ElementResistances(fire:int.MinValue));
            Assert.AreEqual(int.MaxValue,new ElementResistances(fire:int.MinValue).CalculateDamage(int.MaxValue,ElementType.Fire,true));
        }
        [TestCase(50, 7)][TestCase(-50, 21)][TestCase(-100, 28)][TestCase(100, 0)]
        public void 草の火倍率と耐性を途中丸めせず乗算する(int resistance,int expected)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Water,new Vector2Int(1,0),resistances:new ElementResistances(fire:resistance));
            var battle=CreateBattle(enemy);battle.Field.ChangePanelType(enemy.GridPosition,PanelType.Grass);
            enemy.TakeDamage(7,ElementType.Fire);Assert.AreEqual(100-expected,enemy.CurrentHp);
            player.SetElementResistances(new ElementResistances(fire:resistance));battle.Field.ChangePanelType(player.GridPosition,PanelType.Grass);
            player.TakeDamage(7,attackElement:ElementType.Fire);Assert.AreEqual(100-expected,player.CurrentHp);
        }
        [TestCase(false, 50, 95)][TestCase(false, 100, 100)][TestCase(true, 50, 100)]
        public void 変身中の耐性で計算後に解除し無敵なら解除しない(bool invincible,int formResistance,int hp)
        {
            player.SetElementResistances(new ElementResistances(fire:-100));
            player.BeginTransformationDefense(new ElementResistances(fire:formResistance),ElementType.Fire);
            int endedHp=-1;player.OnTransformationDefenseEnded+=()=>endedHp=player.CurrentHp;
            if(invincible)player.SetInvincible(1000);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            Assert.AreEqual(hp,player.CurrentHp);Assert.AreEqual(invincible,player.HasTransformationDefense);
            Assert.AreEqual(invincible?-1:hp,endedHp);
            if(!invincible){player.TakeDamage(10,attackElement:ElementType.Fire);Assert.AreEqual(hp-20,player.CurrentHp);}
        }
        [Test]
        public void 解除用属性以外なら置換耐性を維持する()
        {
            player.BeginTransformationDefense(new ElementResistances(water:50),ElementType.Fire);
            player.TakeDamage(10,attackElement:ElementType.Water);
            Assert.AreEqual(95,player.CurrentHp);Assert.IsTrue(player.HasTransformationDefense);
        }

        // ==================== Enemy.TakeDamage の属性相性テスト ====================

        [Test]
        public void 水耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            enemy.TakeDamage(50, ElementType.Water);

            Assert.AreEqual(0, enemy.CurrentHp, "50×2倍=100ダメージでHPが0になるはず");
        }

        [Test]
        public void 火耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("wind_slime", "風スライム", 100, ElementType.Wind, new Vector2Int(1, 0), resistances: new ElementResistances(fire: -100));
            enemy.TakeDamage(50, ElementType.Fire);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 風耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("earth_slime", "土スライム", 100, ElementType.Earth, new Vector2Int(1, 0), resistances: new ElementResistances(wind: -100));
            enemy.TakeDamage(50, ElementType.Wind);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 土耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("water_slime", "水スライム", 100, ElementType.Water, new Vector2Int(1, 0));
            enemy = new Enemy("water", "水", 100, ElementType.Water, new Vector2Int(1, 0), resistances: new ElementResistances(earth: -100));
            enemy.TakeDamage(50, ElementType.Earth);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 逆相性は等倍ダメージになる()
        {
            var enemy = new Enemy("water_slime", "水スライム", 100, ElementType.Water, new Vector2Int(1, 0));
            enemy.TakeDamage(50, ElementType.Fire); // 水の弱点は土、火は無効

            Assert.AreEqual(50, enemy.CurrentHp, "逆相性は等倍（1.0倍）のはず");
        }

        [Test]
        public void 攻撃側がNoneのとき等倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            enemy.TakeDamage(30, ElementType.None);

            Assert.AreEqual(70, enemy.CurrentHp, "None属性は1.0倍のはず");
        }

        [Test]
        public void 防御側がNoneのとき等倍ダメージになる()
        {
            var enemy = new Enemy("none_slime", "スライム", 100, ElementType.None, new Vector2Int(1, 0));
            enemy.TakeDamage(30, ElementType.Water);

            Assert.AreEqual(70, enemy.CurrentHp, "相手がNoneなら1.0倍のはず");
        }

        [Test]
        public void OnDamagedイベントで2倍の倍率が通知される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            float receivedMultiplier = 0;
            enemy.OnDamaged += (damage, mult) => receivedMultiplier = mult;

            enemy.TakeDamage(10, ElementType.Water);

            Assert.AreEqual(2.0f, receivedMultiplier, 0.001f, "OnDamagedイベントで倍率2.0が通知されるはず");
        }

        // ==================== ModifiableCardEffect との統合テスト ====================

        [Test]
        public void 水属性カードで火属性の敵に2倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            var card = new Card(
                "water_cannon", "ウォーターキャノン", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.Water, CardSeries.Canon,
                30, "水属性攻撃", DamageTargetType.Single
            );
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(40, enemy.CurrentHp, "30×2倍=60ダメージでHP100→40になるはず");
        }

        [Test]
        public void ElementOverrideが設定されたカードで上書き属性で相性計算される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            // 無属性カードに「水属性上書き」のModifierを付与
            var modifier = new CardModifier { ElementOverride = ElementType.Water };
            var card = new Card(
                "elemental_cannon", "エレメンタルキャノン", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, // 元は無属性
                30, "属性上書き攻撃", DamageTargetType.Single,
                modifier: modifier
            );
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            // 水属性として処理 → 火属性の敵に2倍ダメージ
            Assert.AreEqual(40, enemy.CurrentHp, "ElementOverrideで水属性になり30×2=60ダメージのはず");
        }

        [Test]
        public void DamageEffectでElementOverrideが正しく適用される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            // 無属性カードに水属性オーバーライドを付与
            var modifier = new CardModifier { ElementOverride = ElementType.Water };
            var card = new Card(
                "elemental_atk", "属性攻撃", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon,
                20, "属性上書き", DamageTargetType.Single,
                modifier: modifier
            );

            var effect = new DamageEffect(20, DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            // GetFinalElement()でElementOverride(Water)が使われ、火属性の敵に2倍ダメージ
            Assert.AreEqual(60, enemy.CurrentHp, "DamageEffectでも20×2=40ダメージになるはず");
        }
    }
}
