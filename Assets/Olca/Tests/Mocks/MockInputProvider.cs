using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用の入力プロバイダー
    /// </summary>
    public class MockInputProvider : IInputProvider
    {
        public float Horizontal { get; set; }
        public float HorizontalDown { get; set; }
        public float Vertical { get; set; }
        public bool JumpButtonDown { get; set; }
        public bool DashButton { get; set; }
        public bool CardUseButtonDown { get; set; }
        public bool BasicAttackButton { get; set; }
        public float BasicAttackButtonHoldTime { get; set; }
        public bool CustomScreenButtonDown { get; set; }

        /// <summary>
        /// すべての入力をリセット
        /// </summary>
        public void Reset()
        {
            Horizontal = 0f;
            HorizontalDown = 0f;
            Vertical = 0f;
            JumpButtonDown = false;
            DashButton = false;
            CardUseButtonDown = false;
            BasicAttackButton = false;
            BasicAttackButtonHoldTime = 0f;
            CustomScreenButtonDown = false;
        }

        /// <summary>
        /// ジャンプボタンを押す（テスト用）
        /// </summary>
        public void PressJumpButton()
        {
            JumpButtonDown = true;
        }

        /// <summary>
        /// カスタム画面ボタンを押す（テスト用）
        /// </summary>
        public void PressCustomScreenButton()
        {
            CustomScreenButtonDown = true;
        }

        /// <summary>
        /// 横方向入力を設定（テスト用）
        /// </summary>
        public void SetHorizontal(float value)
        {
            Horizontal = value;
        }

        /// <summary>
        /// 縦方向入力を設定（テスト用）
        /// </summary>
        public void SetVertical(float value)
        {
            Vertical = value;
        }
    }
}
