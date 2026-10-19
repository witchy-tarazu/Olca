using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// 敵キャラクター
    /// </summary>
    public class Enemy
    {
        /// <summary>戦闘限定の地形特性。戦闘終了時に解除する。</summary>
        public BattleTerrainState TerrainState { get; } = new BattleTerrainState();

        public ElementResistances Resistances { get; }
        internal Func<bool> IsOnGrass;

        private int currentHp;
        private readonly int maxHp;
        private readonly ElementType element;
        private Vector2Int gridPosition;
        private FrameCountdown stunTimer;
        private StatusEffect activeStatusEffect;
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
        public bool IsStunned => stunTimer.IsActive;

        /// <summary>残スタン時間（ミリ秒）</summary>
        public int StunDurationMs => stunTimer.Milliseconds;

        /// <summary>現在の状態異常</summary>
        public StatusEffect ActiveStatus => activeStatusEffect;

        /// <summary>麻痺中か</summary>
        public bool IsParalyzed => activeStatusEffect?.Type == StatusEffectType.Paralyze
                                && activeStatusEffect.RemainingMs > 0;

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
        public Enemy(string enemyId, string name, int maxHp, ElementType element, Vector2Int initialPosition, bool isSummoned = false, ElementResistances resistances = null)
        {
            Resistances = resistances ?? ElementResistances.Neutral;
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
        public void TakeDamage(int damage, ElementType attackElement)
        {
            OnDamageProcessingStarted?.Invoke();
            try
            {
                if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));
                if (IsDead) return;

                // 属性相性によるダメージ補正
                bool grass = attackElement == ElementType.Fire && TerrainState.ReceivesPanelEffects && (IsOnGrass?.Invoke() ?? false);
                float multiplier = (float)(Resistances.Multiplier(attackElement) * (grass ? 2 : 1));
                int finalDamage = Resistances.CalculateDamage(damage, attackElement, grass);

                currentHp -= finalDamage;
                if (currentHp < 0)
                {
                    currentHp = 0;
                }

                // 通知中の別攻撃で死亡しても、この非致死攻撃から二重通知しない。
                bool killedByThisHit = IsDead;
                OnHpChanged?.Invoke(currentHp, maxHp);
                OnDamaged?.Invoke(finalDamage, multiplier);

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
            stunTimer.Tick(deltaTimeMs);
        }

        public void TickStunFrame() => stunTimer.TickFrame();

        /// <summary>
        /// 状態異常を付与する（既存の状態異常は上書き）
        /// </summary>
        /// <param name="status">付与する状態異常</param>
        public void ApplyStatus(StatusEffect status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (IsDead) return;
            activeStatusEffect = status;
            poisonDamageRemainder = 0; poisonFrameRemainder = 0;
        }

        /// <summary>戦闘終了で必ず解除する麻痺だけを取り除く。他の状態の持越し規則は変更しない。</summary>
        internal void ClearBattleParalysis()
        {
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
            if (activeStatusEffect.TickFrame()) { activeStatusEffect = null; poisonFrameRemainder = 0; }
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
