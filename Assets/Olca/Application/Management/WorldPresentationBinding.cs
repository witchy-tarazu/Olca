using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Field;
using Olca.Presentation.Management;
namespace Olca.Application.Management
{
    /// <summary>役者/床/背景/数値poolを持つWorld実体をhostへ供給する。論理状態は所有しない。</summary>
    public sealed class WorldPresentationBinding:IDisposable
    {
        private readonly object owner;
        private readonly ViewManager layer;
        private GameObject root;
        private bool disposed;
        public GameFieldView Field{get;private set;}
        public ScrollingBackgroundView[] Backgrounds{get;private set;}=Array.Empty<ScrollingBackgroundView>();
        public WorldPresentationBinding(object owner,ResourceManager resources,ViewManager views,Transform parent)
        {
            this.owner=owner??throw new ArgumentNullException(nameof(owner));layer=views.GetLayer("world");
            layer.Register(owner,"dungeon-world",ct=>{ct.ThrowIfCancellationRequested();return Task.FromResult(UnityEngine.Object.Instantiate(resources.GetShared<GameObject>("OlcaSystem/WorldPresentation"),parent,false));},(panel,context)=>{
                root=panel;Field=panel.GetComponent<GameFieldView>();if(Field==null)throw new InvalidOperationException("World Viewがありません");
                Backgrounds=panel.GetComponentsInChildren<ScrollingBackgroundView>(true);
                var assets=new WorldVisualResources(resources);Field.ConfigureVisualResources(assets);
                // Scene内MaterialへのPPtrをPrefabへ残さず、同設定の共有assetを有効化前に供給する。
                var renderers=panel.GetComponentsInChildren<SpriteRenderer>(true);
                renderers.Single(x=>x.name=="Ground").sharedMaterial=assets.Ground;
                renderers.Single(x=>x.name=="DungeonBackground").sharedMaterial=assets.Background;
                panel.SetActive(false);
            },panel=>{if(panel!=null)panel.SetActive(false);});
            try{
                using(var cancellation=new CancellationTokenSource()){
                    var opening=layer.ShowAsync("dungeon-world",cancellation.Token);
                    if(!opening.IsCompleted){cancellation.Cancel();throw new InvalidOperationException("World起動は同期ロード可能なPrefabが必要です");}
                    var result=opening.GetAwaiter().GetResult();if(!result.Succeeded)throw new InvalidOperationException(result.Error??"World Viewを開始できません");
                }
            }catch{Dispose();throw;}
        }
        public void ConfigureCamera(Camera camera)
        {
            if(disposed)throw new ObjectDisposedException(nameof(WorldPresentationBinding));
            Field.ConfigurePresentationCamera(camera);foreach(var background in Backgrounds)background.ConfigurePresentationCamera(camera);
        }
        public void Activate(){if(disposed)throw new ObjectDisposedException(nameof(WorldPresentationBinding));root.SetActive(true);}
        public void Dispose()
        {
            if(disposed)return;
            if(root!=null)root.SetActive(false);
            // managed参照解除はScene終了で先に破棄されたwrapperにも適用する。
            if(!ReferenceEquals(Field,null))Field.ReleasePresentation();
            foreach(var background in Backgrounds)if(!ReferenceEquals(background,null))background.ConfigurePresentationCamera(null);
            layer.UnregisterOwner(owner);root=null;Field=null;Backgrounds=Array.Empty<ScrollingBackgroundView>();disposed=true;
        }
    }
}
