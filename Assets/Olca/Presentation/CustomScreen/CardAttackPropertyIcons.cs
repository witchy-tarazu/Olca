using System.Collections.Generic;
using Olca.Core.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>属性・系統や状態とは分け、明示された攻撃特性だけへ共有素材を対応させる。</summary>
    public static class AttackPropertyIconCatalog
    {
        private static readonly AttackProperty[] order = { AttackProperty.Sword, AttackProperty.Break, AttackProperty.Wind };
        private static readonly Dictionary<AttackProperty, Sprite> sprites = new Dictionary<AttackProperty, Sprite>();
        public static IReadOnlyList<AttackProperty> Resolve(AttackProperty properties)
        {
            var result = new List<AttackProperty>();
            foreach (var property in order) if ((properties & property) != 0) result.Add(property);
            return result;
        }
        public static Sprite Get(AttackProperty property)
        {
            if (property != AttackProperty.Sword && property != AttackProperty.Break && property != AttackProperty.Wind) return null;
            if (sprites.TryGetValue(property, out var shared) && shared != null) return shared;
            var source = Resources.Load<TextAsset>("OlcaAttackProperties/" + property.ToString().ToLowerInvariant());
            if (source == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(source.bytes, true)) {
                if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
                return null;
            }
            texture.name = "attack-property/" + property; texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp; texture.hideFlags = HideFlags.HideAndDontSave;
            shared = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            shared.name = texture.name; shared.hideFlags = HideFlags.HideAndDontSave; sprites[property] = shared;
            return shared;
        }
    }

    /// <summary>カード詳細のスクロール内容内に専用行を予約する。素材は借用し入力を受けない。</summary>
    public sealed class CardAttackPropertyIcons : MonoBehaviour
    {
        private readonly List<Image> slots = new List<Image>();
        public int VisibleCount { get; private set; }
        public static void Bind(Text label, Card card, string content)
        {
            if (label == null) return;
            var properties = AttackPropertyIconCatalog.Resolve(card?.Properties ?? AttackProperty.None);
            label.text = (properties.Count > 0 ? "\n\n" : "") + content;
            var row = label.GetComponentInChildren<CardAttackPropertyIcons>(true);
            if (row == null && properties.Count > 0) {
                var obj = new GameObject("CardAttackPropertyIcons", typeof(RectTransform), typeof(CardAttackPropertyIcons));
                obj.transform.SetParent(label.transform, false); row = obj.GetComponent<CardAttackPropertyIcons>();
                var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(168, 48);
            }
            if (row != null) row.Show(properties);
        }
        public static void Clear(Text label)
        {
            if (label == null) return;
            var row = label.GetComponentInChildren<CardAttackPropertyIcons>(true);
            if (row != null) row.Show(System.Array.Empty<AttackProperty>());
        }
        private void Show(IReadOnlyList<AttackProperty> properties)
        {
            while (slots.Count < properties.Count) {
                var obj = new GameObject("Property", typeof(RectTransform), typeof(Image)); obj.transform.SetParent(transform, false);
                var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(slots.Count * 56, 0); rect.sizeDelta = new Vector2(48, 48);
                var image = obj.GetComponent<Image>(); image.raycastTarget = false; image.preserveAspect = true; slots.Add(image);
            }
            VisibleCount = 0;
            for (int i = 0; i < slots.Count; i++) {
                var sprite = i < properties.Count ? AttackPropertyIconCatalog.Get(properties[i]) : null;
                slots[i].sprite = sprite; slots[i].gameObject.SetActive(sprite != null);
                if (sprite != null) { slots[i].name = "Property_" + properties[i]; VisibleCount++; }
            }
        }
        private void OnDisable() { Show(System.Array.Empty<AttackProperty>()); }
    }
}
