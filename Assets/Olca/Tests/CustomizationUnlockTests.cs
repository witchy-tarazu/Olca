using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
using Olca.Presentation.Dungeon;
using Cell=Olca.Core.Domain.Vector2Int;
namespace Olca.Tests {
 public class CustomizationUnlockTests {
  CustomizationCatalog Catalog()=>new CustomizationCatalog("b",new PartsBoardDefinition(3,2,new[]{new Cell(0,0),new Cell(0,1)}),new[]{"a"},new Dictionary<string,PartShape>{{"dot",new PartShape(new[]{new Cell(0,0)})}},new Dictionary<string,IEnumerable<Cell>>{{"right",new[]{new Cell(1,0),new Cell(1,1)}}});
  CustomizationProgress State()=>new CustomizationProgress{OwnedAccessoryIds=new[]{"a"},EquippedAccessoryIds=new[]{"a"},OwnedParts=new[]{new CustomizationItemCount{Id="dot",Count=2}},OwnedUnlockItems=new[]{new CustomizationItemCount{Id="right",Count=2}},BoardId="b",UnlockedCells=new[]{new CustomizationCell{X=0,Y=0},new CustomizationCell{X=0,Y=1}},Placements=new[]{new SavedPartPlacement{InstanceId="p",PartId="dot",X=0,Y=0},new SavedPartPlacement{InstanceId="q",PartId="dot",X=0,Y=1}}};
  [Test] public void 供給元は有効な装備個体を保持し同種を合算せず入力と隔離する(){var s=State();var sources=new CustomizationLoadoutSources(Catalog(),s);s.EquippedAccessoryIds[0]="changed";s.Placements[0].PartId="changed";CollectionAssert.AreEqual(new[]{"a"},sources.Accessories);CollectionAssert.AreEqual(new[]{"p","q"},sources.Parts.Select(v=>v.InstanceId));Assert.IsTrue(sources.Parts.All(v=>v.PartId=="dot"));}
  [Test] public void 供給元は不整合な装備配置を能力登録先へ渡さない(){var s=State();s.Placements[1].Y=0;Assert.Throws<ArgumentException>(()=>new CustomizationLoadoutSources(Catalog(),s));}
  GameObject root;CustomizationUnlockView view;int applies,cancels,retries;string applied;
  void Open(){root=new GameObject("UnlockFixture",typeof(RectTransform),typeof(Canvas));var go=new GameObject("Unlock",typeof(RectTransform));go.transform.SetParent(root.transform,false);view=go.AddComponent<CustomizationUnlockView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),id=>{applies++;applied=id;},()=>{cancels++;view.Close();},()=>retries++);view.Open(Catalog(),State());}
  Button B(string name)=>view.GetComponentsInChildren<Button>().Single(v=>v.name==name);
  [TearDown] public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
  [Test] public void 選択と取消では解放せず確定だけが固定アイテムIDを渡す(){Open();Assert.IsFalse(B("ApplyCustomizationUnlock").interactable);var old=B("UnlockItem_right");old.onClick.Invoke();Assert.AreEqual(0,applies);Assert.IsTrue(B("ApplyCustomizationUnlock").interactable);old.onClick.Invoke();B("ApplyCustomizationUnlock").onClick.Invoke();Assert.AreEqual("right",applied);Assert.AreEqual(1,applies);B("CancelCustomizationUnlock").onClick.Invoke();Assert.AreEqual(1,cancels);Assert.DoesNotThrow(()=>old.onClick.Invoke());}
  [Test] public void 固定領域の表示は任意マス操作を持たず適用後は再確定を拒否する(){Open();B("UnlockItem_right").onClick.Invoke();Assert.IsFalse(view.GetComponentsInChildren<Button>().Any(v=>v.name.StartsWith("UnlockCell")));var state=State();state.AppliedUnlockItemIds=new[]{"right"};view.Applied(state);Assert.IsFalse(B("ApplyCustomizationUnlock").interactable);Assert.AreEqual(2,state.OwnedUnlockItems.Single().Count);}
  [Test] public void 保存保留は選択取消を遮断し同じ保存の再試行へ進む(){Open();B("UnlockItem_right").onClick.Invoke();view.SaveFailed(true);B("CancelCustomizationUnlock").onClick.Invoke();B("UnlockItem_right").onClick.Invoke();Assert.AreEqual(0,cancels);Assert.AreEqual(0,applies);B("RetryCustomizationUnlock").onClick.Invoke();Assert.AreEqual(1,retries);}
 }
}
