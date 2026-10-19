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
    /// バリア効果のテスト
    /// </summary>
    [TestFixture]
    public class BarrierEffectTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private Deck deck;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))
            };
            var cards = new List<Card>
            {
                new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [TestCase(-100, false, 30)][TestCase(50, false, 30)][TestCase(100, false, 30)]
        [TestCase(-100, true, 20)][TestCase(50, true, 20)][TestCase(100, true, 20)]
        public void バリアは本体耐性を無視して草地補正だけを計上する(int resistance, bool grass, int remaining)
        {
            player.SetElementResistances(new ElementResistances(fire: resistance));
            player.SetBarrier(40);
            if (grass) battle.Field.ChangePanelType(player.GridPosition, PanelType.Grass);
            Assert.IsFalse(player.TryTakeDamage(10, attackElement: ElementType.Fire, poiseDamage: 999));
            Assert.AreEqual(remaining, player.BarrierHp); Assert.AreEqual(100, player.CurrentHp);
            Assert.AreEqual(0, player.AccumulatedDamage); Assert.IsFalse(player.IsInvincible);
        }

        [TestCase(false)][TestCase(true)]
        public void バリア破壊ヒットの付随効果と無敵を抑止し次ヒットだけ受理する(bool logical)
        {
            player.SetBarrier(15); int effects = 0;
            var ai = new BasicAttackAction(10, 0, attackProperties: AttackProperty.Break, poiseDamage: 7,
                onHit: _ => effects++, hitInvincibilityMs: 100);
            var context = new AIContext(battle.Enemies[0], player, battle.Field, new MockRandomProvider(), new MockLogger());
            System.Action hits = () => {
                ai.Execute(context); Assert.AreEqual(5, player.BarrierHp);
                ai.Execute(context); Assert.AreEqual(0, player.BarrierHp);
                Assert.AreEqual(0, effects); Assert.AreEqual(0, player.AccumulatedDamage);
                Assert.IsFalse(player.IsInvincible);
                ai.Execute(context);
            };
            if (logical) battle.RunLogicalFrame(hits); else hits();
            Assert.AreEqual(90, player.CurrentHp); Assert.AreEqual(1, effects);
            Assert.AreEqual(7, player.AccumulatedDamage); Assert.IsTrue(player.IsInvincible);
        }

        [TestCase(false)][TestCase(true)]
        public void 毒と威力ゼロの専用効果はバリアを消費せず通過する(bool logical)
        {
            player.SetBarrier(15); int effects = 0;
            var ai = new BasicAttackAction(0, 0, poiseDamage: 0, onHit: _ => effects++);
            var context = new AIContext(battle.Enemies[0], player, battle.Field, new MockRandomProvider(), new MockLogger());
            System.Action apply = () => { ai.Execute(context); player.TakePoisonDamage(200); };
            if (logical) battle.RunLogicalFrame(apply); else apply();
            Assert.AreEqual(1, effects); Assert.AreEqual(1, player.CurrentHp);
            Assert.AreEqual(15, player.BarrierHp); Assert.AreEqual(0, player.AccumulatedDamage);
            Assert.IsFalse(player.IsInvincible);
        }

        [TestCase(false)][TestCase(true)]
        public void 毒は既存無敵を通過し同フレーム通常攻撃とは下限を分離する(bool withAttack)
        {
            player.SetHp(20); player.SetBarrier(1); player.SetInvincible(100);
            player.TakePoisonDamage(5);
            Assert.AreEqual(15, player.CurrentHp); Assert.AreEqual(1, player.BarrierHp);
            player.TickInvincible(100);
            battle.RunLogicalFrame(() => {
                player.TakePoisonDamage(100);
                player.TakeDamage(100); // バリア破壊の一発は通さない。
                if (withAttack) player.TakeDamage(1);
            });
            Assert.AreEqual(withAttack ? 0 : 1, player.CurrentHp);
            Assert.AreEqual(withAttack, battle.IsGameOver);
        }

        [TestCase(0, false)][TestCase(100, true)]
        public void バリアの耐久計算を変えても変身弱点の既存解除条件を維持する(int resistance, bool remains)
        {
            player.BeginTransformationDefense(new ElementResistances(fire: resistance), ElementType.Fire);
            player.SetBarrier(20);
            Assert.IsFalse(player.TryTakeDamage(10, attackElement: ElementType.Fire));
            Assert.AreEqual(10, player.BarrierHp); Assert.AreEqual(100, player.CurrentHp);
            Assert.AreEqual(remains, player.HasTransformationDefense);
        }

        [Test]
        public void 草地で最大整数ダメージでもバリア減算は溢れず全防御する()
        {
            battle.Field.ChangePanelType(player.GridPosition, PanelType.Grass); player.SetBarrier(int.MaxValue);
            Assert.IsFalse(player.TryTakeDamage(int.MaxValue, attackElement: ElementType.Fire));
            Assert.AreEqual(0, player.BarrierHp); Assert.AreEqual(100, player.CurrentHp);
        }

        [Test]
        public void BarrierEffectでバリアが付与される()
        {
            var effect = new BarrierEffect(30);
            var card = new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(30, player.BarrierHp);
            Assert.IsTrue(player.HasBarrier);
        }

        [Test]
        public void バリアがある場合ダメージを吸収する()
        {
            player.SetBarrier(30);
            int initialHp = player.CurrentHp;

            player.TakeDamage(20); // バリア30 > ダメージ20

            Assert.AreEqual(initialHp, player.CurrentHp, "HPは変化しないはず");
            Assert.AreEqual(10, player.BarrierHp, "バリアが20減るはず");
        }

        [Test]
        public void バリアを超えるダメージもその一発は全て防ぐ()
        {
            player.SetBarrier(20);
            int initialHp = player.CurrentHp;

            player.TakeDamage(30); // バリア20を超える30ダメージ

            Assert.AreEqual(0, player.BarrierHp, "バリアは0になるはず");
            Assert.AreEqual(initialHp, player.CurrentHp, "超過分をHPへ通さない");
        }

        [Test]
        public void バリア以下のダメージではHPが減らない()
        {
            player.SetBarrier(50);
            int initialHp = player.CurrentHp;

            player.TakeDamage(50);

            Assert.AreEqual(0, player.BarrierHp);
            Assert.AreEqual(initialHp, player.CurrentHp, "HPは変化しないはず");
        }

        [Test]
        public void バリアが割れた後の攻撃は通常通りHPに入る()
        {
            player.SetBarrier(10);

            player.TakeDamage(10); // バリアを割る
            int hpAfterBarrierBreak = player.CurrentHp;
            player.TakeDamage(15); // バリアなし

            Assert.AreEqual(hpAfterBarrierBreak - 15, player.CurrentHp, "バリアなしで15ダメージ入るはず");
        }

        [Test]
        public void CardEffectFactoryでbarrierカードはBarrierEffectが生成される()
        {
            var card = new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア");
            var effect = CardEffectFactory.CreateEffect(card);

            Assert.IsInstanceOf<BarrierEffect>(effect);
        }

        [Test]
        public void BarrierEffectのHPが0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new BarrierEffect(0));
        }
    }
}
