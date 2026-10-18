using NUnit.Framework;
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
    [TestFixture]
    public class StartupRecoveryTests
    {
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;
        private BattleSystem battle;

        [SetUp]
        public void Setup()
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
        public void 前隙がある場合_即座にダメージは発生しない()
        {
            // Arrange: 前隙0.5秒のカード
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                DamageTargetType.Single, startupTime: 0.5f, recoveryTime: 0.3f);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, sword);

            // Act: カード使用
            effect.Execute(context);

            // Assert: 前隙中はダメージなし
            Assert.AreEqual(100, enemies[0].CurrentHp, "前隙中はダメージを受けないべき");
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "前隙効果が1つ登録されるべき");
        }

        [Test]
        public void 前隙経過後にダメージが発生する()
        {
            // Arrange: 前隙0.3秒のカード
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                DamageTargetType.Single, startupTime: 0.3f, recoveryTime: 0.2f);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, sword);

            // Act: カード使用
            effect.Execute(context);

            // Assert: 前隙中はダメージなし
            Assert.AreEqual(100, enemies[0].CurrentHp, "前隙中はダメージを受けないべき");

            // Act: 300ms経過（前隙終了）
            battle.DelayedEffects.Update(deltaTimeMs: 300);

            // Assert: 前隙経過後にダメージ発生
            Assert.AreEqual(70, enemies[0].CurrentHp, "前隙経過後に30ダメージを受けるべき");
        }

        [Test]
        public void 前隙と後隙が両方登録される()
        {
            // Arrange: 前隙0.3秒、後隙0.5秒のカード
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                DamageTargetType.Single, startupTime: 0.3f, recoveryTime: 0.5f);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, sword);

            // Act: カード使用
            effect.Execute(context);

            // Assert: 前隙効果が登録される
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "前隙効果が1つ登録されるべき");

            // Act: 300ms経過（前隙終了、ダメージ発生、後隙登録）
            battle.DelayedEffects.Update(deltaTimeMs: 300);

            // Assert: ダメージ発生 + 後隙効果が登録される
            Assert.AreEqual(70, enemies[0].CurrentHp, "前隙経過後に30ダメージを受けるべき");
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "後隙効果が1つ登録されるべき");

            // Act: さらに500ms経過（後隙終了）
            battle.DelayedEffects.Update(deltaTimeMs: 500);

            // Assert: すべての遅延効果が完了
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount, "すべての遅延効果が完了しているべき");
        }

        [Test]
        public void 暗転系カードは即座に発動する()
        {
            // Arrange: 暗転系カード（IsInstantEffect=true）
            var instantCard = new Card("dark", "暗転", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 50, "即座に発動",
                DamageTargetType.Single, startupTime: 0.5f, recoveryTime: 0.5f, isInstantEffect: true);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, instantCard);

            // Act: カード使用
            effect.Execute(context);

            // Assert: 前隙・後隙を無視して即座にダメージ発生
            Assert.AreEqual(50, enemies[0].CurrentHp, "暗転系カードは即座にダメージを与えるべき");
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount, "遅延効果は登録されないべき");
        }

        [Test]
        public void 前隙がゼロの場合は即座に発動するが後隙は登録される()
        {
            // Arrange: 前隙0秒、後隙0.5秒のカード
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                DamageTargetType.Single, startupTime: 0f, recoveryTime: 0.5f);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, sword);

            // Act: カード使用
            effect.Execute(context);

            // Assert: 即座にダメージ発生
            Assert.AreEqual(70, enemies[0].CurrentHp, "前隙がゼロなので即座にダメージを受けるべき");
            // 後隙効果が登録される
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "後隙効果が1つ登録されるべき");

            // Act: 500ms経過（後隙終了）
            battle.DelayedEffects.Update(deltaTimeMs: 500);

            // Assert: 後隙完了
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount, "すべての遅延効果が完了しているべき");
        }

        [Test]
        public void 前隙と投擲遅延が独立して動作する()
        {
            // Arrange: 前隙0.2秒 + 投擲遅延1.0秒のボムカード
            var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 20, "爆風",
                DamageTargetType.FrontTile);

            var cardDatabase = new MockCardDatabase();
            cardDatabase.AddCard(explosionCard);

            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.DelayedProjectile,
                DelayTime = 1.0f,
                TargetOffset = new Vector2Int(1, 0), // 前方1マス = 位置(3,0)
                CancelOnHole = true,
                ChainCardId = "explosion",
                Trigger = DerivativeTrigger.Immediate
            });

            var bomb = new Card("bomb", "ボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
                DamageTargetType.Single, modifier: modifier, startupTime: 0.2f, recoveryTime: 0.3f);

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, bomb);
            context.CardDatabase = cardDatabase;

            // Act: カード使用
            effect.Execute(context);

            // Assert: 前隙効果が登録される（投擲登録はまだ）
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "前隙効果が1つ登録されるべき");
            Assert.AreEqual(100, enemies[0].CurrentHp, "前隙中はダメージなし");

            // Act: 200ms経過（前隙終了 → 投擲登録 + 後隙登録）
            battle.DelayedEffects.Update(deltaTimeMs: 200);

            // Assert: 投擲遅延効果 + 後隙効果 = 2つ
            Assert.AreEqual(2, battle.DelayedEffects.ActiveEffectCount, "投擲遅延 + 後隙 = 2つの効果が登録されるべき");
            Assert.AreEqual(100, enemies[0].CurrentHp, "投擲着弾前はダメージなし");

            // Act: 300ms経過（後隙終了、投擲は残り700ms）
            battle.DelayedEffects.Update(deltaTimeMs: 300);

            // Assert: 投擲遅延効果のみ残る
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount, "投擲遅延効果のみ残っているべき");
            Assert.AreEqual(100, enemies[0].CurrentHp, "まだ投擲着弾前");

            // Act: さらに700ms経過（合計1200ms、投擲着弾）
            battle.DelayedEffects.Update(deltaTimeMs: 700);

            // Assert: 投擲着弾して爆風ダメージ発生
            Assert.AreEqual(80, enemies[1].CurrentHp, "敵2が爆風で20ダメージを受けるべき");
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount, "すべての遅延効果が完了");
        }

        private Card TimedSword(CardModifier modifier = null, bool instant = false)
        {
            foreach (var enemy in enemies) enemy.ApplyStun(60000);
            return new Card("timed_sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 30, "時間付き標準効果",
                DamageTargetType.Single, modifier: modifier, startupTime: 0.1f,
                recoveryTime: 0.1f, isInstantEffect: instant);
        }

        private async Task Advance(int milliseconds)
        {
            battle.Tick(milliseconds);
            await Task.Delay(50);
        }

        [Test]
        public async Task 標準効果は前後隙中の再入を拒否し完了時に一度だけ消費する()
        {
            var sword = TimedSword();
            var next = sword.Clone();
            battle.CardUsage.EnqueueCards(new List<Card> { sword, next });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            Assert.AreSame(sword, battle.CardUsage.InFlightCard);
            Assert.IsFalse(await battle.CardUsage.TryUseCurrentCardAsync());
            await Advance(101);
            Assert.AreEqual(70, enemies[0].CurrentHp);
            Assert.AreEqual(0, used);
            Assert.IsFalse(await battle.CardUsage.TryUseCurrentCardAsync());
            await Advance(101);
            Assert.IsTrue(await pending);
            Assert.AreEqual(1, used);
            Assert.IsNull(battle.CardUsage.InFlightCard);
            CollectionAssert.AreEqual(new[] { next }, battle.CardUsage.GetRemainingCards());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 標準効果の強靭度中断は発動前だけ不発で一度消費する(bool afterHit)
        {
            var sword = TimedSword();
            battle.CardUsage.EnqueueCards(new List<Card> { sword, sword.Clone() });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            if (afterHit) await Advance(101);
            player.TakeDamage(player.Poise);
            Assert.IsTrue(await pending);
            await Advance(1000);
            Assert.AreEqual(afterHit ? 70 : 100, enemies[0].CurrentHp);
            Assert.AreEqual(1, used);
            Assert.AreEqual(1, battle.CardUsage.RemainingCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 標準効果の前後隙はcustom中に残り時間を保持する(bool recovery)
        {
            var sword = TimedSword();
            battle.CardUsage.EnqueueCards(new List<Card> { sword });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            if (recovery) await Advance(101);
            await Advance(40);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider(0));
            await Advance(1000);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(recovery ? 70 : 100, enemies[0].CurrentHp);
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            await Advance(59);
            Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(recovery ? 70 : 100, enemies[0].CurrentHp);
            await Advance(2);
            if (!recovery) await Advance(101);
            Assert.IsTrue(await pending);
            Assert.AreEqual(70, enemies[0].CurrentHp);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task 標準効果は戦闘終了やキュー初期化で旧攻撃を残さない(bool endBattle, bool afterHit)
        {
            var sword = TimedSword();
            battle.CardUsage.EnqueueCards(new List<Card> { sword });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            if (afterHit) await Advance(101);
            if (endBattle) player.TakeDamage(1000);
            else battle.CardUsage.Clear();
            Assert.IsFalse(await pending);
            var next = sword.Clone();
            battle.CardUsage.EnqueueCards(new List<Card> { next });
            await Advance(1000);
            Assert.AreEqual(afterHit ? 70 : 100, enemies[0].CurrentHp);
            Assert.AreEqual(0, used);
            Assert.IsNull(battle.CardUsage.InFlightCard);
            CollectionAssert.AreEqual(new[] { next }, battle.CardUsage.GetRemainingCards());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 発射後の独立投擲は後隙完了や中断で消えず一度だけ着弾する(bool interrupt)
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.DelayedProjectile, DelayTime = 1f,
                TargetOffset = new Vector2Int(1, 0), Trigger = DerivativeTrigger.Immediate });
            var sword = TimedSword(modifier);
            battle.CardUsage.EnqueueCards(new List<Card> { sword });
            var pending = battle.CardUsage.TryUseCurrentCardAsync().AsTask();
            await Advance(101);
            Assert.AreEqual(100, enemies[0].CurrentHp);
            if (interrupt) player.TakeDamage(player.Poise);
            else await Advance(101);
            Assert.IsTrue(await pending);
            Assert.IsNull(battle.CardUsage.InFlightCard);
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount);
            await Advance(1000);
            Assert.AreEqual(70, enemies[0].CurrentHp);
            await Advance(1000);
            Assert.AreEqual(70, enemies[0].CurrentHp);
        }

        [Test]
        public async Task 非同期経路でも暗転効果は前後隙を待たない()
        {
            battle.CardUsage.EnqueueCards(new List<Card> { TimedSword(instant: true) });
            Assert.IsTrue(await battle.CardUsage.TryUseCurrentCardAsync());
            Assert.AreEqual(70, enemies[0].CurrentHp);
            Assert.IsNull(battle.CardUsage.InFlightCard);
        }
    }
}
