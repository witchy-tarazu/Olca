using System;using System.IO;using System.Linq;using System.Reflection;using Olca.Core.Domain;using Olca.Core.Logic.Battle;using Olca.Core.Logic.Dungeon;using Olca.Application.Battle;using Olca.Application.Dungeon;using Olca.Unity;using UnityEditor.SceneManagement;
public static class ExportProductBattle {
 // Mainはこの作業の専用コピーを使う再現入口。別保存ではExportの引数を明示して呼ぶ。
 public static string Main()=>Export("Assets/Scenes/DungeonScene.unity","/workspace/work/Olca-portable-product-proof-20261022/checkpoint-copy.json","/workspace/work/Olca-portable-product-proof-20261022",123);
 public static string Export(string scenePath,string source,string root,uint seed){
  var guard=new ValidationStorageGuard(root,Path.Combine(UnityEngine.Application.persistentDataPath,"Olca"));var io=new CheckpointFileIo(guard);string input=root+"/checkpoint-copy.json",output=root+"/product-battle.json";io.RequireWritable(input,output);Directory.CreateDirectory(root);
  var bytes=File.ReadAllBytes(source);if(File.Exists(input)){if(!File.ReadAllBytes(input).SequenceEqual(bytes))throw new Exception("source copy mismatch");}else io.WriteAndFlush(input,bytes);
  var scene=EditorSceneManager.OpenPreviewScene(scenePath);
  try {var controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DungeonBattleController>(true)).Single();controller.enabled=false;
   controller.ConfigureCheckpointDirectory(root+"/never-start/checkpoint");controller.ConfigureProgressDirectory(root+"/never-start/progress");controller.ConfigureGameplaySettingsDirectory(root+"/never-start/settings");
   var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(controller,args);
   var factory=(SimpleCardFactory)Call("CreateCardFactory");var map=(DungeonConfig)Call("CreateDefaultDungeon",false);var defs=(CheckpointDefinitions)Call("CreateSingleDefinitions",map,factory.CreateStarterDeck());
   var rules=CaptureRules(controller);
   var package=PortableBattlePackage.Capture(defs,File.ReadAllBytes(input),rules,seed);
   package.Processing=PortableProcessingCatalog.Capture("product/processing","v1",factory.FusionCards.Concat(package.Cards.Select(c=>c.Restore())),factory,new SimpleFusionRecipeProvider(factory).FusionDefinitions,factory.GetTuningDefinition);
   package.Restore(out _);var packed=PortableCheckpointJson.Serialize(package);
   // 実際のBCLシリアライズ復元後にも全定義・保存を再検証する。
   var reread=PortableCheckpointJson.Deserialize<PortableBattlePackage>(packed);var restored=reread.Restore(out _);
   if(File.Exists(output)){if(!File.ReadAllBytes(output).SequenceEqual(packed))throw new Exception("existing export differs");}else io.WriteAndFlush(output,packed);
   return "product scene="+scenePath+"; map="+defs.MapHash+"; cards="+package.Cards.Length+"; boundary="+restored.Snapshot.Boundary+"; firstHand="+restored.FirstHand.Count+"; source SHA256="+PortableCheckpointJson.Hash(bytes)+"; replay seed="+reread.ReplaySeed+"; saved RNG="+(restored.Snapshot.Battle.Random!=null);
  }finally{EditorSceneManager.ClosePreviewScene(scene);}
 }
 public static PortableBattleRules CaptureRules(DungeonBattleController controller){
  int Value(string name)=>(int)controller.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
  var t=CheckpointBattleFactory.ProductBasicTiming;
  var rules=new PortableBattleRules{ChargeMultiplierVersion=1,ChargeMultiplier=Value("basicAttackChargeMultiplier"),ExplorationNormalSpeedMilliPerSecond=Value("explorationNormalSpeedMilliPerSecond"),ChargeFrames=Value("basicAttackChargeFrames"),IntervalFrames=Value("basicAttackIntervalFrames"),HitInvincibilityMs=Value("commonHitInvincibilityMs"),StartupFrames=t.StartupFrames,RecoveryFrames=t.RecoveryFrames,CancelAfterFrames=t.CancelAfterFrames,GaugeSpeed=CheckpointBattleFactory.DefaultGaugeSpeed};rules.Validate();return rules;
 }
}
