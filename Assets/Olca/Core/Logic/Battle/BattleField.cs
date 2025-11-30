using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 6x1グリッドのバトルフィールド
    /// </summary>
    public class BattleField
    {
        public const int GRID_WIDTH = 6;
        public const int GRID_HEIGHT = 1;

        private readonly PanelType[,] panels;

        public BattleField()
        {
            panels = new PanelType[GRID_WIDTH, GRID_HEIGHT];

            // 初期化: すべて通常パネル
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                for (int y = 0; y < GRID_HEIGHT; y++)
                {
                    panels[x, y] = PanelType.Normal;
                }
            }
        }

        /// <summary>
        /// 指定位置に移動可能か
        /// </summary>
        public bool CanMoveTo(Vector2Int position)
        {
            // グリッド範囲外
            if (!IsInBounds(position)) return false;

            // 穴パネルは移動不可（将来的にフロート能力を考慮）
            PanelType panelType = GetPanelType(position);
            if (panelType == PanelType.Hole) return false;

            return true;
        }

        /// <summary>
        /// グリッド範囲内か
        /// </summary>
        public bool IsInBounds(Vector2Int position)
        {
            return position.X >= 0 && position.X < GRID_WIDTH &&
                   position.Y >= 0 && position.Y < GRID_HEIGHT;
        }

        /// <summary>
        /// パネルタイプを変更
        /// </summary>
        public void ChangePanelType(Vector2Int position, PanelType type)
        {
            if (!IsInBounds(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");
            }

            panels[position.X, position.Y] = type;
            OnPanelChanged?.Invoke(position, type);
        }

        /// <summary>
        /// パネルタイプを取得
        /// </summary>
        public PanelType GetPanelType(Vector2Int position)
        {
            if (!IsInBounds(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");
            }

            return panels[position.X, position.Y];
        }

        /// <summary>
        /// すべてのパネルを通常に戻す
        /// </summary>
        public void ResetAllPanels()
        {
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                for (int y = 0; y < GRID_HEIGHT; y++)
                {
                    panels[x, y] = PanelType.Normal;
                }
            }
        }

        /// <summary>
        /// パネル変更時のイベント
        /// </summary>
        public event Action<Vector2Int, PanelType> OnPanelChanged;
    }
}
