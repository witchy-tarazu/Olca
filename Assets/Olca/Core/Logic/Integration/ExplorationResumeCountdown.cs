namespace Olca.Core.Logic.Integration
{
    /// <summary>探索再開専用の論理時計。探索の時間や保存状態は進めない。</summary>
    public sealed class ExplorationResumeCountdown
    {
        public const int FramesPerCount = 30;
        public const int TotalFrames = FramesPerCount * 3;
        public int RemainingFrames { get; private set; }
        public bool IsRunning => RemainingFrames > 0;
        public int DisplayCount => IsRunning ? (RemainingFrames + FramesPerCount - 1) / FramesPerCount : 0;
        public void Begin() { if (!IsRunning) RemainingFrames = TotalFrames; }
        public void Cancel() { RemainingFrames = 0; }
        /// <returns>今回のフレームで完了した場合のみtrue。</returns>
        public bool AdvanceFrame()
        {
            if (!IsRunning) return false;
            return --RemainingFrames == 0;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>探索カード用の共有消費ゲージ。Dungeonのカスタム開放時計とは独立。</summary>
    public sealed class OutsideCardGauge
    {
        // 1点を60分割し、整数の毎秒回復量を60Hzで誤差なく積算する。
        public const long UnitsPerPoint=60;
        public int Maximum {get;private set;}
        public int RecoveryPerSecond {get;private set;}
        public long MaximumUnits=>(long)Maximum*UnitsPerPoint;
        public long CurrentUnits {get;private set;}
        public OutsideCardGauge(int maximum,int recoveryPerSecond,int initialPoints)
        {Validate(maximum,recoveryPerSecond);if(initialPoints<0||initialPoints>maximum)throw new System.ArgumentOutOfRangeException(nameof(initialPoints));Maximum=maximum;RecoveryPerSecond=recoveryPerSecond;CurrentUnits=(long)initialPoints*UnitsPerPoint;}
        private static void Validate(int maximum,int recovery)
        {if(maximum<=0||recovery<0)throw new System.ArgumentOutOfRangeException(nameof(maximum));}
        public void Configure(int maximum,int recoveryPerSecond)
        {Validate(maximum,recoveryPerSecond);Maximum=maximum;RecoveryPerSecond=recoveryPerSecond;CurrentUnits=System.Math.Min(CurrentUnits,MaximumUnits);}
        public bool TrySpend(int cardCost)
        {if(cardCost<0)throw new System.ArgumentOutOfRangeException(nameof(cardCost));long units=(long)cardCost*UnitsPerPoint;if(CurrentUnits<units)return false;CurrentUnits-=units;return true;}
        public void AdvanceFrame(){CurrentUnits=System.Math.Min(MaximumUnits,CurrentUnits+RecoveryPerSecond);}
        public void RestoreFull(){CurrentUnits=MaximumUnits;}
        // 仕様で仮値が許可された最大量/回復速度。初期満量も検証マップだけの設定。
        public static OutsideCardGauge ValidationDefault=>new OutsideCardGauge(100,10,100);
    }
    /// <summary>外界マップの水平カメラ方針。投影倍率やゲーム座標は変更しない。</summary>
    public sealed class OutsideCameraPolicy
    {
        public long? FixedCenter {get;}
        private OutsideCameraPolicy(long? center){FixedCenter=center;}
        public static OutsideCameraPolicy Follow {get;}=new OutsideCameraPolicy(null);
        public static OutsideCameraPolicy FixedAt(long center)
        {if(center<0)throw new System.ArgumentOutOfRangeException(nameof(center));return new OutsideCameraPolicy(center);}
        public float ResolveHorizontalCenter(long position,long mapWidth,float viewport)
        {
            if(mapWidth<=0||position<0||position>mapWidth||float.IsNaN(viewport)||float.IsInfinity(viewport)||viewport<=0||FixedCenter>mapWidth)throw new System.ArgumentOutOfRangeException(nameof(viewport));
            double unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float width=(float)(mapWidth/unit),target=(float)((FixedCenter??position)/unit);
            // 追従時の既存余白1単位と、マップが画面より狭い場合の中央配置を維持する。
            return width<=viewport?width*.5f:System.Math.Max(viewport*.5f-1,System.Math.Min(target,width-viewport*.5f+1));
        }
    }
    /// <summary>Root所有の外界状態。現在は製品登録と分けた検証地形のみを扱う。</summary>
    public sealed class OutsideExplorationSession : System.IDisposable
    {
        public sealed class PresentationLease : System.IDisposable
        {
            private OutsideExplorationSession owner;
            internal PresentationLease(OutsideExplorationSession owner){this.owner=owner;}
            public OutsideExplorationCore Core=>owner?.Core;
            public OutsideCameraPolicy Camera=>owner?.Camera;
            public bool IsCurrent=>owner!=null&&!owner.IsEnded&&ReferenceEquals(owner.current,this);
            public void Dispose(){var prior=owner;owner=null;if(prior!=null&&ReferenceEquals(prior.current,this))prior.current=null;}
        }
        private PresentationLease current;
        public OutsideExplorationCore Core {get;}=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain);
        public OutsideCameraPolicy Camera {get;private set;}=OutsideCameraPolicy.Follow;
        // マップ入場前に設定する。表示中の変更は画面の急な移動を防ぐため受理しない。
        public bool TryConfigureCamera(OutsideCameraPolicy policy)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(policy==null)throw new System.ArgumentNullException(nameof(policy));
            if(policy.FixedCenter.HasValue&&policy.FixedCenter.Value>Core.Definition.Width)throw new System.ArgumentOutOfRangeException(nameof(policy));
            if(IsPresented)return false;Camera=policy;return true;
        }
        public bool IsEnded{get;private set;}
        public bool IsPresented=>current!=null;
        public PresentationLease AttachPresentation()
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            return current=new PresentationLease(this);
        }
        public void Dispose(){if(IsEnded)return;IsEnded=true;current?.Dispose();current=null;}
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>検証用の下降接地上面。側面/下面や身体幅の製品ルールは表さない。</summary>
    public sealed class OutsideLandingSurface
    {
        public string Id {get;}public long Left {get;}public long Right {get;}public long Top {get;}
        public OutsideLandingSurface(string id,long left,long right,long top)
        {if(string.IsNullOrWhiteSpace(id)||id=="floor"||left<0||right<=left||top<=0)throw new System.ArgumentException("接地上面の検証値が不正です");Id=id;Left=left;Right=right;Top=top;}
        // 点の足元を使う検証モデル。左右端は同じ閉区間とする。
        public bool Contains(long x)=>x>=Left&&x<=Right;
    }
    /// <summary>製品値ではない検証用定義。座標は1単位=60000の固定小数点。</summary>
    public sealed class OutsideMovementDefinition
    {
        public const long UnitsPerWorldUnit=60000;
        public string Id {get;}
        public long Width {get;}
        public long Start {get;}
        public long SpeedPerSecond {get;}
        public System.Collections.Generic.IReadOnlyList<OutsideLandingSurface> LandingSurfaces {get;}
        public OutsideMovementDefinition(string id,long width,long start,long speedPerSecond,params OutsideLandingSurface[] surfaces)
        {
            if(string.IsNullOrWhiteSpace(id)||width<=0||start<0||start>width||speedPerSecond<=0)
                throw new System.ArgumentOutOfRangeException(nameof(width));
            var copy=surfaces==null?System.Array.Empty<OutsideLandingSurface>():(OutsideLandingSurface[])surfaces.Clone();var ids=new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach(var surface in copy)if(surface==null||surface.Right>width||surface.Top>long.MaxValue-OutsideExplorationCore.ValidationMaximumRise||!ids.Add(surface.Id))throw new System.ArgumentException("上面範囲/識別子/固定小数点の表現範囲が不正です");
            Id=id;Width=width;Start=start;SpeedPerSecond=speedPerSecond;LandingSurfaces=System.Array.AsReadOnly(copy);
        }
        // 操作・追従・両端を実証するだけの平地32単位、開始8、毎秒6。製品マップへ保存しない。
        public static OutsideMovementDefinition ValidationTerrain=>new OutsideMovementDefinition("validation-flat-32",32*UnitsPerWorldUnit,8*UnitsPerWorldUnit,6*UnitsPerWorldUnit);
        // 製品配置ではない。既存32単位平地に12..16、高さ1の接地上面だけを追加する。
        public static OutsideMovementDefinition ValidationLandingTerrain=>new OutsideMovementDefinition("validation-landing-32",32*UnitsPerWorldUnit,8*UnitsPerWorldUnit,6*UnitsPerWorldUnit,new OutsideLandingSurface("validation-top",12*UnitsPerWorldUnit,16*UnitsPerWorldUnit,UnitsPerWorldUnit));
    }
    /// <summary>連続地形の横移動。描画時間・既存ダンジョン座標を参照しない。</summary>
    public sealed class OutsideExplorationCore
    {
        public OutsideMovementDefinition Definition {get;}
        public OutsideCardGauge CardGauge {get;}=OutsideCardGauge.ValidationDefault;
        public long Position {get;private set;}
        public int Facing {get;private set;}=1;
        public long Frame {get;private set;}
        public long Height {get;private set;}
        public long VerticalVelocity {get;private set;}
        public string SupportId {get;private set;}="floor";
        public bool IsGrounded=>SupportId!=null;
        public bool IsMoving {get;private set;}
        public long MovementFrame {get;private set;}
        private bool jumpWasHeld;
        private long remainder;
        // 検証用: 初速12単位/秒、重力24単位/秒²、解放時の上昇上限4単位/秒。
        // 60000固定小数点/60Hzのため、毎frameの値は12000/400/4000。製品値ではない。
        public const long ValidationJumpVelocity=12000,ValidationGravity=400,ValidationReleasedRise=4000;
        public const long ValidationMaximumRise=ValidationJumpVelocity*(ValidationJumpVelocity/ValidationGravity+1)/2;
        public OutsideExplorationCore(OutsideMovementDefinition definition)
        {Definition=definition??throw new System.ArgumentNullException(nameof(definition));Position=definition.Start;}
        private string SupportAt(long x,long y)
        {
            if(y==0)return "floor";string id=null;
            foreach(var surface in Definition.LandingSurfaces)if(surface.Top==y&&surface.Contains(x)&&(id==null||System.StringComparer.Ordinal.Compare(surface.Id,id)<0))id=surface.Id;
            return id;
        }
        private bool Landing(long oldX,long oldY,long nextY,out long top,out string id)
        {
            top=0;id=nextY<=0?"floor":null;
            foreach(var surface in Definition.LandingSurfaces){
                if(oldY<surface.Top||nextY>surface.Top||!surface.Contains(Position))continue;
                decimal fraction=(decimal)(oldY-surface.Top)/(oldY-nextY);
                decimal x=(decimal)oldX+((decimal)Position-oldX)*fraction;
                if(x<surface.Left||x>surface.Right)continue;
                if(id==null||surface.Top>top||surface.Top==top&&System.StringComparer.Ordinal.Compare(surface.Id,id)<0){top=surface.Top;id=surface.Id;}
            }
            return id!=null;
        }
        public void TickFrame(float horizontal,bool jumpDown=false,bool jumpHeld=false)
        {
            if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new System.ArgumentOutOfRangeException(nameof(horizontal));
            Frame++;CardGauge.AdvanceFrame();bool launch=jumpDown&&!jumpWasHeld&&IsGrounded;jumpWasHeld=jumpHeld;
            long previous=Position,oldHeight=Height;
            int direction=horizontal>0?1:horizontal<0?-1:0;
            if(direction==0)remainder=0;
            else{
                Facing=direction;
                long tail=Definition.SpeedPerSecond%LogicalFrameClock.FramesPerSecond*direction+remainder;
                long step=Definition.SpeedPerSecond/LogicalFrameClock.FramesPerSecond*direction+tail/LogicalFrameClock.FramesPerSecond;
                remainder=tail%LogicalFrameClock.FramesPerSecond;
                if(direction>0&&step>=Definition.Width-Position){Position=Definition.Width;remainder=0;}
                else if(direction<0&&step<=-Position){Position=0;remainder=0;}
                else Position+=step;
            }
            IsMoving=Position!=previous;MovementFrame=IsMoving?MovementFrame+1:0;
            if(launch){SupportId=null;VerticalVelocity=ValidationJumpVelocity;}
            else if(IsGrounded){SupportId=SupportAt(Position,Height);if(IsGrounded)return;VerticalVelocity=0;}
            if(!jumpHeld&&VerticalVelocity>ValidationReleasedRise)VerticalVelocity=ValidationReleasedRise;
            long next=Height+VerticalVelocity;
            if(VerticalVelocity<0&&Landing(previous,oldHeight,next,out var top,out var support)){Height=top;VerticalVelocity=0;SupportId=support;}
            else{Height=next;VerticalVelocity-=ValidationGravity;SupportId=null;}
        }
    }
}
