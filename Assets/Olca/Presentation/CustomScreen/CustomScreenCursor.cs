using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタム画面のカーソル管理
    /// 5×2グリッド + 確定ボタンのカーソル移動を制御
    /// </summary>
    public class CustomScreenCursor
    {
        private const int GRID_COLUMNS = 5;  // 横5列
        private const int GRID_ROWS = 2;     // 縦2行

        private int currentCardIndex = -1;   // 現在のカードインデックス (-1: 確定ボタン)
        private int totalCards = 0;          // 手札の総数

        /// <summary>
        /// カーソル位置が変更された際のイベント
        /// </summary>
        public event System.Action<int> OnCursorMoved;  // -1=確定ボタン, 0-9=カードインデックス

        /// <summary>
        /// 現在のカーソル位置（カードインデックス、-1=確定ボタン）
        /// </summary>
        public int CurrentIndex => currentCardIndex;

        /// <summary>
        /// 確定ボタンにカーソルがあるか
        /// </summary>
        public bool IsOnConfirmButton => currentCardIndex == -1;

        /// <summary>
        /// 初期化
        /// </summary>
        public void Initialize(int cardCount)
        {
            totalCards = cardCount;
            currentCardIndex = 0;  // 最初のカードにカーソルを配置
            OnCursorMoved?.Invoke(currentCardIndex);
        }

        /// <summary>
        /// カーソルを左に移動
        /// </summary>
        public void MoveLeft()
        {
            if (IsOnConfirmButton)
            {
                // 確定ボタンでは左右移動しない
                return;
            }

            int row = currentCardIndex / GRID_COLUMNS;
            int col = currentCardIndex % GRID_COLUMNS;

            // 同じ行の左端・右端を計算
            int rowStartIndex = row * GRID_COLUMNS;
            int rowEndIndex = Mathf.Min(rowStartIndex + GRID_COLUMNS - 1, totalCards - 1);

            // 左に移動（ラップアラウンド）
            col--;
            if (currentCardIndex == rowStartIndex)
            {
                // 左端から右端へ
                currentCardIndex = rowEndIndex;
            }
            else
            {
                currentCardIndex = rowStartIndex + col;
            }

            OnCursorMoved?.Invoke(currentCardIndex);
        }

        /// <summary>
        /// カーソルを右に移動
        /// </summary>
        public void MoveRight()
        {
            if (IsOnConfirmButton)
            {
                // 確定ボタンでは左右移動しない
                return;
            }

            int row = currentCardIndex / GRID_COLUMNS;
            int col = currentCardIndex % GRID_COLUMNS;

            // 同じ行の左端・右端を計算
            int rowStartIndex = row * GRID_COLUMNS;
            int rowEndIndex = Mathf.Min(rowStartIndex + GRID_COLUMNS - 1, totalCards - 1);

            // 右に移動（ラップアラウンド）
            col++;
            if (currentCardIndex == rowEndIndex)
            {
                // 右端から左端へ
                currentCardIndex = rowStartIndex;
            }
            else
            {
                currentCardIndex = rowStartIndex + col;
            }

            OnCursorMoved?.Invoke(currentCardIndex);
        }

        /// <summary>
        /// カーソルを上に移動
        /// </summary>
        public void MoveUp()
        {
            if (IsOnConfirmButton)
            {
                // 確定ボタンから下段の中央付近のカードへ
                int bottomRowStart = GRID_COLUMNS;
                int bottomRowEnd = totalCards - 1;

                if (bottomRowEnd >= bottomRowStart)
                {
                    // 下段のカードが存在する場合、中央のカードに移動
                    int bottomRowCount = bottomRowEnd - bottomRowStart + 1;
                    int centerIndex = bottomRowStart + bottomRowCount / 2;
                    currentCardIndex = centerIndex;
                    OnCursorMoved?.Invoke(currentCardIndex);
                }
                else
                {
                    // 下段にカードがない場合、上段の中央へ
                    currentCardIndex = totalCards / 2;
                    OnCursorMoved?.Invoke(currentCardIndex);
                }
                return;
            }

            int row = currentCardIndex / GRID_COLUMNS;
            int col = currentCardIndex % GRID_COLUMNS;

            if (row == 0)
            {
                // 上段では上に移動しない
                return;
            }

            // 下段から上段へ
            int targetIndex = col;
            if (targetIndex < totalCards)
            {
                currentCardIndex = targetIndex;
                OnCursorMoved?.Invoke(currentCardIndex);
            }
        }

        /// <summary>
        /// カーソルを下に移動
        /// </summary>
        public void MoveDown()
        {
            if (IsOnConfirmButton)
            {
                // 確定ボタンでは下に移動しない
                return;
            }

            int row = currentCardIndex / GRID_COLUMNS;
            int col = currentCardIndex % GRID_COLUMNS;

            if (row == 0)
            {
                // 上段から下段へ
                int targetIndex = GRID_COLUMNS + col;

                if (targetIndex < totalCards)
                {
                    // 下段にカードが存在する場合、そのカードへ
                    currentCardIndex = targetIndex;
                }
                else
                {
                    // 下段にカードが存在しない場合、確定ボタンへ
                    currentCardIndex = -1;
                }
            }
            else
            {
                // 下段から確定ボタンへ
                currentCardIndex = -1;
            }

            OnCursorMoved?.Invoke(currentCardIndex);
        }

        /// <summary>
        /// カーソルを確定ボタンに直接移動
        /// </summary>
        public void MoveToCard(int index)
        {
            if (index < 0 || index >= totalCards) return;
            currentCardIndex = index;
            OnCursorMoved?.Invoke(currentCardIndex);
        }

        public void MoveToConfirmButton()
        {
            currentCardIndex = -1;
            OnCursorMoved?.Invoke(currentCardIndex);
        }
    }
}
