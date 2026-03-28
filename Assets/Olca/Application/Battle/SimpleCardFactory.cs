using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Application.Battle
{
    /// <summary>
    /// シンプルなカードファクトリー実装。
    /// コードベースでカードデータを定義する（CSVマスターデータ未整備の間の仮実装）。
    /// </summary>
    public class SimpleCardFactory : ICardFactory
    {
        // カードID定数
        public const string Sword = "sword";
        public const string Bomb = "bomb";
        public const string Heal = "heal";
        public const string Shield = "shield";
        public const string Tuning = "tuning";
        public const string FireSword = "fire_sword";

        private readonly Dictionary<string, Card> catalog;

        public SimpleCardFactory()
        {
            catalog = new Dictionary<string, Card>
            {
                [Sword] = new Card(Sword, "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),

                [Bomb] = new Card(Bomb, "ボム", 2, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Bomb, 40, "範囲攻撃"),

                [Heal] = new Card(Heal, "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復"),

                [Shield] = new Card(Shield, "シールド", 1, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 15, "防御"),

                [Tuning] = new Card(Tuning, "チューニング", 1, CardRarity.Normal, CardCategory.Tuning,
                    ElementType.None, CardSeries.None, 0, "対象カードに強化効果を付与する調律カード"),

                [FireSword] = new Card(FireSword, "ファイアソード", 2, CardRarity.Limited, CardCategory.Attack,
                    ElementType.Fire, CardSeries.Sword, 60, "前方1マス炎属性攻撃（ソード＋ボムの融合）"),
            };
        }

        /// <summary>
        /// カードIDからカードを生成する
        /// </summary>
        public Card CreateById(string cardId)
        {
            if (cardId == null) return null;
            catalog.TryGetValue(cardId, out var card);
            // 同じIDで複数枚作成できるようClone()で新インスタンスを返す
            // （同一参照が複数手札に入ると選択判定が誤作動するため）
            return card?.Clone();
        }

        /// <summary>
        /// 超越後カードを生成する（ベースカードのグレードアップ版）
        /// </summary>
        public Card CreateExceeded(Card baseCard, ExceedLevel nextLevel)
        {
            if (baseCard == null) return null;

            // 超越後の威力倍率: Lv1→×1.5, Lv2→×2.0, Lv3→×3.0
            float powerMultiplier = nextLevel switch
            {
                ExceedLevel.Lv1 => 1.5f,
                ExceedLevel.Lv2 => 2.0f,
                ExceedLevel.Lv3 => 3.0f,
                _ => 1.0f,
            };

            string levelSuffix = nextLevel switch
            {
                ExceedLevel.Lv1 => "★",
                ExceedLevel.Lv2 => "★★",
                ExceedLevel.Lv3 => "★★★",
                _ => "",
            };

            int exceededPower = (int)(baseCard.Power * powerMultiplier);

            return new Card(
                id: baseCard.Id,
                name: $"{baseCard.Name}{levelSuffix}",
                cost: baseCard.Cost,
                rarity: baseCard.Rarity,
                category: baseCard.Category,
                element: baseCard.Element,
                series: baseCard.Series,
                power: exceededPower,
                description: $"{baseCard.Description}（超越{levelSuffix}）",
                exceedLevel: nextLevel);
        }
    }
}
