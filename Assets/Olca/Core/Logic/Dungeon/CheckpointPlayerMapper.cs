using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>検証済みのチェックポイントとPlayerの対応表。保存境界/一時状態の許可はSession/Codecが決める。</summary>
    internal static class CheckpointPlayerMapper
    {
        internal static CheckpointPlayerDto Capture(PlayerCharacter player)
        {
            return new CheckpointPlayerDto {
                CustomizationAbilities = player.DepartureAbilities?.Capture(),
                RawBaseFireResistance = player.DepartureAbilities==null?(int?)null:player.BaseElementResistances.Fire,
                RawBaseWaterResistance = player.DepartureAbilities==null?(int?)null:player.BaseElementResistances.Water,
                RawBaseWindResistance = player.DepartureAbilities==null?(int?)null:player.BaseElementResistances.Wind,
                RawBaseEarthResistance = player.DepartureAbilities==null?(int?)null:player.BaseElementResistances.Earth,
                PoisonResistance = player.BaseStatusResistances.Poison,
                ParalyzeResistance = player.BaseStatusResistances.Paralyze,
                ConfusionResistance = player.BaseStatusResistances.Confusion,
                BlindResistance = player.BaseStatusResistances.Blind,
                AttackGrowth = player.AttackGrowth?.Capture(),
                Style = player.DepartureStyle.Id == DepartureStyleDefinition.BasicId && player.DepartureStyle.TransformationPhaseBonus == 0 ? null : player.DepartureStyle.Capture(),
                Poisons = player.CapturePoisons(),
                Hp = player.CurrentHp,
                MaxHp = player.MaxHp,
                AttackPower = player.AttackPower,
                Poise = player.Poise,
                StaggerFrames = player.StaggerDurationFrames,
                PoiseRecoveryFrames = player.PoiseRecoveryDefinitionFrames,
                FireResistance = Math.Min(100, player.BaseElementResistances.Fire),
                WaterResistance = Math.Min(100, player.BaseElementResistances.Water),
                WindResistance = Math.Min(100, player.BaseElementResistances.Wind),
                EarthResistance = Math.Min(100, player.BaseElementResistances.Earth),
                CustomSpeed = player.CustomSpeed,
                GridX = player.GridPosition.X,
                GridY = player.GridPosition.Y
            };
        }
        internal static PlayerCharacter Restore(CheckpointPlayerDto p)
        {
            var player = new PlayerCharacter(p.MaxHp, new Vector2Int(p.GridX, p.GridY), p.AttackPower, p.CustomSpeed, p.Poise, p.StaggerFrames, p.PoiseRecoveryFrames,new TimedStatusResistances(p.PoisonResistance,p.ParalyzeResistance,p.ConfusionResistance,p.BlindResistance));
            if(p.CustomizationAbilities!=null)player.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(p.CustomizationAbilities));
            if(p.AttackGrowth!=null)player.SetAttackGrowth(p.AttackGrowth.Restore());
            player.SetDepartureStyle(p.Style?.Restore() ?? DepartureStyleDefinition.Basic);
            player.SetElementResistances(new ElementResistances(p.RawBaseFireResistance??p.FireResistance, p.RawBaseWaterResistance??p.WaterResistance, p.RawBaseWindResistance??p.WindResistance, p.RawBaseEarthResistance??p.EarthResistance));
            player.TakeDamage(p.MaxHp - p.Hp, affectsPoise: false);
            player.RestorePoisons(p.Poisons);
            return player;
        }
    }
}
