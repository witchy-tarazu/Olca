using System;
using System.Collections.Generic;
using Olca.Application.Battle;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
namespace Olca.Application.Management
{
    /// <summary>既存Coreの組立だけを行う。保存の確定、時計、入力、Viewは所有しない。</summary>
    public sealed class DungeonCoreFactory
    {
        public sealed class Processing
        {
            public SimpleCardFactory Cards {get;}
            internal ExceedSystem Exceed {get;}
            internal TuningSystem Tuning {get;}
            internal FusionSystem Fusion {get;}
            internal Processing(SimpleCardFactory cards,SimpleFusionRecipeProvider recipes)
            {Cards=cards;Exceed=new ExceedSystem(cards);Tuning=new TuningSystem(cards.GetTuningDefinition);Fusion=new FusionSystem(recipes,cards);}
        }
        public sealed class Launch
        {
            public DungeonConfig Map; public PlayerCharacter Player; public Deck Deck;
            public DungeonCheckpointSession Checkpoints; public RestoredCheckpoint Restoration;
            public bool HasNextFloor; public Processing ProcessingSystems;
        }
        private readonly IRandomProvider random;
        private readonly IGameLogger logger;
        private readonly Func<SimpleCardFactory> cards;
        private readonly IEnemyAIProvider enemies;
        private readonly IEvaluationRewardProvider rewards;
        public DungeonCoreFactory(IRandomProvider random,IGameLogger logger,Func<SimpleCardFactory> cards,IEnemyAIProvider enemies=null,IEvaluationRewardProvider rewards=null)
        {this.random=random;this.logger=logger;this.cards=cards??throw new ArgumentNullException(nameof(cards));this.enemies=enemies;this.rewards=rewards;}
        public PlayerCharacter CreatePlayer(ClearReturnSettings settings,int level,int staggerFrames,int? poiseRecoveryFrames)
        {
            var player=new PlayerCharacter(settings.MaxHp(level),new Vector2Int(0,0),attackPower:settings.Attack(level),staggerDurationFrames:staggerFrames,poiseRecoveryDelayFrames:poiseRecoveryFrames);
            player.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(level));return player;
        }
        public Processing CreateProcessing()
        {
            // 一回の組立では同じ定義元を渡し、別組立へはキャッシュしない。
            var definitions=cards();
            return new Processing(definitions,new SimpleFusionRecipeProvider(definitions));
        }
        public DungeonBattleManager Create(Launch request)
        {
            if(request==null)throw new ArgumentNullException(nameof(request));
            var p=request.ProcessingSystems??CreateProcessing();
            return new DungeonBattleManager(request.Map,request.Player,request.Deck,random,logger,p.Exceed,p.Tuning,p.Fusion,
                checkpoints:request.Checkpoints,restoration:request.Restoration,cardDatabase:p.Cards,hasNextFloor:request.HasNextFloor,enemyAIProvider:enemies,evaluationRewards:rewards);
        }
        // 出撃計画の凍結previewは既存どおり加工Systemなし。製品Coreと同一化しない。
        public DungeonBattleManager CreatePreview(DungeonConfig map,PlayerCharacter player,Deck deck,DungeonCheckpointSession checkpoint)
            =>new DungeonBattleManager(map,player,deck,random,logger,checkpoints:checkpoint,enemyAIProvider:enemies,evaluationRewards:rewards);
        public bool TryRestore(DungeonCheckpointSession checkpoint,out DungeonBattleManager manager,out string error)
        {
            var p=CreateProcessing();
            return DungeonBattleManager.TryRestoreCheckpoint(checkpoint,random,logger,out manager,out error,p.Exceed,p.Tuning,p.Fusion,p.Cards,enemyAIProvider:enemies,evaluationRewards:rewards);
        }
        /// <summary>通常入口で試せる暫定配置。既存SingleのID/hash/性能を置換しない。</summary>
        public static FloorSequenceDefinitions CreateTrialRoute(CheckpointDefinitions cardCatalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            if(cardCatalog==null||cards==null)throw new ArgumentNullException();
            DungeonConfig Floor(int index,int count,bool final){
                var cells=new List<MapCell>();
                for(int i=0;i<count;i++){
                    Enemy enemy=null;
                    if(final&&i==count-1)enemy=new Enemy("boss","ボス",200,ElementType.None,new Vector2Int(i,0),poiseDefinition:boss);
                    else if(i==3||i==9)enemy=new Enemy("enemy_"+i,"スライム",50,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal);
                    cells.Add(enemy==null?new MapCell(i):new MapCell(i,enemy));
                }
                // 階層数・配置は試用。回復量と敵性能は既存定義を借用する。
                var recovery=new DungeonVisitDefinition(6,false,new[]{new DungeonVisitOption("recover",0,heal:cards.CreateById(SimpleCardFactory.Heal).Power)},1);
                return new DungeonConfig("trial-floor-"+index,"シヨウ "+index+"/2（カリ）",count,cells,visits:new[]{recovery});
            }
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-two-floor-v1","trial-v1",new[]{Floor(1,14,false),Floor(2,18,true)},cardCatalog);
        }
        public DungeonBattleManager CreateFloor(FloorSequenceSession route,FloorSequenceDefinitions definitions,out DungeonCheckpointSession checkpoint)
        {
            var state=route.Current.State;checkpoint=definitions.CreateCheckpointSession(route.Current.FloorIndex,route);
            return Create(new Launch{Map=state.Map,Player=state.Player,Deck=state.Deck,Checkpoints=checkpoint,Restoration=state,HasNextFloor=route.Current.FloorIndex+1<definitions.FloorCount});
        }
    }
}
