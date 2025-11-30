namespace Olca.Core.Domain
{
    /// <summary>
    /// カードの系統（ソード系、ボム系など）
    /// 同系統のカードを連続して選択するとコストボーナスが発生
    /// </summary>
    public class CardSeries
    {
        /// <summary>系統名</summary>
        public string Name { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CardSeries(string name)
        {
            Name = name;
        }

        public override bool Equals(object obj)
        {
            if (obj is CardSeries other)
            {
                return Name == other.Name;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Name?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return Name;
        }

        // よく使われる系統の定数
        public static readonly CardSeries None = new CardSeries("なし");
        public static readonly CardSeries Sword = new CardSeries("ソード");
        public static readonly CardSeries Bomb = new CardSeries("ボム");
        public static readonly CardSeries Canon = new CardSeries("キャノン");
    }
}
