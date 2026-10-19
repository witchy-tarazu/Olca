using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using System.Linq;

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


        private BattleSystem CreateTransformationBattle()
        {
            var cards = new List<Card> {
                new Card("fixture-1", "検証1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証"),
                new Card("fixture-2", "検証2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証") };
            var battle = new BattleSystem(player, new List<Enemy> { new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1,0)) }, new Deck(cards), logger: logger);
            battle.Transformations.Configure(new[] {
                new TransformationDefenseDefinition("fixture-a", new ElementResistances(fire:50), ElementType.Water),
                new TransformationDefenseDefinition("fixture-b", new ElementResistances(fire:100), ElementType.Fire) });
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            return battle;
        }
        private void ReopenTransformationScreen(BattleSystem battle)
        {
            battle.CustomScreen.Close();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider());
        }
        [Test]
        public void 変身一覧は使用済みも残し呼出元から変更できない()
        {
            var battle = CreateTransformationBattle();
            var state = battle.Transformations;
            var choices = state.AvailableDefinitions;
            CollectionAssert.AreEqual(new[] { "fixture-a", "fixture-b" }, choices.Select(d => d.Id));
            Assert.Throws<System.NotSupportedException>(() =>
                ((IList<TransformationDefenseDefinition>)choices).Clear());
            state.TryPreview("fixture-a");
            battle.CustomScreen.Confirm();
            Assert.AreEqual(2, choices.Count);
            Assert.IsTrue(state.IsUsed(choices[0].Id));
            ReopenTransformationScreen(battle);
            Assert.IsFalse(state.TryPreview(choices[0].Id));
            Assert.IsTrue(state.TryPreview(choices[1].Id));
        }

        [Test]
        public void 重複ID定義の失敗で一覧を部分公開せず元リストの変更も反映しない()
        {
            var battle = new BattleSystem(player, new List<Enemy> {
                new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0)) }, new Deck(new List<Card>()));
            var state = battle.Transformations;
            var definition = new TransformationDefenseDefinition("fixture", ElementResistances.Neutral, ElementType.Fire);
            Assert.IsEmpty(state.AvailableDefinitions);
            Assert.Throws<System.ArgumentException>(() => state.Configure(new[] { definition, definition }));
            Assert.IsEmpty(state.AvailableDefinitions);
            var source = new List<TransformationDefenseDefinition> { definition };
            state.Configure(source);
            source.Clear();
            Assert.AreEqual(1, state.AvailableDefinitions.Count);
            Assert.AreSame(definition, state.AvailableDefinitions[0]);
        }

        [Test]
        public void 仮選択は未消費で確定時だけ消費し確定再試行で二重消費しない()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            Assert.IsTrue(state.TryPreview("fixture-a"));
            Assert.AreEqual(50,player.ActiveResistances.Fire);Assert.IsTrue(player.HasTransformationPreview);
            Assert.IsFalse(player.HasTransformationDefense);Assert.IsEmpty(state.UsedIds);
            battle.CustomScreen.Confirm();
            Assert.AreEqual("fixture-a",state.ActiveId);Assert.IsNull(state.PendingId);
            Assert.IsFalse(player.HasTransformationPreview);Assert.AreEqual(3,player.RemainingTransformationPhases);
            Assert.AreEqual(1,state.UsedIds.Count);
            battle.CustomScreen.CancelConfirmation();
            Assert.IsFalse(state.TryPreview("fixture-b"));Assert.IsFalse(state.TryCancelPending());
            battle.CustomScreen.Confirm();Assert.AreEqual(1,state.UsedIds.Count);Assert.AreEqual(3,player.RemainingTransformationPhases);
        }
        [Test]
        public void 仮選択取消は末尾カードを先に外し継続変身と開始時残回数へ戻す()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();ReopenTransformationScreen(battle);
            Assert.AreEqual(2,player.RemainingTransformationPhases);
            var first=battle.CustomScreen.CurrentHand[0];var last=battle.CustomScreen.CurrentHand[1];
            battle.CustomScreen.TrySelectCard(first);battle.CustomScreen.TrySelectCard(last);
            Assert.IsTrue(state.TryPreview("fixture-b"));Assert.IsEmpty(battle.CustomScreen.SelectedCards);
            battle.CustomScreen.TrySelectCard(first);battle.CustomScreen.TrySelectCard(last);
            Assert.IsTrue(state.TryCancelPending());Assert.AreSame(first,battle.CustomScreen.SelectedCards[0]);
            Assert.AreEqual("fixture-b",state.PendingId);Assert.AreEqual(100,player.ActiveResistances.Fire);
            Assert.IsTrue(state.TryCancelPending());Assert.IsEmpty(battle.CustomScreen.SelectedCards);
            Assert.AreEqual("fixture-b",state.PendingId);
            Assert.IsTrue(state.TryCancelPending());Assert.IsNull(state.PendingId);
            Assert.AreEqual("fixture-a",state.ActiveId);Assert.AreEqual(50,player.ActiveResistances.Fire);
            Assert.AreEqual(2,player.RemainingTransformationPhases);Assert.IsFalse(state.IsUsed("fixture-b"));
            Assert.IsFalse(state.TryCancelPending(), "継続中の変身は任意解除できない");
        }
        [Test]
        public void 別変身確定は両ID使用済みで強制解除後に前変身へ戻さない()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();ReopenTransformationScreen(battle);
            Assert.IsFalse(state.TryPreview("fixture-a"));Assert.IsTrue(state.TryPreview("fixture-b"));
            battle.CustomScreen.Confirm();Assert.AreEqual("fixture-b",state.ActiveId);
            Assert.AreEqual(3,player.RemainingTransformationPhases);Assert.AreEqual(2,state.UsedIds.Count);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            Assert.AreEqual(100,player.CurrentHp);Assert.IsNull(state.ActiveId);
            Assert.IsFalse(player.HasTransformationDefense);Assert.AreSame(player.Resistances,player.ActiveResistances);
            ReopenTransformationScreen(battle);Assert.IsFalse(state.TryPreview("fixture-a"));Assert.IsFalse(state.TryPreview("fixture-b"));
        }
        [Test]
        public void 期間切れも使用済みを維持し次戦は新しい使用集合になる()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();
            for(int n=0;n<3;n++)ReopenTransformationScreen(battle);
            Assert.IsNull(state.ActiveId);Assert.IsTrue(state.IsUsed("fixture-a"));Assert.IsFalse(state.TryPreview("fixture-a"));
            battle.Enemies[0].TakeDamage(100,ElementType.None);
            Assert.IsFalse(state.TryPreview("fixture-b"));
            var next=CreateTransformationBattle();Assert.IsEmpty(next.Transformations.UsedIds);
            Assert.IsTrue(next.Transformations.TryPreview("fixture-a"));
        }
        [Test]
        public void 未確定で画面を閉じても消費せず仮反映だけ解除する()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            Assert.IsFalse(state.TryPreview("undefined"));Assert.IsFalse(state.TryPreview(null));
            state.TryPreview("fixture-a");state.TryPreview("fixture-b");
            Assert.AreEqual("fixture-b",state.PendingId);Assert.IsEmpty(state.UsedIds);
            battle.CustomScreen.Cancel();Assert.IsNull(state.PendingId);Assert.IsFalse(player.HasTransformationPreview);
            Assert.IsEmpty(state.UsedIds);Assert.IsFalse(state.TryPreview("fixture-a"));
            battle.CustomScreen.Open(new Card[0],new MockRandomProvider());Assert.IsTrue(state.TryPreview("fixture-a"));
        }
        [Test]
        public void 空確定後も同じ画面の再試行では新しい変身を選べない()
        {
            var battle=CreateTransformationBattle();battle.CustomScreen.Confirm();battle.CustomScreen.CancelConfirmation();
            Assert.IsFalse(battle.Transformations.TryPreview("fixture-a"));
            battle.CustomScreen.Confirm();Assert.IsEmpty(battle.Transformations.UsedIds);
            ReopenTransformationScreen(battle);Assert.IsTrue(battle.Transformations.TryPreview("fixture-a"));
        }
        [Test]
        public void 不正定義と戦闘内の定義差替えを拒否する()
        {
            Assert.Throws<System.ArgumentException>(()=>new TransformationDefenseDefinition(" ",ElementResistances.Neutral,ElementType.Fire));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationDefenseDefinition("id",ElementResistances.Neutral,ElementType.None));
            var battle=CreateTransformationBattle();
            Assert.Throws<System.InvalidOperationException>(()=>battle.Transformations.Configure(new TransformationDefenseDefinition[0]));
        }

        [Test]
        public void 確定した画面を数えず次の三回目開始で耐性置換を解除する()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire: 50), ElementType.Water);
            int ended = 0; player.OnTransformationDefenseEnded += () => ended++;
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            battle.CustomScreen.Confirm();battle.CustomScreen.CancelConfirmation();battle.CustomScreen.Confirm();
            Assert.AreEqual(3, player.RemainingTransformationPhases, "重複Openや確定再試行で消費しない");
            for (int remaining = 2; remaining >= 0; remaining--)
            {
                battle.CustomScreen.Close();
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battle.OpenCustomScreen(new MockRandomProvider());
                Assert.AreEqual(remaining, player.RemainingTransformationPhases);
                Assert.AreEqual(remaining > 0, player.HasTransformationDefense);
                Assert.AreEqual(remaining == 0 ? 1 : 0, ended);
            }
            Assert.AreSame(player.Resistances, player.ActiveResistances);
        }

        [Test]
        public void 切替で三回へ戻り被弾解除で残回数も消える()
        {
            var battle = CreateBattle(new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0)));
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire: 50), ElementType.Water);
            battle.CustomScreen.Close();battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            Assert.AreEqual(2,player.RemainingTransformationPhases);
            player.BeginTransformationDefense(new ElementResistances(water: 50), ElementType.Fire);
            Assert.AreEqual(3,player.RemainingTransformationPhases);
            player.TakeDamage(1,attackElement:ElementType.Fire);
            Assert.AreEqual(0,player.RemainingTransformationPhases);
            battle.CustomScreen.Close();battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            Assert.AreEqual(0,player.RemainingTransformationPhases);
        }

        [Test]
        public void 戦闘終了で残回数を解除し次戦で古い購読を動かさない()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            player.BeginTransformationDefense(new ElementResistances(fire: 50),ElementType.Water);
            enemy.TakeDamage(100,ElementType.None);
            Assert.AreEqual(0,player.RemainingTransformationPhases);
            Assert.IsFalse(player.HasTransformationDefense);
            var next=CreateBattle(new Enemy("next","敵",100,ElementType.None,new Vector2Int(1,0)));
            next.CustomScreen.Open(new Card[0],new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire:50),ElementType.Water);
            battle.CustomScreen.Open(new Card[0],new MockRandomProvider());
            Assert.AreEqual(3,player.RemainingTransformationPhases);
            next.CustomScreen.Close();next.CustomScreen.Open(new Card[0],new MockRandomProvider());
            Assert.AreEqual(2,player.RemainingTransformationPhases);
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
