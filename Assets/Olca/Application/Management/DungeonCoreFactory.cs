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
        public const string TrialStepArcherRouteId="trial-dungeon-archer-step-route-v2";
        public const string TrialArcherEnemyId=DungeonTrialArrowAction.EnemyId,TrialArcherRouteId="trial-dungeon-archer-route-v1";
        public const string TrialSwordEnemyId="trial-sword-slime-v1",TrialBombEnemyId="trial-bomb-slime-v1";
        public static IEnemyAIProvider CreateTrialCardEnemyAI()
        {
            EnemyAIRegistration Definition(Olca.Core.Logic.CardEffects.ICardDatabase database,string id){
                var card=database?.GetCardById(id)??throw new InvalidOperationException("試用敵のカード定義がありません");
                // 登録factory内で行動を毎回生成。隣接を待つStayは0、カード終了後の待ちは既存2秒を暫定借用。
                return new EnemyAIRegistration(new[]{new EnemyActionPattern(0,EnemyActionPatternSamples.Slime(card,2000,database),EnemyPatternEndMode.Loop)},0);
            }
            return new PairEnemyProvider447(new EnemyAIRegistry(new Dictionary<string,Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>>{
                [DungeonStepArcherAction.EnemyId]=(e,d)=>new EnemyAIRegistration(new[]{new EnemyActionPattern(0,new IEnemyAIAction[]{new DungeonStepArcherAction()},EnemyPatternEndMode.Loop)},0),
                [TrialArcherEnemyId]=(e,d)=>new EnemyAIRegistration(new[]{new EnemyActionPattern(0,new IEnemyAIAction[]{new DungeonTrialArrowAction()},EnemyPatternEndMode.Loop)},0),
                [TrialSwordEnemyId]=(e,d)=>Definition(d,SimpleCardFactory.Sword),[TrialBombEnemyId]=(e,d)=>Definition(d,SimpleCardFactory.Bomb)}));
        }
        private sealed class PairEnemyProvider447:IEnemyAIProvider
        {
            private readonly IEnemyAIProvider fallback;
            public PairEnemyProvider447(IEnemyAIProvider fallback){this.fallback=fallback;}
            public EnemyAIRegistration Create(Enemy enemy,Olca.Core.Logic.CardEffects.ICardDatabase cards)
                =>DungeonStepArcherAction.IsPairTrialBoss(enemy)?new EnemyAIRegistration(new[]{new EnemyActionPattern(0,new IEnemyAIAction[]{new DungeonStepArcherAction()},EnemyPatternEndMode.Loop)},0):fallback.Create(enemy,cards);
        }
        public const string TrialPairRouteId447="trial-dungeon-pair-route-v1";
        /// <summary>二体だけの任意出撃。弓を既存の区間ボス枠へ登録し、通常接触と保存形式をそのまま使う。</summary>
        public static FloorSequenceDefinitions CreateTrialPairRoute447(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal)
        {
            if(catalog==null||cards==null)throw new ArgumentNullException();var cells=new List<MapCell>();
            for(int i=0;i<8;i++){
                var enemy=i==3?new Enemy("boss",DungeonStepArcherAction.PairTrialBossName,60,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):i==4?new Enemy(TrialSwordEnemyId,"ソードスライム（カリ）",50,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):null;
                cells.Add(enemy==null?new MapCell(i):new MapCell(i,enemy));
            }
            return FloorSequenceDefinitions.WithSharedCardCatalog(TrialPairRouteId447,"trial-dungeon-pair-v1",new[]{new DungeonConfig("trial-dungeon-pair-floor-v1","シヨウ ユミ・ソード（カリ）",8,cells)},catalog);
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
        /// <summary>任意の通常出撃。初回の視界に弓1体だけが入り、旧ルートを変更しない試用配置。</summary>
        public static FloorSequenceDefinitions CreateTrialArcherRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            if(catalog==null||cards==null)throw new ArgumentNullException();
            var cells=new List<MapCell>();
            for(int i=0;i<18;i++){
                Enemy enemy=i==3?new Enemy(TrialArcherEnemyId,"ユミゴブリン（カリ）",60,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):
                    i==17?new Enemy("boss","ボス",200,ElementType.None,new Vector2Int(i,0),poiseDefinition:boss):null;
                cells.Add(enemy==null?new MapCell(i):new MapCell(i,enemy));
            }
            var map=new DungeonConfig("trial-dungeon-archer-floor-v1","シヨウ ユミテキ（カリ）",18,cells);
            return FloorSequenceDefinitions.WithSharedCardCatalog(TrialArcherRouteId,"trial-dungeon-archer-v1",new[]{map},catalog);
        }
        /// <summary>旧弓攻略は保持する、反撃時間と一度の後退を比較する新しい任意出撃。</summary>
        public static FloorSequenceDefinitions CreateTrialStepArcherRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            if(catalog==null||cards==null)throw new ArgumentNullException();
            var cells=new List<MapCell>();for(int i=0;i<18;i++){
                Enemy enemy=i==3?new Enemy(DungeonStepArcherAction.EnemyId,"ユミゴブリン（カリ）",60,ElementType.None,new Vector2Int(i,0),poiseDefinition:normal):
                    i==17?new Enemy("boss","ボス",200,ElementType.None,new Vector2Int(i,0),poiseDefinition:boss):null;
                cells.Add(enemy==null?new MapCell(i):new MapCell(i,enemy));
            }
            return FloorSequenceDefinitions.WithSharedCardCatalog(TrialStepArcherRouteId,"trial-dungeon-archer-step-v2",new[]{new DungeonConfig("trial-dungeon-archer-step-floor-v2","シヨウ ユミ・マアイ（カリ）",18,cells)},catalog);
        }
        public const string TrialRewardMapId="trial-reward-floor-v1";
        /// <summary>旧試用を上書きしない、新しい実カード入手表の試走。</summary>
        public static FloorSequenceDefinitions CreateTrialRewardRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialCardRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            var map=new DungeonConfig(TrialRewardMapId,"シヨウ カードホウシュウ（カリ）",source.TotalCellCount,source.MapCells,visits:source.Visits);
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-card-rewards-v1","trial-reward-v1",new[]{map},catalog);
        }
        /// <summary>既存攻略を置換しない、攻略中通貨とショップ操作の明示的な試用配置。</summary>
        public static FloorSequenceDefinitions CreateTrialShopRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialCardRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            // 価格3/3/2/1、4候補中3枠、セル6は試用値。既存戦闘後の通貨10で試せる範囲にする。
            var shop=new DungeonVisitDefinition(6,true,new[]{
                new DungeonVisitOption("sword",3,cards.CreateById(SimpleCardFactory.Sword)),
                new DungeonVisitOption("heal-card",3,cards.CreateById(SimpleCardFactory.Heal)),
                new DungeonVisitOption("heal-now",2,heal:cards.CreateById(SimpleCardFactory.Heal).Power),
                new DungeonVisitOption("remove-one",1,removeCount:1)},3);
            var map=new DungeonConfig("trial-shop-floor-v1","シヨウ ショップ（カリ）",source.TotalCellCount,source.MapCells,visits:new[]{shop});
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-shop-v1","trial-shop-v1",new[]{map},catalog);
        }
        public const string TrialStatusRouteId="trial-status-v1";
        /// <summary>付与/解除を任意に選ぶ専用試用ルート。旧ルートと解放順を変更しない。</summary>
        public static FloorSequenceDefinitions CreateTrialStatusRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialCardRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            var cells=source.MapCells.Select(c=>c.Index==3?new MapCell(3):c).ToList();
            var visits=new[]{new DungeonVisitDefinition(2,false,new[]{new DungeonVisitOption("trial-three-status",new DungeonVisitStatusTrial(180,900,1200))},1),new DungeonVisitDefinition(6,false,new[]{new DungeonVisitOption("trial-clear-three-status",new DungeonVisitStatusTrial(clear:true))},1)};
            var map=new DungeonConfig("trial-status-floor-v1","シヨウ ジョウタイイジョウ（カリ）",source.TotalCellCount,cells,visits:visits);
            return FloorSequenceDefinitions.WithSharedCardCatalog(TrialStatusRouteId,"trial-status-v1",new[]{map},catalog);
        }
        /// <summary>地形と常時フロートを通常操作で比較する独立の試用ルート。配置と量は仮。</summary>
        public static FloorSequenceDefinitions CreateTrialFloatRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialCardRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            var cells=source.MapCells.Select(c=>c.Index==3?new MapCell(3):c).ToList();
            var terrain=new[]{new ExplorationTerrainCell(1,PanelType.Magma),new ExplorationTerrainCell(2,PanelType.Ice),new ExplorationTerrainCell(3,PanelType.Hole)};
            var map=new DungeonConfig("trial-float-floor-v1","シヨウ フロート・チケイ（カリ）",source.TotalCellCount,cells,visits:source.Visits,explorationTerrain:terrain);
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-float-v1","trial-float-v1",new[]{map},catalog);
        }
        /// <summary>効果ごとの持越しを試す別ID。初回外界持込みの安全停止は解除しない。</summary>
        public static FloorSequenceDefinitions CreateTrialCarryStatusRoute(CheckpointDefinitions catalog,SimpleCardFactory cards,EnemyPoiseDefinition normal,EnemyPoiseDefinition boss)
        {
            var source=CreateTrialStatusRoute(catalog,cards,normal,boss).CreateInitialMap(0);
            var visits=new[]{new DungeonVisitDefinition(2,false,new[]{new DungeonVisitOption("trial-carry-three-status",new DungeonVisitStatusTrial(60,3600,3600,carryAcrossBattles:true))},1),source.Visits[1]};
            var map=new DungeonConfig("trial-carry-status-floor-v1","シヨウ ジョウタイモチコシ（カリ）",source.TotalCellCount,source.MapCells,visits:visits);
            return FloorSequenceDefinitions.WithSharedCardCatalog("trial-carry-status-v1","trial-carry-status-v1",new[]{map},catalog);
        }
        public static IEvaluationRewardProvider CreateTrialRewardProvider(SimpleCardFactory cards)=>new TrialRewardProvider(cards);
        private sealed class TrialRewardProvider:IEvaluationRewardProvider
        {
            private readonly ICardPool pool;
            public TrialRewardProvider(SimpleCardFactory cards){pool=new TrialCardPool(cards??throw new ArgumentNullException(nameof(cards)));}
            public EvaluationRewardDefinition Resolve(BattleRewardContext context)
            {
                if(context==null||context.DungeonId!=TrialRewardMapId||context.HasNextFloor)throw new ArgumentException("試用報酬の対象マップが一致しません");
                if(context.QualityBonus>0){
                    if(context.BaseCurrencyAmounts.Any(v=>v>int.MaxValue-4))throw new ArgumentException("試用の秀＋通貨が上限を超えています");
                    // 装備試用専用の明示5帯。旧4帯・旧IDを変更せず、秀＋を自動補完しない。
                    var bands=Enumerable.Range(0,4).Select(_=>new EvaluationRewardBand(pool,5000,0,Array.Empty<int>(),context.BaseCurrencyAmounts)).ToList();
                    bands.Add(new EvaluationRewardBand(pool,10000,0,Array.Empty<int>(),context.BaseCurrencyAmounts.Select(v=>checked(v+4))));
                    return new EvaluationRewardDefinition("trial-card-quality-rewards-v1",bands);
                }
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

namespace Olca.Application.Management
{
    /// <summary>正式品ではない任意受取の試用装備。値・形・入手元を変更する時は別IDで登録する。</summary>
    public static class TrialCustomizationDefinitions
    {
        public const string BoardId="trial-equipment-board-v1",AbilityId="trial-equipment-abilities-v1";
        public const string QualityAccessory="カリ・ホウシュウシツ+1";
        public const string BoardV2Id="trial-equipment-board-5x5-v2";
        public static IReadOnlyList<string> EdgeV2Ids { get; }=Array.AsReadOnly(new[]{"カリ・ウエ5マス-v2","カリ・シタ5マス-v2","カリ・ヒダリ3マス-v2","カリ・ミギ3マス-v2"});
        public const string FloatingAccessoryV2="カリ・ジョウジフロート-v2";
        public const string FireAccessoryV2="カリ・ヒタイセイ20-v2",PoisonAccessoryV2="カリ・ドクジカン25-v2",FirePartV2="カリ・ヒタイセイ10パーツ-v2",PoisonPartV2="カリ・ドクジカン10パーツ-v2";
        public static bool IsTrialBoard(string id)=>id==BoardId||id==BoardV2Id;
        public static Olca.Core.Logic.Customization.CustomizationCatalog CatalogV2()=>new Olca.Core.Logic.Customization.CustomizationCatalog(BoardV2Id,
            new Olca.Core.Logic.Customization.PartsBoardDefinition(5,5,Enumerable.Range(1,3).SelectMany(y=>Enumerable.Range(1,3).Select(x=>new Vector2Int(x,y)))),new[]{Accessory,QualityAccessory,FireAccessoryV2,PoisonAccessoryV2,FloatingAccessoryV2},
            new Dictionary<string,Olca.Core.Logic.Customization.PartShape>{{Part,new Olca.Core.Logic.Customization.PartShape(new[]{new Vector2Int(0,0),new Vector2Int(1,0)})},{FirePartV2,new Olca.Core.Logic.Customization.PartShape(new[]{new Vector2Int(0,0),new Vector2Int(1,0)})},{PoisonPartV2,new Olca.Core.Logic.Customization.PartShape(new[]{new Vector2Int(0,0)})}},
            new Dictionary<string,IEnumerable<Vector2Int>>{{EdgeV2Ids[0],Enumerable.Range(0,5).Select(x=>new Vector2Int(x,0))},{EdgeV2Ids[1],Enumerable.Range(0,5).Select(x=>new Vector2Int(x,4))},{EdgeV2Ids[2],Enumerable.Range(1,3).Select(y=>new Vector2Int(0,y))},{EdgeV2Ids[3],Enumerable.Range(1,3).Select(y=>new Vector2Int(4,y))}});
        public static Olca.Core.Logic.Customization.CustomizationProgress NewGameProgress(){var catalog=CatalogV2();return new Olca.Core.Logic.Customization.CustomizationProgress{BoardId=BoardV2Id,UnlockedCells=catalog.Board.UnlockedCells.Select(v=>new Olca.Core.Logic.Customization.CustomizationCell{X=v.X,Y=v.Y}).ToArray()};}
        public static Olca.Core.Logic.Customization.CustomizationCatalog CatalogFor(Olca.Core.Logic.Customization.CustomizationProgress state){if(!CanRegister(state))throw new ArgumentException("試用盤面と所持品が一致しません");return state?.BoardId==BoardV2Id?CatalogV2():Catalog();}
        public static Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider ProviderFor(Olca.Core.Logic.Customization.CustomizationProgress state){if(!CanRegister(state))throw new ArgumentException("試用盤面と所持品が一致しません");return state?.BoardId==BoardV2Id?new V2TrialProvider():Provider();}

        public const string Accessory="カリ・ヒタイセイ+10",Part="カリ・ヒタイセイ+5パーツ",Unlock="カリ・ミギ3マス";
        public static Olca.Core.Logic.Customization.CustomizationCatalog Catalog()
            =>new Olca.Core.Logic.Customization.CustomizationCatalog(BoardId,new Olca.Core.Logic.Customization.PartsBoardDefinition(3,3,
                Enumerable.Range(0,3).SelectMany(y=>Enumerable.Range(0,2).Select(x=>new Vector2Int(x,y)))),new[]{Accessory,QualityAccessory},
                new Dictionary<string,Olca.Core.Logic.Customization.PartShape>{{Part,new Olca.Core.Logic.Customization.PartShape(new[]{new Vector2Int(0,0),new Vector2Int(1,0)})}},
                new Dictionary<string,IEnumerable<Vector2Int>>{{Unlock,Enumerable.Range(0,3).Select(y=>new Vector2Int(2,y))}});
        public static Olca.Core.Logic.Customization.CustomizationAbilityCatalog Abilities()
            =>new Olca.Core.Logic.Customization.CustomizationAbilityCatalog(AbilityId,
                new Dictionary<string,Olca.Core.Logic.Customization.CustomizationAbilityValue>{{Accessory,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:10))},{QualityAccessory,new Olca.Core.Logic.Customization.CustomizationAbilityValue(rewardQualityBonus:1)}},
                new Dictionary<string,Olca.Core.Logic.Customization.CustomizationAbilityValue>{{Part,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:5))}});
        // 同じ互換ファミリーへ新IDだけ追加。既存供給元の解決値と保存DTOは変えない。
        public static Olca.Core.Logic.Customization.CustomizationAbilityCatalog AbilitiesV2()=>new Olca.Core.Logic.Customization.CustomizationAbilityCatalog(AbilityId,
            new Dictionary<string,Olca.Core.Logic.Customization.CustomizationAbilityValue>{{Accessory,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:10))},{QualityAccessory,new Olca.Core.Logic.Customization.CustomizationAbilityValue(rewardQualityBonus:1)},{FireAccessoryV2,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:20))},{PoisonAccessoryV2,new Olca.Core.Logic.Customization.CustomizationAbilityValue(statuses:new TimedStatusResistances(poison:25))},{FloatingAccessoryV2,new Olca.Core.Logic.Customization.CustomizationAbilityValue(floating:true)}},
            new Dictionary<string,Olca.Core.Logic.Customization.CustomizationAbilityValue>{{Part,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:5))},{FirePartV2,new Olca.Core.Logic.Customization.CustomizationAbilityValue(new ElementResistances(fire:10))},{PoisonPartV2,new Olca.Core.Logic.Customization.CustomizationAbilityValue(statuses:new TimedStatusResistances(poison:10))}});
        public static Olca.Core.Logic.Customization.CustomizationAbilityCatalog AbilitiesFor(Olca.Core.Logic.Customization.CustomizationProgress state){if(!CanRegister(state))throw new ArgumentException("試用能力と所持品が一致しません");return state?.BoardId==BoardV2Id?AbilitiesV2():Abilities();}
        // 別の登録/将来の所持品を自動変換しない。読込も保存も行わない。
        public static bool CanRegister(Olca.Core.Logic.Customization.CustomizationProgress state)
            =>state==null||(state.BoardId==null||IsTrialBoard(state.BoardId))&&state.OwnedAccessoryIds.All(x=>x==Accessory||x==QualityAccessory||state.BoardId==BoardV2Id&&(x==FireAccessoryV2||x==PoisonAccessoryV2||x==FloatingAccessoryV2))&&state.OwnedParts.All(x=>x.Id==Part||state.BoardId==BoardV2Id&&(x.Id==FirePartV2||x.Id==PoisonPartV2))&&state.OwnedUnlockItems.All(x=>state.BoardId==BoardV2Id?EdgeV2Ids.Contains(x.Id):x.Id==Unlock);
        // 既存の試用盤面/能力定義だけを明示登録。別の製品定義へ仮商品を混ぜない。
        public static OutsideShopDefinition OutsideShop(Olca.Core.Logic.Customization.CustomizationCatalog catalog,Olca.Core.Logic.Customization.CustomizationAbilityCatalog abilities,IReadOnlyList<Card> cards=null,ClearReturnSettings settings=null)
            {
            if(!IsTrialBoard(catalog?.BoardId)||abilities?.DefinitionId!=AbilityId||!catalog.Parts.ContainsKey(Part))return OutsideShopDefinition.TrialInitial;
            var offers=OutsideShopDefinition.TrialInitial.Offers.Concat(new[]{OutsideShopOffer.ForPart("fire-part-v1",Part,"ヒタイセイ+5（カリ）",4,1)}).ToList();
            if(catalog.Accessories.Contains(QualityAccessory))offers.Add(OutsideShopOffer.ForAccessory("quality-accessory-v1",QualityAccessory,"ホウシュウシツ+1（カリ）",5,1));
            var fire=cards?.Where(c=>c!=null&&c.Id=="fire_sword"&&c.ExceedLevel==ExceedLevel.Normal).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
            if(fire?.Length==1&&settings!=null){settings.Validate();offers.Add(OutsideShopOffer.ForCard("fire-sword-v1",fire[0],3,settings.Tokens(fire[0].Rarity,fire[0].ExceedLevel),4));}
            offers.Add(new OutsideShopOffer("combined-recovery-v1",OutsideRecoveryItemDefinition.CombinedProvisional.Id,"HP+ドクケシ（カリ）",3,99));
            offers.Add(new OutsideShopOffer("percent-recovery-v1",OutsideRecoveryItemDefinition.PercentProvisional.Id,"HP25%カイフク（カリ）",3,10));
            if(catalog.BoardId==BoardV2Id)for(int i=0;i<EdgeV2Ids.Count;i++)offers.Add(OutsideShopOffer.ForUnlock("board-edge-v2-"+i,EdgeV2Ids[i],new[]{"ウエ5マス","シタ5マス","ヒダリ3マス","ミギ3マス"}[i]+" カイホウ（カリ）",100));
            if(catalog.BoardId==BoardV2Id){
                if(catalog.Accessories.Contains(FireAccessoryV2)&&abilities.ContainsAccessory(FireAccessoryV2))offers.Add(OutsideShopOffer.ForAccessory("fire-accessory-v2",FireAccessoryV2,"ヒタイセイ+20（カリ）",100));
                if(catalog.Accessories.Contains(PoisonAccessoryV2)&&abilities.ContainsAccessory(PoisonAccessoryV2))offers.Add(OutsideShopOffer.ForAccessory("poison-accessory-v2",PoisonAccessoryV2,"ドクジカンタイセイ+25%（カリ）",100));
                if(catalog.Parts.ContainsKey(FirePartV2)&&abilities.ContainsPart(FirePartV2))offers.Add(OutsideShopOffer.ForPart("fire-part-v2",FirePartV2,"ヒタイセイ+10（カリ）",60,0));
                if(catalog.Parts.ContainsKey(PoisonPartV2)&&abilities.ContainsPart(PoisonPartV2))offers.Add(OutsideShopOffer.ForPart("poison-part-v2",PoisonPartV2,"ドクジカンタイセイ+10%（カリ）",60,0));
                if(catalog.Accessories.Contains(FloatingAccessoryV2)&&abilities.ContainsAccessory(FloatingAccessoryV2))offers.Add(OutsideShopOffer.ForAccessory("floating-accessory-v2",FloatingAccessoryV2,"ジョウジフロート（カリ）",200));
            }
            return new OutsideShopDefinition(OutsideShopDefinition.TrialInitial.Id,offers,catalog);
        }
        public static Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider Provider()=>new TrialProvider();
        private sealed class V2TrialProvider:Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider
        {
            private static string Id(string value){using(var hash=System.Security.Cryptography.SHA256.Create())return string.Concat(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("trial-board-v2:"+value)).Take(16).Select(v=>v.ToString("x2")));}
            public Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition Resolve(Olca.Core.Logic.Customization.CustomizationAcquisitionContext context){
                if(context==null)throw new ArgumentNullException(nameof(context));
                string origin=context.IsClearReward?"clear:"+context.ClearRunId:"trial-equipment-event-v2";
                var names=new Dictionary<string,string>();var grants=new List<Olca.Core.Logic.Customization.CustomizationGrant>();
                if(context.IsClearReward){string id=Id(origin+":quality");grants.Add(new Olca.Core.Logic.Customization.CustomizationGrant(id,new[]{QualityAccessory},clearRunId:context.ClearRunId));names[id]="ホウシュウシツ+1（カリ・キュウヒン）";}
                else{string gear=Id(origin+":gear"),edges=Id(origin+":edges");grants.Add(new Olca.Core.Logic.Customization.CustomizationGrant(gear,new[]{Accessory},new Dictionary<string,int>{{Part,2}}));names[gear]="キュウシヨウ ソウビセット（カリ）";
                    grants.Add(new Olca.Core.Logic.Customization.CustomizationGrant(edges,unlockItems:EdgeV2Ids.ToDictionary(v=>v,v=>1)));names[edges]="5x5 シヘンカイホウ4シュ（カリ・ムリョウシヨウ）";}
                return new Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition(origin,context.IsClearReward?1:2,grants,names);
            }
        }
        private sealed class TrialProvider:Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider
        {
            public Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition Resolve(Olca.Core.Logic.Customization.CustomizationAcquisitionContext context)
            {
                if(context==null)throw new ArgumentNullException(nameof(context));
                string origin=context.IsClearReward?"clear:"+context.ClearRunId:"trial-equipment-event-v1",id;
                using(var hash=System.Security.Cryptography.SHA256.Create())id=string.Concat(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("trial-equipment-v1:"+origin)).Take(16).Select(b=>b.ToString("x2")));
                var grant=new Olca.Core.Logic.Customization.CustomizationGrant(id,new[]{Accessory},new Dictionary<string,int>{{Part,2}},new Dictionary<string,int>{{Unlock,1}},context.ClearRunId);
                var candidates=new List<Olca.Core.Logic.Customization.CustomizationGrant>{grant};var names=new Dictionary<string,string>{{id,"シヨウ ソウビセット（カリ）"}};
                if(context.IsClearReward){
                    string qualityId;using(var hash=System.Security.Cryptography.SHA256.Create())qualityId=string.Concat(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("trial-quality-v1:"+origin)).Take(16).Select(b=>b.ToString("x2")));
                    candidates.Add(new Olca.Core.Logic.Customization.CustomizationGrant(qualityId,new[]{QualityAccessory},clearRunId:context.ClearRunId));names.Add(qualityId,"ホウシュウシツ+1（カリ） / カードホウシュウ ルートヨウ / ソウビチュウ");
                }
                return new Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition(origin,1,candidates,names);
            }
        }
    }
}

