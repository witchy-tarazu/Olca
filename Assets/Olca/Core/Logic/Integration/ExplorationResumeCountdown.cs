using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
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
        internal void Restore(int maximum,int recovery,long units){Validate(maximum,recovery);if(units<0||units>(long)maximum*UnitsPerPoint)throw new ArgumentException("外界ゲージ保存が不正です");Maximum=maximum;RecoveryPerSecond=recovery;CurrentUnits=units;}
        // 仕様で仮値が許可された最大量/回復速度。初期満量も検証マップだけの設定。
        public static OutsideCardGauge ValidationDefault=>new OutsideCardGauge(100,10,100);
    }
    /// <summary>探索専用デッキの初期手札。Dungeonとは個体もドロー履歴も共有しない。</summary>
    public sealed partial class OutsideExplorationCards
    {
        private readonly Olca.Core.Domain.Deck deck;
        public System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card> Hand {get;private set;}
        private readonly Olca.Core.Interfaces.IRandomProvider random;
        public long HandRevision {get;private set;}
        public void Reload(){deck.Shuffle(random);Hand=deck.DrawCards(3,random).AsReadOnly();SelectedIndex=0;HandRevision++;}
        public int SelectedIndex {get;private set;}
        public Olca.Core.Domain.Card SelectedCard=>SelectedIndex<Hand.Count?Hand[SelectedIndex]:null;
        public bool TrySelect(int index)
        {if(index<0||index>=Hand.Count)return false;SelectedIndex=index;return true;}
        /// <summary>発動が成立する境界だけから呼ぶ。効果/入力/永続保存は別の責務。</summary>
        public bool TryConsumeSelected(Olca.Core.Domain.Card expected,long expectedRevision,OutsideCardGauge gauge,out Olca.Core.Domain.Card consumed)
        {
            consumed=null;if(gauge==null)throw new ArgumentNullException(nameof(gauge));
            // 保存可能な外界専用乱数だけ。古いView/手札と不足コストは抽選前に拒否する。
            if(expected==null||expectedRevision!=HandRevision||!ReferenceEquals(expected,SelectedCard)||!(random is Olca.Core.Logic.Battle.SeededBattleRandom))return false;
            int cost=expected.GetFinalCost();if(cost<0||HandRevision==long.MaxValue||gauge.CurrentUnits<(long)cost*OutsideCardGauge.UnitsPerPoint)return false;
            long revision=checked(HandRevision+1);var next=Hand.ToList();next.RemoveAt(SelectedIndex);
            // 封じた専用RNGの同期1draw。ユーザーcallbackや効果処理を挟まない。
            next.AddRange(deck.DrawCards(1,random));if(!gauge.TrySpend(cost))throw new InvalidOperationException("消費境界でゲージが変更されました");
            Hand=next.AsReadOnly();HandRevision=revision;consumed=expected;return true;
        }
        internal int IndexOf(Card card)=>deck.GetAllCards().ToList().FindIndex(c=>ReferenceEquals(c,card));
        internal Card ActionCard(int index)=>deck.GetAllCards()[index];
        public int RemainingCount=>deck.RemainingCount;
        public int TotalCount=>deck.TotalCount;
        internal Olca.Core.Interfaces.IRandomProvider RandomProvider=>random;
        internal string SourceKey {get;private set;}
        internal OutsideExplorationCards(Olca.Core.Domain.Deck deck,Olca.Core.Interfaces.IRandomProvider random,string sourceKey=null)
        {this.deck=deck;this.random=random;SourceKey=sourceKey;Hand=deck.DrawCards(3,random).AsReadOnly();}
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
    public enum OutsideCardUseRestriction { None, EmptyHand, UnsupportedCard, UnsupportedLocation, InsufficientGauge, UnsupportedTrajectory }
    /// <summary>外界での開始可否だけを持つ試用登録。カード性能/hashやDungeonの設定は変更しない。</summary>
    public sealed class OutsideCardUseDefinition
    {
        public string CardId {get;}public ExceedLevel Level {get;}public bool CanUseInAir {get;}
        public OutsideCardUseDefinition(string cardId,ExceedLevel level,bool canUseInAir)
        {if(string.IsNullOrWhiteSpace(cardId)||!Enum.IsDefined(typeof(ExceedLevel),level))throw new ArgumentException("外界使用定義が不正です");CardId=cardId;Level=level;CanUseInAir=canUseInAir;}
        // 登録した既存6種と新近接2種・エアショットだけの暫定指定。未知ID/未登録レベルへ空中許可を自動付与しない。
        public static IReadOnlyList<OutsideCardUseDefinition> TrialDefinitions {get;}=Array.AsReadOnly(new[]{
            new OutsideCardUseDefinition("bomb",ExceedLevel.Normal,true),new OutsideCardUseDefinition("bomb",ExceedLevel.Lv1,true),new OutsideCardUseDefinition("bomb",ExceedLevel.Lv2,true),new OutsideCardUseDefinition("shield",ExceedLevel.Normal,true),new OutsideCardUseDefinition("heal",ExceedLevel.Normal,true),new OutsideCardUseDefinition("sword",ExceedLevel.Normal,true),
            new OutsideCardUseDefinition("fire_sword",ExceedLevel.Normal,true),new OutsideCardUseDefinition("sword",ExceedLevel.Lv1,true),new OutsideCardUseDefinition("sword",ExceedLevel.Lv2,true),new OutsideCardUseDefinition(TrialInitialCardDefinitions.AirShot,ExceedLevel.Normal,true),new OutsideCardUseDefinition(TrialInitialCardDefinitions.Spear,ExceedLevel.Normal,true),new OutsideCardUseDefinition(TrialInitialCardDefinitions.WideSword,ExceedLevel.Normal,true)});
        public static bool AllowsAir(Card card)=>card!=null&&TrialDefinitions.Any(d=>d.CardId==card.Id&&d.Level==card.ExceedLevel&&d.CanUseInAir);
    }

    /// <summary>Root所有の外界状態。現在は製品登録と分けた検証地形のみを扱う。</summary>
    public sealed partial class OutsideExplorationSession : System.IDisposable
    {
        private Olca.Core.Logic.Battle.AttackHitStopSettings hitStopSettings = new Olca.Core.Logic.Battle.AttackHitStopSettings();
        public int HitStopRemainingFrames { get; private set; }
        public bool IsHitStopped => HitStopRemainingFrames > 0;
        public void EnableTrialHitStop() => hitStopSettings = Olca.Core.Logic.Battle.AttackHitStopSettings.Provisional;
        private void RequestHitStop(int frames) { if (!IsEnded) HitStopRemainingFrames = Math.Max(HitStopRemainingFrames, frames); }
        /// <summary>停止残数だけ進める。呼出側はポーズ中に呼ばず、同frameの入力を実行しない。</summary>
        public bool AdvanceHitStopFrame()
        {
            if (IsEnded || !IsPresented || !IsHitStopped) return false;
            HitStopRemainingFrames--; return true;
        }
        private readonly bool trialContactDamageEnabled;
        // 通常Rootでのみ明示登録する試用接触。既存の純粋Session fixtureへ自動追加しない。
        private readonly bool trialSafeHomePlacement;
        // 通常Rootの試用配置だけを分離し、旧fixture/既存攻撃定義の座標と保存keyは維持する。
        public OutsideExplorationSession(bool trialContactDamage = false,bool trialSafeHomePlacement = false){trialContactDamageEnabled=trialContactDamage;this.trialSafeHomePlacement=trialSafeHomePlacement;}
        private long trialContactNextFrame,secondTrialContactNextFrame;
        public const int TrialContactDamage=10,TrialContactInterval=60;
        public const long TrialContactHalfWidth=24000,TrialContactHeight=90000;
        private void AdvanceTrialContactDamage()
        {
            if(trialContactNextFrame<=Core.Frame)trialContactNextFrame=0;
            if(secondTrialContactNextFrame<=Core.Frame)secondTrialContactNextFrame=0;
            if(!trialContactDamageEnabled||Core.Definition.Id!=OutsideKickTrialDefinition.MapId||!OutsideTrialRoute.KnownMap(Core.TrialMapId)||Player.IsDead||Core.HasFallenOut||Core.TraversalProfile==null)return;
            if(flyingTrial454==null)ApplyTrialContact(FirstTrialTargetX,TrialTarget,ref trialContactNextFrame,FirstTrialTargetHeight);
            ApplyTrialContact(SecondTrialTargetX,SecondTrialTarget,ref secondTrialContactNextFrame);
        }
        private void ApplyTrialContact(long x,Enemy enemy,ref long nextFrame,long y=0)
        {
            var profile=Core.TraversalProfile;
            if(enemy.IsDead||nextFrame!=0||Math.Abs(Core.Position-x)>TrialContactHalfWidth+profile.BodyHalfWidth||Core.Height>y+TrialContactHeight||Core.Height+profile.BodyHeight<y)return;
            // 防御で防いだ接触も敵ごとに次の判定まで待つ。別の敵の間隔は流用しない。
            nextFrame=checked(Core.Frame+TrialContactInterval);
            // 試用値: 強靭削り/無敵付与なし。既存無敵・耐性・バリア等は共通被弾APIで判定する。
            Player.TryTakeDamage(TrialContactDamage,affectsPoise:false,attackElement:ElementType.None,poiseDamage:0);
        }
        private OutsideTrialContactData CaptureTrialContact()
        {
            long first=trialContactNextFrame>Core.Frame?trialContactNextFrame:0,second=secondTrialContactNextFrame>Core.Frame?secondTrialContactNextFrame:0;
            return first==0&&second==0?null:new OutsideTrialContactData{NextFrame=first,SecondNextFrame=second};
        }

        public sealed class PresentationLease : System.IDisposable
        {
            private OutsideExplorationSession owner;
            internal PresentationLease(OutsideExplorationSession owner){this.owner=owner;}
            public OutsideExplorationCore Core=>owner?.Core;
            public Olca.Core.Domain.PlayerCharacter Player=>owner?.Player;
            public OutsideCameraPolicy Camera=>owner?.Camera;
            public OutsideExplorationCards Cards=>owner?.Cards;
            public string MapName=>IsCurrent?owner.MapName:null;
            public int PairStage445=>IsCurrent?owner.PairStage445:0;
            public bool IsForest460=>IsCurrent&&owner.IsForest460;
            public double MapNameOpacity=>IsCurrent?owner.MapNameOpacity:0;
            public bool ShouldShowBreath=>IsCurrent&&owner.ShouldShowBreath;
            public int BreathFramesRemaining=>owner?.BreathFramesRemaining??0;public int BreathMaximumFrames=>owner?.BreathMaximumFrames??0;
            public bool IsDeckEditingLocation=>owner?.IsDeckEditingLocation==true;
            public bool IsHitStopped=>owner?.IsHitStopped==true;
            public OutsideKickImpactVisual KickImpact(int target)=>IsCurrent&&target>=0&&target<2?owner.kickImpacts[target]:null;
            public long FirstTargetX=>owner?.FirstTrialTargetX??0;public long SecondTargetX=>owner?.SecondTrialTargetX??0;public Enemy TrialTarget=>owner?.TrialTarget;public Enemy SecondTrialTarget=>owner?.SecondTrialTarget;public OutsideTrialStrike TrialStrike=>owner?.TrialStrike;public IReadOnlyList<OutsideInitialShotData> InitialShots=>owner?.InitialShots??Array.Empty<OutsideInitialShotData>();
            public IReadOnlyList<OutsideBombFlightData> Bombs=>IsCurrent?owner.Bombs:Array.Empty<OutsideBombFlightData>();
            public IReadOnlyList<OutsideBombBlastData> BombBlasts=>IsCurrent?owner.BombBlasts:Array.Empty<OutsideBombBlastData>();
            // 投擲表示は既存行動時計と非保存の発射通知を読む。発射計画を複製・変更しない。
            public long? FirstSwordBlockingWall(long x,long y,int facing,long reach)=>IsCurrent?owner.FirstSwordBlockingWall(x,y,facing,reach):null;
            public bool IsSwingingSword=>IsCurrent&&owner.attackState?.SwordMotionId==OutsideCardPolishTrial.SwordMotionId;
            public string SwordSwingCardId=>IsSwingingSword?owner.Cards.ActionCard(owner.attackState.CardIndex).Id:null;
            public long SwordSwingElapsed=>IsSwingingSword?owner.Core.Frame-owner.attackState.StartedAt:-1;
            public int SwordSwingStartup=>IsSwingingSword?owner.attackState.StartupFrames:0;
            public int SwordSwingRecovery=>IsSwingingSword?owner.attackState.RecoveryFrames:0;
            public long SwordSwingReach=>IsSwingingSword?(owner.Cards.ActionCard(owner.attackState.CardIndex).Id==TrialInitialCardDefinitions.Spear?OutsideCardPolishTrial.SwordReach*3/2:OutsideCardPolishTrial.SwordReach):0;
            public float SwordSwingVerticalScale=>IsSwingingSword&&owner.Cards.ActionCard(owner.attackState.CardIndex).Id==TrialInitialCardDefinitions.WideSword?1.5f:1f;
            // 発射姿勢は保存済み行動の時計を読む。飛翔弾の寿命では身体を拘束しない。
            public bool IsFiringShot=>IsCurrent&&owner.attackState!=null&&ShotDefinition(owner.Cards.ActionCard(owner.attackState.CardIndex));
            public long ShotFiringElapsed=>IsFiringShot?owner.Core.Frame-owner.attackState.StartedAt:-1;
            public int ShotFiringStartup=>IsFiringShot?owner.attackState.StartupFrames:0;
            public int ShotFiringRecovery=>IsFiringShot?owner.attackState.RecoveryFrames:0;
            public bool IsThrowingBomb=>IsCurrent&&owner.attackState?.BombLaunch!=null;
            public long BombThrowElapsedFrames=>IsThrowingBomb?owner.Core.Frame-owner.attackState.StartedAt:-1;
            public int BombThrowStartupFrames=>IsThrowingBomb?owner.attackState.StartupFrames:0;
            public int BombThrowRecoveryFrames=>IsThrowingBomb?owner.attackState.RecoveryFrames:0;
            public int BombThrowFacing=>IsThrowingBomb?owner.attackState.BombLaunch.Facing:IsCurrent?owner.instantBombFacing:1;
            public bool HasAttackAction=>IsCurrent&&owner.IsAttacking;
            public long InstantBombReleasedAt=>IsCurrent?owner.instantBombReleasedAt:-1;
            public bool InstantBombWasAirborne=>IsCurrent&&owner.instantBombAirborne;
            public OutsideCardUseRestriction SelectedCardRestriction=>IsCurrent?owner.SelectedCardRestriction:OutsideCardUseRestriction.None;
            public bool IsDashAttacking=>IsCurrent&&owner.normalKick?.IsDashAttack==true;
            public OutsideKickKind? NormalKickKind=>IsCurrent&&owner.normalKick?.IsActive==true?owner.normalKick.Kind:(OutsideKickKind?)null;
            public bool IsKickHitWindow=>IsCurrent&&owner.normalKick?.IsHitWindow==true;
            public bool IsKickRecovery=>IsCurrent&&owner.normalKick?.IsRecovery==true;
            public bool IsChargedKick=>IsCurrent&&owner.normalKick?.IsCharged==true;
            public bool IsCharging=>IsCurrent&&owner.IsCharging;
            public int ChargeFrames=>IsCurrent?owner.ChargeFrames:0;
            public int ChargeRequiredFrames=>IsCurrent?owner.ChargeRequiredFrames:60;
            public OutsideKickTiming KickTiming=>IsCurrent?owner.normalKick?.ActiveTiming:null;
            public int KickElapsedFrames=>IsCurrent?owner.normalKick?.ElapsedFrames??-1:-1;
            public int? NormalKickFacing=>IsCurrent&&owner.normalKick?.IsActive==true?owner.normalKick.AttackFacing:(int?)null;
            public string CardUseMessage=>owner?.CardUseMessage; public long CardUseMessageFrame=>owner?.CardUseMessageFrame??-1;
            public int TrialBarrierHp=>owner?.Player?.BarrierHp??0;
            public bool TrialContactDamageEnabled=>IsCurrent&&owner.trialContactDamageEnabled;
            public OutsideFlyingTrial454 FlyingTrial454=>IsCurrent?owner.flyingTrial454?.Copy():null;
            public OutsideSlimeTrialData SlimeTrial=>IsCurrent?owner.slimeTrial?.Copy():null;
            public long FirstTargetHeight=>IsCurrent?owner.FirstTrialTargetHeight:0;
            public bool StartSlimeTrial()=>IsCurrent&&owner.StartSlimeTrial();
            public OutsideGoblinTrialData GoblinTrial=>IsCurrent?owner.goblinTrial?.Copy():null;
            public bool StartGoblinTrial()=>IsCurrent&&owner.StartGoblinTrial();
            public bool TrialEnemyAttackEnabled=>IsCurrent&&owner.trialEnemyAttack!=null;
            public bool TrialEnemyHitInvincibilityEnabled=>IsCurrent&&owner.TrialEnemyHitInvincibilityEnabled;
            public bool ToggleTrialEnemyHitInvincibility()=>IsCurrent&&owner.SetTrialEnemyAttack(!owner.TrialEnemyHitInvincibilityEnabled,true,true);
            public int TrialEnemyWindupFrames=>IsCurrent&&owner.trialEnemyAttack!=null?(int)Math.Max(0,owner.trialEnemyAttack.NextFrame-owner.Core.Frame):0;
            public bool ToggleTrialEnemyAttack()=>IsCurrent&&owner.SetTrialEnemyAttack(owner.trialEnemyAttack==null,true);
            public bool IsCurrent=>owner!=null&&!owner.IsEnded&&ReferenceEquals(owner.current,this);
            public void Dispose(){var prior=owner;owner=null;if(prior!=null&&ReferenceEquals(prior.current,this)){prior.current=null;prior.CancelAttackReservation();prior.CancelChargeInput();prior.ClearBombReleaseVisual();}}
        }
        private PresentationLease current;
        // 外界だけが所有する個体。DungeonのPlayerやViewへ所有権を渡さない。
        private PlayerCharacter player;
        public Olca.Core.Domain.PlayerCharacter Player {
            get => player;
            private set {
                if (player != null) player.OnHitStopRequested -= RequestHitStop;
                player = value;
                if (player != null) player.OnHitStopRequested += RequestHitStop;
            }
        }
        private long playerFrame;
        public string CardUseMessage {get;private set;}
        public long CardUseMessageFrame {get;private set;}=-1;
        private long lastCardUseAttempt=-1;
        // 終盤にタイマーが0になっても、そのframe序盤の行動禁止を入力段階まで維持する。
        private long bodyPreventedFrame=-1;
        private OutsideAttackStateData attackState;
        // 瞬時発動の表示用通知。セーブ/判定へ含めず、再入・復元時には再演しない。
        private long instantBombReleasedAt=-1;private int instantBombFacing=1;private bool instantBombAirborne;
        private void ClearBombReleaseVisual(){instantBombReleasedAt=-1;instantBombFacing=1;instantBombAirborne=false;}
        // 試用別マップの射撃確認用配置。正式敵配置ではなく、既存壁24worldの前後へ分離する。
        public long FirstTrialTargetHeight=>flyingTrial454?.Height??slimeTrial?.Height??0;
        public long FirstTrialTargetX=>flyingTrial454?.X??slimeTrial?.X??goblinTrial?.X??(Core.TrialMapId==OutsideTrialRoute.AnnexMapId?1350000:TrialTargetX);
        public long SecondTrialTargetX=>PairStage445>0?31*60000:Core.TrialMapId==OutsideTrialRoute.AnnexMapId?1560000:trialSafeHomePlacement?14*60000:OutsideKickTrialDefinition.SecondTargetX;
        public const long TrialTargetX=18*OutsideMovementDefinition.UnitsPerWorldUnit,TrialSwordRange=90000;
        public const int TrialTargetMaximum=120;
        // 受理済みHP減少だけの表示通知。保存・判定・入力へ含めない。
        private readonly OutsideKickImpactVisual[] kickImpacts=new OutsideKickImpactVisual[2];
        private Enemy trialTarget;private bool trialTargetChanged;private int trialTargetHits;private OutsideTrialStrike trialStrike;
        public Enemy TrialTarget=>trialTarget??(trialTarget=CreateTrialTarget(null));
        public OutsideTrialStrike TrialStrike=>trialStrike?.Copy();
        private Enemy secondTrialTarget;private int secondTrialHits;
        public Enemy SecondTrialTarget=>secondTrialTarget??(secondTrialTarget=CreateSecondTrialTarget(null));
        private Enemy CreateSecondTrialTarget(OutsideTrialTargetData data){var e=CreateTrialTarget(data);return e;}
        private OutsideTrialTargetData CaptureSecondTrialTarget()=>secondTrialTarget!=null&&(secondTrialTarget.CurrentHp!=TrialTargetMaximum||secondTrialHits!=0)?new OutsideTrialTargetData{Id="trial-kick-target-v1",Hp=secondTrialTarget.CurrentHp,Hits=secondTrialHits}:null;
        private void AdvanceTrialKickHits()
        {
            if(normalKick?.IsHitWindow!=true||Core.Definition.Id!="validation-landing-32")return;
            void Hit(int id,long x,Enemy enemy){
                long relativeHeight=Core.Height-(id==0?FirstTrialTargetHeight:0);var charge=normalKick.ActiveChargePlan;bool contains=charge!=null?charge.Contains(Core.Position,relativeHeight,normalKick.AttackFacing,x):OutsideKickTrialDefinition.Contains(normalKick.Kind,Core.Position,relativeHeight,normalKick.AttackFacing,x);
                // 別マップは剣と同じ試用壁で遮る。遮蔽された敵の命中済み印は消費しない。
                long distance=(x-Core.Position)*normalKick.AttackFacing;
                if(enemy.IsDead||!contains||FirstSwordBlockingWall(Core.Position,Core.Height,normalKick.AttackFacing,distance).HasValue||!normalKick.TryMarkHit(id))return;
                int hpBefore=enemy.CurrentHp;
                enemy.TakeDamage(normalKick.ActiveChargePlan?.Power??OutsideKickTrialDefinition.DamageFor(normalKick.Kind),normalKick.ActiveChargePlan?.Element??(Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id?ElementType.Fire:ElementType.None),
                    poiseDamage: normalKick.ActiveChargePlan?.PoiseDamage,hitStopFrames: normalKick.IsDashAttack||normalKick.IsCharged ? hitStopSettings.HeavyFrames : hitStopSettings.NormalFrames); // カエンの外界キックも明示試用。
                if(enemy.CurrentHp<hpBefore)kickImpacts[id]=new OutsideKickImpactVisual(Core.Frame,x,id==0?FirstTrialTargetHeight:0,Core.Position,Core.Height,normalKick.AttackFacing);
                if(id==0){trialTargetChanged=true;trialTargetHits=(int)Math.Min(int.MaxValue,(long)trialTargetHits+1);}else secondTrialHits=(int)Math.Min(int.MaxValue,(long)secondTrialHits+1);
            }
            Hit(0,FirstTrialTargetX,TrialTarget);Hit(1,SecondTrialTargetX,SecondTrialTarget);
        }
        private Enemy CreateTrialTarget(OutsideTrialTargetData data){var e=new Enemy("grunt","シヨウ ヒョウテキ（カリ）",TrialTargetMaximum,ElementType.None,new Olca.Core.Domain.Vector2Int(0,0));if(data!=null)e.RestoreCheckpointHp(data.Hp);e.OnHitStopRequested+=RequestHitStop;return e;}
        // 試用標的1だけの任意AI。正式な敵性能・索敵・地形配置ではない。
        private OutsideTrialEnemyAttackData trialEnemyAttack;
        private OutsideKnockbackStateData knockback;
        public int KnockbackFramesRemaining=>knockback?.FramesRemaining??0;
        public const int TrialEnemyDamage=10,TrialEnemyPoiseDamage=10,TrialEnemyInterval=90,TrialEnemyInitialDelay=60;
        public bool SetTrialEnemyAttack(bool enabled,bool affectsPoise=false,bool hitInvincibilityTrial=false)
        {
            if(IsEnded||!IsPresented||Player==null||Player.IsDead||Core.Definition.Id!="validation-landing-32"||!OutsideTrialRoute.KnownMap(Core.TrialMapId)||enabled&&TrialTarget.IsDead)return false;
            if(goblinTrial!=null||slimeTrial!=null||hitInvincibilityTrial&&!affectsPoise)return false;
            // 既存のON/OFF契約は維持。V3への明示切替だけが既存ONを置換できる。
            if(enabled==(trialEnemyAttack!=null)&&(!enabled||!hitInvincibilityTrial||TrialEnemyHitInvincibilityEnabled))return false;
            trialEnemyAttack=enabled?new OutsideTrialEnemyAttackData{DefinitionId=hitInvincibilityTrial?OutsideTrialEnemyAttackData.V3DefinitionId:affectsPoise?"trial-shield-enemy-v2":"trial-shield-enemy-v1",NextFrame=checked(Core.Frame+TrialEnemyInitialDelay)}:null;return true;
        }
        public bool TrialEnemyHitInvincibilityEnabled=>trialEnemyAttack?.DefinitionId==OutsideTrialEnemyAttackData.V3DefinitionId;
        private OutsideKnockbackStateData AdvanceTrialEnemyAttack()
        {
            if(flyingTrial454!=null){ObserveFlyingDeath454();return ResolveFlyingLanding454();}
            if(slimeTrial!=null){ObserveSlimeDeath();return ResolveSlimeLanding();}
            if(goblinTrial!=null){if(TrialTarget.IsDead){goblinTrial.Phase=OutsideGoblinPhase.Dead;goblinTrial.Age=0;pendingGoblinStrike=false;}return ResolveGoblinStrike();}
            if(trialEnemyAttack==null)return null;
            if(TrialTarget.IsDead||Player.IsDead||Core.HasFallenOut){trialEnemyAttack=null;return null;}
            if(Core.Frame<trialEnemyAttack.NextFrame)return null;
            trialEnemyAttack.NextFrame=checked(Core.Frame+TrialEnemyInterval);
            // 別マップも可視の標的位置を参照し、登録済みの試用壁で近接を遮る。
            long distance=Math.Abs(Core.Position-FirstTrialTargetX);int facing=Core.Position<FirstTrialTargetX?-1:1;
            bool v3=TrialEnemyHitInvincibilityEnabled,v2=trialEnemyAttack.DefinitionId=="trial-shield-enemy-v2";
            if(distance>90000||Core.Height<0||Core.Height>90000||FirstSwordBlockingWall(FirstTrialTargetX,0,facing,distance).HasValue)return null;
            bool accepted=v3?Player.TryTakeOutsideHit(TrialEnemyDamage,ElementType.None,TrialEnemyPoiseDamage,OutsideHitInvincibilityData.TrialDurationMs):Player.TryTakeDamage(TrialEnemyDamage,attackElement:ElementType.None,affectsPoise:v2,poiseDamage:TrialEnemyPoiseDamage);
            return accepted&&(v2||v3)?OutsideTrialEnemyAttackData.V2Knockback.Start(facing):null; // V1と完全防御は従来どおり移動なし。
        }
        private static bool ShieldDefinition(Card c)=>c!=null&&c.AttackPowerBasisPoints==0&&c.Id=="shield"&&c.Category==CardCategory.Defense&&c.ExceedLevel==ExceedLevel.Normal&&c.Power==15&&c.GetFinalPower()==15&&c.SubCategory==CardSubCategory.Barrier&&c.Modifier.AdditionalEffects.Count==0;
        private static bool HealDefinition(Card c)=>c!=null&&c.AttackPowerBasisPoints==0&&c.Id=="heal"&&c.Category==CardCategory.Defense&&c.SubCategory==CardSubCategory.Heal&&c.ExceedLevel==ExceedLevel.Normal&&c.Power>=0;
        private static bool InitialDefinition(Card c)=>TrialInitialCardDefinitions.IsRegistered(c)&&c.Modifier.AdditionalEffects.Count==0&&c.Modifier.ElementOverride==null;
        private static bool ShotDefinition(Card c)=>InitialDefinition(c)&&TrialInitialCardDefinitions.IsShot(c.Id);
        private static bool SwordDefinition(Card c)=>(InitialDefinition(c)&&!TrialInitialCardDefinitions.IsShot(c.Id))||c!=null&&c.AttackPowerBasisPoints==0&&c.Category==CardCategory.Attack&&c.Series==CardSeries.Sword&&((c.Id=="sword"&&(c.ExceedLevel==ExceedLevel.Normal||c.ExceedLevel==ExceedLevel.Lv1||c.ExceedLevel==ExceedLevel.Lv2))||(c.Id=="fire_sword"&&c.Element==ElementType.Fire&&c.ExceedLevel==ExceedLevel.Normal))&&c.Power>=0&&c.Modifier.AdditionalEffects.Count==0&&c.MoveMotionTime==0&&c.AttackMotionTime==0&&c.HitInvincibilityMs==0;
        private OutsideTrialTargetData CaptureTrialTarget()=>trialTargetChanged||trialTarget!=null&&trialTarget.CurrentHp!=TrialTargetMaximum?new OutsideTrialTargetData{Hp=TrialTarget.CurrentHp,Hits=trialTargetHits}:null;
        // 静止標的に対する試用の縦範囲。正式な敵の当たり形状ではない。
        public const long TrialSwordVerticalRange=90000;
        // 命中と表示が同じ閉区間を読む試用壁query。状態・予約・カードは変更しない。
        private long? FirstSwordBlockingWall(long x,long y,int facing,long reach)
        {
            if(IsEnded||Core.TrialMapId!=OutsideTrialRoute.AnnexMapId||x<0||x>Core.Definition.Width||reach<0||facing!=1&&facing!=-1)return null;
            long? first=null;long nearest=long.MaxValue;decimal height=(decimal)y+72000;
            foreach(var wall in Core.TraversalProfile?.Walls??Array.Empty<OutsideValidationWall>()){
                long distance=(wall.X-x)*facing;if(distance<0||distance>reach||height<wall.Bottom||height>wall.Top)continue;
                if(distance<nearest){first=wall.X;nearest=distance;}
            }
            return first;
        }
        private void ApplyTrialSword(Card card,int? resolvedPower=null){
            bool polished=attackState?.SwordMotionId==OutsideCardPolishTrial.SwordMotionId;
            long baseReach=polished?OutsideCardPolishTrial.SwordReach:TrialSwordRange;
            long reach=card.Id==TrialInitialCardDefinitions.Spear?baseReach*3/2:baseReach;
            long verticalRange=card.Id==TrialInitialCardDefinitions.WideSword?TrialSwordVerticalRange*3/2:TrialSwordVerticalRange;
            bool vertical=Core.IsGrounded||(OutsideCardUseDefinition.AllowsAir(card)&&Core.Height>=0&&Core.Height<=verticalRange);
            // 別マップは直進弾と同じ試用の垂直壁・高さ1.2worldで遮蔽する。床の厚みや正式物理は追加しない。
            bool Blocked(long x)=>FirstSwordBlockingWall(Core.Position,Core.Height,Core.Facing,(x-Core.Position)*Core.Facing).HasValue;
            bool InRange(long x,Enemy enemy,long y=0){long dx=(x-Core.Position)*Core.Facing;bool targetVertical=y==0?vertical:Math.Abs(Core.Height-y)<=verticalRange;return targetVertical&&!enemy.IsDead&&dx>=0&&dx<=reach&&!Blocked(x);}
            // 単体命中と従来の第一標的優先を保持する。描画の生成順に依存させない。
            Enemy target=null;bool second=false;
            if(InRange(FirstTrialTargetX,TrialTarget,FirstTrialTargetHeight))target=TrialTarget;
            else if(InRange(SecondTrialTargetX,SecondTrialTarget)){target=SecondTrialTarget;second=true;}
            trialStrike=new OutsideTrialStrike{CardId=card.Id,Frame=Core.Frame,X=Core.Position,Y=Core.Height,Facing=Core.Facing,Hit=target!=null,Reach=reach,SpriteSwing=polished,VerticalScale=card.Id==TrialInitialCardDefinitions.WideSword?1.5f:1f};
            if(target!=null){
                if(second)secondTrialHits=(int)Math.Min(int.MaxValue,(long)secondTrialHits+1);
                else{trialTargetChanged=true;trialTargetHits=(int)Math.Min(int.MaxValue,(long)trialTargetHits+1);}
                target.TakeDamage(resolvedPower??card.GetFinalPower(),card.GetFinalElement(),card.Properties,card.PoiseDamage,card.HitInvincibilityMs,hitStopFrames:hitStopSettings.CardFrames(card));
            }
        }
        private readonly Olca.Core.Logic.Battle.AttackInputReservation attackReservation=new Olca.Core.Logic.Battle.AttackInputReservation();
        private OutsideNormalKickSequence normalKick;
        private readonly OutsideChargeHoldInput chargeInput=new OutsideChargeHoldInput();
        private long lastProductAttackInputFrame=-1;
        public int ChargeFrames=>chargeInput.Frames;
        public int ChargeRequiredFrames=>OutsideChargeKickData.RequiredFor(Player);
        public bool IsCharging=>IsPresented&&BodyCanAttack&&!IsAttacking&&chargeInput.IsAccumulating;
        public void CancelChargeInput()=>chargeInput.Reset();
        public OutsideNormalKickSequence NormalKick {get{if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));return normalKick??(normalKick=new OutsideNormalKickSequence(attackReservation));}}
        /// <summary>試用時間を登録。威力・範囲・共通本体の試作は正式定義と区別する。</summary>
        public void EnableTrialNormalKick()
        {
            if(!OutsideTrialRoute.KnownMap(Core.TrialMapId)||Core.Definition.Id!=OutsideKickTrialDefinition.MapId||NormalKick.IsConfigured)return;
            NormalKick.Configure(OutsideKickTrialDefinition.Timings());NormalKick.ConfigureDash(OutsideKickTrialDefinition.DashTiming());
        }
        // カードは従来どおり移動不可。空中蹴りは移動/落下を保ち、地上蹴りの後隙はジャンプで中断しない。
        public bool AllowsTraversalInput=>attackState==null;
        public void TickTraversal(float horizontal,bool jumpDown=false,bool jumpHeld=false,bool dashDown=false,bool dashHeld=false)
        {
            if(IsEnded||!IsPresented)throw new InvalidOperationException("表示中のSessionが必要です");
            if(AdvanceHitStopFrame())return;
            ObserveNormalKick(BodyCanAttack);
            int facing=Core.Facing;bool grounded=Core.IsGrounded;
            bool kick=normalKick?.IsActive==true;
            // Coreは次のframeでジャンプを実行する。後隙の初frameから抑止し、終了frameでは解除する。
            bool blockRecoveryJump=normalKick?.LocksGroundMotion==true&&
                Core.Frame-normalKick.StartedAt+1>=normalKick.ActiveTiming.StartupFrames+(long)normalKick.ActiveTiming.ActiveFrames&&
                Core.Frame+1<normalKick.EndsAt;
            if(knockback!=null&&(!Player.IsStaggered||Player.IsDead||Core.HasFallenOut))knockback=null;
            if(knockback!=null){
                Core.TickKnockbackFrame(knockback.Direction*knockback.UnitsPerFrame,jumpHeld,dashHeld);
                if(--knockback.FramesRemaining==0)knockback=null;
            }else if(normalKick?.IsDashAttack==true){
                if(Core.Frame+1>=normalKick.EndsAt)Core.TickAfterDashAttackFrame(horizontal,jumpDown,jumpHeld,dashDown,dashHeld,IsMovementLockedForNextFrame);
                else Core.TickDashAttackFrame(normalKick.AttackFacing,jumpHeld,IsMovementLockedForNextFrame);
            }else Core.TickFrame(normalKick?.ResolveHorizontal(horizontal)??horizontal,!blockRecoveryJump&&jumpDown,jumpHeld,!kick&&dashDown,!kick&&dashHeld,IsMovementLockedForNextFrame);
            ObserveAttackBoundary(facing,grounded);AdvanceDeckLocation();AdvancePlayerFrame();
        }
        private void ObserveNormalKick(bool allowed){bool active=normalKick?.IsActive==true,dash=normalKick?.IsDashAttack==true;normalKick?.Observe(Core.Frame,Core.IsGrounded,Core.Facing,allowed);if(dash&&normalKick?.IsDashAttack!=true)Core.EndDashAfterAttack();if(active&&normalKick?.IsActive!=true)Core.RestartIdleMotion();}
        private Card reservedCard;private OutsideExplorationCards reservedOwner;private long reservedRevision,reservedActionFrame,lastAttackInputFrame=-1;
        public Olca.Core.Logic.Battle.AttackReservationKind PendingAttack=>attackReservation.Kind;
        public void CancelAttackReservation(){attackReservation.Cancel();reservedCard=null;reservedOwner=null;}
        public void ObserveAttackBoundary(int facing,bool grounded){if(Core.Facing!=facing||Core.IsGrounded!=grounded)CancelAttackReservation();}
        // 別マップの登録済み標的には剣・直進弾を許可。ボムと溜め攻撃は未登録。
        private static bool SupportsCardLocation(Card card,string mapId)=>mapId==null||mapId==OutsideTrialRoute.AnnexMapId&&(HealDefinition(card)||ShieldDefinition(card)||ShotDefinition(card)||SwordDefinition(card)||BombDefinition(card));
        private bool SupportedCard(Card card)=>SupportsCardLocation(card,Core.TrialMapId)&&Core.Definition.Id=="validation-landing-32"&&(HealDefinition(card)||SwordDefinition(card)||ShieldDefinition(card)||ShotDefinition(card)||BombDefinition(card))&&(Core.IsGrounded||OutsideCardUseDefinition.AllowsAir(card))&&(!BombDefinition(card)||CanPlanBomb());
        // 使用受付ではない読取案内。予約/前後隙を無視して「使用可能」と断言しない。
        public OutsideCardUseRestriction SelectedCardRestriction
        {
            get{
                var card=Cards?.SelectedCard;if(card==null)return OutsideCardUseRestriction.EmptyHand;
                if(!(HealDefinition(card)||SwordDefinition(card)||ShieldDefinition(card)||ShotDefinition(card)||BombDefinition(card)))return OutsideCardUseRestriction.UnsupportedCard;
                if(BombDefinition(card)&&Core.IsGrounded&&!CanPlanBomb())return OutsideCardUseRestriction.UnsupportedTrajectory;
                if(!SupportedCard(card))return OutsideCardUseRestriction.UnsupportedLocation;
                return Core.CardGauge.CurrentUnits<(long)card.GetFinalCost()*OutsideCardGauge.UnitsPerPoint?OutsideCardUseRestriction.InsufficientGauge:OutsideCardUseRestriction.None;
            }
        }
        private bool CanCancelAttack()=>attackState!=null&&attackState.EffectApplied&&Cards.ActionCard(attackState.CardIndex).RecoveryCancelAfterFrames.HasValue&&Core.Frame-attackState.StartedAt>=attackState.StartupFrames+(long)Cards.ActionCard(attackState.CardIndex).RecoveryCancelAfterFrames.Value;
        // 予約入力は保存しない。Sessionの行動開始と同じ境界で最後に再検証する。
        public bool ProcessCardUseInput(bool requested,bool selectionChanged,bool basicRequested,long cardAt,long basicAt,out string error,int vertical=0,bool deferBasicReservation=false)
        {
            error=null;if(IsHitStopped)return false;if(!OutsideTrialRoute.KnownMap(Core.TrialMapId)){CancelAttackReservation();return false;}if(lastAttackInputFrame==Core.Frame)return false;lastAttackInputFrame=Core.Frame;
            attackReservation.Observe(Core.Frame);if(attackReservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.None&&reservedCard!=null)CancelAttackReservation();
            if(selectionChanged||!IsPresented||IsDeckEditingLocation||!BodyCanAttack){CancelAttackReservation();return false;}
            if(reservedCard!=null&&(!ReferenceEquals(reservedOwner,Cards)||!ReferenceEquals(reservedCard,Cards?.SelectedCard)||reservedRevision!=Cards.HandRevision||attackState!=null&&reservedActionFrame!=attackState.StartedAt))CancelAttackReservation();
            // 同frameで時刻順不明はカード優先。後入力が確定した蹴りは共有一枠を上書きする。
            if(basicRequested&&(!requested||cardAt!=0&&basicAt!=0&&basicAt>cardAt)){
                if(normalKick?.IsConfigured!=true){CancelAttackReservation();error="固有攻撃は未接続です";return false;}
                CancelAttackReservation();
                if(Core.IsDashing&&!HasRegisteredDash(Player)){error="この神依のダッシュ攻撃は未登録です";return false;}
                bool started=normalKick.TryRequest(Core.Frame,Core.IsGrounded,Core.Facing,vertical,BodyCanAttack,Core.IsDashing,attackState!=null&&!CanCancelAttack(),true);
                if(started){attackState=null;reservedCard=null;reservedOwner=null;KickFeedback();}return started;
            }
            if(requested){
                CancelAttackReservation();var card=Cards?.SelectedCard;
                if(!SupportedCard(card)){return TryUseSelectedCard(card,Cards?.HandRevision??0,out error);}
                if(Core.CardGauge.CurrentUnits<(long)card.GetFinalCost()*OutsideCardGauge.UnitsPerPoint){error="カードゲージが不足しています";return false;}
                if(IsAttacking&&!CanCancelAnyAttack()){attackReservation.Request(Olca.Core.Logic.Battle.AttackReservationKind.Card,Core.Frame);reservedCard=card;reservedOwner=Cards;reservedRevision=Cards.HandRevision;reservedActionFrame=attackState?.StartedAt??-1;return false;}
                return StartCardAfterRecovery(card,Cards.HandRevision,out error);
            }
            if(attackReservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.Basic){
                if(deferBasicReservation)return false;
                if(Core.IsDashing&&!HasRegisteredDash(Player)){CancelAttackReservation();return false;}
                bool started=normalKick?.TryExecuteReserved(Core.Frame,Core.IsGrounded,Core.Facing,BodyCanAttack,Core.IsDashing,attackState!=null&&!CanCancelAttack())==true;
                if(started){attackState=null;reservedCard=null;reservedOwner=null;KickFeedback();}return started;
            }
            if(attackReservation.Kind!=Olca.Core.Logic.Battle.AttackReservationKind.Card||IsAttacking&&!CanCancelAnyAttack())return false;
            var expected=reservedCard;long revision=reservedRevision;CancelAttackReservation();
            if(!SupportedCard(expected)||Core.CardGauge.CurrentUnits<(long)expected.GetFinalCost()*OutsideCardGauge.UnitsPerPoint)return false;
            return StartCardAfterRecovery(expected,revision,out error);
        }
        /// <summary>製品の保持/解放入力。明示Pause復帰の信頼済み物理sampleは、一般操作の中立待ち中も蓄積と解放を許可する。停止中・focus再同期・初回再入は呼出側で禁止する。</summary>
        public bool ProcessAttackInput(LogicalInput input,bool selectionChanged,bool actionsAllowed,bool chargeInputAllowed,out string error)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));error=null;if(IsHitStopped)return false;if(lastProductAttackInputFrame==Core.Frame)return false;lastProductAttackInputFrame=Core.Frame;
            if(!actionsAllowed&&(!chargeInputAllowed||!input.HasChargeHoldSample)){CancelChargeInput();CancelAttackReservation();return false;}
            bool held=input.HasChargeHoldSample?input.ChargeHoldButton||input.BasicAttackButton:input.BasicAttackButton;
            bool card=actionsAllowed&&!selectionChanged&&input.CardUseButtonDown,basic=actionsAllowed&&!selectionChanged&&input.BasicAttackButtonDown;
            bool cardWins=card&&(!basic||input.CardUsePressedAt==0||input.BasicAttackPressedAt==0||input.CardUsePressedAt>=input.BasicAttackPressedAt);
            if(card)chargeInput.ClearAccumulation();
            if(cardWins){chargeInput.Advance(Core.Frame,held,false,chargeInputAllowed,false,false,ChargeRequiredFrames,out _);return ProcessCardUseInput(true,false,false,input.CardUsePressedAt,0,out error);}
            bool registered=OutsideChargeKickData.Known(Player)&&OutsideTrialRoute.KnownMap(Core.TrialMapId)&&Core.Definition.Id==OutsideKickTrialDefinition.MapId;
            if(!registered){CancelChargeInput();return actionsAllowed&&ProcessCardUseInput(false,selectionChanged,basic,0,input.BasicAttackPressedAt,out error,input.Vertical>0?1:input.Vertical<0?-1:0);}
            if(basic)CancelAttackReservation();
            bool enabled=chargeInputAllowed&&IsPresented&&!IsDeckEditingLocation&&BodyCanAttack;
            var release=chargeInput.Advance(Core.Frame,held,basic,enabled,enabled&&!IsAttacking,IsAttacking,ChargeRequiredFrames,out bool reserve);
            if(selectionChanged)return ProcessCardUseInput(false,true,false,0,0,out error);
            if(reserve){CancelAttackReservation();NormalKick.TryRequest(Core.Frame,Core.IsGrounded,Core.Facing,input.Vertical>0?1:input.Vertical<0?-1:0,BodyCanAttack,Core.IsDashing,true,true);}
            if(release==OutsideBasicRelease.Charged){
                CancelAttackReservation();if(!CanPerformAttack)return false;
                bool started=NormalKick.TryStartCharge(Core.Frame,Core.IsGrounded,Core.Facing,OutsideChargeKickData.Create(Player,Core.IsGrounded),BodyCanAttack);if(started){Core.EndDashAfterAttack();KickFeedback();CardUseMessage="シヨウ タメキック / 20ダメージ・ケズリ10（カリ）";}return started;
            }
            return ProcessCardUseInput(false,false,release==OutsideBasicRelease.Normal,0,input.BasicAttackPressedAt,out error,input.Vertical>0?1:input.Vertical<0?-1:0,held);
        }
        private void KickFeedback()
        {
            ClearBombReleaseVisual();AdvanceTrialKickHits();
            string motion=NormalKick.Kind==OutsideKickKind.AirDown?" / ジュンビのみキソン":NormalKick.Kind==OutsideKickKind.AirUp?" / モーション ミセツゾク":"";
            CardUseMessage=NormalKick.IsDashAttack?(Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id?"シヨウ カエン ダッシュ / 10ヒダメージ（カリ） / セッショクはシヨウ":"シヨウ ダッシュコウゲキ / 10ダメージ（カリ） / セッショクはシヨウ"):"シヨウ キック "+NormalKick.Kind+" / 10ダメージ（カリ）"+motion;
            CardUseMessageFrame=Core.Frame;
        }

        private bool CanCancelAnyAttack()=>attackState!=null?CanCancelAttack():normalKick?.IsActive==true&&normalKick.CanCancelNormal;
        private bool StartCardAfterRecovery(Card card,long revision,out string error)
        {
            var prior=attackState;attackState=null;bool started=TryUseSelectedCard(card,revision,out error,true);if(!started)attackState=prior;return started;
        }
        private bool trialCardPolish;
        /// <summary>通常の試用導線だけで新しい演出/飛翔定義を使う。保存済み旧行動は変更しない。</summary>
        public void EnableTrialCardPolish(){if(Core.Definition.Id==OutsideKickTrialDefinition.MapId)trialCardPolish=true;}
        public OutsideAttackStateData AttackState=>attackState?.Copy();
        public bool IsAttacking=>attackState!=null||normalKick?.IsActive==true;
        private static int Startup(Card card)=>card.StartupFrames??(int)Math.Ceiling(card.StartupTime*60d-0.00001d);
        private static int Recovery(Card card)=>card.RecoveryFrames??(int)Math.Ceiling(card.RecoveryTime*60d-0.00001d);
        private void ResetCardUseFeedback(){ClearBombReleaseVisual();chargeInput.Reset();lastProductAttackInputFrame=-1;HitStopRemainingFrames=0;CardUseMessage=null;CardUseMessageFrame=lastCardUseAttempt=bodyPreventedFrame=-1;attackState=null;lastAttackInputFrame=-1;trialStrike=null;Array.Clear(kickImpacts,0,kickImpacts.Length);normalKick?.Reset();CancelAttackReservation();}
        // Viewと独立した論理時計で一度だけ効果を確定する。停止/同frame再読込では進めない。
        private void AdvanceAttack(bool bodyAllowed)
        {
            var action=attackState;if(action==null)return;
            if(!bodyAllowed){attackState=null;CancelAttackReservation();return;}
            if(!action.EffectApplied&&Core.Frame-action.StartedAt>=action.StartupFrames){action.EffectApplied=true;var card=Cards.ActionCard(action.CardIndex);if(HealDefinition(card))Player.Heal(card.Power);else if(ShieldDefinition(card))Player.SetBarrier(card.Power);else if(ShotDefinition(card))EmitInitialShot(card,action.ResolvedPower.Value);else if(BombDefinition(card))EmitBomb(action);else ApplyTrialSword(card,action.ResolvedPower);}
            if(Core.Frame-action.StartedAt>=action.StartupFrames+(long)action.RecoveryFrames)attackState=null;
        }
        private static void ValidateAttack(OutsideAttackStateData action,OutsideExplorationCards cards,long frame)
        {
            if(action==null)return;var card=cards.ActionCard(action.CardIndex);
            bool polished=action.SwordMotionId==OutsideCardPolishTrial.SwordMotionId;
            if(action.SwordMotionId!=null&&(!polished||!SwordDefinition(card)))throw new ArgumentException("ソード表示定義とカードが不一致です");
            if(BombDefinition(card)!=(action.BombLaunch!=null))throw new ArgumentException("ボムの発射計画とカードが不一致です");
            if(action.BombLaunch!=null){action.BombLaunch.Validate();if(action.BombLaunch.Level!=card.ExceedLevel||action.BombLaunch.Power!=card.GetFinalPower()||action.BombLaunch.Element!=card.GetFinalElement()||action.BombLaunch.PoiseDamage!=card.PoiseDamage)throw new ArgumentException("ボム発射計画の性能が不一致です");}
            if(!(HealDefinition(card)||SwordDefinition(card)||ShieldDefinition(card)||ShotDefinition(card)||BombDefinition(card))||(polished?Math.Max(Startup(card),OutsideCardPolishTrial.SwordStartup):Startup(card))!=action.StartupFrames||(polished?Math.Max(Recovery(card),OutsideCardPolishTrial.SwordRecovery):Recovery(card))!=action.RecoveryFrames||InitialDefinition(card)!=action.ResolvedPower.HasValue||action.ResolvedPower<0)throw new ArgumentException("探索行動の定義と時間が一致しません");
        }
        /// <summary>試用ヒールの開始。地上/空中の登録判定と消費・前隙/発動済/後隙はSessionが所有する。</summary>
        public bool TryUseSelectedHealingCard(Card expected,long revision,out string error){if(!HealDefinition(expected)){error="ヒールではありません";return false;}return TryUseSelectedCard(expected,revision,out error);}
        public bool TryUseSelectedCard(Olca.Core.Domain.Card expected,long revision,out string error)=>TryUseSelectedCard(expected,revision,out error,false);
        private bool TryUseSelectedCard(Olca.Core.Domain.Card expected,long revision,out string error,bool recoveryCancel)
        {
            int? resolvedPower=null;OutsideBombLaunchData bombPlan=null;
            error=null;if(lastCardUseAttempt==Core.Frame){error="このフレームでは受付済みです";return false;}lastCardUseAttempt=Core.Frame;
            if(IsEnded||!IsPresented||IsDeckEditingLocation||!(CanPerformAttack||recoveryCancel&&BodyCanAttack)){error="今はカードを使用できません";return false;}
            if(expected==null||!ReferenceEquals(expected,Cards?.SelectedCard)||Cards.HandRevision!=revision){error="現在の手札を選択してください";return false;}
            if(!(HealDefinition(expected)||SwordDefinition(expected)||ShieldDefinition(expected)||ShotDefinition(expected)||BombDefinition(expected))){error="このカードの外界発動は未対応です";}
            else if(BombDefinition(expected)&&!TryPlanBomb(expected,out bombPlan,out error)){}
            else if(!SupportedCard(expected)){error=Core.Definition.Id=="validation-landing-32"&&SupportsCardLocation(expected,Core.TrialMapId)?"このカードはクウチュウでシヨウできません":"このバショではシヨウできません";}
            else if(!TryInitialPower(expected,out resolvedPower,out error)){}
            else if(!Cards.TryConsumeSelected(expected,revision,Core.CardGauge,out _)){error="カードゲージが不足しています";}
            if(error!=null){CardUseMessage=error;CardUseMessageFrame=Core.Frame;return false;}
            // HealEffectと同じ既存HP反映。満タンでもカード/ゲージの消費を戻さない。
            if(SwordDefinition(expected)||ShotDefinition(expected))Core.FaceAwayFromWallForAttack();
            if(normalKick?.IsActive==true)Core.RestartIdleMotion();
            ClearBombReleaseVisual();chargeInput.ClearAccumulation();normalKick?.CardStarted();
            attackState=new OutsideAttackStateData{CardIndex=Cards.IndexOf(expected),StartedAt=Core.Frame,StartupFrames=trialCardPolish&&SwordDefinition(expected)?Math.Max(Startup(expected),OutsideCardPolishTrial.SwordStartup):Startup(expected),RecoveryFrames=trialCardPolish&&SwordDefinition(expected)?Math.Max(Recovery(expected),OutsideCardPolishTrial.SwordRecovery):Recovery(expected),ResolvedPower=resolvedPower,BombLaunch=bombPlan,SwordMotionId=trialCardPolish&&SwordDefinition(expected)?OutsideCardPolishTrial.SwordMotionId:null};AdvanceAttack(true);CardUseMessage=bombPlan!=null?"ボム: チケイ トウテキ（カリ）":expected.Name+" を シヨウ";CardUseMessageFrame=Core.Frame;return true;
        }
        public bool CanPerformAttack=>!IsHitStopped&&BodyCanAttack&&!IsAttacking;
        private bool BodyCanAttack=>Core.Frame!=bodyPreventedFrame&&!IsEnded&&Player!=null&&!Player.IsDead&&!Player.IsParalyzed&&!Player.IsStaggered&&!IsLandingLocked&&!Core.HasFallenOut;
        private bool KnownOutsideStyle=>Player!=null&&(Player.DepartureStyle.Id==DepartureStyleDefinition.BasicId&&Player.BaseElementResistances.IsNeutral||Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id&&Player.BaseElementResistances.HasSameValues(ElementAffinity.CreatePlayerBase(ElementType.Fire)));
        public bool CanSwitchOutsideStyle=>CanPerformAttack&&Player.CanReplaceOutsideEquipment&&KnownOutsideStyle&&Core.Definition.Id=="validation-landing-32";
        public bool TrySwitchOutsideStyle(ClearReturnSession save,DepartureStyleDefinition style,CheckpointDefinitions definitions,out string error)
        {
            error=null;if(IsEnded||Player==null||style==null||save==null||!IsPresented||usingItem){error="外界の神依を確認できません";return false;}
            if(!ReferenceEquals(style,DepartureStyleDefinition.Basic)&&!ReferenceEquals(style,DepartureStyleDefinition.TrialFire)){error="未登録の試用神依です";return false;}
            if(style.Id==Player.DepartureStyle.Id)return true; // 同じ神依では保存/効果を発生させない。
            if(!CanSwitchOutsideStyle||style.Id!=DepartureStyleDefinition.BasicId&&style.Id!=DepartureStyleDefinition.TrialFire.Id){error="今は神依を切り替えできません";return false;}
            usingItem=true;
            try{
                var candidate=OutsidePlayerSaveData.Capture(Player).Restore();var basic=style.Id==DepartureStyleDefinition.BasicId?ElementResistances.Neutral:ElementAffinity.CreatePlayerBase(ElementType.Fire);candidate.ReplaceOutsideStyle(style,basic);var actor=OutsidePlayerSaveData.Capture(candidate);
                var runtime=CaptureItemRuntime(definitions);runtime.Id=actor.Id;var items=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key(),Cooldowns=itemCooldowns.Where(v=>v.Value>Core.Frame).Select(v=>new OutsideItemCooldownData{Id=v.Key,RemainingFrames=checked((int)(v.Value-Core.Frame))}).ToArray()};items.Validate(actor,runtime);
                if(!save.TryCommitOutsideStyle(style,actor,runtime,items,out error))return false;
                Player.ReplaceOutsideStyle(style,basic);CancelAttackReservation();CancelChargeInput();appliedItemStateId=actor.Id;savedPlayerId=actor.Id;savedPlayerKey=actor.Key();savedRuntimeId=runtime.Id;savedRuntimeKey=runtime.Key();return true;
            }catch(Exception e)when(e is ArgumentException||e is OverflowException||e is InvalidOperationException){error=e.Message;return false;}
            finally{usingItem=false;}
        }
        public bool CanReplaceEquipment=>!IsEnded&&Player!=null&&Player.CanReplaceOutsideEquipment;
        /// <summary>保存成功後の装備補正だけを置換。個体・HP・状態・論理時間は変更しない。</summary>
        public bool TryApplyEquipment(Olca.Core.Logic.Customization.ResolvedCustomizationAbilities abilities)
        {
            if(!CanReplaceEquipment||abilities==null)return false;
            Player.ReplaceOutsideEquipment(abilities);return true;
        }
        public bool TryInitializePlayer(System.Func<Olca.Core.Domain.PlayerCharacter> factory)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(factory==null)throw new System.ArgumentNullException(nameof(factory));
            if(Player!=null)return false;
            if(IsPresented)throw new System.InvalidOperationException("表示前に外界個体を初期化してください");
            var value=factory();if(value==null||value.IsDead)throw new System.ArgumentException("生存する外界個体が必要です");
            ResetCardUseFeedback();trialEnemyAttack=null;goblinTrial=null;pendingGoblinStrike=false;slimeTrial=null;pendingSlimeLanding=false;knockback=null;Player=value;playerFrame=Core.Frame;ResetFall();return true;
        }
        public void RecoverOutside(string pointId,bool relocate=true)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(Player==null)throw new System.InvalidOperationException("外界個体が未初期化です");
            long position=OutsideTrialRoute.ResolveRecovery(Core,pointId);Core.SetRecoveryMap(pointId);itemCooldowns.Clear();
            if(relocate){Core.RestoreRecoveryPosition(position);deckLocationArmed=managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=managedDeckLocations&&OutsideTrialRoute.IsReloadZone(Core);}Cards?.Reload();Core.CardGauge.RestoreFull();
            initialShots.Clear();bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();HitStopRemainingFrames=0;attackState=null;normalKick?.Reset();CancelAttackReservation();trialTarget=null;secondTrialTarget=null;secondTrialHits=0;trialTargetHits=0;trialTargetChanged=false;trialStrike=null;Array.Clear(kickImpacts,0,kickImpacts.Length);trialEnemyAttack=null;goblinTrial=null;pendingGoblinStrike=false;slimeTrial=null;pendingSlimeLanding=false;knockback=null;trialContactNextFrame=secondTrialContactNextFrame=0;Player.SetBarrier(0);Player.ClearPoisons();Player.ResetParalysis();Player.ResetConfusion();Player.ResetBlindness();Player.ClearStagger();Player.ResetPoiseAccumulation();Player.ClearInvincibility();Player.Heal(Player.MaxHp);playerFrame=Core.Frame;ResetFall();
        }
        private string savedPlayerId,savedPlayerKey;
        /// <summary>同じRoot/Actorの再表示では適用し直さない。coldは従来の復帰位置へ個体だけ復元する。</summary>
        public bool RestoreSavedPlayer(Olca.Core.Domain.OutsidePlayerSaveData data,string recoveryPoint)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));if(data==null)return false;
            string key=data.Key();if(savedPlayerId==data.Id){if(savedPlayerKey!=key)throw new System.ArgumentException("同じ外界保存IDの内容が変わりました");return false;}
            if(IsPresented)throw new System.InvalidOperationException("表示中の外界個体を置換できません");
            var candidate=data.Restore();long position=Player==null?OutsideTrialRoute.ResolveRecovery(Core,recoveryPoint):Core.Position;
            if(Player==null){Core.SetRecoveryMap(recoveryPoint);Core.RestoreRecoveryPosition(position);deckLocationArmed=managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=managedDeckLocations&&OutsideTrialRoute.IsReloadZone(Core);}ResetCardUseFeedback();initialShots.Clear();bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();trialTarget=null;secondTrialTarget=null;secondTrialHits=0;trialTargetChanged=false;trialTargetHits=0;Player=candidate;trialEnemyAttack=null;goblinTrial=null;pendingGoblinStrike=false;slimeTrial=null;pendingSlimeLanding=false;knockback=null;itemCooldowns.Clear();playerFrame=Core.Frame;ResetFall();savedPlayerId=data.Id;savedPlayerKey=key;PrepareMapEntry();return true;
        }
        /// <summary>保存成功後に実行した回復だけを記録し、後の再表示で古い値を戻さない。</summary>
        public void AcknowledgeRecovery(Olca.Core.Domain.OutsidePlayerSaveData data)
        {
            if(data==null)return;data.Validate();var actual=Olca.Core.Domain.OutsidePlayerSaveData.Capture(Player);actual.Id=data.Id;
            if(actual.Key()!=data.Key())throw new System.ArgumentException("回復した個体と保存が一致しません");savedPlayerId=data.Id;savedPlayerKey=data.Key();
        }
        private readonly Dictionary<string,HashSet<(int X,int Y)>> mapVisits=new Dictionary<string,HashSet<(int X,int Y)>>(StringComparer.Ordinal);
        public long MapVisitSerial {get;private set;}
        public long MapVisitClock {get;private set;}
        private bool mapVisitGenerationBound;private string mapVisitGeneration;
        // 個体やCoreの時計を戻さず、記録集合だけを進行保存の世代へ所属させる。
        public void BeginMapVisitGeneration(string generation)
        {
            if(generation!=null&&!Guid.TryParseExact(generation,"N",out _))throw new ArgumentException("地図記録の世代が不正です");
            if(mapVisitGenerationBound&&mapVisitGeneration==generation)return;
            mapVisitGenerationBound=true;mapVisitGeneration=generation;mapVisits.Clear();MapVisitSerial=0;
        }
        public OutsideMapRevealData[] CaptureMapVisits()=>mapVisits.OrderBy(v=>v.Key,StringComparer.Ordinal).Select(v=>new OutsideMapRevealData{MapId=v.Key,Cells=v.Value.OrderBy(c=>c.X).ThenBy(c=>c.Y).Select(c=>new OutsideMapCellData{X=c.X,Y=c.Y}).ToArray()}).ToArray();
        private void ObserveMapVisit(PlayerCharacter actor)
        {
            MapVisitClock=checked(MapVisitClock+1);
            if(!IsPresented||!ReferenceEquals(actor,Player)||actor.IsDead||!OutsideMapRevealTrial.TryCell(Core,out var map,out var cell))return;
            if(!mapVisits.TryGetValue(map,out var cells)){cells=new HashSet<(int X,int Y)>();mapVisits.Add(map,cells);}
            if(cells.Add((cell.X,cell.Y)))MapVisitSerial=checked(MapVisitSerial+1);
        }
        private long recoveryVisitSerial;private string recoveryVisitPointId;private bool recoveryVisitInside;
        public (long Serial,string PointId) RecoveryVisitNotice=>(recoveryVisitSerial,recoveryVisitPointId);
        private void ObserveRecoveryVisit(PlayerCharacter actor)
        {
            bool inside=IsPresented&&ReferenceEquals(Player,actor)&&!actor.IsDead&&OutsideTrialRoute.CanRecover(Core);
            if(inside&&!recoveryVisitInside){recoveryVisitPointId=OutsideTrialRoute.RecoveryPointId;recoveryVisitSerial=checked(recoveryVisitSerial+1);}
            recoveryVisitInside=inside;
        }
        public bool AdvancePlayerFrame()=>AdvancePlayerFrame(out _);
        public bool AdvancePlayerFrame(out bool attackAllowed)
        {
            attackAllowed=false;
            if(IsEnded||!IsPresented||Player==null||Core.Frame==playerFrame)return false;
            playerFrame=Core.Frame;var value=Player;
            if(Core.IsGrounded==instantBombAirborne||Core.IsGrounded&&Core.IsMoving||Core.Facing!=instantBombFacing||value.IsActionPrevented)ClearBombReleaseVisual();
            value.BeginHpFrame();value.BeginParalysisFrame(true);value.BeginConfusionFrame(true);value.BeginBlindnessFrame(true);
            OutsideKnockbackStateData pendingKnockback=null;
            try{AdvanceFall();AdvanceWater();AdvanceGoblinTrial();AdvanceSlimeTrial();AdvanceFlying454();bool bodyAllowed=BodyCanAttack;if(!bodyAllowed){ClearBombReleaseVisual();bodyPreventedFrame=Core.Frame;chargeInput.ClearAccumulation();}ObserveNormalKick(bodyAllowed);AdvanceAttack(bodyAllowed);AdvanceInitialShots();AdvanceBombs();AdvanceTrialKickHits();pendingKnockback=AdvanceTrialEnemyAttack();AdvanceTrialContactDamage();attackAllowed=bodyAllowed&&!IsAttacking;value.TickPoisons(0,true);value.TickProtection(0,true);value.TickInvincibleFrame();value.TickStagger(0,true);value.TickParalysis(0,true);value.TickConfusion(0,true);value.TickBlindness(0,true);value.TickPoiseRecovery(0,true);}
            finally{
                value.CommitHpFrame();
                // 共通被弾処理の確定結果だけを使い、HP通知からの退避にも開始済み移動を含める。
                if(pendingKnockback!=null&&value.DidStaggerAtHpFrameCommit&&!value.IsDead)knockback=pendingKnockback;
                if(value.IsDead||!value.IsStaggered)knockback=null;
                value.EndParalysisFrame(true);value.EndConfusionFrame(true);value.EndBlindnessFrame(true);value.PublishHpFrame();
            }
            if(value.IsDead){CancelChargeInput();bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();}
            ObserveMapVisit(value);ObserveRecoveryVisit(value);AdvancePair445();attackAllowed&=!value.IsDead;return true;
        }
        public OutsideExplorationCore Core {get;}=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,OutsideTraversalProfile.ValidationDash);
        public OutsideCameraPolicy Camera {get;private set;}=OutsideCameraPolicy.Follow;
        public OutsideExplorationCards Cards {get;private set;}
        private bool managedDeckLocations,deckLocationArmed,reloadGaugeOnExit;
        private Deck pendingLocationDeck;private Olca.Core.Interfaces.IRandomProvider pendingLocationRandom;private string queuedDeckKey;
        public bool IsDeckEditingLocation=>managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core)&&(deckLocationArmed||OutsideTrialRoute.CanEditDeck(Core));
        /// <summary>保存済み選択だけを準備する。手札はここで抽選せず、地点退出の論理境界で反映する。</summary>
        public bool QueueSelectedDeck(string key,IReadOnlyList<Card> cards,IReadOnlyDictionary<(string Id,ExceedLevel Level),int> owned,Olca.Core.Interfaces.IRandomProvider random,out string error)
        {
            if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));error=null;
            if(key==null){if(cards!=null)throw new ArgumentException("選択IDが必要です");EnableDeckLocations();pendingLocationDeck=null;pendingLocationRandom=null;queuedDeckKey=null;return true;}
            if(string.IsNullOrWhiteSpace(key)||random==null)throw new ArgumentException("選択と専用乱数が必要です");
            if(!DepartureDeckRules.TryCreateForSize(cards,owned,ExplorationDeckRules.RequiredCards,out var deck,out error))return false;
            key=CheckpointValues.Hash(w=>{w.Write(key);w.Write(cards.Count);foreach(var card in cards)w.Write(CollectionDisplay.DefinitionHash(card));});
            if(Cards?.SourceKey==key){EnableDeckLocations();pendingLocationDeck=null;pendingLocationRandom=null;queuedDeckKey=key;return true;}
            if(pendingLocationDeck!=null&&queuedDeckKey==key)return true;
            EnableDeckLocations();
            // 死亡保存はRuntimeを持たない。扉到着点からのcold復帰だけ、最初の論理境界で選択30枚を再装填する。
            // 通常の再表示や保存Runtimeの手札は再抽選しない。編成地点への立入り/退出規則も変えない。
            if(!IsPresented&&Cards==null&&savedPlayerId!=null&&Core.Frame==0&&Core.IsGrounded&&Core.Height==0&&
                ((Core.TrialMapId==OutsideTrialRoute.AnnexMapId&&Core.Position==OutsideTrialRoute.AnnexDoorX)||
                 (Core.TrialMapId==null&&Core.Position==OutsideTrialRoute.HomeDoorX)))deckLocationArmed=true;
            pendingLocationDeck=deck;pendingLocationRandom=random;queuedDeckKey=key;return true;
        }
        private void EnableDeckLocations(){if(!managedDeckLocations){managedDeckLocations=true;deckLocationArmed=OutsideTrialRoute.IsDeckEditingZone(Core)&&(deckLocationArmed||OutsideTrialRoute.CanEditDeck(Core));reloadGaugeOnExit=deckLocationArmed&&OutsideTrialRoute.IsReloadZone(Core);}}
        public bool AdvanceDeckLocation()
        {
            if(!managedDeckLocations||IsEnded)return false;
            if(IsDeckEditingLocation){ClearBombReleaseVisual();attackState=null;normalKick?.Reset();CancelAttackReservation();deckLocationArmed=true;reloadGaugeOnExit|=OutsideTrialRoute.IsReloadZone(Core);return false;}
            if(!deckLocationArmed)return false;deckLocationArmed=false;bool refillGauge=reloadGaugeOnExit;reloadGaugeOnExit=false;
            if(pendingLocationDeck!=null){var random=Cards?.RandomProvider??pendingLocationRandom;pendingLocationDeck.Shuffle(random);Cards=new OutsideExplorationCards(pendingLocationDeck,random,queuedDeckKey);pendingLocationDeck=null;pendingLocationRandom=null;if(refillGauge)Core.CardGauge.RestoreFull();return true;}
            if(Cards?.TotalCount!=ExplorationDeckRules.RequiredCards){if(refillGauge)Core.CardGauge.RestoreFull();return refillGauge;}
            Cards.Reload();if(refillGauge)Core.CardGauge.RestoreFull();return true;
        }
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
            if(IsPresented||Core.Frame!=0)return false;Core.ConfigureTraversal(profile);ResetWater();return true;
        }
        // 名称は装飾時間だけ。Core/保存hashへ加えず、同一訪問のView再生成では戻さない。
        private long? mapNameStartedAt;
        private bool mapEntryPending=true;
        public const int MapNameDisplayFrames=120,MapNameFadeFrames=60;
        public string MapName=>IsForest460?"モリ タチマワリ（カリ・セーブなし）":flyingTrial454!=null?"ヒコウ シヨウ（カリ・セーブなし）":PairStage445>0?"レンセン シヨウ（カリ・セーブなし）":Core.Definition.Id=="validation-landing-32"?(Core.TrialMapId==OutsideTrialRoute.AnnexMapId?"シヨウ ベツマップ（カリ）":"ケンショウ エリア（カリ）"):null;
        public double MapNameOpacity {
            get{if(mapEntryPending||!mapNameStartedAt.HasValue||MapName==null)return 0;long age=Math.Max(0,Core.Frame-mapNameStartedAt.Value);return age<MapNameDisplayFrames?1:Math.Max(0,1-(age-MapNameDisplayFrames)/(double)MapNameFadeFrames);}
        }
        private void PrepareMapEntry(){mapEntryPending=true;mapNameStartedAt=null;}
        public bool IsEnded{get;private set;}
        public bool IsPresented=>current!=null;
        public PresentationLease AttachPresentation()
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(mapEntryPending){mapNameStartedAt=Core.Frame;mapEntryPending=false;}
            recoveryVisitInside=false;CancelAttackReservation();return current=new PresentationLease(this);
        }
        public void Dispose(){if(IsEnded)return;IsEnded=true;current?.Dispose();current=null;ResetCardUseFeedback();initialShots.Clear();bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();trialTarget=null;secondTrialTarget=null;secondTrialHits=0;trialTargetChanged=false;trialTargetHits=0;Player=null;trialEnemyAttack=null;goblinTrial=null;pendingGoblinStrike=false;slimeTrial=null;pendingSlimeLanding=false;knockback=null;pendingLocationDeck=null;pendingLocationRandom=null;queuedDeckKey=null;}
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
        private bool FloorExists(decimal x)=>TraversalProfile==null||!TraversalProfile.FloorGaps.Any(g=>x>g.Left&&x<g.Right);
        public bool HasFallenOut=>TraversalProfile?.VoidDepth>0&&Height<=-TraversalProfile.VoidDepth;
        private string SupportAt(long x,long y)
        {
            if(y==0&&FloorExists(x))return "floor";string id=null;
            foreach(var surface in Definition.LandingSurfaces)if(surface.Top==y&&surface.Contains(x)&&(id==null||System.StringComparer.Ordinal.Compare(surface.Id,id)<0))id=surface.Id;
            return id;
        }
        private bool Landing(long oldX,long oldY,long nextY,out long top,out string id)
        {
            top=0;id=null;
            if(oldY>=0&&nextY<=0){decimal fraction=oldY==nextY?0:(decimal)oldY/(oldY-nextY);decimal x=oldX+((decimal)Position-oldX)*fraction;if(FloorExists(Position)&&FloorExists(x))id="floor";}
            foreach(var surface in Definition.LandingSurfaces){
                if(oldY<surface.Top||nextY>surface.Top||!surface.Contains(Position))continue;
                decimal fraction=(decimal)(oldY-surface.Top)/(oldY-nextY);
                decimal x=(decimal)oldX+((decimal)Position-oldX)*fraction;
                if(x<surface.Left||x>surface.Right)continue;
                if(id==null||surface.Top>top||surface.Top==top&&System.StringComparer.Ordinal.Compare(surface.Id,id)<0){top=surface.Top;id=surface.Id;}
            }
            return id!=null;
        }
        public void TickFrame(float horizontal,bool jumpDown=false,bool jumpHeld=false,bool dashDown=false,bool dashHeld=false,bool movementLocked=false)=>TickTraversalFrame(horizontal,jumpDown,jumpHeld,dashDown,dashHeld,movementLocked,false,false);
        internal void TickDashAttackFrame(int facing,bool jumpHeld,bool locked){if(TraversalProfile==null||facing!=1&&facing!=-1)throw new InvalidOperationException("ダッシュ攻撃の移動定義がありません");TickTraversalFrame(facing,false,jumpHeld,false,false,locked,true,false);}
        internal void TickAfterDashAttackFrame(float horizontal,bool jumpDown,bool jumpHeld,bool dashDown,bool dashHeld,bool locked)=>TickTraversalFrame(horizontal,jumpDown,jumpHeld,dashDown,dashHeld,locked,false,true);
        internal void TickKnockbackFrame(long displacement,bool jumpHeld,bool dashHeld)
        {
            if(displacement==0||displacement < -OutsideMovementDefinition.UnitsPerWorldUnit||displacement>OutsideMovementDefinition.UnitsPerWorldUnit)throw new ArgumentOutOfRangeException(nameof(displacement));
            TickTraversalFrame(0,false,jumpHeld,false,dashHeld,true,false,false,displacement);
        }
        private void TickTraversalFrame(float horizontal,bool jumpDown,bool jumpHeld,bool dashDown,bool dashHeld,bool movementLocked,bool dashAttack,bool endDashAttack,long forcedDisplacement=0)
        {
            if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new System.ArgumentOutOfRangeException(nameof(horizontal));
            bool wasIdle=IsIdleMotion;
            LastWaterContactHeight=null;Frame++;if(endDashAttack)EndDash();CardGauge.AdvanceFrame();if(HasFallenOut){StopTraversalForLanding();IsMoving=false;MovementFrame=0;return;}bool jumpEdge=jumpDown&&!jumpWasHeld;jumpWasHeld=jumpHeld;
            if(movementLocked){dashAttack=false;horizontal=0;jumpEdge=false;dashDown=false;StopTraversalForLanding();}
            BeginTraversalFrame(dashDown,dashHeld);
            bool launch=jumpEdge&&IsGrounded;
            int horizontalDirection=horizontal>0?1:horizontal<0?-1:0;
            bool wallLaunch=TryWallKick(jumpEdge,horizontalDirection);
            // 壁蹴りを優先し、壁蹴りでは空中一回分を回復しない。接地だけで再使用可。
            bool airLaunch=jumpEdge&&!launch&&!wallLaunch&&!IsGrounded&&TraversalProfile?.DoubleJumpUnlocked==true&&!airJumpAt.HasValue;
            if(airLaunch){airJumpAt=Frame;wallKickAt=-1;kickAway=0;VerticalVelocity=WaterJumpVelocity;}
            if(launch){IsDashJumping=IsDashing;if(IsDashing)EndDash();SupportId=null;VerticalVelocity=WaterJumpVelocity;}
            if(!jumpHeld&&VerticalVelocity>WaterReleasedRise)VerticalVelocity=WaterReleasedRise;
            long previous=Position,oldHeight=Height;
            int direction=dashAttack?horizontalDirection:TraversalDirection(horizontalDirection);long rawSpeed=dashAttack?TraversalProfile.DashSpeedPerSecond:TraversalSpeed;long speed=IsInWater?Scale(rawSpeed,TraversalProfile.Water.SpeedPercent):rawSpeed;
            // 外力も通常移動と同じ壁・足場・落下処理へ通す。向きは入力で変更しない。
            if(forcedDisplacement!=0){Position+=forcedDisplacement>0?Math.Min(forcedDisplacement,Definition.Width-Position):Math.Max(forcedDisplacement,-Position);remainder=0;}
            else if(direction==0)remainder=0;
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
            if(!launch&&!wallLaunch&&!airLaunch&&IsGrounded){SupportId=SupportAt(Position,Height);if(IsGrounded){UpdateWallSlide(dashAttack?0:horizontalDirection);airJumpAt=null;TraceWater(previous,oldHeight);CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);return;}VerticalVelocity=0;}
            UpdateWallSlide(dashAttack?0:horizontalDirection);
            long next=Height+VerticalVelocity;
            if(VerticalVelocity<0&&Landing(previous,oldHeight,next,out var top,out var support)){Height=top;VerticalVelocity=0;SupportId=support;airJumpAt=null;}
            else{Height=TraversalProfile?.VoidDepth>0?Math.Max(-TraversalProfile.VoidDepth,next):next;VerticalVelocity-=IsInWater?TraversalProfile.WaterGravity:ValidationGravity;if(IsWallSliding)VerticalVelocity=System.Math.Max(VerticalVelocity,-TraversalProfile.WallSlidePerFrame);SupportId=null;}
            TraceWater(previous,oldHeight);CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);
        }
        // 行動からIdleへ戻る境界だけ再開始する。ゲーム時計・位置には触れない。
        internal void RestartIdleMotion()=>IdleFrame=0;
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
        private static void Move(OutsideExplorationCards cards,int delta){if(cards==null||cards.Hand.Count==0)return;int count=cards.Hand.Count;cards.TrySelect(cards.SelectedIndex>=count?(delta>0?0:count-1):(cards.SelectedIndex+delta+count)%count);}
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
        public bool AdvanceFrame(OutsideExplorationCards cards,bool allowActions=true)
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
            pending.Clear();bool repeated=false;
            if(!waitingForNeutral&&!waitingForResumeSample&&!inputChangedThisFrame&&repeatDirection!=0&&--framesUntilRepeat<=0){
                if(allowActions){Move(cards,repeatDirection);repeated=true;}
                framesUntilRepeat=DefaultRepeatFrames;
            }
            return inputChangedThisFrame||repeated;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>現在実装済みの外界状態だけを運ぶ。入力キュー・物理キー保持・未実装の敵を創作しない。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideRuntimeSaveData
    {
        [System.Runtime.Serialization.DataMember] public int Version=1;
        [System.Runtime.Serialization.DataMember] public string Id;
        [System.Runtime.Serialization.DataMember] public OutsideCoreStateData Core;
        [System.Runtime.Serialization.DataMember] public long? FixedCameraCenter;
        [System.Runtime.Serialization.DataMember] public OutsideCardsStateData Cards;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideAttackStateData Attack;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideTrialTargetData TrialTarget;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideTrialTargetData SecondTrialTarget;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideKickStateData NormalKick;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideChargeKickData ChargeKick;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideFallStateData Fall;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideWaterStateData Water;
        // 試用外界シールドだけ。旧0欄を出力せず旧Runtime keyを維持する。
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public int TrialBarrierHp;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideTrialEnemyAttackData TrialEnemyAttack;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideGoblinTrialData GoblinTrial;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideSlimeTrialData SlimeTrial;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideKnockbackStateData Knockback;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideTrialContactData TrialContact;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public int HitStopFrames;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideInitialShotData[] InitialShots;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideBombFlightData[] Bombs;
        public OutsideRuntimeSaveData Copy()=>new OutsideRuntimeSaveData{Version=Version,Id=Id,Core=Core?.Copy(),FixedCameraCenter=FixedCameraCenter,Cards=Cards?.Copy(),Attack=Attack?.Copy(),TrialTarget=TrialTarget?.Copy(),SecondTrialTarget=SecondTrialTarget?.Copy(),NormalKick=NormalKick?.Copy(),ChargeKick=ChargeKick?.Copy(),Fall=Fall?.Copy(),Water=Water?.Copy(),TrialBarrierHp=TrialBarrierHp,TrialEnemyAttack=TrialEnemyAttack?.Copy(),GoblinTrial=GoblinTrial?.Copy(),SlimeTrial=SlimeTrial?.Copy(),Knockback=Knockback?.Copy(),TrialContact=TrialContact?.Copy(),InitialShots=InitialShots?.Select(v=>v?.Copy()).ToArray(),Bombs=Bombs?.Select(v=>v?.Copy()).ToArray(),HitStopFrames=HitStopFrames};
        public void Validate(){SlimeTrial?.Validate(Core,GoblinTrial,TrialEnemyAttack,TrialTarget);GoblinTrial?.Validate(Core,TrialEnemyAttack,TrialTarget);ChargeKick?.Validate(NormalKick);if(Bombs!=null){if(Cards==null||Bombs.Length==0||Bombs.Length>60||Bombs.Any(v=>v==null)||Bombs.Select(v=>v.ActionStartedAt).Distinct().Count()!=Bombs.Length)throw new ArgumentException("ボム保存配列が不正です");foreach(var bomb in Bombs)bomb.Validate(Core?.Frame??-1);}if(InitialShots!=null){if(Cards==null||InitialShots.Length==0||InitialShots.Length>60||InitialShots.Any(v=>v==null)||InitialShots.Select(v=>v.StartedAt).Distinct().Count()!=InitialShots.Length)throw new ArgumentException("探索弾保存が不正です");foreach(var shot in InitialShots)shot.Validate(Core?.Frame??-1);}if(Version!=1||!Guid.TryParseExact(Id,"N",out _)||Core==null||FixedCameraCenter<0||TrialBarrierHp<0||TrialBarrierHp>15||HitStopFrames<0||HitStopFrames>60)throw new ArgumentException("外界Runtime保存の版/識別が不正です");Core.Validate();if(!OutsideTrialRoute.KnownMap(Core.TrialMapId)&&TrialEnemyAttack!=null)throw new ArgumentException("試用別マップに戦闘状態は登録されていません");TrialEnemyAttack?.Validate(Core.Frame);Knockback?.Validate();TrialContact?.Validate(Core.Frame);Cards?.Validate();Attack?.Validate(Core.Frame,Cards);TrialTarget?.Validate();SecondTrialTarget?.Validate("trial-kick-target-v1");NormalKick?.Validate(Core.Frame,Core.SupportId!=null,Core.Facing);if(NormalKick!=null){if(Attack!=null||NormalKick.Kind!=OutsideKickKind.Dash&&Core.DashEndsAt>Core.Frame||NormalKick.Kind==OutsideKickKind.Dash&&(Core.DashJumping||Core.WallSliding||Core.WallKickAt>=0||Core.DashStartedAt<0||Core.DashStartedAt>NormalKick.StartedAt||Core.LastDashAt<NormalKick.StartedAt))throw new ArgumentException("蹴りと他行動を同時に復元できません");foreach(int id in NormalKick.HitTargets){var target=id==0?TrialTarget:SecondTrialTarget;if(target==null||target.Hits==0||target.Hp>OutsideExplorationSession.TrialTargetMaximum-OutsideKickTrialDefinition.Damage)throw new ArgumentException("蹴り命中済み状態と標的が不一致です");}}Fall?.Validate(Core);Water?.Validate(Core);}
        public string Key(){Validate();return PortableCheckpointJson.Hash(PortableCheckpointJson.Serialize(this));}
        public OutsideRuntimeSaveData ForRun(string run){Validate();var v=Copy();v.Id=run;v.TrialBarrierHp=0;v.TrialEnemyAttack=null;v.Knockback=null;v.TrialContact=null;v.HitStopFrames=0;v.Validate();return v;}
    }
    /// <summary>物理入力や予約は保存せず、開始済みの一個体と効果確定境界だけを保持する。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideAttackStateData
    {
        [System.Runtime.Serialization.DataMember] public int CardIndex,StartupFrames,RecoveryFrames;
        [System.Runtime.Serialization.DataMember] public long StartedAt;
        [System.Runtime.Serialization.DataMember] public bool EffectApplied;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public int? ResolvedPower;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideBombLaunchData BombLaunch;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public string SwordMotionId;
        public OutsideAttackStateData Copy(){var copy=(OutsideAttackStateData)MemberwiseClone();copy.BombLaunch=BombLaunch?.Copy();return copy;}
        public void Validate(long frame,OutsideCardsStateData cards){
            if(SwordMotionId!=null&&SwordMotionId!=OutsideCardPolishTrial.SwordMotionId)throw new ArgumentException("ソード表示定義が未登録です");
            BombLaunch?.Validate();
            if(ResolvedPower<0||cards==null||CardIndex<0||CardIndex>=cards.Cards.Length||!cards.Drawn.Contains(CardIndex)||cards.Hand.Contains(CardIndex)||StartedAt<0||StartedAt>frame||StartupFrames<0||RecoveryFrames<0||frame-StartedAt>=StartupFrames+(long)RecoveryFrames||EffectApplied!=(frame-StartedAt>=StartupFrames))throw new ArgumentException("探索攻撃状態と手札/時計が不正です");
        }
    }
    /// <summary>旧null欄は接触未発生。残り間隔だけを保存し、読込時に被害を再実行しない。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideTrialContactData
    {
        [System.Runtime.Serialization.DataMember] public string DefinitionId="trial-contact-no-poise-v1";
        [System.Runtime.Serialization.DataMember] public long NextFrame;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public long SecondNextFrame;
        public OutsideTrialContactData Copy()=>(OutsideTrialContactData)MemberwiseClone();
        public void Validate(long frame)
        {
            bool Valid(long next)=>next==0||next>frame&&next-frame<=OutsideExplorationSession.TrialContactInterval;
            if(DefinitionId!="trial-contact-no-poise-v1"||frame<0||NextFrame==0&&SecondNextFrame==0||!Valid(NextFrame)||!Valid(SecondNextFrame))throw new ArgumentException("試用接触の定義/時計が不正です");
        }
    }
    /// <summary>攻撃別の仮外力。現在はV2の一種だけに明示登録する。</summary>
    public readonly struct OutsideHitKnockbackDefinition
    {
        public readonly int DirectionMultiplier,Frames;public readonly long UnitsPerFrame;
        public OutsideHitKnockbackDefinition(int direction,long units,int frames){if(direction!=1&&direction!=-1||units<=0||units>60000||frames<1||frames>60)throw new ArgumentException("試用外力の値が不正です");DirectionMultiplier=direction;UnitsPerFrame=units;Frames=frames;}
        public OutsideKnockbackStateData Start(int facing)=>new OutsideKnockbackStateData{Direction=facing*DirectionMultiplier,UnitsPerFrame=UnitsPerFrame,FramesRemaining=Frames};
    }
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideKnockbackStateData
    {
        [System.Runtime.Serialization.DataMember] public string DefinitionId="trial-enemy-v2-knockback-v1";
        [System.Runtime.Serialization.DataMember] public int Direction,FramesRemaining;
        [System.Runtime.Serialization.DataMember] public long UnitsPerFrame;
        public OutsideKnockbackStateData Copy()=>(OutsideKnockbackStateData)MemberwiseClone();
        public void Validate(){if(DefinitionId!="trial-enemy-v2-knockback-v1"||Direction!=1&&Direction!=-1||UnitsPerFrame!=OutsideTrialEnemyAttackData.V2Knockback.UnitsPerFrame||FramesRemaining<1||FramesRemaining>OutsideTrialEnemyAttackData.V2Knockback.Frames)throw new ArgumentException("外界の押し戻し保存が不正です");}
    }
    public sealed class OutsideTrialEnemyAttackData
    {
        // 仮値: 攻撃方向へ0.05world/frameを6frame（最大0.3world）。正式バランスではない。
        public const string V3DefinitionId="trial-shield-enemy-v3"; // 被弾無敵600msの明示試用。
        public static readonly OutsideHitKnockbackDefinition V2Knockback=new OutsideHitKnockbackDefinition(1,3000,6);
        public string DefinitionId="trial-shield-enemy-v1";public long NextFrame;
        public OutsideTrialEnemyAttackData Copy()=>(OutsideTrialEnemyAttackData)MemberwiseClone();
        public void Validate(long frame){if((DefinitionId!="trial-shield-enemy-v1"&&DefinitionId!="trial-shield-enemy-v2"&&DefinitionId!=V3DefinitionId)||frame<0||NextFrame<=frame||NextFrame-frame>OutsideExplorationSession.TrialEnemyInterval)throw new ArgumentException("試用敵攻撃の定義/時計が不正です");}
    }
    public sealed class OutsideTrialTargetData{public string Id="trial-sword-target-v1";public int Hp,Hits;public OutsideTrialTargetData Copy()=>(OutsideTrialTargetData)MemberwiseClone();public void Validate(string expectedId="trial-sword-target-v1"){if(Id!=expectedId||Hp<0||Hp>OutsideExplorationSession.TrialTargetMaximum||Hits<0)throw new ArgumentException("試用標的の保存が不正です");}}
    /// <summary>蹴りでHPを削った位置と論理時刻だけを渡す、非保存の表示値。</summary>
    public sealed class OutsideKickImpactVisual
    {
        public long Frame {get;} public long X {get;} public long Y {get;} public long SourceX {get;} public long SourceY {get;} public int Facing {get;}
        public OutsideKickImpactVisual(long frame,long x,long y,long sourceX,long sourceY,int facing){Frame=frame;X=x;Y=y;SourceX=sourceX;SourceY=sourceY;Facing=facing;}
    }
    public sealed class OutsideTrialStrike{public string CardId;public long Frame,X,Y,Reach;public float VerticalScale;public int Facing;public bool Hit,SpriteSwing;public OutsideTrialStrike Copy()=>(OutsideTrialStrike)MemberwiseClone();}
    public sealed class OutsideAfterimageStateData
    {public long X,Y,Frame;public int Facing;public OutsideAfterimagePose Pose;public OutsideAfterimageStateData Copy()=>(OutsideAfterimageStateData)MemberwiseClone();}
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideCoreStateData
    {
        [System.Runtime.Serialization.DataMember] public string DefinitionKey,SupportId;[System.Runtime.Serialization.DataMember] public long Position,Frame,Height,Velocity,MovementFrame,IdleFrame,Remainder;
        [System.Runtime.Serialization.DataMember] public int Facing,ConsecutiveDashes,KickAway,WallContact;[System.Runtime.Serialization.DataMember] public bool Moving,DashJumping,WallSliding;
        [System.Runtime.Serialization.DataMember] public long DashStartedAt,DashEndsAt,CooldownUntil,LastDashAt,WallKickAt,WallSlideFrame;
        [System.Runtime.Serialization.DataMember] public int GaugeMaximum,GaugeRecovery;[System.Runtime.Serialization.DataMember] public long GaugeUnits;[System.Runtime.Serialization.DataMember] public OutsideAfterimageStateData[] Afterimages=Array.Empty<OutsideAfterimageStateData>();
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public long? AirJumpAt;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public string TrialMapId;
        public OutsideCoreStateData Copy(){var v=(OutsideCoreStateData)MemberwiseClone();v.Afterimages=Afterimages?.Select(a=>a?.Copy()).ToArray();return v;}
        public void Validate()
        {
            if(!OutsideTrialRoute.KnownMap(TrialMapId)||DefinitionKey==null||DefinitionKey.Length!=64||!DefinitionKey.All(Uri.IsHexDigit)||Position<0||Frame<0||Height<0&&SupportId!=null||Facing!=1&&Facing!=-1||MovementFrame<0||MovementFrame>Frame||IdleFrame<0||IdleFrame>Frame||Remainder<=-60||Remainder>=60||Moving!=(MovementFrame>0)||SupportId!=null&&string.IsNullOrWhiteSpace(SupportId)||ConsecutiveDashes<0||ConsecutiveDashes>2||KickAway< -1||KickAway>1||WallContact< -1||WallContact>1||WallSlideFrame<0||WallSlideFrame>Frame||!WallSliding&&WallSlideFrame!=0||new[]{DashStartedAt,DashEndsAt,CooldownUntil,LastDashAt,WallKickAt}.Any(v=>v< -1)||GaugeMaximum<=0||GaugeRecovery<0||GaugeUnits<0||GaugeUnits>(long)GaugeMaximum*OutsideCardGauge.UnitsPerPoint||Afterimages==null||Afterimages.Length>4)throw new ArgumentException("外界Core保存の値が不正です");
            long previous=-1;foreach(var a in Afterimages){if(a==null||a.X<0||a.Frame<0||a.Frame<=previous||a.Frame>Frame||Frame-a.Frame>=12||a.Facing!=1&&a.Facing!=-1||!Enum.IsDefined(typeof(OutsideAfterimagePose),a.Pose))throw new ArgumentException("外界残像保存が不正です");previous=a.Frame;}
        }
    }
    /// <summary>カードは参照番号で各領域を結合し、復元で抽選しない。未登録は外側のnullで表す。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideCardsStateData
    {
        [System.Runtime.Serialization.DataMember] public CheckpointCardDto[] Cards;
        [System.Runtime.Serialization.DataMember] public int[] Hand,Drawn,Returned;
        [System.Runtime.Serialization.DataMember] public int Selected;
        [System.Runtime.Serialization.DataMember] public long Revision;
        [System.Runtime.Serialization.DataMember] public BattleRandomStateDto Random;
        // 旧20枚のserialize/hashを保つため、未設定の新項目は出力しない。
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public string SourceKey;
        public OutsideCardsStateData Copy()=>new OutsideCardsStateData{Cards=Cards?.Select(v=>v?.Copy()).ToArray(),Hand=Hand?.ToArray(),Drawn=Drawn?.ToArray(),Returned=Returned?.ToArray(),Selected=Selected,Revision=Revision,Random=Random?.Copy(),SourceKey=SourceKey};
        public void Validate()
        {
            if(Cards==null||(Cards.Length!=20&&Cards.Length!=ExplorationDeckRules.RequiredCards)||Hand==null||Hand.Length>3||Drawn==null||Returned==null||Selected<0||Selected>=3||Revision<0||Random==null)throw new ArgumentException("外界カード保存が欠落しています");
            if(Hand.Length<3&&(Drawn.Length!=Cards.Length||Returned.Length!=0))throw new ArgumentException("未枯渇の外界手札が欠落しています");
            if(SourceKey!=null&&(Cards.Length!=ExplorationDeckRules.RequiredCards||SourceKey.Length!=64||!SourceKey.All(Uri.IsHexDigit)))throw new ArgumentException("外界デッキの選択識別が不正です");
            for(int i=0;i<Cards.Length;i++){var c=Cards[i];if(c==null||c.InstanceId!="c"+i||string.IsNullOrWhiteSpace(c.DefinitionKey)||c.ValueHash==null||c.ValueHash.Length!=64||!c.ValueHash.All(Uri.IsHexDigit)||c.GrantKeys==null||c.GrantKeys.Any(string.IsNullOrWhiteSpace)||c.TuningCount!=c.GrantKeys.Length)throw new ArgumentException("外界カード定義参照が不正です");}
            foreach(var part in new[]{Hand,Drawn,Returned})if(part.Any(v=>v<0||v>=Cards.Length)||part.Distinct().Count()!=part.Length)throw new ArgumentException("外界カード領域の個体参照が不正です");
            if(Hand.Any(v=>!Drawn.Contains(v))||Returned.Any(Drawn.Contains))throw new ArgumentException("外界カード履歴が矛盾しています");Random.Validate();
        }
        public string Key(){Validate();return PortableCheckpointJson.Hash(PortableCheckpointJson.Serialize(this));}
    }
    public sealed partial class OutsideExplorationCards
    {
        private OutsideExplorationCards(Deck restored,Olca.Core.Interfaces.IRandomProvider random,IReadOnlyList<Card> hand,int selected,long revision,string sourceKey)
        {deck=restored;this.random=random;Hand=Array.AsReadOnly(hand.ToArray());SelectedIndex=selected;HandRevision=revision;SourceKey=sourceKey;}
        public OutsideCardsStateData CaptureState(CheckpointDefinitions definitions)
        {
            if(definitions==null||!(random is Olca.Core.Logic.Battle.SeededBattleRandom seeded))throw new ArgumentException("外界カードの既知定義と保存可能な専用乱数が必要です");
            var all=deck.GetAllCards().ToArray();if((all.Length!=20&&all.Length!=ExplorationDeckRules.RequiredCards)||all.Distinct().Count()!=all.Length)throw new ArgumentException("外界デッキ個体が不正です");var ids=all.Select((c,i)=>(c,i)).ToDictionary(v=>v.c,v=>v.i);
            var value=new OutsideCardsStateData{Cards=all.Select((c,i)=>definitions.CaptureKnownCard("c"+i,c)).ToArray(),Hand=Hand.Select(c=>ids[c]).ToArray(),Drawn=deck.CaptureDrawn().Select(c=>ids[c]).ToArray(),Returned=deck.CaptureReturnedDraws().Select(c=>ids[c]).ToArray(),Selected=SelectedIndex,Revision=HandRevision,Random=seeded.CaptureState(),SourceKey=SourceKey};value.Validate();return value;
        }
        public static OutsideExplorationCards RestoreState(OutsideCardsStateData value,CheckpointDefinitions definitions)
        {
            value.Validate();if(definitions==null)throw new ArgumentException("外界カード定義が未登録です");var all=value.Cards.Select(v=>definitions.RestoreCard(v.Copy())).ToArray();var deck=new Deck(all.ToList());deck.RestoreDrawHistory(value.Drawn.Select(i=>all[i]).ToArray(),value.Returned.Select(i=>all[i]).ToArray());
            return new OutsideExplorationCards(deck,value.Random.Restore(),value.Hand.Select(i=>all[i]).ToArray(),value.Selected,value.Revision,value.SourceKey);
        }
    }
    public sealed partial class OutsideExplorationCore
    {
        public string TrialMapId {get;private set;}
        internal void SetRecoveryMap(string point)=>TrialMapId=OutsideTrialRoute.MapForRecovery(point);
        public string StateDefinitionKey()=>CheckpointValues.Hash(w=>{
            w.Write("outside-core-state-v1");w.Write(Definition.Id);w.Write(Definition.Width);w.Write(Definition.Start);w.Write(Definition.SpeedPerSecond);w.Write(ValidationJumpVelocity);w.Write(ValidationGravity);w.Write(ValidationReleasedRise);w.Write(Definition.LandingSurfaces.Count);
            foreach(var v in Definition.LandingSurfaces){w.Write(v.Id);w.Write(v.Left);w.Write(v.Right);w.Write(v.Top);}var p=TraversalProfile;w.Write(p!=null);if(p==null)return;
            w.Write(p.Id);w.Write(p.DashSpeedPerSecond);w.Write(p.DashFrames);w.Write(p.DashCooldownFrames);w.Write(p.DashResetFrames);w.Write(p.DashJumpSpeedPerSecond);w.Write(p.BodyHalfWidth);w.Write(p.BodyHeight);w.Write(p.WallSlidePerFrame);w.Write(p.WallKickVelocity);w.Write(p.WallKickSpeedPerSecond);w.Write(p.WallKickPushFrames);w.Write(p.WallKickMotionFrames);w.Write(p.WallActionsUnlocked);w.Write(p.Walls.Count);foreach(var v in p.Walls){w.Write(v.Id);w.Write(v.X);w.Write(v.Bottom);w.Write(v.Top);w.Write(v.AllowsSlide);w.Write(v.AllowsKick);}
            if(p.Water!=null){w.Write("outside-water-v1");w.Write(p.Water.SpeedPercent);w.Write(p.Water.GravityPercent);w.Write(p.Water.JumpPercent);w.Write(p.Water.BreathFrames);w.Write(p.Waters.Count);foreach(var a in p.Waters){w.Write(a.Id);w.Write(a.Left);w.Write(a.Right);w.Write(a.Bottom);w.Write(a.Top);}}
            if(p.DoubleJumpUnlocked)w.Write("outside-double-jump-v1");
            if(p.FloorGaps.Count>0){w.Write("outside-floor-gaps-v1");w.Write(p.VoidDepth);w.Write(p.FloorGaps.Count);foreach(var g in p.FloorGaps){w.Write(g.Id);w.Write(g.Left);w.Write(g.Right);}}
        });
        private long? airJumpAt;
        public bool AirJumpUsed=>airJumpAt.HasValue;
        public bool DidAirJumpThisFrame=>airJumpAt==Frame;
        public OutsideCoreStateData CaptureState()=>new OutsideCoreStateData{TrialMapId=TrialMapId,AirJumpAt=airJumpAt,DefinitionKey=StateDefinitionKey(),Position=Position,Facing=Facing,Frame=Frame,Height=Height,Velocity=VerticalVelocity,SupportId=SupportId,Moving=IsMoving,MovementFrame=MovementFrame,IdleFrame=IdleFrame,Remainder=remainder,DashStartedAt=dashStartedAt,DashEndsAt=dashEndsAt,CooldownUntil=cooldownUntil,LastDashAt=lastDashAt,WallKickAt=wallKickAt,ConsecutiveDashes=consecutiveDashes,KickAway=kickAway,DashJumping=IsDashJumping,WallSliding=IsWallSliding,WallSlideFrame=WallSlideFrame,WallContact=WallContactDirection,GaugeMaximum=CardGauge.Maximum,GaugeRecovery=CardGauge.RecoveryPerSecond,GaugeUnits=CardGauge.CurrentUnits,Afterimages=afterimages.Select(v=>new OutsideAfterimageStateData{X=v.X,Y=v.Y,Frame=v.Frame,Facing=v.Facing,Pose=v.Pose}).ToArray()};
        internal long MaximumReachableHeight()
        {
            var p=TraversalProfile;long top=Definition.LandingSurfaces.Select(s=>s.Top).DefaultIfEmpty(0).Max();if(p!=null){top=Math.Max(top,p.Walls.Select(a=>a.Top).DefaultIfEmpty(0).Max());top=Math.Max(top,p.Waters.Select(a=>a.Top).DefaultIfEmpty(0).Max());}long gravity=p?.WaterGravity??ValidationGravity,rise=Math.Max(RiseBound(ValidationJumpVelocity,gravity),RiseBound(p?.WallKickVelocity??ValidationJumpVelocity,gravity));return checked(top+rise+(p?.DoubleJumpUnlocked==true?RiseBound(ValidationJumpVelocity,gravity):0));
        }
        private void ValidateState(OutsideCoreStateData v)
        {
            if(v==null)throw new ArgumentException("外界Core保存が欠落しています");v.Validate();var p=TraversalProfile;
            if(v.AirJumpAt.HasValue&&(p?.DoubleJumpUnlocked!=true||v.AirJumpAt.Value<1||v.AirJumpAt.Value>v.Frame||v.SupportId!=null))throw new ArgumentException("空中ジャンプ保存が地形/接地/時計と一致しません");
            int reserve=p==null?1:Math.Max(Math.Max(p.DashFrames,p.DashCooldownFrames),Math.Max(p.DashResetFrames,p.WallKickMotionFrames));
            long maxHeight=MaximumReachableHeight(),maxVelocity=Math.Max(ValidationJumpVelocity,p?.WallKickVelocity??0);
            if(p==null&&(v.DashJumping||v.WallSliding||v.DashStartedAt!=-1||v.DashEndsAt!=-1||v.CooldownUntil!=-1||v.LastDashAt!=-1||v.WallKickAt!=-1||v.ConsecutiveDashes!=0||v.KickAway!=0||v.WallContact!=0||v.Afterimages.Length!=0)||v.DashJumping&&(p==null||v.DashStartedAt<0||v.WallKickAt>=0)||v.DashEndsAt>=0&&v.CooldownUntil>=0||v.WallSliding&&(p==null||!p.Walls.Any(w=>w.AllowsSlide&&v.Height<w.Top&&v.Height>w.Bottom-p.BodyHeight&&v.Position==w.X-v.WallContact*p.BodyHalfWidth)))throw new ArgumentException("外界の行動と壁接触が保存定義と矛盾しています");
            if(v.DefinitionKey!=StateDefinitionKey()||v.Position>Definition.Width||v.Height>maxHeight||v.Height< -(p?.VoidDepth??0)||v.Velocity< -Math.Max(maxHeight,p?.VoidDepth??0)||v.Velocity>maxVelocity||v.Frame>long.MaxValue-reserve-1||v.DashStartedAt>v.Frame||v.LastDashAt>v.Frame||v.WallKickAt>v.Frame||v.CooldownUntil>v.Frame+(p?.DashCooldownFrames??0)||v.DashEndsAt>v.Frame+(p?.DashFrames??0)||v.DashEndsAt>=0&&(p==null||v.DashEndsAt<=v.Frame||v.DashStartedAt<0||v.DashEndsAt-v.DashStartedAt!=p.DashFrames||v.ConsecutiveDashes==0)||v.CooldownUntil>=0&&(v.CooldownUntil<=v.Frame||v.ConsecutiveDashes!=2)||v.SupportId!=null&&(v.Velocity!=0||v.SupportId!=SupportAt(v.Position,v.Height))||v.SupportId!=null&&(v.DashJumping||v.WallSliding||v.WallKickAt>=0)||v.WallKickAt>=0&&(p==null||v.KickAway==0)||v.WallSliding&&(p?.WallActionsUnlocked!=true||v.WallContact==0||v.SupportId!=null||v.Velocity>=0)||v.Afterimages.Any(a=>a.X>Definition.Width||a.Y>maxHeight||a.Y< -(p?.VoidDepth??0)))throw new ArgumentException("外界Core保存と地形/行動定義が一致しません");
        }
        internal void RestoreState(OutsideCoreStateData v)
        {
            ValidateState(v);TrialMapId=v.TrialMapId;Position=v.Position;Facing=v.Facing;Frame=v.Frame;Height=v.Height;VerticalVelocity=v.Velocity;SupportId=v.SupportId;IsMoving=v.Moving;MovementFrame=v.MovementFrame;IdleFrame=v.IdleFrame;remainder=v.Remainder;
            dashStartedAt=v.DashStartedAt;dashEndsAt=v.DashEndsAt;cooldownUntil=v.CooldownUntil;lastDashAt=v.LastDashAt;wallKickAt=v.WallKickAt;consecutiveDashes=v.ConsecutiveDashes;kickAway=v.KickAway;IsDashJumping=v.DashJumping;IsWallSliding=v.WallSliding;WallSlideFrame=v.WallSlideFrame;WallContactDirection=v.WallContact;
            // 入力世代は再生しない。既存の再開neutral契約で新しい押下を受ける。
            LastWaterContactHeight=null;airJumpAt=v.AirJumpAt;jumpWasHeld=dashWasHeld=false;CardGauge.Restore(v.GaugeMaximum,v.GaugeRecovery,v.GaugeUnits);afterimages.Clear();afterimages.AddRange(v.Afterimages.Select(a=>new OutsideAfterimage(a.X,a.Y,a.Frame,a.Facing,a.Pose)));
        }
    }
    /// <summary>登録済み試用地形の死亡復帰候補。Rootを触らず、既存回復処理と専用乱数を使う。</summary>
    public static class OutsideFirstRecoveryFactory
    {
        internal static OutsideExplorationCore RecoveryCore(OutsideCoreStateData state)
        {
            var profiles=new OutsideTraversalProfile[]{OutsideTraversalProfile.ValidationWater,OutsideTraversalProfile.ValidationDoubleJump,OutsideTraversalProfile.ValidationPit,OutsideTraversalProfile.ValidationWall(true),OutsideTraversalProfile.ValidationWall(false),OutsideTraversalProfile.ValidationDash,null};
            foreach(var profile in profiles){var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,profile);if(core.StateDefinitionKey()==state.DefinitionKey){core.RestoreState(state);return core;}}
            throw new ArgumentException("死亡復帰先の外界地形定義が未登録です");
        }
        public static OutsideRecoveryData Create(OutsideDepartureVitals entry,string point,CheckpointDefinitions definitions,string run)
        {
            entry.Validate();if(entry.Retry||entry.Template==null||entry.Runtime==null||!Guid.TryParseExact(run,"N",out _))throw new ArgumentException("初回の入場時個体とRuntimeが必要です");
            var geometry=RecoveryCore(entry.Runtime.Core);var actor=entry.Template.Copy();actor.Id=entry.Runtime.Id;
            using(var candidate=new OutsideExplorationSession(trialContactDamage:true)){
                candidate.Core.ConfigureTraversal(geometry.TraversalProfile);candidate.RestoreSavedState(actor,entry.Runtime,point,definitions,geometry.TraversalProfile);
                candidate.RecoverOutside(point);var recovered=OutsidePlayerSaveData.ForRecovery(candidate.Player);recovered.Id=run;var runtime=candidate.CaptureRuntime(definitions);runtime.Id=run;
                var result=new OutsideRecoveryData{PointId=point,SourcePlayerKey=entry.Template.Key(),SourceRuntimeKey=entry.Runtime.Key(),Player=recovered,Runtime=runtime};result.Validate();return result;
            }
        }
    }
    public sealed partial class OutsideExplorationSession
    {
        private readonly System.Collections.Generic.Dictionary<string,long> itemCooldowns=new System.Collections.Generic.Dictionary<string,long>();
        private bool usingItem;private string appliedItemStateId;
        /// <summary>扉移動は候補を別Sessionで作り、保存成功後にだけ現在の世代へ反映する。</summary>
        public bool TryTravelThroughDoor(ClearReturnSession save,CheckpointDefinitions definitions,out string error)=>TryTravelOutside(save,definitions,false,out error);
        // 訪問済みの登録回復地点だけ。扉の距離/接地や攻撃可能状態を持ち込まない。
        public bool TryFastTravelToRecovery(ClearReturnSession save,CheckpointDefinitions definitions,out string error)=>TryTravelOutside(save,definitions,true,out error);
        private bool TryTravelOutside(ClearReturnSession save,CheckpointDefinitions definitions,bool recovery,out string error)
        {
            error=null;if(usingItem||save==null||definitions==null||!IsPresented||Player==null||Player.IsDead||(recovery&&(Core.HasFallenOut||Player.HasPendingHpFrame))||(recovery?!save.IsOutsideRecoveryPointDiscovered(OutsideTrialRoute.RecoveryPointId):!CanPerformAttack||!OutsideTrialRoute.CanUseDoor(Core))){error="今はマップ移動できません";return false;}
            usingItem=true;
            try{
                var cards=Cards?.CaptureState(definitions);if(cards==null||cards.SourceKey==null||cards.Cards.Length!=ExplorationDeckRules.RequiredCards){error="探索用30枚デッキを登録してください";return false;}
                string point=recovery?OutsideTrialRoute.RecoveryPointId:Core.TrialMapId==null?OutsideTrialRoute.DoorAnnexPoint:OutsideTrialRoute.DoorHomePoint;
                using(var candidate=new OutsideExplorationSession(trialContactDamageEnabled)){
                    candidate.Core.ConfigureTraversal(Core.TraversalProfile);candidate.Core.CardGauge.Configure(Core.CardGauge.Maximum,Core.CardGauge.RecoveryPerSecond);
                    candidate.TryInitializePlayer(()=>OutsidePlayerSaveData.ForRecovery(Player).Restore());candidate.Cards=OutsideExplorationCards.RestoreState(cards,definitions);candidate.RecoverOutside(point);
                    var actor=OutsidePlayerSaveData.Capture(candidate.Player);var runtime=candidate.CaptureRuntime(definitions);runtime.Id=actor.Id;
                    if(!(recovery?save.TryCommitOutsideFastTravel(actor,runtime,out error):save.TryCommitOutsideMapTravel(point,actor,runtime,out error)))return false;
                    current?.Dispose();normalKick?.Reset();trialStrike=null;Array.Clear(kickImpacts,0,kickImpacts.Length);RestoreSavedState(actor,runtime,point,definitions);
                    // 空CDも同世代として受領する。後の再表示で古い時計へ適用しない。
                    appliedItemStateId=actor.Id;return true;
                }
            }catch(Exception e)when(e is ArgumentException||e is OverflowException){error=e.Message;return false;}
            finally{usingItem=false;}
        }
        public void RestoreItemCooldowns(OutsideItemStateData state,OutsidePlayerSaveData actor,OutsideRuntimeSaveData runtime)
        {
            if(state==null)return;state.Validate(actor,runtime);if(appliedItemStateId==actor.Id)return;
            if(savedRuntimeId!=actor.Id||Core.Frame!=runtime.Core.Frame)throw new ArgumentException("アイテムCDと復元時計が一致しません");
            var prepared=state.Cooldowns.ToDictionary(v=>v.Id,v=>checked(Core.Frame+v.RemainingFrames));itemCooldowns.Clear();foreach(var pair in prepared)itemCooldowns[pair.Key]=pair.Value;appliedItemStateId=actor.Id;
        }
        public int ItemCooldownRemaining(string id)=>id!=null&&itemCooldowns.TryGetValue(id,out var until)?(int)System.Math.Max(0,until-Core.Frame):0;
        public bool TryUseRecoveryItem(ClearReturnSession save,OutsideRecoveryItemDefinition definition,CheckpointDefinitions definitions,out string error)
        {
            error=null;
            if(usingItem||save==null||definition==null||!IsPresented||!CanPerformAttack||!Player.CanCaptureTimedAilments){error="今はアイテムを使用できません";return false;}
            if(ItemCooldownRemaining(definition.Id)>0){error="クールタイム中です";return false;}
            int healHp=definition.ResolveHealHp(Player.MaxHp);
            bool heals=definition.RestoresHp&&healHp>0&&Player.CurrentHp<Player.MaxHp;
            bool cures=definition.ClearsPoison&&Player.HasPoisonState;
            if(!heals&&!cures){error=definition.Effect==OutsideRecoveryEffect.Poison?"解除する毒状態がありません":definition.Effect==OutsideRecoveryEffect.Hp?(Player.CurrentHp==Player.MaxHp?"HPは満タンです":"回復量が0です"):(Player.CurrentHp==Player.MaxHp?"HPは満タンで、解除するドク状態もありません":"回復量が0で、解除するドク状態もありません");return false;}
            usingItem=true;
            try{
                long until=checked(Core.Frame+definition.CooldownFrames);
                var actor=OutsidePlayerSaveData.Capture(Player);if(cures)actor.Player.Poisons=Array.Empty<PlayerPoisonDto>();if(heals)actor.Player.Hp=(int)System.Math.Min(actor.Player.MaxHp,(long)actor.Player.Hp+healHp);actor.Validate();
                var runtime=CaptureItemRuntime(definitions);runtime.Id=actor.Id;
                var cooldowns=itemCooldowns.Where(v=>v.Value>Core.Frame&&v.Key!=definition.Id).Select(v=>new OutsideItemCooldownData{Id=v.Key,RemainingFrames=checked((int)(v.Value-Core.Frame))}).Concat(new[]{new OutsideItemCooldownData{Id=definition.Id,RemainingFrames=definition.CooldownFrames}}).ToArray();
                var itemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key(),Cooldowns=cooldowns};itemState.Validate(actor,runtime);
                if(!save.TryCommitOutsideItem(definition.Id,actor,runtime,itemState,out error))return false;
                // 保存成功後だけ現個体へ効果を適用。個体/時計/手札を置換しない。
                itemCooldowns[definition.Id]=until;appliedItemStateId=actor.Id;if(cures)Player.ClearPoisons();if(heals)Player.Heal(healHp);savedPlayerId=actor.Id;savedPlayerKey=actor.Key();savedRuntimeId=runtime.Id;savedRuntimeKey=runtime.Key();return true;
            }catch(Exception e)when(e is ArgumentException||e is OverflowException){error=e.Message;return false;}
            finally{usingItem=false;}
        }
        // 試用地形だけの明示付与。正式な毒源/進行条件や他状態の探索規則を定義しない。
        public const string TrialPoisonKind="validation-voluntary-poison-v1";
        public const int TrialPoisonDurationFrames=1800,TrialPoisonIntervalFrames=600,TrialPoisonDamage=1;
        private static StatusEffect TrialPoisonEffect()=>new StatusEffect(StatusEffectType.Poison,checked(TrialPoisonDurationFrames*1000/60),TrialPoisonDamage);
        public bool CanTryTrialPoison=>!IsEnded&&!usingItem&&IsPresented&&CanPerformAttack&&Player.CanCaptureTimedAilments&&!Player.HasPoisonState&&OutsideTrialRoute.CanTryPoison(Core);
        public bool TryApplyTrialPoison(ClearReturnSession save,CheckpointDefinitions definitions,out string error)
        {
            error=null;if(save==null||!CanTryTrialPoison){error="今は試用毒を付与できません";return false;}usingItem=true;
            try{
                var candidate=OutsidePlayerSaveData.Capture(Player).Restore();candidate.ApplyPoison(TrialPoisonKind,TrialPoisonEffect(),TrialPoisonIntervalFrames);
                var actor=OutsidePlayerSaveData.Capture(candidate);if(!actor.Player.Poisons.Any(v=>v.Kind==TrialPoisonKind)){error="毒耐性により付与されません";return false;}
                var runtime=CaptureItemRuntime(definitions);runtime.Id=actor.Id;
                var itemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key(),Cooldowns=itemCooldowns.Where(v=>v.Value>Core.Frame).Select(v=>new OutsideItemCooldownData{Id=v.Key,RemainingFrames=checked((int)(v.Value-Core.Frame))}).ToArray()};itemState.Validate(actor,runtime);
                if(!save.TryCommitOutsideTrialPoison(actor,runtime,itemState,out error))return false;
                Player.ApplyPoison(TrialPoisonKind,TrialPoisonEffect(),TrialPoisonIntervalFrames);appliedItemStateId=actor.Id;savedPlayerId=actor.Id;savedPlayerKey=actor.Key();savedRuntimeId=runtime.Id;savedRuntimeKey=runtime.Key();return true;
            }catch(Exception e)when(e is ArgumentException||e is OverflowException){error=e.Message;return false;}
            finally{usingItem=false;}
        }
        private OutsideRuntimeSaveData CaptureItemRuntime(CheckpointDefinitions definitions)
            => BuildRuntimeSnapshot(definitions,CaptureKickState());
        // 呼出元の退避可否判定は保ち、通常退避と即時保存の項目集合だけを共有する。
        private OutsideRuntimeSaveData BuildRuntimeSnapshot(CheckpointDefinitions definitions,OutsideKickStateData kick)
        {
            if(PairStage445>0)throw new InvalidOperationException("連戦試用は保存しません");
            if((goblinTrial!=null||slimeTrial!=null)&&(pendingGoblinStrike||pendingSlimeLanding||Player.HasPendingHpFrame))throw new InvalidOperationException("試用敵の被害確定前には保存できません");
            var value=new OutsideRuntimeSaveData{Id=Guid.NewGuid().ToString("N"),Core=Core.CaptureState(),FixedCameraCenter=Camera.FixedCenter,Cards=Cards?.CaptureState(definitions),Attack=attackState?.Copy(),TrialTarget=CaptureTrialTarget(),SecondTrialTarget=CaptureSecondTrialTarget(),NormalKick=kick,ChargeKick=normalKick?.IsCharged==true?normalKick.ActiveChargePlan.Copy():null,Fall=CaptureFall(),Water=CaptureWater(),TrialBarrierHp=CaptureTrialBarrier(),TrialEnemyAttack=trialEnemyAttack?.Copy(),GoblinTrial=goblinTrial?.Copy(),SlimeTrial=slimeTrial?.Copy(),Knockback=knockback?.Copy(),TrialContact=CaptureTrialContact(),InitialShots=CaptureInitialShots(),Bombs=CaptureBombs(),HitStopFrames=HitStopRemainingFrames};value.Validate();return value;
        }
        private bool startupClassified,startupRecoveryPending;
        // 起動時の行き先を一度だけ記録する。host再生成や帰還後の再表示で分類し直さない。
        public void ClassifyStartupRestore(bool continueOutside)
        {if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));if(startupClassified)return;startupClassified=true;startupRecoveryPending=continueOutside&&Player==null;}
        /// <summary>通常continueだけ復帰地点へ回復する。codecの厳密復元や攻略中の帰還とは分ける。</summary>
        public bool RestoreForPresentation(OutsidePlayerSaveData player,OutsideRuntimeSaveData runtime,string recoveryPoint,CheckpointDefinitions definitions=null,OutsideTraversalProfile initializationProfile=null,OutsideItemStateData itemState=null)
        {
            if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));itemState?.Validate(player,runtime);
            if(!startupRecoveryPending||Player!=null){bool changed=RestoreSavedState(player,runtime,recoveryPoint,definitions,initializationProfile);RestoreItemCooldowns(itemState,player,runtime);return changed;}
            if(player==null)throw new ArgumentException("通常再開には保存個体が必要です");
            string actorKey=player.Key(),runtimeKey=runtime?.Key();OutsidePlayerSaveData recovered;OutsideRuntimeSaveData recoveredRuntime;
            // 定義・地点・状態・専用乱数を別Sessionで検証し、再装填結果を一度だけ採用する。
            using(var candidate=new OutsideExplorationSession(trialContactDamageEnabled)){
                candidate.TryConfigureTraversal(initializationProfile??Core.TraversalProfile);
                candidate.RestoreSavedState(player,runtime,recoveryPoint,definitions);
                candidate.RecoverOutside(recoveryPoint);recovered=OutsidePlayerSaveData.Capture(candidate.Player);recoveredRuntime=candidate.CaptureRuntime(definitions);recoveredRuntime.Id=recovered.Id;
            }
            RestoreSavedState(recovered,recoveredRuntime,recoveryPoint,definitions,initializationProfile);
            // 元保存のIDと内容をackし、再表示で旧HP/手札へ戻したり再抽選したりしない。
            savedPlayerId=player.Id;savedPlayerKey=actorKey;savedRuntimeId=runtime?.Id;savedRuntimeKey=runtimeKey;
            // 通常continueのCDは既存の保存復元契約を維持する。再表示で残秒を戻さない。
            RestoreItemCooldowns(itemState,player,runtime);startupRecoveryPending=false;return true;
        }
        // 通常状態の既存許可は維持し、追加は明示試用カエンの登録耐性との組だけ。
        // geometry/time keyは共通。火属性は保存済みActorが所有し、正式神依の性能とはしない。
        private static bool HasRegisteredDash(PlayerCharacter actor)
        {
            if(actor==null)return false;var style=actor.DepartureStyle;if(style.Id==DepartureStyleDefinition.BasicId)return true;
            var trial=DepartureStyleDefinition.TrialFire;var attack=style.Attack;var registered=trial.Attack;
            return style.Id==trial.Id&&style.Name==trial.Name&&style.RequiredClear==trial.RequiredClear&&style.TransformationPhaseBonus==trial.TransformationPhaseBonus&&attack!=null&&attack.Element==registered.Element&&attack.NormalPowerPercent==registered.NormalPowerPercent&&attack.ChargedPowerPercent==registered.ChargedPowerPercent&&attack.ChargeFrames==registered.ChargeFrames&&attack.IntervalFrames==registered.IntervalFrames&&attack.Timing==registered.Timing&&actor.BaseElementResistances.HasSameValues(ElementAffinity.CreatePlayerBase(ElementType.Fire));
        }
        private string savedRuntimeId,savedRuntimeKey;
        private int CaptureTrialBarrier(){if(Player==null)return 0;if(Player.HasPendingHpFrame||Player.HasAura||Player.ProtectionRemainingFrames.HasValue||Player.BarrierHp>15||Player.BarrierHp>0&&Core.Definition.Id!="validation-landing-32")throw new ArgumentException("未登録の外界防御は保存できません");return Player.BarrierHp;}
        private OutsideKickStateData CaptureKickState(){var value=normalKick?.CaptureState(Core.Frame,Core.IsGrounded,Core.Facing);if(normalKick?.IsCharged==true)normalKick.ActiveChargePlan.ValidateActor(Player);if(value!=null&&Core.Definition.Id!=OutsideKickTrialDefinition.MapId)throw new ArgumentException("このマップの蹴り保存は未登録です");if(value?.Kind==OutsideKickKind.Dash&&!HasRegisteredDash(Player))throw new ArgumentException("この神依のダッシュ攻撃保存は未登録です");if(value!=null&&!BodyCanAttack)throw new ArgumentException("行動不能中の蹴り保存は行いません");return value;}
        public OutsideRuntimeSaveData CaptureRuntime(CheckpointDefinitions definitions=null)
        {
            var kick=CaptureKickState();
            if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));if(IsPresented)throw new InvalidOperationException("外界表示を閉じて退避してください");if(Core.HasFallenOut)throw new ArgumentException("死亡済み落下を生存状態として保存できません");
            return BuildRuntimeSnapshot(definitions,kick);
        }
        public bool RestoreSavedState(OutsidePlayerSaveData player,OutsideRuntimeSaveData runtime,string recoveryPoint,CheckpointDefinitions definitions=null,OutsideTraversalProfile initializationProfile=null)
        {
            if(runtime==null)return RestoreSavedPlayer(player,recoveryPoint);if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));runtime.Validate();OutsideTrialRoute.ValidateMapRecovery(runtime.Core.TrialMapId,recoveryPoint);player?.Validate();if(player==null||player.Id!=runtime.Id)throw new ArgumentException("外界個体とRuntimeの攻略IDが一致しません");string key=runtime.Key(),playerKey=player.Key();
            if(savedRuntimeId==runtime.Id){if(savedRuntimeKey!=key||savedPlayerId!=player.Id||savedPlayerKey!=playerKey)throw new ArgumentException("同じ外界復帰IDの内容が変わりました");return false;}
            if(IsPresented)throw new InvalidOperationException("表示中の外界状態を置換できません");if(savedPlayerId==player.Id&&savedPlayerKey!=playerKey)throw new ArgumentException("同じ外界個体IDの内容が変わりました");
            // 全候補を先に検証し、途中失敗で旧Sessionの一部だけを書き換えない。
            if(initializationProfile!=null&&Core.Frame!=0)throw new ArgumentException("進行済みの外界地形設定を置換できません");
            var actor=player.Restore();actor.SetBarrier(runtime.TrialBarrierHp);var candidateCore=new OutsideExplorationCore(Core.Definition,initializationProfile??Core.TraversalProfile);candidateCore.RestoreState(runtime.Core);if(candidateCore.HasFallenOut)throw new ArgumentException("死亡済み落下を生存個体として復元できません");var camera=runtime.FixedCameraCenter.HasValue?OutsideCameraPolicy.FixedAt(runtime.FixedCameraCenter.Value):OutsideCameraPolicy.Follow;camera.ResolveHorizontalCenter(candidateCore.Position,candidateCore.Definition.Width,1);
            var cards=runtime.Cards==null?null:OutsideExplorationCards.RestoreState(runtime.Cards,definitions);var target=CreateTrialTarget(runtime.TrialTarget);var secondTarget=CreateSecondTrialTarget(runtime.SecondTrialTarget);
            if(runtime.Knockback!=null&&(actor.IsDead||!actor.IsStaggered))throw new ArgumentException("のけぞりのない個体へ外力を復元できません");
            ValidateAttack(runtime.Attack,cards,runtime.Core.Frame);
            if(runtime.Attack!=null&&!SupportsCardLocation(cards.ActionCard(runtime.Attack.CardIndex),runtime.Core.TrialMapId))throw new ArgumentException("このマップに対象カードの発動状態は登録されていません");
            if(runtime.Attack?.BombLaunch!=null&&!runtime.Attack.BombLaunch.MatchesTerrain(candidateCore))throw new ArgumentException("ボムの発射元地形が未対応です");
            foreach(var bomb in runtime.Bombs??Array.Empty<OutsideBombFlightData>())if(actor.IsDead||!bomb.Launch.MatchesTerrain(candidateCore)||runtime.Core.Frame-bomb.FiredAt>=bomb.Launch.Duration)throw new ArgumentException("この個体/地形のボム復元は未対応です");
            if(runtime.InitialShots!=null&&candidateCore.Definition.Id!=OutsideKickTrialDefinition.MapId)throw new ArgumentException("このマップの試用弾復元は未登録です");
            foreach(var shot in runtime.InitialShots??Array.Empty<OutsideInitialShotData>())if(shot.X<0||shot.X>candidateCore.Definition.Width||shot.Height>candidateCore.MaximumReachableHeight()||candidateCore.TraversalProfile.VoidDepth>0&&shot.Height<=-candidateCore.TraversalProfile.VoidDepth)throw new ArgumentException("探索弾の開始位置がマップ外です");
            if((runtime.TrialBarrierHp>0||runtime.TrialEnemyAttack!=null)&&Core.Definition.Id!="validation-landing-32")throw new ArgumentException("このマップの試用バリアは未登録です");
            if(runtime.TrialContact!=null&&(!trialContactDamageEnabled||Core.Definition.Id!=OutsideKickTrialDefinition.MapId))throw new ArgumentException("このSessionの接触定義は未登録です");
            if(runtime.NormalKick?.Kind==OutsideKickKind.Dash&&!HasRegisteredDash(actor))throw new ArgumentException("この神依のダッシュ攻撃復元は未登録です");
            if(runtime.NormalKick!=null){if(Core.Definition.Id!=OutsideKickTrialDefinition.MapId)throw new ArgumentException("このマップの蹴り復元は未登録です");var candidateKick=new OutsideNormalKickSequence(new Olca.Core.Logic.Battle.AttackInputReservation());candidateKick.Configure(OutsideKickTrialDefinition.Timings());candidateKick.ConfigureDash(OutsideKickTrialDefinition.DashTiming());candidateKick.RestoreState(runtime.NormalKick,candidateCore.Frame,candidateCore.IsGrounded,candidateCore.Facing);runtime.ChargeKick?.ValidateActor(actor);candidateKick.RestoreChargePlan(runtime.ChargeKick);if(actor.IsDead||actor.IsParalyzed||actor.IsStaggered||runtime.Fall?.LockedUntil>candidateCore.Frame)throw new ArgumentException("行動不能中の蹴り保存は復元できません");}
            if(runtime.SlimeTrial!=null&&(!SlimeSafeSpan(candidateCore,runtime.SlimeTrial.StartX,runtime.SlimeTrial.LandingX)||actor.IsDead))throw new ArgumentException("跳躍スライムの復元地形/個体が未対応です");
            if(runtime.GoblinTrial!=null&&(candidateCore.Definition.Id!="validation-landing-32"||!GoblinSafePosition(candidateCore,runtime.GoblinTrial.X)||actor.IsDead))throw new ArgumentException("接近ゴブリンの復元地形/個体が未対応です");
            ValidateWater(runtime.Water,candidateCore);
            if(runtime.Fall!=null&&runtime.Fall.Peak>candidateCore.MaximumReachableHeight())throw new ArgumentException("落下頂点が外界地形の上限を超えています");
            if(Cards!=null&&runtime.Cards!=null){try{if(Cards.CaptureState(definitions).Key()==runtime.Cards.Key())cards=Cards;}catch(ArgumentException){}}
            // Runtimeに地点利用履歴はないため、復元時の旧横範囲推定は管理開始前も保持する。
            if(initializationProfile!=null)Core.ConfigureTraversal(initializationProfile);Core.RestoreState(runtime.Core);deckLocationArmed=OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=deckLocationArmed&&OutsideTrialRoute.IsReloadZone(Core);itemCooldowns.Clear();ResetCardUseFeedback();initialShots.Clear();bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();initialShots.AddRange((runtime.InitialShots??Array.Empty<OutsideInitialShotData>()).Select(v=>v.Copy()));bombs.AddRange((runtime.Bombs??Array.Empty<OutsideBombFlightData>()).Select(v=>v.Copy()));attackState=runtime.Attack?.Copy();if(runtime.NormalKick!=null){NormalKick.Reset();NormalKick.Configure(OutsideKickTrialDefinition.Timings());NormalKick.ConfigureDash(OutsideKickTrialDefinition.DashTiming());NormalKick.RestoreState(runtime.NormalKick,Core.Frame,Core.IsGrounded,Core.Facing);NormalKick.RestoreChargePlan(runtime.ChargeKick);}trialTarget=target;secondTrialTarget=secondTarget;secondTrialHits=runtime.SecondTrialTarget?.Hits??0;trialTargetChanged=runtime.TrialTarget!=null;trialTargetHits=runtime.TrialTarget?.Hits??0;Player=actor;trialEnemyAttack=runtime.TrialEnemyAttack?.Copy();goblinTrial=runtime.GoblinTrial?.Copy();pendingGoblinStrike=false;slimeTrial=runtime.SlimeTrial?.Copy();pendingSlimeLanding=false;knockback=runtime.Knockback?.Copy();trialContactNextFrame=runtime.TrialContact?.NextFrame??0;secondTrialContactNextFrame=runtime.TrialContact?.SecondNextFrame??0;Cards=cards;Camera=camera;playerFrame=Core.Frame;HitStopRemainingFrames=runtime.HitStopFrames;RestoreFall(runtime.Fall);RestoreWater(runtime.Water);savedPlayerId=player.Id;savedPlayerKey=playerKey;savedRuntimeId=runtime.Id;savedRuntimeKey=key;PrepareMapEntry();return true;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>仕様で調整を許された試用値。移動定義・旧Core hashとは独立。</summary>
    public static class OutsideFallRules
    {
        public const long SafeHeight=600000; // 調整用10 world。通常二段ジャンプ最大6.2に加えて余裕を設ける。
        public const int PercentPerWorld=10, LandingFrames=12;
        public static int Damage(long distance,int maximumHp)
        {
            if(distance<0||maximumHp<=0)throw new ArgumentOutOfRangeException(nameof(distance));
            if(distance<=SafeHeight)return 0;
            decimal ratio=(decimal)(distance-SafeHeight)/OutsideMovementDefinition.UnitsPerWorldUnit*PercentPerWorld/100;
            return ratio>=((decimal)int.MaxValue/maximumHp)?int.MaxValue:(int)decimal.Floor(ratio*maximumHp);
        }
    }
    public sealed class OutsideFallStateData
    {
        public long Peak,LockedUntil;public bool Grounded;
        public OutsideFallStateData Copy()=>(OutsideFallStateData)MemberwiseClone();
        public void Validate(OutsideCoreStateData core)
        {
            if(Peak<core.Height||LockedUntil<0||LockedUntil>core.Frame&&LockedUntil-core.Frame>OutsideFallRules.LandingFrames||Grounded!=(core.SupportId!=null)||Grounded&&Peak!=core.Height||LockedUntil>core.Frame&&!Grounded)throw new ArgumentException("落下履歴と接地/硬直の保存が不正です");
        }
    }
    public sealed partial class OutsideExplorationSession
    {
        private long fallPeak,landingLockedUntil;private bool fallGrounded=true;
        public bool IsLandingLocked=>Core.Frame<landingLockedUntil;
        public bool IsLandingLockedForNextFrame=>Core.Frame<landingLockedUntil-1;
        public bool IsMovementLockedForNextFrame=>IsLandingLockedForNextFrame||Player?.IsMovementPreventedNextFrame==true;
        public int LandingFramesRemaining=>(int)Math.Max(0,landingLockedUntil-Core.Frame);
        private void ResetFall(){fallPeak=Core.Height;fallGrounded=Core.IsGrounded;landingLockedUntil=0;ResetWater();}
        private void RestoreFall(OutsideFallStateData value)
        {
            // 旧空中保存は未知の過去頂点を推測しない。復元高度から追跡を開始する。
            ResetFall();if(value==null)return;fallPeak=value.Peak;fallGrounded=value.Grounded;landingLockedUntil=value.LockedUntil;
        }
        private OutsideFallStateData CaptureFall()
        {
            if(Core.IsGrounded&&!IsLandingLocked)return null;
            var value=new OutsideFallStateData{Peak=Math.Max(fallPeak,Core.Height),Grounded=Core.IsGrounded,LockedUntil=IsLandingLocked?landingLockedUntil:0};value.Validate(Core.CaptureState());return value;
        }
        private void AdvanceFall()
        {
            if(Core.HasFallenOut){Player.TakeFallDamage(Player.MaxHp);Core.StopTraversalForLanding();attackState=null;CancelAttackReservation();CardUseMessage="ラクカ / フッキ";CardUseMessageFrame=Core.Frame;return;}
            bool ground=Core.IsGrounded;
            if(Core.LastWaterContactHeight.HasValue)fallPeak=Core.LastWaterContactHeight.Value;
            if(Core.DidAirJumpThisFrame||Core.IsWallSliding||Core.IsWallKicking&&Core.WallKickFrame==0)fallPeak=Core.Height;
            fallPeak=Math.Max(fallPeak,Core.Height);
            if(ground&&!fallGrounded&&!Core.IsInWater){
                int damage=OutsideFallRules.Damage(fallPeak-Core.Height,Player.MaxHp);
                if(damage>0){Player.TakeFallDamage(damage);landingLockedUntil=checked(Core.Frame+OutsideFallRules.LandingFrames);Core.StopTraversalForLanding();attackState=null;CancelAttackReservation();CardUseMessage="ラクカ "+damage+" / チャクチコウチョク";CardUseMessageFrame=Core.Frame;}
            }
            fallGrounded=ground;if(ground)fallPeak=Core.Height;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>独立寿命の試用直進弾。デッキ再装填や発射者の行動中断から独立した確定済み威力。</summary>
    public sealed class OutsideInitialShotData
    {
        public string CardId;public long X,Height,StartedAt;public int Facing,Power;
        public OutsideInitialShotData Copy()=>(OutsideInitialShotData)MemberwiseClone();
        public long Position(long frame)=>checked(X+Math.Max(0,Math.Min(60,frame-StartedAt))*8000*Facing);
        public void Validate(long frame){if(!TrialInitialCardDefinitions.IsShot(CardId)||Power<0||X<0||Facing!=1&&Facing!=-1||StartedAt<0||StartedAt>frame||frame-StartedAt>=60)throw new ArgumentException("試用直進弾の保存値が不正です");Position(frame);}
    }
    public sealed partial class OutsideExplorationSession
    {
        private readonly List<OutsideInitialShotData> initialShots=new List<OutsideInitialShotData>();
        public IReadOnlyList<OutsideInitialShotData> InitialShots=>initialShots.Select(v=>v.Copy()).ToArray();
        private OutsideInitialShotData[] CaptureInitialShots(){
            if(initialShots.Count==0)return null;
            if(Core.Definition.Id!=OutsideKickTrialDefinition.MapId)throw new ArgumentException("このマップの試用弾保存は未登録です");
            return initialShots.Select(v=>v.Copy()).ToArray();
        }

        private bool TryInitialPower(Card card,out int? power,out string error)
        {
            power=null;error=null;if(!InitialDefinition(card))return true;
            try{power=card.GetFinalPower(Player.AttackPower);if(power<0)throw new OverflowException();return true;}
            catch(OverflowException){error="攻撃力が試用カードの範囲外です";return false;}
        }
        private void EmitInitialShot(Card card,int power)=>initialShots.Add(new OutsideInitialShotData{CardId=card.Id,X=Core.Position,Height=Core.Height,StartedAt=Core.Frame,Facing=Core.Facing,Power=power});
        private void AdvanceInitialShots()
        {
            for(int i=initialShots.Count-1;i>=0;i--){
                var shot=initialShots[i];long age=Core.Frame-shot.StartedAt,current=shot.Position(Core.Frame),prior=shot.Position(Math.Max(shot.StartedAt,Core.Frame-1));
                bool InRange(long x,Enemy target,long y)=>!target.IsDead&&shot.Height-y>=-TrialSwordVerticalRange&&shot.Height-y<=TrialSwordVerticalRange&&(x-shot.X)*shot.Facing>=0&&x>=Math.Min(prior,current)&&x<=Math.Max(prior,current);
                // 同じフレームの壁と敵は壁を優先。地形は現在のマップの垂直面だけを読む試用判定。
                long? blocked=null;
                foreach(var wall in Core.TraversalProfile?.Walls??Array.Empty<OutsideValidationWall>()){
                    long center=checked(shot.Height+72000);if(center<wall.Bottom||center>wall.Top||wall.X<Math.Min(prior,current)||wall.X>Math.Max(prior,current)||(wall.X-shot.X)*shot.Facing<0)continue;
                    if(!blocked.HasValue||(wall.X-shot.X)*shot.Facing<(blocked.Value-shot.X)*shot.Facing)blocked=wall.X;
                }
                var hits=new List<(Enemy Enemy,long X,int Id)>();
                void Add(long x,Enemy enemy,int id){if(InRange(x,enemy,id==0?FirstTrialTargetHeight:0)&&(!blocked.HasValue||(x-shot.X)*shot.Facing<(blocked.Value-shot.X)*shot.Facing))hits.Add((enemy,x,id));}
                Add(FirstTrialTargetX,TrialTarget,0);Add(SecondTrialTargetX,SecondTrialTarget,1);
                var hit=hits.OrderBy(v=>(v.X-shot.X)*shot.Facing).FirstOrDefault();
                if(hit.Enemy!=null){if(hit.Id==0){trialTargetChanged=true;trialTargetHits=(int)Math.Min(int.MaxValue,(long)trialTargetHits+1);}else secondTrialHits=(int)Math.Min(int.MaxValue,(long)secondTrialHits+1);hit.Enemy.TakeDamage(shot.Power,ElementType.None,AttackProperty.Shooting,0,0,hitStopFrames:hitStopSettings.NormalFrames);initialShots.RemoveAt(i);}
                else if(blocked.HasValue||age>=60)initialShots.RemoveAt(i);
            }
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>旧平地v1と暫定地形v2。カード定義やDungeonの3マス投擲を置換しない。</summary>
    public static class OutsideCardPolishTrial
    {
        // 調整可能な試用値。ダンジョン定義と保存済み旧発射計画には適用しない。
        public const string SwordMotionId="trial-outside-sword-swing-v2";
        public const long SwordReach=180000;public const int SwordStartup=8,SwordRecovery=12;
        public const long BombDistance=300000;public const int BombFrames=36;
    }
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideBombLaunchData
    {
        public const string TrialDefinition="trial-outside-flat-bomb-v1",TerrainDefinition="trial-outside-terrain-bomb-v2";
        public const string FastTerrainDefinition="trial-outside-terrain-bomb-v3-fast";
        public const long Distance=120000,Radius=90000;
        public const int FlightFrames=90,BlastFrames=6;
        [System.Runtime.Serialization.DataMember] public string DefinitionId=TrialDefinition;
        [System.Runtime.Serialization.DataMember] public long X;
        [System.Runtime.Serialization.DataMember] public int Facing,Power,HitStopFrames;
        [System.Runtime.Serialization.DataMember] public int? PoiseDamage;
        [System.Runtime.Serialization.DataMember] public ElementType Element;
        [System.Runtime.Serialization.DataMember] public ExceedLevel Level;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideBombTerrainData Terrain;
        public OutsideBombLaunchData Copy(){var copy=(OutsideBombLaunchData)MemberwiseClone();copy.Terrain=Terrain?.Copy();return copy;}
        public long FlightDistance=>DefinitionId==FastTerrainDefinition?OutsideCardPolishTrial.BombDistance:Distance;
        public int FlightDuration=>DefinitionId==FastTerrainDefinition?OutsideCardPolishTrial.BombFrames:FlightFrames;
        public int Duration=>Terrain?.ImpactFrame??FlightDuration;
        public long ImpactX=>Terrain?.ImpactX??Target;
        public long ImpactHeight=>Terrain?.ImpactHeight??0;
        public bool Dissipates=>Terrain?.Dissipates??false;
        public bool MatchesTerrain(OutsideExplorationCore core){if(DefinitionId==TrialDefinition)return Terrain==null&&HasTerrain(core,X,Facing);if(core==null||core.Definition.Id!=OutsideKickTrialDefinition.MapId||!OutsideTrialRoute.KnownMap(core.TrialMapId)||Terrain==null||Terrain.OriginHeight>core.MaximumReachableHeight()||X>core.Definition.Width)return false;return Terrain.SameAs(OutsideBombTerrainData.Create(core,X,Facing,Terrain.OriginHeight,FlightDistance,FlightDuration));}
        public long Target=>checked(X+Facing*FlightDistance);
        public void Validate(){if((DefinitionId!=TrialDefinition&&DefinitionId!=TerrainDefinition&&DefinitionId!=FastTerrainDefinition)||DefinitionId==TrialDefinition&&Terrain!=null||DefinitionId!=TrialDefinition&&Terrain==null||X<0||Facing!=1&&Facing!=-1||Power<0||PoiseDamage<0||HitStopFrames<0||HitStopFrames>60||!Enum.IsDefined(typeof(ElementType),Element)||!Enum.IsDefined(typeof(ExceedLevel),Level)||Level>ExceedLevel.Lv2)throw new ArgumentException("試用ボムの発射値が不正です");checked{_=Target+Radius;_=Target-Radius;}Terrain?.Validate();}
        public static bool HasTerrain(OutsideExplorationCore core,long x,int facing)
        {
            if(core==null||core.Definition.Id!=OutsideKickTrialDefinition.MapId||!OutsideTrialRoute.KnownMap(core.TrialMapId)||x<0||facing!=1&&facing!=-1)return false;
            try{long target=checked(x+facing*Distance),left=Math.Min(x,checked(target-Radius)),right=Math.Max(x,checked(target+Radius));
                if(left<0||right>core.Definition.Width)return false;
                if(core.Definition.LandingSurfaces.Any(v=>v.Left<=right&&v.Right>=left))return false;
                var p=core.TraversalProfile;return p==null||!p.Walls.Any(v=>v.X>=left&&v.X<=right)&&!p.FloorGaps.Any(v=>v.Left<=right&&v.Right>=left)&&!p.Waters.Any(v=>v.Left<=right&&v.Right>=left);
            }catch(OverflowException){return false;}
        }
    }
    /// <summary>暫定v2。90論理フレームの弧を地形と交差させ、結果を消費時に固定する。</summary>
    public sealed class OutsideBombTerrainData
    {
        public long OriginHeight,ImpactX,ImpactHeight;public int ImpactFrame;public bool Dissipates;
        public OutsideBombTerrainData Copy()=>(OutsideBombTerrainData)MemberwiseClone();
        public void Validate(){if(OriginHeight<0||ImpactX<0||ImpactHeight<0||ImpactFrame<1||ImpactFrame>OutsideBombLaunchData.FlightFrames)throw new ArgumentException("ボム地形計画が不正です");}
        public bool SameAs(OutsideBombTerrainData v)=>v!=null&&OriginHeight==v.OriginHeight&&ImpactX==v.ImpactX&&ImpactHeight==v.ImpactHeight&&ImpactFrame==v.ImpactFrame&&Dissipates==v.Dissipates;
        public static OutsideBombTerrainData Create(OutsideExplorationCore core,long x,int facing,long height,long distance=OutsideBombLaunchData.Distance,int flightFrames=OutsideBombLaunchData.FlightFrames)
        {
            if(core==null||x<0||x>core.Definition.Width||height<0||facing!=1&&facing!=-1)throw new ArgumentException("ボム発射元が不正です");
            if(!((distance==OutsideBombLaunchData.Distance&&flightFrames==90)||(distance==OutsideCardPolishTrial.BombDistance&&flightFrames==OutsideCardPolishTrial.BombFrames)))throw new ArgumentException("ボム飛翔定義が未登録です");
            long X(int n)=>checked(x+facing*distance*n/flightFrames);
            long Y(int n)=>checked(height*(flightFrames-n)/flightFrames+4L*n*(flightFrames-n)*84000/(flightFrames*flightFrames));
            var profile=core.TraversalProfile;
            for(int n=1;n<=flightFrames;n++){
                long ax=X(n-1),ay=Y(n-1),bx=X(n),by=Y(n);decimal best=2;bool vanish=false;
                void Contact(decimal t,bool disappears){if(t>=0&&t<=1&&(t<best||t==best&&disappears)){best=t;vanish=disappears;}}
                // 点の足元による閉矩形交差。水域・境界への接触では爆発させない。
                if(profile!=null)foreach(var water in profile.Waters){decimal from=0,to=1;bool Clip(decimal start,decimal delta,decimal low,decimal high){if(delta==0)return start>=low&&start<=high;decimal a=(low-start)/delta,b=(high-start)/delta;if(a>b){decimal t=a;a=b;b=t;}from=Math.Max(from,a);to=Math.Min(to,b);return from<=to;}if(Clip(ax,(decimal)bx-ax,water.Left,water.Right)&&Clip(ay,(decimal)by-ay,water.Bottom,water.Top))Contact(from,true);}
                if(bx<0||bx>core.Definition.Width)Contact(((decimal)(bx<0?0:core.Definition.Width)-ax)/(bx-ax),true);
                if(profile!=null)foreach(var wall in profile.Walls){decimal t=((decimal)wall.X-ax)/(bx-ax);decimal y=ay+((decimal)by-ay)*t;if(y>=wall.Bottom&&y<=wall.Top)Contact(t,false);}
                if(by<ay)foreach(var surface in core.Definition.LandingSurfaces){decimal t=((decimal)surface.Top-ay)/(by-ay);decimal hitX=ax+((decimal)bx-ax)*t;if(hitX>=surface.Left&&hitX<=surface.Right)Contact(t,false);}
                if(n==flightFrames&&best>1){best=1;vanish=profile!=null&&profile.FloorGaps.Any(g=>g.Contains(bx));}
                if(best<=1){var result=new OutsideBombTerrainData{OriginHeight=height,ImpactFrame=n,ImpactX=(long)(ax+((decimal)bx-ax)*best),ImpactHeight=Math.Max(0,(long)(ay+((decimal)by-ay)*best)),Dissipates=vanish};result.Validate();return result;}
            }
            throw new InvalidOperationException("ボム地形計画が終端に到達しません");
        }
    }
    /// <summary>消費時に固定した発射計画を独立寿命で運ぶ。爆発済みは保存せずHP側へ一度だけ確定。</summary>
    public sealed class OutsideBombFlightData
    {
        public OutsideBombLaunchData Launch;public long ActionStartedAt,FiredAt;
        public OutsideBombFlightData Copy()=>new OutsideBombFlightData{Launch=Launch?.Copy(),ActionStartedAt=ActionStartedAt,FiredAt=FiredAt};
        public void Validate(long frame){if(Launch==null||ActionStartedAt<0||FiredAt<ActionStartedAt||FiredAt>frame||frame-FiredAt>=Launch.Duration)throw new ArgumentException("ボム飛翔の時計が不正です");Launch.Validate();}
        public long Position(long frame){long age=Math.Max(0,Math.Min(Launch.FlightDuration,frame-FiredAt));return checked(Launch.X+Launch.Facing*Launch.FlightDistance*age/Launch.FlightDuration);}
        public long Height(long frame){long age=Math.Max(0,Math.Min(Launch.FlightDuration,frame-FiredAt));return (Launch.Terrain?.OriginHeight??0)*(Launch.FlightDuration-age)/Launch.FlightDuration+27000+4*age*(Launch.FlightDuration-age)*84000/(Launch.FlightDuration*Launch.FlightDuration);}
    }
    /// <summary>探索ボムだけの試用遮蔽。有限壁との線分交差と、壁面爆発時の固定発射側を用いる。</summary>
    public static class OutsideBombOcclusion
    {
        public static bool CanReach(IEnumerable<OutsideValidationWall> walls,long sourceX,long x,long height,long targetX,long targetHeight=0)
        {
            if(walls==null)return true;
            foreach(var wall in walls){
                if(targetX==x){if(wall.X==x&&Math.Max(height,targetHeight)>=wall.Bottom&&Math.Min(height,targetHeight)<=wall.Top)return false;continue;}
                decimal t=((decimal)wall.X-x)/((decimal)targetX-x);if(t<0||t>1)continue;
                decimal y=height+((decimal)targetHeight-height)*t;if(y<wall.Bottom||y>wall.Top)continue;
                if(t==0&&((sourceX<wall.X&&targetX<wall.X)||(sourceX>wall.X&&targetX>wall.X)))continue;
                return false;
            }return true;
        }
        // 横へ広がる試用表示用。同じ壁高/発射側で切り、斜めの対敵rayとの違いは正式形状とは区別する。
        public static (long Left,long Right) HorizontalBounds(IEnumerable<OutsideValidationWall> walls,long sourceX,long x,long height)
        {
            long left=x-OutsideBombLaunchData.Radius,right=x+OutsideBombLaunchData.Radius;
            if(walls!=null)foreach(var wall in walls){if(height<wall.Bottom||height>wall.Top)continue;
                if(wall.X==x){if(sourceX<=x)right=x;if(sourceX>=x)left=x;}
                else if(wall.X<x)left=Math.Max(left,wall.X);else right=Math.Min(right,wall.X);
            }return(left,right);
        }
    }
    public sealed class OutsideBombBlastData{public long SourceX,X,Height,Frame;public int VisualFrames=OutsideBombLaunchData.BlastFrames;public OutsideBombBlastData Copy()=>(OutsideBombBlastData)MemberwiseClone();}
    public sealed partial class OutsideExplorationSession
    {
        private readonly List<OutsideBombFlightData> bombs=new List<OutsideBombFlightData>();
        private readonly List<OutsideBombBlastData> bombBlasts=new List<OutsideBombBlastData>();
        public IReadOnlyList<OutsideBombFlightData> Bombs=>bombs.Select(v=>v.Copy()).ToArray();
        public IReadOnlyList<OutsideBombBlastData> BombBlasts=>bombBlasts.Select(v=>v.Copy()).ToArray();
        private static bool BombDefinition(Card c)=>c!=null&&c.Id=="bomb"&&c.Category==CardCategory.Attack&&c.Series==CardSeries.Bomb&&c.AttackPowerBasisPoints==0&&Enum.IsDefined(typeof(ExceedLevel),c.ExceedLevel)&&c.ExceedLevel<=ExceedLevel.Lv2&&c.Power>=0&&c.Properties==AttackProperty.None&&c.Modifier.AdditionalEffects.Count==0&&c.MoveMotionTime==0&&c.AttackMotionTime==0&&c.HitInvincibilityMs==0;
        private bool CanPlanBomb()=>bombs.Count<60&&OutsideTrialRoute.KnownMap(Core.TrialMapId)&&Core.Definition.Id==OutsideKickTrialDefinition.MapId&&Core.Height>=0;
        private bool TryPlanBomb(Card card,out OutsideBombLaunchData plan,out string error)
        {
            plan=null;error=null;if(!CanPlanBomb()){error="ボムは対応する探索マップで使用してください（カリ）";return false;}
            try{plan=new OutsideBombLaunchData{X=Core.Position,Facing=Core.Facing,Power=card.GetFinalPower(),Element=card.GetFinalElement(),Level=card.ExceedLevel,PoiseDamage=card.PoiseDamage,HitStopFrames=hitStopSettings.CardFrames(card)};if(trialCardPolish){plan.DefinitionId=OutsideBombLaunchData.FastTerrainDefinition;plan.Terrain=OutsideBombTerrainData.Create(Core,plan.X,plan.Facing,Core.Height,plan.FlightDistance,plan.FlightDuration);}else if(!Core.IsGrounded||Core.Height!=0||!OutsideBombLaunchData.HasTerrain(Core,Core.Position,Core.Facing)){plan.DefinitionId=OutsideBombLaunchData.TerrainDefinition;plan.Terrain=OutsideBombTerrainData.Create(Core,plan.X,plan.Facing,Core.Height);}plan.Validate();return true;}
            catch(Exception e)when(e is ArgumentException||e is OverflowException){plan=null;error=e.Message;return false;}
        }
        private void EmitBomb(OutsideAttackStateData action){if(action.BombLaunch==null)throw new InvalidOperationException("ボムの発射計画がありません");bombs.Add(new OutsideBombFlightData{Launch=action.BombLaunch.Copy(),ActionStartedAt=action.StartedAt,FiredAt=Core.Frame});if(action.StartupFrames==0&&action.RecoveryFrames==0){instantBombReleasedAt=Core.Frame;instantBombFacing=action.BombLaunch.Facing;instantBombAirborne=!Core.IsGrounded;}}
        private OutsideBombFlightData[] CaptureBombs()=>bombs.Count==0?null:bombs.Select(v=>v.Copy()).ToArray();
        private void AdvanceBombs()
        {
            bombBlasts.RemoveAll(v=>Core.Frame-v.Frame>=v.VisualFrames);
            if(Player.IsDead){bombs.Clear();ClearBombReleaseVisual();bombBlasts.Clear();return;}
            for(int i=bombs.Count-1;i>=0;i--){var flight=bombs[i];if(Core.Frame-flight.FiredAt<flight.Launch.Duration)continue;
                var plan=flight.Launch;long target=plan.ImpactX;if(plan.Dissipates){bombs.RemoveAt(i);continue;}
                void Hit(int id,long x,Enemy enemy){long y=id==0?FirstTrialTargetHeight:0,dy=plan.ImpactHeight-y;if(enemy.IsDead||!OutsideBombOcclusion.CanReach(Core.TraversalProfile?.Walls,plan.X,target,plan.ImpactHeight,x,y)||Math.Abs(x-target)>OutsideBombLaunchData.Radius||(plan.Terrain!=null||y!=0)&&((decimal)(x-target)*(x-target)+(decimal)dy*dy>(decimal)OutsideBombLaunchData.Radius*OutsideBombLaunchData.Radius))return;
                    if(id==0){trialTargetChanged=true;trialTargetHits=(int)Math.Min(int.MaxValue,(long)trialTargetHits+1);}else secondTrialHits=(int)Math.Min(int.MaxValue,(long)secondTrialHits+1);
                    enemy.TakeDamage(plan.Power,plan.Element,AttackProperty.None,plan.PoiseDamage,0,hitStopFrames:plan.HitStopFrames);
                }
                Hit(0,FirstTrialTargetX,TrialTarget);Hit(1,SecondTrialTargetX,SecondTrialTarget);bombs.RemoveAt(i);bombBlasts.Add(new OutsideBombBlastData{SourceX=plan.X,X=target,Height=plan.ImpactHeight,Frame=Core.Frame,VisualFrames=plan.DefinitionId==OutsideBombLaunchData.FastTerrainDefinition?18:OutsideBombLaunchData.BlastFrames});
            }
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    public enum OutsideGoblinPhase { Approach, Windup, Strike, Recovery, Dead }
    /// <summary>Annex明示選択だけの試用。旧Runtimeでは欄を出さず、旧AIを自動昇格しない。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideGoblinTrialData
    {
        public const string RegisteredDefinition="annex-approach-goblin-v1";
        public const long MinX=20*60000,MaxX=23*60000,Speed=2000,StopRange=84000,Reach=90000,Height=90000;
        public const int WindupFrames=24,StrikeFrames=6,RecoveryFrames=48;
        [System.Runtime.Serialization.DataMember] public string DefinitionId=RegisteredDefinition;
        [System.Runtime.Serialization.DataMember] public long X;
        [System.Runtime.Serialization.DataMember] public int Facing=1,Age;
        [System.Runtime.Serialization.DataMember] public OutsideGoblinPhase Phase;
        public OutsideGoblinTrialData Copy()=>(OutsideGoblinTrialData)MemberwiseClone();
        public void Validate(OutsideCoreStateData core,OutsideTrialEnemyAttackData legacy,OutsideTrialTargetData target)
        {
            int limit=Phase==OutsideGoblinPhase.Windup?WindupFrames:Phase==OutsideGoblinPhase.Strike?StrikeFrames:Phase==OutsideGoblinPhase.Recovery?RecoveryFrames:1;
            if(DefinitionId!=RegisteredDefinition||core?.TrialMapId!=OutsideTrialRoute.AnnexMapId||legacy!=null||X<MinX||X>MaxX||Facing!=1&&Facing!=-1||!Enum.IsDefined(typeof(OutsideGoblinPhase),Phase)||Age<0||Age>=limit||(Phase==OutsideGoblinPhase.Dead)!=(target?.Hp==0))throw new ArgumentException("接近ゴブリンの保存定義/段階が不正です");
        }
    }
    public sealed partial class OutsideExplorationSession
    {
        private OutsideGoblinTrialData goblinTrial;
        private bool pendingGoblinStrike;
        private static bool GoblinSafePosition(OutsideExplorationCore core,long x,long min=OutsideGoblinTrialData.MinX,long max=OutsideGoblinTrialData.MaxX)
        {
            if(core?.Definition.Id!="validation-landing-32"||core.TrialMapId!=OutsideTrialRoute.AnnexMapId||core.TraversalProfile==null||x<min||x>max)return false;
            long left=x-TrialContactHalfWidth,right=x+TrialContactHalfWidth;
            return !core.TraversalProfile.Walls.Any(w=>w.X>=left&&w.X<=right&&w.Bottom<=OutsideGoblinTrialData.Height&&w.Top>=0)
                &&!core.TraversalProfile.FloorGaps.Any(g=>right>g.Left&&left<g.Right)
                &&!core.TraversalProfile.Waters.Any(w=>right>=w.Left&&left<=w.Right&&w.Bottom<=0&&w.Top>=0);
        }
        public bool StartGoblinTrial()
        {
            if(IsEnded||!IsPresented||goblinTrial!=null||slimeTrial!=null||Player==null||Player.IsDead||TrialTarget.IsDead||!GoblinSafePosition(Core,FirstTrialTargetX))return false;
            long x=FirstTrialTargetX;trialEnemyAttack=null;pendingGoblinStrike=false;
            goblinTrial=new OutsideGoblinTrialData{X=x,Facing=Core.Position<x?-1:1};return true;
        }
        private void AdvanceGoblinTrial()
        {
            pendingGoblinStrike=false;var g=goblinTrial;if(g==null)return;
            if(Player.IsDead||Core.HasFallenOut){goblinTrial=null;return;}
            if(TrialTarget.IsDead){g.Phase=OutsideGoblinPhase.Dead;g.Age=0;return;}
            // 移動と予告の時計は描画から独立。強靭ブレイク中は振りを解除して反撃時間へ。
            if(TrialTarget.IsStaggered||TrialTarget.IsParalyzed){g.Phase=OutsideGoblinPhase.Recovery;g.Age=0;return;}
            if(g.Phase==OutsideGoblinPhase.Approach){
                long delta=Core.Position-g.X;g.Facing=delta==0?g.Facing:Math.Sign(delta);
                if(Math.Abs(delta)<=OutsideGoblinTrialData.StopRange&&Core.Height>=0&&Core.Height<=OutsideGoblinTrialData.Height){g.Phase=OutsideGoblinPhase.Windup;g.Age=0;return;}
                if(Math.Abs(delta)<=OutsideGoblinTrialData.StopRange||Math.Abs(delta)>6*60000)return;
                long step=Math.Min(OutsideGoblinTrialData.Speed,Math.Abs(delta)-OutsideGoblinTrialData.StopRange)*g.Facing,next=g.X+step;
                if(GoblinSafePosition(Core,next,PairStage445==1?16*60000:OutsideGoblinTrialData.MinX,OutsideGoblinTrialData.MaxX))g.X=next;
                return;
            }
            g.Age++;
            if(g.Phase==OutsideGoblinPhase.Windup&&g.Age>=OutsideGoblinTrialData.WindupFrames){g.Phase=OutsideGoblinPhase.Strike;g.Age=0;pendingGoblinStrike=true;}
            else if(g.Phase==OutsideGoblinPhase.Strike&&g.Age>=OutsideGoblinTrialData.StrikeFrames){g.Phase=OutsideGoblinPhase.Recovery;g.Age=0;}
            else if(g.Phase==OutsideGoblinPhase.Recovery&&g.Age>=OutsideGoblinTrialData.RecoveryFrames){g.Phase=OutsideGoblinPhase.Approach;g.Age=0;}
        }
        private OutsideKnockbackStateData ResolveGoblinStrike()
        {
            if(!pendingGoblinStrike)return null;pendingGoblinStrike=false;var g=goblinTrial;
            if(g==null||TrialTarget.IsDead||Player.IsDead||Core.HasFallenOut||TrialTarget.IsStaggered||TrialTarget.IsParalyzed)return null;
            long distance=(Core.Position-g.X)*g.Facing;
            if(distance<0||distance>OutsideGoblinTrialData.Reach||Core.Height<0||Core.Height>OutsideGoblinTrialData.Height||FirstSwordBlockingWall(g.X,0,g.Facing,distance).HasValue)return null;
            bool accepted=Player.TryTakeDamage(TrialEnemyDamage,attackElement:ElementType.None,affectsPoise:true,poiseDamage:TrialEnemyPoiseDamage);
            return accepted?OutsideTrialEnemyAttackData.V2Knockback.Start(g.Facing):null;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    public enum OutsideSlimePhase { Idle, Squash, Flight, Recovery, Dead }
    /// <summary>壁手前の共通試用枠だけの跳躍。軌道/接触/予告はこの論理状態を共有する。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class OutsideSlimeTrialData
    {
        public const string RegisteredDefinition="annex-hop-slime-v1";
        public const int SquashFrames=30,FlightFrames=36,RecoveryFrames=60;
        public const long MinX=20*60000,MaxX=23*60000,Peak=180000,LandingRadius=72000;
        [System.Runtime.Serialization.DataMember] public string DefinitionId=RegisteredDefinition;
        [System.Runtime.Serialization.DataMember] public long StartX,LandingX,DeadHeight;
        [System.Runtime.Serialization.DataMember] public int Age,Facing=1;
        [System.Runtime.Serialization.DataMember] public OutsideSlimePhase Phase;
        public long X=>Phase==OutsideSlimePhase.Flight?StartX+(LandingX-StartX)*Age/FlightFrames:StartX;
        public long Height=>Phase==OutsideSlimePhase.Flight?4*Peak*Age*(FlightFrames-Age)/(FlightFrames*FlightFrames):Phase==OutsideSlimePhase.Dead?DeadHeight:0;
        public OutsideSlimeTrialData Copy()=>(OutsideSlimeTrialData)MemberwiseClone();
        public void Validate(OutsideCoreStateData core,OutsideGoblinTrialData goblin,OutsideTrialEnemyAttackData legacy,OutsideTrialTargetData target)
        {
            int limit=Phase==OutsideSlimePhase.Squash?SquashFrames:Phase==OutsideSlimePhase.Flight?FlightFrames:Phase==OutsideSlimePhase.Recovery?RecoveryFrames:1;
            if(DefinitionId!=RegisteredDefinition||core?.TrialMapId!=OutsideTrialRoute.AnnexMapId||goblin!=null||legacy!=null||!Enum.IsDefined(typeof(OutsideSlimePhase),Phase)||Age<0||Age>=limit||StartX<MinX||StartX>MaxX||LandingX<MinX||LandingX>MaxX||Facing!=1&&Facing!=-1||DeadHeight<0||DeadHeight>Peak||Phase!=OutsideSlimePhase.Dead&&DeadHeight!=0||(Phase==OutsideSlimePhase.Dead)!=(target?.Hp==0)||(Phase==OutsideSlimePhase.Idle||Phase==OutsideSlimePhase.Recovery||Phase==OutsideSlimePhase.Dead)&&StartX!=LandingX)throw new ArgumentException("跳躍スライムの保存定義/段階が不正です");
        }
    }
    public sealed partial class OutsideExplorationSession
    {
        private OutsideSlimeTrialData slimeTrial;
        private bool pendingSlimeLanding;
        private static bool SlimeSafeSpan(OutsideExplorationCore core,long start,long end,long min=OutsideSlimeTrialData.MinX,long max=OutsideSlimeTrialData.MaxX)
        {
            if(core?.Definition.Id!="validation-landing-32"||core.TrialMapId!=OutsideTrialRoute.AnnexMapId||core.TraversalProfile==null||start<min||start>max||end<min||end>max)return false;
            long left=Math.Min(start,end)-TrialContactHalfWidth,right=Math.Max(start,end)+TrialContactHalfWidth;
            return !core.TraversalProfile.Walls.Any(w=>w.X>=left&&w.X<=right&&w.Bottom<=OutsideSlimeTrialData.Peak+TrialContactHeight&&w.Top>=0)
                &&!core.TraversalProfile.FloorGaps.Any(g=>right>g.Left&&left<g.Right)
                &&!core.TraversalProfile.Waters.Any(w=>right>=w.Left&&left<=w.Right&&w.Bottom<=OutsideSlimeTrialData.Peak+TrialContactHeight&&w.Top>=0);
        }
        public bool StartSlimeTrial()
        {
            if(IsEnded||!IsPresented||slimeTrial!=null||goblinTrial!=null||Player==null||Player.IsDead||TrialTarget.IsDead||!SlimeSafeSpan(Core,FirstTrialTargetX,FirstTrialTargetX))return false;
            long x=FirstTrialTargetX;trialEnemyAttack=null;pendingSlimeLanding=false;slimeTrial=new OutsideSlimeTrialData{StartX=x,LandingX=x,Facing=Core.Position<x?-1:1};return true;
        }
        private void ObserveSlimeDeath()
        {
            var s=slimeTrial;if(s==null||!TrialTarget.IsDead||s.Phase==OutsideSlimePhase.Dead)return;
            long x=s.X,y=s.Height;s.StartX=s.LandingX=x;s.DeadHeight=y;s.Phase=OutsideSlimePhase.Dead;s.Age=0;pendingSlimeLanding=false;
        }
        private void AdvanceSlimeTrial()
        {
            pendingSlimeLanding=false;var s=slimeTrial;if(s==null)return;
            if(Player.IsDead||Core.HasFallenOut){slimeTrial=null;return;}
            ObserveSlimeDeath();if(s.Phase==OutsideSlimePhase.Dead||TrialTarget.IsStaggered||TrialTarget.IsParalyzed)return;
            if(s.Phase==OutsideSlimePhase.Idle){
                if(Math.Abs(Core.Position-s.X)>6*60000)return;
                long min=PairStage445==2?25*60000:OutsideSlimeTrialData.MinX,max=PairStage445==2?28*60000:OutsideSlimeTrialData.MaxX;
                long target=Math.Max(min,Math.Min(max,Core.Position));
                if(!SlimeSafeSpan(Core,s.StartX,target,min,max))return;
                s.LandingX=target;s.Facing=target==s.StartX?s.Facing:Math.Sign(target-s.StartX);s.Phase=OutsideSlimePhase.Squash;s.Age=0;return;
            }
            s.Age++;
            if(s.Phase==OutsideSlimePhase.Squash&&s.Age>=OutsideSlimeTrialData.SquashFrames){s.Phase=OutsideSlimePhase.Flight;s.Age=0;}
            else if(s.Phase==OutsideSlimePhase.Flight&&s.Age>=OutsideSlimeTrialData.FlightFrames){s.StartX=s.LandingX;s.Phase=OutsideSlimePhase.Recovery;s.Age=0;pendingSlimeLanding=true;}
            else if(s.Phase==OutsideSlimePhase.Recovery&&s.Age>=OutsideSlimeTrialData.RecoveryFrames){s.Phase=OutsideSlimePhase.Idle;s.Age=0;}
        }
        private OutsideKnockbackStateData ResolveSlimeLanding()
        {
            if(!pendingSlimeLanding)return null;pendingSlimeLanding=false;var s=slimeTrial;
            if(s==null||TrialTarget.IsDead||Player.IsDead||Core.HasFallenOut||TrialTarget.IsStaggered||TrialTarget.IsParalyzed)return null;
            long dx=Core.Position-s.X;int facing=dx<0?-1:1;
            if(Math.Abs(dx)>OutsideSlimeTrialData.LandingRadius||Core.Height<0||Core.Height>TrialContactHeight||FirstSwordBlockingWall(s.X,0,facing,Math.Abs(dx)).HasValue)return null;
            bool accepted=Player.TryTakeDamage(TrialEnemyDamage,attackElement:ElementType.None,affectsPoise:true,poiseDamage:TrialEnemyPoiseDamage);
            return accepted?OutsideTrialEnemyAttackData.V2Knockback.Start(facing):null;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    public sealed partial class OutsideExplorationSession
    {
        public const string PairDefinition445="trial-pair-goblin-slime-v1";
        public const string ForestDefinition460="trial-forest-cover-flight-v1";
        public bool IsForest460 {get;private set;}
        /// <summary>森の保存しないコピー。低い遮蔽の通過と、射撃/蹴りの選択を既存判定で試す。</summary>
        public static OutsideExplorationSession CreateForestTrial460(OutsideExplorationSession source,CheckpointDefinitions definitions)
        {
            var t=CreatePairTrial445(source,definitions);
            try{
                t.IsForest460=true;var p=t.Core.TraversalProfile;
                var cover=new OutsideTraversalProfile(ForestDefinition460+"-geometry",p.DashSpeedPerSecond,p.DashFrames,p.DashCooldownFrames,p.DashResetFrames,p.DashJumpSpeedPerSecond,p.BodyHalfWidth,p.BodyHeight,p.WallSlidePerFrame,p.WallKickVelocity,p.WallKickSpeedPerSecond,p.WallKickPushFrames,p.WallKickMotionFrames,p.WallActionsUnlocked,new OutsideValidationWall("forest-low-cover",20*60000,0,84000,false,false));
                if(!t.TryConfigureTraversal(p.DoubleJumpUnlocked?cover.WithDoubleJump(cover.Id):cover))throw new InvalidOperationException("森の遮蔽を配置できませんでした");
                t.trialTarget=t.CreatePairTarget445();t.goblinTrial.DefinitionId=ForestDefinition460+"-goblin";return t;
            }catch{t.Dispose();throw;}
        }
        public int PairStage445 {get;private set;}
        public int PairHandVariant445 {get;private set;}
        public bool PairCompleted445 {get;private set;}
        private long pairClearedAt445=-1;
        public long PairIntermissionRemaining445=>pairClearedAt445<0?0:Math.Max(0,60-(Core.Frame-pairClearedAt445));
        /// <summary>通常個体とRNGを変更しない、保存不可の連戦コピー。カードの使用/補充は既存Sessionに一任する。</summary>
        public static OutsideExplorationSession CreatePairTrial445(OutsideExplorationSession source,CheckpointDefinitions definitions,int handVariant=0)
        {
            if(source==null||source.IsEnded||source.Player==null||source.Player.IsDead||source.Cards==null||definitions==null)throw new ArgumentException("連戦には通常の生存個体と登録デッキが必要です");
            if(handVariant<0||handVariant>2)throw new ArgumentOutOfRangeException(nameof(handVariant));
            var actor=OutsidePlayerSaveData.Capture(source.Player).Restore();
            var copy=OutsideExplorationCards.RestoreState(source.Cards.CaptureState(definitions),definitions);
            // 同じ30枚の構成を試用開始時だけ通常シャッフル。親の手札/RNGや使用済み履歴は変えない。
            var all=Enumerable.Range(0,copy.TotalCount).Select(copy.ActionCard).OrderBy(c=>c.Id,StringComparer.Ordinal).ThenBy(c=>c.ExceedLevel).ToArray();
            var owned=all.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            if(!DepartureDeckRules.TryCreateForSize(all,owned,ExplorationDeckRules.RequiredCards,out var deck,out var failure))throw new ArgumentException(failure);
            var random=new Olca.Core.Logic.Battle.SeededBattleRandom(handVariant==1?445u:handVariant==2?447u:446u);deck.Shuffle(random);var cards=new OutsideExplorationCards(deck,random);
            var trial=new OutsideExplorationSession(true);
            try{
                trial.TryInitializePlayer(()=>actor);trial.Cards=cards;trial.PairStage445=1;trial.PairHandVariant445=handVariant;
                var original=source.Core.TraversalProfile??OutsideTraversalProfile.ValidationDash;
                var flat=new OutsideTraversalProfile(PairDefinition445+"-geometry",original.DashSpeedPerSecond,original.DashFrames,original.DashCooldownFrames,original.DashResetFrames,original.DashJumpSpeedPerSecond,original.BodyHalfWidth,original.BodyHeight,original.WallSlidePerFrame,original.WallKickVelocity,original.WallKickSpeedPerSecond,original.WallKickPushFrames,original.WallKickMotionFrames,original.WallActionsUnlocked);
                trial.TryConfigureTraversal(original.DoubleJumpUnlocked?flat.WithDoubleJump(flat.Id):flat);
                trial.Core.SetRecoveryMap(OutsideTrialRoute.DoorAnnexPoint);trial.Core.RestoreRecoveryPosition(18*60000);
                trial.Core.CardGauge.Restore(source.Core.CardGauge.Maximum,source.Core.CardGauge.RecoveryPerSecond,source.Core.CardGauge.CurrentUnits);
                trial.trialTarget=trial.CreatePairTarget445();trial.secondTrialTarget=trial.CreateSecondTrialTarget(null);trial.secondTrialTarget.RestoreCheckpointHp(0);
                trial.goblinTrial=new OutsideGoblinTrialData{DefinitionId=PairDefinition445+"-goblin",X=1350000,Facing=-1};trial.EnableTrialNormalKick();trial.EnableTrialHitStop();trial.EnableTrialCardPolish();return trial;
            }catch{trial.Dispose();throw;}
        }
        // この保存しない短い区間だけ、既存分岐回廊と同じHP30を調整可能な仮値として使う。
        private Enemy CreatePairTarget445(){var e=new Enemy((IsForest460?ForestDefinition460:PairDefinition445)+"-"+PairStage445,"レンセン シヨウ（カリ）",30,ElementType.None,new Vector2Int(0,0));e.OnHitStopRequested+=RequestHitStop;return e;}
        private void AdvancePair445()
        {
            if(PairStage445==0||PairCompleted445||Player.IsDead||!TrialTarget.IsDead)return;
            if(flyingTrial454!=null){PairCompleted445=true;CancelChargeInput();CancelAttackReservation();return;}
            if(PairStage445==2){PairCompleted445=true;CancelChargeInput();CancelAttackReservation();return;}
            if(pairClearedAt445<0){pairClearedAt445=Core.Frame;return;}
            if(Core.Frame-pairClearedAt445<60)return;
            // 一体目の飛翔物や命中済み印を二体目へ持ち越さず、手札/HP/ゲージ/論理時計はそのまま。
            initialShots.Clear();bombs.Clear();bombBlasts.Clear();ClearBombReleaseVisual();attackState=null;normalKick?.Reset();CancelChargeInput();CancelAttackReservation();
            goblinTrial=null;pendingGoblinStrike=false;trialEnemyAttack=null;PairStage445=2;trialTarget=CreatePairTarget445();trialTargetHits=0;trialTargetChanged=false;trialStrike=null;Array.Clear(kickImpacts,0,kickImpacts.Length);trialContactNextFrame=0;
            if(IsForest460)flyingTrial454=new OutsideFlyingTrial454{StartX=1590000,LandingX=1590000};
            else slimeTrial=new OutsideSlimeTrialData{DefinitionId=PairDefinition445+"-slime",StartX=1590000,LandingX=1590000,Facing=-1};pendingSlimeLanding=false;pairClearedAt445=-1;PairStage445=2;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    public enum OutsideFlyingPhase454 { Hover, Windup, Dive, Recovery, Rise, Dead }
    /// <summary>保存しない飛行試用。高さと攻撃は同じ論理状態から読む。数値は全て試用値。</summary>
    public sealed class OutsideFlyingTrial454
    {
        public OutsideFlyingPhase454 Phase;public int Age,Facing=-1;public long StartX=1290000,LandingX=1290000,DeadHeight;
        public const long HoverHeight=192000,LandingRadius=48000;
        public const int HoverFrames=60,WindupFrames=30,DiveFrames=18,RecoveryFrames=84,RiseFrames=30;
        public long X=>Phase==OutsideFlyingPhase454.Dive?StartX+(LandingX-StartX)*Age/DiveFrames:Phase==OutsideFlyingPhase454.Recovery||Phase==OutsideFlyingPhase454.Rise?LandingX:StartX;
        public long Height=>Phase==OutsideFlyingPhase454.Dead?DeadHeight:Phase==OutsideFlyingPhase454.Recovery?0:Phase==OutsideFlyingPhase454.Dive?HoverHeight*(DiveFrames-Age)/DiveFrames:Phase==OutsideFlyingPhase454.Rise?HoverHeight*Age/RiseFrames:HoverHeight;
        public OutsideFlyingTrial454 Copy()=>(OutsideFlyingTrial454)MemberwiseClone();
    }
    public sealed partial class OutsideExplorationSession
    {
        public const string FlyingDefinition454="trial-flying-dive-v1";
        private OutsideFlyingTrial454 flyingTrial454;private bool pendingFlyingLanding454;
        public bool IsFlyingTrial454=>flyingTrial454!=null;
        public static OutsideExplorationSession CreateFlyingTrial454(OutsideExplorationSession source,CheckpointDefinitions definitions)
        {
            var t=CreatePairTrial445(source,definitions);
            t.goblinTrial=null;t.slimeTrial=null;t.trialEnemyAttack=null;
            t.trialTarget=new Enemy(FlyingDefinition454+"-enemy","ヒコウ シヨウ（カリ）",30,ElementType.None,new Vector2Int(0,0));t.trialTarget.OnHitStopRequested+=t.RequestHitStop;
            t.flyingTrial454=new OutsideFlyingTrial454();return t;
        }
        private void ObserveFlyingDeath454()
        {
            var s=flyingTrial454;if(s==null||!TrialTarget.IsDead||s.Phase==OutsideFlyingPhase454.Dead)return;
            long x=s.X,y=s.Height;s.StartX=s.LandingX=x;s.DeadHeight=y;s.Phase=OutsideFlyingPhase454.Dead;s.Age=0;pendingFlyingLanding454=false;
        }
        private void AdvanceFlying454()
        {
            pendingFlyingLanding454=false;var s=flyingTrial454;if(s==null)return;ObserveFlyingDeath454();
            if(Player.IsDead||Core.HasFallenOut||s.Phase==OutsideFlyingPhase454.Dead||TrialTarget.IsStaggered||TrialTarget.IsParalyzed)return;
            // 森コピーだけは遮蔽の向こうへ進んでから起動。降下軌道を低壁の反対側へ通さない。
            if(s.Phase==OutsideFlyingPhase454.Hover&&(Math.Abs(Core.Position-s.X)>6*60000||IsForest460&&Core.Position<20*60000+OutsideFlyingTrial454.LandingRadius))return;
            s.Age++;
            if(s.Phase==OutsideFlyingPhase454.Hover&&s.Age>=OutsideFlyingTrial454.HoverFrames){s.LandingX=Math.Max(IsForest460?20*60000+OutsideFlyingTrial454.LandingRadius:16*60000,Math.Min(28*60000,Core.Position));s.Facing=s.LandingX==s.StartX?s.Facing:Math.Sign(s.LandingX-s.StartX);s.Phase=OutsideFlyingPhase454.Windup;s.Age=0;}
            else if(s.Phase==OutsideFlyingPhase454.Windup&&s.Age>=OutsideFlyingTrial454.WindupFrames){s.Phase=OutsideFlyingPhase454.Dive;s.Age=0;}
            else if(s.Phase==OutsideFlyingPhase454.Dive&&s.Age>=OutsideFlyingTrial454.DiveFrames){s.StartX=s.LandingX;s.Phase=OutsideFlyingPhase454.Recovery;s.Age=0;pendingFlyingLanding454=true;}
            else if(s.Phase==OutsideFlyingPhase454.Recovery&&s.Age>=OutsideFlyingTrial454.RecoveryFrames){s.Phase=OutsideFlyingPhase454.Rise;s.Age=0;}
            else if(s.Phase==OutsideFlyingPhase454.Rise&&s.Age>=OutsideFlyingTrial454.RiseFrames){s.StartX=s.LandingX;s.Phase=OutsideFlyingPhase454.Hover;s.Age=0;}
        }
        private OutsideKnockbackStateData ResolveFlyingLanding454()
        {
            if(!pendingFlyingLanding454)return null;pendingFlyingLanding454=false;var s=flyingTrial454;
            if(s==null||TrialTarget.IsDead||Player.IsDead||Core.HasFallenOut||TrialTarget.IsStaggered||TrialTarget.IsParalyzed)return null;
            long dx=Core.Position-s.X;int facing=dx<0?-1:1;
            if(Math.Abs(dx)>OutsideFlyingTrial454.LandingRadius||Core.Height<0||Core.Height>TrialContactHeight||FirstSwordBlockingWall(s.X,0,facing,Math.Abs(dx)).HasValue)return null;
            bool accepted=Player.TryTakeDamage(TrialEnemyDamage,attackElement:ElementType.None,affectsPoise:true,poiseDamage:TrialEnemyPoiseDamage);
            return accepted?OutsideTrialEnemyAttackData.V2Knockback.Start(facing):null;
        }
    }
}
