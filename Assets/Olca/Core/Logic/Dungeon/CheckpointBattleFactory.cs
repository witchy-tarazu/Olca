using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>既存DungeonSystemの戦闘開始復帰を共用。報酬再抽選・途中保存は行わない。初撃は開始準備完了後に呼出元が適用する。</summary>
    public static class CheckpointBattleFactory
    {
        public const float DefaultGaugeSpeed=2f;
        public static PlayerAttackTiming ProductBasicTiming => new PlayerAttackTiming(0,12,6);
        public static void ApplyTerrain(BattleSystem battle,DungeonMap map,int origin)
        {for(int x=0;x<BattleField.GRID_WIDTH;x++){var terrain=map.GetTerrain(origin+x);if(terrain!=null)battle.Field.ChangePanelType(new Vector2Int(x,0),map.GetTerrainType(origin+x),new BattlePanelValues(terrain.Heal,terrain.Damage));}}
        public static BattleSystem Create(PlayerCharacter player,DungeonMap map,Deck deck,CheckpointDto snapshot,IReadOnlyList<Card> firstHand,
            IRandomProvider random,float gaugeSpeed=DefaultGaugeSpeed,IGameLogger logger=null,ExceedSystem exceed=null,TuningSystem tuning=null,FusionSystem fusion=null,ICardDatabase cardDatabase=null,bool checkpointRandomRestored=false,IEnemyAIProvider enemyAIProvider=null)
        {
            if(snapshot?.Boundary!=CheckpointBoundary.BattleStart)throw new ArgumentException("戦闘開始境界だけ復帰できます");var plan=snapshot.Battle;
            map.RestoreTerrainChanges(snapshot.TerrainChanges);
            foreach(var enemy in snapshot.Enemies.Where(e=>plan.ParticipantCells.Contains(e.CellIndex)))map.GetCell(enemy.CellIndex).Enemy.MoveTo(new Vector2Int(enemy.GridX,enemy.GridY));
            player.MoveTo(new Vector2Int(snapshot.Player.GridX,snapshot.Player.GridY));
            BattleRandomStateDto.ValidateRecord(plan.RandomVersion,plan.Random);
            var savedRandom=plan.Random;
            if(savedRandom!=null){
                savedRandom.Validate();
                if(checkpointRandomRestored){if(!(random is SeededBattleRandom seeded)||!savedRandom.Matches(seeded))throw new ArgumentException("呼出元の乱数が保存状態と一致しません");}
                else random=savedRandom.Restore();
            }else logger?.LogWarning("[Checkpoint] 旧保存に乱数状態がないため従来の外部乱数を使用します。旧ライブ列の再現は保証しません");
            var battle=new BattleSystem(player,plan.ParticipantCells.Select(i=>map.GetCell(i).Enemy).ToList(),deck,gaugeSpeed,random,logger,exceed,tuning,fusion,cardDatabase:cardDatabase,enemyAIProvider:enemyAIProvider);
            if(savedRandom!=null)battle.UseCheckpointRandom();
            battle.StartEffects.Configure(plan.DashContact,plan.DashEffect,plan.DashValue);
            ApplyTerrain(battle,map,(int)plan.GridOriginX);battle.CustomScreen.RestoreCheckpointHand(firstHand);return battle;
        }
    }
}
