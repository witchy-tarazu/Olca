using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Customization
{
    public enum PartRotation { None = 0, Clockwise90 = 90, HalfTurn = 180, Clockwise270 = 270 }

    /// <summary>固定の相対形状。回転後の外接矩形左上を原点に揃え、反転操作は提供しない。</summary>
    public sealed class PartShape
    {
        public IReadOnlyList<Vector2Int> Cells { get; }
        public PartShape(IEnumerable<Vector2Int> cells)
        {
            var values = cells?.ToArray() ?? throw new ArgumentNullException(nameof(cells));
            if (values.Length == 0 || values.Distinct().Count() != values.Length) throw new ArgumentException("形状は重複のない一つ以上のマスが必要です");
            int minX = values.Min(v => v.X), minY = values.Min(v => v.Y);
            if (values.Any(v => (long)v.X - minX > int.MaxValue || (long)v.Y - minY > int.MaxValue)) throw new ArgumentException("相対形状が表現範囲を超えます");
            Cells = Array.AsReadOnly(values.Select(v => new Vector2Int(v.X - minX, v.Y - minY)).OrderBy(v => v.Y).ThenBy(v => v.X).ToArray());
        }
        public IReadOnlyList<Vector2Int> RotatedCells(PartRotation rotation)
        {
            if (!Enum.IsDefined(typeof(PartRotation), rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
            var values = Cells.ToArray();
            for (int i = 0; i < (int)rotation / 90; i++) {
                int maxY = values.Max(v => v.Y);
                values = values.Select(v => new Vector2Int(maxY - v.Y, v.X)).ToArray();
            }
            return Array.AsReadOnly(values.OrderBy(v => v.Y).ThenBy(v => v.X).ToArray());
        }
    }

    /// <summary>外部で確定した盤面と解放済みマス。初期サイズや解放アイテムの製品値を持たない。</summary>
    public sealed class PartsBoardDefinition
    {
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<Vector2Int> UnlockedCells { get; }
        private readonly HashSet<Vector2Int> unlocked;
        public PartsBoardDefinition(int width, int height, IEnumerable<Vector2Int> unlockedCells)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            var values = unlockedCells?.ToArray() ?? throw new ArgumentNullException(nameof(unlockedCells));
            if (values.Distinct().Count() != values.Length || values.Any(v => v.X < 0 || v.Y < 0 || v.X >= width || v.Y >= height)) throw new ArgumentException("解放マスが重複または盤面外です");
            Width = width; Height = height; unlocked = new HashSet<Vector2Int>(values); UnlockedCells = Array.AsReadOnly(values);
        }
        public bool IsUnlocked(Vector2Int cell) => unlocked.Contains(cell);
    }

    /// <summary>同種を複数配置する場合も、配置実体の識別子は別にする。</summary>
    public sealed class PartPlacement
    {
        public string InstanceId { get; }
        public string PartId { get; }
        public Vector2Int Anchor { get; }
        public PartRotation Rotation { get; }
        public PartPlacement(string instanceId, string partId, Vector2Int anchor, PartRotation rotation = PartRotation.None)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(partId)) throw new ArgumentException("配置実体と種類のIDが必要です");
            if (!Enum.IsDefined(typeof(PartRotation), rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
            InstanceId = instanceId; PartId = partId; Anchor = anchor; Rotation = rotation;
        }
    }

    public enum PartsLayoutError { None, MissingDefinition, DuplicateInstance, NotOwned, OutsideBoard, LockedCell, Overlap }

    /// <summary>全配置の検証に成功したときだけ作る不変スナップショット。所持品や保存を変更しない。</summary>
    public sealed class PartsLayout
    {
        public IReadOnlyList<PartPlacement> Placements { get; }
        public IReadOnlyDictionary<Vector2Int, string> OccupiedCells { get; }
        private PartsLayout(PartPlacement[] placements, Dictionary<Vector2Int, string> cells)
        { Placements = Array.AsReadOnly(placements); OccupiedCells = new ReadOnlyDictionary<Vector2Int, string>(cells); }
        public static bool TryCreate(PartsBoardDefinition board, IReadOnlyDictionary<string, PartShape> definitions,
            IReadOnlyDictionary<string, int> ownedCounts, IEnumerable<PartPlacement> placements, out PartsLayout layout, out PartsLayoutError error)
        {
            if (board == null || definitions == null || ownedCounts == null || placements == null) throw new ArgumentNullException("配置検証の入力が必要です");
            // カタログの比較器に依存せず、IDを完全一致で照合する。
            var registered = definitions.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
            var ownership = ownedCounts.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
            var values = placements.ToArray();
            if (values.Any(v => v == null)) throw new ArgumentException("配置が欠落しています");
            layout = null; error = PartsLayoutError.None;
            var instances = new HashSet<string>(); var counts = new Dictionary<string, int>(); var cells = new Dictionary<Vector2Int, string>();
            foreach (var placement in values) {
                if (!instances.Add(placement.InstanceId)) { error = PartsLayoutError.DuplicateInstance; return false; }
                if (!registered.TryGetValue(placement.PartId, out var shape) || shape == null) { error = PartsLayoutError.MissingDefinition; return false; }
                int count = counts.TryGetValue(placement.PartId, out int previous) ? previous + 1 : 1; counts[placement.PartId] = count;
                if (!ownership.TryGetValue(placement.PartId, out int owned) || owned < count) { error = PartsLayoutError.NotOwned; return false; }
                foreach (var local in shape.RotatedCells(placement.Rotation)) {
                    long x = (long)placement.Anchor.X + local.X, y = (long)placement.Anchor.Y + local.Y;
                    if (x < 0 || y < 0 || x >= board.Width || y >= board.Height) { error = PartsLayoutError.OutsideBoard; return false; }
                    var cell = new Vector2Int((int)x, (int)y);
                    if (!board.IsUnlocked(cell)) { error = PartsLayoutError.LockedCell; return false; }
                    if (cells.ContainsKey(cell)) { error = PartsLayoutError.Overlap; return false; }
                    cells.Add(cell, placement.InstanceId);
                }
            }
            layout = new PartsLayout(values, cells); return true;
        }
    }
}
