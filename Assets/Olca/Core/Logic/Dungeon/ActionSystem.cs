using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// アクション管理システム
    /// </summary>
    public class ActionSystem
    {
        /// <summary>
        /// ダッシュ接敵時の効果種類
        /// </summary>
        public enum DashContactEffect
        {
            InitialDamage,  // 初期ダメージ
            Stun,           // スタン
            FirstStrike,    // 先制攻撃
            PlayerDamage    // 接触先の定義によるプレイヤー被害
        }

        public bool CanDash { get; private set; } = true;
        public bool CanJump { get; private set; } = true;

        /// <summary>
        /// ダッシュ接敵時の効果を取得
        /// </summary>
        /// <returns></returns>
        public DashContactEffect GetDashEffect(Enemy enemy = null)
        {
            return enemy?.DashContact!=null?(DashContactEffect)enemy.DashContact.Outcome:DashContactEffect.InitialDamage;
        }

        internal static EnemyDashContactDefinition ResolveDashDefinition(DashContactEffect effect,int? value)
        {
            if(value.HasValue)return new EnemyDashContactDefinition((DashContactOutcome)effect,value.Value);
            int legacy=effect==DashContactEffect.InitialDamage?15:effect==DashContactEffect.Stun?180:
                effect==DashContactEffect.FirstStrike?0:throw new System.ArgumentException("新しい初撃には値の保存が必要です");
            return new EnemyDashContactDefinition((DashContactOutcome)effect,value??legacy);
        }

        /// <summary>
        /// ジャンプで回避可能かチェック
        /// </summary>
        public bool CanAvoidWithJump(MapCell cell, float playerHeight)
        {
            if (cell == null) return false;

            // ボスは回避不可
            if (cell.Type == CellType.Boss)
                return false;

            // 地上の敵は一定の高さ以上でジャンプすれば回避可能
            if (cell.Type == CellType.Enemy && cell.Height == 0f)
            {
                return playerHeight > 0.5f;
            }

            // 空中の敵は高さが足りなければ回避不可
            if (cell.Type == CellType.Enemy && cell.Height > 0f)
            {
                return playerHeight >= cell.Height - 0.2f;
            }

            // 障害物も同様
            if (cell.Type == CellType.Obstacle)
            {
                return playerHeight > cell.Height + 0.2f;
            }

            return false;
        }
    }
}
