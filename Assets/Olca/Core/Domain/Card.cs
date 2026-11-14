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

        /// <summary>調律を反映した現在コスト。Costには元の定義値を保持する。</summary>
        public int GetFinalCost()
        {
            if (Modifier.ZeroCost) return 0;
            // 増減なしなら元から0の定義も保持する。通常調律による変更は最低1。
            if (Modifier.CostAdjustment == 0) return Cost;
            return System.Math.Max(1, checked(Cost + Modifier.CostAdjustment));
        }

        /// <summary>レアリティ</summary>
        public CardRarity Rarity { get; }

        /// <summary>カテゴリ</summary>
        public CardCategory Category { get; }

        public CardSubCategory SubCategory { get; }

        /// <summary>属性</summary>
        public ElementType Element { get; }
        public AttackProperty Properties { get; }
        public int? PoiseDamage { get; }
        /// <summary>各ヒットで発生させる無敵の外部指定期間。0は発生なし。</summary>
        public int HitInvincibilityMs { get; }

        /// <summary>系統（ソード系、ボム系など）</summary>
        public CardSeries Series { get; }

        /// <summary>威力（攻撃カードの場合）</summary>
        public int Power { get; private set; }
        /// <summary>使用者攻撃力の基準点。10000が100%。0は既存の固定威力契約。</summary>
        public int AttackPowerBasisPoints { get; private set; }

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
        public int? StartupFrames { get; private set; }
        public int? RecoveryFrames { get; private set; }
        public int? RecoveryCancelAfterFrames { get; private set; }
        public Card WithAttackTiming(PlayerAttackTiming timing)
        {
            if (timing == null) throw new System.ArgumentNullException(nameof(timing));
            var copy = Clone(); copy.StartupFrames = timing.StartupFrames; copy.RecoveryFrames = timing.RecoveryFrames;
            copy.RecoveryCancelAfterFrames = timing.CancelAfterFrames; return copy;
        }

        /// <summary>派生効果中も硬直を継続するか</summary>
        public bool ExtendRecoveryDuringDerivative { get; }

        /// <summary>暗転系カード（前隙・後隙なしで即座に発動）</summary>
        public bool IsInstantEffect { get; }

        /// <summary>調律による追加効果</summary>
        public CardModifier Modifier { get; private set; }

        /// <summary>超越グレード</summary>
        public ExceedLevel ExceedLevel { get; }

        /// <summary>再調律時に未定義の段階表を必要とすることを識別する。Clone/Undoにも保持する。</summary>
        public int TuningCount { get; private set; }

        /// <summary>付与履歴の判定。合算値が相殺されても段階評価が必要な事実を保持する。</summary>
        public bool HasStageDependentTuning { get; private set; }
        public TuningGrantDefinition TuningDefinition { get; private set; }
        private CardModifier tuningBaseModifier;
        private System.Collections.Generic.List<TuningGrantDefinition> tuningHistory = new System.Collections.Generic.List<TuningGrantDefinition>();
        public System.Collections.Generic.IReadOnlyList<TuningGrantDefinition> TuningHistory => tuningHistory.AsReadOnly();
        /// <summary>使用時だけのコピー。定義・保存・加工素材へ登録しない。</summary>
        public bool IsEffectCopy { get; private set; }
        // 効果copyだけの由来。保存/hashへ混ぜず、同IDの偽固定カードを実行しない。
        internal string EffectDefinitionHash { get; private set; }

        internal CardModifier GetTuningBaseModifier() => (tuningBaseModifier ?? Modifier).Clone();

        /// <summary>加工素材の同一性用。定義固有の効果は残し、付与された調律履歴だけを除く。</summary>
        internal Card CreateUntunedDefinitionCopy()
        {
            var copy = Clone();
            copy.Modifier = GetTuningBaseModifier();
            copy.tuningBaseModifier = null;
            copy.tuningHistory.Clear();
            copy.TuningCount = 0;
            copy.HasStageDependentTuning = false;
            return copy;
        }

        public Card WithTuningDefinition(TuningGrantDefinition definition)
        {
            var copy = Clone(); copy.TuningDefinition = definition; return copy;
        }

        internal Card WithEvaluatedTuning(CardModifier baseModifier,
            System.Collections.Generic.List<TuningGrantDefinition> history, CardModifier evaluated)
        {
            var copy = Clone();
            copy.tuningBaseModifier = baseModifier.Clone();
            copy.tuningHistory = new System.Collections.Generic.List<TuningGrantDefinition>(history);
            copy.TuningCount = history.Count;
            copy.HasStageDependentTuning = history.Exists(grant => grant.Effects.Count > 0);
            copy.Modifier = evaluated.Clone();
            return copy;
        }

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
            ExceedLevel exceedLevel = ExceedLevel.Normal,
            CardSubCategory subCategory = CardSubCategory.None, AttackProperty properties = AttackProperty.None, int? poiseDamage = null, int hitInvincibilityMs = 0, int? startupFrames = null, int? recoveryFrames = null, int? recoveryCancelAfterFrames = null, int attackPowerBasisPoints = 0)
        {
            if (float.IsNaN(startupTime) || float.IsInfinity(startupTime) || startupTime < 0 ||
                float.IsNaN(recoveryTime) || float.IsInfinity(recoveryTime) || recoveryTime < 0)
                throw new System.ArgumentOutOfRangeException("前後隙の秒値は有限の非負値が必要です");
            double recoveryLimit = recoveryFrames.HasValue ? recoveryFrames.Value : System.Math.Ceiling(recoveryTime * 60d - 0.00001d);
            if (startupFrames < 0 || recoveryFrames < 0 || recoveryCancelAfterFrames < 0 || recoveryCancelAfterFrames > recoveryLimit)
                throw new System.ArgumentOutOfRangeException("前後隙とキャンセル開始は有効なフレーム数が必要です");
            if(attackPowerBasisPoints<0)throw new System.ArgumentOutOfRangeException(nameof(attackPowerBasisPoints));
            if(attackPowerBasisPoints>0&&(category!=CardCategory.Attack||power<0))throw new System.ArgumentException("攻撃力基準は非負の攻撃カードに明示してください");
            AttackPowerBasisPoints=attackPowerBasisPoints;
            StartupFrames = startupFrames; RecoveryFrames = recoveryFrames; RecoveryCancelAfterFrames = recoveryCancelAfterFrames;
            if (poiseDamage < 0) throw new System.ArgumentOutOfRangeException(nameof(poiseDamage));
            if (hitInvincibilityMs < 0) throw new System.ArgumentOutOfRangeException(nameof(hitInvincibilityMs));
            PoiseDamage = poiseDamage; HitInvincibilityMs = hitInvincibilityMs;
            ElementRules.ValidateKnown(element);
            AttackPropertyRules.Validate(properties);
            Properties = properties;
            Id = id;
            Name = name;
            Cost = cost;
            Rarity = rarity;
            Category = category;
            SubCategory = subCategory;
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
            if(Modifier.ElementOverride.HasValue)ElementRules.ValidateKnown(Modifier.ElementOverride.Value);
            foreach (var effect in Modifier.AdditionalEffects) effect?.ValidateHitInvincibilityStages();
            ExceedLevel = exceedLevel;
        }

        /// <summary>
        /// 調律: 他のカードの効果をこのカードに付与する
        /// </summary>
        /// <param name="tuningCard">調律元のカード（アタック+10など）</param>
        /// <returns>調律後の新しいカードインスタンス</returns>
        public Card ApplyTuning(Card tuningCard)
        {
            if(IsEffectCopy||tuningCard?.IsEffectCopy==true||tuningCard?.AttackPowerBasisPoints>0)throw new System.NotSupportedException("使用時コピーまたは攻撃力基準カードを調律素材にできません");
            if (tuningCard == null) return this;

            // 調律元のカードから追加効果を取得
            CardModifier newModifier = this.Modifier.Merge(tuningCard.Modifier);

            // 新しいカードインスタンスを作成（元のカードは変更しない）
            return new Card(
                Id, Name, Cost, Rarity, Category, Element, Series, Power, Description,
                TargetType, PanelChangeType, MoveDirection, MoveDistance,
                newModifier, MoveMotionTime, AttackMotionTime, PenetrationMode,
                StartupTime, RecoveryTime, ExtendRecoveryDuringDerivative, IsInstantEffect,
                ExceedLevel, SubCategory, Properties, PoiseDamage, HitInvincibilityMs, StartupFrames, RecoveryFrames, RecoveryCancelAfterFrames, AttackPowerBasisPoints
            )
            {
                TuningCount = TuningCount + 1,
                tuningBaseModifier = GetTuningBaseModifier(),
                HasStageDependentTuning = HasStageDependentTuning || tuningCard.Modifier.HasStageDependentValues
            };
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
                ExceedLevel, SubCategory, Properties, PoiseDamage, HitInvincibilityMs, StartupFrames, RecoveryFrames, RecoveryCancelAfterFrames
            ) { IsEffectCopy = true, EffectDefinitionHash = this.EffectDefinitionHash };
        }

        /// <summary>
        /// 自己チェイン用の定義コピー。調律を再適用せず、再帰チェインを防ぐ。
        /// </summary>
        public Card CreateSelfChainCopy(int powerPercent)
        {
            if(AttackPowerBasisPoints!=0)throw new System.InvalidOperationException("自己派生の前に使用者攻撃力を解決してください");
            var copy = Clone();
            if (powerPercent > 0 && powerPercent != 100) {
                long value=(long)Power*powerPercent/100;
                if(value<int.MinValue||value>int.MaxValue)throw new System.OverflowException("自己派生の威力が範囲外です");
                copy.Power=(int)value;
            }
            copy.Modifier = new CardModifier();
            copy.IsEffectCopy = true;
            return copy;
        }

        /// <summary>
        /// 最終的な威力を計算（調律効果を含む）
        /// </summary>
        public int GetFinalPower()
        {
            if(AttackPowerBasisPoints!=0)throw new System.InvalidOperationException("使用者攻撃力が必要なカードです");
            int finalPower = (int)(Power * Modifier.DamageMultiplier);
            finalPower += Modifier.BonusDamage;
            return finalPower;
        }

        private int ResolvedBasePower(int actorAttackPower)
        {
            if(actorAttackPower<0)throw new System.ArgumentOutOfRangeException(nameof(actorAttackPower));
            if(AttackPowerBasisPoints==0)return Power;
            long value=checked((long)actorAttackPower*AttackPowerBasisPoints/10000L+Power);
            if(value<int.MinValue||value>int.MaxValue)throw new System.OverflowException("攻撃力基準の威力が範囲外です");return (int)value;
        }
        /// <summary>基準分を整数切捨てして固定加算し、その後に既存倍率・Bonusを一度ずつ適用する。</summary>
        public int GetFinalPower(int actorAttackPower)
        {
            int basis=ResolvedBasePower(actorAttackPower);
            if(AttackPowerBasisPoints==0)return GetFinalPower(); // 旧float丸め・加算契約を変更しない。
            float scaled=basis*Modifier.DamageMultiplier;
            if(float.IsNaN(scaled)||float.IsInfinity(scaled)||(double)scaled<int.MinValue||(double)scaled>int.MaxValue)throw new System.OverflowException("補正後の威力が範囲外です");
            return checked((int)scaled+Modifier.BonusDamage);
        }
        /// <summary>定義は変更せず、使用者を明示した効果専用コピーを作る。登録・保存へ戻さない。</summary>
        public Card ResolveAttackPower(int actorAttackPower)
        {
            GetFinalPower(actorAttackPower);var copy=Clone();copy.EffectDefinitionHash=EffectDefinitionHash??CollectionDisplay.DefinitionHash(CreateUntunedDefinitionCopy());copy.Power=ResolvedBasePower(actorAttackPower);copy.AttackPowerBasisPoints=0;copy.IsEffectCopy=true;return copy;
        }
        /// <summary>使用者基準の解決後に最終威力を一度だけ集中等の倍率で強化する。</summary>
        public Card WithPowerMultiplier(float multiplier,int actorAttackPower)
        {
            if(float.IsNaN(multiplier)||float.IsInfinity(multiplier)||multiplier<0)throw new System.ArgumentOutOfRangeException(nameof(multiplier));
            var resolved=ResolveAttackPower(actorAttackPower);float power=resolved.GetFinalPower()*multiplier;
            if(float.IsNaN(power)||float.IsInfinity(power)||(double)power<int.MinValue||(double)power>int.MaxValue)throw new System.OverflowException("使用時倍率の威力が範囲外です");
            return resolved.WithPowerMultiplier(multiplier);
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
        public static Card FromCurrentMaster(CardMaster master) {
            if(master==null)throw new System.ArgumentNullException(nameof(master));ElementRules.ValidateCurrent(master.Element);return FromMaster(master);
        }
        // 旧マスター/保存読取はWindを含む既知値を維持する。
        public static Card FromMaster(CardMaster master)
        {
            // SeriesTypeからCardSeriesに変換
            CardSeries series = ConvertSeriesType(master.Series);

            // Modifierの構築（追加効果を解析）
            CardModifier modifier = ParseAdditionalEffects(master);
            if (master.AdditionalEffectHitInvincibilityStages != null && !modifier.AdditionalEffects.Exists(e => e.EffectType == AdditionalEffectType.MultiHit))
                throw new System.ArgumentException("段ごとの被弾無敵設定には多段効果が必要です");

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
                master.IsInstantEffect,
                subCategory: master.SubCategory, properties: master.Properties, poiseDamage: master.PoiseDamage, hitInvincibilityMs: master.HitInvincibilityMs,
                startupFrames: master.StartupFrames, recoveryFrames: master.RecoveryFrames, recoveryCancelAfterFrames: master.RecoveryCancelAfterFrames
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
                        PoisonKind = master.PoisonKind, PoisonIntervalFrames = master.PoisonIntervalFrames,
                        HitCount = master.AdditionalEffectHitCount,
                        HitInvincibilityStages = effectType == AdditionalEffectType.MultiHit ? master.AdditionalEffectHitInvincibilityStages : null,
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
            copy.tuningBaseModifier = tuningBaseModifier?.Clone();
            copy.tuningHistory = new System.Collections.Generic.List<TuningGrantDefinition>(tuningHistory);
            return copy;
        }

        public override string ToString()
        {
            return $"{Name} (コスト:{Cost}, 威力:{(AttackPowerBasisPoints==0?Power.ToString():CollectionDisplay.PowerText(this))}, {Element})";
        }
    }
}

