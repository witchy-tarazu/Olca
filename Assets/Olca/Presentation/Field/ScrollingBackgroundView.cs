using UnityEngine;
using System.Collections.Generic;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// 地面は横方向に追従し、単枚背景は等比coverで画面上端に合わせる。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ScrollingBackgroundView : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool fitCamera;
        private SpriteRenderer backgroundRenderer;

        [Header("低い洞窟前景")]
        [SerializeField] private Sprite foregroundSprite;
        [SerializeField] private WorldBattleGridView foregroundGrid;
        [SerializeField] private float foregroundScrollFactor = 1.12f;
        // 元絵の平坦な端を隣の凹凸で覆う。Spriteの等倍scaleは変えず、配置間隔だけを詰める。
        [SerializeField, Range(0f, 0.5f)] private float foregroundTileOverlapRatio;
        // 画像下端から全幅が連続して不透明な帯の上端までの比率。最高峰だけで整列しない。
        [SerializeField, Range(0f, 1f)] private float foregroundOpaqueTop = 1f;
        // 不透明帯の追加の重なり量。通常Sceneは0で、元絵の凹凸と画面ピクセル余白だけを重ねる。
        [SerializeField, Range(0f, 0.25f)] private float foregroundOverlapRatio;
        private Transform foregroundRoot;
        private SpriteRenderer foregroundFill;
        private readonly List<SpriteRenderer> foregroundTiles = new List<SpriteRenderer>();
        private Sprite fillSprite;
        private Texture2D fillTexture;

        private void Start()
        {
            // Inspectorで未設定の場合はメインカメラを使用
            if (targetCamera == null)
                targetCamera = Camera.main;
            backgroundRenderer = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (targetCamera == null) return;

            Vector3 pos = transform.position;
            pos.x = targetCamera.transform.position.x;
            if (fitCamera && targetCamera.orthographic && backgroundRenderer != null && backgroundRenderer.sprite != null)
            {
                // 横を繰り返さず、縦横比と天井を保って画面を覆う。
                var bounds = backgroundRenderer.sprite.bounds;
                float height = targetCamera.orthographicSize * 2f;
                float scale = Mathf.Max(height / bounds.size.y, height * targetCamera.aspect / bounds.size.x);
                backgroundRenderer.drawMode = SpriteDrawMode.Simple;
                transform.localScale = new Vector3(scale, scale, 1f);
                pos.x -= bounds.center.x * scale;
                pos.y = targetCamera.transform.position.y + height * 0.5f - bounds.max.y * scale;
            }
            transform.position = pos;
            UpdateForeground();
        }

        private SpriteRenderer CreateForegroundRenderer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(foregroundRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        private void UpdateForeground()
        {
            // 素材未設定時は仮の前景を表示しない。
            if (foregroundSprite == null || foregroundGrid == null || targetCamera == null || !targetCamera.orthographic)
            {
                if (foregroundRoot != null) foregroundRoot.gameObject.SetActive(false);
                return;
            }
            if (foregroundRoot == null)
            {
                foregroundRoot = new GameObject("CaveForeground").transform;
                fillTexture = new Texture2D(1, 1);
                fillTexture.SetPixel(0, 0, Color.white);
                fillTexture.Apply();
                fillSprite = Sprite.Create(fillTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                foregroundFill = CreateForegroundRenderer("ForegroundOpaqueBase", 9);
                foregroundFill.sprite = fillSprite;
                foregroundFill.color = new Color(15f / 255f, 24f / 255f, 32f / 255f, 1f);
            }
            foregroundRoot.gameObject.SetActive(true);
            float cameraHeight = targetCamera.orthographicSize * 2f;
            float cameraWidth = cameraHeight * targetCamera.aspect;
            float cameraX = targetCamera.transform.position.x;
            float bottom = targetCamera.transform.position.y - cameraHeight * 0.5f;
            // 全幅不透明な帯を実床下端へ重ね、そこから上の凹凸で水平の切断線を隠す。
            float seamMargin = cameraHeight * 2f / Mathf.Max(1, targetCamera.pixelHeight);
            float top = foregroundGrid.RenderedFloorBottomY + seamMargin;
            float opaqueBandTop = top + foregroundGrid.CellHeight * Mathf.Clamp(foregroundOverlapRatio, 0f, 0.25f);
            float fillHeight = Mathf.Max(0f, top - bottom);
            foregroundFill.enabled = fillHeight > 0f;
            foregroundFill.transform.position = new Vector3(cameraX, bottom + fillHeight * 0.5f, 0f);
            foregroundFill.transform.localScale = new Vector3(cameraWidth + seamMargin * 2f, fillHeight, 1f);
            var bounds = foregroundSprite.bounds;
            float scale = 1f;
            float opaqueTop = bounds.min.y + bounds.size.y * foregroundOpaqueTop;
            float tileWidth = bounds.size.x * scale;
            float tileStep = tileWidth * (1f - Mathf.Clamp(foregroundTileOverlapRatio, 0f, 0.5f));
            int count = Mathf.CeilToInt(cameraWidth / tileStep) + 2;
            while (foregroundTiles.Count < count)
                foregroundTiles.Add(CreateForegroundRenderer("ForegroundRock_" + foregroundTiles.Count, 10));
            // 絶対カメラ位置から計算し、逆走・停止・解像度変更でも誤差を蓄積しない。
            int first = Mathf.FloorToInt((cameraX * foregroundScrollFactor - cameraWidth * 0.5f) / tileStep);
            for (int i = 0; i < foregroundTiles.Count; i++)
            {
                var tile = foregroundTiles[i];
                tile.enabled = i < count;
                if (!tile.enabled) continue;
                tile.sprite = foregroundSprite;
                tile.transform.localScale = new Vector3(scale, scale, 1f);
                tile.transform.position = new Vector3(
                    (first + i) * tileStep - cameraX * (foregroundScrollFactor - 1f) - bounds.min.x * scale,
                    opaqueBandTop - opaqueTop * scale, 0f);
            }
        }

        private void OnDisable()
        {
            if (foregroundRoot != null) foregroundRoot.gameObject.SetActive(false);
        }

        private static void ReleaseOwned(Object value)
        {
            if (value == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            if (foregroundRoot != null) ReleaseOwned(foregroundRoot.gameObject);
            ReleaseOwned(fillSprite);
            ReleaseOwned(fillTexture);
        }
    }
}
