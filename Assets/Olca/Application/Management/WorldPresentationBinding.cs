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

namespace Olca.Application.Management
{
    /// <summary>外界入口Viewの生成・開始結果だけを調整。RootのSessionをView破棄で終了しない。</summary>
    public sealed class OutsideExplorationPresentationBinding : IDisposable
    {
        public const string ScreenKey="outside-exploration-entry";
        private readonly object registrationOwner=new object();
        private readonly ViewManager layer;
        private readonly Olca.Core.Logic.Integration.OutsideExplorationSession session;
        private Func<bool> allowed;private Action returned;private bool disposed;
        public Olca.Presentation.Exploration.OutsideExplorationView View{get;private set;}
        public bool IsOpen=>!disposed&&View!=null&&View.IsOpen;
        public bool HasLostView=>!disposed&&!ReferenceEquals(View,null)&&View==null;
        public OutsideExplorationPresentationBinding(ViewManager views,Olca.Core.Logic.Integration.OutsideExplorationSession session,Func<CancellationToken,Task<GameObject>> factory,Func<bool> allowed,Action returned)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));this.allowed=allowed??throw new ArgumentNullException(nameof(allowed));this.returned=returned;layer=views.GetLayer("return-hub");
            layer.Register(registrationOwner,ScreenKey,factory,(panel,context)=>{
                if(disposed||!this.allowed())throw new OperationCanceledException();
                View=panel.GetComponent<Olca.Presentation.Exploration.OutsideExplorationView>()??throw new InvalidOperationException("外界Viewがありません");
                var lease=session.AttachPresentation();try{View.Show(context,lease,()=>{if(disposed||!context.IsCurrent||!lease.IsCurrent||!this.allowed())return;layer.CloseCurrent();this.returned?.Invoke();});}catch{lease.Dispose();throw;}
            },panel=>{if(panel!=null)panel.GetComponent<Olca.Presentation.Exploration.OutsideExplorationView>()?.Hide();});
        }
        public async Task<ScreenOpenResult> OpenAsync(CancellationToken cancellation=default)
        {if(disposed||!allowed())return await layer.ShowAsync(ScreenKey,new CancellationToken(true));var result=await layer.ShowAsync(ScreenKey,cancellation);if(!result.Succeeded&&View==null)View=null;return result;}
        public void Dispose(){if(disposed)return;disposed=true;layer.UnregisterOwner(registrationOwner);View=null;allowed=null;returned=null;}
    }
}
