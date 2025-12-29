using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 基本攻撃AI: プレイヤーに隣接したら攻撃
    /// </summary>
    public class BasicAttackAction : IEnemyAIAction
    {
        private readonly int attackDamage;
        private readonly float cooldown;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldown">攻撃後のクールダウン時間（秒）</param>
        public BasicAttackAction(int attackDamage = 10, float cooldown = 2.0f)
        {
            this.attackDamage = attackDamage;
            this.cooldown = cooldown;
        }

        /// <summary>
        /// プレイヤーと隣接していれば攻撃可能
        /// </summary>
        public bool CanExecute(AIContext context)
        {
            if (context.Self == null || context.Player == null) return false;
            if (context.Player.IsDead) return false;

            // プレイヤーと隣接（距離1）していれば攻撃可能
            return context.GetDistanceToPlayer() == 1;
        }

        /// <summary>
        /// プレイヤーにダメージを与える
        /// </summary>
        public void Execute(AIContext context)
        {
            if (context.Player == null) return;

            context.Player.TakeDamage(attackDamage);
            context.Logger?.LogInfo($"{context.Self.Name}が{attackDamage}ダメージを与えた");
        }

        /// <summary>
        /// 攻撃後のクールダウン時間を取得
        /// </summary>
        public float GetCooldown() => cooldown;
    }
}
