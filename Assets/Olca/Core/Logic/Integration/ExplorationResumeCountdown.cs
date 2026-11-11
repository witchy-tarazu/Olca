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
    /// <summary>外界での開始可否だけを持つ試用登録。カード性能/hashやDungeonの設定は変更しない。</summary>
    public sealed class OutsideCardUseDefinition
    {
        public string CardId {get;}public ExceedLevel Level {get;}public bool CanUseInAir {get;}
        public OutsideCardUseDefinition(string cardId,ExceedLevel level,bool canUseInAir)
        {if(string.IsNullOrWhiteSpace(cardId)||!Enum.IsDefined(typeof(ExceedLevel),level))throw new ArgumentException("外界使用定義が不正です");CardId=cardId;Level=level;CanUseInAir=canUseInAir;}
        // 通常ヒールと通常ソードだけの暫定指定。未登録カードや上位を自動的に許可しない。
        public static IReadOnlyList<OutsideCardUseDefinition> TrialDefinitions {get;}=Array.AsReadOnly(new[]{
            new OutsideCardUseDefinition("heal",ExceedLevel.Normal,true),new OutsideCardUseDefinition("sword",ExceedLevel.Normal,true),
            new OutsideCardUseDefinition("fire_sword",ExceedLevel.Normal,false),new OutsideCardUseDefinition("sword",ExceedLevel.Lv1,false),new OutsideCardUseDefinition("sword",ExceedLevel.Lv2,false)});
        public static bool AllowsAir(Card card)=>card!=null&&TrialDefinitions.Any(d=>d.CardId==card.Id&&d.Level==card.ExceedLevel&&d.CanUseInAir);
    }

    /// <summary>Root所有の外界状態。現在は製品登録と分けた検証地形のみを扱う。</summary>
    public sealed partial class OutsideExplorationSession : System.IDisposable
    {
        public sealed class PresentationLease : System.IDisposable
        {
            private OutsideExplorationSession owner;
            internal PresentationLease(OutsideExplorationSession owner){this.owner=owner;}
            public OutsideExplorationCore Core=>owner?.Core;
            public Olca.Core.Domain.PlayerCharacter Player=>owner?.Player;
            public OutsideCameraPolicy Camera=>owner?.Camera;
            public OutsideExplorationCards Cards=>owner?.Cards;
            public string MapName=>IsCurrent?owner.MapName:null;
            public double MapNameOpacity=>IsCurrent?owner.MapNameOpacity:0;
            public bool ShouldShowBreath=>IsCurrent&&owner.ShouldShowBreath;
            public int BreathFramesRemaining=>owner?.BreathFramesRemaining??0;public int BreathMaximumFrames=>owner?.BreathMaximumFrames??0;
            public bool IsDeckEditingLocation=>owner?.IsDeckEditingLocation==true;
            public Enemy TrialTarget=>owner?.TrialTarget;public OutsideTrialStrike TrialStrike=>owner?.TrialStrike;
            public string CardUseMessage=>owner?.CardUseMessage; public long CardUseMessageFrame=>owner?.CardUseMessageFrame??-1;
            public bool IsCurrent=>owner!=null&&!owner.IsEnded&&ReferenceEquals(owner.current,this);
            public void Dispose(){var prior=owner;owner=null;if(prior!=null&&ReferenceEquals(prior.current,this)){prior.current=null;prior.CancelAttackReservation();}}
        }
        private PresentationLease current;
        // 外界だけが所有する個体。DungeonのPlayerやViewへ所有権を渡さない。
        public Olca.Core.Domain.PlayerCharacter Player {get;private set;}
        private long playerFrame;
        public string CardUseMessage {get;private set;}
        public long CardUseMessageFrame {get;private set;}=-1;
        private long lastCardUseAttempt=-1;
        private OutsideAttackStateData attackState;
        public const long TrialTargetX=18*OutsideMovementDefinition.UnitsPerWorldUnit,TrialSwordRange=90000;
        public const int TrialTargetMaximum=120;
        private Enemy trialTarget;private bool trialTargetChanged;private int trialTargetHits;private OutsideTrialStrike trialStrike;
        public Enemy TrialTarget=>trialTarget??(trialTarget=CreateTrialTarget(null));
        public OutsideTrialStrike TrialStrike=>trialStrike?.Copy();
        private static Enemy CreateTrialTarget(OutsideTrialTargetData data){var e=new Enemy("grunt","シヨウ ヒョウテキ（カリ）",TrialTargetMaximum,ElementType.None,new Olca.Core.Domain.Vector2Int(0,0));if(data!=null)e.RestoreCheckpointHp(data.Hp);return e;}
        private static bool HealDefinition(Card c)=>c!=null&&c.Id=="heal"&&c.Category==CardCategory.Defense&&c.SubCategory==CardSubCategory.Heal&&c.ExceedLevel==ExceedLevel.Normal&&c.Power>=0;
        private static bool SwordDefinition(Card c)=>c!=null&&c.Category==CardCategory.Attack&&c.Series==CardSeries.Sword&&((c.Id=="sword"&&(c.ExceedLevel==ExceedLevel.Normal||c.ExceedLevel==ExceedLevel.Lv1||c.ExceedLevel==ExceedLevel.Lv2))||(c.Id=="fire_sword"&&c.Element==ElementType.Fire&&c.ExceedLevel==ExceedLevel.Normal))&&c.Power>=0&&c.Modifier.AdditionalEffects.Count==0&&c.MoveMotionTime==0&&c.AttackMotionTime==0&&c.HitInvincibilityMs==0;
        private OutsideTrialTargetData CaptureTrialTarget()=>trialTargetChanged||trialTarget!=null&&trialTarget.CurrentHp!=TrialTargetMaximum?new OutsideTrialTargetData{Hp=TrialTarget.CurrentHp,Hits=trialTargetHits}:null;
        // 静止標的に対する試用の縦範囲。正式な敵の当たり形状ではない。
        public const long TrialSwordVerticalRange=90000;
        private void ApplyTrialSword(Card card){
            long dx=(TrialTargetX-Core.Position)*Core.Facing;bool vertical=Core.IsGrounded||(card.Id=="sword"&&card.ExceedLevel==ExceedLevel.Normal&&Core.Height>=0&&Core.Height<=TrialSwordVerticalRange);bool hit=vertical&&dx>=0&&dx<=TrialSwordRange&&!TrialTarget.IsDead;
            trialStrike=new OutsideTrialStrike{Frame=Core.Frame,X=Core.Position,Y=Core.Height,Facing=Core.Facing,Hit=hit};
            if(hit){trialTargetChanged=true;trialTargetHits=(int)Math.Min(int.MaxValue,(long)trialTargetHits+1);TrialTarget.TakeDamage(card.GetFinalPower(),card.GetFinalElement(),card.Properties,card.PoiseDamage,card.HitInvincibilityMs);}
        }
        private readonly Olca.Core.Logic.Battle.AttackInputReservation attackReservation=new Olca.Core.Logic.Battle.AttackInputReservation();
        private Card reservedCard;private OutsideExplorationCards reservedOwner;private long reservedRevision,reservedActionFrame,lastAttackInputFrame=-1;
        public Olca.Core.Logic.Battle.AttackReservationKind PendingAttack=>attackReservation.Kind;
        public void CancelAttackReservation(){attackReservation.Cancel();reservedCard=null;reservedOwner=null;}
        public void ObserveAttackBoundary(int facing,bool grounded){if(Core.Facing!=facing||Core.IsGrounded!=grounded)CancelAttackReservation();}
        private bool SupportedCard(Card card)=>Core.Definition.Id=="validation-landing-32"&&(HealDefinition(card)||SwordDefinition(card))&&(Core.IsGrounded||OutsideCardUseDefinition.AllowsAir(card));
        private bool CanCancelAttack()=>attackState!=null&&attackState.EffectApplied&&Cards.ActionCard(attackState.CardIndex).RecoveryCancelAfterFrames.HasValue&&Core.Frame-attackState.StartedAt>=attackState.StartupFrames+(long)Cards.ActionCard(attackState.CardIndex).RecoveryCancelAfterFrames.Value;
        // 予約入力は保存しない。Sessionの行動開始と同じ境界で最後に再検証する。
        public bool ProcessCardUseInput(bool requested,bool selectionChanged,bool basicRequested,long cardAt,long basicAt,out string error)
        {
            error=null;if(lastAttackInputFrame==Core.Frame)return false;lastAttackInputFrame=Core.Frame;
            attackReservation.Observe(Core.Frame);if(attackReservation.Kind==Olca.Core.Logic.Battle.AttackReservationKind.None&&reservedCard!=null)CancelAttackReservation();
            if(selectionChanged||!IsPresented||IsDeckEditingLocation||!BodyCanAttack){CancelAttackReservation();return false;}
            if(reservedCard!=null&&(!ReferenceEquals(reservedOwner,Cards)||!ReferenceEquals(reservedCard,Cards?.SelectedCard)||reservedRevision!=Cards.HandRevision||attackState!=null&&reservedActionFrame!=attackState.StartedAt))CancelAttackReservation();
            // 未登録の通常キックを実行/予約しないが、後からの固有攻撃入力で旧予約を復活させない。
            if(basicRequested&&(!requested||cardAt!=0&&basicAt!=0&&basicAt>cardAt)){CancelAttackReservation();error="固有攻撃は未接続です";return false;}
            if(requested){
                CancelAttackReservation();var card=Cards?.SelectedCard;
                if(!SupportedCard(card)){return TryUseSelectedCard(card,Cards?.HandRevision??0,out error);}
                if(Core.CardGauge.CurrentUnits<(long)card.GetFinalCost()*OutsideCardGauge.UnitsPerPoint){error="カードゲージが不足しています";return false;}
                if(IsAttacking&&!CanCancelAttack()){attackReservation.Request(Olca.Core.Logic.Battle.AttackReservationKind.Card,Core.Frame);reservedCard=card;reservedOwner=Cards;reservedRevision=Cards.HandRevision;reservedActionFrame=attackState.StartedAt;return false;}
                return StartCardAfterRecovery(card,Cards.HandRevision,out error);
            }
            if(attackReservation.Kind!=Olca.Core.Logic.Battle.AttackReservationKind.Card||IsAttacking&&!CanCancelAttack())return false;
            var expected=reservedCard;long revision=reservedRevision;CancelAttackReservation();
            if(!SupportedCard(expected)||Core.CardGauge.CurrentUnits<(long)expected.GetFinalCost()*OutsideCardGauge.UnitsPerPoint)return false;
            return StartCardAfterRecovery(expected,revision,out error);
        }
        private bool StartCardAfterRecovery(Card card,long revision,out string error)
        {
            var prior=attackState;attackState=null;bool started=TryUseSelectedCard(card,revision,out error);if(!started)attackState=prior;return started;
        }
        public OutsideAttackStateData AttackState=>attackState?.Copy();
        public bool IsAttacking=>attackState!=null;
        private static int Startup(Card card)=>card.StartupFrames??(int)Math.Ceiling(card.StartupTime*60d-0.00001d);
        private static int Recovery(Card card)=>card.RecoveryFrames??(int)Math.Ceiling(card.RecoveryTime*60d-0.00001d);
        private void ResetCardUseFeedback(){CardUseMessage=null;CardUseMessageFrame=lastCardUseAttempt=-1;attackState=null;lastAttackInputFrame=-1;trialStrike=null;CancelAttackReservation();}
        // Viewと独立した論理時計で一度だけ効果を確定する。停止/同frame再読込では進めない。
        private void AdvanceAttack(bool bodyAllowed)
        {
            var action=attackState;if(action==null)return;
            if(!bodyAllowed){attackState=null;CancelAttackReservation();return;}
            if(!action.EffectApplied&&Core.Frame-action.StartedAt>=action.StartupFrames){action.EffectApplied=true;var card=Cards.ActionCard(action.CardIndex);if(HealDefinition(card))Player.Heal(card.Power);else ApplyTrialSword(card);}
            if(Core.Frame-action.StartedAt>=action.StartupFrames+(long)action.RecoveryFrames)attackState=null;
        }
        private static void ValidateAttack(OutsideAttackStateData action,OutsideExplorationCards cards,long frame)
        {
            if(action==null)return;var card=cards.ActionCard(action.CardIndex);
            if(!(HealDefinition(card)||SwordDefinition(card))||Startup(card)!=action.StartupFrames||Recovery(card)!=action.RecoveryFrames)throw new ArgumentException("探索行動の定義と時間が一致しません");
        }
        /// <summary>試用ヒールの開始。地上/空中の登録判定と消費・前隙/発動済/後隙はSessionが所有する。</summary>
        public bool TryUseSelectedHealingCard(Card expected,long revision,out string error){if(!HealDefinition(expected)){error="ヒールではありません";return false;}return TryUseSelectedCard(expected,revision,out error);}
        public bool TryUseSelectedCard(Olca.Core.Domain.Card expected,long revision,out string error)
        {
            error=null;if(lastCardUseAttempt==Core.Frame){error="このフレームでは受付済みです";return false;}lastCardUseAttempt=Core.Frame;
            if(IsEnded||!IsPresented||IsDeckEditingLocation||!CanPerformAttack){error="今はカードを使用できません";return false;}
            if(expected==null||!ReferenceEquals(expected,Cards?.SelectedCard)||Cards.HandRevision!=revision){error="現在の手札を選択してください";return false;}
            if(!(HealDefinition(expected)||SwordDefinition(expected))){error="このカードの外界発動は未対応です";}
            else if(!SupportedCard(expected)){error=Core.Definition.Id=="validation-landing-32"?"このカードはクウチュウでシヨウできません":"このバショではシヨウできません";}
            else if(!Cards.TryConsumeSelected(expected,revision,Core.CardGauge,out _)){error="カードゲージが不足しています";}
            if(error!=null){CardUseMessage=error;CardUseMessageFrame=Core.Frame;return false;}
            // HealEffectと同じ既存HP反映。満タンでもカード/ゲージの消費を戻さない。
            attackState=new OutsideAttackStateData{CardIndex=Cards.IndexOf(expected),StartedAt=Core.Frame,StartupFrames=Startup(expected),RecoveryFrames=Recovery(expected)};AdvanceAttack(true);CardUseMessage=expected.Name+" を シヨウ";CardUseMessageFrame=Core.Frame;return true;
        }
        public bool CanPerformAttack=>BodyCanAttack&&attackState==null;
        private bool BodyCanAttack=>!IsEnded&&Player!=null&&!Player.IsDead&&!Player.IsParalyzed&&!Player.IsStaggered&&!IsLandingLocked&&!Core.HasFallenOut;
        public bool TryInitializePlayer(System.Func<Olca.Core.Domain.PlayerCharacter> factory)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(factory==null)throw new System.ArgumentNullException(nameof(factory));
            if(Player!=null)return false;
            if(IsPresented)throw new System.InvalidOperationException("表示前に外界個体を初期化してください");
            var value=factory();if(value==null||value.IsDead)throw new System.ArgumentException("生存する外界個体が必要です");
            ResetCardUseFeedback();Player=value;playerFrame=Core.Frame;ResetFall();return true;
        }
        public void RecoverOutside(string pointId,bool relocate=true)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));
            if(Player==null)throw new System.InvalidOperationException("外界個体が未初期化です");
            long position=OutsideTrialRoute.ResolveRecovery(Core,pointId);itemCooldowns.Clear();
            if(relocate){Core.RestoreRecoveryPosition(position);deckLocationArmed=managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=managedDeckLocations&&OutsideTrialRoute.IsReloadZone(Core);}Cards?.Reload();Core.CardGauge.RestoreFull();
            attackState=null;CancelAttackReservation();trialTarget=null;trialTargetHits=0;trialTargetChanged=false;trialStrike=null;Player.ClearPoisons();Player.ResetParalysis();Player.ResetConfusion();Player.ResetBlindness();Player.ClearStagger();Player.ResetPoiseAccumulation();Player.Heal(Player.MaxHp);playerFrame=Core.Frame;ResetFall();
        }
        private string savedPlayerId,savedPlayerKey;
        /// <summary>同じRoot/Actorの再表示では適用し直さない。coldは従来の復帰位置へ個体だけ復元する。</summary>
        public bool RestoreSavedPlayer(Olca.Core.Domain.OutsidePlayerSaveData data,string recoveryPoint)
        {
            if(IsEnded)throw new System.ObjectDisposedException(nameof(OutsideExplorationSession));if(data==null)return false;
            string key=data.Key();if(savedPlayerId==data.Id){if(savedPlayerKey!=key)throw new System.ArgumentException("同じ外界保存IDの内容が変わりました");return false;}
            if(IsPresented)throw new System.InvalidOperationException("表示中の外界個体を置換できません");
            var candidate=data.Restore();long position=Player==null?OutsideTrialRoute.ResolveRecovery(Core,recoveryPoint):Core.Position;
            if(Player==null){Core.RestoreRecoveryPosition(position);deckLocationArmed=managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=managedDeckLocations&&OutsideTrialRoute.IsReloadZone(Core);}ResetCardUseFeedback();trialTarget=null;trialTargetChanged=false;trialTargetHits=0;Player=candidate;itemCooldowns.Clear();playerFrame=Core.Frame;ResetFall();savedPlayerId=data.Id;savedPlayerKey=key;PrepareMapEntry();return true;
        }
        /// <summary>保存成功後に実行した回復だけを記録し、後の再表示で古い値を戻さない。</summary>
        public void AcknowledgeRecovery(Olca.Core.Domain.OutsidePlayerSaveData data)
        {
            if(data==null)return;data.Validate();var actual=Olca.Core.Domain.OutsidePlayerSaveData.Capture(Player);actual.Id=data.Id;
            if(actual.Key()!=data.Key())throw new System.ArgumentException("回復した個体と保存が一致しません");savedPlayerId=data.Id;savedPlayerKey=data.Key();
        }
        public bool AdvancePlayerFrame()=>AdvancePlayerFrame(out _);
        public bool AdvancePlayerFrame(out bool attackAllowed)
        {
            attackAllowed=false;
            if(IsEnded||!IsPresented||Player==null||Core.Frame==playerFrame)return false;
            playerFrame=Core.Frame;var value=Player;
            value.BeginHpFrame();value.BeginParalysisFrame(true);value.BeginConfusionFrame(true);value.BeginBlindnessFrame(true);
            try{AdvanceFall();AdvanceWater();bool bodyAllowed=BodyCanAttack;AdvanceAttack(bodyAllowed);attackAllowed=bodyAllowed&&attackState==null;value.TickPoisons(0,true);value.TickProtection(0,true);value.TickInvincibleFrame();value.TickStagger(0,true);value.TickParalysis(0,true);value.TickConfusion(0,true);value.TickBlindness(0,true);value.TickPoiseRecovery(0,true);}
            finally{value.CommitHpFrame();value.EndParalysisFrame(true);value.EndConfusionFrame(true);value.EndBlindnessFrame(true);value.PublishHpFrame();}
            attackAllowed&=!value.IsDead;return true;
        }
        public OutsideExplorationCore Core {get;}=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain,OutsideTraversalProfile.ValidationDash);
        public OutsideCameraPolicy Camera {get;private set;}=OutsideCameraPolicy.Follow;
        public OutsideExplorationCards Cards {get;private set;}
        private bool managedDeckLocations,deckLocationArmed,reloadGaugeOnExit;
        private Deck pendingLocationDeck;private Olca.Core.Interfaces.IRandomProvider pendingLocationRandom;private string queuedDeckKey;
        public bool IsDeckEditingLocation=>managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);
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
            EnableDeckLocations();pendingLocationDeck=deck;pendingLocationRandom=random;queuedDeckKey=key;return true;
        }
        private void EnableDeckLocations(){if(!managedDeckLocations){managedDeckLocations=true;deckLocationArmed=OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=OutsideTrialRoute.IsReloadZone(Core);}}
        public bool AdvanceDeckLocation()
        {
            if(!managedDeckLocations||IsEnded)return false;
            if(IsDeckEditingLocation){attackState=null;CancelAttackReservation();deckLocationArmed=true;reloadGaugeOnExit|=OutsideTrialRoute.IsReloadZone(Core);return false;}
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
        public string MapName=>Core.Definition.Id=="validation-landing-32"?"ケンショウ エリア（カリ）":null;
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
            CancelAttackReservation();return current=new PresentationLease(this);
        }
        public void Dispose(){if(IsEnded)return;IsEnded=true;current?.Dispose();current=null;ResetCardUseFeedback();trialTarget=null;trialTargetChanged=false;trialTargetHits=0;Player=null;pendingLocationDeck=null;pendingLocationRandom=null;queuedDeckKey=null;}
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
        public void TickFrame(float horizontal,bool jumpDown=false,bool jumpHeld=false,bool dashDown=false,bool dashHeld=false,bool movementLocked=false)
        {
            if(float.IsNaN(horizontal)||float.IsInfinity(horizontal))throw new System.ArgumentOutOfRangeException(nameof(horizontal));
            bool wasIdle=IsIdleMotion;
            LastWaterContactHeight=null;Frame++;CardGauge.AdvanceFrame();if(HasFallenOut){StopTraversalForLanding();IsMoving=false;MovementFrame=0;return;}bool jumpEdge=jumpDown&&!jumpWasHeld;jumpWasHeld=jumpHeld;
            if(movementLocked){horizontal=0;jumpEdge=false;dashDown=false;StopTraversalForLanding();}
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
            int direction=TraversalDirection(horizontalDirection);long speed=IsInWater?Scale(TraversalSpeed,TraversalProfile.Water.SpeedPercent):TraversalSpeed;
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
            if(!launch&&!wallLaunch&&!airLaunch&&IsGrounded){SupportId=SupportAt(Position,Height);if(IsGrounded){UpdateWallSlide(horizontalDirection);airJumpAt=null;TraceWater(previous,oldHeight);CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);return;}VerticalVelocity=0;}
            UpdateWallSlide(horizontalDirection);
            long next=Height+VerticalVelocity;
            if(VerticalVelocity<0&&Landing(previous,oldHeight,next,out var top,out var support)){Height=top;VerticalVelocity=0;SupportId=support;airJumpAt=null;}
            else{Height=TraversalProfile?.VoidDepth>0?Math.Max(-TraversalProfile.VoidDepth,next):next;VerticalVelocity-=IsInWater?TraversalProfile.WaterGravity:ValidationGravity;if(IsWallSliding)VerticalVelocity=System.Math.Max(VerticalVelocity,-TraversalProfile.WallSlidePerFrame);SupportId=null;}
            TraceWater(previous,oldHeight);CompleteTraversalFrame();AdvanceIdleFrame(wasIdle);
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
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideFallStateData Fall;
        [System.Runtime.Serialization.DataMember(EmitDefaultValue=false)] public OutsideWaterStateData Water;
        public OutsideRuntimeSaveData Copy()=>new OutsideRuntimeSaveData{Version=Version,Id=Id,Core=Core?.Copy(),FixedCameraCenter=FixedCameraCenter,Cards=Cards?.Copy(),Attack=Attack?.Copy(),TrialTarget=TrialTarget?.Copy(),Fall=Fall?.Copy(),Water=Water?.Copy()};
        public void Validate(){if(Version!=1||!Guid.TryParseExact(Id,"N",out _)||Core==null||FixedCameraCenter<0)throw new ArgumentException("外界Runtime保存の版/識別が不正です");Core.Validate();Cards?.Validate();Attack?.Validate(Core.Frame,Cards);TrialTarget?.Validate();Fall?.Validate(Core);Water?.Validate(Core);}
        public string Key(){Validate();return PortableCheckpointJson.Hash(PortableCheckpointJson.Serialize(this));}
        public OutsideRuntimeSaveData ForRun(string run){var v=Copy();v.Id=run;v.Validate();return v;}
    }
    /// <summary>物理入力や予約は保存せず、開始済みの一個体と効果確定境界だけを保持する。</summary>
    public sealed class OutsideAttackStateData
    {
        public int CardIndex,StartupFrames,RecoveryFrames;public long StartedAt;public bool EffectApplied;
        public OutsideAttackStateData Copy()=>(OutsideAttackStateData)MemberwiseClone();
        public void Validate(long frame,OutsideCardsStateData cards){
            if(cards==null||CardIndex<0||CardIndex>=cards.Cards.Length||!cards.Drawn.Contains(CardIndex)||cards.Hand.Contains(CardIndex)||StartedAt<0||StartedAt>frame||StartupFrames<0||RecoveryFrames<0||frame-StartedAt>=StartupFrames+(long)RecoveryFrames||EffectApplied!=(frame-StartedAt>=StartupFrames))throw new ArgumentException("探索攻撃状態と手札/時計が不正です");
        }
    }
    public sealed class OutsideTrialTargetData{public string Id="trial-sword-target-v1";public int Hp,Hits;public OutsideTrialTargetData Copy()=>(OutsideTrialTargetData)MemberwiseClone();public void Validate(){if(Id!="trial-sword-target-v1"||Hp<0||Hp>OutsideExplorationSession.TrialTargetMaximum||Hits<0)throw new ArgumentException("試用標的の保存が不正です");}}
    public sealed class OutsideTrialStrike{public long Frame,X,Y;public int Facing;public bool Hit;public OutsideTrialStrike Copy()=>(OutsideTrialStrike)MemberwiseClone();}
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
        public OutsideCoreStateData Copy(){var v=(OutsideCoreStateData)MemberwiseClone();v.Afterimages=Afterimages?.Select(a=>a?.Copy()).ToArray();return v;}
        public void Validate()
        {
            if(DefinitionKey==null||DefinitionKey.Length!=64||!DefinitionKey.All(Uri.IsHexDigit)||Position<0||Frame<0||Height<0&&SupportId!=null||Facing!=1&&Facing!=-1||MovementFrame<0||MovementFrame>Frame||IdleFrame<0||IdleFrame>Frame||Remainder<=-60||Remainder>=60||Moving!=(MovementFrame>0)||SupportId!=null&&string.IsNullOrWhiteSpace(SupportId)||ConsecutiveDashes<0||ConsecutiveDashes>2||KickAway< -1||KickAway>1||WallContact< -1||WallContact>1||WallSlideFrame<0||WallSlideFrame>Frame||!WallSliding&&WallSlideFrame!=0||new[]{DashStartedAt,DashEndsAt,CooldownUntil,LastDashAt,WallKickAt}.Any(v=>v< -1)||GaugeMaximum<=0||GaugeRecovery<0||GaugeUnits<0||GaugeUnits>(long)GaugeMaximum*OutsideCardGauge.UnitsPerPoint||Afterimages==null||Afterimages.Length>4)throw new ArgumentException("外界Core保存の値が不正です");
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
        public OutsideCoreStateData CaptureState()=>new OutsideCoreStateData{AirJumpAt=airJumpAt,DefinitionKey=StateDefinitionKey(),Position=Position,Facing=Facing,Frame=Frame,Height=Height,Velocity=VerticalVelocity,SupportId=SupportId,Moving=IsMoving,MovementFrame=MovementFrame,IdleFrame=IdleFrame,Remainder=remainder,DashStartedAt=dashStartedAt,DashEndsAt=dashEndsAt,CooldownUntil=cooldownUntil,LastDashAt=lastDashAt,WallKickAt=wallKickAt,ConsecutiveDashes=consecutiveDashes,KickAway=kickAway,DashJumping=IsDashJumping,WallSliding=IsWallSliding,WallSlideFrame=WallSlideFrame,WallContact=WallContactDirection,GaugeMaximum=CardGauge.Maximum,GaugeRecovery=CardGauge.RecoveryPerSecond,GaugeUnits=CardGauge.CurrentUnits,Afterimages=afterimages.Select(v=>new OutsideAfterimageStateData{X=v.X,Y=v.Y,Frame=v.Frame,Facing=v.Facing,Pose=v.Pose}).ToArray()};
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
            ValidateState(v);Position=v.Position;Facing=v.Facing;Frame=v.Frame;Height=v.Height;VerticalVelocity=v.Velocity;SupportId=v.SupportId;IsMoving=v.Moving;MovementFrame=v.MovementFrame;IdleFrame=v.IdleFrame;remainder=v.Remainder;
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
            using(var candidate=new OutsideExplorationSession()){
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
            if(definition.Effect==OutsideRecoveryEffect.Poison?!Player.HasPoisonState:Player.CurrentHp==Player.MaxHp){error=definition.Effect==OutsideRecoveryEffect.Poison?"解除する毒状態がありません":"HPは満タンです";return false;}
            usingItem=true;
            try{
                long until=checked(Core.Frame+definition.CooldownFrames);
                var actor=OutsidePlayerSaveData.Capture(Player);if(definition.Effect==OutsideRecoveryEffect.Poison)actor.Player.Poisons=Array.Empty<PlayerPoisonDto>();else actor.Player.Hp=(int)System.Math.Min(actor.Player.MaxHp,(long)actor.Player.Hp+definition.HealHp);actor.Validate();
                var runtime=CaptureItemRuntime(definitions);runtime.Id=actor.Id;
                var cooldowns=itemCooldowns.Where(v=>v.Value>Core.Frame&&v.Key!=definition.Id).Select(v=>new OutsideItemCooldownData{Id=v.Key,RemainingFrames=checked((int)(v.Value-Core.Frame))}).Concat(new[]{new OutsideItemCooldownData{Id=definition.Id,RemainingFrames=definition.CooldownFrames}}).ToArray();
                var itemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key(),Cooldowns=cooldowns};itemState.Validate(actor,runtime);
                if(!save.TryCommitOutsideItem(definition.Id,actor,runtime,itemState,out error))return false;
                // 保存成功後だけ現個体へ効果を適用。個体/時計/手札を置換しない。
                itemCooldowns[definition.Id]=until;appliedItemStateId=actor.Id;if(definition.Effect==OutsideRecoveryEffect.Poison)Player.ClearPoisons();else Player.Heal(definition.HealHp);savedPlayerId=actor.Id;savedPlayerKey=actor.Key();savedRuntimeId=runtime.Id;savedRuntimeKey=runtime.Key();return true;
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
        {
            var value=new OutsideRuntimeSaveData{Id=Guid.NewGuid().ToString("N"),Core=Core.CaptureState(),FixedCameraCenter=Camera.FixedCenter,Cards=Cards?.CaptureState(definitions),Attack=attackState?.Copy(),TrialTarget=CaptureTrialTarget(),Fall=CaptureFall(),Water=CaptureWater()};value.Validate();return value;
        }
        private string savedRuntimeId,savedRuntimeKey;
        public OutsideRuntimeSaveData CaptureRuntime(CheckpointDefinitions definitions=null)
        {
            if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));if(IsPresented)throw new InvalidOperationException("外界表示を閉じて退避してください");if(Core.HasFallenOut)throw new ArgumentException("死亡済み落下を生存状態として保存できません");
            var value=new OutsideRuntimeSaveData{Id=Guid.NewGuid().ToString("N"),Core=Core.CaptureState(),FixedCameraCenter=Camera.FixedCenter,Cards=Cards?.CaptureState(definitions),Attack=attackState?.Copy(),TrialTarget=CaptureTrialTarget(),Fall=CaptureFall(),Water=CaptureWater()};value.Validate();return value;
        }
        public bool RestoreSavedState(OutsidePlayerSaveData player,OutsideRuntimeSaveData runtime,string recoveryPoint,CheckpointDefinitions definitions=null,OutsideTraversalProfile initializationProfile=null)
        {
            if(runtime==null)return RestoreSavedPlayer(player,recoveryPoint);if(IsEnded)throw new ObjectDisposedException(nameof(OutsideExplorationSession));runtime.Validate();player?.Validate();if(player==null||player.Id!=runtime.Id)throw new ArgumentException("外界個体とRuntimeの攻略IDが一致しません");string key=runtime.Key(),playerKey=player.Key();
            if(savedRuntimeId==runtime.Id){if(savedRuntimeKey!=key||savedPlayerId!=player.Id||savedPlayerKey!=playerKey)throw new ArgumentException("同じ外界復帰IDの内容が変わりました");return false;}
            if(IsPresented)throw new InvalidOperationException("表示中の外界状態を置換できません");if(savedPlayerId==player.Id&&savedPlayerKey!=playerKey)throw new ArgumentException("同じ外界個体IDの内容が変わりました");
            // 全候補を先に検証し、途中失敗で旧Sessionの一部だけを書き換えない。
            if(initializationProfile!=null&&Core.Frame!=0)throw new ArgumentException("進行済みの外界地形設定を置換できません");
            var actor=player.Restore();var candidateCore=new OutsideExplorationCore(Core.Definition,initializationProfile??Core.TraversalProfile);candidateCore.RestoreState(runtime.Core);if(candidateCore.HasFallenOut)throw new ArgumentException("死亡済み落下を生存個体として復元できません");var camera=runtime.FixedCameraCenter.HasValue?OutsideCameraPolicy.FixedAt(runtime.FixedCameraCenter.Value):OutsideCameraPolicy.Follow;camera.ResolveHorizontalCenter(candidateCore.Position,candidateCore.Definition.Width,1);
            var cards=runtime.Cards==null?null:OutsideExplorationCards.RestoreState(runtime.Cards,definitions);var target=CreateTrialTarget(runtime.TrialTarget);
            ValidateAttack(runtime.Attack,cards,runtime.Core.Frame);
            ValidateWater(runtime.Water,candidateCore);
            if(runtime.Fall!=null&&runtime.Fall.Peak>candidateCore.MaximumReachableHeight())throw new ArgumentException("落下頂点が外界地形の上限を超えています");
            if(Cards!=null&&runtime.Cards!=null){try{if(Cards.CaptureState(definitions).Key()==runtime.Cards.Key())cards=Cards;}catch(ArgumentException){}}
            if(initializationProfile!=null)Core.ConfigureTraversal(initializationProfile);Core.RestoreState(runtime.Core);deckLocationArmed=managedDeckLocations&&OutsideTrialRoute.IsDeckEditingZone(Core);reloadGaugeOnExit=managedDeckLocations&&OutsideTrialRoute.IsReloadZone(Core);itemCooldowns.Clear();ResetCardUseFeedback();attackState=runtime.Attack?.Copy();trialTarget=target;trialTargetChanged=runtime.TrialTarget!=null;trialTargetHits=runtime.TrialTarget?.Hits??0;Player=actor;Cards=cards;Camera=camera;playerFrame=Core.Frame;RestoreFall(runtime.Fall);RestoreWater(runtime.Water);savedPlayerId=player.Id;savedPlayerKey=playerKey;savedRuntimeId=runtime.Id;savedRuntimeKey=key;PrepareMapEntry();return true;
        }
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>仕様で調整を許された試用値。移動定義・旧Core hashとは独立。</summary>
    public static class OutsideFallRules
    {
        public const long SafeHeight=210000; // 3.5 world。平地の通常ジャンプ頂点3.1を上回る。
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
