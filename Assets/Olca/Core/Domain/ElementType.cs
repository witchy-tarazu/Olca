namespace Olca.Core.Domain
{
    /// <summary>
    /// 属性タイプ
    /// </summary>
    public enum ElementType
    {
        /// <summary>無属性</summary>
        None,

        /// <summary>火属性</summary>
        Fire,

        /// <summary>水属性</summary>
        Water,

        /// <summary>風属性</summary>
        Wind,

        /// <summary>土属性</summary>
        Earth
    }
    /// <summary>属性・カード系統とは独立した攻撃性質。明示した定義だけで使用する。</summary>
    [System.Flags]
    public enum AttackProperty { None = 0, Sword = 1, Break = 2, Wind = 4 }

    /// <summary>外部指定のガード判定。発生元・期間・入力は所有しない。</summary>
    public sealed class BattleGuardState
    {
        public bool IsActive { get; private set; }
        public bool InterruptOnStagger { get; private set; }
        public void Set(bool active, bool interruptOnStagger)
        {
            IsActive = active; InterruptOnStagger = active && interruptOnStagger;
        }
        public void Clear() => Set(false, false);
        internal void OnStaggered() { if (InterruptOnStagger) Clear(); }
        public bool Blocks(int originalDamage, AttackProperty properties)
        {
            if (originalDamage < 0) throw new System.ArgumentOutOfRangeException(nameof(originalDamage));
            AttackPropertyRules.Validate(properties);
            return IsActive && originalDamage > 0 && (properties & AttackProperty.Break) == 0;
        }
    }

    /// <summary>敵味方共通。フレーム中は全ヒットを防ぎ、合計で終端の消滅を決める。</summary>
    internal sealed class BattleProtectionState
    {
        internal int BarrierHp { get; private set; }
        internal int AuraThreshold { get; private set; }
        private bool frameOpen, appliedThisFrame, expires;
        private long frameDamage;
        private FrameCountdown timer;
        private bool timed;
        private int displayDurationFrames;
        internal int? DisplayRemainingFrames => timed ? timer.Frames : (int?)null;
        internal int? DisplayDurationFrames => timed ? displayDurationFrames : (int?)null;
        internal void Set(int value, bool aura, int? durationFrames = null)
        {
            if (value < 0 || durationFrames <= 0) throw new System.ArgumentOutOfRangeException();
            BarrierHp = aura ? 0 : value; AuraThreshold = aura ? value : 0;
            timed = value > 0 && durationFrames.HasValue;
            displayDurationFrames = timed ? durationFrames.Value : 0;
            timer.SetFrames(timed ? durationFrames.Value : 0);
            frameDamage = 0; expires = false; appliedThisFrame = frameOpen;
        }
        internal void Clear() => Set(0, false);
        internal void BeginFrame() { frameOpen = true; appliedThisFrame = expires = false; frameDamage = 0; }
        // 同一フレームの防御合算でも、どのヒットで解除が確定したかを判定する。
        internal bool WillBreak => expires || (BarrierHp > 0 && frameDamage >= BarrierHp) ||
            (AuraThreshold > 0 && frameDamage >= AuraThreshold);
        internal void CommitFrame()
        {
            if (BarrierHp > 0) BarrierHp = (int)System.Math.Max(0L, BarrierHp - frameDamage);
            if (AuraThreshold > 0 && frameDamage >= AuraThreshold) AuraThreshold = 0;
            frameOpen = false; frameDamage = 0;
            if (expires || (BarrierHp == 0 && AuraThreshold == 0)) Clear();
        }
        internal bool Block(int damage, bool grass, AttackProperty properties = AttackProperty.None)
        {
            if (BarrierHp == 0 && AuraThreshold == 0) return false;
            if ((properties & AttackProperty.Wind) != 0)
            {
                // 剥離するフレームも防御を保持し、次フレームから本体へ通す。
                if (frameOpen) expires = true; else Clear();
                // 元から威力0の状態専用ヒットは従来どおり防御しない。
                return damage > 0;
            }
            if (damage <= 0) return false;
            int adjusted = ElementResistances.Neutral.CalculateDamage(damage, ElementType.None, grass);
            if (frameOpen) frameDamage = System.Math.Min(int.MaxValue, frameDamage + adjusted);
            else
            {
                if (BarrierHp > 0) BarrierHp = System.Math.Max(0, BarrierHp - adjusted);
                if (AuraThreshold > 0 && adjusted >= AuraThreshold) AuraThreshold = 0;
            }
            return true;
        }
        internal void Tick(int ms, bool logical)
        {
            if (!timed || appliedThisFrame) return;
            if (logical) timer.TickFrame(); else timer.Tick(ms);
            if (!timer.IsActive) { if (frameOpen) expires = true; else Clear(); }
        }
    }

    internal static class AttackPropertyRules
    {
        internal static void Validate(AttackProperty value)
        {
            if ((value & ~(AttackProperty.Sword | AttackProperty.Break | AttackProperty.Wind)) != 0)
                throw new System.ArgumentOutOfRangeException(nameof(value), "未定義の攻撃性質です");
        }
    }

    /// <summary>解除用弱点は属性か単一性質のどちらか一つ。耐性・倍率とは独立する。</summary>
    public sealed class TransformationWeakness
    {
        public ElementType Element { get; }
        public AttackProperty Property { get; }
        public TransformationWeakness(ElementType element)
        {
            if (element == ElementType.None || !System.Enum.IsDefined(typeof(ElementType), element))
                throw new System.ArgumentOutOfRangeException(nameof(element));
            Element = element;
        }
        public TransformationWeakness(AttackProperty property)
        {
            if (property != AttackProperty.Sword && property != AttackProperty.Break)
                throw new System.ArgumentOutOfRangeException(nameof(property), "解除用性質は一つだけ指定します");
            Property = property;
        }
        public bool Matches(ElementType element, AttackProperty properties)
            => Element != ElementType.None ? Element == element : (properties & Property) != 0;
    }

    /// <summary>本人属性と独立した耐性。負側のゲーム上の下限は設けない。</summary>
    public sealed class ElementResistances
    {
        public static readonly ElementResistances Neutral = new ElementResistances();
        public int Fire { get; }
        public int Water { get; }
        public int Wind { get; }
        public int Earth { get; }
        public ElementResistances(int fire = 0, int water = 0, int wind = 0, int earth = 0)
        {
            // 元値は旧保存・定義hashとの照合用に保持し、有効値はGetで制限する。
            Fire = fire; Water = water; Wind = wind; Earth = earth;
        }
        private static int Clamp(long value) => (int)System.Math.Max(-100L, System.Math.Min(100L, value));
        public int Get(ElementType element) => Clamp(element == ElementType.Fire ? Fire :
            element == ElementType.Water ? Water : element == ElementType.Wind ? Wind :
            element == ElementType.Earth ? Earth : 0);
        /// <summary>同属性の全補正を合算してから一度だけ上下限を適用する。</summary>
        public static ElementResistances Combine(params ElementResistances[] values)
        {
            if(values==null)throw new System.ArgumentNullException(nameof(values));
            return CombineSources(Neutral,values);
        }
        internal static ElementResistances CombineSources(ElementResistances baseline,System.Collections.Generic.IReadOnlyList<ElementResistances> values)
        {
            if(baseline==null)throw new System.ArgumentException("耐性補正がnullです",nameof(values));
            long fire=baseline.Fire,water=baseline.Water,wind=baseline.Wind,earth=baseline.Earth;
            for(int i=0;i<values.Count;i++){
                var value=values[i];
                if(value==null)throw new System.ArgumentException("耐性補正がnullです",nameof(values));
                fire+=value.Fire;water+=value.Water;wind+=value.Wind;earth+=value.Earth;
            }
            return new ElementResistances(Clamp(fire),Clamp(water),Clamp(wind),Clamp(earth));
        }
        public double Multiplier(ElementType element) => (100L - Get(element)) / 100d;
        public bool IsNeutral => Fire == 0 && Water == 0 && Wind == 0 && Earth == 0;
        public bool HasSameValues(ElementResistances other) => other != null &&
            Fire == other.Fire && Water == other.Water && Wind == other.Wind && Earth == other.Earth;
        public int CalculateDamage(int damage, ElementType element, bool fireOnGrass)
        {
            if (damage < 0) throw new System.ArgumentOutOfRangeException(nameof(damage));
            // 正の整数ダメージを最後に一度だけ切り捨てる。乗算途中で端数を落とさない。
            decimal result = (decimal)damage * (100L - Get(element)) * (fireOnGrass ? 2 : 1) / 100m;
            return result <= 0 ? 0 : (int)System.Math.Max(1m, System.Math.Min(int.MaxValue, result));
        }
    }
    /// <summary>外部定義から受け取るIDと被弾特性。製品ID・解放条件は定義しない。</summary>
    public sealed class TransformationDefenseDefinition
    {
        public string Id { get; }
        public int HandSizeBonus { get; }
        public TransformationAttackDefinition Attack { get; }
        private readonly System.Collections.Generic.Dictionary<string, TransformationCardConversion> cardConversions
            = new System.Collections.Generic.Dictionary<string, TransformationCardConversion>(System.StringComparer.Ordinal);
        internal Card ConvertHandCard(Card card) => cardConversions.TryGetValue(card.Id, out var rule) ? rule.CreateCard() : null;
        public ElementResistances Resistances { get; }
        public TransformationWeakness Weakness { get; }
        public ElementType BreakElement => Weakness.Element;
        public TransformationDefenseDefinition(string id, ElementResistances resistances, ElementType breakElement, int handSizeBonus = 0, System.Collections.Generic.IEnumerable<TransformationCardConversion> cardConversions = null, TransformationAttackDefinition attack = null)
            : this(id, resistances, new TransformationWeakness(breakElement), handSizeBonus, cardConversions, attack) { }
        public TransformationDefenseDefinition(string id, ElementResistances resistances, AttackProperty breakProperty, int handSizeBonus = 0, System.Collections.Generic.IEnumerable<TransformationCardConversion> cardConversions = null, TransformationAttackDefinition attack = null)
            : this(id, resistances, new TransformationWeakness(breakProperty), handSizeBonus, cardConversions, attack) { }
        public TransformationDefenseDefinition(string id, ElementResistances resistances, TransformationWeakness weakness, int handSizeBonus = 0, System.Collections.Generic.IEnumerable<TransformationCardConversion> cardConversions = null, TransformationAttackDefinition attack = null)
        {
            if (handSizeBonus < 0) throw new System.ArgumentOutOfRangeException(nameof(handSizeBonus));
            HandSizeBonus = handSizeBonus;
            Attack = attack;
            foreach (var rule in cardConversions ?? System.Array.Empty<TransformationCardConversion>()) {
                if (rule == null) throw new System.ArgumentException("変換定義がnullです", nameof(cardConversions));
                this.cardConversions.Add(rule.SourceCardId, rule);
            }
            if (string.IsNullOrWhiteSpace(id)) throw new System.ArgumentException("変身IDが必要です", nameof(id));
            Id = id;
            Resistances = resistances ?? throw new System.ArgumentNullException(nameof(resistances));
            Weakness = weakness ?? throw new System.ArgumentNullException(nameof(weakness));
        }
    }
    /// <summary>外部指定の一段置換。テンプレートを深く保持し、手札個体ごとに複製する。</summary>
    public sealed class TransformationCardConversion
    {
        public string SourceCardId { get; }
        private readonly Card template;
        public TransformationCardConversion(string sourceCardId, Card targetTemplate)
        {
            if (string.IsNullOrWhiteSpace(sourceCardId)) throw new System.ArgumentException("変換元IDが必要です", nameof(sourceCardId));
            SourceCardId = sourceCardId;
            template = (targetTemplate ?? throw new System.ArgumentNullException(nameof(targetTemplate))).Clone();
        }
        internal Card CreateCard() => template.Clone();
    }

    /// <summary>既存の前方1マス攻撃に渡す変身専用性能。製品値・専用モーションは持たない。</summary>
    public sealed class TransformationAttackDefinition
    {
        public ElementType Element { get; }
        public int NormalPowerPercent { get; }
        public int ChargedPowerPercent { get; }
        public int ChargeFrames { get; }
        public int IntervalFrames { get; }
        public PlayerAttackTiming Timing { get; }
        public TransformationAttackDefinition(ElementType element, int normalPowerPercent = 100,
            int chargedPowerPercent = 200, int chargeFrames = 60, int intervalFrames = 12, PlayerAttackTiming timing = null)
        {
            if (!System.Enum.IsDefined(typeof(ElementType), element)) throw new System.ArgumentOutOfRangeException(nameof(element));
            if (normalPowerPercent < 0 || chargedPowerPercent < 0 || chargeFrames < 1 || intervalFrames < 1)
                throw new System.ArgumentOutOfRangeException("攻撃率は非負、期間は正の論理フレーム数が必要です");
            Element = element; NormalPowerPercent = normalPowerPercent; ChargedPowerPercent = chargedPowerPercent;
            ChargeFrames = chargeFrames; IntervalFrames = intervalFrames; Timing = timing;
        }
    }

    public enum PlayerAttackPhase { None, Startup, Executing, Recovery }
    /// <summary>論理フレーム指定の前後隙。未指定のキャンセル窓は終了まで受付不可。</summary>
    public sealed class PlayerAttackTiming
    {
        public int StartupFrames { get; }
        public int RecoveryFrames { get; }
        public int? CancelAfterFrames { get; }
        public PlayerAttackTiming(int startupFrames = 0, int recoveryFrames = 0, int? cancelAfterFrames = null)
        {
            if (startupFrames < 0 || recoveryFrames < 0 || cancelAfterFrames < 0 || cancelAfterFrames > recoveryFrames)
                throw new System.ArgumentOutOfRangeException("前後隙とキャンセル開始は有効な論理フレーム数が必要です");
            StartupFrames = startupFrames; RecoveryFrames = recoveryFrames; CancelAfterFrames = cancelAfterFrames;
        }
    }

    /// <summary>出撃前に登録する既存スタイル。解放は変身と別で、保存済みのクリア履歴から判定する。</summary>
    public sealed class DepartureStyleDefinition
    {
        public const string BasicId = "basic";
        public static DepartureStyleDefinition Basic { get; } = new DepartureStyleDefinition(BasicId, "基本 / 無属性", null);
        public string Id { get; }
        public string Name { get; }
        public string RequiredClear { get; }
        public TransformationAttackDefinition Attack { get; }
        public int TransformationPhaseBonus { get; }
        public DepartureStyleDefinition(string id, string name, string requiredClear, TransformationAttackDefinition attack = null, int transformationPhaseBonus = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || (requiredClear != null && string.IsNullOrWhiteSpace(requiredClear)))
                throw new System.ArgumentException("スタイルID・名称・解放条件が不正です");
            if(transformationPhaseBonus<0||transformationPhaseBonus>PlayerCharacter.MaxTransformationPhaseBonus)
                throw new System.ArgumentOutOfRangeException(nameof(transformationPhaseBonus));
            if (id == BasicId && (requiredClear != null || attack != null)) throw new System.ArgumentException("基本スタイルは既存の無属性攻撃を維持します");
            Id = id; Name = name; RequiredClear = requiredClear; Attack = attack; TransformationPhaseBonus=transformationPhaseBonus;
        }
        public bool IsUnlocked(ClearReturnProfile profile) => RequiredClear == null || System.Array.IndexOf(profile?.ClearedDungeons ?? System.Array.Empty<string>(), RequiredClear) >= 0;
        public DepartureStyleDto Capture() => new DepartureStyleDto { Id = Id, Name = Name, Element = Attack?.Element ?? ElementType.None,
            TransformationPhaseBonusVersion=TransformationPhaseBonus==0?(int?)null:1, TransformationPhaseBonus=TransformationPhaseBonus==0?(int?)null:TransformationPhaseBonus,
            HasAttack = Attack != null, NormalPercent = Attack?.NormalPowerPercent ?? 100, ChargedPercent = Attack?.ChargedPowerPercent ?? 200,
            ChargeFrames = Attack?.ChargeFrames ?? 60, IntervalFrames = Attack?.IntervalFrames ?? 12,
            HasTiming = Attack?.Timing != null, StartupFrames = Attack?.Timing?.StartupFrames ?? 0,
            RecoveryFrames = Attack?.Timing?.RecoveryFrames ?? 0, CancelAfterFrames = Attack?.Timing?.CancelAfterFrames };
    }
    // 攻略開始時点の値を固定し、ロード時に現在の登録値へ差し替えない。
    public sealed class DepartureStyleDto
    {
        public string Id, Name;
        public ElementType Element;
        public bool HasAttack, HasTiming;
        public int NormalPercent, ChargedPercent, ChargeFrames, IntervalFrames, StartupFrames, RecoveryFrames;
        public int? CancelAfterFrames;
        public int? TransformationPhaseBonusVersion,TransformationPhaseBonus;
        public bool HasTransformationPhaseBonusRecord => TransformationPhaseBonusVersion.HasValue||TransformationPhaseBonus.HasValue;
        public int ResolveTransformationPhaseBonus()
        {
            if(!HasTransformationPhaseBonusRecord)return 0; // 旧保存省略は延長なし。
            if(TransformationPhaseBonusVersion!=1||!TransformationPhaseBonus.HasValue||TransformationPhaseBonus<0||TransformationPhaseBonus>PlayerCharacter.MaxTransformationPhaseBonus)
                throw new System.ArgumentException("変身持続延長の版または値が不正です");
            return TransformationPhaseBonus.Value;
        }
        public DepartureStyleDto Copy() => (DepartureStyleDto)MemberwiseClone();
        public DepartureStyleDefinition Restore()
        {
            if (!System.Enum.IsDefined(typeof(ElementType), Element) || (!HasAttack && (Element != ElementType.None || HasTiming)))
                throw new System.ArgumentException("スタイル属性・攻撃定義が不正です");
            var timing = new PlayerAttackTiming(StartupFrames, RecoveryFrames, CancelAfterFrames);
            return new DepartureStyleDefinition(Id, Name, null, HasAttack ? new TransformationAttackDefinition(Element, NormalPercent,
                ChargedPercent, ChargeFrames, IntervalFrames, HasTiming ? timing : null) : null, ResolveTransformationPhaseBonus());
        }
    }

}

