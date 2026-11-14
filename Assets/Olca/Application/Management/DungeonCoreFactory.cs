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
        // 別の登録/将来の所持品を自動変換しない。読込も保存も行わない。
        public static bool CanRegister(Olca.Core.Logic.Customization.CustomizationProgress state)
            =>state==null||(state.BoardId==null||state.BoardId==BoardId)&&state.OwnedAccessoryIds.All(x=>x==Accessory||x==QualityAccessory)&&state.OwnedParts.All(x=>x.Id==Part)&&state.OwnedUnlockItems.All(x=>x.Id==Unlock);
        // 既存の試用盤面/能力定義だけを明示登録。別の製品定義へ仮商品を混ぜない。
        public static OutsideShopDefinition OutsideShop(Olca.Core.Logic.Customization.CustomizationCatalog catalog,Olca.Core.Logic.Customization.CustomizationAbilityCatalog abilities,IReadOnlyList<Card> cards=null,ClearReturnSettings settings=null)
            {
            if(catalog?.BoardId!=BoardId||abilities?.DefinitionId!=AbilityId||!catalog.Parts.ContainsKey(Part))return OutsideShopDefinition.TrialLimited;
            var offers=OutsideShopDefinition.TrialLimited.Offers.Concat(new[]{OutsideShopOffer.ForPart("fire-part-v1",Part,"ヒタイセイ+5（カリ）",4,1)}).ToList();
            if(catalog.Accessories.Contains(QualityAccessory))offers.Add(OutsideShopOffer.ForAccessory("quality-accessory-v1",QualityAccessory,"ホウシュウシツ+1（カリ）",5,1));
            var fire=cards?.Where(c=>c!=null&&c.Id=="fire_sword"&&c.ExceedLevel==ExceedLevel.Normal).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
            if(fire?.Length==1&&settings!=null){settings.Validate();offers.Add(OutsideShopOffer.ForCard("fire-sword-v1",fire[0],3,settings.Tokens(fire[0].Rarity,fire[0].ExceedLevel),4));}
            return new OutsideShopDefinition(OutsideShopDefinition.TrialLimited.Id,offers,catalog);
        }
        public static Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider Provider()=>new TrialProvider();
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
