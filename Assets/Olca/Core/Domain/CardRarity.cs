namespace Olca.Core.Domain
{
    /// <summary>
    /// カードのレアリティ
    /// </summary>
    public enum CardRarity
    {
        /// <summary>ノーマル: デッキに同名4枚まで</summary>
        Normal,

        /// <summary>リミテッド: デッキに同名1枚、合計5枚まで</summary>
        Limited,

        /// <summary>アルティメット: デッキに同名1枚、合計1枚のみ</summary>
        Ultimate
    }
}
