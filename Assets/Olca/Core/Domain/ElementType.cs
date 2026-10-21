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
        internal void Set(int value, bool aura, int? durationFrames = null)
        {
            if (value < 0 || durationFrames <= 0) throw new System.ArgumentOutOfRangeException();
            BarrierHp = aura ? 0 : value; AuraThreshold = aura ? value : 0;
            timed = value > 0 && durationFrames.HasValue;
            timer.SetFrames(timed ? durationFrames.Value : 0);
            frameDamage = 0; expires = false; appliedThisFrame = frameOpen;
        }
        internal void Clear() => Set(0, false);
        internal void BeginFrame() { frameOpen = true; appliedThisFrame = expires = false; frameDamage = 0; }
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
            if (values == null) throw new System.ArgumentNullException(nameof(values));
            long fire = 0, water = 0, wind = 0, earth = 0;
            foreach (var value in values)
            {
                if (value == null) throw new System.ArgumentException("耐性補正がnullです", nameof(values));
                fire += value.Fire; water += value.Water; wind += value.Wind; earth += value.Earth;
            }
            return new ElementResistances(Clamp(fire), Clamp(water), Clamp(wind), Clamp(earth));
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
    /// <summary>外部定義から受け取るIDと被弾特性。製品ID・解放条件・カード変換は定義しない。</summary>
    public sealed class TransformationDefenseDefinition
    {
        public string Id { get; }
        public int HandSizeBonus { get; }
        public ElementResistances Resistances { get; }
        public TransformationWeakness Weakness { get; }
        public ElementType BreakElement => Weakness.Element;
        public TransformationDefenseDefinition(string id, ElementResistances resistances, ElementType breakElement, int handSizeBonus = 0)
            : this(id, resistances, new TransformationWeakness(breakElement), handSizeBonus) { }
        public TransformationDefenseDefinition(string id, ElementResistances resistances, AttackProperty breakProperty, int handSizeBonus = 0)
            : this(id, resistances, new TransformationWeakness(breakProperty), handSizeBonus) { }
        public TransformationDefenseDefinition(string id, ElementResistances resistances, TransformationWeakness weakness, int handSizeBonus = 0)
        {
            if (handSizeBonus < 0) throw new System.ArgumentOutOfRangeException(nameof(handSizeBonus));
            HandSizeBonus = handSizeBonus;
            if (string.IsNullOrWhiteSpace(id)) throw new System.ArgumentException("変身IDが必要です", nameof(id));
            Id = id;
            Resistances = resistances ?? throw new System.ArgumentNullException(nameof(resistances));
            Weakness = weakness ?? throw new System.ArgumentNullException(nameof(weakness));
        }
    }
}
