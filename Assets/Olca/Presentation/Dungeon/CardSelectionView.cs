using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// バトル勝利後のカード選択画面View
    /// DungeonBattleController から ShowAsync() を呼び、選択完了まで待機する
    /// </summary>
    public class CardSelectionView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject containerRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private CardSelectionItemView[] cardItems;
        [SerializeField] private Text deckCountText;

        [Header("アニメーション")]
        [SerializeField] private float fadeInDuration = 0.3f;

        private UniTaskCompletionSource<Card> selectionTcs;
        private Font presentationFont;
        private System.Func<Card,Sprite> illustrationSource;
        private Image rewardHeader;
        public void ConfigurePresentation(Font font,System.Func<Card,Sprite> artwork)
        {presentationFont=font;illustrationSource=artwork;if(font!=null)foreach(var text in GetComponentsInChildren<Text>(true))text.font=font;}
        private Button skipButton;
        private Text battleTimeText;
        private Olca.Unity.MenuPadScope rewardMenu;

        /// <summary>
        /// カード選択画面を表示し、選択が完了するまで待機する
        /// </summary>
        /// <param name="choices">選択肢のカードリスト</param>
        /// <param name="currentDeckCount">現在のデッキ枚数</param>
        /// <param name="ct">キャンセルトークン（キャンセル時は null を返す）</param>
        /// <returns>選択されたカード（スキップまたはキャンセル時は null）</returns>
        public async UniTask<Card> ShowAsync(
            IReadOnlyList<Card> choices,
            int currentDeckCount,
            CancellationToken ct = default, long? battleLogicalFrames = null, Olca.Core.Logic.Battle.BattleEvaluationResult evaluation = null)
        {
            selectionTcs = new UniTaskCompletionSource<Card>();
            if(containerRoot!=null)UiLook.Face(containerRoot.GetComponent<Image>());
            UiLook.Label(deckCountText);
            if(deckCountText!=null)deckCountText.color=Color.white; // 暗い背景へ直接重なる枚数表示を読める色にする。
            // Sceneに置かれた見出しも、素材・フォント・配置を保ったままカナで表示する。
            foreach(var label in (containerRoot!=null?containerRoot:gameObject).GetComponentsInChildren<Text>(true))
                label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(label.text);
            EnsureSkipButton();
            UpdateBattleTime(battleLogicalFrames,evaluation);

            // デッキ枚数表示
            if (deckCountText != null)
                deckCountText.text = $"デッキ: {currentDeckCount}枚";

            // cardItems未登録のものも含め、全CardSelectionItemViewを一旦非表示にする
            // （cardItemsに含まれていない余分なビューが card=null のままクリックを受けるのを防ぐ）
            var allItems = GetComponentsInChildren<CardSelectionItemView>(true);
            foreach (var item in allItems)
                item.gameObject.SetActive(false);

            // カード選択肢をセット
            for (int i = 0; i < cardItems.Length; i++)
            {
                if (i < choices.Count)
                {
                    cardItems[i].ConfigurePresentation(presentationFont,illustrationSource?.Invoke(choices[i]));
                    cardItems[i].Setup(choices[i]);
                    var choiceButton=cardItems[i].GetComponentInChildren<Button>(true);if(choiceButton!=null)choiceButton.name="CardRewardChoice_"+i;
                    cardItems[i].gameObject.SetActive(true);
                }
            }

            // 表示
            if (containerRoot != null)
                containerRoot.SetActive(true);

            UpdateChoiceLayout();

            // フェードイン
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                await FadeAsync(canvasGroup, 0f, 1f, fadeInDuration, ct);
            }

            if(rewardMenu==null)rewardMenu=new Olca.Unity.MenuPadScope(()=>!Olca.Unity.GameplayPause.InputBlocked&&Olca.Unity.MenuPadScope.IsTopmost(containerRoot!=null?containerRoot.transform:transform));
            rewardMenu.Bind(containerRoot!=null?containerRoot.transform:transform,"SkipCardReward",()=>OnItemClicked(null));
            // キャンセル時は null を返す
            ct.Register(() => selectionTcs.TrySetResult(null));

            return await selectionTcs.Task;
        }

        /// <summary>描画時刻を使わず、停止を除外した60Hz論理フレームを百分の一秒へ切り捨て表示する。</summary>
        public static string FormatBattleTime(long? frames)
        {
            if(!frames.HasValue)return string.Empty;
            if(frames.Value<0)throw new System.ArgumentOutOfRangeException(nameof(frames));
            long value=frames.Value;
            return $"ゲキハタイム: {value/3600:D2}:{value/60%60:D2}.{value%60*100/60:D2}";
        }
        public static string FormatBattleSummary(long? frames,Olca.Core.Logic.Battle.BattleEvaluationResult evaluation=null)
        {
            if(evaluation==null)return FormatBattleTime(frames);
            string grade=evaluation.Grade==Olca.Core.Logic.Battle.BattleEvaluationGrade.Excellent?"シュウ":
                evaluation.Grade==Olca.Core.Logic.Battle.BattleEvaluationGrade.Great?"ユウ":
                evaluation.Grade==Olca.Core.Logic.Battle.BattleEvaluationGrade.Good?"リョウ":"カ";
            return "ヒョウカ: "+grade+" / "+FormatBattleTime(evaluation.LogicalFrames);
        }
        private void UpdateBattleTime(long? frames,Olca.Core.Logic.Battle.BattleEvaluationResult evaluation)
        {
            if(evaluation!=null)frames=evaluation.LogicalFrames;
            string text=FormatBattleSummary(frames,evaluation);
            if(battleTimeText==null&&frames.HasValue&&deckCountText!=null)
            {
                var go=new GameObject("BattleTimeText",typeof(RectTransform),typeof(Text));
                go.transform.SetParent(deckCountText.transform.parent,false);
                battleTimeText=go.GetComponent<Text>();
                battleTimeText.font=deckCountText.font;battleTimeText.fontSize=deckCountText.fontSize;
                battleTimeText.fontStyle=deckCountText.fontStyle;battleTimeText.alignment=TextAnchor.MiddleCenter;
                battleTimeText.color=Color.white;battleTimeText.raycastTarget=false;
                var rect=battleTimeText.rectTransform;var source=deckCountText.rectTransform;
                rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;
                rect.anchoredPosition=source.anchoredPosition+Vector2.down*(source.rect.height+8f);
                rect.sizeDelta=new Vector2(Mathf.Max(600f,source.rect.width),Mathf.Max(30f,source.rect.height));
            }
            if(battleTimeText!=null){battleTimeText.text=text;battleTimeText.gameObject.SetActive(frames.HasValue);}
        }

        private void LateUpdate()
        {
            if (containerRoot != null && containerRoot.activeInHierarchy) UpdateChoiceLayout();
            rewardMenu?.Tick();
        }

        private void UpdateChoiceLayout()
        {
            RefreshRewardHeader();
            if (cardItems == null) return;
            var visible = new List<RectTransform>();
            float width = 0f, height = 0f;
            foreach (var item in cardItems)
            {
                if (item == null || !item.gameObject.activeSelf) continue;
                var rect = item.transform as RectTransform;
                if (rect == null) continue;
                visible.Add(rect);
                width = Mathf.Max(width, rect.rect.width);
                height = Mathf.Max(height, rect.rect.height);
            }
            if (visible.Count == 0 || width <= 0f || height <= 0f) return;
            var parent = visible[0].parent as RectTransform;
            if (parent == null) return;
            float gap = presentationFont!=null?24f:16f;
            // 上下はHUD用の余白を残し、カードと文字を同じ比率で縮小する。
            float availableWidth = Mathf.Max(1f, parent.rect.width - 32f - gap * (visible.Count - 1));
            float availableHeight = Mathf.Max(1f, parent.rect.height - (presentationFont!=null?320f:220f));
            float scale = Mathf.Min(1f, availableWidth / (width * visible.Count), availableHeight / height);
            for (int i = 0; i < visible.Count; i++)
            {
                var rect = visible[i];
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2((i - (visible.Count - 1) * 0.5f) * (width * scale + gap), presentationFont!=null?Mathf.Clamp(-5f,-parent.rect.height*.5f+112f+height*scale*.5f,parent.rect.height*.5f-208f-height*scale*.5f):45f);
            }
        }

        private void RefreshRewardHeader()
        {
            if(presentationFont==null||containerRoot==null)return;
            var root=containerRoot.transform as RectTransform;if(root==null)return;
            bool created=rewardHeader==null;
            if(created){var obj=new GameObject("RewardHeader",typeof(RectTransform),typeof(Image));obj.transform.SetParent(root,false);rewardHeader=obj.GetComponent<Image>();rewardHeader.sprite=Resources.Load<Sprite>("OlcaUI/description");rewardHeader.type=Image.Type.Sliced;rewardHeader.color=Color.white;rewardHeader.raycastTarget=false;}
            float width=Mathf.Max(360,Mathf.Min(1180,root.rect.width-600));var rect=rewardHeader.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-24);rect.sizeDelta=new Vector2(width,160);
            void Label(Text text,float x,float y,float w,int size){if(text==null)return;text.font=presentationFont;text.fontSize=size;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;var r=text.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,48);if(created)text.transform.SetAsLastSibling();}
            Label(root.Find("TitleText")?.GetComponent<Text>(),0,-62,width-48,36);
            Label(deckCountText,-width*.32f,-128,width*.27f,24);
            Label(battleTimeText,width*.15f,-128,width*.64f,24);
        }

        /// <summary>
        /// カード選択画面を非表示にしてリセット
        /// </summary>
        public void Hide()
        {
            rewardMenu?.Suspend();
            selectionTcs?.TrySetResult(null);
            if (containerRoot != null)
                containerRoot.SetActive(false);
        }

        /// <summary>
        /// カードアイテムがクリックされたときに CardSelectionItemView から呼ばれる
        /// </summary>
        internal void OnItemClicked(Card card)
        {
            if (Olca.Unity.GameplayPause.InputBlocked) return;
            // 受取とスキップの競合も最初の確定だけを既存Controllerへ渡す。
            if (selectionTcs == null || !selectionTcs.TrySetResult(card)) return;
            rewardMenu?.Suspend();
            if (containerRoot != null) containerRoot.SetActive(false);
        }

        private void EnsureSkipButton()
        {
            if (skipButton != null) return;
            var buttonObject = new GameObject("SkipCardReward", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(containerRoot != null ? containerRoot.transform : transform, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 65f); rect.sizeDelta = new Vector2(500f, 70f);
            buttonObject.GetComponent<Image>().color = new Color(.08f, .2f, .27f, .9f);
            skipButton = buttonObject.GetComponent<Button>();
            skipButton.onClick.AddListener(() => OnItemClicked(null));
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
            var label = labelObject.GetComponent<Text>();
            label.font = deckCountText != null ? deckCountText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28; label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.9f, .97f, 1f); label.raycastTarget = false;
            label.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana("受け取らずに進む"); UiLook.Button(skipButton);
        }

        private async UniTask FadeAsync(
            CanvasGroup group,
            float from, float to, float duration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                await Olca.Unity.GameplayPause.NextFrame(ct);
            }
            group.alpha = to;
        }

        private void OnDisable(){rewardMenu?.Suspend();}
        private void OnDestroy()
        {
            rewardMenu?.Dispose();
            selectionTcs?.TrySetCanceled();
        }
    }

}

