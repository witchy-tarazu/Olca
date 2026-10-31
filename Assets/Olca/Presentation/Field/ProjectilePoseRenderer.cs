using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>複数アクションから共有する飛翔素材。軌道は描画だけで判定・寿命を変更しない。</summary>
    public sealed class ProjectileAppearance
    {
        public string Key { get; }
        public IReadOnlyList<Sprite> Sprites { get; }
        public IReadOnlyList<ActionDisplayFrame> Frames { get; }
        public float StartHeight { get; }
        public float EndHeight { get; }
        public float ArcHeight { get; }
        public bool Loop { get; }
        /// <summary>右向きに描かれた素材だけ、表示上の進行方向へ反転する。</summary>
        public bool FaceTravelDirection { get; }
        private readonly long duration;
        public ProjectileAppearance(string key, IEnumerable<Sprite> sprites, IEnumerable<ActionDisplayFrame> frames,
            float startHeight, float endHeight, float arcHeight, bool loop = false, bool faceTravelDirection = false)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("飛翔素材参照が必要です");
            var images = sprites?.ToArray() ?? throw new ArgumentNullException(nameof(sprites));
            var schedule = frames?.ToArray() ?? throw new ArgumentNullException(nameof(frames));
            if (images.Length == 0 || images.Any(s => s == null) || schedule.Length == 0 || schedule.Any(f => f == null || f.Index >= images.Length) ||
                new[] { startHeight, endHeight, arcHeight }.Any(f => float.IsNaN(f) || float.IsInfinity(f))) throw new ArgumentException("飛翔素材とコマ配分が不正です");
            Key = key; Sprites = Array.AsReadOnly(images); Frames = Array.AsReadOnly(schedule);
            StartHeight = startHeight; EndHeight = endHeight; ArcHeight = arcHeight; Loop = loop; FaceTravelDirection = faceTravelDirection;
            duration = schedule.Sum(f => (long)f.Frames);
        }
        public Sprite SelectFrame(long elapsed)
        {
            elapsed = Math.Max(0, elapsed); if (Loop) elapsed %= duration;
            foreach (var frame in Frames) { if (elapsed < frame.Frames) return Sprites[frame.Index]; elapsed -= frame.Frames; }
            return Sprites[Frames[Frames.Count - 1].Index];
        }
    }

    /// <summary>既存の遅延効果を表示する。発射元/アクションの寿命とは独立し、自分の描画実体だけを所有する。</summary>
    public sealed class ProjectilePoseRenderer : IDisposable
    {
        private readonly Transform parent;
        private Dictionary<string, ProjectileAppearance> appearances = new Dictionary<string, ProjectileAppearance>();
        private readonly Dictionary<DelayedEffect, SpriteRenderer> instances = new Dictionary<DelayedEffect, SpriteRenderer>();
        public int ActiveCount => instances.Count;
        public bool Owns(DelayedEffect effect) => instances.ContainsKey(effect);
        public ProjectilePoseRenderer(Transform parent) { this.parent = parent != null ? parent : throw new ArgumentNullException(nameof(parent)); }
        public void Configure(IEnumerable<ProjectileAppearance> values)
        {
            var next = new Dictionary<string, ProjectileAppearance>();
            foreach (var value in values ?? Array.Empty<ProjectileAppearance>()) {
                if (value == null || next.ContainsKey(value.Key)) throw new ArgumentException("飛翔素材参照の重複/欠落があります");
                next.Add(value.Key, value);
            }
            Clear(); appearances = next;
        }
        public void Refresh(BattleSystem battle, Func<Olca.Core.Domain.Vector2Int, Vector3> world, Func<float, float> logicalLength, int sortingLayer, int sortingOrder)
        {
            if (battle == null || battle.IsGameOver || parent == null) { Clear(); return; }
            var active = battle.DelayedEffects.GetActiveEffects().Where(e => e.EffectType == DelayedEffectType.Projectile &&
                e.SourcePosition.HasValue && e.ProjectileVisualKey != null && appearances.ContainsKey(e.ProjectileVisualKey)).ToArray();
            foreach (var old in instances.Keys.Except(active).ToArray()) { Release(instances[old]); instances.Remove(old); }
            foreach (var effect in active) {
                var appearance = appearances[effect.ProjectileVisualKey];
                if (!instances.TryGetValue(effect, out var renderer)) {
                    var root = new GameObject("ActionProjectile"); root.transform.SetParent(parent, false);
                    renderer = root.AddComponent<SpriteRenderer>(); instances.Add(effect, renderer);
                }
                float t = effect.Progress;
                var source = world(effect.SourcePosition.Value);
                var target = world(effect.TargetPosition);
                renderer.sprite = appearance.SelectFrame(effect.ElapsedDisplayFrames);
                renderer.sortingLayerID = sortingLayer; renderer.sortingOrder = sortingOrder;
                // 素材の基準向きは明示指定。判定座標・軌道・スケールは変えない。
                renderer.flipX = appearance.FaceTravelDirection && target.x < source.x;
                renderer.transform.position = Vector3.Lerp(source, target, t) + Vector3.up *
                    logicalLength(Mathf.Lerp(appearance.StartHeight, appearance.EndHeight, t) + 4f * t * (1f - t) * appearance.ArcHeight);
            }
        }
        private static void Release(SpriteRenderer renderer)
        {
            if (renderer == null) return;
            renderer.sprite = null; renderer.gameObject.SetActive(false);
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(renderer.gameObject); else UnityEngine.Object.DestroyImmediate(renderer.gameObject);
        }
        public void Clear() { foreach (var renderer in instances.Values) Release(renderer); instances.Clear(); }
        public void Dispose() { Clear(); appearances.Clear(); }
    }
}
