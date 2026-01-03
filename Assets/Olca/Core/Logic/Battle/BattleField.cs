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
        private Vector2Int? protectedPosition;

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
        /// パネル保護機能: 保護位置が穴パネルに変更されようとした場合、ヒビパネルにする
        /// </summary>
        public void ChangePanelType(Vector2Int position, PanelType type)
        {
            if (!IsInBounds(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");
            }

            // パネル保護: 保護位置が穴パネルに変更されようとした場合、ヒビパネルにする
            if (protectedPosition.HasValue && position == protectedPosition.Value)
            {
                PanelType currentType = panels[position.X, position.Y];

                // 元々穴パネルでない場合のみ保護
                if (currentType != PanelType.Hole && type == PanelType.Hole)
                {
                    type = PanelType.Cracked;
                }
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
        /// 指定位置が穴パネルかどうかを判定
        /// </summary>
        /// <param name="position">判定する位置</param>
        /// <returns>穴パネルならtrue</returns>
        public bool IsHole(Vector2Int position)
        {
            if (!IsInBounds(position))
            {
                // 範囲外は穴扱い（投擲物が落下する）
                return true;
            }

            return GetPanelType(position) == PanelType.Hole;
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
        /// パネル保護を設定（一時移動の移動元を保護）
        /// </summary>
        public void SetProtectedPosition(Vector2Int? position)
        {
            protectedPosition = position;
        }

        /// <summary>
        /// パネル変更時のイベント
        /// </summary>
        public event Action<Vector2Int, PanelType> OnPanelChanged;
    }
}
