using System;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Logic.Integration;

namespace Olca.Tests
{
    public sealed class OutsideTraversalTests
    {
        private static OutsideNormalKickSequence Kick(out Olca.Core.Logic.Battle.AttackInputReservation buffer,int startup=1,int active=2,int recovery=3,int? cancel=1)
        {buffer=new Olca.Core.Logic.Battle.AttackInputReservation();var k=new OutsideNormalKickSequence(buffer);k.Configure(System.Linq.Enumerable.Repeat(new OutsideKickTiming(startup,active,recovery,cancel),6).ToArray());return k;}
        [Test] public void 通常蹴りは時間六種が未登録なら発動せず製品の性能仮値を作らない()
        {
            var b=new Olca.Core.Logic.Battle.AttackInputReservation();var k=new OutsideNormalKickSequence(b);Assert.IsFalse(k.IsConfigured);Assert.IsFalse(k.TryRequest(0,true,1));Assert.IsFalse(k.IsActive);
            Assert.Throws<ArgumentException>(()=>k.Configure(new OutsideKickTiming(1,1,1)));Assert.Throws<ArgumentOutOfRangeException>(()=>new OutsideKickTiming(0,0,0));Assert.Throws<ArgumentOutOfRangeException>(()=>new OutsideKickTiming(0,1,1,2));
        }
        [Test] public void 空振りの地上三段は有効な先行入力で最速取消し三段目は後隙終了まで待つ()
        {
            var k=Kick(out var b);Assert.IsTrue(k.TryRequest(0,true,1));Assert.IsFalse(k.TryRequest(1,true,1));Assert.IsFalse(k.TryExecuteReserved(3,true,1));Assert.IsTrue(k.TryExecuteReserved(4,true,1));Assert.AreEqual(OutsideKickKind.GroundSecond,k.Kind);
            Assert.IsFalse(k.TryRequest(5,true,1));Assert.IsTrue(k.TryExecuteReserved(8,true,1));Assert.AreEqual(OutsideKickKind.GroundThird,k.Kind);
            Assert.IsFalse(k.TryRequest(9,true,1));Assert.IsFalse(k.TryExecuteReserved(12,true,1));Assert.IsTrue(k.TryExecuteReserved(14,true,1));Assert.AreEqual(OutsideKickKind.GroundFirst,k.Kind);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,b.Kind);
        }
        [TestCase(1,false)] [TestCase(2,true)] public void 六フレーム目の開始で失効しそのフレームの取消可能化でも復活しない(int press,bool starts)
        {
            var k=Kick(out var b,2,1,6,4);k.TryRequest(0,true,1);Assert.IsFalse(k.TryRequest(press,true,1));Assert.IsFalse(k.TryExecuteReserved(6,true,1));Assert.AreEqual(starts,k.TryExecuteReserved(7,true,1));Assert.AreEqual(starts?OutsideKickKind.GroundSecond:OutsideKickKind.GroundFirst,k.Kind);
        }
        [TestCase(17,OutsideKickKind.GroundSecond)] [TestCase(18,OutsideKickKind.GroundFirst)] public void 後隙終了後十八フレームの継続猶予は期限端で一段目へ戻る(int after,OutsideKickKind expected)
        {
            var k=Kick(out _);k.TryRequest(0,true,1);k.Observe(6,true,1);Assert.IsFalse(k.IsActive);Assert.AreEqual(24,k.ComboExpiresAt);Assert.IsTrue(k.TryRequest(6+after,true,1));Assert.AreEqual(expected,k.Kind);
        }
        [Test] public void 継続猶予は期限切れ予約を復活させず押し直しだけ予約期限を更新する()
        {
            var a=Kick(out _,2,1,5,null);a.TryRequest(0,true,1);a.TryRequest(1,true,1);Assert.IsFalse(a.TryExecuteReserved(8,true,1));Assert.AreEqual(2,a.NextGroundStage);Assert.IsTrue(a.TryRequest(9,true,1));Assert.AreEqual(OutsideKickKind.GroundSecond,a.Kind);
            var b=Kick(out _,2,1,5,null);b.TryRequest(0,true,1);b.TryRequest(1,true,1);b.TryRequest(5,true,1);Assert.IsTrue(b.TryExecuteReserved(8,true,1));Assert.AreEqual(OutsideKickKind.GroundSecond,b.Kind);
        }
        [Test] public void 時間観測や保持だけでは攻撃せず同一フレームの重複要求も二重実行しない()
        {
            var k=Kick(out var b);k.TryRequest(0,true,1);for(int i=0;i<20;i++){k.Observe(0,true,1);Assert.IsFalse(k.TryRequest(0,true,1));}Assert.AreEqual(0,k.StartedAt);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,b.Kind);
            k.Observe(100,true,1);Assert.IsFalse(k.IsActive);Assert.AreEqual(1,k.NextGroundStage);Assert.Throws<ArgumentOutOfRangeException>(()=>k.Observe(99,true,1));
        }
        [Test] public void 各段の有効期間だけ範囲内の各敵を一度ずつ受け付ける()
        {
            var k=Kick(out _);k.TryRequest(0,true,1);Assert.IsFalse(k.TryMarkHit(7));k.Observe(1,true,1);Assert.IsTrue(k.TryMarkHit(7));Assert.IsTrue(k.TryMarkHit(8));Assert.IsFalse(k.TryMarkHit(7));k.Observe(2,true,1);Assert.IsFalse(k.TryMarkHit(7));Assert.IsTrue(k.TryMarkHit(9));k.Observe(3,true,1);Assert.IsFalse(k.TryMarkHit(10));
            k.TryRequest(4,true,1);k.Observe(5,true,1);Assert.IsTrue(k.TryMarkHit(7));Assert.IsTrue(k.TryMarkHit(8));Assert.IsFalse(k.TryMarkHit(8));k.TryRequest(8,true,1);k.Observe(9,true,1);Assert.IsTrue(k.TryMarkHit(7));
        }
        [Test] public void 地上は反対方向でも移動と向きを固定し空中では横移動と通常落下を継続する()
        {
            var ground=Kick(out _);ground.TryRequest(0,true,1);Assert.AreEqual(0,ground.ResolveHorizontal(-1));Assert.AreEqual(1,ground.ResolveFacing(-1));Assert.AreEqual(1,ground.AttackFacing);
            var core=Core(OutsideTraversalProfile.ValidationDoubleJump);core.TickFrame(0,true,true);var air=Kick(out _);air.TryRequest(0,false,1,-1);long x=core.Position;long velocity=core.VerticalVelocity;core.TickFrame(air.ResolveHorizontal(-1),jumpHeld:true);Assert.Less(core.Position,x);Assert.Less(core.VerticalVelocity,velocity);Assert.AreEqual(-1,air.ResolveFacing(-1));Assert.AreEqual(1,air.AttackFacing);
            var plain=Core(OutsideTraversalProfile.ValidationDoubleJump);plain.TickFrame(0,true,true);plain.TickFrame(-1,jumpHeld:true);Assert.AreEqual(plain.Height,core.Height);Assert.AreEqual(plain.VerticalVelocity,core.VerticalVelocity);
        }
        [TestCase(-1,OutsideKickKind.AirDown)] [TestCase(0,OutsideKickKind.AirForward)] [TestCase(1,OutsideKickKind.AirUp)] public void 空中の三方向は別攻撃で各敵一回かつ着地で中断する(int vertical,OutsideKickKind kind)
        {
            var k=Kick(out _);k.TryRequest(0,false,-1,vertical);Assert.AreEqual(kind,k.Kind);Assert.IsFalse(k.LocksGroundMotion);k.Observe(1,false,-1);Assert.IsTrue(k.TryMarkHit(1));Assert.IsTrue(k.TryMarkHit(2));Assert.IsFalse(k.TryMarkHit(1));k.Observe(2,true,-1);Assert.IsFalse(k.IsActive);Assert.IsFalse(k.TryMarkHit(2));Assert.AreEqual(1,k.NextGroundStage);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void ジャンプと被弾とカード使用は地上コンボと固有攻撃予約をリセットする(int reason)
        {
            var k=Kick(out var b);k.TryRequest(0,true,1);k.TryRequest(1,true,1);if(reason==0)k.Observe(2,false,1);else if(reason==1)k.Observe(2,true,1,false);else k.CardStarted();Assert.IsFalse(k.IsActive);Assert.AreEqual(1,k.NextGroundStage);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,b.Kind);
        }
        [Test] public void 共通一枠はカード上書きを尊重し中断でカード予約まで勝手に破棄しない()
        {
            var k=Kick(out var b);k.TryRequest(0,true,1);k.TryRequest(1,true,1);b.Request(Olca.Core.Logic.Battle.AttackReservationKind.Card,2);Assert.IsFalse(k.TryExecuteReserved(4,true,1));Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.Card,b.Kind);k.CardStarted();Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.Card,b.Kind);
        }
        [TestCase(false,false)] [TestCase(true,true)] public void 同フレームの時刻前後不明はカード優先で後入力と確定できた場合だけ上書きする(bool knownLater,bool starts)
        {
            var k=Kick(out var b);b.Request(Olca.Core.Logic.Battle.AttackReservationKind.Card,0);Assert.AreEqual(starts,k.TryRequest(0,true,1,knownLaterThanCard:knownLater));Assert.AreEqual(starts?Olca.Core.Logic.Battle.AttackReservationKind.None:Olca.Core.Logic.Battle.AttackReservationKind.Card,b.Kind);
        }
        [Test] public void 前フレームのカード予約より新しい蹴りの即時開始は古い予約を残さない()
        {
            var k=Kick(out var b);b.Request(Olca.Core.Logic.Battle.AttackReservationKind.Card,0);Assert.IsTrue(k.TryRequest(1,true,1));Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,b.Kind);Assert.AreEqual(OutsideKickKind.GroundFirst,k.Kind);
        }
        [Test] public void 向きと離地着地で予約を取り消しダッシュ中の未定攻撃を仮実装しない()
        {
            var k=Kick(out var b);k.TryRequest(0,false,1);k.TryRequest(1,false,1);k.Observe(2,false,-1);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,b.Kind);Assert.AreEqual(1,k.AttackFacing);k.Interrupt();Assert.IsFalse(k.TryRequest(3,true,-1,dashing:true));Assert.IsFalse(k.IsActive);
        }
        [Test] public void 蹴り状態はViewleaseを越えて同じSessionに残り論理frameだけで進み終了時に解放する()
        {
            var s=new OutsideExplorationSession();try{
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var k=s.NormalKick;k.Configure(System.Linq.Enumerable.Repeat(new OutsideKickTiming(1,2,3),6).ToArray());var lease=s.AttachPresentation();Assert.IsTrue(k.TryRequest(s.Core.Frame,true,s.Core.Facing));s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsFalse(k.TryRequest(s.Core.Frame,true,s.Core.Facing));Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.Basic,s.PendingAttack);lease.Dispose();Assert.IsTrue(k.IsActive);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,s.PendingAttack);
                using(var next=s.AttachPresentation()){Assert.AreSame(k,s.NormalKick);Assert.AreEqual(0,k.StartedAt);for(int n=0;n<5;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.IsFalse(k.IsActive);Assert.AreEqual(2,k.NextGroundStage);}
                s.Dispose();Assert.IsFalse(k.HasProgress);Assert.Throws<ObjectDisposedException>(()=>{var unused=s.NormalKick;});
            }finally{s.Dispose();}
        }
        [TestCase(false)] [TestCase(true)] public void 回復成功は蹴りの実行中と継続猶予を消して旧形式で退避できる(bool grace)
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var k=s.NormalKick;k.Configure(System.Linq.Enumerable.Repeat(new OutsideKickTiming(1,2,3),6).ToArray());using(var lease=s.AttachPresentation()){k.TryRequest(s.Core.Frame,true,s.Core.Facing);if(grace)for(int n=0;n<6;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}}Assert.IsTrue(k.HasProgress);Assert.AreEqual(!grace,k.IsActive);s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.AreSame(k,s.NormalKick);Assert.IsTrue(k.IsConfigured);Assert.IsFalse(k.HasProgress);Assert.AreEqual(1,k.NextGroundStage);Assert.DoesNotThrow(()=>s.CaptureRuntime());
            }
        }
        [Test] public void 不明な回復先では既存の蹴り進行も先に消さない()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var k=s.NormalKick;k.Configure(System.Linq.Enumerable.Repeat(new OutsideKickTiming(1,2,3),6).ToArray());k.TryRequest(s.Core.Frame,true,s.Core.Facing);Assert.Throws<ArgumentException>(()=>s.RecoverOutside("unknown-kick-recovery"));Assert.IsTrue(k.IsActive);Assert.IsTrue(k.HasProgress);
            }
        }
        [Test] public void Session所有の蹴りは製品未登録のまま旧保存を変えず進行中の未接続保存は拒否する()
        {
            using(var s=new OutsideExplorationSession()){
                Assert.IsFalse(s.NormalKick.IsConfigured);var before=s.Core.CaptureState();string key=s.Core.StateDefinitionKey();Assert.IsFalse(s.NormalKick.TryRequest(s.Core.Frame,true,1));Assert.AreEqual(key,s.Core.StateDefinitionKey());
                s.NormalKick.Configure(System.Linq.Enumerable.Repeat(new OutsideKickTiming(1,2,3),6).ToArray());s.NormalKick.TryRequest(1,true,1);Assert.Throws<ArgumentException>(()=>s.CaptureRuntime());s.NormalKick.Reset();Assert.IsFalse(s.NormalKick.HasProgress);Assert.AreEqual(before.Position,s.Core.Position);Assert.AreEqual(before.Frame,s.Core.Frame);
            }
        }
        [Test] public void 二段ジャンプは空中の押し直し一回だけで保持や三回目を拒否し接地で回復する()
        {
            var c=Core(OutsideTraversalProfile.ValidationDoubleJump);c.TickFrame(0,true,true);for(int n=0;n<8;n++)c.TickFrame(0,jumpHeld:true);Assert.IsFalse(c.AirJumpUsed);
            c.TickFrame(0);long old=c.Height;c.TickFrame(0,true,true);Assert.IsTrue(c.DidAirJumpThisFrame);Assert.IsTrue(c.AirJumpUsed);Assert.AreEqual(old+12000,c.Height);
            c.TickFrame(0);long velocity=c.VerticalVelocity;c.TickFrame(0,true,true);Assert.IsFalse(c.DidAirJumpThisFrame);Assert.Less(c.VerticalVelocity,12000);
            for(int n=0;n<200&&!c.IsGrounded;n++)c.TickFrame(0,jumpHeld:true);Assert.IsTrue(c.IsGrounded);Assert.IsFalse(c.AirJumpUsed);c.TickFrame(0,jumpHeld:true);Assert.IsTrue(c.IsGrounded);c.TickFrame(0);c.TickFrame(0,true,true);Assert.IsFalse(c.IsGrounded);
        }
        [Test] public void 二段ジャンプ未解放と旧地形は同じ空中押下を拒否する()
        {
            var c=Core(OutsideTraversalProfile.ValidationPit);string key=c.StateDefinitionKey();c.TickFrame(0,true,true);c.TickFrame(0);c.TickFrame(0,true,true);Assert.IsFalse(c.AirJumpUsed);Assert.AreEqual(key,c.StateDefinitionKey());
            string json=System.Text.Encoding.UTF8.GetString(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(c.CaptureState()));StringAssert.DoesNotContain("AirJumpAt",json);StringAssert.Contains("Position",json);StringAssert.Contains("Afterimages",json);StringAssert.Contains("Moving",json);
        }
        [Test] public void 二段ジャンプの短押しは上昇を抑え空中でも左右と一押下を保つ()
        {
            var c=Core(OutsideTraversalProfile.ValidationDoubleJump);c.TickFrame(0,true,true);c.TickFrame(0);c.TickFrame(1,true,true);long x=c.Position;c.TickFrame(-1);Assert.Less(c.Position,x);Assert.LessOrEqual(c.VerticalVelocity,OutsideExplorationCore.ValidationReleasedRise);Assert.IsTrue(c.AirJumpUsed);
        }
        [Test] public void 二段ジャンプ消費後の壁蹴りでは回数を回復しない()
        {
            var c=Core(OutsideTraversalProfile.ValidationDoubleJump);ToWall(c);c.TickFrame(-1,true,true);c.TickFrame(0);c.TickFrame(0,true,true);Assert.IsTrue(c.AirJumpUsed);c.TickFrame(1);c.TickFrame(1,true,true);Assert.IsTrue(c.IsWallKicking);Assert.IsTrue(c.AirJumpUsed);c.TickFrame(-1);c.TickFrame(-1,true,true);Assert.IsFalse(c.DidAirJumpThisFrame);Assert.IsTrue(c.AirJumpUsed);
        }
        [Test] public void 壁蹴り優先は未使用の二段ジャンプを消費しない()
        {
            var c=Core(OutsideTraversalProfile.ValidationDoubleJump);ToWall(c);c.TickFrame(1,true,true);c.TickFrame(1);c.TickFrame(1,true,true);Assert.IsTrue(c.IsWallKicking);Assert.IsFalse(c.AirJumpUsed);c.TickFrame(-1);c.TickFrame(-1,true,true);Assert.IsTrue(c.DidAirJumpThisFrame);
        }
        [Test] public void 二段追加前の実保存Coreは全フィールドとbytesを変えず再出力する()
        {
            string json=@"{""Afterimages"":[],""ConsecutiveDashes"":0,""CooldownUntil"":-1,""DashEndsAt"":-1,""DashJumping"":false,""DashStartedAt"":-1,""DefinitionKey"":""F1A69D39ACDB21C7408FB7A62339D0EB338BA00E70D05D8D3E38632A1CDF20FF"",""Facing"":-1,""Frame"":2128,""GaugeMaximum"":100,""GaugeRecovery"":10,""GaugeUnits"":6000,""Height"":0,""IdleFrame"":0,""KickAway"":0,""LastDashAt"":-1,""MovementFrame"":0,""Moving"":false,""Position"":300000,""Remainder"":0,""SupportId"":""floor"",""Velocity"":0,""WallContact"":0,""WallKickAt"":-1,""WallSlideFrame"":0,""WallSliding"":false}";var bytes=System.Text.Encoding.UTF8.GetBytes(json);var dto=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<OutsideCoreStateData>(bytes);Assert.IsNull(dto.AirJumpAt);CollectionAssert.AreEqual(bytes,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(dto));var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,OutsideTraversalProfile.ValidationPit);Assert.AreEqual(dto.DefinitionKey,core.StateDefinitionKey());typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(core,new object[]{dto});Assert.IsFalse(core.AirJumpUsed);
        }
        [Test] public void 二段用の追加高度を含めた上限で地形を拒否する()
        {
            long top=long.MaxValue-135000-OutsideExplorationCore.ValidationMaximumRise;var p=new OutsideTraversalProfile("overflow",720000,12,30,60,600000,31200,135000,1800,12000,480000,6,12,true,new OutsideValidationWall("high",24*60000,0,top));Assert.Throws<ArgumentException>(()=>Core(p.WithDoubleJump("double-overflow")));
        }
        [Test] public void 水域の追加は旧地形キーを変えず不正範囲と設定を拒否する()
        {var old=Core(OutsideTraversalProfile.ValidationDoubleJump);string key=old.StateDefinitionKey();var wet=Core(OutsideTraversalProfile.ValidationWater);Assert.AreEqual(key,old.StateDefinitionKey());Assert.AreNotEqual(key,wet.StateDefinitionKey());Assert.Throws<ArgumentException>(()=>new OutsideWaterProfile(0,50,75,600));Assert.Throws<ArgumentException>(()=>new OutsideWaterArea("bad",0,1,5,5));Assert.Throws<ArgumentException>(()=>Core(OutsideTraversalProfile.ValidationDoubleJump.WithWater("bad",OutsideWaterProfile.Validation,new OutsideWaterArea("outside",0,long.MaxValue,0,1))));}
        private static OutsideExplorationCore Core(OutsideTraversalProfile p=null)=>new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain,p??OutsideTraversalProfile.ValidationDash);
        private static void ToWall(OutsideExplorationCore c)
        {long x=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position<x;i++)c.TickFrame(1);Assert.AreEqual(x,c.Position);}
        [Test] public void 外界ダッシュは地上の一押下だけで十二frame進み保持と途中押下を予約しない()
        {
            var c=Core();long start=c.Position;c.TickFrame(1,dashDown:true,dashHeld:true);
            Assert.IsTrue(c.IsDashing);Assert.AreEqual(0,c.DashFrame);
            for(int i=1;i<12;i++){c.TickFrame(1,dashHeld:true);Assert.IsTrue(c.IsDashing);Assert.AreEqual(i,c.DashFrame);}
            Assert.AreEqual(144000,c.Position-start);
            for(int i=0;i<30;i++){c.TickFrame(1,dashHeld:true);Assert.IsFalse(c.IsDashing);}
            Assert.AreEqual(144000+30*6000,c.Position-start);
        }
        [Test] public void 外界ダッシュの反転は残時間を更新せず離しても一回分を走り切る()
        {
            var c=Core();long start=c.Position;
            for(int i=0;i<4;i++)c.TickFrame(1,dashDown:i==0,dashHeld:i==0);
            for(int i=0;i<8;i++)c.TickFrame(-1);
            Assert.IsTrue(c.IsDashing);Assert.AreEqual(11,c.DashFrame);Assert.AreEqual(start-48000,c.Position);
            c.TickFrame(0);Assert.IsFalse(c.IsDashing);Assert.AreEqual(start-48000,c.Position);
        }
        [Test] public void 二連続後の待機は三十frameで不足中の保持を自動発動しない()
        {
            var c=Core();for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);Assert.AreEqual(30,c.DashCooldownRemaining);
            c.TickFrame(0,dashDown:true,dashHeld:true);Assert.IsFalse(c.IsDashing);Assert.AreEqual(29,c.DashCooldownRemaining);
            for(int i=0;i<29;i++)c.TickFrame(0,dashHeld:true);
            Assert.AreEqual(0,c.DashCooldownRemaining);Assert.IsFalse(c.IsDashing);
            c.TickFrame(0);c.TickFrame(0,dashDown:true,dashHeld:true);Assert.IsTrue(c.IsDashing);
        }
        [Test] public void 一秒ダッシュ未使用で連続回数を戻す()
        {
            var c=Core();for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            while(c.Frame<71)c.TickFrame(0);
            for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);Assert.AreEqual(0,c.DashCooldownRemaining);
        }
        [Test] public void 空中の通常ダッシュ押下と保持着地では開始せずゲージも消費しない()
        {
            var c=Core();Assert.IsTrue(c.CardGauge.TrySpend(80));c.TickFrame(1,true,true);
            c.TickFrame(1,false,true,true,true);Assert.IsFalse(c.IsDashing);
            for(int i=0;i<118;i++)c.TickFrame(1,false,true,false,true);
            Assert.IsTrue(c.IsGrounded);Assert.IsFalse(c.IsDashing);Assert.AreEqual(2400,c.CardGauge.CurrentUnits);
        }
        [Test] public void ダッシュジャンプは同じ縦軌道で遠くへ進み空中停止と反転を受ける()
        {
            var normal=Core();var dash=Core();long a=0,b=0;
            for(int i=0;i<80;i++){
                normal.TickFrame(1,i==0,true);dash.TickFrame(1,i==0,true,i==0,i==0);
                a=Math.Max(a,normal.Height);b=Math.Max(b,dash.Height);
                Assert.AreEqual(normal.Height,dash.Height);Assert.AreEqual(normal.VerticalVelocity,dash.VerticalVelocity);
            }
            Assert.AreEqual(186000,a);Assert.AreEqual(a,b);Assert.Greater(dash.Position,normal.Position);Assert.IsFalse(dash.IsDashJumping);
            dash=Core();dash.TickFrame(1,true,true,true,true);Assert.IsFalse(dash.IsDashing);Assert.IsTrue(dash.IsDashJumping);
            long x=dash.Position;dash.TickFrame(0,false,true);Assert.AreEqual(x,dash.Position);dash.TickFrame(-1,false,true);Assert.Less(dash.Position,x);
        }
        [Test] public void 検証壁は未解放なら接触だけで壁能力を使えない()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(false));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding);Assert.IsFalse(c.IsWallKicking);
            c.TickFrame(1);long v=c.VerticalVelocity;c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallKicking);Assert.AreEqual(v-400,c.VerticalVelocity);
        }
        [Test] public void 壁方向保持だけで滑り速度を制限し離すと通常重力へ戻る()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding,"上昇中は壁ずりしない");
            while(c.VerticalVelocity>=0){c.TickFrame(1,false,true);Assert.IsFalse(c.IsWallSliding);}
            c.TickFrame(1,false,true);Assert.IsTrue(c.IsWallSliding);Assert.AreEqual(0,c.WallSlideFrame);
            for(int i=0;i<60&&c.VerticalVelocity>-1800;i++)c.TickFrame(1,false,true);
            Assert.AreEqual(-1800,c.VerticalVelocity);
            long y=c.Height;c.TickFrame(1,false,true);Assert.AreEqual(y-1800,c.Height);Assert.AreEqual(-1800,c.VerticalVelocity);
            c.TickFrame(0,false,true);Assert.IsFalse(c.IsWallSliding);Assert.AreEqual(-2200,c.VerticalVelocity);
        }
        [TestCase(-1)][TestCase(1)] public void 壁ずりは左右とも頂点の静止stepまで開始せず下降stepから始まり接地で消える(int side)
        {
            var p=OutsideTraversalProfile.ValidationWall(true);long contact=p.Walls[0].X-side*p.BodyHalfWidth;
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("wall-boundary",32*60000,contact,6*60000),p);c.TickFrame(side,true,true);
            bool apex=false,fall=false;
            for(int i=0;i<150&&!c.IsGrounded;i++){
                long beforeV=c.VerticalVelocity,y=c.Height;c.TickFrame(side,false,true);
                if(beforeV>=0){Assert.IsFalse(c.IsWallSliding);Assert.GreaterOrEqual(c.Height,y);if(beforeV==0){apex=true;Assert.AreEqual(y,c.Height);}}
                else if(!c.IsGrounded){fall=true;Assert.IsTrue(c.IsWallSliding);Assert.Less(c.Height,y);}
            }
            Assert.IsTrue(apex);Assert.IsTrue(fall);Assert.IsTrue(c.IsGrounded);Assert.IsFalse(c.IsWallSliding);Assert.AreEqual(0,c.WallSlideFrame);
        }
        [Test] public void 壁蹴りは離す押し直しで同壁へ反復でき押出し中だけ反対方向へ進む()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);c.TickFrame(1,true,true);c.TickFrame(1);
            long x=c.Position,y=c.Height;c.TickFrame(1,true,true);Assert.IsTrue(c.IsWallKicking);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(-1,c.Facing);Assert.AreEqual(x-8000,c.Position);Assert.AreEqual(y+12000,c.Height);
            for(int i=1;i<6;i++)c.TickFrame(1,false,true);
            Assert.AreEqual(x-48000,c.Position);Assert.AreEqual(-1,c.Facing);
            for(int i=0;i<20&&c.Position<x;i++)c.TickFrame(1);
            Assert.AreEqual(x,c.Position);Assert.IsFalse(c.IsGrounded);long secondY=c.Height;c.TickFrame(1,true,true);
            Assert.IsTrue(c.IsWallKicking);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(secondY+12000,c.Height);Assert.Greater(secondY,y);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 壁ごとの滑りと蹴りの可否を独立に使う(bool slide,bool kick)
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true,slide,kick));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding);while(c.VerticalVelocity>=0)c.TickFrame(1,false,true);c.TickFrame(1,false,true);Assert.AreEqual(slide,c.IsWallSliding);
            c.TickFrame(1);c.TickFrame(1,true,true);Assert.AreEqual(kick,c.IsWallKicking);
        }
        [Test] public void 高速で検証壁を通り抜けず実移動停止からIdleを開始し残ダッシュ中も継続する()
        {
            var p=OutsideTraversalProfile.ValidationWall(false);long wall=p.Walls[0].X-p.BodyHalfWidth;
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("wall-near",32*60000,wall-1000,6*60000),p);
            c.TickFrame(1,dashDown:true,dashHeld:true);Assert.AreEqual(wall,c.Position);Assert.IsTrue(c.IsDashing);Assert.AreEqual(0,c.IdleFrame);
            for(int i=1;i<12;i++)c.TickFrame(1,dashHeld:true);
            c.TickFrame(0);Assert.IsFalse(c.IsDashing);Assert.AreEqual(11,c.IdleFrame);c.TickFrame(0);Assert.AreEqual(12,c.IdleFrame);
        }
        [Test] public void 接地壁でShiftダッシュしても待機時計だけは連続しダッシュ時間と再使用待ちは変わらない()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);for(int i=0;i<8;i++)c.TickFrame(1);long idle=c.IdleFrame,x=c.Position;
            for(int i=0;i<12;i++){c.TickFrame(1,dashDown:i==0,dashHeld:true);Assert.IsTrue(c.IsDashing);Assert.AreEqual(i,c.DashFrame);Assert.AreEqual(x,c.Position);Assert.IsFalse(c.IsMoving);Assert.AreEqual(idle+i+1,c.IdleFrame);}
            c.TickFrame(1);Assert.IsFalse(c.IsDashing);Assert.AreEqual(0,c.DashCooldownRemaining);
            for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);c.TickFrame(1);Assert.AreEqual(30,c.DashCooldownRemaining);
        }
        [Test] public void 残像は四つまでの論理状態で表示借用と停止中に変わらず十二frameで消える()
        {
            using(var s=new OutsideExplorationSession()){
                var c=s.Core;for(int i=0;i<10;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
                Assert.AreEqual(4,c.Afterimages.Count);var last=c.Afterimages[3];long frame=c.Frame;
                var first=s.AttachPresentation();first.Dispose();var second=s.AttachPresentation();Assert.AreSame(c,second.Core);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(last.Frame,c.Afterimages[3].Frame);Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true)));second.Dispose();
                for(int i=0;i<20;i++)c.TickFrame(0);Assert.AreEqual(0,c.Afterimages.Count);
            }
        }
        [TestCase(1)][TestCase(7)][TestCase(120)] public void ダッシュジャンプの結果は描画分割に依存しない(int parts)
        {
            var expected=Core();var actual=Core();var clock=new LogicalFrameClock();
            void Step(OutsideExplorationCore c,long f)=>c.TickFrame(f<60?1:-1,f==17,f>=17&&f<60,f==1||f==15,f<=12||f>=15&&f<=28);
            for(int f=1;f<=120;f++)Step(expected,f);
            long consumed=0;for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((f,ms)=>Step(actual,f));}
            Assert.AreEqual(120,actual.Frame);Assert.AreEqual(expected.Position,actual.Position);Assert.AreEqual(expected.Height,actual.Height);Assert.AreEqual(expected.VerticalVelocity,actual.VerticalVelocity);Assert.AreEqual(expected.DashCooldownRemaining,actual.DashCooldownRemaining);Assert.AreEqual(expected.IsDashJumping,actual.IsDashJumping);Assert.AreEqual(expected.CardGauge.CurrentUnits,actual.CardGauge.CurrentUnits);
        }
        [Test] public void 縦追従は必要な高さだけ表示を送り低い跳躍と固定表示を変えない()
        {
            var follow=OutsideCameraPolicy.Follow;
            Assert.AreEqual(0,follow.ResolveVerticalOffset(3*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(0,follow.ResolveVerticalOffset(4*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(2,follow.ResolveVerticalOffset(6*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(0,OutsideCameraPolicy.FixedAt(12*60000).ResolveVerticalOffset(6*60000,10,2,1.5f,2.5f));
        }
        [Test] public void 縦追従の不正高さと非有限表示寸法は拒否する()
        {
            var follow=OutsideCameraPolicy.Follow;
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(-1,10,2,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,float.NaN,2,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,float.PositiveInfinity,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,2,-1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,2,1,0));
        }
        [Test] public void 試走設定は初回表示前だけで解放や地形を保存へ書かない()
        {
            using(var s=new OutsideExplorationSession()){
                Assert.IsFalse(s.Core.TraversalProfile.WallActionsUnlocked);Assert.AreEqual(0,s.Core.TraversalProfile.Walls.Count);Assert.Throws<ArgumentNullException>(()=>s.TryConfigureTraversal(null));
                Assert.IsTrue(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true)));var lease=s.AttachPresentation();Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDash));lease.Dispose();
                s.Core.TickFrame(0);Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDash));Assert.IsTrue(s.Core.TraversalProfile.WallActionsUnlocked);
            }
        }
        [Test] public void 外界個体は一度だけ生成しView再生成でもHPと状態を保持する()
        {
            using(var s=new OutsideExplorationSession()){
                int made=0;Assert.IsTrue(s.TryInitializePlayer(()=>{made++;return new Olca.Core.Domain.PlayerCharacter(100);}));var actor=s.Player;actor.SetHp(46);actor.ApplyParalysis(3);
                using(var first=s.AttachPresentation()){Assert.AreSame(actor,first.Player);s.Core.TickFrame(0);Assert.IsTrue(s.AdvancePlayerFrame());Assert.IsFalse(s.CanPerformAttack);Assert.AreEqual(2,actor.ParalysisRemainingFrames);Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(2,actor.ParalysisRemainingFrames);}
                Assert.IsFalse(s.TryInitializePlayer(()=>{made++;return new Olca.Core.Domain.PlayerCharacter(200);}));using(var second=s.AttachPresentation()){Assert.AreSame(actor,second.Player);Assert.AreEqual(46,second.Player.CurrentHp);Assert.AreEqual(2,actor.ParalysisRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.AreEqual(1,actor.ParalysisRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsFalse(s.CanPerformAttack,"麻痺最終frameは移動と同様に禁止");s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.CanPerformAttack);}
                Assert.AreEqual(1,made);Assert.AreEqual(46,actor.CurrentHp);
            }
        }
        [Test] public void 外界停止は状態時間を進めず死亡しても表示再入で全回復しない()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));using(var lease=s.AttachPresentation()){
                    s.Player.ApplyParalysis(4);for(int i=0;i<20;i++)Assert.IsFalse(s.AdvancePlayerFrame());s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.AreEqual(3,s.Player.ParalysisRemainingFrames);s.Player.SetHp(0);Assert.IsFalse(s.CanPerformAttack);
                }
                using(var lease=s.AttachPresentation()){Assert.AreEqual(0,lease.Player.CurrentHp);Assert.IsFalse(s.CanPerformAttack);}
            }
        }
        [Test] public void 外界終了は旧leaseから個体を切り離し新規世代へ状態を渡さない()
        {
            var s=new OutsideExplorationSession();Assert.Throws<ArgumentNullException>(()=>s.TryInitializePlayer(null));Assert.Throws<ArgumentException>(()=>s.TryInitializePlayer(()=>null));s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var old=s.AttachPresentation();s.Player.SetHp(31);s.Dispose();Assert.IsNull(old.Player);Assert.IsNull(s.Player);Assert.IsFalse(s.CanPerformAttack);Assert.IsFalse(s.AdvancePlayerFrame());Assert.Throws<ObjectDisposedException>(()=>s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100)));
            using(var next=new OutsideExplorationSession()){next.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Assert.AreEqual(100,next.Player.CurrentHp);}
        }
        [Test] public void 外界の期間防御は同じ論理時計で二frame後に終了し停止時は保持する()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));using(var lease=s.AttachPresentation()){
                    s.Player.SetAura(10,2);Assert.IsTrue(s.Player.HasAura);for(int i=0;i<5;i++)Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(2,s.Player.ProtectionRemainingFrames);
                    s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.Player.HasAura);Assert.AreEqual(1,s.Player.ProtectionRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsFalse(s.Player.HasAura);
                }
            }
        }
        [Test] public void 回復復帰はHPと状態とゲージを戻し時間と個体と永続所持を再初期化しない()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var actor=s.Player;using(var lease=s.AttachPresentation()){
                    for(int i=0;i<20;i++)s.Core.TickFrame(1,jumpDown:i==0,jumpHeld:true);long frame=s.Core.Frame;actor.SetHp(0);s.Core.CardGauge.TrySpend(30);actor.ApplyParalysis(60);
                    s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.AreSame(actor,s.Player);Assert.AreEqual(100,actor.CurrentHp);Assert.IsFalse(actor.IsParalyzed);Assert.AreEqual(frame,s.Core.Frame);Assert.AreEqual(OutsideTrialRoute.RecoveryX,s.Core.Position);Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(0,s.Core.Height);Assert.AreEqual(0,s.Core.VerticalVelocity);Assert.IsFalse(s.Core.IsDashing);Assert.IsFalse(s.Core.IsWallKicking);Assert.AreEqual(s.Core.CardGauge.MaximumUnits,s.Core.CardGauge.CurrentUnits);Assert.IsFalse(s.AdvancePlayerFrame());
                    actor.SetHp(20);long x=s.Core.Position;s.RecoverOutside(OutsideTrialRoute.RecoveryPointId,false);Assert.AreEqual(x,s.Core.Position);Assert.AreEqual(100,actor.CurrentHp);
                }
            }
        }
        [Test] public void 未登録の復帰先は元のHPと位置を変更せず旧nullだけ既存開始地点へ戻す()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));s.Player.SetHp(33);s.Core.TickFrame(1);long x=s.Core.Position;Assert.Throws<ArgumentException>(()=>s.RecoverOutside("unknown-preserve"));Assert.AreEqual(x,s.Core.Position);Assert.AreEqual(33,s.Player.CurrentHp);s.RecoverOutside(null);Assert.AreEqual(s.Core.Definition.Start,s.Core.Position);Assert.AreEqual(100,s.Player.CurrentHp);
            }
        }
    }
}
