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

        /// <summary>
        /// HUDを最新のダンジョン状態で更新する
        /// DungeonフェーズのUpdateViews()から毎フレーム呼ばれる
        /// </summary>
        public void Refresh(DungeonHudData data)
        {
            // 既存シーンで進行度欄が未割当でも、明示した階層列の現在階層を表示する。
            if (data.FloorNumber > 0 && progressText == null)
            {
                var label = new GameObject("FloorProgressText", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(transform, false);
                progressText = label.GetComponent<Text>();
                progressText.font = deckCountText != null && deckCountText.font != null
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
                progressText.text = (data.FloorNumber > 0 ? $"{data.FloorNumber}階  " : "") + $"{data.PlayerPosition:F0} / {data.DungeonLength:F0}";

            if (dashStateText != null)
            {
                string state = data.IsDashing  ? "DASH" :
                               data.IsDashCooldown ? "WAIT" : "READY";
                dashStateText.text = state;
            }

            if (deckCountText != null)
                deckCountText.text = $"デッキ: {data.DeckCount}枚\n獲得: {data.ObtainedCardCount}枚";
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
