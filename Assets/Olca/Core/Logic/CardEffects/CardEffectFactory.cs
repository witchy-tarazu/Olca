using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Core.Logic.CardEffects
{
    /// <summary>
    /// カード効果のファクトリ
    /// カードIDから対応する効果を生成
    /// </summary>
    public class CardEffectFactory
    {
        private static readonly Dictionary<string, Func<ICardEffect>> staticEffectCreators
            = new Dictionary<string, Func<ICardEffect>>();

        private readonly Dictionary<string, Func<ICardEffect>> effectCreators;

        public CardEffectFactory()
        {
            effectCreators = new Dictionary<string, Func<ICardEffect>>();
        }

        /// <summary>
        /// カード効果を登録
        /// </summary>
        public void RegisterEffect(string cardId, Func<ICardEffect> creator)
        {
            if (string.IsNullOrEmpty(cardId))
                throw new ArgumentException("カードIDが無効です", nameof(cardId));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            effectCreators[cardId] = creator;
        }

        /// <summary>
        /// カード効果を生成
        /// </summary>
        public ICardEffect CreateEffect(string cardId)
        {
            if (effectCreators.TryGetValue(cardId, out var creator))
            {
                return creator();
            }

            // 登録されていない場合はデフォルト効果（何もしない）
            return new NullEffect();
        }

        /// <summary>
        /// staticメソッド: カード効果を登録
        /// </summary>
        public static void RegisterEffectStatic(string cardId, Func<ICardEffect> creator)
        {
            if (string.IsNullOrEmpty(cardId))
                throw new ArgumentException("カードIDが無効です", nameof(cardId));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            staticEffectCreators[cardId] = creator;
        }

        /// <summary>
        /// staticメソッド: カードから効果を生成
        /// </summary>
        /// <summary>
        /// staticメソッド: カードから効果を生成
        /// </summary>
        /// <summary>
        /// staticメソッド: カードから効果を生成
        /// CardMasterの情報に基づいて適切な効果クラスを自動生成
        /// </summary>
        public static ICardEffect CreateEffect(Card card)
        {
            if (card == null) return new NullEffect();

            // 一時移動カードの判定（モーション時間が設定されている）
            if (card.MoveMotionTime > 0 || card.AttackMotionTime > 0)
            {
                return CreateTemporaryMoveEffect(card);
            }

            // 登録済みの効果を検索（カスタム効果用）
            if (staticEffectCreators.TryGetValue(card.Id, out var creator))
            {
                return creator();
            }

            // カテゴリに基づくデフォルト効果
            // ModifiableCardEffectを使用することで、調律効果（Modifier）が自動的に適用される
            switch (card.Category)
            {
                case CardCategory.Attack:
                    // 攻撃カードは ModifiableCardEffect を使用
                    // CardMasterの TargetType に基づいて攻撃範囲を決定
                    // Modifier の追加効果（スプレッド、貫通、多段ヒット等）が自動適用される
                    return new ModifiableCardEffect(card.TargetType);

                case CardCategory.Defense:
                    // 明示された小カテゴリを優先し、未指定の旧定義だけID判定を維持する。
                    if (card.SubCategory == CardSubCategory.Barrier ||
                        (card.SubCategory == CardSubCategory.None && card.Id.StartsWith("barrier", StringComparison.OrdinalIgnoreCase)))
                        return new BarrierEffect(card.Power > 0 ? card.Power : 20);
                    // それ以外の防御カードはHealEffect（回復）
                    return new HealEffect(card.Power);

                case CardCategory.Field:
                    // フィールド操作カードも ModifiableCardEffect で対応
                    // パネル変化は AdditionalEffect (PanelIce, PanelMagma等) で指定
                    return new ModifiableCardEffect(card.TargetType);

                case CardCategory.Support:
                    // 集中カード（IDが"concentrate"で始まる）はConcentrateEffectを使用
                    if (card.SubCategory == CardSubCategory.Concentrate ||
                        (card.SubCategory == CardSubCategory.None && card.Id.StartsWith("concentrate", StringComparison.OrdinalIgnoreCase)))
                    {
                        float mult = card.Power > 0 ? 1.0f + card.Power / 100.0f : 1.5f;
                        return new ConcentrateEffect(mult);
                    }
                    // それ以外のサポートカードは調律用（何もしない）
                    return new NullEffect();

                default:
                    return new NullEffect();
            }
        }

        /// <summary>
        /// 何もしない効果（デフォルト）
        /// </summary>

        /// <summary>
        /// 一時移動カード効果を生成
        /// EffectClassNameの形式: "TemporaryMove_Direction_Distance"
        /// 例: "TemporaryMove_Right_2" → 右に2マス一時移動して攻撃
        /// </summary>
        /// <summary>
        /// 一時移動カード効果を生成
        /// CardMasterの MoveDirection と MoveDistance フィールドを使用
        /// </summary>
        private static ICardEffect CreateTemporaryMoveEffect(Card card)
        {
            // CardMasterから方向と距離を取得
            Vector2Int direction = ParseDirection(card.MoveDirection ?? "Right");
            int distance = card.MoveDistance > 0 ? card.MoveDistance : 1;

            return new TemporaryMoveAttackEffect(
                direction: direction,
                distance: distance,
                moveMotionTime: card.MoveMotionTime,
                attackMotionTime: card.AttackMotionTime,
                penetrationMode: card.PenetrationMode
            );
        }

        /// <summary>
        /// 方向文字列をVector2Intに変換
        /// </summary>
        private static Vector2Int ParseDirection(string directionStr)
        {
            switch (directionStr.ToUpper())
            {
                case "RIGHT":
                    return new Vector2Int(1, 0);
                case "LEFT":
                    return new Vector2Int(-1, 0);
                case "UP":
                    return new Vector2Int(0, 1);
                case "DOWN":
                    return new Vector2Int(0, -1);
                default:
                    return new Vector2Int(1, 0); // デフォルトは右
            }
        }

        /// <summary>
        /// パネル種類文字列をPanelTypeに変換
        /// </summary>
        private static PanelType ParsePanelType(string panelTypeStr)
        {
            if (string.IsNullOrEmpty(panelTypeStr))
                return PanelType.Normal;

            switch (panelTypeStr.ToUpper())
            {
                case "ICE":
                    return PanelType.Ice;
                case "GRASS":
                    return PanelType.Grass;
                case "MAGMA":
                    return PanelType.Magma;
                case "CRACKED":
                    return PanelType.Cracked;
                case "HOLE":
                    return PanelType.Hole;
                default:
                    return PanelType.Normal;
            }
        }

        private class NullEffect : ICardEffect
        {
            public void Execute(BattleContext context)
            {
                // 何もしない
            }
        }
    }
}
