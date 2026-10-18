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
            if (hpText != null)
                hpText.text = $"HP: {data.CurrentHp} / {data.MaxHp}";

            if (progressText != null)
                progressText.text = $"{data.PlayerPosition:F0} / {data.DungeonLength:F0}";

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

        public DungeonHudData(
            int currentHp, int maxHp,
            float playerPosition, float dungeonLength,
            bool isDashing, bool isDashCooldown,
            int deckCount, int obtainedCardCount)
        {
            CurrentHp = currentHp;
            MaxHp = maxHp;
            PlayerPosition = playerPosition;
            DungeonLength = dungeonLength;
            IsDashing = isDashing;
            IsDashCooldown = isDashCooldown;
            DeckCount = deckCount;
            ObtainedCardCount = obtainedCardCount;
        }
    }
}
