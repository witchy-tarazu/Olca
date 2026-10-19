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

        [TestCase("none")][TestCase("guard")][TestCase("barrier")][TestCase("aura")][TestCase("resistance")][TestCase("simple")]
        public void カード被弾無敵は受理ヒットだけで発生し同フレームの後続を防がない(string defense)
        {
            var enemy = new Enemy("hit", "検証", 100, ElementType.None, new Vector2Int(3,0),
                resistances: new ElementResistances(fire: defense == "resistance" ? 100 : 0));
            var model = new BattleSystem(player, new List<Enemy>{enemy}, new Deck());
            if (defense == "guard") enemy.Guard.Set(true,false);
            if (defense == "barrier") enemy.SetBarrier(1);
            if (defense == "aura") enemy.SetAura(1);
            var card = new Card("hit", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.None, 10, "検証", hitInvincibilityMs: 100);
            var effect = defense == "simple" ? (ICardEffect)new DamageEffect(10) : CardEffectFactory.CreateEffect(card); var context = new BattleContext(model,card);
            model.RunLogicalFrame(() => { effect.Execute(context); effect.Execute(context); });
            Assert.AreEqual(defense == "none" || defense == "simple" ? 80 : 100, enemy.CurrentHp);
            Assert.AreEqual(defense == "none" || defense == "simple", enemy.IsInvincible);
            if (defense != "none" && defense != "simple") return;
            var disabled = new Card("off", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.None, 10, "検証");
            model.RunLogicalFrame(() => CardEffectFactory.CreateEffect(disabled).Execute(new BattleContext(model,disabled)));
            Assert.AreEqual(80, enemy.CurrentHp, "発生なし設定でも既存の無敵を通さない");
            model.Pause(BattlePauseReason.User); for(int i=0;i<10;i++)model.TickFrame(17); Assert.IsTrue(enemy.IsInvincible);
            model.Resume(BattlePauseReason.User); for(int i=0;i<6;i++)model.TickFrame(17); Assert.IsFalse(enemy.IsInvincible);
            model.RunLogicalFrame(() => CardEffectFactory.CreateEffect(disabled).Execute(new BattleContext(model,disabled)));
            Assert.AreEqual(70, enemy.CurrentHp); Assert.IsFalse(enemy.IsInvincible);
        }

        [TestCase(0)][TestCase(200)]
        public void 状態専用ヒットと死亡した対象には新たな被弾無敵を残さない(int damage)
        {
            var enemy = battle.Enemies[0];
            var card = new Card("hit", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, damage, "検証", hitInvincibilityMs: 100);
            battle.RunLogicalFrame(() => CardEffectFactory.CreateEffect(card).Execute(new BattleContext(battle,card)));
            Assert.IsFalse(enemy.IsInvincible);
            var attacker = new Enemy("source", "検証", 100, ElementType.None, new Vector2Int(3,0));
            var other = new BattleSystem(player,new List<Enemy>{attacker},new Deck()); int effects=0;
            new BasicAttackAction(damage,1000,onHit:_=>effects++,hitInvincibilityMs:100).Execute(new AIContext(attacker,player,other.Field,new MockRandomProvider(),new MockLogger()));
            Assert.AreEqual(1,effects); Assert.IsFalse(player.IsInvincible);
        }

        [Test]
        public void カード被弾無敵の定義は複製とマスターで保持し不正値を副作用前に拒否する()
        {
            var master = new Olca.Core.Domain.Master.CardMaster { Id="hit", Name="検証", HitInvincibilityMs=100 };
            var card = Card.FromMaster(master);
            Assert.AreEqual(100, card.HitInvincibilityMs); Assert.AreEqual(100,card.Clone().HitInvincibilityMs);
            Assert.AreEqual(100,new Olca.Application.Battle.SimpleCardFactory().CreateExceeded(card, ExceedLevel.Lv1).HitInvincibilityMs);
            master.HitInvincibilityMs=-1; Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card.FromMaster(master));
            int notices=0; var enemy=battle.Enemies[0]; enemy.OnDamaged+=(_,__)=>notices++;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>enemy.TakeDamage(1,ElementType.None,hitInvincibilityMs:-1));
            Assert.AreEqual(0,notices); Assert.AreEqual(50,enemy.CurrentHp); Assert.IsFalse(enemy.IsInvincible);
        }

        [TestCase(false, false)][TestCase(false, true)][TestCase(true, false)][TestCase(true, true)]
        public void 防御の消滅フレームは合計判定で全攻撃を防ぎ次フレームから通す(bool enemySide, bool aura)
        {
            var enemy = battle.Enemies[0]; int initial = enemySide ? enemy.CurrentHp : player.CurrentHp;
            if (enemySide) { if (aura) enemy.SetAura(15); else enemy.SetBarrier(15); }
            else { if (aura) player.SetAura(15); else player.SetBarrier(15); }
            System.Action<int> hit = damage => { if (enemySide) enemy.TakeDamage(damage, ElementType.None); else player.TakeDamage(damage); };
            battle.RunLogicalFrame(() => { hit(6); hit(6); hit(6); });
            Assert.AreEqual(initial, enemySide ? enemy.CurrentHp : player.CurrentHp);
            Assert.AreEqual(0, enemySide ? enemy.AuraThreshold + enemy.BarrierHp : player.AuraThreshold + player.BarrierHp);
            battle.RunLogicalFrame(() => hit(1));
            Assert.AreEqual(initial - 1, enemySide ? enemy.CurrentHp : player.CurrentHp);
        }

        [TestCase(false, false)][TestCase(false, true)][TestCase(true, false)][TestCase(true, true)]
        public void 防御の外部指定期間はポーズ中停止し期限フレーム終端で解除する(bool enemySide, bool aura)
        {
            var enemy = battle.Enemies[0]; enemy.ApplyStun(60000);
            if (enemySide) { if (aura) enemy.SetAura(15, 2); else enemy.SetBarrier(15, 2); }
            else { if (aura) player.SetAura(15, 2); else player.SetBarrier(15, 2); }
            System.Func<bool> active = () => enemySide ? enemy.HasAura || enemy.HasBarrier : player.HasAura || player.HasBarrier;
            battle.Pause(BattlePauseReason.User);
            for (int f = 0; f < 5; f++) battle.TickFrame(17);
            Assert.IsTrue(active()); battle.Resume(BattlePauseReason.User);
            battle.TickFrame(17); Assert.IsTrue(active());
            battle.RunLogicalFrame(() => {
                battle.TickFrame(17); Assert.IsTrue(active(), "期限フレーム途中は防御を維持");
                if (enemySide) enemy.TakeDamage(1, ElementType.None); else player.TakeDamage(1);
            });
            Assert.IsFalse(active());
            Assert.AreEqual(enemySide ? 50 : 100, enemySide ? enemy.CurrentHp : player.CurrentHp);
        }

        [TestCase(9, 10)][TestCase(10, 0)][TestCase(11, 0)]
        public void オーラは基準未満を維持し同値以上の一発も全防御する(int damage, int remaining)
        {
            player.SetAura(10); int effects = 0;
            var ai = new BasicAttackAction(damage, 0, attackProperties: AttackProperty.Break, poiseDamage: 999,
                onHit: _ => effects++, hitInvincibilityMs: 100);
            ai.Execute(new AIContext(battle.Enemies[0], player, battle.Field, new MockRandomProvider(), new MockLogger()));
            Assert.AreEqual(remaining, player.AuraThreshold); Assert.AreEqual(100, player.CurrentHp);
            Assert.AreEqual(0, effects); Assert.AreEqual(0, player.AccumulatedDamage); Assert.IsFalse(player.IsInvincible);
        }

        [TestCase(-100, false, 15)][TestCase(50, false, 15)][TestCase(100, false, 15)]
        [TestCase(-100, true, 0)][TestCase(50, true, 0)][TestCase(100, true, 0)]
        public void オーラは本体耐性を無視し地形補正で消滅を決める(int resistance, bool grass, int remaining)
        {
            player.SetElementResistances(new ElementResistances(fire: resistance)); player.SetAura(15);
            if (grass) battle.Field.ChangePanelType(player.GridPosition, PanelType.Grass);
            Assert.IsFalse(player.TryTakeDamage(10, attackElement: ElementType.Fire));
            Assert.AreEqual(remaining, player.AuraThreshold); Assert.AreEqual(100, player.CurrentHp);
        }

        [Test]
        public void オーラは威力ゼロと毒を通し既存無敵で防いだ攻撃では消滅しない()
        {
            player.SetAura(10);
            Assert.IsTrue(player.TryTakeDamage(0, poiseDamage: 0));
            player.SetInvincible(100); Assert.IsFalse(player.TryTakeDamage(100));
            player.TakePoisonDamage(200);
            Assert.AreEqual(1, player.CurrentHp); Assert.AreEqual(10, player.AuraThreshold);
            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [Test]
        public void オーラとバリアの再付与は置換し変身競合だけを拒否する()
        {
            player.SetAura(10); player.SetAura(20); Assert.AreEqual(20, player.AuraThreshold);
            player.SetBarrier(30); Assert.IsFalse(player.HasAura); Assert.AreEqual(30, player.BarrierHp);
            player.SetAura(10); Assert.IsFalse(player.HasBarrier);
            Assert.Throws<System.InvalidOperationException>(() => player.BeginTransformationDefense(ElementResistances.Neutral, ElementType.Fire));
            player.SetAura(0); player.BeginTransformationDefense(ElementResistances.Neutral, ElementType.Fire);
            Assert.Throws<System.InvalidOperationException>(() => player.SetAura(10));
            Assert.IsTrue(player.HasTransformationDefense); Assert.IsFalse(player.HasAura);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => player.SetAura(-1));
        }

        [Test]
        public void オーラは戦闘終了通知前に解除され保存未対応状態を黙って落とさない()
        {
            player.SetAura(10); bool ended = false;
            battle.OnBattleEnd += _ => { ended = true; Assert.IsFalse(player.HasAura); };
            battle.Enemies[0].TakeDamage(1000, ElementType.None); Assert.IsTrue(ended);
            var map = DungeonConfig.CreateTestDungeon(6, 30, 100); var empty = new Deck();
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, empty);
            var pending = new PlayerCharacter(100); pending.SetAura(10);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, pending, empty,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
            Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked); Assert.IsNull(checkpoints.Store.Read());
            Assert.AreEqual(10, pending.AuraThreshold);
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
                ai.Execute(context); Assert.AreEqual(logical ? 15 : 5, player.BarrierHp);
                ai.Execute(context); Assert.AreEqual(logical ? 15 : 0, player.BarrierHp);
                Assert.AreEqual(0, effects); Assert.AreEqual(0, player.AccumulatedDamage);
                Assert.IsFalse(player.IsInvincible);
                ai.Execute(context);
            };
            if (logical) battle.RunLogicalFrame(hits); else hits();
            Assert.AreEqual(logical ? 100 : 90, player.CurrentHp); Assert.AreEqual(logical ? 0 : 1, effects);
            Assert.AreEqual(logical ? 0 : 7, player.AccumulatedDamage); Assert.AreEqual(!logical, player.IsInvincible);
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
            player.TickInvincible(100); player.SetBarrier(0);
            battle.RunLogicalFrame(() => {
                player.TakePoisonDamage(100);
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
