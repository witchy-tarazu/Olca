using System;using System.IO;using System.Linq;using System.Reflection;using Olca.Core.Domain;using Olca.Core.Logic.Dungeon;using Olca.Application.Dungeon;using Olca.Unity;using UnityEditor.SceneManagement;
public static class ExportProductReturn
{
 public static PortableReturnPackage Capture(string scene,string definitionsPath,string checkpoint,string progress,string root,RunReturnOutcome outcome,string commitSeed)
 {
  var io=new CheckpointFileIo(new ValidationStorageGuard(root,"/workspace/.olca-cloud/config/unity3d/DefaultCompany/Olca/Olca"));io.RequireWritable(root+"/product-input.json");
  var opened=EditorSceneManager.OpenPreviewScene(scene);try{
   var c=opened.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DungeonBattleController>(true)).Single();c.enabled=false;c.ConfigureCheckpointDirectory(root+"/never-start/checkpoint");c.ConfigureProgressDirectory(root+"/never-start/progress");c.ConfigureGameplaySettingsDirectory(root+"/never-start/settings");
   var settings=(ClearReturnSettings)c.GetType().GetMethod("ReturnSettings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,null);
   var p=new PortableReturnPackage{Definitions=PortableCheckpointJson.Deserialize<PortableBattlePackage>(File.ReadAllBytes(definitionsPath)),CheckpointEnvelope=File.ReadAllBytes(checkpoint),InitialProgressEnvelope=File.ReadAllBytes(progress),Settings=settings.Copy(),Outcome=outcome,CommitSeed=commitSeed};p.Restore(out _);return p;
  }finally{EditorSceneManager.ClosePreviewScene(opened);}
 }
}
