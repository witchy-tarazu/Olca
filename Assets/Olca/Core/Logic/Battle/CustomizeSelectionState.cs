using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 超越操作の選択状態。同名カード2枚を選ぶ。
    /// </summary>
    public class ExceedSelectionState
    {
        private Card materialA;
        private Card materialB;

        public Card MaterialA => materialA;
        public Card MaterialB => materialB;

        /// <summary>素材が2枚揃っているか</summary>
        public bool IsReady => materialA != null && materialB != null;

        /// <summary>
        /// カードをトグル選択する。
        /// 選択済みなら解除、未選択なら追加。2枚選択済みは追加不可。
        /// </summary>
        public bool TryToggle(Card card)
        {
            if (card == null) return false;

            if (materialA == card)
            {
                materialA = null;
                return true;
            }
            if (materialB == card)
            {
                materialB = null;
                return true;
            }

            if (materialA == null)
            {
                materialA = card;
                return true;
            }
            if (materialB == null)
            {
                materialB = card;
                return true;
            }

            // 2枚選択済みなので追加不可
            return false;
        }

        /// <summary>指定カードが選択済みか</summary>
        public bool IsSelected(Card card) => card != null && (materialA == card || materialB == card);

        /// <summary>選択状態をリセット</summary>
        public void Reset()
        {
            materialA = null;
            materialB = null;
        }

        /// <summary>
        /// 指定手札の中に超越可能なペア（同名カード2枚以上）が存在するか
        /// </summary>
        public static bool HasExceedablePairs(IReadOnlyList<Card> hand)
        {
            if (hand == null || hand.Count < 2) return false;
            return hand.GroupBy(c => c.Id).Any(g => g.Count() >= 2);
        }
    }

    /// <summary>
    /// 調律操作の選択状態。調律対象カード1枚 + 調律カード1枚を選ぶ。
    /// </summary>
    public class TuningSelectionState
    {
        /// <summary>調律対象カード（CardCategory.Tuning 以外）</summary>
        public Card TargetCard { get; private set; }

        /// <summary>調律素材カード（CardCategory.Tuning）</summary>
        public Card TuningCard { get; private set; }

        /// <summary>対象と調律カードが両方揃っているか</summary>
        public bool IsReady => TargetCard != null && TuningCard != null;

        /// <summary>
        /// カードをトグル選択する。
        /// CardCategory.Tuning のカードは調律素材スロットへ、それ以外は対象スロットへ自動振り分け。
        /// 選択済みなら解除。
        /// </summary>
        public bool TryToggle(Card card)
        {
            if (card == null) return false;

            if (TargetCard == card)
            {
                TargetCard = null;
                return true;
            }
            if (TuningCard == card)
            {
                TuningCard = null;
                return true;
            }

            if (card.Category == CardCategory.Tuning)
            {
                TuningCard = card;
                return true;
            }

            TargetCard = card;
            return true;
        }

        /// <summary>指定カードが選択済みか</summary>
        public bool IsSelected(Card card) => card != null && (TargetCard == card || TuningCard == card);

        /// <summary>指定カードが調律対象として選択されているか</summary>
        public bool IsTuningTarget(Card card) => card != null && TargetCard == card;

        /// <summary>指定カードが調律素材として選択されているか</summary>
        public bool IsTuningMaterial(Card card) => card != null && TuningCard == card;

        /// <summary>選択状態をリセット</summary>
        public void Reset()
        {
            TargetCard = null;
            TuningCard = null;
        }
    }

    /// <summary>
    /// 融合操作の選択状態。2枚以上の素材カードを選ぶ。
    /// </summary>
    public class FusionSelectionState
    {
        private readonly List<Card> materials = new List<Card>();

        /// <summary>現在選択中の素材カードリスト</summary>
        public IReadOnlyList<Card> Materials => materials.AsReadOnly();

        /// <summary>2枚以上選択されているか</summary>
        public bool IsReady => materials.Count >= 2;

        /// <summary>
        /// カードをトグル選択する。
        /// 未選択なら追加、選択済みなら解除。
        /// </summary>
        public bool TryToggle(Card card)
        {
            if (card == null) return false;

            if (materials.Contains(card))
            {
                materials.Remove(card);
                return true;
            }

            materials.Add(card);
            return true;
        }

        /// <summary>指定カードが選択済みか</summary>
        public bool IsSelected(Card card) => card != null && materials.Contains(card);

        /// <summary>選択状態をリセット</summary>
        public void Reset()
        {
            materials.Clear();
        }
    }
}
