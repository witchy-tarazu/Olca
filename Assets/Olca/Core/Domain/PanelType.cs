namespace Olca.Core.Domain
{
    /// <summary>
    /// プレイヤー・敵で共用する地形判定。常時供給と一時フラグを分け、Clearは一時分だけ解除する。
    /// </summary>
    public sealed class BattleTerrainState
    {
        private readonly System.Func<bool> permanentFloating;
        private bool temporaryFloating;
        public BattleTerrainState(System.Func<bool> permanent=null){permanentFloating=permanent;}
        public bool IsTemporarilyFloating=>temporaryFloating;
        public bool IsFloating { get=>temporaryFloating||(permanentFloating?.Invoke()??false);set=>temporaryFloating=value; }
        public bool ReceivesPanelEffects => !IsFloating;
        public bool CanEnter(PanelType panel) => panel != PanelType.Hole || IsFloating;
        public void Clear() => temporaryFloating = false;
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

namespace Olca.Core.Domain
{
    /// <summary>各戦闘パネルの固定量。種類を上書きする際は未指定なら既存の5へ戻す。</summary>
    public sealed class BattlePanelValues
    {
        public static readonly BattlePanelValues Default = new BattlePanelValues();
        public int Heal { get; }
        public int Damage { get; }
        public BattlePanelValues(int heal=5,int damage=5)
        {
            if(heal<0||damage<0)throw new System.ArgumentOutOfRangeException("パネル量は0以上が必要です");
            Heal=heal;Damage=damage;
        }
    }
}
