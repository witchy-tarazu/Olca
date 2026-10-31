#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Olca.Application.Management
{
    /// <summary>GUIDを固定せず、各EditorのSceneパスから標準ビルド入口を登録する。</summary>
    public static class BootstrapBuildSetup
    {
        public static EditorBuildSettingsScene[] Plan(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            var result=new List<EditorBuildSettingsScene>{new EditorBuildSettingsScene(BootstrapEntry.BootstrapScenePath,true)};
            foreach(var item in scenes)if(item.path!=BootstrapEntry.BootstrapScenePath&&!(item.path==BootstrapEntry.CompatibilityScenePath&&result.Any(x=>x.path==item.path)))result.Add(new EditorBuildSettingsScene(item.path,item.path==BootstrapEntry.CompatibilityScenePath||item.enabled));
            if(!result.Any(x=>x.path==BootstrapEntry.CompatibilityScenePath))result.Add(new EditorBuildSettingsScene(BootstrapEntry.CompatibilityScenePath,true));
            return result.ToArray();
        }
        [InitializeOnLoadMethod] private static void Schedule()=>EditorApplication.delayCall+=Install;
        [MenuItem("Window/Olca/Set Bootstrap Build Entry")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapEntry.BootstrapScenePath)==null||AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapEntry.CompatibilityScenePath)==null)return;
            var prior=EditorBuildSettings.scenes;var next=Plan(prior);
            if(prior.Length!=next.Length||prior.Where((x,i)=>x.path!=next[i].path||x.enabled!=next[i].enabled).Any())EditorBuildSettings.scenes=next;
        }
    }
}
#endif
