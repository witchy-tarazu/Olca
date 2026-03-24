using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// 背景・地面をカメラのX座標に追従させることで、
    /// 有限幅のスプライトを無限ループしているように見せるView。
    ///
    /// Ground や DungeonBackground にアタッチして使用する。
    /// スプライトの描画モードが Tiled（drawMode=1）である前提。
    /// </summary>
    public class ScrollingBackgroundView : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;

        private void Start()
        {
            // Inspectorで未設定の場合はメインカメラを使用
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (targetCamera == null) return;

            // X座標のみカメラに追従（Y・Zはシーン設定値を維持）
            Vector3 pos = transform.position;
            pos.x = targetCamera.transform.position.x;
            transform.position = pos;
        }
    }
}
