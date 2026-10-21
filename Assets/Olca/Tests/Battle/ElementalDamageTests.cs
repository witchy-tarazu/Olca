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


        private BattleSystem CreateTransformationBattle(int secondFireResistance = 100)
        {
            var cards = new List<Card> {
                new Card("fixture-1", "検証1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証"),
                new Card("fixture-2", "検証2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証") };
            var battle = new BattleSystem(player, new List<Enemy> { new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1,0)) }, new Deck(cards), logger: logger);
            battle.Transformations.Configure(new[] {
                new TransformationDefenseDefinition("fixture-a", new ElementResistances(fire:50), ElementType.Water),
                new TransformationDefenseDefinition("fixture-b", new ElementResistances(fire:secondFireResistance), ElementType.Fire) });
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            return battle;
        }
        [Test]
        public void オーラとの未規定競合で変身仮選択や使用済み状態を変更しない()
        {
            var battle = CreateTransformationBattle(); player.SetAura(10);
            Assert.IsFalse(battle.Transformations.TryPreview("fixture-a"));
            Assert.IsFalse(player.HasTransformationPreview); Assert.AreEqual(10, player.AuraThreshold);
            player.SetAura(0); Assert.IsTrue(battle.Transformations.TryPreview("fixture-a"));
            Assert.Throws<System.InvalidOperationException>(() => player.SetAura(10));
            Assert.IsTrue(player.HasTransformationPreview); Assert.IsFalse(player.HasAura);
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
            var battle=CreateTransformationBattle(50);var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();ReopenTransformationScreen(battle);
            Assert.IsFalse(state.TryPreview("fixture-a"));Assert.IsTrue(state.TryPreview("fixture-b"));
            battle.CustomScreen.Confirm();Assert.AreEqual("fixture-b",state.ActiveId);
            Assert.AreEqual(3,player.RemainingTransformationPhases);Assert.AreEqual(2,state.UsedIds.Count);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            Assert.AreEqual(95,player.CurrentHp);Assert.IsNull(state.ActiveId);
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
        [TestCase(int.MinValue, 600000000)]
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

        [TestCase(0, 7)][TestCase(50, 3)][TestCase(-50, 10)][TestCase(-100, 14)][TestCase(100, 0)][TestCase(-200, 14)]
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
        public void 自身属性から弱点を推測せず戦闘値だけ上下限で制限する()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Fire, Vector2Int.Zero);
            enemy.TakeDamage(10, ElementType.Water);Assert.AreEqual(90, enemy.CurrentHp);
            Assert.AreEqual(100, new ElementResistances(fire:101).Get(ElementType.Fire));
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
        [TestCase(false, 50, 95)][TestCase(false, 99, 99)][TestCase(false, 100, 100)][TestCase(false, 150, 100)][TestCase(true, 50, 100)]
        public void 変身中の耐性で計算後に解除し無敵や耐性完全無効なら解除しない(bool invincible,int formResistance,int hp)
        {
            player.SetElementResistances(new ElementResistances(fire:-100));
            player.BeginTransformationDefense(new ElementResistances(fire:formResistance),ElementType.Fire);
            int endedHp=-1;player.OnTransformationDefenseEnded+=()=>endedHp=player.CurrentHp;
            if(invincible)player.SetInvincible(1000);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            bool preserved = invincible || formResistance >= 100;
            Assert.AreEqual(hp,player.CurrentHp);Assert.AreEqual(preserved,player.HasTransformationDefense);
            Assert.AreEqual(preserved?-1:hp,endedHp);
            if(!preserved){player.TakeDamage(10,attackElement:ElementType.Fire);Assert.AreEqual(hp-20,player.CurrentHp);}
            else { player.TakeDamage(10,attackElement:ElementType.Fire); Assert.AreEqual(hp,player.CurrentHp); Assert.AreEqual(3,player.RemainingTransformationPhases); }
        }
        [Test]
        public void 解除用属性以外なら置換耐性を維持する()
        {
            player.BeginTransformationDefense(new ElementResistances(water:50),ElementType.Fire);
            player.TakeDamage(10,attackElement:ElementType.Water);
            Assert.AreEqual(95,player.CurrentHp);Assert.IsTrue(player.HasTransformationDefense);
        }

        [TestCase(AttackProperty.Sword, AttackProperty.Sword | AttackProperty.Break, true)]
        [TestCase(AttackProperty.Break, AttackProperty.Sword | AttackProperty.Break, true)]
        [TestCase(AttackProperty.Sword, AttackProperty.Break, false)]
        [TestCase(AttackProperty.Break, AttackProperty.None, false)]
        public void 単一の性質弱点は複合攻撃と一致し属性倍率を追加しない(AttackProperty weakness, AttackProperty attack, bool matches)
        {
            player.BeginTransformationDefense(new ElementResistances(fire: 50), new TransformationWeakness(weakness));
            int ended = 0; player.OnTransformationDefenseEnded += () => ended++;
            player.TakeDamage(10, attackElement: ElementType.Fire, attackProperties: attack);
            Assert.AreEqual(95, player.CurrentHp);
            Assert.AreEqual(5, player.AccumulatedDamage);
            Assert.AreEqual(!matches, player.HasTransformationDefense);
            Assert.AreEqual(matches ? 1 : 0, ended);
        }

        [TestCase(AttackProperty.Sword)] [TestCase(AttackProperty.Break)]
        public void 性質弱点のダメージと強靭度を変身中耐性で処理してから解除する(AttackProperty weakness)
        {
            var target = new PlayerCharacter(100, poise: 4);
            target.SetElementResistances(new ElementResistances(fire: -100));
            target.BeginTransformationDefense(new ElementResistances(fire: 50), new TransformationWeakness(weakness));
            var order = new List<string>();
            target.OnHpChanged += (hp, max) => order.Add("hp:" + hp + ":" + target.HasTransformationDefense);
            target.OnStaggered += () => order.Add("stagger:" + target.HasTransformationDefense);
            target.OnTransformationDefenseEnded += () => order.Add("end:" + target.CurrentHp);
            target.TakeDamage(10, attackElement: ElementType.Fire, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            CollectionAssert.AreEqual(new[] { "hp:95:True", "stagger:True", "end:95" }, order);
            Assert.AreEqual(0, target.AccumulatedDamage);
            Assert.AreEqual(-100, target.ActiveResistances.Fire);
        }

        [Test]
        public void 性質一致だけでは別の属性弱点を解除せず無属性の性質弱点は解除する()
        {
            player.BeginTransformationDefense(ElementResistances.Neutral, ElementType.Water);
            player.TakeDamage(1, attackElement: ElementType.Fire, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            Assert.IsTrue(player.HasTransformationDefense);
            player.BeginTransformationDefense(ElementResistances.Neutral, new TransformationWeakness(AttackProperty.Sword));
            player.TakeDamage(1, attackProperties: AttackProperty.Sword);
            Assert.IsFalse(player.HasTransformationDefense); Assert.AreEqual(98, player.CurrentHp);
        }

        [Test]
        public void ブレイク性質もバリアのHP吸収と強靭度保護を迂回しない()
        {
            player.SetBarrier(20);
            player.TakeDamage(10, attackProperties: AttackProperty.Break);
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(10, player.BarrierHp);
            Assert.AreEqual(0, player.AccumulatedDamage);
            player.TakeDamage(15, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(0, player.BarrierHp);
            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [Test]
        public void 不正な複数弱点と未知性質を副作用前に拒否する()
        {
            foreach (var invalid in new[] { AttackProperty.None, AttackProperty.Sword | AttackProperty.Break, (AttackProperty)4 })
                Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness(invalid));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness(ElementType.None));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness((ElementType)99));
            int changed = 0; player.OnHpChanged += (hp, max) => changed++;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => player.TakeDamage(10, attackProperties: (AttackProperty)8));
            Assert.AreEqual(0, changed); Assert.AreEqual(100, player.CurrentHp);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BasicAttackAction(10, 100, attackProperties: (AttackProperty)8));
            var enemy = new Enemy("e", "検証", 100, ElementType.None, Vector2Int.Zero);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => enemy.TakeDamage(10, ElementType.None, (AttackProperty)8));
            Assert.AreEqual(100, enemy.CurrentHp);
        }

        [Test]
        public void 一時移動攻撃も最終属性と複合性質を使用して完全無効を維持する()
        {
            var enemy = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                resistances: new ElementResistances(water: 100));
            var battle = CreateBattle(enemy);
            var card = new Card("move", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 10, "検証", modifier: new CardModifier { ElementOverride = ElementType.Water },
                properties: AttackProperty.Sword | AttackProperty.Break);
            int hits = 0; enemy.OnDamaged += (damage, multiplier) => hits++;
            var effect = new TemporaryMoveAttackEffect(new Vector2Int(1, 0), 1, 0, 0, MovePenetrationMode.Invincible);
            effect.ExecuteAsync(new BattleContext(battle, card), System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(100, enemy.CurrentHp); Assert.AreEqual(0, hits);
            Assert.AreEqual(Vector2Int.Zero, player.GridPosition); Assert.IsFalse(player.IsInTemporaryMove);
        }

        [TestCase(false, false, 0)] [TestCase(false, false, 1)]
        [TestCase(false, true, 0)] [TestCase(false, true, 999)]
        [TestCase(true, false, 0)] [TestCase(true, false, 1)]
        [TestCase(true, true, 0)] [TestCase(true, true, 999)]
        public void 風性質は威力と耐久値によらず終端で防御を剥がし同フレームを防ぐ(bool enemySide, bool aura, int power)
        {
            var enemy = new Enemy("e", "敵", 2000, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            if (enemySide) { if (aura) enemy.SetAura(10000); else enemy.SetBarrier(10000); }
            else { if (aura) player.SetAura(10000); else player.SetBarrier(10000); }
            void Hit(int amount, AttackProperty property) {
                if (enemySide) enemy.TakeDamage(amount, ElementType.Fire, property, amount > 0 ? 99 : 0);
                else player.TakeDamage(amount, attackElement: ElementType.Fire, attackProperties: property, poiseDamage: amount > 0 ? 99 : 0);
            }
            battle.RunLogicalFrame(() => {
                Hit(power, AttackProperty.Wind); Hit(1, AttackProperty.None);
                Assert.IsTrue(enemySide ? enemy.HasBarrier || enemy.HasAura : player.HasBarrier || player.HasAura);
            });
            Assert.AreEqual(enemySide ? 2000 : 100, enemySide ? enemy.CurrentHp : player.CurrentHp);
            Assert.IsFalse(enemySide ? enemy.HasBarrier || enemy.HasAura : player.HasBarrier || player.HasAura);
            Assert.AreEqual(0, enemySide ? enemy.AccumulatedPoiseDamage : player.AccumulatedDamage);
            battle.RunLogicalFrame(() => Hit(1, AttackProperty.None));
            Assert.AreEqual(enemySide ? 1999 : 99, enemySide ? enemy.CurrentHp : player.CurrentHp);
        }

        [TestCase(false)] [TestCase(true)]
        public void 風属性だけでは剥がさずガードを通過した風性質だけが剥がす(bool aura)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            if (aura) enemy.SetAura(10000); else enemy.SetBarrier(10000);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.Wind));
            Assert.IsTrue(enemy.HasAura || enemy.HasBarrier);
            enemy.Guard.Set(true, false);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind));
            Assert.IsTrue(enemy.HasAura || enemy.HasBarrier);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind | AttackProperty.Break));
            Assert.IsFalse(enemy.HasAura || enemy.HasBarrier); Assert.AreEqual(100, enemy.CurrentHp);
        }

        [TestCase(false)] [TestCase(true)]
        public void 敵の通常攻撃経路でも風性質がプレイヤー防御を剥がし付随効果と被弾無敵を発生させない(bool aura)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy); int effects = 0;
            if (aura) player.SetAura(10000); else player.SetBarrier(10000);
            var attack = new BasicAttackAction(1, 100, attackProperties: AttackProperty.Wind,
                poiseDamage: 30, onHit: p => effects++, hitInvincibilityMs: 600);
            var context = new AIContext(enemy, player, battle.Field, new MockRandomProvider(), logger);
            battle.RunLogicalFrame(() => attack.Execute(context));
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(0, effects);
            Assert.IsFalse(player.HasBarrier || player.HasAura); Assert.IsFalse(player.IsInvincible);
            battle.RunLogicalFrame(() => attack.Execute(context));
            Assert.AreEqual(99, player.CurrentHp); Assert.AreEqual(1, effects); Assert.IsTrue(player.IsInvincible);
        }

        [Test]
        public void 無敵中の風性質は防御を剥がさない()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy); enemy.SetBarrier(1000); player.SetAura(1000);
            enemy.SetInvincible(600); player.SetInvincible(600);
            battle.RunLogicalFrame(() => {
                enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind);
                player.TakeDamage(1, attackProperties: AttackProperty.Wind);
            });
            Assert.AreEqual(1000, enemy.BarrierHp); Assert.AreEqual(1000, player.AuraThreshold);
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
