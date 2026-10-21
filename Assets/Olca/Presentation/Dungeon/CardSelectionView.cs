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
        private Button skipButton;

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
            CancellationToken ct = default)
        {
            selectionTcs = new UniTaskCompletionSource<Card>();
            EnsureSkipButton();

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
                    cardItems[i].Setup(choices[i]);
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

            // キャンセル時は null を返す
            ct.Register(() => selectionTcs.TrySetResult(null));

            return await selectionTcs.Task;
        }

        private void LateUpdate()
        {
            if (containerRoot != null && containerRoot.activeInHierarchy) UpdateChoiceLayout();
        }

        private void UpdateChoiceLayout()
        {
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
            const float gap = 16f;
            // 上下はHUD用の余白を残し、カードと文字を同じ比率で縮小する。
            float availableWidth = Mathf.Max(1f, parent.rect.width - 32f - gap * (visible.Count - 1));
            float availableHeight = Mathf.Max(1f, parent.rect.height - 220f);
            float scale = Mathf.Min(1f, availableWidth / (width * visible.Count), availableHeight / height);
            for (int i = 0; i < visible.Count; i++)
            {
                var rect = visible[i];
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2((i - (visible.Count - 1) * 0.5f) * (width * scale + gap), 45f);
            }
        }

        /// <summary>
        /// カード選択画面を非表示にしてリセット
        /// </summary>
        public void Hide()
        {
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
            label.text = "受け取らずに進む";
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

        private void OnDestroy()
        {
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
        private int page;
        private string lastRun;
        private ClearReturnStage? lastStage;
        private const int PageSize = 8;
        public void Initialize(Font font, Action<int> toggle, Action confirm, Action finish, Action retry, Action depart = null, Action closeCollection = null, Action editOwnedDeparture = null, Action savedDeckMenu = null, Action selectDungeon = null, Action selectStyle = null, Action openExchange = null)
        {
            this.font = font; this.toggle = toggle; this.confirm = confirm; this.finish = finish; this.retry = retry; this.depart = depart; this.closeCollection = closeCollection; this.editOwnedDeparture = editOwnedDeparture;this.savedDeckMenu=savedDeckMenu;this.selectDungeon=selectDungeon;this.selectStyle=selectStyle;this.openExchange=openExchange;
            var rect = (RectTransform)transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.085f, 0.12f, .88f);
            var frame = gameObject.AddComponent<Outline>(); frame.effectColor = new Color(.48f,.9f,.94f,.45f); frame.effectDistance = new Vector2(1,-1); gameObject.SetActive(false);
        }
        private int collectionPage;
        private bool recipesTab;
        public void RenderCollection(string[] cards,string[] recipes,bool pending,string error,Action close,Action retrySave)
        {
            if(content!=null){content.SetActive(false);if(Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("CardCollectionContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);
            Text("CollectionTitle","カード図鑑",0,455,1500,70,36);
            Text("CollectionStatus",error??"永続入手で登録。融合の公開履歴はカード入手と別に保存します。",0,360,1700,80,24);
            Button("CardCollectionTab","カード",-420,265,750,60,()=>{recipesTab=false;collectionPage=0;RenderCollection(cards,recipes,pending,error,close,retrySave);});
            Button("RecipeCollectionTab","融合レシピ",420,265,750,60,()=>{recipesTab=true;collectionPage=0;RenderCollection(cards,recipes,pending,error,close,retrySave);});
            var rows=recipesTab?recipes:cards;const int perPage=4;int pages=Math.Max(1,(rows.Length+perPage-1)/perPage);collectionPage=Math.Min(collectionPage,pages-1);
            for(int n=0;n<perPage&&collectionPage*perPage+n<rows.Length;n++)Text("CollectionRow_"+n,rows[collectionPage*perPage+n],0,155-n*110,1740,100,25);
            Button("PreviousCollectionPage","前へ",-430,-330,500,60,()=>{collectionPage--;RenderCollection(cards,recipes,pending,error,close,retrySave);}).interactable=collectionPage>0;
            Button("NextCollectionPage","次へ",430,-330,500,60,()=>{collectionPage++;RenderCollection(cards,recipes,pending,error,close,retrySave);}).interactable=collectionPage+1<pages;
            Text("CollectionPage",$"{collectionPage+1}/{pages}",0,-330,220,60,24);
            Button("CloseCardCollection","戻る",0,-440,850,75,close).interactable=!pending;
            if(pending||error!=null)Button("RetryCardCollection","図鑑の保存を再試行",0,-515,850,55,retrySave);
        }
        public void Render(ClearReturnProfile profile, ClearReturnData flow, bool blocked, string error, bool collectionOnly = false, string dungeonName = null)
        {
            if (lastRun != flow?.RunId || lastStage != flow?.Stage) page = 0;
            lastRun = flow?.RunId; lastStage = flow?.Stage;
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("ReturnContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.SetActive(true); profile = profile ?? new ClearReturnProfile();
            Text("Title", blocked && flow != null && flow.Outcome != RunReturnOutcome.Cleared ? "敗北・撤退の帰還を保存できません" : collectionOnly ? "所持カード・成長" : flow == null ? "進行データの確認" : flow.Stage == ClearReturnStage.Choosing ? "持ち帰りカードを選択" :
                flow.Stage == ClearReturnStage.Awarded ? "持ち帰り・経験値を反映しました" :
                flow.Outcome == RunReturnOutcome.Defeated ? "敗北して帰還しました" : flow.Outcome == RunReturnOutcome.Retreated ? "撤退して帰還しました" : "帰還しました", 0, 455, 1750, 80, 36);
            Text("ReturnStatus", error ?? "", 0, 365, 1750, 80, 24);
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
                Text("AwardSummary", $"経験値 +{flow.ExperienceAward}（反映済み）　Lv {profile.Level}　総経験値 {profile.Experience}\n超過変換 +{flow.TokenAward} トークン　所持トークン {profile.Tokens}", 0, 240, 1750, 160, 30);
                string chosen = string.Join("\n", flow.Selected.Select(i => flow.Candidates[i].Name + new string('★', (int)flow.Candidates[i].Level)));
                Text("AwardCards", "持ち帰り確定\n" + (chosen.Length == 0 ? "0枚" : chosen), 0, 0, 1700, 260, 28);
                Text("ReturnStats", $"帰還後：HP {flow.ReturnedMaxHp}/{flow.ReturnedMaxHp}　攻撃 {flow.ReturnedAttack}\n帰還を確定すると、攻略中の状態を解除して全回復します。", 0, -220, 1750, 130, 26);
                Button("ConfirmReturn", "帰還を確定", 0, -420, 900, 75, finish).interactable = !blocked;
            } else {
                Text("HomeStats", collectionOnly ? $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\n所持カードは攻略中に消費・減少しません。" : $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\nHP {profile.ReturnedHp}/{flow.ReturnedMaxHp}　攻撃 {flow.ReturnedAttack}\n{(flow.Outcome == RunReturnOutcome.Cleared ? "カード・経験値は保存済みです。" : "獲得カードは失いました。経験値+0。既存の所持・成長は保持します。")}", 0, 260, 1750, 130, 28);
                int pages = Math.Max(1, (profile.Owned.Length + PageSize - 1) / PageSize); page = Math.Min(page, pages - 1);
                Text("OwnedTitle", "永続所持カード", 0, 180, 1500, 60, 28);
                for (int row = 0; row < PageSize && page * PageSize + row < profile.Owned.Length; row++) {
                    var card = profile.Owned[page * PageSize + row];
                    Text("OwnedCard_" + row, OwnedDefinition.DisplayName(card) + $"　{card.Count}/4枚", 0, 105 - row * 49, 1500, 48, 26);
                }
                if (profile.Owned.Length == 0) Text("NoOwnedCards", "永続所持カードはまだありません。", 0, 50, 1500, 80, 28);
                Pages(profile, flow, blocked, error, pages, collectionOnly);
                if (collectionOnly) Button("CloseOwnedCards", "戻る", 0, -420, 900, 75, closeCollection);
                else {
                    if (editOwnedDeparture != null) Button("EditOwnedDeparture", "所持カードで出撃デッキを編成", -440, -420, 820, 75, editOwnedDeparture).interactable = !blocked;
                    if (depart != null) Button("NextDeparture", (dungeonName==null?"":dungeonName+"へ\n")+(profile.EquippedDeckId==null?"既存デッキで再出撃":"装備デッキで再出撃"), 440, -420, 820, 75, depart).interactable = !blocked;
                }
            }
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&savedDeckMenu!=null&&!blocked&&error==null)Button("SavedDeckMenu","保存デッキ・装備",-645,-510,410,58,savedDeckMenu);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&selectDungeon!=null&&!blocked&&error==null)Button("SelectDungeon","ダンジョンを選択",645,-510,410,58,selectDungeon);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&selectStyle!=null&&!blocked&&error==null)Button("SelectDepartureStyle","出撃スタイル",-215,-510,410,58,selectStyle);
            if(!collectionOnly&&flow?.Stage==ClearReturnStage.Returned&&openExchange!=null&&!blocked&&error==null)Button("OpenTokenExchange","トークン交換所",215,-510,410,58,openExchange);
            if (blocked || error != null) Button("RetryClearReturn", "保存・読込を再試行", 0, -510, 800, 58, retry);
        }
        private int dungeonPage;
        public void RenderDungeonSelection(string[] names, bool[] unlocked, int selected, Action<int> choose,
            Action confirmDeparture, Action cancelSelection, bool blocked, string error, Action retrySave, Action chooseStyle = null, string styleName = null, Action openTokenExchange = null)
        {
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("DungeonSelectionContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.SetActive(true);
            Text("DungeonSelectionTitle", "出撃するダンジョン", 0, 455, 1700, 80, 36);
            Text("DungeonSelectionHelp", error ?? ("クリアに応じて次のダンジョンが解放されます。 / " + styleName), 0, 350, 1700, 80, 25);
            const int rows = 5; int pages = Math.Max(1, (names.Length + rows - 1) / rows); dungeonPage = Math.Min(dungeonPage, pages - 1);
            for (int row = 0; row < rows && dungeonPage * rows + row < names.Length; row++) {
                int index = dungeonPage * rows + row;
                var button = Button("DungeonChoice_" + index, (unlocked[index] ? selected == index ? "［選択中］ " : "［選択］ " : "［未解放］ ") + names[index], 0, 240 - row * 95, 1450, 78, () => choose(index));
                button.interactable = !blocked && unlocked[index];
                if (selected == index) button.GetComponent<Image>().color = new Color(.12f, .42f, .47f, .82f);
            }
            Button("PreviousDungeonPage", "前へ", -380, -285, 260, 60, () => { dungeonPage--; RenderDungeonSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave, chooseStyle, styleName, openTokenExchange); }).interactable = !blocked && dungeonPage > 0;
            Button("NextDungeonPage", "次へ", 380, -285, 260, 60, () => { dungeonPage++; RenderDungeonSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave, chooseStyle, styleName, openTokenExchange); }).interactable = !blocked && dungeonPage + 1 < pages;
            Text("DungeonPage", $"{dungeonPage+1}/{pages}", 0, -285, 250, 60, 24);
            Button("ConfirmDungeonSelection", "選択を確定", -430, -400, 800, 75, confirmDeparture).interactable = !blocked && selected >= 0 && selected < unlocked.Length && unlocked[selected];
            Button("CancelDungeonSelection", "取消し", 430, -400, 800, 75, cancelSelection).interactable = !blocked;
            if (chooseStyle != null && !blocked && error == null) Button("SelectDepartureStyle", "出撃スタイル", -430, -510, 800, 58, chooseStyle);
            if (openTokenExchange != null && !blocked && error == null) Button("OpenTokenExchange", "トークン交換所", 430, -510, 800, 58, openTokenExchange);
            if (error != null || blocked) Button("RetryDungeonDeparture", "保存・出撃を再試行", 0, -510, 850, 58, retrySave);
        }
                        public void RenderDungeonVisit(string title,string[] names,bool[] enabled,int selected,bool blocked,bool canConfirm,
            Action<int> choose,Action confirmVisit,Action cancelVisit,Action exitVisit,Action pauseVisit,bool canExit=true)
        {
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("DungeonVisitContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);
            Text("VisitTitle",title,0,435,1450,90,32);
            Button("PauseVisit","ポーズ",780,480,260,60,pauseVisit);
            for(int i=0;i<names.Length;i++){int slot=i;var button=Button("VisitOption_"+i,(selected==i?"［選択中］ ":"")+names[i],0,260-i*90,1700,76,()=>choose(slot));button.interactable=!blocked&&enabled[i];}
            Text("VisitHelp",blocked?"退出保存を完了するまで操作できません。保存状態の再試行を使ってください。":"選択/取消では効果なし。確定した取引と結果は退出時に保存します。",0,-280,1750,100,24);
            Button("ConfirmVisit","選択を確定",-560,-440,520,72,confirmVisit).interactable=!blocked&&canConfirm;
            Button("CancelVisit","選択取消",0,-440,520,72,cancelVisit).interactable=!blocked&&selected>=0;
            Button("ExitVisit","退出して保存",560,-440,520,72,exitVisit).interactable=!blocked&&canExit;
        }

        private int removalPage;
        public void RenderVisitRemoval(string title,string[] names,bool[] selected,int needed,long price,bool blocked,bool canConfirm,
            Action<int> choose,Action confirm,Action cancel,Action pause)
        {
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("VisitRemovalContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);
            Text("RemovalTitle",title,0,435,1450,90,32);Button("PauseVisit","ポーズ",780,480,260,60,pause);
            const int rows=6;int pages=System.Math.Max(1,(names.Length+rows-1)/rows);removalPage=System.Math.Min(removalPage,pages-1);
            for(int row=0;row<rows&&removalPage*rows+row<names.Length;row++){
                int index=removalPage*rows+row;
                Button("RemovalCard_"+index,(selected[index]?"［選択中］ ":"")+names[index],0,285-row*78,1750,68,()=>choose(index)).interactable=!blocked;
            }
            Button("RemovalPrevious","前",-430,-240,450,55,()=>{removalPage--;RenderVisitRemoval(title,names,selected,needed,price,blocked,canConfirm,choose,confirm,cancel,pause);}).interactable=!blocked&&removalPage>0;
            Button("RemovalNext","次",430,-240,450,55,()=>{removalPage++;RenderVisitRemoval(title,names,selected,needed,price,blocked,canConfirm,choose,confirm,cancel,pause);}).interactable=!blocked&&removalPage<pages-1;
            Text("RemovalHelp",$"{removalPage+1}/{pages}ページ / {System.Array.FindAll(selected,x=>x).Length}/{needed}枚選択\n確定前は支払・削除なし。獲得済みカードの持帰り履歴は残ります。",0,-340,1750,110,25);
            Button("ConfirmVisitRemoval",price+"通貨で削除を確定",-430,-460,800,72,confirm).interactable=!blocked&&canConfirm;
            Button("CancelVisitRemoval","確定せず戻る",430,-460,800,72,cancel).interactable=!blocked;
        }

        private int exchangePage;
        public void RenderTokenExchange(ClearReturnProfile profile, string selected, bool blocked, string error,
            Action<string> choose, Action buy, Action cancel, Action close, Action retrySave)
        {
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("TokenExchangeContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            gameObject.SetActive(true);
            Text("ExchangeTitle","トークン交換所",0,455,1700,80,36);
            Text("ExchangeBalance",$"所持 {profile.Tokens} トークン / 永続所持へ交換\n"+(error??"ランダム商品は帰還時に更新。任意の再抽選はできません。"),0,345,1750,100,25);
            var offers=profile.ExchangeStock?.Offers??Array.Empty<ExchangeOfferData>();const int rows=5;int pages=Math.Max(1,(offers.Length+rows-1)/rows);exchangePage=Math.Min(exchangePage,pages-1);
            for(int row=0;row<rows&&exchangePage*rows+row<offers.Length;row++){
                var item=offers[exchangePage*rows+row];var ownedMatches=profile.Owned.Where(c=>c.Id==item.CardId&&c.Level==item.Level&&c.Name==item.Name&&c.Rarity==item.Rarity&&(item.CardDefinitionHash==null||c.DefinitionHash==item.CardDefinitionHash)).ToArray();int owned=ownedMatches.Length==1?ownedMatches[0].Count:0;
                long total=(profile.ExchangeTotals??Array.Empty<ExchangePurchaseTotal>()).SingleOrDefault(t=>t.ProductId==item.Id)?.Count??0;
                bool sold=item.Limit!=ExchangeLimit.Unlimited&&(item.Limit==ExchangeLimit.PerRefresh?item.Purchased:total)>=item.Maximum;
                string limit=item.Limit==ExchangeLimit.Unlimited?"無制限":item.Limit==ExchangeLimit.PerRefresh?$"更新まで {item.Purchased}/{item.Maximum}":$"通算 {total}/{item.Maximum}";
                var b=Button("ExchangeOffer_"+item.Id,(selected==item.Id?"［選択中］ ":sold?"［売切れ］ ":"［選択］ ")+(item.Random?"ランダム ":"固定 ")+item.Name+new string('★',(int)item.Level)+$" / {item.Price} トークン / 所持{owned}/4 / "+limit,0,205-row*90,1730,76,()=>choose(item.Id));
                b.interactable=!blocked&&!sold;if(selected==item.Id)b.GetComponent<Image>().color=new Color(.12f,.42f,.47f,.82f);
            }
            Button("PreviousExchangePage","前へ",-380,-270,260,60,()=>{exchangePage--;RenderTokenExchange(profile,selected,blocked,error,choose,buy,cancel,close,retrySave);}).interactable=!blocked&&exchangePage>0;
            Button("NextExchangePage","次へ",380,-270,260,60,()=>{exchangePage++;RenderTokenExchange(profile,selected,blocked,error,choose,buy,cancel,close,retrySave);}).interactable=!blocked&&exchangePage+1<pages;
            var choice=offers.SingleOrDefault(o=>o.Id==selected);
            string help=choice==null?"商品を選んで確認してください。":$"{choice.Price} トークンを支払い1枚獲得。"+(profile.Owned.Any(c=>c.Id==choice.CardId&&c.Level==choice.Level&&c.Count==4)?$"所持上限のため{choice.OverflowTokens}トークンへ変換。":!ClearReturnSession.CanExchange(profile,choice)?"トークン不足または売切れ。":"");
            Text("ExchangeConfirmation",help,0,-355,1750,75,24);
            Button("ConfirmTokenExchange","交換を確定",-560,-440,520,72,buy).interactable=!blocked&&ClearReturnSession.CanExchange(profile,choice);
            Button("CancelTokenExchange","選択取消",0,-440,520,72,cancel).interactable=!blocked&&selected!=null;
            Button("CloseTokenExchange","拠点へ戻る",560,-440,520,72,close).interactable=!blocked;
            if(error!=null||blocked)Button("RetryTokenExchange","保存を再試行",0,-525,800,55,retrySave);
        }

        private int stylePage;
        public void RenderStyleSelection(string[] names, bool[] unlocked, int selected, Action<int> choose,
            Action confirmDeparture, Action cancelSelection, bool blocked, string error, Action retrySave)
        {
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("StyleSelectionContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.SetActive(true);
            Text("StyleSelectionTitle", "出撃スタイル", 0, 455, 1700, 80, 36);
            Text("StyleSelectionHelp", error ?? "攻略中は固定。属性と固有・タメ攻撃が変わります。", 0, 350, 1700, 80, 25);
            const int rows = 5; int pages = Math.Max(1, (names.Length + rows - 1) / rows); stylePage = Math.Min(stylePage, pages - 1);
            for (int row = 0; row < rows && stylePage * rows + row < names.Length; row++) {
                int index = stylePage * rows + row;
                var button = Button("StyleChoice_" + index, (unlocked[index] ? selected == index ? "［選択中］ " : "［選択］ " : "［未解放］ ") + names[index], 0, 240 - row * 95, 1450, 78, () => choose(index));
                button.interactable = !blocked && unlocked[index];
                if (selected == index) button.GetComponent<Image>().color = new Color(.12f, .42f, .47f, .82f);
            }
            Button("PreviousStylePage", "前へ", -380, -285, 260, 60, () => { stylePage--; RenderStyleSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave); }).interactable = !blocked && stylePage > 0;
            Button("NextStylePage", "次へ", 380, -285, 260, 60, () => { stylePage++; RenderStyleSelection(names, unlocked, selected, choose, confirmDeparture, cancelSelection, blocked, error, retrySave); }).interactable = !blocked && stylePage + 1 < pages;
            Text("StylePage", $"{stylePage+1}/{pages}", 0, -285, 250, 60, 24);
            Button("ConfirmStyleSelection", "選択を確定", -430, -400, 800, 75, confirmDeparture).interactable = !blocked && selected >= 0 && selected < unlocked.Length && unlocked[selected];
            Button("CancelStyleSelection", "取消し", 430, -400, 800, 75, cancelSelection).interactable = !blocked;
            if (error != null || blocked) Button("RetryStyleDeparture", "保存・出撃を再試行", 0, -510, 850, 58, retrySave);
        }

        public void RenderOwnedDeparture(ClearReturnProfile profile, Card[] options, IReadOnlyList<Card> draft,
            string error, bool valid, Action<int, int> change, Action confirm, Action<bool> close, Action resume, bool confirmDiscard, Action save = null, string deckName = null, Action<string> rename = null, bool pending = false, Action retrySave = null, IReadOnlyList<string> draftKeys=null)
        {
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("DepartureContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.SetActive(true);
            Text("DepartureTitle", "所持から出撃デッキを編成", 0, 455, 1750, 70, 36);
            if (confirmDiscard) {
                Text("DiscardDepartureHelp", "未確定の編成を破棄して帰還画面へ戻りますか？", 0, 100, 1700, 130, 30);
                Button("KeepEditingDeparture", "編集に戻る", -420, -150, 750, 80, resume);
                Button("DiscardDepartureDraft", "変更を破棄", 420, -150, 750, 80, () => close(true)); return;
            }
            if(save!=null){var inputObj=new GameObject("SavedDeckName",typeof(RectTransform),typeof(Image),typeof(InputField));inputObj.transform.SetParent(content.transform,false);
                var ir=(RectTransform)inputObj.transform;ir.anchorMin=ir.anchorMax=new Vector2(.5f,.5f);ir.anchoredPosition=new Vector2(0,405);ir.sizeDelta=new Vector2(1100,48);inputObj.GetComponent<Image>().color=new Color(.05f,.12f,.16f,.8f);
                var label=Text("SavedDeckNameText",deckName??"",0,0,1080,46,25);label.transform.SetParent(inputObj.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;
                var input=inputObj.GetComponent<InputField>();input.textComponent=label;input.text=deckName??"";input.interactable=!pending;input.onValueChanged.AddListener(value=>rename(value));}
            Text("DepartureRules", $"編成 {draft.Count}/20枚　所持枚数は消費しません。\nスタンダード同名4・リミテッド同名1/計5・アルティメット同名1/計1", 0, 350, 1750, 120, 26);
            Text("DepartureValidation", error ?? "この20枚で出撃できます。", 0, 250, 1750, 70, 25);
            const int rows = 6; int pages = Math.Max(1, (profile.Owned.Length + rows - 1) / rows); departurePage = Math.Min(departurePage, pages - 1);
            for (int row = 0; row < rows && departurePage * rows + row < profile.Owned.Length; row++) {
                int index = departurePage * rows + row; var owned = profile.Owned[index]; var option = options[index];
                int count = draftKeys!=null?draftKeys.Count(k=>k==OwnedDefinition.Key(owned)):draft.Count(c=>OwnedDefinition.CardMatches(owned,c)); float y = 160 - row * 75;
                Text("DepartureCard_" + index, OwnedDefinition.DisplayName(owned) + (option==null?"":$"　威力 {option.GetFinalPower()}") + $"　編成 {count} / 所持 {owned.Count}" + (option == null ? "（定義未解決）" : ""), -260, y, 1100, 65, 25);
                Button("RemoveDepartureCard_" + index, "−", 420, y, 120, 60, () => change(index, -1)).interactable = !pending && count > 0;
                Button("AddDepartureCard_" + index, "＋", 610, y, 120, 60, () => change(index, 1)).interactable = !pending && option != null && (save!=null||(count < owned.Count && draft.Count < 20));
            }
            if (profile.Owned.Length == 0) Text("NoDepartureOwned", "所持カードがありません。スターターは自動付与しません。", 0, 80, 1750, 100, 28);
            Text("DeparturePage", $"{departurePage+1}/{pages}", 0, -340, 250, 50, 24);
            Button("PreviousDeparturePage", "前へ", -380, -340, 240, 55, () => { departurePage--; resume(); }).interactable = departurePage > 0;
            Button("NextDeparturePage", "次へ", 380, -340, 240, 55, () => { departurePage++; resume(); }).interactable = departurePage+1 < pages;
            if(save==null)Button("ConfirmOwnedDeparture", "この20枚で出撃", 0, -420, 900, 75, confirm).interactable = valid;
            else Button("SaveNamedDeck", "デッキを保存（装備中は20枚条件必須）",0,-420,1500,75,save).interactable=!pending;
            if(pending)Button("RetrySavedDeckSave","保存を再試行",0,-510,800,58,retrySave);
            else Button("CloseOwnedDeparture", save==null?"帰還画面へ戻る":"保存デッキ一覧へ戻る", 0, -510, 800, 58, () => close(false));
        }

        public void RenderSavedDeckMenu(ClearReturnProfile profile,string error,bool pending,Action create,Action<string> edit,Action<string> clone,Action<string> equip,Action<string> delete,Action close,Action retry,string deleting=null,Action confirmDelete=null,Action reload=null)
        {
            if(content!=null){content.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("SavedDeckContents",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.SetActive(true);
            Text("SavedDeckTitle","保存デッキ・装備",0,455,1700,70,36);
            Text("SavedDeckError",error??"",0,290,1750,80,25);
            var decks=profile.SavedDecks??new SavedDepartureDeck[0];
            if(deleting!=null){var d=decks.SingleOrDefault(x=>x.Id==deleting);Text("DeleteDeckWarning",(d?.Name??"")+" を削除しますか？\n所持カードは減りません。",0,100,1700,180,30);
                Button("ConfirmDeleteDeck","削除する",-400,-150,700,75,confirmDelete).interactable=!pending;Button("CancelDeleteDeck","取消し",400,-150,700,75,()=>RenderSavedDeckMenu(profile,error,pending,create,edit,clone,equip,delete,close,retry,reload:reload));return;}
            Text("SavedDeckHelp","非装備は不完全でも保存可。装備・出撃は20枚/所持/共通制限を確認。\n装備中の削除は未対応です。先に別のデッキを装備してください。",0,365,1750,100,25);
            const int rows=5;int pages=Math.Max(1,(decks.Length+rows-1)/rows);savedDeckPage=Math.Min(savedDeckPage,pages-1);
            for(int n=0;n<rows&&savedDeckPage*rows+n<decks.Length;n++){var d=decks[savedDeckPage*rows+n];float y=190-n*85;bool active=d.Id==profile.EquippedDeckId;
                Text("SavedDeckRow_"+d.Id,(active?"［装備中］ ":"")+d.Name+"　"+d.Cards.Length+"枚",-470,y,700,70,25);
                Button("EquipDeck_"+d.Id,"装備",30,y,160,60,()=>equip(d.Id)).interactable=!pending&&!active;
                Button("EditDeck_"+d.Id,"編集",240,y,160,60,()=>edit(d.Id)).interactable=!pending;
                Button("CloneDeck_"+d.Id,"複製",450,y,160,60,()=>clone(d.Id)).interactable=!pending;
                Button("DeleteDeck_"+d.Id,"削除",660,y,160,60,()=>delete(d.Id)).interactable=!pending&&!active;}
            Text("SavedDeckPage",$"{savedDeckPage+1}/{pages}",0,-300,300,55,24);
            Button("PreviousSavedDeckPage","前へ",-380,-300,240,55,()=>{savedDeckPage--;RenderSavedDeckMenu(profile,error,pending,create,edit,clone,equip,delete,close,retry,reload:reload);}).interactable=!pending&&savedDeckPage>0;
            Button("NextSavedDeckPage","次へ",380,-300,240,55,()=>{savedDeckPage++;RenderSavedDeckMenu(profile,error,pending,create,edit,clone,equip,delete,close,retry,reload:reload);}).interactable=!pending&&savedDeckPage+1<pages;
            Button("NewSavedDeck","新しいデッキ",-450,-420,750,75,create).interactable=!pending;
            Button("CloseSavedDeckMenu","帰還画面へ戻る",450,-420,750,75,close).interactable=!pending;
            if(pending)Button("RetrySavedDeckSave","保存を再試行",0,-510,800,58,retry);
            else if(reload!=null)Button("ReloadSavedDecks","保存から再読込",0,-510,800,58,reload);
        }

        private void Pages(ClearReturnProfile p, ClearReturnData f, bool blocked, string error, int pages, bool collectionOnly = false)
        {
            Text("ReturnPage", $"{page + 1}/{pages}", 0, -340, 300, 55, 24);
            Button("ReturnPreviousPage", "前へ", -380, -340, 240, 55, () => { page = Math.Max(0, page - 1); Render(p, f, blocked, error, collectionOnly); }).interactable = page > 0;
            Button("ReturnNextPage", "次へ", 380, -340, 240, 55, () => { page = Math.Min(pages - 1, page + 1); Render(p, f, blocked, error, collectionOnly); }).interactable = page + 1 < pages;
        }
        private UnityEngine.UI.Text Text(string name, string value, float x, float y, float width, float height, int size)
        {
            var o = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text)); o.transform.SetParent(content.transform, false);
            var t = o.GetComponent<UnityEngine.UI.Text>(); t.text = value; t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(.5f, .5f); t.rectTransform.anchoredPosition = new Vector2(x, y); t.rectTransform.sizeDelta = new Vector2(width, height); return t;
        }
        private Button Button(string name, string label, float x, float y, float width, float height, Action action)
        {
            var o = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); o.transform.SetParent(content.transform, false);
            var r = (RectTransform)o.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
            o.GetComponent<Image>().color = new Color(.14f, .29f, .36f, .78f);
            var edge = o.AddComponent<Outline>(); edge.effectColor = new Color(.5f,.9f,.93f,.6f); edge.effectDistance = new Vector2(1,-1); var b = o.GetComponent<Button>(); b.navigation = new Navigation { mode = Navigation.Mode.None }; b.onClick.AddListener(() => action());
            var text = Text(name + "Label", label, x, y, width, height, 25); text.transform.SetParent(o.transform, false); text.rectTransform.anchoredPosition = Vector2.zero; return b;
        }
    }
}

namespace Olca.Presentation.Dungeon
{
    // 既存FHD Canvas上の小さな操作面。面は透過し、操作ボタン以外は入力を遮らない。
    public sealed class BasicAttackHud : UnityEngine.MonoBehaviour
    {
        private UnityEngine.UI.Text title, state;
        private UnityEngine.UI.Button button, jumpButton, dashButton, cardButton;
        private UnityEngine.GameObject exploration;
        private BasicAttackTapButton attackPointer;
        public void Initialize(UnityEngine.Font font, System.Action tap, System.Action<bool> hold, System.Action cancel)
        {
            var rect = (UnityEngine.RectTransform)transform; rect.anchorMin = rect.anchorMax = new UnityEngine.Vector2(1,0);
            rect.pivot = new UnityEngine.Vector2(.5f,.5f); rect.sizeDelta = new UnityEngine.Vector2(500,170); rect.anchoredPosition = new UnityEngine.Vector2(-280,115);
            var face = gameObject.AddComponent<UnityEngine.UI.Image>(); face.color = new UnityEngine.Color(.04f,.11f,.16f,.87f); face.raycastTarget = false;
            var edge = gameObject.AddComponent<UnityEngine.UI.Outline>(); edge.effectColor = new UnityEngine.Color(.5f,.9f,.93f,.6f); edge.effectDistance = new UnityEngine.Vector2(1,-1);
            UnityEngine.UI.Text Label(string name, float x, float y, float w, float h, int size)
            {
                var o = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Text)); o.transform.SetParent(transform,false);
                var text = o.GetComponent<UnityEngine.UI.Text>(); text.font = font; text.fontSize = size; text.color = new UnityEngine.Color(.94f,.99f,1f);
                text.alignment = UnityEngine.TextAnchor.MiddleCenter; text.raycastTarget = false;
                var r = text.rectTransform; r.anchorMin = r.anchorMax = new UnityEngine.Vector2(.5f,.5f); r.anchoredPosition = new UnityEngine.Vector2(x,y); r.sizeDelta = new UnityEngine.Vector2(w,h); return text;
            }
            title = Label("BasicAttackKey",-80,50,300,44,26);
            Label("BasicAttackHelp",-80,0,310,60,22).text = "前方一マス・敵一体\n短く押して離す";
            state = Label("BasicAttackState",0,-55,470,36,22);
            var o = new UnityEngine.GameObject("BasicAttackButton",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button)); o.transform.SetParent(transform,false);
            var r = (UnityEngine.RectTransform)o.transform; r.anchorMin = r.anchorMax = new UnityEngine.Vector2(.5f,.5f); r.anchoredPosition = new UnityEngine.Vector2(155,25); r.sizeDelta = new UnityEngine.Vector2(155,74);
            o.GetComponent<UnityEngine.UI.Image>().color = new UnityEngine.Color(.2f,.46f,.53f,.86f); button = o.GetComponent<UnityEngine.UI.Button>();
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
                var b=o.GetComponent<UnityEngine.UI.Button>();b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};b.onClick.AddListener(()=>action());
                var t=new UnityEngine.GameObject(name+"Label",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));t.transform.SetParent(o.transform,false);
                var text=t.GetComponent<UnityEngine.UI.Text>();text.font=font;text.fontSize=26;text.text=label;text.color=UnityEngine.Color.white;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.raycastTarget=false;
                var tr=text.rectTransform;tr.anchorMin=UnityEngine.Vector2.zero;tr.anchorMax=UnityEngine.Vector2.one;tr.offsetMin=tr.offsetMax=UnityEngine.Vector2.zero;return b;
            }
            jumpButton=Make("ExplorationJumpButton","ジャンプ",-110,jump);dashButton=Make("ExplorationDashButton","ダッシュ",110,dash);
            cardButton=Make("TouchCardUseButton","カード",0,card);
            cardButton.transform.SetParent(transform,false);((UnityEngine.RectTransform)cardButton.transform).anchoredPosition=new UnityEngine.Vector2(-80,5);
            ((UnityEngine.RectTransform)cardButton.transform).sizeDelta=new UnityEngine.Vector2(190,60);
            title.rectTransform.anchoredPosition=new UnityEngine.Vector2(0,60);title.rectTransform.sizeDelta=new UnityEngine.Vector2(470,30);title.fontSize=22;
            exploration.SetActive(false);
        }
        public void RenderExploration(bool available)
        {
            title.gameObject.SetActive(false);state.gameObject.SetActive(false);button.gameObject.SetActive(false);
            transform.Find("BasicAttackHelp").gameObject.SetActive(false);cardButton.gameObject.SetActive(false);exploration.SetActive(true);
            jumpButton.interactable=dashButton.interactable=available;
        }
        public void Render(string key, string value, bool available, bool canUseCard=false)
        {
            title.gameObject.SetActive(true);state.gameObject.SetActive(true);button.gameObject.SetActive(true);
            transform.Find("BasicAttackHelp").gameObject.SetActive(false);if(exploration!=null)exploration.SetActive(false);
            if(cardButton!=null){cardButton.gameObject.SetActive(true);cardButton.interactable=canUseCard;}
            title.text = "攻撃 [" + (key ?? "未割当") + "] / カード"; state.text = value; button.interactable = available;
            state.color = available ? new UnityEngine.Color(.65f,1f,.95f) : new UnityEngine.Color(.92f,.94f,.98f);
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
