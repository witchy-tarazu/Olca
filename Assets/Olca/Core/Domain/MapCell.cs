namespace Olca.Core.Domain
{
    /// <summary>
    /// マスの種類
    /// </summary>
    public enum CellType
    {
        Empty,      // 空白
        Enemy,      // 敵
        Obstacle,   // 障害物
        Item,       // アイテム
        Boss        // ボス
    }

    /// <summary>
    /// ダンジョンマップの1マス分のデータ
    /// </summary>
    public class MapCell
    {
        public int Index { get; }              // マス番号
        public CellType Type { get; private set; }   // マス種類（変更可能）
        public float Height { get; }           // 高さ（0.0=地上、1.0以上=空中）
        public Enemy Enemy { get; private set; }          // 敵データ（nullの場合あり)
        public Obstacle Obstacle { get; private set; }    // 障害物データ（nullの場合あり）

        /// <summary>
        /// 空白マスを作成
        /// </summary>
        public MapCell(int index)
        {
            Index = index;
            Type = CellType.Empty;
            Height = 0f;
            Enemy = null;
            Obstacle = null;
        }

        /// <summary>
        /// 敵マスを作成
        /// </summary>
        public MapCell(int index, Enemy enemy, float height = 0f)
        {
            Index = index;
            Type = enemy.EnemyId == "boss" ? CellType.Boss : CellType.Enemy;
            Height = height;
            Enemy = enemy;
            Obstacle = null;
        }

        /// <summary>
        /// 障害物マスを作成
        /// </summary>
        public MapCell(int index, Obstacle obstacle, float height = 0f)
        {
            Index = index;
            Type = CellType.Obstacle;
            Height = height;
            Enemy = null;
            Obstacle = obstacle;
        }

        /// <summary>
        /// 敵を削除（倒した後に呼ばれる）
        /// </summary>
        public void RemoveEnemy()
        {
            Enemy = null;
            if (Type == CellType.Enemy || Type == CellType.Boss)
            {
                Type = CellType.Empty;
            }
        }
    }
}
