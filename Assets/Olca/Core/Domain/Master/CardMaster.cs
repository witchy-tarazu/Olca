using MasterMemory;
using MessagePack;

namespace Olca.Core.Domain.Master
{
    /// <summary>
    /// カードマスターデータ
    /// MasterMemoryで管理されるカード情報
    /// </summary>
    [MemoryTable("cards"), MessagePackObject(true)]
    public class CardMaster
    {
        [PrimaryKey]
        public string Id { get; set; }

        public string Name { get; set; }

        public int Cost { get; set; }

        public CardRarity Rarity { get; set; }

        public CardCategory Category { get; set; }

        public ElementType Element { get; set; }

        public SeriesType Series { get; set; }

        public int Power { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// カード効果のクラス名（例: "CannonEffect"）
        /// リフレクションまたはファクトリーパターンで効果を生成する際に使用
        /// </summary>
        public string EffectClassName { get; set; }
    }
}
