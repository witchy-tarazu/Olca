using Olca.Core.Interfaces;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Inputを使用した入力プロバイダー
    /// </summary>
    public class UnityInputProvider : IInputProvider
    {
        private float basicAttackButtonPressTime;
        private bool wasBasicAttackButtonPressed;
        private float lastHorizontal;

        public float Horizontal => Input.GetAxisRaw("Horizontal");

        public float HorizontalDown
        {
            get
            {
                float current = Input.GetAxisRaw("Horizontal");
                // 前フレームが0で、今フレームが0でない場合に立ち上がり
                if (lastHorizontal == 0f && current != 0f)
                {
                    return current;
                }
                return 0f;
            }
        }

        public float Vertical => Input.GetAxisRaw("Vertical");

        public bool JumpButtonDown => Input.GetKeyDown(KeyCode.Space);

        public bool DashButton => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public bool CardUseButtonDown => Input.GetKeyDown(KeyCode.X);

        public bool BasicAttackButton => Input.GetKey(KeyCode.Z);

        public float BasicAttackButtonHoldTime => basicAttackButtonPressTime;

        public bool CustomScreenButtonDown => Input.GetKeyDown(KeyCode.C);

        /// <summary>
        /// 毎フレーム呼び出して状態を更新
        /// </summary>
        public void Update()
        {
            // 固有攻撃ボタンの長押し時間を計測
            if (BasicAttackButton)
            {
                if (!wasBasicAttackButtonPressed)
                {
                    // ボタンが押された瞬間
                    basicAttackButtonPressTime = 0f;
                    wasBasicAttackButtonPressed = true;
                }
                else
                {
                    // 押され続けている
                    basicAttackButtonPressTime += UnityEngine.Time.deltaTime;
                }
            }
            else
            {
                // ボタンが離された
                basicAttackButtonPressTime = 0f;
                wasBasicAttackButtonPressed = false;
            }

            // 前フレームの横方向入力を記録（HorizontalDownの立ち上がり検出用）
            lastHorizontal = Input.GetAxisRaw("Horizontal");
        }
    }
    /// <summary>表示・非同期演出と入力のポーズ境界。Coreの停止理由とは独立して扱う。</summary>
    public static class GameplayPause
    {
        public static bool IsPaused { get; private set; }
        private static bool awaitRelease;
        private static int resumeFrame = -2;
        public static bool InputBlocked => IsPaused || awaitRelease || Time.frameCount <= resumeFrame + 1;
        public static float DeltaTime => IsPaused ? 0f : Time.deltaTime;
        public static void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (!paused) { awaitRelease = true; resumeFrame = Time.frameCount; }
        }
        public static void UpdateInputBarrier()
        {
            if (!IsPaused && awaitRelease && !Input.anyKey)
            { awaitRelease = false; resumeFrame = Time.frameCount; }
        }
        public static async Cysharp.Threading.Tasks.UniTask WaitAsync(System.Threading.CancellationToken ct = default)
        {
            while (IsPaused) await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
            ct.ThrowIfCancellationRequested();
        }
        public static async Cysharp.Threading.Tasks.UniTask NextFrame(System.Threading.CancellationToken ct = default)
        {
            await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
            await WaitAsync(ct);
        }
        public static async Cysharp.Threading.Tasks.UniTask Delay(int milliseconds, System.Threading.CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken);
            float remaining = milliseconds / 1000f;
            while (remaining > 0f) { await NextFrame(cancellationToken); remaining -= DeltaTime; }
        }
        public static Cysharp.Threading.Tasks.UniTask Delay(System.TimeSpan duration, System.Threading.CancellationToken cancellationToken = default)
            => Delay((int)duration.TotalMilliseconds, cancellationToken);
    }
}
