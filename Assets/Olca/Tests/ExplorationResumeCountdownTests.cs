using NUnit.Framework;
using Olca.Core.Logic.Integration;
namespace Olca.Tests
{
    public class ExplorationResumeCountdownTests
    {
        private static Olca.Core.Domain.Card[] CardFixture()=>System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Range(0,5),id=>System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),n=>new Olca.Core.Domain.Card("outside-fixture-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))));
        private static System.Collections.Generic.Dictionary<(string Id,Olca.Core.Domain.ExceedLevel Level),int> Owned(Olca.Core.Domain.Card[] cards)=>System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(cards,c=>(c.Id,c.ExceedLevel)),g=>g.Key,g=>System.Linq.Enumerable.Count(g));
        private static OutsideExplorationSession SelectionSession()
        {
            var source=CardFixture();var session=new OutsideExplorationSession();Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));return session;
        }
        private static void SelectionSample(OutsideSelectionRepeat repeat,long time,bool previous=false,bool next=false,bool previousDown=false,bool nextDown=false,bool snapshot=false,long previousAt=0,long nextAt=0)
            =>repeat.Queue(time,previous,next,previousDown,nextDown,snapshot,previous||next,previousAt,nextAt);
        private static OutsideSelectionRepeat ReadySelection(OutsideExplorationCards cards)
        {
            var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,0);repeat.AdvanceFrame(cards);return repeat;
        }
        [Test] public void 外界巡回は即時一回と二十四九フレームで端を循環する()
        {
            using(var session=SelectionSession()){
                var cards=session.Cards;var repeat=ReadySelection(cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);
                for(int i=0;i<8;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(0,cards.SelectedIndex);
                Assert.AreEqual(0,session.Core.Frame);Assert.AreEqual(6000,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(17,cards.RemainingCount);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 外界巡回の同時刻両押下は列挙順によらず次を一回採用する(bool reverse)
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);
                SelectionSample(repeat,10,previous:!reverse,next:reverse,previousDown:!reverse,nextDown:reverse);
                SelectionSample(repeat,10,previous:true,next:true,previousDown:reverse,nextDown:!reverse);
                repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は後押し優先と優先解除後の反対保持待機を守る()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
                SelectionSample(repeat,2,previous:true,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,3,previous:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は同フレーム短押しを失わず初回保持を解除まで拒否する()
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,1,next:true,snapshot:true);repeat.AdvanceFrame(session.Cards);for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,2);SelectionSample(repeat,3,next:true,nextDown:true);SelectionSample(repeat,4);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(20,10,2)] [TestCase(10,20,1)] [TestCase(20,20,1)]
        public void 外界巡回の再開保持は新押下とせず時刻優先で二十四フレーム待つ(long previousAt,long nextAt,int expected)
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();repeat.Reset(true);SelectionSample(repeat,30,previous:true,next:true,snapshot:true,previousAt:previousAt,nextAt:nextAt);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(expected,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は入力禁止中の選択を進めず解除後の保持を継続する()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<9;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(-1)] [TestCase(3)] [TestCase(int.MaxValue)]
        public void 外界の直接選択は初期左端と範囲を守りカードと時計を変更しない(int invalid)
        {
            var source=CardFixture();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=session.Cards;var hand=System.Linq.Enumerable.ToArray(cards.Hand);long gauge=session.Core.CardGauge.CurrentUnits;
                Assert.AreEqual(0,cards.SelectedIndex);Assert.AreSame(hand[0],cards.SelectedCard);Assert.IsTrue(cards.TrySelect(2));Assert.AreSame(hand[2],cards.SelectedCard);
                Assert.IsFalse(cards.TrySelect(invalid));Assert.AreEqual(2,cards.SelectedIndex);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(20,cards.TotalCount);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(0,session.Core.Frame);
                var first=session.AttachPresentation();first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);Assert.AreEqual(2,next.Cards.SelectedIndex);next.Dispose();
            }
        }
        [Test] public void 外界初期手札は独立した二十枚から三枚を引き表示借用と時計で再抽選しない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));var cards=session.Cards;Assert.AreEqual(20,cards.TotalCount);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(3,cards.Hand.Count);
                foreach(var card in cards.Hand)foreach(var original in source)Assert.AreNotSame(original,card);
                var first=session.AttachPresentation();var hand=System.Linq.Enumerable.ToArray(cards.Hand);for(int n=0;n<120;n++)session.Core.TickFrame(0);first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);CollectionAssert.AreEqual(hand,next.Cards.Hand);Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));next.Dispose();Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [Test] public void 外界初回登録は所持と構築制限を共用し拒否で状態を作らない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsFalse(session.TryInitializeCards(System.Array.Empty<Olca.Core.Domain.Card>(),Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.IsFalse(session.TryInitializeCards(source,new System.Collections.Generic.Dictionary<(string,Olca.Core.Domain.ExceedLevel),int>(),random,out _));Assert.IsNull(session.Cards);
                using(var lease=session.AttachPresentation())Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.Throws<System.ArgumentNullException>(()=>session.TryInitializeCards(source,Owned(source),null,out _));Assert.IsNull(session.Cards);
                var over=(Olca.Core.Domain.Card[])source.Clone();over[4]=source[0];Assert.IsFalse(session.TryInitializeCards(over,Owned(over),random,out _));Assert.IsNull(session.Cards);
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [TestCase(1)] [TestCase(5)] [TestCase(90)]
        public void 論理フレームのまとめ方にかかわらず三二一は各三十で完了通知は一回(int batch)
        {
            var timer=new ExplorationResumeCountdown();timer.Begin();int finished=0;
            for(int start=0;start<90;start+=batch)for(int i=start;i<System.Math.Min(start+batch,90);i++)
            {Assert.AreEqual(3-i/30,timer.DisplayCount);if(timer.AdvanceFrame())finished++;}
            Assert.AreEqual(1,finished);Assert.AreEqual(0,timer.DisplayCount);Assert.IsFalse(timer.AdvanceFrame());
        }
        [Test] public void 再開連打は延長せず取消後だけ全長で始める()
        {
            var timer=new ExplorationResumeCountdown();Assert.IsFalse(timer.AdvanceFrame());timer.Begin();timer.AdvanceFrame();timer.Begin();Assert.AreEqual(89,timer.RemainingFrames);
            timer.Cancel();Assert.AreEqual(0,timer.DisplayCount);timer.Begin();Assert.AreEqual(90,timer.RemainingFrames);
        }
        [Test] public void 外界Sessionは表示借用を返して再表示しても同じ状態を保持し旧借用は新表示を閉じない()
        {
            var session=new OutsideExplorationSession();var first=session.AttachPresentation();Assert.IsTrue(first.IsCurrent);first.Dispose();Assert.IsFalse(session.IsPresented);Assert.IsFalse(session.IsEnded);
            var second=session.AttachPresentation();var third=session.AttachPresentation();Assert.IsFalse(second.IsCurrent);second.Dispose();Assert.IsTrue(third.IsCurrent);Assert.IsTrue(session.IsPresented);third.Dispose();Assert.IsFalse(session.IsPresented);
        }
        [Test] public void 外界SessionはRoot終了後の再表示と旧入力を受けない()
        {
            var session=new OutsideExplorationSession();var lease=session.AttachPresentation();session.Dispose();session.Dispose();Assert.IsTrue(session.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsFalse(session.IsPresented);lease.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>session.AttachPresentation());
        }

        [TestCase(1)] [TestCase(10)] [TestCase(60)]
        public void 外界横移動は描画分割に依存せず六十Hzで進み停止と反転ができる(int parts)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();
            long consumed=0;
            for(int i=1;i<=parts;i++){long target=LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((frame,ms)=>core.TickFrame(1));}
            Assert.AreEqual(60,core.Frame);Assert.AreEqual(14*OutsideMovementDefinition.UnitsPerWorldUnit,core.Position);
            long stopped=core.Position;for(int i=0;i<60;i++)core.TickFrame(0);Assert.AreEqual(stopped,core.Position);
            for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(core.Definition.Start,core.Position);Assert.AreEqual(-1,core.Facing);
        }
        [Test] public void 外界の境界で止まり逆入力は即座に離れ検証状態は表示借用と別に保持される()
        {
            var s=new OutsideExplorationSession();var lease=s.AttachPresentation();for(int i=0;i<1000;i++)s.Core.TickFrame(-1);Assert.AreEqual(0,s.Core.Position);
            lease.Dispose();long frame=s.Core.Frame;var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual(frame,next.Core.Frame);
            s.Core.TickFrame(1);Assert.Greater(s.Core.Position,0);for(int i=0;i<1000;i++)s.Core.TickFrame(1);Assert.AreEqual(s.Core.Definition.Width,s.Core.Position);
            s.Core.TickFrame(-1);Assert.Less(s.Core.Position,s.Core.Definition.Width);
        }
        [Test] public void 外界定義が受け付ける整数極値でも端から反対側へ桁あふれしない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,long.MaxValue,long.MaxValue));
            core.TickFrame(1);Assert.AreEqual(long.MaxValue,core.Position);core.TickFrame(-1);Assert.Greater(core.Position,0);Assert.Less(core.Position,long.MaxValue);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,0,long.MaxValue));left.TickFrame(-1);Assert.AreEqual(0,left.Position);left.TickFrame(1);Assert.Greater(left.Position,0);
        }
        [Test] public void 外界低速設定でも端から逆入力で離れ整数未満の余りを失わない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,100,1));for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(99,core.Position);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,0,1));for(int i=0;i<60;i++)left.TickFrame(1);Assert.AreEqual(1,left.Position);
        }

        [Test] public void 外界Idleは移動から停止するたびゼロで開始し停止中だけ進む()
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);
            Assert.AreEqual(0,core.IdleFrame);
            for(int cycle=0;cycle<4;cycle++){
                for(int i=0;i<17;i++)core.TickFrame(1);
                Assert.AreEqual(0,core.IdleFrame);core.TickFrame(0);
                Assert.AreEqual(0,core.IdleFrame);Assert.AreEqual(0,core.MovementFrame);
                for(int i=1;i<=13;i++){core.TickFrame(0);Assert.AreEqual(i,core.IdleFrame);}
            }
        }
        [Test] public void 外界Idleは着地と境界停止でもゼロから始まり表示再入で時計を変えない()
        {
            using(var session=new OutsideExplorationSession()){
                var core=session.Core;for(int i=0;i<19;i++)core.TickFrame(0);
                core.TickFrame(0,true,true);Assert.AreEqual(0,core.IdleFrame);
                for(int i=0;i<120&&!core.IsGrounded;i++)core.TickFrame(0,false,true);
                Assert.IsTrue(core.IsGrounded);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(0);Assert.AreEqual(1,core.IdleFrame);
                var first=session.AttachPresentation();long frame=core.Frame;first.Dispose();
                var second=session.AttachPresentation();Assert.AreEqual(1,core.IdleFrame);Assert.AreEqual(frame,core.Frame);second.Dispose();
                while(core.Position>0)core.TickFrame(-1);
                core.TickFrame(-1);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(-1);Assert.AreEqual(1,core.IdleFrame);
            }
        }

        [TestCase(1)] [TestCase(7)] [TestCase(60)]
        public void 外界ジャンプは保持で高くなり描画分割で軌道が変わらない(int parts)
        {
            var held=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var tap=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();long prior=0,peak=0,shortPeak=0;
            for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-prior);prior=target;clock.Drain((f,ms)=>{held.TickFrame(1,f==1,true);tap.TickFrame(1,f==1,f==1);peak=System.Math.Max(peak,held.Height);shortPeak=System.Math.Max(shortPeak,tap.Height);});}
            Assert.AreEqual(186000,peak);Assert.Less(shortPeak,peak);Assert.IsTrue(held.IsGrounded);Assert.IsTrue(tap.IsGrounded);Assert.AreEqual(20*OutsideMovementDefinition.UnitsPerWorldUnit,held.Position);Assert.AreEqual(120,held.Frame);
        }
        [Test] public void 外界ジャンプは保持着地と空中押下で再ジャンプを予約しない()
        {
            var c=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
            c.TickFrame(0,true,true);Assert.IsTrue(c.IsGrounded);c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,0);
            c.TickFrame(0,false,false);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
        }
        [Test] public void 外界空中状態はView借用終了で進まず再表示時も保持する()
        {
            var s=new OutsideExplorationSession();var a=s.AttachPresentation();s.Core.TickFrame(-1,true,true);long y=s.Core.Height,v=s.Core.VerticalVelocity,f=s.Core.Frame;a.Dispose();var b=s.AttachPresentation();Assert.AreSame(s.Core,b.Core);Assert.AreEqual(y,b.Core.Height);Assert.AreEqual(v,b.Core.VerticalVelocity);Assert.AreEqual(f,b.Core.Frame);b.Dispose();
        }
        private static OutsideExplorationCore LandingCore(params OutsideLandingSurface[] surfaces)=>new OutsideExplorationCore(new OutsideMovementDefinition("landing-test",32*60000,8*60000,6*60000,surfaces));
        private static void LandOnValidationTop(OutsideExplorationCore c){for(int i=0;i<80;i++){c.TickFrame(1,i==0,true);if(c.SupportId=="top")return;}Assert.Fail("検証上面へ着地しない");}
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界上面接地は描画分割に依存せず下から通過して下降時だけ止まる(int parts)
        {
            var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));var clock=new LogicalFrameClock();long prior=0;bool landed=false,passedBelow=false;
            for(int i=1;i<=parts;i++){long t=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(t-prior);prior=t;clock.Drain((f,ms)=>{c.TickFrame(landed?0:1,f==1,true);if(c.VerticalVelocity>0&&c.Height<60000)passedBelow=true;if(c.SupportId=="top")landed=true;});}
            Assert.IsTrue(passedBelow);Assert.IsTrue(landed);Assert.IsTrue(c.IsGrounded);Assert.AreEqual("top",c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(0,c.VerticalVelocity);Assert.AreEqual(120,c.Frame);
        }
        [Test] public void 外界足場上の保持は再ジャンプせず新しい押下だけで飛び両端から落ちる()
        {
            foreach(int direction in new[]{-1,1}){var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));LandOnValidationTop(c);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.AreEqual(60000,c.Height);Assert.AreEqual("top",c.SupportId);
                c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,60000);Assert.IsFalse(c.IsGrounded);for(int i=0;i<100;i++)c.TickFrame(0,false,true);Assert.AreEqual("top",c.SupportId);
                for(int i=0;i<80&&c.IsGrounded;i++)c.TickFrame(direction);Assert.IsFalse(c.IsGrounded);Assert.IsNull(c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(-400,c.VerticalVelocity);
                for(int i=0;i<60;i++)c.TickFrame(direction);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);}
        }
        [Test] public void 外界重複上面は配列順でなく最初に横切る高さと識別子で決まる()
        {
            var low=new OutsideLandingSurface("low",0,32*60000,58000);var a=new OutsideLandingSurface("a",0,32*60000,60000);var b=new OutsideLandingSurface("b",0,32*60000,60000);
            foreach(var surfaces in new[]{new[]{low,b,a},new[]{a,low,b}}){var c=LandingCore(surfaces);for(int i=0;i<100;i++)c.TickFrame(0,i==0,true);Assert.AreEqual("a",c.SupportId);Assert.AreEqual(60000,c.Height);}
        }
        [Test] public void 外界上面の支持は高さと端を検証し旧入力配列の変更から独立する()
        {
            var top=new OutsideLandingSurface("top",12*60000,16*60000,60000);Assert.IsTrue(top.Contains(top.Left));Assert.IsTrue(top.Contains(top.Right));Assert.IsFalse(top.Contains(top.Left-1));Assert.IsFalse(top.Contains(top.Right+1));
            var input=new[]{top};var c=LandingCore(input);input[0]=new OutsideLandingSurface("other",0,32*60000,90000);LandOnValidationTop(c);Assert.AreEqual("top",c.SupportId);
            Assert.Throws<System.ArgumentException>(()=>LandingCore(top,top));Assert.Throws<System.ArgumentException>(()=>LandingCore(new OutsideLandingSurface("overflow",0,32*60000,long.MaxValue)));
        }
        [Test] public void 外界上面を同frameで横断し終点が面外なら検証モデルでは着地しない()
        {
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("fast",100*60000,0,60*60000,new OutsideLandingSurface("thin",334000,338000,60000)));
            // 初めはその場で跳び、下降時だけ高速横移動する。狭い上面を飛び越してfloorへ戻る。
            for(int i=0;i<55;i++)c.TickFrame(i>=50?1:0,i==0,true);Assert.AreEqual(66000,c.Height);Assert.AreEqual(300000,c.Position);c.TickFrame(1,false,true);Assert.AreEqual(56000,c.Height);Assert.IsNull(c.SupportId);for(int i=0;i<64;i++)c.TickFrame(1,false,true);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);
        }
        [Test] public void 外界支持面の識別子と位置は表示借用を返しても保持される()
        {
            var s=new OutsideExplorationSession();var first=s.AttachPresentation();for(int i=0;i<80&&s.Core.SupportId!="validation-top";i++)s.Core.TickFrame(1,i==0,true);Assert.AreEqual("validation-top",s.Core.SupportId);long y=s.Core.Height,f=s.Core.Frame,x=s.Core.Position;first.Dispose();var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual("validation-top",next.Core.SupportId);Assert.AreEqual(y,next.Core.Height);Assert.AreEqual(f,next.Core.Frame);Assert.AreEqual(x,next.Core.Position);next.Dispose();
        }
        [Test] public void 外界カメラ追従は既存の両端余白と狭いマップの中央を保つ()
        {
            var p=OutsideCameraPolicy.Follow;const long u=60000;
            Assert.AreEqual(4,p.ResolveHorizontalCenter(0,32*u,10));Assert.AreEqual(8,p.ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(28,p.ResolveHorizontalCenter(32*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界固定カメラは移動で動かず画面比率とマップ範囲に応じて拘束する()
        {
            const long u=60000;var p=OutsideCameraPolicy.FixedAt(12*u);
            foreach(long x in new[]{0,8*u,16*u,32*u}){Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,10));Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,20));}
            Assert.AreEqual(4,OutsideCameraPolicy.FixedAt(0).ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界カメラ設定は非表示だけで変更しView世代や位置と時計を初期化しない()
        {
            using(var s=new OutsideExplorationSession()){
                var core=s.Core;core.TickFrame(1);long x=core.Position,f=core.Frame;var fixedPolicy=OutsideCameraPolicy.FixedAt(12*60000);
                Assert.IsTrue(s.TryConfigureCamera(fixedPolicy));var a=s.AttachPresentation();Assert.AreSame(fixedPolicy,a.Camera);Assert.IsFalse(s.TryConfigureCamera(OutsideCameraPolicy.Follow));var b=s.AttachPresentation();a.Dispose();Assert.IsTrue(b.IsCurrent);Assert.AreSame(fixedPolicy,b.Camera);b.Dispose();Assert.AreEqual(x,core.Position);Assert.AreEqual(f,core.Frame);Assert.IsTrue(s.TryConfigureCamera(OutsideCameraPolicy.Follow));Assert.AreSame(OutsideCameraPolicy.Follow,s.Camera);
            }
        }
        [Test] public void 外界カメラ不正値と終了後の設定を拒否する()
        {
            const long u=60000;Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(-1));var p=OutsideCameraPolicy.Follow;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,float.NaN));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(-1,32*u,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(33*u).ResolveHorizontalCenter(0,32*u,10));
            var s=new OutsideExplorationSession();Assert.Throws<System.ArgumentNullException>(()=>s.TryConfigureCamera(null));Assert.Throws<System.ArgumentOutOfRangeException>(()=>s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(33*u)));s.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>s.TryConfigureCamera(p));
        }
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界カードゲージは描画分割に依存せず移動ジャンプ中も六十Hzで回復する(int batch)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);Assert.IsTrue(core.CardGauge.TrySpend(100));
            for(int i=0;i<120;i+=batch)for(int j=i;j<System.Math.Min(i+batch,120);j++)core.TickFrame(j<60?1:-1,j==0,j<24);
            Assert.AreEqual(20*OutsideCardGauge.UnitsPerPoint,core.CardGauge.CurrentUnits);Assert.AreEqual(120,core.Frame);
        }
        [Test] public void 外界カードゲージ不足と不正消費は残量を変えず回復に待ち時間を設けない()
        {
            var g=new OutsideCardGauge(100,10,20);Assert.IsFalse(g.TrySpend(21));Assert.AreEqual(1200,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.TrySpend(-1));Assert.IsTrue(g.TrySpend(20));g.AdvanceFrame();Assert.AreEqual(10,g.CurrentUnits);Assert.IsTrue(g.TrySpend(0));Assert.AreEqual(10,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージ最大量変更は残量を維持し超過だけ切り捨てる()
        {
            var g=new OutsideCardGauge(100,10,80);g.AdvanceFrame();long amount=g.CurrentUnits;g.Configure(140,20);Assert.AreEqual(amount,g.CurrentUnits);g.Configure(50,0);Assert.AreEqual(3000,g.CurrentUnits);Assert.IsTrue(g.TrySpend(10));for(int i=0;i<120;i++)g.AdvanceFrame();Assert.AreEqual(2400,g.CurrentUnits);g.RestoreFull();Assert.AreEqual(3000,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージは無効設定を原子的に拒否し表現上限でも溢れない()
        {
            var g=new OutsideCardGauge(100,10,80);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(0,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(200,-1));Assert.AreEqual(100,g.Maximum);Assert.AreEqual(10,g.RecoveryPerSecond);Assert.AreEqual(4800,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>new OutsideCardGauge(100,1,101));
            var large=new OutsideCardGauge(int.MaxValue,int.MaxValue,int.MaxValue);Assert.IsTrue(large.TrySpend(int.MaxValue));for(int i=0;i<61;i++)large.AdvanceFrame();Assert.AreEqual(large.MaximumUnits,large.CurrentUnits);
        }
        [Test] public void 外界カードゲージはView世代と切断で初期化せずSessionごとに独立する()
        {
            using(var a=new OutsideExplorationSession())using(var b=new OutsideExplorationSession()){a.Core.CardGauge.TrySpend(20);long amount=a.Core.CardGauge.CurrentUnits;var first=a.AttachPresentation();var second=a.AttachPresentation();first.Dispose();Assert.IsTrue(second.IsCurrent);second.Dispose();Assert.AreEqual(amount,a.Core.CardGauge.CurrentUnits);Assert.AreEqual(6000,b.Core.CardGauge.CurrentUnits);}
        }
        [TestCase(1,0,"0.9 / 1","1.0 / 1")] [TestCase(100,99,"99.9 / 100","100.0 / 100")]
        public void 外界ゲージ表示は整数コスト直前と満量直前に消費可能量を過大表示しない(int maximum,int initial,string before,string full)
        {
            var gauge=new OutsideCardGauge(maximum,1,initial);for(int i=0;i<59;i++)gauge.AdvanceFrame();long units=gauge.CurrentUnits;
            StringAssert.EndsWith(before,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.AreEqual(units,gauge.CurrentUnits);Assert.IsFalse(gauge.TrySpend(maximum));
            gauge.AdvanceFrame();StringAssert.EndsWith(full,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.IsTrue(gauge.TrySpend(maximum));Assert.AreEqual(0,gauge.CurrentUnits);
        }
    }
}