namespace Olca.Presentation.Dungeon
{
    using System;
    using System.Linq;
    // 通常クリアの選択・経験値反映・帰還確認。保存判断はController/Coreへ委譲する。
    public sealed class ClearReturnView : MonoBehaviour
    {
        private Font font;
        private Action<int> toggle;
        private Action confirm, finish, retry, depart, closeCollection, editOwnedDeparture, savedDeckMenu, selectDungeon, selectStyle, openExchange;
        private int savedDeckPage;
        private int departurePage;
        private GameObject content;
        private RectTransform referenceContent;
        private int page;
        private string lastRun;
        private ClearReturnStage? lastStage;
        private const int PageSize = 8;
        public void Initialize(Font font, Action<int> toggle, Action confirm, Action finish, Action retry, Action depart = null, Action closeCollection = null, Action editOwnedDeparture = null, Action savedDeckMenu = null, Action selectDungeon = null, Action selectStyle = null, Action openExchange = null)
        {
            this.font = font; this.toggle = toggle; this.confirm = confirm; this.finish = finish; this.retry = retry; this.depart = depart; this.closeCollection = closeCollection; this.editOwnedDeparture = editOwnedDeparture;this.savedDeckMenu=savedDeckMenu;this.selectDungeon=selectDungeon;this.selectStyle=selectStyle;this.openExchange=openExchange;
            var rect = (RectTransform)transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = UiLook.Surface;
            var frame = gameObject.AddComponent<Outline>(); frame.effectColor = new Color(.48f,.9f,.94f,.45f); frame.effectDistance = new Vector2(1,-1); gameObject.SetActive(false);
        }
        private int collectionPage;
        private bool recipesTab;
        private Action collectionBackAction;
        private Olca.Unity.MenuPadScope menu;private Transform menuRoot;private Action menuBack,menuBackSource;
        private readonly System.Collections.Generic.Dictionary<string,string> menuFocus=new System.Collections.Generic.Dictionary<string,string>();
        private Func<System.Collections.Generic.IEnumerable<Button>> menuExtras;private Func<OwnedCardCount,Card> resolveOwned;private Func<Card,Sprite> resolveReturnIcon;
        private GameObject readonlyDetails;private ScrollRect readonlyScroll;
        private Vector2 readonlyViewportSize,readonlyParentSize;private string readonlyBodyValue;private float readonlyPixelsPerUnit;private float readonlyScrollPosition=1;
        public bool HasMenuModal=>readonlyDetails!=null||(content!=null&&(content.transform.Find("KeepEditingDeparture")!=null||content.transform.Find("ConfirmDeleteDeck")!=null));
        public void ConfigureMenuNavigation(Func<System.Collections.Generic.IEnumerable<Button>> extras,Func<OwnedCardCount,Card> resolveOwned=null,Func<Card,Sprite> resolveReturnIcon=null){menuExtras=extras;this.resolveOwned=resolveOwned;this.resolveReturnIcon=resolveReturnIcon;}
        private bool IsMenuTop()
        {
            if(!gameObject.activeInHierarchy||menuRoot==null||!menuRoot.gameObject.activeInHierarchy)return false;
            var es=UnityEngine.EventSystems.EventSystem.current;if(es==null)return true;
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es){position=new Vector2(Screen.width/2f,Screen.height/2f)},hits);
            return hits.Count==0||hits[0].gameObject.transform==transform||hits[0].gameObject.transform.IsChildOf(transform);
        }
        private void SaveMenuFocus(){if(menuRoot==null)return;var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;var name=selected!=null&&selected.GetComponent<Button>()!=null?selected.name:menu?.FocusName;if(name!=null)menuFocus[menuRoot.name]=name;}
        private void StopMenu(){var surface=GetComponent<Image>();if(surface!=null)surface.color=UiLook.Surface;SaveMenuFocus();menu?.Suspend();menuRoot=null;menuBack=null;menuBackSource=null;if(readonlyDetails!=null){readonlyDetails.SetActive(false);if(Application.isPlaying)Destroy(readonlyDetails);else DestroyImmediate(readonlyDetails);readonlyDetails=null;}readonlyScroll=null;}
        // 現在の画面か、その画面を覆う読取専用モーダルだけが入力を所有する。
        private bool OwnsContent(GameObject owner)=>this!=null&&owner!=null&&owner.activeInHierarchy&&(readonlyDetails!=null?readonlyDetails==owner:content==owner);
        private void SetMenu(Transform root,Action back,string preferred,bool force=false,ScrollRect scroll=null,bool directionScroll=true,bool resetMissing=false)
        {
            var owner=root.gameObject;Action guardedBack=back==null?null:(Action)(()=>{if(OwnsContent(owner))back();});
            SaveMenuFocus();menuRoot=root;menuBackSource=back;menuBack=guardedBack;bool cached=menuFocus.TryGetValue(root.name,out var old);
            if(!force&&cached){
                // 消えた全文入口からは画面の既定へ戻し、無関係な外部操作へ飛ばさない。
                if((resetMissing||old.EndsWith("Details",StringComparison.Ordinal))&&!root.GetComponentsInChildren<Button>().Concat(menuExtras?.Invoke()??System.Array.Empty<Button>()).Any(b=>b!=null&&b.gameObject.activeInHierarchy&&b.name==old&&b.IsInteractable()))cached=false;
                else preferred=old;
            }
            if(menu==null)menu=new Olca.Unity.MenuPadScope(IsMenuTop,()=>menuRoot==content?.transform&&!HasMenuModal?(menuExtras?.Invoke()??System.Array.Empty<Button>()):System.Array.Empty<Button>());
            Canvas.ForceUpdateCanvases();menu.Bind(root,preferred,guardedBack,scroll,cached,directionScroll);
        }
        // 固定配置の画面は基準の行間を保ち、幅または高さが足りないときだけ等比で収める。
        private void UseReferenceLayout()
        {
            referenceContent=(RectTransform)content.transform;
            referenceContent.anchorMin=referenceContent.anchorMax=new Vector2(.5f,.5f);
            referenceContent.anchoredPosition=Vector2.zero;referenceContent.sizeDelta=new Vector2(1920,1080);
            FitReferenceLayout();
        }
        private void FitReferenceLayout()
        {
            if(referenceContent==null||content==null||referenceContent.gameObject!=content)return;
            var viewport=((RectTransform)transform).rect.size;
            if(viewport.x<=0||viewport.y<=0)return;
            float scale=Mathf.Min(1,viewport.x/1920f,viewport.y/1080f);
            var desired=new Vector3(scale,scale,1);
            if(referenceContent.localScale!=desired)referenceContent.localScale=desired;
        }
        private void RefreshReadonlyLayout()
        {
            if(readonlyScroll==null||readonlyScroll.viewport==null||readonlyScroll.content==null||readonlyDetails==null)return;
            var viewport=readonlyScroll.viewport;var body=readonlyScroll.content.GetComponent<Text>();if(body==null)return;
            var parentSize=((RectTransform)readonlyDetails.transform).rect.size;float pixelsPerUnit=body.pixelsPerUnit;
            if(viewport.rect.size==readonlyViewportSize&&parentSize==readonlyParentSize&&body.text==readonlyBodyValue&&Mathf.Approximately(pixelsPerUnit,readonlyPixelsPerUnit)){readonlyScrollPosition=readonlyScroll.verticalNormalizedPosition;return;}
            float position=readonlyViewportSize==Vector2.zero?1:readonlyScrollPosition;
            // 縮めたrectを上限に使わず、元のanchorと親から最大の読み面を復元する。
            float maximum=Mathf.Max(1,parentSize.y*(viewport.anchorMax.y-viewport.anchorMin.y)+viewport.offsetMax.y);
            float needed=body.cachedTextGeneratorForLayout.GetPreferredHeight(body.text,body.GetGenerationSettings(new Vector2(viewport.rect.width,0)))/pixelsPerUnit+20;
            float height=Mathf.Clamp(needed,Mathf.Min(120,maximum),maximum);
            viewport.offsetMin=new Vector2(viewport.offsetMin.x,maximum-height);
            var size=viewport.rect.size;readonlyViewportSize=size;readonlyParentSize=parentSize;readonlyBodyValue=body.text;readonlyPixelsPerUnit=pixelsPerUnit;
            body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Max(size.y,needed));
            if(readonlyDetails.transform.Find("CollectionReadonlyReading") is RectTransform reading)reading.sizeDelta=new Vector2(-160,size.y+40);
            readonlyScroll.verticalNormalizedPosition=Mathf.Clamp01(position);readonlyScrollPosition=readonlyScroll.verticalNormalizedPosition;
        }
        private void FitRecipeReading()
        {
            if(content==null||content.name!="RecipeResultDetails")return;
            var body=content.transform.Find("RecipePerformance")?.GetComponent<Text>();var reading=content.transform.Find("RecipeDetailsReading") as RectTransform;var notice=content.transform.Find("RecipeKnowledgeNotice") as RectTransform;
            if(body==null||reading==null||notice==null)return;
            float height=Mathf.Clamp(body.preferredHeight+20,120,500);
            body.rectTransform.sizeDelta=new Vector2(body.rectTransform.sizeDelta.x,height);body.rectTransform.anchoredPosition=new Vector2(body.rectTransform.anchoredPosition.x,350-height/2);
            reading.sizeDelta=new Vector2(1760,height+90);reading.anchoredPosition=new Vector2(0,365-(height+90)/2);
            notice.anchoredPosition=new Vector2(0,220-height);
        }
        // 上端の既存補助ボタンの間へ文字領域だけを収め、FHDの中央位置は保つ。
        private void FitToolbarTitle()
        {
            if(content==null)return;bool departure=content.name=="DepartureContents"||content.name=="DepartureDiscardContents";
            if((!departure&&content.name!="SavedDeckContents")||content.transform.Find("ConfirmDeleteDeck")!=null)return;
            var title=content.transform.Find(departure?"DepartureTitle":"SavedDeckTitle")?.GetComponent<UnityEngine.UI.Text>();if(title==null)return;
            var root=(RectTransform)content.transform;var rect=title.rectTransform;
            float left=root.rect.xMin+110,right=root.rect.xMax-110,top=root.rect.yMax-50,bottom=root.rect.yMax-120;
            foreach(var button in menuExtras?.Invoke()??System.Array.Empty<Button>()){
                if(button==null||!button.gameObject.activeInHierarchy)continue;
                var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                var points=corners.Select(root.InverseTransformPoint).ToArray();
                float minX=points.Min(v=>v.x),maxX=points.Max(v=>v.x);
                if(points.Min(v=>v.y)>=top||points.Max(v=>v.y)<=bottom)continue;
                if((minX+maxX)/2<0)left=Mathf.Max(left,maxX+16);else right=Mathf.Min(right,minX-16);
            }
            if(right<=left)return;
            float width=departure?right-left:Mathf.Min(title.preferredWidth+32,right-left);
            if(departure){title.resizeTextForBestFit=true;title.resizeTextMinSize=20;title.resizeTextMaxSize=36;}
            var size=new Vector2(departure?width:width-root.rect.width,departure?60:70);var position=new Vector2(Mathf.Clamp(0,left+width/2,right-width/2),departure?455:-85);
            if(rect.sizeDelta!=size)rect.sizeDelta=size;if(rect.anchoredPosition!=position)rect.anchoredPosition=position;
        }
        // 帰還の補助操作は既存位置に残し、実座標が重なる面だけを避ける。
        private void FitReturnPanels()
        {
            if(content==null||content.name!="ReturnContents")return;
            var stats=content.transform.Find("HomeStats")?.GetComponent<Text>();if(stats==null)return;
            var root=(RectTransform)content.transform;
            void Fit(string name,float y,float height,Text label,float inset)
            {
                var face=content.transform.Find(name) as RectTransform;if(face==null)return;
                float left=-880,right=880;
                foreach(var button in menuExtras?.Invoke()??System.Array.Empty<Button>()){
                    if(button==null||!button.gameObject.activeInHierarchy)continue;
                    var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);var points=corners.Select(root.InverseTransformPoint).ToArray();
                    if(points.Min(v=>v.y)>=y+height/2||points.Max(v=>v.y)<=y-height/2)continue;
                    float min=points.Min(v=>v.x),max=points.Max(v=>v.x);if((min+max)/2<0)left=Mathf.Max(left,max+16);else right=Mathf.Min(right,min-16);
                }
                if(right-left<=inset)return;
                face.anchoredPosition=new Vector2((left+right)/2,y);face.sizeDelta=new Vector2(right-left,height);
                label.rectTransform.anchoredPosition=new Vector2((left+right)/2,label.rectTransform.anchoredPosition.y);label.rectTransform.sizeDelta=new Vector2(right-left-inset,label.rectTransform.sizeDelta.y);
            }
            Fit("ReturnHeader",450,120,content.transform.Find("Title").GetComponent<Text>(),80);
            Fit("ReturnSummary",270,110,stats,140);
        }
        private void LateUpdate(){FitReferenceLayout();FitToolbarTitle();FitReturnPanels();FitRecipeReading();RefreshReadonlyLayout();menu?.Tick();}
        private void OnDisable(){SaveMenuFocus();menu?.Suspend();}
        private void OnDestroy(){menu?.Dispose();}
        private void OpenCardDetails(Card card,string returnFocus)
        {
            if(card==null)return;var value=CollectionDisplay.PerformanceText(card);
            OpenReadonlyText("カードの詳細",value,returnFocus,body=>Olca.Presentation.CustomScreen.CardAttackPropertyIcons.Bind(body,card,Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value)));
        }
        private void OpenReadonlyText(string titleText,string value,string returnFocus,Action<UnityEngine.UI.Text> decorate=null)
        {
            if(content==null||readonlyDetails!=null)return;var parent=content;var back=menuBackSource;
            readonlyDetails=new GameObject("ReadonlyCardDetails",typeof(RectTransform),typeof(Image));readonlyDetails.transform.SetParent(transform,false);var rect=(RectTransform)readonlyDetails.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            // 読取中の背面一覧を操作面に見せず、前景の水鏡素材はそのまま保つ。
            var backdrop=UiLook.Surface;backdrop.a=1;readonlyDetails.GetComponent<Image>().color=backdrop;var modal=readonlyDetails;
            content=modal;
            // 図鑑の読取面だけを一覧と同じ水鏡素材へ揃え、背面の文字との競合を抑える。
            bool collectionReading=parent.name=="CardCollectionContents"||parent.name=="RecipeResultDetails";
            if(collectionReading){
                var header=CommerceFace("CollectionReadonlyHeader",0,0,1,1);var hr=header.rectTransform;hr.anchorMin=new Vector2(0,1);hr.anchorMax=Vector2.one;hr.sizeDelta=new Vector2(-180,110);hr.anchoredPosition=new Vector2(0,-90);
                var reading=CommerceFace("CollectionReadonlyReading",0,0,1,1);var rr=reading.rectTransform;rr.anchorMin=new Vector2(0,1);rr.anchorMax=Vector2.one;rr.pivot=new Vector2(.5f,1);rr.anchoredPosition=new Vector2(0,-160);rr.sizeDelta=new Vector2(-160,1);
            }
            var title=Text("ReadonlyCardTitle",titleText,0,450,1700,70,36);
            title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=Vector2.one;title.rectTransform.sizeDelta=new Vector2(-220,70);title.rectTransform.anchoredPosition=new Vector2(0,-90);
            var area=new GameObject("ReadonlyCardScroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));area.transform.SetParent(modal.transform,false);var ar=(RectTransform)area.transform;ar.anchorMin=new Vector2(.08f,.2f);ar.anchorMax=new Vector2(.92f,collectionReading?1:.86f);ar.offsetMin=Vector2.zero;ar.offsetMax=collectionReading?new Vector2(0,-180):Vector2.zero;area.GetComponent<Image>().color=new Color(0,0,0,.02f);
            var body=Text("ReadonlyCardPerformance",value,0,0,1500,600,30);body.supportRichText=false;body.alignment=TextAnchor.UpperLeft;body.transform.SetParent(area.transform,false);body.rectTransform.anchorMin=new Vector2(0,1);body.rectTransform.anchorMax=Vector2.one;body.rectTransform.pivot=new Vector2(.5f,1);body.rectTransform.anchoredPosition=Vector2.zero;body.rectTransform.sizeDelta=Vector2.zero;
            decorate?.Invoke(body);
            readonlyScroll=area.GetComponent<ScrollRect>();readonlyScroll.viewport=ar;readonlyScroll.content=body.rectTransform;readonlyScroll.horizontal=false;readonlyScroll.movementType=ScrollRect.MovementType.Clamped;
            Action close=()=>{if(this==null||modal==null||parent==null||readonlyDetails!=modal)return;modal.SetActive(false);if(Application.isPlaying)Destroy(modal);else DestroyImmediate(modal);readonlyDetails=null;readonlyScroll=null;SetMenu(parent.transform,back,returnFocus,true);};
            var closeButton=Button("CloseReadonlyCardDetails","戻る / East・Esc",0,-440,850,75,close);
            var closeRect=(RectTransform)closeButton.transform;closeRect.anchorMin=closeRect.anchorMax=new Vector2(.5f,0);closeRect.anchoredPosition=new Vector2(0,100);content=parent;Canvas.ForceUpdateCanvases();readonlyViewportSize=Vector2.zero;readonlyScrollPosition=1;RefreshReadonlyLayout();SetMenu(modal.transform,close,"CloseReadonlyCardDetails",true,readonlyScroll);
        }
        private void CollectionContent(string name)
        {
            StopMenu();
            if(content!=null){content.SetActive(false);if(Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject(name,typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);UseReferenceLayout();
        }
        private void CollectionFocus(string name,bool force=true)=>SetMenu(content.transform,collectionBackAction,name,force,resetMissing:true);
        private void CollectionIcon(string name,Card card,float x,float y,float size,Func<Card,Sprite> icon)
        {
            var sprite=icon?.Invoke(card);if(sprite==null)return;
            var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(content.transform,false);
            var image=obj.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;
            var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(size,size);
        }
        public void RenderCollection(string[] cards,CollectionRecipeRow[] recipes,bool pending,string error,Action close,Action retrySave,Func<Card,Sprite> icon=null,string focus=null,IReadOnlyList<Card> cardDetails=null)
        {
            bool updating=isActiveAndEnabled&&content!=null&&content.activeInHierarchy&&content.name=="CardCollectionContents";
            StopMenu();
            CollectionContent("CardCollectionContents");collectionBackAction=pending?null:close;
            CommerceFace("CollectionHeader",0,405,1760,170);
            Text("CollectionTitle","カード図鑑",0,455,1500,70,36);
            var status=Text("CollectionStatus",error??"永続入手で登録。融合の公開履歴はカード入手と別に保存します。",0,360,1700,80,24);
            AddTrailingTextDetails(status,"CollectionStatusDetails","ズカンのメッセージ");
            Button("CardCollectionTab","カード",-420,265,750,60,()=>{recipesTab=false;collectionPage=0;RenderCollection(cards,recipes,pending,error,close,retrySave,icon,"CardCollectionTab",cardDetails);});
            Button("RecipeCollectionTab","融合レシピ",420,265,750,60,()=>{recipesTab=true;collectionPage=0;RenderCollection(cards,recipes,pending,error,close,retrySave,icon,"RecipeCollectionTab",cardDetails);});
            int count=recipesTab?recipes.Length:cards.Length;const int perPage=4;int pages=Math.Max(1,(count+perPage-1)/perPage);collectionPage=Math.Min(collectionPage,pages-1);
            // 選択カテゴリは移動中のフォーカスと分離し、ページ内の行は同じ左端で読む。
            var tabMark=new GameObject("CollectionActiveTab",typeof(RectTransform),typeof(Image));tabMark.transform.SetParent(content.transform,false);var mark=tabMark.GetComponent<Image>();mark.color=UiLook.Edge;mark.raycastTarget=false;var mr=(RectTransform)tabMark.transform;mr.anchorMin=mr.anchorMax=new Vector2(.5f,.5f);mr.anchoredPosition=new Vector2(recipesTab?420:-420,228);mr.sizeDelta=new Vector2(730,4);
            int shown=Math.Min(perPage,Math.Max(0,count-collectionPage*perPage));float top=shown==0?165:215,bottom=shown==0?-65:155-(shown-1)*110-60;CommerceFace("CollectionReading",0,(top+bottom)/2,1760,top-bottom);
            if(count==0)Text("CollectionEmpty",recipesTab?"レシピはありません。":"カードはありません。",0,45,1500,160,30);
            void RowText(string value,int row,int index,float y){
                var label=Text("CollectionRow_"+row,value,-100,y,1300,100,25);
                label.alignment=TextAnchor.MiddleLeft;
                AddTrailingTextDetails(label,"CollectionRow_"+index+"Details","ズカンのジョウホウ");
            }
            for(int n=0;n<perPage&&collectionPage*perPage+n<count;n++){
                int index=collectionPage*perPage+n;float y=155-n*110;
                if(!recipesTab){RowText(cards[index],n,index,y);if(cardDetails!=null&&index<cardDetails.Count&&cardDetails[index]!=null){var known=cardDetails[index];CollectionIcon("CollectionCardIcon_"+index,known,-805,y,70,icon);Button("CollectionCardDetails_"+index,"詳細",740,y,260,70,()=>OpenCardDetails(known,"CollectionCardDetails_"+index));}continue;}
                var row=recipes[index];var result=row.Result;
                RowText(row.Text,n,index,y);
                if(result!=null){
                    CollectionIcon("RecipeResultIcon_"+index,result,-805,y,70,icon);
                    Button("RecipeDetails_"+index,"結果の性能",740,y,260,70,()=>{
                        CollectionContent("RecipeResultDetails");
                        CommerceFace("RecipeDetailsHeader",0,435,1760,130);CommerceFace("RecipeDetailsReading",0,70,1760,590);
                        Text("RecipeDetailsTitle","融合結果の性能",0,430,1500,80,36);
                        CollectionIcon("RecipeDetailIcon",result,-785,435,80,icon);
                        var performance=Text("RecipePerformance",row.Performance,0,100,1600,500,30);
                        performance.alignment=TextAnchor.UpperLeft;
                        AddTrailingTextDetails(performance,"RecipePerformanceDetails","レシピのセイノウ");
                        Text("RecipeKnowledgeNotice","レシピの公開情報です。カード本体の入手済み登録とは別です。",0,-250,1650,80,24);FitRecipeReading();
                        Action back=()=>RenderCollection(cards,recipes,pending,error,close,retrySave,icon,"RecipeDetails_"+index,cardDetails);
                        collectionBackAction=back;Button("CloseRecipeDetails","レシピへ戻る（Esc）",0,-430,850,75,back);CollectionFocus("CloseRecipeDetails");
                    });
                }
            }
            void ChangeCollectionPage(int direction){
                int next=Math.Max(0,Math.Min(pages-1,collectionPage+direction));if(next==collectionPage)return;
                collectionPage=next;string preferred=direction>0?(next+1<pages?"NextCollectionPage":"PreviousCollectionPage"):(next>0?"PreviousCollectionPage":"NextCollectionPage");
                RenderCollection(cards,recipes,pending,error,close,retrySave,icon,preferred,cardDetails);
            }
            Button("PreviousCollectionPage","前へ",-430,-330,500,60,()=>ChangeCollectionPage(-1)).interactable=collectionPage>0;
            Button("NextCollectionPage","次へ",430,-330,500,60,()=>ChangeCollectionPage(1)).interactable=collectionPage+1<pages;
            Text("CollectionPage",$"{collectionPage+1}/{pages}",0,-330,220,60,24);
            Button("CloseCardCollection","戻る",0,-440,850,75,close).interactable=!pending;
            if(pending||error!=null)Button("RetryCardCollection","図鑑の保存を再試行",0,-510,850,50,retrySave);
            CollectionFocus(focus??(recipesTab?"RecipeCollectionTab":"CardCollectionTab"),force:focus!=null||!updating);
        }
        // 商品/帰還の一画面だけに適用し、他画面はStopMenuで既存面へ戻す。
        private void CommerceSurface(){var image=GetComponent<Image>();if(image!=null)image.color=new Color(.025f,.075f,.10f,.985f);}
        private Image CommerceFace(string name,float x,float y,float width,float height,bool dark=false)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(content.transform,false);var image=obj.GetComponent<Image>();image.sprite=Resources.Load<Sprite>(dark?"OlcaUI/description":"OlcaUI/window");image.type=Image.Type.Sliced;image.color=Color.white;image.raycastTarget=false;var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);return image;
        }
        public void Render(ClearReturnProfile profile, ClearReturnData flow, bool blocked, string error, bool collectionOnly = false, string dungeonName = null)
        {
            StopMenu();
            collectionBackAction=null;
            if (lastRun != flow?.RunId || lastStage != flow?.Stage) page = 0;
            lastRun = flow?.RunId; lastStage = flow?.Stage;
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("ReturnContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            UseReferenceLayout();
            gameObject.SetActive(true); profile = profile ?? new ClearReturnProfile();
            float homeFooterY=-420,homeAuxY=-493,homePageY=-340;
            CommerceSurface();CommerceFace("ReturnHeader",0,450,1760,120,true);CommerceFace("ReturnBody",0,-30,1760,700);
            var heading=Text("Title", blocked && flow != null && flow.Outcome != RunReturnOutcome.Cleared ? "敗北・撤退の帰還を保存できません" : collectionOnly ? "所持カード・成長" : flow == null ? "進行データの確認" : flow.Stage == ClearReturnStage.Choosing ? "持ち帰りカードを選択" :
                flow.Stage == ClearReturnStage.Awarded ? "持ち帰り・経験値を反映しました" :
                flow.Outcome == RunReturnOutcome.Defeated ? "敗北して帰還しました" : flow.Outcome == RunReturnOutcome.Retreated ? "撤退して帰還しました" : "帰還しました", 0, 455, 1680, 80, 36);heading.color=Color.white;
            var status=Text("ReturnStatus",error??"",0,355,1680,60,24);status.color=Color.white;AddTrailingTextDetails(status,"ReturnStatusDetails","ホゾンのメッセージ");
            if (flow == null && !collectionOnly) {
                Text("ReturnHelp", "進行データを確認できるまで、既存の保存を変更せず停止します。", 0, 100, 1700, 120, 28);
            } else if (!collectionOnly && flow.Stage == ClearReturnStage.Choosing) {
                Text("SelectionRules", $"最大3枚・同じカード1枚まで・★★★不可　選択 {flow.Selected.Length}/3\n選ばなかったカードは失います。経験値予定 +{flow.ExperienceAward}", 0, 260, 1740, 110, 25);
                int pages = Math.Max(1, (flow.Candidates.Length + PageSize - 1) / PageSize); page = Math.Min(page, pages - 1);
                for (int row = 0; row < PageSize && page * PageSize + row < flow.Candidates.Length; row++) {
                    int index = page * PageSize + row; var card = flow.Candidates[index];
                    bool selected = flow.Selected.Contains(index);
                    string level = new string('★', (int)card.Level);
                    var b = Button("CarryCard_" + index, (selected ? "［選択中］ " : "［選択］ ") + card.Name + level +
                        (card.Level == ExceedLevel.Lv3 ? "（持ち帰り不可）" : ""), 0, 180 - row * 59, 1450, 52, () => toggle(index));
                    b.interactable = !blocked && card.Level != ExceedLevel.Lv3;
                }
                if (flow.Candidates.Length == 0) Text("NoCarryCards", "持ち帰れる獲得カードはありません。0枚で確定できます。", 0, 100, 1700, 90, 28);
                Pages(profile, flow, blocked, error, pages);
                Button("ConfirmCarryHome", $"{flow.Selected.Length}枚で確定・経験値反映", 0, -420, 900, 75, confirm).interactable = !blocked;
            } else if (!collectionOnly && flow.Stage == ClearReturnStage.Awarded) {
                var experience=Text("AwardSummary",$"経験値 +{flow.ExperienceAward}（反映済み）\nLv {profile.Level} / 総経験値 {profile.Experience}",-425,235,760,145,30);experience.lineSpacing=1.25f;
                var tokens=Text("AwardTokens",$"超過変換 +{flow.TokenAward} トークン\n所持トークン {profile.Tokens}",425,235,760,145,30);tokens.lineSpacing=1.25f;
                var carryHeading=Text("AwardCardsHeading","持ち帰り確定",0,130,1600,44,24);carryHeading.alignment=TextAnchor.MiddleLeft;
                int rows=Mathf.Max(1,flow.Selected.Length);
                if(flow.Selected.Length==0)Text("AwardCards","0枚",0,60,1600,64,30).alignment=TextAnchor.MiddleLeft;
                for(int row=0;row<flow.Selected.Length;row++){
                    var candidate=flow.Candidates[flow.Selected[row]];float y=60-row*80;
                    var label=Text(row==0?"AwardCards":"AwardCard_"+row,candidate.Name+new string('★',(int)candidate.Level),50,y,1500,64,30);label.alignment=TextAnchor.MiddleLeft;label.resizeTextForBestFit=true;label.resizeTextMinSize=22;label.resizeTextMaxSize=30;
                    var owned=new OwnedCardCount{Id=candidate.Id,Name=candidate.Name,Rarity=candidate.Rarity,Level=candidate.Level,DefinitionHash=candidate.ValueHash,Count=1};var card=resolveOwned?.Invoke(owned);var sprite=card==null?null:resolveReturnIcon?.Invoke(card);
                    if(sprite!=null)CollectionIcon("ReturnCardIcon_"+row,card,-780,y,64,_=>sprite);else Text("ReturnCardIndex_"+row,(row+1).ToString(),-780,y,64,64,24);
                }
                float statsY=-115-(rows-1)*80;var body=(RectTransform)content.transform.Find("ReturnBody");float bodyHeight=Mathf.Min(700,540+(rows-1)*80);body.sizeDelta=new Vector2(1760,bodyHeight);body.anchoredPosition=new Vector2(0,320-bodyHeight/2);
                var stats=Text("ReturnStats",$"帰還後：HP {flow.ReturnedMaxHp}/{flow.ReturnedMaxHp} / 攻撃 {flow.ReturnedAttack}\n帰還を確定すると、攻略中の状態を解除して全回復します。",0,statsY,1640,120,26);stats.lineSpacing=1.2f;
                Button("ConfirmReturn","帰還を確定",0,Mathf.Max(-420,-280-(rows-1)*80),900,75,finish).interactable=!blocked;
            } else {
                if(profile.Owned.Length<=4){
                    float bottom=profile.Owned.Length==0?10:120-(profile.Owned.Length-1)*56-60;
                    var body=(RectTransform)content.transform.Find("ReturnBody");body.sizeDelta=new Vector2(1760,320-bottom);body.anchoredPosition=new Vector2(0,(320+bottom)/2);
                    homePageY=bottom-55;homeFooterY=homePageY-100;homeAuxY=homeFooterY-85;
                }
                var listBody=(RectTransform)content.transform.Find("ReturnBody");float listBottom=listBody.anchoredPosition.y-listBody.sizeDelta.y/2;listBody.sizeDelta=new Vector2(1760,185-listBottom);listBody.anchoredPosition=new Vector2(0,(185+listBottom)/2);
                CommerceFace("ReturnSummary",0,270,1760,110);
                var homeStats=Text("HomeStats", collectionOnly ? $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\n所持カードは攻略中に消費・減少しません。" : $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\nHP {profile.ReturnedHp}/{flow.ReturnedMaxHp}　攻撃 {flow.ReturnedAttack}{(flow.Outcome == RunReturnOutcome.Cleared ? "\nカード・経験値は保存済みです。" : "")}", 0, 270, 1620, 90, 26);homeStats.alignment=TextAnchor.MiddleLeft;
                int pages = Math.Max(1, (profile.Owned.Length + PageSize - 1) / PageSize); page = Math.Min(page, pages - 1);
                Text("OwnedTitle","永続所持カード",-135,155,1150,32,24).alignment=TextAnchor.MiddleLeft;Text("OwnedQuantityHeading","マイスウ",565,155,170,32,22);
                for (int row = 0; row < PageSize && page * PageSize + row < profile.Owned.Length; row++) {
                    var card = profile.Owned[page * PageSize + row];
                    float y=120-row*56;int index=page*PageSize+row;var detail=resolveOwned?.Invoke(card);
                    var name=Text("OwnedCard_"+row,OwnedDefinition.DisplayName(card),-135,y,1150,50,28);name.alignment=TextAnchor.MiddleLeft;name.resizeTextForBestFit=true;name.resizeTextMinSize=20;name.resizeTextMaxSize=28;
                    Text("OwnedCardCount_"+row,$"{card.Count}/4枚",565,y,170,50,26);
                    var sprite=detail==null?null:resolveReturnIcon?.Invoke(detail);if(sprite!=null)CollectionIcon("OwnedCardIcon_"+row,detail,-800,y,44,_=>sprite);else Text("OwnedCardIndex_"+row,(index+1).ToString(),-800,y,44,50,22);
                    var divider=new GameObject("OwnedRowDivider_"+row,typeof(RectTransform),typeof(Image));divider.transform.SetParent(content.transform,false);var line=(RectTransform)divider.transform;line.anchorMin=line.anchorMax=new Vector2(.5f,.5f);line.anchoredPosition=new Vector2(0,y-28);line.sizeDelta=new Vector2(1640,1);divider.GetComponent<Image>().color=new Color(.17f,.5f,.58f,.2f);divider.GetComponent<Image>().raycastTarget=false;
                    Button("OwnedCardDetails_"+index,"詳細",780,y,180,44,()=>OpenCardDetails(detail,"OwnedCardDetails_"+index)).interactable=detail!=null&&!blocked;
                }
                if (profile.Owned.Length == 0) Text("NoOwnedCards", "永続所持カードはまだありません。", 0, 80, 1500, 80, 28);
                Pages(profile, flow, blocked, error, pages, collectionOnly,dungeonName,homePageY,profile.Owned.Length<=4);
                if (collectionOnly) Button("CloseOwnedCards", "戻る", 0, homeFooterY, 900, 75, closeCollection);
                else {
                    if (editOwnedDeparture != null) Button("EditOwnedDeparture", "所持カードで出撃デッキを編成", -440, homeFooterY, 820, 75, editOwnedDeparture).interactable = !blocked;
                    if (depart != null) Button("NextDeparture", (dungeonName==null?"":dungeonName+"へ\n")+(profile.EquippedDeckId==null?"既存デッキで再出撃":"装備デッキで再出撃"), 440, homeFooterY, 820, 75, depart).interactable = !blocked;
                }
            }
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&savedDeckMenu!=null&&!blocked&&error==null)Button("SavedDeckMenu","保存デッキ・装備",-645,homeAuxY,410,58,savedDeckMenu);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&selectDungeon!=null&&!blocked&&error==null)Button("SelectDungeon","ダンジョンを選択",645,homeAuxY,410,58,selectDungeon);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&selectStyle!=null&&!blocked&&error==null)Button("SelectDepartureStyle","出撃スタイル",-215,homeAuxY,410,58,selectStyle);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&openExchange!=null&&!blocked&&error==null)Button("OpenTokenExchange","トークン交換所",215,homeAuxY,410,58,openExchange);
            if (blocked || error != null) Button("RetryClearReturn", "保存・読込を再試行", 0, homeAuxY, 800, 58, retry);
                    SetMenu(content.transform,collectionOnly?closeCollection:null,collectionOnly?"CloseOwnedCards":"SavedDeckMenu");
        }
        public static string FormatDungeonInformation(DungeonDeparturePreview info)
        {
            if(info==null)throw new ArgumentNullException(nameof(info));
            string Element(ElementType value) => value==ElementType.None?"ムゾクセイ":value==ElementType.Fire?"ヒ":value==ElementType.Water?"ミズ":value==ElementType.Wind?"カゼ":value==ElementType.Earth?"ツチ":"？？？";
            string Terrain(PanelType value) => value==PanelType.Normal?"ノーマル":value==PanelType.Grass?"クサムラ":value==PanelType.Ice?"コオリ":value==PanelType.Magma?"マグマ":value==PanelType.Cracked?"ヒビ":value==PanelType.Hole?"アナ":"？？？";
            return "テキゾクセイ: "+(info.EnemyElements.Count==0?"情報なし":string.Join(" / ",System.Linq.Enumerable.Select(info.EnemyElements,Element)))+
                "　チケイ: "+(info.TerrainTypes.Count==0?"情報なし":string.Join(" / ",System.Linq.Enumerable.Select(info.TerrainTypes,Terrain)));
        }
        private int dungeonPage;
        // 一覧行の文字だけを枠の内側へ収め、ボタン外形と入力領域を変えない。
        private static void InsetChoiceLabel(Button button)
        {
            var label=button.GetComponentInChildren<UnityEngine.UI.Text>();
            label.rectTransform.sizeDelta=((RectTransform)button.transform).sizeDelta-new Vector2(64,12);
        }

        private static bool NeedsTextDetails(UnityEngine.UI.Text label)
        {
            if(label.font==null||string.IsNullOrEmpty(label.text))return false;
            using(var generator=new TextGenerator()){
                generator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size));
                return generator.characterCountVisible<label.text.TrimEnd('\r','\n').Length;
            }
        }
        private Button TextDetailsButton(string name,string value,string title,float x,float y,float width,float height)
        {
            var owner=content;
            return Button(name,"ゼンブン",x,y,width,height,()=>{
                if(this==null||owner==null||content!=owner||!owner.activeInHierarchy||readonlyDetails!=null)return;
                OpenReadonlyText(title,value,name);
            });
        }
        // 固定配置の長文だけ、左端を保って右側に既存の全文入口を置く。
        private void AddTrailingTextDetails(UnityEngine.UI.Text label,string name,string title)
        {
            if(!NeedsTextDetails(label))return;
            var rect=label.rectTransform;var center=(Vector2)content.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            float right=center.x+rect.rect.width/2;
            rect.sizeDelta-=new Vector2(180,0);rect.anchoredPosition-=new Vector2(90,0);
            TextDetailsButton(name,label.text,title,right-70,center.y,140,50);
        }
        // 短文は従来表示のまま、収まらない説明だけ既存の読取専用詳細へつなぐ。
        private void AddOverflowTextDetails(UnityEngine.UI.Text label,string title,bool reserveAuxiliary=false)
        {
            if(string.IsNullOrEmpty(label.text))return;
            var rect=label.rectTransform;var position=(Vector2)content.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));float left=position.x-rect.rect.width/2;
            if(!reserveAuxiliary&&!NeedsTextDetails(label)&&label.preferredWidth<=rect.rect.width-800)return;
            // 長文は右上の補助操作を避け、全文入口も左側の空き領域へ置く。
            rect.sizeDelta-=new Vector2(400,0);rect.anchoredPosition-=new Vector2(200,0);
            if(!NeedsTextDetails(label))return;
            var details=TextDetailsButton(label.name+"Details",label.text,title,left+70,position.y,140,60);
            // 上端伸張の見出しでも画面比率に追従し、左側の全文入口を維持する。
            var rootRect=(RectTransform)content.transform;var detailsRect=(RectTransform)details.transform;
            detailsRect.anchorMin=detailsRect.anchorMax=new Vector2(0,1);
            detailsRect.anchoredPosition=new Vector2(left-rootRect.rect.xMin+70,position.y-rootRect.rect.yMax);
            rect.sizeDelta-=new Vector2(180,0);rect.anchoredPosition+=new Vector2(90,0);
        }
        private void AddOverflowConditionDetails(Button row)
        {
            var label=row.GetComponentInChildren<UnityEngine.UI.Text>();if(!NeedsTextDetails(label))return;
            var details=TextDetailsButton(row.name+"Details",label.text,"カイホウジョウケン",0,0,140,50);
            details.transform.SetParent(row.transform,false);((RectTransform)details.transform).anchoredPosition=new Vector2(((RectTransform)row.transform).rect.width/2-102,0);
            label.rectTransform.sizeDelta-=new Vector2(180,0);label.rectTransform.anchoredPosition-=new Vector2(90,0);
        }

        public void RenderDungeonSelection(string[] names, bool[] unlocked, int selected, Action<int> choose,
            Action confirmDeparture, Action cancelSelection, bool blocked, string error, Action retrySave, Action chooseStyle = null, string styleName = null, Action openTokenExchange = null,
            bool[] cleared = null, string[] unlockConditions = null, string[] descriptions = null, bool startImmediately = true)
        {
            if(names==null||unlocked==null||names.Length!=unlocked.Length||
                (cleared!=null&&cleared.Length!=names.Length)||(unlockConditions!=null&&unlockConditions.Length!=names.Length)||(descriptions!=null&&descriptions.Length!=names.Length))
                throw new ArgumentException("ダンジョン一覧の表示情報が一致しません");
            StopMenu();
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("DungeonSelectionContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            UseReferenceLayout();
            gameObject.SetActive(true);CommerceSurface();
            var heading=Text("DungeonSelectionTitle","シュツゲキサキをエラぶ",-245,390,1250,60,36);heading.color=Color.white;heading.alignment=TextAnchor.MiddleLeft;
            const int rows = 5; int pages = Math.Max(1, (names.Length + rows - 1) / rows); dungeonPage = Math.Min(dungeonPage, pages - 1);
            int shown=Math.Min(rows,Math.Max(0,names.Length-dungeonPage*rows));float bottom=shown>0?208-(shown-1)*95-69:120;bool preview=shown<=3;
            float actionY=Mathf.Max(-400,bottom-(preview?(pages>1?285:245):150)),auxiliaryY=actionY-93,pageY=actionY+115;
            bool valid=selected>=0&&selected<names.Length&&unlocked[selected];
            string info = selected>=0&&selected<names.Length&&unlocked[selected]&&descriptions!=null ? descriptions[selected] : null;
            var help=Text("DungeonSelectionHelp",error??("スタイル: "+styleName+(!preview&&!string.IsNullOrEmpty(info)?"\n"+info:"")),-245,305,1250,80,25);help.color=Color.white;help.alignment=TextAnchor.MiddleLeft;AddOverflowTextDetails(help,error==null?"シュツゲキのセツメイ":"ホゾンエラー");
            for (int row = 0; row < rows && dungeonPage * rows + row < names.Length; row++) {
                int index = dungeonPage * rows + row;
                string label = unlocked[index]
                    ? (selected==index ? "選択中  " : "")+names[index]+(cleared==null?"":cleared[index]?"　クリア済み":"　ミクリア")
                    : "［ミカイホウ］ ？？？" + (unlockConditions==null||string.IsNullOrEmpty(unlockConditions[index])?"":"\nカイホウ: "+unlockConditions[index]);
                var button = Button("DungeonChoice_" + index, label, 0, 208 - row * 95, 1450, 78, () => choose(index));
                InsetChoiceLabel(button);var rowLabel=button.GetComponentInChildren<UnityEngine.UI.Text>();rowLabel.supportRichText=false;rowLabel.alignment=TextAnchor.MiddleLeft;
                if(!unlocked[index])AddOverflowConditionDetails(button);
                button.interactable = !blocked && unlocked[index];
                if (selected == index){button.GetComponent<Image>().color = UiLook.Chosen;CustomizationMenuElements.SelectionMarker(button.transform,-710,58);}
            }
            if(names.Length==0)Text("NoDepartureDungeons","トウロクされたイきサキはありません。",0,180,1600,100,30);
            // 行き先名は選択行だけに置き、必要な地形/敵情報を静かな説明へまとめる。
            if(preview){var detail=Text("DungeonPreviewInformation",valid?(info??"トウロクされたツイカジョウホウはありません。"):"ミカイホウのイきサキはジョウケンをカクニンしてください。",0,bottom-72,1450,100,24);detail.color=new Color(.78f,.9f,.95f,1);detail.alignment=TextAnchor.MiddleLeft;detail.supportRichText=false;AddOverflowTextDetails(detail,"イキサキのジョウホウ");}
            if(pages>1){
            Button("PreviousDungeonPage", "前へ", -380, pageY, 260, 60, () => { dungeonPage--; RenderDungeonSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave, chooseStyle, styleName, openTokenExchange, cleared, unlockConditions, descriptions, startImmediately); }).interactable = !blocked && dungeonPage > 0;
            Button("NextDungeonPage", "次へ", 380, pageY, 260, 60, () => { dungeonPage++; RenderDungeonSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave, chooseStyle, styleName, openTokenExchange, cleared, unlockConditions, descriptions, startImmediately); }).interactable = !blocked && dungeonPage + 1 < pages;
            Text("DungeonPage", $"{dungeonPage+1}/{pages}", 0, pageY, 250, 60, 24).color=Color.white;
            }
            Button("ConfirmDungeonSelection", startImmediately?"このイきサキで出撃":"イきサキを確定", -430, actionY, 800, 75, confirmDeparture).interactable = !blocked && selected >= 0 && selected < unlocked.Length && unlocked[selected];
            var cancel=Button("CancelDungeonSelection", "変更せず戻る", 430, actionY, 800, 75, cancelSelection);cancel.interactable=!blocked;QuietDepartureAction(cancel);
            if (chooseStyle != null && !blocked && error == null) QuietDepartureAction(Button("SelectDepartureStyle", "スタイルを変更", -430, auxiliaryY, 480, 58, chooseStyle));
            if (openTokenExchange != null && !blocked && error == null) QuietDepartureAction(Button("OpenTokenExchange", "トークン交換所", 430, auxiliaryY, 480, 58, openTokenExchange));
            if (error != null || blocked) Button("RetryDungeonDeparture", "保存・出撃を再試行", 0, auxiliaryY, 850, 58, retrySave);
                    SetMenu(content.transform,blocked?null:cancelSelection,blocked?"RetryDungeonDeparture":"DungeonChoice_"+selected,blocked);
        }
        private static void QuietDepartureAction(Button button)
        {
            button.GetComponent<Image>().color=new Color(.35f,.55f,.65f,.75f);
            button.GetComponentInChildren<UnityEngine.UI.Text>().color=new Color(.82f,.93f,.98f,1f);
        }
        public void RenderDungeonVisit(string title,string[] names,bool[] enabled,int selected,bool blocked,bool canConfirm,
            Action<int> choose,Action confirmVisit,Action cancelVisit,Action exitVisit,Action pauseVisit,bool canExit=true,
            IReadOnlyList<DungeonVisitOption> details=null,IReadOnlyList<bool> purchased=null,long? balance=null,Func<Card,Sprite> icon=null)
        {
            StopMenu();
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("DungeonVisitContents",typeof(RectTransform));content.transform.SetParent(transform,false);UseReferenceLayout();gameObject.SetActive(true);CommerceSurface();
            CommerceFace("VisitHeader",-140,430,1480,150,true);
            var titleLabel=Text("VisitTitle",title,-140,balance.HasValue?455:430,1400,66,36);titleLabel.color=Color.white;
            if(balance.HasValue){var value=Text("VisitBalance",$"所持 {balance.Value} 通貨",-140,397,1360,44,26);value.color=Color.white;}
            Button("PauseVisit","ポーズ",780,480,260,60,pauseVisit);
            float rowHeight=Mathf.Min(176,580f/Mathf.Max(1,Mathf.Min(5,names.Length)));float bodyHeight=Mathf.Min(5,names.Length)*rowHeight+48,bodyBottom=310-bodyHeight;
            CommerceFace("VisitBody",0,310-bodyHeight/2,1760,bodyHeight);
            var listScroll=names.Length>5?CreateRowScroll("VisitOptionsScroll",1700,580,names.Length*rowHeight,-4):null;
            for(int i=0;i<names.Length;i++){
                int firstChild=content.transform.childCount;int slot=i;float y=286-rowHeight/2-i*rowHeight;var option=details!=null&&i<details.Count?details[i]:null;
                var button=Button("VisitOption_"+i,option==null?(selected==i?"［選択中］ ":"")+names[i]:"",0,y,1660,rowHeight-16,()=>choose(slot));button.interactable=!blocked&&enabled[i];
                if(option!=null){
                    var card=option.Card;bool descriptionVisible=card!=null;float nameHeight=descriptionVisible?Mathf.Min(64,Mathf.Max(44,rowHeight*.38f)):Mathf.Max(40,rowHeight-32);float descriptionHeight=Mathf.Min(52,Mathf.Max(26,rowHeight-40-nameHeight-6));float labelY=descriptionVisible?rowHeight/2-20-nameHeight/2:0;float descriptionY=-rowHeight/2+20+descriptionHeight/2;
                    var label=button.GetComponentInChildren<UnityEngine.UI.Text>();label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(option.Label);label.alignment=TextAnchor.MiddleLeft;label.fontSize=30;label.resizeTextMaxSize=30;label.rectTransform.sizeDelta=new Vector2(980,nameHeight);label.rectTransform.anchoredPosition=new Vector2(-150,labelY);
                    var price=Text("VisitPrice_"+i,$"{option.Price} 通貨",590,y+labelY,400,nameHeight,30);price.alignment=TextAnchor.MiddleRight;price.resizeTextForBestFit=true;price.resizeTextMinSize=20;price.resizeTextMaxSize=30;
                    string state=purchased!=null&&i<purchased.Count&&purchased[i]?"購入済み":selected==i?"選択中":"";var stateLabel=Text("VisitState_"+i,state,590,y+descriptionY,400,32,22);stateLabel.alignment=TextAnchor.MiddleRight;
                    if(card!=null){if(descriptionVisible){var description=Text("VisitDescription_"+i,card.Description,-150,y+descriptionY,980,descriptionHeight,22);description.alignment=TextAnchor.MiddleLeft;description.resizeTextForBestFit=true;description.resizeTextMinSize=20;description.resizeTextMaxSize=22;}var sprite=icon?.Invoke(card);if(sprite!=null)CollectionIcon("VisitCardIcon_"+i,card,-740,y,Mathf.Min(90,rowHeight-32),_=>sprite);}
                }
                if(listScroll!=null)foreach(var child in content.transform.Cast<Transform>().Skip(firstChild).ToArray()){var rect=(RectTransform)child;var position=rect.anchoredPosition;child.SetParent(listScroll.content,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.anchoredPosition=position-new Vector2(0,286);}
            }
            float footer=Mathf.Max(-440,bodyBottom-210);
            var help=Text("VisitHelp",blocked?"退出保存を完了するまで操作できません。保存状態の再試行を使ってください。":"選択/取消では効果なし。確定した取引と結果は退出時に保存します。",0,bodyBottom-50,1700,60,24);help.color=Color.white;
            Button("ConfirmVisit","選択を確定",-560,footer,520,72,confirmVisit).interactable=!blocked&&canConfirm;
            Button("CancelVisit","選択取消",0,footer,520,72,cancelVisit).interactable=!blocked&&selected>=0;
            Button("ExitVisit","退出して保存",560,footer,520,72,exitVisit).interactable=!blocked&&canExit;
            SetMenu(content.transform,blocked?null:selected>=0?cancelVisit:canExit?exitVisit:null,selected>=0?"VisitOption_"+selected:"VisitOption_0",scroll:listScroll,directionScroll:false);
        }

        private ScrollRect CreateRowScroll(string name,float width,float height,float contentHeight,float y)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));obj.transform.SetParent(content.transform,false);var area=(RectTransform)obj.transform;area.anchorMin=area.anchorMax=new Vector2(.5f,.5f);area.anchoredPosition=new Vector2(0,y);area.sizeDelta=new Vector2(width,height);obj.GetComponent<Image>().color=Color.clear;
            var body=new GameObject("Rows",typeof(RectTransform));body.transform.SetParent(area,false);var rows=(RectTransform)body.transform;rows.anchorMin=new Vector2(0,1);rows.anchorMax=Vector2.one;rows.pivot=new Vector2(.5f,1);rows.anchoredPosition=Vector2.zero;rows.sizeDelta=new Vector2(0,contentHeight);
            var scroll=obj.GetComponent<ScrollRect>();scroll.viewport=area;scroll.content=rows;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;
            var barObject=new GameObject("Scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar));barObject.transform.SetParent(area,false);var barRect=(RectTransform)barObject.transform;barRect.anchorMin=barRect.anchorMax=new Vector2(1,.5f);barRect.pivot=new Vector2(1,.5f);barRect.sizeDelta=new Vector2(8,height);barRect.anchoredPosition=Vector2.zero;barObject.GetComponent<Image>().color=new Color(.05f,.2f,.25f,.15f);
            var handleObject=new GameObject("Handle",typeof(RectTransform),typeof(Image));handleObject.transform.SetParent(barRect,false);var handle=(RectTransform)handleObject.transform;handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;handleObject.GetComponent<Image>().color=new Color(.05f,.25f,.3f,.65f);var bar=barObject.GetComponent<Scrollbar>();bar.direction=Scrollbar.Direction.BottomToTop;bar.handleRect=handle;bar.targetGraphic=handleObject.GetComponent<Image>();bar.navigation=new Navigation{mode=Navigation.Mode.None};scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;return scroll;
        }

        // 16:9での余白を保ち、狭い横幅や横長画面でも操作部を画面端から離す。
        private static Button AnchorVisitButton(Button button,Vector2 anchor,Vector2 position)
        {
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position;return button;
        }
        private static void InsetVisitRow(RectTransform rect,float margin)
        {
            rect.anchorMin=new Vector2(0,rect.anchorMin.y);rect.anchorMax=new Vector2(1,rect.anchorMax.y);
            rect.sizeDelta=new Vector2(-2*margin,rect.sizeDelta.y);rect.anchoredPosition=new Vector2(0,rect.anchoredPosition.y);
        }

        private int removalPage;
        public void RenderVisitRemoval(string title,string[] names,bool[] selected,int needed,long price,bool blocked,bool canConfirm,
            Action<int> choose,Action confirm,Action cancel,Action pause,IReadOnlyList<Card> cards=null,IReadOnlyList<bool> obtained=null,Func<Card,Sprite> icon=null)
        {
            StopMenu();if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("VisitRemovalContents",typeof(RectTransform));content.transform.SetParent(transform,false);UseReferenceLayout();gameObject.SetActive(true);CommerceSurface();CommerceFace("RemovalHeader",-140,450,1480,120,true);var body=CommerceFace("RemovalBody",0,-4,1760,628);
            Text("RemovalTitle",title,-140,450,1400,80,32).color=Color.white;Button("PauseVisit","ポーズ",780,480,260,60,pause);
            var summary=Text("RemovalSummary",$"削除対象 {System.Array.FindAll(selected,x=>x).Length}/{needed}枚 / 支払 {price} 通貨",0,350,1720,56,26);summary.color=Color.white;
            const int rows=6;int pages=System.Math.Max(1,(names.Length+rows-1)/rows);removalPage=System.Math.Min(removalPage,pages-1);
            // ページ操作は固定し、少数行では明るい面だけを最終行へ合わせる。
            int visibleRows=Mathf.Min(rows,names.Length-removalPage*rows);if(visibleRows<rows){float height=Mathf.Max(1,visibleRows)*96+36;body.rectTransform.sizeDelta=new Vector2(1760,height);body.rectTransform.anchoredPosition=new Vector2(0,310-height/2);}
            for(int row=0;row<rows&&removalPage*rows+row<names.Length;row++){
                int index=removalPage*rows+row;float y=254-row*96;var card=cards!=null&&index<cards.Count?cards[index]:null;
                var button=Button("RemovalCard_"+index,card==null?(selected[index]?"［削除対象］ ":"")+names[index]:"",0,y,1660,88,()=>choose(index));button.interactable=!blocked;
                if(card!=null){var label=button.GetComponentInChildren<UnityEngine.UI.Text>();label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(card.Name+new string('★',(int)card.ExceedLevel));label.alignment=TextAnchor.MiddleLeft;label.fontSize=28;label.resizeTextMaxSize=28;label.rectTransform.anchoredPosition=new Vector2(-120,14);label.rectTransform.sizeDelta=new Vector2(980,40);
                    var provenance=Text("RemovalOrigin_"+index,obtained!=null&&index<obtained.Count&&obtained[index]?"攻略中取得・持帰り履歴は保持":"持込・永続所持は保持",-120,y-22,980,24,20);provenance.alignment=TextAnchor.MiddleLeft;
                    Text("RemovalCost_"+index,$"コスト {card.GetFinalCost()}",620,y+14,330,28,22).alignment=TextAnchor.MiddleRight;Text("RemovalPower_"+index,$"威力 {card.GetFinalPower()}",620,y-14,330,28,22).alignment=TextAnchor.MiddleRight;
                    var sprite=icon?.Invoke(card);if(sprite!=null)CollectionIcon("RemovalCardIcon_"+index,card,-790,y,56,_=>sprite);
                    var state=Text("RemovalState_"+index,selected[index]?"削除対象":"",-690,y,132,40,21);state.color=new Color(.50f,.19f,.10f,1);
                }
                if(selected[index]){var mark=new GameObject("RemovalSelected_"+index,typeof(RectTransform),typeof(Image));mark.transform.SetParent(button.transform,false);var rect=(RectTransform)mark.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(6,0);rect.sizeDelta=new Vector2(5,68);mark.GetComponent<Image>().color=new Color(.66f,.28f,.14f,1);mark.GetComponent<Image>().raycastTarget=false;}
            }
            Button("RemovalPrevious","前",-430,-345,450,48,()=>{removalPage--;RenderVisitRemoval(title,names,selected,needed,price,blocked,canConfirm,choose,confirm,cancel,pause,cards,obtained,icon);}).interactable=!blocked&&removalPage>0;
            Button("RemovalNext","次",430,-345,450,48,()=>{removalPage++;RenderVisitRemoval(title,names,selected,needed,price,blocked,canConfirm,choose,confirm,cancel,pause,cards,obtained,icon);}).interactable=!blocked&&removalPage<pages-1;Text("RemovalPage",$"{removalPage+1}/{pages}",0,-345,240,48,24).color=Color.white;
            Text("RemovalHelp","確定前は支払・削除なし。獲得済みカードの持帰り履歴は残ります。",0,-395,1740,44,22).color=Color.white;
            var commit=Button("ConfirmVisitRemoval",price+"通貨で削除を確定",-430,-470,800,72,confirm);commit.interactable=!blocked&&canConfirm;commit.GetComponentInChildren<UnityEngine.UI.Text>().color=commit.interactable?new Color(.50f,.19f,.10f,1):new Color(.29f,.36f,.38f,.85f);
            Button("CancelVisitRemoval","確定せず戻る",430,-470,800,72,cancel).interactable=!blocked;
            SetMenu(content.transform,blocked?null:cancel,"CancelVisitRemoval");
        }

        private int exchangePage;
        public void RenderTokenExchange(ClearReturnProfile profile, string selected, bool blocked, string error,
            Action<string> choose, Action buy, Action cancel, Action close, Action retrySave)
        {
            StopMenu();
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("TokenExchangeContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            UseReferenceLayout();
            gameObject.SetActive(true);CommerceSurface();CommerceFace("ExchangeHeader",-140,435,1480,145,true);CommerceFace("ExchangeFooter",0,-425,1760,245,true);
            Text("ExchangeTitle","トークン交換所",-220,465,1300,60,36).color=Color.white;
            Text("ExchangeTokens",$"所持 {profile.Tokens} トークン",-220,405,1300,50,32).color=Color.white;
            var explanation=Text("ExchangeBalance",error??"永続所持へ交換 / ランダム商品は帰還時に更新。任意の再抽選はできません。",0,345,1750,65,24);explanation.color=Color.white;AddOverflowTextDetails(explanation,error==null?"コウカンのセツメイ":"ホゾンエラー");
            var offers=profile.ExchangeStock?.Offers??Array.Empty<ExchangeOfferData>();const int rows=5;int pages=Math.Max(1,(offers.Length+rows-1)/rows);exchangePage=Math.Min(exchangePage,pages-1);
            int shown=Math.Min(rows,Math.Max(0,offers.Length-exchangePage*rows));float bottom=shown>0?205-(shown-1)*90-70:50;CommerceFace("ExchangeBody",0,(310+bottom)/2,1760,310-bottom);
            Text("ExchangeCardColumn","カード",-290,280,850,30,22).alignment=TextAnchor.MiddleLeft;Text("ExchangePriceColumn","カカク",330,280,260,30,22).alignment=TextAnchor.MiddleRight;Text("ExchangeStateColumn","ショジ / セイゲン",650,280,350,30,22).alignment=TextAnchor.MiddleRight;
            for(int row=0;row<rows&&exchangePage*rows+row<offers.Length;row++){
                var item=offers[exchangePage*rows+row];var ownedMatches=profile.Owned.Where(c=>c.Id==item.CardId&&c.Level==item.Level&&c.Name==item.Name&&c.Rarity==item.Rarity&&(item.CardDefinitionHash==null||c.DefinitionHash==item.CardDefinitionHash)).ToArray();int owned=ownedMatches.Length==1?ownedMatches[0].Count:0;
                long total=(profile.ExchangeTotals??Array.Empty<ExchangePurchaseTotal>()).SingleOrDefault(t=>t.ProductId==item.Id)?.Count??0;
                bool sold=item.Limit!=ExchangeLimit.Unlimited&&(item.Limit==ExchangeLimit.PerRefresh?item.Purchased:total)>=item.Maximum;
                string limit=item.Limit==ExchangeLimit.Unlimited?"無制限":item.Limit==ExchangeLimit.PerRefresh?$"更新まで {item.Purchased}/{item.Maximum}":$"通算 {total}/{item.Maximum}";
                float y=205-row*90;string state=selected==item.Id?"［選択中］ ":sold?"［売切れ］ ":"";
                var b=Button("ExchangeOffer_"+item.Id,state+CustomizationMenuElements.ShortName(item.Name,36)+new string('★',(int)item.Level),0,y,1730,76,()=>choose(item.Id));
                var name=b.GetComponentInChildren<UnityEngine.UI.Text>();name.supportRichText=false;name.alignment=TextAnchor.MiddleLeft;name.rectTransform.anchoredPosition=new Vector2(-290,0);name.rectTransform.sizeDelta=new Vector2(850,64);name.fontSize=28;name.resizeTextMaxSize=28;
                string digits=item.Price.ToString();bool widePrice=digits.Length>12;var price=Text("ExchangeOfferPrice_"+item.Id,widePrice?digits+"\nトークン":digits+" トークン",widePrice?310:330,y,widePrice?300:260,52,30);price.alignment=TextAnchor.MiddleRight;price.resizeTextForBestFit=true;price.resizeTextMinSize=20;price.resizeTextMaxSize=30;
                Text("ExchangeOfferOwned_"+item.Id,(item.Random?"ランダム":"固定")+" / 所持 "+owned+"/4",650,y+14,350,24,22).alignment=TextAnchor.MiddleRight;
                Text("ExchangeOfferLimit_"+item.Id,limit,650,y-14,350,24,22).alignment=TextAnchor.MiddleRight;
                // 保存済みの識別子/定義hashを既存の定義解決へ渡す。別の同名カードへ代替しない。
                var card=resolveOwned?.Invoke(new OwnedCardCount{Id=item.CardId,Name=item.Name,Rarity=item.Rarity,Level=item.Level,DefinitionHash=item.CardDefinitionHash,Count=1});var sprite=card==null?null:resolveReturnIcon?.Invoke(card);if(sprite!=null)CollectionIcon("ExchangeCardIcon_"+item.Id,card,-790,y,60,_=>sprite);
                b.interactable=!blocked&&!sold;if(selected==item.Id){b.GetComponent<Image>().color=UiLook.Chosen;CustomizationMenuElements.SelectionMarker(b.transform,-850,56);}

            }
            Button("PreviousExchangePage","前へ",-380,-270,260,60,()=>{exchangePage--;RenderTokenExchange(profile,selected,blocked,error,choose,buy,cancel,close,retrySave);}).interactable=!blocked&&exchangePage>0;
            Button("NextExchangePage","次へ",380,-270,260,60,()=>{exchangePage++;RenderTokenExchange(profile,selected,blocked,error,choose,buy,cancel,close,retrySave);}).interactable=!blocked&&exchangePage+1<pages;
            Text("ExchangePage",$"{exchangePage+1}/{pages}",0,-270,250,60,24).color=Color.white;
            if(offers.Length==0)Text("ExchangeEmpty","交換商品はありません。",0,120,1500,130,30);
            var choice=offers.SingleOrDefault(o=>o.Id==selected);
            string help=choice==null?(offers.Length==0?"交換商品はありません。拠点へ戻ってください。":"商品を選んで確認してください。"):$"{choice.Price} トークンを支払い1枚獲得。"+(profile.Owned.Any(c=>c.Id==choice.CardId&&c.Level==choice.Level&&c.Count==4)?$"所持上限のため{choice.OverflowTokens}トークンへ変換。":!ClearReturnSession.CanExchange(profile,choice)?"トークン不足または売切れ。":"");
            var confirmation=Text("ExchangeConfirmation",choice==null?help:choice.Name+"\n"+help,0,-355,1750,75,24);confirmation.color=Color.white;confirmation.supportRichText=false;AddOverflowTextDetails(confirmation,"コウカンナイヨウ");
            Button("ConfirmTokenExchange","交換を確定",-560,-440,520,72,buy).interactable=!blocked&&ClearReturnSession.CanExchange(profile,choice);
            Button("CancelTokenExchange","選択取消",0,-440,520,72,cancel).interactable=!blocked&&selected!=null;
            Button("CloseTokenExchange","拠点へ戻る",560,-440,520,72,close).interactable=!blocked;
            if(error!=null||blocked)Button("RetryTokenExchange","保存を再試行",0,-510,800,50,retrySave);
            SetMenu(content.transform,blocked?null:selected!=null?cancel:close,blocked?"RetryTokenExchange":selected==null?"CloseTokenExchange":"ExchangeOffer_"+selected,blocked);
        }

        public static string FormatDepartureStyleChoice(DepartureStyleDefinition value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));var element=value.Attack?.Element??ElementType.None;
            string attribute=element==ElementType.None?"ムゾクセイ":element==ElementType.Fire?"ヒ":element==ElementType.Water?"ミズ":element==ElementType.Wind?"カゼ":element==ElementType.Earth?"ツチ":"？？？";
            return value.Name+(value.Id==DepartureStyleDefinition.BasicId?"":" / ゾクセイ "+attribute)+(value.RequiredClear==null?"":" / カイホウ: "+value.RequiredClear+"クリア");
        }
        private int stylePage;
        public void RenderStyleSelection(string[] names, bool[] unlocked, int selected, Action<int> choose,
            Action confirmDeparture, Action cancelSelection, bool blocked, string error, Action retrySave, int current = -1)
        {
            if(names==null||unlocked==null||names.Length!=unlocked.Length)throw new ArgumentException("スタイル一覧の表示情報が一致しません");
            StopMenu();
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("StyleSelectionContents",typeof(RectTransform));content.transform.SetParent(transform,false);UseReferenceLayout();gameObject.SetActive(true);CommerceSurface();CommerceFace("StyleHeader",-140,440,1480,145,true);CommerceFace("StyleFooter",0,-420,1760,230,true);
            Text("StyleSelectionTitle","出撃スタイル",-220,465,1300,60,36).color=Color.white;
            var help=Text("StyleSelectionHelp",error??"攻略中は固定。保存しても出撃は開始しません。",-220,397,1300,44,24);help.color=Color.white;if(error!=null||NeedsTextDetails(help))AddOverflowTextDetails(help,error==null?"スタイルのセツメイ":"ホゾンエラー");
            bool hasCurrent=current>=0&&current<names.Length,hasCandidate=selected>=0&&selected<names.Length;
            string currentName=hasCurrent?(names[current]??"ミトウロク"):"ミトウロク",candidateName=hasCandidate?(names[selected]??"ミトウロク"):"コウホなし";
            const int rows=5;int pages=Math.Max(1,(names.Length+rows-1)/rows);stylePage=Math.Min(stylePage,pages-1);int shown=Math.Min(rows,Math.Max(0,names.Length-stylePage*rows));float bottom=shown>0?240-(shown-1)*95-69:120;float comparisonHeight=Mathf.Min(190,bottom+189);bool hasComparison=comparisonHeight>=75,same=hasCurrent&&hasCandidate&&current==selected;int shortLimit=comparisonHeight<130?26:36;
            bool shortened=CustomizationMenuElements.ShortName(currentName,shortLimit)!=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(currentName)||CustomizationMenuElements.ShortName(candidateName,shortLimit)!=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(candidateName);
            if(!hasComparison||shortened){CommerceFace("StyleSummaryHeader",0,330,1760,60,true);var summary=Text("StyleSelectionSummary",same?"現在とコウホは同じ: "+currentName:"現在: "+currentName+" / コウホ: "+candidateName,0,330,1700,44,23);summary.color=Color.white;summary.supportRichText=false;AddOverflowTextDetails(summary,"ゲンザイとコウホ");}
            CommerceFace("StyleBody",0,(295+bottom)/2,1760,295-bottom);
            for(int row=0;row<rows&&stylePage*rows+row<names.Length;row++){
                int index=stylePage*rows+row;string state=!unlocked[index]?"［未解放］ ":current==index&&selected==index?"［現在・コウホ］ ":current==index?"［現在］ ":selected==index?"［コウホ］ ":"［選択］ ";
                var button=Button("StyleChoice_"+index,state+names[index],0,240-row*95,1450,78,()=>choose(index));InsetChoiceLabel(button);button.GetComponentInChildren<UnityEngine.UI.Text>().supportRichText=false;
                if(!unlocked[index])AddOverflowConditionDetails(button);button.interactable=!blocked&&unlocked[index];if(selected==index){button.GetComponent<Image>().color=UiLook.Chosen;CustomizationMenuElements.SelectionMarker(button.transform,-710,58);}
            }
            if(names.Length==0)Text("NoDepartureStyles","登録されたスタイルはありません。",0,205,1600,100,30);
            // 少数コウホの余白は保存済み/変更コウホの比較に使う。密なページでは上部の同じ情報を保持する。
            if(hasComparison){
                float y=(bottom-26-215)/2,top=y+comparisonHeight/2,low=y-comparisonHeight/2;
                for(int role=0;role<2;role++){
                    float x=role==0?-440:440;bool candidate=role==1;CommerceFace(candidate?"StyleCandidatePanel":"StyleCurrentPanel",x,y,800,comparisonHeight,candidate);
                    var label=Text(candidate?"StyleCandidateRole":"StyleCurrentRole",candidate?"コウホ / "+(!hasCandidate?"なし":!unlocked[selected]?"未解放":same?"現在と同じ":"ミホゾン"):"現在 / "+(hasCurrent?"保存済み":"未登録"),x,top-(comparisonHeight>=130?26:18),740,30,comparisonHeight>=130?26:22);if(candidate)label.color=Color.white;
                    string valueText=candidate&&same?"変更なし":CustomizationMenuElements.ShortName(candidate?candidateName:currentName,shortLimit);var value=Text(candidate?"StyleCandidateValue":"StyleCurrentValue",valueText,x,comparisonHeight>=130?y:low+24,740,comparisonHeight>=130?70:30,comparisonHeight>=130?28:23);value.supportRichText=false;if(candidate)value.color=Color.white;
                    if(comparisonHeight>=160){var note=Text(candidate?"StyleCandidateNote":"StyleCurrentNote",candidate?(!hasCandidate?"変更せず戻れます":!unlocked[selected]?"解放条件を確認してください":"確定するまで変更しません"):(hasCurrent?"保存された出撃スタイル":"登録情報を確認してください"),x,low+26,740,28,22);if(candidate)note.color=Color.white;}
                }
            }
            Button("PreviousStylePage","前へ",-380,-285,260,60,()=>{stylePage--;RenderStyleSelection(names,unlocked,selected,choose,confirmDeparture,cancelSelection,blocked,error,retrySave,current);}).interactable=!blocked&&stylePage>0;
            Button("NextStylePage","次へ",380,-285,260,60,()=>{stylePage++;RenderStyleSelection(names,unlocked,selected,choose,confirmDeparture,cancelSelection,blocked,error,retrySave,current);}).interactable=!blocked&&stylePage+1<pages;
            Text("StylePage",$"{stylePage+1}/{pages}",0,-285,250,60,24).color=Color.white;
            if(!blocked&&error==null)Text("StyleEffectHelp","属性と固有・タメ攻撃が変わります。",0,-477,1700,40,22).color=Color.white;
            Button("ConfirmStyleSelection","スタイルを保存",-430,-400,800,75,confirmDeparture).interactable=!blocked&&hasCandidate&&unlocked[selected];Button("CancelStyleSelection","変更せず戻る",430,-400,800,75,cancelSelection).interactable=!blocked;
            if(error!=null||blocked)Button("RetryStyleDeparture","スタイル保存を再試行",0,-493,850,58,retrySave);
            SetMenu(content.transform,blocked?null:cancelSelection,blocked?"RetryStyleDeparture":"StyleChoice_"+selected,blocked);
        }

        public void RenderOwnedDeparture(ClearReturnProfile profile, Card[] options, IReadOnlyList<Card> draft,
            string error, bool valid, Action<int, int> change, Action confirm, Action<bool> close, Action resume, bool confirmDiscard, Action save = null, string deckName = null, Action<string> rename = null, bool pending = false, Action retrySave = null, IReadOnlyList<string> draftKeys=null, string persistedDeckName=null)
        {
            StopMenu();
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject(confirmDiscard?"DepartureDiscardContents":"DepartureContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            UseReferenceLayout();
            gameObject.SetActive(true);
            var owner=content;
            Action Live(Action action,bool allowPending=false)=>()=>{if(this!=null&&owner!=null&&content==owner&&owner.activeInHierarchy&&(allowPending||!pending)&&readonlyDetails==null)action?.Invoke();};
            Text("DepartureTitle", "所持から出撃デッキを編成", 0, 455, 1750, 60, 36);
            if (confirmDiscard) {
                Text("DiscardDepartureHelp", "未確定の編成を破棄して帰還画面へ戻りますか？", 0, 100, 1700, 130, 30);
                Button("KeepEditingDeparture", "編集に戻る", -420, -150, 750, 80, Live(resume,true));
                Button("DiscardDepartureDraft", "変更を破棄", 420, -150, 750, 80, Live(() => close(true),true)); SetMenu(content.transform,Live(resume,true),"KeepEditingDeparture",true);return;
            }
            Button nameSaveButton=null;Func<bool> nameCanSave=()=>true;Action refreshNameState=null;
            if(save!=null){var inputObj=new GameObject("SavedDeckName",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(SavedDeckNameInputField));inputObj.transform.SetParent(content.transform,false);
                var ir=(RectTransform)inputObj.transform;ir.anchorMin=ir.anchorMax=new Vector2(.5f,.5f);ir.anchoredPosition=new Vector2(-150,400);ir.sizeDelta=new Vector2(1000,48);inputObj.GetComponent<Image>().color=UiLook.ButtonFace;
                var label=Text("SavedDeckNameText",deckName??"",0,0,980,46,25);label.transform.SetParent(inputObj.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;label.supportRichText=false;
                var input=inputObj.GetComponent<InputField>();input.textComponent=label;input.lineType=InputField.LineType.SingleLine;input.text=deckName??"";input.interactable=!pending;
                var hint=Text("SavedDeckNameLimit","16モジまで",635,400,430,48,22);var nameOwner=content;string accepted=input.text,initialName=deckName??"",draftName=initialName,focusName=initialName;
                ((SavedDeckNameInputField)input).FocusStarted=()=>focusName=draftName;
                nameCanSave=()=>CanUseDeckName(draftName)||(persistedDeckName!=null&&string.Equals(draftName,persistedDeckName,StringComparison.Ordinal));
                refreshNameState=()=>{if(nameSaveButton!=null)nameSaveButton.interactable=!pending&&nameCanSave();hint.text=persistedDeckName!=null&&string.Equals(draftName,persistedDeckName,StringComparison.Ordinal)&&!CanUseDeckName(draftName)?"キュウメイはそのままホジ":nameCanSave()?"16モジまで":"16モジまでにヘンコウ";};
                bool applyingName=false;
                void ApplyName(string value,bool ending){
                    if(applyingName||this==null||nameOwner==null||content!=nameOwner||!nameOwner.activeInHierarchy||pending||HasMenuModal)return;
                    applyingName=true;try{
                    if(ending&&input.wasCanceled){accepted=value;draftName=focusName;input.SetTextWithoutNotify(value);rename?.Invoke(focusName);refreshNameState();return;}
                    bool restoringLegacy=persistedDeckName!=null&&string.Equals(value,persistedDeckName,StringComparison.Ordinal);
                    bool reducingLegacy=persistedDeckName!=null&&!CanUseDeckName(accepted)&&SavedDeckNameRules.IsSingleLine(value)&&SavedDeckNameRules.Count(value)<SavedDeckNameRules.Count(accepted);
                    if(value!=accepted&&!string.IsNullOrEmpty(value)&&!CanUseDeckName(value)&&!reducingLegacy&&!restoringLegacy){input.SetTextWithoutNotify(accepted);hint.text="16モジまで";return;}
                    if(value!=accepted){input.SetTextWithoutNotify(value);accepted=input.text;draftName=restoringLegacy?persistedDeckName:value;rename?.Invoke(draftName);}if(ending)focusName=draftName;refreshNameState();
                    }finally{applyingName=false;}
                }
                input.onValueChanged.AddListener(value=>ApplyName(value,false));input.onEndEdit.AddListener(value=>ApplyName(value,true));
            }
            // 編集中の枚数と候補一覧を分け、数量は操作の隣で読めるようにする。
            CommerceFace("DepartureSummary",0,325,1760,96);
            Text("DepartureDraftCount",$"編成 {draft.Count} / 20枚",-650,325,410,74,34);
            Text("DepartureRules", "所持枚数は消費しません。\nスタンダード同名4・リミテッド同名1/計5・アルティメット同名1/計1", 220, 325, 1270, 84, 23);
            CommerceFace("DepartureCandidates",0,-35,1760,500);
            Text("DepartureCandidateHeading","所持カード",-650,190,410,26,22);
            Text("DepartureCountHeading","編成",515,190,140,26,22);
            Text("DepartureOwnedHeading","所持",775,190,140,26,22);
            AddOverflowTextDetails(Text("DepartureValidation", error ?? "この20枚で出撃できます。", 0, 235, 1750, 60, 25),"ヘンセイのカクニン");
            const int rows = 6; int pages = Math.Max(1, (profile.Owned.Length + rows - 1) / rows); departurePage = Math.Min(departurePage, pages - 1);
            for (int row = 0; row < rows && departurePage * rows + row < profile.Owned.Length; row++) {
                int index = departurePage * rows + row; var owned = profile.Owned[index]; var option = options[index];
                int count = draftKeys!=null?draftKeys.Count(k=>k==OwnedDefinition.Key(owned)):draft.Count(c=>OwnedDefinition.CardMatches(owned,c)); float y = 145 - row * 75;
                var fullRow=OwnedDefinition.DisplayName(owned) + (option==null?"":$"　威力 {option.GetFinalPower()}") + $"　編成 {count} / 所持 {owned.Count}" + (option == null ? "（定義未解決）" : "");
                var sprite=option==null?null:resolveReturnIcon?.Invoke(option);
                if(sprite!=null)CollectionIcon("DepartureCardIcon_"+index,option,-815,y,54,_=>sprite);
                var rowText=Text("DepartureCard_" + index,OwnedDefinition.DisplayName(owned),-320,y+13,880,34,27);
                rowText.alignment=TextAnchor.MiddleLeft;
                if(NeedsTextDetails(rowText)){
                    rowText.rectTransform.sizeDelta-=new Vector2(180,0);rowText.rectTransform.anchoredPosition-=new Vector2(90,0);
                    TextDetailsButton("DepartureRow_"+index+"Details",Olca.Presentation.CustomScreen.CustomDisplay.Katakana(fullRow),"ヘンセイのジョウホウ",35,y,140,50);
                }
                var metrics=Text("DeparturePerformance_"+index,option==null?"定義未解決":$"威力 {option.GetFinalPower()}",-410,y-22,700,26,22);metrics.alignment=TextAnchor.MiddleLeft;
                var quantity=Text("DepartureDraftQuantity_"+index,count.ToString(),515,y,64,50,28);quantity.resizeTextForBestFit=true;quantity.resizeTextMinSize=18;quantity.resizeTextMaxSize=28;
                Text("DepartureOwnedQuantity_"+index,owned.Count.ToString(),775,y,140,50,27);
                Button("DepartureDetails_"+index,"詳細",230,y,170,60,Live(()=>OpenCardDetails(option,"DepartureDetails_"+index))).interactable=option!=null&&!pending;
                Button("RemoveDepartureCard_" + index, "−", 420, y, 120, 60, Live(() => change(index, -1))).interactable = !pending && count > 0;
                Button("AddDepartureCard_" + index, "＋", 610, y, 120, 60, Live(() => change(index, 1))).interactable = !pending && option != null && (save!=null||(count < owned.Count && draft.Count < 20));
            }
            if (profile.Owned.Length == 0) Text("NoDepartureOwned", "所持カードがありません。スターターは自動付与しません。", 0, 80, 1750, 100, 28);
            Text("DeparturePage", $"{departurePage+1}/{pages}", 0, -340, 250, 50, 24);
            void ChangeDeparturePage(int direction){
                int next=Math.Max(0,Math.Min(pages-1,departurePage+direction));if(next==departurePage)return;
                departurePage=next;resume();
                if(this==null||pending||content==null||content==owner||content.name!="DepartureContents"||HasMenuModal)return;
                string preferred=direction>0?"NextDeparturePage":"PreviousDeparturePage";
                var button=content.transform.Find(preferred)?.GetComponent<Button>();
                if(button==null||!button.IsInteractable()){preferred=direction>0?"PreviousDeparturePage":"NextDeparturePage";button=content.transform.Find(preferred)?.GetComponent<Button>();}
                if(button!=null&&button.IsInteractable())SetMenu(content.transform,menuBackSource,preferred,true);
            }
            Button("PreviousDeparturePage", "前へ", -380, -340, 240, 55, Live(()=>ChangeDeparturePage(-1),true)).interactable = departurePage > 0;
            Button("NextDeparturePage", "次へ", 380, -340, 240, 55, Live(()=>ChangeDeparturePage(1),true)).interactable = departurePage+1 < pages;
            if(save==null)Button("ConfirmOwnedDeparture", "この20枚で出撃", 0, -420, 900, 75, Live(confirm,true)).interactable = valid;
            else {nameSaveButton=Button("SaveNamedDeck", "デッキを保存（装備中は20枚条件必須）",0,-420,1500,75,Live(()=>{if(nameCanSave())save?.Invoke();}));refreshNameState?.Invoke();}
            if(pending)Button("RetrySavedDeckSave","保存を再試行",0,-493,800,58,Live(retrySave,true));
            else Button("CloseOwnedDeparture", save==null?"帰還画面へ戻る":"保存デッキ一覧へ戻る", 0, -493, 800, 58, Live(() => close(false)));
            // 無効になった詳細や再試行の焦点は、変更操作でなく有効な読取／既定操作へ戻す。
            string preferred=pending?"RetrySavedDeckSave":save!=null?"SaveNamedDeck":
                content.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name.StartsWith("DepartureDetails_",StringComparison.Ordinal)&&b.IsInteractable())?.name??
                content.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name.StartsWith("DepartureRow_",StringComparison.Ordinal)&&b.name.EndsWith("Details",StringComparison.Ordinal)&&b.IsInteractable())?.name??"CloseOwnedDeparture";
            SetMenu(content.transform,pending?null:Live(()=>close(false)),preferred,force:pending,resetMissing:true);FitToolbarTitle();
        }

        // 一覧の最も狭い700幅へ、装備注記と枚数を含めて収める。raw入力は書き換えない。
        public bool CanUseDeckName(string value)
        {
            if(!SavedDeckNameRules.IsValid(value)||font==null)return false;
            var settings=new TextGenerationSettings{font=font,fontSize=25,fontStyle=FontStyle.Normal,scaleFactor=1,lineSpacing=1,
                richText=false,horizontalOverflow=HorizontalWrapMode.Overflow,verticalOverflow=VerticalWrapMode.Overflow};
            var generator=new TextGenerator();
            float Width(string text)=>generator.GetPreferredWidth(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text),settings);
            return Width("［装備中］ "+value+"　20枚")<=680&&Width(value+"　"+int.MaxValue+"枚")<=680;
        }

        private ScrollRect DeleteDeckName(string value)
        {
            var label=Text("DeleteDeckName",value??"",0,150,1700,180,30);label.supportRichText=false;
            if(!NeedsTextDetails(label))return null;
            var area=new GameObject("DeleteDeckNameScroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));area.transform.SetParent(content.transform,false);
            var rect=(RectTransform)area.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,150);rect.sizeDelta=new Vector2(1700,180);area.GetComponent<Image>().color=new Color(0,0,0,.02f);
            label.transform.SetParent(area.transform,false);label.alignment=TextAnchor.UpperCenter;label.rectTransform.anchorMin=new Vector2(0,1);label.rectTransform.anchorMax=Vector2.one;label.rectTransform.pivot=new Vector2(.5f,1);label.rectTransform.anchoredPosition=Vector2.zero;label.rectTransform.sizeDelta=Vector2.zero;label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,label.preferredHeight+20);
            var scroll=area.GetComponent<ScrollRect>();scroll.viewport=rect;scroll.content=label.rectTransform;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;return scroll;
        }

        public void RenderSavedDeckMenu(ClearReturnProfile profile,string error,bool pending,Action create,Action<string> edit,Action<string> clone,Action<string> equip,Action<string> delete,Action close,Action retry,string deleting=null,Action confirmDelete=null,Action reload=null)
        {
            StopMenu();
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("SavedDeckContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);
            var title=Text("SavedDeckTitle","保存デッキ・装備",0,455,1700,70,36);
            title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=Vector2.one;title.rectTransform.sizeDelta=new Vector2(-220,70);title.rectTransform.anchoredPosition=new Vector2(0,-85);
            var errorLabel=Text("SavedDeckError",error??"",0,365,1750,100,25);
            errorLabel.rectTransform.anchorMin=new Vector2(0,1);errorLabel.rectTransform.anchorMax=Vector2.one;errorLabel.rectTransform.sizeDelta=new Vector2(-170,100);errorLabel.rectTransform.anchoredPosition=new Vector2(0,-175);
            var decks=profile.SavedDecks??new SavedDepartureDeck[0];
            if(deleting!=null){
                UseReferenceLayout();var dialog=content;var d=decks.SingleOrDefault(x=>x.Id==deleting);var nameScroll=DeleteDeckName(d?.Name);
                Text("DeleteDeckWarning","このデッキを削除しますか？\n所持カードは減りません。",0,-25,1700,90,30);
                Action cancelDelete=()=>{if(this==null||dialog==null||content!=dialog||!dialog.activeInHierarchy)return;RenderSavedDeckMenu(profile,error,pending,create,edit,clone,equip,delete,close,retry,reload:reload);};
                Button("ConfirmDeleteDeck","削除する",-400,-150,700,75,()=>{if(this!=null&&dialog!=null&&content==dialog&&dialog.activeInHierarchy&&!pending)confirmDelete?.Invoke();}).interactable=!pending;
                Button("CancelDeleteDeck","取消し",400,-150,700,75,cancelDelete);
                // 矢印は既存の確認操作へ、PageUp/Downだけを長い名前の読取へ割り当てる。
                SetMenu(content.transform,cancelDelete,"CancelDeleteDeck",true,nameScroll,false);return;
            }
            AddOverflowTextDetails(errorLabel,"ホゾンエラー");
            if(decks.Length==0)Text("SavedDeckEmpty",pending?"保存デッキはまだありません。\n保存を再試行してください。":"保存デッキはまだありません。\n新しいデッキからつくれます。",0,80,1450,150,30);
            var help=Text("SavedDeckHelp",string.IsNullOrEmpty(error)?"非装備は不完全でも保存可。装備・出撃は20枚/所持/共通制限を確認。\n装備中の削除は未対応です。先に別のデッキを装備してください。":"",0,365,1750,100,25);
            help.rectTransform.anchorMin=new Vector2(0,1);help.rectTransform.anchorMax=Vector2.one;help.rectTransform.sizeDelta=new Vector2(-170,100);help.rectTransform.anchoredPosition=new Vector2(0,-175);
            AddOverflowTextDetails(help,"ホゾンデッキのセツメイ",true);
            const int rows=5;int pages=Math.Max(1,(decks.Length+rows-1)/rows);savedDeckPage=Math.Min(savedDeckPage,pages-1);
            for(int n=0;n<rows&&savedDeckPage*rows+n<decks.Length;n++){var d=decks[savedDeckPage*rows+n];float y=190-n*85;bool active=d.Id==profile.EquippedDeckId;
                var row=Text("SavedDeckRow_"+d.Id,(active?"［装備中］ ":"")+d.Name+"　"+d.Cards.Length+"枚",-470,y,700,70,25);row.supportRichText=false;
                if(!CanUseDeckName(d.Name)&&NeedsTextDetails(row)){
                    row.rectTransform.sizeDelta-=new Vector2(180,0);row.rectTransform.anchoredPosition-=new Vector2(90,0);
                    TextDetailsButton("SavedDeckName_"+d.Id+"Details",row.text,"デッキのナマエ",-210,y,140,50);
                }
                Button("EquipDeck_"+d.Id,"装備",30,y,160,60,()=>equip(d.Id)).interactable=!pending&&!active;
                Button("EditDeck_"+d.Id,"編集",240,y,160,60,()=>edit(d.Id)).interactable=!pending;
                Button("CloneDeck_"+d.Id,"複製",450,y,160,60,()=>clone(d.Id)).interactable=!pending;
                Button("DeleteDeck_"+d.Id,"削除",660,y,160,60,()=>delete(d.Id)).interactable=!pending&&!active;}
            Text("SavedDeckPage",$"{savedDeckPage+1}/{pages}",0,-300,300,55,24);
            var pageOwner=content;
            void ChangeSavedDeckPage(int direction){
                if(this==null||pageOwner==null||content!=pageOwner||!pageOwner.activeInHierarchy||pending||HasMenuModal)return;
                int next=Math.Max(0,Math.Min(pages-1,savedDeckPage+direction));if(next==savedDeckPage)return;
                savedDeckPage=next;RenderSavedDeckMenu(profile,error,pending,create,edit,clone,equip,delete,close,retry,reload:reload);
                // 端で無効になったページ操作からは反対側へ戻し、別の業務操作へ飛ばさない。
                string focus=direction>0&&next+1<pages||direction<0&&next==0?"NextSavedDeckPage":"PreviousSavedDeckPage";
                SetMenu(content.transform,close,focus,true);
            }
            Button("PreviousSavedDeckPage","前へ",-380,-300,240,55,()=>ChangeSavedDeckPage(-1)).interactable=!pending&&savedDeckPage>0;
            Button("NextSavedDeckPage","次へ",380,-300,240,55,()=>ChangeSavedDeckPage(1)).interactable=!pending&&savedDeckPage+1<pages;
            Button("NewSavedDeck","新しいデッキ",-450,-420,750,75,create).interactable=!pending;
            Button("CloseSavedDeckMenu","帰還画面へ戻る",450,-420,750,75,close).interactable=!pending;
            if(pending)Button("RetrySavedDeckSave","保存を再試行",0,-493,800,58,retry);
            else if(reload!=null)Button("ReloadSavedDecks","保存から再読込",0,-493,800,58,reload);
            // 基準画面の配置を保ち、横長でも下部のページ/保存操作を欠けさせない。
            foreach(var name in new[]{"SavedDeckPage","PreviousSavedDeckPage","NextSavedDeckPage","NewSavedDeck","CloseSavedDeckMenu","RetrySavedDeckSave","ReloadSavedDecks"}){
                var child=content.transform.Find(name);if(child==null)continue;var control=(RectTransform)child;
                control.anchorMin=control.anchorMax=new Vector2(.5f,0);control.anchoredPosition+=new Vector2(0,540);
            }
            SetMenu(content.transform,pending?null:close,pending?"RetrySavedDeckSave":decks.Length==0?"NewSavedDeck":"EditDeck_"+profile.EquippedDeckId,force:pending,resetMissing:decks.Length==0);FitToolbarTitle();
        }

        private void Pages(ClearReturnProfile p, ClearReturnData f, bool blocked, string error, int pages, bool collectionOnly = false,string dungeonName=null,float y=-340,bool darkBackdrop=false)
        {
            Text("ReturnPage", $"{page + 1}/{pages}", 0, y, 300, 55, 24).color=darkBackdrop?Color.white:UiLook.Ink;
            Button("ReturnPreviousPage", "前へ", -380, y, 240, 55, () => { page = Math.Max(0, page - 1); Render(p, f, blocked, error, collectionOnly,dungeonName); }).interactable = page > 0;
            Button("ReturnNextPage", "次へ", 380, y, 240, 55, () => { page = Math.Min(pages - 1, page + 1); Render(p, f, blocked, error, collectionOnly,dungeonName); }).interactable = page + 1 < pages;
        }
        private UnityEngine.UI.Text Text(string name, string value, float x, float y, float width, float height, int size)
        {
            var o = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text)); o.transform.SetParent(content.transform, false);
            var t = o.GetComponent<UnityEngine.UI.Text>(); t.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value); t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            UiLook.Label(t);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(.5f, .5f); t.rectTransform.anchoredPosition = new Vector2(x, y); t.rectTransform.sizeDelta = new Vector2(width, height); return t;
        }
        private Button Button(string name, string label, float x, float y, float width, float height, Action action)
        {
            var o = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); o.transform.SetParent(content.transform, false);
            var r = (RectTransform)o.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
            o.GetComponent<Image>().color = UiLook.ButtonFace;
            var edge = o.AddComponent<Outline>(); edge.effectColor = new Color(.5f,.9f,.93f,.6f); edge.effectDistance = new Vector2(1,-1); var b = o.GetComponent<Button>(); b.navigation = new Navigation { mode = Navigation.Mode.None }; var owner=content;b.onClick.AddListener(()=>{if(b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable()&&OwnsContent(owner))action?.Invoke();});
            var text = Text(name + "Label", label, x, y, width, height, 25); text.transform.SetParent(o.transform, false); text.rectTransform.anchoredPosition = Vector2.zero; UiLook.Button(b); return b;
        }
    }
}

