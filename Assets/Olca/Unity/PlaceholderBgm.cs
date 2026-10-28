using System.IO;
using UnityEngine;
namespace Olca.Unity
{
    /// <summary>明示曲を優先する共通仮BGM。PCM包装でループ端点を圧縮せず保持する。</summary>
    public static class PlaceholderBgm
    {
        private static AudioClip exploration, battle;
        public static AudioClip Exploration(AudioClip explicitClip = null) => explicitClip != null ? explicitClip : exploration != null ? exploration : exploration = Load("exploration");
        public static AudioClip Battle(AudioClip explicitBattle = null, AudioClip explicitExploration = null)
            => explicitBattle != null ? explicitBattle : explicitExploration != null ? explicitExploration : battle != null ? battle : battle = Load("battle");
        private static AudioClip Load(string name)
        {
            var asset = Resources.Load<TextAsset>("OlcaBgm/" + name);
            if (asset == null) return null;
            // 自作スクリプトの固定PCM形式だけを読み、未知形式を推測しない。
            using (var reader = new BinaryReader(new MemoryStream(asset.bytes))) {
                if (reader.BaseStream.Length < 44 || new string(reader.ReadChars(4)) != "RIFF") return null;
                reader.ReadInt32();
                if (new string(reader.ReadChars(4)) != "WAVE" || new string(reader.ReadChars(4)) != "fmt " || reader.ReadInt32() != 16 || reader.ReadInt16() != 1 || reader.ReadInt16() != 1 || reader.ReadInt32() != 48000) return null;
                if (reader.ReadInt32() != 96000 || reader.ReadInt16() != 2 || reader.ReadInt16() != 16 || new string(reader.ReadChars(4)) != "data") return null;
                int size = reader.ReadInt32();
                if (size <= 0 || size % 2 != 0 || size != reader.BaseStream.Length - reader.BaseStream.Position) return null;
                var samples = new float[size / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
                var clip = AudioClip.Create("placeholder-bgm/" + name, samples.Length, 1, 48000, false);
                clip.SetData(samples, 0); clip.hideFlags = HideFlags.HideAndDontSave; return clip;
            }
        }
    }
}
