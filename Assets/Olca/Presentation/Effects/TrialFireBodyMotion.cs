using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Olca.Presentation.Effects
{
    /// <summary>試用カエンの本体衣装。元の姿勢と時計はViewが決め、共有素材の参照だけを借りる。</summary>
    public sealed class TrialFireBodyMotion : IDisposable
    {
        public const string ResourceRoot = "OlcaCharacterMotion/TrialFire/";
        private static readonly string[] PoseKeys = {
            "idle", "run_01", "run_02", "run_03", "run_04", "run_05", "run_06", "run_07", "run_08",
            "jump_rise", "jump_apex", "jump_fall", "dash", "wall_slide_01", "wall_slide_02",
            "ground_kick_prepare", "ground_kick_contact", "air_forward_contact"
        };
        private readonly Func<string, Sprite> load;
        private Dictionary<string, Sprite> sprites;
        private bool attempted, disposed;

        public TrialFireBodyMotion(Func<string, Sprite> loader = null)
        {
            load = loader ?? (key => Resources.Load<Sprite>(ResourceRoot + key));
        }

        public Sprite Resolve(Sprite original, Olca.Core.Domain.PlayerCharacter player)
        {
            if (disposed || original == null || !TrialFireKamuiVisual.IsActive(player)) return original;
            string key = PoseKey(original.name);
            if (key == null) return original;
            if (!attempted) LoadFullSet();
            return sprites != null && sprites.TryGetValue(key, out var sprite) ? sprite : original;
        }

        private void LoadFullSet()
        {
            attempted = true;
            var candidate = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (string key in PoseKeys)
            {
                var sprite = load(key);
                // 欠損や誤登録なら全衣装を無効にし、一部の動作だけ通常衣装へ戻る状態を作らない。
                if (sprite == null || sprite.name != "trial_fire_" + key || sprite.texture.width != 256 ||
                    sprite.texture.height != 256 || Mathf.Abs(sprite.pixelsPerUnit - 100) > .001f) return;
                candidate.Add(key, sprite);
            }
            sprites = candidate;
        }

        private static string PoseKey(string name)
        {
            int index = Numbered(name, "heroine_idle_", 10);
            if (index > 0) return "idle"; // 呼吸連番は未完成のため、試用の立ち姿1枚を使う。
            index = Numbered(name, "move_", 8);
            if (index > 0) return "run_" + index.ToString("00", CultureInfo.InvariantCulture);
            switch (name)
            {
                case "jump_rise": case "jump_apex": case "jump_fall": return name;
                case "heroine_dash_low_v4_nn256": return "dash";
                case "WallSlide_01": return "wall_slide_01";
                case "WallSlide_02": return "wall_slide_02";
                case "trial_ground_kick_prepare": return "ground_kick_prepare";
                case "trial_ground_kick_contact": return "ground_kick_contact";
                case "trial_air_forward_kick_contact": return "air_forward_contact";
                default: return null; // 未登録の姿勢を別のポーズで代用しない。
            }
        }

        private static int Numbered(string name, string prefix, int maximum)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            string tail = name.Substring(prefix.Length);
            if (tail.EndsWith("_0", StringComparison.Ordinal)) tail = tail.Substring(0, tail.Length - 2);
            return tail.Length == 2 && int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 1 && index <= maximum ? index : 0;
        }

        public void Dispose()
        {
            disposed = true;
            sprites?.Clear();
            sprites = null;
        }
    }
}