namespace Olca.Presentation.Dungeon
{
    // 既存FHD Canvas上の小さな操作面。面は透過し、操作ボタン以外は入力を遮らない。
    public sealed class BasicAttackHud : UnityEngine.MonoBehaviour
    {
        private UnityEngine.UI.Text title, state;
        private UnityEngine.UI.Image surface;
        private UnityEngine.Color surfaceColor;
        private UnityEngine.UI.Button button, jumpButton, dashButton, cardButton;
        private UnityEngine.GameObject exploration;
        private BasicAttackTapButton attackPointer;
        public void Initialize(UnityEngine.Font font, System.Action tap, System.Action<bool> hold, System.Action cancel)
        {
            var rect = (UnityEngine.RectTransform)transform; rect.anchorMin = rect.anchorMax = new UnityEngine.Vector2(1,0);
            rect.pivot = new UnityEngine.Vector2(.5f,.5f); rect.sizeDelta = new UnityEngine.Vector2(500,210); rect.anchoredPosition = new UnityEngine.Vector2(-280,135);
            var face = gameObject.AddComponent<UnityEngine.UI.Image>(); UiLook.Face(face); face.raycastTarget = false;
            surface=face;surfaceColor=face.color;
            var edge = gameObject.GetComponent<UnityEngine.UI.Outline>() ?? gameObject.AddComponent<UnityEngine.UI.Outline>(); edge.effectColor = new UnityEngine.Color(.5f,.9f,.93f,.6f); edge.effectDistance = new UnityEngine.Vector2(1,-1);
            UnityEngine.UI.Text Label(string name, float x, float y, float w, float h, int size)
            {
                var o = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Text)); o.transform.SetParent(transform,false);
                var text = o.GetComponent<UnityEngine.UI.Text>(); text.font = font; text.fontSize = size; text.color = UiLook.Ink;
                text.alignment = UnityEngine.TextAnchor.MiddleCenter; text.raycastTarget = false;
                var r = text.rectTransform; r.anchorMin = r.anchorMax = new UnityEngine.Vector2(.5f,.5f); r.anchoredPosition = new UnityEngine.Vector2(x,y); r.sizeDelta = new UnityEngine.Vector2(w,h); UiLook.Label(text); return text;
            }
            title = Label("BasicAttackKey",-80,50,300,44,26);
            Label("BasicAttackHelp",-80,0,310,60,22).text = "前方一マス・敵一体\n短く押して離す";
            state = Label("BasicAttackState",0,-72,470,36,20);
            var o = new UnityEngine.GameObject("BasicAttackButton",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button)); o.transform.SetParent(transform,false);
            var r = (UnityEngine.RectTransform)o.transform; r.anchorMin = r.anchorMax = new UnityEngine.Vector2(.5f,.5f); r.anchoredPosition = new UnityEngine.Vector2(155,-5); r.sizeDelta = new UnityEngine.Vector2(155,74);
            o.GetComponent<UnityEngine.UI.Image>().color = new UnityEngine.Color(.2f,.46f,.53f,.86f); button = o.GetComponent<UnityEngine.UI.Button>(); UiLook.Button(button);
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            attackPointer = o.AddComponent<BasicAttackTapButton>(); attackPointer.InitializeHold(hold, cancel, tap);
            var label = Label("BasicAttackLabel",0,0,155,74,26); label.text = "攻撃"; label.transform.SetParent(o.transform,false); label.rectTransform.anchoredPosition = UnityEngine.Vector2.zero;
        }
        public void CancelAttackPointer() => attackPointer?.ClearPointer();
        public void InitializeExploration(UnityEngine.Font font, System.Action jump, System.Action dash, System.Action card)
        {
            exploration=new UnityEngine.GameObject("ExplorationTouchControls",typeof(UnityEngine.RectTransform));
            exploration.transform.SetParent(transform,false);
            UnityEngine.UI.Button Make(string name,string label,float x,System.Action action) {
                var o=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));o.transform.SetParent(exploration.transform,false);
                var r=(UnityEngine.RectTransform)o.transform;r.sizeDelta=new UnityEngine.Vector2(190,80);r.anchoredPosition=new UnityEngine.Vector2(x,0);
                o.GetComponent<UnityEngine.UI.Image>().color=new UnityEngine.Color(.2f,.46f,.53f,.86f);
                var b=o.GetComponent<UnityEngine.UI.Button>();UiLook.Button(b);b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};b.onClick.AddListener(()=>action());
                var t=new UnityEngine.GameObject(name+"Label",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));t.transform.SetParent(o.transform,false);
                var text=t.GetComponent<UnityEngine.UI.Text>();text.font=font;text.fontSize=26;text.text=label;UiLook.Label(text);text.color=UiLook.Ink;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.raycastTarget=false;
                var tr=text.rectTransform;tr.anchorMin=UnityEngine.Vector2.zero;tr.anchorMax=UnityEngine.Vector2.one;tr.offsetMin=tr.offsetMax=UnityEngine.Vector2.zero;return b;
            }
            jumpButton=Make("ExplorationJumpButton","ジャンプ\nPad South",-110,jump);dashButton=Make("ExplorationDashButton","ダッシュ\nPad L押込",110,dash);
            cardButton=Make("TouchCardUseButton","カード",0,card);
            cardButton.transform.SetParent(transform,false);((UnityEngine.RectTransform)cardButton.transform).anchoredPosition=new UnityEngine.Vector2(-80,-5);
            ((UnityEngine.RectTransform)cardButton.transform).sizeDelta=new UnityEngine.Vector2(190,60);
            // 説明・操作・状態を別の段に置き、2行の割当表示とボタンを重ねない。
            title.rectTransform.anchoredPosition=new UnityEngine.Vector2(0,70);title.rectTransform.sizeDelta=new UnityEngine.Vector2(470,52);title.fontSize=18;
            exploration.SetActive(false);
        }
        private void SetSurfaceSize(float width, float height)
        {
            var rect = (UnityEngine.RectTransform)transform; var size = new UnityEngine.Vector2(width, height);
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }
        public void RenderExploration(bool available,InputBindingProfile keyboardProfile=null)
        {
            // 操作ボタンの位置と当たり領域を維持し、探索では使わない説明領域だけを縮める。
            SetSurfaceSize(460, 132);if(surface!=null)surface.color=surfaceColor;
            title.gameObject.SetActive(false);state.gameObject.SetActive(false);button.gameObject.SetActive(false);
            transform.Find("BasicAttackHelp").gameObject.SetActive(false);cardButton.gameObject.SetActive(false);exploration.SetActive(true);
            string Pad(string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad","exploration",action)]);
            var profile=keyboardProfile??Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();
            string Hint(string action)=>Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Touch?"":Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad?"\n"+Pad(action):"\n"+(profile.Bindings[("keyboard","exploration",action)]??"ミワリアテ");
            jumpButton.GetComponentInChildren<UnityEngine.UI.Text>().text="ジャンプ"+Hint("JumpPrimary");dashButton.GetComponentInChildren<UnityEngine.UI.Text>().text="ダッシュ"+Hint("DashPrimary");
            jumpButton.interactable=dashButton.interactable=available;
        }
        public void Render(string key, string value, bool available, bool canUseCard=false,InputBindingProfile keyboardProfile=null)
        {
            // 戦闘の操作領域は固定したまま、背後の敵や床が読める薄い水鏡にする。
            SetSurfaceSize(500, 210);if(surface!=null){var color=surfaceColor;color.a*=.68f;surface.color=color;}
            title.gameObject.SetActive(true);state.gameObject.SetActive(true);button.gameObject.SetActive(true);
            transform.Find("BasicAttackHelp").gameObject.SetActive(false);if(exploration!=null)exploration.SetActive(false);
            if(cardButton!=null){cardButton.gameObject.SetActive(true);cardButton.interactable=canUseCard;}
            string Pad(string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad","battle",action)]);
            var profile=keyboardProfile??Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();
            string Key(string action)=>profile.Bindings[("keyboard","battle",action)]??"ミワリアテ";
            title.text=Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad?
                "コウゲキ ["+Pad("AttackPrimary")+"]  カード ["+Pad("CardPrimary")+"]\n"+Pad("CustomPrimary")+": カスタム / "+Pad("Pause")+": ポーズ":
                Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Touch?"タッチ: コウゲキ / カード / カスタム":
                "コウゲキ ["+(key??"ミワリアテ")+"]  カード ["+Key("CardPrimary")+"]\n"+Key("CustomPrimary")+": カスタム / Escape: ポーズ";
            state.text = value; button.interactable = available;
            state.color = UiLook.Ink;
        }
    }
}

