using Olca.Core.Interfaces;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Debugを使用したロガー
    /// </summary>
    public class UnityLogger : IGameLogger
    {
        public void LogInfo(string message)
        {
            UnityEngine.Debug.Log($"[INFO] {message}");
        }

        public void LogWarning(string message)
        {
            UnityEngine.Debug.LogWarning($"[WARNING] {message}");
        }

        public void LogError(string message)
        {
            UnityEngine.Debug.LogError($"[ERROR] {message}");
        }
    }
}

namespace Olca.Unity
{
    /// <summary>OS失敗を権限変更なしで検証できるファイル操作境界。</summary>
    public class CheckpointFileIo
    {
        public virtual void WriteAndFlush(string path, byte[] bytes)
        {
            using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew,
                System.IO.FileAccess.Write, System.IO.FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public virtual void Replace(string temporary, string destination, string backup)
            => System.IO.File.Replace(temporary, destination, backup);
        public virtual void MoveNew(string temporary, string destination) => System.IO.File.Move(temporary, destination);
    }

    /// <summary>既存ファイルを検証してから更新する。未知版/破損は自動修復・上書きしない。</summary>
    public sealed class FileCheckpointStore : Olca.Core.Logic.Dungeon.ICheckpointStore, Olca.Core.Logic.Dungeon.ICheckpointReadStatus
    {
        [System.Runtime.Serialization.DataContract]
        private sealed class Envelope
        {
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Payload;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Sha256;
        }
        private readonly Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions;
        private readonly CheckpointFileIo io;
        public string DirectoryPath { get; }
        public string ActivePath => System.IO.Path.Combine(DirectoryPath, "active.json");
        public string BackupPath => System.IO.Path.Combine(DirectoryPath, "active.backup.json");
        public string ReadError { get; private set; }
        public bool HasExistingData => System.IO.File.Exists(ActivePath) || System.IO.Directory.Exists(ActivePath) ||
            System.IO.File.Exists(BackupPath) || System.IO.Directory.Exists(BackupPath);
        public FileCheckpointStore(Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions, string directory, CheckpointFileIo io = null)
        {
            this.definitions = definitions ?? throw new System.ArgumentNullException(nameof(definitions));
            DirectoryPath = System.IO.Path.GetFullPath(directory ?? throw new System.ArgumentNullException(nameof(directory)));
            this.io = io ?? new CheckpointFileIo();
        }
        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }
        private static T Deserialize<T>(byte[] bytes)
        {
            using (var stream = new System.IO.MemoryStream(bytes))
                return (T)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static byte[] Encode(Olca.Core.Domain.CheckpointDto dto)
        {
            var payload = Serialize(dto);
            return Serialize(new Envelope { Version = 1, Payload = System.Text.Encoding.UTF8.GetString(payload), Sha256 = Hash(payload) });
        }
        private Olca.Core.Domain.CheckpointDto Decode(string path)
        {
            var info = new System.IO.FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > 16 * 1024 * 1024)
                throw new System.IO.InvalidDataException("保存ファイルが存在しないかサイズが不正です");
            var envelope = Deserialize<Envelope>(System.IO.File.ReadAllBytes(path));
            if (envelope == null || envelope.Version != 1 || envelope.Payload == null ||
                Hash(System.Text.Encoding.UTF8.GetBytes(envelope.Payload)) != envelope.Sha256)
                throw new System.IO.InvalidDataException("保存形式が未対応、または整合性検証に失敗しました");
            var dto = Deserialize<Olca.Core.Domain.CheckpointDto>(System.Text.Encoding.UTF8.GetBytes(envelope.Payload));
            if (!Olca.Core.Logic.Dungeon.CheckpointCodec.TryRestore(dto, definitions, out var verified, out var error))
                throw new System.IO.InvalidDataException(error);
            // 読み取りは正規化済みコピーを返すだけで、元ファイルには書き込まない。
            return verified.Snapshot;
        }
        private static bool StorageFailure(System.Exception ex) => ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is System.UnauthorizedAccessException ||
            ex is System.Runtime.Serialization.SerializationException || ex is System.ArgumentException || ex is System.Security.SecurityException || ex is System.Xml.XmlException;
        private static string FailureMessage(System.Exception ex)
        {
            if (ex is System.IO.InvalidDataException) return ex.Message;
            if (ex is System.UnauthorizedAccessException || ex is System.Security.SecurityException)
                return "保存先にアクセスできません。アクセス権限を確認してください。";
            if (ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException)
                return "保存ファイルの形式が破損しています。既存データは上書きしません。";
            if (ex is System.IO.IOException)
                return "保存ファイルの入出力に失敗しました。空き容量や保存先を確認してください。";
            return "保存内容または保存先が不正です。既存データは上書きしません。";
        }
        public Olca.Core.Domain.CheckpointDto Read()
        {
            ReadError = null;
            if (!HasExistingData) return null;
            try { return Decode(ActivePath); }
            catch (System.Exception ex) when (StorageFailure(ex)) { ReadError = FailureMessage(ex); return null; }
        }
        public bool TryReadBackup(out Olca.Core.Domain.CheckpointDto dto, out string error)
        {
            dto = null; error = null;
            try { dto = Decode(BackupPath); return true; }
            catch (System.Exception ex) when (StorageFailure(ex)) { error = FailureMessage(ex); return false; }
        }
        public bool TryWrite(Olca.Core.Domain.CheckpointDto data, out string error)
        {
            error = null;
            if (!Olca.Core.Logic.Dungeon.CheckpointCodec.TryRestore(data, definitions, out var verified, out error)) return false;
            var snapshot = verified.Snapshot;
            byte[] expected = Encode(snapshot);
            string temporary = null;
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                using (var gate = new System.IO.FileStream(System.IO.Path.Combine(DirectoryPath, "active.lock"),
                    System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    bool exists = HasExistingData;
                    if (exists)
                    {
                        var previous = Decode(ActivePath);
                        if (previous.RunId != snapshot.RunId || previous.Sequence > snapshot.Sequence)
                            throw new System.IO.InvalidDataException("保存世代または攻略IDが競合しています");
                        if (previous.Sequence == snapshot.Sequence)
                        {
                            if (Hash(Encode(previous)) == Hash(expected)) return true; // 置換後に応答を失った再試行も安全。
                            throw new System.IO.InvalidDataException("同じ保存世代に異なる内容があります");
                        }
                    }
                    temporary = System.IO.Path.Combine(DirectoryPath, ".checkpoint-" + System.Guid.NewGuid().ToString("N") + ".tmp");
                    io.WriteAndFlush(temporary, expected);
                    var reread = Decode(temporary);
                    if (Hash(Encode(reread)) != Hash(expected)) throw new System.IO.InvalidDataException("書込後の再読込結果が一致しません");
                    if (exists) io.Replace(temporary, ActivePath, BackupPath);
                    else io.MoveNew(temporary, ActivePath);
                    temporary = null;
                    ReadError = null;
                    return true;
                }
            }
            catch (System.Exception ex) when (StorageFailure(ex))
            {
                // OSが置換を完了した後に例外を返した場合、実体確認なしに再実行しない。
                try { if (Hash(Encode(Decode(ActivePath))) == Hash(expected)) return true; }
                catch (System.Exception readFailure) when (StorageFailure(readFailure)) { }
                error = FailureMessage(ex); return false;
            }
            finally
            {
                // 自分で作成した未確定の一時ファイルだけを除去。本体/バックアップは削除しない。
                if (temporary != null)
                    try { System.IO.File.Delete(temporary); }
                    catch (System.Exception ex) when (StorageFailure(ex)) { }
            }
        }
    }
}

