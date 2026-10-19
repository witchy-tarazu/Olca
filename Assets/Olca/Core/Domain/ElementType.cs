namespace Olca.Core.Domain
{
    /// <summary>
    /// 属性タイプ
    /// </summary>
    public enum ElementType
    {
        /// <summary>無属性</summary>
        None,

        /// <summary>火属性</summary>
        Fire,

        /// <summary>水属性</summary>
        Water,

        /// <summary>風属性</summary>
        Wind,

        /// <summary>土属性</summary>
        Earth
    }
    /// <summary>本人属性と独立した耐性。負側のゲーム上の下限は設けない。</summary>
    public sealed class ElementResistances
    {
        public static readonly ElementResistances Neutral = new ElementResistances();
        public int Fire { get; }
        public int Water { get; }
        public int Wind { get; }
        public int Earth { get; }
        public ElementResistances(int fire = 0, int water = 0, int wind = 0, int earth = 0)
        {
            if (fire > 100 || water > 100 || wind > 100 || earth > 100)
                throw new System.ArgumentOutOfRangeException("resistance", "属性耐性の上限は100です");
            Fire = fire; Water = water; Wind = wind; Earth = earth;
        }
        public int Get(ElementType element) => element == ElementType.Fire ? Fire :
            element == ElementType.Water ? Water : element == ElementType.Wind ? Wind :
            element == ElementType.Earth ? Earth : 0;
        public double Multiplier(ElementType element) => (100L - Get(element)) / 100d;
        public bool IsNeutral => Fire == 0 && Water == 0 && Wind == 0 && Earth == 0;
        public bool HasSameValues(ElementResistances other) => other != null &&
            Fire == other.Fire && Water == other.Water && Wind == other.Wind && Earth == other.Earth;
        public int CalculateDamage(int damage, ElementType element, bool fireOnGrass)
        {
            if (damage < 0) throw new System.ArgumentOutOfRangeException(nameof(damage));
            // 正の整数ダメージを最後に一度だけ切り捨てる。乗算途中で端数を落とさない。
            decimal result = (decimal)damage * (100L - Get(element)) * (fireOnGrass ? 2 : 1) / 100m;
            return (int)System.Math.Min(int.MaxValue, result);
        }
    }
    /// <summary>外部定義から受け取るIDと被弾特性。製品ID・解放条件・カード変換は定義しない。</summary>
    public sealed class TransformationDefenseDefinition
    {
        public string Id { get; }
        public ElementResistances Resistances { get; }
        public ElementType BreakElement { get; }
        public TransformationDefenseDefinition(string id, ElementResistances resistances, ElementType breakElement)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new System.ArgumentException("変身IDが必要です", nameof(id));
            if (breakElement == ElementType.None || !System.Enum.IsDefined(typeof(ElementType), breakElement))
                throw new System.ArgumentOutOfRangeException(nameof(breakElement));
            Id = id;
            Resistances = resistances ?? throw new System.ArgumentNullException(nameof(resistances));
            BreakElement = breakElement;
        }
    }
}
