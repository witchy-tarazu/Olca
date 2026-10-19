using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 基本攻撃AI: プレイヤーに隣接したら攻撃
    /// 整数ベース（ミリ秒）で時間を管理
    /// </summary>
    public class BasicAttackAction : IEnemyAIAction
    {
        private readonly int attackDamage;
        private readonly ElementType attackElement;
        private readonly AttackProperty attackProperties;
        private readonly int cooldownMs;

        /// <summary>
        /// コンストラクタ - ミリ秒版
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldownMs">攻撃後のクールダウン時間（ミリ秒）</param>
        public BasicAttackAction(int attackDamage, int cooldownMs, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None)
        {
            AttackPropertyRules.Validate(attackProperties);
            this.attackProperties = attackProperties;
            this.attackElement = attackElement;
            this.attackDamage = attackDamage;
            this.cooldownMs = cooldownMs;
        }

        /// <summary>
        /// コンストラクタ - float版（後方互換）
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldown">攻撃後のクールダウン時間（秒）</param>
        public BasicAttackAction(int attackDamage = 10, float cooldown = 2.0f, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None)
            : this(attackDamage, (int)(cooldown * 1000f), attackElement, attackProperties)
        {
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

            context.Player.TakeDamage(attackDamage, attackElement: attackElement, attackProperties: attackProperties);
            context.Logger?.LogInfo($"{context.Self.Name}が{attackDamage}ダメージを与えた");
        }

        /// <summary>
        /// 攻撃後のクールダウン時間を取得（ミリ秒）
        /// </summary>
        public int GetCooldownMs() => cooldownMs;

        /// <summary>
        /// 攻撃後のクールダウン時間を取得（秒）- 互換性用
        /// </summary>
        public float GetCooldown() => cooldownMs / 1000f;
    }
}
