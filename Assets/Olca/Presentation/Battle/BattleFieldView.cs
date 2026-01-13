using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// バトルフィールド（6x1グリッド）の表示
    /// </summary>
    public class BattleFieldView : MonoBehaviour
    {
        [SerializeField] private GameObject panelPrefab;
        [SerializeField] private Transform gridContainer;
        [SerializeField] private GameObject playerMarker;

        private BattleField field;
        private PlayerCharacter player;
        private Image[] panelImages = new Image[6];

        // GridContainerのPivotが(0.5, 0.5)の場合、中心が配置基準点
        // 6パネル×100px = 600px幅なので、左端は中心から-300px
        // パネル中心位置: -250, -150, -50, 50, 150, 250
        private const float GRID_OFFSET_X = -250f;  // 最左パネルの中心位置

        public void Initialize(BattleField field, PlayerCharacter player)
        {
            this.field = field;
            this.player = player;

            // 6x1グリッド生成
            for (int x = 0; x < 6; x++)
            {
                var panelObj = Instantiate(panelPrefab, gridContainer);
                panelImages[x] = panelObj.GetComponent<Image>();

                // パネルの位置設定（横並び）UI要素なのでRectTransformを使用
                // GridContainerのPivotが中心なので、中心基準で配置
                var rectTransform = panelObj.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = new Vector2(GRID_OFFSET_X + x * 100, 0);
            }

            Refresh();
        }

        public void Refresh()
        {
            // パネルの色を状態に応じて変更
            for (int x = 0; x < 6; x++)
            {
                var panelType = field.GetPanelType(new Vector2Int(x, 0));
                panelImages[x].color = GetPanelColor(panelType);
            }

            // プレイヤー位置マーカー更新（UI要素なのでRectTransformを使用）
            // パネルと同じ座標系を使用
            var playerPos = player.GridPosition;
            var markerRect = playerMarker.GetComponent<RectTransform>();
            markerRect.anchoredPosition = new Vector2(GRID_OFFSET_X + playerPos.X * 100, 60);
        }

        private Color GetPanelColor(PanelType type)
        {
            switch (type)
            {
                case PanelType.Normal: return Color.white;
                case PanelType.Ice: return Color.cyan;
                case PanelType.Grass: return Color.green;
                case PanelType.Magma: return Color.red;
                case PanelType.Cracked: return Color.yellow;
                case PanelType.Hole: return Color.black;
                default: return Color.gray;
            }
        }
    }
}
