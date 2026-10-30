using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// ダンジョン探索中のHUD表示
    /// プレイヤーHP・進行度・ダッシュ状態・デッキ枚数・獲得履歴枚数を表示する
    /// </summary>
    public class DungeonHudView : MonoBehaviour
    {
        [Header("HP")]
        [SerializeField] private Text hpText;

        private Font missingStatusFont;
        private bool generatedHpText, generatedProgressText;

        /// <summary>新しく補うステータス欄だけに既存UIのフォントを渡し、Sceneの明示割当は変更しない。</summary>
        public void ConfigureMissingStatusFont(Font font)
        {
            if (font == null) return;
            missingStatusFont = font;
            if (generatedHpText && hpText != null) hpText.font = font;
            if (generatedProgressText && progressText != null) progressText.font = font;
        }

        [Header("進行度")]
        [SerializeField] private Text progressText;

        [Header("ダッシュ")]
        [SerializeField] private Text dashStateText;

        [Header("デッキ")]
        [SerializeField] private Text deckCountText;

        [Header("CanvasGroup（バトル時のフェード制御）")]
        [SerializeField] private CanvasGroup canvasGroup;

        /// <summary>外部からCanvasGroupを参照する（GameFieldView.HideHUDAsync/ShowHUDAsync用）</summary>
        public CanvasGroup CanvasGroup => canvasGroup;

        /// <summary>報酬画面には専用の枚数欄があるため、背景側の重複表示だけを隠す。</summary>
        public void SetDeckCountVisible(bool visible)
        {
            if(deckCountText!=null)deckCountText.gameObject.SetActive(visible);
        }


        /// <summary>
        /// HUDを最新のダンジョン状態で更新する
        /// DungeonフェーズのUpdateViews()から毎フレーム呼ばれる
        /// </summary>
        public void Refresh(DungeonHudData data)
        {
            // 探索ステータスバーのHP欄が未割当の旧Sceneでも、同じHUD内へ一度だけ接続する。
            // 既存の割当は維持し、戦闘切替時のCanvasGroup制御を共用する。
            if (hpText == null)
            {
                var label = new GameObject("ExplorationHpText", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(transform, false);
                hpText = label.GetComponent<Text>();
                generatedHpText = true;
                hpText.font = missingStatusFont != null ? missingStatusFont : deckCountText != null && deckCountText.font != null
                    ? deckCountText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                hpText.fontSize = 28;
                hpText.resizeTextForBestFit = true;
                hpText.resizeTextMinSize = 18;
                hpText.resizeTextMaxSize = 28;
                hpText.alignment = TextAnchor.MiddleLeft;
                hpText.color = Color.white;
                hpText.raycastTarget = false;
                var outline = label.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, .85f);
                outline.effectDistance = new Vector2(1f, -1f);
                var rect = hpText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(36f, -24f);
                rect.sizeDelta = new Vector2(360f, 48f);
            }
            // 既存シーンで進行度欄が未割当でも、明示した階層列の現在階層を表示する。
            if (data.FloorNumber > 0 && progressText == null)
            {
                var label = new GameObject("FloorProgressText", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(transform, false);
                progressText = label.GetComponent<Text>();
                generatedProgressText = true;
                progressText.font = missingStatusFont != null ? missingStatusFont : deckCountText != null && deckCountText.font != null
                    ? deckCountText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                progressText.fontSize = 28;
                progressText.alignment = TextAnchor.MiddleCenter;
                progressText.raycastTarget = false;
                var rect = progressText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -24f);
                rect.sizeDelta = new Vector2(400f, 48f);
            }
            if (hpText != null)
                hpText.text = $"HP: {data.CurrentHp} / {data.MaxHp}";

            if (progressText != null)
                progressText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana((data.FloorNumber > 0 ? $"{data.FloorNumber}階  " : "") + $"{data.PlayerPosition:F0} / {data.DungeonLength:F0}");

            if (dashStateText != null)
            {
                string state = data.IsDashing  ? "DASH" :
                               data.IsDashCooldown ? "WAIT" : "READY";
                dashStateText.text = state;
            }

            if (deckCountText != null)
                deckCountText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana($"デッキ: {data.DeckCount}枚\n獲得: {data.ObtainedCardCount}枚");
        }
    }

    /// <summary>
    /// DungeonHudView.Refresh() に渡すデータ（値型・Unity非依存）
    /// </summary>
    public readonly struct DungeonHudData
    {
        public readonly int CurrentHp;
        public readonly int MaxHp;
        public readonly float PlayerPosition;
        public readonly float DungeonLength;
        public readonly bool IsDashing;
        public readonly bool IsDashCooldown;
        public readonly int DeckCount;
        public readonly int ObtainedCardCount;
        public readonly int FloorNumber;

        public DungeonHudData(
            int currentHp, int maxHp,
            float playerPosition, float dungeonLength,
            bool isDashing, bool isDashCooldown,
            int deckCount, int obtainedCardCount, int floorNumber = 0)
        {
            CurrentHp = currentHp;
            MaxHp = maxHp;
            PlayerPosition = playerPosition;
            DungeonLength = dungeonLength;
            IsDashing = isDashing;
            IsDashCooldown = isDashCooldown;
            DeckCount = deckCount;
            ObtainedCardCount = obtainedCardCount;
            FloorNumber = floorNumber;
        }
    }
}
