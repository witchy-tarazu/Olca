using System;
using Olca.Core.Interfaces;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
namespace Olca.Core.Logic.Battle
{
    /// <summary>開始通知/初回カスタム準備の後に一度だけ適用する既存接敵効果。</summary>
    public sealed class BattleStartEffects
    {
        private readonly BattleSystem battle;
        private readonly IGameLogger logger;
        private bool configured;
        public bool Applied { get; private set; }
        public bool DashContact { get; private set; }
        public ActionSystem.DashContactEffect? Effect { get; private set; }
        public int? Value { get; private set; }
        public event Action<string> OnApplied;
        internal BattleStartEffects(BattleSystem battle,IGameLogger logger){this.battle=battle;this.logger=logger;}
        public void Configure(bool dash,int? effect,int? value)
        {
            if(configured||Applied||battle.LogicalFrame!=0)throw new InvalidOperationException("開始条件は開始前に一度だけ設定できます");
            if((!dash&&effect.HasValue)||(value.HasValue&&!effect.HasValue)||(effect.HasValue&&!Enum.IsDefined(typeof(ActionSystem.DashContactEffect),effect.Value)))throw new ArgumentException("接敵開始条件が不正です");
            var selected=effect.HasValue?(ActionSystem.DashContactEffect?)effect.Value:null;
            int? amount=selected.HasValue?ActionSystem.ResolveDashDefinition(selected.Value,value).Value:(int?)null;
            DashContact=dash;Effect=selected;Value=amount;configured=true;
        }
        public bool Complete()
        {
            if(Applied||battle.IsGameOver)return false;
            Applied=true; // 外部通知から再入しても同じ初撃を重ねない。
            if(DashContact&&Effect.HasValue){
                int amount=Value.Value;
                switch(Effect.Value){
                    case ActionSystem.DashContactEffect.InitialDamage:
                        foreach(var enemy in battle.Enemies){enemy.TakeDamage(amount,ElementType.None);logger?.LogInfo($"  - {enemy.Name}に{amount}ダメージ！（ダッシュ接敵）");}break;
                    case ActionSystem.DashContactEffect.Stun:
                        foreach(var enemy in battle.Enemies)enemy.ApplyStunFrames(amount);logger?.LogInfo($"  - 敵をスタン（{amount}フレーム）！（ダッシュ接敵）");break;
                    case ActionSystem.DashContactEffect.PlayerDamage:
                        battle.Player.TakeDamage(amount,poiseDamage:0);break;
                    case ActionSystem.DashContactEffect.FirstStrike:
                        battle.SetFirstStrike();logger?.LogInfo("  - 先制攻撃！カスタムゲージ満タン（ダッシュ接敵）");break;
                }
            }
            OnApplied?.Invoke(!DashContact?"start:normal":!Effect.HasValue?"start:dash:none":"start:"+Effect+":"+Value);
            return true;
        }
    }
}
