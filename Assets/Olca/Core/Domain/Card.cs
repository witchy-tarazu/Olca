using Olca.Core.Domain.Master;

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

        /// <summary>系統（ソード系、ボム系など）</summary>
        public CardSeries Series { get; }

        /// <summary>威力（攻撃カードの場合）</summary>
        public int Power { get; }

        /// <summary>説明文</summary>
        public string Description { get; }

        /// <summary>カード効果のクラス名</summary>
        public string EffectClassName { get; }

        /// <summary>一時移動カードの移動モーション時間（秒）</summary>
        public float MoveMotionTime { get; }

        /// <summary>一時移動カードの攻撃モーション時間（秒）</summary>
        public float AttackMotionTime { get; }

        /// <summary>一時移動時の貫通モード</summary>
        public MovePenetrationMode PenetrationMode { get; }

        /// <summary>調律による追加効果</summary>
        public CardModifier Modifier { get; private set; }

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
            CardSeries series,
            int power,
            string description,
            string effectClassName = null,
            CardModifier modifier = null,
            float moveMotionTime = 0f,
            float attackMotionTime = 0f,
            MovePenetrationMode penetrationMode = MovePenetrationMode.Blocking)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Rarity = rarity;
            Category = category;
            Element = element;
            Series = series;
            Power = power;
            Description = description;
            EffectClassName = effectClassName;
            MoveMotionTime = moveMotionTime;
            AttackMotionTime = attackMotionTime;
            PenetrationMode = penetrationMode;
            Modifier = modifier ?? new CardModifier();
        }

        /// <summary>
        /// 調律: 他のカードの効果をこのカードに付与する
        /// </summary>
        /// <param name="tuningCard">調律元のカード（アタック+10など）</param>
        /// <returns>調律後の新しいカードインスタンス</returns>
        public Card ApplyTuning(Card tuningCard)
        {
            if (tuningCard == null) return this;

            // 調律元のカードから追加効果を取得
            CardModifier newModifier = this.Modifier.Merge(tuningCard.Modifier);

            // 新しいカードインスタンスを作成（元のカードは変更しない）
            return new Card(
                Id, Name, Cost, Rarity, Category, Element, Series, Power, Description, EffectClassName,
                newModifier, MoveMotionTime, AttackMotionTime, PenetrationMode
            );
        }

        /// <summary>
        /// 最終的な威力を計算（調律効果を含む）
        /// </summary>
        public int GetFinalPower()
        {
            int finalPower = (int)(Power * Modifier.DamageMultiplier);
            finalPower += Modifier.BonusDamage;
            return finalPower;
        }

        /// <summary>
        /// 最終的な属性を取得（調律効果を含む）
        /// </summary>
        public ElementType GetFinalElement()
        {
            return Modifier.ElementOverride ?? Element;
        }

        /// <summary>
        /// マスターデータからCardを生成
        /// </summary>
        public static Card FromMaster(CardMaster master)
        {
            // SeriesTypeからCardSeriesに変換
            CardSeries series = ConvertSeriesType(master.Series);

            return new Card(
                master.Id,
                master.Name,
                master.Cost,
                master.Rarity,
                master.Category,
                master.Element,
                series,
                master.Power,
                master.Description,
                master.EffectClassName,
                null,  // modifier
                master.MoveMotionTime,
                master.AttackMotionTime,
                master.PenetrationMode
            );
        }

        /// <summary>
        /// SeriesTypeからCardSeriesに変換
        /// </summary>
        private static CardSeries ConvertSeriesType(SeriesType seriesType)
        {
            return seriesType switch
            {
                SeriesType.None => CardSeries.None,
                SeriesType.Sword => CardSeries.Sword,
                SeriesType.Bomb => CardSeries.Bomb,
                SeriesType.Canon => CardSeries.Canon,
                _ => CardSeries.None
            };
        }

        /// <summary>
        /// カードのコピーを作成
        /// </summary>
        public Card Clone()
        {
            return new Card(Id, Name, Cost, Rarity, Category, Element, Series, Power, Description, EffectClassName,
                Modifier, MoveMotionTime, AttackMotionTime, PenetrationMode);
        }

        public override string ToString()
        {
            return $"{Name} (コスト:{Cost}, 威力:{Power}, {Element})";
        }
    }
}
