namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 入力情報のインターフェース
    /// Unity非依存のロジックで入力を扱うための抽象化
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>
        /// 横方向の入力（-1.0 ~ 1.0）
        /// </summary>
        float Horizontal { get; }

        /// <summary>
        /// 横方向の入力が押された瞬間か（-1.0 ~ 1.0）
        /// </summary>
        float HorizontalDown { get; }

        /// <summary>
        /// 縦方向の入力（-1.0 ~ 1.0）
        /// </summary>
        float Vertical { get; }

        /// <summary>
        /// ジャンプボタンが押された瞬間か
        /// </summary>
        bool JumpButtonDown { get; }

        /// <summary>
        /// ダッシュボタンが押されているか
        /// </summary>
        bool DashButton { get; }

        /// <summary>
        /// カード使用ボタンが押された瞬間か
        /// </summary>
        bool CardUseButtonDown { get; }

        /// <summary>
        /// 固有攻撃ボタンが押されているか
        /// </summary>
        bool BasicAttackButton { get; }

        /// <summary>
        /// 固有攻撃ボタンが押されている時間（秒）
        /// タメ攻撃の判定に使用
        /// </summary>
        float BasicAttackButtonHoldTime { get; }

        /// <summary>
        /// カスタム画面ボタンが押された瞬間か
        /// </summary>
        bool CustomScreenButtonDown { get; }
    }
}
