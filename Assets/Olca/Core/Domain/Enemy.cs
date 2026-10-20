using System;

namespace Olca.Core.Domain
{
    /// <summary>外部指定の回復待ち。フレーム内の被弾と期限成立を終端で調停する。</summary>
    internal sealed class PoiseRecoveryState
    {
        internal int? DelayFrames { get; private set; }
        internal int RemainingFrames => timer.Frames;
        private FrameCountdown timer;
        private bool frameOpen, hit, expired;
        internal void Configure(int? frames)
        {
            if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
            DelayFrames = frames; timer.SetFrames(0); hit = expired = false;
        }
        internal void BeginFrame() { frameOpen = true; hit = expired = false; }
        internal void Hit(int impact)
        {
            if (impact <= 0 || !DelayFrames.HasValue) return;
            if (frameOpen) hit = true; else timer.SetFrames(DelayFrames.Value);
        }
        internal bool Tick(int ms, bool logical)
        {
            if (hit || !timer.IsActive) return false;
            if (logical) timer.TickFrame(); else timer.Tick(ms);
            if (timer.IsActive) return false;
            if (frameOpen) { expired = true; return false; }
            return true;
        }
        internal bool CommitFrame()
        {
            bool recover = expired && !hit;
            if (hit && DelayFrames.HasValue) timer.SetFrames(DelayFrames.Value);
            frameOpen = false; hit = expired = false; return recover;
        }
        internal void Clear() { Configure(null); frameOpen = false; }
    }

    /// <summary>製品値を持たない外部指定の敵強靭度。期間は被弾した攻撃によらず共通。</summary>
    public sealed class EnemyPoiseDefinition
    {
        public int Maximum { get; }
        public int StaggerFrames { get; }
        public int? RecoveryDelayFrames { get; }
        public EnemyPoiseDefinition(int maximum, int staggerFrames, int? recoveryDelayFrames = null)
        {
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (staggerFrames <= 0) throw new ArgumentOutOfRangeException(nameof(staggerFrames));
            if (recoveryDelayFrames <= 0) throw new ArgumentOutOfRangeException(nameof(recoveryDelayFrames));
            Maximum = maximum; StaggerFrames = staggerFrames; RecoveryDelayFrames = recoveryDelayFrames;
        }
        public bool HasSameValues(EnemyPoiseDefinition other)
            => other != null && Maximum == other.Maximum && StaggerFrames == other.StaggerFrames && RecoveryDelayFrames == other.RecoveryDelayFrames;
    }

    /// <summary>
    /// 敵キャラクター
    /// </summary>
    public class Enemy
    {
        /// <summary>戦闘限定の地形特性。戦闘終了時に解除する。</summary>
        public BattleTerrainState TerrainState { get; } = new BattleTerrainState();

