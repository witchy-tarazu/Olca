using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Customization
{
    public sealed class CustomizationItemCount
    {
        public string Id; public int Count;
        public CustomizationItemCount Copy() => new CustomizationItemCount { Id = Id, Count = Count };
    }
    public sealed class CustomizationCell
    {
        public int X, Y;
        public CustomizationCell Copy() => new CustomizationCell { X = X, Y = Y };
    }
    public sealed class SavedPartPlacement
    {
        public string InstanceId, PartId; public int X, Y; public PartRotation Rotation;
        public SavedPartPlacement Copy() => new SavedPartPlacement { InstanceId = InstanceId, PartId = PartId, X = X, Y = Y, Rotation = Rotation };
        public PartPlacement ToPlacement() => new PartPlacement(InstanceId, PartId, new Vector2Int(X, Y), Rotation);
    }
    public sealed class CustomizationGrantReceipt
    {
        public string RequestId, Fingerprint;
        public CustomizationGrantReceipt Copy() => new CustomizationGrantReceipt { RequestId = RequestId, Fingerprint = Fingerprint };
    }
    /// <summary>省略可能な進行保存。定義が未登録でも読込時にIDや所持品を捨てない。</summary>
    public sealed class CustomizationProgress
    {
        public string BoardId;
        public string[] OwnedAccessoryIds = Array.Empty<string>();
        public CustomizationItemCount[] OwnedParts = Array.Empty<CustomizationItemCount>();
        public CustomizationItemCount[] OwnedUnlockItems = Array.Empty<CustomizationItemCount>();
        public string[] EquippedAccessoryIds = Array.Empty<string>();
        public SavedPartPlacement[] Placements = Array.Empty<SavedPartPlacement>();
        public CustomizationCell[] UnlockedCells = Array.Empty<CustomizationCell>();
        public string[] AppliedUnlockItemIds = Array.Empty<string>();
        public CustomizationGrantReceipt[] Receipts = Array.Empty<CustomizationGrantReceipt>();
        public CustomizationProgress Copy() => new CustomizationProgress {
            BoardId = BoardId, OwnedAccessoryIds = (OwnedAccessoryIds ?? Array.Empty<string>()).ToArray(),
            OwnedParts = (OwnedParts ?? Array.Empty<CustomizationItemCount>()).Select(v => v?.Copy()).ToArray(),
            OwnedUnlockItems = (OwnedUnlockItems ?? Array.Empty<CustomizationItemCount>()).Select(v => v?.Copy()).ToArray(),
            EquippedAccessoryIds = (EquippedAccessoryIds ?? Array.Empty<string>()).ToArray(),
            Placements = (Placements ?? Array.Empty<SavedPartPlacement>()).Select(v => v?.Copy()).ToArray(),
            UnlockedCells = (UnlockedCells ?? Array.Empty<CustomizationCell>()).Select(v => v?.Copy()).ToArray(),
            AppliedUnlockItemIds = (AppliedUnlockItemIds ?? Array.Empty<string>()).ToArray(),
            Receipts = (Receipts ?? Array.Empty<CustomizationGrantReceipt>()).Select(v => v?.Copy()).ToArray() };
        public static void Validate(CustomizationProgress value)
        {
            if (value == null) return;
            var p = value.Copy();
            bool InvalidIds(IEnumerable<string> ids) => ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count();
            bool InvalidCounts(CustomizationItemCount[] counts) => counts.Any(v => v == null || string.IsNullOrWhiteSpace(v.Id) || v.Count < 1) || counts.Select(v => v.Id).Distinct().Count() != counts.Length;
            if (p.BoardId != null && string.IsNullOrWhiteSpace(p.BoardId) || InvalidIds(p.OwnedAccessoryIds) || InvalidIds(p.EquippedAccessoryIds) || InvalidIds(p.AppliedUnlockItemIds) ||
                InvalidCounts(p.OwnedParts) || InvalidCounts(p.OwnedUnlockItems) || p.EquippedAccessoryIds.Except(p.OwnedAccessoryIds).Any() ||
                p.AppliedUnlockItemIds.Except(p.OwnedUnlockItems.Select(v => v.Id)).Any()) throw new ArgumentException("装備所持データが不正です");
            if (p.UnlockedCells.Any(v => v == null || v.X < 0 || v.Y < 0) || p.UnlockedCells.Select(v => (v.X, v.Y)).Distinct().Count() != p.UnlockedCells.Length ||
                p.Placements.Any(v => v == null || string.IsNullOrWhiteSpace(v.InstanceId) || string.IsNullOrWhiteSpace(v.PartId) || v.X < 0 || v.Y < 0 || !Enum.IsDefined(typeof(PartRotation), v.Rotation)) ||
                p.Placements.Select(v => v.InstanceId).Distinct().Count() != p.Placements.Length ||
                p.Placements.GroupBy(v => v.PartId).Any(g => p.OwnedParts.SingleOrDefault(v => v.Id == g.Key)?.Count < g.Count() || !p.OwnedParts.Any(v => v.Id == g.Key))) throw new ArgumentException("パーツ配置データが不正です");
            if (p.BoardId == null && (p.UnlockedCells.Length != 0 || p.Placements.Length != 0 || p.AppliedUnlockItemIds.Length != 0)) throw new ArgumentException("盤面定義IDが必要です");
            if (p.Receipts.Any(v => v == null || !Guid.TryParseExact(v.RequestId, "N", out _) || v.Fingerprint == null || v.Fingerprint.Length != 64 || !v.Fingerprint.All(Uri.IsHexDigit)) ||
                p.Receipts.Select(v => v.RequestId).Distinct().Count() != p.Receipts.Length) throw new ArgumentException("装備取得の受領履歴が不正です");
        }
    }

    /// <summary>外部で確定した製品定義だけを登録する。能力値や入手先は生成しない。</summary>
    public sealed class CustomizationCatalog
    {
        public string BoardId { get; }
        public PartsBoardDefinition Board { get; }
        public int AccessorySlots { get; }
        public IReadOnlyList<string> Accessories { get; }
        public IReadOnlyDictionary<string, PartShape> Parts { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<Vector2Int>> UnlockItems { get; }
        public CustomizationCatalog(string boardId, PartsBoardDefinition board, IEnumerable<string> accessories,
            IReadOnlyDictionary<string, PartShape> parts, IReadOnlyDictionary<string, IEnumerable<Vector2Int>> unlockItems, int accessorySlots = 3)
        {
            if (string.IsNullOrWhiteSpace(boardId) || accessorySlots < 1) throw new ArgumentException("盤面IDと装飾品枠数が必要です");
            BoardId = boardId; Board = board ?? throw new ArgumentNullException(nameof(board)); AccessorySlots = accessorySlots;
            var ids = (accessories ?? Array.Empty<string>()).ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length) throw new ArgumentException("装飾品IDが不正です");
            Accessories = Array.AsReadOnly(ids);
            var shapes = (parts ?? new Dictionary<string, PartShape>()).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
            if (shapes.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value == null)) throw new ArgumentException("パーツ定義が不正です");
            Parts = new ReadOnlyDictionary<string, PartShape>(shapes);
            var fixedCells = new Dictionary<string, IReadOnlyList<Vector2Int>>();
            foreach (var pair in unlockItems ?? new Dictionary<string, IEnumerable<Vector2Int>>()) {
                var cells = pair.Value?.ToArray();
                if (string.IsNullOrWhiteSpace(pair.Key) || cells == null || cells.Length == 0 || cells.Distinct().Count() != cells.Length ||
                    cells.Any(v => v.X < 0 || v.Y < 0 || v.X >= board.Width || v.Y >= board.Height)) throw new ArgumentException("マス解放アイテムの固定領域が不正です");
                fixedCells.Add(pair.Key, Array.AsReadOnly(cells));
            }
            UnlockItems = new ReadOnlyDictionary<string, IReadOnlyList<Vector2Int>>(fixedCells);
        }
    }

    public sealed class CustomizationGrant
    {
        public string RequestId { get; }
        public string ClearRunId { get; }
        public IReadOnlyList<string> Accessories { get; }
        public IReadOnlyDictionary<string, int> Parts { get; }
        public IReadOnlyDictionary<string, int> UnlockItems { get; }
        public CustomizationGrant(string requestId, IEnumerable<string> accessories = null, IReadOnlyDictionary<string, int> parts = null,
            IReadOnlyDictionary<string, int> unlockItems = null, string clearRunId = null)
        {
            if (!Guid.TryParseExact(requestId, "N", out _) || clearRunId != null && !Guid.TryParseExact(clearRunId, "N", out _)) throw new ArgumentException("取得要求IDが不正です");
            RequestId = requestId; ClearRunId = clearRunId;
            var ids = (accessories ?? Array.Empty<string>()).ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length) throw new ArgumentException("取得装飾品が不正です");
            Accessories = Array.AsReadOnly(ids);
            IReadOnlyDictionary<string, int> Counts(IReadOnlyDictionary<string, int> values) {
                var copy = (values ?? new Dictionary<string, int>()).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
                if (copy.Any(v => string.IsNullOrWhiteSpace(v.Key) || v.Value < 1)) throw new ArgumentException("取得数が不正です");
                return new ReadOnlyDictionary<string, int>(copy);
            }
            Parts = Counts(parts); UnlockItems = Counts(unlockItems);
            if (Accessories.Count + Parts.Count + UnlockItems.Count == 0) throw new ArgumentException("取得内容が必要です");
        }
        internal string Fingerprint(string boardId) => Olca.Core.Logic.Dungeon.CheckpointValues.Hash(w => {
            w.Write("customization-grant-v1"); w.Write(boardId); w.Write(ClearRunId ?? "");
            w.Write(Accessories.Count); foreach (var id in Accessories.OrderBy(v => v, StringComparer.Ordinal)) w.Write(id);
            w.Write(Parts.Count); foreach (var pair in Parts.OrderBy(v => v.Key, StringComparer.Ordinal)) { w.Write(pair.Key); w.Write(pair.Value); }
            w.Write(UnlockItems.Count); foreach (var pair in UnlockItems.OrderBy(v => v.Key, StringComparer.Ordinal)) { w.Write(pair.Key); w.Write(pair.Value); }
        });
    }
}
