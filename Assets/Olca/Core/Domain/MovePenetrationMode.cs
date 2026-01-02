namespace Olca.Core.Domain
{
    /// <summary>
    /// 一時移動時の貫通モード
    /// </summary>
    public enum MovePenetrationMode
    {
        /// <summary>障害物・敵で止まる（効果キャンセル）</summary>
        Blocking = 0,

        /// <summary>障害物・敵を無視して通過（無敵）</summary>
        Invincible = 1
    }
}
