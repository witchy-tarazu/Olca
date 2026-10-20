using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    /// <summary>
    /// Enemy スタン機能 と EnemyAIBehavior のスタン中スキップのテスト
    /// </summary>
    [TestFixture]
    public class EnemyStunTests
    {
        private static StatusEffect PlayerPoison(int frames, int damage=3, int resistance=0)
            => new StatusEffect(StatusEffectType.Poison, frames*1000/60, damage, resistance);
        private static BattleSystem PoisonBattle(PlayerCharacter player)
            => new BattleSystem(player,new List<Enemy>{new Enemy("p","p",9999,ElementType.None,new Vector2Int(5,0))},new Deck());
        [Test]
        public void プレイヤー毒は自動更新し種類別に共存して最終フレームも与え下限一を守る()
        {
            var p=new PlayerCharacter(100);var b=PoisonBattle(p);
            p.ApplyPoison("fixture-poison",PlayerPoison(6,4),2);
            p.ApplyPoison("fixture-strong",PlayerPoison(6,7),3);
            for(int i=0;i<6;i++) b.TickFrame(16);
            Assert.AreEqual(74,p.CurrentHp);Assert.IsEmpty(p.CapturePoisons());
            b.Tick(1);Assert.AreEqual(74,p.CurrentHp,"期限終了後のms互換更新も安全");
            p.ApplyPoison("fixture-poison",PlayerPoison(1,1000),1);b.TickFrame(16);
            Assert.AreEqual(1,p.CurrentHp);Assert.IsFalse(p.IsInvincible);Assert.IsFalse(p.IsStaggered);
        }
        [Test]
        public void プレイヤー毒は停止中カウントを進めず付与フレームを消費しない()
        {
            var p=new PlayerCharacter(100);var b=PoisonBattle(p);
            b.RunLogicalFrame(()=>p.ApplyPoison("fixture",PlayerPoison(6),2));
            Assert.AreEqual(300,p.CapturePoisons()[0].RemainingUnits);
            b.Pause();for(int i=0;i<20;i++)b.TickFrame(16);
            Assert.AreEqual(300,p.CapturePoisons()[0].RemainingUnits);Assert.AreEqual(100,p.CurrentHp);
            b.Resume();b.TickFrame(16);Assert.AreEqual(250,p.CapturePoisons()[0].RemainingUnits);
            b.TickFrame(16);Assert.AreEqual(97,p.CurrentHp);
        }
        [Test]
        public void プレイヤー毒の同フレーム回復は順序不変で通常ダメージは別計上する()
        {
            foreach(bool healFirst in new[]{true,false}) {
                var p=new PlayerCharacter(100);p.TakeDamage(95,affectsPoise:false);var b=PoisonBattle(p);
                p.ApplyPoison("fixture",PlayerPoison(1,10),1);
                b.RunLogicalFrame(()=>{if(healFirst)p.Heal(10);b.TickFrame(16);if(!healFirst)p.Heal(10);});
                Assert.AreEqual(5,p.CurrentHp);
                p.ApplyPoison("fixture",PlayerPoison(1,100),1);
                b.RunLogicalFrame(()=>{b.TickFrame(16);p.TakeDamage(1,affectsPoise:false);});
                Assert.AreEqual(0,p.CurrentHp);
            }
        }
        [Test]
        public void プレイヤー毒は再付与で間隔を維持し移行で間隔だけ戻し全種解除する()
        {
            var p=new PlayerCharacter(100);var b=PoisonBattle(p);
            p.ApplyPoison("a",PlayerPoison(12),4);b.TickFrame(16);
            p.ApplyPoison("a",PlayerPoison(6),4);
            var state=p.CapturePoisons()[0];Assert.AreEqual(300,state.RemainingUnits);Assert.AreEqual(150,state.NextDamageUnits);
            p.ResetPoisonIntervals();state=p.CapturePoisons()[0];Assert.AreEqual(300,state.RemainingUnits);Assert.AreEqual(200,state.NextDamageUnits);
            p.ApplyPoison("b",PlayerPoison(6),2);p.ClearPoisons();Assert.IsEmpty(p.CapturePoisons());
        }
        [Test]
        public void プレイヤー毒の耐性は期間だけ短縮し短期間は無発生で共存未確定を拒否する()
        {
            var p=new PlayerCharacter(100);var b=PoisonBattle(p);
            p.ApplyPoison("a",PlayerPoison(5,30,50),3);
            Assert.AreEqual(100,p.CapturePoisons()[0].RemainingUnits);
            Assert.Throws<System.NotSupportedException>(()=>p.ApplyParalysis(6));
            for(int i=0;i<2;i++)b.TickFrame(16);Assert.AreEqual(100,p.CurrentHp);Assert.IsEmpty(p.CapturePoisons());
            p.ApplyParalysis(6);Assert.Throws<System.NotSupportedException>(()=>p.ApplyPoison("a",PlayerPoison(6),2));
        }
        [Test]
        public void プレイヤー毒の保存は独立コピーでms端数も復元し重複や破損を拒否する()
        {
            var p=new PlayerCharacter(100);var b=PoisonBattle(p);p.ApplyPoison("a",PlayerPoison(60),6);b.Tick(1);
            var saved=p.CapturePoisons();Assert.AreEqual(2997,saved[0].RemainingUnits);Assert.AreEqual(297,saved[0].NextDamageUnits);
            var q=new PlayerCharacter(100);q.RestorePoisons(saved);saved[0].RemainingUnits=1;
            Assert.AreEqual(2997,q.CapturePoisons()[0].RemainingUnits);
            var good=q.CapturePoisons()[0];Assert.Throws<System.ArgumentException>(()=>q.RestorePoisons(new[]{good,good}));
            good.NextDamageUnits=0;Assert.Throws<System.ArgumentException>(()=>q.RestorePoisons(new[]{good}));
            Assert.AreEqual(2997,q.CapturePoisons()[0].RemainingUnits,"失敗時に既存状態を壊さない");
        }

        private Enemy enemy;

        [SetUp]
        public void SetUp()
        {
            enemy = new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(3, 0));
        }

        [TestCase(false, 9, 1)][TestCase(false, 10, 0)][TestCase(false, 11, 0)]
        [TestCase(true, 9, 10)][TestCase(true, 10, 0)][TestCase(true, 11, 0)]
        public void 敵防御は破壊した一発でも命中と独立削りを通さない(bool aura, int damage, int remaining)
        {
            var target = new Enemy("defense", "検証", 100, ElementType.None, Vector2Int.Zero,
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            if (aura) target.SetAura(10); else target.SetBarrier(10);
            int hits = 0; target.OnDamaged += (_, __) => hits++;
            target.TakeDamage(damage, ElementType.Fire, AttackProperty.Sword | AttackProperty.Break, 999);
            Assert.AreEqual(100, target.CurrentHp); Assert.AreEqual(0, hits);
            Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.IsFalse(target.IsStaggered);
            Assert.AreEqual(remaining, aura ? target.AuraThreshold : target.BarrierHp);
        }

        [TestCase(false)][TestCase(true)]
        public void 敵防御の地形補正は完全耐性を無視し毒と状態専用は通る(bool aura)
        {
            var target = new Enemy("defense", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                resistances: new ElementResistances(fire: 100), poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target }, new Deck());
            if (aura) target.SetAura(15); else target.SetBarrier(15);
            battle.Field.ChangePanelType(target.GridPosition, PanelType.Grass);
            target.TakeDamage(10, ElementType.Fire, AttackProperty.Break, 999);
            Assert.IsFalse(target.HasBarrier); Assert.IsFalse(target.HasAura); Assert.AreEqual(100, target.CurrentHp);
            if (aura) target.SetAura(15); else target.SetBarrier(15);
            int hits = 0; target.OnDamaged += (_, __) => hits++;
            target.TakeDamage(0, ElementType.Fire, poiseDamage: 0); Assert.AreEqual(1, hits);
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int f = 0; f < 10; f++) battle.TickFrame(17);
            Assert.Less(target.CurrentHp, 100); Assert.AreEqual(15, aura ? target.AuraThreshold : target.BarrierHp);
            Assert.AreEqual(0, target.AccumulatedPoiseDamage);
        }

        [TestCase(false)][TestCase(true)]
        public void 敵防御はガード併用と再付与置換に対応し標準保存には混ぜない(bool aura)
        {
            if (aura) enemy.SetAura(10); else enemy.SetBarrier(10);
            enemy.Guard.Set(true, false);
            enemy.TakeDamage(100, ElementType.None);
            Assert.AreEqual(10, aura ? enemy.AuraThreshold : enemy.BarrierHp);
            enemy.TakeDamage(1, ElementType.None, AttackProperty.Break);
            Assert.AreEqual(aura ? 10 : 9, aura ? enemy.AuraThreshold : enemy.BarrierHp);
            enemy.SetBarrier(20); enemy.SetAura(30); Assert.AreEqual(0, enemy.BarrierHp);
            Assert.AreEqual(30, enemy.AuraThreshold); Assert.IsTrue(enemy.Guard.IsActive);
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { enemy }, new Deck());
            battle.Player.TakeDamage(1000); Assert.IsFalse(enemy.HasBarrier); Assert.IsFalse(enemy.HasAura);
            var map = DungeonConfig.CreateTestDungeon(6, 30, 100);
            if (aura) map.MapCells[5].Enemy.SetAura(10); else map.MapCells[5].Enemy.SetBarrier(10);
            Assert.Throws<System.ArgumentException>(() => Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, new Deck()));
        }

        [TestCase(false)][TestCase(true)]
        public void フレーム途中に防御を付与しても死亡通知前に解除する(bool aura)
        {
            var survivor = new Enemy("survivor", "検証", 100, ElementType.None, new Vector2Int(5, 0));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { enemy, survivor }, new Deck());
            int deaths = 0;
            enemy.OnDeath += () => { deaths++; Assert.IsFalse(enemy.HasBarrier); Assert.IsFalse(enemy.HasAura); };
            battle.RunLogicalFrame(() => {
                enemy.TakeDamage(1000, ElementType.None);
                if (aura) enemy.SetAura(10); else enemy.SetBarrier(10);
            });
            Assert.AreEqual(1, deaths); Assert.IsFalse(battle.IsGameOver);
        }

        [Test]
        public void 敵の新規無敵は同フレーム全命中後に有効になり期間中は毒だけ通す()
        {
            var target = new Enemy("invincible", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target }, new Deck());
            battle.RunLogicalFrame(() => {
                target.TakeDamage(1, ElementType.None, poiseDamage: 7); target.SetInvincible(100);
                Assert.IsFalse(target.IsInvincible); target.TakeDamage(1, ElementType.None, poiseDamage: 7);
            });
            Assert.AreEqual(98, target.CurrentHp); Assert.AreEqual(14, target.AccumulatedPoiseDamage); Assert.IsTrue(target.IsInvincible);
            target.SetBarrier(15); int hits = 0; target.OnDamaged += (_, __) => hits++;
            battle.RunLogicalFrame(() => { target.TakeDamage(999, ElementType.Fire, AttackProperty.Break, 999); target.TakeDamage(0, ElementType.None); });
            Assert.AreEqual(0, hits); Assert.AreEqual(15, target.BarrierHp); Assert.AreEqual(14, target.AccumulatedPoiseDamage);
            battle.Pause(BattlePauseReason.User); for (int f=0;f<20;f++) battle.TickFrame(17); Assert.IsTrue(target.IsInvincible);
            battle.Resume(BattlePauseReason.User);
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 100, 60));
            for (int f=0;f<5;f++) battle.TickFrame(17);
            Assert.IsTrue(target.IsInvincible); Assert.Less(target.CurrentHp, 98); Assert.AreEqual(15, target.BarrierHp);
            battle.TickFrame(17); Assert.IsFalse(target.IsInvincible);
            target.SetBarrier(0); int hp=target.CurrentHp; target.TakeDamage(1, ElementType.None, poiseDamage: 0); Assert.AreEqual(hp-1,target.CurrentHp);
        }

        [Test]
        public void 敵の無敵は死亡と戦闘終了で解除し標準保存定義には混ぜない()
        {
            var survivor = new Enemy("survivor", "検証", 100, ElementType.None, new Vector2Int(5,0));
            var battle = new BattleSystem(new PlayerCharacter(100),new List<Enemy>{enemy,survivor},new Deck());
            enemy.OnDeath += () => Assert.IsFalse(enemy.IsInvincible);
            battle.RunLogicalFrame(() => {enemy.TakeDamage(1000,ElementType.None);enemy.SetInvincible(100);});
            Assert.IsTrue(enemy.IsDead); Assert.IsFalse(enemy.IsInvincible);
            survivor.SetInvincible(100); battle.Player.TakeDamage(1000); Assert.IsFalse(survivor.IsInvincible);
            var map=DungeonConfig.CreateTestDungeon(6,30,100);map.MapCells[5].Enemy.SetInvincible(100);
            Assert.Throws<System.ArgumentException>(()=>Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,new Deck()));
        }

        private static StatusEffect PoisonFrames(int frames,int damage=3,int resistance=0)
            => new StatusEffect(StatusEffectType.Poison,(frames*1000)/60,damage,resistance);

        [Test]
        public void 外部毒は付与フレームに発生せず間隔と最終フレームを含めて進む()
        {
            var poison=new TimedPoisonState();var source=PoisonFrames(6);
            poison.BeginFrame();poison.Apply(source,2,true);Assert.AreEqual(0,poison.TickFrame());poison.EndFrame();
            Assert.AreEqual(6,poison.RemainingFrames);Assert.AreEqual(2,poison.FramesUntilDamage);
            var damages=new List<int>();for(int f=0;f<6;f++){poison.BeginFrame();damages.Add(poison.TickFrame());poison.EndFrame();}
            CollectionAssert.AreEqual(new[]{0,3,0,3,0,3},damages);Assert.IsFalse(poison.IsActive);Assert.AreEqual(0,poison.FramesUntilDamage);
            Assert.AreEqual(6,source.RemainingFrames,"外部の共有効果インスタンスは変更しない");
        }

        [Test]
        public void 同種毒の再付与は短縮も受理し期限一致時も次の間隔をリセットしない()
        {
            var poison=new TimedPoisonState();poison.Apply(PoisonFrames(6),4,true);
            poison.BeginFrame();Assert.AreEqual(0,poison.TickFrame());poison.EndFrame();
            poison.BeginFrame();poison.Apply(PoisonFrames(1),4,true);Assert.AreEqual(0,poison.TickFrame());poison.EndFrame();
            Assert.AreEqual(1,poison.RemainingFrames);Assert.AreEqual(2,poison.FramesUntilDamage);
            poison.BeginFrame();Assert.AreEqual(0,poison.TickFrame());poison.Apply(PoisonFrames(6),4,true);poison.EndFrame();
            Assert.AreEqual(6,poison.RemainingFrames);Assert.AreEqual(1,poison.FramesUntilDamage);
            poison.BeginFrame();Assert.AreEqual(3,poison.TickFrame());poison.EndFrame();
        }

        [TestCase(false)][TestCase(true)]
        public void 毒解除は発生予定の同フレーム量を計上し付与予約を取り消す(bool active)
        {
            var poison=new TimedPoisonState();if(active){poison.Apply(PoisonFrames(6),1,true);poison.BeginFrame();poison.EndFrame();}
            poison.BeginFrame();poison.Apply(PoisonFrames(6),1,true);poison.Clear();
            Assert.AreEqual(active?3:0,poison.TickFrame());poison.EndFrame();Assert.IsFalse(poison.IsActive);Assert.IsFalse(poison.HasPendingApplication);
        }

        [Test]
        public void 毒の無効耐性と間隔未満の期間はダメージを生まず不整合な定義を拒否する()
        {
            var poison=new TimedPoisonState();poison.Apply(PoisonFrames(6,resistance:100),2,true);Assert.IsFalse(poison.HasPendingApplication);
            poison.Apply(PoisonFrames(6,resistance:50),4,true);
            for(int i=0;i<3;i++){poison.BeginFrame();Assert.AreEqual(0,poison.TickFrame());poison.EndFrame();}Assert.IsFalse(poison.IsActive);
            Assert.Throws<System.ArgumentException>(()=>poison.Apply(PoisonFrames(6),0,true));
            Assert.Throws<System.NotSupportedException>(()=>poison.Apply(PoisonFrames(6),2,false));
            poison.Apply(PoisonFrames(6),2,true);
            Assert.Throws<System.InvalidOperationException>(()=>poison.Apply(PoisonFrames(6,4),2,true));
            Assert.Throws<System.InvalidOperationException>(()=>poison.Apply(PoisonFrames(6),3,true));
            poison.Reset();Assert.IsFalse(poison.HasPendingApplication);Assert.IsFalse(poison.IsActive);
        }

        [Test]
        public void 毒のms互換更新は同じ実経過で論理フレームと一致する()
        {
            int Run(bool logical){var state=new TimedPoisonState();state.Apply(PoisonFrames(60,7),6,true);int total=0;
                for(int f=0;f<(logical?60:10);f++){state.BeginFrame();total+=logical?state.TickFrame():state.TickMilliseconds(100);state.EndFrame();}
                Assert.IsFalse(state.IsActive);return total;}
            Assert.AreEqual(70,Run(true));Assert.AreEqual(70,Run(false));
        }

        [TestCase(false)][TestCase(true)]
        public void 独立毒量は既存の通常ダメージ合算と防御例外へ渡せる(bool normalHit)
        {
            var player=new PlayerCharacter(5);var target=new Enemy("p","検証",100,ElementType.None,new Vector2Int(5,0));target.ApplyStun(60000);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());var poison=new TimedPoisonState();poison.Apply(PoisonFrames(1,10),1,true);
            if(!normalHit){player.SetBarrier(100);player.SetInvincible(100);}
            battle.RunLogicalFrame(()=>{poison.BeginFrame();if(normalHit)player.TakeDamage(1,poiseDamage:0);player.TakePoisonDamage(poison.TickFrame());poison.EndFrame();});
            Assert.AreEqual(normalHit?0:1,player.CurrentHp);Assert.AreEqual(0,player.AccumulatedDamage);Assert.IsFalse(poison.IsActive);
            if(!normalHit)Assert.AreEqual(100,player.BarrierHp);
        }

        [Test]
        public void プレイヤー麻痺は次フレーム開始から最終フレームまで有効で再付与は残り期間を置換する()
        {
            var player=new PlayerCharacter(100);var target=new Enemy("p","検証",100,ElementType.None,new Vector2Int(5,0));target.ApplyStun(60000);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());
            battle.RunLogicalFrame(()=>{player.ApplyParalysis(2);Assert.IsFalse(player.IsParalyzed);battle.TickFrame(17);});
            Assert.AreEqual(2,player.ParalysisRemainingFrames);Assert.IsFalse(player.IsInvincible);
            battle.RunLogicalFrame(()=>{Assert.IsTrue(player.IsParalyzed);player.TakeDamage(1,poiseDamage:0);battle.TickFrame(17);});
            Assert.AreEqual(1,player.ParalysisRemainingFrames);Assert.IsTrue(player.IsParalyzed);
            battle.RunLogicalFrame(()=>{battle.TickFrame(17);Assert.IsTrue(player.IsParalyzed);player.ApplyParalysis(3);});
            Assert.AreEqual(3,player.ParalysisRemainingFrames);
            battle.RunLogicalFrame(()=>{player.ApplyParalysis(1);battle.TickFrame(17);});Assert.AreEqual(1,player.ParalysisRemainingFrames);
            battle.RunLogicalFrame(()=>{battle.TickFrame(17);Assert.IsTrue(player.IsParalyzed);});Assert.IsFalse(player.IsParalyzed);
        }

        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void プレイヤー麻痺の解除は付与予約を取り消し有効中ならフレーム末まで維持する(bool active,bool clearFirst)
        {
            var player=new PlayerCharacter(100);var target=new Enemy("p","検証",100,ElementType.None,new Vector2Int(5,0));target.ApplyStun(60000);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());
            if(active)battle.RunLogicalFrame(()=>player.ApplyParalysis(6));
            battle.RunLogicalFrame(()=>{if(clearFirst)player.ClearParalysis();player.ApplyParalysis(10);if(!clearFirst)player.ClearParalysis();Assert.AreEqual(active,player.IsParalyzed);battle.TickFrame(17);Assert.AreEqual(active,player.IsParalyzed);});
            Assert.IsFalse(player.IsParalyzed);Assert.AreEqual(0,player.ParalysisRemainingFrames);
            battle.TickFrame(17);Assert.IsFalse(player.IsParalyzed);
        }

        [TestCase(false)][TestCase(true)]
        public void プレイヤー麻痺はポーズ中に期間を消費せず死亡と勝利で予約も解除する(bool death)
        {
            var player=new PlayerCharacter(100);var target=new Enemy("p","検証",100,ElementType.None,new Vector2Int(5,0));target.ApplyStun(60000);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());
            battle.RunLogicalFrame(()=>player.ApplyParalysis(6));battle.Pause(BattlePauseReason.User);
            for(int f=0;f<10;f++)battle.TickFrame(17);Assert.AreEqual(6,player.ParalysisRemainingFrames);
            battle.Resume(BattlePauseReason.User);
            battle.RunLogicalFrame(()=>{player.ApplyParalysis(10);if(death)player.TakeDamage(1000);else target.TakeDamage(1000,ElementType.None);});
            Assert.IsFalse(player.IsParalyzed);Assert.AreEqual(0,player.ParalysisRemainingFrames);
        }

        [Test]
        public void 未確定または有効なプレイヤー麻痺は保存へ混入できず不正期間は拒否する()
        {
            foreach(bool active in new[]{false,true}) {
                var player=new PlayerCharacter(100);Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ApplyParalysis(0));
                var map=DungeonConfig.CreateTestDungeon(6,30,100);var deck=new Deck();
                var checkpoints=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);
                if(active){var battle=new BattleSystem(player,new List<Enemy>{map.MapCells[5].Enemy},deck);battle.RunLogicalFrame(()=>player.ApplyParalysis(6));}
                else player.ApplyParalysis(6);
                var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(map,player,deck,new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger(),checkpoints:checkpoints);
                Assert.IsTrue(manager.Dungeon.IsCheckpointBlocked);Assert.IsNull(checkpoints.Store.Read());
            }
        }

        [TestCase(false)][TestCase(true)]
        public void 強靭度回復待ちは正の受理削りだけで更新し最大値変更と防御では延長しない(bool enemySide)
        {
            var target = new Enemy("recovery", "検証", 100, ElementType.None, new Vector2Int(5,0), poiseDefinition:new EnemyPoiseDefinition(30,6));
            target.ApplyStun(60000); var player=new PlayerCharacter(100);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());
            if(enemySide)target.ConfigurePoiseRecovery(6);else player.ConfigurePoiseRecovery(6);
            System.Action<int> hit=impact=>{if(enemySide)target.TakeDamage(1,ElementType.None,poiseDamage:impact);else player.TakeDamage(1,poiseDamage:impact);};
            System.Func<int> remaining=()=>enemySide?target.PoiseRecoveryRemainingFrames:player.PoiseRecoveryRemainingFrames;
            System.Func<int> accumulated=()=>enemySide?target.AccumulatedPoiseDamage:player.AccumulatedDamage;
            battle.RunLogicalFrame(()=>{hit(7);battle.TickFrame(17);}); Assert.AreEqual(6,remaining());
            battle.TickFrame(17);battle.TickFrame(17);Assert.AreEqual(4,remaining());
            if(enemySide)target.SetPoise(100);else player.SetPoise(100);Assert.AreEqual(4,remaining());
            battle.RunLogicalFrame(()=>{if(enemySide)target.SetBarrier(1);else player.SetBarrier(1);hit(999);battle.TickFrame(17);});
            Assert.AreEqual(3,remaining());Assert.AreEqual(7,accumulated());
            battle.RunLogicalFrame(()=>{hit(0);battle.TickFrame(17);});Assert.AreEqual(2,remaining());
            battle.Pause(BattlePauseReason.User);for(int f=0;f<10;f++)battle.TickFrame(17);Assert.AreEqual(2,remaining());battle.Resume(BattlePauseReason.User);
            battle.TickFrame(17);Assert.AreEqual(7,accumulated());battle.TickFrame(17);Assert.AreEqual(0,accumulated());Assert.AreEqual(0,remaining());
        }

        [TestCase(false)][TestCase(true)]
        public void 回復期限と同フレームの正の削りは蓄積を維持して待ち時間を更新する(bool enemySide)
        {
            var target=new Enemy("recovery","検証",100,ElementType.None,new Vector2Int(5,0),poiseDefinition:new EnemyPoiseDefinition(30,6));target.ApplyStun(60000);
            var player=new PlayerCharacter(100);var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());
            if(enemySide)target.ConfigurePoiseRecovery(1);else player.ConfigurePoiseRecovery(1);
            System.Action hit=()=>{if(enemySide)target.TakeDamage(1,ElementType.None,poiseDamage:7);else player.TakeDamage(1,poiseDamage:7);};
            battle.RunLogicalFrame(hit);
            battle.RunLogicalFrame(()=>{battle.TickFrame(17);hit();});
            Assert.AreEqual(14,enemySide?target.AccumulatedPoiseDamage:player.AccumulatedDamage);
            Assert.AreEqual(1,enemySide?target.PoiseRecoveryRemainingFrames:player.PoiseRecoveryRemainingFrames);
            battle.TickFrame(17);Assert.AreEqual(0,enemySide?target.AccumulatedPoiseDamage:player.AccumulatedDamage);
        }

        [Test]
        public void 回復待ちの外部設定は不正値と保存混入を拒否し戦闘終了で解除する()
        {
            var map=DungeonConfig.CreateTestDungeon(6,30,100);var original=map.MapCells[5].Enemy;
            var target=new Enemy(original.EnemyId,original.Name,original.MaxHp,original.Element,original.GridPosition,poiseDefinition:new EnemyPoiseDefinition(30,6));
            map.MapCells[5]=new MapCell(5,target);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>target.ConfigurePoiseRecovery(0));
            target.ConfigurePoiseRecovery(6);
            Assert.Throws<System.ArgumentException>(()=>Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,new Deck()));
            var player=new PlayerCharacter(100);Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ConfigurePoiseRecovery(-1));player.ConfigurePoiseRecovery(6);
            var battle=new BattleSystem(player,new List<Enemy>{target},new Deck());player.TakeDamage(1000);
            Assert.IsNull(player.PoiseRecoveryDelayFrames);Assert.IsNull(target.PoiseRecoveryDelayFrames);
        }

        [Test]
        public void 無敵ガード防御と強靭度は確定した優先順で同フレーム処理する()
        {
            var target=new Enemy("priority","検証",100,ElementType.None,new Vector2Int(5,0),poiseDefinition:new EnemyPoiseDefinition(30,6));target.ApplyStun(60000);
            var battle=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{target},new Deck());
            target.Guard.Set(true,true);target.SetBarrier(10);target.SetInvincible(1);
            battle.RunLogicalFrame(()=>{target.TakeDamage(100,ElementType.None,AttackProperty.Break,30);battle.TickFrame(17);});
            Assert.AreEqual(10,target.BarrierHp);Assert.AreEqual(0,target.AccumulatedPoiseDamage);
            battle.RunLogicalFrame(()=>{target.TakeDamage(100,ElementType.None,AttackProperty.Sword,30);target.TakeDamage(6,ElementType.None,AttackProperty.Break,30);target.TakeDamage(6,ElementType.None,AttackProperty.Break,30);});
            Assert.AreEqual(100,target.CurrentHp);Assert.AreEqual(0,target.BarrierHp);Assert.IsTrue(target.Guard.IsActive);Assert.IsFalse(target.IsStaggered);
            battle.RunLogicalFrame(()=>target.TakeDamage(1,ElementType.None,AttackProperty.Break,30));
            Assert.AreEqual(99,target.CurrentHp);Assert.IsTrue(target.IsStaggered);Assert.IsFalse(target.Guard.IsActive);
        }

        [Test]
        public void 明示強靭度は独立削りを蓄積し最大値低下で残り1を保ち再成立で期間を戻す()
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, Vector2Int.Zero,
                poiseDefinition: new EnemyPoiseDefinition(100, 6));
            int notices = 0; target.OnStaggered += () => notices++;
            target.TakeDamage(1, ElementType.None, poiseDamage: 30);
            target.SetPoise(150); Assert.AreEqual(30, target.AccumulatedPoiseDamage);
            target.SetPoise(20); Assert.AreEqual(19, target.AccumulatedPoiseDamage); Assert.AreEqual(0, notices);
            target.TakeDamage(1, ElementType.None, poiseDamage: 0); Assert.AreEqual(19, target.AccumulatedPoiseDamage);
            target.TakeDamage(1, ElementType.None, poiseDamage: 1); Assert.AreEqual(1, notices);
            Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.AreEqual(6, target.StaggerRemainingFrames);
            for (int i = 0; i < 4; i++) target.TickStunFrame(); Assert.AreEqual(2, target.StaggerRemainingFrames);
            target.TakeDamage(1, ElementType.None, poiseDamage: 20); Assert.AreEqual(6, target.StaggerRemainingFrames);
            target.SetPoise(50); Assert.AreEqual(6, target.StaggerRemainingFrames); Assert.AreEqual(2, notices);
            for (int i = 0; i < 6; i++) target.TickStunFrame(); Assert.IsFalse(target.IsStunned);
        }

        [TestCase(50, 95, 7)][TestCase(-100, 80, 7)][TestCase(100, 100, 0)]
        public void 敵の完全耐性は削りを遮断し軽減増幅は独立値を変えない(int resistance, int hp, int impact)
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, Vector2Int.Zero,
                resistances: new ElementResistances(fire: resistance), poiseDefinition: new EnemyPoiseDefinition(30, 6));
            target.TakeDamage(10, ElementType.Fire, poiseDamage: 7);
            Assert.AreEqual(hp, target.CurrentHp); Assert.AreEqual(impact, target.AccumulatedPoiseDamage);
        }

        [Test]
        public void 強靭度未設定は旧動作を維持し不正定義と負の削りを副作用前に拒否する()
        {
            enemy.TakeDamage(1, ElementType.None, poiseDamage: int.MaxValue);
            Assert.IsNull(enemy.Poise); Assert.IsFalse(enemy.IsStaggered); Assert.AreEqual(0, enemy.AccumulatedPoiseDamage);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyPoiseDefinition(0, 6));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyPoiseDefinition(10, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => enemy.TakeDamage(1, ElementType.None, poiseDamage: -1));
            Assert.AreEqual(99, enemy.CurrentHp);
        }

        [TestCase(false)][TestCase(true)]
        public void 敵AIは明示のけぞり期間を全て停止し終了翌更新に行動する(bool logical)
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                poiseDefinition: new EnemyPoiseDefinition(10, 6));
            var player = new PlayerCharacter(100); var field = new BattleField();
            var ai = new EnemyAIBehavior(target, new List<IEnemyAIAction> { new BasicAttackAction(1, 0) });
            var context = new AIContext(target, player, field, new MockRandomProvider(), new MockLogger());
            target.TakeDamage(1, ElementType.None, poiseDamage: 10); Assert.IsFalse(ai.CanAct);
            if (logical) for (int i = 0; i < 6; i++) ai.Update(17, context, true);
            else ai.Update(100, context);
            Assert.AreEqual(100, player.CurrentHp); Assert.IsFalse(target.IsStaggered);
            ai.Update(17, context, logical); Assert.AreEqual(99, player.CurrentHp);
        }

        [Test]
        public void 敵の毒ダメージは強靭度を削らず通常フレームで進む()
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, new Vector2Int(5, 0),
                poiseDefinition: new EnemyPoiseDefinition(1, 6));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target }, new Deck(), randomProvider: new MockRandomProvider());
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int i = 0; i < 10; i++) battle.TickFrame(17);
            Assert.Less(target.CurrentHp, 100); Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.IsFalse(target.IsStaggered);
        }

        [TestCase(false)][TestCase(true)]
        public void ガードは前後の基本攻撃と独立削りを防ぎブレイク中断だけ発生元設定に従う(bool interruptible)
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var player = new PlayerCharacter(100);
            var battle = new BattleSystem(player, new List<Enemy> { target }, new Deck());
            target.Guard.Set(true, interruptible); int hits = 0; target.OnDamaged += (_, __) => hits++;
            foreach (int x in new[] { 2, 4 }) {
                player.MoveTo(new Vector2Int(x, 0)); battle.PlayerBasicAttack(target);
            }
            target.TakeDamage(1, ElementType.None, AttackProperty.Sword, int.MaxValue);
            Assert.AreEqual(100, target.CurrentHp); Assert.AreEqual(0, hits); Assert.AreEqual(0, target.AccumulatedPoiseDamage);
            battle.RunLogicalFrame(() => {
                target.TakeDamage(1, ElementType.None, AttackProperty.Break, 15);
                target.TakeDamage(1, ElementType.None, AttackProperty.Sword | AttackProperty.Break, 15);
                Assert.IsTrue(target.Guard.IsActive, "同フレームの中断判定前はガードを保つ");
            });
            Assert.AreEqual(98, target.CurrentHp); Assert.AreEqual(2, hits); Assert.IsTrue(target.IsStaggered);
            Assert.AreEqual(!interruptible, target.Guard.IsActive);
            player.TakeDamage(1000); Assert.IsFalse(target.Guard.IsActive, "戦闘終了で一時状態を解除する");
        }

        [Test]
        public void 中断行動のない敵はブレイクでガードを失わず毒と状態専用を受ける()
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { enemy }, new Deck());
            enemy.Guard.Set(true, false); int hits = 0; enemy.OnDamaged += (_, __) => hits++;
            enemy.TakeDamage(1, ElementType.None, AttackProperty.Break, int.MaxValue);
            Assert.IsTrue(enemy.Guard.IsActive); Assert.AreEqual(99, enemy.CurrentHp);
            enemy.TakeDamage(0, ElementType.Fire); Assert.AreEqual(2, hits);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int i = 0; i < 10; i++) battle.TickFrame(17);
            Assert.Less(enemy.CurrentHp, 99); Assert.IsTrue(enemy.Guard.IsActive);
        }

        [Test]
        public void 属性完全無効はブレイクでもガードと削りを変えず一時ガードを標準保存定義に混ぜない()
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, Vector2Int.Zero,
                resistances: new ElementResistances(fire: 100), poiseDefinition: new EnemyPoiseDefinition(30, 6));
            target.Guard.Set(true, true);
            target.TakeDamage(1, ElementType.Fire, AttackProperty.Break, int.MaxValue);
            Assert.AreEqual(100, target.CurrentHp); Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.IsTrue(target.Guard.IsActive);
            var map = DungeonConfig.CreateTestDungeon(6, 30, 100);
            map.MapCells[5].Enemy.Guard.Set(true, false);
            Assert.Throws<System.ArgumentException>(() => Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, new Deck()));
        }

        [TestCase(false)][TestCase(true)]
        public void 死亡確定時はHP通知より前にガードとのけぞりを解除する(bool logical)
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var survivor = new Enemy("survivor", "検証", 100, ElementType.None, new Vector2Int(5, 0));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target, survivor }, new Deck());
            target.Guard.Set(true, false);
            target.TakeDamage(1, ElementType.None, AttackProperty.Break, 30);
            Assert.IsTrue(target.Guard.IsActive); Assert.IsTrue(target.IsStaggered);
            int hpNotices = 0, deathNotices = 0;
            target.OnHpChanged += (_, __) => {
                hpNotices++;
                Assert.IsTrue(target.IsDead);
                Assert.IsFalse(target.Guard.IsActive);
                Assert.IsFalse(target.Guard.InterruptOnStagger);
                Assert.AreEqual(0, target.StaggerRemainingFrames);
            };
            target.OnDeath += () => {
                deathNotices++;
                Assert.IsFalse(target.Guard.IsActive); Assert.IsFalse(target.IsStaggered);
            };
            if (logical) battle.RunLogicalFrame(() => target.TakeDamage(200, ElementType.None, AttackProperty.Break, 0));
            else target.TakeDamage(200, ElementType.None, AttackProperty.Break, 0);
            Assert.IsFalse(battle.IsGameOver);
            Assert.AreEqual(1, hpNotices); Assert.AreEqual(1, deathNotices);
        }

        // ========== Enemy.ApplyStun / IsStunned / StunDurationMs ==========

        [Test]
        public void ApplyStun_スタン時間が設定される()
        {
            enemy.ApplyStun(3000);

            Assert.IsTrue(enemy.IsStunned, "スタン中はIsStunnedがtrue");
            Assert.AreEqual(3000, enemy.StunDurationMs);
        }

        [Test]
        public void ApplyStun_より長い時間を指定すると上書きされる()
        {
            enemy.ApplyStun(1000);
            enemy.ApplyStun(5000);

            Assert.AreEqual(5000, enemy.StunDurationMs, "長い方の時間に上書きされる");
        }

        [Test]
        public void ApplyStun_より短い時間を指定しても上書きされない()
        {
            enemy.ApplyStun(5000);
            enemy.ApplyStun(1000);

            Assert.AreEqual(5000, enemy.StunDurationMs, "長い方の時間を維持");
        }

        [Test]
        public void ApplyStun_0以下は無効()
        {
            enemy.ApplyStun(0);

            Assert.IsFalse(enemy.IsStunned, "0ms以下のスタンは付与されない");
        }

        [Test]
        public void IsStunned_初期状態はfalse()
        {
            Assert.IsFalse(enemy.IsStunned);
        }

        // ========== Enemy.TickStun ==========

        [Test]
        public void TickStun_時間経過でスタンが解除される()
        {
            enemy.ApplyStun(1000);

            enemy.TickStun(1000);

            Assert.IsFalse(enemy.IsStunned, "1000ms経過でスタン解除");
        }

        [Test]
        public void TickStun_残り時間より多く経過してもゼロ止まり()
        {
            enemy.ApplyStun(500);

            enemy.TickStun(2000);

            Assert.AreEqual(0, enemy.StunDurationMs, "負の値にならない");
            Assert.IsFalse(enemy.IsStunned);
        }

        [Test]
        public void TickStun_残り時間が残っていればスタン継続()
        {
            enemy.ApplyStun(3000);

            enemy.TickStun(1000);

            Assert.IsTrue(enemy.IsStunned, "まだスタン中");
            Assert.AreEqual(2000, enemy.StunDurationMs);
        }

        // ========== EnemyAIBehavior スタン中スキップ ==========

        [Test]
        public void EnemyAIBehavior_スタン中は行動をスキップする()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            int attackCount = 0;
            var action = new TestCountAction(() => attackCount++);
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            // スタンを付与
            enemy.ApplyStun(3000);

            // Act: スタン中にTick
            behavior.Update(1000, context);

            // Assert
            Assert.AreEqual(0, attackCount, "スタン中は行動しない");
        }

        [Test]
        public void EnemyAIBehavior_スタン中はTickStunが呼ばれる()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            var action = new TestCountAction(() => { });
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            enemy.ApplyStun(3000);

            // Act
            behavior.Update(1000, context);

            // Assert: 3000 - 1000 = 2000ms 残っているはず
            Assert.AreEqual(2000, enemy.StunDurationMs, "スタン時間がカウントダウンされている");
        }

        [Test]
        public void EnemyAIBehavior_スタン解除後に行動できる()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            int attackCount = 0;
            var action = new TestCountAction(() => attackCount++);
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            // 1秒スタン → 1秒経過で解除
            enemy.ApplyStun(1000);
            behavior.Update(1000, context);  // スタン消費
            Assert.IsFalse(enemy.IsStunned, "前提: スタン解除済み");

            // Act: スタン解除後にTick
            behavior.Update(100, context);

            // Assert
            Assert.AreEqual(1, attackCount, "スタン解除後は行動できる");
        }

        /// <summary>
        /// テスト用：呼び出し回数を記録するダミーAIアクション
        /// </summary>
        private class TestCountAction : IEnemyAIAction
        {
            private readonly System.Action onExecute;

            public TestCountAction(System.Action onExecute)
            {
                this.onExecute = onExecute;
            }

            public bool CanExecute(AIContext context) => true;
            public void Execute(AIContext context) => onExecute();
            public int GetCooldownMs() => 0;
            public float GetCooldown() => 0f;
        }
    }
}
