using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Olca.Application.Dungeon;
using Olca.Unity;

namespace Olca.Application.Management
{
    /// <summary>起動Sceneだけの入口。互換Scene直起動や通常View遷移には介入しない。</summary>
    public static class BootstrapEntry
    {
        public const string BootstrapScenePath="Assets/Scenes/BootstrapScene.unity";
        public const string CompatibilityScenePath="Assets/Scenes/DungeonScene.unity";
        private static bool observed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset(){SceneManager.sceneLoaded-=Loaded;observed=false;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initial(){if(!observed&&SceneManager.GetActiveScene().path==BootstrapScenePath)Launch();}
        private static void Loaded(Scene scene,LoadSceneMode mode){if(scene.path==BootstrapScenePath)Launch();}
        private static async void Launch()
        {
            observed=true;
            try {await ShowTitleAsync(GameSystemRoot.Ensure());}
            catch(Exception error){Debug.LogException(error);}
        }
        public static async System.Threading.Tasks.Task ShowTitleAsync(GameSystemRoot root)
        {
            var services=root.Services;
            // Bootstrap再読込で既存攻略をTitleの背面へ走らせない。既存hostの再使用/失効判定はHostManagerへ委譲する。
            if(services.CurrentSession!=null){await root.StartGameplayHostAsync();return;}
            var storage=ValidationStorageGuard.ExplicitEditorBootstrap();
            using(var input=new UnityInputProvider()){
                input.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _);
                var settings=new GamepadBindingFileStore(storage?.BootstrapSettings??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
                if(settings.TryRead(out var profile,out _))input.TryRestoreGamepadProfile(profile,out _);
            }
            var layer=services.Views.GetLayer("title");const string key="title";
            if(layer.RegisteredCount!=0){await layer.ShowAsync(key);return;}
            var owner=new object();
            layer.Register(owner,key,ct=>{ct.ThrowIfCancellationRequested();return System.Threading.Tasks.Task.FromResult(Olca.Presentation.Management.BootstrapTitleView.Create(services.UI.ViewParent,services.Resources));},(panel,context)=>{
                bool starting=false,closed=false;var display=services.Cameras.HoldDisplay();var lifetime=new System.Threading.CancellationTokenSource();
                void End(){if(closed)return;closed=true;display.Dispose();lifetime.Cancel();if(!starting)lifetime.Dispose();}
                var view=panel.GetComponent<Olca.Presentation.Management.BootstrapTitleView>();
                var progressStore=new ClearReturnFileStore(storage?.BootstrapProgress??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","progress"));
                var progress=new Olca.Core.Domain.ClearReturnSession(progressStore);
                var operations=new ProgressActionOperations(progress,()=>!starting&&!closed&&context.IsCurrent&&services.CurrentSession==null,()=>{
                    var prefab=services.Resources.GetShared<GameObject>(GameplayHostPrefabPath);
                    var controllers=prefab.GetComponentsInChildren<DungeonBattleController>(true);
                    if(controllers.Length!=1)throw new InvalidOperationException("hostのControllerは1つ必要です");
                    return controllers[0].CreateNewGameStarterCards();
                },()=>!closed&&context.IsCurrent&&services.CurrentSession==null);
                async void Begin(bool newGame=false){
                    if(starting||!context.IsCurrent)return;starting=true;view.SetBusy(true,null);
                    try{if(newGame)services.ResetOutsideForNewProgress(progress.Snapshot.CheckpointGeneration);var result=await root.StartGameplayHostAsync(lifetime.Token);if(!context.IsCurrent||view==null)return;if(result.Succeeded){layer.UnregisterOwner(owner);return;}view.SetBusy(false,result.Error??"開始を取り消しました");}
                    catch(Exception error){if(context.IsCurrent&&view!=null)view.SetBusy(false,error.Message);}
                    finally{starting=false;if(closed)lifetime.Dispose();}
                }
                try{view.Show(context,()=>Begin(),()=>view.OpenNewGame(operations,services.Dialogs,()=>Begin(true)),progress.Ready&&!progressStore.IsNewProfile,progress.Ready,progress.Ready?null:progress.Error,End);}catch{End();throw;}
            },panel=>{if(panel!=null)panel.GetComponent<Olca.Presentation.Management.BootstrapTitleView>().Hide();});
            var opened=await layer.ShowAsync(key);if(!opened.Succeeded){layer.UnregisterOwner(owner);if(!opened.Cancelled)throw new InvalidOperationException(opened.Error);}
        }
        public const string GameplayHostPrefabPath="OlcaSystem/DungeonHost";
        public static HostOpenRequest CreateGameplayRequest(GameSystem services)
        {
            var scene=SceneManager.GetSceneByPath(BootstrapScenePath);
            if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("本番hostはBootstrapSceneへ配置してください");
            var storage=ValidationStorageGuard.ExplicitEditorBootstrap();
            DungeonBattleController Controller(GameObject root) {
                var values=root.GetComponentsInChildren<DungeonBattleController>(true);
                if(values.Length!=1)throw new InvalidOperationException("hostのControllerは1つ必要です");return values[0];
            }
            return HostOpenRequest.Prefab(GameplayHostPrefabPath,ct=>GameplayPrefabHostLease.LoadAsync(services.Resources,GameplayHostPrefabPath,scene,ct),root=>{
                var controller=Controller(root);controller.ConfigureGameSystem(services);controller.ConfigureOutsideLandingRoute();controller.PrepareWorldPresentation(services);controller.ConfigureManagedCamera(services,null);
                if(storage!=null){controller.ConfigureGameplaySettingsDirectory(storage.BootstrapSettings);controller.ConfigureCheckpointDirectory(storage.BootstrapCheckpoint);controller.ConfigureProgressDirectory(storage.BootstrapProgress);controller.RequireValidationStorageGuard();}
            },root=>Controller(root).StartManagedHost(services));
        }
        public static HostOpenRequest CreateRequest(GameSystem services)
        {
            var storage=ValidationStorageGuard.ExplicitEditorBootstrap();
            DungeonBattleController Controller(Scene scene) {
                var values=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<DungeonBattleController>(true)).ToArray();
                if(values.Length!=1)throw new InvalidOperationException("互換ホストのControllerは1つ必要です");return values[0];
            }
            return new HostOpenRequest(CompatibilityScenePath,scene=>{
                var controller=Controller(scene);controller.ConfigureGameSystem(services);
                controller.PrepareWorldPresentation(services);
                controller.ConfigureManagedCamera(services);
                if(storage!=null){controller.ConfigureGameplaySettingsDirectory(storage.BootstrapSettings);controller.ConfigureCheckpointDirectory(storage.BootstrapCheckpoint);controller.ConfigureProgressDirectory(storage.BootstrapProgress);controller.RequireValidationStorageGuard();}
            },scene=>{
                var controller=Controller(scene);
                // 管理起動はDI済みのControllerを明示的に有効化する。旧直Playの検証用非active状態にも依存しない。
                controller.gameObject.SetActive(true);controller.enabled=true;
                controller.StartManagedHost(services);
            });
        }
    }
}
