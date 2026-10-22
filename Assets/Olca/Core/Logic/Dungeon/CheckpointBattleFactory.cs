using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>既存DungeonSystemの戦闘開始復帰を共用。報酬再抽選・途中保存・ダッシュ初撃は行わない。</summary>
    public static class CheckpointBattleFactory
    {
        public const float DefaultGaugeSpeed=2f;
        public static PlayerAttackTiming ProductBasicTiming => new PlayerAttackTiming(0,12,6);
        public static void ApplyTerrain(BattleSystem battle,DungeonMap map,int origin)
        {for(int x=0;x<BattleField.GRID_WIDTH;x++){var terrain=map.GetTerrain(origin+x);if(terrain!=null)battle.Field.ChangePanelType(new Vector2Int(x,0),map.GetTerrainType(origin+x),new BattlePanelValues(terrain.Heal,terrain.Damage));}}
        public static BattleSystem Create(PlayerCharacter player,DungeonMap map,Deck deck,CheckpointDto snapshot,IReadOnlyList<Card> firstHand,
            IRandomProvider random,float gaugeSpeed=DefaultGaugeSpeed,IGameLogger logger=null,ExceedSystem exceed=null,TuningSystem tuning=null,FusionSystem fusion=null,ICardDatabase cardDatabase=null)
        {
            if(snapshot?.Boundary!=CheckpointBoundary.BattleStart)throw new ArgumentException("戦闘開始境界だけ復帰できます");var plan=snapshot.Battle;
            map.RestoreTerrainChanges(snapshot.TerrainChanges);
            foreach(var enemy in snapshot.Enemies.Where(e=>plan.ParticipantCells.Contains(e.CellIndex)))map.GetCell(enemy.CellIndex).Enemy.MoveTo(new Vector2Int(enemy.GridX,enemy.GridY));
            player.MoveTo(new Vector2Int(snapshot.Player.GridX,snapshot.Player.GridY));
            var battle=new BattleSystem(player,plan.ParticipantCells.Select(i=>map.GetCell(i).Enemy).ToList(),deck,gaugeSpeed,random,logger,exceed,tuning,fusion,cardDatabase:cardDatabase);
            ApplyTerrain(battle,map,(int)plan.GridOriginX);battle.CustomScreen.RestoreCheckpointHand(firstHand);return battle;
        }
    }
}
