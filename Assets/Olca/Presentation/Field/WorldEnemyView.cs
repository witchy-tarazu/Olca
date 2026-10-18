using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Presentation.Battle;
using Olca.Presentation.Effects;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// World Space上の敵スプライト
    /// ダンジョン探索中・バトル中を問わず同一オブジェクトが継続して存在する
    /// - ダンジョン中: ダンジョンマップ上の配置座標に表示。HPは非表示
    /// - バトル中: WorldBattleGridView のグリッド座標に移動。HPを表示
    /// EnemyView と組み合わせて使用する（視覚表現は EnemyView に委譲）
    /// </summary>
    public class WorldEnemyView : MonoBehaviour
    {
        [SerializeField] private EnemyView enemyView;  // 視覚表現コンポーネント

        private Enemy enemy;
        private float dungeonWorldX;       // 換算済みのダンジョン配置World X
        private WorldBattleGridView gridView;  // バトル中のみセットされる
        private int previousHp;
        private int pendingContactDamage;     // バトル開始前に適用されたダッシュダメージ（フェーズ3で発動）
        private bool positionAnimating;       // trueの間はRefreshによる位置上書きを禁止
        private bool hiddenForBattle;         // バトル不参加のため一時非表示中
        private CancellationTokenSource destroyCts;
        private WorldDamageNumberPool damageNumberPool;

        /// <summary>この View が追跡している Enemy（ContactAnimator のルックアップ用）</summary>
        public Enemy TrackedEnemy => enemy;

        /// <summary>アニメーション中フラグ。trueの間 Refresh は位置を更新しない。</summary>
        public void SetPositionAnimating(bool value) { positionAnimating = value; }

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();
        }

        // ==================== 初期化 ====================

        /// <summary>ダメージ数値プールを設定する（GameFieldView から呼ぶ）</summary>
        public void SetDamageNumberPool(WorldDamageNumberPool pool)
        {
            damageNumberPool = pool;
        }

        /// <summary>ダンジョン配置時に呼ぶ</summary>
        public void Initialize(Enemy enemy, float dungeonX)
        {
            this.enemy = enemy;
            this.dungeonWorldX = dungeonX;
            this.gridView = null;
            this.previousHp = enemy.CurrentHp;

            // Prefab 経由では Sprite がシリアライズされないためランタイムで生成
            var bodyRenderer = enemyView?.BodyRenderer;
            if (bodyRenderer != null && bodyRenderer.sprite == null)
                bodyRenderer.sprite = CreateWhiteSprite();

            enemyView?.Initialize(enemy);
            UpdatePosition();
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        // ==================== バトル遷移 ====================

        /// <summary>バトル開始時 - グリッド座標モードに切り替え（グリッド範囲内の敵のみ）</summary>
        public void OnBattleStarted(WorldBattleGridView grid)
        {
            positionAnimating = false;
            if (enemy == null) return;

            // バトル開始前（ダッシュ接触時）に適用済みのダメージをここで保存し、
            // Refresh() による自動エフェクト発動を防ぐ。
            // エフェクトは TriggerPendingDamageEffect() でノックバックと同時に発動させる。
            pendingContactDamage = previousHp - enemy.CurrentHp;
            previousHp = enemy.CurrentHp;
            // GridPosition.X がグリッド範囲外の敵はバトルに参加しない → 一時非表示
            if (enemy.GridPosition.X < 0 || enemy.GridPosition.X >= 6)
            {
                hiddenForBattle = true;
                gameObject.SetActive(false);
                return;
            }
            gridView = grid;
            enemyView?.ShowHp();
            UpdatePosition();
        }

        /// <summary>バトル終了時 - ダンジョン座標モードに戻す。撃破済みの敵は非表示にする。</summary>
        public void OnBattleEnded()
        {
            gridView = null;
            enemyView?.HideHp();
            if (hiddenForBattle)
            {
                hiddenForBattle = false;
                if (enemy != null && !enemy.IsDead)
                    gameObject.SetActive(true);
            }
            // 撃破済みの敵は非表示（カード選択中に背後に映らないよう）
            if (enemy != null && enemy.IsDead)
            {
                gameObject.SetActive(false);
                return;
            }
            UpdatePosition();
        }

        // ==================== 毎フレーム更新 ====================

        /// <summary>位置・状態を更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (enemy == null) return;
            if (hiddenForBattle) return;

            enemyView?.Refresh();
            if (enemy.IsDead) return;

            if (!positionAnimating) UpdatePosition();

            // HP変化を検出してダメージエフェクト
            int currentHp = enemy.CurrentHp;
            int damage = previousHp - currentHp;
            if (damage > 0)
                PlayDamageEffectAsync(damage, destroyCts.Token).Forget();
            previousHp = currentHp;
        }

        // ==================== エフェクト ====================

        /// <summary>
        /// ダッシュ接触ダメージのエフェクトを発動する（ContactAnimator のノックバック開始時に呼ぶ）
        /// OnBattleStarted() で保存した pendingContactDamage を消化する
        /// </summary>
        public void TriggerPendingDamageEffect()
        {
            if (pendingContactDamage <= 0) return;
            PlayDamageEffectAsync(pendingContactDamage, destroyCts.Token).Forget();
            pendingContactDamage = 0;
        }

        private async UniTaskVoid PlayDamageEffectAsync(int damage, CancellationToken ct)
        {
            var bodyRenderer = enemyView?.BodyRenderer;
            if (bodyRenderer == null) return;

            ShowWorldDamageNumber(damage);

            // 点滅 + スケールパルスを並行実行（ヒット感）
            await UniTask.WhenAll(
                FlashEffectAsync(bodyRenderer, ct),
                ScalePulseEffectAsync(ct)
            );
        }

        private async UniTask FlashEffectAsync(SpriteRenderer bodyRenderer, CancellationToken ct)
        {
            var originalColor = bodyRenderer.color;
            for (int i = 0; i < 2; i++)
            {
                bodyRenderer.color = Color.white;
                await Olca.Unity.GameplayPause.Delay(50, cancellationToken: ct);
                bodyRenderer.color = originalColor;
                await Olca.Unity.GameplayPause.Delay(50, cancellationToken: ct);
            }
        }

        private async UniTask ScalePulseEffectAsync(CancellationToken ct)
        {
            var originalScale = transform.localScale;
            var peakScale = originalScale * 1.25f;
            float halfDuration = 0.08f;
            float elapsed = 0f;

            // 拡大フェーズ
            while (elapsed < halfDuration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                transform.localScale = Vector3.Lerp(originalScale, peakScale, elapsed / halfDuration);
                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            elapsed = 0f;

            // 縮小フェーズ
            while (elapsed < halfDuration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                transform.localScale = Vector3.Lerp(peakScale, originalScale, elapsed / halfDuration);
                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            transform.localScale = originalScale;
        }

        private void ShowWorldDamageNumber(int damage)
        {
            if (damageNumberPool == null) return;
            var position = transform.position + new Vector3(0f, 0.6f, -0.5f);
            damageNumberPool.ShowDamage(damage, position, destroyCts.Token);
        }

        // ==================== 座標計算 ====================

        private void UpdatePosition()
        {
            float worldX = (gridView != null && enemy != null)
                ? gridView.GridIndexToWorldX(enemy.GridPosition.X)
                : dungeonWorldX;

            transform.position = new Vector3(worldX, transform.position.y, transform.position.z);
        }

        private void OnDestroy()
        {
            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
