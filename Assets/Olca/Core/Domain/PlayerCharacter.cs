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
        public ElementResistances ActiveResistances => transformationResistances ?? Resistances;
        public bool HasTransformationDefense => transformationResistances != null;
        private ElementResistances transformationResistances;
        private ElementType transformationBreakElement;
        internal Func<bool> IsOnGrass;
        public event Action OnTransformationDefenseEnded;
        public void SetElementResistances(ElementResistances value)
            => Resistances = value ?? throw new ArgumentNullException(nameof(value));
        // 変身選択・使用回数・持続管理とは独立した、被弾計算用の置換状態。
        public void BeginTransformationDefense(ElementResistances value, ElementType breakElement)
        {
            if (breakElement == ElementType.None || !Enum.IsDefined(typeof(ElementType), breakElement))
                throw new ArgumentOutOfRangeException(nameof(breakElement));
            transformationResistances = value ?? throw new ArgumentNullException(nameof(value));
            transformationBreakElement = breakElement;
        }
        public void EndTransformationDefense()
        {
            if (!HasTransformationDefense) return;
            transformationResistances = null;
            OnTransformationDefenseEnded?.Invoke();
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

        /// <summary>無敵中か</summary>
        public bool IsInvincible => invincibilityTimer.IsActive;

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
        public PlayerCharacter(int maxHp, int attackPower = 10, float customSpeed = 1.0f, int poise = 30)
            : this(maxHp, Vector2Int.Zero, attackPower, customSpeed, poise)
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
        public PlayerCharacter(int maxHp, Vector2Int initialPosition, int attackPower = 10, float customSpeed = 1.0f, int poise = 30)
        {
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
        public void TakeDamage(int damage, bool forceStagger = false, bool affectsPoise = true, ElementType attackElement = ElementType.None)
        {
            bool breakTransformation = false;
            OnDamageProcessingStarted?.Invoke();
            try
            {
                if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));

                // 無敵中はダメージ・のけぞりを無効化
                if (invincibilityTimer.IsActive) return;

                breakTransformation = HasTransformationDefense && attackElement == transformationBreakElement;
                bool grass = attackElement == ElementType.Fire && TerrainState.ReceivesPanelEffects && (IsOnGrass?.Invoke() ?? false);
                damage = ActiveResistances.CalculateDamage(damage, attackElement, grass);

                // バリアがある場合は先にバリアで吸収
                if (barrierHp > 0)
                {
                    int absorbed = Math.Min(barrierHp, damage);
                    barrierHp -= absorbed;
                    damage -= absorbed;
                    if (damage <= 0) return;
                }

                currentHp -= damage;
                if (currentHp < 0)
                {
                    currentHp = 0;
                }

                OnHpChanged?.Invoke(currentHp, maxHp);

                // のけぞり判定（強靭度システム）
                // 累計ダメージが強靭度を超えると、のけぞりが発生する
                if (!IsDead && affectsPoise)
                {
                    accumulatedDamage += damage;
                    if (forceStagger || accumulatedDamage >= poise)
                    {
                        accumulatedDamage = 0;  // のけぞり後はリセット
                        OnStaggered?.Invoke();
                    }
                }

                if (IsDead)
                {
                    OnDeath?.Invoke();
                }
            }
            finally
            {
                try { if (breakTransformation) EndTransformationDefense(); }
                finally { OnDamageProcessingCompleted?.Invoke(); }
            }
        }

        /// <summary>
        /// HPを回復する
        /// </summary>
        /// <param name="amount">回復量</param>
        public void Heal(int amount)
        {
            if (amount < 0) throw new ArgumentException("回復量は0以上である必要があります", nameof(amount));

            currentHp += amount;
            if (currentHp > maxHp)
            {
                currentHp = maxHp;
            }

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
            barrierHp = Math.Max(0, hp);
        }

        /// <summary>
        /// 無敵を付与する
        /// </summary>
        /// <param name="durationMs">無敵持続時間（ミリ秒）</param>
        public void SetInvincible(int durationMs)
        {
            if (durationMs <= 0) return;
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
}
