using System;
using System.IO;
using NUnit.Framework;
using Olca.Unity;
using Olca.Core.Domain;

namespace Olca.Tests
{
    public class SettingsWriteRefactorTests
    {
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
