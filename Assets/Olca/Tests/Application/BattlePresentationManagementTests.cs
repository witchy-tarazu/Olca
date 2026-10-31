using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Olca.Application.Management;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Presentation.Management;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class BattlePresentationManagementTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
        [Test] public void Prefab_外部Scene参照なしでBattleCustomの表示と解放を同じleaseへ束縛する()
        {
            var root=new GameObject("BattlePresentationHost");var resources=new ResourceManager();var dialogs=new DialogManager(()=>Time.frameCount);var views=new ViewManager(resources,dialogs);
            try {
                var binding=new BattlePresentationBinding(new object(),resources,views,root.transform);var battle=binding.Battle;var custom=binding.Custom;
                Assert.NotNull(battle);Assert.NotNull(custom);Assert.IsTrue(custom.transform.IsChildOf(battle.transform));Assert.IsFalse(battle.gameObject.activeSelf);Assert.AreEqual(Vector3.one,battle.transform.localScale);
                Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.AreEqual("battle-custom",views.GetLayer("battle").CurrentKey);Assert.AreEqual(10,((Transform[])Get(custom,"cardPositionMarkers")).Length);
                foreach(var field in new[]{"handCardContainer","rightWall","topWall","bottomWall","confirmButton","costDisplay","selectedCardImage","selectedCardDescription","cursorIndicator","screenCanvasGroup","customizeModeView","customizeModeOverlay"}){
                    var value=Get(custom,field);Assert.NotNull(value,field);var obj=value as GameObject??(value as Component)?.gameObject;Assert.NotNull(obj,field);Assert.IsTrue(obj.transform.IsChildOf(battle.transform),field);
                }
                Assert.NotNull(Get(custom,"handCardPrefab"));Assert.NotNull(Get(battle,"playerHpText"));
                binding.Dispose();binding.Dispose();Assert.IsNull(binding.Battle);Assert.IsNull(binding.Custom);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,views.GetLayer("battle").RegisteredCount);Assert.IsNull(views.GetLayer("battle").CurrentKey);Assert.IsTrue(battle==null);Assert.IsTrue(custom==null);
                var next=new BattlePresentationBinding(new object(),resources,views,root.transform);Assert.NotNull(next.Custom);next.Dispose();
            } finally {views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Release_遅延Destroyを待たずCustomモデル購読入力と確認delegateを解除する()
        {
            var prefab=Resources.Load<GameObject>("OlcaSystem/BattlePresentation");var root=UnityEngine.Object.Instantiate(prefab);var custom=root.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomScreenView>(true);
            try {
                var model=new CustomScreenSystem(new Deck(new List<Card>()));custom.Initialize(model);custom.ConfigureLogicalConfirmation((time,action)=>action(),()=>0);
                var input=Get(custom,"gamepadInput");Assert.NotNull(input);var opened=(Delegate)Get(model,"OnScreenOpened");Assert.IsTrue(opened.GetInvocationList().Any(d=>ReferenceEquals(d.Target,custom)));
                custom.ReleaseSessionPresentation();custom.ReleaseSessionPresentation();Assert.IsNull(Get(custom,"customScreen"));Assert.IsNull(Get(custom,"gamepadInput"));Assert.IsNull(Get(custom,"queueConfirmation"));Assert.IsNull(Get(custom,"pointerClickTimestamp"));Assert.IsTrue((bool)Get(input,"disposed"));
                foreach(var name in new[]{"OnScreenOpened","OnScreenClosed","OnHandChanged","OnConfirmed","OnExceeded","OnTuned","OnFused","OnCustomizationUndone","OnSelectionFailed"})Assert.IsFalse(((Delegate)Get(model,name))?.GetInvocationList().Any(d=>ReferenceEquals(d.Target,custom))??false,name);
                var mode=Get(custom,"customizeModeView");foreach(var name in new[]{"OnExecuteRequested","OnCancelled"})Assert.IsFalse(((Delegate)Get(mode,name))?.GetInvocationList().Any(d=>ReferenceEquals(d.Target,custom))??false,name);
                Assert.AreEqual(0,model.SelectedCards.Count);
                var fresh=UnityEngine.Object.Instantiate(prefab);try{var untouched=fresh.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomScreenView>(true);int count=untouched.GetComponentsInChildren<Transform>(true).Length;fresh.GetComponent<Olca.Presentation.Battle.BattleView>().ReleaseSessionPresentation();untouched.ReleaseSessionPresentation();Assert.AreEqual(count,untouched.GetComponentsInChildren<Transform>(true).Length,"未初期化Viewの返却で子UIを生成しない");}finally{UnityEngine.Object.DestroyImmediate(fresh);}
            } finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
