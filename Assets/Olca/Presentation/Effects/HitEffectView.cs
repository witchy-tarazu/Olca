using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

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
    /// <summary>UI用の無購読ピクセル演出。描画だけを更新し、Raycastや進行を発生させない。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ProvisionalCombatGraphic : UnityEngine.UI.MaskableGraphic
    {
        private bool burst,mirror;private int variant;private float progress,strength;private Color tint;
        public void Arc(int kind,float sweep,float alpha,Color value,bool flip){burst=false;variant=kind;progress=sweep;strength=alpha;tint=value;mirror=flip;SetVerticesDirty();}
        public void Burst(float age,Color value){burst=true;progress=age;tint=value;mirror=false;SetVerticesDirty();}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
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
