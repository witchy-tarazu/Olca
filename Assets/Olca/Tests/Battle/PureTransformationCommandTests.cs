using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Integration;

namespace Olca.Tests.Battle
{
    public class PureTransformationCommandTests
    {
        private static BattleFrameObservation Form(BattleSimulation s,string id)=>s.Step(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.PreviewTransformation,id)));
        private static BattleFrameObservation Command(BattleSimulation s,CustomCommandKind kind)=>s.Step(new BattleFrameCommand(custom:new CustomScreenCommand(kind)));
        private static void Frames(BattleSimulation s,int count){for(int i=0;i<count;i++)s.Step(new BattleFrameCommand());}
        private static void Activate(BattleSimulation s,string id="fixture-a") { Form(s,id);Command(s,CustomCommandKind.Confirm);Command(s,CustomCommandKind.ReturnToBattle);Frames(s,12); }
        [TestCase(1)] [TestCase(7)] [TestCase(31)] [TestCase(240)]
        public void 変身弱点解除と期間終了の同じ列は負荷と分割に依存しない(int batch)
        {
            string expected=TransformationBattleReplay.Trace();
            Assert.AreEqual(expected,TransformationBattleReplay.Trace(batch:batch,renderWork:()=>{long n=1;for(int i=0;i<2000;i++)n=unchecked(n*33+i);GC.KeepAlive(n);}));
        }
        [Test]
        public void プレビュー加工連鎖取消と二重確定は無消費で演出停止とバリア弱点解除を維持する()
        {
            var s=TransformationBattleReplay.Create();var battle=s.Battle;var state=battle.Transformations;var screen=battle.CustomScreen;
            var original=screen.Commands.Observe();long frozen=0;bool barrierProtected=false,brokenBarrierProtected=false;int ended=0;
            var commands=TransformationBattleReplay.Commands();
            for(int i=0;i<commands.Count;i++){
                var result=s.Step(commands[i]);
                if(i==1){Assert.AreEqual(12,screen.MaxHandSize);Assert.AreEqual(12,screen.CurrentHand.Count);Assert.IsFalse(state.IsUsed("fixture-a"));Assert.AreEqual(0,battle.Player.RemainingTransformationPhases);}
                if(i==3){Assert.AreEqual("fixture-a",state.PendingId);Assert.IsEmpty(screen.SelectedCards);}
                if(i==5)Assert.AreEqual(2,screen.CustomizationUndoCount);
                if(i==6){Assert.IsNull(state.PendingId);Assert.AreEqual(10,screen.MaxHandSize);Assert.AreEqual(10,screen.CurrentHand.Count);Assert.AreEqual(0,screen.CustomizationUndoCount);
                    CollectionAssert.AreEquivalent(original.Hand.Select(c=>c.DefinitionHash),screen.Commands.Observe().Hand.Select(c=>c.DefinitionHash));}
                if(i==8){Assert.AreEqual("fixture-a",state.ActiveId);Assert.AreEqual(3,state.RemainingPhases);Assert.AreEqual(12,state.ActivationFramesRemaining);Assert.AreEqual(1,state.UsedIds.Count);}
                if(i==9){Assert.AreEqual(3,state.RemainingPhases);Assert.AreEqual(1,state.UsedIds.Count);}
                if(i==11)frozen=battle.LogicalFrame;
                if(i>=11&&i<=16){Assert.AreEqual(frozen,battle.LogicalFrame);Assert.AreEqual(12,state.ActivationFramesRemaining);}
                barrierProtected|=battle.Player.BarrierHp==10&&state.ActiveId=="fixture-a"&&battle.Player.CurrentHp==100;
                brokenBarrierProtected|=battle.Player.BarrierHp==0&&state.ActiveId=="fixture-a"&&battle.Player.CurrentHp==100;
                ended+=result.Events.Count(x=>x=="transformation-ended");
            }
            Assert.IsTrue(barrierProtected&&brokenBarrierProtected);Assert.AreEqual(95,battle.Player.CurrentHp,"弱点攻撃も変身中耐性で計算後に解除");
            Assert.IsNull(state.ActiveId);Assert.AreEqual(0,state.RemainingPhases);Assert.AreEqual(1,ended);Assert.IsTrue(state.IsUsed("fixture-a"));
            Assert.IsTrue(screen.CurrentHand.Any(c=>c.Id=="converted"),"確定済み変換は解除で戻さない");Assert.AreEqual(10,screen.MaxHandSize);
            Assert.IsFalse(battle.IsPaused);Assert.AreEqual(100,battle.Player.MaxHp);
        }
        [Test]
        public void 自然充填と三回の開始で終了し別変身の仮選択取消は残回数を戻す()
        {
            var s=TransformationBattleReplay.Create(123,false);var state=s.Battle.Transformations;int openings=0,ended=0;
            foreach(var command in TransformationBattleReplay.Commands(123,false)){
                var result=s.Step(command);ended+=result.Events.Count(x=>x=="transformation-ended");
                if(command.Custom?.Kind==CustomCommandKind.Open){openings++;Assert.AreEqual(3-openings,state.RemainingPhases);}
                if(command.Custom?.Kind==CustomCommandKind.CancelTransformation&&openings==1){Assert.AreEqual("fixture-a",state.ActiveId);Assert.AreEqual(2,state.RemainingPhases);Assert.IsNull(state.PendingId);Assert.IsFalse(state.IsUsed("fixture-b"));}
            }
            Assert.AreEqual(3,openings);Assert.AreEqual(1,ended);Assert.IsNull(state.ActiveId);Assert.AreEqual(0,state.RemainingPhases);Assert.AreEqual(12,state.ReleaseFramesRemaining);
            Assert.IsFalse(s.Battle.IsPaused);Assert.IsTrue(state.IsUsed("fixture-a"));Assert.AreEqual(100,s.Battle.Player.CurrentHp);
        }
        [Test]
        public void 攻撃性能とタメ切替は通常入力へ反映し取消や復帰で暴発しない()
        {
            var attack=new BasicAttackInputState();var s=TransformationBattleReplay.Create(123,false,attack);Activate(s);
            var damage=new List<int>();s.Battle.OnPlayerAttack+=(_,power)=>damage.Add(power);
            s.Step(new BattleFrameCommand(new LogicalInput{Horizontal=1,HorizontalDown=1}));Frames(s,36);
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButton=true,BasicAttackButtonDown=true}));
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButtonUp=true}));Frames(s,4);
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButton=true,BasicAttackButtonDown=true}));
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButton=true}));s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButton=true}));
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButtonUp=true}));Frames(s,4);
            Assert.AreEqual(2,damage.Count);Assert.AreEqual(s.Battle.Player.AttackPower*150/100,damage[0]);Assert.AreEqual(s.Battle.Player.AttackPower*300/100,damage[1]);
            s.Step(new BattleFrameCommand(new LogicalInput{BasicAttackButton=true,BasicAttackButtonDown=true}));Assert.AreEqual(1,attack.ChargeFrames);
            s.Battle.CustomGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);Command(s,CustomCommandKind.Open);Form(s,"fixture-b");
            Command(s,CustomCommandKind.CancelTransformation);Assert.AreEqual("fixture-a",s.Battle.Transformations.ActiveId);
            Form(s,"fixture-b");Command(s,CustomCommandKind.Confirm);Command(s,CustomCommandKind.ReturnToBattle);Frames(s,13);
            Assert.AreEqual(0,attack.ChargeFrames);Assert.AreEqual(2,damage.Count);Assert.AreEqual(ElementType.Fire,s.Battle.ActivePlayerElement);
            Assert.AreEqual(3,s.Battle.Transformations.RemainingPhases);Assert.AreEqual(2,s.Battle.Transformations.UsedIds.Count);
            foreach(var enemy in s.Battle.Enemies.ToArray())enemy.TakeDamage(enemy.CurrentHp,ElementType.None);
            Assert.AreEqual(BattleState.Victory,s.Battle.State);Assert.IsFalse(s.Battle.Player.HasTransformationDefense);
        }
        [TestCase(false)] [TestCase(true)]
        public void 無敵または耐性ゼロダメージは弱点でも解除せず使用済みを維持する(bool invincible)
        {
            var s=TransformationBattleReplay.Create(123,false);Activate(s);var battle=s.Battle;
            if(invincible)battle.Player.SetInvincible(1000);
            else battle.Player.BeginTransformationDefense(new ElementResistances(fire:100),ElementType.Fire);
            var enemy=new Enemy("hit","hit",10,ElementType.Fire,new Vector2Int(1,0));
            battle.RunLogicalFrame(()=>new BasicAttackAction(10,1000,attackElement:ElementType.Fire).Execute(new AIContext(enemy,battle.Player,battle.Field,s.Random,null)));
            Assert.AreEqual(100,battle.Player.CurrentHp);Assert.AreEqual("fixture-a",battle.Transformations.ActiveId);Assert.IsTrue(battle.Transformations.IsUsed("fixture-a"));
        }
        [Test]
        public void Unityの変身選択取消ハンドラーも同じコマンドを通る()
        {
            var s=TransformationBattleReplay.Create(123,false);var root=new UnityEngine.GameObject("transformation-command-view",typeof(UnityEngine.RectTransform));
            var view=root.AddComponent<Olca.Presentation.CustomScreen.CustomScreenView>();var type=view.GetType();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            type.GetField("customScreen",flags).SetValue(view,s.Battle.CustomScreen);type.GetField("transformations",flags).SetValue(view,s.Battle.Transformations);type.GetField("padTransformationIndex",flags).SetValue(view,0);
            try{var method=type.GetMethod("ApplyCustomPad",flags);long before=s.Battle.CustomScreen.Commands.Sequence;
                method.Invoke(view,new object[]{Olca.Unity.CustomPadCommand.Decide});Assert.AreEqual("fixture-a",s.Battle.Transformations.PendingId);
                method.Invoke(view,new object[]{Olca.Unity.CustomPadCommand.Cancel});Assert.IsNull(s.Battle.Transformations.PendingId);Assert.AreEqual(before+2,s.Battle.CustomScreen.Commands.Sequence);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
