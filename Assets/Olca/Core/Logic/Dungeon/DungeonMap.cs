using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// ダンジョンマップの管理
    /// </summary>
    public class DungeonMap
    {
        private readonly List<MapCell> cells;
        private readonly int totalCellCount;

        public DungeonMap(DungeonConfig config)
        {
            cells = new List<MapCell>(config.MapCells);
            totalCellCount = config.TotalCellCount;
        }

        /// <summary>
        /// 指定したインデックスのマスを取得
        /// </summary>
        public MapCell GetCell(int index)
        {
            if (index < 0 || index >= cells.Count)
            {
                return null;
            }

            return cells[index];
        }

        /// <summary>
        /// 指定した位置（浮動小数点）に最も近いマスを取得
        /// </summary>
        public MapCell GetCellAtPosition(float position)
        {
            int index = (int)Math.Floor(position);
            return GetCell(index);
        }

        /// <summary>
        /// 指定した位置から一定範囲内の敵を取得
        /// </summary>
        public List<Enemy> GetEnemiesInRange(float centerPosition, float range)
        {
            var enemies = new List<Enemy>();

            int startIndex = Math.Max(0, (int)Math.Floor(centerPosition - range));
            int endIndex = Math.Min(cells.Count - 1, (int)Math.Ceiling(centerPosition + range));

            for (int i = startIndex; i <= endIndex; i++)
            {
                var cell = cells[i];
                if (cell != null && cell.Enemy != null)
                {
                    enemies.Add(cell.Enemy);
                }
            }

            return enemies;
        }

        /// <summary>
        /// 敵をマップから削除（倒した後）
        /// </summary>
        public void RemoveEnemy(Enemy enemy)
        {
            var cell = cells.FirstOrDefault(c => c.Enemy == enemy);
            cell?.RemoveEnemy();
        }

        /// <summary>
        /// マップ上に敵が1体でも残っているか
        /// </summary>
        public bool HasAnyEnemy()
        {
            foreach (var cell in cells)
            {
                if (cell != null && cell.Enemy != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 総マス数を取得
        /// </summary>
        public int TotalCellCount => totalCellCount;
    }
}
