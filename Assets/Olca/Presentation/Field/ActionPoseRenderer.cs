using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Logic.CardEffects;
using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>共有Sprite参照とフレームごとの握り位置。カードIDや性能値を所有しない。</summary>
    public sealed class ActionAppearance
    {
        public string MotionKey { get; }
        public string EquipmentKey { get; }
        public IReadOnlyList<Sprite> Body { get; }
        public IReadOnlyList<Sprite> Equipment { get; }
        public IReadOnlyList<Vector2> Grips { get; }
        public ActionAppearance(string motionKey, string equipmentKey, IEnumerable<Sprite> body,
            IEnumerable<Vector2> grips, IEnumerable<Sprite> equipment = null)
        {
            if (string.IsNullOrWhiteSpace(motionKey) || equipmentKey != null && string.IsNullOrWhiteSpace(equipmentKey)) throw new ArgumentException("素材参照が必要です");
            var poses = body?.ToArray() ?? throw new ArgumentNullException(nameof(body));
            var positions = grips?.ToArray() ?? throw new ArgumentNullException(nameof(grips));
            var props = equipment?.ToArray() ?? new Sprite[poses.Length];
            if (poses.Length == 0 || poses.Any(v => v == null) || positions.Length != poses.Length || props.Length != poses.Length ||
                positions.Any(v => float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y)) ||
                (equipmentKey == null ? props.Any(v => v != null) : props.Any(v => v == null)))
                throw new ArgumentException("全コマの本体・握り位置・差替素材を明示してください");
            MotionKey = motionKey; EquipmentKey = equipmentKey;
            Body = Array.AsReadOnly(poses); Grips = Array.AsReadOnly(positions); Equipment = Array.AsReadOnly(props);
        }
        public void Validate(ActionVisualDefinition visual)
        {
            if (visual == null || visual.MotionKey != MotionKey || visual.EquipmentKey != EquipmentKey) throw new ArgumentException("表示定義と素材参照が一致しません");
            if (visual.Startup.Concat(visual.Execution).Concat(visual.Recovery).Any(v => v.Index >= Body.Count))
                throw new ArgumentException("素材に存在しないコマが参照されています");
        }
    }

    /// <summary>モデル時計のコマを描くだけのアダプター。元のSprite/Animatorを一つの所有範囲で復元する。</summary>
    public sealed class ActionPoseRenderer : IDisposable
    {
        private readonly SpriteRenderer body;
        private readonly Animator animator;
        private Dictionary<(string, string), ActionAppearance> appearances = new Dictionary<(string, string), ActionAppearance>();
        private Sprite original;
        private bool animatorWasEnabled;
        private CardActionVisualState owner;
        private SpriteRenderer equipment;
        public bool IsActive { get; private set; }
        public string MissingBinding { get; private set; }
        public SpriteRenderer EquipmentRenderer => equipment;
        public ActionPoseRenderer(SpriteRenderer body, Animator animator)
        { this.body = body != null ? body : throw new ArgumentNullException(nameof(body)); this.animator = animator; }
        public void Configure(IEnumerable<ActionAppearance> bindings)
        {
            var next = new Dictionary<(string, string), ActionAppearance>();
            foreach (var value in bindings ?? Array.Empty<ActionAppearance>()) {
                if (value == null || next.ContainsKey((value.MotionKey, value.EquipmentKey))) throw new ArgumentException("素材参照の重複/欠落があります");
                next.Add((value.MotionKey, value.EquipmentKey), value);
            }
            Clear(); appearances = next;
        }
        public bool Apply(CardActionVisualState state)
        {
            MissingBinding = null;
            if (body == null || state == null) { Clear(); return false; }
            var key = (state.Definition.Visual.MotionKey, state.Definition.Visual.EquipmentKey);
            if (!appearances.TryGetValue(key, out var binding)) { Clear(); MissingBinding = key.Item1 + "/" + key.Item2; return false; }
            int frame = state.DisplayFrame;
            if (frame < 0 || frame >= binding.Body.Count) { Clear(); if (frame >= binding.Body.Count) MissingBinding = key.Item1 + "/frame:" + frame; return false; }
            if (!ReferenceEquals(owner, state)) { Clear(); original = body.sprite; animatorWasEnabled = animator != null && animator.enabled; owner = state; IsActive = true; }
            if (animator != null) animator.enabled = false;
            body.sprite = binding.Body[frame];
            var prop = binding.Equipment[frame];
            if (prop == null) { HideEquipment(); return true; }
            if (equipment == null) {
                var root = new GameObject("ActionEquipment"); root.transform.SetParent(body.transform, false);
                equipment = root.AddComponent<SpriteRenderer>();
            }
            equipment.sprite = prop; equipment.sortingLayerID = body.sortingLayerID; equipment.sortingOrder = body.sortingOrder + 1;
            var point = binding.Grips[frame]; equipment.transform.localPosition = new Vector3(point.x, point.y, 0);
            equipment.gameObject.SetActive(true);
            return true;
        }
        private void HideEquipment()
        { if (equipment != null) { equipment.sprite = null; equipment.gameObject.SetActive(false); } }
        public void Clear()
        {
            MissingBinding = null;
            HideEquipment();
            if (IsActive) { if (body != null) body.sprite = original; if (animator != null) animator.enabled = animatorWasEnabled; }
            original = null; owner = null; IsActive = false;
        }
        public void Dispose()
        {
            Clear(); appearances.Clear();
            if (equipment != null) { if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(equipment.gameObject); else UnityEngine.Object.DestroyImmediate(equipment.gameObject); }
            equipment = null;
        }
    }
}
