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
        public FieldItem Item { get; }
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

        /// <summary>既存の回復効果を持つ地上フィールドアイテム。</summary>
        public MapCell(int index, FieldItem item)
        {
            if (item == null) throw new System.ArgumentNullException(nameof(item));
            Index=index;Type=CellType.Item;Height=0;Item=item;
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

namespace Olca.Core.Domain
{
    /// <summary>取得時に既存Healへ渡す固定回復定義。在庫・任意使用・新しい効果種類を持たない。</summary>
    public sealed class FieldItem
    {
        public string Id { get; }
        public string Name { get; }
        public int Heal { get; }
        public FieldItem(string id,string name,int heal)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(name)||heal<=0)throw new System.ArgumentException("回復アイテムの定義が不正です");
            Id=id;Name=name;Heal=heal;
        }
    }
}

namespace Olca.Core.Domain
{
    /// <summary>探索の固定接地効果。周期/消費/新しい地形種類は持たない。</summary>
    public sealed class ExplorationTerrainCell
    {
        public int Cell { get; }
        public PanelType Type { get; }
        public int Heal { get; }
        public int Damage { get; }
        public ExplorationTerrainCell(int cell, PanelType type, int heal = 5, int magmaDamage = 5)
        {
            if (cell < 0 || (type != PanelType.Grass && type != PanelType.Magma) || heal < 0 || magmaDamage < 0)
                throw new System.ArgumentException("草むら/マグマの固定量は0以上です");
            Cell=cell;Type=type;Heal=heal;Damage=type==PanelType.Magma?magmaDamage:0;
        }
    }
}
