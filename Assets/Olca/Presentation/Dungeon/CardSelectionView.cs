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
        private Action confirm, finish, retry, depart, closeCollection;
        private GameObject content;
        private int page;
        private string lastRun;
        private ClearReturnStage? lastStage;
        private const int PageSize = 8;
        public void Initialize(Font font, Action<int> toggle, Action confirm, Action finish, Action retry, Action depart = null, Action closeCollection = null)
        {
            this.font = font; this.toggle = toggle; this.confirm = confirm; this.finish = finish; this.retry = retry; this.depart = depart; this.closeCollection = closeCollection;
            var rect = (RectTransform)transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = new Color(0.035f, 0.085f, 0.12f, .88f);
            var frame = gameObject.AddComponent<Outline>(); frame.effectColor = new Color(.48f,.9f,.94f,.45f); frame.effectDistance = new Vector2(1,-1); gameObject.SetActive(false);
        }
        public void Render(ClearReturnProfile profile, ClearReturnData flow, bool blocked, string error, bool collectionOnly = false)
        {
            if (lastRun != flow?.RunId || lastStage != flow?.Stage) page = 0;
            lastRun = flow?.RunId; lastStage = flow?.Stage;
            if (content != null) { content.SetActive(false); if (UnityEngine.Application.isPlaying) Destroy(content); else DestroyImmediate(content); }
            content = new GameObject("ReturnContents", typeof(RectTransform)); content.transform.SetParent(transform, false);
            var rect = (RectTransform)content.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            gameObject.SetActive(true); profile = profile ?? new ClearReturnProfile();
            Text("Title", collectionOnly ? "所持カード・成長" : flow == null ? "進行データの確認" : flow.Stage == ClearReturnStage.Choosing ? "持ち帰りカードを選択" :
                flow.Stage == ClearReturnStage.Awarded ? "持ち帰り・経験値を反映しました" : "帰還しました", 0, 455, 1750, 80, 36);
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
                Text("HomeStats", collectionOnly ? $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\n所持カードは攻略中に消費・減少しません。" : $"Lv {profile.Level}　総経験値 {profile.Experience}　トークン {profile.Tokens}\nHP {profile.ReturnedHp}/{flow.ReturnedMaxHp}　攻撃 {flow.ReturnedAttack}　カード・経験値は保存済みです。", 0, 260, 1750, 130, 28);
                int pages = Math.Max(1, (profile.Owned.Length + PageSize - 1) / PageSize); page = Math.Min(page, pages - 1);
                Text("OwnedTitle", "永続所持カード", 0, 180, 1500, 60, 28);
                for (int row = 0; row < PageSize && page * PageSize + row < profile.Owned.Length; row++) {
                    var card = profile.Owned[page * PageSize + row];
                    Text("OwnedCard_" + row, card.Name + new string('★', (int)card.Level) + $"　{card.Count}/4枚", 0, 105 - row * 49, 1500, 48, 26);
                }
                if (profile.Owned.Length == 0) Text("NoOwnedCards", "永続所持カードはまだありません。", 0, 50, 1500, 80, 28);
                Pages(profile, flow, blocked, error, pages, collectionOnly);
                if (collectionOnly) Button("CloseOwnedCards", "戻る", 0, -420, 900, 75, closeCollection);
                else if (depart != null) Button("NextDeparture", "既存デッキで再出撃", 0, -420, 900, 75, depart).interactable = !blocked;
            }
            if (blocked || error != null) Button("RetryClearReturn", "保存・読込を再試行", 0, -510, 800, 58, retry);
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
        private UnityEngine.UI.Button button;
        public void Initialize(UnityEngine.Font font, System.Action tap, System.Func<int> shortPressFrames)
        {
            var rect = (UnityEngine.RectTransform)transform; rect.anchorMin = rect.anchorMax = UnityEngine.Vector2.one;
            rect.pivot = new UnityEngine.Vector2(.5f,.5f); rect.sizeDelta = new UnityEngine.Vector2(500,170); rect.anchoredPosition = new UnityEngine.Vector2(-280,-200);
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
            var pointer = o.AddComponent<BasicAttackTapButton>(); pointer.Initialize(tap, System.Diagnostics.Stopwatch.GetTimestamp, shortPressFrames, System.Diagnostics.Stopwatch.Frequency); button.onClick.AddListener(pointer.Click);
            var label = Label("BasicAttackLabel",0,0,155,74,26); label.text = "攻撃"; label.transform.SetParent(o.transform,false); label.rectTransform.anchoredPosition = UnityEngine.Vector2.zero;
        }
        public void Render(string key, string value, bool available)
        {
            title.text = "基本攻撃 [" + (key ?? "未割当") + "]"; state.text = value; button.interactable = available;
            state.color = available ? new UnityEngine.Color(.65f,1f,.95f) : new UnityEngine.Color(.92f,.94f,.98f);
        }
    }
}

namespace Olca.Presentation.Dungeon
{
    public sealed class BasicAttackTapButton : UnityEngine.MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        private System.Action fire; private System.Func<long> timestamp; private System.Func<int> limit; private long frequency;
        private bool pointer, accepted; private long start;
        public void Initialize(System.Action fire, System.Func<long> timestamp, System.Func<int> limit, long frequency = 60) { this.fire=fire;this.timestamp=timestamp;this.limit=limit;this.frequency=frequency; }
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data) { pointer=true;accepted=false;start=timestamp(); }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData data) { accepted=(timestamp()-start)/(double)frequency < limit()/60.0; }
        public void Click() { bool use=!pointer||accepted;pointer=false;accepted=false;if(use)fire(); }
        private void OnDisable() { pointer=false;accepted=false; }
    }
}
