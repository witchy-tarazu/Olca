using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
namespace Olca.Core.Logic.Battle
{
    /// <summary>輸送された正規定義と保存済み初回手札を再生し、再抽選しない。</summary>
    public static class PortableBattleReplay
    {
        public static IReadOnlyList<BattleFrameCommand> Commands(PortableBattlePackage package,int frames=360)
        {
            if(frames<1||frames>10000)throw new ArgumentOutOfRangeException(nameof(frames));
            var s=package.CreateSimulation();var result=new List<BattleFrameCommand>();
            foreach(var card in s.Battle.CustomScreen.CurrentHand.Take(3))result.Add(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.Select,s.Battle.CustomScreen.Commands.HandleOf(card))));
            result.Add(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.Confirm)));result.Add(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.ReturnToBattle)));
            for(int i=1;i<=frames;i++){
                var input=new LogicalInput{HasInputSample=true};if(i<=36)input.Horizontal=1;if(i==1)input.HorizontalDown=1;
                if(i==20||i==40||i==60)input.CardUseButtonDown=true;
                if(i>=100&&(i-100)%18==0)input.BasicAttackButton=input.BasicAttackButtonDown=true;
                if(i>=100&&(i-100)%18==1)input.BasicAttackButtonUp=true;
                result.Add(new BattleFrameCommand(input));
            }return result.AsReadOnly();
        }
        public static string Trace(PortableBattlePackage package,int batch=1,Action renderWork=null)
        {
            if(batch<1)throw new ArgumentOutOfRangeException(nameof(batch));var simulation=package.CreateSimulation();var commands=Commands(package);var trace=new StringBuilder();
            for(int start=0;start<commands.Count;start+=batch){renderWork?.Invoke();for(int i=start;i<Math.Min(commands.Count,start+batch);i++)trace.Append(simulation.Step(commands[i]).Canonical).Append('\n');}return trace.ToString();
        }
    }
}