namespace Olca.Presentation.Dungeon
{
    public sealed class BasicAttackTapButton : UnityEngine.MonoBehaviour,
        UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler,
        UnityEngine.EventSystems.IPointerClickHandler, UnityEngine.EventSystems.ISubmitHandler, UnityEngine.EventSystems.ICancelHandler
    {
        private System.Action fire, cancel;
        private System.Action<bool> hold;
        private System.Func<long> timestamp; private System.Func<int> limit; private long frequency;
        private bool pointer, accepted, released;
        private int pointerId; private long start;
        // 旧タップ専用呼出しとの互換。通常HUDはInitializeHoldを使う。
        public void Initialize(System.Action fire, System.Func<long> timestamp, System.Func<int> limit, long frequency = 60)
        { this.fire=fire;this.timestamp=timestamp;this.limit=limit;this.frequency=frequency; }
        public void InitializeHold(System.Action<bool> hold, System.Action cancel, System.Action submit)
        { this.hold=hold;this.cancel=cancel;fire=submit; }
        public void ClearPointer() { pointer = accepted = released = false; }
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data)
        {
            if (pointer) return;
            if (hold != null) {
                var button = GetComponent<UnityEngine.UI.Button>();
                if ((button != null && !button.IsInteractable()) || (data != null && !data.eligibleForClick)) return;
                pointer = true; released = false; pointerId = data?.pointerId ?? -1; hold(true);
            } else { pointer=true;accepted=false;start=timestamp(); }
        }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData data)
        {
            if (hold == null) { accepted=(timestamp()-start)/(double)frequency < limit()/60.0; return; }
            if (!pointer || (data != null && data.pointerId != pointerId)) return;
            if (data != null && !data.eligibleForClick) { CancelPointer(); return; }
            // クリック成立まで解放を確定しない。領域外解放/OS取消は攻撃にしない。
            released = true;
        }
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData data)
        {
            if (hold == null) return;
            if (!pointer || !released || (data != null && (data.pointerId != pointerId || !data.eligibleForClick))) return;
            ClearPointer(); hold(false);
        }
        public void Click() { if (hold != null) return; bool use=!pointer||accepted;ClearPointer();if(use)fire(); }
        public void OnSubmit(UnityEngine.EventSystems.BaseEventData data) { if (!pointer) fire?.Invoke(); }
        public void OnCancel(UnityEngine.EventSystems.BaseEventData data) => CancelPointer();
        private void CancelPointer() { bool active=pointer;ClearPointer();if(active)cancel?.Invoke(); }
        private void LateUpdate() { if (hold != null && released) CancelPointer(); }
        private void OnDisable() => CancelPointer();
    }
}

