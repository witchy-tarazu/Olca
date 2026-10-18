using Olca.Core.Domain.Master;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Core.Domain
{
    /// <summary>
    /// カード
    /// </summary>
    /// <summary>
    /// カード
    /// </summary>
    public class Card
    {
        /// <summary>カードID</summary>
        public string Id { get; }

        /// <summary>カード名</summary>
        public string Name { get; }

        /// <summary>コスト</summary>
        public int Cost { get; }

        /// <summary>レアリティ</summary>
        public CardRarity Rarity { get; }

        /// <summary>カテゴリ</summary>
        public CardCategory Category { get; }

        /// <summary>属性</summary>
        public ElementType Element { get; }

        /// <summary>系統（ソード系、ボム系など）</summary>
        public CardSeries Series { get; }

        /// <summary>威力（攻撃カードの場合）</summary>
        public int Power { get; }

        /// <summary>説明文</summary>
        public string Description { get; }

        // ========== 攻撃範囲指定 ==========
        /// <summary>攻撃カードの基本対象タイプ</summary>
        public DamageTargetType TargetType { get; }

        // ========== フィールド操作カード用 ==========
        /// <summary>変化させるパネルの種類（"Ice", "Magma"等）</summary>
        public string PanelChangeType { get; }

        // ========== 一時移動カード専用 ==========
        /// <summary>一時移動方向（"Right", "Left", "Up", "Down"）</summary>
        public string MoveDirection { get; }

        /// <summary>一時移動距離（マス数）</summary>
        public int MoveDistance { get; }

        /// <summary>一時移動カードの移動モーション時間（秒）</summary>
        public float MoveMotionTime { get; }

        /// <summary>一時移動カードの攻撃モーション時間（秒）</summary>
        public float AttackMotionTime { get; }

        /// <summary>一時移動時の貫通モード</summary>
        public MovePenetrationMode PenetrationMode { get; }

        // ========== 硬直時間 ==========
        /// <summary>前隙時間（秒） - カード発動からヒット判定が出るまでの時間</summary>
        public float StartupTime { get; }

        /// <summary>後隙時間（秒） - ヒット判定後、行動可能になるまでの時間</summary>
        public float RecoveryTime { get; }

        /// <summary>派生効果中も硬直を継続するか</summary>
        public bool ExtendRecoveryDuringDerivative { get; }

        /// <summary>暗転系カード（前隙・後隙なしで即座に発動）</summary>
        public bool IsInstantEffect { get; }

        /// <summary>調律による追加効果</summary>
        public CardModifier Modifier { get; private set; }

        /// <summary>超越グレード</summary>
        public ExceedLevel ExceedLevel { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Card(
            string id,
            string name,
            int cost,
            CardRarity rarity,
            CardCategory category,
            ElementType element,
            CardSeries series,
            int power,
            string description,
            DamageTargetType targetType = DamageTargetType.Single,
            string panelChangeType = null,
            string moveDirection = null,
            int moveDistance = 0,
            CardModifier modifier = null,
            float moveMotionTime = 0f,
            float attackMotionTime = 0f,
            MovePenetrationMode penetrationMode = MovePenetrationMode.Blocking,
            float startupTime = 0f,
            float recoveryTime = 0f,
            bool extendRecoveryDuringDerivative = false,
            bool isInstantEffect = false,
            ExceedLevel exceedLevel = ExceedLevel.Normal)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Rarity = rarity;
            Category = category;
            Element = element;
            Series = series;
            Power = power;
            Description = description;
            TargetType = targetType;
            PanelChangeType = panelChangeType;
            MoveDirection = moveDirection;
            MoveDistance = moveDistance;
            MoveMotionTime = moveMotionTime;
            AttackMotionTime = attackMotionTime;
            PenetrationMode = penetrationMode;
            StartupTime = startupTime;
            RecoveryTime = recoveryTime;
            ExtendRecoveryDuringDerivative = extendRecoveryDuringDerivative;
            IsInstantEffect = isInstantEffect;
            Modifier = modifier ?? new CardModifier();
            ExceedLevel = exceedLevel;
        }

        /// <summary>
        /// 調律: 他のカードの効果をこのカードに付与する
        /// </summary>
        /// <param name="tuningCard">調律元のカード（アタック+10など）</param>
        /// <returns>調律後の新しいカードインスタンス</returns>
        public Card ApplyTuning(Card tuningCard)
        {
            if (tuningCard == null) return this;

            // 調律元のカードから追加効果を取得
            CardModifier newModifier = this.Modifier.Merge(tuningCard.Modifier);

            // 新しいカードインスタンスを作成（元のカードは変更しない）
            return new Card(
                Id, Name, Cost, Rarity, Category, Element, Series, Power, Description,
                TargetType, PanelChangeType, MoveDirection, MoveDistance,
                newModifier, MoveMotionTime, AttackMotionTime, PenetrationMode,
                StartupTime, RecoveryTime, ExtendRecoveryDuringDerivative, IsInstantEffect,
                ExceedLevel
            );
        }

        /// <summary>
        /// 使用時だけ威力を強化したカードを作成する。元カードと調律効果は変更しない。
        /// </summary>
        public Card WithPowerMultiplier(float multiplier)
        {
            var modifier = Modifier.Merge(new CardModifier());
            // 調律を含む最終威力を一度だけ強化し、再計算での二重加算を防ぐ。
            int power = (int)(GetFinalPower() * multiplier);
            modifier.BonusDamage = 0;
            modifier.DamageMultiplier = 1.0f;
            return new Card(
                Id, Name, Cost, Rarity, Category, Element, Series, power, Description,
                TargetType, PanelChangeType, MoveDirection, MoveDistance,
                modifier, MoveMotionTime, AttackMotionTime, PenetrationMode,
                StartupTime, RecoveryTime, ExtendRecoveryDuringDerivative, IsInstantEffect,
                ExceedLevel
            );
        }

        /// <summary>
        /// 最終的な威力を計算（調律効果を含む）
        /// </summary>
        public int GetFinalPower()
        {
            int finalPower = (int)(Power * Modifier.DamageMultiplier);
            finalPower += Modifier.BonusDamage;
            return finalPower;
        }

        /// <summary>
        /// 最終的な属性を取得（調律効果を含む）
        /// </summary>
        public ElementType GetFinalElement()
        {
            return Modifier.ElementOverride ?? Element;
        }

        /// <summary>
        /// マスターデータからCardを生成
        /// </summary>
        public static Card FromMaster(CardMaster master)
        {
            // SeriesTypeからCardSeriesに変換
            CardSeries series = ConvertSeriesType(master.Series);

            // Modifierの構築（追加効果を解析）
            CardModifier modifier = ParseAdditionalEffects(master);

            return new Card(
                master.Id,
                master.Name,
                master.Cost,
                master.Rarity,
                master.Category,
                master.Element,
                series,
                master.Power,
                master.Description,
                master.TargetType,
                master.PanelChangeType,
                master.MoveDirection,
                master.MoveDistance,
                modifier,
                master.MoveMotionTime,
                master.AttackMotionTime,
                master.PenetrationMode,
                master.StartupTime,
                master.RecoveryTime,
                master.ExtendRecoveryDuringDerivative,
                master.IsInstantEffect
            );
        }

        /// <summary>
        /// マスターデータの追加効果フィールドを解析してCardModifierを生成
        /// </summary>
        private static CardModifier ParseAdditionalEffects(CardMaster master)
        {
            var modifier = new CardModifier();

            if (string.IsNullOrEmpty(master.AdditionalEffectTypes))
                return modifier;

            // カンマ区切りで分割
            string[] effectTypes = master.AdditionalEffectTypes.Split(',');
            string[] triggerStrs = string.IsNullOrEmpty(master.DerivativeTrigger)
                ? new string[0]
                : master.DerivativeTrigger.Split(',');

            for (int i = 0; i < effectTypes.Length; i++)
            {
                string trimmed = effectTypes[i].Trim();

                if (System.Enum.TryParse<AdditionalEffectType>(trimmed, out var effectType))
                {
                    // 対応するトリガーを取得（トリガー数が足りない場合は最後のトリガーを使用、なければImmediate）
                    DerivativeTrigger trigger;
                    if (i < triggerStrs.Length)
                    {
                        trigger = ParseDerivativeTrigger(triggerStrs[i].Trim());
                    }
                    else if (triggerStrs.Length > 0)
                    {
                        trigger = ParseDerivativeTrigger(triggerStrs[triggerStrs.Length - 1].Trim());
                    }
                    else
                    {
                        trigger = DerivativeTrigger.Immediate;
                    }

                    var effect = new AdditionalEffect
                    {
                        EffectType = effectType,
                        Power = master.AdditionalEffectPower,
                        Duration = master.AdditionalEffectDuration,
                        HitCount = master.AdditionalEffectHitCount,
                        ChainCardId = master.ChainCardId,
                        Trigger = trigger
                    };

                    modifier.AdditionalEffects.Add(effect);
                }
            }

            return modifier;
        }

        /// <summary>
        /// DerivativeTrigger文字列をenumに変換
        /// </summary>
        private static DerivativeTrigger ParseDerivativeTrigger(string triggerStr)
        {
            if (string.IsNullOrEmpty(triggerStr))
                return DerivativeTrigger.Immediate;

            if (System.Enum.TryParse<DerivativeTrigger>(triggerStr, out var trigger))
                return trigger;

            return DerivativeTrigger.Immediate;
        }

        /// <summary>
        /// SeriesTypeからCardSeriesに変換
        /// </summary>
        private static CardSeries ConvertSeriesType(SeriesType seriesType)
        {
            switch (seriesType)
            {
                case SeriesType.Sword: return CardSeries.Sword;
                case SeriesType.Canon: return CardSeries.Canon;
                case SeriesType.Bomb: return CardSeries.Bomb;
                default: return CardSeries.None;
            }
        }

        /// <summary>
        /// カードのコピーを作成
        /// </summary>
        public Card Clone()
        {
            // 定義値（ID・段階・各種時間など）はそのまま保持し、可変の調律情報だけ深く複製する。
            var copy = (Card)MemberwiseClone();
            copy.Modifier = Modifier.Clone();
            return copy;
        }

        public override string ToString()
        {
            return $"{Name} (コスト:{Cost}, 威力:{Power}, {Element})";
        }
    }
}
