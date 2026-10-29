using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// World Space 用ダメージ数値View。
    /// TextMesh を使用して上昇＋フェードアウトアニメーションを行う。
    /// フォント・サイズ・色はすべて Prefab の Inspector で設定し、スクリプトからは変更しない。
    /// </summary>
    public class WorldDamageNumberView : MonoBehaviour
    {
        [Header("参照（Prefab でアサイン）")]
        [SerializeField] private TextMesh textMesh;

        [Header("アニメーション設定")]
        [SerializeField] private float floatSpeed = 2.0f;  // 上昇速度（World 単位/秒）
        [SerializeField] private float duration = 0.8f;     // アニメーション時間（秒）

        internal System.Action AlignViewport;
        private Vector3 viewportOffset;
        private Color originalColor;
        private CancellationTokenSource animCts;

        private void Awake()
        {
            if (textMesh != null)
                originalColor = textMesh.color;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// ダメージ値と World 座標を受け取り、アニメーションを再生する。
        /// 完了（またはキャンセル）まで await 可能。
        /// </summary>
        public UniTask ShowAsync(int damage,Vector3 worldPosition,CancellationToken ct)
            =>ShowTextAsync(damage.ToString(),worldPosition,ct);

        public async UniTask ShowTextAsync(string text, Vector3 worldPosition, CancellationToken ct)
        {
            if (textMesh == null) return;

            // 初期化
            viewportOffset=Vector3.zero;
            transform.position = worldPosition;
            textMesh.text = text;
            textMesh.color = originalColor;
            gameObject.SetActive(true);

            animCts?.Cancel();
            animCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var startPos = worldPosition;
            float elapsed = 0f;

            try
            {
                while (elapsed < duration)
                {
                    if (animCts.Token.IsCancellationRequested) break;

                    elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);

                    // 上昇（Ease-out: 最初速く、途中で減速して止まる）
                    float eased = 1f - (1f - t) * (1f - t);
                    viewportOffset=Vector3.zero;
                    transform.position = startPos + new Vector3(0f, floatSpeed * duration * eased, 0f);
                    KeepBelowViewportTop();

                    // フェードアウト
                    var c = originalColor;
                    c.a = 1f - t;
                    textMesh.color = c;

                    await Olca.Unity.BattleVisualClock.NextFrame(animCts.Token);
                }
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                // 破棄によるキャンセルではUnityオブジェクトにアクセスしない。
                if (this != null)
                {
                    gameObject.SetActive(false);
                    if (textMesh != null) textMesh.color = originalColor;
                }
            }
        }

        // 停止中の解像度変更でも境界だけ追従し、アニメーション時間は進めない。
        private void LateUpdate(){if(gameObject.activeInHierarchy)KeepBelowViewportTop();}

        // ボス等の高い発生位置でも、上昇中の文字全体を画面上端の内側に保つ。
        // 時計・フェード・元の軌道は変えず、描画位置だけ補正する。
        private void KeepBelowViewportTop()
        {
            if(AlignViewport!=null)AlignViewport();else KeepGroupBelowViewportTop(new[]{this});
        }

        // 群の所有はPoolに任せ、ここでは元軌道のboundsから共通の描画補正量だけ求める。
        internal static void KeepGroupBelowViewportTop(System.Collections.Generic.IEnumerable<WorldDamageNumberView> group,System.Collections.Generic.IEnumerable<Renderer> labels=null)
        {
            var views=new System.Collections.Generic.List<WorldDamageNumberView>();
            foreach(var view in group)if(view!=null&&view.gameObject.activeInHierarchy)views.Add(view);
            var camera=Camera.main;float offset=0f;
            if(camera!=null&&camera.isActiveAndEnabled){
                float limit=1f-8f/Mathf.Max(1,camera.pixelHeight);
                foreach(var view in views){
                    var renderer=view.textMesh!=null?view.textMesh.GetComponent<Renderer>():null;if(renderer==null)continue;
                    var bounds=renderer.bounds;bounds.center-=view.viewportOffset;if(bounds.size.sqrMagnitude<=0)continue;
                    for(int i=0;i<8;i++){
                        var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        var projected=camera.WorldToViewportPoint(corner);if(projected.z<=0||projected.y<=limit)continue;
                        projected.y=limit;
                        offset=Mathf.Min(offset,Vector3.Dot(camera.ViewportToWorldPoint(projected)-corner,camera.transform.up));
                    }
                }
            }
            var adjustment=camera!=null?camera.transform.up*offset:Vector3.zero;
            // 上端から下げた群が対象名を隠す場合だけ、画面内に収まる横側へ逃がす。
            if(offset<0&&labels!=null&&views.Count>0){
                var blockers=new System.Collections.Generic.List<Rect>();
                foreach(var label in labels)if(label!=null&&label.enabled&&label.gameObject.activeInHierarchy)blockers.Add(ProjectBounds(camera,label.bounds));
                Bounds total=default;bool hasBounds=false;
                foreach(var view in views){var renderer=view.textMesh!=null?view.textMesh.GetComponent<Renderer>():null;if(renderer==null)continue;var bounds=renderer.bounds;bounds.center+=adjustment-view.viewportOffset;if(!hasBounds){total=bounds;hasBounds=true;}else total.Encapsulate(bounds);}
                if(hasBounds){var rect=ProjectBounds(camera,total);float left=0,right=0,padding=8f/Mathf.Max(1,camera.pixelWidth);bool overlaps=false;
                    foreach(var blocker in blockers)if(rect.Overlaps(blocker)){overlaps=true;left=Mathf.Min(left,blocker.xMin-rect.xMax-padding);right=Mathf.Max(right,blocker.xMax-rect.xMin+padding);}
                    if(overlaps){
                        var center=camera.WorldToViewportPoint(total.center);
                        Vector3 Shift(float x){var moved=center;moved.x+=x;return camera.ViewportToWorldPoint(moved)-camera.ViewportToWorldPoint(center);}
                        bool Fits(Vector3 shift){var moved=total;moved.center+=shift;var area=ProjectBounds(camera,moved);if(area.xMin<padding||area.xMax>1-padding)return false;foreach(var blocker in blockers)if(area.Overlaps(blocker))return false;return true;}
                        var first=Shift(Mathf.Abs(left)<=Mathf.Abs(right)?left:right);var second=Shift(Mathf.Abs(left)<=Mathf.Abs(right)?right:left);
                        if(Fits(first))adjustment+=first;else if(Fits(second))adjustment+=second;
                    }
                }
            }
            foreach(var view in views){view.transform.position+=adjustment-view.viewportOffset;view.viewportOffset=adjustment;}
        }

        private static Rect ProjectBounds(Camera camera,Bounds bounds)
        {
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<8;i++){var point=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));min=Vector2.Min(min,new Vector2(point.x,point.y));max=Vector2.Max(max,new Vector2(point.x,point.y));}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }

        private void OnDestroy()
        {
            AlignViewport=null;
            animCts?.Cancel();
            animCts?.Dispose();
        }
    }
}
