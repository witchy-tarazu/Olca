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
            foreach(var pair in original.Bindings)if(pair.Key.Action!="SelectionPrevious"&&pair.Key.Action!="SelectionNext")slots.Add(pair.Key,pair.Value);
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
                var rows=new System.Collections.Generic.List<string>();foreach(var pair in original.Bindings)if(pair.Key.Action!="SelectionPrevious"&&pair.Key.Action!="SelectionNext")rows.Add("{\"Context\":\""+pair.Key.Context+"\",\"Action\":\""+pair.Key.Action+"\",\"Key\":"+(pair.Value==null?"null":"\""+pair.Value+"\"")+"}");
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
