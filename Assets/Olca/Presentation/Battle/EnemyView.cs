using UnityEngine;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// 敵1体のView（World Space版）
    /// SpriteRenderer + TextMesh ベースで Canvas 不要
    /// WorldEnemyView と組み合わせて使用する
    /// </summary>
    public class EnemyView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer enemyImage;
        [SerializeField] private TextMesh enemyNameText;
        [SerializeField] private TextMesh hpText;
        [SerializeField] private Sprite gruntSprite;
        [SerializeField] private Sprite bossSprite;

        private Enemy enemy;
        private Olca.Unity.GeneratedSpriteAssets.Lease archerAssets;
        private bool archerWindup;
        private long archerReleaseAt=-1000,archerExpectedRelease;
        public bool IsTrialArcher=>Olca.Core.Logic.Battle.DungeonTrialArrowAction.IsArcherEnemyId(enemy?.EnemyId)||Olca.Core.Logic.Battle.DungeonStepArcherAction.IsPairTrialBoss(enemy)||Olca.Core.Logic.Battle.DungeonMixedPriority462.IsRear(enemy);
        public bool IsStepArcher=>enemy?.EnemyId==Olca.Core.Logic.Battle.DungeonStepArcherAction.EnemyId||Olca.Core.Logic.Battle.DungeonStepArcherAction.IsPairTrialBoss(enemy)||Olca.Core.Logic.Battle.DungeonMixedPriority462.IsRear(enemy);
        public bool IsTrialFlying455=>Olca.Core.Logic.Battle.DungeonFlyingDive455.Matches(enemy);
        private Vector3 flyOrigin455;private bool flyOriginKnown455;
        private Sprite FlyingSprite455(Olca.Core.Logic.Integration.OutsideFlyingPhase454 phase,int age=0)
        {
            if(archerAssets==null)archerAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);
            // 共有するのは自作絵の姿勢選択だけ。探索の時計/AIは使用しない。
            return Olca.Presentation.Exploration.FlyingTrialSprite454.Resolve(archerAssets,new Olca.Core.Logic.Integration.OutsideFlyingTrial454{Phase=phase,Age=age});
        }
        public void RenderTrialFlying455(bool warning,int elapsed,long units,int recovery=-1,int total=0,int facing=-1)
        {
            if(!IsTrialFlying455)return;var body=EnsureAttackPoseBody();if(body==null)return;
            if(!flyOriginKnown455){flyOrigin455=body.transform.localPosition;flyOriginKnown455=true;}
            bool down=recovery>=0&&total>0;bool dive=warning&&elapsed>=30;
            var phase=down?Olca.Core.Logic.Integration.OutsideFlyingPhase454.Recovery:dive?Olca.Core.Logic.Integration.OutsideFlyingPhase454.Dive:warning?Olca.Core.Logic.Integration.OutsideFlyingPhase454.Windup:Olca.Core.Logic.Integration.OutsideFlyingPhase454.Hover;
            body.sprite=FlyingSprite455(phase,(int)(units/50));if(facing!=0)body.flipX=facing<0;float foot=gruntSprite!=null?Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(gruntSprite).min.y:0;body.transform.localPosition=flyOrigin455+Vector3.up*(foot+(enemy.IsDead||down?0:dive?.6f*Mathf.Clamp01((42-elapsed)/12f):.6f));
        }
        private Sprite ArcherSprite(int frame)
        {
            if(archerAssets==null)archerAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);
            float foot=gruntSprite!=null?Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(gruntSprite).min.y:0f;
            float pivotY=20f-foot*100f;
            var frames=archerAssets.Get<Sprite[]>("DungeonTrialArcher428_"+pivotY.ToString(System.Globalization.CultureInfo.InvariantCulture),b=>{
                var bytes=Resources.Load<TextAsset>("OlcaPickups/trial_archer_v1");if(bytes==null)return null;
                var texture=b.CreateTexture(2,2);if(!texture.LoadImage(bytes.bytes,true)||texture.width!=2560||texture.height!=256)return null;
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=new Sprite[10];for(int i=0;i<10;i++){result[i]=b.CreateSprite(texture,new Rect(i*256,0,256,256),new Vector2(.5f,pivotY/256),100);result[i].name="dungeon_archer_428_"+i;}
                return result;
            });return frames==null?null:frames[Mathf.Clamp(frame,0,9)];
        }
        /// <summary>専用試用敵だけの論理時計による構え/放ち。判定や待機時間を更新しない。</summary>
        public void RenderTrialArcher(bool active,int elapsed,long logicalUnits,int recoveryElapsed=-1,int recoveryTotal=0)
        {
            ClearRecoveryHint();
            if(!IsTrialArcher||enemyImage==null)return;
            bool live=!enemy.IsDead&&!enemy.IsStunned&&!enemy.IsParalyzed;
            if(active)archerExpectedRelease=logicalUnits-elapsed*50L+1500;
            if(archerWindup&&!active&&live&&logicalUnits>=archerExpectedRelease)archerReleaseAt=archerExpectedRelease;
            if(!live)archerReleaseAt=-1000;
            archerWindup=active&&live;
            int frame=enemy.IsStaggered?8:archerWindup?4+Mathf.Min(2,elapsed/10):logicalUnits>=archerReleaseAt&&logicalUnits-archerReleaseAt<400?7:(int)(logicalUnits/500%4);
            var sprite=ArcherSprite(frame);if(sprite!=null)enemyImage.sprite=sprite;
            if(live&&!enemy.IsStaggered&&!active)RenderRecoveryHint(recoveryElapsed,recoveryTotal);
        }

        private LineRenderer recoveryHint;
        public void ClearRecoveryHint(){if(recoveryHint!=null)recoveryHint.enabled=false;}
        // 武器脇の短い息継ぎ。危険マスや弱点マークを描かず、実際の待ちの時計だけへ追従する。
        private void RenderRecoveryHint(int elapsed,int total)
        {
            if(!IsStepArcher||elapsed<0||total<=0||elapsed>=total||enemyImage==null)return;
            if(recoveryHint==null){var obj=new GameObject("ArcherRecoveryBreath439");obj.transform.SetParent(enemyImage.transform,false);recoveryHint=obj.AddComponent<LineRenderer>();recoveryHint.useWorldSpace=false;recoveryHint.positionCount=4;recoveryHint.numCapVertices=2;recoveryHint.sharedMaterial=enemyImage.sharedMaterial;recoveryHint.startWidth=recoveryHint.endWidth=.035f;}
            float phase=elapsed/(float)total,sign=enemyImage.flipX?1:-1;
            var bounds=Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(enemyImage.sprite);float x=sign*(Mathf.Abs(bounds.center.x)+bounds.extents.x+.07f),y=bounds.center.y;
            float rise=.10f*phase;var color=new Color(.82f,.58f,.50f,.8f-.35f*phase);recoveryHint.startColor=recoveryHint.endColor=color;recoveryHint.sortingLayerID=enemyImage.sortingLayerID;recoveryHint.sortingOrder=enemyImage.sortingOrder+1;
            recoveryHint.SetPosition(0,new Vector3(x,y,0));recoveryHint.SetPosition(1,new Vector3(x+sign*.08f,y+.045f+rise,0));recoveryHint.SetPosition(2,new Vector3(x+sign*.16f,y+rise,0));recoveryHint.SetPosition(3,new Vector3(x+sign*.24f,y+.045f+rise,0));recoveryHint.enabled=true;
        }
        private string displayedEnemyName;
        private LineRenderer bodyLink;
        private Olca.Presentation.Effects.DefensiveFieldVisual defensiveField;
        private Olca.Presentation.Effects.ActorConditionVisual guardVisual, paralysisVisual;
        private void RefreshConditionVisuals()
        {
            if (!isActiveAndEnabled || enemy == null || enemy.IsDead) { guardVisual?.Clear(); paralysisVisual?.Clear(); return; }
            if (guardVisual == null && enemy.Guard.IsActive) guardVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Guard);
            if (paralysisVisual == null && enemy.IsParalyzed) paralysisVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Paralyze);
            guardVisual?.Refresh(enemyImage, enemy.Guard.IsActive); paralysisVisual?.Refresh(enemyImage, enemy.IsParalyzed);
        }
        private void RefreshDefensiveField()
        {
            if (!isActiveAndEnabled || enemy == null || enemy.IsDead) { defensiveField?.Clear(); return; }
            if (defensiveField == null && (enemy.HasBarrier || enemy.HasAura)) defensiveField = new Olca.Presentation.Effects.DefensiveFieldVisual();
            defensiveField?.Refresh(enemyImage, enemy.HasBarrier, enemy.HasAura);
        }
        private static readonly System.Collections.Generic.List<EnemyView> bossGauges=new System.Collections.Generic.List<EnemyView>();
        private static readonly System.Collections.Generic.List<EnemyView> normalGauges=new System.Collections.Generic.List<EnemyView>();
        private const float BossHudSpacing=.10f;
        private Canvas statusCanvas;
        private CombatStatusIconStrip statusIcons;
        private void HideStatusIcons(){statusIcons?.Clear();if(statusCanvas!=null)statusCanvas.gameObject.SetActive(false);}
        private void RefreshStatusIcons()
        {
            var camera=Camera.main;
            if(!isActiveAndEnabled||!GaugeVisible||!IsBossGauge||camera==null){HideStatusIcons();return;}
            var entries=CombatStatusIcons.ForEnemy(enemy);
            if(entries.Count==0){HideStatusIcons();return;}
            if(statusCanvas==null){
                var obj=new GameObject("BossCombatStatusIcons",typeof(RectTransform),typeof(Canvas));obj.transform.SetParent(transform,false);
                statusCanvas=obj.GetComponent<Canvas>();statusCanvas.renderMode=RenderMode.WorldSpace;statusCanvas.overrideSorting=true;statusCanvas.sortingOrder=7;
                var rect=(RectTransform)obj.transform;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(48,520);
                var row=new GameObject("Icons",typeof(RectTransform));row.transform.SetParent(obj.transform,false);var rr=(RectTransform)row.transform;rr.anchorMin=rr.anchorMax=new Vector2(0,1);rr.pivot=new Vector2(0,1);rr.anchoredPosition=Vector2.zero;
                statusIcons=row.AddComponent<CombatStatusIconStrip>();statusIcons.Configure(1);
            }
            statusCanvas.gameObject.SetActive(true);statusIcons.Show(entries);
            float offset=BossHudSpacing*Mathf.Max(0,bossGauges.IndexOf(this));
            float depth=Mathf.Max(camera.nearClipPlane+1,camera.WorldToViewportPoint(transform.position).z);
            var point=camera.ViewportToWorldPoint(new Vector3(.90f-offset,.8f,depth));
            float worldHeight=Vector3.Distance(camera.ViewportToWorldPoint(new Vector3(.9f,.8f,depth)),camera.ViewportToWorldPoint(new Vector3(.9f,.845f,depth)));
            // HP/強靭度の実外周からタイル幅の1/4を空け、演出側へ張り出さない。
            float left=float.PositiveInfinity;
            foreach(var bar in new[]{hpBack,poiseBack})if(bar!=null&&bar.enabled)left=Mathf.Min(left,Vector3.Dot(bar.GetPosition(0)-point,camera.transform.right)-bar.startWidth*.5f);
            if(!float.IsPositiveInfinity(left))point+=camera.transform.right*(left-worldHeight*.75f);
            float scale=worldHeight/48;var parentScale=transform.lossyScale;
            statusCanvas.transform.localScale=new Vector3(scale/(parentScale.x==0?1:parentScale.x),scale/(parentScale.y==0?1:parentScale.y),scale/(parentScale.z==0?1:parentScale.z));
            statusCanvas.transform.position=point;statusCanvas.transform.rotation=camera.transform.rotation;
        }
        private bool hpVisible;
        private LineRenderer hpBack, hpFill, poiseBack, poiseFill;
        private Material gaugeMaterial;
        private Vector3 hpLocalPosition, hpLocalScale, nameLocalPosition;
        private bool namePositionCached;
        public bool IsBossGauge => enemy?.EnemyId == "boss";
        public bool GaugeVisible => hpVisible && enemy != null && !enemy.IsDead;
        // 固定HUDを敵本体の頭上位置として数えない。
        public bool IsFixedHudRenderer(Renderer renderer)=>IsBossGauge&&
            (renderer==hpBack||renderer==hpFill||renderer==poiseBack||renderer==poiseFill||hpText!=null&&renderer==hpText.GetComponent<Renderer>());
        public float HpFraction => enemy == null ? 0 : Mathf.Clamp01((float)enemy.CurrentHp / enemy.MaxHp);
        public float? PoiseFraction => enemy?.Poise == null ? (float?)null :
            enemy.IsStaggered ? 0 : Mathf.Clamp01((float)(enemy.Poise.Value-enemy.AccumulatedPoiseDamage)/enemy.Poise.Value);

        private void EnsureGauges()
        {
            if(hpBack!=null)return;
            gaugeMaterial=new Material(Shader.Find("Sprites/Default"));
            LineRenderer Line(string name,Color color) {
                var obj=new GameObject(name);obj.transform.SetParent(transform,false);
                var line=obj.AddComponent<LineRenderer>();line.sharedMaterial=gaugeMaterial;
                line.useWorldSpace=true;line.startColor=line.endColor=color;line.sortingOrder=3;
                line.numCapVertices=2;return line;
            }
            hpBack=Line("EnemyHpTrack",new Color(.035f,.1f,.13f,1));
            hpFill=Line("EnemyHpFill",new Color(.42f,.85f,.87f,1));
            poiseBack=Line("EnemyPoiseTrack",new Color(.12f,.13f,.08f,1));
            poiseFill=Line("EnemyPoiseFill",new Color(.95f,.8f,.27f,1));
        }
        private static void Arc(LineRenderer line,Vector3 center,float radius,float degrees,float width)
        {
            line.startWidth=line.endWidth=width;
            int steps=Mathf.Max(1,Mathf.CeilToInt(degrees/5));line.positionCount=steps+1;
            for(int n=0;n<=steps;n++){float a=(90-degrees*n/steps)*Mathf.Deg2Rad;line.SetPosition(n,center+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius);}
        }
        private static void Bar(LineRenderer line,Vector3 bottom,Vector3 top,float width,float fraction)
        {line.startWidth=line.endWidth=width;line.positionCount=2;line.SetPosition(0,bottom);line.SetPosition(1,Vector3.Lerp(bottom,top,fraction));}
        private readonly System.Collections.Generic.List<Rect> placedHudRects=new System.Collections.Generic.List<Rect>();
        private Rect NormalHudRect(Vector3 center,float radius)
        {
            var label=enemyNameText==null?null:enemyNameText.GetComponent<Renderer>();
            float width=label==null?0:label.bounds.size.x,height=label==null?0:label.bounds.size.y;
            return Rect.MinMaxRect(center.x-radius*1.6f-width,center.y-Mathf.Max(radius*1.4f,height*.5f),center.x+radius*1.4f,center.y+Mathf.Max(radius*1.4f,height*.5f));
        }
        private bool TryNormalBodyHud(float radius,out Vector3 center)
        {
            center=Vector3.zero;if(enemyImage==null||enemyImage.sprite==null)return false;
            placedHudRects.Clear();
            foreach(var view in normalGauges){
                if(view==null||!view.GaugeVisible||view.enemyImage==null||view.enemyImage.sprite==null)continue;
                var body=view.GaugeBodyBounds();
                // 既存の状態演出の最大1.35倍の外周を常時空け、付与/解除でHUDを跳ねさせない。
                var candidate=new Vector3(body.center.x,body.max.y+body.size.y*.175f+radius*1.5f,view.hpText!=null?view.hpText.transform.position.z:view.transform.position.z);
                var box=view.NormalHudRect(candidate,radius);
                for(int n=0;n<normalGauges.Count;n++){
                    float lift=0;foreach(var placed in placedHudRects)if(box.Overlaps(placed))lift=Mathf.Max(lift,placed.yMax-box.yMin+radius*.2f);
                    if(lift<=0)break;candidate.y+=lift;box=view.NormalHudRect(candidate,radius);
                }
                if(view==this){center=candidate;return true;}placedHudRects.Add(box);
            }
            var fallback=GaugeBodyBounds();center=new Vector3(fallback.center.x,fallback.max.y+fallback.size.y*.175f+radius*1.5f,hpText!=null?hpText.transform.position.z:transform.position.z);return true;
        }
        private bool SharesVisibleBody(EnemyView other)
        {
            return other!=null&&other.GaugeVisible&&GaugeVisible&&enemyImage!=null&&other.enemyImage!=null&&enemyImage.sprite!=null&&
                enemyImage.enabled&&other.enemyImage.enabled&&!enemyImage.forceRenderingOff&&!other.enemyImage.forceRenderingOff&&enemyImage.gameObject.activeInHierarchy&&other.enemyImage.gameObject.activeInHierarchy&&
                enemyImage.sprite==other.enemyImage.sprite&&enemyImage.flipX==other.enemyImage.flipX&&enemyImage.flipY==other.enemyImage.flipY&&
                Vector3.SqrMagnitude(GaugeBodyBounds().center-other.GaugeBodyBounds().center)<.0001f&&
                Vector3.SqrMagnitude(GaugeBodyBounds().size-other.GaugeBodyBounds().size)<.0001f;
        }
        private int CoincidentCount(out bool first)
        {
            int count=0;first=true;
            foreach(var view in normalGauges)if(SharesVisibleBody(view)){
                if(count==0)first=view==this;count++;
            }
            return count;
        }
        private void RefreshNormalNames()
        {
            foreach(var view in normalGauges){
                if(view==null||view.enemyNameText==null||view.enemy==null)continue;
                int count=view.CoincidentCount(out bool first);bool sameName=true;
                if(count>1)foreach(var other in normalGauges)if(view.SharesVisibleBody(other)&&other.displayedEnemyName!=view.displayedEnemyName){sameName=false;break;}
                view.enemyNameText.text=view.displayedEnemyName+(count>1&&first&&sameName?" ×"+count:"");
            }
        }
        private void RefreshBodyLink(Vector3 center,float radius)
        {
            bool grouped=CoincidentCount(out _)>1;
            if(!grouped){if(bodyLink!=null)bodyLink.enabled=false;return;}
            if(bodyLink==null){
                var obj=new GameObject("EnemyBodyLink");obj.transform.SetParent(transform,false);bodyLink=obj.AddComponent<LineRenderer>();bodyLink.sharedMaterial=gaugeMaterial;
                bodyLink.useWorldSpace=true;bodyLink.numCapVertices=2;bodyLink.numCornerVertices=2;bodyLink.startColor=bodyLink.endColor=new Color(.25f,.62f,.67f,1f);bodyLink.sortingLayerID=enemyImage.sortingLayerID;bodyLink.sortingOrder=2;
            }
            var body=GaugeBodyBounds();float x=body.center.x+radius*1.65f,y=body.max.y+radius*.1f;
            // 共通の幹で「一つの見た目に複数のHP」を示す。識別IDや本体のずらしは加えない。
            bodyLink.enabled=true;bodyLink.startWidth=bodyLink.endWidth=radius*.045f;bodyLink.positionCount=4;
            bodyLink.SetPosition(0,center+Vector3.right*radius*1.4f);bodyLink.SetPosition(1,new Vector3(x,center.y,center.z));
            bodyLink.SetPosition(2,new Vector3(x,y,center.z));bodyLink.SetPosition(3,new Vector3(body.center.x,y,center.z));
        }
        private void RefreshGauges()
        {
            bool visible=GaugeVisible;if(bodyLink!=null)bodyLink.enabled=false;if(hpBack==null&&!visible){HideStatusIcons();return;}EnsureGauges();
            hpBack.enabled=visible;hpFill.enabled=visible&&HpFraction>0;
            poiseBack.enabled=visible&&PoiseFraction.HasValue;poiseFill.enabled=poiseBack.enabled&&PoiseFraction.Value>0;
            if(!visible){HideStatusIcons();return;}
            var camera=Camera.main;
            if(IsBossGauge&&camera!=null){
                float offset=BossHudSpacing*Mathf.Max(0,bossGauges.IndexOf(this));
                float depth=Mathf.Max(camera.nearClipPlane+1,camera.WorldToViewportPoint(transform.position).z);
                Vector3 Point(float x,float y)=>camera.ViewportToWorldPoint(new Vector3(x-offset,y,depth));
                float height=Vector3.Distance(Point(.965f,.3f),Point(.965f,.78f));
                Bar(hpBack,Point(.965f,.3f),Point(.965f,.78f),height*.055f,1);
                Bar(hpFill,Point(.965f,.3f),Point(.965f,.78f),height*.043f,HpFraction);
                Bar(poiseBack,Point(.94f,.3f),Point(.94f,.78f),height*.018f,1);
                Bar(poiseFill,Point(.94f,.3f),Point(.94f,.78f),height*.012f,PoiseFraction??0);
                if(hpText!=null)hpText.transform.position=Point(.965f,.265f);
                FitHpText(height*.14f);
            }else{
                RefreshNormalNames();
                if(hpText!=null)hpText.transform.localPosition=hpLocalPosition;
                if(enemyNameText!=null)enemyNameText.transform.localPosition=nameLocalPosition;
                float radius=camera!=null&&camera.orthographic?camera.orthographicSize*.055f:.55f;
                int row=0;
                // 同じ位置にいる敵も、HP/強靭度の値を個体ごとに読めるよう分ける。
                foreach(var other in normalGauges){
                    if(other==this)break;
                    if(other!=null&&other.GaugeVisible&&Vector3.Distance(other.transform.position,transform.position)<radius*2.4f)row++;
                }
                var center=(hpText!=null?hpText.transform.position:transform.position+Vector3.up)+Vector3.down*(row*radius*2.65f);
                bool bodyHud=TryNormalBodyHud(radius,out var bodyCenter);if(bodyHud)center=bodyCenter;
                if(hpText!=null)hpText.transform.position=center;
                FitHpText(radius*2);
                if(bodyHud)RefreshBodyLink(center,radius);
                Arc(hpBack,center,radius,360,radius*.16f);Arc(hpFill,center,radius,360*HpFraction,radius*.11f);
                Arc(poiseBack,center,radius*1.28f,180,radius*.09f);Arc(poiseFill,center,radius*1.28f,180*(PoiseFraction??0),radius*.06f);
                // 本体の近くで名前と円を一組にする。未知の描画fixtureは従来の行分離を保つ。
                if(enemyNameText!=null&&(bodyHud||normalGauges.Exists(other=>other!=this&&other!=null&&other.GaugeVisible&&
                    Vector3.Distance(other.transform.position,transform.position)<radius*2.4f))){
                    var labelRenderer=enemyNameText.GetComponent<Renderer>();
                    if(labelRenderer!=null){var bounds=labelRenderer.bounds;var position=enemyNameText.transform.position;
                        position.x+=center.x-radius*1.6f-bounds.max.x;position.y+=center.y-bounds.center.y;
                        enemyNameText.transform.position=position;}
                }
            }
            RefreshStatusIcons();
        }
        private void FitHpText(float diameter)
        {
            if(hpText==null)return;var renderer=hpText.GetComponent<Renderer>();
            float height=renderer!=null?renderer.bounds.size.y:0;if(height<.0001f)return;
            // 最大HPの桁数に合わせ、残HPの桁が変わっても数字の大きさを変えない。
            float targetHeight=Mathf.Min(diameter*.4f,diameter*.8f/(.7f*enemy.MaxHp.ToString().Length));
            hpText.transform.localScale*=targetHeight/height;
        }
        private void RegisterGauge(){var list=IsBossGauge?bossGauges:normalGauges;if(!list.Contains(this))list.Add(this);}
        private void OnEnable(){if(hpVisible)RegisterGauge();}
        private void OnDisable(){ClearRecoveryHint();if(bodyLink!=null)bodyLink.enabled=false;guardVisual?.Clear();paralysisVisual?.Clear();defensiveField?.Clear();HideStatusIcons();bossGauges.Remove(this);normalGauges.Remove(this);}
        private void LateUpdate(){if(hpVisible)RefreshGauges();RefreshDefensiveField();RefreshConditionVisuals();}
        private void OnDestroy(){archerAssets?.Dispose();archerAssets=null;guardVisual?.Dispose();paralysisVisual?.Dispose();defensiveField?.Dispose();HideStatusIcons();bossGauges.Remove(this);normalGauges.Remove(this);if(gaugeMaterial!=null){if(UnityEngine.Application.isPlaying)Destroy(gaugeMaterial);else DestroyImmediate(gaugeMaterial);}}

        private bool deathPresentationActive;
        public void SetDeathPresentationActive(bool active)=>deathPresentationActive=active;

        /// <summary>WorldEnemyView がダメージフラッシュに使う SpriteRenderer</summary>
        private SpriteRenderer unposedBodyRenderer;
        /// <summary>RootやHP文字を動かさず本体だけ変形するため、必要時に描画部品を子へ分ける。素材は借用する。</summary>
        internal SpriteRenderer EnsureAttackPoseBody()
        {
            if(enemyImage==null)enemyImage=GetComponent<SpriteRenderer>();
            if(enemyImage==null||!gameObject.scene.IsValid())return null;
            if(enemyImage.transform!=transform)return enemyImage.transform.IsChildOf(transform)?enemyImage:null;
            var original=enemyImage;
            var go=new GameObject("EnemyAttackPoseBody");go.layer=gameObject.layer;go.transform.SetParent(transform,false);
            var body=go.AddComponent<SpriteRenderer>();body.sprite=original.sprite;body.sharedMaterials=original.sharedMaterials;body.color=original.color;
            body.flipX=original.flipX;body.flipY=original.flipY;body.sortingLayerID=original.sortingLayerID;body.sortingOrder=original.sortingOrder;
            body.maskInteraction=original.maskInteraction;body.drawMode=original.drawMode;body.size=original.size;body.tileMode=original.tileMode;body.adaptiveModeThreshold=original.adaptiveModeThreshold;body.spriteSortPoint=original.spriteSortPoint;
            body.shadowCastingMode=original.shadowCastingMode;body.receiveShadows=original.receiveShadows;body.lightProbeUsage=original.lightProbeUsage;body.reflectionProbeUsage=original.reflectionProbeUsage;body.renderingLayerMask=original.renderingLayerMask;body.forceRenderingOff=original.forceRenderingOff;body.enabled=original.enabled;
            if(original.HasPropertyBlock()){var block=new MaterialPropertyBlock();original.GetPropertyBlock(block);body.SetPropertyBlock(block);}
            original.enabled=false;unposedBodyRenderer=original;enemyImage=body;return body;
        }
        // 非表示の元Rendererは本体の基準boundsを保つ。予備動作でHP円・数字・名前を揺らさない。
        private Bounds GaugeBodyBounds()
        {
            if(unposedBodyRenderer==null)return Olca.Presentation.Effects.DefensiveFieldVisual.GetBodyBounds(enemyImage);
            unposedBodyRenderer.sprite=enemyImage.sprite;unposedBodyRenderer.flipX=enemyImage.flipX;unposedBodyRenderer.flipY=enemyImage.flipY;
            return Olca.Presentation.Effects.DefensiveFieldVisual.GetBodyBounds(unposedBodyRenderer);
        }
        public SpriteRenderer BodyRenderer => enemyImage;
        public Sprite AuthoredGruntSprite=>gruntSprite;
        public Font AuthoredHudFont=>hpText!=null?hpText.font:null;

        public void Initialize(Enemy enemy)
        {
            ClearRecoveryHint();
            if(flyOriginKnown455&&enemyImage!=null)enemyImage.transform.localPosition=flyOrigin455;flyOriginKnown455=false;
            guardVisual?.Clear(); paralysisVisual?.Clear(); defensiveField?.Clear();
            this.enemy = enemy;archerWindup=false;archerReleaseAt=-1000;displayedEnemyName=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(enemy.Name);
            if(hpText!=null&&hpLocalScale!=Vector3.zero){hpText.transform.localScale=hpLocalScale;hpText.transform.localPosition=hpLocalPosition;}
            if(enemyNameText!=null&&namePositionCached)enemyNameText.transform.localPosition=nameLocalPosition;
            if(enemyNameText!=null)enemyNameText.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(enemy.Name);
            ApplyAppearance();
            if(IsBossGauge&&enemyNameText!=null&&enemyImage!=null&&enemyImage.sprite!=null){
                var body=GaugeBodyBounds();
                var label=enemyNameText.GetComponent<Renderer>();float height=label==null ? .15f : Mathf.Max(.15f,label.bounds.size.y);
                var position=enemyNameText.transform.position;position.x=body.center.x;position.y=body.max.y+body.size.y*.175f+height;enemyNameText.transform.position=position;
            }
            if(enemyNameText!=null){nameLocalPosition=enemyNameText.transform.localPosition;namePositionCached=true;}
            if(hpText!=null){hpLocalPosition=hpText.transform.localPosition;hpLocalScale=hpText.transform.localScale;}


            if (enemyNameText != null)
                enemyNameText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(enemy.Name);

            // 名前とHP文字は飛翔物(6)・状態表示(7)より手前、被弾数値(20)より後ろ。
            // 座標や円・バー・接続線は動かさず、交差中も対象情報を読めるようにする。
            SetTextSortingOrder(hpText, 9);
            SetTextSortingOrder(enemyNameText, 8);

            HideHp(); // ダンジョン探索中は名前・HP非表示
            Refresh();
        }

        /// <summary>既存の敵IDに対応する仮素材を表示する。戦闘データは変更しない。</summary>
        private void ApplyAppearance()
        {
            var sprite = IsTrialFlying455 ? FlyingSprite455(Olca.Core.Logic.Integration.OutsideFlyingPhase454.Hover) : IsTrialArcher ? ArcherSprite(0) : enemy.EnemyId == "boss" ? bossSprite : gruntSprite;
            if (enemyImage == null || sprite == null) return;

            enemyImage.sprite = sprite;
            enemyImage.color = Color.white;
            enemyImage.flipX = true; // 右向きの原画を、左側のプレイヤーへ向ける

            // 元絵の透明余白でなく、既存の可視領域校正へ名前をつなぐ。絵とpivotは変更しない。
            float top = IsTrialArcher?Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(sprite).max.y:sprite.bounds.max.y;
            if (enemyNameText != null)
                enemyNameText.transform.localPosition = new Vector3(0f, top + 1.05f, -0.1f);
            if (hpText != null)
                hpText.transform.localPosition = new Vector3(0f, top + 0.25f, -0.1f);

        }

        private static void SetTextSortingOrder(TextMesh tm, int order)
        {
            if (tm == null) return;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = order;
        }

        /// <summary>HPテキストと死亡表示を更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (enemy == null) return;
            if (hpText != null)
                hpText.text = $"{enemy.CurrentHp}";
            RefreshGauges();
            gameObject.SetActive(!enemy.IsDead||deathPresentationActive);
            RefreshDefensiveField();
            RefreshConditionVisuals();
        }

        /// <summary>バトル開始時に呼ぶ（名前・HP表示）</summary>
        public void ShowHp()
        {
            hpVisible=true;RegisterGauge();
            RefreshGauges();
            if (hpText != null) hpText.gameObject.SetActive(true);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(true);
        }

        /// <summary>バトル終了時に呼ぶ（名前・HP非表示）</summary>
        public void HideHp()
        {
            ClearRecoveryHint();
            hpVisible=false;bossGauges.Remove(this);normalGauges.Remove(this);
            RefreshGauges();
            if (hpText != null) hpText.gameObject.SetActive(false);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(false);
        }
    }
}
