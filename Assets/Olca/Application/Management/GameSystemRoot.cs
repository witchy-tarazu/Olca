using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Management;

namespace Olca.Application.Management
{
    /// <summary>既存起動Sceneとの互換入口。Sceneの内容を巨大なRootクラスへ移さない。</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GameSystemRoot : MonoBehaviour
    {
        private static GameSystemRoot instance;
        public GameSystem Services { get; private set; }
        public string StartupError { get; private set; }
        public async System.Threading.Tasks.Task<HostOpenResult> StartCompatibilityHostAsync(System.Threading.CancellationToken cancellation=default)
        {
            Initialize();HostOpenResult result;
            try { result=await Services.Hosts.OpenAsync(BootstrapEntry.CreateRequest(Services),cancellation); }
            catch(System.Exception error){result=new HostOpenResult(false,error:error.Message);}
            StartupError=result.Succeeded||result.Cancelled?null:result.Error;return result;
        }
        public static GameSystemRoot Ensure()
        {
            if (instance != null) { instance.Initialize(); return instance; }
            if (!UnityEngine.Application.isPlaying) throw new System.InvalidOperationException("Editor検証はGameSystemを明示注入してください");
            var existing = Object.FindObjectsByType<GameSystemRoot>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (existing.Length > 0) { instance = existing[0]; instance.gameObject.SetActive(true); DontDestroyOnLoad(instance.gameObject); instance.Initialize(); return instance; }
            return new GameObject("GameSystemRoot").AddComponent<GameSystemRoot>();
        }
        private void Awake()
        {
            if (!UnityEngine.Application.isPlaying) return;
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject); Initialize();
        }
        private void Initialize()
        {
            if (Services != null) return;
            var resources = new ResourceManager();
            var dialogs = new DialogManager(() => Time.frameCount);
            Services = new GameSystem(new SaveManager(), resources, new SoundManager(transform), new ViewManager(resources, dialogs), dialogs, new UIRuntimeManager(resources,transform), new CameraRuntimeManager(resources,transform));
        }
        private void LateUpdate() => Services?.Dialogs.Tick();
        private void OnDestroy()
        {
            if (instance != this) return;
            Services?.Dispose(); Services = null; instance = null;
        }
    }
}
