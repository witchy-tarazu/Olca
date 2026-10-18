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
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!pending.IsCompleted && DateTime.UtcNow < deadline)
            {
                battle.Tick(10);
                await Task.Delay(10);
            }
            if (!pending.IsCompleted) battle.CardUsage.InterruptCurrentCard();
            Assert.IsTrue(pending.IsCompleted, "実際の待機処理が5秒以内に完了すること");
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
            var nextCard = new Card("next_attack", "次の攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 5, "威力5");
            battle.CardUsage.EnqueueCards(new List<Card> { card, nextCard });
            int used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            using (var cts = new CancellationTokenSource())
            {
                var pending = battle.CardUsage.TryUseCurrentCardAsync(cts.Token).AsTask();
                Assert.IsFalse(pending.IsCompleted);
                Assert.IsFalse(player.IsConcentrating);
                if (afterHit)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (battle.Enemies[0].CurrentHp == 100 && DateTime.UtcNow < deadline)
                    { battle.Tick(10); await Task.Delay(10); }
                    Assert.AreEqual(70, battle.Enemies[0].CurrentHp, "攻撃後の待機へ進む");
                    Assert.IsFalse(pending.IsCompleted);
                }
                if (externalCancel) cts.Cancel();
                else player.TakeDamage(player.Poise); // 実際の強靭度到達イベントから中断する。

                // 全体仕様第5節: 内部のけぞりは命中前でも消費。外部キャンセルは従来どおり。
                bool consumed = afterHit || !externalCancel;
                Assert.AreEqual(consumed, await AwaitCardUse(pending));
                Assert.AreEqual(consumed ? 1 : 0, used);
                Assert.IsFalse(player.IsInTemporaryMove);
                Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
                Assert.AreEqual(afterHit ? 70 : 100, battle.Enemies[0].CurrentHp);
                Assert.AreEqual(20, card.GetFinalPower());
                Assert.AreEqual(10, card.Modifier.BonusDamage);
                Assert.IsFalse(player.IsConcentrating);
                Assert.AreEqual(consumed ? 1 : 2, battle.CardUsage.RemainingCount);
                Assert.AreSame(consumed ? nextCard : card, battle.CardUsage.CurrentCard,
                    "命中済みカードを再使用可能にしない");
                Assert.IsTrue(await AwaitCardUse(battle.CardUsage.TryUseCurrentCardAsync().AsTask()));
                Assert.AreEqual(afterHit ? 65 : consumed ? 95 : 80, battle.Enemies[0].CurrentHp);
                Assert.AreEqual(consumed ? 2 : 1, used);
                Assert.AreEqual(5, nextCard.GetFinalPower());
                if (consumed)
                {
                    Assert.IsTrue(battle.CardUsage.IsEmpty);
                    Assert.IsFalse(battle.CardUsage.TryUseCurrentCard());
                    Assert.AreEqual(afterHit ? 65 : 95, battle.Enemies[0].CurrentHp);
                }
            }
        }

        [Test]
        public async Task 実行中の重複使用とスキップは拒否し中断を繰り返しても一枚だけ消費する()
        {
            var card = CreateMovingAttack(1f, 0.01f);
            var next = new Card("next", "次", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 5, "次");
            battle.CardUsage.EnqueueCards(new List<Card> { card, next });
            int used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            Assert.IsFalse(battle.CardUsage.TryUseCurrentCard());
            Assert.IsFalse(await battle.CardUsage.TryUseCurrentCardAsync().AsTask());
            Assert.IsFalse(battle.CardUsage.SkipCurrentCard());
            player.TakeDamage(player.Poise);
            battle.CardUsage.InterruptCurrentCard();
            Assert.IsTrue(await AwaitCardUse(pending));
            battle.CardUsage.InterruptCurrentCard();
            Assert.AreEqual(1, used);
            Assert.AreSame(next, battle.CardUsage.CurrentCard);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
            Assert.AreEqual(2, used);
            Assert.AreEqual(95, battle.Enemies[0].CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 待機中のキュー破棄や置換は新しいカードを誤消費しない(bool replace)
        {
            var card = CreateMovingAttack(1f, 0.01f);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            int used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.CardUsage.Clear();
            if (replace) battle.CardUsage.EnqueueCards(new List<Card> { card });
            battle.CardUsage.InterruptCurrentCard();
            Assert.IsFalse(await AwaitCardUse(pending));
            Assert.AreEqual(0, used);
            Assert.AreEqual(replace ? 1 : 0, battle.CardUsage.RemainingCount);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
        }

        [Test]
        public async Task 実行中カードはゼロ選択の通常往復で再使用可能にならない()
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            var next = new Card("next", "次", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 5, "次");
            battle.CardUsage.EnqueueCards(new List<Card> { active, next });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            Assert.IsFalse(pending.IsCompleted);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            battle.CustomScreen.Confirm();
            battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            CollectionAssert.AreEqual(new[] { next }, battle.CardUsage.GetRemainingCards());
            Assert.AreEqual(80, battle.Enemies[0].CurrentHp);
        }

        [Test]
        public async Task カスタム画面中は実行中カードの時間と効果が停止する()
        {
            var active = CreateMovingAttack(0.05f, 0.05f);
            battle.CardUsage.EnqueueCards(new List<Card> { active });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            await Task.Delay(250);
            bool stopped = !pending.IsCompleted && battle.Enemies[0].CurrentHp == 100;
            battle.CustomScreen.Confirm();
            battle.CustomScreen.Close();
            await AwaitCardUse(pending);
            Assert.IsTrue(stopped, "custom表示中は残り時間・HPを変えない");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task 実行中の選び直しと中断でも待機分だけを保持する(bool selectNew, bool stagger)
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            var next = new Card("next", "次", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 5, "次");
            battle.CardUsage.EnqueueCards(new List<Card> { active, next });
            int used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            Assert.AreSame(active, battle.CardUsage.InFlightCard);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            CollectionAssert.AreEqual(new[] { next }, battle.CustomScreen.AvailableCards);
            var chosen = selectNew ? battle.CustomScreen.CurrentHand[0] : next;
            if (selectNew) Assert.IsTrue(battle.CustomScreen.TrySelectCard(chosen));
            Assert.IsFalse(battle.CustomScreen.TrySelectCard(active));
            battle.Tick(1000);
            await Task.Delay(50);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            if (stagger) player.TakeDamage(player.Poise);
            battle.CustomScreen.Confirm();
            battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.AreEqual(1, used);
            Assert.IsNull(battle.CardUsage.InFlightCard);
            CollectionAssert.AreEqual(new[] { chosen }, battle.CardUsage.GetRemainingCards());
            Assert.AreEqual(stagger ? 100 : 80, battle.Enemies[0].CurrentHp);
        }

        [Test]
        public async Task 前隙と後隙の残り時間はcustomの停止中に減らない()
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            battle.CardUsage.EnqueueCards(new List<Card> { active });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.Tick(40);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            battle.Tick(1000);
            await Task.Delay(50);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            battle.Tick(59); await Task.Delay(30);
            Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            battle.Tick(2); await Task.Delay(30);
            Assert.AreEqual(80, battle.Enemies[0].CurrentHp);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            battle.Tick(1000); await Task.Delay(50);
            Assert.IsFalse(pending.IsCompleted);
            Assert.IsTrue(player.IsInTemporaryMove);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.IsTrue(battle.CardUsage.IsEmpty);
            Assert.IsFalse(player.IsInTemporaryMove);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task custom中の戦闘終了は古い非同期処理を止め次戦へ漏らさない(bool victory)
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            battle.CardUsage.EnqueueCards(new List<Card> { active });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            if (victory) battle.Enemies[0].TakeDamage(1000, ElementType.None);
            else player.TakeDamage(1000);
            Assert.IsFalse(await AwaitCardUse(pending));
            var nextBattle = new BattleSystem(new PlayerCharacter(100), new List<Enemy>
                { new Enemy("new", "次戦", 100, ElementType.None, new Vector2Int(3, 0)) }, new Deck());
            nextBattle.CardUsage.EnqueueCards(new List<Card> { active });
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            battle.Tick(1000); await Task.Delay(50);
            Assert.AreEqual(0, used);
            Assert.IsTrue(battle.CardUsage.IsEmpty);
            Assert.IsNull(battle.CardUsage.InFlightCard);
            Assert.IsFalse(player.IsInTemporaryMove);
            Assert.AreEqual(victory ? 0 : 100, battle.Enemies[0].CurrentHp);
            CollectionAssert.AreEqual(new[] { active }, nextBattle.CardUsage.GetRemainingCards());
        }

        [Test]
        public async Task 実行中カードは三サービスの加工素材にならない()
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            var spare = CreateMovingAttack(0.1f, 0.1f);
            var factory = new MockCardFactory();
            battle = new BattleSystem(player, new List<Enemy> { battle.Enemies[0] },
                new Deck(new List<Card> { active, spare }), exceedSystem: new ExceedSystem(factory),
                tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier), fusionSystem: new FusionSystem(new MockFusionRecipeProvider(), factory));
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            battle.CustomScreen.TrySelectCard(active);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            Assert.IsFalse(battle.CustomScreen.ExecuteExceed(active, spare).IsSuccess);
            Assert.IsFalse(battle.CustomScreen.ExecuteTuning(active, spare).IsSuccess);
            Assert.IsFalse(battle.CustomScreen.ExecuteFusion(new[] { active, spare }).IsSuccess);
            CollectionAssert.AreEqual(new[] { spare }, battle.CustomScreen.CurrentHand);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.IsTrue(battle.CardUsage.IsEmpty);
        }

        [Test]
        public async Task 同じ参照の複数枚でも実行中の一枚だけを取り出す()
        {
            var card = CreateMovingAttack(0.05f, 0.05f);
            battle.CardUsage.EnqueueCards(new List<Card> { card, card });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            Assert.AreEqual(1, battle.CustomScreen.AvailableCards.Count);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.AreEqual(1, battle.CardUsage.RemainingCount);
            Assert.IsTrue(await AwaitCardUse(battle.CardUsage.TryUseCurrentCardAsync().AsTask()));
            Assert.IsTrue(battle.CardUsage.IsEmpty);
            Assert.AreEqual(60, battle.Enemies[0].CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task custom中の外部キャンセルは未消費カードを保持分へ戻す(bool selectNew)
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            battle.CardUsage.EnqueueCards(new List<Card> { active });
            using (var cts = new CancellationTokenSource())
            {
                var pending = battle.CardUsage.TryUseCurrentCardAsync(cts.Token).AsTask();
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battle.OpenCustomScreen(new MockRandomProvider(0));
                cts.Cancel();
                Assert.IsFalse(await AwaitCardUse(pending));
                Assert.IsNull(battle.CardUsage.InFlightCard);
                CollectionAssert.AreEqual(new[] { active }, battle.CustomScreen.AvailableCards);
                var chosen = selectNew ? battle.CustomScreen.CurrentHand[0] : active;
                if (selectNew) battle.CustomScreen.TrySelectCard(chosen);
                battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
                CollectionAssert.AreEqual(new[] { chosen }, battle.CardUsage.GetRemainingCards());
                Assert.AreEqual(100, battle.Enemies[0].CurrentHp);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 待機期限と同時にcustomを開いても継続処理は閉じるまで進まない(bool recovery)
        {
            var active = CreateMovingAttack(0.1f, 0.1f);
            battle.CardUsage.EnqueueCards(new List<Card> { active });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            if (recovery)
            {
                battle.Tick(101); await Task.Delay(30);
                Assert.AreEqual(80, battle.Enemies[0].CurrentHp);
            }
            battle.Tick(101);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            await Task.Delay(50);
            bool stopped = !pending.IsCompleted && player.IsInTemporaryMove;
            Assert.AreEqual(recovery ? 80 : 100, battle.Enemies[0].CurrentHp);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            Assert.IsTrue(await AwaitCardUse(pending));
            Assert.IsTrue(stopped);
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
