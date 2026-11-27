using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>自作の側面正投影VFX。球状粒子の奥行き・照明を連番へ焼き、既存UIのclipを使う。
    /// 外部素材、乱数、実時間、ゲーム状態を参照しない。Unity生成物は既存の借用cacheが所有する。</summary>
    public static class AuthoredAttackVfx
    {
        private const int Cell=256;
        private static readonly Color32[][][] pixels=new Color32[3][][];
        public static Sprite Resolve(Olca.Unity.GeneratedSpriteAssets.Lease lease,int kind,int frame)
        {
            int count=kind==1?4:6;if(kind<0||kind>2||frame<0||frame>=count||lease==null)return null;
            return lease.Get("AuthoredAttackVfx413/"+kind,b=>Build(b,kind,count))[frame];
        }
        private static Sprite[] Build(Olca.Unity.GeneratedSpriteAssets.Builder builder,int kind,int count)
        {
            if(pixels[kind]==null){var baked=new Color32[count][];for(int n=0;n<count;n++)baked[n]=Render(kind,n);pixels[kind]=baked;}
            var texture=builder.CreateTexture(Cell*count,Cell);texture.name="SelfAuthoredSideVfx413_"+kind;texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;texture.hideFlags=HideFlags.DontSave;
            for(int n=0;n<count;n++)texture.SetPixels32(n*Cell,0,Cell,Cell,pixels[kind][n]);texture.Apply(false,true);
            var frames=new Sprite[count];for(int n=0;n<count;n++){frames[n]=builder.CreateSprite(texture,new Rect(n*Cell,0,Cell,Cell),Vector2.one*.5f,100);frames[n].name=(kind==0?"trial_bomb_blast_":kind==1?"authored_sword_":"authored_hit_")+n;frames[n].hideFlags=HideFlags.DontSave;}return frames;
        }
        private static Color32[] Render(int kind,int frame)
        {
            var data=new Color[Cell*Cell];if(kind==0)Bomb(data,frame);else if(kind==1)Sword(data,frame);else Hit(data,frame);
            var result=new Color32[data.Length];for(int n=0;n<data.Length;n++)result[n]=data[n];return result;
        }
        // Straight alphaで合成し、透過縁へ黒や白の矩形を残さない。
        private static void Over(Color[] data,int x,int y,Color ink)
        {
            if(x<0||x>=Cell||y<0||y>=Cell||ink.a<=0)return;int i=y*Cell+x;Color prior=data[i];float a=ink.a+prior.a*(1-ink.a);var value=(ink*ink.a+prior*(prior.a*(1-ink.a)))/Mathf.Max(a,.00001f);value.a=a;data[i]=value;
        }
        private static void Sphere(Color[] data,float cx,float cy,float rx,float ry,float depth,Color tint,float opacity,bool emissive)
        {
            for(int y=Mathf.Max(84,Mathf.FloorToInt(cy-ry));y<=Mathf.Min(172,Mathf.CeilToInt(cy+ry));y++)for(int x=Mathf.Max(14,Mathf.FloorToInt(cx-rx));x<=Mathf.Min(242,Mathf.CeilToInt(cx+rx));x++){
                float nx=(x+.5f-cx)/rx,ny=(y+.5f-cy)/ry;float warp=1+.055f*Mathf.Sin(nx*13+depth*4)*Mathf.Sin(ny*11+depth*7);float d=(nx*nx+ny*ny)*warp;if(d>=1)continue;float nz=Mathf.Sqrt(1-d);
                float light=emissive ? .75f+.25f*nz:.36f+.64f*Mathf.Clamp01(-.38f*nx+.6f*ny+.7f*nz);var ink=tint*light;ink.a=opacity*Mathf.SmoothStep(0,1,Mathf.Clamp01((1-d)*4));Over(data,x,y,ink);
            }
        }
        private static void Streak(Color[] data,Vector2 start,Vector2 end,float width,Color color)
        {
            var d=end-start;float length=d.sqrMagnitude;for(int y=Mathf.Max(1,Mathf.FloorToInt(Mathf.Min(start.y,end.y)-width));y<=Mathf.Min(254,Mathf.CeilToInt(Mathf.Max(start.y,end.y)+width));y++)for(int x=Mathf.Max(1,Mathf.FloorToInt(Mathf.Min(start.x,end.x)-width));x<=Mathf.Min(254,Mathf.CeilToInt(Mathf.Max(start.x,end.x)+width));x++){
                var p=new Vector2(x+.5f,y+.5f);float t=length<=0?0:Mathf.Clamp01(Vector2.Dot(p-start,d)/length);float radius=width*(1-.85f*t);float alpha=Mathf.Clamp01((radius-Vector2.Distance(p,start+d*t))+.5f);var ink=color;ink.a*=alpha;Over(data,x,y,ink);
            }
        }
        private static void Bomb(Color[] data,int frame)
        {
            float t=new[]{0f,.17f,.35f,.54f,.75f,.94f}[frame],spread=18+84*Mathf.Sqrt(t),rise=7+28*t;
            // 奥から手前へ粒子を重ねる。冷えた陰影の煙の内側だけ、初期は熱い発光を残す。
            var order=new int[17];for(int k=0;k<order.Length;k++)order[k]=k;System.Array.Sort(order,(a,b)=>Mathf.Sin(a*2.399963f*.71f).CompareTo(Mathf.Sin(b*2.399963f*.71f)));
            foreach(int n in order){
                float a=n*2.399963f,z=Mathf.Sin(a*.71f),x=128+Mathf.Cos(a)*spread*(.3f+.65f*(n%4)/3f),y=91+rise+Mathf.Sin(a)*13*t+z*4;
                float r=(11+15*t)*(1+.15f*z),heat=Mathf.Clamp01(1-t*1.7f)*(n%3==0 ? .6f:1);Color cold=Color.Lerp(new Color(.23f,.25f,.28f),new Color(.62f,.64f,.66f),(z+1)*.5f);Color hot=Color.Lerp(new Color(1,.19f,.015f),new Color(1,.68f,.12f),(z+1)*.5f);Sphere(data,x,y,r,r*.73f,z,Color.Lerp(cold,hot,heat),frame==5 ? .65f:.95f,heat>.5f);
            }
            if(t<.62f){float r=20+80*Mathf.Sin(t*Mathf.PI);Color fire=Color.Lerp(new Color(1,.85f,.28f),new Color(1,.23f,.015f),t);for(int side=-1;side<=1;side+=2)Sphere(data,128+side*r*.65f,92+r*.18f,r*.55f,r*.36f,side,fire,1,true);Sphere(data,128,93+r*.22f,r*.65f,r*.40f,0,Color.Lerp(new Color(1,1,.98f),new Color(1,.82f,.2f),Mathf.Clamp01((t-.25f)*2)),1,true);}
            if(frame>0&&frame<4){float rx=25+110*t,ry=5+8*t;for(int y=84;y<114;y++)for(int x=14;x<243;x++){float dx=(x-128)/rx,dy=(y-92)/ry;float edge=Mathf.Abs(Mathf.Sqrt(dx*dx+dy*dy)-1);if(edge<.12f)Over(data,x,y,new Color(1,.72f,.22f,(1-edge/.12f)*.6f*(1-t)));}}
            for(int n=0;n<7;n++){float a=.15f+n*Mathf.PI/7f;var p=new Vector2(128+Mathf.Cos(a)*(16+88*t),90+Mathf.Sin(a)*(13+65*t)-24*t*t);Streak(data,p,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*Mathf.Lerp(8,2,t),1.4f*(1-t)+.3f,new Color(1,.8f,.3f,(1-t)*.9f));}
        }
        private static void Sword(Color[] data,int frame)
        {
            float[] starts={52,70,-12,-50},ends={85,-8,-64,-82};float start=starts[frame],end=ends[frame];float fade=frame==3 ? .55f:1;
            // 面積を持つ曲面の斬撃。各コマで先端が上から下へ走り、残光は薄い層へ戻る。
            for(int y=2;y<254;y++)for(int x=2;x<254;x++){
                float nx=(x-30)/211f,ny=(y-128)/119f,r=Mathf.Sqrt(nx*nx+ny*ny),angle=Mathf.Atan2(ny,nx)*Mathf.Rad2Deg;float low=Mathf.Min(start,end),high=Mathf.Max(start,end);if(angle<low||angle>high||r>1.02f||r<.52f)continue;
                float along=(angle-low)/Mathf.Max(1,high-low),tail=frame==0?1:Mathf.Pow(1-along,.4f);float thickness=(.12f+.36f*(1-along))*Mathf.Pow(Mathf.Max(0,Mathf.Sin(along*Mathf.PI)),.35f);float inner=1-thickness;if(r<inner)continue;float edge=Mathf.Clamp01((r-inner)/Mathf.Max(.001f,thickness));Color ink=Color.Lerp(new Color(.08f,.5f,1,.05f),new Color(.34f,.9f,1,.85f),edge);if(edge>.84f)ink=Color.Lerp(ink,new Color(.96f,1,1,1),(edge-.84f)/.16f);ink.a*=fade*tail*Mathf.Clamp01((1.02f-r)*60)*Mathf.Clamp01(along*30)*Mathf.Clamp01((1-along)*30);Over(data,x,y,ink);
            }
        }
        private static void Hit(Color[] data,int frame)
        {
            float t=frame/6f;for(int n=0;n<9;n++){float a=n*Mathf.PI*2/9+.25f;var direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a));float distance=8+76*t;var start=Vector2.one*128+direction*distance;Streak(data,start,start+direction*(45*(1-t)),4*(1-t)+.4f,new Color(1,.74f,.25f,1-t));}
            if(frame<3){float r=34-frame*8;Streak(data,new Vector2(128-r,128),new Vector2(128+r,128),5-frame,Color.white);Streak(data,new Vector2(128,128-r),new Vector2(128,128+r),5-frame,Color.white);}
        }
    }
}

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// ヒットエフェクトを表示するView
    /// 敵が攻撃を受けた際のフラッシュ・振動エフェクト
    /// </summary>
    public class HitEffectView : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Image targetImage;  // エフェクト対象のImage（敵のスプライト等）

        [Header("Flash Effect Settings")]
        [SerializeField] private Color flashColor = Color.white;     // フラッシュ色
        [SerializeField] private float flashDuration = 0.1f;         // フラッシュ時間（秒）
        [SerializeField] private int flashCount = 2;                 // フラッシュ回数

        [Header("Shake Effect Settings")]
        [SerializeField] private float shakeAmount = 5f;             // 振動の大きさ（ピクセル）
        [SerializeField] private float shakeDuration = 0.2f;         // 振動時間（秒）
        [SerializeField] private int shakeIterations = 10;           // 振動の回数

        [Header("Scale Pulse Settings")]
        [SerializeField] private bool useScalePulse = true;          // スケールパルスを使用するか
        [SerializeField] private float scalePulseAmount = 1.2f;      // パルス時の拡大率
        [SerializeField] private float scalePulseDuration = 0.15f;   // パルス時間（秒）

        private Color originalColor;
        private Vector3 originalPosition;
        private Vector3 originalScale;
        private RectTransform rectTransform;
        private CancellationTokenSource effectCts;

        private void Awake()
        {
            InitializeIfNeeded();
        }

        /// <summary>
        /// 初期化（targetImageが設定された後に呼ぶ）
        /// </summary>
        public void InitializeIfNeeded()
        {
            if (targetImage != null && rectTransform == null)
            {
                rectTransform = targetImage.GetComponent<RectTransform>();
                originalColor = targetImage.color;
                originalPosition = rectTransform.localPosition;
                originalScale = rectTransform.localScale;
            }
        }

        /// <summary>
        /// ヒットエフェクトを再生（フラッシュ + 振動 + スケールパルス）
        /// </summary>
        public async UniTask PlayHitEffectAsync(CancellationToken ct)
        {
            // targetImageまたはrectTransformがnullの場合は何もしない
            if (targetImage == null || rectTransform == null)
            {
                return;
            }

            // 既存のエフェクトをキャンセル
            effectCts?.Cancel();
            effectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 並行実行: フラッシュ、振動、スケールパルス
                await UniTask.WhenAll(
                    FlashEffectAsync(effectCts.Token),
                    ShakeEffectAsync(effectCts.Token),
                    useScalePulse ? ScalePulseEffectAsync(effectCts.Token) : UniTask.CompletedTask
                );
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は元の状態に戻す
                ResetToOriginalState();
            }
        }

        /// <summary>
        /// フラッシュエフェクト（色を点滅）
        /// </summary>
        private async UniTask FlashEffectAsync(CancellationToken ct)
        {
            for (int i = 0; i < flashCount; i++)
            {
                if (ct.IsCancellationRequested) break;

                // フラッシュ色に変更
                targetImage.color = flashColor;
                await Olca.Unity.BattleVisualClock.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: ct);

                // 元の色に戻す
                targetImage.color = originalColor;
                await Olca.Unity.BattleVisualClock.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: ct);
            }

            // 最終的に元の色に戻す
            targetImage.color = originalColor;
        }

        /// <summary>
        /// 振動エフェクト（位置をランダムに振動）
        /// </summary>
        private async UniTask ShakeEffectAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            float interval = shakeDuration / shakeIterations;

            while (elapsed < shakeDuration)
            {
                if (ct.IsCancellationRequested) break;

                // ランダムな方向に振動
                float offsetX = Random.Range(-shakeAmount, shakeAmount);
                float offsetY = Random.Range(-shakeAmount, shakeAmount);
                rectTransform.localPosition = originalPosition + new Vector3(offsetX, offsetY, 0f);

                elapsed += interval;
                await Olca.Unity.BattleVisualClock.Delay(System.TimeSpan.FromSeconds(interval), cancellationToken: ct);
            }

            // 元の位置に戻す
            rectTransform.localPosition = originalPosition;
        }

        /// <summary>
        /// スケールパルスエフェクト（一瞬拡大してから元に戻る）
        /// </summary>
        private async UniTask ScalePulseEffectAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            float halfDuration = scalePulseDuration / 2f;

            // 拡大フェーズ
            while (elapsed < halfDuration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                float t = elapsed / halfDuration;
                rectTransform.localScale = Vector3.Lerp(originalScale, originalScale * scalePulseAmount, t);

                await Olca.Unity.BattleVisualClock.NextFrame(ct);
            }

            elapsed = 0f;

            // 縮小フェーズ
            while (elapsed < halfDuration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                float t = elapsed / halfDuration;
                rectTransform.localScale = Vector3.Lerp(originalScale * scalePulseAmount, originalScale, t);

                await Olca.Unity.BattleVisualClock.NextFrame(ct);
            }

            // 元のスケールに戻す
            rectTransform.localScale = originalScale;
        }

        /// <summary>
        /// 元の状態にリセット
        /// </summary>
        private void ResetToOriginalState()
        {
            if (targetImage != null)
            {
                targetImage.color = originalColor;
            }

            if (rectTransform != null)
            {
                rectTransform.localPosition = originalPosition;
                rectTransform.localScale = originalScale;
            }
        }

        private void OnDestroy()
        {
            effectCts?.Cancel();
            effectCts?.Dispose();
        }

        /// <summary>
        /// Inspector上で対象Imageが変更された際に元の値を更新
        /// </summary>
        private void OnValidate()
        {
            if (targetImage != null)
            {
                originalColor = targetImage.color;

                if (rectTransform == null)
                {
                    rectTransform = targetImage.GetComponent<RectTransform>();
                }

                if (rectTransform != null)
                {
                    originalPosition = rectTransform.localPosition;
                    originalScale = rectTransform.localScale;
                }
            }
        }
    }
}

