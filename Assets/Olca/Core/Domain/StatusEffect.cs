using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// 状態異常の種類
    /// </summary>
    public enum StatusEffectType
    {
        None,
        Paralyze,   // 麻痺: 行動不能
        Poison,     // 毒: 継続ダメージ
        Confusion,  // 混乱: 操作反転
        Blind,      // 盲目
    }

    /// <summary>既存の時間制の毒・麻痺に対する軽減率。属性耐性とは独立する。</summary>
    public sealed class TimedStatusResistances
    {
        public static readonly TimedStatusResistances Neutral = new TimedStatusResistances();
        public int Poison { get; }
        public int Paralyze { get; }
        public bool IsNeutral => Poison == 0 && Paralyze == 0;
        public TimedStatusResistances(int poison = 0, int paralyze = 0)
        {
            if (poison < 0 || paralyze < 0) throw new ArgumentOutOfRangeException("状態異常耐性は非負です");
            Poison = poison; Paralyze = paralyze;
        }
        public int Get(StatusEffectType type) => type == StatusEffectType.Poison ? Poison
            : type == StatusEffectType.Paralyze ? Paralyze : 0;
        public bool HasSameValues(TimedStatusResistances other)
            => other != null && Poison == other.Poison && Paralyze == other.Paralyze;
        public static TimedStatusResistances Combine(params TimedStatusResistances[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            long poison = 0, paralyze = 0;
            foreach (var value in values)
            {
                if (value == null) throw new ArgumentNullException(nameof(values));
                poison += value.Poison; paralyze += value.Paralyze;
            }
            return new TimedStatusResistances((int)Math.Min(int.MaxValue, poison), (int)Math.Min(int.MaxValue, paralyze));
        }
    }

    /// <summary>
    /// 外部定義の単一毒の時間状態。HP反映・他状態との共存・探索持越しは所有しない。
    /// </summary>
    public sealed class TimedPoisonState
    {
        private sealed class Application
        {
            internal int Frames, Damage, Interval;
        }
        private Application active, pending;
        private long remainingUnits, nextDamageUnits;
        private bool frameOpen, clearPending;
        public bool IsActive => active != null && remainingUnits > 0;
        public bool HasPendingApplication => pending != null;
        public int RemainingFrames => (int)((remainingUnits + 49) / 50);
        public int FramesUntilDamage => active == null ? 0 : (int)((nextDamageUnits + 49) / 50);

        /// <summary>期間と量は既存効果から取得し、独立した間隔と終了時解除を明示させる。</summary>
        public void Apply(StatusEffect effect, int intervalFrames, bool clearAtBattleEnd)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (effect.Type != StatusEffectType.Poison || effect.DamagePerTick < 0 || intervalFrames <= 0)
                throw new ArgumentException("毒定義・非負の量・正の間隔が必要です");
            if (!clearAtBattleEnd) throw new NotSupportedException("探索・階層への毒持越しは未接続です");
            // 耐性で0フレームとなった付与では、既存毒を解除・更新しない。
            if (effect.RemainingFrames <= 0) return;
            var definition = pending ?? active;
            if (definition != null && (definition.Damage != effect.DamagePerTick || definition.Interval != intervalFrames))
                throw new InvalidOperationException("同種毒の再付与で量・間隔を変更する定義は未対応です");
            pending = new Application { Frames=effect.RemainingFrames, Damage=effect.DamagePerTick, Interval=intervalFrames };
            if (!frameOpen) clearPending = false;
        }
        public void Clear() { pending = null; clearPending = true; }
        public void BeginFrame()
        {
            if (frameOpen) throw new InvalidOperationException("毒フレームが既に開始されています");
            CommitChanges(); frameOpen = true;
        }
        /// <summary>確定毒量を返す。呼出し元がポーズ中は呼ばず、HP処理は別途行う。</summary>
        public int TickFrame() => Advance(50);
        public int TickMilliseconds(int milliseconds)
        {
            if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            return Advance(milliseconds * 3L);
        }
        private int Advance(long elapsedUnits)
        {
            if (!frameOpen) throw new InvalidOperationException("毒更新にはフレーム境界が必要です");
            if (!IsActive || elapsedUnits == 0) return 0;
            long elapsed = Math.Min(remainingUnits, elapsedUnits), pulses = 0;
            if (elapsed >= nextDamageUnits) {
                long intervalUnits = active.Interval * 50L;
                pulses = 1 + (elapsed - nextDamageUnits) / intervalUnits;
                nextDamageUnits = intervalUnits - (elapsed - nextDamageUnits) % intervalUnits;
            } else nextDamageUnits -= elapsed;
            remainingUnits -= elapsed;
            return (int)Math.Min(int.MaxValue, pulses * active.Damage);
        }
        public void EndFrame()
        {
            if (!frameOpen) throw new InvalidOperationException("毒フレームが開始されていません");
            CommitChanges(); frameOpen = false;
        }
        private void CommitChanges()
        {
            if (clearPending) { active = null; remainingUnits = nextDamageUnits = 0; }
            else if (pending != null) {
                // 期限と再付与が同時でも、同種の次回ダメージまでの進行は引き継ぐ。
                if (active == null) nextDamageUnits = pending.Interval * 50L;
                active = pending; remainingUnits = pending.Frames * 50L;
            }
            pending = null; clearPending = false;
            if (remainingUnits <= 0) { active = null; nextDamageUnits = 0; }
        }
        /// <summary>死亡・終了時解除指定の戦闘終了で使用。付与予約も破棄する。</summary>
        public void Reset()
        {
            active = pending = null; remainingUnits = nextDamageUnits = 0;
            clearPending = false;
        }
    }

    /// <summary>
    /// 状態異常インスタンス
    /// </summary>
    public class StatusEffect
    {
        /// <summary>状態異常の種類</summary>
        public StatusEffectType Type { get; }

        /// <summary>残り時間（ミリ秒）</summary>
        public int RemainingMs { get; private set; }

        public int RemainingFrames { get; private set; }

        /// <summary>毒の場合の1ティックあたりのダメージ</summary>
        public int DamagePerTick { get; }
        private int originalFrames, initialReducedFrames, appliedResistance;

        // 生成側の既存軽減率とも加算する。共有された付与元を変更せず、対象ごとに独立させる。
        internal StatusEffect WithTargetResistance(int resistance)
        {
            if (resistance == 0) return this;
            var copy = (StatusEffect)MemberwiseClone();
            copy.appliedResistance = (int)Math.Min(100L, (long)appliedResistance + resistance);
            copy.initialReducedFrames = (int)((long)originalFrames * (100 - copy.appliedResistance) / 100);
            int elapsedFrames = Math.Max(0, initialReducedFrames - RemainingFrames);
            copy.RemainingFrames = Math.Max(0, copy.initialReducedFrames - elapsedFrames);
            copy.RemainingMs = (int)Math.Min(int.MaxValue, ((long)copy.RemainingFrames * 1000 + 59) / 60);
            return copy;
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="type">状態異常の種類</param>
        /// <param name="durationMs">持続時間（ミリ秒）</param>
        /// <param name="damagePerTick">毒ダメージ（ミリ秒ごと）。毒以外は0</param>
        public StatusEffect(StatusEffectType type, int durationMs, int damagePerTick = 0, int durationResistancePercent = 0)
        {
            if (durationMs <= 0) throw new ArgumentException("持続時間は1以上である必要があります", nameof(durationMs));
            if (durationResistancePercent < 0) throw new ArgumentOutOfRangeException(nameof(durationResistancePercent));
            Type = type;
            long frames = ((long)durationMs * 60 + 999) / 1000;
            originalFrames = (int)frames;
            appliedResistance = Math.Min(100, durationResistancePercent);
            RemainingFrames = initialReducedFrames = (int)(frames * (100 - appliedResistance) / 100);
            // 既存ms APIは耐性なしの場合の値を維持。論理更新では整数フレームを使用する。
            RemainingMs = durationResistancePercent == 0 ? durationMs : (int)Math.Min(int.MaxValue, ((long)RemainingFrames * 1000 + 59) / 60);
            DamagePerTick = damagePerTick;
        }

        /// <summary>
        /// 時間を経過させる。返り値がtrueなら消滅
        /// </summary>
        /// <param name="elapsedMs">経過時間（ミリ秒）</param>
        /// <returns>消滅するか</returns>
        public bool TickFrame()
        {
            RemainingFrames = Math.Max(0, RemainingFrames - 1);
            RemainingMs = (int)Math.Min(int.MaxValue, ((long)RemainingFrames * 1000 + 59) / 60);
            return RemainingFrames == 0;
        }

        public bool Tick(int elapsedMs)
        {
            RemainingMs -= elapsedMs;
            RemainingFrames = (int)(((long)Math.Max(0, RemainingMs) * 60 + 999) / 1000);
            return RemainingMs <= 0;
        }
    }
}

namespace Olca.Core.Domain
{
    /// <summary>
    /// 旧ms入口と60Hz入口で共用する残り時間。1/3000秒を一単位とし、
    /// 1フレームは常に50単位減算する。表示用msへの切上げ値を演算へ戻さない。
    /// </summary>
    internal struct FrameCountdown
    {
        private long remaining;
        public bool IsActive => remaining > 0;
        public int Milliseconds => (int)System.Math.Min(int.MaxValue, (remaining + 2) / 3);
        public int Frames => (int)((remaining + 49) / 50);
        public void SetFrames(int value) => remaining = System.Math.Max(0L, value * 50L);
        public void SetMilliseconds(int value) => remaining = System.Math.Max(0L, value * 3L);
        public void ExtendMilliseconds(int value) => remaining = System.Math.Max(remaining, value * 3L);
        public void Tick(int milliseconds) => remaining = System.Math.Max(0, remaining - milliseconds * 3L);
        public void TickFrame() => remaining = System.Math.Max(0, remaining - 50);
    }
}
