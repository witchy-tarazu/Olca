namespace Olca.Core.Domain
{
    /// <summary>
    /// プレイヤー・敵で共用する戦闘中の地形判定。付与時間や付与手段は扱わない。
    /// </summary>
    public sealed class BattleTerrainState
    {
        public bool IsFloating { get; set; }
        public bool ReceivesPanelEffects => !IsFloating;
        public bool CanEnter(PanelType panel) => panel != PanelType.Hole || IsFloating;
        public void Clear() => IsFloating = false;
    }

    /// <summary>
    /// バトルフィールドのパネルタイプ
    /// </summary>
    public enum PanelType
    {
        /// <summary>通常パネル</summary>
        Normal,

        /// <summary>氷パネル: 滑って移動が制御不能</summary>
        Ice,

        /// <summary>草むらパネル: 土属性のHP回復、火属性攻撃で延焼</summary>
        Grass,

        /// <summary>マグマパネル: 火属性以外は継続ダメージ</summary>
        Magma,

        /// <summary>ヒビパネル: 再度乗ると穴になる</summary>
        Cracked,

        /// <summary>穴パネル: 移動不可（フロート能力持ちは影響なし）</summary>
        Hole
    }
}
