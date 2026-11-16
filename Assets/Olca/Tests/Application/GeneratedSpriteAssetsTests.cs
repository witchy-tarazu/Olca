using System;
using NUnit.Framework;
using UnityEngine;
using Olca.Unity;

namespace Olca.Tests.Application
{
    public class GeneratedSpriteAssetsTests
    {
        [Test] public void 爆風着弾色は輪郭と元素材を保ち同cacheで共有し最後に回収する()
        {
            var source=Resources.Load<TextAsset>("OlcaAttackProps/shared_blast");var bytes=(byte[])source.bytes.Clone();
            Sprite original=null,impact=null;Texture2D originalTexture=null,impactTexture=null;
            Color32[] Read(Texture texture){var prior=RenderTexture.active;var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);try{Graphics.Blit(texture,target);RenderTexture.active=target;copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();return copy.GetPixels32();}finally{RenderTexture.active=prior;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(copy);}}
            using(var scope=GeneratedSpriteAssets.Acquire()){
                Assert.IsTrue(Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Blast,out original,scope));
                Assert.IsTrue(Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,out impact,scope));originalTexture=original.texture;impactTexture=impact.texture;
                Assert.AreNotSame(original,impact);Assert.AreNotSame(originalTexture,impactTexture);Assert.AreEqual(original.rect,impact.rect);Assert.AreEqual(original.pivot,impact.pivot);Assert.AreEqual(original.pixelsPerUnit,impact.pixelsPerUnit);Assert.AreEqual(original.bounds,impact.bounds);Assert.AreEqual(64,impactTexture.width);Assert.AreEqual(32,impactTexture.height);Assert.AreEqual(FilterMode.Point,impactTexture.filterMode);Assert.AreEqual(TextureWrapMode.Clamp,impactTexture.wrapMode);Assert.IsFalse(impactTexture.isReadable);
                var before=Read(originalTexture);var after=Read(impactTexture);int brighter=0,transparent=0;
                for(int i=0;i<before.Length;i++){Assert.AreEqual(before[i].a,after[i].a);if(before[i].a==0){transparent++;continue;}Assert.GreaterOrEqual(after[i].r,before[i].r);Assert.GreaterOrEqual(after[i].g,before[i].g);Assert.GreaterOrEqual(after[i].b,before[i].b);if(after[i].r>before[i].r||after[i].g>before[i].g||after[i].b>before[i].b)brighter++;}
                Assert.Greater(brighter,0);Assert.Greater(transparent,0);for(int n=0;n<20;n++){Assert.IsTrue(Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,out var again,scope));Assert.AreSame(impact,again);Assert.AreSame(impactTexture,again.texture);}
                Assert.IsTrue(Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Blast,out var unchanged,scope));Assert.AreSame(original,unchanged);CollectionAssert.AreEqual(before,Read(originalTexture));CollectionAssert.AreEqual(bytes,source.bytes);
            }
            Assert.IsTrue(original==null&&impact==null&&originalTexture==null&&impactTexture==null);
        }
        private static void QueueUntilExit(System.Threading.CancellationToken lifetime)
        {typeof(GeneratedSpriteAssets).GetMethod("QueuePruneUntilExit",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{lifetime});}
        private static bool PruneQueued=>(bool)typeof(GeneratedSpriteAssets).GetField("pruneQueued",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
        [Test] public void 終了済みtokenではRootなしの生存借用があっても再帰予約しない()
        {
            var owner=new GameObject("CancelledPruneOwner");var scope=GeneratedSpriteAssets.Acquire(owner);
            try{var sprite=scope.Get("fixture/cancelled-prune",Make);using(var lifetime=new System.Threading.CancellationTokenSource()){
                lifetime.Cancel();QueueUntilExit(lifetime.Token);Assert.IsFalse(PruneQueued);Assert.IsTrue(sprite!=null);Assert.Greater(scope.Generation,0);
            }}finally{scope.Dispose();UnityEngine.Object.DestroyImmediate(owner);GeneratedSpriteAssets.Prune();}
        }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator 待機中の終了取消は予約を解除し次の世代を妨げない()
        {
            var owner=new GameObject("PendingPruneOwner");var scope=GeneratedSpriteAssets.Acquire(owner);
            var first=new System.Threading.CancellationTokenSource();var next=new System.Threading.CancellationTokenSource();
            try{scope.Get("fixture/pending-prune",Make);QueueUntilExit(first.Token);Assert.IsTrue(PruneQueued);first.Cancel();
                for(int n=0;n<5&&PruneQueued;n++)yield return null;Assert.IsFalse(PruneQueued,"取消された待機を再予約しない");
                QueueUntilExit(next.Token);Assert.IsTrue(PruneQueued,"新しい有効tokenでは回収を再開できる");next.Cancel();
                for(int n=0;n<5&&PruneQueued;n++)yield return null;Assert.IsFalse(PruneQueued);
            }finally{first.Cancel();next.Cancel();first.Dispose();next.Dispose();scope.Dispose();UnityEngine.Object.DestroyImmediate(owner);GeneratedSpriteAssets.Prune();}
        }
        [SetUp] public void 外部所有者のない検証境界を確認する()
        {
            var state=GeneratedSpriteAssets.Inspect();
            Assert.AreEqual(0,state.GetType().GetProperty("roots").GetValue(state),"外部Rootが生存中。資産を強制破棄せず検証境界を修正すること");
            Assert.AreEqual(0,state.GetType().GetProperty("borrowers").GetValue(state),"外部借用が生存中。利用側の終了を確認すること");
        }
        private static Sprite Make(GeneratedSpriteAssets.Builder builder)
        {
            var texture=builder.CreateTexture(8,8);texture.name="GeneratedOwnershipFixture";
            texture.SetPixel(0,0,Color.cyan);texture.Apply();
            return builder.CreateSprite(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f));
        }
        [Test] public void 同一世代の利用側は同じ素材を共有し最後の借用まで生存する()
        {
            var root=GeneratedSpriteAssets.AcquireRoot();var first=GeneratedSpriteAssets.Acquire();var second=GeneratedSpriteAssets.Acquire();
            Sprite sprite=null;Texture2D texture=null;
            try{
                sprite=first.Get("fixture/shared",Make);texture=sprite.texture;
                Assert.AreSame(sprite,second.Get("fixture/shared",Make));
                root.Dispose();first.Dispose();Assert.IsTrue(sprite!=null);Assert.IsTrue(texture!=null);
                Assert.AreSame(sprite,second.Get("fixture/shared",Make));second.Dispose();
                Assert.IsTrue(sprite==null);Assert.IsTrue(texture==null);
            }finally{first.Dispose();second.Dispose();root.Dispose();GeneratedSpriteAssets.Prune();}
        }
        [Test] public void 終了したRootの借用を新Rootへ付け替えず世代ごとに解放する()
        {
            var oldRoot=GeneratedSpriteAssets.AcquireRoot();var old=GeneratedSpriteAssets.Acquire();
            var before=old.Get("fixture/restart",Make);var oldGeneration=old.Generation;oldRoot.Dispose();
            var nextRoot=GeneratedSpriteAssets.AcquireRoot();var next=GeneratedSpriteAssets.Acquire();
            try{
                Assert.AreNotEqual(oldGeneration,next.Generation);var after=next.Get("fixture/restart",Make);
                Assert.AreNotSame(before,after);old.Dispose();Assert.IsTrue(before==null);Assert.IsTrue(after!=null);
                nextRoot.Dispose();Assert.IsTrue(after!=null);next.Dispose();Assert.IsTrue(after==null);
            }finally{old.Dispose();oldRoot.Dispose();next.Dispose();nextRoot.Dispose();}
        }
        [Test] public void 複数Rootの片方終了では共有資産を破棄しない()
        {
            var first=GeneratedSpriteAssets.AcquireRoot();var second=GeneratedSpriteAssets.AcquireRoot();
            var borrower=GeneratedSpriteAssets.Acquire();var sprite=borrower.Get("fixture/roots",Make);
            try{Assert.AreEqual(first.Generation,second.Generation);first.Dispose();borrower.Dispose();Assert.IsTrue(sprite!=null);second.Dispose();Assert.IsTrue(sprite==null);}
            finally{borrower.Dispose();first.Dispose();second.Dispose();}
        }
        [Test] public void 生成途中の例外と欠損は生成物を解放し再試行できる()
        {
            using(var scope=GeneratedSpriteAssets.Acquire()){
                Texture2D failed=null;
                Assert.Throws<InvalidOperationException>(()=>scope.Get<Sprite>("fixture/failure",b=>{failed=b.CreateTexture(2,2);throw new InvalidOperationException("fixture");}));
                Assert.IsTrue(failed==null);
                Assert.IsNull(scope.Get<Sprite>("fixture/failure",b=>{failed=b.CreateTexture(2,2);return null;}));Assert.IsTrue(failed==null);
                Assert.IsNotNull(scope.Get("fixture/failure",Make));
            }
        }
        [Test] public void 借用Textureを生成所有へ登録できず利用側終了でも破棄しない()
        {
            // importに相当する借用。実資産は変更せず、この処理の外で所有する。
            var borrowed=new Texture2D(8,8);
            try{using(var scope=GeneratedSpriteAssets.Acquire()){
                Assert.Throws<ArgumentException>(()=>scope.Get("fixture/import",b=>b.CreateSprite(borrowed,new Rect(0,0,8,8),Vector2.zero)));
            }Assert.IsTrue(borrowed!=null);Assert.AreEqual(8,borrowed.width);}
            finally{UnityEngine.Object.DestroyImmediate(borrowed);}
        }
        [Test] public void 非表示所有者は保持し破棄済みUnity所有者だけ回収する()
        {
            var owner=new GameObject("GeneratedAssetOwnerFixture");var root=GeneratedSpriteAssets.AcquireRoot();var scope=GeneratedSpriteAssets.Acquire(owner);
            var sprite=scope.Get("fixture/native-owner",Make);
            try{owner.SetActive(false);root.Dispose();GeneratedSpriteAssets.Prune();Assert.IsTrue(sprite!=null);
                UnityEngine.Object.DestroyImmediate(owner);GeneratedSpriteAssets.Prune();Assert.IsTrue(sprite==null);Assert.AreEqual(0,scope.Generation);
            }finally{if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);scope.Dispose();root.Dispose();}
        }
        [Test] public void 描画不能な初回では借用を残さず外部Renderer破棄後も再生成する()
        {
            var owner=new GameObject("ZeroScaleOwner",typeof(SpriteRenderer));var body=owner.GetComponent<SpriteRenderer>();
            var texture=new Texture2D(8,8);var sprite=Sprite.Create(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f));body.sprite=sprite;
            var visual=new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Guard);
            try{
                owner.transform.localScale=Vector3.zero;visual.Refresh(body,true);Assert.IsNull(visual.Renderer);
                var state=GeneratedSpriteAssets.Inspect();Assert.AreEqual(0,state.GetType().GetProperty("borrowers").GetValue(state));
                owner.transform.localScale=Vector3.one;visual.Refresh(body,true);var first=visual.Renderer;
                Assert.IsNotNull(first);UnityEngine.Object.DestroyImmediate(first.gameObject);GeneratedSpriteAssets.Prune();
                visual.Refresh(body,true);Assert.IsTrue(visual.Renderer!=null);Assert.IsFalse(ReferenceEquals(first,visual.Renderer));
                Assert.IsTrue(sprite!=null);Assert.IsTrue(texture!=null);
            }finally{visual.Dispose();UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);GeneratedSpriteAssets.Prune();}
        }
        [Test] public void Rootなし明示scopeも再利用と最終解放を行い再取得で再生成できる()
        {
            var scope=GeneratedSpriteAssets.Acquire();var before=scope.Get("fixture/legacy",Make);var texture=before.texture;var generation=scope.Generation;
            Assert.AreSame(before,scope.Get("fixture/legacy",Make));scope.Dispose();Assert.IsTrue(before==null);Assert.IsTrue(texture==null);
            using(var next=GeneratedSpriteAssets.Acquire()){Assert.AreNotEqual(generation,next.Generation);Assert.IsNotNull(next.Get("fixture/legacy",Make));}
        }
    }
}