namespace Olca.Core.Domain
{
    /// <summary>初期編成向け試用v1。正式な性能表とは分離した明示定義。</summary>
    public static class TrialInitialCardDefinitions
    {
        public const string Shot="trial-shot-v1",Spear="trial-spear-v1",WideSword="trial-wide-sword-v1",AirShot="trial-air-shot-v1";
        private static readonly System.Collections.Generic.Dictionary<string,Card> definitions=Build();
        private static System.Collections.Generic.Dictionary<string,Card> Build()
        {
            var result=new System.Collections.Generic.Dictionary<string,Card>();
            void Add(string id,string name,int cost,bool shot){result.Add(id,new Card(id,name+"（カリ）",cost,CardRarity.Normal,CardCategory.Attack,ElementType.None,shot?CardSeries.None:CardSeries.Sword,0,
                "ショキカード（カリ） / コウゲキリョク100% / "+(shot?"チョクシン タンパツ":id==Spear?"ソードのゼンポウキョリ1.5バイ":"ソードのタテハバ1.5バイ"),
                targetType:DamageTargetType.FrontTile,properties:shot?AttackProperty.Shooting:AttackProperty.Sword,poiseDamage:0,startupFrames:6,recoveryFrames:12,attackPowerBasisPoints:10000));}
            Add(Shot,"ショット",1,true);Add(Spear,"スピア",2,false);Add(WideSword,"ワイドソード",2,false);Add(AirShot,"エアショット",1,true);return result;
        }
        public static Card Create(string id)=>id!=null&&definitions.TryGetValue(id,out var card)?card.Clone():null;
        public static bool IsId(string id)=>id!=null&&definitions.ContainsKey(id);
        public static bool IsShot(string id)=>id==Shot||id==AirShot;
        public static bool IsRegistered(Card card)
        {
            if(card==null||string.IsNullOrEmpty(card.Id)||card.IsEffectCopy||card.Modifier?.AdditionalEffects==null||System.Linq.Enumerable.Any(card.Modifier.AdditionalEffects,e=>e==null)||!definitions.TryGetValue(card.Id,out var definition))return false;
            try{return CollectionDisplay.DefinitionHash(card.CreateUntunedDefinitionCopy())==CollectionDisplay.DefinitionHash(definition);}
            catch(System.ArgumentException){return false;}
        }
        public static bool IsResolved(Card card)=>card!=null&&card.IsEffectCopy&&card.AttackPowerBasisPoints==0&&IsId(card.Id)&&definitions.TryGetValue(card.Id,out var definition)&&card.EffectDefinitionHash==CollectionDisplay.DefinitionHash(definition);
        public static System.Collections.Generic.IReadOnlyList<Card> All=>new[]{Create(Shot),Create(Spear),Create(WideSword),Create(AirShot)};
    }
}
