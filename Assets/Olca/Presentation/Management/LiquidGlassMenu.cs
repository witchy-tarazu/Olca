using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Olca.Presentation.Management
{
    internal static class LiquidGlassMenu
    {
        private static Sprite panelFace;
        private static Sprite buttonFace;
        private static Sprite focusRing;
        private static Material glass;

        private static Material GlassMaterial()
        {
            if (glass == null)
            {
                var shader = Resources.Load<Shader>("OlcaUI/LiquidGlass");
                if (shader != null)
                    glass = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            return glass;
        }

        public static void Panel(Image image)
        {
            panelFace = panelFace != null ? panelFace : CreateFace(28, false);
            image.sprite = panelFace;
            image.type = Image.Type.Sliced;
            SetGlassMaterial(image);
            image.color = glass != null ? Color.white : new Color(.055f, .14f, .19f);
        }

        public static void Backdrop(Image image)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = Color.clear;
            InsetPanel(image.transform, 12f);
        }

        public static void InsetPanel(Transform parent, float inset)
        {
            if (parent.Find("LiquidGlassMenuSurface") != null) return;
            var obj = new GameObject("LiquidGlassMenuSurface", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            obj.transform.SetAsFirstSibling();
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
            var image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            Panel(image);
        }

        public static void ReadingSurface(Image image, bool dense = false)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color(.025f, .075f, .105f, dense ? .77f : .48f);
        }

        public static void ReadingText(Text text)
        {
            text.color = Color.white;
            ProtectText(text);
        }

        public static void Button(Button button)
        {
            if (button.GetComponent<LiquidGlassCoordinates>() != null) return;
            buttonFace = buttonFace != null ? buttonFace : CreateFace(12, true);
            var image = button.image;
            image.sprite = buttonFace;
            image.type = Image.Type.Sliced;
            var feedback = SetGlassMaterial(image);
            feedback.Bind(button);
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            // 旧メニューの明るい補助ボタン配色を、ガラス面用の色に統一する。
            colors.normalColor = new Color(.045f, .25f, .31f, 1f);
            colors.disabledColor = new Color(.16f, .23f, .27f, .72f);
            colors.selectedColor = colors.normalColor;
            colors.highlightedColor = colors.normalColor;
            colors.pressedColor = colors.normalColor;
            button.colors = colors;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                UseButtonFont(label);
                ReadingText(label);
            }
            var outline = image.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        public static void RunMenuAction(Button button, Transform menu, System.Action action)
        {
            if (!button.TryGetComponent<LiquidGlassCoordinates>(out var feedback))
            {
                action();
                return;
            }

            string name = button.name;
            feedback.PlayConfirmation();
            action();
            if (menu == null || !menu.gameObject.activeInHierarchy ||
                (button != null && button.gameObject.activeInHierarchy)) return;

            // 一覧を作り直す操作では、同じ位置の操作項目に決定演出を引き継ぐ。
            foreach (var replacement in menu.GetComponentsInChildren<Button>())
            {
                if (replacement.name != name || !replacement.TryGetComponent<LiquidGlassCoordinates>(out var next)) continue;
                next.PlayConfirmation();
                break;
            }
        }

        public static LiquidGlassCoordinates SetGlassMaterial(Image image)
        {
            image.material = GlassMaterial();
            var canvas = image.GetComponentInParent<Canvas>(true);
            if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            return image.gameObject.AddComponent<LiquidGlassCoordinates>();
        }

        public static void UseButtonFont(Text label)
        {
            var bold = Resources.Load<Font>("OlcaUI/LINESeedJP-Bold");
            if (bold == null) return;
            label.font = bold;
            label.fontStyle = FontStyle.Normal;
        }

        public static void ProtectText(Text label)
        {
            var shadow = label.GetComponent<Outline>() ?? label.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(.035f, .075f, .10f, .78f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);
        }

        // 四隅を固定し、辺と中央だけを伸ばす。色チャンネルには縁の強さとボタンかどうかを格納する。
        public static Image CreateFocusRing(Transform parent, string name)
        {
            if (focusRing == null) focusRing = CreateFace(14, false, true);
            var ring = new GameObject(name, typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(parent, false);
            ring.transform.SetAsFirstSibling();
            var image = ring.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = focusRing;
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, 0f);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * -2f;
            rect.offsetMax = Vector2.one * 2f;
            return image;
        }

        private static Sprite CreateFace(int radius, bool control, bool ring = false)
        {
            int border = radius + 3;
            int size = border * 2 + 2;
            float middle = size * .5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + .5f - middle) - middle + radius + 1);
                float dy = Mathf.Max(0, Mathf.Abs(y + .5f - middle) - middle + radius + 1);
                float distance = radius - Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(distance + .5f);
                if (ring)
                {
                    float ringAlpha = alpha * Mathf.Clamp01(3.4f - distance);
                    byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(35f, 240f, Mathf.SmoothStep(0f, 1f, distance / 1.7f)));
                    pixels[y * size + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(ringAlpha * 255));
                    continue;
                }

                float rim = Mathf.Clamp01(1 - distance / (control ? 8f : 18f));
                byte value = (byte)Mathf.RoundToInt(rim * 255);
                pixels[y * size + x] = new Color32(value, value, control ? (byte)255 : (byte)0,
                    (byte)Mathf.RoundToInt(alpha * 255));
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, Vector4.one * border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    // 9スライスのUVとは別に、面全体で連続する座標をシェーダーへ渡す。
    internal sealed class LiquidGlassCoordinates : BaseMeshEffect
    {
        private Button button;
        private Image ring;
        private LiquidGlassDroplets droplets;
        private float focus;
        private bool hovered;
        private float press;
        private float pressedAt = float.NegativeInfinity;
        private float confirmedAt = float.NegativeInfinity;
        private const float PressDuration = .18f;
        private const float PressShrink = .075f;
        private const float BurstDelay = .045f;
        private const float BurstDuration = .24f;

        public void Bind(Button target)
        {
            button = target;
            ring = LiquidGlassMenu.CreateFocusRing(button.transform, "GlassFocusRing");
            button.onClick.AddListener(PlayConfirmation);
        }

        private void CreateDroplets()
        {
            var spray = new GameObject("GlassConfirmDroplets", typeof(RectTransform), typeof(LiquidGlassDroplets));
            spray.transform.SetParent(button.transform, false);
            droplets = spray.GetComponent<LiquidGlassDroplets>();
            droplets.Initialize();
            spray.SetActive(false);
            var pointer = button.gameObject.AddComponent<LiquidGlassButtonPointer>();
            pointer.Feedback = this;
        }

        public void PlayPress()
        {
            if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return;
            pressedAt = Time.unscaledTime;
            press = 1f;
            ring.rectTransform.localScale = Vector3.one * (1f - PressShrink);
            graphic.SetVerticesDirty();
        }

        public void PlayConfirmation()
        {
            if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return;
            PlayPress();
            confirmedAt = Time.unscaledTime;
        }

        public void SetHovered(bool value) => hovered = value;

        private void LateUpdate()
        {
            if (button == null) return;
            bool canFocus = button.IsInteractable() && EventSystem.current?.currentSelectedGameObject == button.gameObject;
            float focusTarget = canFocus ? 1f : hovered && button.IsInteractable() ? .35f : 0f;
            float nextFocus = Mathf.MoveTowards(focus, focusTarget, Time.unscaledDeltaTime * 14f);
            float elapsed = Mathf.Clamp01((Time.unscaledTime - pressedAt) / PressDuration);
            float nextPress = 1f - Mathf.SmoothStep(0f, 1f, elapsed);
            float burstAge = Time.unscaledTime - confirmedAt - BurstDelay;
            UpdateBurst(burstAge);
            float ringVisibility = burstAge < 0f ? 1f : Mathf.InverseLerp(BurstDuration, BurstDuration + .12f, burstAge);
            var ringColor = new Color(1f, 1f, 1f, Mathf.InverseLerp(.35f, 1f, nextFocus) * ringVisibility);
            if (ring.color != ringColor) ring.color = ringColor;
            if (focus == nextFocus && press == nextPress) return;
            focus = nextFocus;
            press = nextPress;
            ring.rectTransform.localScale = Vector3.one * (1f - press * PressShrink);
            graphic.SetVerticesDirty();
        }

        private void UpdateBurst(float age)
        {
            bool spraying = age >= 0f && age < BurstDuration;
            if (droplets == null)
            {
                if (!spraying) return;
                CreateDroplets();
            }
            if (droplets.gameObject.activeSelf != spraying) droplets.gameObject.SetActive(spraying);
            if (spraying) droplets.SetProgress(age / BurstDuration);
        }

        protected override void OnDisable()
        {
            focus = press = 0f;
            hovered = false;
            if (ring != null)
            {
                ring.color = new Color(1f, 1f, 1f, 0f);
                ring.rectTransform.localScale = Vector3.one;
            }
            pressedAt = confirmedAt = float.NegativeInfinity;
            if (droplets != null) droplets.gameObject.SetActive(false);
            base.OnDisable();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive()) return;
            var area = graphic.GetPixelAdjustedRect();
            var vertex = default(UIVertex);
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = new Vector4(Mathf.InverseLerp(area.xMin, area.xMax, vertex.position.x),
                    Mathf.InverseLerp(area.yMin, area.yMax, vertex.position.y), focus, 0f);
                // 見た目の面だけを縮め、テキストと押下判定の位置は保つ。
                vertex.position = Vector3.LerpUnclamped(vertex.position, area.center, press * PressShrink);
                vertices.SetUIVertex(vertex, i);
            }
        }
    }

    internal sealed class LiquidGlassButtonPointer : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public LiquidGlassCoordinates Feedback { private get; set; }

        public void OnPointerEnter(PointerEventData eventData) => Feedback?.SetHovered(true);
        public void OnPointerExit(PointerEventData eventData) => Feedback?.SetHovered(false);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Feedback?.PlayPress();
        }
    }

    /// <summary>縁から広がる水滴。反射を含む小さな共通スプライトを生成して使い回す。</summary>
    internal sealed class LiquidGlassDroplets : MonoBehaviour
    {
        private static readonly Vector2[] Origins =
        {
            new Vector2(-.92f, .82f), new Vector2(-.60f, 1f), new Vector2(-.16f, 1f),
            new Vector2(.38f, 1f), new Vector2(.83f, .93f), new Vector2(1f, .33f),
            new Vector2(1f, -.48f), new Vector2(.67f, -1f), new Vector2(.13f, -1f),
            new Vector2(-.45f, -1f), new Vector2(-.89f, -.87f), new Vector2(-1f, -.18f)
        };
        private static Sprite droplet;
        private RectTransform area;
        private Image[] images;

        public void Initialize()
        {
            if (droplet == null) droplet = CreateDroplet();
            area = (RectTransform)transform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            images = new Image[Origins.Length];
            for (int i = 0; i < images.Length; i++)
            {
                var obj = new GameObject("Droplet", typeof(RectTransform), typeof(Image));
                obj.transform.SetParent(transform, false);
                var image = obj.GetComponent<Image>();
                image.sprite = droplet;
                image.raycastTarget = false;
                images[i] = image;
            }
        }

        public void SetProgress(float progress)
        {
            var halfSize = area.rect.size * .5f;
            for (int i = 0; i < images.Length; i++)
            {
                var origin = Origins[i];
                var image = images[i];
                float age = Mathf.Clamp01((progress - i % 3 * .065f) / .87f);
                image.enabled = age > 0f && age < 1f;
                if (!image.enabled) continue;

                var direction = new Vector2(origin.x * .68f, origin.y).normalized;
                float distance = (1f - (1f - age) * (1f - age)) * (19f + i % 4 * 5f);
                var rect = image.rectTransform;
                rect.anchoredPosition = Vector2.Scale(origin, halfSize)
                    + direction * distance + Vector2.down * (7f * age * age);
                float diameter = (6f + i % 4 * 1.7f) * Mathf.Lerp(1f, .62f, age);
                rect.sizeDelta = new Vector2(diameter * Mathf.Lerp(1.35f, 1f, age), diameter);
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                image.color = new Color(1f, 1f, 1f, Mathf.Sin(age * Mathf.PI) * (1f - age * .35f));
            }
        }

        private static Sprite CreateDroplet()
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + .5f, y + .5f) / (size * .5f) - Vector2.one;
                float radius = p.magnitude;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.60f, 1f, radius));
                var color = Color.Lerp(new Color(.62f, .89f, .98f), new Color(.10f, .30f, .40f), edge);
                float reflection = 1f - Mathf.Clamp01((p - new Vector2(-.26f, .30f)).magnitude * 2.4f);
                color = Color.Lerp(color, Color.white, reflection);
                color.a = Mathf.Clamp01((1f - radius) * 6f);
                pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
