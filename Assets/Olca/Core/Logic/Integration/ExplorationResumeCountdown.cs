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
    /// <summary>探索専用デッキの初期手札。Dungeonとは個体もドロー履歴も共有しない。</summary>
    public sealed class OutsideExplorationCards
    {
        private readonly Olca.Core.Domain.Deck deck;
        public System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card> Hand {get;private set;}
        private readonly Olca.Core.Interfaces.IRandomProvider random;
        public long HandRevision {get;private set;}
        public void Reload(){deck.Shuffle(random);Hand=deck.DrawCards(3,random).AsReadOnly();SelectedIndex=0;HandRevision++;}
        public int SelectedIndex {get;private set;}
        public Olca.Core.Domain.Card SelectedCard=>Hand[SelectedIndex];
        public bool TrySelect(int index)
        {if(index<0||index>=Hand.Count)return false;SelectedIndex=index;return true;}
        public int RemainingCount=>deck.RemainingCount;
        public int TotalCount=>deck.TotalCount;
        private OutsideExplorationCards(Olca.Core.Domain.Deck deck,Olca.Core.Interfaces.IRandomProvider random)
        {this.deck=deck;this.random=random;Hand=deck.DrawCards(3,random).AsReadOnly();}
        public static bool TryCreate(System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card> cards,System.Collections.Generic.IReadOnlyDictionary<(string Id,Olca.Core.Domain.ExceedLevel Level),int> owned,Olca.Core.Interfaces.IRandomProvider random,out OutsideExplorationCards result,out string error)
        {
            if(random==null)throw new System.ArgumentNullException(nameof(random));
            result=null;
            if(!Olca.Core.Domain.DepartureDeckRules.TryCreate(cards,owned,out var deck,out error))return false;
            result=new OutsideExplorationCards(deck,random);return true;
        }
    }
    /// <summary>外界マップの水平カメラ方針。投影倍率やゲーム座標は変更しない。</summary>
    public sealed class OutsideCameraPolicy
    {
        public long? FixedCenter {get;}
        private OutsideCameraPolicy(long? center){FixedCenter=center;}
        public static OutsideCameraPolicy Follow {get;}=new OutsideCameraPolicy(null);
        public static OutsideCameraPolicy FixedAt(long center)
        {if(center<0)throw new System.ArgumentOutOfRangeException(nameof(center));return new OutsideCameraPolicy(center);}
        /// <summary>追従表示で本体上端を固定HUDより下へ保つ。ゲーム座標や投影倍率は変えない。</summary>
        public float ResolveVerticalOffset(long height,float viewport,float floorInset,float topInset,float actorTop)
        {
            bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
            if(height<0||!Finite(viewport)||viewport<=0||!Finite(floorInset)||floorInset<0||!Finite(topInset)||topInset<0||!Finite(actorTop)||actorTop<=0)throw new System.ArgumentOutOfRangeException(nameof(viewport));
            if(FixedCenter.HasValue)return 0;
            double maximumFoot=System.Math.Max(0,(double)viewport-floorInset-topInset-actorTop);
            return (float)System.Math.Max(0,height/(double)OutsideMovementDefinition.UnitsPerWorldUnit-maximumFoot);
        }
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
            public Olca.Core.Domain.PlayerCharacter Player=>owner?.Player;
            public OutsideCameraPolicy Camera=>owner?.Camera;
            public OutsideExplorationCards Cards=>owner?.Cards;
            public bool IsCurrent=>owner!=null&&!owner.IsEnded&&ReferenceEquals(owner.current,this);
            public void Dispose(){var prior=owner;owner=null;if(prior!=null&&ReferenceEquals(prior.current,this))prior.current=null;}
        }
        private PresentationLease current;
        // 外界だけが所有する個体。DungeonのPlayerやViewへ所有権を渡さない。
        public Olca.Core.Domain.PlayerCharacter Player {get;private set;}
        private long playerFrame;
        public bool CanPerformAttack=>!IsEnded&&Player!=null&&!Player.IsDead&&!Player.IsParalyzed&&!Player.IsStaggered;
        public bool TryInitializePlayer(System.Func<Olca.Core.Domain.PlayerCharacter> factory)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(factory==null)throw new System.ArgumentNullException(nameof(factory));
            if(Player!=null)return false;
            if(IsPresented)throw new System.InvalidOperationException("表示前に外界個体を初期化してください");
            var value=factory();if(value==null||value.IsDead)throw new System.ArgumentException("生存する外界個体が必要です");
            Player=value;playerFrame=Core.Frame;return true;
        }
        public void RecoverOutside(string pointId,bool relocate=true)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(Player==null)throw new System.InvalidOperationException("外界個体が未初期化です");
            long position=OutsideTrialRoute.ResolveRecovery(Core,pointId);
            if(relocate)Core.RestoreRecoveryPosition(position);Cards?.Reload();Core.CardGauge.RestoreFull();
            Player.ClearPoisons();Player.ResetParalysis();Player.ResetConfusion();Player.ResetBlindness();Player.ClearStagger();Player.ResetPoiseAccumulation();Player.Heal(Player.MaxHp);playerFrame=Core.Frame;
        }
        public bool AdvancePlayerFrame()
        {
            if(IsEnded||!IsPresented||Player==null||Core.Frame==playerFrame)return false;
            playerFrame=Core.Frame;var value=Player;
            value.BeginHpFrame();value.BeginParalysisFrame(true);value.BeginConfusionFrame(true);value.BeginBlindnessFrame(true);
            try{value.TickPoisons(0,true);value.TickProtection(0,true);value.TickInvincibleFrame();value.TickStagger(0,true);value.TickParalysis(0,true);value.TickConfusion(0,true);value.TickBlindness(0,true);value.TickPoiseRecovery(0,true);}
            finally{value.CommitHpFrame();value.EndParalysisFrame(true);value.EndConfusionFrame(true);value.EndBlindnessFrame(true);value.PublishHpFrame();}
            return true;
        }
        public OutsideExplorationCore Core {get;}=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,OutsideTraversalProfile.ValidationDash);
        public OutsideCameraPolicy Camera {get;private set;}=OutsideCameraPolicy.Follow;
        public OutsideExplorationCards Cards {get;private set;}
        // 入場前の初回登録だけ。再表示で再抽選せず、製品デッキの保存/切替は別接続とする。
        public bool TryInitializeCards(System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card> cards,System.Collections.Generic.IReadOnlyDictionary<(string Id,Olca.Core.Domain.ExceedLevel Level),int> owned,Olca.Core.Interfaces.IRandomProvider random,out string error)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(IsPresented||Cards!=null){error="探索デッキは既に登録済み、または表示中です";return false;}
            if(!OutsideExplorationCards.TryCreate(cards,owned,random,out var prepared,out error))return false;
            Cards=prepared;return true;
        }

        // マップ入場前に設定する。表示中の変更は画面の急な移動を防ぐため受理しない。
        public bool TryConfigureCamera(OutsideCameraPolicy policy)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(policy==null)throw new System.ArgumentNullException(nameof(policy));
            if(policy.FixedCenter.HasValue&&policy.FixedCenter.Value>Core.Definition.Width)throw new System.ArgumentOutOfRangeException(nameof(policy));
            if(IsPresented)return false;Camera=policy;return true;
        }
        // 試走設定は初回表示前だけ。製品の能力解放や保存には接続しない。
        public bool TryConfigureTraversal(OutsideTraversalProfile profile)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(profile==null)throw new System.ArgumentNullException(nameof(profile));
            if(IsPresented||Core.Frame!=0)return false;Core.ConfigureTraversal(profile);return true;
        }
        public bool IsEnded{get;private set;}
        public bool IsPresented=>current!=null;
        public PresentationLease AttachPresentation()
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            return current=new PresentationLease(this);
        }
        public void Dispose(){if(IsEnded)return;IsEnded=true;current?.Dispose();current=null;Player=null;}
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
    public sealed partial class OutsideExplorationCore
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
        // 全体時計と分け、停止へ切り替わった論理frameを必ず0として表示する。
        public long IdleFrame {get;private set;}
        private bool jumpWasHeld;
        private long remainder;
        // 検証用: 初速12単位/秒、重力24単位/秒²、解放時の上昇上限4単位/秒。
        // 60000固定小数点/60Hzのため、毎frameの値は12000/400/4000。製品値ではない。
        public const long ValidationJumpVelocity=12000,ValidationGravity=400,ValidationReleasedRise=4000;
        public const long ValidationMaximumRise=ValidationJumpVelocity*(ValidationJumpVelocity/ValidationGravity+1)/2;
        public OutsideExplorationCore(OutsideMovementDefinition definition,OutsideTraversalProfile traversal=null)
        {Definition=definition??throw new System.ArgumentNullException(nameof(definition));Position=definition.Start;ConfigureTraversal(traversal);}
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
        public void TickFrame(float horizontal,bool jumpDown=false,bool jumpHeld=false,bool dashDown=false,bool dashHeld=false)
        {
            if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new System.ArgumentOutOfRangeException(nameof(horizontal));
            bool wasIdle=IsIdleMotion;
            Frame++;CardGauge.AdvanceFrame();bool jumpEdge=jumpDown&&!jumpWasHeld;jumpWasHeld=jumpHeld;
            BeginTraversalFrame(dashDown,dashHeld);
            bool launch=jumpEdge&&IsGrounded;
            int horizontalDirection=horizontal>0?1:horizontal<0?-1:0;
            bool wallLaunch=TryWallKick(jumpEdge,horizontalDirection);
            if(launch){IsDashJumping=IsDashing;if(IsDashing)EndDash();SupportId=null;VerticalVelocity=ValidationJumpVelocity;}
            if(!jumpHeld&&VerticalVelocity>ValidationReleasedRise)VerticalVelocity=ValidationReleasedRise;
            long previous=Position,oldHeight=Height;
            int direction=TraversalDirection(horizontalDirection);long speed=TraversalSpeed;
            if(direction==0)remainder=0;
            else{
                Facing=direction;
                long tail=speed%LogicalFrameClock.FramesPerSecond*direction+remainder;
                long step=speed/LogicalFrameClock.FramesPerSecond*direction+tail/LogicalFrameClock.FramesPerSecond;
                remainder=tail%LogicalFrameClock.FramesPerSecond;
                if(direction>0&&step>=Definition.Width-Position){Position=Definition.Width;remainder=0;}
                else if(direction<0&&step<=-Position){Position=0;remainder=0;}
                else Position+=step;
            }
            ClampValidationWalls(previous,Height+VerticalVelocity);
            IsMoving=Position!=previous;MovementFrame=IsMoving?MovementFrame+1:0;
            if(!launch&&!wallLaunch&&IsGrounded){SupportId=SupportAt(Position,Height);if(IsGrounded){UpdateWallSlide(horizontalDirection);CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);return;}VerticalVelocity=0;}
            UpdateWallSlide(horizontalDirection);
            long next=Height+VerticalVelocity;
            if(VerticalVelocity<0&&Landing(previous,oldHeight,next,out var top,out var support)){Height=top;VerticalVelocity=0;SupportId=support;}
            else{Height=next;VerticalVelocity-=ValidationGravity;if(IsWallSliding)VerticalVelocity=System.Math.Max(VerticalVelocity,-TraversalProfile.WallSlidePerFrame);SupportId=null;}
            CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);
        }
        private void AdvanceIdleFrame(bool wasIdle)
        {IdleFrame=IsIdleMotion?wasIdle?IdleFrame+1:0:0;}
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>採用済み入力だけを受ける巡回選択。移動/ジャンプのneutral判定やゲーム時計は所有しない。</summary>
    public sealed class OutsideSelectionRepeat
    {
        public const int DefaultInitialDelayFrames=24;
        public const int DefaultRepeatFrames=9;
        private struct Sample {public long Time,PreviousAt,NextAt;public bool Previous,Next,PreviousDown,NextDown,Snapshot,AnyKey;}
        private readonly System.Collections.Generic.List<Sample> pending=new System.Collections.Generic.List<Sample>();
        private bool waitingForNeutral=true,waitingForResumeSample,previousHeld,nextHeld,blockedPrevious,blockedNext;
        private int repeatDirection,framesUntilRepeat=DefaultInitialDelayFrames;
        public void Reset(bool resumeHeld=false)
        {
            pending.Clear();
            waitingForNeutral=!resumeHeld;
            waitingForResumeSample=resumeHeld;
            previousHeld=nextHeld=blockedPrevious=blockedNext=false;
            repeatDirection=0;
            framesUntilRepeat=DefaultInitialDelayFrames;
        }
        public void Queue(long time,bool prev,bool next,bool prevDown,bool nextDown,bool snapshot,bool anyKey,long previousAt,long nextAt)
            =>pending.Add(new Sample{Time=time,Previous=prev,Next=next,PreviousDown=prevDown,NextDown=nextDown,Snapshot=snapshot,AnyKey=anyKey,PreviousAt=previousAt,NextAt=nextAt});
        private static void Move(OutsideExplorationCards cards,int delta){if(cards!=null)cards.TrySelect((cards.SelectedIndex+delta+cards.Hand.Count)%cards.Hand.Count);}
        private void UpdateHeld(Sample sample)
        {
            if(!sample.Previous)blockedPrevious=false;
            if(!sample.Next)blockedNext=false;
            if(sample.Snapshot&&!waitingForResumeSample){
                if(sample.Previous&&!previousHeld)blockedPrevious=true;
                if(sample.Next&&!nextHeld)blockedNext=true;
            }
            if(sample.PreviousDown)blockedPrevious=false;
            if(sample.NextDown)blockedNext=false;
            previousHeld=sample.Previous&&!blockedPrevious;
            nextHeld=sample.Next&&!blockedNext;
        }
        // 中立sample自身まで消費し、同じ時刻群に後続sampleが残る場合だけ通常処理へ進む。
        private bool ConsumeNeutralWait(ref int start,int end)
        {
            while(start<end){
                var sample=pending[start++];
                if(!sample.AnyKey){waitingForNeutral=false;previousHeld=nextHeld=false;break;}
            }
            return !waitingForNeutral&&start<end;
        }
        private void RestoreResumeHeld(Sample sample)
        {
            blockedPrevious=blockedNext=false;
            previousHeld=sample.Previous;
            nextHeld=sample.Next;
            repeatDirection=previousHeld&&nextHeld?(sample.PreviousAt>sample.NextAt?-1:1):nextHeld?1:previousHeld?-1:0;
            framesUntilRepeat=DefaultInitialDelayFrames;
            waitingForResumeSample=false;
        }
        private bool ApplyTimestampGroup(OutsideExplorationCards cards,int start,int end,bool allowActions)
        {
            bool previousPressed=false,nextPressed=false,overlappingHeld=false;
            for(int i=start;i<end;i++){
                previousPressed|=pending[i].PreviousDown;
                nextPressed|=pending[i].NextDown;
                overlappingHeld|=pending[i].Previous&&pending[i].Next;
            }
            if(!waitingForResumeSample&&previousPressed&&nextPressed&&overlappingHeld){
                for(int i=start;i<end;i++)UpdateHeld(pending[i]);
                if(allowActions)Move(cards,1);
                repeatDirection=nextHeld?1:previousHeld?-1:0;
                framesUntilRepeat=DefaultInitialDelayFrames;
                return true;
            }
            bool changed=false;
            for(int i=start;i<end;i++){
                var sample=pending[i];
                UpdateHeld(sample);
                if(sample.PreviousDown||sample.NextDown){
                    int press=sample.NextDown?1:-1;
                    if(allowActions)Move(cards,press);
                    repeatDirection=press<0?(previousHeld?-1:nextHeld?1:0):(nextHeld?1:previousHeld?-1:0);
                    framesUntilRepeat=DefaultInitialDelayFrames;
                    changed=true;
                }
                else if(repeatDirection<0&&!previousHeld||repeatDirection>0&&!nextHeld){
                    repeatDirection=nextHeld?1:previousHeld?-1:0;
                    framesUntilRepeat=DefaultInitialDelayFrames;
                    changed=true;
                }
            }
            return changed;
        }
        public void AdvanceFrame(OutsideExplorationCards cards,bool allowActions=true)
        {
            bool inputChangedThisFrame=false;
            for(int start=0;start<pending.Count;){
                int end=start+1;
                while(end<pending.Count&&pending[end].Time==pending[start].Time)end++;
                if(waitingForNeutral&&!ConsumeNeutralWait(ref start,end)){start=end;continue;}
                if(waitingForResumeSample&&start<end){
                    RestoreResumeHeld(pending[start++]);
                    inputChangedThisFrame=true;
                    if(start==end){start=end;continue;}
                }
                // 先の群ですでに変更済みでも、後続の全sampleを必ず適用する。
                bool groupChanged=ApplyTimestampGroup(cards,start,end,allowActions);
                inputChangedThisFrame|=groupChanged;
                start=end;
            }
            pending.Clear();
            if(!waitingForNeutral&&!waitingForResumeSample&&!inputChangedThisFrame&&repeatDirection!=0&&--framesUntilRepeat<=0){
                if(allowActions)Move(cards,repeatDirection);
                framesUntilRepeat=DefaultRepeatFrames;
            }
        }
    }
}
