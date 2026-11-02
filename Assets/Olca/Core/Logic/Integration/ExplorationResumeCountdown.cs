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
    /// <summary>Root所有の外界状態。現在は製品登録と分けた検証地形のみを扱う。</summary>
    public sealed class OutsideExplorationSession : System.IDisposable
    {
        public sealed class PresentationLease : System.IDisposable
        {
            private OutsideExplorationSession owner;
            internal PresentationLease(OutsideExplorationSession owner){this.owner=owner;}
            public OutsideExplorationCore Core=>owner?.Core;
            public bool IsCurrent=>owner!=null&&!owner.IsEnded&&ReferenceEquals(owner.current,this);
            public void Dispose(){var prior=owner;owner=null;if(prior!=null&&ReferenceEquals(prior.current,this))prior.current=null;}
        }
        private PresentationLease current;
        public OutsideExplorationCore Core {get;}=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);
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
    /// <summary>製品値ではない検証用定義。座標は1単位=60000の固定小数点。</summary>
    public sealed class OutsideMovementDefinition
    {
        public const long UnitsPerWorldUnit=60000;
        public string Id {get;}
        public long Width {get;}
        public long Start {get;}
        public long SpeedPerSecond {get;}
        public OutsideMovementDefinition(string id,long width,long start,long speedPerSecond)
        {
            if(string.IsNullOrWhiteSpace(id)||width<=0||start<0||start>width||speedPerSecond<=0)
                throw new System.ArgumentOutOfRangeException(nameof(width));
            Id=id;Width=width;Start=start;SpeedPerSecond=speedPerSecond;
        }
        // 操作・追従・両端を実証するだけの平地32単位、開始8、毎秒6。製品マップへ保存しない。
        public static OutsideMovementDefinition ValidationTerrain=>new OutsideMovementDefinition("validation-flat-32",32*UnitsPerWorldUnit,8*UnitsPerWorldUnit,6*UnitsPerWorldUnit);
    }
    /// <summary>連続地形の横移動。描画時間・既存ダンジョン座標を参照しない。</summary>
    public sealed class OutsideExplorationCore
    {
        public OutsideMovementDefinition Definition {get;}
        public long Position {get;private set;}
        public int Facing {get;private set;}=1;
        public long Frame {get;private set;}
        public long Height {get;private set;}
        public long VerticalVelocity {get;private set;}
        public bool IsGrounded=>Height==0&&VerticalVelocity==0;
        public bool IsMoving {get;private set;}
        public long MovementFrame {get;private set;}
        private bool jumpWasHeld;
        private long remainder;
        // 検証用: 初速12単位/秒、重力24単位/秒²、解放時の上昇上限4単位/秒。
        // 60000固定小数点/60Hzのため、毎frameの値は12000/400/4000。製品値ではない。
        public const long ValidationJumpVelocity=12000,ValidationGravity=400,ValidationReleasedRise=4000;
        public OutsideExplorationCore(OutsideMovementDefinition definition)
        {Definition=definition??throw new System.ArgumentNullException(nameof(definition));Position=definition.Start;}
        public void TickFrame(float horizontal,bool jumpDown=false,bool jumpHeld=false)
        {
            if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new System.ArgumentOutOfRangeException(nameof(horizontal));
            Frame++;
            bool launch=jumpDown&&!jumpWasHeld&&IsGrounded;
            jumpWasHeld=jumpHeld;
            if(launch)VerticalVelocity=ValidationJumpVelocity;
            if(!IsGrounded){
                if(!jumpHeld&&VerticalVelocity>ValidationReleasedRise)VerticalVelocity=ValidationReleasedRise;
                // 平地への着地で速度を零にする。空中押下は予約せず捨てる。
                if(VerticalVelocity<0&&Height<=-VerticalVelocity){Height=0;VerticalVelocity=0;}
                else{Height+=VerticalVelocity;VerticalVelocity-=ValidationGravity;}
            }
            long previous=Position;
            // 現単位では左右のデジタル操作。スティックは既存入力のdeadzone後の方向を用いる。
            int direction=horizontal>0?1:horizontal<0?-1:0;
            if(direction==0){remainder=0;IsMoving=false;MovementFrame=0;return;}
            Facing=direction;
            long tail=Definition.SpeedPerSecond%LogicalFrameClock.FramesPerSecond*direction+remainder;
            long step=Definition.SpeedPerSecond/LogicalFrameClock.FramesPerSecond*direction+tail/LogicalFrameClock.FramesPerSecond;
            remainder=tail%LogicalFrameClock.FramesPerSecond;
            // 加算前に残距離を比較する。公開定義のlong極値もwrapさせない。
            if(direction>0&&step>=Definition.Width-Position){Position=Definition.Width;remainder=0;}
            else if(direction<0&&step<=-Position){Position=0;remainder=0;}
            else Position+=step;
            IsMoving=Position!=previous;MovementFrame=IsMoving?MovementFrame+1:0;
        }
    }
}
