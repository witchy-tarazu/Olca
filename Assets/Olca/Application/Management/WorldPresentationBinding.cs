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
        private Func<bool> allowed,restoreAllowed;private Action returned;private bool disposed;
        private readonly OutsideExplorationRuntime runtime;private readonly UnityInputProvider input;private Action paused;private Func<Olca.Core.Domain.Card,Sprite> icon;
        public Olca.Presentation.Exploration.OutsideExplorationView View{get;private set;}
        public ManagedScreenContext Context{get;private set;}
        private Olca.Core.Domain.InputBindingProfile guideKeyboard,guideGamepad;private InputGuideDevice guideDevice;private string selectionGuide,movementGuide,cardUseGuide;
        private string SelectionGuide()
        {
            if(input==null)return "";
            var device=InputGuideActivity.Current;
            if(selectionGuide==null||!ReferenceEquals(guideKeyboard,input.KeyboardProfile)||!ReferenceEquals(guideGamepad,input.GamepadProfile)||guideDevice!=device){
                guideKeyboard=input.KeyboardProfile;guideGamepad=input.GamepadProfile;guideDevice=device;
                var profile=device==InputGuideDevice.Gamepad?guideGamepad:guideKeyboard;string kind=device==InputGuideDevice.Gamepad?"gamepad":"keyboard";
                string traversal=device==InputGuideDevice.Gamepad?"outside":"exploration";
                string Label(string context,string action)=>profile.Bindings.TryGetValue((kind,context,action),out var key)&&key!=null?(device==InputGuideDevice.Gamepad?UnityInputProvider.GamepadBindingLabel(key):key):"ミワリアテ";
                string Group(params string[] actions){var keys=actions.Select(action=>profile.Bindings.TryGetValue((kind,"battle",action),out var key)?key:null).Where(key=>key!=null).Distinct().Select(key=>device==InputGuideDevice.Gamepad?UnityInputProvider.GamepadBindingLabel(key):key);return keys.Any()?string.Join("/",keys):"ミワリアテ";}
                movementGuide=device==InputGuideDevice.Touch?"":Olca.Presentation.CustomScreen.CustomDisplay.Katakana((device==InputGuideDevice.Gamepad?"イドウ: ヒダリ スティック":"イドウ  ヒダリ: "+Group("LeftPrimary","LeftSecondary")+"  ミギ: "+Group("RightPrimary","RightSecondary"))+"\nジャンプ: "+Label(traversal,"JumpPrimary")+"  /  ダッシュ: "+Label(traversal,"DashPrimary")+(device==InputGuideDevice.Gamepad?"":"/"+Label(traversal,"DashSecondary")))+"\nシラベル / ハナす: "+Label("outside","InteractionPrimary")+" / カムイ ホイール: "+Label("outside","WheelPrimary");
                cardUseGuide=Olca.Presentation.CustomScreen.CustomDisplay.Katakana((device==InputGuideDevice.Touch?"オナジ カードを サイ タップ":"カード シヨウ: "+Label("outside","CardPrimary")+" / サイクリック")+"（クウチュウ: ヒール・ツウジョウ ソード カリ）");
                selectionGuide=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(device==InputGuideDevice.Touch?"カード タップ: センタク":"カード センタク  マエ: "+Label(traversal,"SelectionPrevious")+"  /  ツギ: "+Label(traversal,"SelectionNext"));
            }
            return selectionGuide;
        }
        private string CardUseGuide(){SelectionGuide();return cardUseGuide??"";}
        private string MovementGuide(){SelectionGuide();return movementGuide??"";}
        public bool IsOpen=>!disposed&&View!=null&&View.IsOpen;
        public bool HasLostView=>!disposed&&!ReferenceEquals(View,null)&&View==null;
        public OutsideExplorationPresentationBinding(ViewManager views,Olca.Core.Logic.Integration.OutsideExplorationSession session,Func<CancellationToken,Task<GameObject>> factory,Func<bool> allowed,Action returned,OutsideExplorationRuntime runtime=null,UnityInputProvider input=null,Action paused=null,Func<Olca.Core.Domain.Card,Sprite> icon=null,Action entered=null,Func<bool> canEnter=null,Func<bool> trialRoute=null,Func<bool> inputBlocked=null,Func<bool> restoreAllowed=null,Action<int> useItem=null)
        {
            this.restoreAllowed=restoreAllowed;this.runtime=runtime;this.input=input;this.paused=paused;this.icon=icon;this.session=session??throw new ArgumentNullException(nameof(session));this.allowed=allowed??throw new ArgumentNullException(nameof(allowed));this.returned=returned;layer=views.GetLayer("return-hub");
            layer.Register(registrationOwner,ScreenKey,factory,(panel,context)=>{
                if(disposed||(!this.allowed()&&this.restoreAllowed?.Invoke()!=true))throw new OperationCanceledException();
                View=panel.GetComponent<Olca.Presentation.Exploration.OutsideExplorationView>()??throw new InvalidOperationException("外界Viewがありません");
                Context=context;var lease=session.AttachPresentation();try{runtime?.Attach(this,input,useItem);View.Show(context,lease,()=>{if(disposed||!context.IsCurrent||!lease.IsCurrent||!this.allowed())return;layer.CloseCurrent();this.returned?.Invoke();},this.paused==null?(Action)null:()=>{if(!disposed&&context.IsCurrent&&lease.IsCurrent&&this.allowed())this.paused?.Invoke();},this.icon,SelectionGuide,MovementGuide,entered,canEnter,()=>input?.OutsideInteractionLabel??"Enter",trialRoute,inputBlocked,runtime!=null&&input!=null?(Action<Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease,Olca.Core.Logic.Integration.OutsideExplorationCards,int,long>)input.QueueOutsideHandPress:null,CardUseGuide);}catch{runtime?.Detach(this);lease.Dispose();throw;}
            },panel=>{
                runtime?.Detach(this);
                // 管理側の閉鎖は復旧対象から外す。表示中の外部破棄ではこの経路を通らない。
                Context=null;View=null;
                if(panel!=null)panel.GetComponent<Olca.Presentation.Exploration.OutsideExplorationView>()?.Hide();
            });
        }
        public async Task<ScreenOpenResult> OpenAsync(CancellationToken cancellation=default)
        {if(disposed||(!allowed()&&restoreAllowed?.Invoke()!=true))return await layer.ShowAsync(ScreenKey,new CancellationToken(true));var result=await layer.ShowAsync(ScreenKey,cancellation);if(!result.Succeeded&&View==null)View=null;return result;}
        public bool ReturnToHub(){if(disposed||!allowed()||!IsOpen)return false;layer.CloseCurrent();returned?.Invoke();return true;}
        public void ResetInputForFocus(){if(!disposed)runtime?.ResetInputForFocus(this);}
        public void SetCovered(bool covered){runtime?.SetCovered(this,covered);if(View!=null)View.SetCovered(covered);}
        public void Pump(bool active){runtime?.Pump(this,active&&IsOpen&&View.CanPlay);if(View!=null)View.Render();}
        public void Dispose(){if(disposed)return;disposed=true;runtime?.Detach(this);layer.UnregisterOwner(registrationOwner);Context=null;View=null;allowed=null;restoreAllowed=null;returned=null;paused=null;icon=null;}
    }
}

