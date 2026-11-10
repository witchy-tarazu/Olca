using System;
using System.IO;
using NUnit.Framework;
using Olca.Unity;
using Olca.Core.Domain;

namespace Olca.Tests
{
    public class SettingsWriteRefactorTests
    {
        [TestCase(false)][TestCase(true)] public void 外界復帰地点は同じ保存計画を再試行し再起動読取と旧欄を保全する(bool after)
        {
            string dir=Path.Combine(Path.GetTempPath(),"outside-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var io=new ReplaceFault();var store=new ClearReturnFileStore(dir,io);File.WriteAllBytes(store.ActivePath,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(InventoryProfile()));var before=File.ReadAllBytes(store.ActivePath);var session=new ClearReturnSession(store);Assert.IsNull(session.Snapshot.OutsideRecoveryPointId);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsFalse(session.TrySaveOutsideRecoveryPoint("unknown"));CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));io.Fail=true;io.After=after;Assert.AreEqual(after,session.TrySaveOutsideRecoveryPoint(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId));if(!after){Assert.IsNull(session.Snapshot.OutsideRecoveryPointId);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.IsTrue(session.HasPendingSave);}io.Fail=false;Assert.IsTrue(session.Retry(),session.Error);Assert.AreEqual(1,session.Snapshot.Revision);var confirmed=File.ReadAllBytes(store.ActivePath);Assert.IsTrue(session.Retry());CollectionAssert.AreEqual(confirmed,File.ReadAllBytes(store.ActivePath));var loaded=new ClearReturnSession(new ClearReturnFileStore(dir));Assert.AreEqual(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryPointId,loaded.Snapshot.OutsideRecoveryPointId);Assert.AreEqual(99,loaded.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,loaded.Snapshot.Tokens);Assert.AreEqual(3,loaded.OutsideInventorySnapshot.Items[0].Count);CollectionAssert.AreEqual(confirmed,File.ReadAllBytes(store.ActivePath));}finally{Directory.Delete(dir,true);}
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
            var old=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var p in current.Bindings)if(p.Key!=wheel)old.Add(p.Key,p.Value);
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
            var old=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>();foreach(var p in current.Bindings)if(p.Key!=wheel)old.Add(p.Key,p.Value);
            if(collision)old[("gamepad","outside","DashPrimary")]="LeftShoulder";
            var before=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(old);var loaded=UnityInputProvider.DecodeSelectionSlots(old,true);
            foreach(var p in before)Assert.AreEqual(p.Value,loaded.Bindings[p.Key]);CollectionAssert.AreEquivalent(before,old);Assert.AreEqual(collision?null:"LeftShoulder",loaded.Bindings[wheel]);
            var partial=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(loaded.Bindings);partial.Remove(("gamepad","outside","JumpPrimary"));Assert.Throws<InvalidDataException>(()=>UnityInputProvider.DecodeSelectionSlots(partial,true));
            var edit=current.BeginEdit();Assert.IsFalse(edit.TryAssign(wheel,"South",out _),"JumpとDecideの複数競合を原子的に拒否");CollectionAssert.AreEquivalent(current.Bindings,edit.Draft);
            Assert.IsTrue(edit.TryAssign(wheel,"RightShoulder",out var error),error);Assert.AreEqual("LeftShoulder",edit.Draft[("gamepad","outside","DashPrimary")]);Assert.AreEqual("RightShoulder",edit.Draft[("gamepad","battle","CardPrimary")]);
            edit=current.BeginEdit();Assert.IsFalse(edit.TryAssign(("gamepad","menu","Decide"),"LeftShoulder",out _));CollectionAssert.AreEquivalent(current.Bindings,edit.Draft);
            var menu=UnityInputProvider.CreateDefaultGamepadProfile().BeginEdit();Assert.IsTrue(menu.TryAssign(("gamepad","menu","Decide"),"RightStick",out _));var customized=new System.Collections.Generic.Dictionary<(string Device,string Context,string Action),string>(menu.Draft);customized.Remove(wheel);customized[("gamepad","menu","Decide")]="LeftShoulder";customized[("gamepad","menu","PageUp")]="RightStick";
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
    }
}
