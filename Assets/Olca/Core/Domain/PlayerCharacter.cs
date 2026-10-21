using System;
using System.Collections.Generic;

namespace Olca.Core.Domain
{
    /// <summary>
    /// プレイヤーキャラクター
    /// </summary>
    public class PlayerCharacter
    {
        /// <summary>戦闘限定の地形特性。戦闘終了時に解除する。</summary>
        public BattleTerrainState TerrainState { get; } = new BattleTerrainState();
        public DepartureStyleDefinition DepartureStyle { get; private set; } = DepartureStyleDefinition.Basic;
        private bool departureStyleFixed;
        internal void FreezeDepartureStyle() => departureStyleFixed = true;
        internal Func<ElementType?> BattleElement;
        public ElementType ActiveElement => BattleElement?.Invoke() ?? DepartureStyle.Attack?.Element ?? ElementType.None;
        /// <summary>新規出撃または保存復元時に一度だけ固定する。戦闘中の切替は一時変身が所有する。</summary>
        public void SetDepartureStyle(DepartureStyleDefinition style)
        {
            if (departureStyleFixed) throw new InvalidOperationException("攻略中のスタイルは変更できません");
            DepartureStyle = style ?? throw new ArgumentNullException(nameof(style)); departureStyleFixed = true;
        }

        public ElementResistances Resistances { get; private set; } = ElementResistances.Neutral;
        public ElementResistances ActiveResistances => previewTransformationResistances ?? transformationResistances ?? Resistances;
        public bool HasTransformationPreview => previewTransformationResistances != null;
        private ElementResistances previewTransformationResistances;
        internal void SetTransformationPreview(ElementResistances value)
        {
            if (value != null && HasAura) throw new InvalidOperationException("オーラと変身の優先順は未対応です");
            previewTransformationResistances = value;
        }
        public bool HasTransformationDefense => transformationResistances != null;
        /// <summary>確定後に残る戦闘フェーズ数。フレーム数ではない。</summary>
        public int RemainingTransformationPhases { get; private set; }
        private ElementResistances transformationResistances;
        private TransformationWeakness transformationWeakness;
        internal Func<bool> IsOnGrass;
        public event Action OnTransformationDefenseEnded;
        public void SetElementResistances(ElementResistances value)
            => Resistances = value ?? throw new ArgumentNullException(nameof(value));
        // 確定済み変身の被弾状態を開始する。仮選択・取り消しには使用しない。
        public void BeginTransformationDefense(ElementResistances value, ElementType breakElement)
            => BeginTransformationDefense(value, new TransformationWeakness(breakElement));
        public void BeginTransformationDefense(ElementResistances value, TransformationWeakness weakness)
        {
            if (weakness == null) throw new ArgumentNullException(nameof(weakness));
            if (HasAura) throw new InvalidOperationException("オーラと変身の優先順は未対応です");
            transformationResistances = value ?? throw new ArgumentNullException(nameof(value));
            transformationWeakness = weakness;
            RemainingTransformationPhases = 3;
        }
        internal void AdvanceTransformationPhase()
        {
            if (HasTransformationDefense && --RemainingTransformationPhases == 0)
                EndTransformationDefense();
        }
        public void EndTransformationDefense()
        {
            if (!HasTransformationDefense) return;
            transformationResistances = null;
            RemainingTransformationPhases = 0;
            OnTransformationDefenseEnded?.Invoke();
        }

