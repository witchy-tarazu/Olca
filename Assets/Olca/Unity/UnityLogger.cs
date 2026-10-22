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
    /// <summary>検証コピー専用の書込許可範囲。設定は毎回読み直し、再読込後も既定保存先を拒否する。</summary>
    public sealed class ValidationStorageGuard
    {
        public const string ManifestName = ".olca-validation-storage-guard.json";
        [System.Serializable] public sealed class Configuration { public string Root; public string ForbiddenRoot; }
        private readonly string root, forbidden;
        public ValidationStorageGuard(string root, string forbiddenRoot)
        {
            this.root = Canonical(root); forbidden = Canonical(forbiddenRoot);
            if (Within(this.root, forbidden) || Within(forbidden, this.root))
                throw new System.IO.IOException("検証ルートと既定保存先は分離してください");
        }
        // シンボリックリンクは許可せず、既存の各祖先を照合する。未作成の末尾も同じ規則で正規化する。
        private static string Canonical(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
                throw new System.IO.IOException("検証保存先には絶対パスが必要です");
            // ../によるリンク祖先の消去も許可しない。保存先は呼出側で正規化した絶対パスを渡す。
            if (System.Array.IndexOf(path.Split(System.IO.Path.DirectorySeparatorChar), "..") >= 0)
                throw new System.IO.IOException("親参照を含む検証保存先は拒否します");
            string full = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(full)) throw new System.IO.IOException("ファイルシステム全体は検証ルートにできません");
            for (string current = full; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current)) {
                try { if ((System.IO.File.GetAttributes(current) & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new System.IO.IOException("リンクを経由する検証保存先は拒否します"); }
                catch (System.IO.FileNotFoundException) { }
                catch (System.IO.DirectoryNotFoundException) { }
            }
            return full;
        }
        private static System.StringComparison PathComparison => System.IO.Path.DirectorySeparatorChar == '\\'
            ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
        private static bool Within(string path, string parent) =>
            string.Equals(path, parent, PathComparison) ||
            path.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, PathComparison);
        public void Require(params string[] paths)
        {
            // 祖先の置換も検出するため、構築時だけでなく操作ごとに検査する。
            Canonical(root); Canonical(forbidden);
            if (paths == null || paths.Length == 0) throw new System.IO.IOException("検証保存先が未指定です");
            foreach (string path in paths) {
                string target = Canonical(path);
                if (!Within(target, root) || Within(target, forbidden))
                    throw new System.IO.IOException("検証領域外への書込を開始前に停止しました");
            }
        }
        public static ValidationStorageGuard FromEditorProject()
        {
#if UNITY_EDITOR
            string manifest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), ManifestName);
            if (System.IO.File.Exists(manifest)) {
                if ((System.IO.File.GetAttributes(manifest) & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new System.IO.IOException("検証設定のリンクは拒否します");
                var config = UnityEngine.JsonUtility.FromJson<Configuration>(System.IO.File.ReadAllText(manifest));
                if (config == null) throw new System.IO.IOException("検証保存先設定が不正です");
                return new ValidationStorageGuard(config.Root, config.ForbiddenRoot);
            }
#endif
            return null;
        }
        public static void RequireEditorStartup(string settings, string checkpoint, string progress, bool required = false)
        {
            // 未注入のsettingsはnullのまま拒否し、persistentDataPathにフォールバックさせない。
            var guard = FromEditorProject();
            if (required && guard == null) throw new System.IO.IOException("必須の検証保存先設定がありません");
            guard?.Require(settings, checkpoint, progress);
        }
    }

    /// <summary>OS失敗を権限変更なしで検証できるファイル操作境界。</summary>
    public class CheckpointFileIo
    {
        private readonly ValidationStorageGuard validation;
        private readonly bool editorGuardRequired;
        public CheckpointFileIo(ValidationStorageGuard validation = null)
        {
            this.validation = validation;
            editorGuardRequired = validation == null && ValidationStorageGuard.FromEditorProject() != null;
        }
        public void RequireWritable(params string[] paths)
        {
            var guard = validation ?? ValidationStorageGuard.FromEditorProject();
            if (editorGuardRequired && guard == null) throw new System.IO.IOException("検証保存先設定が失われたため停止しました");
            guard?.Require(paths);
        }
        public bool TryRequireWritable(string active, string backup, out string error)
        {
            try { RequireWritable(active, backup); error = null; return true; }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException || e is System.ArgumentException || e is System.Security.SecurityException) {
                error = "検証保存先ガードが書込を開始前に停止しました: " + e.Message; return false;
            }
        }
        public void CreateDirectory(string path) { RequireWritable(path); System.IO.Directory.CreateDirectory(path); }
        public virtual void WriteAndFlush(string path, byte[] bytes)
        {
            RequireWritable(path);
            using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew,
                System.IO.FileAccess.Write, System.IO.FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public virtual void Replace(string temporary, string destination, string backup)
        { RequireWritable(temporary, destination, backup); System.IO.File.Replace(temporary, destination, backup); }
        public virtual void MoveNew(string temporary, string destination)
        { RequireWritable(temporary, destination); System.IO.File.Move(temporary, destination); }
    }

    /// <summary>既存ファイルを検証してから更新する。未知版/破損は自動修復・上書きしない。</summary>
    public sealed class FileCheckpointStore : Olca.Core.Logic.Dungeon.ICheckpointStore, Olca.Core.Logic.Dungeon.ICheckpointReadStatus
    {
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
        private static string Hash(byte[] bytes) => Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(bytes);
        private static byte[] Encode(Olca.Core.Domain.CheckpointDto dto) => Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(dto);
        private Olca.Core.Domain.CheckpointDto Decode(string path)
        {
            var info = new System.IO.FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > 16 * 1024 * 1024)
                throw new System.IO.InvalidDataException("保存ファイルが存在しないかサイズが不正です");
            var dto = Olca.Core.Logic.Dungeon.PortableCheckpointJson.Decode(System.IO.File.ReadAllBytes(path));
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
                io.CreateDirectory(DirectoryPath);
                io.RequireWritable(System.IO.Path.Combine(DirectoryPath, "active.lock"));
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
                io.CreateDirectory(DirectoryPath);
                io.RequireWritable(System.IO.Path.Combine(DirectoryPath, "floors.lock"));
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

namespace Olca.Unity
{
    // 進行・帰還段階・受領台帳を一つの原子的文書へ保存し、二つのファイル間の部分確定を避ける。
    public sealed class ClearReturnFileStore : Olca.Core.Domain.IClearReturnStore, Olca.Core.Domain.INewClearReturnStore
    {
        [System.Runtime.Serialization.DataContract]
        private sealed class Envelope
        {
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Payload;
            [System.Runtime.Serialization.DataMember(IsRequired = true)] public string Sha256;
        }
        private readonly CheckpointFileIo io;
        private bool writing;
        public string DirectoryPath { get; }
        public string ActivePath => System.IO.Path.Combine(DirectoryPath, "progress.json");
        public string BackupPath => System.IO.Path.Combine(DirectoryPath, "progress.backup.json");
        public bool IsNewProfile => !System.IO.File.Exists(ActivePath) && !System.IO.Directory.Exists(ActivePath) &&
            !System.IO.File.Exists(BackupPath) && !System.IO.Directory.Exists(BackupPath);
        public ClearReturnFileStore(string directory, CheckpointFileIo io = null)
        {
            DirectoryPath = System.IO.Path.GetFullPath(directory ?? throw new System.ArgumentNullException(nameof(directory)));
            this.io = io ?? new CheckpointFileIo();
        }
        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new System.IO.MemoryStream()) {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray();
            }
        }
        private static T Deserialize<T>(byte[] bytes)
        {
            using (var stream = new System.IO.MemoryStream(bytes))
                return (T)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static byte[] Encode(Olca.Core.Domain.ClearReturnProfile profile)
        {
            var payload = Serialize(profile);
            return Serialize(new Envelope { Version = 1, Payload = System.Text.Encoding.UTF8.GetString(payload), Sha256 = Hash(payload) });
        }
        private static Olca.Core.Domain.ClearReturnProfile Decode(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) throw new System.IO.InvalidDataException();
            var envelope = Deserialize<Envelope>(bytes);
            if (envelope == null || envelope.Version != 1 || envelope.Payload == null || Hash(System.Text.Encoding.UTF8.GetBytes(envelope.Payload)) != envelope.Sha256)
                throw new System.IO.InvalidDataException();
            var profile = Deserialize<Olca.Core.Domain.ClearReturnProfile>(System.Text.Encoding.UTF8.GetBytes(envelope.Payload));
            Olca.Core.Domain.ClearReturnProfile.Validate(profile); return profile;
        }
        private static bool Failure(System.Exception e) => e is System.IO.IOException || e is System.UnauthorizedAccessException ||
            e is System.Runtime.Serialization.SerializationException || e is System.Xml.XmlException || e is System.ArgumentException || e is System.Security.SecurityException || e is System.OverflowException;
        public Olca.Core.Domain.ClearReturnProfile Read(out string error)
        {
            error = null;
            try {
                if (!System.IO.File.Exists(ActivePath)) {
                    if (System.IO.Directory.Exists(ActivePath) || System.IO.File.Exists(BackupPath) || System.IO.Directory.Exists(BackupPath)) throw new System.IO.InvalidDataException();
                    return new Olca.Core.Domain.ClearReturnProfile();
                }
                return Decode(System.IO.File.ReadAllBytes(ActivePath));
            } catch (System.Exception e) when (Failure(e)) { error = "進行データを読み込めません。既存データは変更していません。"; return null; }
        }
        public bool TryWrite(long expectedRevision, Olca.Core.Domain.ClearReturnProfile next, out string error)
        {
            error = null;
            if (writing) { error = "進行データを保存中です"; return false; }
            writing = true; string temporary = null; byte[] bytes = null;
            try {
                Olca.Core.Domain.ClearReturnProfile.Validate(next);
                bytes = Encode(next);
                io.CreateDirectory(DirectoryPath);
                io.RequireWritable(System.IO.Path.Combine(DirectoryPath, "progress.lock"));
                using (var fileLock = new System.IO.FileStream(System.IO.Path.Combine(DirectoryPath, "progress.lock"),
                    System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None)) {
                var existing = Read(out error); if (existing == null) return false;
                // OSが書換え後にエラーを返した同じ計画の再試行は、世代チェックより先に識別する。
                if (System.IO.File.Exists(ActivePath) && Equal(System.IO.File.ReadAllBytes(ActivePath), bytes)) return true;
                if (existing.Revision != expectedRevision || next.Revision != checked(expectedRevision + 1))
                { error = "進行データが別の処理で更新されています。再読込してください。"; return false; }
                io.CreateDirectory(DirectoryPath);
                // 新規開始の旧進行は通常backupの更新で失われない別名へ保全する。
                if(next.CheckpointGeneration!=existing.CheckpointGeneration&&System.IO.File.Exists(ActivePath)) {
                    var oldBytes=System.IO.File.ReadAllBytes(ActivePath);Decode(oldBytes);
                    var archive=System.IO.Path.Combine(DirectoryPath,"new-game-backups");io.CreateDirectory(archive);
                    var path=System.IO.Path.Combine(archive,"progress-"+existing.Revision+"-"+Hash(oldBytes)+".json");
                    if(!System.IO.File.Exists(path)){
                        temporary=System.IO.Path.Combine(archive,".preserve-"+System.Guid.NewGuid().ToString("N")+".tmp");
                        io.WriteAndFlush(temporary,oldBytes);
                        if(!Equal(System.IO.File.ReadAllBytes(temporary),oldBytes))throw new System.IO.IOException();
                        io.MoveNew(temporary,path);temporary=null;
                    }
                    if(!Equal(System.IO.File.ReadAllBytes(path),oldBytes))throw new System.IO.IOException("旧進行の保全に失敗しました");
                }
                temporary = System.IO.Path.Combine(DirectoryPath, ".progress-" + System.Guid.NewGuid().ToString("N") + ".tmp");
                io.WriteAndFlush(temporary, bytes); Decode(System.IO.File.ReadAllBytes(temporary));
                if (System.IO.File.Exists(ActivePath)) io.Replace(temporary, ActivePath, BackupPath); else io.MoveNew(temporary, ActivePath);
                if (!Equal(System.IO.File.ReadAllBytes(ActivePath), bytes)) throw new System.IO.IOException();
                return true;
                }
            } catch (System.Exception e) when (Failure(e)) {
                try { if (bytes != null && System.IO.File.Exists(ActivePath) && Equal(System.IO.File.ReadAllBytes(ActivePath), bytes)) return true; }
                catch (System.Exception read) when (Failure(read)) { }
                error = "保存できません。現在の所持・経験値を保ったまま同じ処理を再試行できます。"; return false;
            } finally {
                writing = false;
                if (temporary != null) try { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
                    catch (System.Exception e) when (Failure(e)) { }
            }
        }
        private static bool Equal(byte[] a, byte[] b) => System.Linq.Enumerable.SequenceEqual(a, b);
    }
}
