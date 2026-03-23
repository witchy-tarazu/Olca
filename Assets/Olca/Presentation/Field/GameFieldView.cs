using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// ダンジョンとバトル両フェーズを統括する世界空間View
    ///
    /// 設計方針:
    /// - プレイヤー・敵はダンジョン/バトルを問わず同一のスプライトが継続表示
    /// - フィールド背景はフェーズを問わず常時表示
    /// - バトルグリッドは接触位置に出現し、バトル終了後に消える
    /// - HUDのみバトル時にフェードアウトして非表示
    /// </summary>
    public class GameFieldView : MonoBehaviour
    {
        [Header("プレイヤー")]
        [SerializeField] private Transform playerTransform;
        [SerializeField] private SpriteRenderer playerSprite;

        [Header("カメラ")]
        [SerializeField] private Camera fieldCamera;
        [SerializeField] private float cameraFollowSpeed = 5f;
        [SerializeField] private float cameraPlayerOffsetX = 2.5f;  // プレイヤーからカメラ中央までのXオフセット（グリッド半幅）

        [Header("バトルグリッド（World Space）")]
        [SerializeField] private WorldBattleGridView battleGridView;

        [Header("敵スプライトのデフォルト設定")]
        [SerializeField] private Color defaultEnemyColor = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private float enemyY = 0f;
        [SerializeField] private float enemyZ = 0f;

        [Header("HUD（バトル中は非表示）")]
        [SerializeField] private CanvasGroup dungeonHudCanvasGroup;

        [Header("デバッグ")]
        [SerializeField] private UnityEngine.UI.Text debugText;

        // 外部アクセス（ContactAnimator・Controller用）
        public Transform PlayerTransform => playerTransform;
        public SpriteRenderer PlayerSprite => playerSprite;
        public WorldBattleGridView BattleGrid => battleGridView;

        /// <summary>Enemy に対応する WorldEnemyView を返す（見つからなければ null）</summary>
        public WorldEnemyView GetEnemyView(Olca.Core.Domain.Enemy enemy)
        {
            foreach (var view in enemyViews)
                if (view != null && view.TrackedEnemy == enemy)
                    return view;
            return null;
        }

        private DungeonSystem dungeonSystem;
        private readonly List<WorldEnemyView> enemyViews = new List<WorldEnemyView>();
        private bool cameraFollowEnabled = true;
        private float? battleCameraTargetX = null;  // バトル開始時のカメラ移動先（スムーズlerp用）

        // ==================== 初期化 ====================

        /// <summary>ダンジョン初期化。マップ上の全敵スプライトを生成する。</summary>
        public void Initialize(DungeonSystem dungeon)
        {
            dungeonSystem = dungeon;
            cameraFollowEnabled = true;

            // カメラを初期位置にスナップ（ダンジョン開始時のヌルっとした動きを防ぐ）
            if (fieldCamera != null)
            {
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = dungeon.PlayerPosition + cameraPlayerOffsetX;
                fieldCamera.transform.position = camPos;
            }

            // CanvasGroup がなければ自動追加
            if (dungeonHudCanvasGroup == null)
            {
                var canvas = GetComponentInChildren<Canvas>();
                if (canvas != null)
                    dungeonHudCanvasGroup = canvas.gameObject.GetComponent<CanvasGroup>()
                        ?? canvas.gameObject.AddComponent<CanvasGroup>();
            }

            // マップ上の全敵スプライトを生成
            PlaceAllEnemySprites();
        }

        /// <summary>マップ上の全敵スプライトを配置（初期化時・ダンジョン復帰時に呼ぶ）</summary>
        private void PlaceAllEnemySprites()
        {
            // 既存スプライトを削除
            foreach (var view in enemyViews)
                if (view != null) Destroy(view.gameObject);
            enemyViews.Clear();

            if (dungeonSystem == null) return;

            // マップ上の全敵を取得してスプライト生成
            var allEnemies = dungeonSystem.Map.GetAllEnemies();
            foreach (var (cellIndex, enemy) in allEnemies)
            {
                var view = CreateEnemyView(enemy, cellIndex);
                enemyViews.Add(view);
            }
        }

        private WorldEnemyView CreateEnemyView(Enemy enemy, float dungeonX)
        {
            var go = new GameObject($"Enemy_{enemy.Name}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(dungeonX, enemyY, enemyZ);
            // グリッド1セル(1 unit)に対して約0.8unitのサイズ（旧BattlePrototypeSceneのサイズ感に合わせる）
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            // SpriteRenderer（白スプライト + 色で表現、PPU=1なので scale=0.8 → 0.8unit角）
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = defaultEnemyColor;
            sr.sortingOrder = 2;
            sr.sprite = CreateWhiteSprite();

            var view = go.AddComponent<WorldEnemyView>();
            view.Initialize(enemy, dungeonX);
            return view;
        }

        // ==================== バトル遷移 ====================

        /// <summary>
        /// バトル開始時の処理
        /// - バトルグリッドを接触位置に出現
        /// - 全敵スプライトをバトルグリッド座標モードに切り替え
        /// - カメラ追従を停止（バトル中はカメラ固定）
        /// </summary>
        public void OnBattleStarted(BattleSystem battle, float contactPositionX)
        {
            // グリッド原点をプレイヤーの現在位置から逆算
            // → プレイヤーをワープさせず、グリッドをプレイヤーに合わせて出現させる
            float originX = contactPositionX;
            if (playerTransform != null)
                originX = playerTransform.position.x - battle.Player.GridPosition.X;

            battleGridView?.Show(battle.Field, originX);

            // カメラ追従を停止し、グリッド中央へのスムーズ移動を開始
            cameraFollowEnabled = false;
            if (battleGridView != null)
                battleCameraTargetX = battleGridView.GridCenterWorldX;

            // 全敵スプライトをバトルグリッド座標に切り替え
            foreach (var view in enemyViews)
                view?.OnBattleStarted(battleGridView);
        }

        /// <summary>
        /// バトル終了時の処理
        /// - バトルグリッドを非表示
        /// - 生きている敵スプライトをダンジョン座標モードに戻す
        /// - カメラ追従を再開
        /// </summary>
        public void OnBattleEnded()
        {
            battleGridView?.Hide();
            battleCameraTargetX = null;
            cameraFollowEnabled = true;

            foreach (var view in enemyViews)
                view?.OnBattleEnded();
        }

        // ==================== 毎フレーム更新 ====================

        /// <summary>ダンジョンフェーズ更新（毎フレーム）</summary>
        public void UpdateView()
        {
            if (dungeonSystem == null) return;
            UpdatePlayerPosition();
            if (cameraFollowEnabled) UpdateCamera();
            UpdateEnemyViews();
            UpdateDebugUI();
        }

        /// <summary>バトルフェーズ更新（毎フレーム）</summary>
        public void RefreshBattleField()
        {
            // 通常接敵アニメーション中: プレイヤーに追従（cameraFollowEnabled=true）
            if (cameraFollowEnabled) UpdateCamera();

            // バトル開始後: グリッド中央へスムーズ移動（cameraFollowEnabled=false かつ目標あり）
            if (!cameraFollowEnabled && battleCameraTargetX.HasValue && fieldCamera != null)
            {
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = Mathf.Lerp(camPos.x, battleCameraTargetX.Value, Time.deltaTime * cameraFollowSpeed);
                fieldCamera.transform.position = camPos;
            }

            battleGridView?.Refresh();
            foreach (var view in enemyViews)
                view?.Refresh();
        }

        // ==================== HUD制御 ====================

        /// <summary>HUDをフェードアウトして非表示（バトル開始時）</summary>
        public async UniTask HideHUDAsync(float duration, CancellationToken ct = default)
        {
            if (dungeonHudCanvasGroup == null) return;
            await FadeCanvasGroupAsync(dungeonHudCanvasGroup, 1f, 0f, duration, ct);
            dungeonHudCanvasGroup.interactable = false;
            dungeonHudCanvasGroup.blocksRaycasts = false;
        }

        /// <summary>HUDをフェードインして表示（バトル終了時）</summary>
        public async UniTask ShowHUDAsync(float duration, CancellationToken ct = default)
        {
            if (dungeonHudCanvasGroup == null) return;
            dungeonHudCanvasGroup.interactable = true;
            dungeonHudCanvasGroup.blocksRaycasts = true;
            await FadeCanvasGroupAsync(dungeonHudCanvasGroup, 0f, 1f, duration, ct);
        }

        /// <summary>カメラ追従を一時停止/再開（接敵アニメーション中に使用）</summary>
        public void SetCameraFollowEnabled(bool enabled)
        {
            cameraFollowEnabled = enabled;
        }

        // ==================== 内部処理 ====================

        private void UpdatePlayerPosition()
        {
            if (playerTransform == null) return;
            playerTransform.position = new Vector3(
                dungeonSystem.PlayerPosition, dungeonSystem.PlayerHeight, 0f);
        }

        private void UpdateCamera()
        {
            if (fieldCamera == null || playerTransform == null) return;
            Vector3 targetPos = new Vector3(
                playerTransform.position.x + cameraPlayerOffsetX,
                fieldCamera.transform.position.y,
                fieldCamera.transform.position.z);
            fieldCamera.transform.position = Vector3.Lerp(
                fieldCamera.transform.position, targetPos,
                Time.deltaTime * cameraFollowSpeed);
        }

        private void UpdateEnemyViews()
        {
            foreach (var view in enemyViews)
                view?.Refresh();
        }

        private void UpdateDebugUI()
        {
            if (debugText == null || dungeonSystem == null) return;
            debugText.text = $"Position: {dungeonSystem.PlayerPosition:F2}\n" +
                             $"Height: {dungeonSystem.PlayerHeight:F2}\n" +
                             $"Dashing: {dungeonSystem.IsDashing}\n" +
                             $"InBattle: {dungeonSystem.IsInBattle}";
        }

        private async UniTask FadeCanvasGroupAsync(CanvasGroup group,
            float from, float to, float duration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(from, to, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            group.alpha = to;
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