        public BattleGuardState Guard { get; } = new BattleGuardState();
        private readonly BattleProtectionState protection = new BattleProtectionState();
        public int BarrierHp => protection.BarrierHp;
        public bool HasBarrier => BarrierHp > 0;
        public int AuraThreshold => protection.AuraThreshold;
        public bool HasAura => AuraThreshold > 0;
        public void SetBarrier(int hp, int? durationFrames = null) => protection.Set(Math.Max(0, hp), false, durationFrames);
        public void SetAura(int threshold, int? durationFrames = null) => protection.Set(threshold, true, durationFrames);
        internal void ClearProtection() => protection.Clear();
        internal void TickProtection(int ms, bool logical) => protection.Tick(ms, logical);
        private FrameCountdown invincibilityTimer;
        private bool frameInvincible;
        private int pendingInvincibilityMs;
        public bool IsInvincible => frameHp.Active ? frameInvincible : invincibilityTimer.IsActive;
        public void SetInvincible(int durationMs)
        {
            if (durationMs <= 0) return;
            if (frameHp.Active) { pendingInvincibilityMs = Math.Max(pendingInvincibilityMs, durationMs); return; }
            invincibilityTimer.ExtendMilliseconds(durationMs);
        }
        internal void TickInvincible(int ms, bool logical)
        { if (logical) invincibilityTimer.TickFrame(); else invincibilityTimer.Tick(ms); }
        internal void ClearInvincibility() { invincibilityTimer.SetFrames(0); pendingInvincibilityMs = 0; frameInvincible = false; }
        public ElementResistances Resistances { get; }
        public TimedStatusResistances StatusResistances { get; }
        public EnemyPoiseDefinition PoiseDefinition { get; }
        public int? Poise { get; private set; }
        public int AccumulatedPoiseDamage { get; private set; }
        private readonly PoiseRecoveryState poiseRecovery = new PoiseRecoveryState();
        public int? PoiseRecoveryDelayFrames => poiseRecovery.DelayFrames;
        public int PoiseRecoveryRemainingFrames => poiseRecovery.RemainingFrames;
        public void ConfigurePoiseRecovery(int? delayFrames)
        {
            if (PoiseDefinition == null || AccumulatedPoiseDamage != 0 || poiseRecovery.RemainingFrames > 0)
                throw new InvalidOperationException("回復待ち定義は強靭度を持つ戦闘開始状態で指定してください");
            poiseRecovery.Configure(delayFrames);
        }
        internal void TickPoiseRecovery(int ms, bool logical) { if (poiseRecovery.Tick(ms, logical)) AccumulatedPoiseDamage = 0; }
        internal void ClearPoiseRecovery()
        {
            poiseRecovery.Clear(); poiseRecovery.Configure(PoiseDefinition?.RecoveryDelayFrames);
            AccumulatedPoiseDamage = 0;
        }
        public bool IsStaggered => staggerTimer.IsActive;
        public int StaggerRemainingFrames => staggerTimer.Frames;
        private FrameCountdown staggerTimer;
        private long framePoiseDamage;
        private bool frameStaggered;
        public event Action OnStaggered;

        public void SetPoise(int value)
        {
            if (PoiseDefinition == null) throw new InvalidOperationException("強靭度の外部定義が必要です");
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value < Poise.Value) AccumulatedPoiseDamage = Math.Min(AccumulatedPoiseDamage, value - 1);
            Poise = value;
        }

        private bool ApplyPoise(long damage)
        {
            if (!Poise.HasValue || damage <= 0) return false;
            long total = AccumulatedPoiseDamage + damage;
            if (total < Poise.Value) { AccumulatedPoiseDamage = (int)total; return false; }
            AccumulatedPoiseDamage = 0;
            if (IsDead) return false;
            // 再成立時は残時間への加算や最大値選択ではなく、定義期間の先頭へ戻す。
            staggerTimer.SetFrames(PoiseDefinition.StaggerFrames);
            Guard.OnStaggered();
            return true;
        }
        // 死亡確定時に解除し、HP/死亡の購読側にも残留状態を見せない。
        private void ClearDeathReaction()
        {
            Guard.Clear();
            ClearProtection();
            ClearInvincibility();
            ClearPoiseRecovery();
            staggerTimer.SetFrames(0);
        }

        internal Func<bool> IsOnGrass;

