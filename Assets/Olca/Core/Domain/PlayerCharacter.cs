using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// プレイヤーキャラクター
    /// </summary>
    public class PlayerCharacter
    {
        private int currentHp;
        private readonly int maxHp;
        private readonly int attackPower;
        private readonly float customSpeed;
        private int poise;              // 強靭度（累計ダメージがこれを超えるとのけぞり）
        private int accumulatedDamage;  // 現在行動中の累計ダメージ
        private Vector2Int gridPosition;
        private Vector2Int? temporaryPosition;

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
        public void TakeDamage(int damage, bool forceStagger = false)
        {
            if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));

            currentHp -= damage;
            if (currentHp < 0)
            {
                currentHp = 0;
            }

            OnHpChanged?.Invoke(currentHp, maxHp);

            // のけぞり判定（強靭度システム）
            // 累計ダメージが強靭度を超えると、のけぞりが発生する
            if (!IsDead)
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
