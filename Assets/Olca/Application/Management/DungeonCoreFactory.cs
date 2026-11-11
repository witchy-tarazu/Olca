using System;
using System.Collections.Generic;
using System.Linq;
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
        // 新IDの試用定義だけを登録する。変更時は敵IDとroute revisionを新しくし、旧攻略へ上書きしない。
        public const string TrialSwordEnemyId="trial-sword-slime-v1",TrialBombEnemyId="trial-bomb-slime-v1";
        public static IEnemyAIProvider CreateTrialCardEnemyAI()
        {
            EnemyAIRegistration Definition(Olca.Core.Logic.CardEffects.ICardDatabase database,string id){
                var card=database?.GetCardById(id)??throw new InvalidOperationException("試用敵のカード定義がありません");
                // 登録factory内で行動を毎回生成。隣接を待つStayは0、カード終了後の待ちは既存2秒を暫定借用。
                return new EnemyAIRegistration(new[]{new EnemyActionPattern(0,EnemyActionPatternSamples.Slime(card,2000,database),EnemyPatternEndMode.Loop)},0);
            }
            return new EnemyAIRegistry(new Dictionary<string,Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>>{
                [TrialSwordEnemyId]=(e,d)=>Definition(d,SimpleCardFactory.Sword),[TrialBombEnemyId]=(e,d)=>Definition(d,SimpleCardFactory.Bomb)});
        }
        public static FloorSequenceDefinitions CreateTrialCardRoute(CheckpointDefinitions cardCatalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            if(cardCatalog==null||cards==null)throw new ArgumentNullException();
            var cells=new List<MapCell>();for(int i=0;i<14;i++){
                Enemy enemy=i==3?new Enemy(TrialSwordEnemyId,"ソードスライム（カリ）",50,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):
                    i==9?new Enemy(TrialBombEnemyId,"ボムスライム（カリ）",50,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):
                    i==13?new Enemy("boss","ボス",200,ElementType.None,new Vector2Int(i,0),poiseDefinition:boss):null;
                cells.Add(enemy==null?new MapCell(i):new MapCell(i,enemy));
            }
            var recovery=new DungeonVisitDefinition(6,false,new[]{new DungeonVisitOption("recover",0,heal:cards.CreateById(SimpleCardFactory.Heal).Power)},1);
            var map=new DungeonConfig("trial-card-floor-v1","シヨウ テキカード（カリ）",14,cells,visits:new[]{recovery});
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-card-actions-v1","trial-card-v1",new[]{map},cardCatalog);
        }
        public const string TrialRewardMapId="trial-reward-floor-v1";
        /// <summary>旧試用を上書きしない、新しい実カード入手表の試走。</summary>
        public static FloorSequenceDefinitions CreateTrialRewardRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialCardRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            var map=new DungeonConfig(TrialRewardMapId,"シヨウ カードホウシュウ（カリ）",source.TotalCellCount,source.MapCells,visits:source.Visits);
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-card-rewards-v1","trial-reward-v1",new[]{map},catalog);
        }
        public static IEvaluationRewardProvider CreateTrialRewardProvider(SimpleCardFactory cards)=>new TrialRewardProvider(cards);
        private sealed class TrialRewardProvider:IEvaluationRewardProvider
        {
            private readonly ICardPool pool;
            public TrialRewardProvider(SimpleCardFactory cards){pool=new TrialCardPool(cards??throw new ArgumentNullException(nameof(cards)));}
            public EvaluationRewardDefinition Resolve(BattleRewardContext context)
            {
                if(context==null||context.DungeonId!=TrialRewardMapId||context.HasNextFloor)throw new ArgumentException("試用報酬の対象マップが一致しません");
                // 製品の評価差を仮決定しない。全帯を既存50%/同額・等重みの暫定入手表に揃える。
                return new EvaluationRewardDefinition("trial-card-rewards-v1",Enumerable.Range(0,4).Select(_=>new EvaluationRewardBand(pool,5000,0,Array.Empty<int>(),context.BaseCurrencyAmounts)));
            }
        }
        private sealed class TrialCardPool:ICardPool
        {
            private readonly Card[] normal,limited;
            public TrialCardPool(SimpleCardFactory cards){normal=new[]{SimpleCardFactory.Sword,SimpleCardFactory.Bomb,SimpleCardFactory.Heal,SimpleCardFactory.Shield,SimpleCardFactory.Tuning}.Select(cards.CreateById).ToArray();limited=new[]{cards.CreateById(SimpleCardFactory.FireSword)};}
            public IReadOnlyList<Card> GetCandidates(CardRarity rarity)=>(rarity==CardRarity.Normal?normal:rarity==CardRarity.Limited?limited:Array.Empty<Card>()).Select(c=>c.Clone()).ToArray();
            public Card GetRandomCard(CardRarity rarity,IRandomProvider random){if(random==null)throw new ArgumentNullException(nameof(random));var values=GetCandidates(rarity);if(values.Count==0)throw new ArgumentException("試用報酬の候補がありません");return values[random.Range(0,values.Count)].Clone();}
        }
        public DungeonBattleManager CreateFloor(FloorSequenceSession route,FloorSequenceDefinitions definitions,out DungeonCheckpointSession checkpoint)
        {
            var state=route.Current.State;checkpoint=definitions.CreateCheckpointSession(route.Current.FloorIndex,route);
            return Create(new Launch{Map=state.Map,Player=state.Player,Deck=state.Deck,Checkpoints=checkpoint,Restoration=state,HasNextFloor=route.Current.FloorIndex+1<definitions.FloorCount});
        }
    }
}
