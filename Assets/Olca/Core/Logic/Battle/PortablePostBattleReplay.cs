using System;
using System.Text;
using Olca.Core.Logic.Dungeon;
namespace Olca.Core.Logic.Battle
{
    /// <summary>固定の操作列による勝利後の受取／スキップ確認。報酬の計算はManagerへ委譲する。</summary>
    public static class PortablePostBattleReplay
    {
        public static string Trace(PortableBattlePackage package,bool receive,int batch=1,Action work=null,Func<CheckpointDefinitions,ICheckpointStore> store=null,Action<PortableRunCheckpoint> completed=null)
        {
            if(batch<1)throw new ArgumentOutOfRangeException(nameof(batch));var session=new PortableBattleSession(package,store);var commands=PortableBattleReplay.Commands(package);var text=new StringBuilder(session.Observe()).Append('\n');
            for(int start=0;start<commands.Count;start+=batch){work?.Invoke();for(int i=start;i<Math.Min(commands.Count,start+batch);i++)text.Append(session.Step(commands[i])).Append('\n');}
            if(session.Simulation.Battle.State!=BattleState.Victory)throw new InvalidOperationException("この固定操作列では勝利に到達していません");
            foreach(var command in new[]{new PostBattleCommand(PostBattleCommandKind.Card,receive?0:-1),new PostBattleCommand(PostBattleCommandKind.Item,receive:receive),new PostBattleCommand(PostBattleCommandKind.Currency,receive:receive)})text.Append(session.Execute(command)).Append('\n');
            // 同じ報酬操作を繰り返しても確定済み結果は変わらない。
            foreach(var command in new[]{new PostBattleCommand(PostBattleCommandKind.Card,0),new PostBattleCommand(PostBattleCommandKind.Item,receive:true),new PostBattleCommand(PostBattleCommandKind.Currency,receive:true)})text.Append(session.Execute(command)).Append('\n');
            var result=session.CaptureResult();text.Append(result.Canonical()).Append('\n');completed?.Invoke(result);return text.ToString();
        }
    }
}
