using UnityEngine;
using Olca.Core.Logic.Dungeon;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// ダンジョンの表示を担当するView
    /// ロジックから状態を取得して表示のみを行う
    /// </summary>
    public class DungeonView : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private Transform playerTransform;
        [SerializeField] private SpriteRenderer playerSprite;

        [Header("Camera")]
        [SerializeField] private Camera dungeonCamera;
        [SerializeField] private float cameraFollowSpeed = 5f;

        [Header("UI")]
        [SerializeField] private UnityEngine.UI.Text debugText;

        private DungeonSystem dungeonSystem;

        /// <summary>
        /// 初期化
        /// </summary>
        public void Initialize(DungeonSystem dungeon)
        {
            dungeonSystem = dungeon;
        }

        /// <summary>
        /// View更新（毎フレーム呼ばれる）
        /// </summary>
        public void UpdateView()
        {
            if (dungeonSystem == null) return;

            // プレイヤー位置を更新
            UpdatePlayerPosition();

            // カメラ追従
            UpdateCamera();

            // デバッグ情報表示
            UpdateDebugUI();
        }

        /// <summary>
        /// プレイヤー位置を更新
        /// </summary>
        private void UpdatePlayerPosition()
        {
            if (playerTransform == null) return;

            // ロジックから位置と高さを取得
            float positionX = dungeonSystem.PlayerPosition;
            float height = dungeonSystem.PlayerHeight;

            // Unity座標系に変換（仮: 1セル = 1Unity単位）
            playerTransform.position = new Vector3(positionX, height, 0f);
        }

        /// <summary>
        /// カメラ追従
        /// </summary>
        private void UpdateCamera()
        {
            if (dungeonCamera == null || playerTransform == null) return;

            // プレイヤーに追従（X軸のみ）
            Vector3 targetPos = new Vector3(
                playerTransform.position.x,
                dungeonCamera.transform.position.y,
                dungeonCamera.transform.position.z);

            dungeonCamera.transform.position = Vector3.Lerp(
                dungeonCamera.transform.position,
                targetPos,
                Time.deltaTime * cameraFollowSpeed);
        }

        /// <summary>
        /// デバッグUI更新
        /// </summary>
        private void UpdateDebugUI()
        {
            if (debugText == null) return;

            debugText.text = $"Position: {dungeonSystem.PlayerPosition:F2}\n" +
                           $"Height: {dungeonSystem.PlayerHeight:F2}\n" +
                           $"Dashing: {dungeonSystem.IsDashing}\n" +
                           $"InBattle: {dungeonSystem.IsInBattle}";
        }
    }
}