namespace Olca.Application.Management
{
    /// <summary>既存の明示AIを優先し、通常Rootの旧即時近接だけを予告付き試用版へ接続する。</summary>
    public sealed class TrialTelegraphFallbackEnemyAI : Olca.Core.Logic.Battle.IEnemyAIProvider
    {
        public const string DefinitionId="trial-default-melee-telegraph-v1";
        public const int StartupFrames=30,Damage=10,CooldownMs=2000;
        private readonly Olca.Core.Logic.Battle.IEnemyAIProvider specified;
        public TrialTelegraphFallbackEnemyAI(Olca.Core.Logic.Battle.IEnemyAIProvider specified){this.specified=specified;}
        public Olca.Core.Logic.Battle.EnemyAIRegistration Create(Olca.Core.Domain.Enemy enemy,Olca.Core.Logic.CardEffects.ICardDatabase cards)
        {
            if(enemy==null)throw new System.ArgumentNullException(nameof(enemy));
            var registered=specified?.Create(enemy,cards);if(registered!=null)return registered;
            var action=new Olca.Core.Logic.Battle.TelegraphedBasicAttackAction(StartupFrames,Damage,CooldownMs);
            return new Olca.Core.Logic.Battle.EnemyAIRegistration(new[]{new Olca.Core.Logic.Battle.EnemyActionPattern(0,new Olca.Core.Logic.Battle.IEnemyAIAction[]{action},Olca.Core.Logic.Battle.EnemyPatternEndMode.Loop)},0);
        }
    }
}