namespace Olca.Core.Domain
{
    /// <summary>出撃時に固定する節目成長。加算履歴を持たず、共通レベルから一度だけ解決する。</summary>
    public sealed class AttackMilestoneGrowth
    {
        public int Level { get; }
        public int ChargePercent { get; }
        public int IntervalReduction { get; }
        public int RangeBonus { get; }
        public AttackMilestoneGrowth(int level,int chargePercent,int intervalReduction,int rangeBonus=0)
        {
            if(level<1||chargePercent<1||chargePercent>100||intervalReduction<0||rangeBonus<0||rangeBonus>4)throw new System.ArgumentException("節目成長の固定値が不正です");
            Level=level;ChargePercent=chargePercent;IntervalReduction=intervalReduction;RangeBonus=rangeBonus;
        }
        public static AttackMilestoneGrowth ForLevel(int level)=>new AttackMilestoneGrowth(level,level>=10?80:level>=5?90:100,level>=10?2:level>=5?1:0,level>=10?1:0);
        public int ChargeFrames(int frames)=>System.Math.Max(1,(int)((long)frames*ChargePercent/100));
        public int IntervalFrames(int frames)=>System.Math.Max(1,frames-IntervalReduction);
        public AttackMilestoneDto Capture()=>new AttackMilestoneDto{Level=Level,ChargePercent=ChargePercent,IntervalReduction=IntervalReduction,RangeBonus=RangeBonus};
    }
    public sealed class AttackMilestoneDto
    {
        public int Level,ChargePercent,IntervalReduction,RangeBonus;
        public AttackMilestoneDto Copy()=>(AttackMilestoneDto)MemberwiseClone();
        public AttackMilestoneGrowth Restore()=>new AttackMilestoneGrowth(Level,ChargePercent,IntervalReduction,RangeBonus);
    }
}
