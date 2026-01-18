using System.Collections.Generic;

namespace Olca.Core.Domain
{
    /// <summary>
    /// ダンジョン設定データ
    /// </summary>
    public class DungeonConfig
    {
        public string DungeonId { get; }
        public string DungeonName { get; }
        public int TotalCellCount { get; }         // 総マス数
        public int InitialDeckSize { get; }        // 初期デッキ枚数（デフォルト20枚）
        public List<MapCell> MapCells { get; }     // マス配置データ

        public DungeonConfig(
            string dungeonId,
            string dungeonName,
            int totalCellCount,
            List<MapCell> mapCells,
            int initialDeckSize = 20)
        {
            DungeonId = dungeonId;
            DungeonName = dungeonName;
            TotalCellCount = totalCellCount;
            MapCells = mapCells ?? new List<MapCell>();
            InitialDeckSize = initialDeckSize;
        }

        /// <summary>
        /// テスト用の簡易ダンジョン生成
        /// </summary>
        public static DungeonConfig CreateTestDungeon(int cellCount = 20)
        {
            var cells = new List<MapCell>();

            for (int i = 0; i < cellCount; i++)
            {
                // 最後のマスにボスを配置
                if (i == cellCount - 1)
                {
                    var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(i, 0));
                    cells.Add(new MapCell(i, boss));
                }
                // 5マスごとに敵を配置
                else if (i % 5 == 0 && i > 0)
                {
                    var enemy = new Enemy($"enemy_{i}", "スライム", 50, ElementType.None, new Vector2Int(i, 0));
                    cells.Add(new MapCell(i, enemy));
                }
                // それ以外は空白
                else
                {
                    cells.Add(new MapCell(i));
                }
            }

            return new DungeonConfig(
                dungeonId: "test_dungeon",
                dungeonName: "テストダンジョン",
                totalCellCount: cellCount,
                mapCells: cells
            );
        }
    }
}