        private readonly FrameHpChanges frameHp = new FrameHpChanges();
        internal long TotalDamageTaken { get; private set; }
        internal long TotalHitsTaken { get; private set; }
        internal bool HasPendingHpFrame => frameHp.Active;
        internal void BeginHpFrame() { frameHp.Begin(currentHp); protection.BeginFrame(); poiseRecovery.BeginFrame(); frameInvincible = invincibilityTimer.IsActive; pendingInvincibilityMs = 0; framePoiseDamage = 0; frameStaggered = false; }
        internal void CommitHpFrame()
        {
            currentHp = frameHp.Commit(currentHp, maxHp);
            protection.CommitFrame();
            if (pendingInvincibilityMs > 0) invincibilityTimer.ExtendMilliseconds(pendingInvincibilityMs);
            pendingInvincibilityMs = 0;
            if (poiseRecovery.CommitFrame()) AccumulatedPoiseDamage = 0;
            frameStaggered = ApplyPoise(framePoiseDamage);
            if (IsDead) ClearDeathReaction();
            framePoiseDamage = 0;
        }
        internal void PublishHpFrame()
        {
            bool changed = frameHp.TakeNotification(out bool died);
            try { frameHp.PublishPoisonHits(); }
            finally
            {
                if (changed) OnHpChanged?.Invoke(currentHp, maxHp);
                bool staggered = frameStaggered; frameStaggered = false;
                if (!IsDead && staggered) OnStaggered?.Invoke();
                if (died) OnDeath?.Invoke();
            }
        }
        private int currentHp;
        private readonly int maxHp;
        private readonly ElementType element;
        private Vector2Int gridPosition;
        private FrameCountdown stunTimer;
        private StatusEffect activeStatusEffect;
        private bool statusFrameOpen, frameParalyzed, pendingPoisonRefresh;
        private StatusEffect pendingStatus;
        private readonly System.Collections.Generic.HashSet<StatusEffectType> pendingStatusClears = new System.Collections.Generic.HashSet<StatusEffectType>();
        internal void BeginStatusFrame()
        {
            if (statusFrameOpen) return;
            frameParalyzed = IsParalyzed;
            statusFrameOpen = true;
        }
        internal void EndStatusFrame()
        {
            if (!statusFrameOpen) return;
            statusFrameOpen = false;
            if (!IsDead && pendingStatus != null && !pendingStatusClears.Contains(pendingStatus.Type))
                ApplyStatusImmediately(pendingStatus, pendingPoisonRefresh);
            if (activeStatusEffect != null && pendingStatusClears.Contains(activeStatusEffect.Type))
            { activeStatusEffect = null; poisonDamageRemainder = 0; poisonFrameRemainder = 0; }
            CancelStatusFrame();
        }
        internal void CancelStatusFrame()
        {
            statusFrameOpen = false; pendingStatus = null; pendingPoisonRefresh = false;
            pendingStatusClears.Clear();
        }
        /// <summary>論理フレーム中の解除は後段で確定し、同種の未有効付与も取り消す。</summary>
        public void ClearStatus(StatusEffectType type)
        {
            if (statusFrameOpen) { pendingStatusClears.Add(type); return; }
            if (activeStatusEffect?.Type != type) return;
            activeStatusEffect = null; poisonDamageRemainder = 0; poisonFrameRemainder = 0;
        }
        private long poisonDamageRemainder; // ダメージ×ミリ秒の1000未満の端数

        // BattleSystemだけが使う、反撃を含む同期ダメージ処理の境界。
        internal event Action OnDamageProcessingStarted;
        internal event Action OnDamageProcessingCompleted;

        /// <summary>現在HP</summary>
        public int CurrentHp => currentHp;

        /// <summary>最大HP</summary>
        public int MaxHp => maxHp;

        /// <summary>属性</summary>
        public ElementType Element => element;

        /// <summary>グリッド上の位置</summary>
        public Vector2Int GridPosition => gridPosition;

        /// <summary>死亡しているか</summary>
        public bool IsDead => currentHp <= 0;

        // 保存の復元候補専用。ダメージ通知を発生させず、元の敵は変更しない。
        internal void RestoreCheckpointHp(int hp)
        {
            if (hp < 0 || hp > maxHp) throw new ArgumentOutOfRangeException(nameof(hp));
            currentHp = hp;
        }

        /// <summary>スタン中か</summary>
        public bool IsStunned => stunTimer.IsActive || staggerTimer.IsActive;

        /// <summary>残スタン時間（ミリ秒）</summary>
        public int StunDurationMs => Math.Max(stunTimer.Milliseconds, staggerTimer.Milliseconds);