        private readonly Dictionary<string, TimedPoisonState> poisons = new Dictionary<string, TimedPoisonState>();
        /// <summary>製品の付与元・量・間隔は定義せず、外部指定された毒種を所有する。</summary>
        public void ApplyPoison(string kind, StatusEffect effect, int intervalFrames)
        {
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("毒種が必要です");
            if (IsDead) return;
            if (!poisons.TryGetValue(kind, out var state)) {
                state = new TimedPoisonState();
                if (frameHp.Active) state.BeginFrame();
                poisons.Add(kind, state);
            }
            state.ApplyPersistent(effect, intervalFrames);
        }
        public PlayerPoisonDto[] CapturePoisons()
        {
            var result = new List<PlayerPoisonDto>();
            foreach (var pair in poisons) { var value=pair.Value.Capture(pair.Key); if(value!=null) result.Add(value); }
            result.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Kind,b.Kind));
            return result.ToArray();
        }
        public void RestorePoisons(PlayerPoisonDto[] values)
        {
            if (frameHp.Active) throw new InvalidOperationException("状態更新中は毒を復元できません");
            var restored = new Dictionary<string, TimedPoisonState>();
            foreach (var value in values ?? Array.Empty<PlayerPoisonDto>()) {
                if (value == null) throw new ArgumentException("毒状態が欠落しています");
                value.Validate(); var state=new TimedPoisonState(); state.Restore(value);
                if (restored.ContainsKey(value.Kind)) throw new ArgumentException("毒種が重複しています");
                restored.Add(value.Kind,state);
            }
            poisons.Clear(); foreach(var pair in restored) poisons.Add(pair.Key,pair.Value);
        }
        public void ClearPoisons() { foreach(var state in poisons.Values) state.Clear(); }
        public void ResetPoisonIntervals() { foreach(var state in poisons.Values) state.ResetInterval(); }
        internal bool HasPoisonState { get { foreach(var state in poisons.Values) if(state.IsActive || state.HasPendingApplication) return true; return false; } }
        internal void TickPoisons(int ms, bool logical)
        {
            if (!frameHp.Active) return;
            foreach(var state in poisons.Values) TakePoisonDamage(logical ? state.TickFrame() : state.TickMilliseconds(ms));
        }

        private readonly FrameHpChanges frameHp = new FrameHpChanges();
        private bool frameInvincible, frameStaggered, frameWasStaggered;
        private FrameCountdown staggerTimer;
        public int StaggerDurationFrames { get; }
        public int StaggerRemainingFrames => staggerTimer.Frames;
        public bool IsStaggered => !IsDead && (frameHp.Active ? frameWasStaggered : staggerTimer.IsActive);
        private void StartStagger() { if (StaggerDurationFrames > 0) staggerTimer.SetFrames(StaggerDurationFrames); }
        internal void TickStagger(int ms, bool logical) { if (logical) staggerTimer.TickFrame(); else staggerTimer.Tick(ms); }
        internal void ClearStagger() { staggerTimer.SetFrames(0); frameWasStaggered = false; }
        private FrameCountdown paralysisTimer;
        private int? pendingParalysisFrames;
        private bool paralysisFrameOpen, frameParalyzed, clearParalysisPending, paralysisInterruptPending;
        public int ParalysisRemainingFrames => paralysisTimer.Frames;
        public bool IsParalyzed => !IsDead && (paralysisFrameOpen ? frameParalyzed : paralysisTimer.IsActive);
        public bool IsActionPrevented => IsStaggered || IsParalyzed;
        internal bool HasParalysisState => paralysisTimer.IsActive || pendingParalysisFrames.HasValue || clearParalysisPending || frameParalyzed;
        /// <summary>外部で確定した有効期間を予約する。付与フレームには行動を中断しない。</summary>
        public void ApplyParalysis(int durationFrames)
        {
            if (durationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(durationFrames));
            if (IsDead) return;
            pendingParalysisFrames = durationFrames;
            // 既存の敵状態処理と同じく、フレーム内の解除予約を再付与で打ち消さない。
            if (!paralysisFrameOpen) clearParalysisPending = false;
        }
        public void ClearParalysis() { if (!HasParalysisState && !paralysisFrameOpen) return; pendingParalysisFrames = null; clearParalysisPending = true; }
        private void CommitParalysisChanges()
        {
            if (clearParalysisPending) { paralysisTimer.SetFrames(0); paralysisInterruptPending = false; }
            else if (pendingParalysisFrames.HasValue) { paralysisTimer.SetFrames(pendingParalysisFrames.Value); paralysisInterruptPending = true; }
            pendingParalysisFrames = null; clearParalysisPending = false;
        }
        internal bool BeginParalysisFrame(bool advance)
        {
            if (advance) CommitParalysisChanges();
            paralysisFrameOpen = true; frameParalyzed = paralysisTimer.IsActive;
            bool interrupt = advance && frameParalyzed && paralysisInterruptPending;
            if (advance) paralysisInterruptPending = false;
            return interrupt;
        }
        internal void TickParalysis(int ms, bool logical)
        { if (logical) paralysisTimer.TickFrame(); else paralysisTimer.Tick(ms); }
        internal void EndParalysisFrame(bool advance)
        {
            if (IsDead) { ResetParalysis(); return; }
            if (advance) CommitParalysisChanges();
            paralysisFrameOpen = false; frameParalyzed = false;
        }
        internal void ResetParalysis()
        {
            paralysisTimer.SetFrames(0); pendingParalysisFrames = null;
            paralysisFrameOpen = frameParalyzed = clearParalysisPending = paralysisInterruptPending = false;
        }
        private FrameCountdown confusionTimer;
        private int? pendingConfusionFrames;
        private bool confusionFrameOpen, frameConfused, clearConfusionPending;
        public int ConfusionRemainingFrames => confusionTimer.Frames;
        public bool IsConfused => !IsDead && (confusionFrameOpen ? frameConfused : confusionTimer.IsActive);
        internal bool HasConfusionState => confusionTimer.IsActive || pendingConfusionFrames.HasValue || clearConfusionPending || frameConfused;
        /// <summary>有効期間と付与時の合算済み耐性を受け取る。製品付与元や探索持越しは追加しない。</summary>
        public void ApplyConfusion(int durationFrames, int durationResistancePercent = 0)
        {
            if (durationFrames <= 0 || durationResistancePercent < 0) throw new ArgumentOutOfRangeException(nameof(durationFrames));
            int frames=(int)((long)durationFrames*(100-Math.Min(100,durationResistancePercent))/100);
            if(IsDead || frames==0)return;
            pendingConfusionFrames=frames;
            if(!confusionFrameOpen)clearConfusionPending=false;
        }
        public void ClearConfusion(){pendingConfusionFrames=null;clearConfusionPending=true;}
        private void CommitConfusionChanges()
        {
            if(clearConfusionPending)confusionTimer.SetFrames(0);
            else if(pendingConfusionFrames.HasValue)confusionTimer.SetFrames(pendingConfusionFrames.Value);
            pendingConfusionFrames=null;clearConfusionPending=false;
        }
        internal void BeginConfusionFrame(bool advance)
        {if(advance)CommitConfusionChanges();confusionFrameOpen=true;frameConfused=confusionTimer.IsActive;}
        internal void TickConfusion(int ms,bool logical)
        {if(logical)confusionTimer.TickFrame();else confusionTimer.Tick(ms);}
        internal void EndConfusionFrame(bool advance)
        {
            if(IsDead){ResetConfusion(); ResetBlindness();return;}
            if(advance)CommitConfusionChanges();confusionFrameOpen=frameConfused=false;
        }
        internal void ResetConfusion()
        {confusionTimer.SetFrames(0);pendingConfusionFrames=null;confusionFrameOpen=frameConfused=clearConfusionPending=false;}

        private FrameCountdown blindnessTimer;
        private int? pendingBlindnessFrames;
        private bool blindnessFrameOpen, frameBlind, clearBlindnessPending;
        public int BlindRemainingFrames => blindnessTimer.Frames;
        public bool IsBlind => !IsDead && (blindnessFrameOpen ? frameBlind : blindnessTimer.IsActive);
        internal bool HasBlindnessState => blindnessTimer.IsActive || pendingBlindnessFrames.HasValue || clearBlindnessPending || frameBlind;
        /// <summary>有効期間と付与時の合算済み耐性を受け取る。製品付与元や探索持越しは追加しない。</summary>
        public void ApplyBlindness(int durationFrames, int durationResistancePercent = 0)
        {
            if (durationFrames <= 0 || durationResistancePercent < 0) throw new ArgumentOutOfRangeException(nameof(durationFrames));
            int frames=(int)((long)durationFrames*(100-Math.Min(100,durationResistancePercent))/100);
            if(IsDead || frames==0)return;
            pendingBlindnessFrames=frames;
            if(!blindnessFrameOpen)clearBlindnessPending=false;
        }
        public void ClearBlindness(){pendingBlindnessFrames=null;clearBlindnessPending=true;}
        private void CommitBlindnessChanges()
        {
            if(clearBlindnessPending)blindnessTimer.SetFrames(0);
            else if(pendingBlindnessFrames.HasValue)blindnessTimer.SetFrames(pendingBlindnessFrames.Value);
            pendingBlindnessFrames=null;clearBlindnessPending=false;
        }
        internal void BeginBlindnessFrame(bool advance)
        {if(advance)CommitBlindnessChanges();blindnessFrameOpen=true;frameBlind=blindnessTimer.IsActive;}
        internal void TickBlindness(int ms,bool logical)
        {if(logical)blindnessTimer.TickFrame();else blindnessTimer.Tick(ms);}
        internal void EndBlindnessFrame(bool advance)
        {
            if(IsDead){ResetBlindness();return;}
            if(advance)CommitBlindnessChanges();blindnessFrameOpen=frameBlind=false;
        }
        internal void ResetBlindness()
        {blindnessTimer.SetFrames(0);pendingBlindnessFrames=null;blindnessFrameOpen=frameBlind=clearBlindnessPending=false;}

        private int pendingInvincibilityMs;
        private readonly PoiseRecoveryState poiseRecovery = new PoiseRecoveryState();
        /// <summary>保存・戦闘をまたいで保持する外部定義。残り待ち時間は含めない。</summary>
        public int? PoiseRecoveryDefinitionFrames { get; }
        public int? PoiseRecoveryDelayFrames => poiseRecovery.DelayFrames;
        public int PoiseRecoveryRemainingFrames => poiseRecovery.RemainingFrames;
        public void ConfigurePoiseRecovery(int? delayFrames)
        {
            if (accumulatedDamage != 0 || poiseRecovery.RemainingFrames > 0)
                throw new InvalidOperationException("回復待ち定義は戦闘開始状態で指定してください");
            poiseRecovery.Configure(delayFrames);
        }
        internal void TickPoiseRecovery(int ms, bool logical) { if (poiseRecovery.Tick(ms, logical)) accumulatedDamage = 0; }
        internal void ClearPoiseRecovery()
        {
            poiseRecovery.Clear(); poiseRecovery.Configure(PoiseRecoveryDefinitionFrames);
            accumulatedDamage = 0;
        }
        private long framePoiseDamage;
        private bool framePoiseHit, frameForceStagger;
        internal bool HasPendingHpFrame => frameHp.Active;
        internal void BeginHpFrame() { frameHp.Begin(currentHp); foreach(var state in poisons.Values) state.BeginFrame(); protection.BeginFrame(); poiseRecovery.BeginFrame(); frameInvincible = invincibilityTimer.IsActive; frameWasStaggered = staggerTimer.IsActive; frameStaggered = false; pendingInvincibilityMs = 0; framePoiseDamage = 0; framePoiseHit = frameForceStagger = false; }
        internal void CommitHpFrame()
        {
            currentHp = frameHp.Commit(currentHp, maxHp);
            foreach(var state in poisons.Values) { state.EndFrame(); if (IsDead) state.Reset(); }
            if (IsDead) { ResetParalysis(); ResetConfusion(); ResetBlindness(); }
            // 集中の付与/解除は終端で確定する。付与フレーム中の攻撃は旧倍率だけを消費する。
            if (IsDead || clearConcentratePending) nextCardMultiplier = 1f;
            else if (pendingConcentrateMultiplier.HasValue) nextCardMultiplier = pendingConcentrateMultiplier.Value;
            pendingConcentrateMultiplier = null; clearConcentratePending = false;
            protection.CommitFrame();
            if (poiseRecovery.CommitFrame()) accumulatedDamage = 0;
            if (framePoiseHit)
            {
                long total = (long)accumulatedDamage + framePoiseDamage;
                if (frameForceStagger || total >= poise)
                {
                    accumulatedDamage = 0;
                    frameStaggered = !IsDead;
                    if (frameStaggered) StartStagger();
                }
                else accumulatedDamage = (int)total;
            }
            framePoiseDamage = 0; framePoiseHit = frameForceStagger = false;
            // 新規付与を当該フレームのTickで消費せず、次フレームへ全期間を渡す。
            if (pendingInvincibilityMs > 0) invincibilityTimer.ExtendMilliseconds(pendingInvincibilityMs);
            pendingInvincibilityMs = 0;
        }
        internal void PublishHpFrame()
        {
            bool changed = frameHp.TakeNotification(out bool died);
            if (changed) OnHpChanged?.Invoke(currentHp, maxHp);
            bool staggered = frameStaggered; frameStaggered = false;
            if (!IsDead && staggered) OnStaggered?.Invoke();
            if (died) { ResetParalysis(); ResetConfusion(); ResetBlindness(); OnDeath?.Invoke(); }
        }
        private int currentHp;
        private readonly int maxHp;
        private readonly int attackPower;
        private readonly float customSpeed;
        private int poise;              // 強靭度（累計ダメージがこれを超えるとのけぞり）
        private int accumulatedDamage;  // 現在行動中の累計ダメージ
        private Vector2Int gridPosition;
        private Vector2Int? temporaryPosition;
        private readonly BattleProtectionState protection = new BattleProtectionState();
        private FrameCountdown invincibilityTimer; // 無敵残り時間（固定精度）
        private float nextCardMultiplier = 1.0f; // 次カード威力倍率（集中バフ）
        private float? pendingConcentrateMultiplier;
        private bool clearConcentratePending;

        // BattleSystemだけが使う、反撃を含む同期ダメージ処理の境界。
        internal event Action OnDamageProcessingStarted;
        internal event Action OnDamageProcessingCompleted;

        /// <summary>現在HP</summary>
        public int CurrentHp => currentHp;

        /// <summary>最大HP</summary>
        public int MaxHp => maxHp;

        /// <summary>攻撃力</summary>
        public int AttackPower => attackPower;

        /// <summary>カスタムゲージの溜まる速度</summary>
        public float CustomSpeed => customSpeed;

        /// <summary>強靭度（累計ダメージがこれを超えるとのけぞり）</summary>
        public int Poise => poise;

        /// <summary>現在行動中の累計ダメージ</summary>
        public int AccumulatedDamage => accumulatedDamage;

        /// <summary>グリッド上の位置（一時移動中は一時位置を返す）</summary>
        public Vector2Int GridPosition => temporaryPosition ?? gridPosition;

        /// <summary>死亡しているか</summary>
        public bool IsDead => currentHp <= 0;

        /// <summary>HP割合（0.0～1.0）</summary>
        public float HpRatio => maxHp > 0 ? (float)currentHp / maxHp : 0f;

        /// <summary>一時移動中かどうか</summary>
        public bool IsInTemporaryMove => temporaryPosition.HasValue;

        /// <summary>バリアHP</summary>
        public int BarrierHp => protection.BarrierHp;

        /// <summary>バリアを持っているか</summary>
        public bool HasBarrier => BarrierHp > 0;
        public int AuraThreshold => protection.AuraThreshold;
        public bool HasAura => AuraThreshold > 0;

        /// <summary>外部指定オーラで現在の防御を置換。期間未指定は時間制限なし。</summary>
        public void SetAura(int threshold, int? durationFrames = null)
        {
            if (threshold > 0 && (HasTransformationDefense || HasTransformationPreview))
                throw new InvalidOperationException("オーラと変身の優先順は未対応です");
            protection.Set(threshold, true, durationFrames);
        }
        internal void TickProtection(int ms, bool logical) => protection.Tick(ms, logical);

        /// <summary>無敵中か</summary>
        public bool IsInvincible => frameHp.Active ? frameInvincible : invincibilityTimer.IsActive;

        /// <summary>残り無敵時間（ミリ秒）</summary>
        public int InvincibleDurationMs => invincibilityTimer.Milliseconds;

        /// <summary>次カード威力倍率（集中バフ）</summary>
        public float NextCardMultiplier => nextCardMultiplier;

        /// <summary>集中バフ中か</summary>
        public bool IsConcentrating => nextCardMultiplier > 1.0f;

        /// <summary>
        /// コンストラクタ（互換性用：位置指定なし）
        /// </summary>
        /// <param name="maxHp">最大HP</param>
        /// <param name="attackPower">攻撃力</param>
        /// <param name="customSpeed">カスタムゲージ速度</param>
        /// <param name="poise">強靭度（デフォルト30）</param>
        public PlayerCharacter(int maxHp, int attackPower = 10, float customSpeed = 1.0f, int poise = 30, int staggerDurationFrames = 0, int? poiseRecoveryDelayFrames = null)
            : this(maxHp, Vector2Int.Zero, attackPower, customSpeed, poise, staggerDurationFrames, poiseRecoveryDelayFrames)
        {
        }

        /// <summary>
        /// コンストラクタ（位置指定あり）
        /// </summary>
        /// <param name="maxHp">最大HP</param>
        /// <param name="initialPosition">初期位置</param>
        /// <param name="attackPower">攻撃力</param>
        /// <param name="customSpeed">カスタムゲージ速度</param>
        /// <param name="poise">強靭度（デフォルト30）</param>
        public PlayerCharacter(int maxHp, Vector2Int initialPosition, int attackPower = 10, float customSpeed = 1.0f, int poise = 30, int staggerDurationFrames = 0, int? poiseRecoveryDelayFrames = null)
        {
            if (staggerDurationFrames < 0) throw new ArgumentOutOfRangeException(nameof(staggerDurationFrames));
            StaggerDurationFrames = staggerDurationFrames;
            poiseRecovery.Configure(poiseRecoveryDelayFrames);
            PoiseRecoveryDefinitionFrames = poiseRecoveryDelayFrames;
            this.maxHp = maxHp;
            this.currentHp = maxHp;
            this.attackPower = attackPower;
            this.customSpeed = customSpeed;
            this.poise = poise;
            this.accumulatedDamage = 0;
            this.gridPosition = initialPosition;
        }

        /// <summary>
        /// ダメージを受ける
        /// </summary>
        /// <param name="damage">ダメージ量</param>
        /// <summary>
        /// ダメージを受ける
        /// </summary>
        /// <param name="damage">ダメージ量</param>
        /// <param name="forceStagger">強制的にのけぞるか（状態異常による強制のけぞり用）</param>
        /// <param name="affectsPoise">強靭度を削るか。地形などのHPダメージとは独立する。</param>
        /// <param name="poiseDamage">明示した強靭度削り量。nullは旧定義のHPダメージ連動を維持する。</param>
        public void TakeDamage(int damage, bool forceStagger = false, bool affectsPoise = true, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null)
            => TryTakeDamage(damage, forceStagger, affectsPoise, attackElement, attackProperties, poiseDamage);

        /// <summary>防御で遮断されなかったヒットだけtrue。付随効果と被弾無敵の判定に使う。</summary>
        public bool TryTakeDamage(int damage, bool forceStagger = false, bool affectsPoise = true, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null)
        {
            if (poiseDamage < 0) throw new ArgumentOutOfRangeException(nameof(poiseDamage));
            AttackPropertyRules.Validate(attackProperties);
            bool breakTransformation = false;
            OnDamageProcessingStarted?.Invoke();
            try
            {
                if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));

                // 無敵中はダメージ・のけぞりを無効化
                if (frameHp.Active ? frameInvincible : invincibilityTimer.IsActive) return false;

                bool grass = attackElement == ElementType.Fire && TerrainState.ReceivesPanelEffects && (IsOnGrass?.Invoke() ?? false);
                // 単独オーラは地形補正だけで閾値を判定し、消滅するヒットも全防御する。
                if (HasAura && protection.Block(damage, grass, attackProperties)) return false;
                int originalDamage = damage;
                int bodyDamage = ActiveResistances.CalculateDamage(damage, attackElement, grass);
                // 本体耐性を耐久計算に使わず地形補正だけで計上し、破壊した一発も全て防ぐ。
                // 元から威力0の状態専用ヒットはバリアを通過する。
                if (HasBarrier && protection.Block(damage, grass, attackProperties)) return false;
                // 完全防御した弱点ヒットでは解除しない。受理したヒットだけ変身中耐性で計算後に解除する。
                breakTransformation = (originalDamage == 0 || bodyDamage > 0) && HasTransformationDefense && transformationWeakness.Matches(attackElement, attackProperties);
                damage = bodyDamage;
                if (originalDamage > 0 && damage == 0) return false; // 耐性無効は強靭度・付随効果へ進めない。

                if (frameHp.Active) frameHp.Damage(damage);
                else
                {
                    currentHp = Math.Max(0, currentHp - damage);
                    if (IsDead) { ResetParalysis(); ResetConfusion(); ResetBlindness(); }
                    OnHpChanged?.Invoke(currentHp, maxHp);
                }

                // 明示量はHP軽減と独立。完全無効・バリア完全吸収は上段で拒否する。
                int impact = poiseDamage ?? damage;
                // 明示0では蓄積も閾値判定も更新しない。既存の強制のけぞり指定は維持する。
                if (!IsDead && affectsPoise && (poiseDamage != 0 || forceStagger))
                {
                    poiseRecovery.Hit(impact);
                    if (frameHp.Active)
                    {
                        // 同フレームの全ヒットを受理後、閾値とリセットを一度だけ判定する。
                        framePoiseHit = true;
                        framePoiseDamage += impact;
                        frameForceStagger |= forceStagger;
                    }
                    else
                    {
                        // フレーム外の互換呼び出しは従来の即時判定を維持する。
                        long totalDamage = (long)accumulatedDamage + impact;
                        if (forceStagger || totalDamage >= poise)
                        {
                            accumulatedDamage = 0;
                            StartStagger();
                            OnStaggered?.Invoke();
                        }
                        else accumulatedDamage = (int)totalDamage;
                    }
                }

                if (!frameHp.Active && IsDead)
                {
                    ResetParalysis(); ResetConfusion(); ResetBlindness(); OnDeath?.Invoke();
                }
                return true;
            }
            finally
            {
                try { if (breakTransformation) EndTransformationDefense(); }
                finally { OnDamageProcessingCompleted?.Invoke(); }
            }
        }

        /// <summary>拘束障害物の確定量。被弾無敵・防御・強靭度を経由せず、毒のHP1制限も付けない。</summary>
        internal void TakeObstacleDamage(int damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (IsDead || damage == 0) return;
            if (frameHp.Active) { frameHp.Damage(damage); return; }
            currentHp = Math.Max(0, currentHp - damage);
            OnHpChanged?.Invoke(currentHp, maxHp);
            if (IsDead) { ResetParalysis(); ResetConfusion(); ResetBlindness(); OnDeath?.Invoke(); }
        }

        /// <summary>外部の毒更新から受け取る確定量。バリア・強靭度・被弾無敵を経由せずHP1で止める。</summary>
        public void TakePoisonDamage(int damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (IsDead) return;
            if (frameHp.Active) { frameHp.Poison(damage); return; }
            int accepted = Math.Min(damage, Math.Max(0, currentHp - 1));
            if (accepted == 0) return;
            currentHp -= accepted;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// HPを回復する
        /// </summary>
        /// <param name="amount">回復量</param>
        public void Heal(int amount)
        {
            if (amount < 0) throw new ArgumentException("回復量は0以上である必要があります", nameof(amount));

            if (frameHp.Active) { frameHp.Heal(amount); return; }
            currentHp = (int)Math.Min(maxHp, (long)currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// 強靭度の累計ダメージをリセットする
        /// 明示リセット用。通常のカード使用や行動切替では呼び出さない
        /// </summary>
        public void ResetPoiseAccumulation()
        {
            accumulatedDamage = 0;
        }

        /// <summary>
        /// バリアを設定する（上書き）
        /// </summary>
        /// <param name="hp">バリアHP</param>
        public void SetBarrier(int hp, int? durationFrames = null)
        {
            protection.Set(Math.Max(0, hp), false, durationFrames);
        }

        /// <summary>
        /// 無敵を付与する
        /// </summary>
        /// <param name="durationMs">無敵持続時間（ミリ秒）</param>
        public void SetInvincible(int durationMs)
        {
            if (durationMs <= 0) return;
            if (frameHp.Active) { pendingInvincibilityMs = Math.Max(pendingInvincibilityMs, durationMs); return; }
            invincibilityTimer.ExtendMilliseconds(durationMs);
        }

        /// <summary>
        /// 無敵時間をカウントダウンする（毎ティック呼び出す）
        /// </summary>
        /// <param name="elapsedMs">経過時間（ミリ秒）</param>
        public void TickInvincible(int elapsedMs)
        {
            invincibilityTimer.Tick(elapsedMs);
        }

        public void TickInvincibleFrame() => invincibilityTimer.TickFrame();
        // 戦闘限定の無敵と未反映予約を、勝敗通知/保存境界より前に解除する。
        internal void ClearInvincibility()
        {
            invincibilityTimer.SetFrames(0); pendingInvincibilityMs = 0; frameInvincible = false;
        }


        /// <summary>
        /// 集中バフを設定する
        /// </summary>
        /// <param name="multiplier">次カードへの威力倍率</param>
        public void SetConcentrateBuff(float multiplier)
        {
            float value = Math.Max(1.0f, multiplier);
            if (frameHp.Active)
            {
                if (value <= 1f) clearConcentratePending = true;
                else pendingConcentrateMultiplier = value;
                return;
            }
            // フレーム外の旧直接APIは即時設定を維持する。戦闘終了の解除もここを通る。
            nextCardMultiplier = value;
            pendingConcentrateMultiplier = null; clearConcentratePending = false;
        }

        /// <summary>
        /// 集中バフを消費して倍率を取得する（カード使用時に呼ぶ）
        /// </summary>
        /// <returns>威力倍率。バフがない場合は1.0f</returns>
        public float ConsumeConcentrateBuff()
        {
            float val = nextCardMultiplier;
            nextCardMultiplier = 1.0f;
            return val;
        }

        /// <summary>
        /// 強靭度を変更する（バフ・装備などによる変動用）
        /// </summary>
        /// <param name="newPoise">新しい強靭度</param>
        public void SetPoise(int newPoise)
        {
            // 正の最大値を下げたときだけ、残りを最低1にする。変更自体ではのけぞらない。
            // 最大値0を許す既存APIの扱いは維持する。
            if (newPoise > 0 && newPoise < poise)
                accumulatedDamage = Math.Min(accumulatedDamage, newPoise - 1);
            poise = Math.Max(0, newPoise);
        }

        /// <summary>
        /// HPを設定する（デバッグ用）
        /// </summary>
        public void SetHp(int hp)
        {
            currentHp = Math.Max(0, Math.Min(hp, maxHp));
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// 位置を移動する
        /// </summary>
        /// <param name="newPosition">新しい位置</param>
        public void MoveTo(Vector2Int newPosition)
        {
            gridPosition = newPosition;
            OnPositionChanged?.Invoke(newPosition);
        }

        /// <summary>
        /// 一時的に指定位置に移動します（元の位置は保持）
        /// </summary>
        /// <param name="position">一時的な移動先</param>
        public void MoveTemporarily(Vector2Int position)
        {
            if (temporaryPosition.HasValue)
                throw new InvalidOperationException("既に一時移動中です");

            temporaryPosition = position;
            OnPositionChanged?.Invoke(GridPosition);
        }

        /// <summary>
        /// 一時移動を解除し、元の位置に戻ります
        /// </summary>
        public void ReturnToOriginalPosition()
        {
            if (!temporaryPosition.HasValue)
                throw new InvalidOperationException("一時移動中ではありません");

            temporaryPosition = null;
            OnPositionChanged?.Invoke(GridPosition);
        }

        /// <summary>
        /// 論理位置（敵の移動判定用）を取得
        /// </summary>
        public Vector2Int GetLogicalPosition() => gridPosition;

        /// <summary>
        /// HP変更時のイベント
        /// </summary>
        public event Action<int, int> OnHpChanged; // (currentHp, maxHp)

        /// <summary>
        /// 死亡時のイベント
        /// </summary>
        public event Action OnDeath;

        /// <summary>
        /// のけぞり時のイベント（カード効果の中断に使用）
        /// </summary>
        public event Action OnStaggered;

        /// <summary>
        /// 位置変更時のイベント
        /// </summary>
        public event Action<Vector2Int> OnPositionChanged;
    }
    /// <summary>論理フレーム内のHP変化。全参加者の確定と通知を分離する。</summary>
    internal sealed class FrameHpChanges
    {
        public bool Active { get; private set; }
        private int initialHp;
        private long damage, healing, poison;
        private bool touched, notify, died;
        private readonly List<(int Amount, Action<int> Notify)> poisonHits = new List<(int, Action<int>)>();
        private readonly List<(int Amount, Action<int> Notify)> acceptedPoisonHits = new List<(int, Action<int>)>();
        public void Begin(int hp)
        {
            if (Active) throw new InvalidOperationException("HPフレームが既に開始されています");
            initialHp = hp; damage = healing = poison = 0; touched = notify = died = false; Active = true;
            poisonHits.Clear(); acceptedPoisonHits.Clear();
        }
        public void Damage(int amount) { damage += amount; touched = true; }
        public void Heal(int amount) { healing += amount; touched = true; }
        public void Poison(int amount, Action<int> notifyHit = null)
        {
            // 後から入る同フレーム回復も含め、受理量と通知を終端まで確定しない。
            poison += amount;
            poisonHits.Add((amount, notifyHit));
        }
        public void PublishPoisonHits()
        {
            var hits = acceptedPoisonHits.ToArray(); acceptedPoisonHits.Clear();
            foreach (var hit in hits) hit.Notify?.Invoke(hit.Amount);
        }
        public int Commit(int hp, int maxHp)
        {
            if (!Active) return hp;
            Active = false;
            long healedHp = (long)initialHp + healing;
            long acceptedPoison = Math.Min(poison, Math.Max(0L, healedHp - 1));
            notify = touched || acceptedPoison > 0;
            long remaining = acceptedPoison;
            foreach (var hit in poisonHits)
            {
                int accepted = (int)Math.Min(hit.Amount, remaining); remaining -= accepted;
                if (accepted > 0 && hit.Notify != null) acceptedPoisonHits.Add((accepted, hit.Notify));
            }
            poisonHits.Clear();
            // 毒だけではHP1を割らず、通常ダメージはその下限とは独立して死亡させ得る。
            int result = (int)Math.Max(0L, Math.Min(maxHp, healedHp - acceptedPoison - damage));
            died = initialHp > 0 && result == 0;
            return result;
        }
        public bool TakeNotification(out bool death)
        { death = died; bool result = notify; notify = died = false; return result; }
    }

}
