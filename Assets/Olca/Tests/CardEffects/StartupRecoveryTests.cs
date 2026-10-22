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


namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class DerivativeRecoveryTests
    {
        private BattleSystem battle;
        private Enemy enemy;
        private MockCardDatabase database;
        private static Card Card(string id, int start = 0, int recovery = 0, bool extend = false,
            CardModifier modifier = null, bool instant = false) => new Card(id, id, 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.None, id == "parent" ? 1 : 7, "検証",
                DamageTargetType.Single, modifier: modifier, startupFrames: start, recoveryFrames: recovery,
                recoveryCancelAfterFrames: recovery > 0 ? 2 : (int?)null,
                extendRecoveryDuringDerivative: extend, isInstantEffect: instant);
        private static CardModifier Chain(params string[] ids)
        {
            var modifier = new CardModifier();
            foreach (var id in ids) modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.ChainCard, ChainCardId = id,
                EffectOrigin = ChainEffectOrigin.PlayerPosition, Trigger = DerivativeTrigger.OnHit });
            return modifier;
        }
        private void Setup(bool nested = false)
        {
            database = new MockCardDatabase();
            database.AddCard(Card("child", 3, 4, modifier: nested ? Chain("grandchild") : null));
            database.AddCard(Card("grandchild", 8, 3));
            enemy = new Enemy("target", "検証", 500, ElementType.None, new Vector2Int(2, 0));
            battle = new BattleSystem(new PlayerCharacter(500, new Vector2Int(1, 0)),
                new List<Enemy> { enemy }, new Deck(), cardDatabase: database);
            enemy.ApplyStun(60000);
        }
        private void Frames(int count) { for (int i = 0; i < count; i++) battle.TickFrame(17); }
        private Olca.Core.Interfaces.FrameTask<bool> Use(Card card)
        {
            battle.CardUsage.EnqueueCards(new List<Card> { card, Card("next", 2, 4) });
            return battle.CardUsage.TryUseCurrentCardAsync().Preserve();
        }
        [TestCase(false)] [TestCase(true)]
        public void 指定時だけ派生の最終後隙を待ってから親の後隙と窓を開始する(bool extend)
        {
            Setup(); var task = Use(Card("parent", recovery: 6, extend: extend, modifier: Chain("child")));
            Assert.AreEqual(499, enemy.CurrentHp);
            Assert.AreEqual(extend ? PlayerAttackPhase.Executing : PlayerAttackPhase.Recovery, battle.PlayerActionPhase);
            Frames(3); Assert.AreEqual(492, enemy.CurrentHp);
            Assert.AreEqual(extend ? 0 : 3, battle.RecoveryElapsedFrames);
            Assert.AreEqual(!extend, battle.CanCancelPlayerRecovery);
            Assert.IsFalse(battle.PlayerMovement.CanMove);
            Frames(3);
            if (!extend) { Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction); }
            else {
                Assert.IsFalse(task.GetAwaiter().IsCompleted); Assert.IsFalse(battle.TryStartPlayerBasicAttack(false));
                Assert.IsFalse(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());
                Frames(1); Assert.AreEqual(PlayerAttackPhase.Recovery, battle.PlayerActionPhase);
                Assert.AreEqual(0, battle.RecoveryElapsedFrames); Frames(1); Assert.IsFalse(battle.CanCancelPlayerRecovery);
                Frames(1); Assert.IsTrue(battle.CanCancelPlayerRecovery);
                Assert.IsTrue(battle.TryStartPlayerBasicAttack(false)); Assert.IsTrue(task.GetAwaiter().GetResult());
                Assert.AreEqual(482, enemy.CurrentHp);
            }
            Frames(10); Assert.AreEqual(extend ? 482 : 492, enemy.CurrentHp);
        }
        [Test]
        public void 多重派生と孫の後隙をすべて数え無関係な遅延を待たない()
        {
            Setup(true); database.AddCard(Card("parallel", 1, 2));
            battle.DelayedEffects.RegisterDelayedEffect(new DelayedEffect(DelayedEffectType.Recovery, 99999));
            var task = Use(Card("parent", recovery: 6, extend: true, modifier: Chain("child", "parallel")));
            Frames(13); Assert.AreEqual(PlayerAttackPhase.Executing, battle.PlayerActionPhase);
            Assert.AreEqual(478, enemy.CurrentHp); Frames(1);
            Assert.AreEqual(PlayerAttackPhase.Recovery, battle.PlayerActionPhase); Assert.AreEqual(0, battle.RecoveryElapsedFrames);
            Frames(6); Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsTrue(battle.PlayerMovement.CanMove);
            Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount);
        }
        [TestCase(false)] [TestCase(true)]
        public void 停止と変身プレビュー確定は派生時計を増やさず再開時に一度だけ終了する(bool confirm)
        {
            Setup(); battle.Transformations.Configure(new[] { new TransformationDefenseDefinition("form", ElementResistances.Neutral, ElementType.Water) });
            var task = Use(Card("parent", recovery: 6, extend: true, modifier: Chain("child")));
            Frames(1); battle.Pause(BattlePauseReason.User); Frames(30); Assert.AreEqual(499, enemy.CurrentHp);
            battle.Resume(BattlePauseReason.User); battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider()); Assert.IsTrue(battle.Transformations.TryPreview("form"));
            Frames(30); Assert.AreEqual(499, enemy.CurrentHp);
            if (confirm) battle.CustomScreen.Confirm(); else battle.Transformations.TryCancelPending();
            battle.CustomScreen.Close(); Frames(1); Assert.AreEqual(499, enemy.CurrentHp); Frames(1); Assert.AreEqual(492, enemy.CurrentHp);
            Frames(4); Assert.AreEqual(PlayerAttackPhase.Recovery, battle.PlayerActionPhase); Frames(6);
            Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 中断とキュー切替は発生済み派生を残し旧完了が新動作を解除しない(int mode)
        {
            Setup(); int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var task = Use(Card("parent", recovery: 6, extend: true, modifier: Chain("child")));
            if (mode == 0) battle.CardUsage.InterruptCurrentCard();
            else if (mode == 1) battle.CardUsage.Clear();
            else battle.Player.ApplyParalysis(2);
            if (mode == 2) Frames(2);
            Assert.IsFalse(battle.HasPlayerAction); Assert.AreEqual(mode != 1, task.GetAwaiter().GetResult());
            Assert.AreEqual(mode == 1 ? 0 : 1, used);
            battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(10, 20));
            Assert.IsTrue(battle.TryStartPlayerBasicAttack(false)); Frames(7);
            Assert.AreEqual(492, enemy.CurrentHp); Assert.AreEqual(PlayerAttackPhase.Startup, battle.PlayerActionPhase);
            Frames(3); Assert.AreEqual(482, enemy.CurrentHp); Frames(20); Assert.IsFalse(battle.HasPlayerAction);
        }
        [Test]
        public void 遅延の明示破棄と例外と戦闘終了でも待機を残さない()
        {
            Setup(); var task = Use(Card("parent", extend: true, modifier: Chain("child")));
            battle.DelayedEffects.Clear(); Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction);
            Setup(); task = Use(Card("parent", extend: true, modifier: Chain("child")));
            battle.DelayedEffects.GetActiveEffects()[0].ExecuteAction = () => throw new System.InvalidOperationException("検証用");
            Frames(2); Assert.Throws<System.InvalidOperationException>(() => Frames(1));
            Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction);
            Setup(); task = Use(Card("parent", extend: true, modifier: Chain("child")));
            battle.Player.TakeDamage(9999); Assert.IsFalse(task.GetAwaiter().GetResult());
            Assert.IsFalse(battle.HasPlayerAction); Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
        }
        [TestCase(false)] [TestCase(true)]
        public void 指定があっても投擲飛翔と着弾後の別派生は独立し穴落下でも拘束しない(bool hole)
        {
            Setup(); var modifier = new CardModifier(); modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.DelayedProjectile, DelayTime = .5f, TargetOffset = new Vector2Int(2, 0),
                ChainCardId = "child", Trigger = DerivativeTrigger.Immediate, CancelOnHole = true });
            if (hole) battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Hole);
            var task = Use(Card("parent", recovery: 6, extend: true, modifier: modifier)); Frames(6);
            Assert.IsTrue(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction);
            Frames(24); Assert.AreEqual(hole ? 0 : 1, battle.DelayedEffects.ActiveEffectCount);
            Frames(11); Assert.IsFalse(battle.HasPlayerAction); Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
            Assert.AreEqual(500, enemy.CurrentHp, "着弾位置から前方に敵がいない子は命中しない");
        }
        [Test]
        public void 暗転は派生待機も省き直接非同期効果は指定時に寿命を待つ()
        {
            Setup(); var card = Card("parent", recovery: 6, extend: true, modifier: Chain("child"), instant: true);
            Assert.IsTrue(Use(card).GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction); Frames(7); Assert.AreEqual(492, enemy.CurrentHp);
            Setup(); card = Card("parent", recovery: 6, extend: true, modifier: Chain("child"));
            var direct = new ModifiableCardEffect().ExecuteAsync(new BattleContext(battle, card) { CardDatabase = database }, default).Preserve();
            Frames(12); Assert.IsFalse(direct.GetAwaiter().IsCompleted); Frames(1); Assert.IsTrue(direct.GetAwaiter().IsCompleted); direct.GetAwaiter().GetResult();
        }
        [Test]
        public void 派生途中のファイル保存再開は開始境界へ戻り旧待機と遅延を新戦闘へ持ち込まない()
        {
            Setup(); var root = Card("parent", recovery: 6, extend: true, modifier: Chain("child"));
            var config = DungeonConfig.CreateTestDungeon(10);
            config.MapCells[3] = new MapCell(3, new Enemy("boss", "検証", 500, ElementType.None, new Vector2Int(3, 0)));
            var cards = new Deck(new List<Card> { root, root.Clone(), root.Clone(), root.Clone(), root.Clone() });
            var defaults = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(config, cards);
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-derivative-" + System.Guid.NewGuid().ToString("N"));
            try {
                var store = new Olca.Unity.FileCheckpointStore(defaults.Definitions, directory);
                var session = new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions, store);
                var game = new Olca.Core.Logic.Integration.DungeonBattleManager(config, new PlayerCharacter(500), cards,
                    new MockRandomProvider(), new MockLogger(), checkpoints: session, cardDatabase: database);
                for (int f = 0; game.CurrentBattle == null && f < 100; f++) game.TickFrame(17);
                Assert.IsNotNull(game.CurrentBattle); battle = game.CurrentBattle; enemy = battle.Enemies[0]; enemy.ApplyStun(60000);
                battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); var task = Use(root); Frames(1);
                Assert.AreEqual(PlayerAttackPhase.Executing, battle.PlayerActionPhase);
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session, new MockRandomProvider(), new MockLogger(), out var restored, out var error, cardDatabase: database), error);
                Assert.IsTrue(game.SuspendForCheckpoint()); restored.ActivateCheckpoint();
                Assert.IsFalse(task.GetAwaiter().GetResult()); Assert.IsFalse(battle.HasPlayerAction);
                Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount); Frames(100);
                Assert.IsFalse(restored.CurrentBattle.HasPlayerAction); Assert.AreEqual(0, restored.CurrentBattle.DelayedEffects.ActiveEffectCount);
                Assert.AreEqual(500, restored.CurrentBattle.Enemies[0].CurrentHp);
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
            } finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }
        [Test]
        public void 派生の時計は描画頻度と追いつきに依存しない()
        {
            List<string> Trace(int fps) {
                Setup(true); var task = Use(Card("parent", recovery: 6, extend: true, modifier: Chain("child")));
                var trace = new List<string>(); var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); long wall = 0;
                void Tick(long f, int ms) { if (f == 4) battle.Pause(BattlePauseReason.User); if (f == 9) battle.Resume(BattlePauseReason.User); battle.TickFrame(ms); trace.Add(enemy.CurrentHp + ":" + battle.PlayerActionPhase + ":" + battle.RecoveryElapsedFrames); }
                if (fps == 0) { clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond); clock.Drain(Tick); }
                else for (int i = 1; clock.Frame < 60; i++) { long next = (long)i * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps; clock.Accumulate(next - wall); wall = next; clock.Drain(Tick); }
                Assert.IsTrue(task.GetAwaiter().GetResult()); return trace;
            }
            CollectionAssert.AreEqual(Trace(60), Trace(30)); CollectionAssert.AreEqual(Trace(60), Trace(120)); CollectionAssert.AreEqual(Trace(60), Trace(0));
        }
    }
}
