using System;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using UnityEngine;

namespace Olca.Presentation.Effects
{
    /// <summary>入力所有者から受け取った表示専用の読取値。別Battleや取消後へ持ち越さない。</summary>
    public readonly struct BattleChargeVisualState
    {
        public readonly BattleSystem Battle;public readonly bool Charging;public readonly int Frames,Required;private readonly long epoch,serial;
        public BattleChargeVisualState(BattleSystem battle,bool charging,int frames,int required){Battle=battle;Charging=charging;Frames=frames;Required=required;epoch=battle?.AttackInputEpoch??-1;serial=battle?.AttackActionSerial??-1;}
        public bool Full=>Required>0&&Frames>=Required;
        public bool CanDisplayFor(BattleSystem battle)=>ReferenceEquals(Battle,battle)&&battle!=null&&epoch==battle.AttackInputEpoch&&serial==battle.AttackActionSerial&&Charging&&Frames>0&&Required>0&&
            !battle.IsGameOver&&!battle.Player.IsDead&&!battle.Player.IsActionPrevented&&!battle.HasPlayerAction&&
            battle.CardUsage.InFlightCard==null&&!battle.CustomScreen.IsOpen&&battle.Transformations.ActiveId==null&&
            (battle.Player.DepartureStyle.Id==DepartureStyleDefinition.BasicId||battle.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id);
    }
    /// <summary>既存の通常攻撃phaseを読むだけの試用本体。命中後3論理フレームの接触姿勢は暫定演出。</summary>
    public sealed class TrialBasicKickBody : IDisposable
    {
        public const int ContactPoseFrames=3; // 暫定の見た目。命中窓や後隙を延長しない。
        private readonly SpriteRenderer body;
        private readonly Animator animator;
        private readonly Func<string,Sprite> load;
        private Sprite prepare,contact,original;
        private BattleSystem owner;
        private bool attempted,active,animatorWasEnabled,originalFlip,disposed;
        private readonly Olca.Presentation.Exploration.TrialChargeMotion chargeMotion;
        // 表示phaseを4姿勢のindexへ渡すだけのアダプタ。ゲームの前後隙ではない。
        private static readonly Olca.Core.Logic.Integration.OutsideKickTiming ChargePoseTiming=new Olca.Core.Logic.Integration.OutsideKickTiming(1,1,1);
        public TrialBasicKickBody(SpriteRenderer body,Animator animator,Func<string,Sprite> loader=null,Func<string,Sprite[]> chargeLoader=null)
        {
            this.body=body!=null?body:throw new ArgumentNullException(nameof(body));this.animator=animator;
            load=loader??(key=>Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_"+key));
            chargeMotion=new Olca.Presentation.Exploration.TrialChargeMotion(chargeLoader);
        }
        public static bool CanDisplay(BattleSystem battle)=>battle!=null&&battle.IsPlayerBasicAction&&!battle.IsGameOver&&!battle.Player.IsDead&&
            !battle.Player.IsActionPrevented&&battle.Transformations.ActiveId==null&&
            (battle.Player.DepartureStyle.Id==DepartureStyleDefinition.BasicId||battle.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id);
        public bool Apply(BattleSystem battle,BattleChargeVisualState charge=default)
        {
            // 溜め中は元Animatorと姿勢を保ち、解放後の攻撃だけを専用素材にする。
            if(disposed||body==null||!CanDisplay(battle)){
                Clear();return false;
            }
            Sprite selected;bool dedicated=battle.IsPlayerChargedBasicAction;
            bool striking=battle.PlayerActionPhase==PlayerAttackPhase.Executing||battle.PlayerActionPhase==PlayerAttackPhase.Recovery&&battle.RecoveryElapsedFrames<ContactPoseFrames;
            if(dedicated){int elapsed=battle.PlayerActionPhase==PlayerAttackPhase.Startup?0:striking?1:2;
                selected=chargeMotion.Resolve(true,false,false,elapsed,ChargePoseTiming,battle.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id);
            }else{if(!attempted){attempted=true;prepare=load("prepare");contact=load("contact");}selected=prepare!=null&&contact!=null?(striking?contact:prepare):null;}
            if(selected==null){Clear();return false;}
            if(!ReferenceEquals(owner,battle))Clear();
            if(!active){original=body.sprite;originalFlip=body.flipX;animatorWasEnabled=animator!=null&&animator.enabled;owner=battle;active=true;}
            if(animator!=null)animator.enabled=false;
            // 専用連番は右向き原画。既存左向き原画の反転だけを相殺し、Transformや足元は動かさない。
            body.flipX=dedicated?!originalFlip:originalFlip;body.sprite=selected;return true;
        }
        public void Clear()
        {
            if(active){if(body!=null){body.sprite=original;body.flipX=originalFlip;}if(animator!=null)animator.enabled=animatorWasEnabled;}
            active=false;owner=null;original=null;
        }
        public void Dispose(){if(disposed)return;Clear();disposed=true;chargeMotion.Dispose();prepare=contact=null;}
    }
}

namespace Olca.Presentation.Effects
{
    /// <summary>Dungeonの単発Basic用。命中通知では値を控えるだけで、描画例外をCoreへ返さない。</summary>
    public sealed class BattleKickVisual : IDisposable
    {
        private readonly Transform parent;private readonly Func<SpriteRenderer> readBody;private readonly Func<Olca.Presentation.Field.WorldBattleGridView> readGrid;
        private readonly PixelCombatWorld trail;private readonly SlashSpriteWorld slash;private readonly TrialSlashSprites sprites=new TrialSlashSprites();private BattleSystem owner;private Action<Enemy,int> hitHandler;private bool disposed;
        private long hitSerial=long.MinValue;private int hitX,hitRange;private bool hitFire,hitCharged;private float remaining;
        private GameObject labelRoot;private TextMesh label,shadow;private Renderer labelRenderer,shadowRenderer;private string shown;
        public int ObservationErrors{get;private set;}
        public int EmissionCount{get;private set;} public int ActivePixels=>trail.ActiveCount+(slash.IsVisible?1:0);
        public BattleKickVisual(Transform parent,Func<SpriteRenderer> body,Func<Olca.Presentation.Field.WorldBattleGridView> grid){this.parent=parent;readBody=body;readGrid=grid;trail=new PixelCombatWorld(parent,"ProvisionalBattleKickTrail");slash=new SlashSpriteWorld(parent);}
        public void Warmup()
        {
            if(disposed||parent==null)return;trail.Warmup();slash.Warmup();if(labelRoot!=null)return;var font=Resources.Load<Font>("OlcaUI/MenuText");if(font==null)return;
            labelRoot=new GameObject("ProvisionalBasicAttackCaption");labelRoot.transform.SetParent(parent,false);
            TextMesh Text(string name,Color ink,Vector3 offset){var go=new GameObject(name,typeof(TextMesh));go.transform.SetParent(labelRoot.transform,false);go.transform.localPosition=offset;var t=go.GetComponent<TextMesh>();t.font=font;t.fontSize=32;t.characterSize=.08f;t.anchor=TextAnchor.UpperCenter;t.alignment=TextAlignment.Center;t.color=ink;t.GetComponent<Renderer>().sharedMaterial=font.material;return t;}
            shadow=Text("Shadow",new Color(.01f,.025f,.04f,1),new Vector3(.02f,-.02f,.01f));label=Text("Text",Color.white,Vector3.zero);labelRenderer=label.GetComponent<Renderer>();shadowRenderer=shadow.GetComponent<Renderer>();labelRoot.SetActive(false);
        }
        public void Bind(BattleSystem battle)
        {
            if(disposed||ReferenceEquals(owner,battle))return;if(owner!=null&&hitHandler!=null)owner.OnPlayerAttack-=hitHandler;owner=battle;hitHandler=null;remaining=0;hitSerial=long.MinValue;Clear();
            if(owner!=null){hitHandler=(enemy,amount)=>{try{Record(battle);}catch(Exception){ObservationErrors++;remaining=0;}};owner.OnPlayerAttack+=hitHandler;}
        }
        private void Record(BattleSystem battle)
        {
            if(disposed||!ReferenceEquals(owner,battle)||!battle.IsPlayerBasicAction||battle.Player==null||battle.Player.IsBlind||battle.Transformations.ActiveId!=null)return;
            var id=battle.Player.DepartureStyle.Id;if(id!=DepartureStyleDefinition.BasicId&&id!=DepartureStyleDefinition.TrialFire.Id)return;
            long serial=battle.AttackActionSerial;if(battle.IsPlayerBasicAction&&hitSerial==serial)return;
            hitSerial=serial;hitX=battle.Player.GridPosition.X;hitRange=battle.BasicAttackRange;hitFire=id==DepartureStyleDefinition.TrialFire.Id;hitCharged=battle.IsPlayerChargedBasicAction;remaining=.18f;EmissionCount++;
        }
        public static Rect AttackArea(Olca.Presentation.Field.WorldBattleGridView grid,SpriteRenderer body,int column,int range)
        {
            float cells=Mathf.Min(Mathf.Max(0,range),Mathf.Max(0,Olca.Core.Logic.Battle.BattleField.GRID_WIDTH-1-column));var b=body.bounds;
            return new Rect(grid.GridIndexToWorldX(column)+grid.CellSize*.1f,b.min.y,Mathf.Max(0,cells*grid.CellSize),Mathf.Max(.3f,b.size.y*.65f));
        }
        public void Tick(float deltaTime,BattleChargeVisualState charge=default)
        {
            if(disposed)return;var body=readBody?.Invoke();var grid=readGrid?.Invoke();if(owner==null||body==null||grid==null){Clear();return;}Warmup();
            bool known=owner.Transformations.ActiveId==null&&(owner.Player.DepartureStyle.Id==DepartureStyleDefinition.BasicId||owner.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id)&&!owner.Player.IsDead&&!owner.Player.IsBlind;
            if(!known){remaining=0;Clear();return;}if(!owner.IsPaused&&deltaTime>0&&!float.IsNaN(deltaTime)&&!float.IsInfinity(deltaTime))remaining=Mathf.Max(0,remaining-deltaTime);
            bool charging=charge.CanDisplayFor(owner);bool basic=owner.IsPlayerBasicAction;bool charged=basic?owner.IsPlayerChargedBasicAction:hitCharged;var phase=owner.PlayerActionPhase;bool contact=basic&&(phase==PlayerAttackPhase.Executing||phase==PlayerAttackPhase.Recovery&&owner.RecoveryElapsedFrames<TrialBasicKickBody.ContactPoseFrames);
            bool startup=basic&&phase==PlayerAttackPhase.Startup;float strength=contact?1:remaining>0?remaining/.18f*.55f:basic&&phase==PlayerAttackPhase.Recovery?.25f*Mathf.Clamp01(1-(owner.RecoveryElapsedFrames-3)/6f):0;
            int x=basic?owner.Player.GridPosition.X:hitX,range=basic?owner.BasicAttackRange:hitRange;bool fire=basic?owner.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id:hitFire;
            if(charging){slash.Clear();var bounds=body.bounds;float ratio=Mathf.Clamp01(charge.Frames/(float)charge.Required);trail.Arc(new Rect(bounds.min.x,bounds.min.y,bounds.size.x,bounds.size.y),2,ratio,.35f+.65f*ratio,ProvisionalPixelPattern.Tint(owner.Player.DepartureStyle.Id==DepartureStyleDefinition.TrialFire.Id),body);}
            else if(!startup&&strength>0){trail.Clear();
                // 戦闘は各マスへの同時命中。三段の探索仕様を持ち込まず、共通の前方軌跡を重ねる。
                int frame=basic?(phase==PlayerAttackPhase.Executing?0:owner.RecoveryElapsedFrames<3?1:owner.RecoveryElapsedFrames<6?2:3):3;
                slash.Show(AttackArea(grid,body,x,range),sprites.Resolve(charged?3:0,frame),strength,body);
            }else{trail.Clear();slash.Clear();}
            bool showLabel=charging||basic||owner.IsGameOver&&remaining>0;if(labelRoot!=null){labelRoot.SetActive(showLabel);if(showLabel){string text=charging?(charge.Full?"タメ カンリョウ（カリ）":"タメチュウ（カリ）"):charged?(startup?"タメ（カリ） / ジュンビ":contact?"タメ（カリ） / ハツドウ":basic?"タメ（カリ） / フッキ":"タメ（カリ） / ヒット"):(startup?"キホン（カリ） / ジュンビ":contact?"キホン（カリ） / ハツドウ":basic?"キホン（カリ） / フッキ":"キホン（カリ） / ヒット");if(shown!=text){shown=text;label.text=shadow.text=text;}labelRoot.transform.position=new Vector3(body.bounds.center.x,body.bounds.min.y-.15f,body.transform.position.z-.1f);labelRenderer.sortingLayerID=shadowRenderer.sortingLayerID=body.sortingLayerID;labelRenderer.sortingOrder=body.sortingOrder+8;shadowRenderer.sortingOrder=body.sortingOrder+7;}}
        }
        public void Clear(){trail.Clear();slash.Clear();if(labelRoot!=null)labelRoot.SetActive(false);shown=null;}
        public void Dispose(){if(disposed)return;Bind(null);disposed=true;trail.Dispose();slash.Dispose();sprites.Dispose();if(labelRoot!=null){if(UnityEngine.Application.isPlaying)UnityEngine.Object.Destroy(labelRoot);else UnityEngine.Object.DestroyImmediate(labelRoot);}labelRoot=null;label=shadow=null;}
    }
}
