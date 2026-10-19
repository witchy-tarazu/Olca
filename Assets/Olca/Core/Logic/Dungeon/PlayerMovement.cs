using System;
using System.Collections.Generic;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// プレイヤーの移動ロジック（Unity非依存）
    /// 60Hzのフレームで進行し、旧ms入口は境界で変換
    /// 位置は1/1000単位（ミリ単位）で公開し、内部では割算余りを保持
    /// </summary>
    public class PlayerMovement
    {
        private long currentPositionMilli;  // 現在位置（1/1000単位）
        private bool isJumping;
        private bool isDashing;
        private bool isPaused;          // 一時停止中（バトル中など）

        private double jumpTimerFrames;
        private long positionRemainder;
        private MovementStep lastStep;
        private bool hasLastStep;
        internal int ControlRevision { get; private set; }
        internal bool IsPaused => isPaused;
        private void ControlChanged() { ControlRevision++; hasLastStep = false; }
        internal MovementStep LastStep => hasLastStep ? lastStep : PreviewStep(0);
        internal void ConsumeStep() => hasLastStep = false;

        // 区間は位置の既存精度（1/1000セル）のまま扱う。固定時間刻みへの分割はしない。
        internal readonly struct MovementStep
        {
            public readonly long Start, End;
            public readonly double DurationFrames;
            public readonly long EndRemainder;
            public readonly bool WasJumping, IsDashing;
            public readonly double JumpTimeFrames;
            public readonly int StartHeightMilli;
            public MovementStep(long start, long end, double durationFrames, bool jumping, bool dashing, double jumpTimeFrames, int height, long endRemainder)
            { Start = start; End = end; DurationFrames = durationFrames; EndRemainder = endRemainder; WasJumping = jumping; IsDashing = dashing; JumpTimeFrames = jumpTimeFrames; StartHeightMilli = height; }
            public double Fraction(long position) => End == Start ? 0 : (double)(position - Start) / (End - Start);
            public long PositionAt(double fraction) => Start + (long)Math.Round((End - Start) * fraction);
            public float HeightAt(long position)
            {
                if (!WasJumping) return StartHeightMilli / 1000f;
                double time = JumpTimeFrames + DurationFrames * Fraction(position);
                if (time >= JUMP_DURATION_FRAMES) return 0f;
                float t = (float)time / JUMP_DURATION_FRAMES;
                return (int)(Math.Sin(t * Math.PI) * JUMP_HEIGHT_MILLI) / 1000f;
            }
            public bool TryCellRange(int cellIndex, long offset, out long first, out long last)
            {
                long lower = (long)cellIndex * 1000 - offset, upper = ((long)cellIndex + 1) * 1000 - offset - 1;
                long min = Math.Max(Math.Min(Start, End), lower), max = Math.Min(Math.Max(Start, End), upper);
                first = End >= Start ? min : max; last = End >= Start ? max : min;
                return min <= max;
            }
            public void AddHeightTurningPoints(List<long> positions, long first, long last)
            {
                if (!WasJumping || DurationFrames == 0) return;
                foreach (double time in new[] { JUMP_DURATION_FRAMES * .5, (double)JUMP_DURATION_FRAMES })
                {
                    double fraction = (time - JumpTimeFrames) / DurationFrames;
                    if (fraction < 0 || fraction > 1) continue;
                    long position = PositionAt(fraction);
                    if (position >= Math.Min(first, last) && position <= Math.Max(first, last)) positions.Add(position);
                }
            }
        }
        // 1ms=3単位、1論理フレーム=50単位（1/3000秒）。位置の割算余りも保持する。
        internal MovementStep PreviewStep(int deltaTimeMs) => PreviewTime(deltaTimeMs * 3L);
        internal MovementStep PreviewFrame() => PreviewTime(50);
        private MovementStep PreviewTime(long timeUnits)
        {
            long elapsed = isPaused ? 0 : timeUnits;
            int speed = isDashing ? DASH_SPEED_MILLI_PER_SEC : BASE_SPEED_MILLI_PER_SEC;
            long numerator = positionRemainder + speed * elapsed;
            return new MovementStep(currentPositionMilli, currentPositionMilli + numerator / 3000,
                elapsed / 50d, isJumping, isDashing, jumpTimerFrames, heightMilli, numerator % 3000);
        }
        internal void ApplyStep(MovementStep step, long position)
        {
            // 区間終端まで進んだ場合だけ余りを持ち越す。接触で打切った移動は再開後へ漏らさない。
            positionRemainder = position == step.End ? step.EndRemainder : 0;
            currentPositionMilli = position; isJumping = step.WasJumping;
            jumpTimerFrames = step.JumpTimeFrames; heightMilli = step.StartHeightMilli;
            if (isJumping) UpdateJump(step.DurationFrames * step.Fraction(position));
        }
        private const int BASE_SPEED_MILLI_PER_SEC = 10000;  // 基本移動速度（ミリ単位/秒）= 10.0
        private const int DASH_SPEED_MILLI_PER_SEC = 20000;  // ダッシュ速度（ミリ単位/秒）= 20.0
        private const int JUMP_DURATION_FRAMES = 30;           // ジャンプ時間（論理フレーム）= 0.5秒
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

        /// <summary>既存ジャンプの進行率。表示用の参照であり、移動・高さの更新は行わない。</summary>
        public float JumpProgress => isJumping ? (float)(jumpTimerFrames / JUMP_DURATION_FRAMES) : 0f;

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
            isPaused = false; ControlChanged();
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
            var step = PreviewStep(deltaTimeMs);
            ApplyStep(step, step.End);
            lastStep = step; hasLastStep = true;
        }

        /// <summary>
        /// ダッシュ開始
        /// </summary>
        public void StartDash()
        {
            isDashing = true; ControlChanged();
        }

        /// <summary>
        /// ダッシュ停止
        /// </summary>
        public void StopDash()
        {
            isDashing = false; ControlChanged();
        }

        /// <summary>
        /// ジャンプ
        /// </summary>
        public void Jump()
        {
            if (isJumping || isPaused) return;

            isJumping = true;
            jumpTimerFrames = 0; ControlChanged();
        }

        /// <summary>
        /// ジャンプ更新
        /// </summary>
        private void UpdateJump(double elapsedFrames)
        {
            jumpTimerFrames += elapsedFrames;

            if (jumpTimerFrames >= JUMP_DURATION_FRAMES)
            {
                // ジャンプ終了
                isJumping = false;
                heightMilli = 0;
            }
            else
            {
                // 放物線運動（sin波で簡易実装）
                // t = jumpTimerFrames / JUMP_DURATION_FRAMES (0.0 ～ 1.0)
                float t = (float)jumpTimerFrames / JUMP_DURATION_FRAMES;
                heightMilli = (int)(Math.Sin(t * Math.PI) * JUMP_HEIGHT_MILLI);
            }
        }

        /// <summary>
        /// 一時停止（バトル中など）
        /// </summary>
        public void Pause()
        {
            isPaused = true; ControlChanged();
        }

        /// <summary>
        /// 再開
        /// </summary>
        public void Resume()
        {
            isPaused = false; ControlChanged();
        }

        /// <summary>
        /// 位置をリセット（ミリ単位版）
        /// </summary>
        public void SetPositionMilli(long positionMilli)
        {
            currentPositionMilli = positionMilli; positionRemainder = 0; ControlChanged();
        }

        /// <summary>
        /// 位置をリセット（float版、後方互換）
        /// </summary>
        public void SetPosition(float position)
        {
            SetPositionMilli((long)(position * 1000f));
        }
    }
}