        /// <summary>現在の状態異常</summary>
        public StatusEffect ActiveStatus => activeStatusEffect;

        /// <summary>麻痺中か</summary>
        public bool IsParalyzed => statusFrameOpen ? frameParalyzed :
            activeStatusEffect?.Type == StatusEffectType.Paralyze && activeStatusEffect.RemainingMs > 0;

        /// <summary>毒状態か</summary>
        public bool IsPoisoned => activeStatusEffect?.Type == StatusEffectType.Poison
                               && activeStatusEffect.RemainingMs > 0;

        /// <summary>HP割合（0.0～1.0）</summary>
        public float HpRatio => maxHp > 0 ? (float)currentHp / maxHp : 0f;

        /// <summary>敵のID</summary>
        public string EnemyId { get; }

        /// <summary>敵の名前</summary>
        public string Name { get; }

        /// <summary>戦闘限定の召喚物。勝利条件・通貨報酬の対象外。</summary>
        public bool IsSummoned { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Enemy(string enemyId, string name, int maxHp, ElementType element, Vector2Int initialPosition, bool isSummoned = false, ElementResistances resistances = null, TimedStatusResistances statusResistances = null, EnemyPoiseDefinition poiseDefinition = null)
        {
            PoiseDefinition = poiseDefinition; Poise = poiseDefinition?.Maximum;
            poiseRecovery.Configure(poiseDefinition?.RecoveryDelayFrames);
            Resistances = resistances ?? ElementResistances.Neutral;
            StatusResistances = statusResistances ?? TimedStatusResistances.Neutral;
            EnemyId = enemyId;
            IsSummoned = isSummoned;
            Name = name;
            this.maxHp = maxHp;
            this.currentHp = maxHp;
            this.element = element;
            this.gridPosition = initialPosition;
        }

        /// <summary>
        /// ダメージを受ける（属性相性を考慮）
        /// </summary>
        /// <param name="damage">基本ダメージ量</param>
        /// <param name="attackElement">攻撃属性</param>
        public void TakeDamage(int damage, ElementType attackElement, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null, int hitInvincibilityMs = 0)
        {
            if (hitInvincibilityMs < 0) throw new ArgumentOutOfRangeException(nameof(hitInvincibilityMs));
            if (poiseDamage < 0) throw new ArgumentOutOfRangeException(nameof(poiseDamage));
            AttackPropertyRules.Validate(attackProperties);
            ApplyDamage(damage, attackElement, false, poiseDamage, attackProperties, hitInvincibilityMs);
        }

        /// <summary>回復量をHP集計へ加える。確定済みの死亡は復活させない。</summary>
        public void Heal(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsDead) return;
            if (frameHp.Active) { frameHp.Heal(amount); return; }
            currentHp = (int)Math.Min(maxHp, (long)currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        internal void TakePoisonDamage(int damage) => ApplyDamage(damage, ElementType.None, true);

        private void ApplyDamage(int damage, ElementType attackElement, bool poison, int? poiseDamage = null, AttackProperty properties = AttackProperty.None, int hitInvincibilityMs = 0)
        {
            OnDamageProcessingStarted?.Invoke();
            try
            {
                if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));
                if (IsDead) return;
                if (!poison && IsInvincible) return;

                // 属性相性によるダメージ補正
                bool grass = attackElement == ElementType.Fire && TerrainState.ReceivesPanelEffects && (IsOnGrass?.Invoke() ?? false);
                // ガードを先に判定。通過したヒットだけ防御のフレーム合計へ入れる。
                if (!poison && Guard.Blocks(damage, properties)) return;
                if (!poison && protection.Block(damage, grass)) return;
                float multiplier = (float)(Resistances.Multiplier(attackElement) * (grass ? 2 : 1));
                int finalDamage = Resistances.CalculateDamage(damage, attackElement, grass);
                if (damage > 0 && finalDamage == 0) return; // 元から威力0の状態異常専用効果とは区別。

                if (poison && frameHp.Active)
                {
                    frameHp.Poison(finalDamage, accepted => {
                        TotalDamageTaken += accepted;
                        TotalHitsTaken++;
                        OnDamaged?.Invoke(accepted, multiplier);
                    });
                    return;
                }
                if (poison) finalDamage = Math.Min(finalDamage, Math.Max(0, currentHp - 1));
                if (poison && finalDamage == 0) return;
                bool killedByThisHit = false;
                if (frameHp.Active)
                {
                    if (!poison) frameHp.Damage(finalDamage);
                }
                else
                {
                    currentHp = Math.Max(0, currentHp - finalDamage);
                    // 通知内の別攻撃による死亡を、この攻撃から二重通知しない。
                    killedByThisHit = IsDead;
                    if (killedByThisHit) ClearDeathReaction();
                    OnHpChanged?.Invoke(currentHp, maxHp);
                }
                bool staggered = false;
                if (!poison && Poise.HasValue && !IsDead)
                {
                    int impact = poiseDamage ?? finalDamage;
                    poiseRecovery.Hit(impact);
                    if (frameHp.Active) framePoiseDamage += impact;
                    else staggered = ApplyPoise(impact);
                }
                TotalDamageTaken += finalDamage;
                TotalHitsTaken++;
                if (!poison && damage > 0 && !IsDead && hitInvincibilityMs > 0) SetInvincible(hitInvincibilityMs);
                // 命中通知は各ヒット、HPと死亡通知は論理フレームの確定後。
                OnDamaged?.Invoke(finalDamage, multiplier);
                if (!IsDead && staggered) OnStaggered?.Invoke();

                if (killedByThisHit)
                {
                    OnDeath?.Invoke();
                }
            }
            finally
            {
                OnDamageProcessingCompleted?.Invoke();
            }
        }

