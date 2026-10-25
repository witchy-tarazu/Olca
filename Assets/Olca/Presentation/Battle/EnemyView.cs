using UnityEngine;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// 敵1体のView（World Space版）
    /// SpriteRenderer + TextMesh ベースで Canvas 不要
    /// WorldEnemyView と組み合わせて使用する
    /// </summary>
    public class EnemyView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer enemyImage;
        [SerializeField] private TextMesh enemyNameText;
        [SerializeField] private TextMesh hpText;
        [SerializeField] private Sprite gruntSprite;
        [SerializeField] private Sprite bossSprite;

        private Enemy enemy;
        private bool deathPresentationActive;
        public void SetDeathPresentationActive(bool active)=>deathPresentationActive=active;

        /// <summary>WorldEnemyView がダメージフラッシュに使う SpriteRenderer</summary>
        public SpriteRenderer BodyRenderer => enemyImage;

        public void Initialize(Enemy enemy)
        {
            this.enemy = enemy;
            ApplyAppearance();

            if (enemyNameText != null)
                enemyNameText.text = enemy.Name;

            // HP/名前テキストをグリッド・敵スプライトより手前に描画
            SetTextSortingOrder(hpText, 3);
            SetTextSortingOrder(enemyNameText, 3);

            HideHp(); // ダンジョン探索中は名前・HP非表示
            Refresh();
        }

        /// <summary>既存の敵IDに対応する仮素材を表示する。戦闘データは変更しない。</summary>
        private void ApplyAppearance()
        {
            var sprite = enemy.EnemyId == "boss" ? bossSprite : gruntSprite;
            if (enemyImage == null || sprite == null) return;

            enemyImage.sprite = sprite;
            enemyImage.color = Color.white;
            enemyImage.flipX = true; // 右向きの原画を、左側のプレイヤーへ向ける

            // 大型ボスでも名前とHPが絵に重ならないよう、スプライトの上に配置する。
            float top = sprite.bounds.max.y;
            if (enemyNameText != null)
                enemyNameText.transform.localPosition = new Vector3(0f, top + 1.05f, -0.1f);
            if (hpText != null)
                hpText.transform.localPosition = new Vector3(0f, top + 0.25f, -0.1f);
        }

        private static void SetTextSortingOrder(TextMesh tm, int order)
        {
            if (tm == null) return;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = order;
        }

        /// <summary>HPテキストと死亡表示を更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (enemy == null) return;
            if (hpText != null)
                hpText.text = $"{enemy.CurrentHp}";
            gameObject.SetActive(!enemy.IsDead||deathPresentationActive);
        }

        /// <summary>バトル開始時に呼ぶ（名前・HP表示）</summary>
        public void ShowHp()
        {
            if (hpText != null) hpText.gameObject.SetActive(true);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(true);
        }

        /// <summary>バトル終了時に呼ぶ（名前・HP非表示）</summary>
        public void HideHp()
        {
            if (hpText != null) hpText.gameObject.SetActive(false);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(false);
        }
    }
}
