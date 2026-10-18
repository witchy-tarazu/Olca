using UnityEngine;

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
        }
    }
}
