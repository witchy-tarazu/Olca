using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Logic.Dungeon;
using Olca.Presentation.Field;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// 接敵アニメーションを担当するコンポーネント
    /// 通常接敵・ダッシュ接敵それぞれのシーケンスを UniTask で実装する
    /// </summary>
    public class ContactAnimator : MonoBehaviour
    {
        [Header("アニメーション時間設定")]
        [SerializeField] private float normalContactDuration = 0.4f;   // 通常接敵: 停止アニメーション
        [SerializeField] private float dashRushDuration = 0.2f;        // ダッシュ: 突進
        [SerializeField] private float dashRollbackDuration = 0.2f;    // ダッシュ: プレイヤー宙返り戻り
        [SerializeField] private float pauseAfterContactDuration = 0.1f; // 接触後の一時停止

        [Header("移動量設定")]
        [SerializeField] private float normalBumpDistance = 0.15f;      // 通常接敵: 衝突時の前進量
        [SerializeField] private float normalKnockbackDistance = 3f;    // 通常接敵: ノックバック距離（接触距離1マス分+2マス余裕）
        [SerializeField] private float normalKnockbackDuration = 0.35f; // 通常接敵: ノックバック時間
        [SerializeField] private float dashRushDistance = 1f;       // ダッシュ: プレイヤーの突進距離（rushEnd = start + dashRushDistance + 0.5）
        [SerializeField] private float dashEnemyKnockback = 2f;   // ダッシュ: 敵の吹き飛び距離（グリッド配置位置に合わせて2に設定）
        [SerializeField] private float rollbackDistance = 1f;    // プレイヤーの宙返り距離（ダッシュ）
        [SerializeField] private float jumpHeight = 0.5f;        // 宙返り時の高さ

        /// <summary>
        /// 接敵アニメーションを再生する
        /// </summary>
        /// <param name="contactInfo">接敵情報</param>
        /// <param name="playerSprite">プレイヤースプライト（DungeonViewから渡される）</param>
        /// <param name="enemySprite">接触した敵のスプライト（ContactAnimatorが生成・管理）</param>
        /// <param name="ct">キャンセルトークン</param>
        public async UniTask PlayAsync(ContactInfo contactInfo, Transform playerTransform,
            Transform enemyTransform = null, CancellationToken ct = default)
        {
            if (contactInfo.IsDashContact)
                await PlayDashContactAsync(playerTransform, enemyTransform, ct);
            else
                await PlayNormalContactAsync(playerTransform, ct);
        }

        // ==================== 通常接敵 ====================

        /// <summary>
        /// 通常接敵:
        /// 1. わずかに前進（衝突の停止感）
        /// 2. 一時停止（衝撃）
        /// 3. ノックバック後退
        /// </summary>
        private async UniTask PlayNormalContactAsync(Transform playerTransform, CancellationToken ct)
        {
            if (playerTransform == null) return;

            Vector3 startPos = playerTransform.position;

            // フェーズ1: わずかに前進（衝突感）
            Vector3 bumpPos = startPos + new Vector3(normalBumpDistance, 0f, 0f);
            float elapsed = 0f;
            float bumpDuration = normalContactDuration * 0.25f;
            while (elapsed < bumpDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bumpDuration);
                playerTransform.position = Vector3.Lerp(startPos, bumpPos, EaseOutQuad(t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            playerTransform.position = bumpPos;

            // フェーズ2: 衝撃の一時停止
            await UniTask.Delay((int)(pauseAfterContactDuration * 1000), cancellationToken: ct);

            // フェーズ3: ノックバック後退
            Vector3 knockbackPos = bumpPos - new Vector3(normalKnockbackDistance, 0f, 0f);
            elapsed = 0f;
            while (elapsed < normalKnockbackDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / normalKnockbackDuration);
                playerTransform.position = Vector3.Lerp(bumpPos, knockbackPos, EaseOutQuad(t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            playerTransform.position = knockbackPos;
        }

        // ==================== ダッシュ接敵 ====================

        /// <summary>
        /// ダッシュ接敵シーケンス:
        /// 1. プレイヤーが突進
        /// 2. 一時停止（衝撃）
        /// 3. 敵が吹き飛び＋プレイヤーが宙返りで戻る（同時）
        /// </summary>
        private async UniTask PlayDashContactAsync(Transform playerTransform, Transform enemyTransform, CancellationToken ct)
        {
            if (playerTransform == null) return;

            Vector3 rushStart = playerTransform.position;
            Vector3 rushEnd = rushStart + new Vector3(dashRushDistance + 0.5f, 0f, 0f);

            // フェーズ1: 突進（EaseInQuad で加速感）
            float elapsed = 0f;
            while (elapsed < dashRushDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dashRushDuration);
                playerTransform.position = Vector3.Lerp(rushStart, rushEnd, EaseInQuad(t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            playerTransform.position = rushEnd;

            // フェーズ2: 衝撃の一時停止
            await UniTask.Delay((int)(pauseAfterContactDuration * 1000), cancellationToken: ct);

            // フェーズ3: プレイヤー宙返り戻り + 敵吹き飛び（同時）
            // ノックバック開始と同時にダメージエフェクトを発動（フェーズ2までの自動発動を抑制済み）
            enemyTransform?.GetComponent<WorldEnemyView>()?.TriggerPendingDamageEffect();

            Vector3 rollStart = rushEnd;
            Vector3 rollEnd = rushEnd - new Vector3(rollbackDistance, 0f, 0f);
            Vector3 enemyStart = enemyTransform != null ? enemyTransform.position : Vector3.zero;
            Vector3 enemyEnd = enemyStart + new Vector3(dashEnemyKnockback, 0f, 0f);

            elapsed = 0f;
            while (elapsed < dashRollbackDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dashRollbackDuration);

                // プレイヤー: 放物線移動（X: 線形、Y: 放物線）
                float x = Mathf.Lerp(rollStart.x, rollEnd.x, t);
                float y = rollStart.y + jumpHeight * Mathf.Sin(t * Mathf.PI);
                playerTransform.position = new Vector3(x, y, rollStart.z);

                // 敵: 吹き飛び（EaseOutExpo: 初速が非常に速く、終速がゆっくり止まる）
                if (enemyTransform != null)
                    enemyTransform.position = Vector3.Lerp(enemyStart, enemyEnd, EaseOutExpo(t));

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            playerTransform.position = rollEnd;
            if (enemyTransform != null)
                enemyTransform.position = enemyEnd;
        }

        // ==================== バトル後復帰 ====================

        /// <summary>
        /// バトル後の定位置復帰ジャンプ
        /// 現在位置から指定位置へ放物線を描いて移動する（ダッシュ宙返りと同じ弧）
        /// </summary>
        public async UniTask PlayReturnJumpAsync(Transform playerTransform, Vector3 targetPosition, CancellationToken ct = default)
        {
            if (playerTransform == null) return;

            Vector3 jumpStart = playerTransform.position;
            // X方向のみ目標位置へ移動、Y/Zは元の高さを維持
            Vector3 jumpEnd = new Vector3(targetPosition.x, jumpStart.y, jumpStart.z);

            float elapsed = 0f;
            while (elapsed < dashRollbackDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dashRollbackDuration);

                // X: 線形補間、Y: 放物線（ダッシュ宙返りと同じ）
                float x = Mathf.Lerp(jumpStart.x, jumpEnd.x, t);
                float y = jumpStart.y + jumpHeight * Mathf.Sin(t * Mathf.PI);
                playerTransform.position = new Vector3(x, y, jumpStart.z);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            playerTransform.position = jumpEnd;
        }

        // ==================== イージング ====================

        private float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        private float EaseInQuad(float t) => t * t;
        // 指数的イーズアウト: 初速が非常に速く、終盤でなめらかに止まる
        private float EaseOutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
    }
}
