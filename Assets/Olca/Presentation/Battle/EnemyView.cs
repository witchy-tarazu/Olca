using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Presentation.Effects;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// 敵1体のView
    /// </summary>
    public class EnemyView : MonoBehaviour
    {
        [SerializeField] private Text enemyNameText;
        [SerializeField] private Text hpText;
        [SerializeField] private Image enemyImage;

        [Header("Effects")]
        [SerializeField] private HitEffectView hitEffectView;

        private Enemy enemy;
        private int previousHp;  // 前回のHP（ダメージ検出用）
        private CancellationTokenSource destroyCts;

        // BattleFieldViewと同じX座標オフセット（GridContainerのPivotが中心の場合）
        // パネル中心位置: -250, -150, -50, 50, 150, 250
        private const float GRID_OFFSET_X = -250f;

        // Y座標オフセット: EnemyContainerに対する相対座標
        // EnemyContainerが既にPos Y: 100に配置されているため、敵自体のY座標は0
        // （EnemyContainerの配置で高さが決定される）
        private const float ENEMY_Y_OFFSET = 0f;

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();

            // HitEffectViewが未設定なら自動で追加
            if (hitEffectView == null && enemyImage != null)
            {
                hitEffectView = enemyImage.gameObject.AddComponent<HitEffectView>();
                // 対象Imageを設定（リフレクションを使用）
                var targetImageField = typeof(HitEffectView).GetField("targetImage",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (targetImageField != null)
                {
                    targetImageField.SetValue(hitEffectView, enemyImage);
                    // targetImageを設定した後、明示的に初期化を呼ぶ
                    hitEffectView.InitializeIfNeeded();
                }
            }
        }

        public void Initialize(Enemy enemy)
        {
            this.enemy = enemy;
            previousHp = enemy.CurrentHp;
            Refresh();
        }

        public void Refresh()
        {
            if (enemy == null) return;

            enemyNameText.text = enemy.Name;
            hpText.text = $"{enemy.CurrentHp}";  // 数値のみ表示

            // 死亡時は非表示
            gameObject.SetActive(!enemy.IsDead);

            // 位置を敵のグリッド座標に合わせる（UI要素なのでRectTransformを使用）
            // X座標: BattleFieldViewと同じ座標系を使用（GridContainerのPivotが中心の場合）
            // Y座標: EnemyContainerに対する相対座標（EnemyContainerが既にPos Y: 100にあるため）
            var rectTransform = GetComponent<RectTransform>();
            rectTransform.anchoredPosition = new Vector2(GRID_OFFSET_X + enemy.GridPosition.X * 100, ENEMY_Y_OFFSET);
        }

        /// <summary>
        /// ダメージを受けた際のエフェクトを再生
        /// </summary>
        public async UniTask ShowDamageEffectAsync(int damage, DamageNumberPool damageNumberPool)
        {
            if (destroyCts.IsCancellationRequested) return;

            // 敵が死亡している場合はエフェクトを再生しない
            if (enemy != null && enemy.IsDead) return;

            // ヒットエフェクト再生
            if (hitEffectView != null)
            {
                PlayHitEffect().Forget();
            }

            // ダメージ数値表示
            if (damageNumberPool != null)
            {
                // HitEffectがlocalPositionを変更するため、anchoredPositionを直接取得すると
                // エフェクト中の座標になってしまう。そのため期待値を計算して使用する。
                float expectedX = GRID_OFFSET_X + enemy.GridPosition.X * 100;
                Vector2 damagePosition = new Vector2(expectedX, ENEMY_Y_OFFSET);
                await damageNumberPool.ShowDamageAsync(damage, damagePosition, destroyCts.Token);
            }
        }

        /// <summary>
        /// ヒットエフェクトを再生（非同期・並行実行）
        /// </summary>
        private async UniTaskVoid PlayHitEffect()
        {
            if (hitEffectView != null && !destroyCts.IsCancellationRequested)
            {
                await hitEffectView.PlayHitEffectAsync(destroyCts.Token);
            }
        }

        /// <summary>
        /// HPが減少した際に自動的にダメージエフェクトを再生（オプション）
        /// </summary>
        public void RefreshWithDamageDetection(DamageNumberPool damageNumberPool)
        {
            if (enemy == null) return;

            int currentHp = enemy.CurrentHp;
            int damage = previousHp - currentHp;

            // HPが減少していればダメージエフェクト再生
            if (damage > 0)
            {
                ShowDamageEffectAsync(damage, damageNumberPool).Forget();
            }

            previousHp = currentHp;
            Refresh();
        }

        private void OnDestroy()
        {
            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
