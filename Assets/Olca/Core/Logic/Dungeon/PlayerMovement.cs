using System;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// プレイヤーの移動ロジック（Unity非依存）
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// 位置は1/1000単位（ミリ単位）で管理
    /// </summary>
    public class PlayerMovement
    {
        private long currentPositionMilli;  // 現在位置（1/1000単位）
        private bool isJumping;
        private bool isDashing;
        private bool isPaused;          // 一時停止中（バトル中など）

        private int jumpTimerMs;
        private const int BASE_SPEED_MILLI_PER_SEC = 10000;  // 基本移動速度（ミリ単位/秒）= 10.0
        private const int DASH_SPEED_MILLI_PER_SEC = 20000;  // ダッシュ速度（ミリ単位/秒）= 20.0
        private const int JUMP_DURATION_MS = 500;           // ジャンプ時間（ミリ秒）= 0.5秒
        private const int JUMP_HEIGHT_MILLI = 1500;         // ジャンプの最大高さ（ミリ単位）= 1.5

        private int heightMilli;  // 高さ（ミリ単位、0=地上）

        /// <summary>現在位置（ミリ単位）</summary>
        public long CurrentPositionMilli => currentPositionMilli;

        /// <summary>現在位置（float互換）</summary>
        public float CurrentPosition => currentPositionMilli / 1000f;

        /// <summary>高さ（ミリ単位）</summary>
        public int HeightMilli => heightMilli;

        /// <summary>高さ（float互換、0.0=地上）</summary>
        public float Height => heightMilli / 1000f;

        /// <summary>ダッシュ中か</summary>
        public bool IsDashing => isDashing;

        /// <summary>ジャンプ中か</summary>
        public bool IsJumping => isJumping;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialPosition">初期位置（ミリ単位、デフォルト0）</param>
        public PlayerMovement(long initialPositionMilli = 0)
        {
            currentPositionMilli = initialPositionMilli;
            heightMilli = 0;
            isJumping = false;
            isDashing = false;
            isPaused = false;
        }

        /// <summary>
        /// コンストラクタ - float版（後方互換）
        /// </summary>
        public PlayerMovement(float initialPosition)
            : this((long)(initialPosition * 1000f))
        {
        }

        /// <summary>
        /// 毎フレーム更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        public void Update(int deltaTimeMs)
        {
            if (isPaused) return;

            // 速度決定（ミリ単位/秒）
            int speedMilliPerSec = isDashing ? DASH_SPEED_MILLI_PER_SEC : BASE_SPEED_MILLI_PER_SEC;
            // 移動量 = 速度 × 時間 / 1000（速度が「/秒」なので1000で割る）
            currentPositionMilli += (long)speedMilliPerSec * deltaTimeMs / 1000;

            // ジャンプ処理
            if (isJumping)
            {
                UpdateJump(deltaTimeMs);
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
            jumpTimerMs = 0;
        }

        /// <summary>
        /// ジャンプ更新
        /// </summary>
        private void UpdateJump(int deltaTimeMs)
        {
            jumpTimerMs += deltaTimeMs;

            if (jumpTimerMs >= JUMP_DURATION_MS)
            {
                // ジャンプ終了
                isJumping = false;
                heightMilli = 0;
            }
            else
            {
                // 放物線運動（sin波で簡易実装）
                // t = jumpTimerMs / JUMP_DURATION_MS (0.0 ～ 1.0)
                float t = (float)jumpTimerMs / JUMP_DURATION_MS;
                heightMilli = (int)(Math.Sin(t * Math.PI) * JUMP_HEIGHT_MILLI);
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
        /// 位置をリセット（ミリ単位版）
        /// </summary>
        public void SetPositionMilli(long positionMilli)
        {
            currentPositionMilli = positionMilli;
        }

        /// <summary>
        /// 位置をリセット（float版、後方互換）
        /// </summary>
        public void SetPosition(float position)
        {
            currentPositionMilli = (long)(position * 1000f);
        }
    }
}
