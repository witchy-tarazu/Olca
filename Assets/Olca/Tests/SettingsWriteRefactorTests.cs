using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Olca.Unity;
using Olca.Core.Domain;

namespace Olca.Tests
{
    public class SettingsWriteRefactorTests
    {

        [TestCase(false,1920f)][TestCase(true,1920f)][TestCase(false,1662.77f)][TestCase(true,1662.77f)]
        public void 追加アイテムの最終設定行へfocusしても固定保存操作と重ならず列内へ追従する(bool gamepad,float width)
        {
            var host=new UnityEngine.GameObject("OwnedItem143Settings",typeof(UnityEngine.RectTransform));((UnityEngine.RectTransform)host.transform).sizeDelta=new UnityEngine.Vector2(width,1080);
            UnityEngine.Rect Area(UnityEngine.RectTransform r){var points=new UnityEngine.Vector3[4];r.GetWorldCorners(points);return UnityEngine.Rect.MinMaxRect(points.Min(x=>x.x),points.Min(x=>x.y),points.Max(x=>x.x),points.Max(x=>x.y));}
            try{var go=new UnityEngine.GameObject("Settings",typeof(UnityEngine.RectTransform));go.transform.SetParent(host.transform,false);var view=go.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();view.Initialize(UnityEngine.Resources.Load<UnityEngine.Font>("OlcaUI/MenuText"),_=>null,gamepad);view.Open(gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile());UnityEngine.Canvas.ForceUpdateCanvases();var scroll=go.GetComponentInChildren<UnityEngine.UI.ScrollRect>();Assert.NotNull(scroll);var save=go.GetComponentsInChildren<UnityEngine.UI.Button>().Single(x=>x.name=="SaveKeyboardSettings");Assert.IsFalse(save.transform.IsChildOf(scroll.content));Assert.IsFalse(Area((UnityEngine.RectTransform)save.transform).Overlaps(Area(scroll.viewport)));
                using(var menu=new MenuPadScope(()=>true)){menu.Bind(go.transform,"Bind_outside_ItemLeft",()=>{},scroll,directionScroll:false);UnityEngine.Canvas.ForceUpdateCanvases();Assert.AreEqual("Bind_outside_ItemLeft",menu.FocusName);foreach(string name in new[]{"Bind_outside_ItemLeft","Clear_outside_ItemLeft","Action_ItemLeft"}){var row=go.GetComponentsInChildren<UnityEngine.RectTransform>().Single(x=>x.name==name);Assert.IsTrue(row.IsChildOf(scroll.content));var rect=Area(row);var viewport=Area(scroll.viewport);Assert.GreaterOrEqual(rect.yMin,viewport.yMin-.01);Assert.LessOrEqual(rect.yMax,viewport.yMax+.01);Assert.IsFalse(rect.Overlaps(Area((UnityEngine.RectTransform)save.transform)));}}
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test]
        public void 使用不能時に触れて保持したHUDは可能後の解放でも発火せず旧世代と二重clickも拒否する()
        {
            var go=new UnityEngine.GameObject("Item143Press");try{var guard=go.AddComponent<Olca.Presentation.Exploration.OutsideItemPressGuard>();bool available=false;guard.BeginAllowed=()=>available;var data=new UnityEngine.EventSystems.PointerEventData(null){pointerId=1,button=UnityEngine.EventSystems.PointerEventData.InputButton.Left};guard.OnPointerDown(data);available=true;guard.OnPointerUp(data);Assert.IsFalse(guard.Consume());guard.OnPointerDown(data);guard.OnPointerUp(data);Assert.IsTrue(guard.Consume());Assert.IsFalse(guard.Consume());guard.OnPointerDown(data);guard.Clear();guard.OnPointerUp(data);Assert.IsFalse(guard.Consume());}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test]
        public void アイテム受理は状態frameを閉じてからでも麻痺最終frameの入力を早く受け付けない()
        {
            using(var world=new Olca.Core.Logic.Integration.OutsideExplorationSession()){world.TryInitializePlayer(()=>new PlayerCharacter(100));world.Player.ApplyParalysis(2);using(var lease=world.AttachPresentation()){for(int i=0;i<2;i++){world.Core.TickFrame(0);Assert.IsTrue(world.AdvancePlayerFrame(out var allowed));Assert.IsFalse(allowed);}Assert.IsTrue(world.CanPerformAttack);world.Core.TickFrame(0);Assert.IsTrue(world.AdvancePlayerFrame(out var ready));Assert.IsTrue(ready);}}
        }
        [TestCase(false)][TestCase(true)]
        public void 旧設定のアイテム4枠だけを補完し競合は既存を優先して未割当にし原本を変更しない(bool gamepad)
        {
            var defaults=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();string device=gamepad?"gamepad":"keyboard";
            var old=defaults.Bindings.Where(p=>p.Key.Context!="outside"||!p.Key.Action.StartsWith("Item",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value);
            var occupied=gamepad?(device,"outside","WheelPrimary"):(device,"exploration","JumpPrimary");old[occupied]=gamepad?"DpadUp":"Digit1";var before=old.ToArray();var upgraded=UnityInputProvider.DecodeSelectionSlots(old,gamepad);
            CollectionAssert.AreEquivalent(before,old);foreach(var pair in old)Assert.AreEqual(pair.Value,upgraded.Bindings[pair.Key]);Assert.IsNull(upgraded.Bindings[(device,"outside","ItemUp")]);Assert.AreEqual(gamepad?"DpadRight":"Digit2",upgraded.Bindings[(device,"outside","ItemRight")]);
            var partial=upgraded.Bindings.ToDictionary(p=>p.Key,p=>p.Value);partial.Remove((device,"outside","ItemRight"));Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,gamepad));
            string dir=Path.Combine(Path.GetTempPath(),"item143-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var store=gamepad?(GameplayBindingFileStore)new GamepadBindingFileStore(dir):new KeyboardBindingFileStore(dir);var rows=old.Select(p=>"{\"Context\":\""+p.Key.Context+"\",\"Action\":\""+p.Key.Action+"\",\"Key\":"+(p.Value==null?"null":"\""+p.Value+"\"")+"}");File.WriteAllText(store.ActivePath,"{\"Version\":1,\"Device\":\""+device+"\",\"Slots\":["+string.Join(",",rows)+"]}");var bytes=File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryRead(out var loaded,out var error),error);CollectionAssert.AreEquivalent(upgraded.Bindings,loaded.Bindings);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));Assert.IsTrue(store.TrySave(loaded,out error),error);Assert.IsTrue(store.TryRead(out loaded,out error),error);CollectionAssert.AreEquivalent(upgraded.Bindings,loaded.Bindings);var edit=loaded.BeginEdit();Assert.IsTrue(edit.TryAssign((device,"outside","ItemUp"),gamepad?"West":"Digit5",out error),error);var rebound=edit.Apply();foreach(var pair in old)Assert.AreEqual(pair.Value,rebound.Bindings[pair.Key]);Assert.IsTrue(store.TrySave(rebound,out error),error);Assert.IsTrue(store.TryRead(out loaded,out error),error);CollectionAssert.AreEquivalent(rebound.Bindings,loaded.Bindings);}finally{Directory.Delete(dir,true);}
        }
        [TestCase(false,0,false)][TestCase(false,1,false)][TestCase(false,2,false)][TestCase(false,3,false)][TestCase(true,0,false)][TestCase(true,1,false)][TestCase(true,2,false)][TestCase(true,3,false)]
        [TestCase(false,0,true)][TestCase(true,0,true)]
        public void アイテム押下は外界で一回だけ受付し保持とポーズ中を予約せずDungeonの移動を変えない(bool gamepad,int slot,bool rebound)
        {
            var original=UnityInputProvider.ActiveGamepadProfile;var device=gamepad?(UnityEngine.InputSystem.InputDevice)UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>():UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var provider=new UnityInputProvider();string context="outside";
            var key=rebound?UnityEngine.InputSystem.Key.Digit5:new[]{UnityEngine.InputSystem.Key.Digit1,UnityEngine.InputSystem.Key.Digit2,UnityEngine.InputSystem.Key.Digit3,UnityEngine.InputSystem.Key.Digit4}[slot];var button=rebound?UnityEngine.InputSystem.LowLevel.GamepadButton.West:new[]{UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown,UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft}[slot];
            if(rebound){var edit=(gamepad?provider.GamepadProfile:provider.KeyboardProfile).BeginEdit();Assert.IsTrue(edit.TryAssign((gamepad?"gamepad":"keyboard","outside",UnityInputProvider.OutsideItemAction(slot)),gamepad?"West":"Digit5",out var error),error);var profile=edit.Apply();Assert.IsTrue(gamepad?provider.TryRestoreGamepadProfile(profile,out error):provider.TryRestoreKeyboardProfile(profile,out error),error);}
            void Send(bool down){if(device is UnityEngine.InputSystem.Gamepad pad){var state=new UnityEngine.InputSystem.LowLevel.GamepadState();if(down)state=state.WithButton(button);UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);}else UnityEngine.InputSystem.InputSystem.QueueStateEvent((UnityEngine.InputSystem.Keyboard)device,down?new UnityEngine.InputSystem.LowLevel.KeyboardState(key):new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();}
            int Mask()=>provider.CaptureTimestamped().Where(v=>v.Input!=null).Aggregate(0,(v,x)=>v|x.Input.OutsideItemDownMask);
            try{GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();provider.GamepadContext=()=>context;provider.GamepadGameplayActive=()=>true;provider.StartEventCapture();Send(false);provider.CaptureTimestamped();Send(true);Assert.AreEqual(1<<slot,Mask());Send(true);Assert.AreEqual(0,Mask());provider.QueueSnapshot();Assert.AreEqual(0,Mask());Send(false);Mask();Send(true);Assert.AreEqual(1<<slot,Mask());Send(false);Mask();GameplayPause.SetPaused(true);Send(true);Assert.AreEqual(0,Mask());GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();Send(true);Assert.AreEqual(0,Mask());Send(false);Mask();context="battle";Send(true);Assert.AreEqual(0,Mask());}
            finally{provider.Dispose();UnityEngine.InputSystem.InputSystem.RemoveDevice(device);using(var restore=new UnityInputProvider())Assert.IsTrue(restore.TryRestoreGamepadProfile(original,out _));GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();}
        }
        [Test]
        public void 同じ論理frameのアイテム押下を合流し次frameの保持にエッジを残さない()
        {
            var queue=new Olca.Core.Logic.Integration.LogicalInputQueue();queue.Enqueue(1,new Olca.Core.Logic.Integration.LogicalInput{OutsideItemDownMask=1});queue.Enqueue(1,new Olca.Core.Logic.Integration.LogicalInput{OutsideItemDownMask=4});Assert.AreEqual(5,queue.Take(1).OutsideItemDownMask);Assert.AreEqual(0,queue.Take(2).OutsideItemDownMask);
        }
        [Test]
        public void 外界の保存I_O時間は加算せず既得論理frameを保持しDetachで未受理使用を破棄する()
        {
            GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;using(var world=new Olca.Core.Logic.Integration.OutsideExplorationSession())using(var input=new UnityInputProvider())using(var runtime=new Olca.Application.Management.OutsideExplorationRuntime(world)){
                world.TryInitializePlayer(()=>new PlayerCharacter(100));using(var lease=world.AttachPresentation()){
                    var owner=new object();long now=0;int calls=0;runtime.Attach(owner,input,slot=>{calls++;now+=System.Diagnostics.Stopwatch.Frequency*2;});var loop=(UnityLogicalLoop)typeof(Olca.Application.Management.OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);typeof(UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));typeof(UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);loop.StartNewTimeline();runtime.Pump(owner,true);input.CaptureTimestamped();
                    var events=(List<TimestampedLogicalInput>)typeof(UnityInputProvider).GetField("events",flags).GetValue(input);events.Add(new TimestampedLogicalInput(now,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=false}));now+=System.Diagnostics.Stopwatch.Frequency/60+1;runtime.Pump(owner,true);long before=loop.Clock.Frame;Assert.Greater(before,0,"検証時計の起点を明示する");
                    events.Add(new TimestampedLogicalInput(now,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,OutsideItemDownMask=1}));now+=System.Diagnostics.Stopwatch.Frequency*3;runtime.Pump(owner,true);Assert.AreEqual(1,calls);Assert.AreEqual(before+180,loop.Clock.Frame+loop.Clock.PendingFrames);runtime.Pump(owner,true);Assert.AreEqual(1,calls);
                    events.Add(new TimestampedLogicalInput(now,new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,OutsideItemDownMask=1}));runtime.Detach(owner);Assert.IsNull(typeof(Olca.Application.Management.OutsideExplorationRuntime).GetField("itemUse",flags).GetValue(runtime));runtime.Attach(owner,input,slot=>calls++);runtime.Pump(owner,true);Assert.AreEqual(1,calls);
                }
            }
        }
        [TestCase(false)][TestCase(true)] public void 外界復帰地点は同じ保存計画を再試行し再起動読取と旧欄を保全する(bool after)
        {
            string dir=Path.Combine(Path.GetTempPath(),"outside-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var io=new ReplaceFault();var store=new ClearReturnFileStore(dir,io);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var before=File.ReadAllBytes(store.ActivePath);var session=new ClearReturnSession(store);Assert.IsNull(session.Snapshot.OutsideRecoveryPointId);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsFalse(session.TrySaveOutsideRecoveryPoint("unknown"));CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));io.Fail=true;io.After=after;Assert.AreEqual(after,session.TrySaveOutsideRecoveryPoint(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId));if(!after){Assert.IsNull(session.Snapshot.OutsideRecoveryPointId);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsTrue(session.HasPendingSave);}io.Fail=false;Assert.IsTrue(session.Retry(),session.Error);Assert.AreEqual(1,session.Snapshot.Revision);var confirmed=File.ReadAllBytes(store.ActivePath);Assert.IsTrue(session.Retry());CollectionAssert.AreEqual(confirmed,File.ReadAllBytes(store.ActivePath));var loaded=new ClearReturnSession(new ClearReturnFileStore(dir));Assert.AreEqual(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId,loaded.Snapshot.OutsideRecoveryPointId);Assert.AreEqual(99,loaded.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,loaded.Snapshot.Tokens);Assert.AreEqual(3,loaded.OutsideInventorySnapshot.Items[0].Count);CollectionAssert.AreEqual(confirmed,File.ReadAllBytes(store.ActivePath));}finally{Directory.Delete(dir,true);}
        }
        private static Olca.Core.Domain.CheckpointDto Entry135Start(PlayerCharacter player,out Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions)
        {
            var map=DungeonConfig.CreateTestDungeon(6,1,2);definitions=new Olca.Core.Logic.Dungeon.CheckpointDefinitions("entry135",map);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(definitions,new Olca.Core.Logic.Dungeon.MemoryCheckpointStore(definitions));
            var factory=new Olca.Application.Management.DungeonCoreFactory(new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger(),()=>new Olca.Application.Battle.SimpleCardFactory());factory.CreatePreview(map,player,new Deck(),session);return session.Store.Read();
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)] public void 外界入場HPと毒は出撃計画と同時保存し再試行と再起動でも区分を再判定しない(bool retry,bool after)
        {
            string dir=Path.Combine(Path.GetTempPath(),"outside-departure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var io=new ReplaceFault();var store=new ClearReturnFileStore(dir,io);var profile=InventoryProfile();profile.ClearedDungeons=retry?new[]{"entry135"}:Array.Empty<string>();File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(profile));var before=File.ReadAllBytes(store.ActivePath);var progress=new ClearReturnSession(store);var actor=new PlayerCharacter(100);actor.SetHp(37);actor.ApplyPoison("entry",new StatusEffect(StatusEffectType.Poison,60000,1),600);using(var outside=new Olca.Core.Logic.Integration.OutsideExplorationSession()){outside.TryInitializePlayer(()=>actor);using(var lease=outside.AttachPresentation()){for(int i=0;i<7;i++){outside.Core.TickFrame(0);outside.AdvancePlayerFrame();}}}var entry=OutsideDepartureVitals.Capture(actor,retry);actor.ApplyParalysis(17);actor.ApplyConfusion(19);actor.ApplyBlindness(23);entry.TimedAilments=actor.CaptureTimedAilments();entry.Template.Player.TimedAilments=entry.TimedAilments?.Copy();Assert.Less(entry.Poisons[0].NextDamageUnits,entry.Poisons[0].IntervalFrames*50L);var target=new PlayerCharacter(100);entry.ApplyToStart(target);var start=Entry135Start(target,out var defs);Assert.AreNotEqual("entry135",defs.MapHash);string run=start.RunId;io.Fail=true;io.After=after;Assert.AreEqual(after,progress.PrepareNextDeparture(start,defs.MapHash,entry,"entry135"),progress.Error);if(!after){CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsNull(progress.Snapshot.NextDeparture);Assert.IsTrue(progress.HasPendingSave);}entry.Hp=2;entry.Poisons[0].Damage=999;io.Fail=false;Assert.IsTrue(progress.Retry(),progress.Error);var saved=File.ReadAllBytes(store.ActivePath);var loaded=new ClearReturnSession(new ClearReturnFileStore(dir));Assert.IsTrue(loaded.Ready,loaded.Error);var plan=loaded.Snapshot.NextDeparture;Assert.AreEqual(run,plan.Start.RunId);Assert.AreEqual(37,plan.OutsideEntry.Hp);Assert.AreEqual(1,plan.OutsideEntry.Poisons[0].Damage);Assert.AreEqual(retry,plan.OutsideEntry.Retry);Assert.AreEqual(retry?100:37,plan.Start.Player.Hp);Assert.AreEqual(retry?0:1,plan.Start.Player.Poisons.Length);Assert.IsTrue(Olca.Core.Logic.Dungeon.CheckpointCodec.TryRestore(plan.Start,defs,out var restored,out var error),error);Assert.AreEqual(retry?100:37,restored.Player.CurrentHp);Assert.IsTrue(PlayerTimedAilmentsDto.Same(retry?null:plan.OutsideEntry.TimedAilments,restored.Player.CaptureTimedAilments()));Assert.IsTrue(loaded.PrepareNextDeparture(start,defs.MapHash,OutsideDepartureVitals.Capture(new PlayerCharacter(100),!retry),"entry135"));CollectionAssert.AreEqual(saved,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(99,loaded.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,loaded.Snapshot.Tokens);
                if(!retry){var changed=loaded.Snapshot;changed.ClearedDungeons=new[]{"entry135"};File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(changed));var reloaded=new ClearReturnSession(new ClearReturnFileStore(dir));Assert.IsTrue(reloaded.Ready,reloaded.Error);Assert.IsFalse(reloaded.Snapshot.NextDeparture.OutsideEntry.Retry,"保存区分を後のクリア履歴から再計算しない");}
            }finally{Directory.Delete(dir,true);}
        }
        [Test] public void 旧出撃計画は全快制約を保持し新規区分の不一致と毒時計の改変を保存しない()
        {
            var profile=InventoryProfile();var memory=new Olca.Core.Logic.Dungeon.MemoryClearReturnStore(profile);var progress=new ClearReturnSession(memory);var actor=new PlayerCharacter(100);actor.SetHp(37);var entry=OutsideDepartureVitals.Capture(actor,false);var target=new PlayerCharacter(100);entry.ApplyToStart(target);var start=Entry135Start(target,out var defs);Assert.AreNotEqual("entry135",defs.MapHash);Assert.IsFalse(progress.PrepareNextDeparture(start,"entry135"));Assert.IsNull(progress.Snapshot.NextDeparture);entry.Retry=true;Assert.IsFalse(progress.PrepareNextDeparture(start,defs.MapHash,entry,"entry135"));Assert.IsNull(progress.Snapshot.NextDeparture);entry.Retry=false;Assert.IsTrue(progress.PrepareNextDeparture(start,defs.MapHash,entry,"entry135"),progress.Error);var changed=progress.Snapshot;changed.NextDeparture.Start.Player.Hp++;Assert.Throws<ArgumentException>(()=>ClearReturnProfile.Validate(changed));changed=progress.Snapshot;changed.NextDeparture.Start.Player.MaxHp=0;Assert.Throws<ArgumentException>(()=>ClearReturnProfile.Validate(changed));
        }
        private static readonly OutsideItemDisplayDefinition[] InventoryDefinitions={OutsideItemDisplayDefinition.RecoverySample,new OutsideItemDisplayDefinition("outside-second","フタつめ","2")};
        private static ClearReturnProfile InventoryProfile()=>new ClearReturnProfile{ReturnedHp=100,Tokens=3,OutsideInventory=new OutsideInventoryData{Currency=99,Items=new[]{new OutsideItemCount{Id=OutsideItemDisplayDefinition.RecoverySample.Id,Count=3},new OutsideItemCount{Id="outside-second",Count=0},new OutsideItemCount{Id="future-unknown",Count=2}}}};
        [Test] public void 探索所持欄の旧形式は空で読み原本を一切書き換えず明示変更だけ保存する()
        {
            var directory=Path.Combine(Path.GetTempPath(),"olca-inventory-legacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var store=new ClearReturnFileStore(directory);var bytes=Convert.FromBase64String("eyJQYXlsb2FkIjoie1wiQ2hlY2twb2ludEdlbmVyYXRpb25cIjpudWxsLFwiQ2xlYXJlZER1bmdlb25zXCI6W10sXCJDb21taXRJZFwiOm51bGwsXCJDdXN0b21pemF0aW9uXCI6bnVsbCxcIkRpc2NvdmVyZWRDYXJkc1wiOm51bGwsXCJFcXVpcHBlZERlY2tJZFwiOm51bGwsXCJFeGNoYW5nZVJlY2VpcHRzXCI6bnVsbCxcIkV4Y2hhbmdlU3RvY2tcIjpudWxsLFwiRXhjaGFuZ2VUb3RhbHNcIjpudWxsLFwiRXhwZXJpZW5jZVwiOjAsXCJGbG93XCI6bnVsbCxcIkxldmVsXCI6MSxcIk5leHREZXBhcnR1cmVcIjpudWxsLFwiT3duZWRcIjpbXSxcIlJlY2VpcHRzXCI6W10sXCJSZXR1cm5lZEhwXCI6MTAwLFwiUmV2ZWFsZWRSZWNpcGVzXCI6bnVsbCxcIlJldmlzaW9uXCI6MCxcIlNhdmVkRGVja3NcIjpbXSxcIlNlbGVjdGVkU3R5bGVJZFwiOm51bGwsXCJUb2tlbnNcIjozLFwiVmVyc2lvblwiOjF9IiwiU2hhMjU2IjoiMEI2QkFEMTdDMzFGRjNGNUYxQjM2M0VFRjYzMTI0OEFBODhDNzAxN0FDOEREMjU3Qzc4MDg1RTkxRjU2MUM4MCIsIlZlcnNpb24iOjF9");File.WriteAllBytes(store.ActivePath,bytes);var session=new ClearReturnSession(store);Assert.IsTrue(session.Ready,session.Error);Assert.AreEqual(3,session.Snapshot.Tokens);Assert.AreEqual(0,session.OutsideInventorySnapshot.Currency);Assert.IsEmpty(session.OutsideInventorySnapshot.Items);CollectionAssert.AreEqual(new string[4],session.OutsideInventorySnapshot.Shortcuts);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));Assert.IsFalse(session.TryAssignOutsideItem(0,OutsideItemDisplayDefinition.RecoverySample.Id,InventoryDefinitions));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));}
            finally{Directory.Delete(directory,true);}
        }
        [Test] public void 探索4枠は登録交換解除と再読込で所持数と別通貨と既存トークンを変えない()
        {
            var directory=Path.Combine(Path.GetTempPath(),"olca-inventory-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var store=new ClearReturnFileStore(directory);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var session=new ClearReturnSession(store);
                Assert.IsTrue(session.TryAssignOutsideItem(0,InventoryDefinitions[0].Id,InventoryDefinitions),session.Error);long revision=session.InventoryRevision;Assert.IsTrue(session.TryAssignOutsideItem(0,InventoryDefinitions[0].Id,InventoryDefinitions));Assert.AreEqual(revision,session.InventoryRevision);
                Assert.IsTrue(session.TryAssignOutsideItem(1,"outside-second",InventoryDefinitions),session.Error);Assert.IsTrue(session.TryAssignOutsideItem(1,InventoryDefinitions[0].Id,InventoryDefinitions));CollectionAssert.AreEqual(new[]{"outside-second",InventoryDefinitions[0].Id,null,null},session.OutsideInventorySnapshot.Shortcuts);
                Assert.IsTrue(session.TryAssignOutsideItem(1,null,InventoryDefinitions));var bytes=File.ReadAllBytes(store.ActivePath);var loaded=new ClearReturnSession(new ClearReturnFileStore(directory));Assert.IsTrue(loaded.Ready,loaded.Error);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));CollectionAssert.AreEqual(new[]{"outside-second",null,null,null},loaded.OutsideInventorySnapshot.Shortcuts);Assert.AreEqual(99,loaded.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,loaded.Snapshot.Tokens);Assert.AreEqual(3,loaded.OutsideInventorySnapshot.Items[0].Count);Assert.AreEqual(0,loaded.OutsideInventorySnapshot.Items[1].Count);Assert.AreEqual("future-unknown",loaded.OutsideInventorySnapshot.Items[2].Id);
                var copy=loaded.OutsideInventorySnapshot;copy.Currency=0;copy.Items[0].Count=0;copy.Shortcuts[0]=null;Assert.AreEqual(99,loaded.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,loaded.OutsideInventorySnapshot.Items[0].Count);Assert.AreEqual("outside-second",loaded.OutsideInventorySnapshot.Shortcuts[0]);
            }finally{Directory.Delete(directory,true);}
        }
        [TestCase(false)][TestCase(true)] public void 探索登録は保存前後の障害で原子的に確定し同じ再試行を二重適用しない(bool after)
        {
            var directory=Path.Combine(Path.GetTempPath(),"olca-inventory-fault-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var io=new ReplaceFault();var store=new ClearReturnFileStore(directory,io);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var bytes=File.ReadAllBytes(store.ActivePath);var session=new ClearReturnSession(store);io.Fail=true;io.After=after;Assert.AreEqual(after,session.TryAssignOutsideItem(2,InventoryDefinitions[0].Id,InventoryDefinitions));
                if(!after){CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));Assert.IsNull(session.OutsideInventorySnapshot.Shortcuts[2]);Assert.IsTrue(session.HasPendingSave);Assert.IsFalse(session.TryAssignOutsideItem(0,"outside-second",InventoryDefinitions));}
                io.Fail=false;Assert.IsTrue(session.Retry(),session.Error);Assert.AreEqual(InventoryDefinitions[0].Id,session.OutsideInventorySnapshot.Shortcuts[2]);Assert.AreEqual(1,session.InventoryRevision);var confirmed=File.ReadAllBytes(store.ActivePath);Assert.IsTrue(session.Retry());CollectionAssert.AreEqual(confirmed,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(3,session.OutsideInventorySnapshot.Items[0].Count);Assert.IsEmpty(Directory.GetFiles(directory,"*.tmp"));
            }finally{Directory.Delete(directory,true);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)] public void 探索所持欄の不正データは拒否し未知IDの既存所持と登録を保持する(int invalid)
        {
            var profile=InventoryProfile();profile.OutsideInventory.Shortcuts[3]="future-unknown";ClearReturnProfile.Validate(profile);var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(profile);var loaded=Olca.Core.Logic.Dungeon.PortableCheckpointJson.DecodeProgress(bytes);Assert.AreEqual("future-unknown",loaded.OutsideInventory.Shortcuts[3]);
            if(invalid==0)profile.OutsideInventory.Currency=-1;if(invalid==1)profile.OutsideInventory.Items[0].Count=-1;if(invalid==2)profile.OutsideInventory.Shortcuts[0]="future-unknown";if(invalid==3)profile.OutsideInventory.Shortcuts[0]="missing";Assert.Throws<ArgumentException>(()=>ClearReturnProfile.Validate(profile));
        }
        [TestCase(false)][TestCase(true)] public void PCホイールの旧設定補完は旧割当と原本を守りTab競合時だけ新slotを空にする(bool collision)
        {
            var current=UnityInputProvider.CreateDefaultKeyboardProfile();var wheel=("keyboard","outside","WheelPrimary");Assert.AreEqual("Tab",current.Bindings[wheel]);
            var old=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var p in current.Bindings)if(p.Key!=wheel&&(p.Key.Context!="outside"||!p.Key.Action.StartsWith("Item",StringComparison.Ordinal)))old.Add(p.Key,p.Value);
            if(collision)old[("keyboard","exploration","DashSecondary")]="Tab";
            var before=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(old);var loaded=UnityInputProvider.DecodeSelectionSlots(old,false);
            CollectionAssert.AreEquivalent(before,old);foreach(var p in before)Assert.AreEqual(p.Value,loaded.Bindings[p.Key]);Assert.AreEqual(collision?null:"Tab",loaded.Bindings[wheel]);
            string directory=Path.Combine(Path.GetTempPath(),"olca-pc-wheel-"+Guid.NewGuid().ToString("N"));
            try{
                Directory.CreateDirectory(directory);var store=new KeyboardBindingFileStore(directory);var rows=new System.Collections.Generic.List<string>();foreach(var p in old)rows.Add("{\"Context\":\""+p.Key.Context+"\",\"Action\":\""+p.Key.Action+"\",\"Key\":"+(p.Value==null?"null":"\""+p.Value+"\"")+"}");
                File.WriteAllText(store.ActivePath,"{\"Version\":1,\"Device\":\"keyboard\",\"Slots\":["+string.Join(",",rows)+"]}");var bytes=File.ReadAllBytes(store.ActivePath);
                Assert.IsTrue(store.TryRead(out var read,out var error),error);CollectionAssert.AreEquivalent(loaded.Bindings,read.Bindings);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));
                Assert.IsTrue(store.TrySave(read,out error),error);Assert.IsTrue(store.TryRead(out var again,out error),error);CollectionAssert.AreEquivalent(read.Bindings,again.Bindings);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Path.Combine(directory,"keyboard-gameplay.backup.json")));
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
            var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(loaded.Bindings);partial.Remove(("keyboard","exploration","SelectionNext"));Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,false));
            partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(old);partial.Add(("keyboard","outside","Unknown"),null);Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,false));
        }
        [TestCase("W")][TestCase("A")][TestCase("S")][TestCase("D")][TestCase("Enter")][TestCase("NumpadEnter")][TestCase("Escape")][TestCase("Space")]
        public void PCホイールだけ固定modalキーを拒否し既存割当へ制限を遡及しない(string key)
        {
            var current=UnityInputProvider.CreateDefaultKeyboardProfile();var slots=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(current.Bindings);var wheel=("keyboard","outside","WheelPrimary");slots[wheel]=key;
            var candidate=new InputBindingProfile(slots,new (string,string,string)[0]);Assert.IsFalse(UnityInputProvider.ValidateKeyboardProfile(candidate,out _));
            slots[wheel]=null;slots[("keyboard","exploration","JumpPrimary")]=key;candidate=new InputBindingProfile(slots,new (string,string,string)[0]);Assert.IsTrue(UnityInputProvider.ValidateKeyboardProfile(candidate,out var error),error);
        }
        [Test] public void PCホイールrawは文脈初期保持を捨てTab再押下と方向解放とpointer操作順を一度ずつ読む()
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("PcWheelRaw");var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("PcWheelMouse");
            using(var provider=new UnityInputProvider())try{
                provider.GamepadContext=()=>"outside";provider.GamepadGameplayActive=()=>true;provider.StartEventCapture();var context=new object();
                void Keys(params UnityEngine.InputSystem.Key[] keys){UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));UnityEngine.InputSystem.InputSystem.Update();}
                void Pointer(float x,float y,bool click=false){var state=new UnityEngine.InputSystem.LowLevel.MouseState{position=new UnityEngine.Vector2(x,y)};if(click)state=state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,state);UnityEngine.InputSystem.InputSystem.Update();}
                Keys(UnityEngine.InputSystem.Key.Tab);Assert.IsFalse(provider.ReadOutsidePcWheel(context,-1,out _).ToggleDown);Keys();Assert.IsFalse(provider.ReadOutsidePcWheel(context,-1,out _).ToggleDown);
                Keys(UnityEngine.InputSystem.Key.Tab,UnityEngine.InputSystem.Key.W);var value=provider.ReadOutsidePcWheel(context,-1,out var owner);Assert.IsTrue(value.ToggleDown);Assert.AreEqual(keyboard.deviceId,owner);Assert.AreEqual(UnityEngine.Vector2.up,value.Direction);Assert.IsFalse(provider.ReadOutsidePcWheel(context,owner,out _).ToggleDown);
                Keys(UnityEngine.InputSystem.Key.W);value=provider.ReadOutsidePcWheel(context,owner,out _);Assert.IsFalse(value.ToggleDown,"Tab解放は閉じる入力でない");
                Pointer(80,90);Keys();value=provider.ReadOutsidePcWheel(context,owner,out _);Assert.IsTrue(value.KeyboardChanged&&value.PointerChanged);Assert.Greater(value.KeyboardOrder,value.PointerOrder);Assert.AreEqual(UnityEngine.Vector2.zero,value.Direction);
                Keys(UnityEngine.InputSystem.Key.D);Pointer(90,90,true);value=provider.ReadOutsidePcWheel(context,owner,out _);Assert.IsTrue(value.ClickDown);Assert.Greater(value.PointerOrder,value.KeyboardOrder);Assert.IsFalse(provider.ReadOutsidePcWheel(context,owner,out _).ClickDown);
                Keys(UnityEngine.InputSystem.Key.Enter);provider.QueueSnapshot();value=provider.ReadOutsidePcWheel(context,owner,out _);Assert.IsFalse(value.EnterDown||value.ToggleDown||value.ClickDown);Assert.IsTrue(value.EnterHeld);
            }finally{UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}
        }
        [TestCase(false)] [TestCase(true)] public void 外界選択slotの旧設定補完は既存割当を守り衝突時だけ新規を未割当にする(bool gamepad)
        {
            var original=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();
            var slots=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();
            foreach(var pair in original.Bindings)if(pair.Key.Context!="outside"&&pair.Key.Action!="SelectionPrevious"&&pair.Key.Action!="SelectionNext")slots.Add(pair.Key,pair.Value);
            string device=gamepad?"gamepad":"keyboard";slots[(device,"exploration","JumpPrimary")]=gamepad?"LeftTrigger":"Q";
            var before=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(slots);
            var upgraded=UnityInputProvider.DecodeSelectionSlots(slots,gamepad);
            foreach(var pair in before)Assert.AreEqual(pair.Value,upgraded.Bindings[pair.Key]);
            Assert.IsNull(upgraded.Bindings[(device,"exploration","SelectionPrevious")]);Assert.AreEqual(gamepad?"RightTrigger":"E",upgraded.Bindings[(device,"exploration","SelectionNext")]);CollectionAssert.AreEquivalent(before,slots);
            var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(slots);partial.Remove((device,"exploration","JumpPrimary"));Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,gamepad));
            partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(slots);partial.Add((device,"exploration","Unknown"),null);Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,gamepad));
        }
        [Test] public void 外界巡回と水平操作の交換は一相手のみ原子的に行い旧複数共有を解除しない()
        {
            var original=UnityInputProvider.CreateDefaultKeyboardProfile();var slots=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var pair in original.Bindings)slots.Add(pair.Key,pair.Value);
            var previous=("keyboard","exploration","SelectionPrevious");var left=("keyboard","battle","LeftSecondary");var jump=("keyboard","exploration","JumpPrimary");
            var edit=original.BeginEdit();Assert.IsTrue(edit.TryAssign(previous,"A",out _));Assert.AreEqual("Q",edit.Draft[left]);Assert.AreEqual("A",edit.Draft[previous]);
            slots[jump]="A";slots[previous]=null;var shared=UnityInputProvider.DecodeSelectionSlots(slots,false);edit=shared.BeginEdit();Assert.IsFalse(edit.TryAssign(previous,"A",out _));Assert.IsNull(edit.Draft[previous]);Assert.AreEqual("A",edit.Draft[left]);Assert.AreEqual("A",edit.Draft[jump]);
            var with=shared.BeginEdit().Preview();Assert.IsTrue(with.Conflicts(previous,left));Assert.IsFalse(with.Conflicts(jump,left));
        }
        [TestCase(false)] [TestCase(true)] public void 旧Version一設定は読取で書換えず明示保存だけ新slotと旧backupを確定する(bool gamepad)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-selection-migration-"+Guid.NewGuid().ToString("N"));
            try{
                Directory.CreateDirectory(directory);var store=gamepad?(GameplayBindingFileStore)new GamepadBindingFileStore(directory):new KeyboardBindingFileStore(directory);var original=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();string device=gamepad?"gamepad":"keyboard";
                var rows=new System.Collections.Generic.List<string>();foreach(var pair in original.Bindings)if(pair.Key.Context!="outside"&&pair.Key.Action!="SelectionPrevious"&&pair.Key.Action!="SelectionNext")rows.Add("{\"Context\":\""+pair.Key.Context+"\",\"Action\":\""+pair.Key.Action+"\",\"Key\":"+(pair.Value==null?"null":"\""+pair.Value+"\"")+"}");
                File.WriteAllText(store.ActivePath,"{\"Version\":1,\"Device\":\""+device+"\",\"Slots\":["+string.Join(",",rows)+"]}");var before=File.ReadAllBytes(store.ActivePath);
                Assert.IsTrue(store.TryRead(out var upgraded,out var error),error);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsFalse(File.Exists(Path.Combine(directory,device+"-gameplay.backup.json")));
                Assert.IsTrue(store.TrySave(upgraded,out error),error);CollectionAssert.AreEqual(before,File.ReadAllBytes(Path.Combine(directory,device+"-gameplay.backup.json")));Assert.IsTrue(store.TryRead(out var roundtrip,out error),error);CollectionAssert.AreEquivalent(upgraded.Bindings,roundtrip.Bindings);
                var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var pair in upgraded.Bindings)if(pair.Key.Action!="SelectionNext")partial.Add(pair.Key,pair.Value);Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,gamepad));
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase("E",false)] [TestCase("E",true)] [TestCase("LeftShift",false)] [TestCase("LeftShift",true)] public void 外界選択neutralは合成anyKeyを除き別の実キーは除外しない(string binding,bool otherKey)
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("SelectionNeutralTest");
            using(var provider=new UnityInputProvider())try{
                var edit=provider.KeyboardProfile.BeginEdit();Assert.IsTrue(edit.TryAssign(("keyboard","exploration","SelectionNext"),binding,out _));Assert.IsTrue(provider.TryRestoreKeyboardProfile(edit.Preview(),out _));provider.StartEventCapture();var key=System.Enum.Parse<UnityEngine.InputSystem.Key>(binding);var other=binding=="LeftShift"?UnityEngine.InputSystem.Key.RightShift:UnityEngine.InputSystem.Key.F12;UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(otherKey?new[]{key,other}:new[]{key}));UnityEngine.InputSystem.InputSystem.Update();
                provider.QueueSnapshot();var samples=provider.CaptureTimestamped();var value=samples[samples.Length-1].Input;Assert.IsTrue(value.AnyKey,"従来globalAnyKeyは保持");Assert.IsTrue(value.SelectionNextHeld);Assert.AreEqual(otherKey,value.AnyKeyExceptSelection,"外界専用neutralだけ合成anyKeyを除外");
            }finally{UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);}
        }
        [TestCase("LeftStick")][TestCase("RightShoulder")][TestCase(null)]
        public void 外界Pad新文脈は旧設定の明示割当と未割当をコピーし旧場面を交換しない(string savedDash)
        {
            var current=UnityInputProvider.CreateDefaultGamepadProfile();
            Assert.AreEqual("RightShoulder",current.Bindings[("gamepad","outside","DashPrimary")]);
            var old=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();
            foreach(var p in current.Bindings)if(p.Key.Context!="outside")old.Add(p.Key,p.Value);
            old[("gamepad","exploration","DashPrimary")]=savedDash;
            var before=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(old);
            var migrated=UnityInputProvider.DecodeSelectionSlots(old,true);
            CollectionAssert.AreEquivalent(before,old);
            foreach(var p in before)Assert.AreEqual(p.Value,migrated.Bindings[p.Key]);
            Assert.AreEqual(savedDash,migrated.Bindings[("gamepad","outside","DashPrimary")]);
            string directory=Path.Combine(Path.GetTempPath(),"olca-outside-context-"+Guid.NewGuid().ToString("N"));
            try{
                Directory.CreateDirectory(directory);var store=new GamepadBindingFileStore(directory);var rows=new System.Collections.Generic.List<string>();
                foreach(var p in old)rows.Add("{\"Context\":\""+p.Key.Context+"\",\"Action\":\""+p.Key.Action+"\",\"Key\":"+(p.Value==null?"null":"\""+p.Value+"\"")+"}");
                File.WriteAllText(store.ActivePath,"{\"Version\":1,\"Device\":\"gamepad\",\"Slots\":["+string.Join(",",rows)+"]}");var bytes=File.ReadAllBytes(store.ActivePath);
                Assert.IsTrue(store.TryRead(out var loaded,out var storeError),storeError);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));CollectionAssert.AreEquivalent(migrated.Bindings,loaded.Bindings);
                Assert.IsTrue(store.TrySave(loaded,out storeError),storeError);Assert.IsTrue(store.TryRead(out var reloaded,out storeError),storeError);CollectionAssert.AreEquivalent(loaded.Bindings,reloaded.Bindings);
                CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Path.Combine(directory,"gamepad-gameplay.backup.json")));
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
            var edit=migrated.BeginEdit();Assert.IsTrue(edit.TryAssign(("gamepad","outside","DashPrimary"),"South",out var error),error);
            Assert.AreEqual(savedDash,edit.Draft[("gamepad","outside","JumpPrimary")]);
            Assert.AreEqual("South",edit.Draft[("gamepad","exploration","JumpPrimary")]);
            Assert.AreEqual(savedDash,edit.Draft[("gamepad","exploration","DashPrimary")]);
            Assert.AreEqual("RightShoulder",edit.Draft[("gamepad","battle","CardPrimary")]);
            var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(migrated.Bindings);partial.Remove(("gamepad","outside","Pause"));
            Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,true));
            partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(migrated.Bindings);partial.Remove(("gamepad","exploration","SelectionPrevious"));partial.Remove(("gamepad","exploration","SelectionNext"));
            Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,true));
        }
        [Test] public void 外界PadはRBダッシュと左stick移動だけを使い旧DungeonのL3と十字移動を保持する()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            using(var provider=new UnityInputProvider())try{
                string context="outside";provider.GamepadContext=()=>context;provider.StartEventCapture();provider.CaptureTimestamped();
                void State(UnityEngine.InputSystem.LowLevel.GamepadState state){UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,state);UnityEngine.InputSystem.InputSystem.Update();}
                State(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder));
                var samples=provider.CaptureTimestamped();var input=samples[samples.Length-1].Input;Assert.IsTrue(input.DashButton&&input.DashButtonDown);Assert.AreEqual(0,input.Horizontal);
                State(new UnityEngine.InputSystem.LowLevel.GamepadState());provider.CaptureTimestamped();
                State(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight).WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftStick));
                samples=provider.CaptureTimestamped();input=samples[samples.Length-1].Input;Assert.AreEqual(0,input.Horizontal);Assert.IsFalse(input.DashButton||input.DashButtonDown);
                State(new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=UnityEngine.Vector2.right});samples=provider.CaptureTimestamped();Assert.AreEqual(1,samples[samples.Length-1].Input.Horizontal);
                State(new UnityEngine.InputSystem.LowLevel.GamepadState());provider.CaptureTimestamped();context="exploration";
                State(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight).WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftStick));samples=provider.CaptureTimestamped();input=samples[samples.Length-1].Input;Assert.AreEqual(1,input.Horizontal);Assert.IsTrue(input.DashButtonDown);
            }finally{UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [TestCase(false)][TestCase(true)] public void 外界ホイールslot追加は既存LB割当を奪わず完全な旧形式だけ補完する(bool collision)
        {
            var current=UnityInputProvider.CreateDefaultGamepadProfile();var wheel=("gamepad","outside","WheelPrimary");Assert.AreEqual("LeftShoulder",current.Bindings[wheel]);
            var old=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var p in current.Bindings)if(p.Key!=wheel&&(p.Key.Context!="outside"||!p.Key.Action.StartsWith("Item",StringComparison.Ordinal)))old.Add(p.Key,p.Value);
            if(collision)old[("gamepad","outside","DashPrimary")]="LeftShoulder";
            var before=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(old);var loaded=UnityInputProvider.DecodeSelectionSlots(old,true);
            foreach(var p in before)Assert.AreEqual(p.Value,loaded.Bindings[p.Key]);CollectionAssert.AreEquivalent(before,old);Assert.AreEqual(collision?null:"LeftShoulder",loaded.Bindings[wheel]);
            var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(loaded.Bindings);partial.Remove(("gamepad","outside","JumpPrimary"));Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,true));
            var edit=current.BeginEdit();Assert.IsFalse(edit.TryAssign(wheel,"South",out _),"JumpとDecideの複数競合を原子的に拒否");CollectionAssert.AreEquivalent(current.Bindings,edit.Draft);
            Assert.IsTrue(edit.TryAssign(wheel,"RightShoulder",out var error),error);Assert.AreEqual("LeftShoulder",edit.Draft[("gamepad","outside","DashPrimary")]);Assert.AreEqual("RightShoulder",edit.Draft[("gamepad","battle","CardPrimary")]);
            edit=current.BeginEdit();Assert.IsFalse(edit.TryAssign(("gamepad","menu","Decide"),"LeftShoulder",out _));CollectionAssert.AreEquivalent(current.Bindings,edit.Draft);
            var menu=UnityInputProvider.CreateDefaultGamepadProfile().BeginEdit();Assert.IsTrue(menu.TryAssign(("gamepad","menu","Decide"),"RightStick",out _));var customized=menu.Draft.Where(p=>p.Key.Context!="outside"||!p.Key.Action.StartsWith("Item",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value);customized.Remove(wheel);customized[("gamepad","menu","Decide")]="LeftShoulder";customized[("gamepad","menu","PageUp")]="RightStick";
            loaded=UnityInputProvider.DecodeSelectionSlots(customized,true);Assert.IsNull(loaded.Bindings[wheel]);Assert.AreEqual("LeftShoulder",loaded.Bindings[("gamepad","menu","Decide")]);
        }
        [Test] public void 神依ホイールraw入力は開始Padを固定し停止snapshot中も解放と確定を読みBで確定しない()
        {
            var a=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var b=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            using(var input=new UnityInputProvider())try{
                input.GamepadContext=()=>"outside";input.StartEventCapture();input.CaptureTimestamped();
                void State(UnityEngine.InputSystem.Gamepad pad,UnityEngine.InputSystem.LowLevel.GamepadState value){UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,value);UnityEngine.InputSystem.InputSystem.Update();}
                State(a,new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=UnityEngine.Vector2.right}.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder));
                Assert.IsTrue(input.ReadOutsideWheel(-1,out var id,out var held,out var decide,out var direction));Assert.AreEqual(a.deviceId,id);Assert.IsTrue(held);Assert.IsFalse(decide);Assert.AreEqual(UnityEngine.Vector2.right,direction);
                input.QueueSnapshot();Assert.IsFalse(input.ReadOutsideWheel(-1,out _,out _,out _,out _),"blockedな保持から新しく開かない");Assert.IsTrue(input.ReadOutsideWheel(id,out _,out held,out decide,out direction));Assert.IsTrue(held);Assert.AreEqual(UnityEngine.Vector2.right,direction);
                State(b,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));Assert.IsTrue(input.ReadOutsideWheel(id,out _,out held,out decide,out _));Assert.IsFalse(decide,"別PadのAを混ぜない");
                State(a,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder).WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.East));input.ReadOutsideWheel(id,out _,out held,out decide,out _);Assert.IsTrue(held);Assert.IsFalse(decide);
                State(a,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder).WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));input.ReadOutsideWheel(id,out _,out held,out decide,out _);Assert.IsTrue(decide);
                State(a,new UnityEngine.InputSystem.LowLevel.GamepadState());input.ReadOutsideWheel(id,out _,out held,out decide,out _);Assert.IsFalse(held);Assert.IsFalse(decide);
                State(a,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder));Assert.IsTrue(input.ReadOutsideWheel(-1,out var fresh,out held,out _,out _));Assert.AreEqual(id,fresh);Assert.IsTrue(held);
            }finally{UnityEngine.InputSystem.InputSystem.RemoveDevice(a);UnityEngine.InputSystem.InputSystem.RemoveDevice(b);}
        }
        private sealed class ReplaceFault : CheckpointFileIo
        {
            public bool Fail, After;
            public override void Replace(string source, string active, string backup)
            {
                if (Fail && !After) throw new IOException("before replacement");
                base.Replace(source, active, backup);
                if (Fail && After) throw new IOException("after replacement");
            }
        }

        [TestCase("keyboard", false)] [TestCase("keyboard", true)]
        [TestCase("form", false)] [TestCase("form", true)]
        [TestCase("audio", false)] [TestCase("audio", true)]
        [TestCase("dialogue", false)] [TestCase("dialogue", true)]
        [TestCase("vibration", false)] [TestCase("vibration", true)]
        public void 設定の置換前失敗は旧内容を保ち置換後失敗は確定内容を認識して再試行できる(string kind, bool after)
        {
            var directory = Path.Combine(Path.GetTempPath(), "olca-settings-refactor-" + Guid.NewGuid().ToString("N"));
            try
            {
                var io = new ReplaceFault();
                string active = null;
                Func<bool, bool> save = null;
                switch (kind)
                {
                    case "keyboard":
                        var keyboard = new KeyboardBindingFileStore(directory, io);
                        var original = UnityInputProvider.CreateDefaultKeyboardProfile();
                        var edit = original.BeginEdit();
                        Assert.IsTrue(edit.TryAssign(("keyboard", "exploration", "JumpPrimary"), "F12", out _));
                        var changed = edit.Preview();
                        active = keyboard.ActivePath; save = next => keyboard.TrySave(next ? changed : original, out _); break;
                    case "form":
                        var form = new TransformationPresentationFileStore(directory, io);
                        active = form.ActivePath; save = next => form.TrySave(next, out _); break;
                    case "audio":
                        var audio = new AudioVolumeFileStore(directory, io);
                        active = audio.ActivePath; save = next => audio.TrySave(new AudioVolumeSettings(next ? 40 : 80), out _); break;
                    case "dialogue":
                        var dialogue = new DialogueTextSettingsStore(directory, io);
                        active = dialogue.ActivePath; save = next => dialogue.TrySave(next ? DialogueTextSpeed.Fast : DialogueTextSpeed.Normal, out _); break;
                    case "vibration":
                        var vibration = new GamepadVibrationSettingsStore(directory, io);
                        active = vibration.ActivePath; save = next => vibration.TrySave(next, out _); break;
                }
                Assert.IsTrue(save(false));
                var before = File.ReadAllBytes(active);
                io.Fail = true; io.After = after;
                Assert.AreEqual(after, save(true));
                if (after) CollectionAssert.AreNotEqual(before, File.ReadAllBytes(active));
                else CollectionAssert.AreEqual(before, File.ReadAllBytes(active));
                Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
                io.Fail = false;
                Assert.IsTrue(save(true));
                var confirmed = File.ReadAllBytes(active);
                CollectionAssert.AreNotEqual(before, confirmed);
                Assert.IsTrue(save(true));
                CollectionAssert.AreEqual(confirmed, File.ReadAllBytes(active));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [TestCase(0,3)][TestCase(98,1)][TestCase(99,0)][TestCase(120,0)]
        public void 仮補給箱は上限で未開封と部分受取を区別し再読込で二重取得しない(int owned,int received)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-supply-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var store=new ClearReturnFileStore(directory);var profile=InventoryProfile();profile.OutsideInventory.Items[0].Count=owned;profile.OutsideInventory.ClaimedSources=new[]{"future-source"};File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(profile));var before=File.ReadAllBytes(store.ActivePath);var session=new ClearReturnSession(store);
                Assert.AreEqual(received>0,session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out var amount,out var error),error);Assert.AreEqual(received,amount);Assert.IsFalse(session.HasPendingSave);Assert.AreEqual(99,session.OutsideInventorySnapshot.Currency);Assert.AreEqual(2,session.OutsideInventorySnapshot.Items.Single(v=>v.Id=="future-unknown").Count);CollectionAssert.AreEqual(profile.OutsideInventory.Shortcuts,session.OutsideInventorySnapshot.Shortcuts);
                if(received==0){CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));CollectionAssert.AreEqual(new[]{"future-source"},session.OutsideInventorySnapshot.ClaimedSources);}else{Assert.AreEqual(owned+received,session.OutsideInventorySnapshot.Items[0].Count);var bytes=File.ReadAllBytes(store.ActivePath);var cold=new ClearReturnSession(new ClearReturnFileStore(directory));Assert.IsFalse(cold.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out _,out _));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));CollectionAssert.AreEqual(new[]{"future-source",OutsideSupplyDefinition.Provisional.Id},cold.OutsideInventorySnapshot.ClaimedSources);}
            }finally{Directory.Delete(directory,true);}
        }
        [TestCase(false)][TestCase(true)]
        public void 補給保存は失敗なら数量開封とも未確定で置換後例外なら一度だけ確定(bool after)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-supply-fault-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var io=new ReplaceFault();var store=new ClearReturnFileStore(directory,io);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var bytes=File.ReadAllBytes(store.ActivePath);var session=new ClearReturnSession(store);io.Fail=true;io.After=after;Assert.AreEqual(after,session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out var amount,out _));Assert.AreEqual(after?3:0,amount);Assert.IsFalse(session.HasPendingSave);if(!after){CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));Assert.IsEmpty(session.OutsideInventorySnapshot.ClaimedSources);Assert.IsNull(session.Error);io.Fail=false;Assert.IsTrue(session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out _,out _));}io.Fail=false;var done=File.ReadAllBytes(store.ActivePath);Assert.AreEqual(6,session.OutsideInventorySnapshot.Items[0].Count);Assert.IsFalse(session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out _,out _));CollectionAssert.AreEqual(done,File.ReadAllBytes(store.ActivePath));}
            finally{Directory.Delete(directory,true);}
        }
        [TestCase(0)][TestCase(1)] public void 補給開封記録は空白と重複を拒否する(int invalid){var p=InventoryProfile();p.OutsideInventory.ClaimedSources=invalid==0?new[]{" "}:new[]{"same","same"};Assert.Throws<ArgumentException>(()=>ClearReturnProfile.Validate(p));}

        [System.Runtime.Serialization.DataContract] private sealed class LegacySupplyEnvelope
        {
            [System.Runtime.Serialization.DataMember] public int Version;
            [System.Runtime.Serialization.DataMember] public string Payload;
            [System.Runtime.Serialization.DataMember] public string Sha256;
        }
        [TestCase(false)][TestCase(true)] public void 開封欄のない旧所持保存と旧null欄はchecksumを原文検証し読取で書き換えない(bool explicitNull)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-supply-legacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var profile=InventoryProfile();profile.OutsideInventory.ClaimedSources=explicitNull?null:Array.Empty<string>();var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(profile);var payload=System.Text.Encoding.UTF8.GetString(bytes);if(!explicitNull){StringAssert.Contains("\"ClaimedSources\":[],",payload);payload=payload.Replace("\"ClaimedSources\":[],","");}var raw=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(new LegacySupplyEnvelope{Version=1,Payload=payload,Sha256=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(System.Text.Encoding.UTF8.GetBytes(payload))});var store=new ClearReturnFileStore(directory);File.WriteAllBytes(store.ActivePath,raw);var session=new ClearReturnSession(store);Assert.IsTrue(session.Ready,session.Error);Assert.IsFalse(session.HasClaimedOutsideSupply(OutsideSupplyDefinition.Provisional.Id));CollectionAssert.AreEqual(raw,File.ReadAllBytes(store.ActivePath));Assert.IsTrue(session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out var amount,out var error),error);Assert.AreEqual(3,amount);Assert.AreEqual(6,session.OutsideInventorySnapshot.Items[0].Count);Assert.AreEqual(99,session.OutsideInventorySnapshot.Currency);}
            finally{Directory.Delete(directory,true);}
        }
        [Test] public void 補給は旧null所持欄だけを初期化しトークンやHPと登録枠を変えない()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-supply-null-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try{var p=InventoryProfile();p.OutsideInventory=null;var store=new ClearReturnFileStore(directory);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(p));var session=new ClearReturnSession(store);Assert.IsTrue(session.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out var amount,out var error),error);Assert.AreEqual(3,amount);Assert.AreEqual(3,session.Snapshot.Tokens);Assert.AreEqual(100,session.Snapshot.ReturnedHp);Assert.AreEqual(0,session.OutsideInventorySnapshot.Currency);CollectionAssert.AreEqual(new string[4],session.OutsideInventorySnapshot.Shortcuts);Assert.AreEqual(3,session.OutsideInventorySnapshot.Items.Single().Count);}
            finally{Directory.Delete(directory,true);}
        }

        [Test] public void 毒消箱はHP箱と別に取得し両方の数量と開封を保持する()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-poison-box-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);try{var store=new ClearReturnFileStore(directory);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var save=new ClearReturnSession(store);Assert.IsTrue(save.TryClaimOutsideSupply(OutsideSupplyDefinition.Provisional,out var hp,out _));Assert.AreEqual(3,hp);Assert.IsTrue(save.TryClaimOutsideSupply(OutsideSupplyDefinition.PoisonProvisional,out var poison,out _));Assert.AreEqual(2,poison);var cold=new ClearReturnSession(new ClearReturnFileStore(directory));Assert.AreEqual(6,cold.OutsideInventorySnapshot.Items.Single(v=>v.Id==OutsideRecoveryItemDefinition.Provisional.Id).Count);Assert.AreEqual(2,cold.OutsideInventorySnapshot.Items.Single(v=>v.Id==OutsideRecoveryItemDefinition.PoisonProvisional.Id).Count);Assert.IsTrue(cold.HasClaimedOutsideSupply(OutsideSupplyDefinition.Provisional.Id));Assert.IsTrue(cold.HasClaimedOutsideSupply(OutsideSupplyDefinition.PoisonProvisional.Id));Assert.AreEqual(99,cold.OutsideInventorySnapshot.Currency);}
            finally{Directory.Delete(directory,true);}
        }
        [Test] public void 仮地点の利用範囲は歩行で到達でき回復二箱入口の候補は重複しない()
        {
            var core=new Olca.Core.Logic.Integration.OutsideExplorationCore(Olca.Core.Logic.Integration.OutsideMovementDefinition.ValidationLandingTerrain);for(int frame=0;frame<50;frame++)core.TickFrame(-1);bool heal=false,poison=false,entrance=false,recovery=false;
            for(int frame=0;frame<320;frame++){bool h=Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimSupply(core),p=Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimPoisonSupply(core),e=Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core),r=Olca.Core.Logic.Integration.OutsideTrialRoute.CanRecover(core);Assert.LessOrEqual((h?1:0)+(p?1:0)+(e?1:0)+(r?1:0),1);heal|=h;poison|=p;entrance|=e;recovery|=r;core.TickFrame(1);}
            Assert.IsTrue(heal&&poison&&entrance&&recovery);
        }

        [Test] public void 試用毒地点は既存の回復補給入口と重ならず通常歩行で到達する()
        {
            var core=new Olca.Core.Logic.Integration.OutsideExplorationCore(Olca.Core.Logic.Integration.OutsideMovementDefinition.ValidationLandingTerrain);for(int n=0;n<70;n++)core.TickFrame(-1);bool visited=false;for(int n=0;n<280;n++){bool poison=Olca.Core.Logic.Integration.OutsideTrialRoute.CanTryPoison(core);visited|=poison;int total=(poison?1:0)+(Olca.Core.Logic.Integration.OutsideTrialRoute.CanRecover(core)?1:0)+(Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimSupply(core)?1:0)+(Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimPoisonSupply(core)?1:0)+(Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core)?1:0);Assert.LessOrEqual(total,1);core.TickFrame(1);}Assert.IsTrue(visited);
        }

    }
}
