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
            try {var result=await GameSystemRoot.Ensure().StartGameplayHostAsync();if(!result.Succeeded&&!result.Cancelled)Debug.LogWarning("[Bootstrap] "+result.Error);}
            catch(Exception error){Debug.LogException(error);}
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
                var controller=Controller(root);controller.ConfigureGameSystem(services);controller.PrepareWorldPresentation(services);controller.ConfigureManagedCamera(services,null);
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