namespace Olca.Presentation.Effects
{
    /// <summary>独立した斬撃の試用連番。判定やキャラ素材を変更せず、論理phaseから参照する。</summary>
    public sealed class TrialSlashSprites : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private Sprite[] frames;private bool attempted,disposed;
        public TrialSlashSprites(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(int activeAge,int activeFrames,int recoveryAge,int recoveryFrames)
        {
            if(activeFrames<=0||recoveryFrames<0)return -1;
            if(activeAge>=0&&activeAge<activeFrames)return activeAge<Mathf.Max(1,activeFrames/2)?0:1;
            int tail=Mathf.Min(9,recoveryFrames);
            return recoveryAge<0||recoveryAge>=tail?-1:recoveryAge<Mathf.Max(1,tail/2)?2:3;
        }
        public Sprite Resolve(int row,int frame)
        {
            if(disposed||row<0||row>3||frame<0||frame>3)return null;
            if(!attempted){attempted=true;var all=load("OlcaCharacterMotion/trial_slash_effect_sheet");
                if(all!=null&&all.Length==16){var candidate=new Sprite[16];for(int r=0;r<4;r++)for(int f=0;f<4;f++){
                    var found=System.Array.FindAll(all,s=>s!=null&&s.name=="trial_slash_"+r+"_"+f);
                    if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f)return null;
                    candidate[r*4+f]=found[0];
                }frames=candidate;}
            }
            return frames==null?null:frames[row*4+frame];
        }
        public void Dispose(){disposed=true;frames=null;}
    }
    /// <summary>借用スプライトを一枚だけ重ねる。初回以外にGameObjectやSpriteを生成しない。</summary>
    public sealed class SlashSpriteWorld : System.IDisposable
    {
        private readonly Transform parent;private GameObject root;private SpriteRenderer renderer;private bool disposed;
        public bool IsVisible=>renderer!=null&&renderer.enabled;
        public SlashSpriteWorld(Transform parent){this.parent=parent;}
        public void Warmup(){if(disposed||parent==null||root!=null)return;root=new GameObject("TrialSlashAnimation",typeof(SpriteRenderer));root.transform.SetParent(parent,false);renderer=root.GetComponent<SpriteRenderer>();renderer.enabled=false;}
        public void Show(Rect bounds,Sprite sprite,float alpha,SpriteRenderer reference,bool mirror=false)
        {
            if(disposed||parent==null||sprite==null||reference==null||reference.forceRenderingOff||bounds.width<=0||bounds.height<=0||alpha<=0){Clear();return;}Warmup();
            renderer.sprite=sprite;renderer.color=new Color(1,1,1,Mathf.Clamp01(alpha));renderer.flipX=mirror;renderer.sortingLayerID=reference.sortingLayerID;renderer.sortingOrder=reference.sortingOrder+5;
            root.transform.SetPositionAndRotation(new Vector3(bounds.center.x,bounds.center.y,reference.transform.position.z-.05f),Quaternion.identity);var scale=parent.lossyScale;
            root.transform.localScale=new Vector3(bounds.width/sprite.bounds.size.x/(Mathf.Abs(scale.x)<.0001f?.0001f:scale.x),bounds.height/sprite.bounds.size.y/(Mathf.Abs(scale.y)<.0001f?.0001f:scale.y),1);renderer.enabled=true;
        }
        public void Clear(){if(renderer!=null){renderer.enabled=false;renderer.sprite=null;}}
        public void Dispose(){if(disposed)return;disposed=true;Clear();if(root!=null){if(UnityEngine.Application.isPlaying)Object.Destroy(root);else Object.DestroyImmediate(root);}root=null;renderer=null;}
    }
    /// <summary>独自の四角い粒で描く仮演出。乱数・ゲーム時計・命中処理を所有しない。</summary>
    public static class ProvisionalPixelPattern
    {
        public const int ArcCount=24,BurstCount=13;
        public static Color Tint(bool fire)=>fire?new Color(1f,.48f,.12f):new Color(.25f,.94f,1f);
        private static Rect Tile(float x,float y,float width,float height)
        {
            x=Mathf.Round(x*48)/48;y=Mathf.Round(y*48)/48;
            return Rect.MinMaxRect(Mathf.Max(0,x-width*.5f),Mathf.Max(0,y-height*.5f),Mathf.Min(1,x+width*.5f),Mathf.Min(1,y+height*.5f));
        }
        public static bool Arc(int index,int variant,float sweep,float strength,Color tint,out Rect tile,out Color color)
        {
            tile=default;color=Color.clear;if(variant<0||variant>6)throw new System.ArgumentOutOfRangeException(nameof(variant));
            if(index<0||index>=ArcCount||strength<=0)return false;
            float head=Mathf.Clamp01(sweep)*(ArcCount-1);if(index>head)return false;
            float t=index/(float)(ArcCount-1),a=Mathf.Lerp(-70,70,t)*Mathf.Deg2Rad;
            var point=new Vector2(.15f+.75f*Mathf.Cos(a),.5f+.42f*Mathf.Sin(a));
            float rotation=variant==1?-45:variant==2?90:variant==4?-90:variant==5?90:0;
            if(variant==6)point=new Vector2(.12f+.8f*t,.5f+.12f*Mathf.Sin(t*Mathf.PI));
            else if(rotation!=0){var d=point-Vector2.one*.5f;float c=Mathf.Cos(rotation*Mathf.Deg2Rad),s=Mathf.Sin(rotation*Mathf.Deg2Rad);point=Vector2.one*.5f+new Vector2(d.x*c-d.y*s,d.x*s+d.y*c);}
            point=new Vector2(Mathf.Clamp(point.x,.04f,.96f),Mathf.Clamp(point.y,.05f,.95f));
            tile=Tile(point.x,point.y,.065f,.10f);float tail=Mathf.Clamp01(1-(head-index)/18f);
            color=Color.Lerp(tint,Color.white,index>=head-2?.8f:.18f);color.a=Mathf.Clamp01(strength)*(.2f+.8f*tail);return color.a>0;
        }
        public static bool Burst(int index,float progress,Color tint,out Rect tile,out Color color)
        {
            tile=default;color=Color.clear;float t=Mathf.Clamp01(progress);if(index<0||index>=BurstCount||t>=1)return false;
            if(index==12){if(t>=.25f)return false;tile=Tile(.5f,.5f,.55f*(1-t*4),.55f*(1-t*4));color=Color.white;color.a=1-t*4;return true;}
            float angle=index*Mathf.PI/6f,radius=.18f+.29f*t;float size=.095f*(1-t)+.015f;
            tile=Tile(.5f+Mathf.Cos(angle)*radius,.5f+Mathf.Sin(angle)*radius+.06f*Mathf.Sin(t*Mathf.PI),size,size);
            color=index%3==0?Color.white:tint;color.a=1-t;return true;
        }
    }
    /// <summary>表示専用の論理時計。最大到達で粒子の年齢をリセットせず、飽和する蓄積値から保持時間を分離する。</summary>
    public sealed class ChargeParticleClock
    {
        private long start,last=-1,fullAt=-1;private int prior,required;
        public bool Active{get;private set;}public long Age{get;private set;}public long FullAge{get;private set;}=-1;
        public float Fraction{get;private set;}public float Gold=>FullAge<0?0:Mathf.Clamp01(FullAge/4f);
        public void Step(long frame,int frames,int limit)
        {
            if(frame<0||frames<1||limit<1){Reset();return;}long inferred=frame-frames+1;
            if(!Active||frame<last||frames<prior||limit!=required||frames<limit&&inferred!=start){start=inferred;fullAt=-1;}
            Active=true;last=frame;prior=frames;required=limit;Age=System.Math.Max(0,frame-start);Fraction=Mathf.Clamp01(frames/(float)limit);
            if(frames>=limit&&fullAt<0)fullAt=start+limit-1;FullAge=fullAt<0?-1:System.Math.Max(0,frame-fullAt);
        }
        public void Reset(){Active=false;last=-1;fullAt=-1;prior=required=0;Age=0;FullAge=-1;Fraction=0;}
    }
    /// <summary>自作の小粒と一度だけ広がる輪。位相は論理年齢だけで進み、青→白金の補間で再生成しない。</summary>
    public static class ChargeParticlePattern
    {
        public const int Count=10,RingFrames=12;
        public readonly struct Dot
        {
            public readonly Vector2 Position,Size;public readonly Color Color;
            public Dot(Vector2 position,Vector2 size,Color color){Position=position;Size=size;Color=color;}
        }
        public static Dot Sample(int index,long age,float fraction,float gold)
        {
            float phase=((age%100)*.01f+index*.61803399f)%1f;float angle=index*2.399963f+(age%180)*(2*Mathf.PI/180);float radius=.12f+.30f*(1-phase);
            float fade=Mathf.Sin(phase*Mathf.PI);fade*=fade;float size=.013f+.004f*(index%3);
            var blue=Color.Lerp(new Color(.16f,.55f,1),new Color(.4f,.95f,1),(index%3)/2f);var warm=Color.Lerp(new Color(1,.76f,.28f),new Color(1,.98f,.86f),(index%3)/2f);var color=Color.Lerp(blue,warm,gold);color.a=fade*(.5f+.22f*fraction);
            return new Dot(new Vector2(.5f+Mathf.Cos(angle)*radius,.5f+Mathf.Sin(angle)*radius*.68f),new Vector2(size*(1-.15f*gold),size*(1+.9f*gold)),color);
        }
        private static Vector3 At(Rect area,Vector2 value)=>new Vector3(area.x+area.width*value.x,area.y+area.height*value.y);
        public static void Populate(UnityEngine.UI.VertexHelper vh,Rect area,ChargeParticleClock clock)
        {
            vh.Clear();if(!clock.Active)return;
            void Disc(Dot dot,bool border){
                int first=vh.currentVertCount;var core=border?new Color(.025f,.075f,.10f,dot.Color.a*.85f):Color.Lerp(dot.Color,Color.white,.32f);if(!border)core.a=dot.Color.a;
                vh.AddVert(At(area,dot.Position),core,Vector2.zero);var edge=border?core:dot.Color;edge.a=0;var size=dot.Size+(border?Vector2.one*.007f:Vector2.zero);
                for(int n=0;n<=12;n++){float a=n*Mathf.PI/6;vh.AddVert(At(area,dot.Position+new Vector2(Mathf.Cos(a)*size.x,Mathf.Sin(a)*size.y)),edge,Vector2.zero);if(n>0)vh.AddTriangle(first,first+n,first+n+1);}
            }
            // 元の粒の位相・色・寸法はそのまま。周囲のごく小さい暗い縁だけを先に描く。
            for(int i=0;i<Count;i++)Disc(Sample(i,clock.Age,clock.Fraction,clock.Gold),true);
            for(int i=0;i<Count;i++)Disc(Sample(i,clock.Age,clock.Fraction,clock.Gold),false);
            // 元の粒は消さず、最大保持だけを四片のまとまった輪郭で示す。色と同じ4fで滑らかに現す。
            if(clock.FullAge>=0&&clock.Gold>0)for(int pass=0;pass<2;pass++)for(int corner=0;corner<4;corner++)for(int segment=0;segment<3;segment++){
                float width=pass==0?.014f:.006f;var ink=pass==0?new Color(.025f,.075f,.10f,.85f*clock.Gold):new Color(1,.96f,.78f,.65f*clock.Gold);
                int first=vh.currentVertCount;for(int side=0;side<2;side++)for(int end=0;end<2;end++){
                    float angle=Mathf.PI*.25f+corner*Mathf.PI*.5f-.21f+(segment+end)*(.42f/3),markerRadius=.38f+(side==0?-width:width);
                    vh.AddVert(At(area,Vector2.one*.5f+new Vector2(Mathf.Cos(angle)*markerRadius,Mathf.Sin(angle)*markerRadius*.8f)),ink,Vector2.zero);
                }vh.AddTriangle(first,first+1,first+3);vh.AddTriangle(first,first+3,first+2);
            }
            if(clock.FullAge<0||clock.FullAge>=RingFrames)return;float t=clock.FullAge/(float)RingFrames;float radius=.18f+.27f*t;
            // 一度だけの細い輪にも外縁を付け、黄色の敵予告のような面や点滅にはしない。
            for(int pass=0;pass<2;pass++)for(int n=0;n<48;n++){
                float width=pass==0?.008f:.004f;var ink=pass==0?new Color(.025f,.075f,.10f,.45f*(1-t)):new Color(1,.91f,.59f,.55f*(1-t));
                int first=vh.currentVertCount;for(int side=0;side<2;side++)for(int end=0;end<2;end++){float a=(n+end)*Mathf.PI/24,r=radius+(side==0?-width:width);vh.AddVert(At(area,Vector2.one*.5f+new Vector2(Mathf.Cos(a)*r,Mathf.Sin(a)*r*.68f)),ink,Vector2.zero);}vh.AddTriangle(first,first+1,first+3);vh.AddTriangle(first,first+3,first+2);
            }
        }
    }
    /// <summary>UI用の無購読ピクセル演出。描画だけを更新し、Raycastや進行を発生させない。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ProvisionalCombatGraphic : UnityEngine.UI.MaskableGraphic
    {
        private bool chargeParticles;private readonly ChargeParticleClock chargeClock=new ChargeParticleClock();
        public bool IsChargeParticleMode=>chargeParticles;public long ChargeParticleAge=>chargeClock.Age;public long ChargeParticleFullAge=>chargeClock.FullAge;public float ChargeParticleGold=>chargeClock.Gold;
        public void ShowChargeParticles(long frame,int frames,int required){chargeParticles=true;chargeClock.Step(frame,frames,required);enabled=chargeClock.Active;SetVerticesDirty();}
        public void ClearChargeParticles(){chargeParticles=false;chargeClock.Reset();enabled=false;SetVerticesDirty();}
        private bool burst,mirror;private int variant;private float progress,strength;private Color tint;
        public void Arc(int kind,float sweep,float alpha,Color value,bool flip){chargeParticles=false;chargeClock.Reset();burst=false;variant=kind;progress=sweep;strength=alpha;tint=value;mirror=flip;SetVerticesDirty();}
        public void Burst(float age,Color value){chargeParticles=false;chargeClock.Reset();burst=true;progress=age;tint=value;mirror=false;SetVerticesDirty();}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            if(chargeParticles){ChargeParticlePattern.Populate(vh,rectTransform.rect,chargeClock);return;}
            vh.Clear();var area=rectTransform.rect;int count=burst?ProvisionalPixelPattern.BurstCount:ProvisionalPixelPattern.ArcCount;
            for(int i=0;i<count;i++){
                Rect tile;Color ink;bool visible=burst?ProvisionalPixelPattern.Burst(i,progress,tint,out tile,out ink):ProvisionalPixelPattern.Arc(i,variant,progress,strength,tint,out tile,out ink);if(!visible)continue;
                if(mirror)tile.x=1-tile.xMax;float x=area.x+tile.x*area.width,y=area.y+tile.y*area.height,w=tile.width*area.width,h=tile.height*area.height;int start=vh.currentVertCount;
                vh.AddVert(new Vector3(x,y),ink,Vector2.zero);vh.AddVert(new Vector3(x,y+h),ink,Vector2.zero);vh.AddVert(new Vector3(x+w,y+h),ink,Vector2.zero);vh.AddVert(new Vector3(x+w,y),ink,Vector2.zero);vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
    }
    /// <summary>World用の固定上限pool。白Textureは借用し、生成Spriteと子だけを所有する。</summary>
    public sealed class PixelCombatWorld : System.IDisposable
    {
        private readonly Transform parent;private readonly string name;private readonly int limit;private GameObject root;private Sprite sprite;private SpriteRenderer[] quads;private bool disposed;
        public int ActiveCount{get;private set;} public int Capacity=>quads?.Length??0;
        public PixelCombatWorld(Transform parent,string name,int capacity=ProvisionalPixelPattern.ArcCount){if(capacity<1||capacity>ProvisionalPixelPattern.ArcCount)throw new System.ArgumentOutOfRangeException(nameof(capacity));this.parent=parent;this.name=name;limit=capacity;}
        public void Warmup(){if(!disposed&&parent!=null)Ensure();}
        private void Ensure()
        {
            if(root!=null)return;root=new GameObject(name);root.transform.SetParent(parent,false);sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f,1);sprite.name=name+"OwnedWhitePixel";sprite.hideFlags=HideFlags.DontSave;
            quads=new SpriteRenderer[limit];for(int i=0;i<quads.Length;i++){var go=new GameObject("Pixel_"+i,typeof(SpriteRenderer));go.transform.SetParent(root.transform,false);quads[i]=go.GetComponent<SpriteRenderer>();quads[i].sprite=sprite;quads[i].enabled=false;}
        }
        public void Arc(Rect bounds,int variant,float sweep,float strength,Color tint,SpriteRenderer reference,bool mirror=false)=>Paint(bounds,false,variant,sweep,strength,tint,reference,mirror);
        public void Burst(Rect bounds,float progress,Color tint,SpriteRenderer reference)=>Paint(bounds,true,0,progress,1,tint,reference,false);
        private void Paint(Rect bounds,bool burst,int variant,float progress,float strength,Color tint,SpriteRenderer reference,bool mirror)
        {
            if(disposed||parent==null||reference==null||bounds.width<=0||bounds.height<=0){Clear();return;}Ensure();ActiveCount=0;
            for(int i=0;i<quads.Length;i++){
                Rect tile;Color ink;bool show=burst?ProvisionalPixelPattern.Burst(i,progress,tint,out tile,out ink):ProvisionalPixelPattern.Arc(i,variant,progress,strength,tint,out tile,out ink);var q=quads[i];q.enabled=show;if(!show)continue;if(mirror)tile.x=1-tile.xMax;
                q.sortingLayerID=reference.sortingLayerID;q.sortingOrder=reference.sortingOrder+5;q.color=ink;q.forceRenderingOff=reference.forceRenderingOff;
                q.transform.SetPositionAndRotation(new Vector3(bounds.x+(tile.x+tile.width*.5f)*bounds.width,bounds.y+(tile.y+tile.height*.5f)*bounds.height,reference.transform.position.z-.05f),Quaternion.identity);var scale=q.transform.parent.lossyScale;
                q.transform.localScale=new Vector3(bounds.width*tile.width/Mathf.Max(.0001f,Mathf.Abs(scale.x)),bounds.height*tile.height/Mathf.Max(.0001f,Mathf.Abs(scale.y)),1);ActiveCount++;
            }
        }
        public void Clear(){ActiveCount=0;if(quads!=null)foreach(var q in quads)if(q!=null)q.enabled=false;}
        public void Dispose(){if(disposed)return;disposed=true;Clear();if(root!=null){if(UnityEngine.Application.isPlaying)Object.Destroy(root);else Object.DestroyImmediate(root);}if(sprite!=null){if(UnityEngine.Application.isPlaying)Object.Destroy(sprite);else Object.DestroyImmediate(sprite);}root=null;sprite=null;quads=null;}
    }
    /// <summary>敵消滅用のWorldSpace UI。探索と同じメッシュを使い、SpriteやMaterialを生成しない。</summary>
    public sealed class PixelBurstWorld : System.IDisposable
    {
        private readonly Transform parent;private GameObject root;private Canvas canvas;private ProvisionalCombatGraphic graphic;private bool disposed;
        public int ActiveCount{get;private set;}public int Capacity=>graphic!=null?ProvisionalPixelPattern.BurstCount:0;
        public PixelBurstWorld(Transform parent){this.parent=parent;}
        public void Warmup()
        {
            if(disposed||parent==null||root!=null)return;
            root=new GameObject("ProvisionalEnemyDeath",typeof(RectTransform),typeof(Canvas));root.transform.SetParent(parent,false);canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;
            var rect=(RectTransform)root.transform;rect.sizeDelta=Vector2.one;
            var mesh=new GameObject("Pixels",typeof(RectTransform),typeof(ProvisionalCombatGraphic));mesh.transform.SetParent(root.transform,false);graphic=mesh.GetComponent<ProvisionalCombatGraphic>();graphic.raycastTarget=false;graphic.rectTransform.sizeDelta=Vector2.one;root.SetActive(false);
        }
        public void Burst(Rect bounds,float progress,Color tint,SpriteRenderer reference)
        {
            if(disposed||parent==null||reference==null||reference.forceRenderingOff||bounds.width<=0||bounds.height<=0||progress>=1){Clear();return;}Warmup();ActiveCount=0;
            for(int i=0;i<ProvisionalPixelPattern.BurstCount;i++)if(ProvisionalPixelPattern.Burst(i,progress,tint,out _,out _))ActiveCount++;
            if(ActiveCount==0){Clear();return;}canvas.sortingLayerID=reference.sortingLayerID;canvas.sortingOrder=reference.sortingOrder+5;
            root.transform.SetPositionAndRotation(new Vector3(bounds.center.x,bounds.center.y,reference.transform.position.z-.05f),Quaternion.identity);var scale=parent.lossyScale;root.transform.localScale=new Vector3(bounds.width/Mathf.Max(.0001f,Mathf.Abs(scale.x)),bounds.height/Mathf.Max(.0001f,Mathf.Abs(scale.y)),1);graphic.Burst(progress,tint);root.SetActive(true);
        }
        public void Clear(){ActiveCount=0;if(root!=null)root.SetActive(false);}
        public void Dispose(){if(disposed)return;disposed=true;Clear();if(root!=null){if(UnityEngine.Application.isPlaying)Object.Destroy(root);else Object.DestroyImmediate(root);}root=null;canvas=null;graphic=null;}
    }

}
