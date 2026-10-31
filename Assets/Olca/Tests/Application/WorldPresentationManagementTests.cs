using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Olca.Application.Management;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Presentation.Field;
using Olca.Presentation.Management;
using Olca.Tests.Mocks;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class WorldPresentationManagementTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        [Test] public void WorldPrefab_非active生成で内部参照と材質を供給し表示だけを返却する()
        {
            var host=new GameObject("WorldHost");var resources=new ResourceManager();var dialogs=new DialogManager(()=>Time.frameCount);var views=new ViewManager(resources,dialogs);var cameraObject=new GameObject("WorldCamera",typeof(Camera));cameraObject.SetActive(false);
            try{
                var binding=new WorldPresentationBinding(new object(),resources,views,host.transform);var field=binding.Field;var backgrounds=binding.Backgrounds;int children=field.GetComponentsInChildren<Transform>(true).Length;
                Assert.AreEqual(7,children);Assert.IsFalse(field.gameObject.activeSelf);Assert.IsNull(field.PresentationCamera);Assert.AreEqual(2,backgrounds.Length);Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.AreEqual("dungeon-world",views.GetLayer("world").CurrentKey);
                foreach(var name in new[]{"playerTransform","playerSprite","playerAnimator","battleGridView","worldDamageNumberPool"}){var value=Get(field,name);Assert.NotNull(value,name);Assert.IsTrue(((Component)value).transform.IsChildOf(field.transform),name);}
                var renderers=field.GetComponentsInChildren<SpriteRenderer>(true);Assert.AreSame(resources.GetShared<Material>("OlcaSystem/WorldGround"),renderers.Single(x=>x.name=="Ground").sharedMaterial);Assert.AreSame(resources.GetShared<Material>("OlcaSystem/WorldDungeonBackground"),renderers.Single(x=>x.name=="DungeonBackground").sharedMaterial);
                var camera=cameraObject.GetComponent<Camera>();binding.ConfigureCamera(camera);Assert.AreSame(camera,field.PresentationCamera);Assert.IsTrue(backgrounds.All(x=>x.PresentationCamera==camera));
                binding.Dispose();binding.Dispose();Assert.IsNull(binding.Field);Assert.IsEmpty(binding.Backgrounds);Assert.IsNull(field.PresentationCamera);Assert.IsTrue(backgrounds.All(x=>x.PresentationCamera==null));Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,views.GetLayer("world").RegisteredCount);Assert.IsTrue(field==null);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void 素材resolver_未登録nullと必須拒否を区別し共有importを破棄しない()
        {
            var resources=new ResourceManager();var assets=new WorldVisualResources(resources);var material=assets.Ground;Assert.NotNull(material);Assert.AreSame(material,assets.Ground);Assert.IsNull(assets.Find<Texture2D>("OlcaSystem/does-not-exist"));Assert.Throws<InvalidOperationException>(()=>resources.GetShared<Texture2D>("OlcaSystem/does-not-exist"));resources.Dispose();Assert.IsTrue(material!=null);Assert.Throws<ObjectDisposedException>(()=>assets.Find<Texture2D>("OlcaSystem/does-not-exist"));
        }
        [Test] public void World返却_召喚と被弾購読とCamera参照を外し未初期化Viewで子を作らない()
        {
            var root=new GameObject("ReleaseWorld");var field=root.AddComponent<GameFieldView>();var battle=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{new Enemy("slime","スライム",50,ElementType.None,new Olca.Core.Domain.Vector2Int(3,0))},new Deck(new List<Card>()),2f,new MockRandomProvider(0),new MockLogger());
            try{
                field.OnBattleStarted(battle,0);typeof(GameFieldView).GetMethod("BindPlayerDamage",F).Invoke(field,new object[]{battle.Player});Assert.AreSame(battle,Get(field,"currentBattle"));
                int children=field.GetComponentsInChildren<Transform>(true).Length;field.ReleasePresentation();field.ReleasePresentation();Assert.AreEqual(children,field.GetComponentsInChildren<Transform>(true).Length);foreach(string name in new[]{"currentBattle","dungeonSystem","cameraObservedDungeon","damageTrackedPlayer","fieldCamera","visualResources"})Assert.IsNull(Get(field,name),name);
                foreach(string name in new[]{"OnSummonAdded","OnBattleEnd"})Assert.IsFalse(((Delegate)Get(battle,name))?.GetInvocationList().Any(d=>ReferenceEquals(d.Target,field))??false,name);
                Assert.AreEqual(100,battle.Player.CurrentHp);Assert.AreEqual(0,battle.LogicalFrame);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
