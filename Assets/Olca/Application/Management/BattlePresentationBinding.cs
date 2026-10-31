using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Battle;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Management;

namespace Olca.Application.Management
{
    /// <summary>Battle/Customの表示実体だけをhostに貸し、終了時は購読解除してからViewを返す。</summary>
    public sealed class BattlePresentationBinding : IDisposable
    {
        private readonly object owner;
        private readonly ViewManager layer;
        private bool disposed;
        public BattleView Battle { get; private set; }
        public CustomScreenView Custom { get; private set; }

        public BattlePresentationBinding(object owner, ResourceManager resources, ViewManager views, Transform parent)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            layer = views.GetLayer("battle");
            layer.Register(owner, "battle-custom", ct => {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(UnityEngine.Object.Instantiate(resources.GetShared<GameObject>("OlcaSystem/BattlePresentation"), parent, false));
            }, (panel, context) => {
                Battle = panel.GetComponent<BattleView>();
                Custom = panel.GetComponentInChildren<CustomScreenView>(true);
                if (Battle == null || Custom == null) throw new InvalidOperationException("Battle/CustomのViewがありません");
                // 戦闘開始/終了のactive切替とCustomのpauseは既存Controller/Systemに残す。
                panel.SetActive(false);
            }, panel => { if (panel != null) panel.SetActive(false); });
            try {
                using (var cancellation = new CancellationTokenSource()) {
                    var showing = layer.ShowAsync("battle-custom", cancellation.Token);
                    if (!showing.IsCompleted) {
                        cancellation.Cancel();
                        throw new InvalidOperationException("起動Battle Viewは同期ロード可能なPrefabが必要です");
                    }
                    var result = showing.GetAwaiter().GetResult();
                    if (!result.Succeeded) throw new InvalidOperationException(result.Error ?? "Battle Viewを開始できません");
                }
            } catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            // Scene終了で先に破棄された実体は各ViewのOnDestroyが解除済み。
            if (Battle != null) Battle.ReleaseSessionPresentation();
            if (Custom != null) Custom.ReleaseSessionPresentation();
            layer.UnregisterOwner(owner);
            Battle = null; Custom = null; disposed = true;
        }
    }
}
