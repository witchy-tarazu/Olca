using System;
using System.IO;
using NUnit.Framework;
using Olca.Unity;
using Olca.Core.Domain;

namespace Olca.Tests
{
    public class SettingsWriteRefactorTests
    {
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
