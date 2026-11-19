using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Olca.Application.Management;
using Olca.Presentation.Management;
using Olca.Unity;

namespace Olca.Tests.Application
{
    public class GameSystemManagementTests
    {
        private static IEnumerable<object[]> 探索のけぞり表示行列()
        {foreach(bool fire in new[]{false,true})foreach(bool airborne in new[]{false,true})foreach(int facing in new[]{-1,1})foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1440,1080)})yield return new object[]{fire,airborne,facing,size.x,size.y};}
        [TestCaseSource(nameof(探索のけぞり表示行列))]
        public async Task 探索のけぞりは既存三姿勢を論理状態から読み左右空中神依と再表示で時計を変えない(bool fire,bool airborne,int facing,int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;
            model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,staggerDurationFrames:18));model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var field=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);model.TickTraversal(facing,airborne,airborne);Assert.AreEqual(facing,model.Core.Facing);Assert.IsTrue(model.Player.TryTakeDamage(1,forceStagger:true));
                for(int frame=0;frame<=18;frame++){
                    if(frame>0)model.TickTraversal(0);if(frame!=0&&frame!=5&&frame!=6&&frame!=11&&frame!=12&&frame!=17&&frame!=18)continue;
                    var view=binding.View;long before=model.Core.Frame;int remaining=model.Player.StaggerRemainingFrames;var state=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.Render();Canvas.ForceUpdateCanvases();var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();
                    if(frame<18){int pose=frame<6?0:frame<12?1:2;var expected=field.AuthoredStaggerSprite(pose*6);Assert.NotNull(expected);if(fire)expected=Resources.Load<Sprite>("OlcaCharacterMotion/TrialFire/"+expected.name);Assert.AreSame(expected,image.sprite,"frame="+frame);Assert.AreEqual(-facing,image.rectTransform.localScale.x);Assert.AreEqual(expected.pivot/expected.rect.size,image.rectTransform.pivot);Assert.AreEqual(expected.bounds.size.x*1080f/(2*resources.GetShared<GameObject>("OlcaSystem/GameplayCamera").GetComponent<Camera>().orthographicSize),image.rectTransform.sizeDelta.x,.01f);}
                    else StringAssert.DoesNotContain("knockback_",image.sprite.name);
                    view.SetCovered(true);view.Render();view.SetCovered(false);view.Render();Assert.AreEqual(before,model.Core.Frame);Assert.AreEqual(remaining,model.Player.StaggerRemainingFrames);CollectionAssert.AreEqual(state,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(99,model.Player.CurrentHp);
                    if(frame==6){var expectedReopen=image.sprite;views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.Render();Assert.AreSame(expectedReopen,binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite);Assert.AreEqual(remaining,model.Player.StaggerRemainingFrames);}
                }
            }
        }
        [TestCase(1920,1080)][TestCase(3840,2160)][TestCase(1440,1080)]
        public async Task 別マップリロード案内はHome限定callbackに依存せず編成内だけ手札を隠し再描画で進行しない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);PrepareSwordWall347("sword",false,1);var model=system.OutsideExploration;model.QueueSelectedDeck(null,null,null,null,out _);
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>false)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                foreach(long x in new[]{984000L,990000L,1020000L,1050000L,1056000L}){
                    var state=model.Core.CaptureState();state.Position=x;state.Height=0;state.Velocity=0;state.SupportId="floor";typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});long frame=model.Core.Frame,units=model.Core.CardGauge.CurrentUnits;view.Render();Canvas.ForceUpdateCanvases();
                    var point=view.transform.Find("OutsideReloadPoint").GetComponent<RectTransform>();Assert.IsTrue(point.gameObject.activeSelf);Assert.IsFalse(point.GetComponent<Image>().raycastTarget);var text=point.GetComponentInChildren<Text>();StringAssert.Contains("リロード（カリ）",text.text);bool editing=x>=990000&&x<=1050000;Assert.AreEqual(editing,model.IsDeckEditingLocation);Assert.AreEqual(!editing,view.transform.Find("OutsideHandButton0").gameObject.activeSelf);
                    var gen=new TextGenerator();Assert.IsTrue(gen.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size)));Assert.AreEqual(text.text.Length,gen.characterCountVisible);((IDisposable)gen).Dispose();var position=point.anchoredPosition;view.Render();Assert.AreEqual(position,point.anchoredPosition);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(units,model.Core.CardGauge.CurrentUnits);
                }
            }
        }
        [TestCase(1920,1080,false)][TestCase(1920,1080,true)][TestCase(3840,2160,false)][TestCase(3840,2160,true)][TestCase(1440,1080,false)][TestCase(1440,1080,true)]
        public async Task 別マップ任意AIの二行案内は接触登録に依存せず実フォントの全文が板内に収まる(int width,int height,bool contact)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);
            using(var model=new Olca.Core.Logic.Integration.OutsideExplorationSession(contact)){
                model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);
                Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
                using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                    for(int mode=0;mode<2;mode++){
                        if(mode==1)Assert.IsTrue(model.SetTrialEnemyAttack(true));view.Render();Canvas.ForceUpdateCanvases();
                        var text=view.transform.Find("OutsideTrialTargetName").GetComponent<Text>();var plate=(RectTransform)view.transform.Find("OutsideTrialTargetNamePlate");StringAssert.Contains(mode==1?"AIコウゲキON":"AIコウゲキOFF",text.text);
                        var generator=new TextGenerator();Assert.IsTrue(generator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size)));Assert.AreEqual(text.text.Length,generator.characterCountVisible);Assert.AreEqual(2,generator.lineCount);
                        foreach(var vertex in generator.verts){float y=vertex.position.y/text.pixelsPerUnit;Assert.That(y,Is.InRange(plate.rect.yMin+2,plate.rect.yMax-2));}
                        ((System.IDisposable)generator).Dispose();Assert.LessOrEqual(text.preferredHeight,text.rectTransform.rect.height);Assert.Less(text.rectTransform.rect.height,plate.rect.height);
                    }
                }
            }
        }
        [Test] public async Task 別マップ任意AIの表示とボタンは覆い中と標的撃破後に操作を拒否する()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);binding.View.ConfigureCombatFeedback(new Olca.Presentation.Effects.CombatNumberStyle(resources.GetShared<Font>("OlcaUI/MenuText"),FontStyle.Normal,Color.white,.2f,.01f,1,1));binding.View.Render();
                var button=binding.View.transform.Find("OutsideTrialTargetNamePlate").GetComponent<Button>();var label=binding.View.transform.Find("OutsideTrialTargetName").GetComponent<Text>();
                Assert.IsTrue(button.interactable);StringAssert.Contains("AIコウゲキOFF",label.text);button.onClick.Invoke();binding.View.Render();StringAssert.Contains("AIコウゲキON",label.text);
                button.onClick.Invoke();binding.View.Render();StringAssert.Contains("AIコウゲキOFF",label.text);var old=button.onClick;
                binding.View.SetCovered(true);old.Invoke();binding.View.SetCovered(false);binding.View.Render();StringAssert.Contains("AIコウゲキOFF",label.text);
                model.TrialTarget.TakeDamage(120,Olca.Core.Domain.ElementType.None);binding.View.Render();Assert.IsFalse(button.interactable);
            }
        }
        [TestCase(1920,1080,false)][TestCase(1920,1080,true)][TestCase(3840,2160,false)][TestCase(3840,2160,true)][TestCase(1440,1080,false)][TestCase(1440,1080,true)]
        public async Task 試用敵の直前予告は既存十二フレームだけ表示し再描画では進行しない(int width,int height,bool annex)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);
            using(var model=new Olca.Core.Logic.Integration.OutsideExplorationSession()){
                model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(annex)model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);
                Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
                using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.SetTrialEnemyAttack(true));var view=binding.View;
                    var label=view.transform.Find("OutsideTrialTargetName").GetComponent<Text>();
                    for(int elapsed=0;elapsed<=60;elapsed++){
                        long frame=model.Core.Frame;view.Render();view.Render();Assert.AreEqual(frame,model.Core.Frame);
                        bool warning=elapsed>=48&&elapsed<60;Assert.AreEqual(warning,label.text.Contains("チョクゼン"),"elapsed="+elapsed);
                        if(warning){foreach(char glyph in label.text){if(!char.IsWhiteSpace(glyph))Assert.IsTrue(label.font.HasCharacter(glyph),"欠字: "+glyph);}Canvas.ForceUpdateCanvases();var generator=new TextGenerator();Assert.IsTrue(generator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size)));Assert.AreEqual(label.text.Length,generator.characterCountVisible);Assert.AreEqual(annex?2:1,generator.lineCount);((System.IDisposable)generator).Dispose();}
                        if(elapsed<60)model.TickTraversal(0);
                    }
                    Assert.IsTrue(model.SetTrialEnemyAttack(false));view.Render();StringAssert.Contains("コウゲキOFF",label.text);
                }
            }
        }
        [TestCase(1920,1080,false)][TestCase(1920,1080,true)][TestCase(3840,2160,false)][TestCase(3840,2160,true)][TestCase(1440,1080,false)][TestCase(1440,1080,true)]
        public async Task 外界現在神依HUDは固定待機の顔と名称を三比率で表示し移動や覆いで状態を変更しない(int width,int height,bool fire)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;
            model.TryInitializePlayer(()=>{var actor=new Olca.Core.Domain.PlayerCharacter(100);if(fire)actor.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);return actor;});
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;int calls=0;view.ConfigureKamuiShortcut(()=>calls++);view.Render();Canvas.ForceUpdateCanvases();
                var button=view.transform.Find("OutsideKamuiShortcut").GetComponent<Button>();var label=button.GetComponentInChildren<Text>();var mask=(RectTransform)button.transform.Find("OutsideKamuiPortraitMask");var face=mask.Find("OutsideKamuiPortraitImage").GetComponent<Image>();
                StringAssert.Contains(fire?"カエン（カリ）":"ツウジョウ",label.text);Assert.IsNotNull(face.sprite);Assert.IsTrue(face.enabled);Assert.IsFalse(face.raycastTarget);Assert.IsFalse(mask.GetComponent<Image>().raycastTarget);Assert.IsNotNull(mask.GetComponent<RectMask2D>());Assert.AreEqual(new Vector2(240,90),((RectTransform)button.transform).sizeDelta);
                if(fire)StringAssert.StartsWith("trial_fire_idle",face.sprite.name);else StringAssert.StartsWith("heroine_idle_",face.sprite.name);
                var generator=new TextGenerator();Assert.IsTrue(generator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size)));Assert.AreEqual(label.text.Length,generator.characterCountVisible);Assert.AreEqual(2,generator.lineCount);foreach(char glyph in label.text)if(!char.IsWhiteSpace(glyph))Assert.IsTrue(label.font.HasCharacter(glyph));((IDisposable)generator).Dispose();
                Assert.Less(mask.anchoredPosition.x+mask.rect.width/2,label.rectTransform.anchoredPosition.x-label.rectTransform.rect.width/2);Assert.LessOrEqual(label.rectTransform.anchoredPosition.x+label.rectTransform.rect.width/2,120);Assert.GreaterOrEqual(mask.anchoredPosition.x-mask.rect.width/2,-120);
                var portrait=face.sprite;var actor=model.Player;long frameBefore=model.Core.Frame,gauge=model.Core.CardGauge.CurrentUnits;view.Render();view.Render();Assert.AreSame(actor,model.Player);Assert.AreEqual(frameBefore,model.Core.Frame);Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);
                model.TickTraversal(-1,true);view.Render();Assert.AreSame(portrait,face.sprite);Assert.AreEqual(Vector3.one,face.rectTransform.localScale);view.SetCovered(true);view.Render();Assert.IsFalse(button.interactable);Assert.IsTrue(face.enabled);Assert.AreEqual(0,calls);
                view.ConfigureKamuiShortcut(null);Assert.IsFalse(button.gameObject.activeSelf);view.Hide();Assert.IsNull(face.sprite);Assert.IsFalse(face.enabled);
            }
        }
        [Test] public async Task 神依HUDボタンは未設定や覆いと旧callbackから起動せず再表示で新しい所有へ結び直す()
        {
            system.OutsideExploration.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var button=view.transform.Find("OutsideKamuiShortcut").GetComponent<Button>();Assert.IsFalse(button.gameObject.activeSelf);int calls=0;
                view.ConfigureKamuiShortcut(()=>calls++);Assert.IsTrue(button.gameObject.activeSelf);var old=button.onClick;old.Invoke();Assert.AreEqual(1,calls);
                view.SetCovered(true);view.Render();Assert.IsFalse(button.interactable);old.Invoke();Assert.AreEqual(1,calls);view.SetCovered(false);
                view.ConfigureKamuiShortcut(null);Assert.IsFalse(button.gameObject.activeSelf);old.Invoke();Assert.AreEqual(1,calls);
                views.GetLayer("return-hub").CloseCurrent();old.Invoke();Assert.AreEqual(1,calls);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);view=binding.View;view.ConfigureKamuiShortcut(()=>calls+=10);old.Invoke();Assert.AreEqual(1,calls);view.transform.Find("OutsideKamuiShortcut").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(11,calls);
            }
        }
        [TestCase(1920,1080)][TestCase(3840,2160)][TestCase(1440,1080)]
        public async Task 神依HUDボタンは三比率で画面内に収まり本体と既存上部操作へ重ならない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);system.OutsideExploration.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.ConfigureKamuiShortcut(()=>{});view.Render();Canvas.ForceUpdateCanvases();var root=(RectTransform)view.transform;
                Rect Bounds(string name){var rect=(RectTransform)view.transform.Find(name);var corners=new Vector3[4];rect.GetWorldCorners(corners);var min=root.InverseTransformPoint(corners[0]);var max=min;foreach(var corner in corners){var point=root.InverseTransformPoint(corner);min=Vector3.Min(min,point);max=Vector3.Max(max,point);}return Rect.MinMaxRect(min.x,min.y,max.x,max.y);}
                var button=Bounds("OutsideKamuiShortcut");Assert.GreaterOrEqual(button.xMin,root.rect.xMin);Assert.LessOrEqual(button.xMax,root.rect.xMax);Assert.GreaterOrEqual(button.yMin,root.rect.yMin);Assert.LessOrEqual(button.yMax,root.rect.yMax);
                foreach(var other in new[]{"PauseOutsideExploration","CloseOutsideExploration","OutsideEntryPlayer"})Assert.IsFalse(button.Overlaps(Bounds(other)),other);
                var label=view.transform.Find("OutsideKamuiShortcut").GetComponentInChildren<Text>();var generator=new TextGenerator();Assert.IsTrue(generator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size)));Assert.AreEqual(label.text.Length,generator.characterCountVisible);((IDisposable)generator).Dispose();
            }
        }
        [TestCase(false)][TestCase(true)] public async Task 神依HUDのポインタ操作は既存ホイールを停止所有し確定失敗と取消では効果を作らない(bool cancel)
        {
            system.OutsideExploration.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var panel=new GameObject("HudWheelTrial",typeof(RectTransform));panel.transform.SetParent(host.transform,false);var wheel=panel.AddComponent<OutsideKamuiWheelDialog>();wheel.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"));int calls=0;bool success=false,paused=false;
                try{wheel.ConfigureChoices(new[]{"ツウジョウ","カエン（カリ）"},0,index=>{Assert.AreEqual(1,index);calls++;return success;});view.ConfigureKamuiShortcut(()=>Assert.IsTrue(wheel.OpenMenu(dialogs,binding.Context,()=>binding.IsOpen,()=>true,value=>paused=value,"タップ: カクテイ / モドる: トジル")));
                    var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,pointerId=1};ExecuteEvents.Execute<IPointerClickHandler>(view.transform.Find("OutsideKamuiShortcut").gameObject,pointer,ExecuteEvents.pointerClickHandler);Assert.IsTrue(wheel.IsOpen);Assert.IsTrue(paused);Assert.AreEqual(1,dialogs.ActiveCount);
                    if(cancel){ExecuteEvents.Execute<IPointerClickHandler>(panel.transform.Find("KamuiWheelRelease").gameObject,pointer,ExecuteEvents.pointerClickHandler);Assert.AreEqual(0,calls);}
                    else{ExecuteEvents.Execute<IPointerClickHandler>(panel.transform.Find("KamuiWheelAlternate").gameObject,pointer,ExecuteEvents.pointerClickHandler);Assert.AreEqual(1,calls);Assert.IsTrue(wheel.IsOpen);Assert.IsTrue(paused);success=true;frame++;wheel.RefreshMenu();ExecuteEvents.Execute<IPointerClickHandler>(panel.transform.Find("KamuiWheelAlternate").gameObject,pointer,ExecuteEvents.pointerClickHandler);Assert.AreEqual(2,calls);}
                    Assert.IsFalse(wheel.IsOpen);Assert.IsFalse(paused);Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual(100,system.OutsideExploration.Player.CurrentHp);
                }finally{wheel.Abort();UnityEngine.Object.DestroyImmediate(panel);}
            }
        }
        private static IEnumerable<object[]> 蹴り壁表示の行列()
        {
            foreach(int stage in new[]{0,1,2,3})foreach(int facing in new[]{-1,1})foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1440,1080)})yield return new object[]{stage,facing,size.x,size.y};
        }
        [TestCaseSource(nameof(蹴り壁表示の行列))]
        public async Task 蹴り軌跡は三段空中左右と三比率で寸法を変えず壁先だけを除く(int stage,int facing,int width,int height)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);PrepareSwordWall347("sword",stage==3,facing);var model=system.OutsideExploration;model.EnableTrialNormalKick();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);
                for(int n=0;n<(stage==3?0:stage);n++){Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,model.Core.Frame+1,out _));for(int k=0;k<21;k++)model.TickTraversal(0);}
                Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,model.Core.Frame+1,out var error),error);for(int n=0;n<6;n++)model.TickTraversal(0);var view=binding.View;view.Render();
                var image=view.transform.Find("OutsideProvisionalKickTrail").GetComponent<Image>();Assert.IsTrue(image.enabled);var clip=image.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>();Assert.NotNull(clip);Assert.IsTrue(clip.enabled);
                var wall=(RectTransform)view.transform.Find("OutsideValidationWall-"+model.Core.TraversalProfile.Walls.Single().Id);float edge=view.transform.InverseTransformPoint(wall.position).x;var size=image.rectTransform.sizeDelta;var sprite=image.sprite;var color=image.color;
                using(var vh=new VertexHelper()){
                    typeof(Image).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null).Invoke(image,new object[]{vh});var original=new List<UIVertex>();vh.GetUIVertexStream(original);Assert.IsTrue(original.Any(v=>facing*(view.transform.InverseTransformPoint(image.transform.TransformPoint(v.position)).x-edge)>.1f));clip.ModifyMesh(vh);var clipped=new List<UIVertex>();vh.GetUIVertexStream(clipped);Assert.Greater(clipped.Count,0);Assert.IsTrue(clipped.All(v=>facing*(view.transform.InverseTransformPoint(image.transform.TransformPoint(v.position)).x-edge)<=.01f));
                }
                var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int n=0;n<3;n++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreSame(sprite,image.sprite);Assert.AreEqual(color,image.color);Assert.AreEqual(120,model.TrialTarget.CurrentHp);Assert.AreEqual(120,model.SecondTrialTarget.CurrentHp);Assert.IsNull(view.transform.Find("OutsideEntryPlayer").GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>());
                model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorHomePoint);view.Render();Assert.IsFalse(clip.enabled);Assert.IsFalse(image.enabled);
            }
        }
        [TestCase(71999,true)][TestCase(72000,true)][TestCase(72001,false)]
        public async Task 蹴り軌跡の有限壁は現在の命中中心高と同じ閉区間で切取りを解除する(long bottom,bool blocked)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);PrepareSwordWall347("sword",false,1,60000000,bottom);var model=system.OutsideExploration;model.EnableTrialNormalKick();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out _));for(int n=0;n<6;n++)model.TickTraversal(0);binding.View.Render();var image=binding.View.transform.Find("OutsideProvisionalKickTrail").GetComponent<Image>();Assert.AreEqual(blocked,image.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>()?.enabled==true);
            }
        }
        [TestCase(1,1f,false)][TestCase(-1,1f,false)][TestCase(1,2f,false)][TestCase(-1,2f,false)][TestCase(1,1f,true)][TestCase(-1,2f,true)]
        public void ソード壁表示の切取は左右拡大でもUVと元寸法を維持し解除で全幅へ戻る(int facing,float scale,bool negativeParent)
        {
            var root=new GameObject("clip-space",typeof(RectTransform));root.transform.localScale=new Vector3(negativeParent?-1:1,1,1);var go=new GameObject("clip-image",typeof(RectTransform),typeof(Image));go.transform.SetParent(root.transform,false);var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(4,2);rect.localScale=new Vector3(facing*scale,1,1);var clip=go.AddComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>();
            using(var vh=new VertexHelper())try{
                Action quad=()=>{vh.Clear();for(int n=0;n<4;n++){var v=UIVertex.simpleVert;v.position=new Vector3(n<2?-2:2,n==0||n==3?-1:1);v.uv0=new Vector4(n<2?0:1,n==0||n==3?0:1);v.color=new Color32(20,40,60,200);vh.AddVert(v);}vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);};
                quad();clip.SetClip(root.transform,facing,facing*scale);clip.ModifyMesh(vh);var vertices=new List<UIVertex>();vh.GetUIVertexStream(vertices);Assert.Greater(vertices.Count,0);Assert.IsTrue(vertices.All(v=>facing*(root.transform.InverseTransformPoint(go.transform.TransformPoint(v.position)).x-facing*scale)<=.0001f));Assert.IsTrue(vertices.All(v=>v.uv0.x<=.75001f));Assert.IsTrue(vertices.Any(v=>Mathf.Abs(v.uv0.x-.75f)<.0001f));Assert.IsTrue(vertices.Any(v=>v.uv0.x==0));Assert.IsTrue(vertices.All(v=>v.color.Equals(new Color32(20,40,60,200))));Assert.AreEqual(new Vector2(4,2),rect.sizeDelta);
                clip.ClearClip();quad();clip.ModifyMesh(vh);vertices.Clear();vh.GetUIVertexStream(vertices);Assert.AreEqual(6,vertices.Count);Assert.IsTrue(vertices.Any(v=>v.uv0.x==1));Assert.AreEqual(new Vector2(4,2),rect.sizeDelta);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [TestCase(false)][TestCase(true)]
        public async Task ソード壁表示は発動後の落下で有限壁の上下端を跨いでも発動時の面を保つ(bool raisedWall)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);PrepareSwordWall347("sword",true,1,raisedWall?200000:120000,raisedWall?120000:0);var model=system.OutsideExploration;
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);long start=model.Core.Frame;Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out _));for(int n=0;n<8;n++)model.TickTraversal(0);var view=binding.View;var lease=(Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease)view.GetType().GetField("presentation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);var strike=lease.TrialStrike;Assert.AreEqual(raisedWall,lease.FirstSwordBlockingWall(strike.X,strike.Y,strike.Facing,strike.Reach).HasValue);bool crossed=false;int safety=0;
                while(model.Core.Frame<start+20){
                    view.Render();var image=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();Assert.IsTrue(image.enabled);Assert.AreEqual(raisedWall,image.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>()?.enabled==true);
                    crossed|=lease.FirstSwordBlockingWall(model.Core.Position,model.Core.Height,model.Core.Facing,strike.Reach).HasValue!=raisedWall;model.TickTraversal(0);Assert.Less(++safety,100);
                }
                Assert.IsTrue(crossed,"現在高度だけで判定すれば結果が変わる境界を実際に通る");Assert.AreEqual(raisedWall?120:90,model.SecondTrialTarget.CurrentHp);view.Render();Assert.IsFalse(view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>().enabled);
            }
        }
        [Test] public async Task ソード壁表示はHomeと壁を背にした次の攻撃へ切取状態を持ち越さない()
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);PrepareSwordWall347("sword",false,1);var model=system.OutsideExploration;
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out _));for(int n=0;n<8;n++)model.TickTraversal(0);var view=binding.View;view.Render();var image=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();var clip=image.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>();Assert.IsTrue(clip.enabled);
                foreach(bool home in new[]{true,false}){
                    model.RecoverOutside(home?Olca.Core.Logic.Integration.OutsideTrialRoute.DoorHomePoint:Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);view.Render();Assert.IsFalse(clip.enabled);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id=="sword");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));var state=model.Core.CaptureState();state.Position=23*60000;state.Facing=home?1:-1;state.Moving=false;state.MovementFrame=0;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<8;n++)model.TickTraversal(0);view.Render();Assert.IsTrue(image.enabled);Assert.IsFalse(clip.enabled);Assert.AreEqual(home?120:90,model.TrialTarget.CurrentHp);
                }
            }
        }
        private void PrepareSwordWall347(string id,bool air,int facing,long top=60000000,long bottom=0,Olca.Core.Domain.ExceedLevel level=Olca.Core.Domain.ExceedLevel.Normal)
        {
            var model=system.OutsideExploration;var p=Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true);Assert.IsTrue(model.TryConfigureTraversal(new Olca.Core.Logic.Integration.OutsideTraversalProfile("sword-wall347",p.DashSpeedPerSecond,p.DashFrames,p.DashCooldownFrames,p.DashResetFrames,p.DashJumpSpeedPerSecond,p.BodyHalfWidth,p.BodyHeight,p.WallSlidePerFrame,p.WallKickVelocity,p.WallKickSpeedPerSecond,p.WallKickPushFrames,p.WallKickMotionFrames,true,new Olca.Core.Logic.Integration.OutsideValidationWall("wall347",24*60000,bottom,top))));model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var factory=new Olca.Application.Battle.SimpleCardFactory();var source=factory.CreateTrialInitialLoadout().Outside.ToArray();if(id=="fire_sword")source[0]=factory.CreateById(id);if(level!=Olca.Core.Domain.ExceedLevel.Normal){var card=factory.CreateById("sword");for(int n=1;n<=(int)level;n++)card=factory.CreateExceeded(card,(Olca.Core.Domain.ExceedLevel)n);source[0]=card;}Assert.IsTrue(model.QueueSelectedDeck("sword-wall347",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(347),out _));
            using(var boot=model.AttachPresentation())while(model.Core.Position<10*60000)model.TickTraversal(1);model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id&&c.ExceedLevel==level);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id&&c.ExceedLevel==level)));var state=model.Core.CaptureState();state.Position=(facing==1?23:25)*60000;state.Facing=facing;state.Moving=false;state.MovementFrame=0;if(air){state.Height=60000;state.SupportId=null;state.Velocity=0;}typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});model.EnableTrialCardPolish();
        }
        private static IEnumerable<object[]> ソード壁表示の実定義行列()
        {
            foreach(string id in new[]{"sword","fire_sword",Olca.Core.Domain.TrialInitialCardDefinitions.Spear,Olca.Core.Domain.TrialInitialCardDefinitions.WideSword})foreach(bool air in new[]{false,true})foreach(int facing in new[]{-1,1})foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1440,1080)})yield return new object[]{id,air,facing,size.x,size.y,Olca.Core.Domain.ExceedLevel.Normal};
            foreach(var level in new[]{Olca.Core.Domain.ExceedLevel.Lv1,Olca.Core.Domain.ExceedLevel.Lv2})foreach(bool air in new[]{false,true})foreach(int facing in new[]{-1,1})foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1440,1080)}){if(air&&level==Olca.Core.Domain.ExceedLevel.Lv2)continue;yield return new object[]{"sword",air,facing,size.x,size.y,level};}
        }
        [TestCaseSource(nameof(ソード壁表示の実定義行列))]
        public async Task ソード壁表示は四種左右地上空中と三比率で元の幅を保ち壁先だけを除く(string id,bool air,int facing,int width,int height,Olca.Core.Domain.ExceedLevel level)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);PrepareSwordWall347(id,air,facing,level:level);var model=system.OutsideExploration;
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<8;n++)model.TickTraversal(0);var view=binding.View;view.Render();var image=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();Assert.IsTrue(image.enabled);var clip=image.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>();Assert.NotNull(clip);Assert.IsTrue(clip.enabled);
                var wall=(RectTransform)view.transform.Find("OutsideValidationWall-"+model.Core.TraversalProfile.Walls.Single().Id);float edge=view.transform.InverseTransformPoint(wall.position).x;var size=image.rectTransform.sizeDelta;var sprite=image.sprite;var color=image.color;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();Assert.AreEqual(facing,body.rectTransform.localScale.x);Assert.IsNull(body.GetComponent<Olca.Presentation.Exploration.OutsideSwordWallClip>());
                using(var vh=new VertexHelper()){
                    typeof(Image).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null).Invoke(image,new object[]{vh});var original=new List<UIVertex>();vh.GetUIVertexStream(original);Assert.IsTrue(original.Any(v=>facing*(view.transform.InverseTransformPoint(image.transform.TransformPoint(v.position)).x-edge)>.1f));clip.ModifyMesh(vh);var clipped=new List<UIVertex>();vh.GetUIVertexStream(clipped);Assert.Greater(clipped.Count,0);Assert.IsTrue(clipped.All(v=>facing*(view.transform.InverseTransformPoint(image.transform.TransformPoint(v.position)).x-edge)<=.01f));
                }
                var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());long rev=model.Cards.HandRevision,gauge=model.Core.CardGauge.CurrentUnits;for(int n=0;n<3;n++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(rev,model.Cards.HandRevision);Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreSame(sprite,image.sprite);Assert.AreEqual(color,image.color);Assert.AreEqual(120,model.TrialTarget.CurrentHp);Assert.AreEqual(120,model.SecondTrialTarget.CurrentHp);
            }
        }
        private static IEnumerable<string> 探索手札の実カード名候補()
        {
            var factory=new Olca.Application.Battle.SimpleCardFactory();return factory.CollectionCards.Concat(factory.FusionCards).Select(c=>c.Name).Concat(new[]{factory.CreateExceeded(factory.CreateById(Olca.Application.Battle.SimpleCardFactory.FireSword),Olca.Core.Domain.ExceedLevel.Lv3).Name}).Distinct();
        }
        [TestCaseSource(nameof(探索手札の実カード名候補))]
        public async Task 探索手札の実カード名は末尾まで読めコストとアイコンを覆わない(string name)
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-name-"+id,name,2,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var hand=model.Cards.Hand.ToArray();long frame=model.Core.Frame,gauge=model.Core.CardGauge.CurrentUnits,revision=model.Cards.HandRevision;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.Render();Canvas.ForceUpdateCanvases();
                for(int slot=0;slot<3;slot++){
                    var label=view.transform.Find("OutsideHandName"+slot).GetComponent<Text>();Assert.AreSame(Resources.Load<Font>("OlcaUI/MenuText"),label.font);Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(name),label.text);Assert.IsFalse(label.raycastTarget);label.cachedTextGenerator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size));Assert.GreaterOrEqual(label.cachedTextGenerator.characterCountVisible,label.text.Length);Assert.GreaterOrEqual(label.cachedTextGenerator.fontSizeUsedForBestFit,16);
                    var nr=label.rectTransform;var cost=view.transform.Find("OutsideHandCost"+slot).GetComponent<Text>();var cr=cost.rectTransform;var icon=view.transform.Find("OutsideHandIcon"+slot).GetComponent<Image>();var ir=icon.rectTransform;var button=view.transform.Find("OutsideHandButton"+slot).GetComponent<RectTransform>();
                    Assert.LessOrEqual(cr.anchoredPosition.y+cr.rect.height/2,nr.anchoredPosition.y-nr.rect.height/2);Assert.LessOrEqual(ir.anchoredPosition.y+ir.rect.height/2,nr.anchoredPosition.y-nr.rect.height/2);Assert.LessOrEqual(ir.anchoredPosition.x+ir.rect.width/2,cr.anchoredPosition.x-cr.rect.width/2);Assert.GreaterOrEqual(nr.anchoredPosition.x-nr.rect.width/2,button.anchoredPosition.x-button.rect.width/2);Assert.LessOrEqual(nr.anchoredPosition.x+nr.rect.width/2,button.anchoredPosition.x+button.rect.width/2);Assert.IsFalse(icon.raycastTarget);
                    Assert.AreEqual("コスト 2",cost.text);cost.cachedTextGenerator.Populate(cost.text,cost.GetGenerationSettings(cr.rect.size));Assert.GreaterOrEqual(cost.cachedTextGenerator.characterCountVisible,cost.text.Length);
                }
                for(int n=0;n<4;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(revision,model.Cards.HandRevision);CollectionAssert.AreEqual(hand,model.Cards.Hand);Assert.IsTrue(model.Cards.Hand.All(c=>c.Name==name));
            }
        }

        [TestCase(1)][TestCase(10)][TestCase(100)]
        public async Task 探索手札コストは実フォントで三枠とも全文が収まり表示更新で消費しない(int cost)
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-cost-"+id,"ソード",cost,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();
            var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var hand=model.Cards.Hand.ToArray();long frame=model.Core.Frame,gauge=model.Core.CardGauge.CurrentUnits,revision=model.Cards.HandRevision;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.Render();Canvas.ForceUpdateCanvases();
                for(int slot=0;slot<3;slot++){
                    var label=view.transform.Find("OutsideHandCost"+slot).GetComponent<Text>();Assert.AreSame(Resources.Load<Font>("OlcaUI/MenuText"),label.font);Assert.AreEqual("コスト "+hand[slot].GetFinalCost(),label.text);Assert.IsFalse(label.raycastTarget);
                    var settings=label.GetGenerationSettings(label.rectTransform.rect.size);label.cachedTextGenerator.Populate(label.text,settings);Assert.GreaterOrEqual(label.cachedTextGenerator.characterCountVisible,label.text.Length);Assert.Greater(label.cachedTextGenerator.vertexCount,0);Assert.LessOrEqual(label.preferredHeight,label.rectTransform.rect.height);
                    var name=view.transform.Find("OutsideHandName"+slot).GetComponent<RectTransform>();Assert.LessOrEqual(label.rectTransform.anchoredPosition.y+label.rectTransform.rect.height/2,name.anchoredPosition.y-name.rect.height/2);
                }
                for(int n=0;n<4;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(revision,model.Cards.HandRevision);CollectionAssert.AreEqual(hand,model.Cards.Hand);
            }
        }

        [TestCase(-3,-1)][TestCase(-2,0)][TestCase(-1,0)][TestCase(0,1)][TestCase(1,1)][TestCase(2,2)][TestCase(3,2)][TestCase(4,3)][TestCase(6,3)][TestCase(7,4)][TestCase(9,4)][TestCase(10,5)][TestCase(11,5)][TestCase(12,-1)]
        public void ワイド専用斬撃は既存の予兆命中後消失の境界を六枚へ写す(long age,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialWideSwordEffect.FrameIndex(age));}
        [Test] public void ワイド専用斬撃は完全透過と中心規格および初動の縦範囲を保つ()
        {
            Sprite first=null;using(var clip=new Olca.Presentation.Exploration.TrialWideSwordEffect()){var seen=new HashSet<Sprite>();foreach(int age in new[]{-2,0,2,4,7,10}){var sprite=clip.Resolve(age);Assert.NotNull(sprite);Assert.AreEqual("trial_wide_sword_effect_"+Olca.Presentation.Exploration.TrialWideSwordEffect.FrameIndex(age),sprite.name);Assert.AreEqual(new Vector2(128,128),sprite.pivot);Assert.AreEqual(256,sprite.rect.width);Assert.AreEqual(256,sprite.rect.height);Assert.AreEqual(100,sprite.pixelsPerUnit);seen.Add(sprite);first=sprite;}Assert.AreEqual(6,seen.Count);clip.Dispose();Assert.IsNull(clip.Resolve(0));Assert.NotNull(first.texture);}
            var image=new Texture2D(2,2);try{Assert.IsTrue(image.LoadImage(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_wide_sword_effect_sheet.png"))));Assert.AreEqual(768,image.width);Assert.AreEqual(512,image.height);var pixels=image.GetPixels32();Assert.IsTrue(pixels.All(c=>c.a==0||c.a==255));for(int n=0;n<6;n++){int ox=n%3*256,oy=(1-n/3)*256,minY=256,maxY=-1,minX=256,maxX=-1;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}Assert.Greater(minX,0);Assert.Greater(minY,0);Assert.Less(maxX,255);Assert.Less(maxY,255);if(n==1||n==2){Assert.LessOrEqual(minY,16);Assert.GreaterOrEqual(maxY,239);}}}finally{UnityEngine.Object.DestroyImmediate(image);}
            int loads=0;using(var missing=new Olca.Presentation.Exploration.TrialWideSwordEffect(_=>{loads++;return Array.Empty<Sprite>();})){Assert.IsNull(missing.Resolve(0));Assert.IsNull(missing.Resolve(2));Assert.AreEqual(1,loads);}
        }
        [TestCase(false,false,-1)][TestCase(false,false,1)][TestCase(false,true,-1)][TestCase(false,true,1)][TestCase(true,false,-1)][TestCase(true,false,1)][TestCase(true,true,-1)][TestCase(true,true,1)]
        public async Task ワイド専用斬撃は地空左右衣装の表示領域と本体および非変更を保つ(bool fire,bool air,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("wide324",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(324),out _));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.WideSword);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.WideSword)));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();
                for(int age=0;age<20;age++){if(age>0)model.TickTraversal(0);view.Render();StringAssert.StartsWith((fire?"trial_fire_":"trial_")+(air?"air_sword_swing_":"sword_swing_"),body.sprite.name);var trail=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();int effectFrame=Olca.Presentation.Exploration.TrialWideSwordEffect.FrameIndex(age-8);Assert.AreEqual(effectFrame>=0,trail.enabled);if(effectFrame>=0){Assert.AreEqual("trial_wide_sword_effect_"+effectFrame,trail.sprite.name);Assert.AreEqual(facing,trail.rectTransform.localScale.x);float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);Assert.AreEqual(3*pixels,trail.rectTransform.sizeDelta.x,.01f);Assert.AreEqual(2.7f*pixels,trail.rectTransform.sizeDelta.y,.01f);var strike=model.TrialStrike;long y=age>=8?strike.Y:model.Core.Height;Assert.AreEqual(((y-model.Core.Height)/60000f+1.2f)*pixels,trail.rectTransform.anchoredPosition.y-body.rectTransform.anchoredPosition.y,.01f);Assert.AreEqual(facing*1.5f*pixels,trail.rectTransform.anchoredPosition.x-body.rectTransform.anchoredPosition.x,.01f);}var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());int remaining=model.Cards.RemainingCount,hp=model.TrialTarget.CurrentHp;for(int k=0;k<3;k++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(remaining,model.Cards.RemainingCount);Assert.AreEqual(hp,model.TrialTarget.CurrentHp);}
                model.TickTraversal(0);view.Render();Assert.IsFalse(view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>().enabled);Assert.IsFalse(body.sprite.name.Contains("sword_swing"));
            }
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public async Task スピア専用本体は麻痺のけぞり中断と行動取消後に共用剣を重ねない(bool paralysis,bool afterHit)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,staggerDurationFrames:3));var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("spear323-interrupt",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(323),out _));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.Spear);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.Spear)));Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<(afterHit?8:0);n++)model.TickTraversal(0);var view=binding.View;view.Render();StringAssert.Contains("spear_body",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);
                if(paralysis)model.Player.RestoreTimedAilments(new Olca.Core.Domain.PlayerTimedAilmentsDto{Paralysis=new Olca.Core.Domain.PlayerTimedAilmentDto{RemainingUnits=3000,DisplayFrames=60},Confusion=new Olca.Core.Domain.PlayerTimedAilmentDto(),Blindness=new Olca.Core.Domain.PlayerTimedAilmentDto()});else model.Player.TakeDamage(1,forceStagger:true);Assert.IsTrue(paralysis?model.Player.IsParalyzed:model.Player.IsStaggered);
                for(int n=0;n<2;n++){if(n>0)model.TickTraversal(0);view.Render();Assert.IsFalse(view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name.Contains("spear_body"));Assert.IsFalse(view.transform.Find("OutsideTrialSword").GetComponent<Image>().enabled);var trail=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();if(trail.enabled)StringAssert.StartsWith("trial_slash_3_",trail.sprite.name);}
            }
        }
        [TestCase(-1,-1)][TestCase(0,0)][TestCase(7,0)][TestCase(8,1)][TestCase(13,1)][TestCase(14,2)][TestCase(19,2)][TestCase(20,-1)]
        public void スピア専用本体は前隙突き復帰と空中行を論理境界で切り替える(long age,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialSpearBodyMotion.FrameIndex(age,8,12));Assert.AreEqual(expected<0?-1:expected+3,Olca.Presentation.Exploration.TrialSpearBodyMotion.FrameIndex(age,8,12,false));Assert.AreEqual(-1,Olca.Presentation.Exploration.TrialSpearBodyMotion.FrameIndex(age,-1,12));}
        [TestCase(false)][TestCase(true)] public void スピア専用本体は六枚の透過規格と足元および借用破棄を検証する(bool fire)
        {
            string stem=fire?"trial_fire_spear_body":"trial_spear_body";Sprite first=null;using(var clip=new Olca.Presentation.Exploration.TrialSpearBodyMotion()){var seen=new HashSet<Sprite>();foreach(bool ground in new[]{true,false})foreach(int age in new[]{0,8,14}){var sprite=clip.Resolve(age,8,12,fire,ground);Assert.NotNull(sprite);StringAssert.StartsWith(stem+"_",sprite.name);Assert.AreEqual(new Vector2(128,14),sprite.pivot);Assert.AreEqual(256,sprite.rect.width);Assert.AreEqual(256,sprite.rect.height);Assert.AreEqual(100,sprite.pixelsPerUnit);Assert.IsTrue(clip.Contains(sprite));seen.Add(sprite);first=sprite;}Assert.AreEqual(6,seen.Count);clip.Dispose();Assert.IsNull(clip.Resolve(0,8,12,fire));Assert.IsFalse(clip.Contains(first));Assert.NotNull(first.texture);}
            var image=new Texture2D(2,2);try{Assert.IsTrue(image.LoadImage(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/"+stem+"_sheet.png"))));Assert.AreEqual(768,image.width);Assert.AreEqual(512,image.height);var pixels=image.GetPixels32();Assert.IsTrue(pixels.All(c=>c.a==0||c.a==255));for(int n=0;n<6;n++){int ox=n%3*256,oy=(1-n/3)*256,minY=256,maxY=-1,minX=256,maxX=-1;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}Assert.AreEqual(14,minY);Assert.Greater(minX,0);Assert.Less(maxX,255);Assert.Less(maxY,255);}}finally{UnityEngine.Object.DestroyImmediate(image);}
            int loads=0;using(var missing=new Olca.Presentation.Exploration.TrialSpearBodyMotion(_=>{loads++;return Array.Empty<Sprite>();})){Assert.IsNull(missing.Resolve(0,8,12,fire));Assert.IsNull(missing.Resolve(8,8,12,fire));Assert.AreEqual(1,loads);}
        }
        [TestCase(false,false,-1)][TestCase(false,false,1)][TestCase(false,true,-1)][TestCase(false,true,1)]
        [TestCase(true,false,-1)][TestCase(true,false,1)][TestCase(true,true,-1)][TestCase(true,true,1)]
        public async Task スピア専用本体は通常カエンの地上空中左右で軌跡同期と停止および復帰を保つ(bool fire,bool air,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("spear323",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(323),out _));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.Spear);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==Olca.Core.Domain.TrialInitialCardDefinitions.Spear)));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();
                for(int age=0;age<20;age++){if(age>0)model.TickTraversal(0);view.Render();Assert.AreEqual((fire?"trial_fire_spear_body_":"trial_spear_body_")+Olca.Presentation.Exploration.TrialSpearBodyMotion.FrameIndex(age,8,12,!air),body.sprite.name);Assert.AreEqual(facing,body.rectTransform.localScale.x);Assert.IsFalse(view.transform.Find("OutsideTrialSword").GetComponent<Image>().enabled);var trail=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();int effectFrame=Olca.Presentation.Exploration.OutsideSwordSwingVisual.FrameIndex(age-8);Assert.AreEqual(effectFrame>=0,trail.enabled);if(effectFrame>=0){Assert.AreEqual("trial_slash_3_"+effectFrame,trail.sprite.name);Assert.AreEqual(facing,trail.rectTransform.localScale.x);}var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());int remaining=model.Cards.RemainingCount,hp=model.TrialTarget.CurrentHp;for(int k=0;k<3;k++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(remaining,model.Cards.RemainingCount);Assert.AreEqual(hp,model.TrialTarget.CurrentHp);}
                model.TickTraversal(0);view.Render();Assert.IsFalse(body.sprite.name.Contains("spear_body"));Assert.IsFalse(view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>().enabled);
            }
        }
        [TestCase("trial-spear-v1",false,false,1,3)][TestCase("trial-spear-v1",false,true,-1,3)]
        [TestCase("trial-spear-v1",true,false,-1,3)][TestCase("trial-spear-v1",true,true,1,3)]
        [TestCase("trial-wide-sword-v1",false,false,1,1)][TestCase("sword",false,false,1,1)]
        public async Task スピアの突進軌跡はカード種別と左右空中に追従し時計判定消費を変更しない(string id,bool fire,bool air,int facing,int row)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("spear322",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(322),out _));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id)));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);
                Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var lease=(Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease)binding.View.GetType().GetField("presentation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(binding.View);
                var view=binding.View;for(int age=0;age<20;age++){if(age>0)model.TickTraversal(0);view.Render();Assert.AreEqual(id,lease.SwordSwingCardId);var trail=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();int frame=Olca.Presentation.Exploration.OutsideSwordSwingVisual.FrameIndex(age-8);Assert.AreEqual(frame>=0,trail.enabled);if(frame>=0){Assert.AreEqual(id==Olca.Core.Domain.TrialInitialCardDefinitions.WideSword?"trial_wide_sword_effect_"+Olca.Presentation.Exploration.TrialWideSwordEffect.FrameIndex(age-8):"trial_slash_"+row+"_"+frame,trail.sprite.name);Assert.AreEqual(facing,trail.rectTransform.localScale.x);Assert.AreEqual(id==Olca.Core.Domain.TrialInitialCardDefinitions.Spear?270000:180000,lease.SwordSwingReach);}
                    var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());int remaining=model.Cards.RemainingCount,hp=model.TrialTarget.CurrentHp;for(int k=0;k<3;k++)view.Render();Assert.AreEqual(remaining,model.Cards.RemainingCount);Assert.AreEqual(hp,model.TrialTarget.CurrentHp);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));
                }Assert.AreEqual(id,lease.TrialStrike.CardId);Assert.AreEqual(id,lease.TrialStrike.Copy().CardId);model.TickTraversal(0);view.Render();Assert.IsNull(lease.SwordSwingCardId);Assert.IsFalse(view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>().enabled);
            }
        }
        [TestCase(0,1f)][TestCase(1,11f/12)][TestCase(11,1f/12)][TestCase(12,0f)][TestCase(13,0f)][TestCase(59,0f)]
        public void 専用発射姿勢の掌補正は十二論理フレームで既存射線へ連続合流する(int age,float ratio){var offset=Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(age);Assert.AreEqual(.55f*ratio,offset.x,.00001f);Assert.AreEqual(.5f*ratio,offset.y,.00001f);}
        [TestCase(-1)][TestCase(1)] public void 専用発射姿勢の掌補正は左右とも直進弾の表示を逆行させない(int facing)
        {
            var shot=new Olca.Core.Logic.Integration.OutsideInitialShotData{CardId=Olca.Core.Domain.TrialInitialCardDefinitions.Shot,X=15*60000,Height=0,StartedAt=10,Facing=facing,Power=10};float prior=shot.X/60000f+facing*.55f;for(int age=1;age<60;age++){var offset=Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(age);float position=shot.Position(10+age)/60000f+facing*offset.x;Assert.Greater((position-prior)*facing,0);Assert.That(offset.y,Is.InRange(0,.5f));prior=position;}Assert.AreEqual(15*60000,shot.X);Assert.AreEqual(10,shot.StartedAt);
        }
        [TestCase(-1,-1)][TestCase(0,0)][TestCase(5,0)][TestCase(6,1)][TestCase(11,1)][TestCase(12,2)][TestCase(17,2)][TestCase(18,-1)]
        public void 専用発射姿勢は前隙発射復帰を行動時計だけで切り替える(int age,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialShotBodyMotion.FrameIndex(age,6,12));Assert.AreEqual(expected<0?-1:expected+3,Olca.Presentation.Exploration.TrialShotBodyMotion.FrameIndex(age,6,12,false));}
        [TestCase(false)][TestCase(true)] public void 専用発射姿勢は地上空中の六枚と足元および欠損破棄を検証する(bool fire)
        {
            var clip=new Olca.Presentation.Exploration.TrialShotBodyMotion();var seen=new HashSet<Sprite>();foreach(bool ground in new[]{true,false})foreach(int age in new[]{0,6,12}){var sprite=clip.Resolve(age,6,12,fire,ground);Assert.NotNull(sprite);StringAssert.StartsWith(fire?"trial_fire_shot_body_":"trial_shot_body_",sprite.name);Assert.AreEqual(new Vector2(128,14),sprite.pivot);Assert.AreEqual(256,sprite.rect.width);Assert.AreEqual(256,sprite.rect.height);Assert.IsTrue(clip.Contains(sprite));seen.Add(sprite);}Assert.AreEqual(6,seen.Count);clip.Dispose();Assert.IsNull(clip.Resolve(0,6,12,fire));using(var missing=new Olca.Presentation.Exploration.TrialShotBodyMotion(_=>Array.Empty<Sprite>()))Assert.IsNull(missing.Resolve(0,6,12,fire));
        }
        [TestCase(false,false,-1)][TestCase(false,true,1)][TestCase(true,false,1)][TestCase(true,true,-1)]
        public async Task 専用発射姿勢は衣装地上空中左右と停止中の不変性および終了復帰を保つ(bool fire,bool air,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("shot321-active",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(321),out _));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);string id=air?Olca.Core.Domain.TrialInitialCardDefinitions.AirShot:Olca.Core.Domain.TrialInitialCardDefinitions.Shot;for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id);n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();
                for(int n=0;n<18;n++){if(n>0)model.TickTraversal(0);view.Render();Assert.AreEqual((fire?"trial_fire_shot_body_":"trial_shot_body_")+Olca.Presentation.Exploration.TrialShotBodyMotion.FrameIndex(n,6,12,!air),body.sprite.name);Assert.AreEqual(facing,body.rectTransform.localScale.x);var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int k=0;k<3;k++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));}model.TickTraversal(0);view.Render();Assert.IsFalse(body.sprite.name.Contains("shot_body"));
            }
        }
        [TestCase(-1,-1)][TestCase(0,0)][TestCase(1,0)][TestCase(2,1)][TestCase(3,1)][TestCase(10,5)][TestCase(11,5)][TestCase(12,0)][TestCase(59,5)][TestCase(60,-1)]
        public void 専用直進弾は二論理フレームごとに六コマ循環し寿命外を表示しない(long age,int frame){Assert.AreEqual(frame,Olca.Presentation.Exploration.TrialShotFlightMotion.FrameIndex(age));}
        [Test] public void 専用直進弾は六枚の規格と欠損および破棄後を検証する()
        {
            using(var clip=new Olca.Presentation.Exploration.TrialShotFlightMotion()){var seen=new System.Collections.Generic.HashSet<Sprite>();for(int n=0;n<6;n++){var sprite=clip.Resolve(n*2);Assert.NotNull(sprite);Assert.AreEqual("trial_shot_flight_"+n,sprite.name);Assert.AreEqual(new Vector2(128,128),sprite.pivot);Assert.AreEqual(256,sprite.rect.width);Assert.AreEqual(256,sprite.rect.height);seen.Add(sprite);}Assert.AreEqual(6,seen.Count);clip.Dispose();Assert.IsNull(clip.Resolve(0));}using(var missing=new Olca.Presentation.Exploration.TrialShotFlightMotion(_=>Array.Empty<Sprite>()))Assert.IsNull(missing.Resolve(0));
        }
        [TestCase(false)][TestCase(true)] public async Task 別マップ射撃の敵表示はマップ座標とHPを読み描画で状態を変えない(bool annex)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);if(annex)model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);var view=binding.View;view.Render();var first=view.transform.Find("OutsideTrialTarget").GetComponent<Image>();var second=view.transform.Find("OutsideSecondTrialTarget").GetComponent<Image>();Assert.IsTrue(first.enabled);Assert.IsTrue(second.enabled);var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);Assert.AreEqual((model.SecondTrialTargetX-model.FirstTrialTargetX)/60000f*pixels,second.rectTransform.anchoredPosition.x-first.rectTransform.anchoredPosition.x,.01f);if(annex)Assert.IsTrue(view.transform.Find("OutsideTrialTargetNamePlate").GetComponent<Button>().interactable);
                model.TrialTarget.TakeDamage(13,Olca.Core.Domain.ElementType.None);long frame=model.Core.Frame;view.Render();Assert.AreEqual("107",view.transform.Find("OutsideTrialTargetHp").GetComponent<Text>().text);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(107,model.TrialTarget.CurrentHp);
            }
        }
        [TestCase(false,-1,false)][TestCase(false,1,false)][TestCase(true,-1,false)][TestCase(true,1,false)]
        [TestCase(false,-1,true)][TestCase(false,1,true)][TestCase(true,-1,true)][TestCase(true,1,true)]
        public async Task 専用直進弾は地上空中と左右で表示だけ進み消費や論理位置を変更しない(bool air,int facing,bool annex)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("shot320-active",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(320),out _));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);if(annex){model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);while(model.Core.Position<15*60000)model.TickTraversal(1);}string id=air?Olca.Core.Domain.TrialInitialCardDefinitions.AirShot:Olca.Core.Domain.TrialInitialCardDefinitions.Shot;for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id);n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<30&&model.InitialShots.Count==0;n++)model.TickTraversal(0);Assert.AreEqual(1,model.InitialShots.Count);var view=binding.View;
                for(int n=0;n<12;n++){if(n>0)model.TickTraversal(0);view.Render();var shot=model.InitialShots.Single();var image=view.transform.Find("TrialStraightShot").GetComponent<Image>();Assert.IsTrue(image.enabled);Assert.AreEqual(facing,image.rectTransform.localScale.x);var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);Assert.AreEqual(((shot.Position(model.Core.Frame)-model.Core.Position)/60000f+facing*Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(model.Core.Frame-shot.StartedAt).x)*pixels,image.rectTransform.anchoredPosition.x-body.rectTransform.anchoredPosition.x,.01f);Assert.AreEqual(((shot.Height-model.Core.Height)/60000f+1.2f+Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(model.Core.Frame-shot.StartedAt).y)*pixels,image.rectTransform.anchoredPosition.y-body.rectTransform.anchoredPosition.y,.01f);Assert.AreEqual((id==Olca.Core.Domain.TrialInitialCardDefinitions.AirShot?"trial_air_shot_flight_":"trial_shot_flight_")+Olca.Presentation.Exploration.TrialShotFlightMotion.FrameIndex(model.Core.Frame-shot.StartedAt),image.sprite.name);long frame=model.Core.Frame,pos=shot.Position(frame),gauge=model.Core.CardGauge.CurrentUnits;int remaining=model.Cards.RemainingCount;var core=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int k=0;k<5;k++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(pos,model.InitialShots.Single().Position(model.Core.Frame));Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(remaining,model.Cards.RemainingCount);CollectionAssert.AreEqual(core,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));}for(int n=0;n<70;n++)model.TickTraversal(0);view.Render();Assert.IsFalse(view.transform.Find("TrialStraightShot").GetComponent<Image>().enabled);
            }
        }
        [TestCase(-1,-1)][TestCase(0,0)][TestCase(2,0)][TestCase(3,1)][TestCase(5,1)][TestCase(6,2)][TestCase(7,2)][TestCase(8,3)][TestCase(11,3)][TestCase(12,4)][TestCase(15,4)][TestCase(16,5)][TestCase(19,5)][TestCase(20,-1)]
        public void 専用ソード連番は前隙と命中後の六姿勢を境界で切り替える(int elapsed,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialSwordSwingMotion.FrameIndex(elapsed,8,12));}
        [TestCase(false,true)][TestCase(true,true)][TestCase(false,false)][TestCase(true,false)] public void 専用ソード連番は衣装別六枚と共通足元を登録し欠損を代用しない(bool fire,bool grounded)
        {
            using(var clip=new Olca.Presentation.Exploration.TrialSwordSwingMotion()){
                var seen=new System.Collections.Generic.HashSet<Sprite>();foreach(int elapsed in new[]{0,3,6,8,12,16}){var sprite=clip.Resolve(elapsed,8,12,fire,grounded);Assert.NotNull(sprite);StringAssert.StartsWith((fire?"trial_fire_":"trial_")+(grounded?"sword_swing_":"air_sword_swing_"),sprite.name);Assert.AreEqual(new Vector2(128,14),sprite.pivot);Assert.AreEqual(256,sprite.rect.width);Assert.AreEqual(256,sprite.rect.height);Assert.IsTrue(clip.Contains(sprite));seen.Add(sprite);}Assert.AreEqual(6,seen.Count);Assert.IsNull(clip.Resolve(20,8,12,fire,grounded));
            }using(var missing=new Olca.Presentation.Exploration.TrialSwordSwingMotion(_=>Array.Empty<Sprite>()))Assert.IsNull(missing.Resolve(0,8,12,fire,grounded));
        }
        [TestCase(false)][TestCase(true)] public async Task 専用ソード空中連番は跳躍中の六姿勢と剣の重複抑止を保ち終了で戻る(bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var source=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(319),out _));for(int n=0;n<100&&!model.Cards.Hand.Any(c=>c.Id=="sword");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);model.TickTraversal(0,true,true);Assert.IsFalse(model.Core.IsGrounded);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);
                var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();for(int n=0;n<20;n++){if(n>0)model.TickTraversal(0);view.Render();Assert.IsFalse(model.Core.IsGrounded);StringAssert.StartsWith(fire?"trial_fire_air_sword_swing_":"trial_air_sword_swing_",body.sprite.name);Assert.IsFalse(view.transform.Find("OutsideTrialSword").GetComponent<Image>().enabled);var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));}model.TickTraversal(0);view.Render();Assert.IsFalse(body.sprite.name.Contains("air_sword_swing"));
            }
        }
        [Test] public void ソード以外の専用軌跡は残光の濃度を変更しない()
        {
            foreach(var id in new[]{Olca.Core.Domain.TrialInitialCardDefinitions.Spear,Olca.Core.Domain.TrialInitialCardDefinitions.WideSword,Olca.Application.Battle.SimpleCardFactory.FireSword,"unknown"}){for(int frame=8;frame<12;frame++)Assert.AreEqual(1,Olca.Presentation.Exploration.OutsideSwordSwingVisual.Opacity(id,frame));Assert.AreEqual(0,Olca.Presentation.Exploration.OutsideSwordSwingVisual.Opacity(id,12));}
        }
        [TestCase(false,false,-1)][TestCase(false,false,1)][TestCase(false,true,-1)][TestCase(false,true,1)]
        [TestCase(true,false,-1)][TestCase(true,false,1)][TestCase(true,true,-1)][TestCase(true,true,1)]
        public async Task ソード残光は地上空中と衣装左右で復帰に合わせて薄れ再描画で進まない(bool fire,bool air,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(cards,cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(341),out _));for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id=="sword");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<900000)model.TickTraversal(1);model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var body=binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>();
                for(int age=0;age<20;age++){if(age>0)model.TickTraversal(0);binding.View.Render();var trail=binding.View.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();int relative=age-8;Assert.AreEqual(relative>=-2,trail.enabled);Assert.AreEqual(facing,body.rectTransform.localScale.x);StringAssert.StartsWith((fire?"trial_fire_":"trial_")+(air?"air_sword_swing_":"sword_swing_"),body.sprite.name);if(trail.enabled){float expected=relative<=7?1:(12-relative)/5f;Assert.That(trail.color.a,Is.EqualTo(expected).Within(.0001f));Assert.AreEqual(facing,trail.rectTransform.localScale.x);float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);Assert.That(trail.rectTransform.sizeDelta.x,Is.EqualTo(3*pixels).Within(.01f));var color=trail.color;long frame=model.Core.Frame;int remaining=model.Cards.RemainingCount,hp=model.TrialTarget.CurrentHp;binding.View.Render();Assert.AreEqual(color,trail.color);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(remaining,model.Cards.RemainingCount);Assert.AreEqual(hp,model.TrialTarget.CurrentHp);}}
                model.TickTraversal(0);binding.View.Render();Assert.IsFalse(binding.View.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>().enabled);
            }
        }
        [TestCase(false)][TestCase(true)] public async Task 改善カードのソード表示は振りかぶり連番と独立した四コマ斬撃を論理時計で切り替える(bool otherStyle)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(otherStyle)model.Player.SetDepartureStyle(new Olca.Core.Domain.DepartureStyleDefinition("other-style","fixture",null));var source=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(317),out _));for(int n=0;n<100&&!model.Cards.Hand.Any(c=>c.Id=="sword");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));model.EnableTrialCardPolish();
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);var view=binding.View;view.Render();var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var first=body.sprite;var trail=view.transform.Find("OutsideSwordSpriteTrail").GetComponent<Image>();Assert.IsFalse(trail.enabled);for(int n=0;n<4;n++)model.TickTraversal(0);view.Render();if(otherStyle){Assert.IsFalse(body.sprite.name.StartsWith("trial_bomb_"));Assert.IsFalse(body.sprite.name.StartsWith("trial_sword_"));}else{Assert.AreNotSame(first,body.sprite);StringAssert.StartsWith("trial_sword_swing_",body.sprite.name);Assert.IsFalse(view.transform.Find("OutsideTrialSword").GetComponent<Image>().enabled);}for(int n=0;n<4;n++)model.TickTraversal(0);view.Render();Assert.IsTrue(trail.enabled);StringAssert.Contains("trial_slash_1_1",trail.sprite.name);Assert.AreEqual(90,model.TrialTarget.CurrentHp);var peak=trail.sprite;var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int n=0;n<20;n++)view.Render();Assert.AreSame(peak,trail.sprite);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));for(int n=0;n<3;n++)model.TickTraversal(0);view.Render();Assert.AreNotSame(peak,trail.sprite);for(int n=0;n<60;n++)model.TickTraversal(0);view.Render();Assert.IsFalse(trail.enabled);Assert.AreEqual(90,model.TrialTarget.CurrentHp);
            }
        }
        [Test] public void 試用ナビはControllerの保存Sessionと新規開始世代を越えず再読込では維持する()
        {
            var obj=new GameObject("NavigationOwner");obj.SetActive(false);var c=obj.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=c.GetType();var store=new DoorBoundaryStore();var session=new Olca.Core.Domain.ClearReturnSession(store);
            try{void Set(object value)=>type.GetField("clearReturnSession",flags).SetValue(c,value);Olca.Core.Logic.Integration.OutsideTrialNavigation Read()=>(Olca.Core.Logic.Integration.OutsideTrialNavigation)type.GetProperty("OutsideNavigation",flags).GetValue(c);Set(session);Read().Select("trial-find-recovery");Assert.IsTrue(session.Reload());Assert.AreEqual("trial-find-recovery",Read().SelectedId);store.Profile.CheckpointGeneration=Guid.NewGuid().ToString("N");Assert.IsTrue(session.Reload());Assert.IsNull(Read().SelectedId);Read().Select("trial-visit-annex");Set(new Olca.Core.Domain.ClearReturnSession(store));Assert.IsNull(Read().SelectedId);Assert.AreEqual(0,store.Writes);}finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [TestCase(1920,1080)][TestCase(1440,1080)][TestCase(3840,2160)]
        public async Task 試用ナビ目的一覧は選択解除表示切替と古い選択の拒否を行い進行を書き換えない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var nav=new Olca.Core.Logic.Integration.OutsideTrialNavigation();var profile=new Olca.Core.Domain.ClearReturnProfile();var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile);var owner=new object();OutsideObjectivesView view=null;bool alive=true;
            views.Register(owner,"nav-goals",ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideObjectivesView>("NavGoals",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>Olca.Core.Logic.Integration.OutsideTrialGoalOverview.Build(profile,"trial"),()=>{},()=>alive);v.ConfigureNavigation(()=>nav);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync("nav-goals")).Succeeded);Canvas.ForceUpdateCanvases();Button Get(string name)=>view.GetComponentsInChildren<Button>().Single(b=>b.name==name);var first=Get("Objective_trial-find-recovery");var stale=first.onClick;foreach(var b in view.GetComponentsInChildren<Button>())Assert.Less(b.colors.normalColor.r,.5f,"背景を白くして本文を消さない: "+b.name);first.onClick.Invoke();Assert.AreEqual("trial-find-recovery",nav.SelectedId);Get("ToggleOutsideNavigation").onClick.Invoke();Assert.IsFalse(nav.Visible);Get("ClearOutsideNavigation").onClick.Invoke();Assert.IsNull(nav.SelectedId);views.CloseCurrent();stale.Invoke();Assert.IsNull(nav.SelectedId);Assert.IsTrue((await views.ShowAsync("nav-goals")).Succeeded);stale.Invoke();Assert.IsNull(nav.SelectedId);alive=false;Get("Objective_trial-visit-annex").onClick.Invoke();Assert.IsNull(nav.SelectedId);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile));}finally{views.UnregisterOwner(owner);}
        }
        [TestCase(1920,1080)][TestCase(1440,1080)][TestCase(3840,2160)]
        public async Task 試用ナビ全体マップは未開示の目的地印だけを描き出口と表示解除を切り替える(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var core=system.OutsideExploration.Core;var profile=new Olca.Core.Domain.ClearReturnProfile{OutsideDiscoveredRecoveryPoints=new[]{Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId}};var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile);var nav=new Olca.Core.Logic.Integration.OutsideTrialNavigation();nav.Select("trial-visit-annex");var owner=new object();OutsideFastTravelView view=null;
            views.Register(owner,"nav-map",ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("NavMap",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>false,()=>{},()=>{},()=>true,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(profile,core,nav.Resolve(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId)?.MapId));v.ConfigureNavigation(()=>nav);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync("nav-map")).Succeeded);Assert.IsNotNull(view.transform.Find("MapGrid/NavigationExit"));view.GetComponentsInChildren<Button>().Single(b=>b.name=="MapChoice_1").onClick.Invoke();Assert.IsNotNull(view.transform.Find("MapGrid/NavigationDestination"));var children=view.transform.Find("MapGrid").GetComponentsInChildren<Image>();Assert.IsFalse(children.Any(g=>g.name.StartsWith("Visited_")||g.name.StartsWith("Revealed_")));nav.ToggleVisibility();view.RefreshMapSnapshot(null);Assert.IsNull(view.transform.Find("MapGrid/NavigationDestination"));nav.ToggleVisibility();nav.Select("trial-find-recovery");view.RefreshMapSnapshot(null);var mark=view.transform.Find("MapGrid/NavigationDestination").GetComponent<Image>();Assert.IsNotNull(view.transform.Find("MapGrid/DiscoveredRecovery"));Assert.AreEqual(0,mark.color.a);Assert.GreaterOrEqual(Mathf.Abs(mark.GetComponentInChildren<Text>().rectTransform.anchoredPosition.x),40);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile));}finally{views.UnregisterOwner(owner);}
        }
        [Test] public async Task 試用ナビは仮想パッドで目的決定と各行操作と解除戻るを受ける()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var prior=UnityInputProvider.ActiveGamepadProfile;var owner=new object();var nav=new Olca.Core.Logic.Integration.OutsideTrialNavigation();OutsideObjectivesView view=null;int closed=0;
            using(var input=new UnityInputProvider())try{Assert.IsTrue(input.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _));views.Register(owner,"nav-pad",ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideObjectivesView>("NavPad",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>Olca.Core.Logic.Integration.OutsideTrialGoalOverview.Build(new Olca.Core.Domain.ClearReturnProfile(),"trial"),()=>closed++,()=>true);v.ConfigureNavigation(()=>nav);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
                void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var k in keys)state=state.WithButton(k);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();typeof(OutsideObjectivesView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);}
                void Move(UnityEngine.InputSystem.LowLevel.GamepadButton key,string name){State();State(key);Assert.AreEqual(name,EventSystem.current.currentSelectedGameObject.name);}
                Assert.IsTrue((await views.ShowAsync("nav-pad")).Succeeded);Canvas.ForceUpdateCanvases();State();Assert.AreEqual("Objective_trial-find-recovery",EventSystem.current.currentSelectedGameObject.name);State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual("trial-find-recovery",nav.SelectedId);Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"Objective_trial-visit-annex");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"Objective_trial-clear-two-floors");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"ClearOutsideNavigation");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"ToggleOutsideNavigation");State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.IsFalse(nav.Visible);Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft,"ClearOutsideNavigation");State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.IsNull(nav.SelectedId);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.East);Assert.AreEqual(1,closed);
            }finally{views.UnregisterOwner(owner);input.TryRestoreGamepadProfile(prior,out _);UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test] public void 目的一覧の実Controllerは帰還保存と同じルートIDを読み定義hashを達成IDに使わない()
        {
            var obj=new GameObject("GoalRouteController");obj.SetActive(false);var controller=obj.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();var factory=new Olca.Application.Battle.SimpleCardFactory();var checkpoint=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(8,30,2),factory.CreateStarterDeck());var route=Olca.Application.Management.DungeonCoreFactory.CreateTrialRoute(checkpoint.Definitions,factory,new Olca.Core.Domain.EnemyPoiseDefinition(10,6,60),new Olca.Core.Domain.EnemyPoiseDefinition(30,6,60));var store=new DoorBoundaryStore();
            try{controller.ConfigureDungeonSelection(new[]{route});var progress=new Olca.Core.Domain.ClearReturnSession(store);type.GetField("clearReturnSession",flags).SetValue(controller,progress);System.Collections.Generic.IReadOnlyList<Olca.Core.Logic.Integration.OutsideTrialGoalOverview> Read()=>(System.Collections.Generic.IReadOnlyList<Olca.Core.Logic.Integration.OutsideTrialGoalOverview>)type.GetMethod("BuildOutsideTrialGoals",flags).Invoke(controller,null);Assert.AreNotEqual(route.RouteId,route.RouteHash);store.Profile.ClearedDungeons=new[]{route.RouteHash};Assert.IsTrue(progress.Reload());Assert.IsFalse(Read()[2].Completed);store.Profile.ClearedDungeons=new[]{route.RouteId};Assert.IsTrue(progress.Reload());Assert.IsTrue(Read()[2].Completed);}
            finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public async Task 目的一覧の実Controllerは待機中の無効化解除と同Host再表示の旧結果を捨てる(int boundary)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var obj=new GameObject("GoalBoundaryController");obj.SetActive(false);var controller=obj.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);object Get(string n)=>type.GetField(n,flags).GetValue(controller);void Call(string n)=>type.GetMethod(n,flags).Invoke(controller,null);
            var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(8,30,2),new Olca.Core.Domain.PlayerCharacter(100),new Olca.Core.Domain.Deck(),new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger());manager.SetUserPaused(true);var store=new DoorBoundaryStore();var progress=new Olca.Core.Domain.ClearReturnSession(store);var gate=new TaskCompletionSource<GameObject>();var registration=new object();int resumed=0;CancellationToken loadToken=default;float oldScale=Time.timeScale;bool oldPause=GameplayPause.IsPaused;var panels=new HostPanelBindings(controller,host.transform,resources,views);panels.Register("gameplay-pause","goal-pause-test",()=>Panel("GoalPause"),(p,c)=>resumed++,p=>p.SetActive(false));OutsideExplorationPresentationBinding binding=null;var oldCancellation=new CancellationTokenSource();Task pending=null;
            try{system.OutsideExploration.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources)),()=>true,()=>{});Assert.IsTrue((await binding.OpenAsync()).Succeeded);Set("gameSystem",system);Set("manager",manager);Set("initialHub",true);Set("clearReturnSession",progress);Set("outsideExploration",binding);Set("hostPanels",panels);Set("runtimeStarted",true);obj.SetActive(true);
                views.Register(registration,"outside-objectives",ct=>{loadToken=ct;return gate.Task;},(p,c)=>p.SetActive(true),p=>p.SetActive(false));Set("outsideObjectivesActive",true);Set("outsideObjectivesOpening",true);Set("outsideObjectivesEpoch",1L);Set("outsideObjectivesCancellation",oldCancellation);pending=((UniTask)type.GetMethod("ShowOutsideObjectivesAsync",flags).Invoke(controller,new object[]{1L,oldCancellation})).AsTask();Assert.IsFalse(pending.IsCompleted);
                if(boundary==0){controller.enabled=false;Call("OnDisable");}
                else if(boundary==1)type.GetMethod("ApplyUserPauseImmediate",flags).Invoke(controller,new object[]{false});
                else{Call("AbortOutsideObjectives");views.UnregisterOwner(registration);var nextOwner=new object();viewCleanup=()=>views.UnregisterOwner(nextOwner);views.Register(nextOwner,"outside-objectives",ct=>Task.FromResult(Panel("NewGoalGeneration")),(p,c)=>p.SetActive(true),p=>p.SetActive(false));Call("OpenOutsideObjectives");Assert.IsTrue((bool)Get("outsideObjectivesActive"));}
                Assert.IsTrue(loadToken.IsCancellationRequested);gate.SetException(new IOException("late old objective view"));await pending;Assert.AreEqual(0,resumed,"旧show結果でPauseを再表示しない");if(boundary==2){Assert.AreEqual("outside-objectives",views.CurrentKey);Assert.IsTrue((bool)Get("outsideObjectivesActive"));}else Assert.IsFalse((bool)Get("outsideObjectivesActive"));Assert.IsFalse(progress.HasPendingSave);Assert.AreEqual(0,store.Writes);
            }finally{if(!gate.Task.IsCompleted)gate.TrySetCanceled();if(pending!=null)await pending;Call("AbortOutsideObjectives");views.UnregisterOwner(registration);binding?.Dispose();Set("outsideExploration",null);Set("hostPanels",null);Set("gameSystem",null);Set("manager",null);panels.Dispose();UnityEngine.Object.DestroyImmediate(obj);Time.timeScale=oldScale;GameplayPause.SetPaused(oldPause);}
        }
        [TestCase(1920,1080)][TestCase(1440,1080)][TestCase(3840,2160)]
        public async Task 目的一覧は保存済み達成を表示して再表示と閉鎖後の古い戻るを拒否する(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var profile=new Olca.Core.Domain.ClearReturnProfile();var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile);var owner=new object();OutsideObjectivesView view=null;int reads=0,closed=0;bool alive=true;string key="trial-objectives";
            views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideObjectivesView>("ObjectivesTest",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>{reads++;return Olca.Core.Logic.Integration.OutsideTrialGoalOverview.Build(profile,"trial");},()=>closed++,()=>alive);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Canvas.ForceUpdateCanvases();var background=view.GetComponent<Image>();Assert.IsNotNull(background,"RectTransformだけのfactoryにも背景を生成する");Assert.GreaterOrEqual(background.color.a,.95f);Assert.IsTrue(background.raycastTarget);var back=view.transform.Find("CloseOutsideObjectives").GetComponent<Button>();var old=back.onClick;Assert.AreEqual(3,view.transform.Find("ObjectiveRows").childCount);StringAssert.Contains("ミタッセイ",view.transform.Find("ObjectiveRows/Objective_trial-find-recovery/ObjectiveTitle").GetComponent<Text>().text);StringAssert.Contains("ミトウロク",view.transform.Find("ObjectivesScope").GetComponent<Text>().text);
                Rect Bounds(Transform t){var c=new Vector3[4];((RectTransform)t).GetWorldCorners(c);return new Rect(c[0],c[2]-c[0]);}var area=Bounds(host.transform);foreach(var text in view.GetComponentsInChildren<Text>()){var b=Bounds(text.transform);Assert.IsTrue(area.Contains(b.min)&&area.Contains(b.max),text.name);Assert.LessOrEqual(text.preferredHeight,text.rectTransform.rect.height,text.name);Assert.LessOrEqual(text.preferredWidth,text.rectTransform.rect.width,text.name);}
                for(int i=0;i<60;i++)typeof(OutsideObjectivesView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Assert.AreEqual(1,reads);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile));old.Invoke();Assert.AreEqual(1,closed);views.CloseCurrent();profile.OutsideDiscoveredRecoveryPoints=new[]{Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId};Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Assert.AreEqual(3,view.transform.Find("ObjectiveRows").childCount);StringAssert.Contains("タッセイ",view.transform.Find("ObjectiveRows/Objective_trial-find-recovery/ObjectiveTitle").GetComponent<Text>().text);old.Invoke();Assert.AreEqual(1,closed);alive=false;back.onClick.Invoke();Assert.AreEqual(1,closed);views.UnregisterOwner(owner);Assert.DoesNotThrow(()=>old.Invoke());
            }finally{views.UnregisterOwner(owner);}
        }
        [Test] public async Task 目的一覧は仮想パッドの決定と戻るを各一回受け表示だけでデータを書き換えない()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var prior=UnityInputProvider.ActiveGamepadProfile;var owner=new object();OutsideObjectivesView view=null;int closed=0;using(var input=new UnityInputProvider()){
                Assert.IsTrue(input.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _));views.Register(owner,"objective-pad",ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideObjectivesView>("ObjectivePad",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>Olca.Core.Logic.Integration.OutsideTrialGoalOverview.Build(new Olca.Core.Domain.ClearReturnProfile(),null),()=>closed++,()=>true);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
                void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var k in keys)state=state.WithButton(k);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();typeof(OutsideObjectivesView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);}
                try{Assert.IsTrue((await views.ShowAsync("objective-pad")).Succeeded);Canvas.ForceUpdateCanvases();State();Assert.AreEqual("CloseOutsideObjectives",EventSystem.current.currentSelectedGameObject.name);State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(1,closed);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.East);Assert.AreEqual(2,closed);}
                finally{views.UnregisterOwner(owner);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);Assert.IsTrue(input.TryRestoreGamepadProfile(prior,out _));}
            }
        }
        [TestCase(1920,1080)][TestCase(1440,1080)][TestCase(3840,2160)]
        public async Task ミニマップは開示と実訪問地点を共有し保存時計入力を変更しない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var core=model.Core;var store=new DoorBoundaryStore();var save=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(save.TryRecordOutsideMapVisits(new[]{new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=2,Y=0}}}},out _));int reads=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var nav=new Olca.Core.Logic.Integration.OutsideTrialNavigation();nav.Select("trial-visit-annex");void Bind(){view.ConfigureMiniMap(()=>save.InventoryRevision,()=>{reads++;return Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(save.Snapshot,core);});view.ConfigureNavigation(()=>nav);}Bind();view.Render();Canvas.ForceUpdateCanvases();var panel=view.transform.Find("OutsideMiniMap");Assert.IsTrue(panel.gameObject.activeSelf);Assert.IsTrue(panel.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget));Assert.AreEqual(24,panel.GetComponentsInChildren<Image>().Count(i=>i.name.StartsWith("MiniMapCell_")));foreach(var text in panel.GetComponentsInChildren<Text>(true)){Assert.LessOrEqual(text.preferredHeight,text.rectTransform.rect.height,text.name);Assert.LessOrEqual(text.preferredWidth,text.rectTransform.rect.width,text.name);}Assert.IsFalse(panel.Find("MiniMapRecovery").gameObject.activeSelf);var color=panel.Find("MiniMapCell_2_1").GetComponent<Image>().color;Assert.AreNotEqual(color,panel.Find("MiniMapCell_0_1").GetComponent<Image>().color);
                var state=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(core.CaptureState());int writes=store.Writes;var selected=EventSystem.current.currentSelectedGameObject;for(int n=0;n<60;n++)view.Render();Assert.AreEqual(1,reads);Assert.IsTrue(panel.Find("MiniMapTarget").gameObject.activeSelf);nav.ToggleVisibility();view.Render();Assert.IsFalse(panel.Find("MiniMapTarget").gameObject.activeSelf);Assert.IsFalse(panel.Find("OutsideNavigation").gameObject.activeSelf);nav.ToggleVisibility();view.Render();Assert.IsTrue(panel.Find("MiniMapTarget").gameObject.activeSelf);Assert.AreEqual(writes,store.Writes);Assert.AreSame(selected,EventSystem.current.currentSelectedGameObject);CollectionAssert.AreEqual(state,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(core.CaptureState()));
                Assert.IsTrue(save.TryAcquireTrialOutsideMapItem(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId,out _));view.Render();Assert.AreEqual(2,reads);Assert.AreEqual(color,panel.Find("MiniMapCell_0_1").GetComponent<Image>().color);var recovery=panel.Find("MiniMapRecovery").GetComponent<Image>();Assert.IsTrue(recovery.gameObject.activeSelf);var gray=recovery.color;Assert.IsNull(save.Snapshot.OutsideDiscoveredRecoveryPoints);Assert.IsTrue(save.TryDiscoverOutsideRecoveryPoint(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId,out _));view.Render();Assert.AreNotEqual(gray,recovery.color);nav.Select("trial-find-recovery");view.Render();var navMark=panel.Find("MiniMapTarget").GetComponent<Image>();Assert.AreEqual(0,navMark.color.a);Assert.GreaterOrEqual(Mathf.Abs(navMark.GetComponentInChildren<Text>().rectTransform.anchoredPosition.x),18);
                core.TickFrame(1,true,true);for(int n=0;n<15;n++)core.TickFrame(1,false,true);state=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(core.CaptureState());view.Render();var marker=panel.Find("MiniMapPlayer").GetComponent<RectTransform>();Assert.That(marker.anchoredPosition.y,Is.InRange(-8f,8f));Assert.AreEqual(3,reads);CollectionAssert.AreEqual(state,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(core.CaptureState()));Assert.AreEqual(Quaternion.identity,panel.localRotation);
                Rect Bounds(Transform t){var c=new Vector3[4];((RectTransform)t).GetWorldCorners(c);return new Rect(c[0],c[2]-c[0]);}var bounds=Bounds(host.transform);var area=Bounds(panel);Assert.IsTrue(bounds.Contains(area.min)&&bounds.Contains(area.max));foreach(var name in new[]{"OutsideHeadingSurface","OutsidePlayerHp","OutsidePlayerPoise","OutsideMovementGuide","OutsideHandButton0","OutsideHandButton1","OutsideHandButton2","OutsideSelectionGuide"})Assert.IsFalse(area.Overlaps(Bounds(view.transform.Find(name))),name);
                var checkpoint=core.CaptureState();var alternate=core.CaptureState();alternate.TrialMapId=Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(core,new object[]{alternate});view.Render();Assert.AreEqual(4,reads);Assert.IsFalse(recovery.gameObject.activeSelf);Assert.AreNotEqual(color,panel.Find("MiniMapCell_0_1").GetComponent<Image>().color);typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(core,new object[]{checkpoint});view.Render();Assert.AreEqual(5,reads);Assert.IsTrue(recovery.gameObject.activeSelf);
                int oldReads=reads;view.ConfigureMiniMap(null,null);view.Render();Assert.IsFalse(panel.gameObject.activeSelf);Assert.AreEqual(oldReads,reads);Bind();view.Render();Assert.IsTrue(panel.gameObject.activeSelf);Assert.IsTrue(binding.ReturnToHub());Assert.IsFalse(panel.gameObject.activeSelf);Assert.DoesNotThrow(()=>view.Render());Assert.AreEqual(oldReads+1,reads);
            }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void 外界タメ入力は通常と明示Pauseのみ保持を認めfocusとhost初回では解放を発動しない(int mode)
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            var pt=typeof(GameplayPause);var stat=BindingFlags.Static|BindingFlags.NonPublic;var rel=pt.GetField("awaitRelease",stat);var res=pt.GetField("resumeFrames",stat);bool oldPause=GameplayPause.IsPaused;var oldRel=rel.GetValue(null);var oldRes=res.GetValue(null);GameplayPause.SetPaused(false);rel.SetValue(null,false);res.SetValue(null,0);
            try{using(var input=new UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                object driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);long freq=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+freq*10;typeof(UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var samples=(List<TimestampedLogicalInput>)typeof(UnityInputProvider).GetField("events",flags).GetValue(input);
                void Step(bool held){samples.Add(new TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasChargeHoldSample=true,AnyKey=held,ChargeHoldButton=held,BasicAttackButton=held}));now+=freq/60+1;runtime.Pump(driver,true);}
                if(mode!=4){Step(false);Step(false);}
                if(mode==1){runtime.SetCovered(driver,true);runtime.Pump(driver,false);Assert.IsTrue(runtime.ResumeDirectionAfterExplicitPause(driver));runtime.SetCovered(driver,false);rel.SetValue(null,true);}
                if(mode==2){runtime.ResetInputForFocus(driver,false);runtime.ResetInputForFocus(driver,true);}
                if(mode==3){runtime.Detach(driver);driver=new object();runtime.Attach(driver,input);}
                for(int n=0;n<65;n++)Step(true);
                if(mode<=1)Assert.GreaterOrEqual(model.ChargeFrames,60);else Assert.AreEqual(0,model.ChargeFrames);
                Step(false);Assert.AreEqual(mode<=1,model.NormalKick.IsCharged);if(mode>=2)Assert.IsFalse(model.IsAttacking);
            }}finally{GameplayPause.SetPaused(oldPause);rel.SetValue(null,oldRel);res.SetValue(null,oldRes);model.Dispose();}
        }
        [TestCase(false,0,1)] [TestCase(false,1,1)] [TestCase(false,2,1)] [TestCase(true,0,1)] [TestCase(true,1,1)] [TestCase(true,2,1)]
        [TestCase(false,0,-1)] [TestCase(false,1,-1)] [TestCase(false,2,-1)] [TestCase(true,0,-1)] [TestCase(true,1,-1)] [TestCase(true,2,-1)]
        public async Task 探索タメ中も待機移動ジャンプの既存アニメを継続する(bool fire,int mode,int facing)
        {
            var model=system.OutsideExploration;Assert.IsTrue(model.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));var actor=new Olca.Core.Domain.PlayerCharacter(100);if(fire)actor.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);model.TryInitializePlayer(()=>actor);model.EnableTrialNormalKick();
            var field=Resources.Load<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>();var sampler=new GameObject("Charge350Expected",typeof(SpriteRenderer));var sample=sampler.GetComponent<SpriteRenderer>();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));
            try{using(var costume=new Olca.Presentation.Effects.TrialFireBodyMotion())using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);if(facing<0)for(int n=0;n<120;n++)model.TickTraversal(1);model.TickTraversal(0);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var ring=view.GetComponentsInChildren<Olca.Presentation.Effects.ProvisionalCombatGraphic>(true).Single(v=>v.name=="OutsideProvisionalChargeRing");var seen=new HashSet<Sprite>();bool rise=false,fall=false;long initial=model.Core.Position;
                for(int n=0;n<120;n++){
                    model.TickTraversal(mode==0?0:facing,mode==2&&n==12,mode==2&&n>=12&&n<23);model.ProcessAttackInput(new Olca.Core.Logic.Integration.LogicalInput{HasChargeHoldSample=true,ChargeHoldButton=true},false,true,true,out _);var core=model.Core;Sprite expected;
                    if(core.IsGrounded){var clip=core.IsMoving?field.AuthoredMoveClip:field.AuthoredIdleClip;float time=(float)(((core.IsMoving?core.MovementFrame:core.IdleFrame)/60d)%clip.length);var curve=UnityEditor.AnimationUtility.GetObjectReferenceCurve(clip,UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(v=>v.type==typeof(SpriteRenderer)&&v.propertyName=="m_Sprite"));expected=curve.Last(v=>v.time<=time).value as Sprite;}else{expected=Math.Abs(core.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?field.AuthoredJumpApexSprite:core.VerticalVelocity>0?field.AuthoredJumpRiseSprite:field.AuthoredJumpFallSprite;rise|=core.VerticalVelocity>0;fall|=core.VerticalVelocity<0;}
                    expected=costume.Resolve(expected,actor);long frame=core.Frame;int beforeRenderHp=actor.CurrentHp;view.Render();Assert.AreEqual(beforeRenderHp,actor.CurrentHp);Assert.AreSame(expected,body.sprite,"frame "+n);Assert.IsTrue(ring.enabled);Assert.IsTrue(model.IsCharging);Assert.IsFalse(model.IsAttacking);Assert.AreEqual(frame,core.Frame);seen.Add(body.sprite);
                }
                Assert.GreaterOrEqual(model.ChargeFrames,60);Assert.Greater(seen.Count,1,"待機も静止ポーズへ固定しない");if(mode==0)Assert.AreEqual(initial,model.Core.Position);else Assert.Greater((model.Core.Position-initial)*facing,0);if(mode==2){Assert.IsTrue(rise);Assert.IsTrue(fall);}
            }}finally{UnityEngine.Object.DestroyImmediate(sampler);}
        }
        [TestCase(1920,1080)] [TestCase(1440,1080)] [TestCase(3840,2160)]
        public async Task 探索タメ表示は蓄積完了解放を区別し表示だけでは時計とHPを進めない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var ring=view.GetComponentsInChildren<Olca.Presentation.Effects.ProvisionalCombatGraphic>(true).Single(v=>v.name=="OutsideProvisionalChargeRing");var caption=view.GetComponentsInChildren<Text>(true).Single(v=>v.name=="OutsideProvisionalKickCaptionText");
                for(int n=0;n<60;n++){model.TickTraversal(0);model.ProcessAttackInput(new Olca.Core.Logic.Integration.LogicalInput{HasChargeHoldSample=true,ChargeHoldButton=true},false,true,true,out _);view.Render();Assert.IsTrue(ring.enabled);StringAssert.DoesNotContain("charge",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);StringAssert.Contains(n==59?"カンリョウ":"タメチュウ",caption.text);Assert.GreaterOrEqual(caption.rectTransform.rect.width,caption.preferredWidth);}
                long frame=model.Core.Frame;int hp=model.Player.CurrentHp;var sprite=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite;for(int n=0;n<30;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);Assert.AreSame(sprite,view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite);
                model.TickTraversal(0);Assert.IsTrue(model.ProcessAttackInput(new Olca.Core.Logic.Integration.LogicalInput{HasChargeHoldSample=true},false,true,true,out _));view.Render();Assert.IsFalse(ring.enabled);StringAssert.Contains("タメ",caption.text);StringAssert.Contains("ジュンビ",caption.text);Assert.AreEqual("trial_charge_ground_1",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);Assert.IsTrue(model.NormalKick.IsCharged);
                Assert.IsTrue(binding.ReturnToHub());Assert.IsFalse(ring.enabled);
            }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task 探索タメ解放直後でも死亡麻痺のけぞりは専用攻撃表示を中断する(int state)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,staggerDurationFrames:3));model.EnableTrialNormalKick();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);for(int n=0;n<60;n++){model.TickTraversal(0);model.ProcessAttackInput(new Olca.Core.Logic.Integration.LogicalInput{HasChargeHoldSample=true,ChargeHoldButton=true},false,true,true,out _);}
                model.TickTraversal(0);Assert.IsTrue(model.ProcessAttackInput(new Olca.Core.Logic.Integration.LogicalInput{HasChargeHoldSample=true},false,true,true,out _));binding.View.Render();StringAssert.StartsWith("trial_charge_",binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);
                if(state==0)model.Player.TakeDamage(1000);else if(state==1)model.Player.RestoreTimedAilments(new Olca.Core.Domain.PlayerTimedAilmentsDto{Paralysis=new Olca.Core.Domain.PlayerTimedAilmentDto{RemainingUnits=3000,DisplayFrames=60},Confusion=new Olca.Core.Domain.PlayerTimedAilmentDto(),Blindness=new Olca.Core.Domain.PlayerTimedAilmentDto()});else model.Player.TakeDamage(1,forceStagger:true);
                Assert.IsTrue(state==0?model.Player.IsDead:state==1?model.Player.IsParalyzed:model.Player.IsStaggered,"有効な状態が確定していることを先に確認する");
                long frame=model.Core.Frame;binding.View.Render();StringAssert.DoesNotStartWith("trial_charge_",binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);Assert.AreEqual(frame,model.Core.Frame);
            }
        }
        private GameObject host, events;
        private ResourceManager resources;
        private DialogManager dialogs;
        private ViewManager views;
        private SoundManager sounds;
        private GameSystem system;
        private int frame;
        private readonly object owner = new object();
        private readonly List<(EventSystem System, bool Enabled)> priorEvents = new List<(EventSystem, bool)>();
        private EventSystem priorCurrent;
        private Action viewCleanup;
        [SetUp] public void Setup()
        {
            priorCurrent = EventSystem.current;
            foreach (var value in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { priorEvents.Add((value,value.enabled)); value.enabled=false; }
            events = new GameObject("ManagedTestsEventSystem",typeof(EventSystem));
            var eventSystem=events.GetComponent<EventSystem>();
            // EditModeではMonoBehaviourのOnEnableが自動実行されないため、既存テストと同じ登録経路を通す。
            typeof(EventSystem).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(eventSystem,null);
            EventSystem.current=eventSystem;
            host = new GameObject("ManagedTests",typeof(RectTransform));
            resources = new ResourceManager(); dialogs = new DialogManager(()=>frame); views = new ViewManager(resources,dialogs); sounds = new SoundManager(host.transform);
            system = new GameSystem(new SaveManager(),resources,sounds,views,dialogs);
        }
        [TearDown] public void Cleanup()
        {
            viewCleanup?.Invoke();viewCleanup=null;
            system?.Dispose(); UnityEngine.Object.DestroyImmediate(host);
            if(events!=null)typeof(EventSystem).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(events.GetComponent<EventSystem>(),null);
            UnityEngine.Object.DestroyImmediate(events);
            foreach(var item in priorEvents)if(item.System!=null)item.System.enabled=item.Enabled;
            priorEvents.Clear(); if(priorCurrent!=null)EventSystem.current=priorCurrent;
        }
        private GameObject Panel(string name) { var value=new GameObject(name,typeof(RectTransform));value.transform.SetParent(host.transform,false);value.SetActive(false);return value; }
        private void Register(string key,Func<CancellationToken,Task<GameObject>> factory,Action<GameObject,ManagedScreenContext> show=null)
        { views.Register(owner,key,factory,show??((go,context)=>go.SetActive(true)),go=>{if(go!=null)go.SetActive(false);}); }
        private Button Button(GameObject parent,string name) { var value=new GameObject(name,typeof(RectTransform),typeof(Button));value.transform.SetParent(parent.transform,false);return value.GetComponent<Button>(); }
        private async Task<(ManagedScreenContext Context,GameObject Panel,Button Confirm,Button Cancel)> Screen()
        {
            ManagedScreenContext context=null;var screen=Panel("screen");var popup=Panel("popup");popup.transform.SetParent(screen.transform,false);
            var confirm=Button(popup,"confirm");var cancel=Button(popup,"cancel");
            Register("screen",ct=>Task.FromResult(screen),(go,ctx)=>{context=ctx;go.SetActive(true);});
            Assert.IsTrue((await views.ShowAsync("screen")).Succeeded);return(context,popup,confirm,cancel);
        }
        [Test] public void 保存済みマップ移動の案内はページ移動でも保存エラーと区別し再表示入口を保つ()
        {
            var panel=Panel("DoorNotice");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null,null,null,null);int opens=0;
            view.RenderDungeonSelection(Enumerable.Range(0,6).Select(i=>"試用"+i).ToArray(),Enumerable.Repeat(true,6).ToArray(),0,_=>{},()=>{},()=>{},false,null,()=>{},outsideExploration:()=>opens++,presentationNotice:"マップ移動は保存済みです。外界探索から再表示してください。");
            string Message()=>view.GetComponentsInChildren<Text>(true).Single(t=>t.name=="DungeonSelectionHelp").text;
            Button Control(string name)=>view.GetComponentsInChildren<Button>(true).Single(b=>b.name==name);
            var notice=Message();Assert.IsFalse(string.IsNullOrWhiteSpace(notice));Assert.IsTrue(Control("OutsideExplorationEntry").interactable);Control("NextDungeonPage").onClick.Invoke();Assert.AreEqual(notice,Message());Control("PreviousDungeonPage").onClick.Invoke();Assert.AreEqual(notice,Message());Control("OutsideExplorationEntry").onClick.Invoke();Assert.AreEqual(1,opens);
        }
        [TestCase(true,false)] [TestCase(false,false)] [TestCase(true,true)] [TestCase(false,true)]
        public void 入口の行き先focusは記憶した探索戻り先を上書きし通常Backでは維持する(bool entrance,bool otherPage)
        {
            var panel=Panel("EntranceFocus");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null,null,null,null);int opened=0,chosen=-1,confirmed=0;
            void Render(int selected)=>view.RenderDungeonSelection(Enumerable.Range(0,6).Select(n=>"試用"+n).ToArray(),Enumerable.Repeat(true,6).ToArray(),selected,n=>chosen=n,()=>confirmed++,()=>{},false,null,()=>{},outsideExploration:()=>opened++);
            Render(0);view.FocusMenuControl("OutsideExplorationEntry");Assert.AreEqual("OutsideExplorationEntry",EventSystem.current.currentSelectedGameObject.name);
            if(otherPage)view.GetComponentsInChildren<Button>().Single(b=>b.name=="NextDungeonPage").onClick.Invoke();
            Render(1);Assert.AreEqual("OutsideExplorationEntry",EventSystem.current.currentSelectedGameObject.name,"再描画だけでは記憶したfocusへ戻る");
            if(entrance){view.FocusMenuControl("ConfirmDungeonSelection");view.FocusMenuControl("DungeonChoice_1");}else view.FocusMenuControl("OutsideExplorationEntry");
            Assert.AreEqual(!entrance?"OutsideExplorationEntry":otherPage?"ConfirmDungeonSelection":"DungeonChoice_1",EventSystem.current.currentSelectedGameObject.name);
            EventSystem.current.currentSelectedGameObject.GetComponent<Button>().onClick.Invoke();Assert.AreEqual(entrance&&!otherPage?1:-1,chosen);Assert.AreEqual(entrance?0:1,opened);Assert.AreEqual(entrance&&otherPage?1:0,confirmed);
        }
        [TestCase(false)][TestCase(true)] public async Task 地図訪問の実Controllerは世代集合を分離しCore時計を戻しても六十frameで再試行する(bool newGeneration)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(Olca.Application.Dungeon.DungeonBattleController);var go=new GameObject("MapVisitController");go.SetActive(false);var controller=go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            void Call(string name)=>type.GetMethod(name,flags).Invoke(controller,null);
            var store=new DoorBoundaryStore();store.Profile.CheckpointGeneration=Guid.NewGuid().ToString("N");var progress=new Olca.Core.Domain.ClearReturnSession(store);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            try{using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Set("gameSystem",system);Set("clearReturnSession",progress);Set("outsideExploration",binding);Set("initialHub",true);Call("BindOutsideRecovery");
                void Step(int direction=0){model.Core.TickFrame(direction);model.AdvancePlayerFrame();}
                for(int i=0;i<180;i++)Step();store.Fail=true;Call("ObserveOutsideMapVisit");Assert.AreEqual(1,store.Attempts);Call("ObserveOutsideMapVisit");Assert.AreEqual(1,store.Attempts);Assert.IsFalse(progress.HasPendingSave);
                var state=new Olca.Core.Logic.Integration.OutsideExplorationCore(model.Core.Definition,model.Core.TraversalProfile).CaptureState();if(newGeneration)state.Position=18*60000;
                typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",flags).Invoke(model.Core,new object[]{state});
                if(newGeneration){store.Profile.CheckpointGeneration=Guid.NewGuid().ToString("N");Assert.IsTrue(progress.Reload());Call("BindOutsideRecovery");Assert.IsEmpty(model.CaptureMapVisits());Step();store.Fail=false;Call("ObserveOutsideMapVisit");Assert.AreEqual(1,store.Writes);var cells=progress.Snapshot.OutsideMapReveals.Single().Cells;Assert.IsTrue(cells.All(c=>c.X==4),"旧世代の開始セル2は混入しない");}
                else{store.Fail=false;for(int i=0;i<59;i++){Step();Call("ObserveOutsideMapVisit");}Assert.AreEqual(1,store.Attempts);Step();Call("ObserveOutsideMapVisit");Assert.AreEqual(1,store.Writes);Assert.AreEqual(2,progress.Snapshot.OutsideMapReveals.Single().Cells.Single().X);}
                var core=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());Call("ObserveOutsideMapVisit");CollectionAssert.AreEqual(core,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.IsNull(progress.Snapshot.OutsideRecoveryPointId);Assert.IsNull(progress.Snapshot.OutsideDiscoveredRecoveryPoints);Assert.IsNull(progress.Snapshot.OutsidePlayerSave);
            }}finally{Set("outsideExploration",null);Set("gameSystem",null);UnityEngine.Object.DestroyImmediate(go);}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)] public async Task 実Controllerの訪問保存は古い世代通知を捨て現在個体を回復移動させない(bool sameGeneration,bool fail)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(Olca.Application.Dungeon.DungeonBattleController);var go=new GameObject("RecoveryVisitController");go.SetActive(false);var controller=go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            void Observe()=>type.GetMethod("ObserveOutsideRecoveryVisit",flags).Invoke(controller,null);
            var store=new DoorBoundaryStore();store.Profile.CheckpointGeneration=Guid.NewGuid().ToString("N");var progress=new Olca.Core.Domain.ClearReturnSession(store);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(80);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            try{using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Set("gameSystem",system);Set("clearReturnSession",progress);Set("outsideExploration",binding);Set("initialHub",true);Set("recoveryVisitOwner",model);Set("recoveryVisitGeneration",sameGeneration?store.Profile.CheckpointGeneration:"old");
                void Walk(int direction){for(int i=0;i<20;i++){model.Core.TickFrame(direction);model.AdvancePlayerFrame();}}
                store.Fail=fail;Walk(-1);var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());Observe();Observe();Assert.AreEqual(sameGeneration?1:0,store.Attempts);Assert.AreEqual(sameGeneration&&!fail?1:0,store.Writes);Assert.AreEqual(sameGeneration&&!fail,progress.IsOutsideRecoveryPointDiscovered(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId));Assert.AreEqual(80,model.Player.CurrentHp);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.IsNull(progress.Snapshot.OutsideRecoveryPointId);Assert.IsNull(progress.Snapshot.OutsidePlayerSave);Assert.IsFalse(progress.HasPendingSave);
                if(fail)Assert.IsNotNull(type.GetField("recoveryVisitError",flags).GetValue(controller));store.Fail=false;Walk(1);Walk(-1);Observe();Assert.IsNull(type.GetField("recoveryVisitError",flags).GetValue(controller));Assert.AreEqual(1,store.Writes);Assert.IsTrue(progress.IsOutsideRecoveryPointDiscovered(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId));
            }}finally{Set("outsideExploration",null);Set("gameSystem",null);UnityEngine.Object.DestroyImmediate(go);}
        }
        [TestCase(1920f,1080f)][TestCase(3840f,2160f)][TestCase(1280f,960f)]
        public async Task 全体地図は保存セルと率を表示し別地図で誤移動せず旧callbackを遮断する(float width,float height)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);var core=system.OutsideExploration.Core;
            var p=new Olca.Core.Domain.ClearReturnProfile{OutsideMapReveals=new[]{new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=2,Y=0}}},new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=1,Y=0}}}},OutsideDiscoveredRecoveryPoints=new[]{Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId}};var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(p);int travel=0,closed=0;bool alive=true;OutsideFastTravelView view=null;var owner=new object();string key="trial-world-map-test";
            views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("WorldMapTest",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>true,()=>travel++,()=>closed++,()=>alive,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(p,core));}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Canvas.ForceUpdateCanvases();var confirm=view.transform.Find("ConfirmOutsideFastTravel").GetComponent<Button>();Assert.IsTrue(confirm.interactable);StringAssert.Contains("25%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);Assert.IsNotNull(view.transform.Find("MapGrid/Visited_2_0"));Assert.IsNotNull(view.transform.Find("MapGrid/CurrentPosition"));Assert.IsNotNull(view.transform.Find("MapGrid/DiscoveredRecovery"));
                Rect Area(Transform t){var rect=(RectTransform)t;var corners=new Vector3[4];rect.GetWorldCorners(corners);return new Rect(corners[0],corners[2]-corners[0]);}
                var outer=Area(host.transform);foreach(string name in new[]{"FastTravelTitle","MapChoice_0","MapExplorationRate","MapGrid","FastTravelMessage","ConfirmOutsideFastTravel","CancelOutsideFastTravel","MapInputGuide"}){var area=Area(view.transform.Find(name));Assert.IsTrue(outer.Contains(area.min)&&outer.Contains(area.max),name);}
                var legend=Area(view.transform.Find("MapGrid/MapLegend"));Assert.IsFalse(legend.Overlaps(Area(view.transform.Find("FastTravelMessage"))));
                view.transform.Find("MapChoice_1").GetComponent<Button>().onClick.Invoke();Assert.IsFalse(confirm.interactable);confirm.onClick.Invoke();Assert.AreEqual(0,travel);StringAssert.Contains("--%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);Assert.IsNull(view.transform.Find("MapGrid/CurrentPosition"));Assert.IsNull(view.transform.Find("MapGrid/DiscoveredRecovery"));
                view.transform.Find("MapChoice_0").GetComponent<Button>().onClick.Invoke();confirm.onClick.Invoke();Assert.AreEqual(1,travel);view.SetBusy(true,null);confirm.onClick.Invoke();Assert.AreEqual(1,travel);view.SetBusy(false,null);var old=confirm.onClick;views.CloseCurrent();old.Invoke();Assert.AreEqual(1,travel);Assert.IsTrue((await views.ShowAsync(key)).Succeeded);old.Invoke();Assert.AreEqual(1,travel);alive=false;confirm.onClick.Invoke();Assert.AreEqual(1,travel);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(p));Assert.AreEqual(0,closed);
            }finally{views.UnregisterOwner(owner);}
        }
        [Test] public async Task 全体地図は仮想パッドで地図を選び決定戻ると旧地図event遮断を保つ()
        {
            var prior=UnityInputProvider.ActiveGamepadProfile;var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var owner=new object();OutsideFastTravelView view=null;int closed=0,travel=0;string key="map-pad-test";
            var p=new Olca.Core.Domain.ClearReturnProfile{OutsideMapReveals=new[]{new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=1,Y=0}}}},OutsideDiscoveredRecoveryPoints=new[]{Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId}};
            using(var input=new UnityInputProvider()){
                Assert.IsTrue(input.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _));
                views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("MapPad",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>true,()=>travel++,()=>closed++,()=>true,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(p,system.OutsideExploration.Core));}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
                void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var k in keys)state=state.WithButton(k);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();typeof(OutsideFastTravelView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);}
                try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Canvas.ForceUpdateCanvases();State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp);Assert.AreEqual("MapChoice_0",EventSystem.current.currentSelectedGameObject.name);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight);Assert.AreEqual("MapChoice_1",EventSystem.current.currentSelectedGameObject.name);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);StringAssert.Contains("--%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);Assert.IsFalse(view.transform.Find("ConfirmOutsideFastTravel").GetComponent<Button>().interactable);Assert.AreEqual(0,travel);StringAssert.Contains("A /",view.transform.Find("MapInputGuide").GetComponent<Text>().text);
                    var old=view.transform.Find("MapChoice_1").GetComponent<Button>().onClick;State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.East);Assert.AreEqual(1,closed);views.CloseCurrent();Assert.IsTrue((await views.ShowAsync(key)).Succeeded);old.Invoke();StringAssert.Contains("25%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);views.UnregisterOwner(owner);Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(0,travel);
                }finally{views.UnregisterOwner(owner);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);Assert.IsTrue(input.TryRestoreGamepadProfile(prior,out _));}
            }
        }
        [TestCase(1920f,1080f)][TestCase(3840f,2160f)][TestCase(1280f,960f)]
        public async Task マップ入手UIは失敗で未開示を保ち再試行後も未訪問地点へ移動を許さない(float width,float height)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);var owner=new object();var store=new DoorBoundaryStore();store.Profile.OutsideMapReveals=new[]{new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=2,Y=0}}}};var save=new Olca.Core.Domain.ClearReturnSession(store);OutsideFastTravelView view=null;int travel=0;string key="map-item-ui";
            views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("MapItemUI",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>save.IsOutsideRecoveryPointDiscovered(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId),()=>travel++,()=>{},()=>true,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(save.Snapshot,system.OutsideExploration.Core),id=>{view.SetBusy(true,null);bool done=save.TryAcquireTrialOutsideMapItem(id,out var error);view.RefreshMapSnapshot(done?"マップ シュトク":error);});}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Canvas.ForceUpdateCanvases();var acquire=view.transform.Find("AcquireTrialMapItem").GetComponent<Button>();var confirm=view.transform.Find("ConfirmOutsideFastTravel").GetComponent<Button>();var color=view.transform.Find("MapGrid/Visited_2_0").GetComponent<Image>().color;store.Fail=true;acquire.onClick.Invoke();Assert.IsTrue(acquire.interactable);Assert.IsFalse(save.HasOutsideMapItem(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId));Assert.IsNull(view.transform.Find("MapGrid/UnvisitedRecovery"));store.Fail=false;acquire.onClick.Invoke();Assert.IsFalse(acquire.interactable);Assert.IsFalse(confirm.interactable);confirm.onClick.Invoke();Assert.AreEqual(0,travel);Assert.IsNotNull(view.transform.Find("MapGrid/UnvisitedRecovery"));Assert.IsNull(view.transform.Find("MapGrid/DiscoveredRecovery"));Assert.AreEqual(color,view.transform.Find("MapGrid/Revealed_2_0").GetComponent<Image>().color);Assert.AreEqual(24,view.transform.Find("MapGrid").GetComponentsInChildren<Image>().Count(im=>im.name.StartsWith("Revealed_")));StringAssert.Contains("0%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);Assert.AreEqual(1,save.Snapshot.OutsideMapReveals.Single().Cells.Length);Assert.IsNull(save.Snapshot.OutsideDiscoveredRecoveryPoints);Assert.IsFalse(save.HasPendingSave);int attempts=store.Attempts;acquire.onClick.Invoke();Assert.AreEqual(attempts,store.Attempts);
                Rect Area(Transform t){var corners=new Vector3[4];((RectTransform)t).GetWorldCorners(corners);return new Rect(corners[0],corners[2]-corners[0]);}var bounds=Area(host.transform);foreach(string name in new[]{"MapGrid","AcquireTrialMapItem","FastTravelMessage","MapInputGuide"}){var a=Area(view.transform.Find(name));Assert.IsTrue(bounds.Contains(a.min)&&bounds.Contains(a.max),name);}Assert.IsFalse(Area(view.transform.Find("MapGrid/MapLegend")).Overlaps(Area(acquire.transform)));Assert.IsFalse(Area(acquire.transform).Overlaps(Area(view.transform.Find("FastTravelMessage"))));
                var old=acquire.onClick;views.CloseCurrent();Assert.IsTrue((await views.ShowAsync(key)).Succeeded);old.Invoke();Assert.AreEqual(attempts,store.Attempts);Assert.IsTrue(save.TryDiscoverOutsideRecoveryPoint(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId,out _));view.RefreshMapSnapshot(null);Assert.IsTrue(confirm.interactable);Assert.IsNotNull(view.transform.Find("MapGrid/DiscoveredRecovery"));Assert.IsNull(view.transform.Find("MapGrid/UnvisitedRecovery"));StringAssert.Contains("25%",view.transform.Find("MapExplorationRate").GetComponent<Text>().text);views.UnregisterOwner(owner);Assert.DoesNotThrow(()=>old.Invoke());
            }finally{views.UnregisterOwner(owner);}
        }
        [Test] public async Task 仮想パッドから試用マップを取得しても戻る操作と未訪問移動禁止を守る()
        {
            var prior=UnityInputProvider.ActiveGamepadProfile;var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var owner=new object();var store=new DoorBoundaryStore();var save=new Olca.Core.Domain.ClearReturnSession(store);OutsideFastTravelView view=null;int closed=0;string key="map-item-pad";
            using(var input=new UnityInputProvider()){
                Assert.IsTrue(input.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _));views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("MapItemPad",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>false,()=>Assert.Fail("未訪問の移動"),()=>closed++,()=>true,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(save.Snapshot,system.OutsideExploration.Core),id=>{Assert.IsTrue(save.TryAcquireTrialOutsideMapItem(id,out _));view.RefreshMapSnapshot(null);});}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
                void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var k in keys)state=state.WithButton(k);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();typeof(OutsideFastTravelView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);}
                try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Canvas.ForceUpdateCanvases();State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp);Assert.AreEqual("AcquireTrialMapItem",EventSystem.current.currentSelectedGameObject.name);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.IsTrue(save.HasOutsideMapItem(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId));Assert.IsFalse(view.transform.Find("ConfirmOutsideFastTravel").GetComponent<Button>().interactable);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.East);Assert.AreEqual(1,closed);}
                finally{views.UnregisterOwner(owner);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);Assert.IsTrue(input.TryRestoreGamepadProfile(prior,out _));}
            }
        }
        [Test] public async Task 同じ地図入手ボタンはイベント時の表示地図だけを取得し再表示前のeventを拒否する()
        {
            var owner=new object();var initial=new Olca.Core.Domain.ClearReturnProfile{OutsideMapReveals=new[]{new Olca.Core.Domain.OutsideMapRevealData{MapId=Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId,Cells=new[]{new Olca.Core.Domain.OutsideMapCellData{X=1,Y=0}}}}};var store=new DoorBoundaryStore{Profile=initial.Copy()};var save=new Olca.Core.Domain.ClearReturnSession(store);OutsideFastTravelView view=null;int calls=0;bool alive=true;string key="map-item-switch";
            views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null).Component<OutsideFastTravelView>("MapSwitchItem",v=>{view=v;v.Initialize(resources.GetShared<Font>("OlcaUI/MenuText"),()=>false,()=>Assert.Fail("移動禁止"),()=>{},()=>alive,()=>Olca.Core.Logic.Integration.OutsideTrialMapOverview.Build(save.Snapshot,system.OutsideExploration.Core),id=>{calls++;Assert.IsTrue(save.TryAcquireTrialOutsideMapItem(id,out _));view.RefreshMapSnapshot(null);});}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);var old=view.transform.Find("AcquireTrialMapItem").GetComponent<Button>().onClick;view.transform.Find("MapChoice_1").GetComponent<Button>().onClick.Invoke();old.Invoke();Assert.IsTrue(save.HasOutsideMapItem(Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId));Assert.IsFalse(save.HasOutsideMapItem(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId));Assert.AreEqual(1,calls);view.transform.Find("MapChoice_0").GetComponent<Button>().onClick.Invoke();old.Invoke();Assert.IsTrue(save.HasOutsideMapItem(Olca.Core.Logic.Integration.OutsideMapRevealTrial.HomeMapId));Assert.AreEqual(2,calls);
                views.CloseCurrent();store=new DoorBoundaryStore{Profile=initial.Copy()};save=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue((await views.ShowAsync(key)).Succeeded);Assert.IsTrue(view.transform.Find("AcquireTrialMapItem").GetComponent<Button>().interactable);old.Invoke();Assert.AreEqual(2,calls);Assert.AreEqual(0,store.Writes);alive=false;view.transform.Find("AcquireTrialMapItem").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(2,calls);views.UnregisterOwner(owner);Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(2,calls);
            }finally{views.UnregisterOwner(owner);}
        }
        [TestCase(false)][TestCase(true)] public async Task ファストトラベル確認は未発見を選べず保存中と閉鎖後のcallbackを止める(bool known)
        {
            var owner=new object();int travel=0,closed=0;bool alive=true;OutsideFastTravelView view=null;string key="outside-fast-travel-test";
            views.Register(owner,key,ct=>Task.FromResult(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null).Component<OutsideFastTravelView>("FastTravelTest",v=>{view=v;v.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),()=>known,()=>travel++,()=>closed++,()=>alive);}).gameObject),(panel,context)=>view.Open(context),panel=>view.Hide());
            try{Assert.IsTrue((await views.ShowAsync(key)).Succeeded);var confirm=view.transform.Find("ConfirmOutsideFastTravel").GetComponent<Button>();Assert.AreEqual(known,confirm.interactable);confirm.onClick.Invoke();Assert.AreEqual(known?1:0,travel);view.SetBusy(true,null);confirm.onClick.Invoke();Assert.AreEqual(known?1:0,travel);view.SetBusy(false,"ホゾン シッパイ");StringAssert.Contains("ホゾン",view.transform.Find("FastTravelMessage").GetComponent<Text>().text);view.transform.Find("CancelOutsideFastTravel").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(1,closed);var oldConfirm=confirm.onClick;views.CloseCurrent();oldConfirm.Invoke();Assert.AreEqual(known?1:0,travel);Assert.IsTrue((await views.ShowAsync(key)).Succeeded);oldConfirm.Invoke();Assert.AreEqual(known?1:0,travel);alive=false;confirm.onClick.Invoke();Assert.AreEqual(known?1:0,travel);var ended=confirm.onClick;views.UnregisterOwner(owner);alive=true;Assert.IsTrue(view==null);Assert.DoesNotThrow(()=>ended.Invoke());Assert.AreEqual(known?1:0,travel);Assert.AreEqual(1,closed);}
            finally{views.UnregisterOwner(owner);}
        }
        private sealed class DoorBoundaryStore : Olca.Core.Domain.IClearReturnStore
        {
            public Olca.Core.Domain.ClearReturnProfile Profile=new Olca.Core.Domain.ClearReturnProfile();public Action Committed;public bool Fail;public int Attempts,Writes;
            public Olca.Core.Domain.ClearReturnProfile Read(out string error){error=null;return Profile.Copy();}
            public bool TryWrite(long expected,Olca.Core.Domain.ClearReturnProfile value,out string error){Attempts++;error=Fail?"fixture door save":null;if(Fail)return false;Assert.AreEqual(Profile.Revision,expected);Profile=value.Copy();Writes++;Committed?.Invoke();return true;}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public async Task 扉の実Controllerは再生成待機中の無効化旧Host読込失敗と保存再試行を分離する(int boundary)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(Olca.Application.Dungeon.DungeonBattleController);
            var controllerObject=new GameObject("DoorBoundaryController");controllerObject.SetActive(false);var controller=controllerObject.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var directory=Path.Combine(Path.GetTempPath(),"olca-door-boundary-"+Guid.NewGuid().ToString("N"));var waiting=new TaskCompletionSource<GameObject>();OutsideExplorationPresentationBinding binding=null,replacement=null;GameObject late=null;var provider=new UnityInputProvider();int creates=0;
            void Set(string key,object value)=>type.GetField(key,flags).SetValue(controller,value);
            object Get(string key)=>type.GetField(key,flags).GetValue(controller);
            void Call(string key)=>type.GetMethod(key,flags).Invoke(controller,null);
            Task Travel()=>((Cysharp.Threading.Tasks.UniTask)type.GetMethod("TravelOutsideMap",flags).Invoke(controller,null)).AsTask();
            GameObject Create()=>Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources);
            bool Eligible()=>(bool)type.GetProperty("OutsideHubEligible",flags).GetValue(controller);
            var factory=new Olca.Application.Battle.SimpleCardFactory();
            var cards=factory.CreateStarterDeck().GetAllCards().Concat(Enumerable.Repeat(factory.CollectionCards.Single(c=>c.Id=="sword"&&c.ExceedLevel==Olca.Core.Domain.ExceedLevel.Lv1),4)).Concat(Enumerable.Repeat(factory.CollectionCards.Single(c=>c.Id=="bomb"&&c.ExceedLevel==Olca.Core.Domain.ExceedLevel.Lv1),4)).Concat(Enumerable.Repeat(factory.CollectionCards.Single(c=>c.Id=="sword"&&c.ExceedLevel==Olca.Core.Domain.ExceedLevel.Lv2),2)).ToArray();
            var store=new DoorBoundaryStore();store.Profile.CheckpointGeneration=Guid.NewGuid().ToString("N");store.Profile.Owned=cards.GroupBy(c=>(c.Id,c.ExceedLevel)).Select(g=>new Olca.Core.Domain.OwnedCardCount{Id=g.First().Id,Name=g.First().Name,Rarity=g.First().Rarity,Level=g.First().ExceedLevel,Count=g.Count(),DefinitionHash=Olca.Core.Domain.CollectionDisplay.DefinitionHash(g.First())}).ToArray();var progress=new Olca.Core.Domain.ClearReturnSession(store);
            var model=system.OutsideExploration;var owned=cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var random=new Olca.Core.Logic.Battle.SeededBattleRandom(239);
            try{
                var session=system.BeginSession(controller,directory,Path.Combine(directory,"checkpoints"),Path.Combine(directory,"progress"));
                var checkpoint=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(8,30,2),new Olca.Core.Domain.Deck(cards.ToList()));
                using(var decks=new Olca.Core.Domain.ExplorationDeckSession(session.Storage.ExplorationDecks(store.Profile.CheckpointGeneration))){Assert.IsTrue(decks.Save(null,"試用",cards.Select(c=>new Olca.Core.Domain.SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=Olca.Core.Domain.CollectionDisplay.DefinitionHash(c)}).ToArray(),store.Profile));var selected=decks.Snapshot.Decks.Single().Id;Assert.IsTrue(decks.Select(selected,store.Profile));model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Assert.IsTrue(model.QueueSelectedDeck(selected,cards,owned,random,out _));}
                for(int i=0;i<20;i++){model.Core.TickFrame(1);model.AdvanceDeckLocation();}
                typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreRecoveryPosition",flags).Invoke(model.Core,new object[]{Olca.Core.Logic.Integration.OutsideTrialRoute.HomeDoorX});
                Set("gameSystem",system);Set("gameSystemSession",session);Set("clearReturnSession",progress);Set("memoryCheckpoints",checkpoint);Set("inputProvider",provider);Set("initialHub",true);Set("clearReturnVisible",true);Set("outsideLandingRoute",true);
                Set("panelFactory",new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null));var panels=new HostPanelBindings(controller,host.transform,resources,views);Set("hostPanels",panels);Call("RegisterReturnViews");Set("dungeonChoices",Activator.CreateInstance(type.GetField("dungeonChoices",flags).FieldType));
                controllerObject.SetActive(true);
                binding=new OutsideExplorationPresentationBinding(views,model,ct=>++creates==2?waiting.Task:boundary==2&&creates==3?Task.FromException<GameObject>(new IOException("fixture repeated view load")):Task.FromResult(Create()),()=>controller!=null&&controller.isActiveAndEnabled&&Eligible(),()=>{});Set("outsideExploration",binding);Assert.IsTrue((await binding.OpenAsync()).Succeeded);
                var first=binding.View;var before=model.Cards.CaptureState(checkpoint.Definitions).Key();var actor=model.Player;store.Committed=()=>UnityEngine.Object.DestroyImmediate(first.gameObject);
                if(boundary==3){store.Fail=true;await Travel();Assert.AreEqual(0,store.Writes);Assert.AreSame(first,binding.View);Assert.AreSame(actor,model.Player);Assert.AreEqual(before,model.Cards.CaptureState(checkpoint.Definitions).Key());Assert.IsTrue(model.IsPresented);Assert.IsNull(progress.Error);store.Fail=false;}
                var travel=Travel();Assert.IsFalse(travel.IsCompleted);Assert.AreEqual(1,store.Writes);Assert.AreEqual(Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId,model.Core.TrialMapId);Assert.IsTrue((bool)Get("outsideMapTravelBusy"));
                if(boundary==0){controller.enabled=false;Call("OnDisable");late=Create();waiting.SetResult(late);await travel;Assert.IsFalse((bool)Get("outsideMapTravelBusy"));Assert.IsTrue((bool)Get("outsideMapTravelNeedsBind"));controller.enabled=true;
                    // このfixtureにはcheckpoint status画面を登録しない。外界の実OnEnable復帰だけを実行する。
                    Set("hostPanels",null);Call("OnEnable");Set("hostPanels",panels);for(int i=0;i<40&&!binding.IsOpen;i++)await Task.Yield();Assert.IsTrue(binding.IsOpen);Assert.IsFalse((bool)Get("outsideMapTravelNeedsBind"));Assert.IsTrue(binding.View.CanPlay);
                }else if(boundary==1){controller.StopManagedHost();var next=system.BeginSession(new object(),directory,Path.Combine(directory,"checkpoints2"),Path.Combine(directory,"progress"));replacement=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Create()),()=>true,()=>{});Assert.IsTrue((await replacement.OpenAsync()).Succeeded);var current=replacement.View;var hand=model.Cards.CaptureState(checkpoint.Definitions).Key();late=Create();waiting.SetResult(late);await travel;Assert.AreSame(current,replacement.View);Assert.IsTrue(current.CanPlay);Assert.AreEqual(hand,model.Cards.CaptureState(checkpoint.Definitions).Key());Assert.IsFalse((bool)Get("outsideMapTravelBusy"));Assert.IsFalse((bool)Get("outsideMapTravelNeedsBind"));Assert.IsNull(Get("clearReturnError"));Assert.IsTrue(late==null);next.Dispose();
                }else if(boundary==2){waiting.SetException(new IOException("fixture view load"));await travel;Assert.IsFalse((bool)Get("outsideMapTravelBusy"));Assert.IsTrue(Eligible(),"View読込失敗を保存エラーとして外界入口を永久遮断しない");var hub=(Olca.Presentation.Dungeon.ClearReturnView)Get("clearReturnView");var retry=hub.GetComponentsInChildren<Button>(true).Single(b=>b.name=="OutsideExplorationEntry");Assert.IsTrue(retry.gameObject.activeSelf);retry.onClick.Invoke();for(int i=0;i<40&&(bool)Get("outsideExplorationOpening");i++)await Task.Yield();Assert.IsTrue(Eligible(),"再読込の連続失敗でも再試行を保つ");Assert.IsFalse(binding.IsOpen);Assert.AreEqual(1,store.Writes);hub=(Olca.Presentation.Dungeon.ClearReturnView)Get("clearReturnView");retry=hub.GetComponentsInChildren<Button>(true).Single(b=>b.name=="OutsideExplorationEntry");retry.onClick.Invoke();for(int i=0;i<40&&!binding.IsOpen;i++)await Task.Yield();Assert.IsTrue(binding.IsOpen);Assert.IsTrue(binding.View.CanPlay);Assert.AreEqual(1,store.Writes);
                }else{late=Create();waiting.SetResult(late);await travel;Assert.IsFalse((bool)Get("outsideMapTravelBusy"));Assert.IsTrue(binding.IsOpen);Assert.IsTrue(binding.View.CanPlay);Assert.AreEqual(2,store.Attempts);}
                Assert.AreEqual(Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId,model.Core.TrialMapId);Assert.AreEqual(30,model.Cards.TotalCount);Assert.IsFalse(progress.HasPendingSave);
            }finally{if(!waiting.Task.IsCompleted)waiting.TrySetCanceled();replacement?.Dispose();binding?.Dispose();controller.StopManagedHost();UnityEngine.Object.DestroyImmediate(controllerObject);if(late!=null)UnityEngine.Object.DestroyImmediate(late);provider.Dispose();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [Test] public async Task 試用扉は最新の可否と表示世代で操作し覆い旧callbackと非表示を拒否する()
        {
            var model=system.OutsideExploration;int calls=0;bool ready=true;var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreRecoveryPosition",flags).Invoke(model.Core,new object[]{Olca.Core.Logic.Integration.OutsideTrialRoute.HomeDoorX});
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.ConfigureMapDoor(()=>calls++,()=>ready,()=>"シヨウ マップ イドウ（カリ）\nEnter デ シラベル");view.Render();
                var button=view.transform.Find("OutsideMapDoor").GetComponent<Button>();Assert.IsTrue(button.gameObject.activeInHierarchy);Assert.AreEqual(Navigation.Mode.None,button.navigation.mode);Assert.AreEqual(Selectable.Transition.ColorTint,button.transition);Assert.Greater(button.GetComponentInChildren<Text>().color.r,.9f);Assert.Less(button.colors.normalColor.r,.1f);Assert.IsFalse(view.transform.Find("OutsideMapDoorMarker").GetComponent<Image>().raycastTarget);
                var old=button.onClick;EventSystem.current.SetSelectedGameObject(button.gameObject);old.Invoke();Assert.AreEqual(1,calls);Assert.IsNull(EventSystem.current.currentSelectedGameObject);
                ready=false;old.Invoke();Assert.AreEqual(1,calls);ready=true;view.SetCovered(true);old.Invoke();Assert.AreEqual(1,calls);
                Assert.IsTrue((await binding.ReopenAfterMapTravelAsync()).Succeeded);old.Invoke();Assert.AreEqual(1,calls);view=binding.View;view.ConfigureMapDoor(()=>calls++,()=>ready,()=>"イドウ");view.Render();view.transform.Find("OutsideMapDoor").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(2,calls);
                typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("SetRecoveryMap",flags).Invoke(model.Core,new object[]{Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint});view.Render();Assert.IsTrue(view.transform.Find("OutsideTrialTargetNamePlate").gameObject.activeSelf);Assert.IsTrue(view.transform.Find("OutsideTrialTargetName").GetComponent<Text>().enabled);Assert.IsTrue(view.transform.Find("OutsideTrialTargetNamePlate").GetComponent<Button>().interactable);
                Assert.IsTrue(binding.ReturnToHub());old.Invoke();Assert.AreEqual(2,calls);
            }
        }
        [TestCase(1920f,1080f)][TestCase(3840f,2160f)][TestCase(1280f,960f)]
        public async Task 外界回復地点は空中の重なる詳細だけ隠し目印と着地後の操作を保つ(float width,float height)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var state=model.Core.CaptureState();state.Position=6*60000;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});int calls=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;void Configure(){view.ConfigureRecovery(()=>calls++,()=>model.Core.IsGrounded,()=>"ケンショウ カイフク（カリ）");view.Render();}Configure();var button=view.transform.Find("OutsideRecoveryPoint").GetComponent<Button>();var marker=view.transform.Find("OutsideRecoveryMarker").GetComponent<Image>();var actor=view.transform.Find("OutsideEntryPlayer").GetComponent<RectTransform>();Assert.IsFalse(marker.raycastTarget);Assert.IsTrue(marker.gameObject.activeSelf);Assert.IsTrue(button.gameObject.activeSelf);var old=button.onClick;old.Invoke();Assert.AreEqual(1,calls);
                Rect Area(RectTransform r){var c=new Vector3[4];r.GetWorldCorners(c);return Rect.MinMaxRect(c.Min(x=>x.x),c.Min(x=>x.y),c.Max(x=>x.x),c.Max(x=>x.y));}
                int hidden=0;model.Core.TickFrame(0,true,true);
                for(int n=0;n<100&&!model.Core.IsGrounded;n++){
                    long frame=model.Core.Frame,x=model.Core.Position,y=model.Core.Height;view.Render();bool overlap=Area((RectTransform)button.transform).Overlaps(Area(actor));Assert.AreEqual(!overlap,button.gameObject.activeSelf);if(overlap)hidden++;Assert.IsTrue(marker.gameObject.activeSelf);Assert.IsFalse(button.interactable);old.Invoke();Assert.AreEqual(1,calls);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);Assert.AreEqual(y,model.Core.Height);model.Core.TickFrame(0,false,n<10);
                }
                Assert.Greater(hidden,0);Assert.IsTrue(model.Core.IsGrounded);view.Render();Assert.IsTrue(button.gameObject.activeSelf);Assert.IsTrue(button.interactable);button.onClick.Invoke();Assert.AreEqual(2,calls);Assert.AreEqual(100,model.Player.CurrentHp);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(marker.gameObject.activeSelf);Assert.IsTrue((await binding.OpenAsync()).Succeeded);view=binding.View;Configure();old.Invoke();Assert.AreEqual(2,calls);view.transform.Find("OutsideRecoveryPoint").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(3,calls);
            }
        }

        [TestCase(1920f,1080f,0)][TestCase(1920f,1080f,1)][TestCase(1920f,1080f,2)][TestCase(1920f,1080f,3)]
        [TestCase(3840f,2160f,0)][TestCase(3840f,2160f,1)][TestCase(3840f,2160f,2)][TestCase(3840f,2160f,3)]
        [TestCase(1280f,960f,0)][TestCase(1280f,960f,1)][TestCase(1280f,960f,2)][TestCase(1280f,960f,3)]
        public async Task 外界補給詳細は空中で本体と交差する時だけ退避し着地後の操作と文言を保持する(float width,float height,int index)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var state=model.Core.CaptureState();state.Position=index==0?Olca.Core.Logic.Integration.OutsideTrialRoute.SupplyX:index==1?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonSupplyX:index==2?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonTrialX:Olca.Core.Logic.Integration.OutsideTrialRoute.CardSupplyX;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});int calls=0;string message="シヨウ ハコ\nミウケトリ";
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Action claim=()=>calls++;Func<bool> ready=()=>model.Core.IsGrounded;
                if(index==0)view.ConfigureSupply(claim,ready,()=>message);else if(index==1)view.ConfigurePoisonSupply(claim,ready,()=>message);else if(index==2)view.ConfigureTrialPoison(claim,ready,()=>message);else view.ConfigureCardSupply(claim,ready,()=>message);
                var button=view.transform.Find(index==0?"OutsideSupplyChest":index==1?"OutsidePoisonSupplyChest":index==2?"OutsideTrialPoisonPoint":"OutsideCardSupplyChest").GetComponent<Button>();var actor=view.transform.Find("OutsideEntryPlayer").GetComponent<RectTransform>();
                Rect Area(RectTransform q){var c=new Vector3[4];q.GetWorldCorners(c);return Rect.MinMaxRect(c.Min(x=>x.x),c.Min(x=>x.y),c.Max(x=>x.x),c.Max(x=>x.y));}
                for(int cycle=0;cycle<2;cycle++){
                    view.Render();Assert.IsTrue(button.gameObject.activeSelf);var old=button.onClick;var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));int hidden=0;
                    model.Core.TickFrame(0,true,true);for(int n=0;n<100&&!model.Core.IsGrounded;n++){long frame=model.Core.Frame;view.Render();bool overlap=Area((RectTransform)button.transform).Overlaps(Area(actor));Assert.AreEqual(!overlap,button.gameObject.activeSelf);if(overlap)hidden++;Assert.IsFalse(button.interactable);old.Invoke();Assert.AreEqual(cycle,calls);view.Render();Assert.AreEqual(frame,model.Core.Frame);model.Core.TickFrame(0,false,n<10);}
                    Assert.Greater(hidden,0);view.Render();Assert.IsTrue(button.gameObject.activeSelf);Assert.IsTrue(button.interactable);StringAssert.Contains(cycle==0?"ミウケトリ":"ウケトリズミ",button.GetComponentInChildren<Text>().text);button.onClick.Invoke();Assert.AreEqual(cycle+1,calls);message="シヨウ ハコ\nウケトリズミ";
                }
                Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }

        [TestCase(1920f,1080f,false,false)] [TestCase(1920f,1080f,true,false)] [TestCase(1920f,1080f,false,true)] [TestCase(1920f,1080f,true,true)]
        [TestCase(3840f,2160f,false,false)] [TestCase(3840f,2160f,true,false)] [TestCase(3840f,2160f,false,true)] [TestCase(3840f,2160f,true,true)]
        [TestCase(1280f,960f,false,false)] [TestCase(1280f,960f,true,false)] [TestCase(1280f,960f,false,true)] [TestCase(1280f,960f,true,true)]
        public async Task 高い足場の補給詳細は利用不可で本体を覆う時だけ隠れ地面へ戻す(float width,float height,bool fire,bool usable)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            void Position(bool high){var state=model.Core.CaptureState();state.Position=16*60000;state.Height=high?60000:0;state.SupportId=high?"validation-top":"floor";typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});}
            Position(true);int calls=0;Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.ConfigurePoisonSupply(()=>calls++,()=>usable||model.Core.Height==0,()=>"ホキュウバコ（カリ）\nドクケシ2コ");var button=view.transform.Find("OutsidePoisonSupplyChest").GetComponent<Button>();var old=button.onClick;var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());
                for(int n=0;n<3;n++){view.Render();Assert.AreEqual(usable,button.gameObject.activeSelf);CollectionAssert.AreEqual(bytes,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));}old.Invoke();Assert.AreEqual(usable?1:0,calls);
                Position(false);view.Render();Assert.IsTrue(button.gameObject.activeSelf);Assert.IsTrue(button.interactable);button.onClick.Invoke();Assert.AreEqual(usable?2:1,calls);
            }
        }
        [TestCase(1920f,1080f)] [TestCase(3840f,2160f)] [TestCase(1280f,960f)]
        public async Task 爆風maskは壁の発射側へ切り反転と飛翔へのpool再利用で残らない(float width,float height)
        {
            float scale=Mathf.Sqrt(width/1920f*height/1080f);((RectTransform)host.transform).sizeDelta=new Vector2(width/scale,height/scale);var model=system.OutsideExploration;var p=Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true);Assert.IsTrue(model.TryConfigureTraversal(new Olca.Core.Logic.Integration.OutsideTraversalProfile("test-mask19",p.DashSpeedPerSecond,p.DashFrames,p.DashCooldownFrames,p.DashResetFrames,p.DashJumpSpeedPerSecond,p.BodyHalfWidth,p.BodyHeight,p.WallSlidePerFrame,p.WallKickVelocity,p.WallKickSpeedPerSecond,p.WallKickPushFrames,p.WallKickMotionFrames,true,new Olca.Core.Logic.Integration.OutsideValidationWall("mask-wall",19*60000,0,600000))));model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var blasts=(List<Olca.Core.Logic.Integration.OutsideBombBlastData>)typeof(Olca.Core.Logic.Integration.OutsideExplorationSession).GetField("bombBlasts",flags).GetValue(model);var blast=new Olca.Core.Logic.Integration.OutsideBombBlastData{SourceX=17*60000,X=19*60000,Height=0,Frame=0};blasts.Add(blast);blasts.Add(new Olca.Core.Logic.Integration.OutsideBombBlastData{SourceX=17*60000,X=19*60000,Height=0,Frame=0});
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.Render();Canvas.ForceUpdateCanvases();var wall=(RectTransform)view.transform.Find("OutsideValidationWall-mask-wall");var parts=view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").ToArray();Assert.AreEqual(2,parts.Length);var ids=parts.Select(i=>i.GetInstanceID()).ToArray();Assert.IsTrue(parts.All(i=>i.sprite.name=="trial_bomb_blast_0"));
                foreach(var image in parts){Assert.IsFalse(image.raycastTarget);var mask=image.transform.parent.GetComponent<RectMask2D>();Assert.IsTrue(mask.enabled);if(image.enabled)Assert.LessOrEqual(((RectTransform)mask.transform).anchoredPosition.x+((RectTransform)mask.transform).rect.xMax,wall.anchoredPosition.x+.01f);}
                foreach(var value in blasts)value.SourceX=21*60000;view.Render();Canvas.ForceUpdateCanvases();CollectionAssert.AreEqual(ids,view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").Select(i=>i.GetInstanceID()).ToArray());foreach(var image in parts)if(image.enabled){var clip=(RectTransform)image.transform.parent;Assert.GreaterOrEqual(clip.anchoredPosition.x+clip.rect.xMin,wall.anchoredPosition.x-.01f);}
                for(int n=0;n<3;n++)model.Core.TickFrame(0);var stable=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.Render();Assert.IsTrue(parts.All(i=>i.sprite.name=="trial_bomb_blast_3"));Assert.That(parts[0].color.a,Is.EqualTo(.45f).Within(.0001f));CollectionAssert.AreEqual(stable,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));
                blasts.Clear();var flights=(List<Olca.Core.Logic.Integration.OutsideBombFlightData>)typeof(Olca.Core.Logic.Integration.OutsideExplorationSession).GetField("bombs",flags).GetValue(model);var markerTerrain=Olca.Core.Logic.Integration.OutsideBombTerrainData.Create(model.Core,17*60000,1,0);flights.Add(new Olca.Core.Logic.Integration.OutsideBombFlightData{Launch=new Olca.Core.Logic.Integration.OutsideBombLaunchData{DefinitionId=Olca.Core.Logic.Integration.OutsideBombLaunchData.TerrainDefinition,X=17*60000,Facing=1,Terrain=markerTerrain},ActionStartedAt=model.Core.Frame,FiredAt=model.Core.Frame});view.Render();var all=view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").ToArray();Assert.AreEqual(2,all.Length);CollectionAssert.AreEqual(ids,all.Select(i=>i.GetInstanceID()).ToArray());Assert.AreEqual(1,all.Count(i=>i.enabled));Assert.AreEqual(Olca.Presentation.Field.SharedAttackPropCatalog.Bomb,all.Single(i=>i.enabled).sprite.name);Assert.IsFalse(all.Single(i=>i.enabled).transform.parent.GetComponent<RectMask2D>().enabled);
                flights.Clear();var terrain=Olca.Core.Logic.Integration.OutsideBombTerrainData.Create(model.Core,30000,-1,0);Assert.IsTrue(terrain.Dissipates);flights.Add(new Olca.Core.Logic.Integration.OutsideBombFlightData{Launch=new Olca.Core.Logic.Integration.OutsideBombLaunchData{DefinitionId=Olca.Core.Logic.Integration.OutsideBombLaunchData.TerrainDefinition,X=30000,Facing=-1,Terrain=terrain},FiredAt=0});long frame=model.Core.Frame;view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.IsTrue(parts[0].enabled);Assert.IsFalse(parts[0].transform.parent.GetComponent<RectMask2D>().enabled);Assert.IsTrue(parts.Skip(1).All(i=>!i.enabled));
                views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue(all.All(i=>!i.enabled&&i.sprite==null));
            }
        }
        [Test] public async Task 外界店の案内は地名と隣接地点を隠さず覆いと旧世代の操作を拒否する()
        {
            var model=system.OutsideExploration;int calls=0;Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.ConfigureShop(()=>calls++,()=>true,()=>"シヨウ ショップ（カリ）\nEnter デ ハイル");view.ConfigureRecovery(()=>{},()=>false,()=>"カイフク");view.ConfigureCardSupply(()=>{},()=>false,()=>"カード");view.Render();var shop=view.GetComponentsInChildren<Button>(true).Single(v=>v.name=="OutsideShopPoint");Rect Area(RectTransform rect){var corners=new Vector3[4];rect.GetWorldCorners(corners);return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);}Assert.IsTrue(shop.gameObject.activeInHierarchy);Assert.IsFalse(Area((RectTransform)shop.transform).Overlaps(Area(view.transform.Find("OutsideMapName").GetComponent<RectTransform>())));for(int i=0;i<180;i++)model.Core.TickFrame(0);view.Render();foreach(var name in new[]{"OutsideRecoveryPoint","OutsideCardSupplyChest"})Assert.IsFalse(Area((RectTransform)shop.transform).Overlaps(Area(view.transform.Find(name).GetComponent<RectTransform>())));var old=shop.onClick;old.Invoke();Assert.AreEqual(1,calls);view.SetCovered(true);old.Invoke();Assert.AreEqual(1,calls);binding.ReturnToHub();old.Invoke();Assert.AreEqual(1,calls);
            }
        }

        [Test] public async Task 外界店の会話B保持は即取消せず解放後の新Bだけで一度閉じる()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var store=new Shop233Store();var session=new Olca.Core.Domain.ClearReturnSession(store);OutsideShopView shop=null;int closed=0;
            void State(bool held){UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,held?new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.East):new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();if(shop!=null)typeof(OutsideShopView).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(shop,null);}
            try{State(true);Register("shop233",ct=>{var panel=Panel("Shop233Pad");shop=panel.AddComponent<OutsideShopView>();shop.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),session,Olca.Core.Domain.OutsideShopDefinition.Trial,()=>{closed++;views.CloseCurrent();});return Task.FromResult(panel);},(panel,context)=>shop.Open(context));Assert.IsTrue((await views.ShowAsync("shop233")).Succeeded);var old=shop.GetComponentsInChildren<Button>().Single(v=>v.name=="OutsideShopBack").onClick;State(true);Assert.AreEqual(0,closed);Assert.AreEqual("shop233",views.CurrentKey);State(false);State(true);Assert.AreEqual(1,closed);Assert.IsNull(views.CurrentKey);old.Invoke();Assert.AreEqual(1,closed);Assert.AreEqual(0,store.Writes);
            }finally{UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        private sealed class Shop233Store:Olca.Core.Domain.IClearReturnStore
        {
            public int Writes;public Olca.Core.Domain.ClearReturnProfile Read(out string error){error=null;return new Olca.Core.Domain.ClearReturnProfile();}public bool TryWrite(long expected,Olca.Core.Domain.ClearReturnProfile value,out string error){Writes++;error="No purchase expected";return false;}
        }

        [Test] public async Task 外界店はController無効化で現Viewと操作世代を閉じる()
        {
            var controllerRoot=new GameObject("Shop233Controller");controllerRoot.SetActive(false);var controller=controllerRoot.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try{Register("outside-trial-shop",ct=>Task.FromResult(Panel("Shop")));Assert.IsTrue((await views.ShowAsync("outside-trial-shop")).Succeeded);var prior=views.CurrentKey;Assert.AreEqual("outside-trial-shop",prior);type.GetField("gameSystem",flags).SetValue(controller,system);type.GetField("outsideShopActive",flags).SetValue(controller,true);type.GetField("outsideShopOpening",flags).SetValue(controller,true);type.GetMethod("OnDisable",flags).Invoke(controller,null);Assert.IsNull(views.CurrentKey);Assert.IsFalse((bool)type.GetField("outsideShopActive",flags).GetValue(controller));Assert.IsFalse((bool)type.GetField("outsideShopOpening",flags).GetValue(controller));Assert.Greater((long)type.GetField("outsideShopEpoch",flags).GetValue(controller),0);
            }finally{type.GetField("gameSystem",flags).SetValue(controller,null);controller.StopManagedHost();UnityEngine.Object.DestroyImmediate(controllerRoot);}
        }

        [Test] public async Task 移動確認はController無効化で閉じ取消や再開を持ち越さない()
        {
            var root=new GameObject("FastTravelController");root.SetActive(false);var controller=root.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try{Register("outside-fast-travel",ct=>Task.FromResult(Panel("FastTravel")));Assert.IsTrue((await views.ShowAsync("outside-fast-travel")).Succeeded);type.GetField("gameSystem",flags).SetValue(controller,system);type.GetField("outsideFastTravelActive",flags).SetValue(controller,true);type.GetField("outsideFastTravelOpening",flags).SetValue(controller,true);type.GetMethod("OnDisable",flags).Invoke(controller,null);Assert.IsNull(views.CurrentKey);Assert.IsFalse((bool)type.GetField("outsideFastTravelActive",flags).GetValue(controller));Assert.IsFalse((bool)type.GetField("outsideFastTravelOpening",flags).GetValue(controller));Assert.Greater((long)type.GetField("outsideFastTravelEpoch",flags).GetValue(controller),0);Assert.DoesNotThrow(()=>type.GetMethod("CloseOutsideFastTravel",flags).Invoke(controller,null));Assert.IsNull(views.CurrentKey);}
            finally{type.GetField("gameSystem",flags).SetValue(controller,null);controller.StopManagedHost();UnityEngine.Object.DestroyImmediate(root);}
        }
        [TestCase(false)] [TestCase(true)]
        public void 外界Pauseの行列ナビは同じ軸の有効項目だけを選び端で止まり他画面へ残らない(bool skipAudio)
        {
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var panel=factory.Pause();panel.SetActive(true);var view=panel.AddComponent<GameplayPauseView>();view.Initialize(()=>true);view.SetOutsidePresentation(true);view.RestrictControls(new[]{"LoadCheckpointButton","OwnedCardsButton","RetreatButton"});var buttons=panel.GetComponentsInChildren<Button>(true).ToDictionary(b=>b.name);if(skipAudio)buttons["AudioSettingsButton"].interactable=false;
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var scope=new MenuPadScope(()=>true);int actions=0,cancels=0;foreach(var b in buttons.Values)b.onClick.AddListener(()=>actions++);
            void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in keys)state=state.WithButton(key);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();scope.Tick();}
            void Move(UnityEngine.InputSystem.LowLevel.GamepadButton key,string expected){State();State(key);Assert.AreEqual(expected,scope.FocusName);}
            try{
                scope.NavigationResolver=view.FindNavigation;scope.Bind(panel.transform,"ResumeButton",()=>cancels++);scope.Tick();var resume=buttons["ResumeButton"].navigation;Assert.AreSame(buttons["OutsideItemsButton"],resume.selectOnDown);Assert.IsNull(resume.selectOnLeft);Assert.IsNull(resume.selectOnUp);Assert.AreSame(buttons["KeyboardSettingsButton"],resume.selectOnRight);
                Assert.AreSame(buttons[skipAudio?"TransformationAnimationSetting":"AudioSettingsButton"],buttons["KeyboardSettingsButton"].navigation.selectOnDown);Assert.AreSame(buttons["DialogueTextSpeedButton"],buttons["TransformationAnimationSetting"].navigation.selectOnRight);Assert.AreSame(buttons["ResumeButton"],buttons["OutsideItemsButton"].navigation.selectOnUp);Assert.AreSame(buttons["OutsideEquipmentButton"],buttons["OutsideItemsButton"].navigation.selectOnDown);Assert.AreSame(buttons["OutsideKamuiButton"],buttons["OutsideEquipmentButton"].navigation.selectOnDown);Assert.IsNull(buttons["OutsideKamuiButton"].navigation.selectOnDown);Assert.IsNull(buttons["OutsideItemsButton"].navigation.selectOnLeft);Assert.AreSame(buttons[skipAudio?"VibrationSetting":"AudioSettingsButton"],buttons["OutsideItemsButton"].navigation.selectOnRight);
                Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"OutsideItemsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"OutsideEquipmentButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"OutsideKamuiButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"OutsideKamuiButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,"OutsideEquipmentButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,"OutsideItemsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,"ResumeButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"KeyboardSettingsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"GamepadSettingsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"GamepadSettingsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"VibrationSetting");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"DialogueTextSpeedButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft,"TransformationAnimationSetting");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"DialogueTextSpeedButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,"OutsideObjectivesButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft,"OutsideFastTravelButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,"OutsideObjectivesButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,"DialogueTextSpeedButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft,"TransformationAnimationSetting");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,skipAudio?"KeyboardSettingsButton":"AudioSettingsButton");if(!skipAudio)Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,"KeyboardSettingsButton");Move(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft,"ResumeButton");Assert.AreEqual(0,actions);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.East);Assert.AreEqual(1,cancels);
                buttons["AudioSettingsButton"].interactable=true;buttons["VibrationSetting"].gameObject.SetActive(false);scope.Tick();Assert.AreSame(buttons["DialogueTextSpeedButton"],buttons["GamepadSettingsButton"].navigation.selectOnDown,"非表示行も同じ列だけで飛ばす");
                scope.Suspend();Assert.IsNull(scope.NavigationResolver);var other=Panel("OtherMenu");other.SetActive(true);var a=Button(other,"OtherA");var b2=Button(other,"OtherB");((RectTransform)a.transform).anchoredPosition=new Vector2(0,100);((RectTransform)b2.transform).anchoredPosition=new Vector2(100,0);scope.Bind(other.transform,"OtherA",()=>{});scope.Tick();Assert.AreSame(b2,a.navigation.selectOnDown,"他画面は従来の距離ナビのまま");
            }finally{scope.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }

        [Test] public async Task ポーズ神依ホイールは仮想パッドの保持決定を再発動せず失敗後に再確定して元項目へ戻る()
        {
            var screen=await Screen();screen.Panel.SetActive(true);var panel=new GameObject("MenuWheel",typeof(RectTransform));panel.transform.SetParent(host.transform,false);var wheel=panel.AddComponent<OutsideKamuiWheelDialog>();wheel.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();int calls=0;bool success=false,paused=false;
            void State(params UnityEngine.InputSystem.LowLevel.GamepadButton[] keys){frame++;var state=new UnityEngine.InputSystem.LowLevel.GamepadState();foreach(var key in keys)state=state.WithButton(key);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();dialogs.Tick();wheel.RefreshMenu();}
            try{
                State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);EventSystem.current.SetSelectedGameObject(screen.Confirm.gameObject);wheel.ConfigureChoices(new[]{"Normal","Trial"},0,i=>{Assert.AreEqual(1,i);calls++;return success;});Assert.IsTrue(wheel.OpenMenu(dialogs,screen.Context,()=>screen.Context.IsCurrent,()=>true,v=>paused=v,"A: Confirm / B: Back"));State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(0,calls);Assert.IsTrue(paused);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp);Assert.AreEqual("KamuiWheelAlternate",EventSystem.current.currentSelectedGameObject.name);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(1,calls);Assert.IsTrue(wheel.IsOpen);Assert.IsTrue(paused);
                success=true;State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp);State();State(UnityEngine.InputSystem.LowLevel.GamepadButton.South);Assert.AreEqual(2,calls);Assert.IsFalse(wheel.IsOpen);Assert.IsFalse(paused);Assert.AreSame(screen.Confirm.gameObject,EventSystem.current.currentSelectedGameObject);Assert.IsTrue(dialogs.Blocks(screen.Context));
            }finally{wheel.Abort();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.Object.DestroyImmediate(panel);}
        }

        [Test] public void 外界Pauseの分類配置はダンジョン側の表示と矩形を復元する()
        {
            var panel=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null).Pause();panel.SetActive(true);var view=panel.AddComponent<GameplayPauseView>();view.Initialize(()=>true);
            var item=(RectTransform)panel.transform.Find("OutsideItemsButton");var before=item.anchoredPosition;var size=item.sizeDelta;var heading=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="PausePlayHeading");var title=heading.text;
            view.SetOutsidePresentation(true);Assert.AreEqual(new Vector2(-480,0),item.anchoredPosition);Assert.AreEqual(new Vector2(360,88),item.sizeDelta);Assert.AreEqual("タンサク",heading.text);
            foreach(var name in new[]{"LoadCheckpointButton","OwnedCardsButton","RetreatButton"})Assert.IsFalse(panel.transform.Find(name).gameObject.activeSelf);
            view.SetOutsidePresentation(true);view.SetOutsidePresentation(false);Assert.AreEqual(before,item.anchoredPosition);Assert.AreEqual(size,item.sizeDelta);Assert.AreEqual(title,heading.text);Assert.IsFalse(panel.transform.Find("OutsideEquipmentButton").gameObject.activeSelf);Assert.IsFalse(panel.transform.Find("OutsideKamuiButton").gameObject.activeSelf);
            foreach(var name in new[]{"LoadCheckpointButton","OwnedCardsButton","RetreatButton"})Assert.IsTrue(panel.transform.Find(name).gameObject.activeSelf);
        }

        [TestCase(false)] [TestCase(true)] public void Pauseの選択枠は場面によらず有効項目に表示し操作停止中は消える(bool outside)
        {
            var panel=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null).Pause();panel.SetActive(true);
            var view=panel.AddComponent<GameplayPauseView>();bool allowed=true;view.Initialize(()=>allowed);var buttons=panel.GetComponentsInChildren<Button>(true).ToDictionary(b=>b.name);
            Assert.IsNull(panel.transform.Find("OutsidePauseInputGuide"),"表示するまでは作らない");view.SetOutsidePresentation(outside);buttons["ResumeButton"].Select();view.RefreshOutsidePresentation();
            Assert.IsTrue(buttons["ResumeButton"].transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf);Assert.IsFalse(buttons["KeyboardSettingsButton"].transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf);
            buttons["KeyboardSettingsButton"].Select();view.RefreshOutsidePresentation();Assert.IsFalse(buttons["ResumeButton"].transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf);Assert.IsTrue(buttons["KeyboardSettingsButton"].transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf);
            buttons["KeyboardSettingsButton"].interactable=false;view.RefreshOutsidePresentation();Assert.IsFalse(buttons["KeyboardSettingsButton"].transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf);
            allowed=false;view.RefreshOutsidePresentation();Assert.IsFalse(panel.transform.Find("OutsidePauseInputGuide").gameObject.activeSelf);Assert.IsTrue(buttons.Values.All(b=>!b.transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf));
            allowed=true;buttons["ResumeButton"].Select();view.RefreshOutsidePresentation();Assert.IsTrue(panel.transform.Find("OutsidePauseInputGuide").gameObject.activeSelf);view.CoverForDialog();view.RefreshOutsidePresentation();Assert.IsFalse(panel.transform.Find("OutsidePauseInputGuide").gameObject.activeSelf);Assert.IsTrue(buttons.Values.All(b=>!b.transform.Find("OutsidePauseFocusFrame").gameObject.activeSelf));
            view.SetOutsidePresentation(true);Assert.AreEqual(1,buttons["ResumeButton"].transform.Cast<Transform>().Count(x=>x.name=="OutsidePauseFocusFrame"),"再表示でも生成を重ねない");panel.SetActive(false);typeof(GameplayPauseView).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Assert.IsFalse(panel.transform.Find("OutsidePauseInputGuide").gameObject.activeSelf);
        }
        [Test] public void 外界Pause案内はメニュー割当を表示しゲーム割当と混同しない()
        {
            var profile=UnityInputProvider.CreateDefaultGamepadProfile();Assert.AreEqual("A / × : ケッテイ    B / ○ : モドる",GameplayPauseView.InputGuideText(profile,true));
            profile=new Olca.Core.Domain.InputBindingProfile(new Dictionary<(string,string,string),string>{{("gamepad","menu","Decide"),"North"},{("gamepad","menu","Cancel"),"West"}},Array.Empty<(string,string,string)>());
            Assert.AreEqual("Y / △ : ケッテイ    X / □ : モドる",GameplayPauseView.InputGuideText(profile,true));Assert.AreEqual("Enter : ケッテイ    Esc : モドる",GameplayPauseView.InputGuideText(profile,false));
            Assert.AreEqual("未割当 : ケッテイ    未割当 : モドる",GameplayPauseView.InputGuideText(null,true));
        }

        [Test] public async Task 同じ画面の連打は一度だけ生成し表示を共有する()
        {
            var pending=new TaskCompletionSource<GameObject>();int created=0,shown=0;
            Register("pending",ct=>{created++;return pending.Task;},(go,ctx)=>{shown++;go.SetActive(true);});
            var first=views.ShowAsync("pending");var second=views.ShowAsync("pending");Assert.AreEqual(1,created);
            pending.SetResult(Panel("loaded"));Assert.IsTrue((await first).Succeeded);Assert.IsTrue((await second).Succeeded);Assert.AreEqual(1,shown);
            views.CloseCurrent();Assert.IsTrue((await views.ShowAsync("pending")).Succeeded);Assert.AreEqual(1,created);
            views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,views.RegisteredCount);
        }
        [Test] public async Task 取消後に届いた生成物を表示せず破棄し旧画面を保つ()
        {
            var old=Panel("old");Register("old",ct=>Task.FromResult(old));await views.ShowAsync("old");
            var pending=new TaskCompletionSource<GameObject>();Register("pending",ct=>pending.Task);
            using(var cancel=new CancellationTokenSource()) {
                var request=views.ShowAsync("pending",cancel.Token);cancel.Cancel();var late=Panel("late");pending.SetResult(late);
                Assert.IsTrue((await request).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual("old",views.CurrentKey);Assert.IsTrue(old.activeSelf);
            }
        }
        [Test] public async Task 表示中画面への再要求は別画面の後着ロードを取り消す()
        {
            var current=Panel("current");Register("current",ct=>Task.FromResult(current));await views.ShowAsync("current");
            var firstLoad=new TaskCompletionSource<GameObject>();var nextLoad=new TaskCompletionSource<GameObject>();int loads=0;
            Register("slow",ct=>++loads==1?firstLoad.Task:nextLoad.Task);var slow=views.ShowAsync("slow");
            Assert.IsTrue((await views.ShowAsync("current")).Succeeded);Assert.AreEqual("current",views.CurrentKey);Assert.IsTrue(current.activeSelf);
            var reopened=views.ShowAsync("slow");Assert.AreEqual(2,loads);
            var late=Panel("late");firstLoad.SetResult(late);Assert.IsTrue((await slow).Cancelled);Assert.IsTrue(late==null);
            nextLoad.SetResult(Panel("latest"));Assert.IsTrue((await reopened).Succeeded);Assert.AreEqual("slow",views.CurrentKey);
        }
        [Test] public async Task 生成失敗では元画面と開いていたDialogを維持する()
        {
            var screen=await Screen();var popup=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{});
            Register("fail",ct=>Task.FromException<GameObject>(new InvalidOperationException("fixture load failure")));
            var failed=await views.ShowAsync("fail");Assert.IsFalse(failed.Succeeded);StringAssert.Contains("fixture",failed.Error);
            Assert.AreEqual("screen",views.CurrentKey);Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(popup.IsCompleted);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await popup);
        }
        [Test] public async Task 別画面要求が先行ロードを無効にして遅延結果を解放する()
        {
            var pending=new TaskCompletionSource<GameObject>();Register("slow",ct=>pending.Task);
            var slow=views.ShowAsync("slow");Register("fast",ct=>Task.FromResult(Panel("fast")));Assert.IsTrue((await views.ShowAsync("fast")).Succeeded);
            var late=Panel("late");pending.SetResult(late);Assert.IsTrue((await slow).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual("fast",views.CurrentKey);
        }
        [Test] public async Task Dialog連打を共有し終了前に状態を確定して旧クリックを拒否する()
        {
            var screen=await Screen();int callbacks=0;
            var first=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{callbacks++;Assert.AreEqual(0,dialogs.ActiveCount);});
            Assert.AreSame(first,dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>callbacks++));
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialogs);
            var oldClick=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
            screen.Cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await first);Assert.AreEqual(1,callbacks);
            var next=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>callbacks++);oldClick();Assert.IsFalse(next.IsCompleted);
            screen.Confirm.onClick.Invoke();Assert.AreEqual(DialogResult.Confirmed,await next);Assert.AreEqual(2,callbacks);
        }
        [Test] public async Task 親画面を閉じるとDialogは親終了になり結果処理も保存も実行しない()
        {
            var screen=await Screen();int effects=0;var result=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>effects++);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await result);Assert.AreEqual(0,effects);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(screen.Panel.activeSelf);
            Assert.AreEqual(DialogResult.ParentClosed,await screen.Context.ShowDialog(screen.Panel,screen.Confirm,screen.Cancel,_=>effects++));
        }
        [Test] public async Task Dialogを閉じた同frameは親入力を止め次frameで解放する()
        {
            var screen=await Screen();frame=10;var result=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{});screen.Cancel.onClick.Invoke();await result;
            Assert.IsTrue(screen.Context.DialogBlocksInput);frame++;dialogs.Tick();Assert.IsFalse(screen.Context.DialogBlocksInput);
        }
        [TestCase(false)] [TestCase(true)] public async Task 設定の初期化Dialogを取消すと起点ボタンへ戻る(bool gamepad)
        {
            var panel=Panel("keyboard");var view=panel.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();
            // EditModeで手動更新したViewの入力購読も明示解放する。
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),profile=>null,gamepad);
            Register("keyboard",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile());});
            await views.ShowAsync("keyboard");var update=view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);
            Assert.AreEqual("CloseKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
            var buttons=panel.GetComponentsInChildren<Button>(true);Array.Find(buttons,x=>x.name=="ResetKeyboardSettings").onClick.Invoke();update.Invoke(view,null);
            panel.GetComponentsInChildren<Button>().Single(x=>x.name=="CancelKeyboardReset").onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);
            Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual("ResetKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public async Task 割当初期化は共通確認を必要時生成し候補だけ変更して旧世代と閉鎖後保存を拒否する(bool managed,bool gamepad)
        {
            var panel=Panel("reset-shared");var view=panel.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            var defaults=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();string device=gamepad?"gamepad":"keyboard";
            var slot=(device,"battle","AttackPrimary");var changed=defaults.BeginEdit();Assert.IsTrue(changed.TryAssign(slot,defaults.Bindings[(device,"battle","CardPrimary")],out var error),error);var profile=changed.Preview();int saves=0;
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),candidate=>{saves++;return "fixture save failure";},gamepad);
            if(managed){Register("reset-shared",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(profile);});await views.ShowAsync("reset-shared");}else view.Open(profile);
            var update=view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);
            Button Find(string name)=>panel.GetComponentsInChildren<Button>().Single(x=>x.name==name);string Draft()=>Find("Bind_battle_AttackPrimary").GetComponentInChildren<Text>().text;
            string original=Draft();var save=Find("SaveKeyboardSettings").onClick;var close=Find("CloseKeyboardSettings").onClick;Assert.IsNull(panel.GetComponentInChildren<ConfirmationDialog>());
            Find("ResetKeyboardSettings").Select();Find("ResetKeyboardSettings").onClick.Invoke();var first=panel.GetComponentInChildren<ConfirmationDialog>();Assert.IsNotNull(first);var oldConfirm=first.ConfirmButton.onClick;var oldCancel=first.CancelButton.onClick;save.Invoke();close.Invoke();Assert.AreEqual(0,saves);Assert.IsTrue(first.IsOpen);Assert.IsTrue(panel.activeSelf);
            first.CancelButton.onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);Assert.AreEqual(original,Draft());Assert.AreEqual("ResetKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
            Find("ResetKeyboardSettings").onClick.Invoke();var second=panel.GetComponentInChildren<ConfirmationDialog>();Assert.IsNotNull(second);Assert.AreNotSame(first,second);oldConfirm.Invoke();oldCancel.Invoke();Assert.IsTrue(second.IsOpen);Assert.AreEqual(original,Draft());
            second.ConfirmButton.onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);Assert.AreEqual(0,saves);Assert.AreNotEqual(original,Draft());Assert.AreSame(profile,view.GetType().GetField("applied",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view));
            save.Invoke();Assert.AreEqual(1,saves);StringAssert.Contains("保存できませんでした",panel.GetComponentsInChildren<Text>().Single(x=>x.name=="Status").text);
            view.DiscardAndClose();view.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);save.Invoke();oldConfirm.Invoke();Assert.AreEqual(1,saves);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        [Test] public async Task 音量画面は閉鎖通知し旧クリックを拒否して再表示できる()
        {
            var panel=Panel("audio");var view=panel.AddComponent<Olca.Presentation.Field.GameplayAudioSettingsView>();
            int saved=0,closed=0;
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),value=>{saved++;return null;},()=>{closed++;view.DiscardAndClose();});
            Register("audio",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(new AudioVolumeSettings(),null);});
            await views.ShowAsync("audio");
            var buttons=panel.GetComponentsInChildren<Button>(true);var save=Array.Find(buttons,x=>x.name=="SaveAudioSettings");var cancel=Array.Find(buttons,x=>x.name=="CancelAudioSettings");
            save.onClick.Invoke();Assert.AreEqual(1,saved);
            views.CloseCurrent();save.onClick.Invoke();cancel.onClick.Invoke();Assert.AreEqual(1,saved);Assert.AreEqual(0,closed);
            await views.ShowAsync("audio");save.onClick.Invoke();Assert.AreEqual(2,saved);
            cancel.onClick.Invoke();Assert.AreEqual(1,closed);
            // EditModeの手動View操作ではMonoBehaviourの非表示通知も明示する。
            view.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Assert.IsNull(views.CurrentKey);
            save.onClick.Invoke();Assert.AreEqual(2,saved);
            var oldSave=save.onClick;var oldCancel=cancel.onClick;var oldSlider=panel.GetComponentInChildren<Slider>(true).onValueChanged;
            view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);viewCleanup=null;
            views.UnregisterOwner(owner);oldSave.Invoke();oldCancel.Invoke();oldSlider.Invoke(50);Assert.AreEqual(2,saved);Assert.AreEqual(1,closed);
        }
        [Test] public async Task 破棄中のロードは後着しても表示せず二重解放しない()
        {
            var pending=new TaskCompletionSource<GameObject>();Register("late",ct=>pending.Task);var request=views.ShowAsync("late");system.Dispose();
            var late=Panel("late");pending.SetResult(late);Assert.IsTrue((await request).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual(0,resources.OwnedInstanceCount);system.Dispose();
        }
        [Test] public void 保存先確定だけではファイルを書かず攻略所有を重複させない()
        {
            string path=Path.Combine(Path.GetTempPath(),"managed-"+Guid.NewGuid().ToString("N"));
            var session=system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress");Assert.IsFalse(Directory.Exists(path));
            Assert.AreSame(session,system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress"));
            Assert.Throws<InvalidOperationException>(()=>system.BeginSession(new object(),path,path,path));session.Dispose();Assert.IsNull(system.CurrentSession);session.Dispose();
        }
        [Test] public void 音声は画面毎に生成せず所有者を一つに保ち終了時に停止する()
        {
            var route=sounds.BeginSession(owner,new AudioVolumeSettings());Assert.AreSame(route,sounds.BeginSession(owner,new AudioVolumeSettings()));
            Assert.AreEqual(3,host.GetComponentsInChildren<AudioSource>().Length);
            Assert.Throws<InvalidOperationException>(()=>sounds.BeginSession(new object(),new AudioVolumeSettings()));
            sounds.EndSession(owner);Assert.IsNull(route.Bgm.clip);sounds.Dispose();sounds.EndSession(owner);Assert.AreEqual(0,host.GetComponentsInChildren<AudioSource>().Length);
        }
        [Test] public void 初期化途中の失敗で攻略所有を戻し再試行と二重開始を区別する()
        {
            string path = Path.Combine(Path.GetTempPath(), "managed-start-" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("controller");var controller = go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            try {
                controller.ConfigureGameSystem(system);controller.ConfigureGameplaySettingsDirectory(path+"/settings");controller.ConfigureCheckpointDirectory(path+"/checkpoints");controller.ConfigureProgressDirectory(path+"/progress");
                controller.ConfigureDungeon(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(6,1,2));
                string key=(string)typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("ManagedKeyboardScreen",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
                views.Register(controller,key,ct=>Task.FromResult(Panel("duplicate")),(panel,context)=>{},panel=>{});
                var start=typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("Start",flags);
                var failed=Assert.Throws<TargetInvocationException>(()=>start.Invoke(controller,null));StringAssert.Contains("画面登録",failed.InnerException.Message);
                Assert.IsNull(system.CurrentSession);Assert.AreEqual(0,views.RegisteredCount);
                start.Invoke(controller,null);var session=system.CurrentSession;Assert.IsNotNull(session);Assert.IsNotNull(session.Input);Assert.IsNotNull(session.Dungeon);
                Assert.IsFalse(sounds.Router.IsPaused);
                controller.enabled=false;typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnDisable",flags).Invoke(controller,null);Assert.IsTrue(sounds.Router.IsPaused);
                controller.enabled=true;typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnEnable",flags).Invoke(controller,null);Assert.IsFalse(sounds.Router.IsPaused);
                Assert.AreEqual(8,views.RegisteredCount);start.Invoke(controller,null);Assert.AreSame(session,system.CurrentSession);Assert.AreEqual(8,views.RegisteredCount);
                var entries=(System.Collections.IDictionary)typeof(ViewManager).GetField("entries",flags).GetValue(views);
                CollectionAssert.AreEquivalent(new[]{"keyboard-settings","gamepad-settings","audio-settings","outside-item-registration","outside-equipment","outside-trial-shop","outside-fast-travel","outside-objectives"},entries.Keys);
            } finally {
                var canvas=(GameObject)typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("pauseCanvas",flags).GetValue(controller);if(canvas!=null)UnityEngine.Object.DestroyImmediate(canvas);
                // 反射StartのEditMode fixtureでは破棄通知も明示し、実行中購読を残さない。
                typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnDestroy",flags).Invoke(controller,null);
                UnityEngine.Object.DestroyImmediate(go);if(Directory.Exists(path))Directory.Delete(path,true);
            }
            Assert.IsNull(system.CurrentSession);Assert.AreEqual(0,views.RegisteredCount);
        }
        [Test] public void 初期化済みと未完成Rootの再取得を分けて重複サービスを作らない()
        {
            var type=typeof(GameSystemRoot);var field=type.GetField("instance",BindingFlags.NonPublic|BindingFlags.Static);var prior=field.GetValue(null);
            var go=new GameObject("root-retry");var root=go.AddComponent<GameSystemRoot>();
            try {
                field.SetValue(null,root);Assert.AreSame(root,GameSystemRoot.Ensure());var first=root.Services;Assert.IsNotNull(first);
                Assert.AreSame(first,GameSystemRoot.Ensure().Services);Assert.AreEqual(3,go.GetComponentsInChildren<AudioSource>().Length);
                first.Dispose();type.GetField("<Services>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(root,null);
                var repaired=GameSystemRoot.Ensure().Services;Assert.IsNotNull(repaired);Assert.AreNotSame(first,repaired);Assert.AreEqual(3,go.GetComponentsInChildren<AudioSource>().Length);
            } finally { root.Services?.Dispose();field.SetValue(null,prior);UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public async Task Pause画面は閉じた世代のクリックを再表示後も実行しない()
        {
            int calls=0;bool allowed=true;var panel=Panel("pause");var resume=Button(panel,"Resume");resume.onClick.AddListener(()=>calls++);
            var pause=panel.AddComponent<GameplayPauseView>();pause.Initialize(()=>allowed);var layer=views.GetLayer("pause-test");
            layer.Register(owner,"pause",ct=>Task.FromResult(panel),(go,context)=>pause.Show(context),go=>pause.Hide());
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);var old=resume.onClick;old.Invoke();Assert.AreEqual(1,calls);
            allowed=false;old.Invoke();Assert.AreEqual(1,calls);allowed=true;layer.CloseCurrent();old.Invoke();Assert.AreEqual(1,calls);
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);old.Invoke();Assert.AreEqual(1,calls);resume.onClick.Invoke();Assert.AreEqual(2,calls);
            views.UnregisterOwner(owner);old.Invoke();Assert.AreEqual(2,calls);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public async Task Pauseを覆う確認は取消で戻り親終了では旧確定を実行しない()
        {
            int confirms=0;var panel=Panel("pause");var source=Button(panel,"Retreat");var pause=panel.AddComponent<GameplayPauseView>();pause.Initialize(()=>true);
            var popup=Panel("retreat");var confirm=Button(popup,"ConfirmRetreat");var cancel=Button(popup,"CancelRetreat");
            var layer=views.GetLayer("pause-dialog-test");layer.Register(owner,"pause",ct=>Task.FromResult(panel),(go,context)=>pause.Show(context),go=>pause.Hide());
            await layer.ShowAsync("pause");var context=pause.Context;EventSystem.current.SetSelectedGameObject(source.gameObject);
            pause.CoverForDialog();var request=context.ShowDialog(popup,confirm,cancel,result=>{pause.RestoreAfterDialog();if(result==DialogResult.Confirmed)confirms++;});
            Assert.IsTrue(context.IsCurrent);Assert.IsTrue(panel.activeSelf);Assert.AreEqual(0,panel.GetComponent<CanvasGroup>().alpha);
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);Assert.AreEqual(1,dialogs.ActiveCount);
            cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await request);
            Assert.IsTrue(panel.activeSelf);Assert.AreSame(source.gameObject,EventSystem.current.currentSelectedGameObject);
            Assert.AreEqual("Retreat",pause.TakeRestoredFocus());Assert.IsNull(pause.TakeRestoredFocus());
            frame++;pause.CoverForDialog();request=context.ShowDialog(popup,confirm,cancel,result=>{pause.RestoreAfterDialog();if(result==DialogResult.Confirmed)confirms++;});
            var stale=confirm.onClick;layer.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await request);stale.Invoke();Assert.AreEqual(0,confirms);Assert.IsFalse(panel.activeSelf);Assert.AreEqual(0,dialogs.ActiveCount);
            UnityEngine.Object.DestroyImmediate(popup);
        }
        [Test] public void Checkpoint読取面は本文と既存操作を覆い入力やフォントを変更しない()
        {
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var factory=new GameplayPanelFactory(host.transform,font,null);var panel=factory.Checkpoint(out var message,out var resume,out var retry);
            Assert.IsFalse(panel.activeSelf);Assert.AreSame(factory.Font,message.font);Assert.IsFalse(message.raycastTarget);
            var backing=panel.transform.Find("CheckpointReadingBacking").GetComponent<Image>();var surface=panel.transform.Find("CheckpointReadingSurface").GetComponent<Image>();Assert.IsFalse(backing.raycastTarget);Assert.IsFalse(surface.raycastTarget);Assert.AreEqual(1f,backing.color.a);Assert.Less(surface.transform.GetSiblingIndex(),message.transform.GetSiblingIndex());
            Assert.AreSame(Resources.Load<Sprite>("OlcaUI/description"),surface.sprite);var corners=new Vector3[4];foreach(var item in new[]{message.transform,resume.transform,retry.transform}){((RectTransform)item).GetWorldCorners(corners);foreach(var corner in corners)Assert.IsTrue(backing.rectTransform.rect.Contains(backing.transform.InverseTransformPoint(corner)),item.name);}
            Assert.AreEqual(new Vector2(0,90),message.rectTransform.anchoredPosition);Assert.AreEqual(new Vector2(1360,560),message.rectTransform.sizeDelta);Assert.AreEqual(new Vector2(0,-270),((RectTransform)resume.transform).anchoredPosition);Assert.AreEqual(new Vector2(0,-270),((RectTransform)retry.transform).anchoredPosition);Assert.AreEqual(0,resume.onClick.GetPersistentEventCount());Assert.AreEqual(0,retry.onClick.GetPersistentEventCount());
            float L(Color c)=>.2126f*c.r+.7152f*c.g+.0722f*c.b;Assert.Greater(L(message.color),.9f);Assert.Less(L(backing.color),.1f);
        }

        [Test] public async Task Checkpoint案内は内容更新で世代を変えず再表示後の旧入力を拒否する()
        {
            int calls=0;bool allowed=true;var panel=Panel("checkpoint");var button=Button(panel,"Continue");button.onClick.AddListener(()=>calls++);
            var view=panel.AddComponent<CheckpointPromptView>();view.Initialize(()=>allowed);var layer=views.GetLayer("checkpoint-test");
            layer.Register(owner,"checkpoint",ct=>Task.FromResult(panel),(go,context)=>view.Show(context),go=>view.Hide());
            await layer.ShowAsync("checkpoint");var first=view.Context;var old=button.onClick;
            await layer.ShowAsync("checkpoint");Assert.AreSame(first,view.Context);old.Invoke();old.Invoke();Assert.AreEqual(1,calls);
            allowed=false;old.Invoke();Assert.AreEqual(1,calls);allowed=true;button.gameObject.SetActive(false);old.Invoke();Assert.AreEqual(1,calls);button.gameObject.SetActive(true);
            layer.CloseCurrent();await layer.ShowAsync("checkpoint");old.Invoke();Assert.AreEqual(1,calls);
            button.onClick.Invoke();Assert.AreEqual(2,calls);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public async Task 保存再試行Dialogは失敗で閉じず無効取消と同frame再入を拒否する()
        {
            int attempts=0,completed=0;bool parent=true;var panel=Panel("retry");var confirm=Button(panel,"Retry");var cancel=Button(panel,"Close");cancel.interactable=false;
            using(var scope=dialogs.CreateOwner(()=>parent)){
                var task=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{attempts++;confirm.onClick.Invoke();if(attempts==2){confirm.gameObject.SetActive(false);cancel.interactable=true;}return false;},_=>completed++);
                confirm.onClick.Invoke();confirm.onClick.Invoke();cancel.onClick.Invoke();Assert.AreEqual(1,attempts);Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(task.IsCompleted);
                frame++;confirm.onClick.Invoke();Assert.AreEqual(2,attempts);Assert.AreEqual(1,dialogs.ActiveCount);
                cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await task);Assert.AreEqual(1,completed);
            }
        }
        [Test] public async Task 未移行親のDialog寿命も終了後と再表示後の旧確定を拒否する()
        {
            bool parent=true;int effects=0;var panel=Panel("progress");var confirm=Button(panel,"Confirm");var cancel=Button(panel,"Cancel");
            using(var scope=dialogs.CreateOwner(()=>parent)){
                var task=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{effects++;return true;},_=>effects++);
                var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
                var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
                parent=false;dialogs.Tick();Assert.AreEqual(DialogResult.ParentClosed,await task);stale();Assert.AreEqual(0,effects);
                parent=true;frame++;var next=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{effects++;return true;},_=>{});stale();Assert.AreEqual(0,effects);
                confirm.onClick.Invoke();Assert.AreEqual(DialogResult.Confirmed,await next);Assert.AreEqual(1,effects);
            }
        }
        [Test] public async Task Checkpointの進行確認取消は同frameの親クリック遮断を解除しない()
        {
            var parent=Panel("checkpoint-parent");var prompt=parent.AddComponent<CheckpointPromptView>();prompt.Initialize(()=>true);var layer=views.GetLayer("checkpoint");
            layer.Register(owner,"checkpoint",ct=>Task.FromResult(parent),(go,context)=>prompt.Show(context),go=>prompt.Hide());await layer.ShowAsync("checkpoint");
            var popup=Panel("progress-popup");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{}));var cancel=dialog.CancelButton;
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            Set("gameSystem",system);Set("managedCheckpointView",prompt);Set("progressActionDialog",dialog);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"fixture",false,true});Assert.AreEqual(1,dialogs.ActiveCount);
            cancel.onClick.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(dialog.IsOpen);Assert.IsTrue(prompt.Context.DialogBlocksInput);
            frame++;dialogs.Tick();Assert.IsFalse(prompt.Context.DialogBlocksInput);
            // 未起動fixtureに所有サービスを残してOnDestroyへ流さない。
            Set("gameSystem",null);UnityEngine.Object.DestroyImmediate(controller);
        }
        [Test] public void 未移行親の進行Dialogもhost終了時点でUnity破棄より先に解除する()
        {
            var parent=Panel("hub-parent");parent.SetActive(true);var hub=parent.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();
            var popup=Panel("progress-popup");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{}));var cancel=dialog.CancelButton;
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            Set("gameSystem",system);Set("clearReturnView",hub);Set("progressActionDialog",dialog);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"fixture",false,true});Assert.AreEqual(1,dialogs.ActiveCount);
            var scope=dialog.Owner;Assert.IsTrue(scope.IsCurrent);
            controller.StopManagedHost();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(scope.IsCurrent);Assert.IsFalse(popup.activeSelf);
            // TickやUnity側のDestroyを待つ前に終了境界が成立している。
            UnityEngine.Object.DestroyImmediate(controller);
        }
        [Test] public void host終了後の旧手動保存イベントはUnity破棄前でも保存と世代を変更しない()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stopped-progress-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);
                Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()),progress.Error);
                var before=File.ReadAllBytes(store.ActivePath);long revision=progress.Snapshot.Revision;
                var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);
                var button=Button(host,"ManualSaveButton");button.onClick.AddListener((UnityEngine.Events.UnityAction)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction),controller,type.GetMethod("SaveProgressFromUi",flags)));var old=button.onClick;
                controller.StopManagedHost();Assert.DoesNotThrow(()=>old.Invoke());CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(revision,progress.Snapshot.Revision);Assert.IsFalse(progress.HasPendingSave);
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [Test] public async Task 読取専用Dialogは閉鎖世代と親終了を守りスクロール所有を返す()
        {
            var screen=await Screen();var scroll=screen.Panel.AddComponent<ScrollRect>();int completed=0;
            var request=dialogs.ShowNotice(screen.Context,screen.Panel,screen.Cancel,scroll,_=>completed++);
            Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsTrue(screen.Context.DialogBlocksInput);
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);
            screen.Cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await request);Assert.AreEqual(1,completed);
            Assert.IsTrue(screen.Context.DialogBlocksInput);frame++;dialogs.Tick();Assert.IsFalse(screen.Context.DialogBlocksInput);
            var next=dialogs.ShowNotice(screen.Context,screen.Panel,screen.Cancel,scroll,_=>completed++);stale();Assert.IsFalse(next.IsCompleted);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await next);Assert.AreEqual(1,completed);Assert.AreEqual(0,dialogs.ActiveCount);
            stale();Assert.AreEqual(1,completed);
        }
        [Test] public async Task 帰還共有画面は世代入力と詳細Popupを管理し別層図鑑を独立閉鎖する()
        {
            var panel=Panel("return-hub");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();
            int actions=0;view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{},()=>actions++);
            var layer=views.GetLayer("return-test");layer.Register(owner,"return",ct=>Task.FromResult(panel),(go,ctx)=>view.ShowManaged(ctx),go=>view.HideManaged());
            view.ConfigureManagement(()=>{var result=layer.ShowAsync("return");Assert.IsTrue(result.IsCompleted&&result.Result.Succeeded);},layer.CloseCurrent,dialogs);
            void Render()=>view.Render(new Olca.Core.Domain.ClearReturnProfile(),new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);
            Render();var first=view.ManagementContext;Assert.IsTrue(first.IsCurrent);
            var other=views.GetLayer("collection-test");var collection=Panel("collection");other.Register(owner,"collection",ct=>Task.FromResult(collection),(go,ctx)=>go.SetActive(true),go=>go.SetActive(false));
            await other.ShowAsync("collection");Assert.IsTrue(first.IsCurrent);other.CloseCurrent();Assert.IsTrue(first.IsCurrent);
            var method=view.GetType().GetMethod("OpenReadonlyText",BindingFlags.Instance|BindingFlags.NonPublic);
            var parameters=method.GetParameters();Assert.AreEqual(4,parameters.Length);
            method.Invoke(view,new object[]{"詳細","表示内容",null,null});Assert.AreEqual(1,dialogs.ActiveCount);
            layer.CloseCurrent();Assert.IsFalse(first.IsCurrent);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);
            Render();Assert.IsTrue(view.ManagementContext.IsCurrent);Assert.AreNotSame(first,view.ManagementContext);
            views.UnregisterOwner(owner);Assert.AreEqual(0,layer.RegisteredCount);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        private Olca.Presentation.Dungeon.ClearReturnView ReturnView()
        {
            var panel=Panel("return15");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
            var layer=views.GetLayer("return15");layer.Register(owner,"return15",ct=>Task.FromResult(panel),(go,ctx)=>view.ShowManaged(ctx),go=>view.HideManaged());
            view.ConfigureManagement(()=>{var request=layer.ShowAsync("return15");Assert.IsTrue(request.IsCompleted&&request.Result.Succeeded);},layer.CloseCurrent,dialogs);return view;
        }
        private UnityEngine.Events.UnityAction CurrentConfirm()
        {
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            return (UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
        }
        [Test] public void 編成破棄Dialogは親を保持し取消再描画後のframe遮断と旧確定を守る()
        {
            var view=ReturnView();int resumes=0,discards=0;var profile=new Olca.Core.Domain.ClearReturnProfile();var draft=new List<Olca.Core.Domain.Card>();
            void Render(bool discard=false)=>view.RenderOwnedDeparture(profile,new Olca.Core.Domain.Card[0],draft,null,false,(_,__)=>{},()=>{},_=>discards++,()=>{resumes++;Render();},discard);
            Render();var parent=view.transform.Find("DepartureContents").gameObject;Render(true);var old=CurrentConfirm();Render(true);
            Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(parent.activeSelf);Assert.AreSame(parent,view.transform.Find("DepartureContents").gameObject);
            view.GetComponentsInChildren<Button>().Single(x=>x.name=="KeepEditingDeparture").onClick.Invoke();
            Assert.AreEqual(1,resumes);Assert.AreEqual(0,discards);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
            Assert.AreEqual("CloseOwnedDeparture",EventSystem.current.currentSelectedGameObject.name);old();Assert.AreEqual(0,discards);
            frame++;dialogs.Tick();Render(true);var confirm=CurrentConfirm();confirm();confirm();Assert.AreEqual(1,discards);
        }
        [Test] public void 保存デッキ削除Dialogは取消focusと左右選択を保ち旧確定を再使用しない()
        {
            var view=ReturnView();int deletes=0;var profile=new Olca.Core.Domain.ClearReturnProfile{SavedDecks=new[]{new Olca.Core.Domain.SavedDepartureDeck{Id="isolated",Name=new string('長',300),Cards=new Olca.Core.Domain.SavedDeckCard[0]}}};
            void Render(string deleting=null)=>view.RenderSavedDeckMenu(profile,null,false,()=>{},_=>{},_=>{},_=>{},id=>Render(id),()=>{},()=>{},deleting,()=>{deletes++;Render();});
            Render();Render("isolated");var old=CurrentConfirm();var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            Assert.IsFalse((bool)ticket.GetType().GetField("DirectionScroll").GetValue(ticket));Assert.NotNull(ticket.GetType().GetField("Scroll").GetValue(ticket));
            view.GetComponentsInChildren<Button>().Single(x=>x.name=="CancelDeleteDeck").onClick.Invoke();Assert.AreEqual(0,deletes);Assert.AreEqual("DeleteDeck_isolated",EventSystem.current.currentSelectedGameObject.name);Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
            old();Assert.AreEqual(0,deletes);frame++;dialogs.Tick();Render("isolated");var next=CurrentConfirm();old();Assert.AreEqual(1,dialogs.ActiveCount);next();next();Assert.AreEqual(1,deletes);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        [Test] public void 親終了で編成削除Dialogの業務callbackと後着確定を破棄する()
        {
            var view=ReturnView();int effects=0;var profile=new Olca.Core.Domain.ClearReturnProfile();
            view.RenderOwnedDeparture(profile,new Olca.Core.Domain.Card[0],new List<Olca.Core.Domain.Card>(),null,false,(_,__)=>{},()=>{},_=>effects++,()=>effects++,true);
            var old=CurrentConfirm();views.GetLayer("return15").CloseCurrent();Assert.AreEqual(0,dialogs.ActiveCount);old();Assert.AreEqual(0,effects);Assert.IsFalse(view.gameObject.activeSelf);Assert.IsNull(view.ManagementContext);
        }
        [Test] public void Dialog取消後の再描画で閉鎖frameを消さず新拠点の保存入口も遮断する()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stage15-frame-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()));
                var view=ReturnView();view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);
                var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);
                Assert.IsTrue((bool)type.GetProperty("IsProgressHub",flags).GetValue(controller),"保存入口自体が有効な拠点で遮断を検証する");
                var popup=Panel("frame-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(view.ManagementContext,popup,yes,no,_=>view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null));
                var bytes=File.ReadAllBytes(store.ActivePath);no.onClick.Invoke();Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
                Assert.DoesNotThrow(()=>type.GetMethod("SaveProgressFromUi",flags).Invoke(controller,null));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 装備三画面は管理世代とDialog入力を守り明示終端で参照を返す(int kind)
        {
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var panel=Panel("equipment");var layer=views.GetLayer("equipment16");Component view;Action open,hide,release;Action<ManagedScreenContext> show;Action<Action,Action> configure;Func<ManagedScreenContext> context;int cancels=0;
            var catalog=new Olca.Core.Logic.Customization.CustomizationCatalog("fixture16",new Olca.Core.Logic.Customization.PartsBoardDefinition(2,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null);
            var progress=new Olca.Core.Logic.Customization.CustomizationProgress();
            if(kind==0){var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationLoadoutView>();v.Initialize(font,()=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(new Olca.Core.Logic.Customization.CustomizationEditDraft(catalog,progress,0));}
            else if(kind==1){var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationUnlockView>();v.Initialize(font,_=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(catalog,progress);}
            else{var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();v.Initialize(font,_=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(new Olca.Core.Logic.Customization.CustomizationAcquisitionPlan{ContextId="fixture16",BoardId=catalog.BoardId,MaximumClaims=0,Candidates=Array.Empty<Olca.Core.Logic.Customization.CustomizationAcquisitionCandidate>()},progress,false,catalog);}
            layer.Register(owner,"equipment",ct=>Task.FromResult(panel),(go,ctx)=>show(ctx),go=>hide());configure(()=>Assert.IsTrue(layer.ShowAsync("equipment").GetAwaiter().GetResult().Succeeded),layer.CloseCurrent);
            var origin=Button(host,"origin16");EventSystem.current.SetSelectedGameObject(origin.gameObject);view.GetType().GetMethod("PrepareManagedOpen").Invoke(view,null);Assert.IsTrue(context().IsCurrent);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);Assert.IsNull(view.GetType().GetField(kind==0?"draft":kind==1?"state":"plan",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view));
            open();var first=context();Assert.IsTrue(first.IsCurrent);var cancel=view.GetComponentsInChildren<Button>().Single(b=>b.name==(kind==0?"CancelCustomization":kind==1?"CancelCustomizationUnlock":"CancelCustomizationAcquisition"));var old=cancel.onClick;var cancelName=cancel.name;
            var popup=Panel("equipment-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(first,popup,yes,no,_=>{});old.Invoke();Assert.AreEqual(0,cancels);Assert.IsTrue(first.IsCurrent);
            layer.CloseCurrent();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(first.IsCurrent);Assert.IsNull(context());Assert.IsFalse(panel.activeSelf);old.Invoke();Assert.AreEqual(0,cancels);
            open();Assert.AreNotSame(first,context());old.Invoke();Assert.AreEqual(0,cancels);Assert.IsTrue(context().IsCurrent);
            var current=view.GetComponentsInChildren<Button>().Single(b=>b.name==cancelName).onClick;current.Invoke();Assert.AreEqual(1,cancels);Assert.IsNull(context());Assert.IsNull(EventSystem.current.currentSelectedGameObject);current.Invoke();Assert.AreEqual(1,cancels);
            open();release();Assert.IsNull(context());Assert.IsFalse(panel.activeSelf);Assert.IsNull(layer.CurrentKey);
            foreach(var name in new[]{"menu","ensureManagedVisible","hideManaged",kind==0?"draft":kind==1?"state":"plan"})Assert.IsNull(view.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view),name);
            layer.UnregisterOwner(owner);Assert.AreEqual(0,layer.RegisteredCount);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(1,cancels);
        }
        [TestCase(false)][TestCase(true)]
        public void 帰還面の必要時生成後も装備入口は前面にあり確認中だけ隠れる(bool registered)
        {
            // Chrome表示のfixtureは読込済み拠点を使い、新規配布の契約を要求しない。
            var progress=new Olca.Core.Domain.ClearReturnSession(new Olca.Core.Logic.Dungeon.MemoryClearReturnStore(new Olca.Core.Domain.ClearReturnProfile()));
            Assert.IsTrue(progress.Ready);
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var chrome=factory.Chrome();
            var view=ReturnView();view.transform.SetParent(host.transform,false);view.gameObject.SetActive(true);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
            Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);
            Set("manualSaveButton",chrome.Save);Set("newGameButton",chrome.NewGame);Set("customizationEditorButton",chrome.Loadout);Set("customizationUnlockButton",chrome.Unlock);Set("customizationAcquisitionButton",chrome.Acquisition);Set("customizationAcquisitionProvider",new EquipmentSourceProbe());
            if(registered)Set("customizationCatalog",new Olca.Core.Logic.Customization.CustomizationCatalog("chrome20",new Olca.Core.Logic.Customization.PartsBoardDefinition(1,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null));
            type.GetMethod("RefreshProgressActions",flags).Invoke(controller,null);
            foreach(var button in new[]{chrome.Loadout,chrome.Unlock,chrome.Acquisition}){Assert.IsTrue(button.gameObject.activeSelf);Assert.AreEqual(registered,button.interactable);Assert.Greater(button.transform.GetSiblingIndex(),view.transform.GetSiblingIndex());}
            var popup=Panel("progress-overlay");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(factory);Set("progressActionDialog",dialog);dialog.OpenNotice("fixture",false,true,null,null,()=>true);type.GetMethod("RefreshProgressActions",flags).Invoke(controller,null);
            foreach(var button in new[]{chrome.Loadout,chrome.Unlock,chrome.Acquisition})Assert.IsFalse(button.gameObject.activeSelf);
        }
        private sealed class EquipmentSourceProbe:Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider
        {
            public int Calls;
            public Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition Resolve(Olca.Core.Logic.Customization.CustomizationAcquisitionContext context){Calls++;return null;}
        }
        [TestCase("OpenCustomizationEditor")][TestCase("OpenCustomizationUnlocks")][TestCase("OpenCustomizationAcquisitions")]
        public void 帰還確認を閉じた同frameの旧装備入口は草稿も取得計画も作らない(string method)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stage16-fence-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()));
                var view=ReturnView();view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string n,object value)=>type.GetField(n,flags).SetValue(controller,value);
                var catalog=new Olca.Core.Logic.Customization.CustomizationCatalog("fence16",new Olca.Core.Logic.Customization.PartsBoardDefinition(1,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null);var source=new EquipmentSourceProbe();
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);Set("customizationCatalog",catalog);Set("customizationAcquisitionProvider",source);
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var load=Panel("load").AddComponent<Olca.Presentation.Dungeon.CustomizationLoadoutView>();load.Initialize(font,()=>{},()=>{},()=>{});var unlock=Panel("unlock").AddComponent<Olca.Presentation.Dungeon.CustomizationUnlockView>();unlock.Initialize(font,_=>{},()=>{},()=>{});var acquire=Panel("acquire").AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();acquire.Initialize(font,_=>{},()=>{},()=>{});Set("customizationEditorView",load);Set("customizationUnlockView",unlock);Set("customizationAcquisitionView",acquire);
                Assert.IsTrue((bool)type.GetProperty("IsProgressHub",flags).GetValue(controller));Assert.IsTrue(progress.Ready);Assert.IsFalse(progress.HasPendingSave);
                var popup=Panel("fence16-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(view.ManagementContext,popup,yes,no,_=>view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null));var bytes=File.ReadAllBytes(store.ActivePath);no.onClick.Invoke();Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
                Assert.DoesNotThrow(()=>type.GetMethod(method,flags).Invoke(controller,null));Assert.IsFalse(load.gameObject.activeSelf);Assert.IsFalse(unlock.gameObject.activeSelf);Assert.IsFalse(acquire.gameObject.activeSelf);Assert.IsNull(load.Draft);Assert.AreEqual(0,source.Calls);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase(false)][TestCase(true)]
        public void 資源報酬画面は旧選択を次の待機へ渡さず親終了で取消す(bool item)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool wasPaused=GameplayPause.IsPaused;var wasRelease=release.GetValue(null);var wasFrames=frames.GetValue(null);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();
            void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
            object Call(string n,params object[] args)=>type.GetMethod(n,flags).Invoke(controller,args);
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();Assert.IsFalse(GameplayPause.InputBlocked);
                var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});
                var binding=new HostPanelBindings(controller,host.transform,resources,views);Set("gameSystem",system);Set("panelFactory",factory);Set("hostPanels",binding);Call("RegisterResourceRewardViews");
                Assert.IsNull(type.GetField("currencyOverlay",flags).GetValue(controller));Assert.IsNull(type.GetField("itemRewardOverlay",flags).GetValue(controller));Assert.AreEqual(0,resources.OwnedInstanceCount);
                var root=binding.Show(item?"item":"currency");var yes=root.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"ReceiveItemReward":"ReceiveCurrencyButton"));var no=root.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"SkipItemReward":"SkipCurrencyButton"));
                var first=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,first,CancellationToken.None);var old=yes.onClick;var context=(ManagedScreenContext)type.GetField("resourceRewardContext",flags).GetValue(controller);Assert.IsTrue(context.IsCurrent);
                var popup=Panel("reward-popup");var confirm=Button(popup,"confirm");var cancel=Button(popup,"cancel");dialogs.Show(context,popup,confirm,cancel,_=>{});old.Invoke();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,first.Task.Status);
                views.GetLayer("resource-reward").CloseCurrent();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,first.Task.Status);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsNull(EventSystem.current.currentSelectedGameObject);
                frame++;var next=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,next,CancellationToken.None);old.Invoke();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,next.Task.Status);
                yes.onClick.Invoke();no.onClick.Invoke();Assert.IsTrue(next.Task.GetAwaiter().GetResult());Call("CloseResourceReward",root,next);Assert.IsFalse(root.activeSelf);Assert.IsNull(type.GetField("activeResourceRewardChoice",flags).GetValue(controller));
                frame++;var final=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,final,CancellationToken.None);Call("OnDisable");Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,final.Task.Status);Call("OnEnable");Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,final.Task.Status);views.GetLayer("resource-reward").CloseCurrent();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,final.Task.Status);Assert.IsNull(type.GetField("resourceRewardRoot",flags).GetValue(controller));
                var crossOld=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,crossOld,CancellationToken.None);var other=binding.Show(item?"currency":"item");var otherNo=other.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"SkipCurrencyButton":"SkipItemReward"));var crossNext=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",other,otherNo.name,crossNext,CancellationToken.None);Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,crossOld.Task.Status);Assert.IsTrue(((ManagedScreenContext)type.GetField("resourceRewardContext",flags).GetValue(controller)).IsCurrent);otherNo.onClick.Invoke();Assert.IsFalse(crossNext.Task.GetAwaiter().GetResult());Call("CloseResourceReward",other,crossNext);
                Call("ReleasePresentationBindings");Assert.IsNull(type.GetField("resourceRewardMenu",flags).GetValue(controller));Assert.IsNull(type.GetField("resourceRewardViews",flags).GetValue(controller));views.UnregisterOwner(controller);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{UnityEngine.Object.DestroyImmediate(controller);GameplayPause.SetPaused(wasPaused);release.SetValue(null,wasRelease);frames.SetValue(null,wasFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public void Custom詳細はDialog世代と親終了を守り閲覧で手札と選択を変更しない(bool parentClose)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool wasPaused=GameplayPause.IsPaused;var wasRelease=release.GetValue(null);var wasFrames=frames.GetValue(null);
            var binding=new BattlePresentationBinding(owner,resources,views,host.transform);var v=binding.Custom;var type=v.GetType();
            object Call(string n,params object[] args)=>type.GetMethod(n,flags).Invoke(v,args);
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();
                binding.Battle.gameObject.SetActive(true);v.gameObject.SetActive(true);v.ConfigureDetailsManagement(dialogs);
                var f=new Olca.Application.Battle.SimpleCardFactory();var model=new Olca.Core.Logic.Battle.CustomScreenSystem(new Olca.Core.Domain.Deck(Enumerable.Range(0,5).Select(_=>f.CreateById("sword")).ToList()));model.Open(Array.Empty<Olca.Core.Domain.Card>(),new Olca.Tests.Mocks.MockRandomProvider());v.Initialize(model);
                var hand=model.CurrentHand.ToArray();var cost=model.GetTotalUsedCost();var origin=Button(host,"detail-origin");EventSystem.current.SetSelectedGameObject(origin.gameObject);Action queued=null;v.ConfigureLogicalConfirmation((_,action)=>queued=action,()=>0L);Call("OnConfirmClicked");Assert.NotNull(queued);
                Call("ShowDetailsValue","セイノウ\n"+string.Concat(Enumerable.Repeat("ナガイ セツメイ\n",80))+"オワリ");Assert.AreEqual(1,dialogs.ActiveCount);var panel=v.transform.Find("CustomPadDetails").gameObject;var text=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="CustomPadDetailsText");var scroll=panel.GetComponentInChildren<ScrollRect>();Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);StringAssert.EndsWith("オワリ",text.text);
                var ticket=typeof(DialogManager).GetField("current",flags).GetValue(dialogs);var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);Assert.IsNull(ticket.GetType().GetField("Menu").GetValue(ticket),"Custom割当と標準menuの二重入力は作らない");Assert.DoesNotThrow(()=>dialogs.Tick(),"Custom固有入力のticketもRootの更新を通す");
                var materials=(List<Olca.Core.Domain.Card>)type.GetField("customizeSelectedCards",flags).GetValue(v);materials.Add(hand[0]);queued();v.OnCardClicked(hand[0]);Call("CancelSelection");Call("OnCustomizeModeCancelled");Assert.AreEqual(1,materials.Count);Call("OnConfirmClicked");Assert.IsFalse(model.IsConfirming);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(0,model.SelectedCards.Count);Assert.AreEqual(cost,model.GetTotalUsedCost());
                var imported=panel.GetComponent<Image>().sprite;Assert.NotNull(imported);
                // pause側の確認が同じDialogManagerを使えるよう、Customのモデルを維持して所有を返す。
                v.CloseDetailsForSystemPause();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);Assert.IsTrue(model.IsOpen);Assert.IsFalse(model.IsConfirming);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(cost,model.GetTotalUsedCost());
                using(var pauseOwner=dialogs.CreateOwner(()=>true)){var pausePanel=Panel("pause-detail-handoff");var pauseClose=Button(pausePanel,"pause-close");var pauseRequest=dialogs.ShowNotice(pauseOwner,pausePanel,pauseClose,null,_=>{});Assert.AreEqual(1,dialogs.ActiveCount);stale();Assert.AreEqual(1,dialogs.ActiveCount);pauseClose.onClick.Invoke();Assert.IsTrue(pauseRequest.IsCompleted);}
                frame++;EventSystem.current.SetSelectedGameObject(origin.gameObject);Call("ShowDetailsValue","ポーズ カラ フッキ");Assert.AreEqual(1,dialogs.ActiveCount);ticket=typeof(DialogManager).GetField("current",flags).GetValue(dialogs);stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);
                if(parentClose){v.ReleaseSessionPresentation();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(panel==null||!panel.activeSelf);Assert.IsNull(type.GetField("detailsOwner",flags).GetValue(v));stale();Assert.DoesNotThrow(()=>v.OnCardClicked(hand[0]));Assert.DoesNotThrow(()=>Call("OnConfirmClicked"));Assert.DoesNotThrow(()=>Call("OnCustomizeModeCancelled"));Assert.IsFalse(model.IsConfirming);}
                else{stale();Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);queued();v.OnCardClicked(hand[0]);Call("OnConfirmClicked");Assert.IsFalse(model.IsConfirming);Assert.AreEqual(0,model.SelectedCards.Count);frame++;Call("ShowDetailsValue","ミジカイ セツメイ");Assert.AreEqual(1,dialogs.ActiveCount);stale();Assert.AreEqual(1,dialogs.ActiveCount);model.Close();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);}
                CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(cost,model.GetTotalUsedCost());binding.Dispose();Assert.IsTrue(imported!=null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{binding.Dispose();GameplayPause.SetPaused(wasPaused);release.SetValue(null,wasRelease);frames.SetValue(null,wasFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public void Custom専用詳細は読取末尾と固有入力を持ち終端破棄後の旧操作を次世代へ渡さない(bool managed)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool paused=GameplayPause.IsPaused;var priorRelease=release.GetValue(null);var priorFrames=frames.GetValue(null);
            var binding=new BattlePresentationBinding(owner,resources,views,host.transform);var view=binding.Custom;
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();binding.Battle.gameObject.SetActive(true);view.gameObject.SetActive(true);view.ConfigureDetailsManagement(managed?dialogs:null);
                var factory=new Olca.Application.Battle.SimpleCardFactory();var model=new Olca.Core.Logic.Battle.CustomScreenSystem(new Olca.Core.Domain.Deck(Enumerable.Range(0,5).Select(_=>factory.CreateById("sword")).ToList()));model.Open(Array.Empty<Olca.Core.Domain.Card>(),new Olca.Tests.Mocks.MockRandomProvider());view.Initialize(model);var hand=model.CurrentHand.ToArray();int cost=model.GetTotalUsedCost();
                var show=typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetMethod("ShowDetailsValue",flags);show.Invoke(view,new object[]{string.Concat(Enumerable.Repeat("ナガイ ブン\n",80))+"オワリ"});var first=view.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomCardDetailsDialog>();Assert.NotNull(first);Assert.Greater(first.Scroll.content.rect.height,first.Scroll.viewport.rect.height);first.Scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,first.Scroll.verticalNormalizedPosition,.001);StringAssert.EndsWith("オワリ",first.Body.text);
                first.HandlePad(CustomPadCommand.Up);Assert.AreEqual(.2f,first.Scroll.verticalNormalizedPosition,.001);first.HandlePad(CustomPadCommand.Down);Assert.AreEqual(0,first.Scroll.verticalNormalizedPosition,.001);var oldClose=first.GetComponentsInChildren<Button>().Single(x=>x.name=="CloseCustomPadDetails").onClick;var imported=first.GetComponent<Image>().sprite;
                // EditModeではMonoBehaviour終了を明示配送する。自然DestroyはPlay実入口で別途確認する。
                typeof(Olca.Presentation.CustomScreen.CustomCardDetailsDialog).GetMethod("OnDestroy",flags).Invoke(first,null);UnityEngine.Object.DestroyImmediate(first.gameObject);Assert.AreEqual(0,dialogs.ActiveCount);frame++;show.Invoke(view,new object[]{"ツギ ノ ショウサイ"});var second=view.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomCardDetailsDialog>();Assert.NotNull(second);Assert.IsTrue(second.IsOpen);oldClose.Invoke();Assert.IsTrue(second.IsOpen);second.Dismiss();Assert.IsFalse(second.IsOpen);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(0,model.SelectedCards.Count);Assert.AreEqual(cost,model.GetTotalUsedCost());Assert.IsFalse(model.IsConfirming);binding.Dispose();Assert.IsTrue(imported!=null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{binding.Dispose();GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);frames.SetValue(null,priorFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public async Task 会話Popupは元ViewとSessionを保持し世代入力とpause親終了を管理する(bool skipMode)
        {
            var root=Panel("managed-dialogue");var v=root.AddComponent<Olca.Presentation.Field.DialogueView>();v.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),()=>{});
            var session=new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript("dialogue-owner",new[]{new Olca.Core.Domain.DialogueLine(string.Concat(Enumerable.Repeat("ナガイ カイワ\n",80))+"オワリ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);
            var layer=views.GetLayer("dialogue");layer.Register(owner,"dialogue",_=>Task.FromResult(root),(panel,ctx)=>v.BindManaged(session,ctx,dialogs),panel=>v.HideManaged());
            Assert.IsTrue((await layer.ShowAsync("dialogue")).Succeeded);
            Button Find(string name)=>root.GetComponentsInChildren<Button>().Single(b=>b.name==name);
            var next=Find("DialogueNext");var history=Find("DialogueHistory");var entry=Find(skipMode?"DialogueSkip":"DialogueHistory");var oldNormal=next.onClick;EventSystem.current.SetSelectedGameObject(entry.gameObject);int index=session.Index;long initialVersion=session.Version;
            entry.onClick.Invoke();v.Refresh();var livePopup=root.GetComponentInChildren<ReturnDialog>();livePopup.RefreshLifetime();Assert.Greater(Find("DialoguePause").transform.GetSiblingIndex(),livePopup.transform.GetSiblingIndex(),"毎frameの前面化でも親のPause操作を覆わない");Assert.AreEqual(1,dialogs.ActiveCount);Assert.AreEqual(skipMode,session.SkipConfirmationOpen);Assert.AreEqual(!skipMode,session.BacklogOpen);var oldClose=Find("DialogueHistory").onClick;oldNormal.Invoke();Assert.AreEqual(index,session.Index);Assert.DoesNotThrow(()=>dialogs.Tick());
            session.IsPaused=true;v.SetPresentationPaused(true);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(root.activeSelf);Assert.AreEqual(skipMode,session.SkipConfirmationOpen);Assert.AreEqual(!skipMode,session.BacklogOpen);
            using(var other=dialogs.CreateOwner(()=>true)){var panel=Panel("pause-dialogue-confirm");var close=Button(panel,"close-pause-dialogue");var otherRequest=dialogs.ShowNotice(other,panel,close,null,_=>{});Assert.AreEqual(1,dialogs.ActiveCount);close.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await otherRequest);}
            frame++;session.IsPaused=false;v.SetPresentationPaused(false);Assert.AreEqual(1,dialogs.ActiveCount);oldClose.Invoke();Assert.AreEqual(1,dialogs.ActiveCount);Find("DialogueHistory").onClick.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(session.SkipConfirmationOpen);Assert.IsFalse(session.BacklogOpen);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);long afterClose=session.Version;oldNormal.Invoke();next.onClick.Invoke();Assert.AreEqual(afterClose,session.Version);Assert.AreEqual(index,session.Index);
            frame++;entry.onClick.Invoke();v.Refresh();Assert.AreEqual(1,dialogs.ActiveCount);var stalePopup=Find("DialogueHistory").onClick;var beforeHide=Find("DialogueNext").onClick;
            if(skipMode){var confirm=Find("DialogueNext").onClick;confirm.Invoke();Assert.IsFalse(session.IsOpen);long ended=session.Version;confirm.Invoke();Assert.AreEqual(ended,session.Version);Assert.AreEqual(0,dialogs.ActiveCount);}
            else{var scroll=root.GetComponentInChildren<ScrollRect>();Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);layer.CloseCurrent();Assert.IsTrue(session.IsOpen);Assert.IsTrue(session.BacklogOpen);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(root.activeSelf);}
            layer.CloseCurrent();Assert.AreNotSame(beforeHide,next.onClick);stalePopup.Invoke();var oldSession=session;long oldVersion=oldSession.Version;
            session=new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript("dialogue-reopen",new[]{new Olca.Core.Domain.DialogueLine("フッキ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);frame++;Assert.IsTrue((await layer.ShowAsync("dialogue")).Succeeded);oldNormal.Invoke();stalePopup.Invoke();Assert.AreEqual(0,session.Index);Assert.AreEqual(oldVersion,oldSession.Version);Find("DialogueNext").onClick.Invoke();Assert.AreEqual(1,session.Index);layer.CloseCurrent();views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public void 旧Bindで会話を交換した後のskip取消は新入口へ戻り前の履歴callbackを拒否する()
        {
            var root=Panel("legacy-dialogue-rebind");var view=root.AddComponent<Olca.Presentation.Field.DialogueView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),()=>{});
            Olca.Core.Domain.DialogueSession Session(string id)=>new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript(id,new[]{new Olca.Core.Domain.DialogueLine("カイワ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);
            Button Find(string name)=>root.GetComponentsInChildren<Button>().Single(b=>b.name==name);
            var first=Session("first");view.Bind(first);EventSystem.current.SetSelectedGameObject(Find("DialogueHistory").gameObject);Find("DialogueHistory").onClick.Invoke();view.Refresh();var oldClose=Find("DialogueHistory").onClick;long oldVersion=first.Version;
            var second=Session("second");view.Bind(second);var entry=Find("DialogueSkip");EventSystem.current.SetSelectedGameObject(entry.gameObject);entry.onClick.Invoke();view.Refresh();Assert.IsTrue(second.SkipConfirmationOpen);
            oldClose.Invoke();Assert.IsTrue(second.SkipConfirmationOpen);Assert.AreEqual(oldVersion,first.Version);Find("DialogueHistory").onClick.Invoke();Assert.IsFalse(second.SkipConfirmationOpen);Assert.AreEqual(0,second.Index);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(oldVersion,first.Version);view.Bind(null);
        }
        [TestCase(false)][TestCase(true)]
        public void 履歴Dialogは読取ページと末尾位置を所有し同ticketの閉じると旧世代を分離する(bool managed)
        {
            var parent=Panel("backlog-underlay");parent.SetActive(true);bool live=true;int closed=0;
            var data=new[]{"ミジカイ ブン",string.Concat(Enumerable.Repeat("ナガイ リレキ\n",80))+"オワリ"};
            using(var scope=dialogs.CreateOwner(()=>live)){
                var first=DialogueBacklogDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),data);first.Finished+=_=>closed++;
                Assert.IsTrue(first.Open(parent,managed?dialogs:null,managed?scope:null,()=>live,true));Canvas.ForceUpdateCanvases();Assert.AreEqual(1,first.Position.Page);Assert.Greater(first.Scroll.content.rect.height,first.Scroll.viewport.rect.height);first.Scroll.verticalNormalizedPosition=0;
                var previous=first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueNext").onClick;var close=first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick;
                previous.Invoke();Assert.AreEqual(0,first.Position.Page);Assert.AreSame(close,first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick,"ページ更新でticketのclose listenerを作り直さない");first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueFast").onClick.Invoke();Assert.AreEqual(1,first.Position.Page);first.Scroll.verticalNormalizedPosition=0;var reading=first.Position;
                first.Abort();Assert.AreEqual(1,closed);frame++;var second=DialogueBacklogDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),data,reading);second.Finished+=_=>closed++;
                Assert.IsTrue(second.Open(parent,managed?dialogs:null,managed?scope:null,()=>live,true));Canvas.ForceUpdateCanvases();Assert.AreEqual(1,second.Position.Page);Assert.AreEqual(0,second.Scroll.verticalNormalizedPosition,.01);previous.Invoke();close.Invoke();Assert.IsTrue(second.IsOpen);Assert.AreEqual(1,second.Position.Page);Assert.AreEqual(1,closed);
                second.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick.Invoke();Assert.IsFalse(second.IsOpen);Assert.AreEqual(2,closed);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [Test] public void 旧SceneのCheckpoint親でも帰還Viewなしで進行Popupを開閉できる()
        {
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
            var checkpoint=Panel("legacy-checkpoint");checkpoint.SetActive(true);var popup=ProgressPopup();type.GetField("checkpointOverlay",flags).SetValue(controller,checkpoint);type.GetField("progressActionDialog",flags).SetValue(controller,popup);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"checkpoint legacy",false,true});Assert.IsTrue(popup.IsOpen);Assert.AreEqual("checkpoint legacy",popup.Message.text);popup.CancelButton.onClick.Invoke();Assert.IsFalse(popup.IsOpen);Assert.IsTrue(checkpoint.activeSelf);
        }
        private ConfirmationDialog DiscardPopup()=>ConfirmationDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),new ConfirmationContent{Title="編成",Message="破棄しますか",ConfirmName="DiscardDepartureDraft",CancelName="KeepEditingDeparture"});
        private ConfirmationDialog DeletePopup(string name,bool pending=false)=>ConfirmationDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),new ConfirmationContent{Title="削除",Subject=name,Message="削除しますか",ConfirmName="ConfirmDeleteDeck",CancelName="CancelDeleteDeck",ConfirmEnabled=!pending});
        [TestCase(false)][TestCase(true)]
        public void 共通確認Dialogの編成破棄用途は取消確定と旧世代を自分で所有する(bool managed)
        {
            var parent=Panel("deck-parent");parent.SetActive(true);int discarded=0,kept=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DiscardPopup();popup.Completed+=yes=>{if(yes)discarded++;else kept++;};Assert.IsTrue(popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));Assert.IsFalse(parent.activeSelf);
                var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick;popup.GetComponentsInChildren<Button>().Single(b=>b.name=="KeepEditingDeparture").onClick.Invoke();Assert.AreEqual(1,kept);Assert.IsTrue(parent.activeSelf);if(managed)Assert.IsTrue(dialogs.Blocks(scope));frame++;
                var next=DiscardPopup();next.Completed+=yes=>{if(yes)discarded++;};Assert.IsTrue(next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));old.Invoke();Assert.AreEqual(0,discarded);next.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick.Invoke();Assert.AreEqual(1,discarded);Assert.IsTrue(parent.activeSelf);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 共通確認Dialogの削除用途は対象Idと長名を保持し旧確定を別対象へ渡さない(bool managed)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);var parent=Panel("saved-parent");parent.SetActive(true);string deleted=null;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DeletePopup(new string('名',2000));popup.Completed+=accepted=>{if(accepted)deleted="first";};Assert.IsTrue(popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));Assert.NotNull(popup.GetComponentInChildren<ScrollRect>());
                var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck").onClick;popup.GetComponentsInChildren<Button>().Single(b=>b.name=="CancelDeleteDeck").onClick.Invoke();Assert.IsNull(deleted);frame++;
                var next=DeletePopup("別の保存名");next.Completed+=accepted=>{if(accepted)deleted="second";};next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true);old.Invoke();Assert.IsNull(deleted);next.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck").onClick.Invoke();Assert.AreEqual("second",deleted);Assert.IsTrue(parent.activeSelf);
            }
        }
        [Test] public void 削除待機Dialogの無効確定と親終了は業務結果を返さない()
        {
            var parent=Panel("pending-parent");parent.SetActive(true);bool live=true;int results=0;using(var scope=dialogs.CreateOwner(()=>live)){
                var popup=DeletePopup("保存名",true);popup.Completed+=r=>results++;popup.Open(parent,dialogs,scope,()=>live,true);var button=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck");Assert.IsFalse(button.interactable);var old=button.onClick;old.Invoke();Assert.AreEqual(0,results);live=false;popup.RefreshLifetime();old.Invoke();Assert.AreEqual(0,results);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 個別読取Dialogは長文末尾とresizeを保持し閉じた世代を拒否する(bool managed)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);var parent=Panel("read-parent");parent.SetActive(true);int closes=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=ReadOnlyDetailsDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),"詳細",string.Join("\n",Enumerable.Repeat("長文の検証",200))+"\nオワリシルシ",true);popup.Closed+=()=>closes++;popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,false);Canvas.ForceUpdateCanvases();popup.RefreshLayout();Assert.Greater(popup.Scroll.content.rect.height,popup.Scroll.viewport.rect.height);popup.Scroll.verticalNormalizedPosition=0;popup.RefreshLayout();((RectTransform)host.transform).sizeDelta=new Vector2(1440,1080);Canvas.ForceUpdateCanvases();popup.RefreshLayout();Assert.AreEqual(0,popup.Scroll.verticalNormalizedPosition,.001);Assert.IsTrue(popup.Scroll.content.GetComponent<Text>().text.EndsWith("オワリシルシ"));
                var old=popup.GetComponentInChildren<Button>().onClick;old.Invoke();Assert.AreEqual(1,closes);frame++;var next=ReadOnlyDetailsDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),"短文","短い説明",false);next.Closed+=()=>closes++;next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,false);old.Invoke();Assert.AreEqual(1,closes);next.GetComponentInChildren<Button>().onClick.Invoke();Assert.AreEqual(2,closes);Assert.IsTrue(parent.activeSelf);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 共通確認の直接破棄と拒否終了はcallbackと描画参照を返す(bool managed)
        {
            var parent=Panel("destroy-parent");parent.SetActive(true);int results=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DiscardPopup();popup.Completed+=_=>results++;popup.Terminated+=()=>{};popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true);var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick;
                // EditMode fixtureではMonoBehaviourの破棄messageを明示し、実Playでの自然な破棄も別途確認する。
                Assert.DoesNotThrow(()=>typeof(ConfirmationDialog).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,null));Assert.DoesNotThrow(()=>UnityEngine.Object.DestroyImmediate(popup.gameObject));old.Invoke();Assert.AreEqual(0,results);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsNull(typeof(ReturnDialog).GetField("Terminated",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup));Assert.IsNull(typeof(ReturnDialog).GetField("widgets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup));
                if(managed){frame++;var other=Panel("busy-other");dialogs.ShowPersistent(scope,other,Button(other,"yes"),Button(other,"no"),()=>true,_=>{});var rejected=DiscardPopup();rejected.Completed+=_=>results++;rejected.Terminated+=()=>{};Assert.IsFalse(rejected.Open(parent,dialogs,scope,()=>true,true));Assert.IsNull(typeof(ReturnDialog).GetField("Terminated",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rejected));Assert.AreEqual(0,results);Assert.AreEqual(1,dialogs.ActiveCount);}
            }
        }
        private sealed class ProgressOperationsProbe : IProgressActionOperations
        {
            public long Revision=>47;public bool Pending {get;private set;}public ProgressPendingAction PendingAction {get;private set;}public string Error=>"fixture-write-failure";public bool CanStartNewGame=>true;
            public int Saves,Starts,Retries;public long ConfirmedRevision;public bool RetrySucceeds;
            public bool ManualSave(){Saves++;Pending=true;PendingAction=ProgressPendingAction.ManualSave;return false;}
            public bool StartNewGame(long revision){Starts++;ConfirmedRevision=revision;Pending=true;PendingAction=ProgressPendingAction.NewGame;return false;}
            public bool Retry(){Retries++;if(!RetrySucceeds)return false;Pending=false;PendingAction=ProgressPendingAction.None;return true;}
        }
        private ProgressActionDialog ProgressPopup(string name="progress-owned")
        {
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var popup=factory.Panel(name,true);var value=popup.AddComponent<ProgressActionDialog>();value.Initialize(factory);value.ConfigureProgressPresentation(factory);return value;
        }
        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)]
        public void 進行確認は背面を遮り取消だけでは保存せず本文と操作が重ならない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);
            var popup=ProgressPopup();var service=new ProgressOperationsProbe();popup.OpenNewGame(service,dialogs,null,()=>true);Canvas.ForceUpdateCanvases();
            var backing=popup.transform.Find("ProgressReadingBacking").GetComponent<Image>();var surface=popup.transform.Find("ProgressReadingSurface").GetComponent<Image>();
            Assert.AreEqual(1f,backing.color.a);Assert.IsFalse(backing.raycastTarget);Assert.IsFalse(surface.raycastTarget);Assert.IsNotNull(surface.sprite);Assert.Less(surface.transform.GetSiblingIndex(),popup.Message.transform.GetSiblingIndex());Assert.AreEqual(Color.white,popup.Message.color);
            Rect Area(RectTransform t){var c=new Vector3[4];t.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);}
            var bounds=Area((RectTransform)popup.transform);var message=Area(popup.Message.rectTransform);var confirm=Area((RectTransform)popup.ConfirmButton.transform);var cancel=Area((RectTransform)popup.CancelButton.transform);var heading=Area((RectTransform)popup.transform.Find("ProgressHeading"));var guide=Area((RectTransform)popup.transform.Find("ProgressInputGuide"));
            foreach(var area in new[]{message,confirm,cancel,heading,guide}){Assert.GreaterOrEqual(area.xMin,bounds.xMin);Assert.LessOrEqual(area.xMax,bounds.xMax);Assert.GreaterOrEqual(area.yMin,bounds.yMin);Assert.LessOrEqual(area.yMax,bounds.yMax);}
            foreach(var area in new[]{confirm,cancel,heading,guide})Assert.IsFalse(message.Overlaps(area));Assert.IsFalse(confirm.Overlaps(cancel));
            Assert.IsTrue(popup.transform.Find("ProgressInputGuide").gameObject.activeSelf);Assert.AreSame(popup.CancelButton.gameObject,EventSystem.current.currentSelectedGameObject);popup.CancelButton.onClick.Invoke();Assert.IsFalse(popup.IsOpen);Assert.AreEqual(0,service.Starts);Assert.AreEqual(0,service.Saves);Assert.AreEqual(0,service.Retries);
            frame++;popup.OpenNotice("閉じるだけの通知",false,true,dialogs,null,()=>true);Assert.IsFalse(popup.transform.Find("ProgressInputGuide").gameObject.activeSelf);popup.CancelButton.onClick.Invoke();Assert.IsFalse(popup.IsOpen);
        }
        [TestCase(false)][TestCase(true)]
        public void 個別進行Dialogは失敗と同計画再試行を所有し同frame二重実行を拒否する(bool newGame)
        {
            var parent=Panel("progress-parent");parent.SetActive(true);var focus=Button(parent,"origin");EventSystem.current.SetSelectedGameObject(focus.gameObject);
            using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=ProgressPopup();var service=new ProgressOperationsProbe();int results=0;ProgressActionOutcome outcome=ProgressActionOutcome.Closed;popup.Completed+=r=>{results++;outcome=r.Outcome;};
                if(newGame){popup.OpenNewGame(service,dialogs,scope,()=>true);Assert.AreEqual(0,service.Starts);popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(47,service.ConfirmedRevision);}
                else popup.OpenManualSave(service,dialogs,scope,()=>true);
                Assert.AreEqual(newGame,popup.transform.Find("ProgressInputGuide").gameObject.activeSelf);Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(popup.CancelButton.interactable);popup.CancelButton.onClick.Invoke();Assert.IsTrue(popup.IsOpen);
                var old=popup.ConfirmButton.onClick;frame++;old.Invoke();old.Invoke();Assert.AreEqual(1,service.Retries);Assert.AreEqual(1,dialogs.ActiveCount);
                service.RetrySucceeds=true;frame++;old.Invoke();Assert.AreEqual(2,service.Retries);Assert.AreEqual(newGame?1:0,service.Starts);Assert.AreEqual(newGame?0:1,service.Saves);
                if(newGame){Assert.IsFalse(popup.IsOpen);Assert.AreEqual(ProgressActionOutcome.NewGameStarted,outcome);}
                else{Assert.IsTrue(popup.IsOpen);Assert.IsFalse(popup.ConfirmButton.gameObject.activeSelf);Assert.IsTrue(popup.CancelButton.interactable);popup.CancelButton.onClick.Invoke();}
                Assert.AreEqual(1,results);Assert.IsTrue(dialogs.Blocks(scope));old.Invoke();Assert.AreEqual(2,service.Retries);Assert.AreSame(focus.gameObject,EventSystem.current.currentSelectedGameObject);
            }
        }
        [Test] public void 進行Dialogの再生成は既知の未保存計画を新規保存せず引き継ぐ()
        {
            var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>true);var old=popup.ConfirmButton.onClick;popup.Abort();UnityEngine.Object.DestroyImmediate(popup.gameObject);
            var next=ProgressPopup("recreated");next.OpenManualSave(service,dialogs,null,()=>true);old.Invoke();Assert.AreEqual(1,service.Saves);Assert.AreEqual(0,service.Retries);Assert.IsFalse(next.CancelButton.interactable);
            service.RetrySucceeds=true;next.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,service.Retries);Assert.AreEqual(1,service.Saves);next.CancelButton.onClick.Invoke();Assert.IsFalse(next.IsOpen);
        }
        [Test] public void 進行DialogのSuspendResumeと親終了は旧callbackを失効させる()
        {
            bool live=true;var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>live);var old=popup.ConfirmButton.onClick;popup.Suspend();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(popup.IsOpen);
            popup.Resume(dialogs,null,()=>live);old.Invoke();Assert.AreEqual(0,service.Retries);Assert.AreEqual(1,service.Saves);Assert.AreEqual(1,dialogs.ActiveCount);
            var current=popup.ConfirmButton.onClick;live=false;popup.RefreshLifetime();current.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual(0,service.Retries);Assert.IsFalse(popup.IsOpen);Assert.IsTrue(service.Pending);
        }
        [Test] public void 他Dialogが占有中の進行要求は保存前に拒否する()
        {
            var panel=Panel("other");using(var scope=dialogs.CreateOwner(()=>true)){dialogs.ShowPersistent(scope,panel,Button(panel,"confirm"),Button(panel,"cancel"),()=>true,_=>{});var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>true);Assert.AreEqual(0,service.Saves);Assert.IsFalse(popup.IsOpen);Assert.AreEqual(1,dialogs.ActiveCount);}
        }
        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)]
        public void 撤退確認は背面を遮り警告と決定取消が画面内で重ならない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var root=factory.Panel("RetreatVisual",true);var view=root.AddComponent<RetreatConfirmationDialog>();view.Initialize(factory);view.Open(()=>new RetreatDialogState(true,false,false,false,false,false),null,null,null,()=>true);Canvas.ForceUpdateCanvases();
            var dialog=root.GetComponentInChildren<ConfirmationDialog>(true);Assert.IsTrue(view.IsOpen);Assert.AreEqual(1,dialog.GetComponent<Image>().color.a);var reading=dialog.GetComponentsInChildren<Image>(true).Single(v=>v.name=="RetreatReadingFace");Assert.IsNotNull(reading.sprite,"既存の水鏡素材を維持する");Assert.Less(reading.color.b,.5f);
            var warning=dialog.GetComponentsInChildren<Text>(true).Single(v=>v.name=="RetreatWarning");Assert.Less(reading.transform.GetSiblingIndex(),warning.transform.GetSiblingIndex(),"読取面を文字より手前に描かない");Assert.Less(reading.transform.GetSiblingIndex(),dialog.GetComponentsInChildren<Text>(true).Single(v=>v.name=="RetreatTitle").transform.GetSiblingIndex());Assert.Greater(warning.color.r,.8f);Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana("撤退すると攻略中に獲得したカード・通貨をすべて失います。\n経験値は獲得しません。既存の所持・成長は保持します。"),warning.text);Assert.AreEqual("テッタイのカクニン",dialog.GetComponentsInChildren<Text>(true).Single(v=>v.name=="RetreatTitle").text);
            Rect Area(RectTransform t){var c=new Vector3[4];t.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,c[0].y,c[2].x,c[2].y);}var bounds=Area((RectTransform)root.transform);var message=Area(warning.rectTransform);var confirm=Area((RectTransform)view.ConfirmButton.transform);var cancel=Area((RectTransform)view.CancelButton.transform);foreach(var area in new[]{message,confirm,cancel}){Assert.GreaterOrEqual(area.xMin,bounds.xMin);Assert.LessOrEqual(area.xMax,bounds.xMax);Assert.GreaterOrEqual(area.yMin,bounds.yMin);Assert.LessOrEqual(area.yMax,bounds.yMax);}Assert.IsFalse(message.Overlaps(confirm));Assert.IsFalse(message.Overlaps(cancel));Assert.IsFalse(confirm.Overlaps(cancel));
            view.CancelButton.onClick.Invoke();Assert.IsFalse(view.IsOpen);
        }
        [TestCase(false)][TestCase(true)]
        public void 撤退確認は選択後の比率変更でも同じ操作を保ち枠と実割当案内を表示する(bool accept)
        {
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var root=factory.Panel("RetreatFocus",true);var view=root.AddComponent<RetreatConfirmationDialog>();view.Initialize(factory);int effects=0;view.Confirmed+=()=>effects++;
            view.Open(()=>new RetreatDialogState(true,false,false,false,false,false),null,null,null,()=>true);var selected=accept?view.ConfirmButton:view.CancelButton;EventSystem.current.SetSelectedGameObject(selected.gameObject);
            foreach(var size in new[]{new Vector2(1920,1080),new Vector2(3840,2160),new Vector2(1440,1080),new Vector2(1920,1080)}){
                ((RectTransform)host.transform).sizeDelta=size;Canvas.ForceUpdateCanvases();var dialog=root.GetComponentInChildren<ConfirmationDialog>(true);typeof(ReturnDialog).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(dialog,null);view.RefreshLifetime();typeof(RetreatConfirmationDialog).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
                Assert.AreSame(selected.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(0,effects);
                Assert.IsTrue(selected.transform.Find("RetreatSelectionFrame").gameObject.activeSelf);Assert.IsFalse((accept?view.CancelButton:view.ConfirmButton).transform.Find("RetreatSelectionFrame").gameObject.activeSelf);
                var guide=root.GetComponentsInChildren<Text>(true).Single(t=>t.name=="RetreatInputGuide");Assert.AreEqual(BootstrapTitleView.InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad,true),guide.text);Assert.IsFalse(guide.raycastTarget);
            }
            selected.onClick.Invoke();Assert.IsFalse(view.IsOpen);Assert.AreEqual(accept?1:0,effects);
        }
        [Test] public void 撤退Dialogは最新可否で判断し取消後の旧確定は次世代へ作用しない()
        {
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var pause=Panel("legacy-pause");pause.SetActive(true);var origin=Button(pause,"RetreatButton");EventSystem.current.SetSelectedGameObject(origin.gameObject);
            var root=factory.Panel("retreat-owned",true);var popup=root.AddComponent<RetreatConfirmationDialog>();popup.Initialize(factory);bool paused=true,live=true;int effects=0;popup.Confirmed+=()=>effects++;
            RetreatDialogState State()=>new RetreatDialogState(paused,false,false,false,false,false);
            popup.Open(State,null,null,pause,()=>live);Assert.IsFalse(pause.activeSelf);var old=popup.ConfirmButton.onClick;paused=false;old.Invoke();Assert.AreEqual(0,effects);Assert.IsTrue(popup.IsOpen);
            paused=true;popup.CancelButton.onClick.Invoke();Assert.AreEqual(0,effects);Assert.IsTrue(pause.activeSelf);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);
            popup.Open(State,null,null,pause,()=>live);old.Invoke();Assert.AreEqual(0,effects);popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,effects);Assert.IsFalse(popup.IsOpen);
            popup.Open(State,null,null,pause,()=>live);var stale=popup.ConfirmButton.onClick;live=false;popup.RefreshLifetime();stale.Invoke();Assert.AreEqual(1,effects);Assert.IsFalse(popup.IsOpen);
        }
        [TestCase(false)][TestCase(true)]
        public void Host表示は初回要求だけ生成し再表示とPopupを共有して終了後に返す(bool managed)
        {
            var content=Panel("host-factory-content");content.SetActive(true);var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var factory=new GameplayPanelFactory(content.transform,font,_=>{});var bindings=new HostPanelBindings(owner,content.transform,managed?resources:null,managed?views:null);
            int generated=0,actions=0;GameplayPauseView pause=null;
            bindings.Register("pause","fixture-host-ui",()=>{generated++;var root=factory.Pause();pause=root.AddComponent<GameplayPauseView>();root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ResumeButton").onClick.AddListener(()=>{if(bindings.IsAlive)actions++;});pause.Initialize(()=>bindings.IsAlive);return root;},(root,ctx)=>pause.Show(ctx),root=>{if(root!=null)root.GetComponent<GameplayPauseView>().Hide();});
            Assert.AreEqual(0,generated);Assert.AreEqual(0,content.transform.childCount);Assert.AreEqual(0,resources.OwnedInstanceCount);
            var first=bindings.Show("pause");Assert.AreEqual(1,generated);if(!managed)first.SetActive(true);var old=first.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ResumeButton").onClick;
            Assert.AreSame(first,bindings.Show("pause"));Assert.AreEqual(1,generated);
            if(managed){old.Invoke();Assert.AreEqual(1,actions);views.GetLayer("fixture-host-ui").CloseCurrent();old.Invoke();Assert.AreEqual(1,actions);frame++;bindings.Show("pause");old.Invoke();Assert.AreEqual(1,actions);}
            int prompts=0;GameObject CreatePrompt(){prompts++;var panel=factory.Panel("ProgressActionOverlay",true);panel.AddComponent<ProgressActionDialog>().Initialize(factory);return panel;}
            var popup=bindings.Popup("prompt",CreatePrompt);Assert.AreSame(popup,bindings.Popup("prompt",CreatePrompt));Assert.AreEqual(1,prompts);
            Assert.AreEqual(managed?2:0,resources.OwnedInstanceCount);bindings.Dispose();bindings.Dispose();Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(first==null);Assert.IsTrue(popup==null);Assert.IsTrue(font!=null);Assert.Throws<ObjectDisposedException>(()=>bindings.Show("pause"));int beforeOld=actions;using(var replacement=new HostPanelBindings(owner,content.transform,managed?resources:null,managed?views:null)){Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(beforeOld,actions,"旧bindingの入力は新binding生成後も作用しない");}
            UnityEngine.Object.DestroyImmediate(content);
        }
        [Test]
        public void Host表示factory失敗は旧Viewを保ち同じ登録で再試行できる()
        {
            var content=Panel("host-factory-failure");content.SetActive(true);var factory=new GameplayPanelFactory(content.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});
            using(var bindings=new HostPanelBindings(owner,content.transform,resources,views)){
                bindings.Register("base","fixture-host-ui",()=>factory.Panel("base"),(root,ctx)=>root.SetActive(true),root=>{if(root!=null)root.SetActive(false);});var prior=bindings.Show("base");
                bool fail=true;bindings.Register("retry","fixture-host-ui",()=>factory.Component<GameplayPauseView>("retry",v=>{if(fail)throw new InvalidOperationException("fixture20");v.Initialize(()=>true);}).gameObject,(root,ctx)=>root.GetComponent<GameplayPauseView>().Show(ctx),root=>{if(root!=null)root.GetComponent<GameplayPauseView>().Hide();});
                Assert.Throws<InvalidOperationException>(()=>bindings.Show("retry"));Assert.IsTrue(prior.activeSelf);Assert.AreEqual("base",views.GetLayer("fixture-host-ui").CurrentKey);Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.AreEqual(1,content.transform.childCount);
                fail=false;var next=bindings.Show("retry");Assert.NotNull(next);Assert.IsFalse(prior.activeSelf);Assert.AreEqual(2,resources.OwnedInstanceCount);
                views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(next==null);Assert.IsTrue(prior==null);
            }
            UnityEngine.Object.DestroyImmediate(content);
        }
        [TestCase(false)][TestCase(true)]
        public void 同画面の再表示は旧contextを閉じ現在の実体だけ保持する(bool destroyed)
        {
            int created=0;ManagedScreenContext context=null;var layer=views.GetLayer("recreate20");
            layer.Register(owner,"screen",_=>{created++;return Task.FromResult(Panel("recreate20"));},(root,ctx)=>{context=ctx;root.SetActive(true);},root=>{if(root!=null)root.SetActive(false);});
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var priorContext=context;var first=host.transform.Find("recreate20").gameObject;
            var popup=Panel("recreate20-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");var pending=dialogs.Show(priorContext,popup,yes,no,_=>Assert.Fail("親終了は業務callbackを呼ばない"));
            if(destroyed)UnityEngine.Object.DestroyImmediate(first);else first.SetActive(false);
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var next=host.transform.Find("recreate20").gameObject;
            Assert.AreEqual(destroyed?2:1,created);Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.IsTrue(next.activeSelf);Assert.IsTrue(context.IsCurrent);Assert.IsFalse(priorContext.IsCurrent);Assert.AreNotSame(priorContext,context);Assert.AreEqual(DialogResult.ParentClosed,pending.GetAwaiter().GetResult());Assert.AreEqual(0,dialogs.ActiveCount);
            if(!destroyed)Assert.AreSame(first,next);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(next==null);
        }
        [Test]
        public void 同画面の再表示失敗は無効なcurrentとleaseを残さず再生成できる()
        {
            bool fail=false;ManagedScreenContext context=null;var layer=views.GetLayer("retry20");
            layer.Register(owner,"screen",_=>Task.FromResult(Panel("retry20")),(root,ctx)=>{context=ctx;root.SetActive(true);if(fail)throw new InvalidOperationException("fixture20-show");},root=>{if(root!=null)root.SetActive(false);});
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var prior=context;
            var popup=Panel("retry20-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");int callbacks=0;var pending=dialogs.Show(prior,popup,yes,no,_=>callbacks++);var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);var old=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
            host.transform.Find("retry20").gameObject.SetActive(false);fail=true;
            var failed=layer.ShowAsync("screen").GetAwaiter().GetResult();Assert.IsFalse(failed.Succeeded);Assert.IsFalse(failed.Cancelled);Assert.IsNull(layer.CurrentKey);Assert.IsFalse(prior.IsCurrent);Assert.IsFalse(context.IsCurrent);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,dialogs.ActiveCount,"Tick前に旧ticketを返す");Assert.AreEqual(DialogResult.ParentClosed,pending.GetAwaiter().GetResult());old.Invoke();Assert.AreEqual(0,callbacks);
            fail=false;Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);Assert.IsTrue(context.IsCurrent);Assert.AreEqual(1,resources.OwnedInstanceCount);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [TestCase(1920,1080,0f)] [TestCase(1440,1080,0f)] [TestCase(3840,2160,0f)]
        [TestCase(1920,1080,-700f)] [TestCase(1920,1080,700f)]
        public async Task 外界試用標的の名札は主人公を避け画面内に収まり非接触時は動かない(int width,int height,float actorX)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var type=view.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var actor=((Image)type.GetField("player",flags).GetValue(view)).rectTransform;
                var plate=new GameObject("TrialNamePlate",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();plate.SetParent(view.transform,false);plate.sizeDelta=new Vector2(316,48);
                var name=new GameObject("TrialName",typeof(RectTransform),typeof(Text)).GetComponent<Text>();name.transform.SetParent(view.transform,false);name.rectTransform.sizeDelta=new Vector2(300,40);name.raycastTarget=false;
                type.GetField("trialTargetPlate",flags).SetValue(view,plate);type.GetField("trialTargetName",flags).SetValue(view,name);
                actor.anchorMin=actor.anchorMax=new Vector2(.5f,.5f);actor.pivot=new Vector2(.5f,.5f);actor.anchoredPosition=new Vector2(actorX,0);actor.sizeDelta=new Vector2(256,256);
                var original=actor.anchoredPosition;var pose=model.Core.CaptureState();long frame=model.Core.Frame;int hp=model.Player.CurrentHp;
                var place=type.GetMethod("PlaceTrialTargetName",flags);var bounds=type.GetMethod("BoundsInView",flags);
                name.rectTransform.anchoredPosition=plate.anchoredPosition=new Vector2(actorX,100);place.Invoke(view,null);
                var actual=(Rect)bounds.Invoke(view,new object[]{plate});var body=(Rect)bounds.Invoke(view,new object[]{actor});var viewport=((RectTransform)view.transform).rect;
                Assert.IsFalse(actual.Overlaps(body));Assert.GreaterOrEqual(actual.xMin,viewport.xMin);Assert.LessOrEqual(actual.xMax,viewport.xMax);Assert.AreEqual(name.rectTransform.anchoredPosition,plate.anchoredPosition);Assert.AreEqual(100,plate.anchoredPosition.y);
                name.rectTransform.anchoredPosition=plate.anchoredPosition=new Vector2(0,300);place.Invoke(view,null);Assert.AreEqual(new Vector2(0,300),plate.anchoredPosition);
                Assert.AreEqual(original,actor.anchoredPosition);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(pose.Position,model.Core.CaptureState().Position);Assert.AreEqual(hp,model.Player.CurrentHp);Assert.IsFalse(name.raycastTarget);
            }
        }

        [TestCase(1920,1080)] [TestCase(1440,1080)] [TestCase(3840,2160)] public async Task 外界回復数値は表示中マップ名を避け終了後は不可視領域を使わない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(50);
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,font,resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var style=new Olca.Presentation.Effects.CombatNumberStyle(font,FontStyle.Normal,Color.white,.2f,.01f,1,1);view.ConfigureCombatFeedback(style);model.Player.Heal(1);view.Render();
                var obstacles=(List<RectTransform>)typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetField("feedbackObstacles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);var name=view.transform.Find("OutsideMapName").GetComponent<RectTransform>();Assert.Contains(name,obstacles);
                var canvas=view.GetComponent<Olca.Presentation.Exploration.OutsideCombatFeedbackCanvas>();canvas.Unbind();canvas.Bind(model.Player,style,()=>true,()=>Vector2.zero,100);model.Player.Heal(5);canvas.Paint(_=>name.anchoredPosition,obstacles);var number=view.GetComponentsInChildren<Text>().Single(x=>x.name.StartsWith("OutsideCombatNumber_")&&x.gameObject.activeInHierarchy);var corners=new Vector3[4];name.GetWorldCorners(corners);float top=corners.Select(p=>view.transform.InverseTransformPoint(p).y).Max();Assert.Greater(number.rectTransform.anchoredPosition.y-number.preferredHeight*.5f,top);Assert.AreSame(font,number.font);Assert.AreEqual("+5",number.text);
                for(int i=0;i<180;i++)model.Core.TickFrame(0);view.Render();Assert.IsFalse(name.gameObject.activeSelf);Assert.IsFalse(obstacles.Contains(name));Assert.AreEqual(56,model.Player.CurrentHp);
            }
        }

        [Test] public async Task 試用穴の負高度は描画カメラへ渡さず死亡保存直前もRenderが例外にならない()
        {
            var model=system.OutsideExploration;model.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationPit);model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);for(int i=0;i<100&&!model.Core.HasFallenOut;i++){model.Core.TickFrame(-1);model.AdvancePlayerFrame();if(model.Core.Height<0)Assert.DoesNotThrow(()=>binding.Pump(false));}
                Assert.IsTrue(model.Core.HasFallenOut);Assert.IsTrue(model.Player.IsDead);Assert.DoesNotThrow(()=>binding.Pump(false));var offset=(float)typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("ResolveVerticalViewOffset",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(binding.View,new object[]{model.Core});Assert.AreEqual(0,offset);Assert.Less(model.Core.Height,0);
            }
        }
        [TestCase(0,30,1f)] [TestCase(10,30,2f/3)] [TestCase(31,30,0f)] [TestCase(0,0,0f)] public async Task 外界強靭HUDは既存被弾と回復を読み再表示で状態を変更しない(int damage,int poise,float fraction)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,poise:30,staggerDurationFrames:6,poiseRecoveryDelayFrames:10));model.Player.SetPoise(poise);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;if(damage>0)model.Player.TakeDamage(damage);view.Render();var fill=view.transform.Find("OutsidePlayerPoiseTrack/OutsidePlayerPoiseFill").GetComponent<Image>();var text=view.transform.Find("OutsidePlayerPoise").GetComponent<Text>();Assert.AreEqual(fraction,fill.fillAmount,.0001f);Assert.AreEqual(430*fraction,fill.rectTransform.rect.width,.01f);Assert.IsFalse(fill.raycastTarget);Assert.IsFalse(text.raycastTarget);StringAssert.Contains("/ "+poise,text.text);Assert.IsFalse(float.IsNaN(fill.fillAmount));
                long frame=model.Core.Frame;int hp=model.Player.CurrentHp,accumulated=model.Player.AccumulatedDamage;bool staggered=model.Player.IsStaggered;binding.SetCovered(true);for(int i=0;i<4;i++)view.Render();views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(fraction,fill.fillAmount,.0001f);Assert.AreEqual(430*fraction,fill.rectTransform.rect.width,.01f);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);Assert.AreEqual(accumulated,model.Player.AccumulatedDamage);Assert.AreEqual(staggered,model.Player.IsStaggered);
                for(int i=0;i<32;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();Assert.IsFalse(model.Player.IsStaggered);Assert.AreEqual(0,model.Player.AccumulatedDamage);Assert.AreEqual(poise==0?0:1,fill.fillAmount);Assert.AreEqual(poise==0?0:430,fill.rectTransform.rect.width,.01f);Assert.AreEqual(hp,model.Player.CurrentHp);
            }
        }
        [Test] public async Task 管理編成地点は手札を隠し退出で新個体へ束縛し旧操作を拒否する()
        {
            var data=Enumerable.Range(0,30).Select(i=>new Olca.Core.Domain.Card("hud155-"+i,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,0,"")).ToArray();var owned=data.ToDictionary(c=>(c.Id,c.ExceedLevel),c=>1);var model=system.OutsideExploration;model.TryInitializeCards(data.Take(20).ToArray(),owned,new Olca.Core.Logic.Battle.SeededBattleRandom(155),out _);var old=model.Cards;old.TrySelect(1);model.QueueSelectedDeck("new30",data,owned,new Olca.Core.Logic.Battle.SeededBattleRandom(156),out _);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var button=view.transform.Find("OutsideHandButton2").GetComponent<Button>();var oldClick=button.onClick;void Heading(){var text=view.transform.Find("OutsideHandHeading").GetComponent<Text>();Canvas.ForceUpdateCanvases();text.cachedTextGenerator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size));Assert.GreaterOrEqual(text.cachedTextGenerator.characterCountVisible,text.text.TrimEnd().Length);Assert.GreaterOrEqual(text.rectTransform.rect.height,36,"新書体の単行の描画余裕を確保");}Heading();Assert.IsFalse(button.gameObject.activeSelf);Assert.IsFalse(view.transform.Find("OutsideHandName0").gameObject.activeSelf);oldClick.Invoke();Assert.AreEqual(1,old.SelectedIndex);
                void Walk(long x){for(int i=0;i<100&&model.Core.Position!=x;i++){model.Core.TickFrame(Math.Sign(x-model.Core.Position));model.AdvanceDeckLocation();}Assert.AreEqual(x,model.Core.Position);view.Render();Heading();}
                Walk(10*60000);Assert.AreNotSame(old,model.Cards);Assert.IsTrue(button.gameObject.activeSelf);Assert.IsTrue(view.transform.Find("OutsideHandName0").gameObject.activeSelf);oldClick.Invoke();Assert.AreEqual(0,model.Cards.SelectedIndex);Assert.AreEqual(1,old.SelectedIndex);button.onClick.Invoke();Assert.AreEqual(2,model.Cards.SelectedIndex);var currentClick=button.onClick;Walk(8*60000);currentClick.Invoke();Assert.AreEqual(2,model.Cards.SelectedIndex);var hand=model.Cards.Hand.ToArray();long frame=model.Core.Frame;binding.SetCovered(true);view.Render();views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(frame,model.Core.Frame);CollectionAssert.AreEqual(hand,model.Cards.Hand);Assert.IsFalse(button.gameObject.activeSelf);
            }
        }
        [TestCase(1920,1080,false)][TestCase(1440,1080,false)][TestCase(3840,2160,false)][TestCase(1920,1080,true)][TestCase(1440,1080,true)][TestCase(3840,2160,true)] public async Task 探索カードの未対応とゲージ不足を入力前に表示し選択と消費を変えない(int width,int height,bool pad)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var source=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(262),out _));for(int n=0;n<100&&!(model.Cards.Hand.Any(c=>c.Id=="tuning")&&model.Cards.Hand.Any(c=>c.Id=="heal"));n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="tuning")));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetField("cardUseGuide",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,(Func<string>)(()=>"カード シヨウ: "+(pad?"Y / △":"F")+" / サイクリック"));model.EnableTrialNormalKick();model.Player.SetBarrier(15);model.TickTraversal(1,dashDown:true,dashHeld:true);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out _));var label=view.transform.Find("OutsideCardUseGuide").GetComponent<Text>();long frame=model.Core.Frame,revision=model.Cards.HandRevision,gauge=model.Core.CardGauge.CurrentUnits;var hand=model.Cards.Hand.ToArray();view.Render();StringAssert.Contains("ミタイオウ",label.text);StringAssert.DoesNotContain("シヨウ ダッシュコウゲキ",label.text);StringAssert.Contains("バリア",label.text);StringAssert.Contains(pad?"Y / △":"F",label.text);Canvas.ForceUpdateCanvases();label.cachedTextGenerator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size));Assert.GreaterOrEqual(label.cachedTextGenerator.characterCountVisible,label.text.TrimEnd().Length);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(revision,model.Cards.HandRevision);Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);CollectionAssert.AreEqual(hand,model.Cards.Hand);
                Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal")));view.Render();StringAssert.DoesNotContain("ミタイオウ",label.text);StringAssert.Contains("シヨウ ダッシュコウゲキ",label.text);label.cachedTextGenerator.Populate(label.text,label.GetGenerationSettings(label.rectTransform.rect.size));Assert.GreaterOrEqual(label.cachedTextGenerator.characterCountVisible,label.text.TrimEnd().Length);Assert.IsTrue(model.Core.CardGauge.TrySpend(100));view.Render();StringAssert.Contains("ゲージが",label.text);view.SetCovered(true);view.Render();view.SetCovered(false);views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);view.Render();StringAssert.Contains("ゲージが",label.text);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(0,model.Core.CardGauge.CurrentUnits);
            }
        }
        [TestCase(-1,false)][TestCase(1,false)][TestCase(-1,true)][TestCase(1,true)] public async Task 外界地上三段は試用準備接触を論理命中窓から描き復帰Idleをゼロから始める(int facing,bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var prepare=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/ground_kick_prepare":"OlcaCharacterMotion/trial_ground_kick_prepare");var contact=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/ground_kick_contact":"OlcaCharacterMotion/trial_ground_kick_contact");Assert.IsNotNull(prepare);Assert.IsNotNull(contact);Assert.AreEqual(prepare.rect,contact.rect);Assert.AreEqual(prepare.pivot,contact.pivot);Assert.AreEqual(100f,prepare.pixelsPerUnit,.001f);Assert.AreEqual(100f,contact.pixelsPerUnit,.001f);Assert.AreEqual(256,prepare.texture.width);Assert.AreEqual(256,contact.texture.width);
            string root=fire?"OlcaCharacterMotion/TrialFire/":"OlcaCharacterMotion/trial_";
            var prepares=new[]{prepare,Resources.Load<Sprite>(root+"ground_second_prepare"),Resources.Load<Sprite>(root+"ground_third_prepare")};var contacts=new[]{contact,Resources.Load<Sprite>(root+"ground_second_contact"),Resources.Load<Sprite>(root+"ground_third_contact")};Assert.AreEqual(3,prepares.Distinct().Count());Assert.AreEqual(3,contacts.Distinct().Count());for(int i=0;i<3;i++){Assert.IsNotNull(prepares[i]);Assert.IsNotNull(contacts[i]);Assert.AreEqual(prepares[i].pivot,contacts[i].pivot);Assert.AreEqual(100,contacts[i].pixelsPerUnit);Assert.AreEqual(256,contacts[i].texture.width);}
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var frames=new Olca.Presentation.Exploration.TrialGroundComboMotion())
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;model.TickTraversal(facing);model.TickTraversal(0);for(int n=0;n<20;n++)model.TickTraversal(0);view.Render();var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var point=body.rectTransform.anchoredPosition;long start=model.Core.Frame;long position=model.Core.Position;
                for(int stage=0;stage<3;stage++){
                    Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,model.Core.Frame+1,out var error),error);view.Render();Assert.AreEqual(stage,(int)model.NormalKick.Kind);Assert.AreSame(frames.Resolve((Olca.Core.Logic.Integration.OutsideKickKind)stage,0,model.NormalKick.ActiveTiming,fire),body.sprite);
                    for(int age=1;age<=21;age++){model.TickTraversal(0);view.Render();if(age<21)Assert.AreSame(frames.Resolve((Olca.Core.Logic.Integration.OutsideKickKind)stage,age,model.NormalKick.ActiveTiming,fire),body.sprite);else{Assert.IsFalse(model.NormalKick.IsActive);Assert.AreEqual(0,model.Core.IdleFrame);if(fire)Assert.AreEqual("trial_fire_idle",body.sprite.name);else StringAssert.EndsWith("/Idle/heroine_idle_01.png",UnityEditor.AssetDatabase.GetAssetPath(body.sprite));}Assert.AreEqual(point,body.rectTransform.anchoredPosition);Assert.AreEqual(age<21?facing:-facing,body.rectTransform.localScale.x);Assert.AreEqual(position,model.Core.Position);}
                }
                Assert.AreEqual(start+63,model.Core.Frame);Assert.AreEqual(100,model.Player.CurrentHp);model.TickTraversal(0);view.Render();Assert.AreEqual(1,model.Core.IdleFrame);
            }
        }

        [TestCase(-1,false)][TestCase(1,false)][TestCase(-1,true)][TestCase(1,true)] public async Task 外界空中前蹴りは命中窓だけ補正済み試用本体を描き着地で解除する(int facing,bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var contact=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/air_forward_contact":"OlcaCharacterMotion/trial_air_forward_kick_contact");Assert.IsNotNull(contact);Assert.AreEqual(256,contact.texture.width);Assert.AreEqual(256,contact.texture.height);Assert.AreEqual(100f,contact.pixelsPerUnit,.001f);Assert.AreEqual(156f,contact.pivot.x,.01f);Assert.AreEqual(14f,contact.pivot.y,.01f);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var rise=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite;if(fire)rise=Resources.Load<Sprite>("OlcaCharacterMotion/TrialFire/jump_rise");
                model.TickTraversal(facing);model.TickTraversal(0,true,true);Assert.IsFalse(model.Core.IsGrounded);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out var error),error);Assert.AreEqual(Olca.Core.Logic.Integration.OutsideKickKind.AirForward,model.NormalKick.Kind);
                for(int age=0;age<21;age++){
                    if(age>0)model.TickTraversal(-facing,false,true);long frame=model.Core.Frame,x=model.Core.Position,y=model.Core.Height;view.Render();Assert.AreSame(age>=6&&age<9?contact:rise,body.sprite);Assert.AreEqual(-facing,body.rectTransform.localScale.x);Assert.AreEqual(body.sprite.pivot/body.sprite.rect.size,body.rectTransform.pivot);view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);Assert.AreEqual(y,model.Core.Height);Assert.AreEqual(100,model.Player.CurrentHp);
                    if(age==7){view.SetCovered(true);view.Render();Assert.AreEqual(frame,model.Core.Frame);view.SetCovered(false);view.Render();Assert.AreSame(contact,body.sprite);}
                }
                model.TickTraversal(0);view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreNotSame(contact,body.sprite);
                while(!model.Core.IsGrounded)model.TickTraversal(0);model.TickTraversal(0,true,true);while(model.Core.VerticalVelocity>0||model.Core.Height>15000)model.TickTraversal(0);
                Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out error),error);while(!model.Core.IsGrounded)model.TickTraversal(0);view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreNotSame(contact,body.sprite);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }

        [TestCase(-1,false)][TestCase(1,false)][TestCase(-1,true)][TestCase(1,true)] public async Task 外界空中下蹴りは既存前隙と専用接触復帰を描き落下や着地と時計を変えない(int facing,bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
            var contact=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/air_down_contact":"OlcaCharacterMotion/trial_air_down_contact");Assert.IsNotNull(contact);Assert.AreEqual(256,contact.texture.width);Assert.AreEqual(256,contact.texture.height);Assert.AreEqual(100f,contact.pixelsPerUnit,.001f);Assert.AreEqual(127f,contact.pivot.x,.01f);Assert.AreEqual(14f,contact.pivot.y,.01f);
            var recovery=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/air_down_recovery":"OlcaCharacterMotion/trial_air_down_recovery");Assert.IsNotNull(recovery);Assert.AreEqual(contact.pivot,recovery.pivot);Assert.AreEqual(contact.rect,recovery.rect);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var rise=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite;if(fire)rise=Resources.Load<Sprite>("OlcaCharacterMotion/TrialFire/jump_rise");
                model.TickTraversal(facing);model.TickTraversal(0,true,true);Assert.IsFalse(model.Core.IsGrounded);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out var error,-1),error);Assert.AreEqual(Olca.Core.Logic.Integration.OutsideKickKind.AirDown,model.NormalKick.Kind);StringAssert.Contains("ジュンビのみキソン",model.CardUseMessage);StringAssert.DoesNotContain("ミセツゾク",model.CardUseMessage);
                for(int age=0;age<21;age++){
                    if(age>0)model.TickTraversal(-facing,false,true);long frame=model.Core.Frame,x=model.Core.Position,y=model.Core.Height;view.Render();Assert.AreSame(age<6?rise:age<9?contact:recovery,body.sprite);Assert.AreEqual(age>=9,model.NormalKick.IsRecovery);Assert.AreEqual(-facing,body.rectTransform.localScale.x);Assert.AreEqual(body.sprite.pivot/body.sprite.rect.size,body.rectTransform.pivot);view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);Assert.AreEqual(y,model.Core.Height);Assert.AreEqual(100,model.Player.CurrentHp);
                    if(age==11){views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);view.Render();Assert.AreSame(recovery,body.sprite);Assert.AreEqual(frame,model.Core.Frame);}
                    if(age==7){view.SetCovered(true);view.Render();Assert.AreEqual(frame,model.Core.Frame);view.SetCovered(false);view.Render();Assert.AreSame(contact,body.sprite);}
                }
                model.TickTraversal(0);view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreNotSame(contact,body.sprite);Assert.AreNotSame(recovery,body.sprite);Assert.IsFalse(model.NormalKick.IsRecovery);Assert.AreEqual(-model.Core.Facing,body.rectTransform.localScale.x);
                while(!model.Core.IsGrounded)model.TickTraversal(0);model.TickTraversal(0,true,true);while(model.Core.VerticalVelocity>0||model.Core.Height>15000)model.TickTraversal(0);
                Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out error,-1),error);while(!model.Core.IsGrounded)model.TickTraversal(0);view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreNotSame(contact,body.sprite);Assert.AreNotSame(recovery,body.sprite);Assert.IsFalse(model.NormalKick.IsRecovery);Assert.AreEqual(-model.Core.Facing,body.rectTransform.localScale.x);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }

        [TestCase(-1)][TestCase(1)] public async Task 空中上蹴りの既存代替表示も逆移動と別の開始向きを保ち終了で通常方向へ戻る(int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();model.TickTraversal(facing);model.TickTraversal(0,true,true);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out var error,1),error);Assert.AreEqual(Olca.Core.Logic.Integration.OutsideKickKind.AirUp,model.NormalKick.Kind);long start=model.Core.Position;
                for(int age=0;age<21;age++){if(age>0)model.TickTraversal(-facing,false,true);view.Render();Assert.AreEqual(-facing,body.rectTransform.localScale.x);Assert.AreEqual(facing,model.NormalKick.AttackFacing);if(age>0){Assert.AreEqual(-facing,model.Core.Facing);Assert.Less((model.Core.Position-start)*facing,0);}}
                model.TickTraversal(0);view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreEqual(-model.Core.Facing,body.rectTransform.localScale.x);
            }
        }
        [TestCase(-1)][TestCase(1)] public void 空中蹴り開始向きは逆移動中の保存再開と旧leaseを越えて保持し終了で解放する(int facing)
        {
            using(var s=new Olca.Core.Logic.Integration.OutsideExplorationSession())using(var cold=new Olca.Core.Logic.Integration.OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));s.EnableTrialNormalKick();var old=s.AttachPresentation();s.TickTraversal(facing);s.TickTraversal(0,true,true);Assert.IsTrue(s.ProcessCardUseInput(false,false,true,0,1,out var error,-1),error);s.TickTraversal(-facing,false,true);Assert.AreEqual(-facing,s.Core.Facing);Assert.AreEqual(facing,old.NormalKickFacing);old.Dispose();Assert.IsNull(old.NormalKickFacing);
                var data=s.CaptureRuntime();var actor=Olca.Core.Domain.OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;Assert.IsTrue(cold.RestoreSavedState(actor,data,null,null));var superseded=s.AttachPresentation();
                using(var live=s.AttachPresentation())using(var resumed=cold.AttachPresentation()){
                    Assert.IsNull(superseded.NormalKickFacing);superseded.Dispose();Assert.IsTrue(live.IsCurrent);Assert.AreEqual(facing,live.NormalKickFacing);Assert.AreEqual(facing,resumed.NormalKickFacing);
                    while(s.IsAttacking){s.TickTraversal(-facing,false,true);cold.TickTraversal(-facing,false,true);Assert.AreEqual(s.Core.Frame,cold.Core.Frame);Assert.AreEqual(s.Core.Position,cold.Core.Position);Assert.AreEqual(s.Core.Height,cold.Core.Height);Assert.AreEqual(s.Core.VerticalVelocity,cold.Core.VerticalVelocity);Assert.AreEqual(s.Player.CurrentHp,cold.Player.CurrentHp);if(s.IsAttacking){Assert.AreEqual(facing,live.NormalKickFacing);Assert.AreEqual(facing,resumed.NormalKickFacing);}}
                    Assert.IsNull(live.NormalKickFacing);Assert.IsNull(resumed.NormalKickFacing);Assert.AreEqual(-facing,cold.Core.Facing);
                }
            }
        }

        [TestCase(-1,false)][TestCase(1,false)][TestCase(-1,true)][TestCase(1,true)] public async Task 外界ダッシュ攻撃は命中窓だけ専用試用本体を描き方向と時計を変えず終了復帰する(int facing,bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();if(fire){model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);model.Player.SetElementResistances(Olca.Core.Domain.ElementAffinity.CreatePlayerBase(Olca.Core.Domain.ElementType.Fire));}
            typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreRecoveryPosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(model.Core,new object[]{14L*60000});
            var contact=Resources.Load<Sprite>(fire?"OlcaCharacterMotion/TrialFire/dash_kick_contact":"OlcaCharacterMotion/trial_dash_kick_contact");Assert.IsNotNull(contact);Assert.AreEqual(256,contact.texture.width);Assert.AreEqual(256,contact.texture.height);Assert.AreEqual(100f,contact.pixelsPerUnit,.001f);Assert.AreEqual(new Vector2(127,14),contact.pivot+contact.rect.position);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var dash=fire?Resources.Load<Sprite>("OlcaCharacterMotion/TrialFire/dash"):resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredDashPlayerSprite;
                model.TickTraversal(facing,dashDown:true,dashHeld:true);Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out var error),error);Assert.AreEqual(Olca.Core.Logic.Integration.OutsideKickKind.Dash,model.NormalKick.Kind);
                long end=model.NormalKick.EndsAt;
                while(model.Core.Frame<end){long frame=model.Core.Frame,x=model.Core.Position,y=model.Core.Height;int hp=model.Player.CurrentHp;bool hit=model.NormalKick.IsHitWindow;view.Render();Assert.AreSame(hit?contact:dash,body.sprite);Assert.AreEqual(-facing,body.rectTransform.localScale.x);Assert.AreEqual(body.sprite.pivot/body.sprite.rect.size,body.rectTransform.pivot);for(int n=0;n<3;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);Assert.AreEqual(y,model.Core.Height);Assert.AreEqual(hp,model.Player.CurrentHp);
                    if(hit){view.SetCovered(true);view.Render();Assert.AreEqual(frame,model.Core.Frame);view.SetCovered(false);views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);view.Render();Assert.AreSame(contact,body.sprite);}
                    model.TickTraversal(-facing);
                }
                Assert.AreEqual(facing>0?90:100,model.Player.CurrentHp); // このダッシュ区間は標的1まで。標的2の接触域へは届かない。描画自身はHPを変更しない。
                StringAssert.Contains("セッショク10（カリ）",view.transform.Find("OutsideTrialTargetName").GetComponent<Text>().text);StringAssert.Contains("AIコウゲキ",view.transform.Find("OutsideTrialTargetName").GetComponent<Text>().text);
                view.Render();Assert.IsFalse(model.NormalKick.IsActive);Assert.AreNotSame(contact,body.sprite);Assert.IsFalse(model.Core.IsDashing);model.TickTraversal(0);view.Render();if(fire)Assert.AreEqual("trial_fire_idle",body.sprite.name);else StringAssert.EndsWith("/Idle/heroine_idle_01.png",UnityEditor.AssetDatabase.GetAssetPath(body.sprite));
            }
        }

        [Test] public async Task 外界カエンの走行八枚は既存時計を読み通常復帰とView再生成で参照を残さない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var save=new Olca.Core.Domain.ClearReturnSession(new DoorBoundaryStore());
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.TrialFire,null,out var error),error);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var seen=new System.Collections.Generic.HashSet<string>();
                for(int n=0;n<40;n++){model.TickTraversal(1);long frame=model.Core.Frame;view.Render();StringAssert.StartsWith("trial_fire_run_",body.sprite.name);seen.Add(body.sprite.name);Assert.AreEqual(new Vector2(128,17),body.sprite.pivot);Assert.AreEqual(-1,body.rectTransform.localScale.x);view.Render();Assert.AreEqual(frame,model.Core.Frame);}
                Assert.AreEqual(8,seen.Count);model.TickTraversal(0);view.Render();Assert.AreEqual("trial_fire_idle",body.sprite.name);long stopped=model.Core.Frame;view.SetCovered(true);view.Render();Assert.AreEqual(stopped,model.Core.Frame);view.SetCovered(false);
                Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.Basic,null,out error),error);view.Render();StringAssert.StartsWith("heroine_idle_",body.sprite.name);Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.TrialFire,null,out error),error);view.Render();Assert.AreEqual("trial_fire_idle",body.sprite.name);
                UnityEngine.Object.DestroyImmediate(view.gameObject);Assert.IsTrue((await binding.OpenAsync()).Succeeded);body=binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>();Assert.AreEqual("trial_fire_idle",body.sprite.name);Assert.AreEqual(stopped,model.Core.Frame);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }

        [Test] public async Task 外界カエン呼吸は既存Idle時計から三姿勢を描き足の基準と停止再入を維持する()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var save=new Olca.Core.Domain.ClearReturnSession(new DoorBoundaryStore());
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.TrialFire,null,out var error),error);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var seen=new System.Collections.Generic.HashSet<string>();long position=model.Core.Position;
                for(int n=0;n<240;n++){model.TickTraversal(0);long frame=model.Core.Frame;view.Render();seen.Add(body.sprite.name);Assert.AreEqual(new Vector2(127,14),body.sprite.rect.position+body.sprite.pivot);view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(position,model.Core.Position);}Assert.AreEqual(3,seen.Count);CollectionAssert.AreEquivalent(new[]{"trial_fire_idle","trial_fire_idle_breathe_mid","trial_fire_idle_breathe_high"},seen);var stopped=body.sprite;long clock=model.Core.Frame;view.SetCovered(true);for(int n=0;n<5;n++)view.Render();Assert.AreSame(stopped,body.sprite);Assert.AreEqual(clock,model.Core.Frame);view.SetCovered(false);views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(stopped,body.sprite);Assert.AreEqual(clock,model.Core.Frame);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }

        [Test] public async Task 外界カエン識別は実切替保存の成功だけで衣装を替え被覆再入と解除で時計を変えない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var store=new DoorBoundaryStore();var save=new Olca.Core.Domain.ClearReturnSession(store);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();Assert.IsNull(view.transform.Find("OutsideTrialFireKamui"));var sprite=body.sprite;var originalColor=body.color;var frame=model.Core.Frame;
                Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.TrialFire,null,out var error),error);view.Render();var layer=view.transform.Find("OutsideTrialFireKamui").GetComponent<Image>();Assert.IsNull(view.transform.Find("OutsideDefensiveField"));for(int n=0;n<4;n++){view.Render();Assert.Less(layer.transform.GetSiblingIndex(),body.transform.GetSiblingIndex());}model.Player.SetBarrier(5);view.Render();var defense=view.transform.Find("OutsideDefensiveField");Assert.IsTrue(layer.enabled);Assert.IsFalse(layer.raycastTarget);Assert.Less(layer.transform.GetSiblingIndex(),defense.GetSiblingIndex());Assert.Less(defense.GetSiblingIndex(),body.transform.GetSiblingIndex());Assert.AreEqual(body.rectTransform.anchoredPosition,layer.rectTransform.anchoredPosition);Assert.AreEqual("trial_fire_idle",body.sprite.name);Assert.AreNotSame(sprite,body.sprite);Assert.AreEqual(originalColor,body.color);
                model.Player.SetBarrier(0);for(int n=0;n<4;n++){view.Render();Assert.IsFalse(defense.GetComponent<Image>().enabled);Assert.Less(layer.transform.GetSiblingIndex(),body.transform.GetSiblingIndex());}model.Player.SetBarrier(5);view.Render();int writes=store.Writes;view.SetCovered(true);for(int n=0;n<4;n++){view.Render();Assert.Less(layer.transform.GetSiblingIndex(),defense.GetSiblingIndex());Assert.Less(defense.GetSiblingIndex(),body.transform.GetSiblingIndex());}Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(100,model.Player.CurrentHp);Assert.AreEqual(5,model.Player.BarrierHp);Assert.AreEqual(writes,store.Writes);views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(layer.enabled);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(layer.enabled);
                store.Fail=true;Assert.IsFalse(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.Basic,null,out _));view.Render();Assert.IsTrue(layer.enabled);store.Fail=false;Assert.IsTrue(model.TrySwitchOutsideStyle(save,Olca.Core.Domain.DepartureStyleDefinition.Basic,null,out error),error);view.Render();Assert.IsFalse(layer.enabled);Assert.AreSame(sprite,body.sprite);Assert.AreEqual(frame,model.Core.Frame);
            }
        }

        [Test] public async Task 外界の毒本体色は確定状態だけを読み再表示と解除と別個体で残らない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();Assert.AreEqual(Color.white,body.color);var sprite=body.sprite;var size=body.rectTransform.sizeDelta;var pivot=body.rectTransform.pivot;
                model.Player.ApplyPoison("tint153",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,1000,1),60);view.Render();Assert.AreEqual(Color.white,body.color,"予約だけでは未確定の毒を描かない");model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);Assert.AreEqual(1,body.color.a);Assert.AreSame(sprite,body.sprite);Assert.AreEqual(size,body.rectTransform.sizeDelta);Assert.AreEqual(pivot,body.rectTransform.pivot);
                long frame=model.Core.Frame;int hp=model.Player.CurrentHp;var states=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons());view.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);CollectionAssert.AreEqual(states,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons()));
                views.GetLayer("return-hub").CloseCurrent();Assert.AreEqual(Color.white,body.color);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);
                model.Player.ClearPoisons();model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(Color.white,body.color);
                model.Player.ApplyPoison("tint153-short",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,50,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);for(int i=0;i<4;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();Assert.AreEqual(Color.white,body.color,"自然失効でも戻る");
                UnityEngine.Object.DestroyImmediate(view.gameObject);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(Color.white,binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>().color);
            }
        }
        [Test] public async Task 外界状態HUDは既存毒の割合を読み停止再表示解除で時計と保存を変更しない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Assert.IsNull(view.transform.Find("OutsideCombatStatusIcons"),"無状態なら生成しない");
                model.Player.ApplyPoison("hud149",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,10000,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();
                var strip=view.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>();Assert.IsNotNull(strip);Assert.AreEqual(1,strip.VisibleCount);Assert.IsTrue(strip.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget));
                for(int i=0;i<149;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();var fill=strip.GetComponentsInChildren<Image>().Single(i=>i.name=="Remaining");Assert.AreEqual(.75f,fill.fillAmount,.0001f);
                var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons());long frame=model.Core.Frame;int hp=model.Player.CurrentHp;
                binding.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons()));
                views.GetLayer("return-hub").CloseCurrent();Assert.AreEqual(0,strip.VisibleCount);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(1,strip.VisibleCount);Assert.AreEqual(.75f,fill.fillAmount,.0001f);Assert.AreEqual(frame,model.Core.Frame);
                model.Player.ClearPoisons();model.Core.TickFrame(0);model.AdvancePlayerFrame();frame++;view.Render();Assert.AreEqual(0,strip.VisibleCount);Assert.AreEqual(frame,model.Core.Frame);
                model.Player.ApplyPoison("hud149b",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,10000,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreSame(strip,view.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>());Assert.AreEqual(1,strip.VisibleCount);Assert.AreEqual(599f/600,fill.fillAmount,.0001f);
                UnityEngine.Object.DestroyImmediate(strip.gameObject);Assert.DoesNotThrow(()=>binding.ReturnToHub());Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(1,binding.View.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>().VisibleCount);
            }
        }
        [Test] public async Task 所有者の回復保存待ちだけ画面を再生成でき戻る条件と旧世代を緩めない()
        {
            bool allowed=true,pending=false;int returned=0;var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(40);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>allowed,()=>returned++,restoreAllowed:()=>pending)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var old=binding.View.GetComponentsInChildren<Button>().Single(v=>v.name=="CloseOutsideExploration").onClick;allowed=false;pending=true;var lost=binding.View;typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lost,null);UnityEngine.Object.DestroyImmediate(lost.gameObject);Assert.IsTrue(binding.HasLostView);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(40,model.Player.CurrentHp);Assert.IsFalse(binding.ReturnToHub());old.Invoke();Assert.AreEqual(0,returned);Assert.IsTrue(binding.IsOpen);pending=false;allowed=true;Assert.IsTrue(binding.ReturnToHub());Assert.AreEqual(1,returned);
            }
        }
        [TestCase(1920,1080)][TestCase(3840,2160)][TestCase(1440,1080)]
        public async Task 探索攻撃表示は両対象の撃破を一度だけ描き保持再描画と再入で進行を変えない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.EnableTrialNormalKick();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.Render();
                var fx=view.GetComponentsInChildren<Olca.Presentation.Effects.ProvisionalCombatGraphic>(true);Assert.AreEqual(3,fx.Length);var slash=view.GetComponentsInChildren<Image>(true).Single(v=>v.name=="OutsideProvisionalKickTrail");Assert.IsFalse(slash.enabled);Assert.IsFalse(slash.raycastTarget);Assert.IsNotNull(slash.GetComponent<CanvasRenderer>());Assert.IsFalse(fx.Single(v=>v.name=="OutsideProvisionalChargeRing").enabled);Assert.IsTrue(fx.All(v=>!v.raycastTarget));Assert.IsTrue(fx.All(v=>v.GetComponent<CanvasRenderer>()!=null),"実描画に必要なRendererを全ピクセル演出へ付ける");
                var burst=fx.Where(v=>v.name.StartsWith("OutsideProvisionalTargetDeath_")).ToArray();Assert.IsTrue(burst.All(v=>!v.enabled));
                model.TrialTarget.TakeDamage(999,Olca.Core.Domain.ElementType.None);model.SecondTrialTarget.TakeDamage(999,Olca.Core.Domain.ElementType.None);view.Render();Assert.IsTrue(burst.All(v=>v.enabled));
                long frame=model.Core.Frame;for(int n=0;n<100;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.IsTrue(burst.All(v=>v.enabled));
                Assert.IsTrue(binding.ReturnToHub());Assert.IsTrue(burst.All(v=>!v.enabled));Assert.IsTrue((await binding.OpenAsync()).Succeeded);view=binding.View;view.Render();Assert.IsTrue(view.GetComponentsInChildren<Olca.Presentation.Effects.ProvisionalCombatGraphic>(true).Where(v=>v.name.StartsWith("OutsideProvisionalTargetDeath_")).All(v=>!v.enabled),"再入済みdeadを新たな撃破として表示しない");
                Assert.IsTrue(model.ProcessCardUseInput(false,false,true,0,1,out _));view.Render();var caption=view.GetComponentsInChildren<Text>(true).Single(v=>v.name=="OutsideProvisionalKickCaptionText");StringAssert.Contains("1ダンメ",caption.text);Assert.GreaterOrEqual(caption.rectTransform.rect.height,caption.preferredHeight);Assert.GreaterOrEqual(caption.rectTransform.rect.width,caption.preferredWidth);
                for(int n=0;n<6;n++)model.TickTraversal(0);view.Render();slash=view.GetComponentsInChildren<Image>(true).Single(v=>v.name=="OutsideProvisionalKickTrail");Assert.IsTrue(slash.enabled);Assert.AreEqual("trial_slash_0_0",slash.sprite.name);frame=model.Core.Frame;for(int n=0;n<10;n++)view.Render();Assert.AreEqual(frame,model.Core.Frame);for(int n=0;n<3;n++)model.TickTraversal(0);view.Render();Assert.AreEqual("trial_slash_0_2",slash.sprite.name);for(int n=0;n<9;n++)model.TickTraversal(0);view.Render();Assert.IsFalse(slash.enabled);
            }
        }
        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)]
        public async Task リロード案内は既知別マップでも表示しHome地形案内だけ帰還で戻る(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var session=system.OutsideExploration;bool trial=true;
            Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));session.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{},trialRoute:()=>trial)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                void Check(bool reloadVisible,bool routeVisible){long frame=session.Core.Frame,position=session.Core.Position,gauge=session.Core.CardGauge.CurrentUnits;int hp=session.Player.CurrentHp;var snapshot=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(session.Core.CaptureState());for(int n=0;n<3;n++)view.Render();Assert.AreEqual(reloadVisible,view.transform.Find("OutsideReloadPoint").gameObject.activeSelf,"OutsideReloadPoint");Assert.AreEqual(routeVisible,view.transform.Find("OutsideTrialRouteHint").gameObject.activeSelf,"OutsideTrialRouteHint");Assert.AreEqual(frame,session.Core.Frame);Assert.AreEqual(position,session.Core.Position);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(hp,session.Player.CurrentHp);CollectionAssert.AreEqual(snapshot,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(session.Core.CaptureState()));}
                Check(true,true);session.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);Assert.IsFalse(Olca.Core.Logic.Integration.OutsideTrialRoute.IsReloadZone(session.Core));Check(true,false);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Check(true,false);
                session.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorHomePoint);Check(true,true);trial=false;Check(false,false);trial=true;Check(true,true);
            }
        }
        [Test] public async Task 外界手札の枯渇表示は空枠を無効化し古い選択でも落ちず再装填で戻る()
        {
            var model=system.OutsideExploration;var source=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(157),out _));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var old=view.transform.Find("OutsideHandButton2").GetComponent<Button>().onClick;var c=model.Cards;for(int i=0;i<20;i++){if(c.SelectedCard==null)c.TrySelect(c.Hand.Count-1);Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,model.Core.CardGauge,out _));view.Render();}Assert.IsNull(c.SelectedCard);StringAssert.Contains("テフダ 0",view.transform.Find("OutsideHandHeading").GetComponent<Text>().text);for(int i=0;i<3;i++)Assert.IsFalse(view.transform.Find("OutsideHandButton"+i).GetComponent<Button>().IsInteractable());Assert.DoesNotThrow(()=>old.Invoke());c.Reload();view.Render();Assert.IsTrue(view.transform.Find("OutsideHandButton2").GetComponent<Button>().IsInteractable());}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] public async Task 遠方の試用案内は概要へ縮み近接では既存詳細を保持して操作状態を変えない(int index)
        {
            var model=system.OutsideExploration;int calls=0;bool allowed=false;string message="カリのハコ\n3コ / ジョウゲン99\nA / × デ ウケトル";
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;if(index==0)view.ConfigureSupply(()=>calls++,()=>allowed,()=>message);else if(index==1)view.ConfigurePoisonSupply(()=>calls++,()=>allowed,()=>message);else if(index==2)view.ConfigureTrialPoison(()=>calls++,()=>allowed,()=>message);else view.ConfigureCardSupply(()=>calls++,()=>allowed,()=>message);view.Render();
                var button=view.GetComponentsInChildren<Button>().Single(b=>b.name==(index==0?"OutsideSupplyChest":index==1?"OutsidePoisonSupplyChest":index==2?"OutsideTrialPoisonPoint":"OutsideCardSupplyChest"));var rect=(RectTransform)button.transform;var label=button.GetComponentInChildren<Text>();Assert.AreEqual(new Vector2(320,82),rect.sizeDelta);Assert.AreEqual(2,label.text.Split('\n').Length);StringAssert.DoesNotContain("デ ウケトル",label.text);Assert.IsFalse(button.IsInteractable());
                long target=index==0?Olca.Core.Logic.Integration.OutsideTrialRoute.SupplyX:index==1?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonSupplyX:index==2?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonTrialX:Olca.Core.Logic.Integration.OutsideTrialRoute.CardSupplyX;
                void Move(long x){for(int i=0;i<200&&model.Core.Position!=x;i++)model.Core.TickFrame(System.Math.Sign(x-model.Core.Position));model.Core.TickFrame(0);Assert.AreEqual(x,model.Core.Position);}
                Move(target);allowed=true;view.Render();Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message),label.text);Assert.IsTrue(button.IsInteractable());var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(0,calls);
                allowed=false;message="カリのハコ\nウケトリズミ";Move(model.Core.Definition.Start);view.Render();Assert.AreEqual(new Vector2(320,82),rect.sizeDelta);StringAssert.Contains("ウケトリズミ",label.text);Assert.IsFalse(button.IsInteractable());Assert.AreEqual(0,calls);message="カリのハコ\n"+new string('カ',80)+"\nホゾン ケッカ";view.Render();Assert.AreEqual(2,label.text.Split('\n').Length);StringAssert.EndsWith("...",label.text);Assert.Less(label.text.Length,message.Length);Move(target);view.Render();Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message),label.text);Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);Assert.AreEqual(0,calls);
            }
        }
        [Test] public async Task 補給表示は覆いと親終了と再表示後の旧操作を拒否する()
        {
            int calls=0;Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.ConfigureCardSupply(()=>calls++,()=>true,()=>"カリのハコ");binding.View.Render();var button=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="OutsideCardSupplyChest");Assert.IsTrue(button.IsInteractable());var old=button.onClick;old.Invoke();Assert.AreEqual(1,calls);
                binding.View.SetCovered(true);old.Invoke();Assert.AreEqual(1,calls);Assert.IsTrue(binding.ReturnToHub());old.Invoke();Assert.AreEqual(1,calls);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.ConfigureCardSupply(()=>calls++,()=>true,()=>"カリのハコ");binding.View.Render();old.Invoke();Assert.AreEqual(1,calls);binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="OutsideCardSupplyChest").onClick.Invoke();Assert.AreEqual(2,calls);
            }
        }
        [Test] public async Task 外界入口は同レイヤーを切替え旧操作を拒否しView再生成でRootSessionを失わない()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-fixture-hub");var entry=Button(hub,"outside-fixture-entry");ManagedScreenContext hubContext=null;int hubCalls=0,returned=0;
            layer.Register(owner,"outside-fixture-hub",_=>Task.FromResult(hub),(go,ctx)=>{hubContext=ctx;entry.onClick=new Button.ButtonClickedEvent();entry.onClick.AddListener(()=>{if(ctx.IsCurrent)hubCalls++;});go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));
            Assert.IsTrue((await layer.ShowAsync("outside-fixture-hub")).Succeeded);var oldHub=entry.onClick;var previous=hubContext;var model=system.OutsideExploration;int before=resources.OwnedInstanceCount;
            Func<CancellationToken,Task<GameObject>> factory=ct=>{ct.ThrowIfCancellationRequested();return Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));};
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{returned++;layer.ShowAsync("outside-fixture-hub").GetAwaiter().GetResult();})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.IsPresented);Assert.IsFalse(previous.IsCurrent);Assert.IsFalse(hub.activeSelf);oldHub.Invoke();Assert.AreEqual(0,hubCalls);
                var back=binding.View.GetComponentsInChildren<Button>().Single(v=>v.name=="CloseOutsideExploration");Assert.AreSame(back.gameObject,EventSystem.current.currentSelectedGameObject);var oldBack=back.onClick;oldBack.Invoke();oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsFalse(model.IsPresented);Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsTrue(binding.IsOpen);Assert.AreSame(model,system.OutsideExploration);
                var lost=binding.View;typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lost,null);UnityEngine.Object.DestroyImmediate(lost.gameObject);Assert.IsTrue(binding.HasLostView);Assert.IsFalse(model.IsPresented);Assert.IsTrue((await binding.OpenAsync()).Succeeded);oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsTrue(binding.IsOpen);Assert.AreSame(model,system.OutsideExploration);
            }
            Assert.IsFalse(model.IsEnded);Assert.IsFalse(model.IsPresented);Assert.AreEqual(before,resources.OwnedInstanceCount);
            using(var recreated=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){Assert.IsTrue((await recreated.OpenAsync()).Succeeded);Assert.IsTrue(model.IsPresented);Assert.AreSame(model,system.OutsideExploration);}
            system.Dispose();Assert.IsTrue(model.IsEnded);Assert.IsFalse(model.IsPresented);
        }
        [Test] public async Task 外界入口は管理側で閉じたViewの外部破棄を再表示要求と見なさない()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-closed-hub");var entry=Button(hub,"outside-entry");
            layer.Register(owner,"outside-closed-hub",_=>Task.FromResult(hub),(go,ctx)=>{go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));
            var model=system.OutsideExploration;var core=model.Core;var gauge=core.CardGauge;int created=0,returned=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>{created++;return Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));};
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{returned++;layer.ShowAsync("outside-closed-hub").GetAwaiter().GetResult();})){
                for(int route=0;route<3;route++){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);var closed=binding.View;var oldBack=closed.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseOutsideExploration").onClick;
                    if(route==0)oldBack.Invoke();else if(route==1)Assert.IsTrue((await layer.ShowAsync("outside-closed-hub")).Succeeded);else {layer.CloseCurrent();Assert.IsTrue((await layer.ShowAsync("outside-closed-hub")).Succeeded);}
                    Assert.IsNull(binding.View);Assert.IsFalse(binding.IsOpen);Assert.IsFalse(model.IsPresented);
                    typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(closed,null);UnityEngine.Object.DestroyImmediate(closed.gameObject);
                    Assert.IsFalse(binding.HasLostView);oldBack.Invoke();binding.Pump(true);
                    Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(1,returned);Assert.AreEqual(route+1,created);
                    Assert.AreSame(core,model.Core);Assert.AreSame(gauge,core.CardGauge);Assert.AreEqual(0,core.Frame);
                }
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(binding.IsOpen);Assert.AreEqual(4,created);
            }
            Assert.IsFalse(model.IsPresented);
        }

        [Test] public async Task 補給四種は操作不可でも文字を明瞭にし可否と素材と旧callbackを維持する()
        {
            bool usable=false;int calls=0;var session=system.OutsideExploration;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Action claim=()=>calls++;Func<bool> allowed=()=>usable;Func<string> message=()=>"シヨウ カリ\nウケトリズミ";
                view.ConfigureSupply(claim,allowed,message);view.ConfigurePoisonSupply(claim,allowed,message);view.ConfigureTrialPoison(claim,allowed,message);view.ConfigureCardSupply(claim,allowed,message);
                var buttons=new[]{"OutsideSupplyChest","OutsidePoisonSupplyChest","OutsideTrialPoisonPoint","OutsideCardSupplyChest"}.Select(n=>view.transform.Find(n).GetComponent<Button>()).ToArray();var fonts=buttons.Select(b=>b.GetComponentInChildren<Text>().font).ToArray();var sprites=buttons.Select(b=>b.spriteState).ToArray();long frame=session.Core.Frame,gauge=session.Core.CardGauge.CurrentUnits;
                view.Render();foreach(var button in buttons){Assert.IsFalse(button.IsInteractable());Assert.AreEqual(Color.white,button.GetComponentInChildren<Text>().color);button.onClick.Invoke();}Assert.AreEqual(0,calls);
                usable=true;view.Render();for(int i=0;i<buttons.Length;i++){var button=buttons[i];Assert.IsTrue(button.IsInteractable());Assert.AreEqual(Olca.Presentation.Dungeon.UiLook.Ink,button.GetComponentInChildren<Text>().color);Assert.AreSame(fonts[i],button.GetComponentInChildren<Text>().font);Assert.AreEqual(sprites[i],button.spriteState);button.onClick.Invoke();}Assert.AreEqual(4,calls);
                var old=buttons[0].onClick;usable=false;view.Render();foreach(var button in buttons){Assert.IsFalse(button.IsInteractable());Assert.AreEqual(Color.white,button.GetComponentInChildren<Text>().color);}Assert.AreEqual(frame,session.Core.Frame);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);views.GetLayer("return-hub").CloseCurrent();old.Invoke();Assert.AreEqual(4,calls);
            }
        }

        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)]
        public async Task リロード案内は移動と高度と再表示でもHP強靭HUDを覆わず状態を変えない(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var session=system.OutsideExploration;
            Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));session.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                Rect Area(RectTransform rect){var corners=new Vector3[4];rect.GetWorldCorners(corners);return Rect.MinMaxRect(corners.Min(v=>v.x),corners.Min(v=>v.y),corners.Max(v=>v.x),corners.Max(v=>v.y));}
                foreach(int x in new[]{17,18,20,214,215,22,225,23})foreach(long y in new[]{0L,34800L,70000L}){
                    var state=session.Core.CaptureState();state.Position=(x>100?x*6000L:x*60000L);state.Height=y;state.Velocity=0;state.SupportId=y==0?"floor":null;
                    typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session.Core,new object[]{state});
                    long frame=session.Core.Frame,gauge=session.Core.CardGauge.CurrentUnits;view.Render();var point=view.transform.Find("OutsideReloadPoint").GetComponent<RectTransform>();Assert.IsTrue(point.gameObject.activeSelf);
                    foreach(string label in new[]{"OutsidePlayerHp","OutsidePlayerPoise"})Assert.IsFalse(Area(point).Overlaps(Area(view.transform.Find(label).GetComponent<RectTransform>())),label+" x="+x+" y="+y);
                    var position=point.anchoredPosition;view.Render();Assert.AreEqual(position,point.anchoredPosition,"再描画で位置が累積しない");Assert.AreEqual(frame,session.Core.Frame);Assert.AreEqual(state.Position,session.Core.Position);Assert.AreEqual(y,session.Core.Height);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(100,session.Player.CurrentHp);Assert.IsFalse(point.GetComponent<Image>().raycastTarget);
                }
                views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);view.Render();var marker=view.transform.Find("OutsideReloadPoint").GetComponent<RectTransform>();Assert.IsFalse(Area(marker).Overlaps(Area(view.transform.Find("OutsidePlayerHp").GetComponent<RectTransform>())));
            }
        }

        [TestCase(-1)][TestCase(1)] public async Task 外界壁ずりは下降二コマで壁蹴りは通常上昇姿勢を同尺度アンカーで表示し閉鎖で参照を返す(int side)
        {
            var session=system.OutsideExploration;var p=Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true);
            if(side<0)p=new Olca.Core.Logic.Integration.OutsideTraversalProfile(p.Id,p.DashSpeedPerSecond,p.DashFrames,p.DashCooldownFrames,p.DashResetFrames,p.DashJumpSpeedPerSecond,p.BodyHalfWidth,p.BodyHeight,p.WallSlidePerFrame,p.WallKickVelocity,p.WallKickSpeedPerSecond,p.WallKickPushFrames,p.WallKickMotionFrames,true,new Olca.Core.Logic.Integration.OutsideValidationWall("left-wall",4*60000,0,1000*60000));Assert.IsTrue(session.TryConfigureTraversal(p));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var c=session.Core;
                c.TickFrame(side,dashDown:true,dashHeld:true);view.Render();
                var trails=view.GetComponentsInChildren<Image>(true).Where(i=>i.name.StartsWith("OutsideDashAfterimage",StringComparison.Ordinal)).ToArray();Assert.AreEqual(4,trails.Length);Assert.IsTrue(trails.Any(i=>i.enabled&&i.sprite!=null));
                long dashFrame=c.Frame,dashX=c.Position;views.GetLayer("return-hub").CloseCurrent();foreach(var trail in trails){Assert.IsFalse(trail.enabled);Assert.IsNull(trail.sprite);Assert.IsFalse(trail.raycastTarget);}
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(dashFrame,c.Frame);Assert.AreEqual(dashX,c.Position);Assert.IsTrue(c.IsDashing);Assert.IsTrue(trails.Any(i=>i.enabled&&i.sprite!=null));
                long wall=c.TraversalProfile.Walls[0].X-side*c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position!=wall;i++)c.TickFrame(side);Assert.AreEqual(wall,c.Position);
                c.TickFrame(side,true,true);view.Render();Assert.IsFalse(c.IsWallSliding);while(c.VerticalVelocity>=0)c.TickFrame(side,false,true);c.TickFrame(side,false,true);view.Render();var slide1=resources.GetShared<Sprite>("OlcaTraversal/WallSlide_01");Assert.AreSame(slide1,image.sprite);
                Assert.AreEqual(new Vector2(128,14),image.sprite.pivot);Assert.AreEqual(100,image.sprite.pixelsPerUnit);Assert.AreEqual(new Vector2(256,256),image.sprite.rect.size);Assert.IsFalse(image.raycastTarget);
                var size=image.rectTransform.sizeDelta;var pivot=image.rectTransform.pivot;
                for(int i=0;i<6;i++)c.TickFrame(side,false,true);view.Render();Assert.AreSame(resources.GetShared<Sprite>("OlcaTraversal/WallSlide_02"),image.sprite);Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreEqual(pivot,image.rectTransform.pivot);
                c.TickFrame(side);long kickStartX=c.Position,kickStartY=c.Height;c.TickFrame(side,true,true);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(kickStartX-side*8000,c.Position);Assert.AreEqual(kickStartY+12000,c.Height);view.Render();var rise=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite;Assert.AreSame(rise,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);Assert.AreEqual(rise.pivot/rise.rect.size,image.rectTransform.pivot);size=image.rectTransform.sizeDelta;pivot=image.rectTransform.pivot;
                for(int i=0;i<6;i++)c.TickFrame(side,false,true);view.Render();Assert.AreSame(resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite,image.sprite);Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreEqual(pivot,image.rectTransform.pivot);
                long frame=c.Frame,y=c.Height;views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue(slide1!=null);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(y,c.Height);
                foreach(var trail in view.GetComponentsInChildren<Image>(true).Where(i=>i.name.StartsWith("OutsideDashAfterimage",StringComparison.Ordinal))){Assert.IsFalse(trail.enabled);Assert.IsNull(trail.sprite);Assert.IsFalse(trail.raycastTarget);}
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(y,c.Height);Assert.AreSame(resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite,image.sprite);
                for(int i=0;i<12&&c.IsWallKicking;i++)c.TickFrame(0,false,true);Assert.IsFalse(c.IsWallKicking);view.Render();var source=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>();var expected=Math.Abs(c.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?source.AuthoredJumpApexSprite:c.VerticalVelocity>0?source.AuthoredJumpRiseSprite:source.AuthoredJumpFallSprite;Assert.AreSame(expected,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);
            }
        }

        [Test] public async Task 接地壁のShift中も実IdleClipの同じ位相を表示し固定立ち絵へ切り替えない()
        {
            var session=system.OutsideExploration;Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var c=session.Core;long wall=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position<wall;i++)c.TickFrame(1);for(int i=0;i<8;i++)c.TickFrame(1);long idle=c.IdleFrame;
                var sample=new GameObject("ExpectedIdle",typeof(SpriteRenderer),typeof(Animator));try{var renderer=sample.GetComponent<SpriteRenderer>();renderer.enabled=false;var animator=sample.GetComponent<Animator>();animator.enabled=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var clip=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredIdleClip;
                    for(int i=0;i<12;i++){c.TickFrame(1,dashDown:i==0,dashHeld:true);view.Render();Assert.IsTrue(c.IsDashing);Assert.IsFalse(c.IsMoving);Assert.AreEqual(idle+i+1,c.IdleFrame);clip.SampleAnimation(sample,(float)((c.IdleFrame/60d)%clip.length));Assert.AreSame(renderer.sprite,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);}
                }finally{UnityEngine.Object.DestroyImmediate(sample);}
            }
        }

        [Test] public async Task 高い壁蹴りでも本体は固定ヘッダーより下に映り足場壁と再入位置を一致させる()
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);
            var session=system.OutsideExploration;Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var c=session.Core;long wall=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;
                for(int i=0;i<300&&c.Position<wall;i++)c.TickFrame(1);
                c.TickFrame(1,true,true);c.TickFrame(1);
                for(int i=0;i<400&&c.Height<7*60000;i++){bool kick=!c.IsGrounded&&c.WallContactDirection==1&&!c.IsWallKicking;c.TickFrame(1,kick,kick);}
                Assert.GreaterOrEqual(c.Height,7*60000);long frame=c.Frame,height=c.Height;view.Render();Assert.AreEqual(frame,c.Frame);Assert.AreEqual(height,c.Height);
                var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var rect=(RectTransform)view.transform;
                float top=rect.rect.height*.21f+image.rectTransform.anchoredPosition.y+(1-image.rectTransform.pivot.y)*image.rectTransform.sizeDelta.y;
                Assert.LessOrEqual(top,rect.rect.height-144+.05f,"本体上端を固定HUDの下へ保つ");
                Assert.IsFalse(view.transform.Find("OutsideEntryGround").GetComponent<Image>().enabled,"地表が画面下へ出た後に床を反転表示しない");
                var wallImage=view.transform.Find("OutsideValidationWall-validation-wall").GetComponent<Image>();Assert.IsTrue(wallImage.enabled);Assert.Greater(wallImage.rectTransform.rect.height,0);Assert.LessOrEqual(wallImage.rectTransform.rect.height,rect.rect.height+wallImage.sprite.rect.height/(wallImage.pixelsPerUnit*wallImage.pixelsPerUnitMultiplier)+.1f);
                var before=image.rectTransform.anchoredPosition;views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(before,image.rectTransform.anchoredPosition);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(height,c.Height);
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] public async Task 外界移動案内は未登録手札でも現在割当へ追従し未接続Touchを案内しない(int device)
        {
            var flags=BindingFlags.Static|BindingFlags.NonPublic;var activity=typeof(InputGuideActivity).GetField("<Current>k__BackingField",flags);var priorDevice=activity.GetValue(null);var active=typeof(UnityInputProvider).GetField("<ActiveGamepadProfile>k__BackingField",flags);var version=typeof(UnityInputProvider).GetField("<GamepadProfileVersion>k__BackingField",flags);var priorActive=active.GetValue(null);var priorVersion=version.GetValue(null);
            using(var input=new UnityInputProvider())try{
                activity.SetValue(null,(InputGuideDevice)device);var model=system.OutsideExploration;Assert.IsNull(model.Cards);
                Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
                using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},input:input)){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var guide=view.transform.Find("OutsideMovementGuide").GetComponent<Text>();Assert.IsFalse(guide.raycastTarget);Assert.IsFalse(view.transform.Find("OutsideHandButton0").gameObject.activeSelf);
                    if(device==0){StringAssert.Contains("LeftArrow/A",guide.text);StringAssert.Contains("RightArrow/D",guide.text);StringAssert.Contains("Space",guide.text);}else if(device==1){StringAssert.Contains("スティック",guide.text);StringAssert.DoesNotContain("ジュウジ",guide.text);StringAssert.Contains("ダッシュ: RB",guide.text);StringAssert.Contains("A / ×",guide.text);StringAssert.DoesNotContain("South",guide.text);}else Assert.IsEmpty(guide.text);
                    var edit=(device==1?input.GamepadProfile:input.KeyboardProfile).BeginEdit();Assert.IsTrue(edit.TryAssign((device==1?"gamepad":"keyboard","battle","LeftPrimary"),device==1?"LeftShoulder":"F12",out _));Assert.IsTrue(edit.TryAssign((device==1?"gamepad":"keyboard",device==1?"outside":"exploration","JumpPrimary"),device==1?"RightStick":"J",out _));
                    if(device==1)Assert.IsTrue(edit.TryAssign(("gamepad","outside","DashPrimary"),"LeftShoulder",out _));
                    if(device==1)Assert.IsTrue(input.TryRestoreGamepadProfile(edit.Preview(),out _));else Assert.IsTrue(input.TryRestoreKeyboardProfile(edit.Preview(),out _));view.Render();
                    if(device==0){StringAssert.Contains("F12/A",guide.text);StringAssert.Contains("ジャンプ: J",guide.text);StringAssert.DoesNotContain("LeftArrow",guide.text);}else if(device==1){StringAssert.Contains("LB",guide.text);StringAssert.Contains("R3",guide.text);StringAssert.DoesNotContain("South",guide.text);StringAssert.DoesNotContain("A / ×",guide.text);}else Assert.IsEmpty(guide.text);
                    views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(0,model.Core.Frame);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);Assert.IsNull(model.Cards);Assert.AreEqual(new Vector2(204,66),view.transform.Find("OutsideHandButton0").GetComponent<RectTransform>().sizeDelta);
                }
            }finally{activity.SetValue(null,priorDevice);active.SetValue(null,priorActive);version.SetValue(null,priorVersion);}
        }

        [Test] public async Task 外界の直接選択は閉鎖再生成で保持し旧操作と覆い中を拒否する()
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-select-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=model.Cards;var hand=cards.Hand.ToArray();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Button Pick(int slot)=>view.transform.Find("OutsideHandButton"+slot).GetComponent<Button>();
                Assert.AreEqual(0,cards.SelectedIndex);Assert.IsTrue(view.transform.Find("OutsideHandButton0/OutsideHandSelected0").gameObject.activeSelf);
                var old=Pick(0).onClick;Pick(2).onClick.Invoke();Assert.AreEqual(2,cards.SelectedIndex);Assert.IsTrue(view.transform.Find("OutsideHandButton2/OutsideHandSelected2").gameObject.activeSelf);
                view.SetCovered(true);Pick(0).onClick.Invoke();Assert.AreEqual(2,cards.SelectedIndex);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(model.IsPresented);UnityEngine.Object.DestroyImmediate(view.gameObject);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);view=binding.View;Assert.AreEqual(2,cards.SelectedIndex);old.Invoke();Assert.AreEqual(2,cards.SelectedIndex);Pick(0).onClick.Invoke();Assert.AreEqual(0,cards.SelectedIndex);
                CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(0,model.Core.Frame);
                // 名前を上段へ広げた配置でも、再生成後のアイコンをコスト横へ収める。
                Assert.AreEqual(new Vector2(32,32),view.transform.Find("OutsideHandIcon0").GetComponent<RectTransform>().sizeDelta);
                Assert.AreEqual(new Vector2(204,66),Pick(0).GetComponent<RectTransform>().sizeDelta);
            }
        }
        [TestCase("攻撃", "コウゲキ")] [TestCase("ひらがな Latin", "ひらがな Latin")] public async Task 外界初期手札HUDはRootの三枚を表示し閉鎖と再表示で再抽選しない(string name,string shown)
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-hud-"+id,name,1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=model.Cards;var hand=cards.Hand.ToArray();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                for(int slot=0;slot<3;slot++){Assert.AreEqual(shown,view.transform.Find("OutsideHandName"+slot).GetComponent<Text>().text);Assert.AreEqual(name,hand[slot].Name);Assert.AreEqual(name,source[slot].Name);Assert.AreEqual("コスト "+hand[slot].GetFinalCost(),view.transform.Find("OutsideHandCost"+slot).GetComponent<Text>().text);Assert.IsFalse(view.transform.Find("OutsideHandName"+slot).GetComponent<Text>().raycastTarget);}
                StringAssert.Contains("17",view.transform.Find("OutsideHandHeading").GetComponent<Text>().text);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(model.IsPresented);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreSame(cards,model.Cards);CollectionAssert.AreEqual(hand,model.Cards.Hand);Assert.AreEqual(0,model.Core.Frame);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);
            }
        }

        [Test] public async Task 外界入口の取消と欠損失敗は前画面のfocusとSessionを変更せず再試行できる()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-failure-hub");var entry=Button(hub,"outside-failure-entry");ManagedScreenContext prior=null;
            layer.Register(owner,"outside-failure-hub",_=>Task.FromResult(hub),(go,ctx)=>{prior=ctx;go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));Assert.IsTrue((await layer.ShowAsync("outside-failure-hub")).Succeeded);int before=resources.OwnedInstanceCount;
            var source=new TaskCompletionSource<GameObject>();using(var cancel=new CancellationTokenSource())using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>source.Task,()=>true,null)){
                var pending=binding.OpenAsync(cancel.Token);cancel.Cancel();source.SetResult(Panel("outside-cancelled"));Assert.IsTrue((await pending).Cancelled);
            }
            using(var missing=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>throw new InvalidOperationException("outside-fixture-missing"),()=>true,null)){var failed=await missing.OpenAsync();Assert.IsFalse(failed.Succeeded);StringAssert.Contains("outside-fixture-missing",failed.Error);}
            using(var incomplete=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>Task.FromResult(Panel("outside-no-view")),()=>true,null)){Assert.IsFalse((await incomplete.OpenAsync()).Succeeded);}
            Assert.IsTrue(prior.IsCurrent);Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.IsFalse(system.OutsideExploration.IsPresented);Assert.IsFalse(system.OutsideExploration.IsEnded);Assert.AreEqual(before,resources.OwnedInstanceCount);
        }

        [TestCase(null)] [TestCase("")] [TestCase("not-a-generation")]
        public void 外界新規進行は保存済みGUIDでない要求を拒否し現状態を保持する(string generation)
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;
            model.Core.TickFrame(1,false,false);long position=model.Core.Position;
            Assert.Throws<ArgumentException>(()=>system.ResetOutsideForNewProgress(generation));
            Assert.AreSame(model,system.OutsideExploration);Assert.AreSame(runtime,system.OutsideRuntime);
            Assert.IsFalse(model.IsEnded);Assert.AreEqual(position,model.Core.Position);Assert.IsNull(system.OutsideProgressGeneration);
        }
        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)] public async Task 接触二体と任意AIの案内は最終描画でも全字が板内に収まる(int width,int height)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreRecoveryPosition",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{18L*60000});
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                foreach(int mode in new[]{0,1,2,3}){
                    if(mode==1)Assert.IsTrue(model.SetTrialEnemyAttack(true));if(mode==2)model.TrialTarget.TakeDamage(120,Olca.Core.Domain.ElementType.None);if(mode==3)model.SecondTrialTarget.TakeDamage(120,Olca.Core.Domain.ElementType.None);view.Render();Canvas.ForceUpdateCanvases();var text=view.transform.Find("OutsideTrialTargetName").GetComponent<Text>();var plate=(RectTransform)view.transform.Find("OutsideTrialTargetNamePlate");if(mode==3)Assert.AreEqual("カイフクデ サイセイ",text.text);else {StringAssert.Contains((mode==2?"1タイ":"2タイ")+" / セッショク10（カリ）",text.text);StringAssert.Contains(mode==1?"AIコウゲキON":"AIコウゲキOFF",text.text);}
                    var generator=new TextGenerator();Assert.IsTrue(generator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size)));Assert.AreEqual(text.text.Length,generator.characterCountVisible);Assert.AreEqual(mode==3?1:2,generator.lineCount);
                    // UICharInfoのbaseline差と実fontのglyph寸法で行間を測る。改行にはquadがないためi*4で行分割しない。
                    if(mode!=3){float[] low={float.PositiveInfinity,float.PositiveInfinity},high={float.NegativeInfinity,float.NegativeInfinity};for(int row=0;row<2;row++){int start=generator.lines[row].startCharIdx,end=row==0?generator.lines[1].startCharIdx:text.text.Length;for(int i=start;i<end;i++){if(char.IsWhiteSpace(text.text[i]))continue;Assert.IsTrue(text.font.GetCharacterInfo(text.text[i],out var glyph,text.fontSize,text.fontStyle),"glyph: "+text.text[i]);float baseline=generator.characters[i].cursorPos.y/text.pixelsPerUnit;low[row]=Mathf.Min(low[row],baseline+glyph.minY);high[row]=Mathf.Max(high[row],baseline+glyph.maxY);}}Assert.GreaterOrEqual(low[0]-high[1],6f,"640画面で2px以上の行間を確保する");}
                    foreach(var vertex in generator.verts){float y=vertex.position.y/text.pixelsPerUnit;Assert.That(y,Is.InRange(plate.rect.yMin+2,plate.rect.yMax-2));}
                    ((System.IDisposable)generator).Dispose();Assert.That(text.rectTransform.rect.width,Is.LessThan(plate.rect.width));Assert.That(text.rectTransform.rect.height,Is.LessThan(plate.rect.height));Assert.AreEqual(100,model.Player.CurrentHp);Assert.AreEqual(0,model.Core.Frame);
                }
            }
        }
        [TestCase(1920,1080,false,false)]
        [TestCase(1920,1080,true,false)]
        [TestCase(1920,1080,false,true)]
        [TestCase(1920,1080,true,true)]
        [TestCase(3840,2160,false,false)]
        [TestCase(3840,2160,true,false)]
        [TestCase(3840,2160,false,true)]
        [TestCase(3840,2160,true,true)]
        [TestCase(1440,1080,false,false)]
        [TestCase(1440,1080,true,false)]
        [TestCase(1440,1080,false,true)]
        [TestCase(1440,1080,true,true)]
        public async Task 別マップの撃破案内は残敵とマップ出入りを表示し回復を案内しない(int width,int height,bool firstDead,bool secondDead)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(width,height);var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var core=model.Core.CaptureState();core.TrialMapId=Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId;
            typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",flags).Invoke(model.Core,new object[]{core});
            if(firstDead)model.TrialTarget.TakeDamage(120,Olca.Core.Domain.ElementType.None);if(secondDead)model.SecondTrialTarget.TakeDamage(120,Olca.Core.Domain.ElementType.None);
            int firstHp=model.TrialTarget.CurrentHp,secondHp=model.SecondTrialTarget.CurrentHp;long frame=model.Core.Frame;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;view.Render();Canvas.ForceUpdateCanvases();var text=view.transform.Find("OutsideTrialTargetName").GetComponent<Text>();
                StringAssert.DoesNotContain("カイフク",text.text);
                if(firstDead&&secondDead)Assert.AreEqual("マップノ デイリデ サイセイ",text.text);
                else {StringAssert.Contains(firstDead||secondDead?"1タイ / セッショク10（カリ）":"シヨウテキ / セッショク10（カリ）",text.text);StringAssert.Contains("AIコウゲキOFF",text.text);}
                using(var generator=new TextGenerator()){Assert.IsTrue(generator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size)));Assert.AreEqual(text.text.Length,generator.characterCountVisible,"案内の末尾を欠けさせない");}
                Assert.AreEqual(firstHp,model.TrialTarget.CurrentHp);Assert.AreEqual(secondHp,model.SecondTrialTarget.CurrentHp);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }
        [Test] public void 通常Rootの初回と新規進行だけ試用敵の接触を明示登録する()
        {
            using(var lease=system.OutsideExploration.AttachPresentation())Assert.IsTrue(lease.TrialContactDamageEnabled);
            Assert.IsTrue(system.ResetOutsideForNewProgress(Guid.NewGuid().ToString("N")));using(var lease=system.OutsideExploration.AttachPresentation())Assert.IsTrue(lease.TrialContactDamageEnabled);
            using(var fixture=new Olca.Core.Logic.Integration.OutsideExplorationSession())using(var lease=fixture.AttachPresentation())Assert.IsFalse(lease.TrialContactDamageEnabled);
        }
        [Test] public void 外界新規進行は旧表示時計手札を終了し同世代の重複通知では再初期化しない()
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;
            var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            var owned=cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            Assert.IsTrue(model.TryInitializeCards(cards,owned,new Olca.Core.Logic.Battle.SeededBattleRandom(7),out var error),error);
            Assert.IsTrue(model.TryConfigureCamera(Olca.Core.Logic.Integration.OutsideCameraPolicy.FixedAt(60000)));
            using(var input=new UnityInputProvider())using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);model.Core.TickFrame(1,false,false);Assert.IsTrue(model.Core.CardGauge.TrySpend(20));
                string generation=Guid.NewGuid().ToString("N");Assert.IsTrue(system.ResetOutsideForNewProgress(generation));
                Assert.IsTrue(model.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsNull(lease.Core);
                Assert.Throws<ObjectDisposedException>(()=>runtime.Attach(driver,input));runtime.Pump(driver,true);
                Assert.AreNotSame(model,system.OutsideExploration);Assert.AreNotSame(runtime,system.OutsideRuntime);
                using(var defaults=new Olca.Core.Logic.Integration.OutsideExplorationSession()){
                    var fresh=system.OutsideExploration;Assert.AreEqual(defaults.Core.Position,fresh.Core.Position);
                    Assert.AreEqual(0,fresh.Core.Frame);Assert.AreEqual(0,system.OutsideRuntime.Frame);
                    Assert.AreEqual(defaults.Core.CardGauge.CurrentUnits,fresh.Core.CardGauge.CurrentUnits);
                    Assert.IsNull(fresh.Cards);Assert.IsNull(fresh.Camera.FixedCenter);Assert.IsFalse(fresh.IsPresented);
                    fresh.Core.TickFrame(-1,false,false);Assert.IsFalse(system.ResetOutsideForNewProgress(generation));
                    Assert.AreSame(fresh,system.OutsideExploration);Assert.AreEqual(1,fresh.Core.Frame);Assert.AreEqual(generation,system.OutsideProgressGeneration);
                }
            }
            system.Dispose();Assert.Throws<ObjectDisposedException>(()=>system.ResetOutsideForNewProgress(Guid.NewGuid().ToString("N")));
        }
        [Test] public void 通常host終了再開は新規進行と見なさず外界状態を保持する()
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;model.Core.TickFrame(1,false,false);
            string path=Path.Combine(Path.GetTempPath(),"olca-outside-normal-"+Guid.NewGuid().ToString("N"));
            try{
                using(system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress")){}
                using(system.BeginSession(new object(),path+"/settings",path+"/checkpoints",path+"/progress")){}
                Assert.AreSame(model,system.OutsideExploration);Assert.AreSame(runtime,system.OutsideRuntime);
                Assert.AreEqual(1,model.Core.Frame);Assert.IsFalse(model.IsEnded);Assert.IsNull(system.OutsideProgressGeneration);
            }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        }

        [Test] public void 外界の追いつき中に死亡したら同じPumpの残frameで移動を続けない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(1));bool paused=Olca.Unity.GameplayPause.IsPaused;var pt=typeof(Olca.Unity.GameplayPause);var release=pt.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pt.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var oldRelease=release.GetValue(null);var oldResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);release.SetValue(null,false);resume.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);long freq=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+freq*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);loop.AfterFrames(1,()=>model.Player.TakeDamage(1,affectsPoise:false));now+=freq/10;runtime.Pump(driver,true);Assert.IsTrue(model.Player.IsDead);Assert.AreEqual(1,model.Core.Frame);Assert.AreEqual(1,runtime.Frame);long x=model.Core.Position;now+=freq;runtime.Pump(driver,true);Assert.AreEqual(1,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,oldRelease);resume.SetValue(null,oldResume);model.Dispose();}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] public void 外界ヒールの固定更新は選択優先と再クリック世代と重複要求を守る(int mode)
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(50);var source=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(158),out _));for(int i=0;i<100&&!model.Cards.Hand.Any(c=>c.Id=="heal");i++)model.Cards.Reload();int heal=model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal");Assert.GreaterOrEqual(heal,0);model.Cards.TrySelect(heal);bool paused=Olca.Unity.GameplayPause.IsPaused;var type=typeof(Olca.Unity.GameplayPause);var flags=BindingFlags.Static|BindingFlags.NonPublic;var rel=type.GetField("awaitRelease",flags);var res=type.GetField("resumeFrames",flags);var oldRel=rel.GetValue(null);var oldRes=res.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);rel.SetValue(null,false);res.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model)){var lease=model.AttachPresentation();try{var driver=new object();runtime.Attach(driver,input);var ff=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",ff).GetValue(runtime);long f=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+f*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",ff).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",ff).GetValue(input);void Step(Olca.Core.Logic.Integration.LogicalInput value){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,value));now+=f/60+1;runtime.Pump(driver,true);}Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});long rev=model.Cards.HandRevision;
                var press=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,CardUseButtonDown=true};if(mode==1){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true,AnyKey=true,SelectionPreviousHeld=true,SelectionPreviousDown=true}));events.Add(new Olca.Unity.TimestampedLogicalInput(now+2,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true,AnyKey=true,SelectionNextHeld=true,SelectionNextDown=true}));}if(mode>=2){press.OutsideHandOwner=model.Cards;press.OutsideHandRevision=rev;press.OutsideHandPress=heal;press.OutsideHandLease=lease;if(mode==2){press.CardUseButtonDown=false;lease.Dispose();lease=model.AttachPresentation();}if(mode==3){press.CardUseButtonDown=false;press.OutsideHandPress=(heal+1)%3;}}if(mode>=5){press.CardUseButtonDown=false;press.BasicAttackButtonDown=true;press.BasicAttackPressedAt=mode==5?20:mode==7?0:10;press.OutsideHandPressedAt=mode==5?10:mode==7?0:20;if(mode==8)press.OutsideHandPress=(heal+1)%3;if(mode==9)press.OutsideHandSelectionChanged=true;}if(mode==1){events.Add(new Olca.Unity.TimestampedLogicalInput(now+3,press));now+=f/60+1;runtime.Pump(driver,true);}else Step(press);
                bool used=mode==0||mode==4||mode==6||mode==7;Assert.AreEqual(used?70:50,model.Player.CurrentHp);Assert.AreEqual(rev+(used?1:0),model.Cards.HandRevision);if(used){for(int i=0;i<10;i++)Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true});Assert.AreEqual(rev+1,model.Cards.HandRevision);}
            }finally{lease.Dispose();}}}finally{Olca.Unity.GameplayPause.SetPaused(paused);rel.SetValue(null,oldRel);res.SetValue(null,oldRes);model.Dispose();}
        }
        [Test] public void 外界の前後隙は移動ジャンプダッシュを止めPauseとView再接続で論理時間だけ保持する()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(50);var source=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new Dictionary<string,Olca.Core.Domain.PlayerAttackTiming>{{"heal",new Olca.Core.Domain.PlayerAttackTiming(3,4)}}).CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(158),out _));for(int i=0;i<100&&!model.Cards.Hand.Any(c=>c.Id=="heal");i++)model.Cards.Reload();int heal=model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal");Assert.GreaterOrEqual(heal,0);model.Cards.TrySelect(heal);bool paused=Olca.Unity.GameplayPause.IsPaused;var type=typeof(Olca.Unity.GameplayPause);var flags=BindingFlags.Static|BindingFlags.NonPublic;var rel=type.GetField("awaitRelease",flags);var res=type.GetField("resumeFrames",flags);var oldRel=rel.GetValue(null);var oldRes=res.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);rel.SetValue(null,false);res.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model)){var lease=model.AttachPresentation();try{var driver=new object();runtime.Attach(driver,input);var ff=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",ff).GetValue(runtime);long f=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+f*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",ff).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",ff).GetValue(input);void Step(Olca.Core.Logic.Integration.LogicalInput value){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,value));now+=f/60+1;runtime.Pump(driver,true);}Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});long rev=model.Cards.HandRevision;
                Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,CardUseButtonDown=true});Assert.AreEqual(50,model.Player.CurrentHp);Assert.IsTrue(model.IsAttacking);long x=model.Core.Position,frame=model.Core.Frame;runtime.Pump(driver,false);now+=f;runtime.Pump(driver,false);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(50,model.Player.CurrentHp);Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});for(int i=0;i<5;i++){Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,Horizontal=1,JumpButton=true,JumpButtonDown=true,DashButton=true,DashButtonDown=true});Assert.AreEqual(x,model.Core.Position);Assert.IsTrue(model.Core.IsGrounded);}for(int i=0;i<10&&model.Core.Frame<frame+7;i++)Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});Assert.AreEqual(frame+7,model.Core.Frame);Assert.AreEqual(70,model.Player.CurrentHp);Assert.IsFalse(model.IsAttacking);Assert.AreEqual(rev+1,model.Cards.HandRevision);
            }finally{lease.Dispose();}}}finally{Olca.Unity.GameplayPause.SetPaused(paused);rel.SetValue(null,oldRel);res.SetValue(null,oldRes);model.Dispose();}
        }
        [TestCase(0,0f,1f)] [TestCase(5,-70f,1f)] [TestCase(11,-70f,1f/6)] public void 探索ソードの暫定振り抜きは論理frameと向きを対称に標本化する(int age,float expected,float opacity){Assert.IsTrue(Olca.Presentation.Exploration.OutsideSwordTrailGraphic.TrySample(age,1,out var angle,out var alpha));Assert.AreEqual(expected,angle);Assert.AreEqual(opacity,alpha,.00001f);Assert.IsTrue(Olca.Presentation.Exploration.OutsideSwordTrailGraphic.TrySample(age,-1,out var left,out var leftAlpha));Assert.AreEqual(-angle,left);Assert.AreEqual(alpha,leftAlpha);}
        [TestCase(-1)] [TestCase(12)] [TestCase(long.MaxValue)] public void 探索ソードの暫定振り抜きは有効期間外を表示しない(long age){Assert.IsFalse(Olca.Presentation.Exploration.OutsideSwordTrailGraphic.TrySample(age,1,out var angle,out var alpha));Assert.AreEqual(0,angle);Assert.AreEqual(0,alpha);}
        [Test] public void 探索ソード軌跡は有限頂点を持ち無効入力を拒否する(){var go=new UnityEngine.GameObject("SwordTrailTest",typeof(UnityEngine.RectTransform));try{var trail=go.AddComponent<Olca.Presentation.Exploration.OutsideSwordTrailGraphic>();Assert.Throws<ArgumentOutOfRangeException>(()=>trail.SetPose(0,1,float.NaN));trail.SetPose(-70,1,124);using(var mesh=new UnityEngine.UI.VertexHelper()){typeof(Olca.Presentation.Exploration.OutsideSwordTrailGraphic).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(UnityEngine.UI.VertexHelper)},null).Invoke(trail,new object[]{mesh});Assert.AreEqual(48,mesh.currentVertCount);for(int n=0;n<mesh.currentVertCount;n++){var v=new UnityEngine.UIVertex();mesh.PopulateUIVertex(ref v,n);Assert.IsFalse(float.IsNaN(v.position.x)||float.IsInfinity(v.position.x));Assert.LessOrEqual(v.position.magnitude,125);}}}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [TestCase(0f)] [TestCase(.5f)] [TestCase(1f)] public void 試用標的の円HPは有限の既存比率範囲へ描画する(float fraction)
        {
            var go=new UnityEngine.GameObject("TrialGaugeTest",typeof(UnityEngine.RectTransform));try{var arc=go.AddComponent<Olca.Presentation.Exploration.OutsideTrialHpArc>();arc.SetFraction(fraction);using(var mesh=new UnityEngine.UI.VertexHelper()){typeof(Olca.Presentation.Exploration.OutsideTrialHpArc).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(UnityEngine.UI.VertexHelper)},null).Invoke(arc,new object[]{mesh});Assert.Greater(mesh.currentVertCount,0);for(int n=0;n<mesh.currentVertCount;n++){var v=new UnityEngine.UIVertex();mesh.PopulateUIVertex(ref v,n);Assert.IsFalse(float.IsNaN(v.position.x)||float.IsInfinity(v.position.x));Assert.IsFalse(float.IsNaN(v.position.y)||float.IsInfinity(v.position.y));Assert.Less(v.position.magnitude,50);}}}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] public void 試用標的の円HPは不正な比率を拒否する(float fraction){var go=new UnityEngine.GameObject("BadTrialGauge",typeof(UnityEngine.RectTransform));try{Assert.Throws<ArgumentOutOfRangeException>(()=>go.AddComponent<Olca.Presentation.Exploration.OutsideTrialHpArc>().SetFraction(fraction));}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [Test] public void 外界の敵数値は旧敵を解除して再Bindと非表示後に購読を残さない()
        {
            var go=new UnityEngine.GameObject("EnemyFeedbackTest",typeof(UnityEngine.RectTransform));try{var canvas=go.AddComponent<Olca.Presentation.Exploration.OutsideCombatFeedbackCanvas>();var font=UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");var style=new Olca.Presentation.Effects.CombatNumberStyle(font,UnityEngine.FontStyle.Normal,UnityEngine.Color.white,.2f,.01f,1,1);var one=new Olca.Core.Domain.Enemy("grunt","one",120,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(0,0));var two=new Olca.Core.Domain.Enemy("grunt","two",120,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(0,0));canvas.BindEnemy(one,style,()=>true,()=>UnityEngine.Vector2.zero,100);one.TakeDamage(3,Olca.Core.Domain.ElementType.None);Assert.AreEqual(1,canvas.ActiveCount);canvas.Unbind();one.TakeDamage(3,Olca.Core.Domain.ElementType.None);Assert.AreEqual(0,canvas.ActiveCount);canvas.BindEnemy(two,style,()=>true,()=>UnityEngine.Vector2.zero,100);one.TakeDamage(3,Olca.Core.Domain.ElementType.None);Assert.AreEqual(0,canvas.ActiveCount);two.TakeDamage(3,Olca.Core.Domain.ElementType.None);Assert.AreEqual(1,canvas.ActiveCount);canvas.Unbind();go.SetActive(false);go.SetActive(true);two.TakeDamage(3,Olca.Core.Domain.ElementType.None);Assert.AreEqual(0,canvas.ActiveCount);}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test] public void 外界RuntimeのPauseはカード予約だけを取り消し開始済行動を再開する()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(50);var source=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new Dictionary<string,Olca.Core.Domain.PlayerAttackTiming>{{"heal",new Olca.Core.Domain.PlayerAttackTiming(3,8)}}).CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(158),out _));for(int i=0;i<100&&model.Cards.Hand.Count(c=>c.Id=="heal")<2;i++)model.Cards.Reload();int heal=model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal");Assert.GreaterOrEqual(heal,0);model.Cards.TrySelect(heal);bool paused=Olca.Unity.GameplayPause.IsPaused;var type=typeof(Olca.Unity.GameplayPause);var flags=BindingFlags.Static|BindingFlags.NonPublic;var rel=type.GetField("awaitRelease",flags);var res=type.GetField("resumeFrames",flags);var oldRel=rel.GetValue(null);var oldRes=res.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);rel.SetValue(null,false);res.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model)){var lease=model.AttachPresentation();try{var driver=new object();runtime.Attach(driver,input);var ff=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",ff).GetValue(runtime);long f=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+f*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",ff).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",ff).GetValue(input);void Step(Olca.Core.Logic.Integration.LogicalInput value){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,value));now+=f/60+1;runtime.Pump(driver,true);}Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});long rev=model.Cards.HandRevision;
                Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,CardUseButtonDown=true});model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,CardUseButtonDown=true});Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.Card,model.PendingAttack);long frame=model.Core.Frame;runtime.Pump(driver,false);now+=f;runtime.Pump(driver,false);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(Olca.Core.Logic.Battle.AttackReservationKind.None,model.PendingAttack);for(int i=0;i<25;i++)Step(new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true});Assert.AreEqual(70,model.Player.CurrentHp);Assert.AreEqual(rev+1,model.Cards.HandRevision);Assert.IsFalse(model.IsAttacking);
            }finally{lease.Dispose();}}}finally{Olca.Unity.GameplayPause.SetPaused(paused);rel.SetValue(null,oldRel);res.SetValue(null,oldRes);model.Dispose();}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void 外界の明示Pause復帰だけが保持横移動を許し旧actionと再入を遮断する(int mode)
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(50);var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(model.TryInitializeCards(cards,cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(197),out _));for(int i=0;i<100&&!model.Cards.Hand.Any(c=>c.Id=="heal");i++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));
            var pt=typeof(Olca.Unity.GameplayPause);var flags=BindingFlags.Static|BindingFlags.NonPublic;var rel=pt.GetField("awaitRelease",flags);var res=pt.GetField("resumeFrames",flags);bool oldPause=Olca.Unity.GameplayPause.IsPaused;var oldRel=rel.GetValue(null);var oldRes=res.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);rel.SetValue(null,false);res.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                object driver=new object();runtime.Attach(driver,input);var ff=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",ff).GetValue(runtime);long freq=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+freq*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",ff).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",ff).GetValue(input);
                void Step(bool held){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasOutsideHorizontalSample=true,OutsideHorizontal=held?1:0,Horizontal=0,AnyKey=held&&mode!=5&&mode!=8,JumpButtonDown=held&&mode!=5&&mode!=8,JumpButton=held&&mode!=5&&mode!=8,DashButtonDown=held&&mode!=5&&mode!=8,DashButton=held&&mode!=5&&mode!=8,CardUseButtonDown=held&&mode!=5&&mode!=8,OutsideItemDownMask=held&&mode!=5&&mode!=8?1:0}));now+=freq/60+1;runtime.Pump(driver,true);}
                Assert.IsFalse(runtime.ResumeDirectionAfterExplicitPause(driver),"初回/非被覆では許可しない");
                if(mode!=0){runtime.SetCovered(driver,true);runtime.Pump(driver,false);Assert.IsFalse(runtime.ResumeDirectionAfterExplicitPause(new object()));Assert.IsTrue(runtime.ResumeDirectionAfterExplicitPause(driver));if(mode==2)runtime.ResetInputForFocus(driver);if(mode==3){runtime.Detach(driver);driver=new object();runtime.Attach(driver,input);}if(mode==4){runtime.SetCovered(driver,false);runtime.SetCovered(driver,true);}runtime.SetCovered(driver,false);}
                long x=model.Core.Position,rev=model.Cards.HandRevision;Step(true);Assert.AreEqual(x,model.Core.Position,"再開同frameでは背面を動かさない");Step(true);Assert.AreEqual(x,model.Core.Position,"2論理frame保護");for(int i=0;i<15;i++)Step(true);
                if(mode==1||mode==5||mode==6||mode==7||mode==8)Assert.Greater(model.Core.Position,x,"明示Pause後の保持横方向だけ復帰");else Assert.AreEqual(x,model.Core.Position,"初回/取消/focus相当/host再入の保持で自走しない");Assert.IsTrue(model.Core.IsGrounded);Assert.IsFalse(model.Core.IsDashing);Assert.IsFalse(model.IsAttacking);Assert.AreEqual(50,model.Player.CurrentHp);Assert.AreEqual(rev,model.Cards.HandRevision);
                if(mode==6){long moved=model.Core.Position;runtime.ResetInputForFocus(driver);for(int n=0;n<6;n++)Step(true);Assert.AreEqual(moved,model.Core.Position,"有効化後のfocusでも保持方向の例外を解除する");}
                if(mode==7||mode==8){long moved=model.Core.Position;runtime.ResetInputForFocus(driver,false);for(int n=0;n<6;n++)Step(false);Assert.IsFalse(runtime.ResumeDirectionAfterExplicitPause(driver));runtime.ResetInputForFocus(driver,true);for(int n=0;n<6;n++)Step(true);Assert.AreEqual(moved,model.Core.Position,"背景の機器resetを実中立として再武装しない");}
                Step(false);Step(false);Step(false);events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,CardUseButtonDown=true}));now+=freq/60+1;runtime.Pump(driver,true);Assert.AreEqual(rev+1,model.Cards.HandRevision,"全中立後の新規actionは通常どおり");
            }}finally{Olca.Unity.GameplayPause.SetPaused(oldPause);rel.SetValue(null,oldRel);res.SetValue(null,oldRes);model.Dispose();}
        }
        [Test] public void 外界Runtimeは単一driverだけを許し解放後も同じRoot状態と時計を再利用する()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();
            using(var runtime=new OutsideExplorationRuntime(model))using(var input=new Olca.Unity.UnityInputProvider()){
                var first=new object();var second=new object();var core=model.Core;
                runtime.Attach(first,input);Assert.Throws<InvalidOperationException>(()=>runtime.Attach(second,input));
                runtime.Pump(second,true);Assert.AreEqual(0,runtime.Frame);Assert.AreEqual(0,core.Frame);
                runtime.Detach(second);Assert.Throws<InvalidOperationException>(()=>runtime.Attach(second,input));
                runtime.Detach(first);runtime.Attach(second,input);runtime.Pump(second,false);
                Assert.AreSame(core,model.Core);Assert.AreEqual(0,runtime.Frame);Assert.AreEqual(0,core.Frame);
                runtime.Dispose();Assert.Throws<ObjectDisposedException>(()=>runtime.Attach(first,input));
            }
        }

        [TestCase(0.33333334f)] [TestCase(1f)] [TestCase(2f)] public async Task 呼吸ラベルは実フォントと縮小倍率でも全glyphを描画する(float scale)
        {
            var canvas=host.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.scaleFactor=scale;var model=system.OutsideExploration;model.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWater);var state=model.Core.CaptureState();state.Position=60000;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.Load<Font>("OlcaUI/MenuText"),resources));using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.Render();Canvas.ForceUpdateCanvases();var text=binding.View.GetComponentsInChildren<Text>().Single(x=>x.name=="OutsideBreathLabel");using(var generated=new TextGenerator()){generated.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size));Assert.AreEqual(text.text.Length,generated.characterCountVisible);Assert.Greater(generated.vertexCount,4);}}
        }
        [Test] public async Task 水面線は読取パネルの下で遮り文字と水位と操作位置を変えない()
        {
            var model=system.OutsideExploration;model.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWater);model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.ConfigureTrialPoison(()=>{},()=>false,()=>"シヨウ ドク（カリ）\n30ビョウ / 10ビョウごとHP1");binding.View.Render();var button=binding.View.GetComponentsInChildren<Button>(true).Single(x=>x.name=="OutsideTrialPoisonPoint");Assert.IsTrue(button.gameObject.activeSelf);var area=binding.View.GetComponentsInChildren<Image>().Single(x=>x.name.StartsWith("OutsideTrialWater-"));var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);float left=corners.Min(x=>area.rectTransform.InverseTransformPoint(x).x),right=corners.Max(x=>area.rectTransform.InverseTransformPoint(x).x);var lines=area.GetComponentsInChildren<Image>().Where(x=>x.name.StartsWith("WaterSurface")).ToArray();foreach(var line in lines){float x=line.rectTransform.anchoredPosition.x;Assert.IsTrue(x+line.rectTransform.rect.width<=left||x>=right);Assert.AreEqual(0,line.rectTransform.anchoredPosition.y);Assert.GreaterOrEqual(line.rectTransform.rect.width,0);}Assert.AreEqual(3*60000,model.Core.TraversalProfile.Waters[0].Top);var buttonRect=(RectTransform)button.transform;var originalSize=buttonRect.sizeDelta;var originalPosition=buttonRect.anchoredPosition;var draw=typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("RenderWaterSurface",BindingFlags.Instance|BindingFlags.NonPublic);buttonRect.sizeDelta=new Vector2(80,originalSize.y);draw.Invoke(binding.View,new object[]{area});buttonRect.GetWorldCorners(corners);left=corners.Min(x=>area.rectTransform.InverseTransformPoint(x).x);right=corners.Max(x=>area.rectTransform.InverseTransformPoint(x).x);lines=area.GetComponentsInChildren<Image>().Where(x=>x.name.StartsWith("WaterSurface")).ToArray();Assert.IsNotEmpty(lines);foreach(var line in lines){float x=line.rectTransform.anchoredPosition.x;Assert.IsTrue(x+line.rectTransform.rect.width<=left||x>=right);}button.gameObject.SetActive(false);draw.Invoke(binding.View,new object[]{area});lines=area.GetComponentsInChildren<Image>().Where(x=>x.name.StartsWith("WaterSurface")).ToArray();Assert.AreEqual(1,lines.Length);Assert.AreEqual(area.rectTransform.rect.width,lines[0].rectTransform.rect.width,.01f);buttonRect.sizeDelta=originalSize;button.gameObject.SetActive(true);Assert.AreEqual(originalPosition,buttonRect.anchoredPosition);draw.Invoke(binding.View,new object[]{area});
            }
        }
        [Test] public async Task 水中呼吸ゲージは残り一frameでも内側の非負幅を保ち死亡で隠す()
        {
            var model=system.OutsideExploration;model.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWater);var state=model.Core.CaptureState();state.Position=60000;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.Render();var fill=binding.View.GetComponentsInChildren<Image>(true).Single(x=>x.name=="BreathRemaining");Assert.AreEqual(216,fill.rectTransform.rect.width,.001f);for(int n=0;n<599;n++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}binding.View.Render();Assert.Greater(fill.rectTransform.rect.width,0);Assert.Less(fill.rectTransform.rect.width,1);Assert.GreaterOrEqual(fill.rectTransform.anchoredPosition.x,0);Assert.LessOrEqual(fill.rectTransform.anchoredPosition.x+fill.rectTransform.rect.width,218);model.Core.TickFrame(0);model.AdvancePlayerFrame();binding.View.Render();Assert.IsFalse(fill.transform.parent.gameObject.activeSelf);
            }
        }
        [Test] public async Task 外界Pause被覆は状態を保ち背面Backと復帰同frameの旧操作を拒否する()
        {
            int returned=0,paused=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,factory,()=>true,()=>returned++,paused:()=>paused++)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);
                var back=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseOutsideExploration");var pause=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="PauseOutsideExploration");
                pause.onClick.Invoke();Assert.AreEqual(1,paused);binding.SetCovered(true);back.onClick.Invoke();pause.onClick.Invoke();Assert.AreEqual(0,returned);Assert.AreEqual(1,paused);Assert.IsTrue(binding.IsOpen);Assert.IsFalse(binding.View.CanPlay);
                binding.SetCovered(false);back.onClick.Invoke();Assert.AreEqual(0,returned);Assert.AreSame(back.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(0,system.OutsideExploration.Core.Frame);
            }
            Assert.IsFalse(system.OutsideExploration.IsPresented);
        }

        [Test] public async Task Pause一時制限は旧イベントも拒否し終了時に元の各ボタン状態だけを復元する()
        {
            var panel=Panel("restricted-pause");var resume=Button(panel,"ResumeButton");var load=Button(panel,"LoadCheckpointButton");resume.interactable=false;int loaded=0;load.onClick.AddListener(()=>loaded++);
            var view=panel.AddComponent<GameplayPauseView>();view.Initialize(()=>true);var layer=views.GetLayer("restriction-fixture");
            layer.Register(owner,"restricted",_=>Task.FromResult(panel),(go,ctx)=>view.Show(ctx),go=>view.Hide());
            Assert.IsTrue((await layer.ShowAsync("restricted")).Succeeded);var old=load.onClick;view.RestrictControls(new[]{"LoadCheckpointButton"});Assert.IsFalse(load.interactable);old.Invoke();Assert.AreEqual(0,loaded);
            layer.CloseCurrent();Assert.IsTrue(load.interactable);Assert.IsFalse(resume.interactable);old.Invoke();Assert.AreEqual(0,loaded);
            Assert.IsTrue((await layer.ShowAsync("restricted")).Succeeded);load.onClick.Invoke();Assert.AreEqual(1,loaded);Assert.IsFalse(resume.interactable);
        }

        [TestCase(false)] [TestCase(true)] public void 外界フォーカス入力破棄は現ownerの未消化edgeだけを捨て時計と方向受付を保つ(bool currentOwner)
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            bool paused=Olca.Unity.GameplayPause.IsPaused;var pt=typeof(Olca.Unity.GameplayPause);var release=pt.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pt.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);release.SetValue(null,false);resume.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);long frequency=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true}));now+=frequency/60+1;runtime.Pump(driver,true);
                long frame=runtime.Frame,x=model.Core.Position;int oldCommand=0;
                events.Add(new Olca.Unity.TimestampedLogicalInput(now+frequency/10,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,JumpButtonDown=true,JumpButton=true}));loop.QueueTimestampedCommand(now+frequency/10,()=>oldCommand++);runtime.Pump(driver,true);
                runtime.ResetInputForFocus(currentOwner?driver:new object());Assert.AreEqual(frame,runtime.Frame);Assert.AreEqual(x,model.Core.Position);
                events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,Horizontal=1}));now+=frequency/10+frequency/60+1;runtime.Pump(driver,true);
                Assert.Greater(runtime.Frame,frame);Assert.Greater(model.Core.Position,x);Assert.AreEqual(currentOwner?0:1,oldCommand);
                if(currentOwner)Assert.AreEqual(0,model.Core.Height,"既にruntimeへ取り込んだ旧ジャンプを再生しない");else Assert.Greater(model.Core.Height,0,"旧ownerからの破棄要求では現入力を変えない");
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [TestCase(false)] [TestCase(true)] public void 外界行動不能は移動前に判定し自走と保持再発動を止め重力時計は進む(bool stagger)
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,staggerDurationFrames:3));
            bool paused=Olca.Unity.GameplayPause.IsPaused;var pt=typeof(Olca.Unity.GameplayPause);var release=pt.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pt.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);release.SetValue(null,false);resume.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);long frequency=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                void Step(bool held=false){events.Add(new Olca.Unity.TimestampedLogicalInput(now+1,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=held,Horizontal=held?1:0,JumpButtonDown=held,JumpButton=held,DashButtonDown=held,DashButton=held}));now+=frequency/60+1;runtime.Pump(driver,true);}
                Step();Step(true);Assert.IsTrue(model.Core.IsDashing||model.Core.IsDashJumping);long x=model.Core.Position,y=model.Core.Height,frame=model.Core.Frame;
                if(stagger)model.Player.TakeDamage(1,forceStagger:true);else model.Player.ApplyParalysis(3);
                Assert.IsTrue(model.IsMovementLockedForNextFrame);Step(true);Assert.AreEqual(x,model.Core.Position);Assert.Greater(model.Core.Height,y,"空中の重力時計は停止しない");Assert.AreEqual(frame+1,model.Core.Frame);Assert.IsFalse(model.Core.IsDashing);Assert.IsFalse(model.Core.IsDashJumping);Assert.AreEqual(model.Core.Frame,runtime.Frame);
                int remain=stagger?model.Player.StaggerRemainingFrames:Math.Max(model.Player.ParalysisRemainingFrames,model.Player.StaggerRemainingFrames);
                for(int i=0;i<remain;i++){Step(true);Assert.AreEqual(x,model.Core.Position);}
                Assert.IsFalse(model.IsMovementLockedForNextFrame);Step(true);Assert.Greater(model.Core.Position,x);Assert.IsFalse(model.Core.IsDashing,"保持中は解除後に自動ダッシュしない");
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [Test] public void 外界巡回は採用済み時刻だけを使い選択保持が移動neutralを妨げない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();bool paused=Olca.Unity.GameplayPause.IsPaused;var pauseType=typeof(Olca.Unity.GameplayPause);var release=pauseType.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pauseType.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);
            try{
                var source=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Range(0,5),id=>System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),n=>new Olca.Core.Domain.Card("cycle-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))));
                var owned=System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(source,c=>(c.Id,c.ExceedLevel)),g=>g.Key,g=>System.Linq.Enumerable.Count(g));Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));
                using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                    var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);
                    long frequency=System.Diagnostics.Stopwatch.Frequency,origin=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10,now=origin;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);
                    var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                    var neutral=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true};
                    var selection=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true,AnyKey=true,AnyKeyExceptSelection=false,SelectionNextHeld=true,SelectionNextDown=true,SelectionNextAt=origin+frequency/20};
                    events.Add(new Olca.Unity.TimestampedLogicalInput(origin-frequency,selection));events.Add(new Olca.Unity.TimestampedLogicalInput(origin,neutral));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency*2,selection));
                    now=origin+frequency/10;runtime.Pump(driver,true);Assert.AreEqual(0,model.Cards.SelectedIndex,"旧時刻と未来の押下は未採用");
                    events.Add(new Olca.Unity.TimestampedLogicalInput(now,selection));now+=frequency/10;runtime.Pump(driver,true);Assert.AreEqual(1,model.Cards.SelectedIndex);
                    var moving=selection.Copy();moving.SelectionNextDown=false;moving.AnyKeyExceptSelection=true;moving.Horizontal=1;events.Add(new Olca.Unity.TimestampedLogicalInput(now,moving));now+=frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Position,model.Core.Definition.Start,"選択キーだけ保持中にも移動neutralは成立済み");Assert.AreEqual(runtime.Frame,model.Core.Frame);
                }
            }finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [Test] public void 外界入力epochは古い履歴を捨て未来neutralを採用前に評価しない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();bool paused=Olca.Unity.GameplayPause.IsPaused;var pauseType=typeof(Olca.Unity.GameplayPause);var release=pauseType.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pauseType.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();input.QueueSnapshot();runtime.Attach(driver,input);Assert.AreEqual(1,input.CaptureTimestamped().Length,"所有境界前の履歴を残さない");
                // 製品入力とは別の時刻列fixture。実入口はnative InputSystemで別途確認する。
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);
                long frequency=System.Diagnostics.Stopwatch.Frequency,origin=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10,now=origin;
                typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);
                var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                var held=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,Horizontal=1};var neutral=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true};
                events.Add(new Olca.Unity.TimestampedLogicalInput(origin,held));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency*2,neutral));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency/20,held));
                now=origin+frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Frame,0);Assert.AreEqual(model.Core.Definition.Start,model.Core.Position,"未来neutralでheldを先に許可しない");Assert.AreEqual(runtime.Frame,model.Core.Frame);
                now=origin+frequency*2+frequency/10;runtime.Pump(driver,true);Assert.AreEqual(model.Core.Definition.Start,model.Core.Position);
                events.Add(new Olca.Unity.TimestampedLogicalInput(now,held));now+=frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Position,model.Core.Definition.Start);Assert.AreEqual(runtime.Frame,model.Core.Frame);
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [Test] public void Chrome遮蔽は背面描画とraycastだけを止め各元状態とactiveを復元する()
        {
            var chrome=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null).Chrome();
            var original=chrome.Save.gameObject.AddComponent<CanvasGroup>();original.alpha=.4f;original.interactable=false;original.blocksRaycasts=true;original.enabled=false;
            bool collectionActive=chrome.Collection.gameObject.activeSelf;chrome.SetCovered(true);chrome.SetCovered(true);
            Assert.IsTrue(original.enabled);Assert.AreEqual(0,original.alpha);Assert.IsFalse(original.interactable);Assert.IsFalse(original.blocksRaycasts);Assert.AreEqual(collectionActive,chrome.Collection.gameObject.activeSelf);
            chrome.SetCovered(false);Assert.AreEqual(.4f,original.alpha);Assert.IsFalse(original.interactable);Assert.IsTrue(original.blocksRaycasts);Assert.IsFalse(original.enabled);Assert.AreEqual(collectionActive,chrome.Collection.gameObject.activeSelf);
            var added=chrome.Pause.GetComponent<CanvasGroup>();Assert.AreEqual(1,added.alpha);Assert.IsTrue(added.blocksRaycasts);Assert.IsTrue(added.interactable);chrome.SetCovered(false);
        }

        [TestCase(false,-.01f,-1)][TestCase(false,0,0)][TestCase(false,.19f,0)][TestCase(false,.21f,1)][TestCase(false,.41f,2)][TestCase(false,.6f,-1)][TestCase(true,0,3)][TestCase(true,.12f,3)][TestCase(true,.14f,4)][TestCase(true,.28f,5)][TestCase(true,.4f,-1)]
        public void 支援専用本体は効果の時計で三姿勢を選び寿命外を使わない(bool shield,float age,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialSupportBodyMotion.FrameIndex(shield,age));}
        [TestCase(false)][TestCase(true)] public void 支援専用本体は衣装ごとの六姿勢と足元を保ち借用素材を破棄しない(bool fire)
        {
            using(var clip=new Olca.Presentation.Exploration.TrialSupportBodyMotion()){var seen=new HashSet<Sprite>();foreach(bool shield in new[]{false,true})foreach(float fraction in new[]{0f,.4f,.8f}){float age=fraction*(shield ? .4f : .6f);var sprite=clip.Resolve(shield,age,fire);Assert.NotNull(sprite);Assert.AreEqual((fire?"trial_fire_support_body_":"trial_support_body_")+Olca.Presentation.Exploration.TrialSupportBodyMotion.FrameIndex(shield,age),sprite.name);Assert.AreEqual(new Vector2(128,14),sprite.pivot);Assert.IsTrue(clip.Contains(sprite));seen.Add(sprite);}Assert.AreEqual(6,seen.Count);clip.Dispose();Assert.IsNull(clip.Resolve(false,0,fire));Assert.IsTrue(seen.All(x=>x!=null&&x.texture!=null));}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void 支援専用本体の欠損重複規格違いは一度だけ読み元姿勢へ戻す(int malformed)
        {
            var source=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_support_body_sheet").OrderBy(x=>x.name).ToArray();var bad=source.ToArray();Sprite temporary=null;if(malformed==0)bad=Array.Empty<Sprite>();else if(malformed==1)bad[1]=bad[0];else{temporary=Sprite.Create(source[0].texture,new Rect(0,0,128,128),new Vector2(.5f,.5f),100);temporary.name="trial_support_body_0";bad[0]=temporary;}int loads=0;try{using(var clip=new Olca.Presentation.Exploration.TrialSupportBodyMotion(key=>{loads++;return bad;})){Assert.IsNull(clip.Resolve(false,0));Assert.IsNull(clip.Resolve(true,.2f));Assert.AreEqual(1,loads);}}finally{if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary);}
        }
        [TestCase(false)][TestCase(true)] public void 支援専用本体の全セルは完全透過と二二四画素の高さと共通足位置を保つ(bool fire)
        {
            var t=new Texture2D(2,2);try{string stem=fire?"trial_fire_support_body":"trial_support_body";Assert.IsTrue(t.LoadImage(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/"+stem+"_sheet.png"))));Assert.AreEqual(768,t.width);Assert.AreEqual(512,t.height);var pixels=t.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));int maxHeight=0;var toes=new List<int>();for(int n=0;n<6;n++){int ox=n%3*256,oy=(1-n/3)*256,minY=256,maxY=-1,minX=256,maxX=-1,toe=256;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);if(y<25)toe=Math.Min(toe,x);}Assert.AreEqual(14,minY);Assert.Greater(minX,0);Assert.Less(maxX,255);Assert.Less(maxY,255);maxHeight=Math.Max(maxHeight,maxY-minY+1);toes.Add(toe);}Assert.AreEqual(224,maxHeight);Assert.LessOrEqual(toes.Max()-toes.Min(),2);}finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        [TestCase(false,false,-1)][TestCase(false,false,1)][TestCase(false,true,-1)][TestCase(false,true,1)][TestCase(true,false,-1)][TestCase(true,false,1)][TestCase(true,true,-1)][TestCase(true,true,1)]
        public async Task 支援専用本体は実使用の衣装左右と効果時計へ同期し描画で性能を変えない(bool fire,bool shield,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);model.Player.TakeDamage(20);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("support331",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(331),out _));using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);model.TickTraversal(facing);model.TickTraversal(0);string id=shield?"shield":"heal";for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id);n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id));var view=binding.View;view.ConfigureCombatFeedback(null);int remaining=model.Cards.RemainingCount;Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<30&&(shield?!model.Player.HasBarrier:model.Player.CurrentHp<100);n++)model.TickTraversal(0);view.Render();var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var canvas=shield?(Olca.Presentation.Exploration.OutsideFeedbackSpriteCanvas)view.GetComponent<Olca.Presentation.Exploration.OutsideShieldActivationSpriteCanvas>():view.GetComponent<Olca.Presentation.Exploration.OutsideHealingSpriteCanvas>();Assert.AreEqual((fire?"trial_fire_support_body_":"trial_support_body_")+(shield?3:0),body.sprite.name);Assert.AreEqual(facing,body.rectTransform.localScale.x);var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int n=0;n<3;n++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(remaining-1,model.Cards.RemainingCount);Assert.AreEqual(shield?80:100,model.Player.CurrentHp);Assert.AreEqual(shield?15:0,model.Player.BarrierHp);float duration=shield ? .4f : .6f;var clockOwner=new object();Olca.Unity.BattleVisualClock.SetPaused(clockOwner,true);try{canvas.Paint(0,10000);canvas.Paint(duration*.5f,10001);view.Render();StringAssert.EndsWith("_"+Olca.Presentation.Exploration.TrialSupportBodyMotion.FrameIndex(shield,canvas.AnimationAge),body.sprite.name);var borrowed=body.sprite;canvas.Paint(duration,10002);view.Render();StringAssert.DoesNotContain("support_body",body.sprite.name);views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(canvas.IsAnimating);Assert.NotNull(borrowed.texture);}finally{Olca.Unity.BattleVisualClock.SetPaused(clockOwner,false);}
            }
        }
        [TestCase(false)][TestCase(true)] public async Task 支援専用本体は最新効果を優先し短い新効果終了後に古い姿勢へ戻らない(bool fire)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);model.TickTraversal(0);var view=binding.View;view.ConfigureCombatFeedback(null);model.Player.TakeDamage(50);model.Player.Heal(10);view.Render();StringAssert.EndsWith("support_body_0",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);model.Player.SetBarrier(15);view.Render();StringAssert.EndsWith("support_body_3",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);var heal=view.GetComponent<Olca.Presentation.Exploration.OutsideHealingSpriteCanvas>();var defense=view.GetComponent<Olca.Presentation.Exploration.OutsideShieldActivationSpriteCanvas>();Assert.Greater(defense.PulseOrder,heal.PulseOrder);defense.Paint(0,10000);defense.Paint(.4f,10001);view.Render();Assert.IsTrue(heal.IsAnimating);StringAssert.DoesNotContain("support_body",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);model.Player.Heal(10);view.Render();StringAssert.EndsWith("support_body_0",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);}
        }
        [TestCase(false,0)][TestCase(false,1)][TestCase(false,2)][TestCase(true,0)][TestCase(true,1)][TestCase(true,2)] public async Task 支援専用本体は移動跳躍次攻撃で中断し停止後に再表示しない(bool shield,int mode)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.TakeDamage(40);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("supportcancel331",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(331),out _));model.EnableTrialCardPolish();using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);model.TickTraversal(0);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id=="sword");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));var view=binding.View;view.ConfigureCombatFeedback(null);if(shield)model.Player.SetBarrier(15);else model.Player.Heal(10);view.Render();StringAssert.Contains("support_body",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);if(mode==0)model.TickTraversal(1);else if(mode==1)model.TickTraversal(0,true,true);else Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out _));view.Render();StringAssert.DoesNotContain("support_body",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);for(int n=0;n<100;n++)model.TickTraversal(0);view.Render();StringAssert.DoesNotContain("support_body",view.transform.Find("OutsideEntryPlayer").GetComponent<Image>().sprite.name);}
        }
        [TestCase(-.01f,-1)] [TestCase(0f,0)] [TestCase(.099f,0)] [TestCase(.1f,1)] [TestCase(.3f,3)] [TestCase(.599f,5)] [TestCase(.6f,-1)]
        public void 回復光粒は六枚の短い表示だけを持つ(float elapsed,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.OutsideHealingSpriteCanvas.FrameIndex(elapsed));}
        [Test] public void 回復光粒は正の実回復だけに反応し停止と重複描画と解除を守る()
        {
            var root=new GameObject("HealingTest",typeof(RectTransform));var bodyGo=new GameObject("Body",typeof(RectTransform),typeof(Image));bodyGo.transform.SetParent(root.transform,false);var body=bodyGo.GetComponent<Image>();body.rectTransform.anchoredPosition=new Vector2(20,30);body.rectTransform.localScale=new Vector3(-1,1,1);var canvas=root.AddComponent<Olca.Presentation.Exploration.OutsideHealingSpriteCanvas>();var actor=new Olca.Core.Domain.PlayerCharacter(100);bool live=true;
            try{canvas.Bind(actor,()=>live,body,100);
            actor.Heal(10);
            Assert.IsFalse(canvas.IsAnimating);
            actor.TakeDamage(30);
            Assert.IsFalse(canvas.IsAnimating);
            actor.Heal(10);
            Assert.AreEqual(80,actor.CurrentHp);
            Assert.IsTrue(canvas.IsAnimating);
            var fx=root.transform.Find("OutsideHealingEffect").GetComponent<Image>();
            Assert.IsFalse(fx.raycastTarget);
            Assert.Less(fx.transform.GetSiblingIndex(),body.transform.GetSiblingIndex());
            Assert.AreEqual(new Vector2(20,155),fx.rectTransform.anchoredPosition);
            Assert.AreEqual(Vector3.one,fx.rectTransform.localScale);
            canvas.Paint(0,1);
            Assert.AreEqual("trial_healing_effect_0",fx.sprite.name);
            canvas.Paint(.2f,1);
            Assert.AreEqual("trial_healing_effect_0",fx.sprite.name);
            canvas.Paint(0,2);
            Assert.AreEqual("trial_healing_effect_0",fx.sprite.name);
            canvas.Paint(.2f,3);
            Assert.AreEqual("trial_healing_effect_2",fx.sprite.name);
            body.rectTransform.anchoredPosition=new Vector2(40,60);
            canvas.Paint(0,4);
            Assert.AreEqual(new Vector2(40,185),fx.rectTransform.anchoredPosition);
            actor.Heal(5);
            Assert.AreEqual("trial_healing_effect_0",fx.sprite.name);
            canvas.Paint(0,5);
            canvas.Paint(.6f,6);
            Assert.IsFalse(canvas.IsAnimating);
            Assert.IsNull(fx.sprite);
            live=false;
            actor.Heal(5);
            Assert.IsFalse(canvas.IsAnimating);
            live=true;
            canvas.Unbind();
            actor.Heal(5);
            Assert.IsFalse(canvas.IsAnimating);
            canvas.Bind(actor,()=>true,body,100);
            actor.Heal(1);
            Assert.IsTrue(canvas.IsAnimating);
            root.SetActive(false);
            // EditModeでは非ExecuteAlwaysのUnityメッセージは自動配信されないため、解除ハンドラを明示検証する。
            typeof(Olca.Presentation.Exploration.OutsideHealingSpriteCanvas).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(canvas,null);
            Assert.IsFalse(canvas.IsAnimating);
            root.SetActive(true);
            actor.Heal(1);
            Assert.IsFalse(canvas.IsAnimating);
            Assert.NotNull(Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_healing_effect_sheet").First().texture);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void 回復光粒の連番は透過と中心規格を保つ()
        {
            var frames=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_healing_effect_sheet");Assert.AreEqual(6,frames.Length);for(int n=0;n<6;n++){var frame=frames.Single(x=>x.name=="trial_healing_effect_"+n);Assert.AreEqual(new Vector2(256,256),frame.rect.size);Assert.AreEqual(new Vector2(128,128),frame.pivot);Assert.AreEqual(100,frame.pixelsPerUnit);}
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_healing_effect_sheet.png"))));Assert.AreEqual(768,texture.width);Assert.AreEqual(512,texture.height);var pixels=texture.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));for(int n=0;n<6;n++){int ox=n%3*256,oy=(1-n/3)*256;int count=0;for(int y=0;y<256;y++)for(int x=0;x<256;x++){var c=pixels[(oy+y)*768+ox+x];if(c.a==0)continue;count++;Assert.IsTrue(x>0&&x<255&&y>0&&y<255);}Assert.Greater(count,0);}}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }

        [TestCase(-.01f,-1)] [TestCase(0f,0)] [TestCase(.049f,0)] [TestCase(.05f,1)] [TestCase(.15f,3)] [TestCase(.299f,5)] [TestCase(.3f,-1)]
        public void シールド吸収光は三百ミリ秒で六枚を終える(float elapsed,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.OutsideShieldHitSpriteCanvas.FrameIndex(elapsed));}
        [Test] public void シールド吸収光は防いだ時だけ出て破壊後のHP被弾では出ない()
        {
            var root=new GameObject("ShieldFeedbackTest",typeof(RectTransform));var bodyGo=new GameObject("Body",typeof(RectTransform),typeof(Image));bodyGo.transform.SetParent(root.transform,false);var body=bodyGo.GetComponent<Image>();var canvas=root.AddComponent<Olca.Presentation.Exploration.OutsideShieldHitSpriteCanvas>();var actor=new Olca.Core.Domain.PlayerCharacter(100);bool live=true;
            try{
                actor.SetBarrier(15);canvas.Bind(actor,()=>live,body,100);Assert.IsFalse(canvas.IsAnimating);
                actor.TakeDamage(10);Assert.AreEqual(100,actor.CurrentHp);Assert.AreEqual(5,actor.BarrierHp);Assert.IsTrue(canvas.IsAnimating);
                var fx=root.transform.Find("OutsideShieldHitEffect").GetComponent<Image>();Assert.IsFalse(fx.raycastTarget);Assert.Less(fx.transform.GetSiblingIndex(),body.transform.GetSiblingIndex());
                canvas.Paint(0,1);canvas.Paint(0,2);Assert.AreEqual("trial_shield_hit_effect_0",fx.sprite.name);canvas.Paint(.15f,3);Assert.AreEqual("trial_shield_hit_effect_3",fx.sprite.name);canvas.Paint(.15f,3);Assert.AreEqual("trial_shield_hit_effect_3",fx.sprite.name);
                actor.TakeDamage(10);Assert.AreEqual(100,actor.CurrentHp);Assert.AreEqual(0,actor.BarrierHp);Assert.AreEqual("trial_shield_hit_effect_0",fx.sprite.name);
                canvas.Paint(0,4);canvas.Paint(.3f,5);Assert.IsFalse(canvas.IsAnimating);actor.TakeDamage(10);Assert.AreEqual(90,actor.CurrentHp);Assert.IsFalse(canvas.IsAnimating);actor.Heal(5);Assert.IsFalse(canvas.IsAnimating);
                actor.SetBarrier(15);actor.TakeDamage(10);Assert.IsTrue(canvas.IsAnimating);live=false;canvas.Paint(0,6);Assert.IsFalse(canvas.IsAnimating);live=true;actor.TakeDamage(10);Assert.IsFalse(canvas.IsAnimating);
                canvas.Bind(actor,()=>true,body,100);actor.SetBarrier(15);actor.TakeDamage(10);Assert.IsTrue(canvas.IsAnimating);typeof(Olca.Presentation.Exploration.OutsideShieldHitSpriteCanvas).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(canvas,null);Assert.IsFalse(canvas.IsAnimating);actor.TakeDamage(10);Assert.IsFalse(canvas.IsAnimating);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void シールド吸収光と回復光粒は同時に出ても寿命とHPを混同しない()
        {
            var root=new GameObject("TwoFeedbackTest",typeof(RectTransform));var bodyGo=new GameObject("Body",typeof(RectTransform),typeof(Image));bodyGo.transform.SetParent(root.transform,false);var body=bodyGo.GetComponent<Image>();var heal=root.AddComponent<Olca.Presentation.Exploration.OutsideHealingSpriteCanvas>();var shield=root.AddComponent<Olca.Presentation.Exploration.OutsideShieldHitSpriteCanvas>();var actor=new Olca.Core.Domain.PlayerCharacter(100);
            try{actor.TakeDamage(20);actor.SetBarrier(15);heal.Bind(actor,()=>true,body,100);shield.Bind(actor,()=>true,body,100);actor.Heal(5);Assert.IsTrue(heal.IsAnimating);Assert.IsFalse(shield.IsAnimating);actor.TakeDamage(10);Assert.IsTrue(heal.IsAnimating);Assert.IsTrue(shield.IsAnimating);heal.Paint(0,1);shield.Paint(0,1);heal.Paint(.3f,2);shield.Paint(.3f,2);Assert.IsTrue(heal.IsAnimating);Assert.IsFalse(shield.IsAnimating);Assert.AreEqual("trial_healing_effect_3",root.transform.Find("OutsideHealingEffect").GetComponent<Image>().sprite.name);heal.Paint(.3f,3);Assert.IsFalse(heal.IsAnimating);Assert.AreEqual(85,actor.CurrentHp);Assert.AreEqual(5,actor.BarrierHp);}
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void シールド吸収光は六枚の透過セルと中心規格を守る()
        {
            var frames=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_shield_hit_effect_sheet");Assert.AreEqual(6,frames.Length);for(int n=0;n<6;n++){var frame=frames.Single(x=>x.name=="trial_shield_hit_effect_"+n);Assert.AreEqual(new Vector2(256,256),frame.rect.size);Assert.AreEqual(new Vector2(128,128),frame.pivot);Assert.AreEqual(100,frame.pixelsPerUnit);}
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_shield_hit_effect_sheet.png"))));Assert.AreEqual(768,texture.width);Assert.AreEqual(512,texture.height);var pixels=texture.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));for(int n=0;n<6;n++){int count=0,ox=n%3*256,oy=(1-n/3)*256;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){count++;Assert.IsTrue(x>0&&x<255&&y>0&&y<255);}Assert.Greater(count,0);}}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }

        [TestCase(-.01f,-1)][TestCase(0,0)][TestCase(.066f,0)][TestCase(.067f,1)][TestCase(.2f,3)][TestCase(.399f,5)][TestCase(.4f,-1)] public void シールド発動専用光は四割秒の境界を六枚へ写す(float age,int expected){Assert.AreEqual(expected,Olca.Presentation.Exploration.OutsideShieldActivationSpriteCanvas.FrameIndex(age));}
        [Test] public void シールド発動専用通知は成立した正の付与だけを発火し既存数値通知を変えない()
        {
            var actor=new Olca.Core.Domain.PlayerCharacter(100);var values=new List<int>();var ordinary=new List<Olca.Core.Domain.CombatFeedback>();actor.OnBarrierApplied+=values.Add;actor.OnCombatFeedback+=ordinary.Add;actor.SetBarrier(0);Assert.IsEmpty(values);actor.SetBarrier(15);CollectionAssert.AreEqual(new[]{15},values);Assert.AreEqual(15,actor.BarrierHp);Assert.Throws<ArgumentOutOfRangeException>(()=>actor.SetBarrier(10,0));Assert.AreEqual(15,actor.BarrierHp);Assert.AreEqual(1,values.Count);actor.SetBarrier(20);CollectionAssert.AreEqual(new[]{15,20},values);Assert.IsEmpty(ordinary);Assert.AreEqual(100,actor.CurrentHp);
        }
        [TestCase(false)][TestCase(true)] public void シールド発動専用光は既存防御の再表示や吸収で始まらず停止再開と購読解除を保つ(bool already)
        {
            var root=new GameObject("ShieldCast330",typeof(RectTransform));var go=new GameObject("Body",typeof(RectTransform),typeof(Image));go.transform.SetParent(root.transform,false);var body=go.GetComponent<Image>();var actor=new Olca.Core.Domain.PlayerCharacter(100);if(already)actor.SetBarrier(15);var canvas=root.AddComponent<Olca.Presentation.Exploration.OutsideShieldActivationSpriteCanvas>();
            try{canvas.Bind(actor,()=>true,body,100);Assert.IsFalse(canvas.IsAnimating);actor.SetBarrier(15);var image=root.transform.Find("OutsideShieldActivation").GetComponent<Image>();Assert.IsTrue(canvas.IsAnimating);Assert.AreEqual("trial_shield_activation_0",image.sprite.name);Assert.IsFalse(image.raycastTarget);Assert.Less(image.transform.GetSiblingIndex(),body.transform.GetSiblingIndex());canvas.Paint(0,100);canvas.Paint(0,101);Assert.AreEqual("trial_shield_activation_0",image.sprite.name);canvas.Paint(.2f,102);Assert.AreEqual("trial_shield_activation_3",image.sprite.name);canvas.Paint(.2f,102);Assert.AreEqual("trial_shield_activation_3",image.sprite.name);canvas.Paint(0,103);Assert.IsTrue(canvas.IsAnimating);canvas.Paint(.2f,104);Assert.IsFalse(canvas.IsAnimating);actor.TakeDamage(10);Assert.IsFalse(canvas.IsAnimating);Assert.AreEqual(5,actor.BarrierHp);actor.SetBarrier(15);Assert.AreEqual("trial_shield_activation_0",image.sprite.name);var borrowed=image.sprite;canvas.Unbind();Assert.IsFalse(image.enabled);Assert.IsNull(image.sprite);actor.SetBarrier(20);Assert.IsFalse(canvas.IsAnimating);Assert.NotNull(borrowed.texture);Assert.AreEqual(100,actor.CurrentHp);}finally{canvas.Unbind();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void シールド発動専用連番は中心と完全透過の六セルを保つ()
        {
            var frames=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_shield_activation_sheet");Assert.AreEqual(6,frames.Length);for(int n=0;n<6;n++){var frame=frames.Single(x=>x.name=="trial_shield_activation_"+n);Assert.AreEqual(new Vector2(256,256),frame.rect.size);Assert.AreEqual(new Vector2(128,128),frame.pivot);Assert.AreEqual(100,frame.pixelsPerUnit);}
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_shield_activation_sheet.png"))));Assert.AreEqual(768,texture.width);Assert.AreEqual(512,texture.height);var pixels=texture.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));for(int n=0;n<6;n++){int count=0,ox=n%3*256,oy=(1-n/3)*256;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){count++;Assert.IsTrue(x>0&&x<255&&y>0&&y<255);}Assert.Greater(count,0);}}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        [TestCase(false,-1)][TestCase(false,1)][TestCase(true,-1)][TestCase(true,1)] public async Task シールド発動専用光は実カード使用の消費と防御を変えず衣装左右と解除を保つ(bool fire,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("shield330",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(330),out _));using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id=="shield");n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="shield")));model.TickTraversal(facing);var view=binding.View;view.ConfigureCombatFeedback(null);Assert.IsNull(view.transform.Find("OutsideShieldActivation"));int remaining=model.Cards.RemainingCount;Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<20&&!model.Player.HasBarrier;n++)model.TickTraversal(0);view.Render();var effect=view.transform.Find("OutsideShieldActivation").GetComponent<Image>();Assert.IsTrue(effect.enabled);Assert.AreEqual("trial_shield_activation_0",effect.sprite.name);Assert.AreEqual(15,model.Player.BarrierHp);Assert.AreEqual(remaining-1,model.Cards.RemainingCount);var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int n=0;n<3;n++)view.Render();CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(100,model.Player.CurrentHp);views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(effect.enabled);Assert.IsNull(effect.sprite);model.Player.SetBarrier(20);Assert.IsFalse(effect.enabled);
            }
        }
        [TestCase(6)][TestCase(18)] public void 爆風専用連番は既存期間へ六コマを配分し寿命外と破棄後を表示しない(int duration)
        {
            var clip=new Olca.Presentation.Exploration.TrialBombBlastMotion();Sprite retained=null;try{var seen=new HashSet<Sprite>();Assert.IsNull(clip.Resolve(-1,duration));Assert.IsNull(clip.Resolve(0,0));for(int n=0;n<duration;n++){var sprite=clip.Resolve(n,duration);Assert.NotNull(sprite);Assert.AreEqual("trial_bomb_blast_"+(n*6/duration),sprite.name);seen.Add(sprite);retained=sprite;}Assert.AreEqual(6,seen.Count);Assert.IsNull(clip.Resolve(duration,duration));clip.Dispose();Assert.IsNull(clip.Resolve(0,duration));Assert.NotNull(retained.texture);}finally{clip.Dispose();}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void 爆風専用連番の欠損重複規格違いは一度だけ読み表示なしへ戻す(int malformed)
        {
            var good=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_bomb_blast_sheet").OrderBy(x=>x.name).ToArray();Sprite temporary=null;var bad=good.ToArray();if(malformed==0)bad=Array.Empty<Sprite>();else if(malformed==1)bad[1]=bad[0];else{temporary=Sprite.Create(good[0].texture,new Rect(0,0,128,128),new Vector2(.5f,.5f),100);temporary.name="trial_bomb_blast_0";bad[0]=temporary;}int loads=0;try{using(var clip=new Olca.Presentation.Exploration.TrialBombBlastMotion(key=>{loads++;return bad;})){for(int n=0;n<18;n++)Assert.IsNull(clip.Resolve(n,18));Assert.AreEqual(1,loads);}}finally{if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary);}
        }
        [Test] public void 爆風専用連番は中心と完全透過の六セルを保つ()
        {
            var frames=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_bomb_blast_sheet");Assert.AreEqual(6,frames.Length);for(int n=0;n<6;n++){var frame=frames.Single(x=>x.name=="trial_bomb_blast_"+n);Assert.AreEqual(new Vector2(256,256),frame.rect.size);Assert.AreEqual(new Vector2(128,128),frame.pivot);Assert.AreEqual(100,frame.pixelsPerUnit);}
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_bomb_blast_sheet.png"))));Assert.AreEqual(768,texture.width);Assert.AreEqual(512,texture.height);var pixels=texture.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));for(int n=0;n<6;n++){int count=0,ox=n%3*256,oy=(1-n/3)*256;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){count++;Assert.IsTrue(x>0&&x<255&&y>0&&y<255);}Assert.Greater(count,0);}}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        [TestCase(false)][TestCase(true)] public async Task ボム着弾は実通知前に爆風を表示せず発動後だけ一つの連番を出す(bool annex)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var cards=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("bomb340",cards,cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(490),out _));if(annex){while(model.Core.Position<600000){model.Core.TickFrame(1);model.AdvanceDeckLocation();}Assert.NotNull(model.Cards);model.RecoverOutside(Olca.Core.Logic.Integration.OutsideTrialRoute.DoorAnnexPoint);}
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);long throwPosition=annex?1080000:990000;while(model.Core.Position<throwPosition)model.TickTraversal(1);for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id=="bomb");n++)model.Cards.Reload();model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id=="bomb"));Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out _));long revision=model.Cards.HandRevision;bool flightSeen=false;
                for(int n=0;n<180&&model.BombBlasts.Count==0;n++){
                    long frame=model.Core.Frame;binding.View.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(revision,model.Cards.HandRevision);var active=binding.View.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual"&&i.enabled).ToArray();Assert.IsTrue(active.All(i=>i.sprite.name==Olca.Presentation.Field.SharedAttackPropCatalog.Bomb));flightSeen|=model.Bombs.Count>0;model.TickTraversal(0);
                }
                Assert.IsTrue(flightSeen);Assert.AreEqual(1,model.BombBlasts.Count);binding.View.Render();var burst=binding.View.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual"&&i.enabled).ToArray();Assert.AreEqual(1,burst.Length);StringAssert.StartsWith("trial_bomb_blast_",burst[0].sprite.name);Assert.AreEqual(revision,model.Cards.HandRevision);Assert.AreEqual(100,model.Player.CurrentHp);
            }
        }
        [TestCase(false,6)][TestCase(false,18)][TestCase(true,6)][TestCase(true,18)] public async Task 爆風専用表示は同じ論理期間だけ進み停止再描画と消失と衣装を保つ(bool fire,int duration)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var blasts=(List<Olca.Core.Logic.Integration.OutsideBombBlastData>)typeof(Olca.Core.Logic.Integration.OutsideExplorationSession).GetField("bombBlasts",flags).GetValue(model);long began=model.Core.Frame;blasts.Add(new Olca.Core.Logic.Integration.OutsideBombBlastData{SourceX=15*60000,X=15*60000,Height=fire?60000:0,Frame=began,VisualFrames=duration});Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;for(int n=0;n<duration;n++){var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.Render();var parts=view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").ToArray();Assert.AreEqual(1,parts.Length);Assert.IsTrue(parts.All(x=>x.enabled&&x.sprite.name=="trial_bomb_blast_"+(n*6/duration)&&!x.raycastTarget));Assert.That(parts[0].color.a,Is.EqualTo(.9f*(1f-n/(float)duration)).Within(.0001f));var sprite=parts[0].sprite;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);float radius=Olca.Core.Logic.Integration.OutsideBombLaunchData.Radius/60000f;float blastHeight=radius*2*Olca.Presentation.Exploration.TrialBombBlastMotion.VisibleHeight/Olca.Presentation.Exploration.TrialBombBlastMotion.VisibleWidth;
                Assert.That(parts[0].rectTransform.sizeDelta.x/sprite.bounds.size.x,Is.EqualTo(parts[0].rectTransform.sizeDelta.y/sprite.bounds.size.y).Within(.001f));Assert.That(parts[0].rectTransform.sizeDelta.x/sprite.bounds.size.x*Olca.Presentation.Exploration.TrialBombBlastMotion.VisibleWidth,Is.EqualTo(radius*2*pixels).Within(.01f));Assert.That(((RectTransform)parts[0].transform.parent).anchoredPosition.y,Is.EqualTo(((fire?1:0)+blastHeight*.5f)*pixels).Within(.01f));var position=parts[0].rectTransform.anchoredPosition;view.Render();Assert.AreSame(sprite,parts[0].sprite);Assert.AreEqual(position,parts[0].rectTransform.anchoredPosition);Assert.AreEqual(began+n,model.Core.Frame);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(100,model.Player.CurrentHp);model.Core.TickFrame(0);}view.Render();Assert.IsTrue(view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").All(x=>!x.enabled));}
        }
        [Test] public async Task 爆風専用素材欠落時は従来の着弾表示へ戻り飛翔へ持ち越さない()
        {
            var model=system.OutsideExploration;var flags=BindingFlags.Instance|BindingFlags.NonPublic;var blasts=(List<Olca.Core.Logic.Integration.OutsideBombBlastData>)typeof(Olca.Core.Logic.Integration.OutsideExplorationSession).GetField("bombBlasts",flags).GetValue(model);blasts.Add(new Olca.Core.Logic.Integration.OutsideBombBlastData{SourceX=900000,X=900000,Frame=model.Core.Frame,VisualFrames=18});Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var field=typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetField("bombBlastMotion",flags);((IDisposable)field.GetValue(view)).Dispose();field.SetValue(view,new Olca.Presentation.Exploration.TrialBombBlastMotion(key=>Array.Empty<Sprite>()));view.Render();var parts=view.GetComponentsInChildren<Image>(true).Where(i=>i.name=="TrialOutsideBombVisual").ToArray();Assert.AreEqual(3,parts.Length);Assert.IsTrue(parts.All(x=>x.enabled&&x.sprite.name==Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact));blasts.Clear();view.Render();Assert.IsTrue(parts.All(x=>!x.enabled));}
        }
        [Test] public void エア弾専用連番はショットと区別して同じ時計で循環し借用素材を破棄しない()
        {
            var clip=new Olca.Presentation.Exploration.TrialShotFlightMotion();Sprite retained=null;try{var seen=new HashSet<Sprite>();for(int age=0;age<60;age++){var air=clip.Resolve(age,Olca.Core.Domain.TrialInitialCardDefinitions.AirShot);var normal=clip.Resolve(age,Olca.Core.Domain.TrialInitialCardDefinitions.Shot);Assert.NotNull(air);Assert.NotNull(normal);Assert.AreEqual("trial_air_shot_flight_"+Olca.Presentation.Exploration.TrialShotFlightMotion.FrameIndex(age),air.name);StringAssert.StartsWith("trial_shot_flight_",normal.name);Assert.AreNotSame(air,normal);seen.Add(air);retained=air;}Assert.AreEqual(6,seen.Count);Assert.IsNull(clip.Resolve(-1,Olca.Core.Domain.TrialInitialCardDefinitions.AirShot));Assert.IsNull(clip.Resolve(60,Olca.Core.Domain.TrialInitialCardDefinitions.AirShot));clip.Dispose();Assert.IsNull(clip.Resolve(0,Olca.Core.Domain.TrialInitialCardDefinitions.AirShot));Assert.NotNull(retained.texture);}finally{clip.Dispose();}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void エア弾専用連番の不足重複規格違いは旧ショットへ戻り再読込しない(int malformed)
        {
            var normal=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_shot_flight_sheet");var air=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_air_shot_flight_sheet").OrderBy(x=>x.name).ToArray();Assert.AreEqual(6,air.Length);Sprite temporary=null;var bad=air.ToArray();if(malformed==0)bad=Array.Empty<Sprite>();else if(malformed==1)bad[1]=bad[0];else{temporary=Sprite.Create(air[0].texture,new Rect(0,0,128,128),new Vector2(.5f,.5f),100);temporary.name="trial_air_shot_flight_0";bad[0]=temporary;}int airLoads=0,normalLoads=0;
            try{using(var clip=new Olca.Presentation.Exploration.TrialShotFlightMotion(key=>{if(key.EndsWith("trial_air_shot_flight_sheet")){airLoads++;return bad;}normalLoads++;return normal;})){for(int n=0;n<20;n++){var sprite=clip.Resolve(n,Olca.Core.Domain.TrialInitialCardDefinitions.AirShot);Assert.AreEqual("trial_shot_flight_"+Olca.Presentation.Exploration.TrialShotFlightMotion.FrameIndex(n),sprite.name);}Assert.AreEqual(1,airLoads);Assert.AreEqual(1,normalLoads);}}finally{if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary);}
        }
        [Test] public void エア弾専用連番は中心と完全透過の六セルを保つ()
        {
            var frames=Resources.LoadAll<Sprite>("OlcaCharacterMotion/trial_air_shot_flight_sheet");Assert.AreEqual(6,frames.Length);for(int n=0;n<6;n++){var frame=frames.Single(x=>x.name=="trial_air_shot_flight_"+n);Assert.AreEqual(new Vector2(256,256),frame.rect.size);Assert.AreEqual(new Vector2(128,128),frame.pivot);Assert.AreEqual(100,frame.pixelsPerUnit);}
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaCharacterMotion/trial_air_shot_flight_sheet.png"))));Assert.AreEqual(768,texture.width);Assert.AreEqual(512,texture.height);var pixels=texture.GetPixels32();Assert.IsTrue(pixels.All(x=>x.a==0||x.a==255));for(int n=0;n<6;n++){int count=0,ox=n%3*256,oy=(1-n/3)*256;for(int y=0;y<256;y++)for(int x=0;x<256;x++)if(pixels[(oy+y)*768+ox+x].a!=0){count++;Assert.IsTrue(x>0&&x<255&&y>0&&y<255);}Assert.Greater(count,0);}}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        [TestCase(false,false,-1)][TestCase(false,false,1)][TestCase(false,true,-1)][TestCase(false,true,1)][TestCase(true,false,-1)][TestCase(true,false,1)][TestCase(true,true,-1)][TestCase(true,true,1)]
        public async Task エア弾専用表示は地空左右衣装と時計消費論理位置を変えない(bool fire,bool air,int facing)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));if(fire)model.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);var source=new Olca.Application.Battle.SimpleCardFactory().CreateTrialInitialLoadout().Outside.ToArray();Assert.IsTrue(model.QueueSelectedDeck("air327",source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(327),out _));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,resources.GetShared<Font>("OlcaUI/MenuText"),resources)),()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);while(model.Core.Position<15*60000)model.TickTraversal(1);string id=Olca.Core.Domain.TrialInitialCardDefinitions.AirShot;for(int n=0;n<1000&&!model.Cards.Hand.Any(c=>c.Id==id);n++)model.Cards.Reload();Assert.IsTrue(model.Cards.TrySelect(model.Cards.Hand.ToList().FindIndex(c=>c.Id==id)));model.TickTraversal(facing);if(air)model.TickTraversal(0,true,true);Assert.IsTrue(model.TryUseSelectedCard(model.Cards.SelectedCard,model.Cards.HandRevision,out var error),error);for(int n=0;n<30&&model.InitialShots.Count==0;n++)model.TickTraversal(0);Assert.AreEqual(1,model.InitialShots.Count);var view=binding.View;
                for(int n=0;n<12;n++){if(n>0)model.TickTraversal(0);view.Render();var shot=model.InitialShots.Single();Assert.AreEqual(id,shot.CardId);var image=view.transform.Find("TrialStraightShot").GetComponent<Image>();Assert.IsTrue(image.enabled);Assert.AreEqual(facing,image.rectTransform.localScale.x);var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();float pixels=body.rectTransform.sizeDelta.x/(body.sprite.rect.width/body.sprite.pixelsPerUnit);Assert.AreEqual(.8f*pixels,image.rectTransform.sizeDelta.x,.01f);Assert.AreEqual(.8f*pixels,image.rectTransform.sizeDelta.y,.01f);Assert.AreEqual(((shot.Position(model.Core.Frame)-model.Core.Position)/60000f+facing*Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(model.Core.Frame-shot.StartedAt).x)*pixels,image.rectTransform.anchoredPosition.x-body.rectTransform.anchoredPosition.x,.01f);Assert.AreEqual(((shot.Height-model.Core.Height)/60000f+1.2f+Olca.Presentation.Exploration.TrialShotBodyMotion.FlightOffset(model.Core.Frame-shot.StartedAt).y)*pixels,image.rectTransform.anchoredPosition.y-body.rectTransform.anchoredPosition.y,.01f);Assert.AreEqual("trial_air_shot_flight_"+Olca.Presentation.Exploration.TrialShotFlightMotion.FrameIndex(model.Core.Frame-shot.StartedAt),image.sprite.name);long frame=model.Core.Frame,pos=shot.Position(frame),gauge=model.Core.CardGauge.CurrentUnits;int remaining=model.Cards.RemainingCount,hp=model.Player.CurrentHp;var core=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());for(int k=0;k<4;k++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(pos,model.InitialShots.Single().Position(model.Core.Frame));Assert.AreEqual(gauge,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(remaining,model.Cards.RemainingCount);Assert.AreEqual(hp,model.Player.CurrentHp);CollectionAssert.AreEqual(core,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));}for(int n=0;n<70;n++)model.TickTraversal(0);view.Render();Assert.IsFalse(view.transform.Find("TrialStraightShot").GetComponent<Image>().enabled);
            }
        }
    }
}
