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
    public enum PostBattleCommandKind { Card, Item, Currency, RetrySave, Suspend }
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
        private readonly PortableBattlePackage source;private readonly ICheckpointStore store;private Card[] pendingCards;
        public DungeonBattleManager Manager {get;}
        public BattleSimulation Simulation {get;private set;}
        public long OperationIndex {get;private set;}
        public IReadOnlyList<Card> CardChoices=>Array.AsReadOnly((pendingCards??Array.Empty<Card>()).Select(c=>c.Clone()).ToArray());
        public CheckpointDto Saved=>store.Read();
        public PortableBattleSession(PortableBattlePackage package,Func<CheckpointDefinitions,ICheckpointStore> openStore=null)
        {
            source=PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(package));
            var restored=source.Restore(out var catalog,out var definitions);
            if(source.FloorRoute!=null||source.Rules.GaugeSpeed!=CheckpointBattleFactory.DefaultGaugeSpeed)throw new NotSupportedException("この保存接続は単階層の既存製品ゲージ設定だけに対応します");
            store=openStore?.Invoke(definitions)??new MemoryCheckpointStore(definitions);
            var saved=store.Read();if(saved==null){if(store is ICheckpointReadStatus status&&status.ReadError!=null)throw new InvalidDataException(status.ReadError);if(!store.TryWrite(restored.Snapshot,out var error))throw new IOException(error);}
            else if(!PortableCheckpointJson.Encode(saved).SequenceEqual(source.CheckpointEnvelope))throw new InvalidDataException("専用保存先に異なる状態があります。上書きしません");
            var processing=source.Processing?.Restore();var session=new DungeonCheckpointSession(definitions,store);
            if(!DungeonBattleManager.TryRestoreCheckpoint(session,new SeededBattleRandom(source.ReplaySeed,source.RandomState,source.RandomDraws),new Log(),out var manager,out var failure,processing?.Exceed,processing?.Tuning,processing?.Fusion,catalog))throw new InvalidDataException(failure);
            Manager=manager;
            Manager.OnCardRewardPresented+=cards=>pendingCards=cards.ToArray();
            Manager.OnBattleStarted+=(battle,contact)=>{
                var r=source.Rules;battle.ConfigureDefaultHitInvincibility(r.HitInvincibilityMs);battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(r.StartupFrames,r.RecoveryFrames,r.CancelAfterFrames));
                Simulation=new BattleSimulation(new BattleSimulationSettings(restored.Player.MaxHp,1,r.ChargeFrames,r.IntervalFrames),source.ReplaySeed,_=>battle,true,randomState:source.RandomState,randomDraws:source.RandomDraws);
            };
            Manager.ActivateCheckpoint();if(Simulation==null)throw new InvalidDataException("戦闘開始を復帰できません");
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
            var d=Manager.Dungeon;var saved=store.Read();
            return string.Join("|",OperationIndex,Manager.CurrentPhase,Manager.IsSuspended,Manager.IsUserPaused,d.IsCheckpointBlocked,d.Player.CurrentHp,d.Currency,d.PendingCurrencyReward,
                string.Join(",",(pendingCards??Array.Empty<Card>()).Select(CollectionDisplay.DefinitionHash)),d.PendingItemReward?.Id??"-",
                string.Join(",",d.GetCurrentDeck().GetAllCards().Select(CollectionDisplay.DefinitionHash)),string.Join(",",d.GetObtainedCards().Select(CollectionDisplay.DefinitionHash)),
                saved==null?"-":PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(saved)),eventText);
        }
        public PortableRunCheckpoint CaptureResult()
        {
            if(Manager.IsSuspended||Manager.Dungeon.IsCheckpointBlocked||Simulation.Battle.State!=BattleState.Victory||pendingCards!=null||Manager.Dungeon.PendingItemReward!=null||Manager.Dungeon.PendingCurrencyReward>0)throw new InvalidOperationException("勝利後の報酬と保存が完了していません");
            var result=new PortableRunCheckpoint{Source=PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(source)),ResultEnvelope=PortableCheckpointJson.Encode(store.Read())};result.Restore();return result;
        }
    }
    /// <summary>既存の完了境界だけを輸送する結果。Terminalを通常再開へ変換しない。</summary>
    public sealed class PortableRunCheckpoint
    {
        public int Version=1;public PortableBattlePackage Source;public byte[] ResultEnvelope;
        public RestoredCheckpoint Restore()
        {
            if(Version!=1||Source==null||Source.FloorRoute!=null||ResultEnvelope==null)throw new InvalidDataException("結果輸送の版または内容が不正です");
            var initial=Source.Restore(out _,out var definitions);var result=PortableCheckpointJson.Decode(ResultEnvelope);
            if(result.RunId!=initial.Snapshot.RunId||result.Sequence<=initial.Snapshot.Sequence||result.Player?.Hp<=0||(result.Boundary!=CheckpointBoundary.RewardCompleted&&result.Boundary!=CheckpointBoundary.Terminal))throw new InvalidDataException("勝利後の既存保存境界ではありません");
            if(!CheckpointCodec.TryRestore(result,definitions,out var restored,out var error))throw new InvalidDataException(error);return restored;
        }
        public DungeonBattleManager Resume(out string error)
        {
            var restored=Restore();Source.Restore(out var catalog,out var definitions);var store=new MemoryCheckpointStore(definitions);if(!store.TryWrite(restored.Snapshot,out error))return null;
            var processing=Source.Processing?.Restore();return DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(definitions,store),new SeededBattleRandom(Source.ReplaySeed),PortableBattleSession.CreateLogger(),out var manager,out error,processing?.Exceed,processing?.Tuning,processing?.Fusion,catalog)?manager:null;
        }
        public string Canonical(){var r=Restore();return string.Join("|",r.Snapshot.Boundary,r.Snapshot.Player.Hp,r.Snapshot.Currency,r.Deck.TotalCount,r.Obtained.Count,PortableCheckpointJson.Hash(ResultEnvelope));}
    }
}