        /// <summary>
        /// スタン状態を付与する
        /// 既存のスタン時間より長い場合のみ上書き
        /// </summary>
        /// <param name="durationMs">スタン持続時間（ミリ秒）</param>
        public void ApplyStun(int durationMs)
        {
            if (durationMs <= 0) return;
            stunTimer.ExtendMilliseconds(durationMs);
        }

        /// <summary>
        /// スタン時間をカウントダウンする（毎フレーム呼び出す）
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        public void TickStun(int deltaTimeMs)
        {
            stunTimer.Tick(deltaTimeMs); staggerTimer.Tick(deltaTimeMs);
        }

        public void TickStunFrame() { stunTimer.TickFrame(); staggerTimer.TickFrame(); }

        /// <summary>
        /// 状態異常を付与する（既存の状態異常は上書き）
        /// </summary>
        /// <param name="status">付与する状態異常</param>
        public void ApplyStatus(StatusEffect status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            status = status.WithTargetResistance(StatusResistances.Get(status.Type));
            // 時間耐性で無効化・0フレームになった付与は、既存状態と毒積算に触れない。
            if (IsDead || status.RemainingFrames <= 0) return;
            if (statusFrameOpen)
            {
                pendingStatus = status;
                pendingPoisonRefresh = activeStatusEffect?.Type == StatusEffectType.Poison
                    && activeStatusEffect.RemainingFrames > 0 && status.Type == StatusEffectType.Poison;
                return;
            }
            ApplyStatusImmediately(status);
        }
        private void ApplyStatusImmediately(StatusEffect status, bool preservePoison = false)
        {
            bool refreshPoison = preservePoison || (activeStatusEffect?.Type == StatusEffectType.Poison
                && activeStatusEffect.RemainingFrames > 0 && status.Type == StatusEffectType.Poison);
            activeStatusEffect = status;
            // 同じ毒の再付与は時間だけ更新し、次のダメージへ向かう積算を失わない。
            if (!refreshPoison) { poisonDamageRemainder = 0; poisonFrameRemainder = 0; }
        }