namespace Olca.Application.Management
{
    /// <summary>拠点の探索デッキ一覧と編集。描画用所持投影は保存先へ渡さない。</summary>
    public sealed class ExplorationDeckLibrary:IDisposable
    {
        private const string Key="exploration-decks";
        private readonly HostPanelBindings panels;
        private readonly ViewManager layer;
        private readonly Func<Olca.Core.Domain.ClearReturnProfile> owned;
        private readonly Func<Olca.Core.Domain.OwnedCardCount,Olca.Core.Domain.Card> resolve;
        private readonly Func<bool> alive;
        private readonly Action returned;
        private Olca.Core.Domain.ExplorationDeckSession session;
        private readonly Func<Olca.Core.Domain.IExplorationDeckStore> store;
        private string generation;
        private Olca.Presentation.Dungeon.ClearReturnView view;
        private readonly System.Collections.Generic.List<Olca.Core.Domain.SavedDeckCard> draft=new System.Collections.Generic.List<Olca.Core.Domain.SavedDeckCard>();
        private string editingId,name,persistedName;
        private bool editing,dirty,disposed;
        public bool IsOpen{get;private set;}
        public ExplorationDeckLibrary(HostPanelBindings panels,ViewManager views,DialogManager dialogs,Olca.Presentation.Management.GameplayPanelFactory factory,
            Func<Olca.Core.Domain.IExplorationDeckStore> store,Func<Olca.Core.Domain.ClearReturnProfile> owned,Func<Olca.Core.Domain.OwnedCardCount,Olca.Core.Domain.Card> resolve,
            Func<Olca.Core.Domain.Card,Sprite> icon,Func<bool> alive,Action returned)
        {
            this.panels=panels;this.owned=owned;this.resolve=resolve;this.alive=alive;this.returned=returned;this.store=store;generation=owned().CheckpointGeneration;
            session=new Olca.Core.Domain.ExplorationDeckSession(store());layer=views?.GetLayer("return-hub");
            panels.Register(Key,"return-hub",()=>factory.Component<Olca.Presentation.Dungeon.ClearReturnView>("ExplorationDeckLibrary",created=>{
                created.Initialize(factory.Font,null,null,null,null);created.ConfigureExplorationDeckLibrary();
                created.ConfigureMenuNavigation(()=>Array.Empty<UnityEngine.UI.Button>(),resolve,icon);
                if(layer!=null)created.ConfigureManagement(()=>panels.Show(Key),layer.CloseCurrent,dialogs);view=created;
            }).gameObject,(panel,context)=>{if(!Live)throw new OperationCanceledException();view=panel.GetComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.ShowManaged(context);},panel=>{if(panel!=null)panel.GetComponent<Olca.Presentation.Dungeon.ClearReturnView>().HideManaged();});
        }
        private bool Live=>!disposed&&alive()&&owned()?.CheckpointGeneration==generation;
        private bool Operate=>Live&&IsOpen&&view!=null&&view.gameObject.activeInHierarchy&&view.ManagementContext?.DialogBlocksInput!=true;
        public void Open(){if(disposed||!alive())return;if(generation!=owned().CheckpointGeneration){session.Dispose();generation=owned().CheckpointGeneration;session=new Olca.Core.Domain.ExplorationDeckSession(store());}IsOpen=true;editing=false;RenderList();}
        private Olca.Core.Domain.ClearReturnProfile Projection()
        {var source=owned().Copy();source.SavedDecks=session.Snapshot?.Decks??Array.Empty<Olca.Core.Domain.SavedDepartureDeck>();source.EquippedDeckId=session.Snapshot?.SelectedDeckId;return source;}
        private void Ensure(){view=panels.Show(Key).GetComponent<Olca.Presentation.Dungeon.ClearReturnView>();}
        private void RenderList(string deleting=null)
        {
            if(!Live||!IsOpen)return;editing=false;Ensure();
            view.RenderSavedDeckMenu(Projection(),session.Error,session.HasPendingSave,()=>Edit(null),id=>Edit(id),null,id=>{if(!Operate||session.HasPendingSave)return;var latest=owned();var selected=session.Snapshot.Decks.SingleOrDefault(d=>d.Id==id);if(selected==null||selected.Cards.Any(c=>{var row=Olca.Core.Domain.OwnedDefinition.Resolve(latest,c);return row==null||resolve(row)==null;}))return;session.Select(id,latest);RenderList();},id=>{
                if(!Operate||session.HasPendingSave)return;RenderList(id);
            },Close,Retry,deleting,()=>{if(!Live||!IsOpen||session.HasPendingSave)return;session.Delete(deleting);RenderList();},()=>{if(!Operate)return;session.Reload();RenderList();});
        }
        private void Edit(string id)
        {
            if(!Operate||!session.Ready||session.HasPendingSave)return;
            var saved=id==null?null:session.Snapshot.Decks.SingleOrDefault(d=>d.Id==id);if(id!=null&&saved==null)return;
            editing=true;editingId=id;persistedName=saved?.Name;name=saved?.Name??"探索デッキ";draft.Clear();if(saved!=null)draft.AddRange(saved.Cards.Select(c=>c.Copy()));dirty=false;RenderEditor();
        }
        private void RenderEditor(bool discard=false)
        {
            if(!Live||!IsOpen)return;Ensure();var source=Projection();var options=source.Owned.Select(resolve).ToArray();
            var slots=draft.ToArray();
            bool valid=Olca.Core.Domain.ExplorationDeckRules.TryValidate(source,new Olca.Core.Domain.SavedDepartureDeck{Cards=slots},out var invalid);
            var cards=ResolveDraftCards(source,options,slots);
            valid&=cards.All(c=>c!=null);
            view.RenderOwnedDeparture(source,options,cards,session.Error??(valid?null:invalid??"定義未解決のカードがあります"),valid,(index,delta)=>ChangeDraft(source,options,index,delta),null,RequestCloseEditor,()=>RenderEditor(),discard,
            SaveDraft,name,value=>{if(!Operate||session.HasPendingSave)return;name=value;dirty=true;},session.HasPendingSave,Retry,draft.Select(c=>c.DefinitionKey).ToArray(),persistedName);
        }
        private void ChangeDraft(Olca.Core.Domain.ClearReturnProfile source,Olca.Core.Domain.Card[] options,int index,int delta)
        {
            if(!Operate||session.HasPendingSave||index<0||index>=source.Owned.Length)return;
            var row=source.Owned[index];string key=Olca.Core.Domain.OwnedDefinition.Key(row);int count=draft.Count(c=>c.DefinitionKey==key);
            if(delta>0){if(options[index]==null||count>=row.Count||draft.Count>=Olca.Core.Domain.ExplorationDeckRules.RequiredCards)return;draft.Add(new Olca.Core.Domain.SavedDeckCard{Id=row.Id,Level=row.Level,DefinitionKey=key});}
            else{int at=draft.FindLastIndex(c=>c.DefinitionKey==key);if(at<0)return;draft.RemoveAt(at);}dirty=true;RenderEditor();
        }
        private void RequestCloseEditor(bool force)
        {
            if(!Live||!IsOpen||session.HasPendingSave)return;if(dirty&&!force){RenderEditor(true);return;}dirty=false;RenderList();
        }
        private void SaveDraft()
        {
            if(!Operate||session.HasPendingSave||!view.CanUseDeckName(name)&&name!=persistedName)return;
            // 確定時の最新所持を使い、読取投影や過去の候補を永続化しない。
            var latest=owned();if(latest.CheckpointGeneration!=generation)return;
            if(draft.Any(slot=>{var row=Olca.Core.Domain.OwnedDefinition.Resolve(latest,slot);return row==null||resolve(row)==null;}))return;
            bool saved=session.Save(editingId,name,draft,latest);if(saved){dirty=false;RenderList();}else RenderEditor();
        }
        private static Olca.Core.Domain.Card[] ResolveDraftCards(Olca.Core.Domain.ClearReturnProfile source,Olca.Core.Domain.Card[] options,Olca.Core.Domain.SavedDeckCard[] slots)
        {
            // 同じ描画で解決済みの候補を使う。採用slotの可変状態は個別Cloneに分ける。
            return slots.Select(slot=>{var row=Olca.Core.Domain.OwnedDefinition.Resolve(source,slot);
                return row==null?null:options[Array.IndexOf(source.Owned,row)]?.Clone();}).ToArray();
        }
        public void Tick(){if(Live&&IsOpen&&view==null){if(editing)RenderEditor();else RenderList();}}
        private void Retry(){if(!Live||!IsOpen||!session.HasPendingSave)return;bool saved=session.Retry();if(saved){dirty=false;RenderList();}else if(editing)RenderEditor();else RenderList();}
        private void Close(){if(!Operate||session.HasPendingSave)return;IsOpen=false;view.Hide();returned();}
        public void Dispose(){if(disposed)return;disposed=true;IsOpen=false;session.Dispose();if(view!=null)view.Hide();view=null;}
    }
}
