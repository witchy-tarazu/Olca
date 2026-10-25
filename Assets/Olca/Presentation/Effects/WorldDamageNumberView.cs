using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// World Space 用ダメージ数値View。
    /// TextMesh を使用して上昇＋フェードアウトアニメーションを行う。
    /// フォント・サイズ・色はすべて Prefab の Inspector で設定し、スクリプトからは変更しない。
    /// </summary>
    public class WorldDamageNumberView : MonoBehaviour
    {
        [Header("参照（Prefab でアサイン）")]
        [SerializeField] private TextMesh textMesh;

        [Header("アニメーション設定")]
        [SerializeField] private float floatSpeed = 2.0f;  // 上昇速度（World 単位/秒）
        [SerializeField] private float duration = 0.8f;     // アニメーション時間（秒）

        private Color originalColor;
        private CancellationTokenSource animCts;

        private void Awake()
        {
            if (textMesh != null)
                originalColor = textMesh.color;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// ダメージ値と World 座標を受け取り、アニメーションを再生する。
        /// 完了（またはキャンセル）まで await 可能。
        /// </summary>
        public UniTask ShowAsync(int damage,Vector3 worldPosition,CancellationToken ct)
            =>ShowTextAsync(damage.ToString(),worldPosition,ct);

        public async UniTask ShowTextAsync(string text, Vector3 worldPosition, CancellationToken ct)
        {
            if (textMesh == null) return;

            // 初期化
            transform.position = worldPosition;
            textMesh.text = text;
            textMesh.color = originalColor;
            gameObject.SetActive(true);

            animCts?.Cancel();
            animCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var startPos = worldPosition;
            float elapsed = 0f;

            try
            {
                while (elapsed < duration)
                {
                    if (animCts.Token.IsCancellationRequested) break;

                    elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);

                    // 上昇（Ease-out: 最初速く、途中で減速して止まる）
                    float eased = 1f - (1f - t) * (1f - t);
                    transform.position = startPos + new Vector3(0f, floatSpeed * duration * eased, 0f);

                    // フェードアウト
                    var c = originalColor;
                    c.a = 1f - t;
                    textMesh.color = c;

                    await Olca.Unity.BattleVisualClock.NextFrame(animCts.Token);
                }
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                // 破棄によるキャンセルではUnityオブジェクトにアクセスしない。
                if (this != null)
                {
                    gameObject.SetActive(false);
                    if (textMesh != null) textMesh.color = originalColor;
                }
            }
        }

        private void OnDestroy()
        {
            animCts?.Cancel();
            animCts?.Dispose();
        }
    }
}
