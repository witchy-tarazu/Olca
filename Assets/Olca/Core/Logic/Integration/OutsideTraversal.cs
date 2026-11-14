using System;
using System.Collections.Generic;
using System.Linq;

namespace Olca.Core.Logic.Integration
{
    /// <summary>検証用の垂直面。製品のsolid/身体幅/解放アイテム定義ではない。</summary>
    public sealed class OutsideValidationWall
    {
        public string Id {get;} public long X {get;} public long Bottom {get;} public long Top {get;}
        public bool AllowsSlide {get;} public bool AllowsKick {get;}
        public OutsideValidationWall(string id,long x,long bottom,long top,bool allowsSlide=true,bool allowsKick=true)
        {if(string.IsNullOrWhiteSpace(id)||x<=0||bottom<0||top<=bottom)throw new ArgumentException("検証壁の定義が不正です");Id=id;X=x;Bottom=bottom;Top=top;AllowsSlide=allowsSlide;AllowsKick=allowsKick;}
    }
    /// <summary>床だけを抜く試用区間。側面/身体幅や製品Mapの衝突規則は追加しない。</summary>
    public sealed class OutsideFloorGap
    {
        public string Id {get;}public long Left {get;}public long Right {get;}
        public OutsideFloorGap(string id,long left,long right){if(string.IsNullOrWhiteSpace(id)||left<0||right<=left)throw new ArgumentException("床抜け範囲が不正です");Id=id;Left=left;Right=right;}
        public bool Contains(long x)=>x>Left&&x<Right;
    }
    /// <summary>試用水域は足元の閉矩形。製品の潜水判定点/Map定義ではない。</summary>
    public sealed class OutsideWaterArea
    {
        public string Id {get;}public long Left {get;}public long Right {get;}public long Bottom {get;}public long Top {get;}
        public OutsideWaterArea(string id,long left,long right,long bottom,long top){if(string.IsNullOrWhiteSpace(id)||left<0||right<=left||top<=bottom)throw new ArgumentException("試用水域が不正です");Id=id;Left=left;Right=right;Bottom=bottom;Top=top;}
        public bool Contains(long x,long y)=>x>=Left&&x<=Right&&y>=Bottom&&y<=Top;
        internal bool LastContact(long x,long y,long nextX,long nextY,out long height,out decimal at)
        {
            decimal from=0,to=1;bool Clip(decimal start,decimal delta,decimal low,decimal high){if(delta==0)return start>=low&&start<=high;decimal a=(low-start)/delta,b=(high-start)/delta;if(a>b){decimal c=a;a=b;b=c;}from=Math.Max(from,a);to=Math.Min(to,b);return from<=to;}
            height=0;at=0;if(!Clip(x,(decimal)nextX-x,Left,Right)||!Clip(y,(decimal)nextY-y,Bottom,Top))return false;height=(long)decimal.Floor(y+((decimal)nextY-y)*to);at=to;return true;
        }
    }
    public sealed class OutsideWaterProfile
    {
        public int SpeedPercent {get;}public int GravityPercent {get;}public int JumpPercent {get;}public int BreathFrames {get;}
        public OutsideWaterProfile(int speedPercent,int gravityPercent,int jumpPercent,int breathFrames){if(speedPercent<1||speedPercent>100||gravityPercent<1||gravityPercent>100||jumpPercent<1||jumpPercent>100||breathFrames<1)throw new ArgumentException("水中試用値が不正です");SpeedPercent=speedPercent;GravityPercent=gravityPercent;JumpPercent=jumpPercent;BreathFrames=breathFrames;}
        public static OutsideWaterProfile Validation=>new OutsideWaterProfile(50,50,75,600);
    }
    /// <summary>製品未確定値を分けた試走設定。Rootの検証地形だけへ明示注入する。</summary>
    public sealed class OutsideTraversalProfile
    {
        public string Id {get;} public long DashSpeedPerSecond {get;} public int DashFrames {get;}
        public int DashCooldownFrames {get;} public int DashResetFrames {get;} public long DashJumpSpeedPerSecond {get;}
        public long BodyHalfWidth {get;} public long BodyHeight {get;} public long WallSlidePerFrame {get;}
        public long WallKickVelocity {get;} public long WallKickSpeedPerSecond {get;} public int WallKickPushFrames {get;}
        public int WallKickMotionFrames {get;} public bool WallActionsUnlocked {get;}
        public IReadOnlyList<OutsideValidationWall> Walls {get;}
        public IReadOnlyList<OutsideFloorGap> FloorGaps {get;private set;}=Array.Empty<OutsideFloorGap>();
        public long VoidDepth {get;private set;}
        public bool DoubleJumpUnlocked {get;private set;}
        public OutsideWaterProfile Water {get;private set;}
        public IReadOnlyList<OutsideWaterArea> Waters {get;private set;}=Array.Empty<OutsideWaterArea>();
        public OutsideTraversalProfile WithWater(string id,OutsideWaterProfile water,params OutsideWaterArea[] areas)
        {
            var copy=(areas??Array.Empty<OutsideWaterArea>()).ToArray();if(water==null||copy.Length==0||copy.Any(a=>a==null)||copy.Select(a=>a.Id).Distinct().Count()!=copy.Length)throw new ArgumentException("水域/設定が欠落しています");
            var next=new OutsideTraversalProfile(id,DashSpeedPerSecond,DashFrames,DashCooldownFrames,DashResetFrames,DashJumpSpeedPerSecond,BodyHalfWidth,BodyHeight,WallSlidePerFrame,WallKickVelocity,WallKickSpeedPerSecond,WallKickPushFrames,WallKickMotionFrames,WallActionsUnlocked,Walls.ToArray());
            next.DoubleJumpUnlocked=DoubleJumpUnlocked;next.VoidDepth=VoidDepth;next.FloorGaps=FloorGaps;next.Water=water;next.Waters=Array.AsReadOnly(copy.OrderBy(a=>a.Id,StringComparer.Ordinal).ToArray());return next;
        }
        // 製品の装備/解放条件は別責務。試用プロファイルでのみ明示的に有効化する。
        public OutsideTraversalProfile WithDoubleJump(string id)
        {
            var next=new OutsideTraversalProfile(id,DashSpeedPerSecond,DashFrames,DashCooldownFrames,DashResetFrames,DashJumpSpeedPerSecond,BodyHalfWidth,BodyHeight,WallSlidePerFrame,WallKickVelocity,WallKickSpeedPerSecond,WallKickPushFrames,WallKickMotionFrames,WallActionsUnlocked,Walls.ToArray());
            next.Water=Water;next.Waters=Waters;next.DoubleJumpUnlocked=true;next.VoidDepth=VoidDepth;next.FloorGaps=FloorGaps;return next;
        }
        public OutsideTraversalProfile WithFloorGaps(string id,long voidDepth,params OutsideFloorGap[] gaps)
        {
            var copy=(gaps??Array.Empty<OutsideFloorGap>()).OrderBy(g=>g?.Left??-1).ToArray();
            if(voidDepth<=0||copy.Length==0||copy.Any(g=>g==null)||copy.Select(g=>g.Id).Distinct().Count()!=copy.Length||copy.Skip(1).Where((g,i)=>g.Left<copy[i].Right).Any())throw new ArgumentException("床抜けと下端の試用定義が不正です");
            var next=new OutsideTraversalProfile(id,DashSpeedPerSecond,DashFrames,DashCooldownFrames,DashResetFrames,DashJumpSpeedPerSecond,BodyHalfWidth,BodyHeight,WallSlidePerFrame,WallKickVelocity,WallKickSpeedPerSecond,WallKickPushFrames,WallKickMotionFrames,WallActionsUnlocked,Walls.ToArray());
            next.Water=Water;next.Waters=Waters;next.DoubleJumpUnlocked=DoubleJumpUnlocked;next.VoidDepth=voidDepth;next.FloorGaps=Array.AsReadOnly(copy);return next;
        }
        public OutsideTraversalProfile(string id,long dashSpeed,int dashFrames,int cooldownFrames,int resetFrames,long dashJumpSpeed,
            long bodyHalfWidth,long bodyHeight,long wallSlidePerFrame,long wallKickVelocity,long wallKickSpeed,int wallKickPushFrames,int wallKickMotionFrames,
            bool wallActionsUnlocked=false,params OutsideValidationWall[] walls)
        {
            if(string.IsNullOrWhiteSpace(id)||dashSpeed<=0||dashFrames<1||cooldownFrames<1||resetFrames<1||dashJumpSpeed<=0||bodyHalfWidth<=0||bodyHeight<=0||wallSlidePerFrame<=0||wallKickVelocity<=0||wallKickSpeed<=0||wallKickPushFrames<1||wallKickMotionFrames<wallKickPushFrames)throw new ArgumentException("試走設定が不正です");
            var copy=(walls??Array.Empty<OutsideValidationWall>()).ToArray();if(copy.Any(w=>w==null)||copy.Select(w=>w.Id).Distinct(StringComparer.Ordinal).Count()!=copy.Length)throw new ArgumentException("検証壁の識別子が不正です");
            Id=id;DashSpeedPerSecond=dashSpeed;DashFrames=dashFrames;DashCooldownFrames=cooldownFrames;DashResetFrames=resetFrames;DashJumpSpeedPerSecond=dashJumpSpeed;
            BodyHalfWidth=bodyHalfWidth;BodyHeight=bodyHeight;WallSlidePerFrame=wallSlidePerFrame;WallKickVelocity=wallKickVelocity;WallKickSpeedPerSecond=wallKickSpeed;WallKickPushFrames=wallKickPushFrames;WallKickMotionFrames=wallKickMotionFrames;WallActionsUnlocked=wallActionsUnlocked;
            Walls=Array.AsReadOnly(copy.OrderBy(w=>w.X).ThenBy(w=>w.Id,StringComparer.Ordinal).ToArray());
        }
        internal void ValidateFor(OutsideMovementDefinition definition)
        {
            if(DashSpeedPerSecond<=definition.SpeedPerSecond||DashJumpSpeedPerSecond<=definition.SpeedPerSecond||BodyHalfWidth>definition.Width/2)throw new ArgumentException("地形と試走速度/身体幅の組合せが不正です");
            long kickRise;
            try{kickRise=OutsideExplorationCore.RiseBound(WallKickVelocity,WaterGravity);}
            catch(OverflowException){throw new ArgumentException("壁蹴り速度が検証座標の表現範囲外です");}
            long reserve;try{reserve=checked(Math.Max(kickRise,OutsideExplorationCore.RiseBound(OutsideExplorationCore.ValidationJumpVelocity,WaterGravity))+(DoubleJumpUnlocked?OutsideExplorationCore.RiseBound(OutsideExplorationCore.ValidationJumpVelocity,WaterGravity):0));}catch(OverflowException){throw new ArgumentException("二段ジャンプの高度が表現範囲外です");}
            if(VoidDepth>long.MaxValue-reserve||FloorGaps.Any(g=>g.Right>definition.Width||g.Contains(definition.Start)))throw new ArgumentException("床抜けの範囲/開始位置/下端が不正です");
            if(Waters.Any(a=>a.Right>definition.Width||a.Bottom< -VoidDepth||a.Top>long.MaxValue-reserve))throw new ArgumentException("水域が地形の範囲外です");
            if(BodyHeight>long.MaxValue-reserve)throw new ArgumentException("検証身体高が表現範囲外です");
            foreach(var wall in Walls)if(wall.X<BodyHalfWidth||wall.X>definition.Width-BodyHalfWidth||wall.Top>long.MaxValue-BodyHeight-reserve||wall.Bottom<BodyHeight&&Math.Abs(definition.Start-wall.X)<BodyHalfWidth)throw new ArgumentException("検証壁の範囲または開始位置が不正です");
        }
        // 全て調整用。12単位/秒、0.2秒、2回後0.5秒待ち、1秒未使用で回数リセット。
        // ダッシュジャンプは試走では着地まで空中速度10。正式な慣性/速度表へ転用しない。
        // 壁身体幅は既存Idleの104px/PPU100に対応、滑り1.8単位/秒、蹴り初速12/横8。
        internal long WaterGravity=>Water==null?OutsideExplorationCore.ValidationGravity:Math.Max(1,OutsideExplorationCore.Scale(OutsideExplorationCore.ValidationGravity,Water.GravityPercent));
        public static OutsideTraversalProfile ValidationWater=>ValidationDoubleJump.WithWater("validation-water-v1",OutsideWaterProfile.Validation,new OutsideWaterArea("trial-water-left",30000,150000,0,180000));
        public static OutsideTraversalProfile ValidationDoubleJump=>ValidationPit.WithDoubleJump("validation-double-jump-v1");
        public static OutsideTraversalProfile ValidationPit=>ValidationWall(true).WithFloorGaps("validation-pit-v1",60000,new OutsideFloorGap("trial-void",225000,255000));
        public static OutsideTraversalProfile ValidationDash=>new OutsideTraversalProfile("validation-traversal-v1",720000,12,30,60,600000,31200,135000,1800,12000,480000,6,12);
        public static OutsideTraversalProfile ValidationWall(bool unlocked,bool slide=true,bool kick=true)=>new OutsideTraversalProfile("validation-wall-actions-v1",720000,12,30,60,600000,31200,135000,1800,12000,480000,6,12,unlocked,new OutsideValidationWall("validation-wall",24*60000,0,1000*60000,slide,kick));
    }
    /// <summary>導線確認専用の仮配置。製品Map/能力解放/永続座標とは区別する。</summary>
    public static class OutsideTrialRoute
    {
        // 通常導線の仮拠点と既存回復地点の横範囲。新規利用はCanEditDeckの接地が必要で、利用後の地点内ジャンプはSessionで保持する。
        public static bool IsDeckEditingZone(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&(Math.Abs(core.Position-core.Definition.Start)<=OutsideMovementDefinition.UnitsPerWorldUnit||Math.Abs(core.Position-RecoveryX)<=OutsideMovementDefinition.UnitsPerWorldUnit||IsReloadZone(core));
        public static bool CanEditDeck(OutsideExplorationCore core)=>IsDeckEditingZone(core)&&core.IsGrounded&&core.Height==0;
        public static OutsideTraversalProfile ResolveTraversal(OutsideRuntimeSaveData runtime)
        {
            if(runtime==null)return OutsideTraversalProfile.ValidationWater;
            foreach(var profile in new[]{OutsideTraversalProfile.ValidationWater,OutsideTraversalProfile.ValidationDoubleJump,OutsideTraversalProfile.ValidationPit,OutsideTraversalProfile.ValidationWall(true),OutsideTraversalProfile.ValidationWall(false),OutsideTraversalProfile.ValidationDash}){
                var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,profile);if(core.StateDefinitionKey()==runtime.Core.DefinitionKey)return profile;
            }
            throw new ArgumentException("外界試走の地形定義が未登録です");
        }
        // 回復/入口/壁と接触範囲を重ねない、追加の試用リロード専用地点。
        public const long ReloadX=22*OutsideMovementDefinition.UnitsPerWorldUnit,ReloadRadius=OutsideMovementDefinition.UnitsPerWorldUnit/2;
        public static bool IsReloadZone(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&Math.Abs(core.Position-ReloadX)<=ReloadRadius;
        public const string RecoveryPointId="validation-recovery-left-v1";
        public const long RecoveryX=6*OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanRecover(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-RecoveryX)<=OutsideMovementDefinition.UnitsPerWorldUnit;
        // A/Bは既存地形を使った往復確認用。正式マップや解放条件ではない。
        public const string AnnexMapId="trial-annex-v1",DoorHomePoint="trial-door-home-v1",DoorAnnexPoint="trial-door-annex-v1";
        public const long HomeDoorX=1050000,AnnexDoorX=360000,DoorRadius=30000;
        public static bool KnownMap(string id)=>id==null||id==AnnexMapId;
        public static bool KnownRecovery(string point)=>point==null||point==RecoveryPointId||point==DoorHomePoint||point==DoorAnnexPoint;
        public static string MapForRecovery(string point){if(!KnownRecovery(point))throw new ArgumentException("復帰先が未登録です");return point==DoorAnnexPoint?AnnexMapId:null;}
        public static void ValidateMapRecovery(string map,string point){if(!KnownMap(map)||MapForRecovery(point)!=map)throw new ArgumentException("外界マップと復帰地点が一致しません");}
        public static long DoorPosition(OutsideExplorationCore core)=>core.TrialMapId==AnnexMapId?AnnexDoorX:HomeDoorX;
        public static bool CanUseDoor(OutsideExplorationCore core)=>core!=null&&KnownMap(core.TrialMapId)&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&!core.IsDashing&&Math.Abs(core.Position-DoorPosition(core))<=DoorRadius;
        public static long ResolveRecovery(OutsideExplorationCore core,string id)
        {if(core==null||core.Definition.Id!="validation-landing-32"||!KnownRecovery(id))throw new ArgumentException("復帰先の外界マップ/地点が未登録です");return id==null?core.Definition.Start:id==DoorHomePoint?HomeDoorX:RecoveryX;}
        public const long PoisonTrialX=2*OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanTryPoison(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-PoisonTrialX)<=OutsideMovementDefinition.UnitsPerWorldUnit;
        // 既存の地点/接触半径と重ならない試用配置。正式な入手源ではない。
        public const string CardSupplyId="validation-exploration-card-chest-v1";
        public const long CardSupplyX=10*OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanClaimCardSupply(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-CardSupplyX)<=OutsideMovementDefinition.UnitsPerWorldUnit/2;
        public const long ShopX=8*OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanVisitShop(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-ShopX)<=OutsideMovementDefinition.UnitsPerWorldUnit/2;
        public const long SupplyX=12*OutsideMovementDefinition.UnitsPerWorldUnit;
        public const long PoisonSupplyX=16*OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanClaimPoisonSupply(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-PoisonSupplyX)<=OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanClaimSupply(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.Definition.Id=="validation-landing-32"&&core.IsGrounded&&core.Height==0&&Math.Abs(core.Position-SupplyX)<=OutsideMovementDefinition.UnitsPerWorldUnit;
        public const long EntranceX=20*OutsideMovementDefinition.UnitsPerWorldUnit;
        public const long EntranceRadius=OutsideMovementDefinition.UnitsPerWorldUnit;
        public static bool CanInspect(OutsideExplorationCore core)=>core!=null&&core.TrialMapId==null&&core.IsGrounded&&Math.Abs(core.Position-EntranceX)<=EntranceRadius;
    }
    public enum OutsideAfterimagePose {Dash,JumpRise,JumpApex,JumpFall}
    public readonly struct OutsideAfterimage
    {
        public readonly long X,Y,Frame;public readonly int Facing;public readonly OutsideAfterimagePose Pose;
        public OutsideAfterimage(long x,long y,long frame,int facing,OutsideAfterimagePose pose){X=x;Y=y;Frame=frame;Facing=facing;Pose=pose;}
    }
    public sealed partial class OutsideExplorationCore
    {
        internal void RestoreRecoveryPosition(long position)
        {
            if(position<0||position>Definition.Width)throw new ArgumentOutOfRangeException(nameof(position));
            Position=position;Height=0;VerticalVelocity=0;SupportId="floor";IsMoving=false;MovementFrame=IdleFrame=0;remainder=0;jumpWasHeld=dashWasHeld=false;airJumpAt=null;LastWaterContactHeight=null;
            dashStartedAt=dashEndsAt=cooldownUntil=lastDashAt=wallKickAt=-1;consecutiveDashes=kickAway=0;IsDashJumping=IsWallSliding=false;WallSlideFrame=0;WallContactDirection=0;afterimages.Clear();
        }
        public OutsideTraversalProfile TraversalProfile {get;private set;}
        private bool IsIdleMotion=>IsGrounded&&!IsMoving&&!IsWallKicking;
        private long dashStartedAt=-1,dashEndsAt=-1,cooldownUntil=-1,lastDashAt=-1,wallKickAt=-1;
        private int consecutiveDashes,kickAway;private bool dashWasHeld;
        public bool IsDashing=>dashEndsAt>Frame;
        public int DashFrame=>IsDashing?(int)(Frame-dashStartedAt):0;
        public int DashCooldownRemaining=>(int)Math.Max(0,cooldownUntil-Frame);
        public bool IsDashJumping {get;private set;}
        public bool IsWallSliding {get;private set;}
        public long WallSlideFrame {get;private set;}
        public int WallContactDirection {get;private set;}
        public bool IsWallKicking=>TraversalProfile!=null&&wallKickAt>=0&&Frame-wallKickAt<TraversalProfile.WallKickMotionFrames;
        public int WallKickFrame=>IsWallKicking?(int)(Frame-wallKickAt):0;
        private readonly List<OutsideAfterimage> afterimages=new List<OutsideAfterimage>();
        private IReadOnlyList<OutsideAfterimage> afterimageView;
        public IReadOnlyList<OutsideAfterimage> Afterimages=>afterimageView??(afterimageView=afterimages.AsReadOnly());
        // 発動開始時の向きは既存Core状態に残し、前隙中やcold復元でも保持する。
        internal void FaceAwayFromWallForAttack(){if(IsWallSliding)Facing=-WallContactDirection;}
        internal void ConfigureTraversal(OutsideTraversalProfile profile)
        {profile?.ValidateFor(Definition);TraversalProfile=profile;}
        internal void EndDashAfterAttack()=>EndDash();
        internal void StopTraversalForLanding(){EndDash();IsDashJumping=false;wallKickAt=-1;kickAway=0;}
        private void EndDash()
        {if(dashEndsAt<0)return;dashEndsAt=-1;if(consecutiveDashes>=2)cooldownUntil=Frame+TraversalProfile.DashCooldownFrames;}
        private void BeginTraversalFrame(bool dashDown,bool dashHeld)
        {
            afterimages.RemoveAll(p=>Frame-p.Frame>=12);
            var p=TraversalProfile;
            if(p!=null){
                if(dashEndsAt>=0&&Frame>=dashEndsAt)EndDash();
                if(cooldownUntil>=0&&Frame>=cooldownUntil){cooldownUntil=-1;consecutiveDashes=0;}
                if(!IsDashing&&cooldownUntil<0&&lastDashAt>=0&&Frame-lastDashAt>=p.DashResetFrames)consecutiveDashes=0;
                if(dashDown&&!dashWasHeld&&IsGrounded&&!IsDashing&&DashCooldownRemaining==0&&consecutiveDashes<2){dashStartedAt=Frame;dashEndsAt=Frame+p.DashFrames;lastDashAt=Frame;consecutiveDashes++;}
            }
            if(IsDashing)lastDashAt=Frame;
            dashWasHeld=dashHeld;
        }
        private OutsideValidationWall ContactWall(int preferred,out int side)
        {
            side=0;var p=TraversalProfile;if(p==null)return null;
            OutsideValidationWall fallback=null;int fallbackSide=0;
            foreach(var wall in p.Walls){
                if(Height>=wall.Top||Height<=wall.Bottom-p.BodyHeight)continue;
                int contact=Position==wall.X-p.BodyHalfWidth?1:Position==wall.X+p.BodyHalfWidth?-1:0;
                if(contact==0)continue;
                if(contact==preferred){side=contact;return wall;}
                if(fallback==null){fallback=wall;fallbackSide=contact;}
            }
            side=fallbackSide;return fallback;
        }
        private bool TryWallKick(bool jumpEdge,int horizontal)
        {
            var p=TraversalProfile;if(!jumpEdge||IsGrounded||p==null||!p.WallActionsUnlocked)return false;
            var wall=ContactWall(horizontal!=0?horizontal:Facing,out int side);if(wall==null||!wall.AllowsKick)return false;
            EndDash();IsDashJumping=false;wallKickAt=Frame;kickAway=-side;Facing=kickAway;SupportId=null;VerticalVelocity=IsInWater?Scale(p.WallKickVelocity,p.Water.JumpPercent):p.WallKickVelocity;IsWallSliding=false;WallSlideFrame=0;return true;
        }
        private int TraversalDirection(int horizontal)
        {if(IsWallKicking&&Frame-wallKickAt<TraversalProfile.WallKickPushFrames)return kickAway;return IsDashing&&horizontal==0?Facing:horizontal;}
        private long TraversalSpeed=>IsWallKicking&&Frame-wallKickAt<TraversalProfile.WallKickPushFrames?TraversalProfile.WallKickSpeedPerSecond:IsDashing?TraversalProfile.DashSpeedPerSecond:IsDashJumping?TraversalProfile.DashJumpSpeedPerSecond:Definition.SpeedPerSecond;
        private void ClampValidationWalls(long previous,long projectedHeight)
        {
            var p=TraversalProfile;if(p==null)return;
            foreach(var wall in p.Walls){
                if(Math.Min(Height,projectedHeight)>=wall.Top||Math.Max(Height,projectedHeight)<=wall.Bottom-p.BodyHeight)continue;
                long left=wall.X-p.BodyHalfWidth,right=wall.X+p.BodyHalfWidth;
                if(Position>previous&&previous<=left&&Position>=left){Position=left;remainder=0;}
                else if(Position<previous&&previous>=right&&Position<=right){Position=right;remainder=0;}
            }
        }
        private void UpdateWallSlide(int horizontal)
        {
            bool prior=IsWallSliding;var p=TraversalProfile;var wall=ContactWall(horizontal,out int side);WallContactDirection=side;
            IsWallSliding=!IsGrounded&&VerticalVelocity<0&&p!=null&&p.WallActionsUnlocked&&wall!=null&&wall.AllowsSlide&&horizontal==side&&horizontal!=0;
            WallSlideFrame=IsWallSliding?prior?WallSlideFrame+1:0:0;
            if(IsWallSliding){IsDashJumping=false;VerticalVelocity=Math.Max(VerticalVelocity,-p.WallSlidePerFrame);}
        }
        private void CompleteTraversalFrame()
        {
            if(IsGrounded){IsDashJumping=false;wallKickAt=-1;IsWallSliding=false;WallSlideFrame=0;}
            if((IsDashing||IsDashJumping)&&(IsMoving||!IsGrounded)&&(afterimages.Count==0||Frame-afterimages[afterimages.Count-1].Frame>=3)){
                var pose=IsGrounded?OutsideAfterimagePose.Dash:Math.Abs(VerticalVelocity)<=ValidationGravity?OutsideAfterimagePose.JumpApex:VerticalVelocity>0?OutsideAfterimagePose.JumpRise:OutsideAfterimagePose.JumpFall;
                if(afterimages.Count==4)afterimages.RemoveAt(0);afterimages.Add(new OutsideAfterimage(Position,Height,Frame,Facing,pose));
            }
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    public sealed partial class OutsideExplorationCore
    {
        internal static long Scale(long value,int percent)=>value/100*percent+value%100*percent/100;
        internal static long RiseBound(long velocity,long gravity){long n=(velocity-1)/gravity+1;return checked((long)((decimal)n*velocity-(decimal)gravity*n*(n-1)/2));}
        public bool IsInWater=>TraversalProfile!=null&&TraversalProfile.Waters.Any(a=>a.Contains(Position,Height));
        public long? LastWaterContactHeight {get;private set;}
        private long WaterJumpVelocity=>IsInWater?Scale(ValidationJumpVelocity,TraversalProfile.Water.JumpPercent):ValidationJumpVelocity;
        private long WaterReleasedRise=>IsInWater?Scale(ValidationReleasedRise,TraversalProfile.Water.JumpPercent):ValidationReleasedRise;
        private void TraceWater(long previousX,long previousY)
        {
            if(TraversalProfile==null)return;
            // 各水域の最後の交差を使う。終点が乾いた床でも着水前の高所を再利用しない。
            decimal latest=-1;foreach(var a in TraversalProfile.Waters){if(!a.LastContact(previousX,previousY,Position,Height,out var y,out var t))continue;if(t>=latest){latest=t;LastWaterContactHeight=y;}}
        }
    }
    public sealed class OutsideWaterStateData
    {
        public long Frame;public int RemainingFrames;public bool Unlimited,Submerged;
        public OutsideWaterStateData Copy()=>(OutsideWaterStateData)MemberwiseClone();
        public void Validate(OutsideCoreStateData core){if(core==null||Frame!=core.Frame||RemainingFrames<0)throw new ArgumentException("呼吸保存の時計/残量が不正です");}
    }
    public sealed partial class OutsideExplorationSession
    {
        private int waterRemaining;private long waterLastFrame;private bool waterUnlimited;
        public int BreathFramesRemaining=>waterRemaining;
        public int BreathMaximumFrames=>Core.TraversalProfile?.Water?.BreathFrames??0;
        public bool IsWaterBreathingUnlimited=>waterUnlimited;
        public bool ShouldShowBreath=>Core.IsInWater&&!waterUnlimited&&Player!=null&&!Player.IsDead;
        // 装備/神依の登録や入手を定義しない。呼出側が既存サービスで効果を確定した後に通知する境界。
        public bool SetWaterBreathingUnlimited(bool value){if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));if(waterUnlimited==value)return false;waterUnlimited=value;waterRemaining=BreathMaximumFrames;return true;}
        private void ResetWater(){waterRemaining=BreathMaximumFrames;waterLastFrame=Core.Frame;}
        private void AdvanceWater()
        {
            waterLastFrame=Core.Frame;if(Core.TraversalProfile?.Water==null)return;
            if(!Core.IsInWater||waterUnlimited){waterRemaining=BreathMaximumFrames;return;}
            if(Core.HasFallenOut||Player.IsDead)return;
            if(waterRemaining>0)waterRemaining--;
            if(waterRemaining==0){Player.TakeFallDamage(Player.MaxHp);Core.StopTraversalForLanding();attackState=null;CancelAttackReservation();CardUseMessage="コキュウ 0 / フッキ";CardUseMessageFrame=Core.Frame;}
        }
        private OutsideWaterStateData CaptureWater()
        {
            if(Core.TraversalProfile?.Water==null)return null;
            if(waterLastFrame!=Core.Frame||Core.IsInWater&&!waterUnlimited&&waterRemaining==0)throw new ArgumentException("未反映または死亡済みの呼吸を保存できません");
            return new OutsideWaterStateData{Frame=Core.Frame,RemainingFrames=waterRemaining,Unlimited=waterUnlimited,Submerged=Core.IsInWater};
        }
        private static void ValidateWater(OutsideWaterStateData value,OutsideExplorationCore core)
        {
            var profile=core.TraversalProfile?.Water;
            if(profile==null){if(value!=null)throw new ArgumentException("旧地形に呼吸保存を追加できません");return;}
            if(value==null)throw new ArgumentException("水域地形の呼吸保存が欠落しています");value.Validate(core.CaptureState());
            if(value.RemainingFrames>profile.BreathFrames||value.Submerged!=core.IsInWater||(!value.Submerged||value.Unlimited)&&value.RemainingFrames!=profile.BreathFrames||value.Submerged&&!value.Unlimited&&value.RemainingFrames==0)throw new ArgumentException("水域と呼吸保存が一致しません");
        }
        private void RestoreWater(OutsideWaterStateData value){if(value==null){ResetWater();return;}waterRemaining=value.RemainingFrames;waterUnlimited=value.Unlimited;waterLastFrame=value.Frame;}
    }
    /// <summary>通常導線の試用蹴り専用。正式な威力/範囲/敵配置や成長表ではない。</summary>
    public static class OutsideKickTrialDefinition
    {
        public const string MapId="validation-landing-32";
        public static OutsideKickTiming[] Timings()=>Enumerable.Repeat(new OutsideKickTiming(6,3,12,6),6).ToArray();
        public static string SaveDefinitionKey=>Key(Timings(),18);
        // 専用攻撃の明示試用値。旧6種の時間配列/keyへ追加しない。
        public static OutsideKickTiming DashTiming()=>new OutsideKickTiming(6,3,12);
        public const int DashDamage=10;public const long DashRange=180000;
        internal static string DashKey(OutsideKickTiming t)=>Olca.Core.Logic.Dungeon.CheckpointValues.Hash(w=>{w.Write("trial-dash-kick-v1");w.Write(SaveDefinitionKey);w.Write(DashDamage);w.Write(DashRange);w.Write(t.StartupFrames);w.Write(t.ActiveFrames);w.Write(t.RecoveryFrames);w.Write(t.RecoveryCancelAfterFrames??-1);});
        public static string DashSaveDefinitionKey=>DashKey(DashTiming());
        public static int DamageFor(OutsideKickKind kind)=>kind==OutsideKickKind.Dash?DashDamage:Damage;
        internal static string Key(OutsideKickTiming[] timings,int continuation)=>Olca.Core.Logic.Dungeon.CheckpointValues.Hash(w=>{
            w.Write("trial-kick-state-v1");w.Write(MapId);w.Write(OutsideExplorationSession.TrialTargetX);w.Write(OutsideExplorationSession.TrialTargetMaximum);w.Write(Damage);w.Write(Range);w.Write(TargetHeight);w.Write(SecondTargetX);w.Write(continuation);
            foreach(var v in timings){w.Write(v.StartupFrames);w.Write(v.ActiveFrames);w.Write(v.RecoveryFrames);w.Write(v.RecoveryCancelAfterFrames??-1);}
            for(int n=0;n<6;n++){Bounds((OutsideKickKind)n,out var low,out var high);w.Write(low);w.Write(high);}
        });
        public const int Damage=10;
        public const long Range=180000,TargetHeight=54000,SecondTargetX=1170000;
        // 標的の水平中心点と縦区間を使う明示試用範囲。地形遮蔽/身体幅は製品衝突として確定しない。
        public static bool Contains(OutsideKickKind kind,long x,long y,int facing,long targetX)
        {
            if(facing!=-1&&facing!=1)throw new ArgumentOutOfRangeException(nameof(facing));
            long dx=checked((targetX-x)*facing);if(dx<0||dx>(kind==OutsideKickKind.Dash?DashRange:Range))return false;
            Bounds(kind,out var bottom,out var top);return checked(y+bottom)<=TargetHeight&&checked(y+top)>=0;
        }
        private static void Bounds(OutsideKickKind kind,out long bottom,out long top)
        {
            switch(kind){
                case OutsideKickKind.Dash:case OutsideKickKind.GroundFirst:case OutsideKickKind.GroundSecond:case OutsideKickKind.GroundThird:bottom=-12000;top=90000;break;
                case OutsideKickKind.AirForward:bottom=-45000;top=45000;break;
                case OutsideKickKind.AirUp:bottom=0;top=120000;break;
                case OutsideKickKind.AirDown:bottom=-120000;top=0;break;
                default:throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
    /// <summary>開始済みの試用蹴りだけ。入力/予約/物理保持は含めない。</summary>
    public sealed class OutsideKickStateData
    {
        public string DefinitionKey;public bool Active;public OutsideKickKind Kind;
        public long StartedAt,EndsAt,ComboUntil;public int AttackFacing,NextGroundStage;public int[] HitTargets;
        public OutsideKickStateData Copy()=>new OutsideKickStateData{DefinitionKey=DefinitionKey,Active=Active,Kind=Kind,StartedAt=StartedAt,EndsAt=EndsAt,ComboUntil=ComboUntil,AttackFacing=AttackFacing,NextGroundStage=NextGroundStage,HitTargets=HitTargets?.ToArray()};
        public void Validate(long frame,bool grounded,int facing)
        {
            if(DefinitionKey!=(Kind==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashSaveDefinitionKey:OutsideKickTrialDefinition.SaveDefinitionKey)||(int)Kind<0||(int)Kind>6||StartedAt<0||StartedAt>frame||AttackFacing!=1&&AttackFacing!=-1||HitTargets==null||HitTargets.Any(v=>v<0||v>1)||HitTargets.Distinct().Count()!=HitTargets.Length)throw new ArgumentException("蹴り保存の定義/識別が不正です");
            var timing=Kind==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashTiming():OutsideKickTrialDefinition.Timings()[(int)Kind];if(EndsAt!=checked(StartedAt+timing.Duration))throw new ArgumentException("蹴り保存の時間が不正です");
            if(Active){if(frame>=EndsAt||NextGroundStage!=1||ComboUntil!=-1||Kind!=OutsideKickKind.Dash&&grounded!=(Kind<=OutsideKickKind.GroundThird)||(grounded||Kind==OutsideKickKind.Dash)&&facing!=AttackFacing||HitTargets.Length>0&&frame-StartedAt<timing.StartupFrames)throw new ArgumentException("蹴り保存の発動/接地境界が不正です");}
            else if(Kind>=OutsideKickKind.GroundThird||!grounded||frame<EndsAt||ComboUntil!=checked(EndsAt+18)||frame>=ComboUntil||NextGroundStage!=(int)Kind+2||HitTargets.Length!=0)throw new ArgumentException("蹴り保存の継続猶予が不正です");
        }
    }
    public enum OutsideKickKind { GroundFirst, GroundSecond, GroundThird, AirForward, AirUp, AirDown, Dash }

    /// <summary>性能値や素材を含まない必須時間定義。未登録の有効期間を暗黙の既定値で補わない。</summary>
    public sealed class OutsideKickTiming
    {
        public int StartupFrames {get;} public int ActiveFrames {get;} public int RecoveryFrames {get;}
        public int? RecoveryCancelAfterFrames {get;} public int Duration {get;}
        public OutsideKickTiming(int startupFrames,int activeFrames,int recoveryFrames,int? recoveryCancelAfterFrames=null)
        {
            if(startupFrames<0||activeFrames<=0||recoveryFrames<0||recoveryCancelAfterFrames<0||recoveryCancelAfterFrames>recoveryFrames)throw new ArgumentOutOfRangeException(nameof(activeFrames));
            Duration=checked(startupFrames+activeFrames+recoveryFrames);StartupFrames=startupFrames;ActiveFrames=activeFrames;RecoveryFrames=recoveryFrames;RecoveryCancelAfterFrames=recoveryCancelAfterFrames;
        }
    }
    /// <summary>外界通常蹴りのSession所有状態。登録データ/空間判定/HP反映/本体描画は別責務。</summary>
    public sealed class OutsideNormalKickSequence
    {
        private readonly Olca.Core.Logic.Battle.AttackInputReservation reservation;
        private readonly HashSet<int> hitTargets=new HashSet<int>();
        private OutsideKickTiming[] timings;private OutsideKickTiming dashTiming;
        private long frame=-1,lastRequestFrame=-1,comboUntil=-1;
        private int observedFacing,nextGroundStage=1,pendingVertical;
        private bool observed,observedGrounded,active;
        public OutsideKickKind Kind {get;private set;}
        public long StartedAt {get;private set;} public long EndsAt {get;private set;}
        public int AttackFacing {get;private set;}
        public bool IsConfigured=>timings!=null;
        public bool IsActive=>active;
        public bool HasDashTiming=>dashTiming!=null;
        public bool IsDashAttack=>active&&Kind==OutsideKickKind.Dash;
        private OutsideKickTiming CurrentTiming=>Kind==OutsideKickKind.Dash?dashTiming:timings[(int)Kind];
        public bool LocksGroundMotion=>active&&Kind<=OutsideKickKind.GroundThird;
        public int NextGroundStage=>nextGroundStage;
        public long ComboExpiresAt=>comboUntil;
        public bool HasProgress=>active||nextGroundStage!=1||reservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.Basic;
        public bool IsHitWindow=>active&&frame-StartedAt>=CurrentTiming.StartupFrames&&frame-StartedAt<CurrentTiming.StartupFrames+(long)CurrentTiming.ActiveFrames;
        public bool IsRecovery=>active&&frame-StartedAt>=CurrentTiming.StartupFrames+(long)CurrentTiming.ActiveFrames;
        public OutsideNormalKickSequence(Olca.Core.Logic.Battle.AttackInputReservation reservation)
        {this.reservation=reservation??throw new ArgumentNullException(nameof(reservation));}
        // 順序は地上1/2/3・空中横/上/下。全6定義が必要。検証値を製品既定として登録しない。
        public void Configure(params OutsideKickTiming[] definitions)
        {
            if(definitions==null||definitions.Length!=6||definitions.Any(v=>v==null))throw new ArgumentException("蹴り6種の時間定義が必要です");
            if(HasProgress)throw new InvalidOperationException("進行中の蹴り定義は変更できません");timings=definitions.ToArray();
        }
        public void ConfigureDash(OutsideKickTiming definition){if(!IsConfigured||HasProgress)throw new InvalidOperationException("進行中/未設定の蹴りへダッシュ定義は追加できません");dashTiming=definition??throw new ArgumentNullException(nameof(definition));}
        public OutsideKickStateData CaptureState(long logicalFrame,bool grounded,int facing)
        {
            if(!active&&nextGroundStage==1)return null;
            if(!IsConfigured||frame!=logicalFrame||OutsideKickTrialDefinition.Key(timings,ContinuationFrames)!=OutsideKickTrialDefinition.SaveDefinitionKey)throw new ArgumentException("未登録の蹴り時間/性能は保存できません");
            if(Kind==OutsideKickKind.Dash&&(dashTiming==null||OutsideKickTrialDefinition.DashKey(dashTiming)!=OutsideKickTrialDefinition.DashSaveDefinitionKey))throw new ArgumentException("未登録のダッシュ攻撃は保存できません");
            var v=new OutsideKickStateData{DefinitionKey=Kind==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashSaveDefinitionKey:OutsideKickTrialDefinition.SaveDefinitionKey,Active=active,Kind=Kind,StartedAt=StartedAt,EndsAt=EndsAt,ComboUntil=comboUntil,AttackFacing=AttackFacing,NextGroundStage=nextGroundStage,HitTargets=hitTargets.OrderBy(x=>x).ToArray()};v.Validate(logicalFrame,grounded,facing);return v;
        }
        public void RestoreState(OutsideKickStateData value,long logicalFrame,bool grounded,int facing)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));value.Validate(logicalFrame,grounded,facing);
            if(!IsConfigured||OutsideKickTrialDefinition.Key(timings,ContinuationFrames)!=OutsideKickTrialDefinition.SaveDefinitionKey||value.Kind==OutsideKickKind.Dash&&(dashTiming==null||OutsideKickTrialDefinition.DashKey(dashTiming)!=value.DefinitionKey))throw new ArgumentException("復元する蹴り定義が一致しません");
            Reset();frame=lastRequestFrame=logicalFrame;observed=true;observedFacing=facing;observedGrounded=grounded;active=value.Active;Kind=value.Kind;StartedAt=value.StartedAt;EndsAt=value.EndsAt;AttackFacing=value.AttackFacing;nextGroundStage=value.NextGroundStage;comboUntil=value.ComboUntil;foreach(int id in value.HitTargets)hitTargets.Add(id);
        }
        private void CancelOwnReservation(){if(reservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.Basic)reservation.Cancel();}
        public void Interrupt(){active=false;nextGroundStage=1;comboUntil=-1;hitTargets.Clear();CancelOwnReservation();}
        public void Reset(){Interrupt();frame=lastRequestFrame=-1;observed=false;}
        public void CardStarted()=>Interrupt();
        public float ResolveHorizontal(float horizontal)
        {if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new ArgumentOutOfRangeException(nameof(horizontal));return IsDashAttack?AttackFacing:LocksGroundMotion?0:horizontal;}
        public int ResolveFacing(int facing)
        {if(facing!=-1&&facing!=1)throw new ArgumentOutOfRangeException(nameof(facing));return LocksGroundMotion||IsDashAttack?AttackFacing:facing;}
        public void Observe(long logicalFrame,bool grounded,int facing,bool bodyAllowed=true)
        {
            if(logicalFrame<0||logicalFrame<frame||(facing!=-1&&facing!=1))throw new ArgumentOutOfRangeException(nameof(logicalFrame));
            frame=logicalFrame;reservation.Observe(frame);
            if(observed&&(observedFacing!=facing||observedGrounded!=grounded))CancelOwnReservation();
            observed=true;observedFacing=facing;observedGrounded=grounded;
            if(!bodyAllowed){Interrupt();return;}
            if(active&&!IsDashAttack&&(LocksGroundMotion&&!grounded||!LocksGroundMotion&&grounded)){Interrupt();return;}
            if(!grounded){nextGroundStage=1;comboUntil=-1;}
            if(active&&frame>=EndsAt){
                bool continueGround=Kind<OutsideKickKind.GroundThird;active=false;hitTargets.Clear();
                nextGroundStage=continueGround?(int)Kind+2:1;comboUntil=continueGround?checked(EndsAt+ContinuationFrames):-1;
            }
            if(nextGroundStage!=1&&frame>=comboUntil){nextGroundStage=1;comboUntil=-1;}
        }
        /// <summary>18frameは承認済みの調整可能な仮猶予。6frame予約の寿命を延長しない。</summary>
        public int ContinuationFrames {get;}=18;
        public OutsideNormalKickSequence(Olca.Core.Logic.Battle.AttackInputReservation reservation,int continuationFrames):this(reservation)
        {if(continuationFrames<0)throw new ArgumentOutOfRangeException(nameof(continuationFrames));ContinuationFrames=continuationFrames;}
        public bool CanCancelNormal {
            get{
                if(!active)return true;if(Kind==OutsideKickKind.GroundThird||Kind==OutsideKickKind.Dash)return false;
                var timing=timings[(int)Kind];return timing.RecoveryCancelAfterFrames.HasValue&&frame-StartedAt>=timing.StartupFrames+(long)timing.ActiveFrames+timing.RecoveryCancelAfterFrames.Value;
            }
        }
        private bool Begin(bool grounded,int facing,int vertical,bool dashing=false)
        {
            int stage=grounded?(active?(int)Kind+2:nextGroundStage):0;
            var kind=grounded&&dashing?OutsideKickKind.Dash:grounded?(OutsideKickKind)(stage-1):vertical>0?OutsideKickKind.AirUp:vertical<0?OutsideKickKind.AirDown:OutsideKickKind.AirForward;
            long end=checked(frame+(kind==OutsideKickKind.Dash?dashTiming:timings[(int)kind]).Duration);if(kind!=OutsideKickKind.Dash&&grounded&&stage<3)checked{_ = end+ContinuationFrames;}
            active=true;Kind=kind;AttackFacing=facing;StartedAt=frame;EndsAt=end;nextGroundStage=1;comboUntil=-1;hitTargets.Clear();reservation.Cancel();return true;
        }
        public bool TryRequest(long logicalFrame,bool grounded,int facing,int vertical=0,bool bodyAllowed=true,bool dashing=false,bool otherAttack=false,bool knownLaterThanCard=false)
        {
            if(vertical<-1||vertical>1)throw new ArgumentOutOfRangeException(nameof(vertical));Observe(logicalFrame,grounded,facing,bodyAllowed);
            if(!IsConfigured||!bodyAllowed||dashing&&(!grounded||dashTiming==null)||lastRequestFrame==frame)return false;lastRequestFrame=frame;
            // 同frameで前後不明ならカード優先。既存入力coordinatorが時刻順を確定できた場合だけ上書きする。
            if(reservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.Card&&reservation.RequestedFrame==frame&&!knownLaterThanCard)return false;
            if(otherAttack||active&&!CanCancelNormal){pendingVertical=vertical;reservation.Request(Olca.Core.Logic.Battle.AttackReservationKind.Basic,frame);return false;}
            return Begin(grounded,facing,vertical,dashing);
        }
        public bool TryExecuteReserved(long logicalFrame,bool grounded,int facing,bool bodyAllowed=true,bool dashing=false,bool otherAttack=false)
        {
            Observe(logicalFrame,grounded,facing,bodyAllowed);
            if(!IsConfigured||!bodyAllowed||dashing&&(!grounded||dashTiming==null)||otherAttack||reservation.Kind!=Olca.Core.Logic.Battle.AttackReservationKind.Basic||active&&!CanCancelNormal)return false;
            return Begin(grounded,facing,pendingVertical,dashing);
        }
        /// <summary>空間サービスが範囲内と判定した各敵のSession内ID。威力/範囲はここで作らない。</summary>
        public bool TryMarkHit(int targetId)
        {if(targetId<0)throw new ArgumentOutOfRangeException(nameof(targetId));return IsHitWindow&&hitTargets.Add(targetId);}
    }

}
