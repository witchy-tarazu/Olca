using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタム画面（カード選択UI）
    /// </summary>
    public class CustomScreenView : MonoBehaviour
    {
        [Header("カード配置")]
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private GameObject handCardPrefab;
        [SerializeField] private Transform[] cardPositionMarkers; // カード位置マーカー（10個）

        [Header("壁マーカー（アニメーション用）")]
        [SerializeField] private RectTransform rightWall;
        [SerializeField] private RectTransform topWall;
        [SerializeField] private RectTransform bottomWall;

        [Header("UI要素")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text costDisplay;
        [SerializeField] private Image selectedCardImage;
        [SerializeField] private Text selectedCardDescription;

        [Header("アニメーション設定")]
        [SerializeField] private float cardShootDuration = 0.35f;  // カード1枚あたりの射出時間
        [SerializeField] private float cardShootInterval = 0.05f; // カード間の射出間隔

        private CustomScreenSystem customScreen;
        private List<HandCardView> handCardViews = new List<HandCardView>();
        private CancellationTokenSource animationCts;

        public void Initialize(CustomScreenSystem customScreen)
        {
            this.customScreen = customScreen;

            // 確定ボタン
            confirmButton.onClick.AddListener(OnConfirmClicked);

            // イベント購読
            customScreen.OnScreenOpened += OnScreenOpened;
            customScreen.OnScreenClosed += OnScreenClosed;
        }

        private void OnScreenOpened(List<Card> hand)
        {
            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            animationCts = new CancellationTokenSource();

            // 既存の手札Viewをクリア
            foreach (var view in handCardViews)
            {
                Destroy(view.gameObject);
            }
            handCardViews.Clear();

            // 新しい手札View生成（アニメーション付き）
            AnimateCardsShootAsync(hand, animationCts.Token).Forget();
        }

        /// <summary>
        /// 手札カードを順次射出するアニメーション
        /// </summary>
        private async UniTask AnimateCardsShootAsync(List<Card> hand, CancellationToken ct)
        {
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var cardObj = Instantiate(handCardPrefab, handCardContainer);
                var cardView = cardObj.GetComponent<HandCardView>();
                cardView.Initialize(card, this);
                handCardViews.Add(cardView);

                // カード射出アニメーション（非同期で並行実行）
                AnimateCardShootAsync(cardView, i, ct).Forget();

                // 次のカードまで少し待つ
                await UniTask.Delay((int)(cardShootInterval * 1000f), cancellationToken: ct);
            }

            // すべてのカードが配置されるまで待つ
            await UniTask.Delay((int)(cardShootDuration * 1000f), cancellationToken: ct);

            // UI更新
            UpdateConfirmButton();
            UpdateCostDisplay();
            UpdateSelectedCardDisplay();
        }

        /// <summary>
        /// カード1枚の射出アニメーション（経路補間方式）
        /// </summary>
        private async UniTask AnimateCardShootAsync(HandCardView cardView, int index, CancellationToken ct)
        {
            var rectTransform = cardView.GetComponent<RectTransform>();
            Vector2 targetPos = GetCardPosition(index);

            // 初期位置（画面外左）
            float startX = -400f;
            float startY = Random.Range(-200f, 200f);
            Vector2 startPos = new Vector2(startX, startY);

            // バウンド回数をランダムに決定（2回 or 3回）
            int bounceCount = Random.Range(2, 4); // 2 or 3
            int row = index / 5;
            int col = index % 5;

            // 経路生成
            Vector2[] pathPoints;
            if (bounceCount == 2)
            {
                var (bounce1, bounce2) = CalculateRandomBouncePoints(targetPos, row, col);
                pathPoints = new Vector2[] { startPos, bounce1, bounce2, targetPos };
            }
            else // bounceCount == 3
            {
                var (bounce1, bounce2, bounce3) = CalculateRandomBouncePoints3(targetPos, row, col);
                pathPoints = new Vector2[] { startPos, bounce1, bounce2, bounce3, targetPos };
            }

            // 回転の初期設定
            float startRotation = Random.Range(-180f, 180f);
            float totalRotations = Random.Range(2f, 4f); // 2〜4回転

            float elapsed = 0f;

            while (elapsed < cardShootDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / cardShootDuration;

                // 経路上の位置を計算（Catmull-Rom補間）
                Vector2 currentPos = GetPointOnPath(pathPoints, t);

                // 回転計算（線形）
                float currentRotation = startRotation + (360f * totalRotations * t);

                // 位置と回転を適用
                rectTransform.anchoredPosition = currentPos;
                rectTransform.rotation = Quaternion.Euler(0f, 0f, currentRotation);

                await UniTask.Yield(ct);
            }

            // 最終位置と回転を保証
            rectTransform.anchoredPosition = targetPos;
            rectTransform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 経路上の位置を取得（距離ベースの線形補間）
        /// </summary>
        private Vector2 GetPointOnPath(Vector2[] points, float t)
        {
            // 各セグメントの距離を計算
            float[] segmentLengths = new float[points.Length - 1];
            float totalLength = 0f;

            for (int i = 0; i < points.Length - 1; i++)
            {
                float length = Vector2.Distance(points[i], points[i + 1]);
                segmentLengths[i] = length;
                totalLength += length;
            }

            // t に基づいて目標距離を計算
            float targetDistance = t * totalLength;
            float accumulatedDistance = 0f;

            // 目標距離が含まれるセグメントを探す
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                if (accumulatedDistance + segmentLengths[i] >= targetDistance)
                {
                    // このセグメント内にある
                    float segmentT = (targetDistance - accumulatedDistance) / segmentLengths[i];
                    return Vector2.Lerp(points[i], points[i + 1], segmentT);
                }
                accumulatedDistance += segmentLengths[i];
            }

            // 最終点を返す（念のため）
            return points[points.Length - 1];
        }

        /// <summary>
        /// ランダムなバウンド位置を計算（2回バウンド）
        /// </summary>
        private (Vector2 bounce1, Vector2 bounce2) CalculateRandomBouncePoints(Vector2 targetPos, int row, int col)
        {
            Rect containerRect = GetContainerRect();

            // 矩形範囲のマージン（壁ギリギリを避ける）
            float marginX = 50f;
            float marginY = 50f;
            float minX = containerRect.xMin + marginX;
            float maxX = containerRect.xMax - marginX;
            float minY = containerRect.yMin + marginY;
            float maxY = containerRect.yMax - marginY;

            // 6パターンの壁組み合わせからランダム選択
            int pattern = Random.Range(0, 6);
            Vector2 bounce1, bounce2;

            switch (pattern)
            {
                case 0: // 右の壁 → 上の壁
                    bounce1 = new Vector2(maxX, Random.Range(minY, maxY));
                    bounce2 = new Vector2(Random.Range(minX, maxX), maxY);
                    break;

                case 1: // 右の壁 → 下の壁
                    bounce1 = new Vector2(maxX, Random.Range(minY, maxY));
                    bounce2 = new Vector2(Random.Range(minX, maxX), minY);
                    break;

                case 2: // 上の壁 → 右の壁
                    bounce1 = new Vector2(Random.Range(minX, maxX), maxY);
                    bounce2 = new Vector2(maxX, Random.Range(minY, maxY));
                    break;

                case 3: // 上の壁 → 下の壁
                    bounce1 = new Vector2(Random.Range(minX, maxX), maxY);
                    bounce2 = new Vector2(Random.Range(minX, maxX), minY);
                    break;

                case 4: // 下の壁 → 右の壁
                    bounce1 = new Vector2(Random.Range(minX, maxX), minY);
                    bounce2 = new Vector2(maxX, Random.Range(minY, maxY));
                    break;

                default: // 下の壁 → 上の壁
                    bounce1 = new Vector2(Random.Range(minX, maxX), minY);
                    bounce2 = new Vector2(Random.Range(minX, maxX), maxY);
                    break;
            }

            return (bounce1, bounce2);
        }

        /// <summary>
        /// ランダムなバウンド位置を計算（3回バウンド）
        /// 一度バウンドした壁には二度と当たらない
        /// </summary>
        private (Vector2 bounce1, Vector2 bounce2, Vector2 bounce3) CalculateRandomBouncePoints3(Vector2 targetPos, int row, int col)
        {
            Rect containerRect = GetContainerRect();

            // 矩形範囲のマージン（壁ギリギリを避ける）
            float marginX = 50f;
            float marginY = 50f;
            float minX = containerRect.xMin + marginX;
            float maxX = containerRect.xMax - marginX;
            float minY = containerRect.yMin + marginY;
            float maxY = containerRect.yMax - marginY;

            // 3つの壁 (0=右, 1=上, 2=下) をランダムにシャッフル
            List<int> walls = new List<int> { 0, 1, 2 };

            // Fisher-Yatesシャッフル
            for (int i = walls.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int temp = walls[i];
                walls[i] = walls[j];
                walls[j] = temp;
            }

            // シャッフルされた順番で3つの壁にバウンド
            Vector2 bounce1 = GetWallPosition(walls[0], minX, maxX, minY, maxY);
            Vector2 bounce2 = GetWallPosition(walls[1], minX, maxX, minY, maxY);
            Vector2 bounce3 = GetWallPosition(walls[2], minX, maxX, minY, maxY);

            return (bounce1, bounce2, bounce3);
        }

        /// <summary>
        /// 指定された壁上のランダムな位置を取得
        /// </summary>
        private Vector2 GetWallPosition(int wall, float minX, float maxX, float minY, float maxY)
        {
            switch (wall)
            {
                case 0: // 右の壁
                    return new Vector2(maxX, Random.Range(minY, maxY));
                case 1: // 上の壁
                    return new Vector2(Random.Range(minX, maxX), maxY);
                default: // 下の壁
                    return new Vector2(Random.Range(minX, maxX), minY);
            }
        }

        /// <summary>
        /// コンテナの矩形範囲を取得
        /// </summary>
        private Rect GetContainerRect()
        {
            if (rightWall != null && topWall != null && bottomWall != null)
            {
                return new Rect(
                    0f,
                    bottomWall.anchoredPosition.y,
                    rightWall.anchoredPosition.x,
                    topWall.anchoredPosition.y - bottomWall.anchoredPosition.y
                );
            }

            // フォールバック
            return new Rect(0f, -270f, 790f, 320f);
        }

        /// <summary>
        /// カード位置マーカーからRectTransform位置を取得
        /// </summary>
        private Vector2 GetCardPosition(int index)
        {
            if (cardPositionMarkers == null || index < 0 || index >= cardPositionMarkers.Length)
            {
                // フォールバック: マーカーが設定されていない場合は手動計算
                Debug.LogWarning($"CardPositionMarker[{index}] が設定されていません。デフォルト位置を使用します。");
                int row = index / 5;
                int col = index % 5;
                return new Vector2(col * 160f, -row * 160f); // 150px + 10px間隔
            }

            var markerRect = cardPositionMarkers[index] as RectTransform;
            return markerRect.anchoredPosition;
        }

        public void OnCardClicked(Card card)
        {
            // カード選択トグル
            if (customScreen.SelectedCards.Any(c => c == card))
            {
                customScreen.TryDeselectCard(card);
            }
            else
            {
                customScreen.TrySelectCard(card);
            }

            // View更新（選択状態のハイライト）
            foreach (var view in handCardViews)
            {
                view.UpdateSelection(customScreen.SelectedCards.Any(c => c == view.Card));
            }

            // 確定ボタンの有効/無効を更新
            UpdateConfirmButton();

            // コスト表示を更新
            UpdateCostDisplay();

            // 選択カード表示エリアを更新
            UpdateSelectedCardDisplay();
        }

        /// <summary>
        /// 確定ボタンの有効/無効を更新
        /// </summary>
        private void UpdateConfirmButton()
        {
            if (confirmButton != null)
            {
                confirmButton.interactable = customScreen.CanConfirm();
            }
        }

        /// <summary>
        /// コスト表示を更新
        /// </summary>
        private void UpdateCostDisplay()
        {
            if (costDisplay != null)
            {
                int totalCost = customScreen.SelectedCards.Sum(c => c.Cost);
                int costLimit = customScreen.MaxCost;
                costDisplay.text = $"コスト: {totalCost} / {costLimit}";

                // コスト超過時は赤色表示
                if (totalCost > costLimit)
                {
                    costDisplay.color = new Color(1f, 0.39f, 0.39f); // R:255, G:100, B:100
                }
                else
                {
                    costDisplay.color = Color.white;
                }
            }
        }

        /// <summary>
        /// 選択カード表示エリアを更新
        /// </summary>
        private void UpdateSelectedCardDisplay()
        {
            if (customScreen.SelectedCards.Count > 0)
            {
                // 最後に選択されたカード（最新）を表示
                var lastSelectedCard = customScreen.SelectedCards[customScreen.SelectedCards.Count - 1];

                if (selectedCardDescription != null)
                {
                    // コスト、威力、説明を表示
                    selectedCardDescription.text =
                        $"コスト: {lastSelectedCard.Cost}\n" +
                        $"威力: {lastSelectedCard.Power}\n\n" +
                        $"{lastSelectedCard.Description}";
                }

                // 画像は現時点では設定なし（Phase2では白背景）
                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
            else
            {
                // 何も選択されていない場合
                if (selectedCardDescription != null)
                {
                    selectedCardDescription.text = "カードを選択してください";
                }

                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
        }

        private void OnConfirmClicked()
        {
            // 選択確定
            customScreen.Confirm();
        }

        private void OnScreenClosed(List<Card> selectedCards)
        {
            // BattleControllerに通知（BattleViewを再表示）
            var battleController = FindFirstObjectByType<Olca.Presentation.Battle.BattleController>();
            if (battleController != null)
            {
                battleController.OnCustomScreenClosed();
            }
        }

        private void OnDestroy()
        {
            // アニメーション用CancellationTokenをキャンセル
            animationCts?.Cancel();
            animationCts?.Dispose();

            if (customScreen != null)
            {
                customScreen.OnScreenOpened -= OnScreenOpened;
                customScreen.OnScreenClosed -= OnScreenClosed;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }
        }
    }
}
