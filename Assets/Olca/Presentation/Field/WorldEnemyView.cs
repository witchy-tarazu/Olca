using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// World Space上の敵スプライト
    /// ダンジョン探索中・バトル中を問わず同一オブジェクトが継続して存在する
    /// - ダンジョン中: ダンジョンマップ上の配置座標に表示
    /// - バトル中: WorldBattleGridView のグリッド座標に移動して表示
    /// </summary>
    public class WorldEnemyView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private UnityEngine.UI.Text nameLabel;  // World Space Canvas上のラベル（省略可）

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
        public void Initialize(Enemy enemy, float dungeonX, Sprite sprite = null)
        {
            this.enemy = enemy;
            this.dungeonPositionX = dungeonX;
            this.gridView = null;
            this.previousHp = enemy.CurrentHp;

            if (bodyRenderer != null && sprite != null)
                bodyRenderer.sprite = sprite;

            if (nameLabel != null)
                nameLabel.text = enemy.Name;

            UpdatePosition();
        }

        // ==================== バトル遷移 ====================

        /// <summary>バトル開始時 - グリッド座標モードに切り替え（グリッド範囲内の敵のみ）</summary>
        public void OnBattleStarted(WorldBattleGridView grid)
        {
            positionAnimating = false;  // アニメーション終了・位置上書きを再開
            if (enemy == null) return;
            // GridPosition.X がグリッド範囲外の敵はバトルに参加しない → 一時非表示
            if (enemy.GridPosition.X < 0 || enemy.GridPosition.X >= 6)
            {
                hiddenForBattle = true;
                gameObject.SetActive(false);
                return;
            }
            gridView = grid;
            UpdatePosition();
        }

        /// <summary>バトル終了時 - ダンジョン座標モードに戻す</summary>
        public void OnBattleEnded()
        {
            gridView = null;
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

            // バトル不参加で非表示中は何もしない（SetActive を上書きしない）
            if (hiddenForBattle) return;

            // 死亡時は非表示
            gameObject.SetActive(!enemy.IsDead);
            if (enemy.IsDead) return;

            // アニメーション中は ContactAnimator が位置を管理するので上書きしない
            if (!positionAnimating) UpdatePosition();

            // HP変化を検出してダメージエフェクト
            int currentHp = enemy.CurrentHp;
            int damage = previousHp - currentHp;
            if (damage > 0)
            {
                PlayDamageEffectAsync(damage, destroyCts.Token).Forget();
            }
            previousHp = currentHp;
        }

        // ==================== エフェクト ====================

        private async UniTaskVoid PlayDamageEffectAsync(int damage, CancellationToken ct)
        {
            if (bodyRenderer == null) return;

            // 点滅エフェクト（ヒット感）
            var originalColor = bodyRenderer.color;
            var hitColor = Color.white;

            for (int i = 0; i < 2; i++)
            {
                bodyRenderer.color = hitColor;
                await UniTask.Delay(50, cancellationToken: ct);
                bodyRenderer.color = originalColor;
                await UniTask.Delay(50, cancellationToken: ct);
            }

            // ダメージ数値をWorld Spaceに浮遊表示
            ShowWorldDamageNumber(damage);
        }

        private void ShowWorldDamageNumber(int damage)
        {
            // TextMesh を使ったシンプルな浮遊数値
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
            float worldX;
            if (gridView != null && enemy != null)
                worldX = gridView.GridIndexToWorldX(enemy.GridPosition.X);
            else
                worldX = dungeonPositionX;

            transform.position = new Vector3(worldX, transform.position.y, transform.position.z);
        }

        private void OnDestroy()
        {
            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