namespace Olca.Presentation.Dungeon
{
    /// <summary>仮UIの共通色。入力/寸法/ゲームの値には触れず、既存のGraphicだけを整える。</summary>
    public static class UiLook
    {
        public static readonly Color Surface = new Color(.84f,.94f,.97f,.94f);
        public static readonly Color ButtonFace = new Color(.69f,.87f,.92f,.96f);
        public static readonly Color Chosen = new Color(.34f,.75f,.81f,1f);
        public static readonly Color Ink = new Color(.055f,.17f,.22f,1f);
        public static readonly Color Edge = new Color(.17f,.63f,.71f,.75f);
        public static void Label(UnityEngine.UI.Text label)
        {
            if(label==null)return;
            if(label.GetComponent<UiKanaLabel>()==null)label.gameObject.AddComponent<UiKanaLabel>();
            // 共通の色や枠の適用で、Scene/Prefab/呼出元の指定フォントを置き換えない。
            if(label.font==null)label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color=Ink; label.raycastTarget=false;
            label.horizontalOverflow=HorizontalWrapMode.Wrap;
        }
        public static void Face(Image image)
        {
            if(image==null)return;
            var sprite=Resources.Load<Sprite>("OlcaUI/window");
            image.sprite=sprite;image.type=Image.Type.Sliced;image.color=sprite!=null?Color.white:Surface;
            var edge=image.GetComponent<Outline>()??image.gameObject.AddComponent<Outline>();
            edge.enabled=sprite==null;edge.effectColor=Edge;edge.effectDistance=new Vector2(1,-1);edge.useGraphicAlpha=true;
        }
        public static void Button(Button button)
        {
            if(button==null)return;
            var image=button.targetGraphic as Image;
            // 絵そのものがtargetGraphicのカードでは、素材を差し替えない。
            bool artwork=image!=null&&image.sprite!=null&&image.sprite.name!="UISprite"&&image.sprite.name!="window"&&!image.sprite.name.StartsWith("button-");
            if(image!=null&&!artwork){Face(image);image.color=Color.white;}
            var normal=Resources.Load<Sprite>("OlcaUI/button-normal");
            if(normal!=null&&image!=null&&!artwork){
                image.sprite=normal;image.type=Image.Type.Sliced;image.color=Color.white;
                button.transition=Selectable.Transition.SpriteSwap;
                button.spriteState=new SpriteState{highlightedSprite=Resources.Load<Sprite>("OlcaUI/button-selected"),selectedSprite=Resources.Load<Sprite>("OlcaUI/button-selected"),pressedSprite=Resources.Load<Sprite>("OlcaUI/button-pressed"),disabledSprite=Resources.Load<Sprite>("OlcaUI/button-disabled")};
            }
            var colors=button.colors;colors.normalColor=ButtonFace;
            colors.highlightedColor=new Color(.83f,.98f,1f,1f);
            colors.selectedColor=Chosen;colors.pressedColor=new Color(.46f,.72f,.79f,1f);
            colors.disabledColor=new Color(.68f,.75f,.78f,.65f);colors.colorMultiplier=1f;colors.fadeDuration=.1f;
            button.colors=colors;
            foreach(var label in button.GetComponentsInChildren<UnityEngine.UI.Text>(true)){
                Label(label);label.fontSize=Mathf.Min(label.fontSize,30);
                label.resizeTextForBestFit=true;label.resizeTextMinSize=20;label.resizeTextMaxSize=label.fontSize;
            }
        }
    }
}

