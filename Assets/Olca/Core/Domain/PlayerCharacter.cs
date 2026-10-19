using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// プレイヤーキャラクター
    /// </summary>
    public class PlayerCharacter
    {
        /// <summary>戦闘限定の地形特性。戦闘終了時に解除する。</summary>
        public BattleTerrainState TerrainState { get; } = new BattleTerrainState();

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

        private readonly FrameHpChanges frameHp = new FrameHpChanges();
        private bool frameInvincible, frameStaggered, frameWasStaggered;
        private FrameCountdown staggerTimer;
        public int StaggerDurationFrames { get; }
        public int StaggerRemainingFrames => staggerTimer.Frames;
        public bool IsStaggered => !IsDead && (frameHp.Active ? frameWasStaggered : staggerTimer.IsActive);
        private void StartStagger() { if (StaggerDurationFrames > 0) staggerTimer.SetFrames(StaggerDurationFrames); }
        internal void TickStagger(int ms, bool logical) { if (logical) staggerTimer.TickFrame(); else staggerTimer.Tick(ms); }
        internal void ClearStagger() { staggerTimer.SetFrames(0); frameWasStaggered = false; }
        private int pendingInvincibilityMs;
        private long framePoiseDamage;
        private bool framePoiseHit, frameForceStagger;
        internal bool HasPendingHpFrame => frameHp.Active;
        internal void BeginHpFrame() { frameHp.Begin(currentHp); frameInvincible = invincibilityTimer.IsActive; frameWasStaggered = staggerTimer.IsActive; frameStaggered = false; pendingInvincibilityMs = 0; framePoiseDamage = 0; framePoiseHit = frameForceStagger = false; }
        internal void CommitHpFrame()
        {
            currentHp = frameHp.Commit(currentHp, maxHp);
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
            if (died) OnDeath?.Invoke();
        }
        private int currentHp;
        private readonly int maxHp;
        private readonly int attackPower;
        private readonly float customSpeed;
        private int poise;              // 強靭度（累計ダメージがこれを超えるとのけぞり）
        private int accumulatedDamage;  // 現在行動中の累計ダメージ
        private Vector2Int gridPosition;
        private Vector2Int? temporaryPosition;
        private int barrierHp;          // バリア（ダメージを吸収するHP）
        private FrameCountdown invincibilityTimer; // 無敵残り時間（固定精度）
        private float nextCardMultiplier = 1.0f; // 次カード威力倍率（集中バフ）

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
        public int BarrierHp => barrierHp;

        /// <summary>バリアを持っているか</summary>
        public bool HasBarrier => barrierHp > 0;
        public int AuraThreshold { get; private set; }
        public bool HasAura => AuraThreshold > 0;

        /// <summary>外部指定の単独オーラ。共存・再付与の未規定状態は変更前に拒否する。0は解除。</summary>
        public void SetAura(int threshold)
        {
            if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (threshold > 0 && (HasAura || HasBarrier || HasTransformationDefense || HasTransformationPreview))
                throw new InvalidOperationException("オーラの共存・再付与・変身優先順は未対応です");
            AuraThreshold = threshold;
        }

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
        public PlayerCharacter(int maxHp, int attackPower = 10, float customSpeed = 1.0f, int poise = 30, int staggerDurationFrames = 0)
            : this(maxHp, Vector2Int.Zero, attackPower, customSpeed, poise, staggerDurationFrames)
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
        public PlayerCharacter(int maxHp, Vector2Int initialPosition, int attackPower = 10, float customSpeed = 1.0f, int poise = 30, int staggerDurationFrames = 0)
        {
            if (staggerDurationFrames < 0) throw new ArgumentOutOfRangeException(nameof(staggerDurationFrames));
            StaggerDurationFrames = staggerDurationFrames;
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
                if (HasAura && damage > 0)
                {
                    int auraDamage = ElementResistances.Neutral.CalculateDamage(damage, attackElement, grass);
                    if (auraDamage >= AuraThreshold) AuraThreshold = 0;
                    return false;
                }
                int originalDamage = damage;
                int bodyDamage = ActiveResistances.CalculateDamage(damage, attackElement, grass);
                // 変身解除は既存の弱点規則を維持し、バリアの耐久計算とは分ける。
                breakTransformation = (originalDamage == 0 || bodyDamage > 0) && HasTransformationDefense && transformationWeakness.Matches(attackElement, attackProperties);
                // 本体耐性を耐久計算に使わず地形補正だけで計上し、破壊した一発も全て防ぐ。
                // 元から威力0の状態専用ヒットはバリアを通過する。
                if (barrierHp > 0 && damage > 0)
                {
                    int barrierDamage = ElementResistances.Neutral.CalculateDamage(damage, attackElement, grass);
                    barrierHp = Math.Max(0, barrierHp - barrierDamage);
                    return false;
                }
                damage = bodyDamage;
                if (originalDamage > 0 && damage == 0) return false; // 耐性無効は強靭度・付随効果へ進めない。

                if (frameHp.Active) frameHp.Damage(damage);
                else
                {
                    currentHp = Math.Max(0, currentHp - damage);
                    OnHpChanged?.Invoke(currentHp, maxHp);
                }

                // 明示量はHP軽減と独立。完全無効・バリア完全吸収は上段で拒否する。
                int impact = poiseDamage ?? damage;
                // 明示0では蓄積も閾値判定も更新しない。既存の強制のけぞり指定は維持する。
                if (!IsDead && affectsPoise && (poiseDamage != 0 || forceStagger))
                {
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
                    OnDeath?.Invoke();
                }
                return true;
            }
            finally
            {
                try { if (breakTransformation) EndTransformationDefense(); }
                finally { OnDamageProcessingCompleted?.Invoke(); }
            }
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
        /// カード使用完了時や行動キャンセル後に呼び出す
        /// </summary>
        public void ResetPoiseAccumulation()
        {
            accumulatedDamage = 0;
        }

        /// <summary>
        /// バリアを設定する（上書き）
        /// </summary>
        /// <param name="hp">バリアHP</param>
        public void SetBarrier(int hp)
        {
            if (hp > 0 && HasAura) throw new InvalidOperationException("バリアとオーラの共存は未対応です");
            barrierHp = Math.Max(0, hp);
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

        /// <summary>
        /// 集中バフを設定する
        /// </summary>
        /// <param name="multiplier">次カードへの威力倍率</param>
        public void SetConcentrateBuff(float multiplier)
        {
            nextCardMultiplier = Math.Max(1.0f, multiplier);
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
        public void Begin(int hp)
        {
            if (Active) throw new InvalidOperationException("HPフレームが既に開始されています");
            initialHp = hp; damage = healing = poison = 0; touched = notify = died = false; Active = true;
        }
        public void Damage(int amount) { damage += amount; touched = true; }
        public void Heal(int amount) { healing += amount; touched = true; }
        public int Poison(int amount)
        {
            // 通常攻撃・同フレーム回復とは独立して、開始時HPを基準に毒をHP1で止める。
            int accepted = (int)Math.Min(amount, Math.Max(0L, (long)initialHp - 1 - poison));
            poison += accepted; touched |= accepted > 0; return accepted;
        }
        public int Commit(int hp, int maxHp)
        {
            if (!Active) return hp;
            Active = false; notify = touched;
            int result = (int)Math.Max(0L, Math.Min(maxHp, (long)initialHp + healing - damage - poison));
            died = initialHp > 0 && result == 0;
            return result;
        }
        public bool TakeNotification(out bool death)
        { death = died; bool result = notify; notify = died = false; return result; }
    }

}
