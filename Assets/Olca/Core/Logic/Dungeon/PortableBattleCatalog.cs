using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Core.Logic.Dungeon
{
    // 純C#の値DTO。未対応をhash不一致/明示拒否にし、欠落値を製品既定値へ推測しない。
    public sealed class PortableCardDefinition
    {
        public string Key, Hash;
        public PortableTuningGrant TuningDefinition;
        public string Id;
        public string Name;
        public int Cost;
        public CardRarity Rarity;
        public CardCategory Category;
        public ElementType Element;
        public string Series;
        public int Power;
        public string Description;
        public DamageTargetType TargetType;
        public string PanelChangeType;
        public string MoveDirection;
        public int MoveDistance;
        public CardModifier Modifier;
        public float MoveMotionTime;
        public float AttackMotionTime;
        public MovePenetrationMode PenetrationMode;
        public float StartupTime;
        public float RecoveryTime;
        public bool ExtendRecoveryDuringDerivative;
        public bool IsInstantEffect;
        public ExceedLevel ExceedLevel;
        public CardSubCategory SubCategory;
        public AttackProperty Properties;
        public int? PoiseDamage;
        public int HitInvincibilityMs;
        public int? StartupFrames;
        public int? RecoveryFrames;
        public int? RecoveryCancelAfterFrames;
        public static PortableCardDefinition Capture(string key, Card card)
        {
            if(card==null||card.TuningCount!=0||card.IsEffectCopy)throw new NotSupportedException("加工/付与定義付きカードの輸送は未対応です");
            return new PortableCardDefinition { Key=key,Hash=CollectionDisplay.DefinitionHash(card),TuningDefinition=card.TuningDefinition==null?null:PortableTuningGrant.Capture(card.TuningDefinition.Id,card.TuningDefinition),
                Id=card.Id,
                Name=card.Name,
                Cost=card.Cost,
                Rarity=card.Rarity,
                Category=card.Category,
                Element=card.Element,
                Series=card.Series?.Name,
                Power=card.Power,
                Description=card.Description,
                TargetType=card.TargetType,
                PanelChangeType=card.PanelChangeType,
                MoveDirection=card.MoveDirection,
                MoveDistance=card.MoveDistance,
                Modifier=card.Modifier.Clone(),
                MoveMotionTime=card.MoveMotionTime,
                AttackMotionTime=card.AttackMotionTime,
                PenetrationMode=card.PenetrationMode,
                StartupTime=card.StartupTime,
                RecoveryTime=card.RecoveryTime,
                ExtendRecoveryDuringDerivative=card.ExtendRecoveryDuringDerivative,
                IsInstantEffect=card.IsInstantEffect,
                ExceedLevel=card.ExceedLevel,
                SubCategory=card.SubCategory,
                Properties=card.Properties,
                PoiseDamage=card.PoiseDamage,
                HitInvincibilityMs=card.HitInvincibilityMs,
                StartupFrames=card.StartupFrames,
                RecoveryFrames=card.RecoveryFrames,
                RecoveryCancelAfterFrames=card.RecoveryCancelAfterFrames };
        }
        public Card Restore()
        {
            if(string.IsNullOrWhiteSpace(Key)||Hash==null||Modifier==null||Modifier.AdditionalEffects==null)throw new ArgumentException("カード定義が欠損しています");
            var card=new Card(Id, Name, Cost, Rarity, Category, Element, Series == null ? null : new CardSeries(Series), Power, Description, TargetType, PanelChangeType, MoveDirection, MoveDistance, Modifier.Clone(), MoveMotionTime, AttackMotionTime, PenetrationMode, StartupTime, RecoveryTime, ExtendRecoveryDuringDerivative, IsInstantEffect, ExceedLevel, SubCategory, Properties, PoiseDamage, HitInvincibilityMs, StartupFrames, RecoveryFrames, RecoveryCancelAfterFrames);
            if(TuningDefinition!=null)card=card.WithTuningDefinition(TuningDefinition.Restore());
            if(CollectionDisplay.DefinitionHash(card)!=Hash)throw new ArgumentException("カード定義のhashが一致しません");return card;
        }
    }
    public sealed class PortableEnemyDefinition
    {
        public string Id,Name;public int Hp,Element,X,Y;public int[] Resistances,Status;public int? Poise,Stagger,Recovery,Dash,DashValue;
        public static PortableEnemyDefinition Capture(Enemy e)=>new PortableEnemyDefinition{Id=e.EnemyId,Name=e.Name,Hp=e.MaxHp,Element=(int)e.Element,X=e.GridPosition.X,Y=e.GridPosition.Y,
            Resistances=new[]{e.Resistances.Fire,e.Resistances.Water,e.Resistances.Wind,e.Resistances.Earth},Status=new[]{e.StatusResistances.Poison,e.StatusResistances.Paralyze,e.StatusResistances.Confusion,e.StatusResistances.Blind},
            Poise=e.PoiseDefinition?.Maximum,Stagger=e.PoiseDefinition?.StaggerFrames,Recovery=e.PoiseDefinition?.RecoveryDelayFrames,Dash=(int?)e.DashContact?.Outcome,DashValue=e.DashContact?.Value};
        public Enemy Restore(){if(Resistances?.Length!=4||Status?.Length!=4)throw new ArgumentException("敵耐性が欠損しています");
            return new Enemy(Id,Name,Hp,(ElementType)Element,new Vector2Int(X,Y),resistances:new ElementResistances(Resistances[0],Resistances[1],Resistances[2],Resistances[3]),
                statusResistances:new TimedStatusResistances(Status[0],Status[1],Status[2],Status[3]),poiseDefinition:Poise.HasValue?new EnemyPoiseDefinition(Poise.Value,Stagger.Value,Recovery):null,
                dashContact:Dash.HasValue?new EnemyDashContactDefinition((DashContactOutcome)Dash.Value,DashValue.Value):null);}
    }
    public sealed class PortableCellDefinition
    {
        public int Cell;public float Height;public PortableEnemyDefinition Enemy;
        public string ItemId,ItemName,ObstacleId,ObstacleName;public int Heal,Penalty,Value;public int[] Control;
        public static PortableCellDefinition Capture(MapCell c){var o=c.Obstacle;return new PortableCellDefinition{Cell=c.Index,Height=c.Height,Enemy=c.Enemy==null?null:PortableEnemyDefinition.Capture(c.Enemy),
            ItemId=c.Item?.Id,ItemName=c.Item?.Name,Heal=c.Item?.Heal??0,ObstacleId=o?.ObstacleId,ObstacleName=o?.Name,Penalty=(int)(o?.PenaltyType??0),Value=o?.PenaltyValue??0,
            Control=o?.Control==null?null:new[]{o.Control.DurationFrames,o.Control.IsRestraint?1:0,o.Control.DamagePerPulse,o.Control.DamageIntervalFrames}};}
        public MapCell Restore(){if(Enemy!=null)return new MapCell(Cell,Enemy.Restore(),Height);if(ItemId!=null)return new MapCell(Cell,new FieldItem(ItemId,ItemName,Heal));
            if(ObstacleId!=null){var c=Control==null?null:Control.Length==4?new ObstacleControl(Control[0],Control[1]!=0,Control[2],Control[3]):throw new ArgumentException("障害物制御が欠損しています");
                if(Penalty!=(int)ObstaclePenaltyType.ImmediateDamage&&Penalty!=(int)ObstaclePenaltyType.CardDiscard)throw new NotSupportedException("未対応の障害物です");
                return new MapCell(Cell,Penalty==(int)ObstaclePenaltyType.CardDiscard?Obstacle.CreateCardDiscardObstacle(ObstacleId,ObstacleName,Value,c):new Obstacle(ObstacleId,ObstacleName,Value,c));}
            return new MapCell(Cell);}
    }
    public sealed class PortableVisitOption
    {
        public string Id;public long Price;public int Heal,Remove;public PortableCardDefinition Card;
        public static PortableVisitOption Capture(DungeonVisitOption o)=>new PortableVisitOption{Id=o.Id,Price=o.Price,Heal=o.Heal,Remove=o.RemoveCount,Card=o.Card==null?null:PortableCardDefinition.Capture("visit",o.Card)};
        public DungeonVisitOption Restore()=>new DungeonVisitOption(Id,Price,Card?.Restore(),Heal,Remove);
    }
    public sealed class PortableVisitDefinition
    {
        public int Cell,Slots;public bool Shop,AllowDecline;public PortableVisitOption[] Options;
        public static PortableVisitDefinition Capture(DungeonVisitDefinition v)=>new PortableVisitDefinition{Cell=v.Cell,Slots=v.Slots,Shop=v.IsShop,AllowDecline=v.AllowDecline,Options=v.Options.Select(PortableVisitOption.Capture).ToArray()};
        public DungeonVisitDefinition Restore()=>new DungeonVisitDefinition(Cell,Shop,Options.Select(o=>o.Restore()).ToArray(),Slots,AllowDecline);
    }
    public sealed class PortableItemDefinition {public string Id,Name;public int Heal,Weight;}
    public sealed class PortableMapDefinition
    {
        public string Id,Name;public int InitialDeckSize,ItemChance;public PortableCellDefinition[] Cells;public PortableVisitDefinition[] Visits;public PortableItemDefinition[] Items;
        public int[] Strong;public int[][] Terrain;
        public static PortableMapDefinition Capture(DungeonConfig m)=>new PortableMapDefinition{Id=m.DungeonId,Name=m.DungeonName,InitialDeckSize=m.InitialDeckSize,ItemChance=m.ItemRewardChance,
            Cells=m.MapCells.Select(PortableCellDefinition.Capture).ToArray(),Visits=m.Visits.Select(PortableVisitDefinition.Capture).ToArray(),Strong=m.StrongEnemyCells.ToArray(),
            Items=m.ItemRewards.Select(x=>new PortableItemDefinition{Id=x.Item.Id,Name=x.Item.Name,Heal=x.Item.Heal,Weight=x.Weight}).ToArray(),
            Terrain=m.ExplorationTerrain.Select(t=>new[]{t.Cell,(int)t.Type,t.Heal,t.Damage,t.FallDamage}).ToArray()};
        public DungeonConfig Restore(){if(Cells==null||Visits==null||Items==null||Strong==null||Terrain==null||Terrain.Any(t=>t?.Length!=5))throw new ArgumentException("マップ定義が欠損しています");
            return new DungeonConfig(Id,Name,Cells.Length,Cells.Select(c=>c.Restore()).ToList(),InitialDeckSize,Visits.Select(v=>v.Restore()).ToArray(),Strong,
                Items.Select(i=>new ItemRewardEntry(new FieldItem(i.Id,i.Name,i.Heal),i.Weight)).ToArray(),ItemChance,Terrain.Select(t=>new ExplorationTerrainCell(t[0],(PanelType)t[1],t[2],t[3],t[4])).ToArray());}
    }
    public sealed class PortableBattleRules
    {
        public int ChargeFrames,IntervalFrames,HitInvincibilityMs,StartupFrames,RecoveryFrames;public int? CancelAfterFrames;
        public float GaugeSpeed;
        public void Validate(){if(ChargeFrames<1||IntervalFrames<1||HitInvincibilityMs<0||float.IsNaN(GaugeSpeed)||float.IsInfinity(GaugeSpeed)||GaugeSpeed<=0)throw new ArgumentException("戦闘ルール値が不正です");_ = new PlayerAttackTiming(StartupFrames,RecoveryFrames,CancelAfterFrames);}
    }
    public sealed class PortableBattlePackage
    {
        public int Version=1;public string Revision,MapHash,CatalogHash;public PortableMapDefinition Map;public PortableCardDefinition[] Cards;
        public PortableFloorRoute FloorRoute;public PortableProcessingCatalog Processing;public PortableTuningGrant[] Grants;
        public byte[] CheckpointEnvelope;public uint ReplaySeed;public uint? RandomState;public long RandomDraws;public PortableBattleRules Rules;
        public static PortableBattlePackage Capture(CheckpointDefinitions definitions,byte[] checkpoint,PortableBattleRules rules,uint replaySeed)
        {
            if(definitions==null)throw new ArgumentNullException(nameof(definitions));
            var result=new PortableBattlePackage{Revision=definitions.Revision,MapHash=definitions.MapHash,CatalogHash=definitions.CardCatalogHash,Map=PortableMapDefinition.Capture(definitions.CreateInitialMap()),
                Grants=definitions.PortableGrants.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>PortableTuningGrant.Capture(p.Key,p.Value)).ToArray(),Cards=definitions.PortableCards.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>PortableCardDefinition.Capture(p.Key,p.Value)).ToArray(),CheckpointEnvelope=(byte[])checkpoint.Clone(),Rules=rules,ReplaySeed=replaySeed};
            var saved=PortableCheckpointJson.Decode(checkpoint).Battle?.Random;
            if(saved!=null){saved.Validate();result.ReplaySeed=saved.Seed;result.RandomState=saved.State;result.RandomDraws=saved.Draws;}
            result.Restore(out _);return result;
        }
        public static PortableBattlePackage Capture(FloorSequenceDefinitions definitions,byte[] checkpoint,PortableBattleRules rules,uint replaySeed)
        {
            var envelope=PortableCheckpointJson.DecodeFloor(checkpoint);
            if(!definitions.TryRestore(envelope,out var restored,out var error))throw new InvalidDataException(error);
            var result=Capture(definitions.Floor(envelope.FloorIndex),PortableCheckpointJson.Encode(envelope.State),rules,replaySeed);
            result.FloorRoute=PortableFloorRoute.Capture(definitions,checkpoint);
            result.Restore(out _);return result;
        }
        public CheckpointDefinitions CreateCheckpointDefinitions(){Restore(out _,out var definitions);return definitions;}
        public RestoredCheckpoint Restore(out PortableCardCatalog catalog)=>Restore(out catalog,out _);
        internal RestoredCheckpoint Restore(out PortableCardCatalog catalog,out CheckpointDefinitions defs)
        {
            if(Version!=1||Map==null||Cards==null||Rules==null||CheckpointEnvelope==null)throw new ArgumentException("未対応版または欠損した輸送データです");Rules.Validate();
            if(RandomState==0||RandomDraws<0||(!RandomState.HasValue&&RandomDraws!=0))throw new ArgumentException("乱数状態が不正です");
            defs=new CheckpointDefinitions(Revision,Map.Restore());foreach(var card in Cards)defs.RegisterCard(card.Key,card.Restore());
            if(Grants!=null)foreach(var grant in Grants)defs.RegisterGrant(grant.Key,grant.Restore());
            if(Processing!=null)Processing.Restore();
            if(defs.MapHash!=MapHash||defs.CardCatalogHash!=CatalogHash)throw new ArgumentException("輸送された製品定義のhashが一致しません");
            if(FloorRoute!=null) {
                var selected=FloorRoute.Restore(out var state);
                if(selected.Revision!=Revision||selected.MapHash!=MapHash||selected.CardCatalogHash!=CatalogHash||
                    !PortableCheckpointJson.Encode(state).SequenceEqual(CheckpointEnvelope))
                    throw new ArgumentException("階層封筒と選択された戦闘定義が一致しません");
            }
            if(!CheckpointCodec.TryRestore(PortableCheckpointJson.Decode(CheckpointEnvelope),defs,out var restored,out var error))throw new InvalidDataException(error);
            if(restored.Snapshot.Boundary!=CheckpointBoundary.BattleStart)throw new NotSupportedException("この入口は戦闘開始チェックポイントのみ対応します");
            var saved=restored.Snapshot.Battle.Random;
            if(saved!=null&&(ReplaySeed!=saved.Seed||RandomState!=saved.State||RandomDraws!=saved.Draws))throw new ArgumentException("輸送側の乱数指定が保存状態と一致しません");
            catalog=new PortableCardCatalog(Cards.Select(c=>c.Restore()));return restored;
        }
        public BattleSimulation CreateSimulation()
        {
            var restored=Restore(out var catalog);var processing=Processing?.Restore();var r=Rules;var settings=new BattleSimulationSettings(restored.Player.MaxHp,1,r.ChargeFrames,r.IntervalFrames);
            return new BattleSimulation(settings,ReplaySeed,random=>{
                var battle=CheckpointBattleFactory.Create(restored.Player,new DungeonMap(restored.Map),restored.Deck,restored.Snapshot,restored.FirstHand,random,r.GaugeSpeed,exceed:processing?.Exceed,tuning:processing?.Tuning,fusion:processing?.Fusion,cardDatabase:catalog,checkpointRandomRestored:restored.Snapshot.Battle.Random!=null);
                battle.ConfigureDefaultHitInvincibility(r.HitInvincibilityMs);battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(r.StartupFrames,r.RecoveryFrames,r.CancelAfterFrames));return battle;
            },true,randomState:RandomState,randomDraws:RandomDraws);
        }
    }
    /// <summary>IDだけの参照が曖昧なら拒否。保存からの個体復元は必ず定義キー+hashを使う。</summary>
    public sealed class PortableCardCatalog : ICardDatabase
    {
        private readonly Card[] cards;
        public PortableCardCatalog(IEnumerable<Card> cards){this.cards=cards.GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First().Clone()).ToArray();}
        public Card GetCardById(string id){var found=cards.Where(c=>c.Id==id&&c.ExceedLevel==ExceedLevel.Normal).ToArray();if(found.Length>1)throw new InvalidOperationException("同ID別定義はIDだけでは解決できません");return found.SingleOrDefault()?.Clone();}
    }
}