namespace Olca.Presentation.Dungeon
{
    /// <summary>データやフォントを変更せず、描画するUI文字だけを既存用語のカナ表記へそろえる。</summary>
    public sealed class UiKanaLabel : UnityEngine.MonoBehaviour
    {
        private UnityEngine.UI.Text label;
        private string applied;
        private void OnEnable(){label=GetComponent<UnityEngine.UI.Text>();UnityEngine.Canvas.preWillRenderCanvases+=Apply;Apply();}
        private void OnDisable(){UnityEngine.Canvas.preWillRenderCanvases-=Apply;}
        private void Apply(){if(label==null||label.text==applied)return;applied=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(label.text);label.text=applied;}
    }
}

namespace Olca.Presentation.Dungeon
{
    /// <summary>貼付けを文字ごとに通知せず、選択範囲の結合文字を含めて一候補として検証する。</summary>
    public sealed class SavedDeckNameInputField : UnityEngine.UI.InputField
    {
        private char pendingHighSurrogate;
        public System.Action FocusStarted;
        protected override void LateUpdate(){bool focused=isFocused;base.LateUpdate();if(!focused&&isFocused){pendingHighSurrogate='\0';FocusStarted?.Invoke();}}
        private static int Boundary(string value,int position,bool ceiling)
        {
            int prior=0;
            foreach(int start in System.Globalization.StringInfo.ParseCombiningCharacters(value)){
                if(start==position)return start;
                if(start>position)return ceiling?start:prior;
                prior=start;
            }
            return ceiling?value.Length:prior;
        }
        protected override void Append(string input)
        {
            pendingHighSurrogate='\0';
            if(readOnly||!isActiveAndEnabled||!interactable||string.IsNullOrEmpty(input))return;
            int a=UnityEngine.Mathf.Min(selectionAnchorPosition,selectionFocusPosition),b=UnityEngine.Mathf.Max(selectionAnchorPosition,selectionFocusPosition);
            if(a==b)a=b=Boundary(text,a,true);else {a=Boundary(text,a,false);b=Boundary(text,b,true);}
            string proposal=text.Remove(a,b-a).Insert(a,input);
            // EditModeのInputFieldはSetText内で旧キャレットをclampしない。短い全置換でも先に安全化する。
            caretPosition=0;text=proposal;
            if(text==proposal)caretPosition=UnityEngine.Mathf.Min(a+input.Length,text.Length);
            else {selectionAnchorPosition=a;selectionFocusPosition=b;}
        }
        protected override void Append(char input)
        {
            if(char.IsHighSurrogate(input)){pendingHighSurrogate=input;return;}
            if(char.IsLowSurrogate(input)){
                if(pendingHighSurrogate!=0){var pair=new string(new[]{pendingHighSurrogate,input});pendingHighSurrogate='\0';Append(pair);}return;
            }
            pendingHighSurrogate='\0';Append(input.ToString());
        }
        public override void OnDeselect(UnityEngine.EventSystems.BaseEventData eventData){pendingHighSurrogate='\0';base.OnDeselect(eventData);}
        protected override void OnDisable(){pendingHighSurrogate='\0';base.OnDisable();}
    }
}
