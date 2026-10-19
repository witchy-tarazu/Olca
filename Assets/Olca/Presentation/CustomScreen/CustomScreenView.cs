using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Presentation.CustomScreen.CustomizeMode;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタム画面（カード選択UI）
    /// </summary>
    public class CustomScreenView : MonoBehaviour, ICustomScreenView
    {
        /// <summary>
        /// 確定ボタンが押されてカスタム画面が確定された時に発行されるイベント。
        /// DungeonBattleController など外部コントローラーが購読して画面を閉じる処理を行う。
        /// </summary>
        public event System.Action OnCustomScreenConfirmed;
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

        [Header("カーソル設定")]
        [SerializeField] private GameObject cursorIndicator;  // カーソル表示用のUI要素（枠線等）
        [SerializeField] private CanvasGroup screenCanvasGroup;  // 一時非表示用

        [Header("カスタマイズモード")]
        [SerializeField] private CustomizeModeView customizeModeView; // カスタマイズモードUI（子オブジェクト）
        [SerializeField] private Image customizeModeOverlay;          // モード中に画面全体にかける半透明オーバーレイ

        private CustomScreenSystem customScreen;
        private System.Action<long, System.Action> queueConfirmation;
        private System.Func<long> pointerClickTimestamp;
        public void ConfigureLogicalConfirmation(System.Action<long, System.Action> queue, System.Func<long> pointerTimestamp)
        { queueConfirmation = queue; pointerClickTimestamp = pointerTimestamp; }

        public void HandleLogicalConfirmation(Olca.Core.Logic.Integration.LogicalInput input)
        {
            if (queueConfirmation == null || customScreen == null || !customScreen.IsOpen ||
                Olca.Unity.GameplayPause.InputBlocked || (customizeModeView != null && customizeModeView.IsActive)) return;
            if (input.MenuDecideButtonDown) OnDecidePressed(true);
            if (input.MenuConfirmButtonDown) customScreen.Confirm();
        }
        private List<HandCardView> handCardViews = new List<HandCardView>();
        private CancellationTokenSource animationCts;
        private CustomScreenCursor cursor;
        private bool isScreenVisible = true;
        private Button undoCustomizationButton;
        private bool undoInProgress;
        private int lastUndoFrame = -1;

        // カスタマイズ選択中のカード（統合リスト）
        private readonly List<Card> customizeSelectedCards = new List<Card>();

        public void Initialize(CustomScreenSystem customScreen)
        {
            // 再初期化時は既存の購読を解除
            if (this.customScreen != null)
            {
                this.customScreen.OnScreenOpened -= OnScreenOpened;
                this.customScreen.OnConfirmed -= OnConfirmed;
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
                this.customScreen.OnExceeded -= OnCustomizeOperationCompleted;
                this.customScreen.OnTuned -= OnCustomizeOperationCompleted;
                this.customScreen.OnFused -= OnCustomizeOperationCompleted;
                this.customScreen.OnSelectionFailed -= OnCustomizeSelectionFailed;
                this.customScreen.OnCustomizationUndone -= OnCustomizationUndone;
            }

            if (cursor != null) cursor.OnCursorMoved -= OnCursorMoved;
            this.customScreen = customScreen;
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            if (selectedCardDescription != null)
            {
                selectedCardDescription.resizeTextForBestFit = true;
                selectedCardDescription.resizeTextMinSize = 14;
                selectedCardDescription.resizeTextMaxSize = selectedCardDescription.fontSize;
                selectedCardDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
                selectedCardDescription.verticalOverflow = VerticalWrapMode.Truncate;
            }
            EnsureUndoButton();
            UpdateUndoButton();

            // 確定ボタン
            confirmButton.onClick.AddListener(OnConfirmClicked);

            // イベント購読
            customScreen.OnScreenOpened += OnScreenOpened;
            customScreen.OnConfirmed += OnConfirmed;

            // カーソル初期化
            cursor = new CustomScreenCursor();
            cursor.OnCursorMoved += OnCursorMoved;

            // カーソル表示を初期非表示
            if (cursorIndicator != null)
            {
                cursorIndicator.SetActive(false);
            }

            // カスタマイズ操作イベント購読
            customScreen.OnExceeded += OnCustomizeOperationCompleted;
            customScreen.OnTuned += OnCustomizeOperationCompleted;
            customScreen.OnFused += OnCustomizeOperationCompleted;
            customScreen.OnCustomizationUndone += OnCustomizationUndone;
            customScreen.OnSelectionFailed += OnCustomizeSelectionFailed;

            // CustomizeModeView のイベント購読
            if (customizeModeView != null)
            {
                customizeModeView.OnExecuteRequested -= OnCustomizeExecuteRequested;
                customizeModeView.OnCancelled -= OnCustomizeModeCancelled;
                customizeModeView.OnExecuteRequested += OnCustomizeExecuteRequested;
                customizeModeView.OnCancelled += OnCustomizeModeCancelled;
            }
            if (customScreen.IsOpen && !customScreen.IsConfirming) OnScreenOpened(new List<Card>(customScreen.CurrentHand));
        }

        private void Update()
        {
            UpdateUndoButton();
            // 未初期化またはカスタム画面が開いていない場合は入力を受け付けない
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming) return;

            HandleInput();
        }

        private void EnsureUndoButton()
        {
            if (undoCustomizationButton != null || confirmButton == null) return;
            // 既存の確定ボタンの見た目を使い、選択順表示を避けて確定ボタンの右隣へ置く。
            var obj = Instantiate(confirmButton.gameObject, confirmButton.transform.parent);
            obj.name = "UndoCustomizationButton";
            undoCustomizationButton = obj.GetComponent<Button>();
            undoCustomizationButton.onClick = new Button.ButtonClickedEvent();
            undoCustomizationButton.onClick.AddListener(OnUndoClicked);
            undoCustomizationButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var rect = (RectTransform)obj.transform;
            var confirmRect = (RectTransform)confirmButton.transform;
            rect.sizeDelta = new Vector2(280f, confirmRect.sizeDelta.y);
            rect.anchoredPosition = confirmRect.anchoredPosition + new Vector2(320f, 0f);
            var label = obj.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = "加工を戻す";
                label.fontSize = 36;
                label.resizeTextForBestFit = false;
                var textRect = label.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            }
        }

        private void UpdateUndoButton()
        {
            if (undoCustomizationButton == null) return;
            // 閉鎖位置ではボタン右端がHUD側へ残るため、操作画面の表示中だけ描画する。
            bool visible = customScreen != null && customScreen.IsOpen && !customScreen.IsConfirming;
            undoCustomizationButton.gameObject.SetActive(visible);
            undoCustomizationButton.interactable = visible && customScreen.CanUndoCustomization &&
                !undoInProgress && lastUndoFrame != Time.frameCount;
        }

        private void OnUndoClicked()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            if (customScreen == null || !customScreen.CanUndoCustomization || undoInProgress || lastUndoFrame == Time.frameCount) return;
            undoInProgress = true;
            lastUndoFrame = Time.frameCount;
            try { customScreen.TryUndoCustomization(); }
            finally { undoInProgress = false; UpdateUndoButton(); }
        }

        private void OnCustomizationUndone()
        {
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            RefreshHandCardViews();
            UpdateUndoButton();
        }

        /// <summary>
        /// 入力処理
        /// </summary>
        private void HandleInput()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            // カスタマイズモード中
            if (customizeModeView != null && customizeModeView.IsActive)
            {
                // カーソル移動（方向キー/WASD）
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                    cursor.MoveLeft();
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                    cursor.MoveRight();
                else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                    cursor.MoveUp();
                else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                    cursor.MoveDown();

                // Space でカーソル位置のカードを選択/解除
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    int cardIndex = cursor.CurrentIndex;
                    if (!cursor.IsOnConfirmButton && cardIndex >= 0 && cardIndex < handCardViews.Count)
                        OnCardClicked(handCardViews[cardIndex].Card);
                }

                // Enter/ESCはcustomizeModeViewに委譲
                customizeModeView.HandleKeyInput();
                return;
            }

            // カーソル移動（方向キー/WASD）
            // TODO: コントローラ対応時に左スティック/十字キーの処理を追加
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                cursor.MoveLeft();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                cursor.MoveRight();
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                cursor.MoveUp();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                cursor.MoveDown();
            }

            // 決定（Space）
            // TODO: コントローラ対応時にAボタン（Fire1）の処理を追加
            if (queueConfirmation == null && Input.GetKeyDown(KeyCode.Space))
            {
                OnDecidePressed();
            }

            // ショートカット確定（Enter）
            // TODO: コントローラ対応時にStartボタン（Submit）の処理を追加
            if (queueConfirmation == null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (customScreen.CanConfirm())
                {
                    OnConfirmClicked();
                }
            }

            // 一時非表示（Tab - ホールド）
            // TODO: コントローラ対応時に左トリガー（LT）の処理を追加
            if (Input.GetKey(KeyCode.Tab))
            {
                SetScreenVisibility(false);
            }
            else if (Input.GetKeyUp(KeyCode.Tab))
            {
                SetScreenVisibility(true);
            }

            // カスタマイズモード開始（Cキー）
            // TODO: コントローラ対応時にYボタン（Fire3）の処理を追加
            if (Input.GetKeyDown(KeyCode.C))
            {
                EnterCustomizeMode();
                return;
            }

            // キャンセル（ESC）
            // TODO: コントローラ対応時にBボタン（Cancel）の処理を追加
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                DeselectAllCards();
            }
        }

        /// <summary>
        /// 画面の表示/非表示を切り替え
        /// </summary>
        private void SetScreenVisibility(bool visible)
        {
            if (isScreenVisible == visible) return;

            isScreenVisible = visible;

            if (screenCanvasGroup != null)
            {
                screenCanvasGroup.alpha = visible ? 1f : 0.3f;  // 30%の透明度で背景を確認可能
            }
        }

        /// <summary>
        /// 決定ボタンが押された
        /// </summary>
        private void OnDecidePressed(bool logical = false)
        {
            if (cursor.IsOnConfirmButton)
            {
                // 確定ボタンで決定 → 選択確定
                if (customScreen.CanConfirm())
                {
                    if (logical) customScreen.Confirm(); else OnConfirmClicked();
                }
            }
            else
            {
                // カードで決定 → 選択/解除トグル
                int cardIndex = cursor.CurrentIndex;
                if (cardIndex >= 0 && cardIndex < handCardViews.Count)
                {
                    var cardView = handCardViews[cardIndex];
                    OnCardClicked(cardView.Card);
                }
            }
        }

        /// <summary>
        /// すべてのカードの選択を解除
        /// </summary>
        private void DeselectAllCards()
        {
            foreach (var card in customScreen.SelectedCards.ToList())
            {
                customScreen.TryDeselectCard(card);
            }

            // View更新
            foreach (var view in handCardViews)
            {
                view.UpdateSelection(false);
            }

            UpdateConfirmButton();
            UpdateCostDisplay();
            UpdateSelectedCardDisplay();
        }

        /// <summary>
        /// カーソル位置が変更された
        /// </summary>
        public void OnCardPointerEntered(HandCardView view)
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming) return;
            int index = handCardViews.IndexOf(view);
            if (index >= 0) cursor.MoveToCard(index);
        }

        public void OnCardPointerExited(HandCardView view)
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            if (cursor != null && handCardViews.IndexOf(view) == cursor.CurrentIndex)
                cursor.MoveToConfirmButton();
        }

        private void OnCursorMoved(int index)
        {
            UpdateCursorIndicator(index);
            UpdateCursorCardDisplay(index);
        }

        /// <summary>
        /// アニメーション完了後にカーソルを表示
        /// </summary>
        private void ShowCursorAfterAnimation()
        {
            if (handCardViews.Count > 0)
            {
                // カードが存在する場合は最初のカード（インデックス0）に移動
                cursor.Initialize(handCardViews.Count);
                UpdateCursorIndicator(0);
            }
            else
            {
                // カードが存在しない場合は確定ボタンに移動
                cursor.Initialize(0);
                cursor.MoveToConfirmButton();
                UpdateCursorIndicator(-1);
            }
        }

        /// <summary>
        /// カーソル表示を更新
        /// </summary>
        private void UpdateCursorIndicator(int index)
        {
            if (cursorIndicator == null) return;

            cursorIndicator.SetActive(true);

            RectTransform cursorRect = cursorIndicator.GetComponent<RectTransform>();

            if (index == -1)
            {
                // 確定ボタンにカーソル
                if (confirmButton != null)
                {
                    RectTransform buttonRect = confirmButton.GetComponent<RectTransform>();
                    cursorRect.position = buttonRect.position;
                    cursorRect.sizeDelta = buttonRect.sizeDelta;
                }
            }
            else if (index >= 0 && index < handCardViews.Count)
            {
                // カードにカーソル
                RectTransform cardRect = handCardViews[index].GetComponent<RectTransform>();
                cursorRect.position = cardRect.position;
                cursorRect.sizeDelta = cardRect.sizeDelta;
            }
        }

        private void OnScreenOpened(List<Card> hand)
        {
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            if (selectedCardDescription != null) selectedCardDescription.text = "カードを選択してください";
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

            // カーソルは非表示（アニメーション完了後に表示）
            if (cursorIndicator != null)
            {
                cursorIndicator.SetActive(false);
            }

            // カスタマイズ選択状態のリセット
            customizeSelectedCards.Clear();

            // 画面表示状態をリセット
            isScreenVisible = true;
            if (screenCanvasGroup != null)
            {
                screenCanvasGroup.alpha = 1f;
            }
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
                await Olca.Unity.GameplayPause.Delay((int)(cardShootInterval * 1000f), cancellationToken: ct);
            }

            // すべてのカードが配置されるまで待つ
            await Olca.Unity.GameplayPause.Delay((int)(cardShootDuration * 1000f), cancellationToken: ct);

            // UI更新
            UpdateConfirmButton();
            UpdateCostDisplay();
            UpdateSelectedCardDisplay();

            // アニメーション完了後、カーソルを表示
            ShowCursorAfterAnimation();
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
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / cardShootDuration;

                // 経路上の位置を計算（Catmull-Rom補間）
                Vector2 currentPos = GetPointOnPath(pathPoints, t);

                // 回転計算（線形）
                float currentRotation = startRotation + (360f * totalRotations * t);

                // 位置と回転を適用
                rectTransform.anchoredPosition = currentPos;
                rectTransform.rotation = Quaternion.Euler(0f, 0f, currentRotation);

                await Olca.Unity.GameplayPause.NextFrame(ct);
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
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            // カスタマイズモード中はカスタマイズ選択へ転送
            if (customizeModeView != null && customizeModeView.IsActive)
            {
                OnCardClickedInCustomizeMode(card);
                return;
            }

            // 通常モード: カード選択トグル
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
        /// カスタマイズモードに入る
        /// </summary>
        private void EnterCustomizeMode()
        {
            if (customizeModeView == null) return;
            customizeSelectedCards.Clear();
            customizeModeView.Enter();
            if (customizeModeOverlay != null)
                customizeModeOverlay.gameObject.SetActive(true);
            UpdateCursorCardDisplay(cursor?.CurrentIndex ?? -1);
        }

        /// <summary>
        /// カスタマイズモード中のカード選択処理。
        /// カードをトグルし、組み合わせから操作種別を自動判定してUIを更新する。
        /// 1枚選択済みの場合は互換性のないカードの選択をブロックする。
        /// </summary>
        private void OnCardClickedInCustomizeMode(Card card)
        {
            if (customizeSelectedCards.Contains(card))
            {
                // 選択解除は常に許可
                customizeSelectedCards.Remove(card);
                customizeModeView.ClearError();
            }
            else
            {
                // 使用予定カードは、通常選択を解除して手札へ戻すまで加工素材にできない。
                if (!customScreen.CurrentHand.Contains(card))
                {
                    customizeModeView.ShowError("使用予定の選択を解除してから素材にしてください");
                    return;
                }

                // 1枚以上選択済みの場合は互換性チェック
                if (customizeSelectedCards.Count > 0 &&
                    !customScreen.IsCompatibleWithCurrentSelection(customizeSelectedCards, card))
                {
                    var invalid = customScreen.PreviewCustomization(customizeSelectedCards.Concat(new[] { card }).ToArray());
                    customizeModeView.ShowError(invalid.Reason ?? "このカードは現在の選択と組み合わせできません");
                    UpdateCursorCardDisplay(handCardViews.FindIndex(v => v.Card == card));
                    return;
                }
                customizeSelectedCards.Add(card);
                customizeModeView.ClearError();
            }

            // 操作種別を判定してUIに反映
            var opType = DetectCustomizeOperation();
            bool canExecute = customScreen.PreviewCustomization(customizeSelectedCards).CanExecute;
            customizeModeView.UpdateExecuteButton(canExecute);
            customizeModeView.UpdateOperationHint(GetOperationHint(opType));

            RefreshCustomizeHighlights(opType);
            UpdateCursorCardDisplay(cursor?.CurrentIndex ?? -1);
        }

        /// <summary>
        /// 選択中のカードから操作種別を自動判定する
        /// </summary>
        private CustomizationOperation DetectCustomizeOperation()
            => customScreen.PreviewCustomization(customizeSelectedCards).Operation;

        /// <summary>操作種別に応じたガイドテキストを返す</summary>
        private string GetOperationHint(CustomizationOperation opType)
        {
            switch (opType)
            {
                case CustomizationOperation.Exceed:
                    return $"超越: {customizeSelectedCards[0].Name} × 2 → Enterで実行";
                case CustomizationOperation.Tuning:
                    return $"調律: Enterで実行";
                case CustomizationOperation.Fusion:
                    return $"融合: {customizeSelectedCards.Count}枚選択中 → Enterで実行";
                default:
                    return "カスタマイズ: カードを選んでください（ESC でキャンセル）";
            }
        }

        /// <summary>
        /// 実行ボタン押下: 自動判定した操作種別でカスタマイズを実行する
        /// </summary>
        private void OnCustomizeExecuteRequested()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            var result = customScreen.ExecuteCustomization(customizeSelectedCards);
            if (!result.CanExecute)
            {
                UpdateCursorCardDisplay(cursor?.CurrentIndex ?? -1);
                customizeModeView.ShowError(result.Reason);
            }
        }

        /// <summary>
        /// カスタマイズ操作成功時（超越・調律・融合いずれか）
        /// </summary>
        private void OnCustomizeOperationCompleted(Card resultCard)
        {
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null)
                customizeModeOverlay.gameObject.SetActive(false);
            RefreshHandCardViews();
            UpdateUndoButton();
        }

        /// <summary>
        /// カスタマイズ選択失敗時（CustomScreenSystem.OnSelectionFailed）
        /// </summary>
        private void OnCustomizeSelectionFailed(string message)
        {
            if (customizeModeView != null && customizeModeView.IsActive)
                customizeModeView.ShowError(message);
        }

        /// <summary>
        /// カスタマイズモードキャンセル
        /// </summary>
        private void OnCustomizeModeCancelled()
        {
            customizeSelectedCards.Clear();
            customizeModeView.Exit();
            if (customizeModeOverlay != null)
                customizeModeOverlay.gameObject.SetActive(false);
            RefreshAllHighlights();
            UpdateCursorCardDisplay(cursor?.CurrentIndex ?? -1);
        }

        /// <summary>
        /// 手札Viewを即時再描画する（カスタマイズ操作後に手札が変化した際に使用）
        /// </summary>
        private void RefreshHandCardViews()
        {
            animationCts?.Cancel();
            foreach (var view in handCardViews)
                Destroy(view.gameObject);
            handCardViews.Clear();

            var hand = customScreen.CurrentHand.Concat(customScreen.SelectedCards).ToList();
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var cardObj = Instantiate(handCardPrefab, handCardContainer);
                var cardView = cardObj.GetComponent<HandCardView>();
                cardView.Initialize(card, this);
                handCardViews.Add(cardView);

                var rectTransform = cardView.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = GetCardPosition(i);
                rectTransform.rotation = Quaternion.identity;
            }

            cursor.Initialize(handCardViews.Count);
            RefreshAllHighlights();
            UpdateConfirmButton();
            UpdateCostDisplay();
        }

        /// <summary>
        /// 操作種別に応じたカードハイライトを全View に適用する。
        /// 1枚以上選択済みの場合、互換性のないカードは暗転表示する。
        /// </summary>
        private void RefreshCustomizeHighlights(CustomizationOperation opType)
        {
            bool hasSelection = customizeSelectedCards.Count > 0;

            foreach (var view in handCardViews)
            {
                var card = view.Card;
                bool isSelected = customizeSelectedCards.Contains(card);

                CardHighlightType highlightType;
                if (isSelected)
                {
                    switch (opType)
                    {
                        case CustomizationOperation.Exceed:
                            highlightType = CardHighlightType.ExceedMaterial;
                            break;
                        case CustomizationOperation.Tuning:
                            highlightType = card.Category == CardCategory.Tuning
                                ? CardHighlightType.TuningMaterial
                                : CardHighlightType.TuningTarget;
                            break;
                        default: // Fusion または None（選択中だが未確定）
                            highlightType = CardHighlightType.FusionMaterial;
                            break;
                    }
                }
                else if (hasSelection && !customScreen.IsCompatibleWithCurrentSelection(customizeSelectedCards, card))
                {
                    // 現在の選択と組み合わせ不可 → 暗転
                    highlightType = CardHighlightType.Incompatible;
                }
                else
                {
                    highlightType = CardHighlightType.None;
                }

                view.UpdateHighlight(highlightType);
            }
        }

        /// <summary>
        /// 通常モードのハイライト（デッキ選択状態）に戻す
        /// </summary>
        private void RefreshAllHighlights()
        {
            foreach (var view in handCardViews)
            {
                bool isSelected = customScreen.SelectedCards.Any(c => c == view.Card);
                view.UpdateHighlight(isSelected ? CardHighlightType.Selected : CardHighlightType.None);
            }
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
                int totalCost = customScreen.GetTotalUsedCost();
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
        /// 選択カード表示エリアを更新（旧実装 - 非推奨）
        /// 現在はUpdateCursorCardDisplay()を使用
        /// </summary>
        private void UpdateSelectedCardDisplay()
        {
            // カーソル位置のカード表示に統一するため、このメソッドは使用しない
            // 互換性のため残しているが、何もしない
        }

        /// <summary>
        /// カーソル位置のカード情報を表示エリアに表示
        /// </summary>
        private void UpdateCursorCardDisplay(int cursorIndex)
        {
            if (customizeModeView != null && customizeModeView.IsActive)
            {
                UpdateCustomizationPreview(cursorIndex);
                return;
            }
            if (cursorIndex >= 0 && cursorIndex < handCardViews.Count)
            {
                // カーソルが置かれているカードの情報を表示
                var cursorCard = handCardViews[cursorIndex].Card;

                if (selectedCardDescription != null)
                {
                    // コスト、威力、説明を表示
                    selectedCardDescription.text =
                        $"コスト: {cursorCard.GetFinalCost()}\n" +
                        $"威力: {cursorCard.GetFinalPower()}\n\n" +
                        $"{cursorCard.Description}";
                }

                // 画像は現時点では設定なし（将来的にカードイラストを表示）
                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
            else if (cursorIndex == -1)
            {
                // 確定ボタンにカーソルがある場合
                if (selectedCardDescription != null)
                {
                    if (customScreen.SelectedCards.Count > 0)
                    {
                        selectedCardDescription.text = "選択したカードを確定します";
                    }
                    else
                    {
                        selectedCardDescription.text = "カードを選択してください";
                    }
                }

                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
            else
            {
                // カードが存在しない場合
                if (selectedCardDescription != null)
                {
                    selectedCardDescription.text = "カードがありません";
                }

                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
        }

        private void UpdateCustomizationPreview(int cursorIndex)
        {
            var materials = new List<Card>(customizeSelectedCards);
            if (materials.Count == 1 && cursorIndex >= 0 && cursorIndex < handCardViews.Count)
            {
                var candidate = handCardViews[cursorIndex].Card;
                if (!materials.Contains(candidate)) materials.Add(candidate);
            }
            customizeModeView.ClearError();
            customizeModeView.UpdateExecuteButton(customScreen.PreviewCustomization(customizeSelectedCards).CanExecute);
            if (materials.Count < 2)
            {
                string guide = materials.Count == 0 ? "最初の素材を選んでください" : "2枚目にカーソルを合わせてください";
                customizeModeView.UpdateOperationHint(guide);
                if (selectedCardDescription != null) selectedCardDescription.text = guide;
                return;
            }
            var preview = customScreen.PreviewCustomization(materials);
            string operation = preview.Operation == CustomizationOperation.Exceed ? "超越" :
                preview.Operation == CustomizationOperation.Tuning ? "調律" : "融合";
            if (!preview.CanExecute)
            {
                string status = preview.Status == CustomizationPreviewStatus.Undefined ? "未定義" : "実行不可";
                customizeModeView.UpdateOperationHint($"{operation}: {status}");
                if (selectedCardDescription != null)
                    selectedCardDescription.text = $"{operation} / {status}\n{preview.Reason}";
                return;
            }
            var result = preview.ResultCard;
            string effects = string.Join("\n", result.Modifier.AdditionalEffects.Where(e => e != null)
                .Select(e => $"{e.EffectType}: 威力{e.Power} / {e.Duration:0.##}秒"));
            customizeModeView.UpdateOperationHint(customizeSelectedCards.Count >= 2 ?
                $"{operation}: Enterで実行" : $"{operation}: 2枚目を選択して確定");
            if (selectedCardDescription != null)
                selectedCardDescription.text = $"{operation} 結果プレビュー\n{result.Name}\n" +
                    $"コスト: {result.GetFinalCost()} / 威力: {result.GetFinalPower()}\n" +
                    $"{result.Description}" + (effects.Length > 0 ? "\n" + effects : "");
        }

        private void OnConfirmClicked()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            if (queueConfirmation == null) { customScreen.Confirm(); return; }
            var requestedScreen = customScreen;
            queueConfirmation(pointerClickTimestamp(), () => {
                if (this == null || !ReferenceEquals(customScreen, requestedScreen) || Olca.Unity.GameplayPause.InputBlocked) return;
                requestedScreen.Confirm();
            });
        }

        private void OnConfirmed(List<Card> selectedCards)
        {
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            if (selectedCardDescription != null) selectedCardDescription.text = "選択したカードを確定しました";
            UpdateUndoButton();
            // 購読者（BattleController・DungeonBattleControllerなど）に通知
            OnCustomScreenConfirmed?.Invoke();
        }

        private void OnDestroy()
        {
            if (undoCustomizationButton != null) undoCustomizationButton.onClick.RemoveListener(OnUndoClicked);
            // アニメーション用CancellationTokenをキャンセル
            animationCts?.Cancel();
            animationCts?.Dispose();

            if (customScreen != null)
            {
                customScreen.OnScreenOpened -= OnScreenOpened;
                customScreen.OnConfirmed -= OnConfirmed;
                customScreen.OnExceeded -= OnCustomizeOperationCompleted;
                customScreen.OnTuned -= OnCustomizeOperationCompleted;
                customScreen.OnFused -= OnCustomizeOperationCompleted;
                customScreen.OnCustomizationUndone -= OnCustomizationUndone;
                customScreen.OnSelectionFailed -= OnCustomizeSelectionFailed;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }

            if (customizeModeView != null)
            {
                customizeModeView.OnExecuteRequested -= OnCustomizeExecuteRequested;
                customizeModeView.OnCancelled -= OnCustomizeModeCancelled;
            }
        }
    }
}