        /// <summary>戦闘終了で必ず解除する麻痺だけを取り除く。他の状態の持越し規則は変更しない。</summary>
        internal void ClearBattleParalysis()
        {
            if (pendingStatus?.Type == StatusEffectType.Paralyze) pendingStatus = null;
            if (activeStatusEffect?.Type != StatusEffectType.Paralyze) return;
            activeStatusEffect = null;
            poisonDamageRemainder = 0; poisonFrameRemainder = 0;
        }

        /// <summary>
        /// 状態異常の時間をカウントダウンする（毎ティック呼び出す）
        /// </summary>
        /// <param name="elapsedMs">経過時間（ミリ秒）</param>
        /// <returns>有効な残時間に対する毒ダメージ量（毒以外は0）</returns>
        private long poisonFrameRemainder;
        public int TickStatusFrame()
        {
            if (activeStatusEffect == null) return 0;
            int damage = 0;
            if (activeStatusEffect.RemainingFrames > 0 && activeStatusEffect.Type == StatusEffectType.Poison)
            {
                // 現行の毎秒量を維持。未定義の毒の間隔・製品値はここで新設しない。
                poisonFrameRemainder += activeStatusEffect.DamagePerTick;
                damage = (int)(poisonFrameRemainder / 60); poisonFrameRemainder %= 60;
            }
            // 終了フレームのダメージを計算してから解除する。
            if (activeStatusEffect.TickFrame())
            {
                activeStatusEffect = null;
                if (!pendingPoisonRefresh) poisonFrameRemainder = 0;
            }
            return damage;
        }

        public int TickStatus(int elapsedMs)
        {
            if (activeStatusEffect == null || elapsedMs <= 0) return 0;

            int activeMs = Math.Min(elapsedMs, Math.Max(0, activeStatusEffect.RemainingMs));

            int poisonDamage = 0;
            if (activeStatusEffect.Type == StatusEffectType.Poison)
            {
                // 既存の1秒あたりの量を維持し、フレームごとの整数切捨てを避ける。
                long accumulated = poisonDamageRemainder + (long)activeStatusEffect.DamagePerTick * activeMs;
                poisonDamage = (int)(accumulated / 1000);
                poisonDamageRemainder = accumulated % 1000;
            }

            bool expired = activeStatusEffect.Tick(activeMs);
            if (expired)
            {
                activeStatusEffect = null;
                poisonDamageRemainder = 0; poisonFrameRemainder = 0;
            }

            return poisonDamage;
        }

        /// <summary>
        /// 位置を移動する
        /// </summary>
        public void MoveTo(Vector2Int newPosition)
        {
            gridPosition = newPosition;
            OnPositionChanged?.Invoke(newPosition);
        }

        /// <summary>
        /// HP変更時のイベント
        /// </summary>
        public event Action<int, int> OnHpChanged; // (currentHp, maxHp)

        /// <summary>
        /// ダメージを受けた時のイベント
        /// </summary>
        public event Action<int, float> OnDamaged; // (damage, multiplier)

        /// <summary>
        /// 死亡時のイベント
        /// </summary>
        public event Action OnDeath;

        /// <summary>
        /// 位置変更時のイベント
        /// </summary>
        public event Action<Vector2Int> OnPositionChanged;
    }

    /// <summary>
    /// 2次元整数ベクトル（Unity非依存）
    /// </summary>
    public struct Vector2Int
    {
        public int X { get; }
        public int Y { get; }

        public Vector2Int(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static Vector2Int Zero => new Vector2Int(0, 0);

        public override string ToString() => $"({X}, {Y})";

        public override bool Equals(object obj)
        {
            if (obj is Vector2Int other)
            {
                return X == other.X && Y == other.Y;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ (Y.GetHashCode() << 2);
        }

        public static bool operator ==(Vector2Int a, Vector2Int b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Vector2Int a, Vector2Int b)
        {
            return !(a == b);
        }
    }
}
