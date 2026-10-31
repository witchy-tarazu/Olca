using System;
using System.IO;

namespace Olca.Unity
{
    /// <summary>保存先確定後だけ既存Storeを供給する。保存の確定時点や形式は変更しない。</summary>
    public sealed class SaveManager
    {
        public sealed class Context
        {
            public string SettingsDirectory { get; }
            public string CheckpointDirectory { get; }
            public string ProgressDirectory { get; }
            private KeyboardBindingFileStore keyboard;
            private GamepadBindingFileStore gamepad;
            private AudioVolumeFileStore audio;
            private ClearReturnFileStore progress;
            internal Context(string settings, string checkpoints, string progress)
            { SettingsDirectory = Full(settings); CheckpointDirectory = Full(checkpoints); ProgressDirectory = Full(progress); }
            private static string Full(string path) => Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            public KeyboardBindingFileStore Keyboard => keyboard ?? (keyboard = new KeyboardBindingFileStore(SettingsDirectory));
            public GamepadBindingFileStore Gamepad => gamepad ?? (gamepad = new GamepadBindingFileStore(SettingsDirectory));
            public AudioVolumeFileStore Audio => audio ?? (audio = new AudioVolumeFileStore(SettingsDirectory));
            public ClearReturnFileStore Progress => progress ?? (progress = new ClearReturnFileStore(ProgressDirectory));
        }
        // Contextの作成だけでは読み書きしない。検証用保存ガードは呼出元が先に確定する。
        public Context OpenContext(string settings, string checkpoints, string progress) => new Context(settings, checkpoints, progress);
    }
}
