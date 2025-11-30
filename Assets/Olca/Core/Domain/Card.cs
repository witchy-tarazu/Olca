namespace Olca.Core.Domain
{
    /// <summary>
    /// カード
    /// </summary>
    public class Card
    {
        /// <summary>カードID</summary>
        public string Id { get; }

        /// <summary>カード名</summary>
        public string Name { get; }

        /// <summary>コスト</summary>
        public int Cost { get; }

        /// <summary>レアリティ</summary>
        public CardRarity Rarity { get; }

        /// <summary>カテゴリ</summary>
        public CardCategory Category { get; }

        /// <summary>属性</summary>
        public ElementType Element { get; }

        /// <summary>威力（攻撃カードの場合）</summary>
        public int Power { get; }

        /// <summary>説明文</summary>
        public string Description { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Card(
            string id,
            string name,
            int cost,
            CardRarity rarity,
            CardCategory category,
            ElementType element,
            int power,
            string description)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Rarity = rarity;
            Category = category;
            Element = element;
            Power = power;
            Description = description;
        }

        /// <summary>
        /// カードのコピーを作成
        /// </summary>
        public Card Clone()
        {
            return new Card(Id, Name, Cost, Rarity, Category, Element, Power, Description);
        }

        public override string ToString()
        {
            return $"{Name} (コスト:{Cost}, 威力:{Power}, {Element})";
        }
    }
}
