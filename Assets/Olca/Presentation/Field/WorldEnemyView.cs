using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Presentation.Battle;

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
        private float dungeonPositionX;       // ダンジョン上の配置X座標
        private WorldBattleGridView gridView;  // バトル中のみセットされる
        private int previousHp;
        private bool positionAnimating;       // trueの間はRefreshによる位置上書きを禁止
        private bool hiddenForBattle;         // バトル不参加のため一時非表示中
        private CancellationTokenSource destroyCts;

        /// <summary>この View が追跡している Enemy（ContactAnimator のルックアップ用）</summary>
        public Enemy TrackedEnemy => enemy;

        /// <summary>アニメーション中フラグ。trueの間 Refresh は位置を更新しない。</summary>
        public void SetPositionAnimating(bool value) { positionAnimating = value; }

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();
        }

        // ==================== 初期化 ====================

        /// <summary>ダンジョン配置時に呼ぶ</summary>
        public void Initialize(Enemy enemy, float dungeonX)
        {
            this.enemy = enemy;
            this.dungeonPositionX = dungeonX;
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

        /// <summary>バトル終了時 - ダンジョン座標モードに戻す</summary>
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

        private async UniTaskVoid PlayDamageEffectAsync(int damage, CancellationToken ct)
        {
            var bodyRenderer = enemyView?.BodyRenderer;
            if (bodyRenderer == null) return;

            // 点滅エフェクト（ヒット感）
            var originalColor = bodyRenderer.color;
            for (int i = 0; i < 2; i++)
            {
                bodyRenderer.color = Color.white;
                await UniTask.Delay(50, cancellationToken: ct);
                bodyRenderer.color = originalColor;
                await UniTask.Delay(50, cancellationToken: ct);
            }

            ShowWorldDamageNumber(damage);
        }

        private void ShowWorldDamageNumber(int damage)
        {
            var go = new GameObject("DamageNumber");
            go.transform.position = transform.position + new Vector3(0f, 0.6f, -0.5f);

            var tm = go.AddComponent<TextMesh>();
            tm.text = damage.ToString();
            tm.fontSize = 24;
            tm.color = Color.red;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            FloatAndFadeAsync(go, destroyCts.Token).Forget();
        }

        private async UniTaskVoid FloatAndFadeAsync(GameObject go, CancellationToken ct)
        {
            float duration = 0.8f;
            float elapsed = 0f;
            var tm = go.GetComponent<TextMesh>();
            var startPos = go.transform.position;

            while (elapsed < duration && go != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                go.transform.position = startPos + new Vector3(0f, t * 0.5f, 0f);
                if (tm != null)
                    tm.color = new Color(1f, 0.2f, 0.2f, 1f - t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (go != null) Destroy(go);
        }

        // ==================== 座標計算 ====================

        private void UpdatePosition()
        {
            float worldX = (gridView != null && enemy != null)
                ? gridView.GridIndexToWorldX(enemy.GridPosition.X)
                : dungeonPositionX;

            transform.position = new Vector3(worldX, transform.position.y, transform.position.z);
        }

        private void OnDestroy()
        {
            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
