using System;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using UnityEngine;

namespace Olca.Presentation.Effects
{
    /// <summary>既存の通常攻撃phaseを読むだけの試用本体。命中後3論理フレームの接触姿勢は暫定演出。</summary>
    public sealed class TrialBasicKickBody : IDisposable
    {
        public const int ContactPoseFrames=3; // 暫定の見た目。命中窓や後隙を延長しない。
        private readonly SpriteRenderer body;
        private readonly Animator animator;
        private readonly Func<string,Sprite> load;
        private Sprite prepare,contact,original;
        private BattleSystem owner;
        private bool attempted,active,animatorWasEnabled,disposed;
        public TrialBasicKickBody(SpriteRenderer body,Animator animator,Func<string,Sprite> loader=null)
        {
            this.body=body!=null?body:throw new ArgumentNullException(nameof(body));this.animator=animator;
            load=loader??(key=>Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_"+key));
        }
        public static bool CanDisplay(BattleSystem battle)=>battle!=null&&battle.IsPlayerBasicAction&&!battle.IsGameOver&&!battle.Player.IsDead&&
            !battle.Player.IsActionPrevented&&battle.Transformations.ActiveId==null&&
            (battle.Player.DepartureStyle.Id==DepartureStyleDefinition.BasicId||battle.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id);
        public bool Apply(BattleSystem battle)
        {
            if(disposed||body==null||!CanDisplay(battle)){
                Clear();return false;
            }
            if(!attempted){attempted=true;prepare=load("prepare");contact=load("contact");}
            if(prepare==null||contact==null){Clear();return false;}
            if(!ReferenceEquals(owner,battle))Clear();
            if(!active){original=body.sprite;animatorWasEnabled=animator!=null&&animator.enabled;owner=battle;active=true;}
            if(animator!=null)animator.enabled=false;
            bool striking=battle.PlayerActionPhase==PlayerAttackPhase.Executing||
                battle.PlayerActionPhase==PlayerAttackPhase.Recovery&&battle.RecoveryElapsedFrames<ContactPoseFrames;
            body.sprite=striking?contact:prepare;return true;
        }
        public void Clear()
        {
            if(active){if(body!=null)body.sprite=original;if(animator!=null)animator.enabled=animatorWasEnabled;}
            active=false;owner=null;original=null;
        }
        public void Dispose(){if(disposed)return;Clear();disposed=true;prepare=contact=null;}
    }
}
