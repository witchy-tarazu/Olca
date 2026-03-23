namespace Olca.Core.Domain
{
    /// <summary>
    /// 超越（Exceed）グレード
    /// </summary>
    public enum ExceedLevel
    {
        /// <summary>通常（無印）</summary>
        Normal = 0,
        /// <summary>超越Lv1（★）</summary>
        Lv1 = 1,
        /// <summary>超越Lv2（★★）</summary>
        Lv2 = 2,
        /// <summary>超越Lv3（★★★）- 最上位。これ以上超越不可</summary>
        Lv3 = 3,
    }
}
