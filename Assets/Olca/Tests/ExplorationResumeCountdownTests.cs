using System;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using NUnit.Framework;
using Olca.Core.Logic.Integration;
namespace Olca.Tests
{
    public class ExplorationResumeCountdownTests
    {

        [TestCase(false)] [TestCase(true)] public void 試用ソードは空中とコスト不足を拒否して標的も手札も変更しない(bool air)
        {
            using(var s=Sword161())using(var view=s.AttachPresentation()){var card=s.Cards.SelectedCard;long rev=s.Cards.HandRevision;if(air)s.Core.TickFrame(0,true,true);else Assert.IsTrue(s.Core.CardGauge.TrySpend(100));long units=s.Core.CardGauge.CurrentUnits;int remaining=s.Cards.RemainingCount;Assert.IsFalse(s.TryUseSelectedCard(card,rev,out _));Assert.AreEqual(120,s.TrialTarget.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);Assert.AreEqual(remaining,s.Cards.RemainingCount);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);Assert.IsNull(s.TrialStrike);}
        }
        [Test] public void 試用ソードは未対応の命中無敵時間を無消費で拒否する()
        {
            using(var s=Sword161())using(var lease=s.AttachPresentation()){var prior=s.Cards.SelectedCard;var card=new Card("sword","ソード",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,30,"明示検証定義",hitInvincibilityMs:100);var deck=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().Select(c=>c.Id=="sword"?card.Clone():c).ToArray();var blocked=new OutsideExplorationSession();try{blocked.TryInitializePlayer(()=>new PlayerCharacter(100));Assert.IsTrue(blocked.TryInitializeCards(deck,Owned(deck),new SeededBattleRandom(161),out _));for(int n=0;n<100&&!blocked.Cards.Hand.Any(c=>c.Id=="sword");n++)blocked.Cards.Reload();blocked.Cards.TrySelect(blocked.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));using(var view=blocked.AttachPresentation()){long revision=blocked.Cards.HandRevision;long gauge=blocked.Core.CardGauge.CurrentUnits;Assert.IsFalse(blocked.TryUseSelectedCard(blocked.Cards.SelectedCard,revision,out _));Assert.AreEqual(revision,blocked.Cards.HandRevision);Assert.AreEqual(gauge,blocked.Core.CardGauge.CurrentUnits);Assert.AreEqual(120,blocked.TrialTarget.CurrentHp);Assert.IsFalse(blocked.TrialTarget.IsInvincible);}}finally{blocked.Dispose();}Assert.AreEqual(0,prior.HitInvincibilityMs);}
        }
        private static OutsideExplorationSession Sword161(int tenths=170,int facing=1,int startup=0,int recovery=0)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100));var source=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(startup,recovery)}}).CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(161),out _));for(int n=0;n<1000&&s.Cards.Hand.Count(c=>c.Id=="sword")<2;n++)s.Cards.Reload();Assert.GreaterOrEqual(s.Cards.Hand.Count(c=>c.Id=="sword"),2);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));long x=tenths*6000L+(facing<0?6000:0);while(s.Core.Position<x)s.Core.TickFrame(1);if(facing<0)s.Core.TickFrame(-1);Assert.AreEqual(tenths*6000L,s.Core.Position);return s;
        }
        [TestCase(164,1,false)] [TestCase(165,1,true)] [TestCase(170,1,true)] [TestCase(180,1,true)] [TestCase(185,1,false)] [TestCase(190,-1,true)] [TestCase(196,-1,false)] public void 試用ソードは向きと暫定一点範囲を判定し実カード威力を一度だけ与える(int x,int facing,bool hit)
        {
            using(var s=Sword161(x,facing))using(var view=s.AttachPresentation()){var card=s.Cards.SelectedCard;long rev=s.Cards.HandRevision;int count=0;s.TrialTarget.OnCombatFeedback+=e=>{if(e.Kind==CombatFeedbackKind.Damage)count++;};Assert.IsTrue(s.TryUseSelectedCard(card,rev,out _));Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit,s.TrialStrike.Hit);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.IsFalse(s.TryUseSelectedCard(card,rev,out _));for(int n=0;n<20;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit?1:0,count);Assert.AreEqual(100,s.Player.CurrentHp);}
        }
        [TestCase(0)] [TestCase(3)] [TestCase(6)] public void 試用ソードの前隙と命中済みは保存失敗再試行と冷起動で再命中しない(int at)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sword161-"+Guid.NewGuid().ToString("N"));try{using(var s=Sword161(startup:3,recovery:8)){var view=s.AttachPresentation();Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));for(int n=0;n<at;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}view.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;Assert.AreEqual(at<3?null:(int?)90,runtime.TrialTarget?.Hp);var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=actor,OutsideRuntimeSave=runtime,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var lease=cold.AttachPresentation()){for(int n=at;n<20;n++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();cold.ProcessCardUseInput(false,false,false,0,0,out _);}Assert.AreEqual(90,cold.TrialTarget.CurrentHp);}Assert.AreEqual(1,cold.CaptureRuntime(defs).TrialTarget.Hits);}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [Test] public void 試用ソードは既存六frame予約で二枚だけ発動し撃破後は報酬や自動保存を作らない()
        {
            using(var s=Sword161(startup:3,recovery:8))using(var lease=s.AttachPresentation()){s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));long rev=s.Cards.HandRevision;for(int n=1;n<=20;n++)Tick160(s,n==6);Assert.AreEqual(60,s.TrialTarget.CurrentHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [Test] public void 試用標的は撃破を保持し回復地点で再生するが新規保存へ強制追加しない()
        {
            using(var s=Sword161()){var view=s.AttachPresentation();var before=s.TrialTarget;Assert.AreEqual(120,before.CurrentHp);before.TakeDamage(120,ElementType.None);view.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);Assert.AreEqual(0,runtime.TrialTarget.Hp);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,runtime,null,defs);Assert.IsTrue(cold.TrialTarget.IsDead);}var recovery=OutsideTrialRoute.RecoveryPointId; s.RecoverOutside(recovery);Assert.AreEqual(120,s.TrialTarget.CurrentHp);Assert.AreNotSame(before,s.TrialTarget);Assert.IsNull(s.CaptureRuntime(defs).TrialTarget);}
        }
        [TestCase(-1)] [TestCase(121)] public void 不正な試用標的保存は旧個体とHPを変更せず拒否する(int hp)
        {
            using(var s=Sword161()){var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);runtime.TrialTarget=new OutsideTrialTargetData{Hp=hp};var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var before=s.TrialTarget;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(before,s.TrialTarget);Assert.AreEqual(120,before.CurrentHp);}
        }
        [TestCase(2)] [TestCase(6)] public void 探索の予約入力は保存せず失敗再試行と冷起動で二枚目を重複消費しない(int at)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"buffer160-"+Guid.NewGuid().ToString("N"));try{using(var s=Buffer160()){var lease=s.AttachPresentation();Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=at;n++)Tick160(s,n==at);Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);lease.Dispose();Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsideRuntimeSave=runtime,OutsidePlayerSave=actor,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var view=cold.AttachPresentation()){Assert.AreEqual(AttackReservationKind.None,cold.PendingAttack);for(int n=at;n<20;n++)Tick160(cold);Assert.AreEqual(30,cold.Player.CurrentHp);Assert.AreEqual(rev,cold.Cards.HandRevision);}}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索の後隙キャンセルで次カード消費を拒否しても旧後隙を消さない(bool pending)
        {
            using(var s=Buffer160(2))using(var view=s.AttachPresentation()){Start160(s);typeof(OutsideExplorationCards).GetProperty("HandRevision").SetValue(s.Cards,long.MaxValue);for(int n=1;n<=5;n++)Tick160(s,pending?n==1:n==5);Assert.IsTrue(s.IsAttacking);Assert.AreEqual(0,s.AttackState.StartedAt);Assert.AreEqual(8,s.AttackState.RecoveryFrames);Assert.AreEqual(long.MaxValue,s.Cards.HandRevision);for(int n=6;n<11;n++){Tick160(s);Assert.IsTrue(s.IsAttacking);}Tick160(s);Assert.IsFalse(s.IsAttacking);Assert.AreEqual(30,s.Player.CurrentHp);}
        }
        private static OutsideExplorationSession Buffer160(int? cancel=null)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100,staggerDurationFrames:2));var source=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"heal",new PlayerAttackTiming(3,8,cancel)}}).CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(160),out _));for(int n=0;n<1000&&s.Cards.Hand.Count(c=>c.Id=="heal")<2;n++)s.Cards.Reload();Assert.GreaterOrEqual(s.Cards.Hand.Count(c=>c.Id=="heal"),2);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));s.Player.SetHp(10);return s;
        }
        private static void Start160(OutsideExplorationSession s){Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));}
        private static void Tick160(OutsideExplorationSession s,bool press=false,bool selection=false,bool basic=false,long cardAt=0,long basicAt=0){s.Core.TickFrame(0);s.AdvancePlayerFrame();s.ProcessCardUseInput(press,selection,basic,cardAt,basicAt,out _);}
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] public void 探索カード予約は六フレーム目の開始で失効し予約時に消費しない(int at)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=15;n++){Tick160(s,n==at);if(n==at)Assert.AreEqual(rev,s.Cards.HandRevision);}Assert.AreEqual(at>=6?50:30,s.Player.CurrentHp);Assert.AreEqual(rev+(at>=6?1:0),s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索カード予約の押し直しだけが期限を更新し同frameの多重呼出は延長しない(bool refresh)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=15;n++){Tick160(s,n==5||refresh&&n==8);if(n==5)for(int i=0;i<10;i++)s.ProcessCardUseInput(true,false,false,0,0,out _);}Assert.AreEqual(refresh?50:30,s.Player.CurrentHp);Assert.AreEqual(rev+(refresh?1:0),s.Cards.HandRevision);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索カード予約は入力時と発動時の不足コストを拒否して後で自動復活しない(bool atExecution)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=20;n++){if(n==(atExecution?11:6))Assert.IsTrue(s.Core.CardGauge.TrySpend((int)(s.Core.CardGauge.CurrentUnits/60)));Tick160(s,n==6);}Assert.AreEqual(30,s.Player.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] public void 探索カード予約は選択と場面身体状態の変化で取り消す(int mode)
        {
            using(var s=Buffer160()){var view=s.AttachPresentation();try{Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=6;n++)Tick160(s,n==6);Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);
                if(mode==0){s.Core.TickFrame(0);s.AdvancePlayerFrame();s.ProcessCardUseInput(false,true,false,0,0,out _);}if(mode==1){int facing=s.Core.Facing;bool grounded=s.Core.IsGrounded;s.Core.TickFrame(-1);s.ObserveAttackBoundary(facing,grounded);}if(mode==2){int facing=s.Core.Facing;bool grounded=s.Core.IsGrounded;s.Core.TickFrame(0,true,true);s.ObserveAttackBoundary(facing,grounded);}if(mode==3){s.Player.TakeDamage(0,forceStagger:true);Tick160(s);}if(mode==4){s.Player.ApplyParalysis(2);Tick160(s);}if(mode==5){view.Dispose();view=s.AttachPresentation();}if(mode==6)s.CancelAttackReservation();Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);for(int n=0;n<20;n++)Tick160(s);Assert.AreEqual(30,s.Player.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);
            }finally{view.Dispose();}}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] public void 探索の同frame優先は選択が最優先で前後不明はカードを優先し未実装キックを実行しない(int mode)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);for(int n=1;n<6;n++)Tick160(s);Tick160(s,true,mode==3,true,mode==0?10:mode==1?20:0,mode==0?20:mode==1?10:0);Assert.AreEqual(mode==0||mode==3?AttackReservationKind.None:AttackReservationKind.Card,s.PendingAttack);for(int n=7;n<=15;n++)Tick160(s);Assert.AreEqual(mode==0||mode==3?30:50,s.Player.CurrentHp);}
        }
        [TestCase(0)] [TestCase(2)] [TestCase(8)] public void 探索カード予約は既存の後隙キャンセル開始か終了の最速で発動する(int cancel)
        {
            using(var s=Buffer160(cancel))using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;Tick160(s,true);long expected=3+cancel;for(int n=2;n<=Math.Max(11,expected);n++){Tick160(s);if(n<expected)Assert.AreEqual(rev,s.Cards.HandRevision);}Assert.AreEqual(rev+(expected<7?1:0),s.Cards.HandRevision);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] public void 探索の開始済ヒールは前隙と発動済を保存し復元後一度だけ適用する(int saveAt)
        {
            using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);var card=s.Cards.SelectedCard;Assert.IsTrue(s.TryUseSelectedHealingCard(card,s.Cards.HandRevision,out _));Assert.AreEqual(50,s.Player.CurrentHp);Assert.IsFalse(s.CanPerformAttack);
                for(int i=0;i<saveAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;
                var bytes=PortableCheckpointJson.Serialize(runtime);var loaded=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);using(var restored=new OutsideExplorationSession()){Assert.IsTrue(restored.RestoreSavedState(player,loaded,null,defs));using(var view=restored.AttachPresentation()){Assert.IsFalse(restored.AdvancePlayerFrame());Assert.AreEqual(saveAt<3?50:70,restored.Player.CurrentHp);for(int n=saveAt;n<7;n++){restored.Core.TickFrame(0);restored.AdvancePlayerFrame();}Assert.AreEqual(70,restored.Player.CurrentHp);Assert.IsNull(restored.AttackState);for(int n=0;n<8;n++){restored.Core.TickFrame(0);restored.AdvancePlayerFrame();}Assert.AreEqual(70,restored.Player.CurrentHp);}}}
        }
        [TestCase(0)] [TestCase(2)] [TestCase(3)] [TestCase(4)] public void 探索ヒールは開始後の中断で消費を戻さず発動後の効果を消さない(int interruptAt)
        {
            using(var s=Heal158(3,4))using(var view=s.AttachPresentation()){s.Player.SetHp(50);long rev=s.Cards.HandRevision;Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,rev,out _));for(int i=0;i<interruptAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}s.Player.TakeDamage(0,forceStagger:true);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsNull(s.AttackState);Assert.AreEqual(interruptAt<3?50:70,s.Player.CurrentHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);for(int i=0;i<10;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.AreEqual(interruptAt<3?50:70,s.Player.CurrentHp);}
        }
        private sealed class Action159Io:Olca.Unity.CheckpointFileIo{public bool Fail=true;public override void WriteAndFlush(string path,byte[] bytes){if(Fail)throw new System.IO.IOException("action159 explicit fault");base.WriteAndFlush(path,bytes);}}
        [TestCase(2)] [TestCase(4)] public void 探索行動の保存失敗再試行は前隙と発動済を同じ計画で復元し二重適用しない(int saveAt)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"action159-"+Guid.NewGuid().ToString("N"));try{using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));for(int i=0;i<saveAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var p=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsideRuntimeSave=runtime,OutsidePlayerSave=actor,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,p,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));Assert.AreEqual(saveAt<3?50:70,s.Player.CurrentHp);io.Fail=false;Assert.IsTrue(store.TryWrite(0,p,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,p,out error),error);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);Assert.IsNotNull(loaded,error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var view=cold.AttachPresentation()){for(int i=saveAt;i<10;i++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();}Assert.AreEqual(70,cold.Player.CurrentHp);Assert.IsNull(cold.AttackState);}}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索行動の不正な時間と発動済改変は旧Sessionを変更せず拒否する(bool applied)
        {
            using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _);lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;if(applied)runtime.Attack.EffectApplied=true;else runtime.Attack.StartupFrames++;using(var target=new OutsideExplorationSession()){target.TryInitializePlayer(()=>new PlayerCharacter(123));var before=target.Player;Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(before,target.Player);Assert.AreEqual(123,target.Player.CurrentHp);Assert.AreEqual(0,target.Core.Frame);Assert.IsNull(target.Cards);}}
        }
        private static OutsideExplorationSession Heal158(int timing=0,int recovery=0)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100,staggerDurationFrames:2));var f=timing==0&&recovery==0?new Olca.Application.Battle.SimpleCardFactory():new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"heal",new PlayerAttackTiming(timing,recovery)}});var source=f.CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(158),out _));for(int n=0;n<100&&!s.Cards.Hand.Any(c=>c.Id=="heal");n++)s.Cards.Reload();Assert.IsTrue(s.Cards.Hand.Any(c=>c.Id=="heal"));s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));return s;
        }
        [TestCase(50,70)] [TestCase(100,100)] public void 外界ヒールは既存二十回復と同じコストを開始時に一度だけ消費する(int hp,int expected)
        {
            using(var s=Heal158())using(var lease=s.AttachPresentation()){s.Player.SetHp(hp);var c=s.Cards;var selected=c.SelectedCard;var old=c.Hand.ToArray();long rev=c.HandRevision;Assert.IsTrue(s.TryUseSelectedHealingCard(selected,rev,out var error),error);Assert.AreEqual(expected,s.Player.CurrentHp);Assert.AreEqual(6000-selected.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(rev+1,c.HandRevision);Assert.IsFalse(c.Hand.Contains(selected));CollectionAssert.AreEqual(old.Where(x=>!ReferenceEquals(x,selected)).ToArray(),c.Hand.Take(2));Assert.IsFalse(s.TryUseSelectedHealingCard(c.SelectedCard,c.HandRevision,out _));Assert.AreEqual(rev+1,c.HandRevision);Assert.AreEqual(0,s.Core.Frame);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(8)] [TestCase(9)]
        public void 外界ヒールの不許可はHP手札ゲージと乱数を変更せず予約を作らない(int mode)
        {
            using(var s=Heal158(mode==7?1:0)){var lease=s.AttachPresentation();try{s.Player.SetHp(50);var c=s.Cards;var expected=mode==0?c.SelectedCard.Clone():c.SelectedCard;long rev=c.HandRevision+(mode==1?1:0);if(mode==2)s.Core.CardGauge.TrySpend(100);if(mode==3){s.Player.ApplyParalysis(2);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.Player.IsParalyzed);}if(mode==4)s.Player.TakeDamage(0,forceStagger:true);if(mode==5){int i=c.Hand.ToList().FindIndex(x=>x.Id!="heal");Assert.GreaterOrEqual(i,0);c.TrySelect(i);expected=c.SelectedCard;}if(mode==6)s.Core.TickFrame(0,true,true);if(mode==8)lease.Dispose();if(mode==9){var data=Deck155();Assert.IsTrue(s.QueueSelectedDeck("edit158",data,Owned(data),new SeededBattleRandom(158),out _));}
                var hand=c.Hand;long units=s.Core.CardGauge.CurrentUnits;var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(c);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var random=PortableCheckpointJson.Serialize(c.CaptureState(defs));Assert.IsFalse(s.TryUseSelectedHealingCard(expected,rev,out _));Assert.AreSame(hand,c.Hand);Assert.AreEqual(50,s.Player.CurrentHp);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);CollectionAssert.AreEqual(random,PortableCheckpointJson.Serialize(c.CaptureState(defs)));Assert.AreEqual(rev-(mode==1?1:0),c.HandRevision);
            }finally{lease.Dispose();}}
        }

        [TestCase(false)] [TestCase(true)] public void 枯渇した空の使用枠からは前後の実在手札へ巡回する(bool previous)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("cycle157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;c.TrySelect(2);for(int i=0;i<28;i++)Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));Assert.AreEqual(2,c.Hand.Count);Assert.AreEqual(2,c.SelectedIndex);Assert.IsNull(c.SelectedCard);var repeat=new OutsideSelectionRepeat();repeat.Queue(0,false,false,false,false,false,false,0,0);repeat.Queue(1,previous,!previous,previous,!previous,false,true,0,0);repeat.AdvanceFrame(c);Assert.AreEqual(previous?1:0,c.SelectedIndex);}
        }
        [Test] public void 未知乱数の消費は未変更で拒否して保存可能な専用乱数の境界を維持する()
        {
            var data=CardFixture();Assert.IsTrue(OutsideExplorationCards.TryCreate(data,Owned(data),new Olca.Tests.Mocks.MockRandomProvider(),out var c,out _));var hand=c.Hand;var gauge=OutsideCardGauge.ValidationDefault;Assert.IsFalse(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,gauge,out _));Assert.AreSame(hand,c.Hand);Assert.AreEqual(0,c.HandRevision);Assert.AreEqual(100*60,gauge.CurrentUnits);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 探索一枚使用は選択個体だけを左詰め末尾補充し使用枠と定義を保つ(int slot)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("use157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var cards=s.Cards;cards.TrySelect(slot);var hand=cards.Hand.ToArray();long revision=cards.HandRevision,units=s.Core.CardGauge.CurrentUnits;var expected=cards.SelectedCard;Assert.IsTrue(cards.TryConsumeSelected(expected,revision,s.Core.CardGauge,out var used));Assert.AreSame(expected,used);Assert.AreEqual(slot,cards.SelectedIndex);Assert.AreEqual(revision+1,cards.HandRevision);Assert.AreEqual(3,cards.Hand.Count);Assert.AreEqual(26,cards.RemainingCount);CollectionAssert.AreEqual(hand.Where(c=>!ReferenceEquals(c,expected)).ToArray(),cards.Hand.Take(2));Assert.IsFalse(cards.Hand.Contains(expected));Assert.AreEqual(units-expected.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(30,cards.TotalCount);}
        }
        [Test] public void 探索一枚使用は三十回で枯渇し自動循環せず空の使用枠と再装填を保つ()
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("empty157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;var used=new System.Collections.Generic.List<Card>();c.TrySelect(2);for(int i=0;i<30;i++){if(c.SelectedCard==null)c.TrySelect(c.Hand.Count-1);var expected=c.SelectedCard;int slot=c.SelectedIndex;Assert.IsTrue(c.TryConsumeSelected(expected,c.HandRevision,s.Core.CardGauge,out var taken));used.Add(taken);Assert.AreEqual(slot,c.SelectedIndex);Assert.AreEqual(Math.Min(3,29-i),c.Hand.Count);Assert.AreEqual(Math.Max(0,26-i),c.RemainingCount);}Assert.AreEqual(30,used.Distinct().Count());Assert.IsNull(c.SelectedCard);long units=s.Core.CardGauge.CurrentUnits,rev=c.HandRevision;Assert.IsFalse(c.TryConsumeSelected(null,rev,s.Core.CardGauge,out _));var repeat=new OutsideSelectionRepeat();repeat.Queue(0,false,false,false,false,false,false,0,0);repeat.Queue(1,false,true,false,true,false,true,0,0);repeat.AdvanceFrame(c);Assert.AreEqual(0,c.SelectedIndex);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(rev,c.HandRevision);c.Reload();Assert.AreEqual(3,c.Hand.Count);Assert.AreEqual(27,c.RemainingCount);Assert.AreEqual(0,c.SelectedIndex);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 探索一枚使用の拒否は乱数履歴ゲージ手札世代を変えない(int mode)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("reject157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;var definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var expected=mode==0?c.SelectedCard.Clone():c.SelectedCard;long revision=c.HandRevision-(mode==1?1:0);if(mode==2)Assert.IsTrue(s.Core.CardGauge.TrySpend(100));var before=PortableCheckpointJson.Serialize(c.CaptureState(definitions));long gauge=s.Core.CardGauge.CurrentUnits;Assert.IsFalse(c.TryConsumeSelected(expected,revision,s.Core.CardGauge,out _));CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(c.CaptureState(definitions)));Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(10)]
        public void 探索使用後の手札は枯渇までcold復元し次抽選と使用済みを維持する(int count)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("cold157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;c.TrySelect(2);for(int i=0;i<30-count;i++){if(c.SelectedCard==null)c.TrySelect(c.Hand.Count-1);Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));}var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var state=c.CaptureState(defs);var restored=OutsideExplorationCards.RestoreState(state,defs);Assert.AreEqual(state.Key(),restored.CaptureState(defs).Key());Assert.AreEqual(Math.Min(3,count),restored.Hand.Count);if(count>0){c.TrySelect(0);restored.TrySelect(0);Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));var gauge=OutsideCardGauge.ValidationDefault;Assert.IsTrue(restored.TryConsumeSelected(restored.SelectedCard,restored.HandRevision,gauge,out _));Assert.AreEqual(c.CaptureState(defs).Key(),restored.CaptureState(defs).Key());}else Assert.IsNull(restored.SelectedCard);}
        }

        [TestCase(9,false)] [TestCase(9.5,true)] [TestCase(10,true)] [TestCase(10.5,true)] [TestCase(11,false)] [TestCase(12,false)] [TestCase(6,false)]
        public void 試用カード箱は半径と接地を守り既存操作地点と競合しない(double x,bool expected)
        {
            using(var s=new OutsideExplorationSession()){Walk155(s,(long)(x*60000));Assert.AreEqual(expected,OutsideTrialRoute.CanClaimCardSupply(s.Core));if(expected){Assert.IsFalse(OutsideTrialRoute.CanRecover(s.Core));Assert.IsFalse(OutsideTrialRoute.CanClaimSupply(s.Core));Assert.IsFalse(OutsideTrialRoute.CanTryPoison(s.Core));s.Core.TickFrame(0,true,true);Assert.IsFalse(OutsideTrialRoute.CanClaimCardSupply(s.Core));}}
        }

        private static Card[] Deck155()=>Enumerable.Range(0,30).Select(i=>new Card("selected155-"+i,"fixture",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,0,"")).ToArray();
        private static void Walk155(OutsideExplorationSession s,long target){for(int i=0;i<400&&s.Core.Position!=target;i++){s.Core.TickFrame(Math.Sign(target-s.Core.Position));s.AdvanceDeckLocation();}Assert.AreEqual(target,s.Core.Position);}
        [Test] public void 選択30枚は保存準備だけで抽選せず地点退出で一度だけ適用する()
        {
            var data=Deck155();var rng=new SeededBattleRandom(155);using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));Assert.IsTrue(s.IsDeckEditingLocation);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);using(var lease=s.AttachPresentation()){for(int i=0;i<4;i++)Assert.IsFalse(s.AdvanceDeckLocation());}Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));Assert.AreEqual(0,rng.Draws);Walk155(s,10*60000);var cards=s.Cards;Assert.AreEqual(30,cards.TotalCount);Assert.AreEqual(27,cards.RemainingCount);Assert.AreEqual(0,cards.SelectedIndex);long draws=rng.Draws;Assert.Greater(draws,0);Assert.IsFalse(s.AdvanceDeckLocation());using(var lease=s.AttachPresentation())Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreSame(cards,s.Cards);Assert.AreEqual(draws,rng.Draws);
                Walk155(s,8*60000);Assert.IsTrue(s.IsDeckEditingLocation);Assert.IsTrue(s.QueueSelectedDeck("one",data.Reverse().ToArray(),Owned(data),rng,out _));Assert.AreSame(cards,s.Cards);Assert.AreEqual(draws,rng.Draws);Walk155(s,10*60000);Assert.AreNotSame(cards,s.Cards);Assert.AreEqual(30,s.Cards.TotalCount);Assert.Greater(rng.Draws,draws);}
        }
        [Test] public void 地点内のジャンプや無効選択では再装填も乱数進行もない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(156);using(var s=new OutsideExplorationSession()){Assert.IsFalse(s.QueueSelectedDeck("bad",data.Take(20).ToArray(),Owned(data),rng,out _));Assert.IsFalse(s.IsDeckEditingLocation);Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));for(int i=0;i<65;i++){s.Core.TickFrame(0,i==0,true);Assert.IsFalse(s.AdvanceDeckLocation());}Assert.IsTrue(s.Core.IsGrounded);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);Assert.IsTrue(OutsideTrialRoute.CanEditDeck(s.Core));Walk155(s,10*60000);Assert.IsFalse(OutsideTrialRoute.CanEditDeck(s.Core));}
        }
        [Test] public void 三十枚Runtimeはcold復元と通常再入で個体順序と乱数を保持する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var s=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));s.QueueSelectedDeck("one",data,Owned(data),new SeededBattleRandom(157),out _);Walk155(s,10*60000);s.Cards.TrySelect(2);var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;Assert.AreEqual(30,runtime.Cards.Cards.Length);Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));var before=cold.Cards.CaptureState(defs).Key();Assert.IsTrue(cold.QueueSelectedDeck("one",data,Owned(data),new SeededBattleRandom(999),out _));Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(before,cold.Cards.CaptureState(defs).Key());using(var lease=cold.AttachPresentation())Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(before,cold.Cards.CaptureState(defs).Key());}
        }
        [Test] public void 回復操作と地点退出は別々に一回ずつ再装填し再表示では増えない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(158);using(var s=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));s.QueueSelectedDeck("one",data,Owned(data),rng,out _);Walk155(s,10*60000);Walk155(s,6*60000);long before=rng.Draws;s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.Greater(rng.Draws,before);long recovered=rng.Draws;using(var lease=s.AttachPresentation())s.AdvanceDeckLocation();Assert.AreEqual(recovered,rng.Draws);Walk155(s,10*60000);Assert.Greater(rng.Draws,recovered);long exited=rng.Draws;Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreEqual(exited,rng.Draws);}
        }
        [Test] public void pending選択の前にRuntimeが復元されても最新乱数を使いwarmとcoldで一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var recovered=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(159),out _);Walk155(warm,10*60000);var original=warm.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=original.Id;recovered.RestoreSavedState(actor,original,null,defs);warm.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(160),out _);recovered.RecoverOutside(OutsideTrialRoute.RecoveryPointId);var restored=recovered.CaptureRuntime(defs);var next=OutsidePlayerSaveData.Capture(recovered.Player);next.Id=restored.Id;warm.RestoreSavedState(next,restored,OutsideTrialRoute.RecoveryPointId,defs);cold.RestoreSavedState(next,restored,OutsideTrialRoute.RecoveryPointId,defs);cold.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(999),out _);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [Test] public void 同じ選択の回復保存から再開してもwarmとcoldの退出抽選が一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));var rng=new SeededBattleRandom(160);warm.QueueSelectedDeck("same",data,Owned(data),rng,out _);Walk155(warm,10*60000);Assert.AreEqual(32,rng.Draws,"新planも全30枚Shuffle29回とDraw3回");Walk155(warm,6*60000);warm.RecoverOutside(OutsideTrialRoute.RecoveryPointId);var runtime=warm.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=runtime.Id;var bytes=PortableCheckpointJson.Serialize(runtime);var decoded=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);Assert.AreEqual(runtime.Key(),decoded.Key());cold.RestoreSavedState(actor,decoded,OutsideTrialRoute.RecoveryPointId,defs);warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(888),out _);cold.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(999),out _);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [TestCase("bad")] [TestCase("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
        public void 三十枚の選択識別が不正なら復元せず既存状態を保持する(string key)
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var s=new OutsideExplorationSession()){s.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(161),out _);Walk155(s,10*60000);var value=s.Cards.CaptureState(defs);var before=value.Key();value.SourceKey=key;Assert.Throws<ArgumentException>(()=>OutsideExplorationCards.RestoreState(value,defs));Assert.AreEqual(before,s.Cards.CaptureState(defs).Key());}
        }
        [Test] public void A適用後にBを復元してもA再選択のpendingを作りwarmとcoldの退出で一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var replacement=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(162),out _);Walk155(warm,10*60000);replacement.TryInitializePlayer(()=>new PlayerCharacter(100));replacement.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(163),out _);Walk155(replacement,10*60000);var runtime=replacement.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(replacement.Player);actor.Id=runtime.Id;warm.RestoreSavedState(actor,runtime,null,defs);cold.RestoreSavedState(actor,runtime,null,defs);warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(164),out _);cold.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(165),out _);Walk155(warm,8*60000);Walk155(cold,8*60000);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [Test] public void 未選択を読み戻した場合は未適用planを破棄し自動登録しない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(166);using(var s=new OutsideExplorationSession()){s.QueueSelectedDeck("a",data,Owned(data),rng,out _);s.QueueSelectedDeck(null,null,null,null,out _);Walk155(s,10*60000);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);}
        }
        [Test] public void 地点内から地点外Runtimeへ復元しても歩行退出と誤認して再抽選しない()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var source=new OutsideExplorationSession())using(var warm=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                source.TryInitializePlayer(()=>new PlayerCharacter(100));source.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(167),out _);Walk155(source,10*60000);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(168),out _);Assert.IsTrue(warm.IsDeckEditingLocation);warm.RestoreSavedState(actor,runtime,null,defs);cold.RestoreSavedState(actor,runtime,null,defs);warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(169),out _);cold.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(170),out _);string key=source.Cards.CaptureState(defs).Key();warm.Core.TickFrame(0);cold.Core.TickFrame(0);Assert.IsFalse(warm.AdvanceDeckLocation());Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(key,warm.Cards.CaptureState(defs).Key());Assert.AreEqual(key,cold.Cards.CaptureState(defs).Key());
            }
        }
        private static OutsideExplorationSession RuntimeSession141(bool cards,int pose,out CheckpointDefinitions definitions)
        {
            var source=CardFixture();definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var s=new OutsideExplorationSession();s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(17*60000));var actor=new PlayerCharacter(100);actor.SetHp(37);actor.ApplyPoison("runtime141",new StatusEffect(StatusEffectType.Poison,60000,1),600);s.TryInitializePlayer(()=>actor);if(cards)Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(141),out _));s.Core.CardGauge.Configure(140,7);s.Core.CardGauge.TrySpend(70);
            using(var lease=s.AttachPresentation()){
                Action<float,bool,bool,bool,bool> tick=(x,j,h,d,dh)=>{s.Core.TickFrame(x,j,h,d,dh);s.AdvancePlayerFrame();};
                if(pose==0){for(int i=0;i<7;i++)tick(1,false,false,false,false);}
                else if(pose==1)tick(1,true,true,true,true);
                else if(pose==2||pose==3){for(int i=0;i<400&&s.Core.Position<24*60000-31200;i++)tick(1,false,false,false,false);tick(1,true,true,false,false);for(int i=0;i<100&&!s.Core.IsWallSliding;i++)tick(1,false,true,false,false);Assert.IsTrue(s.Core.IsWallSliding);if(pose==3){tick(1,false,false,false,false);tick(1,true,true,false,false);Assert.IsTrue(s.Core.IsWallKicking);}}
                else if(pose==4){tick(0,false,false,true,true);while(s.Core.IsDashing)tick(0,false,false,false,false);tick(0,false,false,true,true);while(s.Core.IsDashing)tick(0,false,false,false,false);while(s.Core.DashCooldownRemaining>1)tick(0,false,false,false,false);Assert.AreEqual(1,s.Core.DashCooldownRemaining);}
                else {tick(0,false,false,true,true);while(s.Core.DashFrame<OutsideTraversalProfile.ValidationWall(true).DashFrames-1)tick(0,false,false,false,true);Assert.IsTrue(s.Core.IsDashing);}
            }
            if(cards)s.Cards.TrySelect(2);return s;
        }
        private static string CoreKey141(OutsideExplorationCore core)=>PortableCheckpointJson.Hash(PortableCheckpointJson.Serialize(core.CaptureState()));
        [TestCase(false,0)][TestCase(true,0)][TestCase(false,1)][TestCase(true,1)][TestCase(false,2)][TestCase(true,3)][TestCase(false,4)][TestCase(true,5)]
        public void 外界の退避は位置時計空中ダッシュゲージとカードを抽選せず復元し同じIDを再適用しない(bool cards,int pose)
        {
            using(var source=RuntimeSession141(cards,pose,out var defs))using(var cold=new OutsideExplorationSession()){
                var runtime=source.CaptureRuntime(defs);runtime=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime));var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;var key=runtime.Key();var core=source.Core;var oldCards=source.Cards;var frame=core.Frame;long units=core.CardGauge.CurrentUnits;var cardKey=runtime.Cards?.Key();core.TickFrame(-1);source.Player.SetHp(1);
                Assert.IsTrue(source.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(core,source.Core);Assert.AreSame(oldCards,source.Cards);Assert.AreEqual(frame,core.Frame);Assert.AreEqual(units,core.CardGauge.CurrentUnits);Assert.AreEqual(37,source.Player.CurrentHp);Assert.AreEqual(cardKey,source.Cards?.CaptureState(defs).Key());Assert.AreEqual(key,runtime.Key());
                cold.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));Assert.AreEqual(CoreKey141(source.Core),CoreKey141(cold.Core));Assert.AreEqual(source.Camera.FixedCenter,cold.Camera.FixedCenter);Assert.AreEqual(cardKey,cold.Cards?.CaptureState(defs).Key());if(cards){Assert.AreEqual(17,cold.Cards.RemainingCount);Assert.AreEqual(2,cold.Cards.SelectedIndex);Assert.AreNotSame(source.Cards.Hand[0],cold.Cards.Hand[0]);}else Assert.IsNull(cold.Cards);
                using(var a=source.AttachPresentation())using(var b=cold.AttachPresentation()){Assert.IsFalse(cold.AdvancePlayerFrame());Assert.IsFalse(cold.RestoreSavedState(actor,runtime,null,defs));for(int i=0;i<20;i++){source.Core.TickFrame(0);cold.Core.TickFrame(0);Assert.IsTrue(source.AdvancePlayerFrame());Assert.IsTrue(cold.AdvancePlayerFrame());Assert.IsFalse(cold.AdvancePlayerFrame());Assert.AreEqual(CoreKey141(source.Core),CoreKey141(cold.Core));}Assert.AreEqual(frame+20,cold.Core.Frame);Assert.IsFalse(source.RestoreSavedState(actor,runtime,null,defs));}
                var changed=runtime.Copy();changed.Core.GaugeUnits--;Assert.Throws<ArgumentException>(()=>source.RestoreSavedState(actor,changed,null,defs));Assert.AreEqual(frame+20,core.Frame);
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)][TestCase(9)][TestCase(10)][TestCase(11)][TestCase(12)][TestCase(13)][TestCase(14)]
        public void 不正な外界退避はActorだけでなく全Sessionを変更する前に拒否する(int invalid)
        {
            using(var source=RuntimeSession141(true,0,out var defs))using(var target=new OutsideExplorationSession()){
                target.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));target.TryInitializePlayer(()=>new PlayerCharacter(120));target.TryConfigureCamera(OutsideCameraPolicy.FixedAt(60000));target.Core.CardGauge.TrySpend(7);var old=target.Player;var coreKey=CoreKey141(target.Core);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;
                if(invalid==0)runtime.Version=2;else if(invalid==1)actor.Id=Guid.NewGuid().ToString("N");else if(invalid==2)runtime.Core.DefinitionKey=new string('0',64);else if(invalid==3)runtime.Core.GaugeUnits=long.MaxValue;else if(invalid==4)runtime.Core.Height=long.MaxValue;else if(invalid==5)runtime.Core.SupportId="missing";else if(invalid==6)runtime.Core.Frame=long.MaxValue;else if(invalid==7)runtime.Cards.Hand[1]=runtime.Cards.Hand[0];else if(invalid==8)runtime.Cards.Random.State=0;else if(invalid==9)runtime.Cards.Cards[0].ValueHash=new string('0',64);else if(invalid==10)runtime.FixedCameraCenter=long.MaxValue;else if(invalid==11)runtime.Core.Afterimages=new[]{new OutsideAfterimageStateData{Facing=1,Frame=runtime.Core.Frame,Pose=(OutsideAfterimagePose)99}};else if(invalid==12){runtime.Core.Height=10000;runtime.Core.Velocity=1000;runtime.Core.SupportId=null;runtime.Core.DashJumping=true;runtime.Core.DashStartedAt=-1;}else if(invalid==13){runtime.Core.Height=10000;runtime.Core.Velocity=-400;runtime.Core.SupportId=null;runtime.Core.WallSliding=true;runtime.Core.WallContact=1;}else {runtime.Core.DashStartedAt=1;runtime.Core.DashEndsAt=13;runtime.Core.CooldownUntil=8;runtime.Core.ConsecutiveDashes=2;runtime.Core.LastDashAt=runtime.Core.Frame;}
                if(invalid==10)Assert.Throws<ArgumentOutOfRangeException>(()=>target.RestoreSavedState(actor,runtime,null,defs));else Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(old,target.Player);Assert.AreEqual(coreKey,CoreKey141(target.Core));Assert.AreEqual(60000,target.Camera.FixedCenter);Assert.IsNull(target.Cards);Assert.IsFalse(target.IsPresented);
            }
        }
        [Test] public void 外界cold起動の既知試走設定も全候補検証後にだけ適用する()
        {
            using(var source=RuntimeSession141(false,1,out var defs))using(var target=new OutsideExplorationSession()){
                var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;target.TryInitializePlayer(()=>new PlayerCharacter(120));var old=target.Player;string before=CoreKey141(target.Core);var invalid=runtime.Copy();invalid.FixedCameraCenter=long.MaxValue;Assert.Throws<ArgumentOutOfRangeException>(()=>target.RestoreSavedState(actor,invalid,null,defs,OutsideTraversalProfile.ValidationWall(true)));Assert.AreSame(old,target.Player);Assert.AreEqual(before,CoreKey141(target.Core));Assert.AreEqual("validation-traversal-v1",target.Core.TraversalProfile.Id);Assert.IsTrue(target.RestoreSavedState(actor,runtime,null,defs,OutsideTraversalProfile.ValidationWall(true)));Assert.AreEqual(CoreKey141(source.Core),CoreKey141(target.Core));Assert.IsTrue(target.Core.TraversalProfile.WallActionsUnlocked);
            }
        }
        [Test] public void 外界の固定小数点余りも復元し未定義のダッシュ状態は受け入れない()
        {
            var map=new OutsideMovementDefinition("fraction141",32*60000,6*60000,360017);var a=new OutsideExplorationCore(map,OutsideTraversalProfile.ValidationDash);var b=new OutsideExplorationCore(map,OutsideTraversalProfile.ValidationDash);for(int i=0;i<5;i++)a.TickFrame(1);var saved=a.CaptureState();Assert.AreNotEqual(0,saved.Remainder);var restore=typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);restore.Invoke(b,new object[]{saved});for(int i=0;i<60;i++){a.TickFrame(i<30?1:-1);b.TickFrame(i<30?1:-1);Assert.AreEqual(CoreKey141(a),CoreKey141(b));}
            var plain=new OutsideExplorationCore(map);var invalid=plain.CaptureState();invalid.SupportId=null;invalid.Height=1000;invalid.Velocity=1000;invalid.DashJumping=true;string before=CoreKey141(plain);var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>restore.Invoke(plain,new object[]{invalid}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);Assert.AreEqual(before,CoreKey141(plain));
        }
        [Test] public void 外界カードの使用履歴返却順と乱数継続は保存復元時に抽選を消費しない()
        {
            using(var source=RuntimeSession141(true,0,out var defs)){
                var cards=source.Cards;cards.Reload();cards.TrySelect(2);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",flags).GetValue(cards);var random=(SeededBattleRandom)typeof(OutsideExplorationCards).GetField("random",flags).GetValue(cards);var returned=deck.DrawCards(2,random);typeof(Deck).GetMethod("ReturnPreviewDraws",flags).Invoke(deck,new object[]{returned});var data=cards.CaptureState(defs);long draws=random.Draws;var restored=OutsideExplorationCards.RestoreState(data,defs);Assert.AreEqual(data.Key(),restored.CaptureState(defs).Key());Assert.AreEqual(draws,random.Draws);Assert.AreEqual(1,restored.HandRevision);var other=(Deck)typeof(OutsideExplorationCards).GetField("deck",flags).GetValue(restored);var otherRandom=(SeededBattleRandom)typeof(OutsideExplorationCards).GetField("random",flags).GetValue(restored);for(int n=0;n<4;n++){var a=deck.DrawCards(1,random).Single();var b=other.DrawCards(1,otherRandom).Single();Assert.AreEqual(Array.IndexOf(deck.GetAllCards().ToArray(),a),Array.IndexOf(other.GetAllCards().ToArray(),b));Assert.AreEqual(random.Draws,otherRandom.Draws);}Assert.AreEqual(2,restored.SelectedIndex);
            }
        }
        [Test] public void 外界退避は表示中と未知乱数を拒否し回復後の古いRuntimeを再生しない()
        {
            using(var source=RuntimeSession141(true,0,out var defs)){
                using(var lease=source.AttachPresentation())Assert.Throws<InvalidOperationException>(()=>source.CaptureRuntime(defs));var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;Assert.IsTrue(source.RestoreSavedState(actor,runtime,null,defs));var recovered=OutsidePlayerSaveData.ForRecovery(source.Player);source.RecoverOutside(null);source.AcknowledgeRecovery(recovered);source.Player.SetHp(56);Assert.Throws<ArgumentException>(()=>source.RestoreSavedState(actor,runtime,null,defs));Assert.AreEqual(56,source.Player.CurrentHp);Assert.IsFalse(source.RestoreSavedState(recovered,null,null,defs));Assert.AreEqual(56,source.Player.CurrentHp);
            }
            using(var unknown=SelectionSession()){var cards=unknown.Cards;Assert.Throws<ArgumentException>(()=>unknown.CaptureRuntime());Assert.AreSame(cards,unknown.Cards);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)] public void 不正な外界保存個体は生きた個体を置き換える前に拒否する(int invalid)
        {
            using(var session=new OutsideExplorationSession()){var actor=new Olca.Core.Domain.PlayerCharacter(100);actor.SetHp(37);if(invalid==4){actor.SetElementResistances(Olca.Core.Domain.ElementResistances.Restore(150,0,0,0,null));actor.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="raw139",Sources=System.Array.Empty<Olca.Core.Logic.Customization.CustomizationAbilitySourceDto>()}));}session.TryInitializePlayer(()=>actor);var data=Olca.Core.Domain.OutsidePlayerSaveData.Capture(actor);if(invalid==0)data.Version=2;else if(invalid==1)data.Id="unknown";else if(invalid==2)data.RawBaseElements=new int[3];else if(invalid==3)data.Player.Hp=0;else data.RawBaseElements[0]=151;Assert.Throws<System.ArgumentException>(()=>session.RestoreSavedPlayer(data,null));Assert.AreSame(actor,session.Player);Assert.AreEqual(37,actor.CurrentHp);}
        }
        [Test] public void 外界保存個体の差替えは永久能力と共有状態を保ち旧Actorを表示側へ残さない()
        {
            using(var session=SelectionSession()){
                var actor=new Olca.Core.Domain.PlayerCharacter(120,attackPower:13,statusResistances:new Olca.Core.Domain.TimedStatusResistances(80,20,30,40));actor.SetAttackGrowth(Olca.Core.Domain.AttackMilestoneGrowth.ForLevel(5));actor.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="fixture139",Sources=new[]{new Olca.Core.Logic.Customization.CustomizationAbilitySourceDto{Kind="part",SourceId="slot139",ItemId="part139",Floating=true,Fire=7,Poison=10}}}));actor.SetElementResistances(Olca.Core.Domain.ElementResistances.Restore(150,-30,70,20,new[]{10,20,30,40}));actor.SetHp(37);actor.ApplyPoison("saved139",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,60000,2),600);session.TryInitializePlayer(()=>actor);var core=session.Core;var cards=session.Cards;var hand=System.Linq.Enumerable.ToArray(cards.Hand);using(var lease=session.AttachPresentation()){core.TickFrame(1);session.AdvancePlayerFrame();}var saved=Olca.Core.Domain.OutsidePlayerSaveData.Capture(actor);long frame=core.Frame,position=core.Position,gauge=core.CardGauge.CurrentUnits;
                Assert.IsTrue(session.RestoreSavedPlayer(saved,null));Assert.AreNotSame(actor,session.Player);Assert.AreSame(core,session.Core);Assert.AreSame(cards,session.Cards);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(frame,core.Frame);Assert.AreEqual(position,core.Position);Assert.AreEqual(gauge,core.CardGauge.CurrentUnits);Assert.AreEqual(150,session.Player.BaseElementResistances.Fire);Assert.AreEqual(80,session.Player.BaseStatusResistances.Poison);Assert.IsTrue(session.Player.DepartureAbilities.Floating);Assert.AreEqual(90,session.Player.StatusResistances.Poison);Assert.AreEqual("fixture139",session.Player.DepartureAbilities.Capture().DefinitionId);Assert.AreEqual(5,session.Player.AttackGrowth.Level);Assert.AreEqual(saved.Player.Poisons[0].RemainingUnits,session.Player.CapturePoisons()[0].RemainingUnits);actor.SetHp(1);
                using(var lease=session.AttachPresentation()){Assert.AreSame(session.Player,lease.Player);Assert.AreEqual(37,lease.Player.CurrentHp);Assert.IsFalse(session.RestoreSavedPlayer(saved,null));var next=saved.Copy();next.Id=System.Guid.NewGuid().ToString("N");Assert.Throws<System.InvalidOperationException>(()=>session.RestoreSavedPlayer(next,null));Assert.AreSame(session.Player,lease.Player);Assert.IsFalse(session.AdvancePlayerFrame());core.TickFrame(0);Assert.IsTrue(session.AdvancePlayerFrame());Assert.IsFalse(session.AdvancePlayerFrame());}
                var invalid=saved.Copy();invalid.Player.Hp=0;Assert.Throws<System.ArgumentException>(()=>session.RestoreSavedPlayer(invalid,null));Assert.AreEqual(37,session.Player.CurrentHp);
            }
        }
        private static Olca.Core.Domain.Card[] CardFixture()=>System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Range(0,5),id=>System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),n=>new Olca.Core.Domain.Card("outside-fixture-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))));
        private static System.Collections.Generic.Dictionary<(string Id,Olca.Core.Domain.ExceedLevel Level),int> Owned(Olca.Core.Domain.Card[] cards)=>System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(cards,c=>(c.Id,c.ExceedLevel)),g=>g.Key,g=>System.Linq.Enumerable.Count(g));
        private static OutsideExplorationSession SelectionSession()
        {
            var source=CardFixture();var session=new OutsideExplorationSession();Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));return session;
        }
        private static void SelectionSample(OutsideSelectionRepeat repeat,long time,bool previous=false,bool next=false,bool previousDown=false,bool nextDown=false,bool snapshot=false,long previousAt=0,long nextAt=0)
            =>repeat.Queue(time,previous,next,previousDown,nextDown,snapshot,previous||next,previousAt,nextAt);
        private static OutsideSelectionRepeat ReadySelection(OutsideExplorationCards cards)
        {
            var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,0);repeat.AdvanceFrame(cards);return repeat;
        }
        [Test] public void 外界巡回は即時一回と二十四九フレームで端を循環する()
        {
            using(var session=SelectionSession()){
                var cards=session.Cards;var repeat=ReadySelection(cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);
                for(int i=0;i<8;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(0,cards.SelectedIndex);
                Assert.AreEqual(0,session.Core.Frame);Assert.AreEqual(6000,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(17,cards.RemainingCount);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 外界巡回の同時刻両押下は列挙順によらず次を一回採用する(bool reverse)
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);
                SelectionSample(repeat,10,previous:!reverse,next:reverse,previousDown:!reverse,nextDown:reverse);
                SelectionSample(repeat,10,previous:true,next:true,previousDown:reverse,nextDown:!reverse);
                repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は後押し優先と優先解除後の反対保持待機を守る()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
                SelectionSample(repeat,2,previous:true,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,3,previous:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は同フレーム短押しを失わず初回保持を解除まで拒否する()
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,1,next:true,snapshot:true);repeat.AdvanceFrame(session.Cards);for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,2);SelectionSample(repeat,3,next:true,nextDown:true);SelectionSample(repeat,4);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(20,10,2)] [TestCase(10,20,1)] [TestCase(20,20,1)]
        public void 外界巡回の再開保持は新押下とせず時刻優先で二十四フレーム待つ(long previousAt,long nextAt,int expected)
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();repeat.Reset(true);SelectionSample(repeat,30,previous:true,next:true,snapshot:true,previousAt:previousAt,nextAt:nextAt);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(expected,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は入力禁止中の選択を進めず解除後の保持を継続する()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<9;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(-1)] [TestCase(3)] [TestCase(int.MaxValue)]
        public void 外界の直接選択は初期左端と範囲を守りカードと時計を変更しない(int invalid)
        {
            var source=CardFixture();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=session.Cards;var hand=System.Linq.Enumerable.ToArray(cards.Hand);long gauge=session.Core.CardGauge.CurrentUnits;
                Assert.AreEqual(0,cards.SelectedIndex);Assert.AreSame(hand[0],cards.SelectedCard);Assert.IsTrue(cards.TrySelect(2));Assert.AreSame(hand[2],cards.SelectedCard);
                Assert.IsFalse(cards.TrySelect(invalid));Assert.AreEqual(2,cards.SelectedIndex);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(20,cards.TotalCount);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(0,session.Core.Frame);
                var first=session.AttachPresentation();first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);Assert.AreEqual(2,next.Cards.SelectedIndex);next.Dispose();
            }
        }
        [Test] public void 外界初期手札は独立した二十枚から三枚を引き表示借用と時計で再抽選しない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));var cards=session.Cards;Assert.AreEqual(20,cards.TotalCount);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(3,cards.Hand.Count);
                foreach(var card in cards.Hand)foreach(var original in source)Assert.AreNotSame(original,card);
                var first=session.AttachPresentation();var hand=System.Linq.Enumerable.ToArray(cards.Hand);for(int n=0;n<120;n++)session.Core.TickFrame(0);first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);CollectionAssert.AreEqual(hand,next.Cards.Hand);Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));next.Dispose();Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [Test] public void 外界初回登録は所持と構築制限を共用し拒否で状態を作らない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsFalse(session.TryInitializeCards(System.Array.Empty<Olca.Core.Domain.Card>(),Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.IsFalse(session.TryInitializeCards(source,new System.Collections.Generic.Dictionary<(string,Olca.Core.Domain.ExceedLevel),int>(),random,out _));Assert.IsNull(session.Cards);
                using(var lease=session.AttachPresentation())Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.Throws<System.ArgumentNullException>(()=>session.TryInitializeCards(source,Owned(source),null,out _));Assert.IsNull(session.Cards);
                var over=(Olca.Core.Domain.Card[])source.Clone();over[4]=source[0];Assert.IsFalse(session.TryInitializeCards(over,Owned(over),random,out _));Assert.IsNull(session.Cards);
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [TestCase(1)] [TestCase(5)] [TestCase(90)]
        public void 論理フレームのまとめ方にかかわらず三二一は各三十で完了通知は一回(int batch)
        {
            var timer=new ExplorationResumeCountdown();timer.Begin();int finished=0;
            for(int start=0;start<90;start+=batch)for(int i=start;i<System.Math.Min(start+batch,90);i++)
            {Assert.AreEqual(3-i/30,timer.DisplayCount);if(timer.AdvanceFrame())finished++;}
            Assert.AreEqual(1,finished);Assert.AreEqual(0,timer.DisplayCount);Assert.IsFalse(timer.AdvanceFrame());
        }
        [Test] public void 再開連打は延長せず取消後だけ全長で始める()
        {
            var timer=new ExplorationResumeCountdown();Assert.IsFalse(timer.AdvanceFrame());timer.Begin();timer.AdvanceFrame();timer.Begin();Assert.AreEqual(89,timer.RemainingFrames);
            timer.Cancel();Assert.AreEqual(0,timer.DisplayCount);timer.Begin();Assert.AreEqual(90,timer.RemainingFrames);
        }
        [Test] public void 外界Sessionは表示借用を返して再表示しても同じ状態を保持し旧借用は新表示を閉じない()
        {
            var session=new OutsideExplorationSession();var first=session.AttachPresentation();Assert.IsTrue(first.IsCurrent);first.Dispose();Assert.IsFalse(session.IsPresented);Assert.IsFalse(session.IsEnded);
            var second=session.AttachPresentation();var third=session.AttachPresentation();Assert.IsFalse(second.IsCurrent);second.Dispose();Assert.IsTrue(third.IsCurrent);Assert.IsTrue(session.IsPresented);third.Dispose();Assert.IsFalse(session.IsPresented);
        }
        [Test] public void 外界SessionはRoot終了後の再表示と旧入力を受けない()
        {
            var session=new OutsideExplorationSession();var lease=session.AttachPresentation();session.Dispose();session.Dispose();Assert.IsTrue(session.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsFalse(session.IsPresented);lease.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>session.AttachPresentation());
        }

        [TestCase(1)] [TestCase(10)] [TestCase(60)]
        public void 外界横移動は描画分割に依存せず六十Hzで進み停止と反転ができる(int parts)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();
            long consumed=0;
            for(int i=1;i<=parts;i++){long target=LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((frame,ms)=>core.TickFrame(1));}
            Assert.AreEqual(60,core.Frame);Assert.AreEqual(14*OutsideMovementDefinition.UnitsPerWorldUnit,core.Position);
            long stopped=core.Position;for(int i=0;i<60;i++)core.TickFrame(0);Assert.AreEqual(stopped,core.Position);
            for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(core.Definition.Start,core.Position);Assert.AreEqual(-1,core.Facing);
        }
        [Test] public void 外界の境界で止まり逆入力は即座に離れ検証状態は表示借用と別に保持される()
        {
            var s=new OutsideExplorationSession();var lease=s.AttachPresentation();for(int i=0;i<1000;i++)s.Core.TickFrame(-1);Assert.AreEqual(0,s.Core.Position);
            lease.Dispose();long frame=s.Core.Frame;var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual(frame,next.Core.Frame);
            s.Core.TickFrame(1);Assert.Greater(s.Core.Position,0);for(int i=0;i<1000;i++)s.Core.TickFrame(1);Assert.AreEqual(s.Core.Definition.Width,s.Core.Position);
            s.Core.TickFrame(-1);Assert.Less(s.Core.Position,s.Core.Definition.Width);
        }
        [Test] public void 外界定義が受け付ける整数極値でも端から反対側へ桁あふれしない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,long.MaxValue,long.MaxValue));
            core.TickFrame(1);Assert.AreEqual(long.MaxValue,core.Position);core.TickFrame(-1);Assert.Greater(core.Position,0);Assert.Less(core.Position,long.MaxValue);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,0,long.MaxValue));left.TickFrame(-1);Assert.AreEqual(0,left.Position);left.TickFrame(1);Assert.Greater(left.Position,0);
        }
        [Test] public void 外界低速設定でも端から逆入力で離れ整数未満の余りを失わない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,100,1));for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(99,core.Position);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,0,1));for(int i=0;i<60;i++)left.TickFrame(1);Assert.AreEqual(1,left.Position);
        }

        [Test] public void 外界Idleは移動から停止するたびゼロで開始し停止中だけ進む()
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);
            Assert.AreEqual(0,core.IdleFrame);
            for(int cycle=0;cycle<4;cycle++){
                for(int i=0;i<17;i++)core.TickFrame(1);
                Assert.AreEqual(0,core.IdleFrame);core.TickFrame(0);
                Assert.AreEqual(0,core.IdleFrame);Assert.AreEqual(0,core.MovementFrame);
                for(int i=1;i<=13;i++){core.TickFrame(0);Assert.AreEqual(i,core.IdleFrame);}
            }
        }
        [Test] public void 外界Idleは着地と境界停止でもゼロから始まり表示再入で時計を変えない()
        {
            using(var session=new OutsideExplorationSession()){
                var core=session.Core;for(int i=0;i<19;i++)core.TickFrame(0);
                core.TickFrame(0,true,true);Assert.AreEqual(0,core.IdleFrame);
                for(int i=0;i<120&&!core.IsGrounded;i++)core.TickFrame(0,false,true);
                Assert.IsTrue(core.IsGrounded);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(0);Assert.AreEqual(1,core.IdleFrame);
                var first=session.AttachPresentation();long frame=core.Frame;first.Dispose();
                var second=session.AttachPresentation();Assert.AreEqual(1,core.IdleFrame);Assert.AreEqual(frame,core.Frame);second.Dispose();
                while(core.Position>0)core.TickFrame(-1);
                core.TickFrame(-1);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(-1);Assert.AreEqual(1,core.IdleFrame);
            }
        }

        [TestCase(1)] [TestCase(7)] [TestCase(60)]
        public void 外界ジャンプは保持で高くなり描画分割で軌道が変わらない(int parts)
        {
            var held=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var tap=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();long prior=0,peak=0,shortPeak=0;
            for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-prior);prior=target;clock.Drain((f,ms)=>{held.TickFrame(1,f==1,true);tap.TickFrame(1,f==1,f==1);peak=System.Math.Max(peak,held.Height);shortPeak=System.Math.Max(shortPeak,tap.Height);});}
            Assert.AreEqual(186000,peak);Assert.Less(shortPeak,peak);Assert.IsTrue(held.IsGrounded);Assert.IsTrue(tap.IsGrounded);Assert.AreEqual(20*OutsideMovementDefinition.UnitsPerWorldUnit,held.Position);Assert.AreEqual(120,held.Frame);
        }
        [Test] public void 外界ジャンプは保持着地と空中押下で再ジャンプを予約しない()
        {
            var c=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
            c.TickFrame(0,true,true);Assert.IsTrue(c.IsGrounded);c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,0);
            c.TickFrame(0,false,false);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
        }
        [Test] public void 外界空中状態はView借用終了で進まず再表示時も保持する()
        {
            var s=new OutsideExplorationSession();var a=s.AttachPresentation();s.Core.TickFrame(-1,true,true);long y=s.Core.Height,v=s.Core.VerticalVelocity,f=s.Core.Frame;a.Dispose();var b=s.AttachPresentation();Assert.AreSame(s.Core,b.Core);Assert.AreEqual(y,b.Core.Height);Assert.AreEqual(v,b.Core.VerticalVelocity);Assert.AreEqual(f,b.Core.Frame);b.Dispose();
        }
        private static OutsideExplorationCore LandingCore(params OutsideLandingSurface[] surfaces)=>new OutsideExplorationCore(new OutsideMovementDefinition("landing-test",32*60000,8*60000,6*60000,surfaces));
        private static void LandOnValidationTop(OutsideExplorationCore c){for(int i=0;i<80;i++){c.TickFrame(1,i==0,true);if(c.SupportId=="top")return;}Assert.Fail("検証上面へ着地しない");}
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界上面接地は描画分割に依存せず下から通過して下降時だけ止まる(int parts)
        {
            var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));var clock=new LogicalFrameClock();long prior=0;bool landed=false,passedBelow=false;
            for(int i=1;i<=parts;i++){long t=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(t-prior);prior=t;clock.Drain((f,ms)=>{c.TickFrame(landed?0:1,f==1,true);if(c.VerticalVelocity>0&&c.Height<60000)passedBelow=true;if(c.SupportId=="top")landed=true;});}
            Assert.IsTrue(passedBelow);Assert.IsTrue(landed);Assert.IsTrue(c.IsGrounded);Assert.AreEqual("top",c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(0,c.VerticalVelocity);Assert.AreEqual(120,c.Frame);
        }
        [Test] public void 外界足場上の保持は再ジャンプせず新しい押下だけで飛び両端から落ちる()
        {
            foreach(int direction in new[]{-1,1}){var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));LandOnValidationTop(c);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.AreEqual(60000,c.Height);Assert.AreEqual("top",c.SupportId);
                c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,60000);Assert.IsFalse(c.IsGrounded);for(int i=0;i<100;i++)c.TickFrame(0,false,true);Assert.AreEqual("top",c.SupportId);
                for(int i=0;i<80&&c.IsGrounded;i++)c.TickFrame(direction);Assert.IsFalse(c.IsGrounded);Assert.IsNull(c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(-400,c.VerticalVelocity);
                for(int i=0;i<60;i++)c.TickFrame(direction);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);}
        }
        [Test] public void 外界重複上面は配列順でなく最初に横切る高さと識別子で決まる()
        {
            var low=new OutsideLandingSurface("low",0,32*60000,58000);var a=new OutsideLandingSurface("a",0,32*60000,60000);var b=new OutsideLandingSurface("b",0,32*60000,60000);
            foreach(var surfaces in new[]{new[]{low,b,a},new[]{a,low,b}}){var c=LandingCore(surfaces);for(int i=0;i<100;i++)c.TickFrame(0,i==0,true);Assert.AreEqual("a",c.SupportId);Assert.AreEqual(60000,c.Height);}
        }
        [Test] public void 外界上面の支持は高さと端を検証し旧入力配列の変更から独立する()
        {
            var top=new OutsideLandingSurface("top",12*60000,16*60000,60000);Assert.IsTrue(top.Contains(top.Left));Assert.IsTrue(top.Contains(top.Right));Assert.IsFalse(top.Contains(top.Left-1));Assert.IsFalse(top.Contains(top.Right+1));
            var input=new[]{top};var c=LandingCore(input);input[0]=new OutsideLandingSurface("other",0,32*60000,90000);LandOnValidationTop(c);Assert.AreEqual("top",c.SupportId);
            Assert.Throws<System.ArgumentException>(()=>LandingCore(top,top));Assert.Throws<System.ArgumentException>(()=>LandingCore(new OutsideLandingSurface("overflow",0,32*60000,long.MaxValue)));
        }
        [Test] public void 外界上面を同frameで横断し終点が面外なら検証モデルでは着地しない()
        {
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("fast",100*60000,0,60*60000,new OutsideLandingSurface("thin",334000,338000,60000)));
            // 初めはその場で跳び、下降時だけ高速横移動する。狭い上面を飛び越してfloorへ戻る。
            for(int i=0;i<55;i++)c.TickFrame(i>=50?1:0,i==0,true);Assert.AreEqual(66000,c.Height);Assert.AreEqual(300000,c.Position);c.TickFrame(1,false,true);Assert.AreEqual(56000,c.Height);Assert.IsNull(c.SupportId);for(int i=0;i<64;i++)c.TickFrame(1,false,true);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);
        }
        [Test] public void 外界支持面の識別子と位置は表示借用を返しても保持される()
        {
            var s=new OutsideExplorationSession();var first=s.AttachPresentation();for(int i=0;i<80&&s.Core.SupportId!="validation-top";i++)s.Core.TickFrame(1,i==0,true);Assert.AreEqual("validation-top",s.Core.SupportId);long y=s.Core.Height,f=s.Core.Frame,x=s.Core.Position;first.Dispose();var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual("validation-top",next.Core.SupportId);Assert.AreEqual(y,next.Core.Height);Assert.AreEqual(f,next.Core.Frame);Assert.AreEqual(x,next.Core.Position);next.Dispose();
        }
        [Test] public void 外界カメラ追従は既存の両端余白と狭いマップの中央を保つ()
        {
            var p=OutsideCameraPolicy.Follow;const long u=60000;
            Assert.AreEqual(4,p.ResolveHorizontalCenter(0,32*u,10));Assert.AreEqual(8,p.ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(28,p.ResolveHorizontalCenter(32*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界固定カメラは移動で動かず画面比率とマップ範囲に応じて拘束する()
        {
            const long u=60000;var p=OutsideCameraPolicy.FixedAt(12*u);
            foreach(long x in new[]{0,8*u,16*u,32*u}){Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,10));Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,20));}
            Assert.AreEqual(4,OutsideCameraPolicy.FixedAt(0).ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界カメラ設定は非表示だけで変更しView世代や位置と時計を初期化しない()
        {
            using(var s=new OutsideExplorationSession()){
                var core=s.Core;core.TickFrame(1);long x=core.Position,f=core.Frame;var fixedPolicy=OutsideCameraPolicy.FixedAt(12*60000);
                Assert.IsTrue(s.TryConfigureCamera(fixedPolicy));var a=s.AttachPresentation();Assert.AreSame(fixedPolicy,a.Camera);Assert.IsFalse(s.TryConfigureCamera(OutsideCameraPolicy.Follow));var b=s.AttachPresentation();a.Dispose();Assert.IsTrue(b.IsCurrent);Assert.AreSame(fixedPolicy,b.Camera);b.Dispose();Assert.AreEqual(x,core.Position);Assert.AreEqual(f,core.Frame);Assert.IsTrue(s.TryConfigureCamera(OutsideCameraPolicy.Follow));Assert.AreSame(OutsideCameraPolicy.Follow,s.Camera);
            }
        }
        [Test] public void 外界カメラ不正値と終了後の設定を拒否する()
        {
            const long u=60000;Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(-1));var p=OutsideCameraPolicy.Follow;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,float.NaN));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(-1,32*u,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(33*u).ResolveHorizontalCenter(0,32*u,10));
            var s=new OutsideExplorationSession();Assert.Throws<System.ArgumentNullException>(()=>s.TryConfigureCamera(null));Assert.Throws<System.ArgumentOutOfRangeException>(()=>s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(33*u)));s.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>s.TryConfigureCamera(p));
        }
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界カードゲージは描画分割に依存せず移動ジャンプ中も六十Hzで回復する(int batch)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);Assert.IsTrue(core.CardGauge.TrySpend(100));
            for(int i=0;i<120;i+=batch)for(int j=i;j<System.Math.Min(i+batch,120);j++)core.TickFrame(j<60?1:-1,j==0,j<24);
            Assert.AreEqual(20*OutsideCardGauge.UnitsPerPoint,core.CardGauge.CurrentUnits);Assert.AreEqual(120,core.Frame);
        }
        [Test] public void 外界カードゲージ不足と不正消費は残量を変えず回復に待ち時間を設けない()
        {
            var g=new OutsideCardGauge(100,10,20);Assert.IsFalse(g.TrySpend(21));Assert.AreEqual(1200,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.TrySpend(-1));Assert.IsTrue(g.TrySpend(20));g.AdvanceFrame();Assert.AreEqual(10,g.CurrentUnits);Assert.IsTrue(g.TrySpend(0));Assert.AreEqual(10,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージ最大量変更は残量を維持し超過だけ切り捨てる()
        {
            var g=new OutsideCardGauge(100,10,80);g.AdvanceFrame();long amount=g.CurrentUnits;g.Configure(140,20);Assert.AreEqual(amount,g.CurrentUnits);g.Configure(50,0);Assert.AreEqual(3000,g.CurrentUnits);Assert.IsTrue(g.TrySpend(10));for(int i=0;i<120;i++)g.AdvanceFrame();Assert.AreEqual(2400,g.CurrentUnits);g.RestoreFull();Assert.AreEqual(3000,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージは無効設定を原子的に拒否し表現上限でも溢れない()
        {
            var g=new OutsideCardGauge(100,10,80);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(0,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(200,-1));Assert.AreEqual(100,g.Maximum);Assert.AreEqual(10,g.RecoveryPerSecond);Assert.AreEqual(4800,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>new OutsideCardGauge(100,1,101));
            var large=new OutsideCardGauge(int.MaxValue,int.MaxValue,int.MaxValue);Assert.IsTrue(large.TrySpend(int.MaxValue));for(int i=0;i<61;i++)large.AdvanceFrame();Assert.AreEqual(large.MaximumUnits,large.CurrentUnits);
        }
        [Test] public void 外界カードゲージはView世代と切断で初期化せずSessionごとに独立する()
        {
            using(var a=new OutsideExplorationSession())using(var b=new OutsideExplorationSession()){a.Core.CardGauge.TrySpend(20);long amount=a.Core.CardGauge.CurrentUnits;var first=a.AttachPresentation();var second=a.AttachPresentation();first.Dispose();Assert.IsTrue(second.IsCurrent);second.Dispose();Assert.AreEqual(amount,a.Core.CardGauge.CurrentUnits);Assert.AreEqual(6000,b.Core.CardGauge.CurrentUnits);}
        }
        [TestCase(1,0,"0.9 / 1","1.0 / 1")] [TestCase(100,99,"99.9 / 100","100.0 / 100")]
        public void 外界ゲージ表示は整数コスト直前と満量直前に消費可能量を過大表示しない(int maximum,int initial,string before,string full)
        {
            var gauge=new OutsideCardGauge(maximum,1,initial);for(int i=0;i<59;i++)gauge.AdvanceFrame();long units=gauge.CurrentUnits;
            StringAssert.EndsWith(before,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.AreEqual(units,gauge.CurrentUnits);Assert.IsFalse(gauge.TrySpend(maximum));
            gauge.AdvanceFrame();StringAssert.EndsWith(full,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.IsTrue(gauge.TrySpend(maximum));Assert.AreEqual(0,gauge.CurrentUnits);
        }
        [Test] public void 外界の回復ポイントは二十枚全体を再装填し左端へ戻すが再表示だけでは変えない()
        {
            var source=CardFixture();using(var session=new OutsideExplorationSession()){
                session.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=session.Cards;cards.TrySelect(2);long revision=cards.HandRevision;var old=cards.Hand;session.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.AreSame(cards,session.Cards);Assert.AreEqual(revision+1,cards.HandRevision);Assert.AreEqual(0,cards.SelectedIndex);Assert.AreEqual(20,cards.TotalCount);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(3,cards.Hand.Count);Assert.AreNotSame(old,cards.Hand);using(var lease=session.AttachPresentation()){}Assert.AreEqual(revision+1,cards.HandRevision);
            }
        }
    }
}
