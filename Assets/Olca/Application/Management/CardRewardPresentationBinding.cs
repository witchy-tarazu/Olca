using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Olca.Presentation.Dungeon;
using Olca.Presentation.Management;
using Olca.Unity;
namespace Olca.Application.Management
{
    /// <summary>報酬の画面実体だけを所有する。確定・保存は既存攻略側が判断する。</summary>
    public sealed class CardRewardPresentationBinding : IDisposable
    {
        private readonly object owner;
        private readonly ViewManager layer;
        private bool disposed;
        public CardSelectionView View {get;private set;}
        public CardRewardPresentationBinding(object owner,ResourceManager resources,ViewManager views,Transform parent)
        {
            this.owner=owner;layer=views.GetLayer("card-reward");
            layer.Register(owner,"card-reward",ct=>{ct.ThrowIfCancellationRequested();var prefab=resources.GetShared<GameObject>("OlcaSystem/CardReward");if(prefab.activeSelf)throw new InvalidOperationException("報酬Prefabは非activeで供給してください");return Task.FromResult(UnityEngine.Object.Instantiate(prefab,parent,false));},
                (panel,context)=>{View=panel.GetComponentInChildren<CardSelectionView>(true);if(View==null)throw new InvalidOperationException("報酬Viewがありません");panel.SetActive(true);},panel=>{if(panel!=null){View?.Hide();panel.SetActive(false);}});
            try{using(var cancellation=new CancellationTokenSource()){var showing=layer.ShowAsync("card-reward",cancellation.Token);if(!showing.IsCompleted){cancellation.Cancel();throw new InvalidOperationException("報酬起動は同期ロード可能なPrefabが必要です");}var result=showing.GetAwaiter().GetResult();if(!result.Succeeded)throw new InvalidOperationException(result.Error??"報酬Viewを開始できません");}}
            catch{Dispose();throw;}
        }
        public void Dispose(){if(disposed)return;if(View!=null){View.Hide();View.ConfigurePresentation(null,null);}layer.UnregisterOwner(owner);View=null;disposed=true;}
    }
}
