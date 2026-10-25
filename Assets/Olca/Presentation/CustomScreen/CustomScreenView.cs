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
        [SerializeField] private CardVisualEntry[] cardVisuals=System.Array.Empty<CardVisualEntry>();
        public Sprite GetCardIllustration(string id)=>cardVisuals?.FirstOrDefault(v=>v!=null&&v.CardId==id)?.Illustration;
        private readonly Dictionary<string, Sprite> generatedMiniIcons = new Dictionary<string, Sprite>();
        public Sprite GetCardIcon(string id)
        {
            var assigned=cardVisuals?.FirstOrDefault(v=>v!=null&&v.CardId==id)?.CustomIcon;
            if(assigned!=null||(id!="sword"&&id!="fire_sword"&&id!="heal"&&id!="shield"&&id!="bomb"&&id!="tuning"))return assigned;
            if(!generatedMiniIcons.TryGetValue(id,out var icon)||icon==null){
                icon=(id=="sword"||id=="fire_sword")?SwordMiniIcon.Create(id=="fire_sword"):SupportMiniIcon.Create(id);
                generatedMiniIcons[id]=icon;
            }
            return icon;
        }
        public void ConfigureCardVisuals(IEnumerable<CardVisualEntry> entries)
        {
            var values=(entries??System.Array.Empty<CardVisualEntry>()).ToArray();var seen=new HashSet<string>();
            foreach(var value in values){
                if(value==null||string.IsNullOrWhiteSpace(value.CardId)||!seen.Add(value.CardId))throw new System.ArgumentException("カード素材IDが不正または重複しています");
                if(value.Illustration!=null&&value.Illustration==value.CustomIcon)throw new System.ArgumentException("本絵と専用miniは別Spriteが必要です");
            }
            cardVisuals=values.Select(v=>v.Copy()).ToArray();
            foreach(var view in handCardViews)if(view!=null)view.RefreshArtwork();
            if(customScreen!=null&&cursor!=null)UpdateCursorCardDisplay(cursor.CurrentIndex);
            UpdateSelectedCardDisplay();
        }

        [SerializeField] private TransformationVisualEntry[] transformationVisuals=System.Array.Empty<TransformationVisualEntry>();
        public string GetTransformationName(string id)
            => transformationVisuals?.FirstOrDefault(v=>v!=null&&v.Id==id)?.DisplayNameKana ?? CustomDisplay.Katakana(id);
        public Sprite GetTransformationIcon(string id)
            => transformationVisuals?.FirstOrDefault(v=>v!=null&&v.Id==id)?.Icon;
        public void ConfigureTransformationVisuals(IEnumerable<TransformationVisualEntry> entries)
        {
            var values=(entries??System.Array.Empty<TransformationVisualEntry>()).ToArray();
            var ids=new HashSet<string>();
            foreach(var value in values){
                if(value==null||string.IsNullOrWhiteSpace(value.Id)||!ids.Add(value.Id)||string.IsNullOrWhiteSpace(value.DisplayNameKana)||
                    System.Text.RegularExpressions.Regex.IsMatch(value.DisplayNameKana,@"[一-鿿々ぁ-ゖ]"))
                    throw new System.ArgumentException("変身のIDとカナ名称を重複なく指定してください");
            }
            transformationVisuals=values.Select(v=>v.Copy()).ToArray();RefreshTransformationButtons();
        }
        private string TransformationStatusText()
        {
            if(transformations==null)return "";
            var value=transformations.HasActive?GetTransformationName(transformations.ActiveId)+" ノコリ "+transformations.RemainingPhases+" カイ":"ヘンシンナシ（ノコリ0カイ）";
            return transformations.PendingId==null?value:value+"\nカリセンタク "+GetTransformationName(transformations.PendingId)+"（カクテイゴ3カイ）";
        }

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
        private TransformationSelectionState transformations;
        private Text transformationStatus;
        private readonly List<Button> transformationButtons = new List<Button>();
        private void RefreshTransformationButtons()
        {
            int count = transformations?.AvailableDefinitions.Count ?? 0;
            if(transformationStatus==null&&confirmButton!=null){
                var obj=new GameObject("CustomTransformationStatus",typeof(RectTransform),typeof(Text));obj.transform.SetParent(confirmButton.transform.parent,false);
                transformationStatus=obj.GetComponent<Text>();transformationStatus.font=confirmButton.GetComponentInChildren<Text>()?.font;
                transformationStatus.fontSize=24;transformationStatus.alignment=TextAnchor.MiddleLeft;Olca.Presentation.Dungeon.UiLook.Label(transformationStatus);
                var rect=transformationStatus.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.anchoredPosition=new Vector2(350,-80);rect.sizeDelta=new Vector2(650,80);
            }
            if(transformationStatus!=null){
                transformationStatus.gameObject.SetActive(transformations!=null&&customScreen!=null&&customScreen.IsOpen&&(count>0||transformations.HasActive));
                transformationStatus.text=TransformationStatusText();
            }
            while (transformationButtons.Count > count) {
                var last = transformationButtons[transformationButtons.Count - 1];
                transformationButtons.RemoveAt(transformationButtons.Count - 1); Destroy(last.gameObject);
            }
            while (transformationButtons.Count < count && confirmButton != null) {
                int index = transformationButtons.Count;
                var obj = Instantiate(confirmButton.gameObject, confirmButton.transform.parent);
                obj.name = "TransformationChoice" + index;
                var button = obj.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent();
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => {
                    if (!CanOpenTouchDetails()) return;
                    var state = transformations; var id = state.AvailableDefinitions[index].Id;
                    System.Action select = () => {
                        if (this != null && ReferenceEquals(state, transformations) && !Olca.Unity.GameplayPause.InputBlocked) customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.PreviewTransformation, id));
                    };
                    if (queueConfirmation == null) select(); else queueConfirmation(pointerClickTimestamp(), select);
                });
                var rect = (RectTransform)obj.transform;
                rect.sizeDelta = new Vector2(230f, 64f);
                rect.anchoredPosition = ((RectTransform)confirmButton.transform).anchoredPosition + new Vector2(110f + (index % 2) * 250f, 550f + (index / 2) * 76f);
                var label = obj.GetComponentInChildren<Text>();
                if (label != null) {
                    label.fontSize=24;label.resizeTextForBestFit=true;label.resizeTextMinSize=16;label.resizeTextMaxSize=24;
                    label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;
                    label.rectTransform.offsetMin=new Vector2(8,4);label.rectTransform.offsetMax=new Vector2(-8,-4);
                    label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;
                }
                ConfigureTouchDetails(button, () => ShowTouchTransformationDetails(index));
                transformationButtons.Add(button);
            }
            for (int i=0;i<transformationButtons.Count;i++) {
                var button=transformationButtons[i];var definition=transformations.AvailableDefinitions[i];
                bool visible=customScreen != null && customScreen.IsOpen && !customScreen.IsConfirming;
                button.gameObject.SetActive(visible);
                button.interactable=visible && !handAnimating && !transformations.IsUsed(definition.Id) && !Olca.Unity.GameplayPause.InputBlocked;
                var label=button.GetComponentInChildren<Text>();
                if(label!=null)label.text=(padTransformationIndex==i?"▶ ":"")+GetTransformationName(definition.Id)+"\n"+(transformations.IsUsed(definition.Id)?"使用済み":transformations.PendingId==definition.Id?"選択中":$"手札+{definition.HandSizeBonus}");
                var iconTransform=button.transform.Find("TransformationIcon");
                var sprite=GetTransformationIcon(definition.Id);
                if(iconTransform==null&&sprite!=null){
                    var obj=new GameObject("TransformationIcon",typeof(RectTransform),typeof(Image));obj.transform.SetParent(button.transform,false);iconTransform=obj.transform;
                    var rect=(RectTransform)iconTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(32,0);rect.sizeDelta=new Vector2(48,48);
                }
                if(iconTransform!=null){var image=iconTransform.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;iconTransform.gameObject.SetActive(sprite!=null);}
                if(label!=null)label.rectTransform.offsetMin=new Vector2(sprite!=null?62:8,4);

            }
        }
        private void OnHandChanged()
        {
            ResetCustomPad(false);
            if(customScreen==null || !customScreen.IsOpen || customScreen.IsConfirming)return;
            customizeSelectedCards.Clear();
            if(customizeModeView!=null)customizeModeView.Exit();
            if(customizeModeOverlay!=null)customizeModeOverlay.gameObject.SetActive(false);
            RefreshHandCardViews(); RefreshSelection();
        }
        public void BindTransformations(TransformationSelectionState transformations)
        {
            ResetCustomPad();
            if (this.transformations != null) this.transformations.OnPreviewChanged -= RefreshSelection;
            this.transformations = transformations;
            if (transformations != null) transformations.OnPreviewChanged += RefreshSelection;
            RefreshTransformationButtons();
        }

        private void RefreshSelection()
        {
            if (customScreen == null) return;
            foreach (var view in handCardViews)
                view.UpdateSelection(customScreen.SelectedCards.Contains(view.Card));
            UpdateConfirmButton();
            UpdateCostDisplay();
            UpdateSelectedCardDisplay();
            RefreshTransformationButtons();
        }
        private System.Action<long, System.Action> queueConfirmation;
        private System.Func<long> pointerClickTimestamp;
        public void ConfigureLogicalConfirmation(System.Action<long, System.Action> queue, System.Func<long> pointerTimestamp)
        { queueConfirmation = queue; pointerClickTimestamp = pointerTimestamp; }

        public void HandleLogicalConfirmation(Olca.Core.Logic.Integration.LogicalInput input)
        {
            if (lastModeInputFrame==Time.frameCount||lastDetailsCloseFrame==Time.frameCount||lastPadFrame==Time.frameCount||(detailsPanel!=null&&detailsPanel.activeSelf)||queueConfirmation == null || customScreen == null || !customScreen.IsOpen ||
                Olca.Unity.GameplayPause.InputBlocked || (customizeModeView != null && customizeModeView.IsActive)) return;
            if (input.MenuDecideButtonDown) OnDecidePressed(true);
            if (input.MenuConfirmButtonDown) customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm));
            if (input.MenuCancelButtonDown) CancelSelection();
        }
        private List<HandCardView> handCardViews = new List<HandCardView>();
        private List<Card> inputHand = new List<Card>();
        private bool handAnimating;
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
            StopHandPresentation();
            Olca.Unity.BattleVisualClock.SetPaused(this,false);
            foreach(var text in GetComponentsInChildren<Text>(true))text.color=Color.white;
            Olca.Presentation.Dungeon.UiLook.Button(confirmButton);
            if(confirmButton!=null){var label=confirmButton.GetComponentInChildren<Text>();if(label!=null)label.text="確定";}
            if(cursorIndicator!=null){var image=cursorIndicator.GetComponent<Image>();if(image!=null){image.sprite=Resources.Load<Sprite>("OlcaUI/button-selected");image.type=Image.Type.Sliced;image.fillCenter=false;image.color=Color.white;}}
            Olca.Presentation.Dungeon.UiLook.Label(costDisplay);
            Olca.Presentation.Dungeon.UiLook.Label(selectedCardDescription);
            if(costDisplay!=null){costDisplay.fontSize=30;costDisplay.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,56);}
            if(selectedCardDescription!=null)selectedCardDescription.fontSize=28;
            // BgCustomはSceneに保存された元のsprite/colorを保持する。背景を共通部品で上書きしない。
            if(costDisplay!=null)costDisplay.color=Color.white;
            if(selectedCardDescription!=null)selectedCardDescription.color=Color.white;

            // 再初期化時は既存の購読を解除
            if (this.customScreen != null)
            {
                this.customScreen.OnScreenOpened -= OnScreenOpened;
                this.customScreen.OnScreenClosed -= OnBattleScreenClosed;
                this.customScreen.OnHandChanged -= OnHandChanged;
                this.customScreen.OnConfirmed -= OnConfirmed;
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
                this.customScreen.OnExceeded -= OnCustomizeOperationCompleted;
                this.customScreen.OnTuned -= OnCustomizeOperationCompleted;
                this.customScreen.OnFused -= OnCustomizeOperationCompleted;
                this.customScreen.OnSelectionFailed -= OnCustomizeSelectionFailed;
                this.customScreen.OnCustomizationUndone -= OnCustomizationUndone;
            }

            if (cursor != null) cursor.OnCursorMoved -= OnCursorMoved;
            gamepadInput?.Dispose();gamepadInput=new Olca.Unity.CustomGamepadInput(()=>IsCustomPadUiActive()&&!handAnimating,IsCustomPadUiActive);
            ResetCustomPad();
            this.customScreen = customScreen;
            if(isActiveAndEnabled&&customScreen.IsOpen)Olca.Unity.BattleVisualClock.SetPaused(this,true);
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
            customScreen.OnScreenClosed += OnBattleScreenClosed;
            customScreen.OnHandChanged += OnHandChanged;
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
            if(customScreen!=null&&customScreen.IsOpen)Olca.Unity.BattleVisualClock.Refresh();
            bool padHandled=HandleCustomGamepad();
            UpdateUndoButton();
            RefreshTransformationButtons();
            // 未初期化またはカスタム画面が開いていない場合は入力を受け付けない
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming) return;

            if(!padHandled)HandleInput();
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
                label.fontSize = 28;
                label.resizeTextForBestFit = false;
                var textRect = label.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            }
        }

        private void UpdateUndoButton()
        {
            Olca.Presentation.Dungeon.UiLook.Button(undoCustomizationButton);
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
            try { customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Undo)); }
            finally { undoInProgress = false; UpdateUndoButton(); }
        }

        private void OnCustomizationUndone()
        {
            ResetCustomPad();
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            RefreshHandCardViews();
            UpdateUndoButton();
        }

        /// <summary>
        /// 入力処理
        /// </summary>
        private bool IsCustomPadUiActive()=>this!=null&&gameObject.activeInHierarchy&&customScreen!=null&&customScreen.IsOpen&&!customScreen.IsConfirming&&!Olca.Unity.GameplayPause.InputBlocked;
        private Olca.Unity.CustomGamepadInput gamepadInput;
        private int padTransformationIndex=-1,lastPadFrame=-1,padViewVersion,lastModeInputFrame=-1;
        private int lastDetailsCloseFrame=-1;
        private GameObject detailsPanel,padGuidePanel;private Text padDetailsText,padGuide;private ScrollRect padDetailsScroll;
        private void ResetCustomPad(bool resetFocus=true){foreach(var hold in touchDetailHolds)hold.CancelPress();padViewVersion++;gamepadInput?.Reset();if(resetFocus)padTransformationIndex=-1;ClosePadDetails();SetScreenVisibility(true);}
        private void OnEnable()
        {
            Canvas.preWillRenderCanvases+=RefreshKana;
            if(customScreen!=null&&customScreen.IsOpen){
                Olca.Unity.BattleVisualClock.SetPaused(this,true);
                // 無効化前のタスクを再利用せず、現在の手札から表示を作り直す。
                if(cursor!=null&&handCardPrefab!=null)RefreshHandCardViews();
            }
        }
        private void RefreshKana(){foreach(var label in GetComponentsInChildren<Text>(true))label.text=CustomDisplay.Katakana(label.text);}
        private void OnDisable(){StopHandPresentation();Canvas.preWillRenderCanvases-=RefreshKana;ResetCustomPad();Olca.Unity.BattleVisualClock.SetPaused(this,false);}
        private bool HandleCustomGamepad()
        {
            if(gamepadInput==null)return false;var commands=gamepadInput.Read();
            if(customScreen!=null&&customScreen.IsOpen&&!customScreen.IsConfirming)EnsurePadGuide();else if(padGuidePanel!=null)padGuidePanel.SetActive(false);
            foreach(var input in commands){lastPadFrame=Time.frameCount;var owner=customScreen;int view=padViewVersion;System.Action execute=()=>{
                if(this==null||gamepadInput==null||input.Epoch!=gamepadInput.Epoch||view!=padViewVersion||!ReferenceEquals(owner,customScreen)||!owner.IsOpen||owner.IsConfirming||handAnimating||Olca.Unity.GameplayPause.InputBlocked)return;
                ApplyCustomPad(input.Command);
            };if(queueConfirmation==null)execute();else queueConfirmation(input.Timestamp,execute);}
            return commands.Length>0;
        }
        private void ApplyCustomPad(Olca.Unity.CustomPadCommand command)
        {
            bool mode=customizeModeView!=null&&customizeModeView.IsActive;
            if(detailsPanel!=null&&detailsPanel.activeSelf){
                if(command==Olca.Unity.CustomPadCommand.Cancel||command==Olca.Unity.CustomPadCommand.Details)ClosePadDetails();
                else if(command==Olca.Unity.CustomPadCommand.Up)padDetailsScroll.verticalNormalizedPosition=Mathf.Clamp01(padDetailsScroll.verticalNormalizedPosition+.2f);
                else if(command==Olca.Unity.CustomPadCommand.Down)padDetailsScroll.verticalNormalizedPosition=Mathf.Clamp01(padDetailsScroll.verticalNormalizedPosition-.2f);
                return;
            }
            if(command==Olca.Unity.CustomPadCommand.Details){ShowPadDetails();return;}
            if(command==Olca.Unity.CustomPadCommand.Transform){if(!mode&&transformations?.AvailableDefinitions.Count>0){padTransformationIndex=padTransformationIndex<0?0:-1;RefreshTransformationButtons();}return;}
            if(command==Olca.Unity.CustomPadCommand.Cancel){if(mode)OnCustomizeModeCancelled();else if(padTransformationIndex>=0){customScreen?.Commands.Execute(new CustomScreenCommand(CustomCommandKind.CancelTransformation));RefreshTransformationButtons();}else CancelSelection();return;}
            if(command==Olca.Unity.CustomPadCommand.Customize){if(mode)OnCustomizeModeCancelled();else{padTransformationIndex=-1;EnterCustomizeMode();}return;}
            if(command==Olca.Unity.CustomPadCommand.Undo){if(mode)OnCustomizeModeCancelled();OnUndoClicked();return;}
            if(command==Olca.Unity.CustomPadCommand.Confirm){if(mode){if(PreviewFromCommand(customizeSelectedCards).CanExecute)OnCustomizeExecuteRequested();}else if(customScreen.CanConfirm())customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm));return;}
            if(padTransformationIndex>=0&&transformations?.AvailableDefinitions.Count>0){
                int count=transformations.AvailableDefinitions.Count;
                if(command==Olca.Unity.CustomPadCommand.Left||command==Olca.Unity.CustomPadCommand.Up)padTransformationIndex=(padTransformationIndex+count-1)%count;
                else if(command==Olca.Unity.CustomPadCommand.Right||command==Olca.Unity.CustomPadCommand.Down)padTransformationIndex=(padTransformationIndex+1)%count;
                else if(command==Olca.Unity.CustomPadCommand.Decide){var id=transformations.AvailableDefinitions[padTransformationIndex].Id;if(!transformations.IsUsed(id))customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.PreviewTransformation, id));}
                RefreshTransformationButtons();return;
            }
            if(command==Olca.Unity.CustomPadCommand.Left)cursor.MoveLeft();else if(command==Olca.Unity.CustomPadCommand.Right)cursor.MoveRight();else if(command==Olca.Unity.CustomPadCommand.Up)cursor.MoveUp();else if(command==Olca.Unity.CustomPadCommand.Down)cursor.MoveDown();
            else if(command==Olca.Unity.CustomPadCommand.Decide){if(mode){int index=cursor.CurrentIndex;if(index>=0&&index<inputHand.Count)OnCardClicked(inputHand[index]);}else OnDecidePressed(true);}
        }
        private void EnsurePadGuide()
        {
            if(padGuide==null){
                padGuidePanel=new GameObject("CustomPadGuidePanel",typeof(RectTransform),typeof(Image));padGuidePanel.transform.SetParent(transform,false);var area=(RectTransform)padGuidePanel.transform;area.anchorMin=new Vector2(.02f,.94f);area.anchorMax=new Vector2(.98f,.995f);area.offsetMin=area.offsetMax=Vector2.zero;var face=padGuidePanel.GetComponent<Image>();Olca.Presentation.Dungeon.UiLook.Face(face);face.raycastTarget=false;
                var obj=new GameObject("CustomPadGuide",typeof(RectTransform),typeof(Text));obj.transform.SetParent(padGuidePanel.transform,false);padGuide=obj.GetComponent<Text>();padGuide.font=selectedCardDescription?.font??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");padGuide.fontSize=18;padGuide.alignment=TextAnchor.MiddleCenter;padGuide.raycastTarget=false;Olca.Presentation.Dungeon.UiLook.Label(padGuide);var r=padGuide.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(8,2);r.offsetMax=new Vector2(-8,-2);
            }
            padGuidePanel.SetActive(gamepadInput.HasGamepad&&!Olca.Unity.GameplayPause.InputBlocked);string Key(string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad","custom",action)]);
            padGuide.text=$"Pad {Key("Decide")}: 選択 / {Key("Cancel")}: 取消 / {Key("Confirm")}: 確定・加工 / {Key("Customize")}: 加工切替\n{Key("Undo")}: Undo / {Key("Transform")}: 手札・変身 / {Key("Details")}: 詳細 / 方向は1押下1移動";
            if(detailsPanel!=null){var close=detailsPanel.transform.Find("CloseCustomPadDetails")?.GetComponentInChildren<Text>();if(close!=null)close.text="閉じる / "+Key("Cancel")+"・"+Key("Details");}
        }
        private readonly HashSet<CustomDetailHold> touchDetailHolds = new HashSet<CustomDetailHold>();
        private bool CanOpenTouchDetails()=>IsCustomPadUiActive()&&!handAnimating&&!(detailsPanel!=null&&detailsPanel.activeSelf);
        public void ConfigureTouchDetails(Button button, System.Action show)
        {
            if(button==null)return;
            var hold=button.GetComponent<CustomDetailHold>()??button.gameObject.AddComponent<CustomDetailHold>();
            hold.Configure(button,CanOpenTouchDetails,show,touchDetailHolds);
        }
        public void ShowTouchCardDetails(HandCardView view)
        {
            if(!CanOpenTouchDetails()||view==null)return;
            int index=inputHand.IndexOf(view.Card);
            if(index<0||!handCardViews.Contains(view))return;
            padTransformationIndex=-1;cursor.MoveToCard(index);ShowPadDetails();
        }
        private void ShowTouchTransformationDetails(int index)
        {
            if(!CanOpenTouchDetails()||transformations==null||index<0||index>=transformations.AvailableDefinitions.Count||transformations.IsUsed(transformations.AvailableDefinitions[index].Id))return;
            padTransformationIndex=index;ShowPadDetails();
        }
        private void ClosePadDetails(){if(detailsPanel!=null){if(detailsPanel.activeSelf)lastDetailsCloseFrame=Time.frameCount;detailsPanel.SetActive(false);if(padDetailsText!=null)padDetailsText.text="";}}
        private void ShowPadDetails()
        {
            string value;
            if(padTransformationIndex>=0&&transformations!=null&&padTransformationIndex<transformations.AvailableDefinitions.Count){var d=transformations.AvailableDefinitions[padTransformationIndex];value=$"{GetTransformationName(d.Id)}\n手札上限 +{d.HandSizeBonus}\n弱点: {d.Weakness.Element} / {d.Weakness.Property}\n火 {d.Resistances.Get(ElementType.Fire)} / 水 {d.Resistances.Get(ElementType.Water)} / 風 {d.Resistances.Get(ElementType.Wind)} / 土 {d.Resistances.Get(ElementType.Earth)}";if(d.Attack!=null)value+=$"\n固有攻撃: {d.Attack.Element} / 通常 {d.Attack.NormalPowerPercent}% / タメ {d.Attack.ChargedPowerPercent}% / {d.Attack.ChargeFrames}フレーム";}
            else{int index=cursor?.CurrentIndex??-1;if(index<0||index>=inputHand.Count)return;var card=inputHand[index];value=$"{card.Name}\nコスト {card.GetFinalCost()} / 威力 {card.GetFinalPower()}\n属性 {card.Element} / {card.Rarity} / {card.Series} / {card.ExceedLevel}\n\n{card.Description}";}
            if(detailsPanel==null){
                detailsPanel=new GameObject("CustomPadDetails",typeof(RectTransform),typeof(Image));detailsPanel.transform.SetParent(transform,false);var rect=(RectTransform)detailsPanel.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;var detailFace=detailsPanel.GetComponent<Image>();detailFace.sprite=Resources.Load<Sprite>("OlcaUI/description");detailFace.type=Image.Type.Sliced;detailFace.color=Color.white;
                var area=new GameObject("CustomPadDetailsScroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));area.transform.SetParent(detailsPanel.transform,false);var ar=(RectTransform)area.transform;ar.anchorMin=new Vector2(.04f,.18f);ar.anchorMax=new Vector2(.96f,.96f);ar.offsetMin=ar.offsetMax=Vector2.zero;area.GetComponent<Image>().color=new Color(0,0,0,.02f);
                var text=new GameObject("CustomPadDetailsText",typeof(RectTransform),typeof(Text));text.transform.SetParent(area.transform,false);padDetailsText=text.GetComponent<Text>();padDetailsText.font=selectedCardDescription?.font??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");padDetailsText.fontSize=28;padDetailsText.supportRichText=false;padDetailsText.color=Color.white;padDetailsText.alignment=TextAnchor.UpperLeft;padDetailsText.rectTransform.anchorMin=new Vector2(0,1);padDetailsText.rectTransform.anchorMax=Vector2.one;padDetailsText.rectTransform.pivot=new Vector2(.5f,1);padDetailsText.rectTransform.anchoredPosition=Vector2.zero;padDetailsText.rectTransform.sizeDelta=Vector2.zero;
                padDetailsScroll=area.GetComponent<ScrollRect>();padDetailsScroll.viewport=ar;padDetailsScroll.content=padDetailsText.rectTransform;padDetailsScroll.horizontal=false;padDetailsScroll.movementType=ScrollRect.MovementType.Clamped;
                var close=Instantiate(confirmButton.gameObject,detailsPanel.transform);close.name="CloseCustomPadDetails";var button=close.GetComponent<Button>();button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(ClosePadDetails);var cr=(RectTransform)close.transform;cr.anchorMin=cr.anchorMax=new Vector2(.5f,.08f);cr.anchoredPosition=Vector2.zero;cr.sizeDelta=new Vector2(500,64);close.GetComponentInChildren<Text>().text="閉じる / "+Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad","custom","Cancel")])+"・"+Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad","custom","Details")]);
            }
            detailsPanel.SetActive(true);detailsPanel.transform.SetAsLastSibling();padDetailsText.text=value;Canvas.ForceUpdateCanvases();padDetailsText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Max(padDetailsScroll.viewport.rect.height,padDetailsText.preferredHeight+20));padDetailsScroll.verticalNormalizedPosition=1;
        }

        private void HandleInput()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            if(detailsPanel!=null&&detailsPanel.activeSelf){if(Input.GetKeyDown(KeyCode.Escape))ClosePadDetails();return;}
            if(Input.GetKeyDown(KeyCode.LeftArrow)||Input.GetKeyDown(KeyCode.RightArrow)||Input.GetKeyDown(KeyCode.UpArrow)||Input.GetKeyDown(KeyCode.DownArrow)||Input.GetKeyDown(KeyCode.A)||Input.GetKeyDown(KeyCode.D)||Input.GetKeyDown(KeyCode.W)||Input.GetKeyDown(KeyCode.S))padTransformationIndex=-1;
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
            // パッドはHandleCustomGamepadから同じカーソルへ接続する。
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
            // パッドSouthは標準Submitと分離して処理する。
            if (queueConfirmation == null && Input.GetKeyDown(KeyCode.Space))
            {
                OnDecidePressed();
            }

            // ショートカット確定（Enter）
            // パッドStartも同じ確定処理へ渡す。
            if (queueConfirmation == null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (customScreen.CanConfirm())
                {
                    OnConfirmClicked();
                }
            }

            // 一時非表示（Tab - ホールド）
            // パッドの一時非表示操作は今回の対象外。
            if (Input.GetKey(KeyCode.Tab))
            {
                SetScreenVisibility(false);
            }
            else if (Input.GetKeyUp(KeyCode.Tab))
            {
                SetScreenVisibility(true);
            }

            // カスタマイズモード開始（Cキー）
            // パッドNorthも同じ加工入口へ渡す。
            if (Input.GetKeyDown(KeyCode.C))
            {
                EnterCustomizeMode();
                return;
            }

            // キャンセル（ESC）
            // パッドEastも同じ取消入口へ渡す。
            if (queueConfirmation == null && Input.GetKeyDown(KeyCode.Escape))
            {
                CancelSelection();
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
                    if (logical) customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm)); else OnConfirmClicked();
                }
            }
            else
            {
                // カードで決定 → 選択/解除トグル
                int cardIndex = cursor.CurrentIndex;
                if (cardIndex >= 0 && cardIndex < inputHand.Count)
                {
                    OnCardClicked(inputHand[cardIndex]);
                }
            }
        }

        /// <summary>
        /// 末尾のカード1枚、選択が空なら未確定の変身を取り消す。
        /// </summary>
        private void CancelSelection()
        {
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming ||
                Olca.Unity.GameplayPause.InputBlocked) return;
            var selected = customScreen.SelectedCards;
            if (selected.Count == 0)
            {
                customScreen?.Commands.Execute(new CustomScreenCommand(CustomCommandKind.CancelTransformation));
                return;
            }
            if (!customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Deselect, customScreen.Commands.HandleOf(selected[selected.Count - 1]))).Accepted) return;
            RefreshSelection();
        }

        /// <summary>
        /// カーソル位置が変更された
        /// </summary>
        public void OnCardPointerEntered(HandCardView view)
        {
            if (Olca.Unity.GameplayPause.InputBlocked || handAnimating) return;
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming) return;
            padTransformationIndex=-1;
            int index = handCardViews.IndexOf(view);
            if (index >= 0) cursor.MoveToCard(index);
        }

        public void OnCardPointerExited(HandCardView view)
        {
            if (Olca.Unity.GameplayPause.InputBlocked || handAnimating) return;
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
            handAnimating = false;
            if (customScreen == null || !customScreen.IsOpen || customScreen.IsConfirming) return;
            // 表示完了で、演出中に操作したカーソル位置を巻き戻さない。
            OnCursorMoved(cursor.CurrentIndex);
        }

        /// <summary>
        /// カーソル表示を更新
        /// </summary>
        private void UpdateCursorIndicator(int index)
        {
            if (cursorIndicator == null) return;

            cursorIndicator.SetActive(true);

            RectTransform cursorRect = cursorIndicator.GetComponent<RectTransform>();
            // 枠は手札の後ろに描く。背景より前、カードの絵と文字より後ろを維持する。
            if(handCardContainer!=null&&handCardContainer.parent!=null){
                cursorRect.SetParent(handCardContainer.parent,true);
                int sibling=handCardContainer.GetSiblingIndex();
                if(cursorRect.GetSiblingIndex()<sibling)sibling--;
                cursorRect.SetSiblingIndex(sibling);
            }
            var cursorImage=cursorIndicator.GetComponent<Image>();
            if(cursorImage!=null)cursorImage.raycastTarget=false;

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

        private void InitializeInputHand(IEnumerable<Card> hand)
        {
            inputHand = hand.ToList();
            cursor.Initialize(inputHand.Count);
            if (inputHand.Count == 0) cursor.MoveToConfirmButton();
        }

        // スライドアウト開始・論理終了・再表示・破棄で同じ所有タスクを終了する。
        public void StopHandPresentation()
        {
            var previous = animationCts;
            animationCts = null;
            previous?.Cancel();
            previous?.Dispose();
            handAnimating = false;
        }

        private void OnBattleScreenClosed(List<Card> cards)
        {
            StopHandPresentation();
            Olca.Unity.BattleVisualClock.SetPaused(this,false);
        }

        private void OnScreenOpened(List<Card> hand)
        {
            Olca.Unity.BattleVisualClock.SetPaused(this,true);
            ResetCustomPad();
            UpdateSelectedCardDisplay();
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            if (selectedCardDescription != null) selectedCardDescription.text = "カードを選択してください";
            // 前回のアニメーションをキャンセル
            StopHandPresentation();
            animationCts = new CancellationTokenSource();

            // 既存の手札Viewをクリア
            foreach (var view in handCardViews)
            {
                Destroy(view.gameObject);
            }
            handCardViews.Clear();

            // 入力対象とカーソルは演出を待たず、開いた時点の手札順で固定する。
            InitializeInputHand(hand);
            handAnimating = true;
            // 新しい手札View生成（アニメーション付き）
            if (isActiveAndEnabled) AnimateCardsShootAsync(new List<Card>(hand), animationCts.Token).Forget();

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
                ct.ThrowIfCancellationRequested();
                var card = hand[i];
                var cardObj = Instantiate(handCardPrefab, handCardContainer);
                var cardView = cardObj.GetComponent<HandCardView>();
                cardView.Initialize(card, this);
                cardView.UpdateSelection(customScreen.SelectedCards.Contains(card));
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
            ct.ThrowIfCancellationRequested();
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
                if (index >= 0 && cardPositionMarkers != null && cardPositionMarkers.Length >= 6) {
                    var first = ((RectTransform)cardPositionMarkers[0]).anchoredPosition;
                    var column = ((RectTransform)cardPositionMarkers[1]).anchoredPosition - first;
                    var rowStep = ((RectTransform)cardPositionMarkers[5]).anchoredPosition - first;
                    return first + column * (index % 5) + rowStep * (index / 5);
                }
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
            padTransformationIndex=-1;
            if (Olca.Unity.GameplayPause.InputBlocked || (detailsPanel!=null&&detailsPanel.activeSelf)) return;
            // カスタマイズモード中はカスタマイズ選択へ転送
            if (customizeModeView != null && customizeModeView.IsActive)
            {
                OnCardClickedInCustomizeMode(card);
                return;
            }

            // ポインターが静止したままキー操作から戻っても、クリックした個体へ操作位置を揃える。
            // 選択で詰め直されるCurrentHandではなく、画面を開いた時の固定順を使う。
            int clickedIndex = inputHand.IndexOf(card);
            if (cursor != null && clickedIndex >= 0 && cursor.CurrentIndex != clickedIndex)
                cursor.MoveToCard(clickedIndex);

            // 通常モード: カード選択トグル
            if (customScreen.SelectedCards.Any(c => c == card))
            {
                customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Deselect, customScreen.Commands.HandleOf(card)));
            }
            else
            {
                customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Select, customScreen.Commands.HandleOf(card)));
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
            padTransformationIndex=-1;
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
                    var invalid = PreviewFromCommand(customizeSelectedCards.Concat(new[] { card }).ToArray());
                    customizeModeView.ShowError(invalid.Reason ?? "このカードは現在の選択と組み合わせできません");
                    UpdateCursorCardDisplay(handCardViews.FindIndex(v => v.Card == card));
                    return;
                }
                customizeSelectedCards.Add(card);
                customizeModeView.ClearError();
            }

            // 操作種別を判定してUIに反映
            var opType = DetectCustomizeOperation();
            bool canExecute = PreviewFromCommand(customizeSelectedCards).CanExecute;
            customizeModeView.UpdateExecuteButton(canExecute);
            customizeModeView.UpdateOperationHint(GetOperationHint(opType));

            RefreshCustomizeHighlights(opType);
            UpdateCursorCardDisplay(cursor?.CurrentIndex ?? -1);
        }

        /// <summary>
        /// 選択中のカードから操作種別を自動判定する
        /// </summary>
        private CustomizationOperation DetectCustomizeOperation()
            => PreviewFromCommand(customizeSelectedCards).Operation;

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
        private CustomizationPreview PreviewFromCommand(IReadOnlyList<Card> materials)
        {
            if (materials == null || materials.Count != 2) return CustomizationPreview.Invalid("素材を2枚選んでください");
            var response = customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.PreviewCustomization,
                materials.Select(customScreen.Commands.HandleOf).ToArray()));
            return response.Preview ?? CustomizationPreview.Invalid(response.Reason);
        }

        private void OnCustomizeExecuteRequested()
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            lastModeInputFrame=Time.frameCount;
            var response = customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.ExecuteCustomization, customizeSelectedCards.Select(customScreen.Commands.HandleOf).ToArray()));
            var result = response.Preview ?? CustomizationPreview.Invalid(response.Reason);
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
            ResetCustomPad();
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
            customScreen?.Commands.Execute(new CustomScreenCommand(CustomCommandKind.CancelCustomization));
            lastModeInputFrame=Time.frameCount;
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
            StopHandPresentation();
            foreach (var view in handCardViews)
                Destroy(view.gameObject);
            handCardViews.Clear();

            var hand = customScreen.CurrentHand.Concat(customScreen.SelectedCards).ToList();
            InitializeInputHand(hand); handAnimating = false;
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var cardObj = Instantiate(handCardPrefab, handCardContainer);
                var cardView = cardObj.GetComponent<HandCardView>();
                cardView.Initialize(card, this);
                cardView.UpdateSelection(customScreen.SelectedCards.Contains(card));
                handCardViews.Add(cardView);

                var rectTransform = cardView.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = GetCardPosition(i);
                rectTransform.rotation = Quaternion.identity;
            }

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
                            highlightType = customScreen.IsTuningMaterial(card)
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
        private void RemoveSelectedSlot(Card card)
        {
            if(Olca.Unity.GameplayPause.InputBlocked||(detailsPanel!=null&&detailsPanel.activeSelf))return;
            var response=customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.RemoveSelectedCard,customScreen.Commands.HandleOf(card)));
            if(!response.Accepted)return;
            int index=inputHand.IndexOf(card);
            if(index>=0&&cursor!=null)cursor.MoveToCard(index);
            RefreshSelection();UpdateCursorCardDisplay(index);
        }

        private void UpdateSelectedCardDisplay()
        {
            if (customScreen == null) return;
            var indicator = transform.Find("SelectionIndicator");
            if (indicator == null) return;
            // 追加した装飾子を再帰列挙せず、元のスロットだけを左から割り当てる。
            var images = indicator.Cast<Transform>().Select(t=>t.GetComponent<Image>()).Where(i=>i!=null)
                .OrderBy(i=>i.rectTransform.anchoredPosition.x).ToArray();
            for(int i=0;i<images.Length;i++){
                var image=images[i];var card=i<customScreen.SelectedCards.Count?customScreen.SelectedCards[i]:null;
                image.gameObject.SetActive(i<customScreen.MaxSelectionCount);
                image.sprite=card==null?null:GetCardIcon(card.Id);
                image.preserveAspect=true;
                image.color=card==null?new Color(.18f,.27f,.33f,1f):Color.white;
                image.raycastTarget=card!=null;
                var button=image.GetComponent<Button>()??image.gameObject.AddComponent<Button>();
                button.targetGraphic=image;button.transition=Selectable.Transition.None;
                button.navigation=new Navigation{mode=Navigation.Mode.None};
                button.onClick.RemoveAllListeners();
                button.interactable=card!=null&&customScreen.IsOpen&&!customScreen.IsConfirming;
                if(card!=null){var owner=customScreen;button.onClick.AddListener(()=>{
                    if(!ReferenceEquals(owner,customScreen)||!owner.IsOpen||owner.IsConfirming||
                        !owner.SelectedCards.Contains(card)||(customizeModeView!=null&&customizeModeView.IsActive))return;
                    RemoveSelectedSlot(card);
                });}
            }
        }

        /// <summary>
        /// カーソル位置のカード情報を表示エリアに表示
        /// </summary>
        private void UpdateCursorCardDisplay(int cursorIndex)
        {
            if (customizeModeView != null && customizeModeView.IsActive)
            {
                if(selectedCardImage!=null)selectedCardImage.sprite=null;
                UpdateCustomizationPreview(cursorIndex);
                return;
            }
            if (cursorIndex >= 0 && cursorIndex < inputHand.Count)
            {
                // 表示の生成途中でも、操作対象のカード情報を使用する。
                var cursorCard = inputHand[cursorIndex];

                if (selectedCardDescription != null)
                {
                    // コスト、威力、説明を表示
                    selectedCardDescription.text =
                        $"{cursorCard.Name}\nコスト: {cursorCard.GetFinalCost()}\n" +
                        $"威力: {cursorCard.GetFinalPower()}\n\n" +
                        $"{cursorCard.Description}";
                }

                // 本絵の登録だけを参照する。未登録時に専用miniやUI部品を本絵へ流用しない。
                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                    selectedCardImage.sprite=GetCardIllustration(cursorCard.Id);
                    selectedCardImage.color=selectedCardImage.sprite!=null?Color.white:new Color(.35f,.7f,.77f,.16f);
                    selectedCardImage.preserveAspect=true;
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
                    selectedCardImage.color = new Color(.35f,.7f,.77f,.16f);
                    selectedCardImage.sprite=null;
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
                    selectedCardImage.color = new Color(.35f,.7f,.77f,.16f);
                    selectedCardImage.sprite=null;
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
            customizeModeView.UpdateExecuteButton(PreviewFromCommand(customizeSelectedCards).CanExecute);
            if (materials.Count < 2)
            {
                string guide = materials.Count == 0 ? "最初の素材を選んでください" : "2枚目にカーソルを合わせてください";
                customizeModeView.UpdateOperationHint(guide);
                if (selectedCardDescription != null) selectedCardDescription.text = guide;
                return;
            }
            var preview = PreviewFromCommand(materials);
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
            if (queueConfirmation == null) { customScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm)); return; }
            var requestedScreen = customScreen;
            queueConfirmation(pointerClickTimestamp(), () => {
                if (this == null || !ReferenceEquals(customScreen, requestedScreen) || Olca.Unity.GameplayPause.InputBlocked) return;
                requestedScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm));
            });
        }

        private void OnConfirmed(List<Card> selectedCards)
        {
            StopHandPresentation();
            ResetCustomPad();
            customizeSelectedCards.Clear();
            if (customizeModeView != null) customizeModeView.Exit();
            if (customizeModeOverlay != null) customizeModeOverlay.gameObject.SetActive(false);
            if (selectedCardDescription != null) selectedCardDescription.text = "選択したカードを確定しました";
            UpdateUndoButton();
            UpdateSelectedCardDisplay();
            // 購読者（BattleController・DungeonBattleControllerなど）に通知
            OnCustomScreenConfirmed?.Invoke();
        }

        private void OnDestroy()
        {
            Olca.Unity.BattleVisualClock.SetPaused(this,false);
            gamepadInput?.Dispose();
            foreach(var icon in generatedMiniIcons.Values)if(icon!=null){var texture=icon.texture;if(Application.isPlaying){Destroy(icon);Destroy(texture);}else{DestroyImmediate(icon);DestroyImmediate(texture);}}
            generatedMiniIcons.Clear();

            BindTransformations(null);
            if (undoCustomizationButton != null) undoCustomizationButton.onClick.RemoveListener(OnUndoClicked);
            // アニメーション用CancellationTokenをキャンセル
            StopHandPresentation();

            if (customScreen != null)
            {
                customScreen.OnScreenOpened -= OnScreenOpened;
                customScreen.OnScreenClosed -= OnBattleScreenClosed;
                customScreen.OnHandChanged -= OnHandChanged;
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

namespace Olca.Presentation.CustomScreen
{
    /// <summary>既存のカード・カスタムUI用語を、指定フォントで読めるカナ表記へ変換する。</summary>
    public static class CustomDisplay
    {
        private static readonly System.Collections.Generic.Dictionary<string,string> readings=CreateReadings();
        private static System.Collections.Generic.Dictionary<string,string> CreateReadings()
        {
            var result=new System.Collections.Generic.Dictionary<string,string>();
            foreach(var entry in "不正=フセイ|付与=フヨ|以上=イジョウ|位置=イチ|使用=シヨウ|使用予定=シヨウヨテイ|使用済=シヨウズ|別=ベツ|前方=ゼンポウ|加工=カコウ|加工切替=カコウキリカエ|取消=トリケシ|合=ア|回復=カイフク|固有攻撃=コユウコウゲキ|土=ツチ|壁=カベ|変身=ヘンシン|威力=イリョク|実行=ジッコウ|実行不可=ジッコウフカ|対象=タイショウ|専用=センヨウ|属性=ゾクセイ|弱点=ジャクテン|強化効果=キョウカコウカ|強靭度削=キョウジンドケズ|必要=ヒツヨウ|戻=モド|手札=テフダ|手札上限=テフダジョウゲン|押下=オウカ|攻撃=コウゲキ|方向=ホウコウ|既存=キソン|時間定義=ジカンテイギ|最初=サイショ|未定義=ミテイギ|本絵=ホンエ|枚目=マイメ|枚選=マイエラ|枚選択中=マイセンタクチュウ|水=ミズ|火=ヒ|炎属性攻撃=ホノオゾクセイコウゲキ|現在=ゲンザイ|用=ヨウ|確定=カクテイ|秒=ビョウ|移動=イドウ|範囲攻撃=ハンイコウゲキ|素材=ソザイ|組=ク|結果=ケッカ|融合=ユウゴウ|要素=ヨウソ|解除=カイジョ|設定=セッテイ|詳細=ショウサイ|調律=チョウリツ|超越=チョウエツ|通常=ツウジョウ|選=エラ|選択=センタク|選択中=センタクチュウ|配置=ハイチ|重複=チョウフク|閉=ト|防御=ボウギョ|風=カゼ|一度=イチド|上位=ジョウイ|上限=ジョウゲン|仮選択=カリセンタク|初回手札=ショカイテフダ|別々=ベツベツ|回=カイ|変身定義=ヘンシンテイギ|復元=フクゲン|性能=セイノウ|戦闘=セントウ|指定=シテイ|最大=サイダイ|最大選択枚数=サイダイセンタクマイスウ|枚=マイ|枚固定=マイコテイ|残=ノコ|画面状態=ガメンジョウタイ|確定後=カクテイゴ|融合先=ユウゴウサキ|超=コ|開始前=カイシマエ|中=チュウ|離=ハナ|再押下=サイオウカ|短押=タンオ|長押=ナガオ|再開=サイカイ|停止=テイシ|完了=カンリョウ|発動=ハツドウ|中断=チュウダン|待機=タイキ|説明=セツメイ|出撃=シュツゲキ|出発=シュッパツ|所持=ショジ|編成=ヘンセイ|新規=シンキ|攻略=コウリャク|開始=カイシ|試作=シサク|保存=ホゾン|続行=ゾッコウ|未設定=ミセッテイ|敗北=ハイボク|撤退=テッタイ|帰還=キカン|総経験値=ソウケイケンチ|経験値=ケイケンチ|永続=エイゾク|装備=ソウビ|図鑑=ズカン|交換所=コウカンジョ|交換=コウカン|成長=セイチョウ|保持=ホジ|基本=キホン|無属性=ムゾクセイ|再=サイ|失=ウシナ|前回=ゼンカイ|次回=ジカイ|前=マエ|次=ツギ|通貨=ツウカ|獲得=カクトク|未割当=ミワリアテ|左=ヒダリ|右=ミギ|上=ウエ|下=シタ|十字=ジュウジ|自動=ジドウ|前進=ゼンシン|階=カイ".Split('|')){var pair=entry.Split('=');result.Add(pair[0],pair[1]);}
            foreach(var entry in "続=ツヅ|演出=エンシュツ|音量=オンリョウ|文字表示=モジヒョウジ|標準=ヒョウジュン|短縮=タンシュク|即時=ソクジ|速=ハヤ|失敗=シッパイ|再試行=サイシコウ|消去=ショウキョ".Split('|')){var pair=entry.Split('=');result.Add(pair[0],pair[1]);}
            foreach(var entry in "保存済=ホゾンズ|反映=ハンエイ|反映済=ハンエイズ|不可=フカ|予定=ヨテイ|超過変換=チョウカヘンカン|帰還後=キカンゴ|状態=ジョウタイ|全回復=ゼンカイフク".Split('|')){var pair=entry.Split('=');result.Add(pair[0],pair[1]);}
            return result;
        }
        public static string Katakana(string text)
        {
            if(string.IsNullOrEmpty(text))return text;
            // 送り仮名込みで読む語だけ先に扱い、単漢字の多義な読みを増やさない。
            text=text.Replace("持ち帰","モチカエ").Replace("同じ","オナジ");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"[一-鿿々]+",m=>{
                var result=new System.Text.StringBuilder();
                for(int i=0;i<m.Value.Length;){bool found=false;
                    for(int length=m.Value.Length-i;length>0;length--){
                        if(!readings.TryGetValue(m.Value.Substring(i,length),out var kana))continue;
                        result.Append(kana);i+=length;found=true;break;
                    }
                    if(!found)result.Append(m.Value[i++]);
                }
                return result.ToString();
            });
            var chars=text.ToCharArray();
            for(int i=0;i<chars.Length;i++)if(chars[i]>='ぁ'&&chars[i]<='ゖ')chars[i]=(char)(chars[i]+0x60);
            return new string(chars);
        }
    }
}

namespace Olca.Presentation.CustomScreen
{
    /// <summary>表示素材だけをIDに対応付ける。未登録の製品名称・画像を生成しない。</summary>
    [System.Serializable]
    public sealed class TransformationVisualEntry
    {
        public string Id;
        public string DisplayNameKana;
        public UnityEngine.Sprite Icon;
        public TransformationVisualEntry Copy()=>new TransformationVisualEntry{Id=Id,DisplayNameKana=DisplayNameKana,Icon=Icon};
    }
}
