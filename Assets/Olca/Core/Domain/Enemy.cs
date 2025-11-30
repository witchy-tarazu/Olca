using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// 敵キャラクター
    /// </summary>
    public class Enemy
    {
        private int currentHp;
        private readonly int maxHp;
        private readonly ElementType element;
        private Vector2Int gridPosition;

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

        /// <summary>HP割合（0.0～1.0）</summary>
        public float HpRatio => maxHp > 0 ? (float)currentHp / maxHp : 0f;

        /// <summary>敵のID</summary>
        public string EnemyId { get; }

        /// <summary>敵の名前</summary>
        public string Name { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Enemy(string enemyId, string name, int maxHp, ElementType element, Vector2Int initialPosition)
        {
            EnemyId = enemyId;
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
            if (damage < 0) throw new ArgumentException("ダメージは0以上である必要があります", nameof(damage));

            // 属性相性によるダメージ補正
            float multiplier = GetElementMultiplier(attackElement, element);
            int finalDamage = (int)(damage * multiplier);

            currentHp -= finalDamage;
            if (currentHp < 0)
            {
                currentHp = 0;
            }

            OnHpChanged?.Invoke(currentHp, maxHp);
            OnDamaged?.Invoke(finalDamage, multiplier);

            if (IsDead)
            {
                OnDeath?.Invoke();
            }
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
        /// 属性相性の倍率を取得
        /// 水→火、火→風、風→土、土→水で2倍
        /// </summary>
        private float GetElementMultiplier(ElementType attackElement, ElementType defenseElement)
        {
            if (attackElement == ElementType.None || defenseElement == ElementType.None)
            {
                return 1.0f;
            }

            // 相性チェック
            if (attackElement == ElementType.Water && defenseElement == ElementType.Fire) return 2.0f;
            if (attackElement == ElementType.Fire && defenseElement == ElementType.Wind) return 2.0f;
            if (attackElement == ElementType.Wind && defenseElement == ElementType.Earth) return 2.0f;
            if (attackElement == ElementType.Earth && defenseElement == ElementType.Water) return 2.0f;

            return 1.0f;
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
