using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Logic.Dungeon;
namespace Olca.Core.Logic.Battle
{
    /// <summary>輸送した加工規則を通常カスタムコマンドで再生する。加工判定をCLIへ複製しない。</summary>
    public static class PortableProcessingReplay
    {
        public static IReadOnlyList<BattleFrameCommand> Commands(PortableBattlePackage package)
        {
            var s=package.CreateSimulation();var result=new List<BattleFrameCommand>();var screen=s.Battle.CustomScreen;
            void Add(CustomCommandKind kind,params long[] handles){var c=new BattleFrameCommand(custom:new CustomScreenCommand(kind,handles));result.Add(c);s.Step(c);}
            foreach(var operation in new[]{CustomizationOperation.Exceed,CustomizationOperation.Tuning,CustomizationOperation.Fusion}) {
                var hand=screen.CurrentHand.ToArray();long[] found=null;
                for(int i=0;i<hand.Length&&found==null;i++)for(int j=i+1;j<hand.Length;j++){
                    var preview=screen.PreviewCustomization(new[]{hand[i],hand[j]});
                    if(preview.CanExecute&&preview.Operation==operation){found=new[]{screen.Commands.HandleOf(hand[i]),screen.Commands.HandleOf(hand[j])};break;}
                }
                if(found==null)continue;
                Add(CustomCommandKind.PreviewCustomization,found);Add(CustomCommandKind.CancelCustomization);
                Add(CustomCommandKind.ExecuteCustomization,found);Add(CustomCommandKind.ExecuteCustomization,found);Add(CustomCommandKind.Undo);
            }
            foreach(var card in screen.CurrentHand.Take(2).ToArray())Add(CustomCommandKind.Select,screen.Commands.HandleOf(card));
            Add(CustomCommandKind.Confirm);Add(CustomCommandKind.Confirm);Add(CustomCommandKind.ReturnToBattle);
            return result.AsReadOnly();
        }
        public static string Trace(PortableBattlePackage package,int batch=1,Action renderWork=null)
        {
            if(batch<1)throw new ArgumentOutOfRangeException(nameof(batch));var s=package.CreateSimulation();var commands=Commands(package);var b=new StringBuilder(s.InitialObservation.Canonical).Append('\n');
            for(int start=0;start<commands.Count;start+=batch){renderWork?.Invoke();for(int i=start;i<Math.Min(commands.Count,start+batch);i++)b.Append(s.Step(commands[i]).Canonical).Append('\n');}return b.ToString();
        }
    }
}
