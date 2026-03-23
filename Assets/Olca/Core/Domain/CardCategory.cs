namespace Olca.Core.Domain
{
    /// <summary>
    /// カードのカテゴリ
    /// </summary>
    public enum CardCategory
    {
        /// <summary>攻撃系</summary>
        Attack,

        /// <summary>補助・強化系</summary>
        Support,

        /// <summary>フィールド操作系</summary>
        Field,

        /// <summary>防御・回復系</summary>
        Defense,

        /// <summary>状態異常系</summary>
        StatusAilment,

        /// <summary>調律素材専用カード（他カードへの追加効果付与に使用）</summary>
        Tuning
    }
}
