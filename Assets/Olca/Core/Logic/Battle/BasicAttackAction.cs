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
        private readonly int? poiseDamage;
        private readonly ElementType attackElement;
        private readonly AttackProperty attackProperties;
        private readonly int cooldownMs;
        private readonly System.Action<PlayerCharacter> onHit;
        private readonly int hitInvincibilityMs;

        /// <summary>
        /// コンストラクタ - ミリ秒版
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldownMs">攻撃後のクールダウン時間（ミリ秒）</param>
        public BasicAttackAction(int attackDamage, int cooldownMs, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null, System.Action<PlayerCharacter> onHit = null, int hitInvincibilityMs = 0)
        {
            if (hitInvincibilityMs < 0) throw new System.ArgumentOutOfRangeException(nameof(hitInvincibilityMs));
            this.onHit = onHit; this.hitInvincibilityMs = hitInvincibilityMs;
            if (poiseDamage < 0) throw new System.ArgumentOutOfRangeException(nameof(poiseDamage));
            this.poiseDamage = poiseDamage;
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
        public BasicAttackAction(int attackDamage = 10, float cooldown = 2.0f, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null, System.Action<PlayerCharacter> onHit = null, int hitInvincibilityMs = 0)
            : this(attackDamage, (int)(cooldown * 1000f), attackElement, attackProperties, poiseDamage, onHit, hitInvincibilityMs)
        {
        }

        /// <summary>
        /// プレイヤーと隣接していれば攻撃可能
        /// </summary>
        public bool CanExecute(AIContext context)
        {
            if (!context.HasVisiblePlayer) return false;

            // プレイヤーと隣接（距離1）していれば攻撃可能
            return context.GetDistanceToPlayer() == 1;
        }

        /// <summary>
        /// プレイヤーにダメージを与える
        /// </summary>
        public void Execute(AIContext context)
        {
            if (!CanExecute(context)) return;
            var direction = new Vector2Int(context.Player.GridPosition.X - context.Self.GridPosition.X,
                context.Player.GridPosition.Y - context.Self.GridPosition.Y);
            var resolved = context.ResolveDirectionalInput(direction);
            if (resolved != direction) {
                context.Logger?.LogInfo($"{context.Self.Name}の攻撃方向が反転して空振りした");
                return; // 行動は実行済み。AI側の既存クールダウンは通常どおり開始する。
            }

            if (context.Player.TryTakeDamage(attackDamage, attackElement: attackElement, attackProperties: attackProperties, poiseDamage: poiseDamage))
            {
                onHit?.Invoke(context.Player);
                if (attackDamage > 0 && !context.Player.IsDead && hitInvincibilityMs > 0) context.Player.SetInvincible(hitInvincibilityMs);
            }
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
