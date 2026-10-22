using System;
using System.Linq;
using Olca.Core.Logic.Battle;

internal static class Program
{
    private static int Main(string[] args)
    {
        try {
            uint seed = 123; int frames = 240, batch = 1, work = 0; bool verify = false, custom = false;
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--custom") { custom = true; continue; }
                if (args[i] == "--verify") { verify = true; continue; }
                if (i + 1 >= args.Length) throw new ArgumentException("値が必要です: " + args[i]);
                string key = args[i], value = args[++i];
                switch (key) { case "--seed": seed = uint.Parse(value); break; case "--frames": frames = int.Parse(value); break;
                    case "--batch": batch = int.Parse(value); break; case "--work": work = int.Parse(value); break; default: throw new ArgumentException("不明な引数: " + key); }
            }
            if (work < 0 || work > 1000000) throw new ArgumentOutOfRangeException(nameof(work));
            var references = typeof(BattleSimulation).Assembly.GetReferencedAssemblies();
            if (references.Any(a => a.Name.StartsWith("Unity", StringComparison.Ordinal) || a.Name.Contains("UniTask"))) throw new InvalidOperationException("Unity依存が混入しています");
            void Work(int load) { long scratch = 1; for (int n = 0; n < load; n++) scratch = unchecked(scratch * 33 + n); GC.KeepAlive(scratch); }
            string Run(int size, int load) => custom ? CustomBattleReplay.Trace(seed, size, () => Work(load)) : BattleSimulation.SampleTrace(seed, frames, size, () => Work(load));
            string trace = Run(batch, work);
            if (verify) {
                foreach (int size in new[] { 1, 7, 31, 240 }) if (trace != Run(size, 2000)) throw new InvalidOperationException("バッチ/負荷で状態が変化しました");
                Console.Error.WriteLine("PASS same-source " + (custom ? "custom-command" : "frame") + " replay; frames=" + frames + "; seed=" + seed + "; references=" + string.Join(",", references.Select(a => a.Name)));
            }
            Console.Write(trace); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; }
    }
}
