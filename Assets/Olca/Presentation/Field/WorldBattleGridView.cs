using UnityEngine;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// World Space上の6x1バトルグリッド
    /// SpriteRendererを使用してダンジョン世界空間に描画する
    /// バトル開始時に接触位置へ出現し、バトル終了時に非表示になる
    /// </summary>
    public class WorldBattleGridView : MonoBehaviour
    {
        [Header("グリッド設定")]
        [SerializeField] private float panelSize = 1f;      // 1 Unity単位 = 1グリッドセル
        [SerializeField] private float panelY = -0.25f;     // グリッドのY座標（地面付近）
        [SerializeField] private float panelZ = 0.1f;       // 背景より手前・キャラより奥
        [SerializeField] private float panelScale = 0.95f;  // 隙間を作るスケール

        private const int GRID_WIDTH = 6;

        private BattleField battleField;
        private float originX;

        private SpriteRenderer[] panelRenderers;
        private Sprite whiteSprite;

        private void Awake()
        {
            whiteSprite = CreateWhiteSprite();
            panelRenderers = new SpriteRenderer[GRID_WIDTH];

            for (int x = 0; x < GRID_WIDTH; x++)
            {
                var go = new GameObject($"GridPanel_{x}");
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(panelScale, panelScale, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = whiteSprite;
                sr.sortingOrder = 1;  // 背景より手前、キャラより奥
                sr.color = GetPanelColor(PanelType.Normal);

                panelRenderers[x] = sr;
            }

            gameObject.SetActive(false);
        }

        /// <summary>バトル開始時にグリッドを接触位置へ配置して表示</summary>
        public void Show(BattleField field, float contactPositionX)
        {
            battleField = field;
            originX = contactPositionX;

            UpdatePanelPositions();
            Refresh();

            gameObject.SetActive(true);
        }

        /// <summary>バトル終了時にグリッドを非表示</summary>
        public void Hide()
        {
            battleField = null;
            gameObject.SetActive(false);
        }

        /// <summary>パネル状態を最新に更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (battleField == null) return;

            for (int x = 0; x < GRID_WIDTH; x++)
            {
                var panelType = battleField.GetPanelType(new Vector2Int(x, 0));
                panelRenderers[x].color = GetPanelColor(panelType);
            }
        }

        /// <summary>グリッドインデックスから World X 座標を返す</summary>
        public float GridIndexToWorldX(int gridX) => originX + gridX * panelSize;

        /// <summary>グリッド中央の World X 座標（カメラ中央合わせ用）</summary>
        public float GridCenterWorldX => originX + (GRID_WIDTH - 1) * 0.5f * panelSize;

        // ==================== 内部処理 ====================

        private void UpdatePanelPositions()
        {
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                panelRenderers[x].transform.position = new Vector3(
                    originX + x * panelSize, panelY, panelZ);
            }
        }

        private Color GetPanelColor(PanelType type)
        {
            switch (type)
            {
                case PanelType.Normal:  return new Color(0.75f, 0.75f, 0.75f, 0.85f);
                case PanelType.Ice:     return new Color(0.6f, 0.9f, 1.0f, 0.85f);
                case PanelType.Grass:   return new Color(0.5f, 0.85f, 0.5f, 0.85f);
                case PanelType.Magma:   return new Color(1.0f, 0.4f, 0.2f, 0.85f);
                case PanelType.Cracked: return new Color(0.9f, 0.85f, 0.5f, 0.85f);
                case PanelType.Hole:    return new Color(0.1f, 0.1f, 0.1f, 0.85f);
                default:                return new Color(0.5f, 0.5f, 0.5f, 0.85f);
            }
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
