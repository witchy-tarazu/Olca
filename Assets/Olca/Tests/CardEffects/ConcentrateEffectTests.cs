using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    /// <summary>
    /// 集中効果のテスト
    /// </summary>
    [TestFixture]
    public class ConcentrateEffectTests
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
                new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(3, 0))
            };
            var cards = new List<Card>
            {
                new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 10, "攻撃"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [Test]
        public void ConcentrateEffectでNextCardMultiplierが設定される()
        {
            var effect = new ConcentrateEffect(1.5f);
            var card = new Card("concentrate", "集中", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.Canon, 0, "集中");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(1.5f, player.NextCardMultiplier, 0.001f);
            Assert.IsTrue(player.IsConcentrating);
        }

        [Test]
        public void ConsumeConcentrateBuffで倍率が取得されリセットされる()
        {
            player.SetConcentrateBuff(2.0f);

            float val = player.ConsumeConcentrateBuff();

            Assert.AreEqual(2.0f, val, 0.001f);
            Assert.AreEqual(1.0f, player.NextCardMultiplier, 0.001f, "消費後は1.0にリセットされるはず");
            Assert.IsFalse(player.IsConcentrating);
        }

        [Test]
        public void 集中バフがない状態ではConsumeで1_0fが返る()
        {
            float val = player.ConsumeConcentrateBuff();

            Assert.AreEqual(1.0f, val, 0.001f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void カード使用で次の攻撃だけ強化し元カードを保持する(bool asyncUse)
        {
            var attack = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃", modifier: CardModifier.CreateAttackBonus(10));
            player.SetConcentrateBuff(1.5f);
            battle.CardUsage.EnqueueCards(new List<Card> { attack, attack });

            if (asyncUse) Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());
            else Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(70, battle.Enemies[0].CurrentHp, "調律込み20の1.5倍");
            Assert.IsFalse(player.IsConcentrating);
            Assert.AreEqual(20, attack.GetFinalPower(), "元カードの威力は不変");
            Assert.AreEqual(10, attack.Modifier.BonusDamage);
            battle.CardUsage.TryUseCurrentCard();
            Assert.AreEqual(50, battle.Enemies[0].CurrentHp, "2枚目は通常威力");
        }

        [TestCase(CardCategory.Defense)]
        [TestCase(CardCategory.Support)]
        [TestCase(CardCategory.Field)]
        public void 攻撃以外のカードでは集中を消費しない(CardCategory category)
        {
            player.SetConcentrateBuff(1.5f);
            var card = new Card("non_attack", "補助", 1, CardRarity.Normal, category,
                ElementType.None, CardSeries.None, 0, "補助");
            battle.CardUsage.EnqueueCards(new List<Card> { card });

            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(1.5f, player.NextCardMultiplier);
        }

        [Test]
        public void 集中カードから次の攻撃まで使用経路で適用される()
        {
            var concentrate = new Card("concentrate", "集中", 1, CardRarity.Normal, CardCategory.Support,
                ElementType.None, CardSeries.None, 50, "次の攻撃を1.5倍");
            var attack = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃");
            battle.CardUsage.EnqueueCards(new List<Card> { concentrate, attack });

            battle.CardUsage.TryUseCurrentCard();
            Assert.IsTrue(player.IsConcentrating);
            battle.CardUsage.TryUseCurrentCard();

            Assert.AreEqual(85, battle.Enemies[0].CurrentHp);
            Assert.IsFalse(player.IsConcentrating);
        }

        [Test]
        public void スキップと空キューでは集中を消費しない()
        {
            player.SetConcentrateBuff(1.5f);
            var card = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃");
            battle.CardUsage.EnqueueCards(new List<Card> { card });

            Assert.IsTrue(battle.CardUsage.SkipCurrentCard());
            Assert.IsFalse(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(1.5f, player.NextCardMultiplier);
        }

        private Card CreateMovingAttack(float moveTime, float attackTime)
        {
            return new Card("moving_attack", "一時移動攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "一時移動攻撃",
                modifier: CardModifier.CreateAttackBonus(10), moveDirection: "Right", moveDistance: 1,
                moveMotionTime: moveTime, attackMotionTime: attackTime,
                penetrationMode: MovePenetrationMode.Invincible);
        }

        private async Task<bool> AwaitCardUse(Task<bool> pending)
        {
            var completed = await Task.WhenAny(pending, Task.Delay(5000));
            if (completed != pending) battle.CardUsage.InterruptCurrentCard();
            Assert.AreSame(pending, completed, "実際の待機処理が5秒以内に完了すること");
            return await pending;
        }

        [Test]
        public async Task 集中した一時移動攻撃は実待機後に一度だけ強化される()
        {
            var card = CreateMovingAttack(0.05f, 0.05f);
            player.SetConcentrateBuff(1.5f);
            battle.CardUsage.EnqueueCards(new List<Card> { card, card });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();

            Assert.IsFalse(pending.IsCompleted, "移動モーションを実際に待機する");
            Assert.IsTrue(player.IsInTemporaryMove);
            Assert.IsFalse(player.IsConcentrating, "使用開始時点で消費");
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.AreEqual(70, battle.Enemies[0].CurrentHp);
            Assert.IsFalse(player.IsInTemporaryMove);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            Assert.AreEqual(20, card.GetFinalPower());

            Assert.IsTrue(await AwaitCardUse(battle.CardUsage.TryUseCurrentCardAsync().AsTask()));
            Assert.AreEqual(50, battle.Enemies[0].CurrentHp, "次の使用へ集中が漏れない");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task 集中攻撃の実待機中断で元カードと次使用へ強化が漏れない(bool externalCancel, bool afterHit)
        {
            var card = CreateMovingAttack(afterHit ? 0.01f : 0.05f, afterHit ? 1f : 0.05f);
            player.SetConcentrateBuff(1.5f);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            using (var cts = new CancellationTokenSource())
            {
                var pending = battle.CardUsage.TryUseCurrentCardAsync(cts.Token).AsTask();
                Assert.IsFalse(pending.IsCompleted);
                Assert.IsFalse(player.IsConcentrating);
                if (afterHit)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (battle.Enemies[0].CurrentHp == 100 && DateTime.UtcNow < deadline)
                        await Task.Delay(10);
                    Assert.AreEqual(70, battle.Enemies[0].CurrentHp, "攻撃後の待機へ進む");
                    Assert.IsFalse(pending.IsCompleted);
                }
                if (externalCancel) cts.Cancel();
                else battle.CardUsage.InterruptCurrentCard();

                Assert.IsFalse(await AwaitCardUse(pending), "キャンセルは使用システムへ伝わる");
                Assert.IsFalse(player.IsInTemporaryMove);
                Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
                Assert.AreEqual(afterHit ? 70 : 100, battle.Enemies[0].CurrentHp);
                Assert.AreEqual(20, card.GetFinalPower());
                Assert.AreEqual(10, card.Modifier.BonusDamage);
                Assert.IsFalse(player.IsConcentrating);
                Assert.AreEqual(1, battle.CardUsage.RemainingCount);
                Assert.IsTrue(await AwaitCardUse(battle.CardUsage.TryUseCurrentCardAsync().AsTask()));
                Assert.AreEqual(afterHit ? 50 : 80, battle.Enemies[0].CurrentHp);
            }
        }

        [Test]
        public void 集中した遅延着弾は使用時コピーを保持し新しい集中を消費しない()
        {
            var modifier = CardModifier.CreateAttackBonus(10);
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1f, TargetOffset = new Vector2Int(1, 0),
                Trigger = DerivativeTrigger.Immediate
            });
            var card = new Card("projectile", "投擲", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "遅延着弾", modifier: modifier);
            player.SetConcentrateBuff(1.5f);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            battle.CardUsage.TryUseCurrentCard();
            Assert.IsFalse(player.IsConcentrating);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            player.SetConcentrateBuff(2f);

            battle.DelayedEffects.Update(999);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            battle.DelayedEffects.Update(1);

            Assert.AreEqual(70, battle.Enemies[0].CurrentHp, "使用時の30ダメージを保持");
            Assert.AreEqual(2f, player.NextCardMultiplier, "新しい集中は着弾で消費しない");
            Assert.AreEqual(20, card.GetFinalPower());
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
        }

        [Test]
        public void ConcentrateEffectの倍率が1_0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(1.0f));
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(0.5f));
        }
    }
}
