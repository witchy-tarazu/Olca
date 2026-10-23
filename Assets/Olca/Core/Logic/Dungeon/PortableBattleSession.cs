using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Integration;
namespace Olca.Core.Logic.Dungeon
{
    public enum PostBattleCommandKind { Card, Item, Currency, RetrySave, Suspend, ExplorationFrame }
    public sealed class PostBattleCommand
    {
        public PostBattleCommandKind Kind {get;}public int CardIndex {get;}public bool Receive {get;}
        public PostBattleCommand(PostBattleCommandKind kind,int cardIndex=-1,bool receive=false){Kind=kind;CardIndex=cardIndex;Receive=receive;}
    }
    /// <summary>通常Managerの戦闘・報酬・保存をコマンド化する薄い入口。結果の計算を持たない。</summary>
    public sealed class PortableBattleSession
    {
        private sealed class Log : IGameLogger {public void LogInfo(string s){}public void LogWarning(string s){}public void LogError(string s){}}
        internal static IGameLogger CreateLogger()=>new Log();
        private readonly PortableBattlePackage source;private ICheckpointStore store;private Card[] pendingCards;
        private FloorSequenceDefinitions floors;private FloorSequenceSession route;private bool floorSwapPending;private int managerFloorIndex;
        public int CurrentFloorIndex=>route?.Current?.FloorIndex??0;
        public byte[] SavedFloorEnvelope=>route==null?null:PortableCheckpointJson.Encode(route.Current.Snapshot);
        public DungeonBattleManager Manager {get;private set;}
        public BattleSimulation Simulation {get;private set;}
        public long OperationIndex {get;private set;}
        public IReadOnlyList<Card> CardChoices=>Array.AsReadOnly((pendingCards??Array.Empty<Card>()).Select(c=>c.Clone()).ToArray());
        public CheckpointDto Saved=>route?.Current?.Snapshot.State??store.Read();
        public PortableBattleSession(PortableBattlePackage package,Func<CheckpointDefinitions,ICheckpointStore> openStore=null,Func<FloorSequenceDefinitions,IFloorSequenceStore> openFloorStore=null)
        {
            source=PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(package));
            var restored=source.Restore(out var catalog,out var definitions);
            if(source.Rules.GaugeSpeed!=CheckpointBattleFactory.DefaultGaugeSpeed)throw new NotSupportedException("この保存接続は既存製品ゲージ設定だけに対応します");
            if(source.FloorRoute!=null){
                if(openStore!=null)throw new ArgumentException("階層列には階層Storeを明示してください");
                floors=source.FloorRoute.CreateDefinitions();var floorStore=openFloorStore?.Invoke(floors)??new MemoryFloorSequenceStore(floors);
                var saved=floorStore.Read();if(saved==null){if(floorStore.ReadError!=null)throw new InvalidDataException(floorStore.ReadError);if(!floorStore.TryWrite(PortableCheckpointJson.DecodeFloor(source.FloorRoute.CheckpointEnvelope),out var error))throw new IOException(error);}
                else if(!PortableCheckpointJson.Encode(saved).SequenceEqual(source.FloorRoute.CheckpointEnvelope))throw new InvalidDataException("専用階層保存先に異なる状態があります。上書きしません");
                route=new FloorSequenceSession(floors,floorStore);if(!route.TryLoad(out var errorLoad))throw new InvalidDataException(errorLoad);managerFloorIndex=route.Current.FloorIndex;
                AttachManager(CreateFloorManager());
            }else{
                if(openFloorStore!=null)throw new ArgumentException("単階層に階層Storeは指定できません");
                store=openStore?.Invoke(definitions)??new MemoryCheckpointStore(definitions);
                var saved=store.Read();if(saved==null){if(store is ICheckpointReadStatus status&&status.ReadError!=null)throw new InvalidDataException(status.ReadError);if(!store.TryWrite(restored.Snapshot,out var error))throw new IOException(error);}
                else if(!PortableCheckpointJson.Encode(saved).SequenceEqual(source.CheckpointEnvelope))throw new InvalidDataException("専用保存先に異なる状態があります。上書きしません");
                var processing=source.Processing?.Restore();var session=new DungeonCheckpointSession(definitions,store);
                if(!DungeonBattleManager.TryRestoreCheckpoint(session,new SeededBattleRandom(source.ReplaySeed,source.RandomState,source.RandomDraws),new Log(),out var manager,out var failure,processing?.Exceed,processing?.Tuning,processing?.Fusion,catalog))throw new InvalidDataException(failure);
                AttachManager(manager);
            }
            Manager.ActivateCheckpoint();if(Simulation==null)throw new InvalidDataException("戦闘開始を復帰できません");
        }
        private DungeonBattleManager CreateFloorManager()
        {
            var state=route.Current.State;var session=floors.CreateCheckpointSession(route.Current.FloorIndex,route);store=session.Store;var processing=source.Processing?.Restore();
            return new DungeonBattleManager(state.Map,state.Player,state.Deck,new SeededBattleRandom(source.ReplaySeed),new Log(),processing?.Exceed,processing?.Tuning,processing?.Fusion,
                checkpoints:session,restoration:state,cardDatabase:new PortableCardCatalog(session.Definitions.PortableCards.Values),hasNextFloor:route.Current.FloorIndex+1<floors.FloorCount);
        }
        private void AttachManager(DungeonBattleManager manager)
        {
            Manager=manager;if(source.Rules.ExplorationNormalSpeedMilliPerSecond.HasValue)Manager.Dungeon.ConfigureNormalMovementSpeed(source.Rules.ExplorationNormalSpeedMilliPerSecond.Value);manager.OnCardRewardPresented+=cards=>pendingCards=cards.ToArray();manager.OnFloorCompleted+=()=>floorSwapPending=true;
            manager.OnBattleStarted+=(battle,contact)=>{
                var r=source.Rules;battle.ConfigureDefaultHitInvincibility(r.HitInvincibilityMs);battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(r.StartupFrames,r.RecoveryFrames,r.CancelAfterFrames));
                Simulation=new BattleSimulation(new BattleSimulationSettings(battle.Player.MaxHp,1,r.ChargeFrames,r.IntervalFrames,r.ResolveChargeMultiplier()),source.ReplaySeed,_=>battle,true,randomState:source.RandomState,randomDraws:source.RandomDraws);
            };
        }
        private void ApplyPendingFloor()
        {
            // Controllerと同じく、終了イベント配信後かつ保存済みの次階層だけを交換する。
            if(!floorSwapPending||route==null||route.Current.FloorIndex!=managerFloorIndex+1)return;
            var next=CreateFloorManager();if(!Manager.SuspendForCheckpoint())throw new InvalidOperationException("旧階層を停止できません");
            floorSwapPending=false;managerFloorIndex=route.Current.FloorIndex;pendingCards=null;AttachManager(next);Manager.ActivateCheckpoint();
        }
        public string Step(BattleFrameCommand command)
        {
            if(command==null)throw new ArgumentNullException(nameof(command));OperationIndex++;
            if(command.UserPaused.HasValue)Manager.SetUserPaused(command.UserPaused.Value);
            if(Manager.IsSuspended||Manager.Dungeon.IsCheckpointBlocked||Manager.CurrentPhase!=GamePhase.Battle)return Observe("battle-rejected");
            return Observe(Simulation.Step(command).Canonical);
        }
        public string Execute(PostBattleCommand command)
        {
            OperationIndex++;if(command==null)return Observe("invalid-command");
            if(command.Kind==PostBattleCommandKind.ExplorationFrame){
                if(Manager.IsSuspended||Manager.IsUserPaused||Manager.Dungeon.IsCheckpointBlocked||Manager.CurrentPhase!=GamePhase.Dungeon)return Observe("exploration-rejected");
                if(!source.Rules.ExplorationNormalSpeedMilliPerSecond.HasValue)throw new NotSupportedException("探索フレームには正規の通常移動設定が必要です");
                Manager.ResumeAfterBattle();Manager.TickFrame(16);return Observe("exploration-frame");
            }
            if(command.Kind==PostBattleCommandKind.RetrySave)return Observe(Manager.RetryCheckpoint()?"save-retried":"retry-rejected");
            if(command.Kind==PostBattleCommandKind.Suspend)return Observe(Manager.SuspendForCheckpoint()?"suspended":"suspend-rejected");
            if(Manager.IsSuspended||Manager.IsUserPaused||Manager.Dungeon.IsCheckpointBlocked||Manager.CurrentPhase!=GamePhase.CardReward)return Observe("reward-rejected");
            switch(command.Kind){
                case PostBattleCommandKind.Card:
                    if(pendingCards==null||command.CardIndex< -1||command.CardIndex>=pendingCards.Length)return Observe("card-rejected");
                    var selected=command.CardIndex<0?null:pendingCards[command.CardIndex];pendingCards=null;Manager.SelectCardReward(selected);break;
                case PostBattleCommandKind.Item:
                    if(Manager.Dungeon.PendingItemReward==null)return Observe("item-rejected");Manager.SelectItemReward(command.Receive);break;
                case PostBattleCommandKind.Currency:
                    if(Manager.Dungeon.PendingCurrencyReward<=0)return Observe("currency-rejected");Manager.SelectCurrencyReward(command.Receive);break;
                default:return Observe("invalid-command");
            }
            return Observe("reward-accepted");
        }
        public string Observe(string eventText="")
        {
            ApplyPendingFloor();var d=Manager.Dungeon;var saved=Saved;
            return string.Join("|",OperationIndex,Manager.CurrentPhase,Manager.IsSuspended,Manager.IsUserPaused,d.IsCheckpointBlocked,d.Player.CurrentHp,d.Currency,d.PendingCurrencyReward,
                string.Join(",",(pendingCards??Array.Empty<Card>()).Select(CollectionDisplay.DefinitionHash)),d.PendingItemReward?.Id??"-",
                string.Join(",",d.GetCurrentDeck().GetAllCards().Select(CollectionDisplay.DefinitionHash)),string.Join(",",d.GetObtainedCards().Select(CollectionDisplay.DefinitionHash)),
                saved==null?"-":PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(saved)),eventText)+(route==null?"":"|floor="+CurrentFloorIndex+":"+PortableCheckpointJson.Hash(SavedFloorEnvelope));
        }
        public PortableRunCheckpoint CaptureResult()
        {
            ApplyPendingFloor();
            if(Manager.IsSuspended||Manager.Dungeon.IsCheckpointBlocked||Simulation.Battle.State!=BattleState.Victory||pendingCards!=null||Manager.Dungeon.PendingItemReward!=null||Manager.Dungeon.PendingCurrencyReward>0)throw new InvalidOperationException("勝利後の報酬と保存が完了していません");
            var result=new PortableRunCheckpoint{Source=PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(source)),ResultEnvelope=PortableCheckpointJson.Encode(Saved),FloorEnvelope=SavedFloorEnvelope};result.Restore();return result;
        }
    }
    /// <summary>既存の完了境界だけを輸送する結果。Terminalを通常再開へ変換しない。</summary>
    public sealed class PortableRunCheckpoint
    {
        public int Version=1;public PortableBattlePackage Source;public byte[] ResultEnvelope;public byte[] FloorEnvelope;
        public RestoredCheckpoint Restore()
        {
            if(Version!=1||Source==null||ResultEnvelope==null)throw new InvalidDataException("結果輸送の版または内容が不正です");
            var initial=Source.Restore(out _,out var definitions);var result=PortableCheckpointJson.Decode(ResultEnvelope);bool moved=false;
            if(Source.FloorRoute!=null){
                if(FloorEnvelope==null)throw new InvalidDataException("階層封筒が欠損しています");
                var floors=Source.FloorRoute.CreateDefinitions();var saved=PortableCheckpointJson.DecodeFloor(FloorEnvelope);
                if(!floors.TryRestore(saved,out var restoredFloor,out var floorError))throw new InvalidDataException(floorError);
                var before=PortableCheckpointJson.DecodeFloor(Source.FloorRoute.CheckpointEnvelope).FloorIndex;
                moved=saved.FloorIndex==before+1;
                if((saved.FloorIndex!=before&&!moved)||!PortableCheckpointJson.Encode(restoredFloor.State.Snapshot).SequenceEqual(ResultEnvelope))throw new InvalidDataException("結果の階層と状態が一致しません");
                definitions=floors.Floor(saved.FloorIndex);
            }else if(FloorEnvelope!=null)throw new InvalidDataException("単階層に階層封筒は指定できません");
            if(result.RunId!=initial.Snapshot.RunId||result.Sequence<=initial.Snapshot.Sequence||result.Player?.Hp<=0||
                (moved?result.Boundary!=CheckpointBoundary.Departure:(result.Boundary!=CheckpointBoundary.RewardCompleted&&result.Boundary!=CheckpointBoundary.Terminal)))throw new InvalidDataException("勝利後の既存保存境界ではありません");
            if(!CheckpointCodec.TryRestore(result,definitions,out var restored,out var error))throw new InvalidDataException(error);return restored;
        }
        public DungeonBattleManager Resume(out string error)
        {
            var restored=Restore();Source.Restore(out var catalog,out var definitions);var processing=Source.Processing?.Restore();DungeonCheckpointSession session;bool hasNext=false;
            if(Source.FloorRoute!=null){
                var floors=Source.FloorRoute.CreateDefinitions();var store=new MemoryFloorSequenceStore(floors);if(!store.TryWrite(PortableCheckpointJson.DecodeFloor(FloorEnvelope),out error))return null;
                var route=new FloorSequenceSession(floors,store);if(!route.TryLoad(out error))return null;
                session=floors.CreateCheckpointSession(route.Current.FloorIndex,route);hasNext=route.Current.FloorIndex+1<floors.FloorCount;catalog=new PortableCardCatalog(session.Definitions.PortableCards.Values);
            }else{
                var store=new MemoryCheckpointStore(definitions);if(!store.TryWrite(restored.Snapshot,out error))return null;session=new DungeonCheckpointSession(definitions,store);
            }
            if(!DungeonBattleManager.TryRestoreCheckpoint(session,new SeededBattleRandom(Source.ReplaySeed),PortableBattleSession.CreateLogger(),out var manager,out error,processing?.Exceed,processing?.Tuning,processing?.Fusion,catalog,hasNext))return null;
            if(Source.Rules.ExplorationNormalSpeedMilliPerSecond.HasValue)manager.Dungeon.ConfigureNormalMovementSpeed(Source.Rules.ExplorationNormalSpeedMilliPerSecond.Value);return manager;
        }
        public string Canonical(){var r=Restore();return string.Join("|",r.Snapshot.Boundary,r.Snapshot.Player.Hp,r.Snapshot.Currency,r.Deck.TotalCount,r.Obtained.Count,PortableCheckpointJson.Hash(ResultEnvelope))+
            (FloorEnvelope==null?"":"|floor="+PortableCheckpointJson.DecodeFloor(FloorEnvelope).FloorIndex+":"+PortableCheckpointJson.Hash(FloorEnvelope));}
    }
}