namespace Olca.Unity
{
    /// <summary>階層列専用の原子的保存。単階層active.jsonは読込・変更しない。</summary>
    public sealed class FloorSequenceFileStore : Olca.Core.Logic.Dungeon.IFloorSequenceStore
    {
        [System.Runtime.Serialization.DataContract]
        private sealed class Envelope
        {
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Payload;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Sha256;
        }
        private readonly Olca.Core.Logic.Dungeon.FloorSequenceDefinitions definitions;
        private readonly CheckpointFileIo io;
        public string DirectoryPath { get; }
        public string ActivePath => System.IO.Path.Combine(DirectoryPath, "floors.active.json");
        public string BackupPath => System.IO.Path.Combine(DirectoryPath, "floors.backup.json");
        public string ReadError { get; private set; }
        public bool HasExistingData => System.IO.File.Exists(ActivePath) || System.IO.Directory.Exists(ActivePath) ||
            System.IO.File.Exists(BackupPath) || System.IO.Directory.Exists(BackupPath);
        public FloorSequenceFileStore(Olca.Core.Logic.Dungeon.FloorSequenceDefinitions definitions, string directory, CheckpointFileIo io = null)
        {
            this.definitions = definitions ?? throw new System.ArgumentNullException(nameof(definitions));
            DirectoryPath = System.IO.Path.GetFullPath(directory ?? throw new System.ArgumentNullException(nameof(directory)));
            this.io = io ?? new CheckpointFileIo();
        }
        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }
        private static T Deserialize<T>(byte[] bytes)
        {
            using (var stream = new System.IO.MemoryStream(bytes))
                return (T)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static byte[] Encode(Olca.Core.Domain.FloorSequenceCheckpointDto dto)
        {
            var payload = Serialize(dto);
            return Serialize(new Envelope { Version = 1, Payload = System.Text.Encoding.UTF8.GetString(payload), Sha256 = Hash(payload) });
        }
        private Olca.Core.Domain.FloorSequenceCheckpointDto Decode(string path)
        {
            var info = new System.IO.FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > 16 * 1024 * 1024)
                throw new System.IO.InvalidDataException("保存ファイルが存在しないかサイズが不正です");
            var envelope = Deserialize<Envelope>(System.IO.File.ReadAllBytes(path));
            if (envelope == null || envelope.Version != 1 || envelope.Payload == null ||
                Hash(System.Text.Encoding.UTF8.GetBytes(envelope.Payload)) != envelope.Sha256)
                throw new System.IO.InvalidDataException("保存形式が未対応、または整合性検証に失敗しました");
            var dto = Deserialize<Olca.Core.Domain.FloorSequenceCheckpointDto>(System.Text.Encoding.UTF8.GetBytes(envelope.Payload));
            if (!definitions.TryRestore(dto, out var verified, out var error))
                throw new System.IO.InvalidDataException(error);
            // 読み取りは正規化済みコピーを返すだけで、元ファイルには書き込まない。
            return verified.Snapshot;
        }
        private static bool StorageFailure(System.Exception ex) => ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is System.UnauthorizedAccessException ||
            ex is System.Runtime.Serialization.SerializationException || ex is System.ArgumentException || ex is System.Security.SecurityException || ex is System.Xml.XmlException;
        private static string FailureMessage(System.Exception ex)
        {
            if (ex is System.IO.InvalidDataException) return ex.Message;
            if (ex is System.UnauthorizedAccessException || ex is System.Security.SecurityException)
                return "保存先にアクセスできません。アクセス権限を確認してください。";
            if (ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException)
                return "保存ファイルの形式が破損しています。既存データは上書きしません。";
            if (ex is System.IO.IOException)
                return "保存ファイルの入出力に失敗しました。空き容量や保存先を確認してください。";
            return "保存内容または保存先が不正です。既存データは上書きしません。";
        }
        public Olca.Core.Domain.FloorSequenceCheckpointDto Read()
        {
            ReadError = null;
            if (!HasExistingData) return null;
            try { return Decode(ActivePath); }
            catch (System.Exception ex) when (StorageFailure(ex)) { ReadError = FailureMessage(ex); return null; }
        }
        public bool TryReadBackup(out Olca.Core.Domain.FloorSequenceCheckpointDto dto, out string error)
        {
            dto = null; error = null;
            try { dto = Decode(BackupPath); return true; }
            catch (System.Exception ex) when (StorageFailure(ex)) { error = FailureMessage(ex); return false; }
        }
        public bool TryWrite(Olca.Core.Domain.FloorSequenceCheckpointDto data, out string error)
        {
            error = null;
            if (!definitions.TryRestore(data, out var verified, out error)) return false;
            var snapshot = verified.Snapshot;
            byte[] expected = Encode(snapshot);
            string temporary = null;
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                using (var gate = new System.IO.FileStream(System.IO.Path.Combine(DirectoryPath, "floors.lock"),
                    System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    bool exists = HasExistingData;
                    if (exists)
                    {
                        var previous = Decode(ActivePath);
                        if (snapshot.FloorIndex < previous.FloorIndex || snapshot.FloorIndex > previous.FloorIndex + 1 ||
                            (snapshot.FloorIndex != previous.FloorIndex &&
                             (snapshot.State.Boundary != Olca.Core.Domain.CheckpointBoundary.Departure || snapshot.State.PositionMilli != 0)))
                            throw new System.IO.InvalidDataException("階層を飛ばすか戻る保存はできません");
                        if (previous.State.RunId != snapshot.State.RunId || previous.State.Sequence > snapshot.State.Sequence)
                            throw new System.IO.InvalidDataException("保存世代または攻略IDが競合しています");
                        if (previous.State.Sequence == snapshot.State.Sequence)
                        {
                            if (Hash(Encode(previous)) == Hash(expected)) return true; // 置換後に応答を失った再試行も安全。
                            throw new System.IO.InvalidDataException("同じ保存世代に異なる内容があります");
                        }
                    }
                    temporary = System.IO.Path.Combine(DirectoryPath, ".floors-" + System.Guid.NewGuid().ToString("N") + ".tmp");
                    io.WriteAndFlush(temporary, expected);
                    var reread = Decode(temporary);
                    if (Hash(Encode(reread)) != Hash(expected)) throw new System.IO.InvalidDataException("書込後の再読込結果が一致しません");
                    if (exists) io.Replace(temporary, ActivePath, BackupPath);
                    else io.MoveNew(temporary, ActivePath);
                    temporary = null;
                    ReadError = null;
                    return true;
                }
            }
            catch (System.Exception ex) when (StorageFailure(ex))
            {
                // OSが置換を完了した後に例外を返した場合、実体確認なしに再実行しない。
                try { if (Hash(Encode(Decode(ActivePath))) == Hash(expected)) return true; }
                catch (System.Exception readFailure) when (StorageFailure(readFailure)) { }
                error = FailureMessage(ex); return false;
            }
            finally
            {
                // 自分で作成した未確定の一時ファイルだけを除去。本体/バックアップは削除しない。
                if (temporary != null)
                    try { System.IO.File.Delete(temporary); }
                    catch (System.Exception ex) when (StorageFailure(ex)) { }
            }
        }
    }
}
