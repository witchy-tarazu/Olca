using System;
using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// 接敵情報
    /// </summary>
    public class ContactInfo
    {
        public Enemy Enemy { get; set; }
        public bool IsDashContact { get; set; }
        public ActionSystem.DashContactEffect? DashEffect { get; set; }
        /// <summary>接触時のプレイヤー世界X座標（バトルグリッド配置の起点）</summary>
        public float ContactPositionX { get; set; }
        /// <summary>接触した敵のダンジョンセルインデックス</summary>
        public int EnemyCellIndex { get; set; }
    }

    /// <summary>
    /// 接敵判定システム
    /// </summary>
    public class ContactSystem
    {
        private readonly DungeonMap map;
        private readonly ActionSystem actionSystem;
        private const float CONTACT_DISTANCE = 1.0f;      // 非ダッシュ時の停止距離（敵に隣接したとき）
        private const float DASH_CONTACT_DISTANCE = 1.5f;  // ダッシュ時の接触距離（貫通防止のため余裕を持たせる）

        private Enemy lastContactedEnemy;  // 最後に接触した敵（連続判定防止）

        public ContactSystem(DungeonMap map, ActionSystem actionSystem = null)
        {
            this.map = map;
            this.actionSystem = actionSystem ?? new ActionSystem();
            lastContactedEnemy = null;
        }

        /// <summary>
        /// 接敵判定（毎フレーム呼び出し）
        /// </summary>
        private bool notifying;
        internal readonly struct SweptContact
        {
            public readonly MapCell Cell;
            public readonly long Position;
            public readonly double Fraction;
            public SweptContact(MapCell cell, long position, PlayerMovement.MovementStep step)
            { Cell = cell; Position = position; Fraction = step.Fraction(position); }
        }
        private bool CanContact(MapCell cell, float height)
        {
            if (cell?.Enemy == null || cell.Enemy.IsDead || cell.Enemy == lastContactedEnemy) return false;
            if (cell.Height > 0f && height < cell.Height - .2f) return false;
            return cell.Height > 0f || !actionSystem.CanAvoidWithJump(cell, height);
        }
        internal SweptContact? FindContact(PlayerMovement.MovementStep step, MapCell cell)
        {
            if (cell?.Enemy == null || cell.Enemy.IsDead || cell.Enemy == lastContactedEnemy) return null;
            long offset = (long)((step.IsDashing ? DASH_CONTACT_DISTANCE : CONTACT_DISTANCE) * 1000);
            if (!step.TryCellRange(cell.Index, offset, out var first, out var last)) return null;
            var points = new List<long> { first, last };
            step.AddHeightTurningPoints(points, first, last);
            int direction = step.End >= step.Start ? 1 : -1;
            points.Sort((a, b) => direction * a.CompareTo(b));
            // 上昇/下降/着地で分けた単調区間内だけ二分探索する。終点高度で全区間を判定しない。
            for (int i = 0; i < points.Count; i++)
            {
                long a = points[i];
                if (CanContact(cell, step.HeightAt(a))) return new SweptContact(cell, a, step);
                if (i + 1 == points.Count || !CanContact(cell, step.HeightAt(points[i + 1]))) continue;
                long low = 0, high = Math.Abs(points[i + 1] - a);
                while (low + 1 < high)
                {
                    long mid = low + (high - low) / 2;
                    if (CanContact(cell, step.HeightAt(a + direction * mid))) high = mid; else low = mid;
                }
                return new SweptContact(cell, a + direction * high, step);
            }
            return null;
        }
        internal bool NotifyContact(MapCell cell, PlayerMovement player)
        {
            if (notifying || !CanContact(cell, player.Height)) return false;
            lastContactedEnemy = cell.Enemy;
            notifying = true;
            try
            {
                OnEnemyContact?.Invoke(new ContactInfo { Enemy = cell.Enemy, IsDashContact = player.IsDashing,
                    ContactPositionX = player.CurrentPosition, EnemyCellIndex = cell.Index });
            }
            finally { notifying = false; }
            return true;
        }
        public void CheckContact(PlayerMovement player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (notifying || player.IsPaused) return;
            var step = player.LastStep;
            SweptContact? first = null;
            for (int i = 0; i < map.TotalCellCount; i++)
            {
                var candidate = FindContact(step, map.GetCell(i));
                if (candidate.HasValue && (!first.HasValue || candidate.Value.Fraction < first.Value.Fraction)) first = candidate;
            }
            if (first.HasValue) player.ApplyStep(step, first.Value.Position);
            player.ConsumeStep();
            if (first.HasValue) NotifyContact(first.Value.Cell, player);
        }

        /// <summary>
        /// 接敵リセット（バトル終了後に呼ぶ）
        /// </summary>
        public void ResetContact()
        {
            lastContactedEnemy = null;
        }

        public event Action<ContactInfo> OnEnemyContact;
    }
}
