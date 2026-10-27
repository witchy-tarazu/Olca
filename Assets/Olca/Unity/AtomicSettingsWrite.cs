using System;
using System.IO;
using System.Linq;

namespace Olca.Unity
{
    /// <summary>設定ファイルの確定処理だけを共有する。入力・読込・既定値は各Storeが所有する。</summary>
    internal static class AtomicSettingsWrite
    {
        internal static bool TryCommit(CheckpointFileIo io, string directory, string active, string backup,
            string prefix, byte[] bytes, Action<byte[]> validate, Func<Exception, bool> failure, out string temporary)
        {
            temporary = null;
            try
            {
                io.RequireWritable(active, backup);
                io.CreateDirectory(directory);
                temporary = Path.Combine(directory, prefix + Guid.NewGuid().ToString("N") + ".tmp");
                io.WriteAndFlush(temporary, bytes);
                validate(File.ReadAllBytes(temporary));
                if (File.Exists(active)) io.Replace(temporary, active, backup);
                else io.MoveNew(temporary, active);
                if (!File.ReadAllBytes(active).SequenceEqual(bytes)) throw new IOException();
                return true;
            }
            catch (Exception e) when (failure(e))
            {
                // OSが置換後に失敗を返した場合も確定済み内容を照合する。
                try { if (File.Exists(active) && File.ReadAllBytes(active).SequenceEqual(bytes)) return true; }
                catch (Exception read) when (failure(read)) { }
                return false;
            }
        }

        internal static void Cleanup(string temporary, Func<Exception, bool> failure)
        {
            if (temporary == null) return;
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (failure(e)) { }
        }
    }
}
