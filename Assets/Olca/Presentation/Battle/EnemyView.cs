using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;

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

        private Enemy enemy;

        // BattleFieldViewと同じX座標オフセット（GridContainerのPivotが中心の場合）
        // パネル中心位置: -250, -150, -50, 50, 150, 250
        private const float GRID_OFFSET_X = -250f;

        // Y座標オフセット: EnemyContainerに対する相対座標
        // EnemyContainerが既にPos Y: 100に配置されているため、敵自体のY座標は0
        // （EnemyContainerの配置で高さが決定される）
        private const float ENEMY_Y_OFFSET = 0f;

        public void Initialize(Enemy enemy)
        {
            this.enemy = enemy;
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
    }
}
