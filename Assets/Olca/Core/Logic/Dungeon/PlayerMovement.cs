using System;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// プレイヤーの移動ロジック（Unity非依存）
    /// </summary>
    public class PlayerMovement
    {
        private float currentPosition;  // 現在位置（浮動小数点）
        private float velocity;         // 速度
        private bool isJumping;
        private bool isDashing;
        private bool isPaused;          // 一時停止中（バトル中など）

        private float jumpTimer;
        private const float BASE_SPEED = 2.0f;      // 基本移動速度（マス/秒）
        private const float DASH_SPEED = 5.0f;      // ダッシュ速度
        private const float JUMP_DURATION = 0.5f;   // ジャンプ時間（秒）
        private const float JUMP_HEIGHT = 1.5f;     // ジャンプの最大高さ

        public float CurrentPosition => currentPosition;
        public float Height { get; private set; }  // 高さ（0.0=地上）
        public bool IsDashing => isDashing;
        public bool IsJumping => isJumping;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PlayerMovement(float initialPosition = 0f)
        {
            currentPosition = initialPosition;
            velocity = 0f;
            Height = 0f;
            isJumping = false;
            isDashing = false;
            isPaused = false;
        }

        /// <summary>
        /// 毎フレーム更新（deltaTimeを外部から受け取る）
        /// </summary>
        public void Update(float deltaTime)
        {
            if (isPaused) return;

            // 速度決定
            float speed = isDashing ? DASH_SPEED : BASE_SPEED;
            currentPosition += speed * deltaTime;

            // ジャンプ処理
            if (isJumping)
            {
                UpdateJump(deltaTime);
            }
        }

        /// <summary>
        /// ダッシュ開始
        /// </summary>
        public void StartDash()
        {
            isDashing = true;
        }

        /// <summary>
        /// ダッシュ停止
        /// </summary>
        public void StopDash()
        {
            isDashing = false;
        }

        /// <summary>
        /// ジャンプ
        /// </summary>
        public void Jump()
        {
            if (isJumping || isPaused) return;

            isJumping = true;
            jumpTimer = 0f;
        }

        /// <summary>
        /// ジャンプ更新
        /// </summary>
        private void UpdateJump(float deltaTime)
        {
            jumpTimer += deltaTime;

            // 放物線運動（sin波で簡易実装）
            float t = jumpTimer / JUMP_DURATION;
            if (t > 1.0f)
            {
                // ジャンプ終了
                isJumping = false;
                Height = 0f;
            }
            else
            {
                Height = (float)Math.Sin(t * Math.PI) * JUMP_HEIGHT;
            }
        }

        /// <summary>
        /// 一時停止（バトル中など）
        /// </summary>
        public void Pause()
        {
            isPaused = true;
        }

        /// <summary>
        /// 再開
        /// </summary>
        public void Resume()
        {
            isPaused = false;
        }

        /// <summary>
        /// 位置をリセット
        /// </summary>
        public void SetPosition(float position)
        {
            currentPosition = position;
        }
    }
}
