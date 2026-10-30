using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Customization;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Dungeon;

namespace Olca.Tests
{
    public class UiOwnershipRefactorTests
    {
        [TestCase("ひかりフォーム")][TestCase("フォームA-1")][TestCase("あいうアイウ123!")]
        public void 変身名は漢字以外の元表記を登録時に保持する(string name)
        {
            var root=new GameObject("TransformationName",typeof(RectTransform));
            try{var view=root.AddComponent<CustomScreenView>();view.ConfigureTransformationVisuals(new[]{new TransformationVisualEntry{Id="fixture",DisplayNameKana=name}});Assert.AreEqual(name,view.GetTransformationName("fixture"));Assert.Throws<ArgumentException>(()=>view.ConfigureTransformationVisuals(new[]{new TransformationVisualEntry{Id="fixture",DisplayNameKana="変身"}}));}
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test]
        public void カスタム再初期化と破棄通知は旧モデルの全購読を解除し同じモデルへ重複登録しない()
        {
            var root=new GameObject("CustomSubscription",typeof(RectTransform));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var first=new CustomScreenSystem(new Deck(new List<Card>()));var second=new CustomScreenSystem(new Deck(new List<Card>()));
            var names=new[]{"OnScreenOpened","OnScreenClosed","OnHandChanged","OnConfirmed","OnExceeded","OnTuned","OnFused","OnSelectionFailed","OnCustomizationUndone"};
            try{
                var view=root.AddComponent<CustomScreenView>();
                var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirm.transform.SetParent(root.transform,false);
                var hand=new GameObject("Hand",typeof(RectTransform));hand.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,confirm.GetComponent<Button>());
                typeof(CustomScreenView).GetField("handCardContainer",flags).SetValue(view,hand.transform);
                typeof(CustomScreenView).GetField("handCardPrefab",flags).SetValue(view,UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab"));
                int Count(CustomScreenSystem model,string name)=>(typeof(CustomScreenSystem).GetField(name,flags).GetValue(model) as Delegate)?.GetInvocationList().Count(v=>ReferenceEquals(v.Target,view))??0;
                view.Initialize(first);view.Initialize(first);foreach(string name in names)Assert.AreEqual(1,Count(first,name),name);
                view.Initialize(second);foreach(string name in names){Assert.AreEqual(0,Count(first,name),name);Assert.AreEqual(1,Count(second,name),name);}
                // 子が先に破棄された場合にも、終了処理で表示を作り直さない。
                view.BindTransformations(null);
                var status=root.transform.Find("CustomTransformationStatus");if(status!=null)UnityEngine.Object.DestroyImmediate(status.gameObject);
                // EditModeではライフサイクル通知を明示する。Unityの実破棄は別途Playで検証する。
                typeof(CustomScreenView).GetMethod("OnDestroy",flags).Invoke(view,null);
                foreach(string name in names)Assert.AreEqual(0,Count(second,name),name);
                Assert.IsNull(root.transform.Find("CustomTransformationStatus"));
            }finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);Olca.Unity.BattleVisualClock.Refresh();}
        }
        [Test]
        public void 受取画面の再生成は指定フォントと配置を保持し古いボタンから確定しない()
        {
            var root=new GameObject("AcquisitionRebuild",typeof(RectTransform),typeof(Canvas));
            try{
                var panel=new GameObject("Panel",typeof(RectTransform));panel.transform.SetParent(root.transform,false);var view=panel.AddComponent<CustomizationAcquisitionView>();
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");int received=0;view.Initialize(font,_=>received++,()=>view.Close(),()=>{});
                string id=Guid.NewGuid().ToString("N");var grant=new CustomizationGrant(id,parts:new Dictionary<string,int>{{"part",1}});
                var plan=new CustomizationAcquisitionDefinition("test",1,new[]{grant},new Dictionary<string,string>{{id,"テスト"}}).Snapshot;
                view.Open(plan,new CustomizationProgress());
                Button Find(string name)=>view.GetComponentsInChildren<Button>().Single(v=>v.name==name);
                var old=Find("AcquisitionCandidate_"+id);// 左側候補列の新配置を検証し、フォントと旧入力の寿命の期待値は維持する。
                Assert.AreEqual(new Vector2(620,80),old.GetComponent<RectTransform>().sizeDelta);Assert.AreEqual(new Vector2(-620,255),old.GetComponent<RectTransform>().anchoredPosition);old.onClick.Invoke();
                Assert.IsTrue(view.GetComponentsInChildren<Text>().All(v=>v.font==font));Assert.DoesNotThrow(()=>old.onClick.Invoke());Assert.AreEqual(0,received);
                Find("ClaimCustomizationAcquisition").onClick.Invoke();Assert.AreEqual(1,received);
                view.Close();Assert.DoesNotThrow(()=>old.onClick.Invoke());Assert.AreEqual(1,received);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
