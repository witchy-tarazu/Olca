using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Integration;

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

        [Header("プレイヤーモックスプライト")]
        [SerializeField] private Sprite idlePlayerSprite;
        [SerializeField] private Sprite dashPlayerSprite;
        [SerializeField] private Sprite jumpRisePlayerSprite;
        [SerializeField] private Sprite jumpApexPlayerSprite;
        [SerializeField] private Sprite jumpFallPlayerSprite;

        [Header("プレイヤーアニメーション")]
        [SerializeField] private Animator playerAnimator;

        private static readonly int IsRunningParameter = Animator.StringToHash("isRunning");
        private static readonly int IsMovingParameter = Animator.StringToHash("isMoving");
        private static readonly int JumpPhaseParameter = Animator.StringToHash("jumpPhase");
        [SerializeField] private Sprite[] contactKnockbackSprites;
        private Sprite contactPoseSprite;
        private Sprite contactPreviousSprite;
        private bool contactAnimatorWasEnabled;
        private bool contactPoseActive;

        /// <summary>接敵表示の進捗だけを受け取る。位置・回転・倍率は変更しない。</summary>
        public void SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase phase, float progress)
        {
            if (phase == Olca.Presentation.Dungeon.ContactVisualPhase.None) {
                contactPoseSprite = null;
                if (contactPoseActive && playerSprite != null) playerSprite.sprite = contactPreviousSprite;
                contactPreviousSprite = null;
                if (contactPoseActive && playerAnimator != null) playerAnimator.enabled = contactAnimatorWasEnabled;
                contactPoseActive = false;
                UpdatePlayerSprite();
                if (playerAnimator != null && playerAnimator.enabled && playerAnimator.runtimeAnimatorController != null) playerAnimator.Update(0f);
                return;
            }
            Sprite[] frames = contactKnockbackSprites;
            Sprite sprite = frames == null || frames.Length == 0 ? null : frames[SelectContactFrame(phase, progress, frames.Length)];
            if (sprite == null) return; // 専用素材の未設定を別モーションの完成扱いにしない。
            if (!contactPoseActive) {
                contactPreviousSprite = playerSprite != null ? playerSprite.sprite : null;
                contactAnimatorWasEnabled = playerAnimator != null && playerAnimator.enabled;
                contactPoseActive = true;
            }
            if (playerAnimator != null) playerAnimator.enabled = false;
            contactPoseSprite = sprite;
            SetPlayerSprite(sprite);
        }

        public static int SelectContactFrame(Olca.Presentation.Dungeon.ContactVisualPhase phase, float progress, int count)
        {
            if (count <= 0) throw new System.ArgumentOutOfRangeException(nameof(count));
            if (phase == Olca.Presentation.Dungeon.ContactVisualPhase.Impact) return 0;
            int first = phase == Olca.Presentation.Dungeon.ContactVisualPhase.Knockback && count > 1 ? 1 : 0;
            return first + Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(progress) * (count - first)) - 1, 0, count - first - 1);
        }

        private int appliedJumpPhase;
        private bool isMoving;

        [Header("カメラ")]
        [SerializeField] private Camera fieldCamera;
        [SerializeField] private float cameraFollowSpeed = 50f;            // 通常時の追従速度
        [SerializeField] private float cameraFollowSpeedDash = 15f;       // ダッシュ時の追従速度（プレイヤーが前に出る演出）
        [SerializeField] private float cameraFollowSpeedTransition = 3f;  // 速度切替のなめらかさ（小さいほどゆっくり）

        private float currentFollowSpeed;  // 実際に使用する追従速度（Lerpで遷移）
        // スナップ距離 = CameraOffsetXの2倍（ダッシュ程度のラグでは発動しない大きさ）
        private float cameraMaxLagDistance => CameraOffsetX * 2f;
        [SerializeField] private float cameraPlayerOffsetX = 2.5f;  // フォールバック用（非orthographicカメラ等）

        /// <summary>
        /// プレイヤーがグリッド左半分の中央列（列1）に来るためのカメラXオフセット。
        /// battleGridView が設定されていればグリッド幅から算出、なければInspector値を使用。
        /// </summary>
        private float CameraOffsetX =>
            battleGridView != null
                ? battleGridView.LeftHalfCenterCameraOffsetX
                : cameraPlayerOffsetX;

        [Header("バトルグリッド（World Space）")]
        [SerializeField] private WorldBattleGridView battleGridView;

        [Header("敵Prefab")]
        [SerializeField] private GameObject enemyViewPrefab;   // WorldEnemyView + EnemyView を持つ prefab

        [Header("ダメージ数値プール（World Space）")]
        [SerializeField] private Olca.Presentation.Effects.WorldDamageNumberPool worldDamageNumberPool;

        [Header("敵スプライトのデフォルト設定（prefab未設定時のフォールバック）")]
        [SerializeField] private Color defaultEnemyColor = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private float enemyY = 0f;
        [SerializeField] private float enemyZ = 0f;

        [Header("HUD（バトル中は非表示）")]
        [SerializeField] private CanvasGroup dungeonHudCanvasGroup;

        [Header("デバッグ")]
        [SerializeField] private UnityEngine.UI.Text debugText;
        [SerializeField] private UnityEngine.UI.Text debugPhaseText;

        // 外部アクセス（ContactAnimator・Controller用）
        public Transform PlayerTransform => playerTransform;
        public SpriteRenderer PlayerSprite => playerSprite;
        public WorldBattleGridView BattleGrid => battleGridView;

        /// <summary>探索・戦闘で共通の、論理1マスあたりの表示単位</summary>
        public float CellSize => battleGridView != null ? battleGridView.CellSize : 1f;
        public float LogicalToWorld(float logicalDistance) => logicalDistance * CellSize;

        /// <summary>足元をパネル中央より少し上へ置く、探索・戦闘共通の表示差分。</summary>
        public float CharacterDisplayOffsetY => battleGridView != null ? -battleGridView.CellHeight * 0.45f : 0f;
        private Vector3? playerSpriteBaseLocalPosition;

        private void ApplyCharacterDisplayOffset()
        {
            // 論理位置・接敵演出が使う親Transformは動かさず、描画用の子だけを下げる。
            if (playerSprite == null || playerTransform == null || playerSprite.transform == playerTransform) return;
            if (!playerSpriteBaseLocalPosition.HasValue)
                playerSpriteBaseLocalPosition = playerSprite.transform.localPosition;
            playerSprite.transform.localPosition = playerSpriteBaseLocalPosition.Value
                + playerSprite.transform.parent.InverseTransformVector(Vector3.up * CharacterDisplayOffsetY);
        }

        /// <summary>Enemy に対応する WorldEnemyView を返す（見つからなければ null）</summary>
        public WorldEnemyView GetEnemyView(Olca.Core.Domain.Enemy enemy)
        {
            foreach (var view in enemyViews)
                if (view != null && view.TrackedEnemy == enemy)
                    return view;
            return null;
        }

        private PlayerCharacter damageTrackedPlayer;
        private int previousPlayerHp;
        private CancellationTokenSource playerDamageCts;
        private Vector3 playerNormalScale;
        private Color playerNormalColor;
        private float playerHitElapsed = -1f;

        private DungeonSystem dungeonSystem;
        private BattleSystem currentBattle; // バトル中のプレイヤー座標更新用
        private readonly List<WorldEnemyView> enemyViews = new List<WorldEnemyView>();
        private bool cameraFollowEnabled = true;
        private float? battleCameraTargetX = null;  // バトル開始時のカメラ移動先（スムーズlerp用）
        private bool playerPositionUpdateEnabled = true;  // 復帰ジャンプ中はfalseにしてY座標の上書きを防ぐ

        // HPの通知ごとに差分を取り、同フレームの連続被弾も個別に表示する。
        private void BindPlayerDamage(PlayerCharacter player)
        {
            UnbindPlayerDamage();
            damageTrackedPlayer = player;
            if (player == null || !isActiveAndEnabled) return;
            previousPlayerHp = player.CurrentHp;
            if (playerSprite != null)
            {
                playerNormalScale = playerSprite.transform.localScale;
                playerNormalColor = playerSprite.color;
            }
            player.OnHpChanged += OnPlayerHpChanged;
        }

        private void OnPlayerHpChanged(int currentHp, int maxHp)
        {
            int damage = previousPlayerHp - currentHp;
            previousPlayerHp = currentHp;
            if (damage <= 0 || !isActiveAndEnabled || playerSprite == null) return;

            playerDamageCts ??= new CancellationTokenSource();
            var bounds = playerSprite.bounds;
            var position = new Vector3(bounds.center.x, bounds.max.y + 0.2f, bounds.center.z - 0.5f);
            worldDamageNumberPool?.ShowDamage(damage, position, playerDamageCts.Token);
            // 連続被弾でも倍率を累積させず、同じ基準サイズから再生する。
            playerHitElapsed = 0f;
            UpdatePlayerHitEffect(0f);
        }

        private void UpdatePlayerHitEffect(float deltaTime)
        {
            if (playerHitElapsed < 0f || playerSprite == null) return;
            playerHitElapsed += deltaTime;
            // 既存の敵演出と同じ1.25倍・往復0.16秒のスケールパルス。
            float pulse = Mathf.Clamp01(1f - Mathf.Abs(playerHitElapsed - 0.08f) / 0.08f);
            playerSprite.transform.localScale = playerNormalScale * (1f + 0.25f * pulse);
            // 通常被弾仕様の白・赤、50ms×3フレームの短い点滅。
            playerSprite.color = playerHitElapsed < 0.15f
                ? ((int)(playerHitElapsed / 0.05f) % 2 == 0 ? new Color(1f, 0.4f, 0.4f, playerNormalColor.a) : Color.white)
                : playerNormalColor;
            if (playerHitElapsed >= 0.16f)
            {
                playerSprite.transform.localScale = playerNormalScale;
                playerSprite.color = playerNormalColor;
                playerHitElapsed = -1f;
            }
        }

        private void StopPlayerDamageFeedback()
        {
            playerDamageCts?.Cancel();
            playerDamageCts?.Dispose();
            playerDamageCts = null;
            if (playerHitElapsed >= 0f && playerSprite != null)
            {
                playerSprite.transform.localScale = playerNormalScale;
                playerSprite.color = playerNormalColor;
            }
            playerHitElapsed = -1f;
        }

        private void UnbindPlayerDamage()
        {
            if (damageTrackedPlayer != null)
                damageTrackedPlayer.OnHpChanged -= OnPlayerHpChanged;
            damageTrackedPlayer = null;
            StopPlayerDamageFeedback();
        }

        private void OnEnable()
        {
            if (dungeonSystem != null) BindPlayerDamage(dungeonSystem.Player);
        }

        private void OnDisable() => UnbindPlayerDamage();
        private void OnDestroy()
        {
            UnbindPlayerDamage();
            if (currentBattle != null)
            {
                currentBattle.OnSummonAdded -= OnSummonAdded;
                currentBattle.OnBattleEnd -= OnSummonBattleEnd;
            }
        }

        // ==================== 初期化 ====================

        /// <summary>ダンジョン初期化。マップ上の全敵スプライトを生成する。</summary>
        public void Initialize(DungeonSystem dungeon)
        {
            ApplyCharacterDisplayOffset();
            dungeonSystem = dungeon;
            BindPlayerDamage(dungeon?.Player);
            ResetMovementAnimation();
            cameraFollowEnabled = true;
            currentFollowSpeed = cameraFollowSpeed;  // 初期値は通常速度
            UpdateCameraFraming();

            // カメラを初期位置にスナップ（ダンジョン開始時のヌルっとした動きを防ぐ）
            if (fieldCamera != null)
            {
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = LogicalToWorld(dungeon.PlayerPosition) + CameraOffsetX;
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
            UpdatePlayerSprite();
        }

        private void UpdateCameraFraming()
        {
            if (fieldCamera == null || !fieldCamera.orthographic || fieldCamera.aspect <= 0f) return;
            // 探索・バトル共通で横7マス。縦方向は実際の画面比率から求める。
            float cellWidth = CellSize;
            fieldCamera.orthographicSize = 7f * cellWidth / (2f * fieldCamera.aspect);
            // 地面を下寄りに置き、拡大後も頭部・敵名の表示領域を確保する。
            float groundY = battleGridView != null ? battleGridView.GroundY : -0.25f;
            var position = fieldCamera.transform.position;
            position.y = groundY + fieldCamera.orthographicSize * 0.5f;
            fieldCamera.transform.position = position;
        }

        private void LateUpdate()
        {
            UpdateCameraFraming();
            // Controllerの論理状態から姿勢を選ぶ。描画だけのフレームでも移動姿勢を維持。
            UpdateMovementAnimation();
            UpdatePlayerSprite();
            UpdatePlayerHitEffect(Olca.Unity.GameplayPause.DeltaTime);
        }

        /// <summary>マップ上の全敵スプライトを配置（初期化時・ダンジョン復帰時に呼ぶ）</summary>
        private void PlaceAllEnemySprites()
        {
            // 既存スプライトを削除
            foreach (var view in enemyViews)
            {
                if (view == null) continue;
                // 同フレームの階層交換でも旧敵を表示し続けない。Editor上の再初期化も扱う。
                view.gameObject.SetActive(false);
                if (UnityEngine.Application.isPlaying) Destroy(view.gameObject);
                else DestroyImmediate(view.gameObject);
            }
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
            GameObject go;
            if (enemyViewPrefab != null)
            {
                go = Instantiate(enemyViewPrefab, transform);
            }
            else
            {
                // フォールバック: 白スプライト + SpriteRenderer のシンプル表示
                go = new GameObject($"Enemy_{enemy.Name}");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.color = defaultEnemyColor;
                sr.sortingOrder = 2;
                sr.sprite = CreateWhiteSprite();
                go.AddComponent<Olca.Presentation.Battle.EnemyView>();
                go.AddComponent<WorldEnemyView>();
            }

            go.transform.position = new Vector3(LogicalToWorld(dungeonX), enemyY + CharacterDisplayOffsetY, enemyZ);
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            var view = go.GetComponent<WorldEnemyView>();
            view.Initialize(enemy, LogicalToWorld(dungeonX));
            view.SetDamageNumberPool(worldDamageNumberPool);
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
            if (currentBattle != null)
            {
                currentBattle.OnSummonAdded -= OnSummonAdded;
                currentBattle.OnBattleEnd -= OnSummonBattleEnd;
            }
            currentBattle = battle;
            currentBattle.OnSummonAdded += OnSummonAdded;
            currentBattle.OnBattleEnd += OnSummonBattleEnd;
            ResetMovementAnimation();

            // グリッド原点をプレイヤーの現在位置から逆算
            // → プレイヤーをワープさせず、グリッドをプレイヤーに合わせて出現させる
            float originX = LogicalToWorld(contactPositionX);
            if (playerTransform != null)
                originX = playerTransform.position.x - LogicalToWorld(battle.Player.GridPosition.X);

            battleGridView?.Show(battle.Field, originX);

            Debug.Log($"[Camera] OnBattleStarted: playerPos={playerTransform?.position.x:F2}, gridColumn={battle.Player.GridPosition.X}, originX={originX:F2}, gridCenterX={battleGridView?.GridCenterWorldX:F2}, cameraX={fieldCamera?.transform.position.x:F2}, CameraOffsetX={CameraOffsetX:F2}");

            // カメラ追従を停止し、グリッド中央へのスムーズ移動を開始
            cameraFollowEnabled = false;
            if (battleGridView != null)
                battleCameraTargetX = battleGridView.GridCenterWorldX;

            foreach (var enemy in battle.Enemies)
                if (enemy.IsSummoned && GetEnemyView(enemy) == null) OnSummonAdded(enemy);

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
            StopPlayerDamageFeedback();
            if (currentBattle != null)
            {
                currentBattle.OnSummonAdded -= OnSummonAdded;
                currentBattle.OnBattleEnd -= OnSummonBattleEnd;
            }
            currentBattle = null;
            ClearSummonViews();
            ResetMovementAnimation();
            battleGridView?.Hide();
            battleCameraTargetX = null;
            // cameraFollowEnabled はここで再開しない
            // → SnapCameraToPlayerAndResumeFollow() でカード選択後にスナップしてから再開する

            foreach (var view in enemyViews)
                view?.OnBattleEnded();
        }

        /// <summary>
        /// カード報酬選択終了後に呼ぶ。
        /// カメラをプレイヤー現在位置に即スナップしてから追従を再開することで
        /// カメラ飛び（ジャンプ）を防ぐ。
        /// </summary>
        public void SnapCameraToPlayerAndResumeFollow()
        {
            ResetMovementAnimation();
            if (playerTransform != null && fieldCamera != null)
            {
                // カメラをプレイヤー位置に即スナップ（Lerpなし）
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = playerTransform.position.x + CameraOffsetX;
                fieldCamera.transform.position = camPos;
            }
            cameraFollowEnabled = true;
        }

        // ==================== 毎フレーム更新 ====================

        /// <summary>ダンジョンフェーズ更新（毎フレーム）</summary>
        public void UpdateView()
        {
            if (dungeonSystem == null) return;
            UpdatePlayerPosition();
            UpdatePlayerSprite();
            if (cameraFollowEnabled) UpdateCamera();
            UpdateEnemyViews();
        }

        /// <summary>バトルフェーズ更新（毎フレーム）</summary>
        public void RefreshBattleField()
        {
            // バトル中のプレイヤー位置をグリッド座標からワールド座標に変換して反映
            if (playerTransform != null && battleGridView != null && currentBattle != null)
            {
                float worldX = battleGridView.GridIndexToWorldX(currentBattle.Player.GridPosition.X);
                playerTransform.position = new Vector3(worldX, playerTransform.position.y, 0f);
            }

            // 通常接敵アニメーション中: プレイヤーに追従（cameraFollowEnabled=true）
            if (cameraFollowEnabled) UpdateCamera();

            // バトル開始後: グリッド中央へスムーズ移動（cameraFollowEnabled=false かつ目標あり）
            if (!cameraFollowEnabled && battleCameraTargetX.HasValue && fieldCamera != null)
            {
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = Mathf.Lerp(camPos.x, battleCameraTargetX.Value, Olca.Unity.GameplayPause.DeltaTime * cameraFollowSpeed);
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
            ResetMovementAnimation();
        }

        // ==================== 内部処理 ====================

        /// <summary>復帰ジャンプ中はfalseにするとUpdatePlayerPositionによるY座標上書きを抑制できる</summary>
        public void SetPlayerPositionUpdateEnabled(bool enabled)
        {
            playerPositionUpdateEnabled = enabled;
            ResetMovementAnimation();
        }

        private void UpdatePlayerPosition()
        {
            if (!playerPositionUpdateEnabled) return;
            if (playerTransform == null) return;
            playerTransform.position = new Vector3(
                LogicalToWorld(dungeonSystem.PlayerPosition), LogicalToWorld(dungeonSystem.PlayerHeight), 0f);
        }

        /// <summary>
        /// 30論理フレームの既存ジャンプを、上昇・頂点付近・下降の表示だけに分ける。
        /// </summary>
        public static int SelectJumpAnimationPhase(bool jumping, float progress)
            => !jumping ? 0 : progress < 14f / 30f ? 1 : progress <= 16f / 30f ? 2 : 3;

        private void UpdatePlayerSprite()
        {
            if (contactPoseActive && contactPoseSprite != null) { SetPlayerSprite(contactPoseSprite); return; }
            int jumpPhase = SelectJumpAnimationPhase(dungeonSystem != null && dungeonSystem.IsJumping
                && !dungeonSystem.IsInBattle && playerPositionUpdateEnabled && cameraFollowEnabled,
                dungeonSystem?.JumpProgress ?? 0f);
            // 接敵後の宙返り中はダッシュフラグが残る場合があるため、
            // バトル中でないこともダッシュ表示の条件にする。
            bool isDashing = dungeonSystem != null
                && dungeonSystem.IsDashing
                && dungeonSystem.IsExplorationMoving && playerPositionUpdateEnabled && cameraFollowEnabled
                && Olca.Unity.GameplayPause.DeltaTime > 0f;

            // Animatorを設定した場合は、状態だけを渡してスプライトの再生を任せる。
            // 未設定のシーンでは従来のモック画像表示を使用する。
            if (playerAnimator != null && playerAnimator.runtimeAnimatorController != null)
            {
                playerAnimator.SetBool(IsRunningParameter, isDashing);
                playerAnimator.SetBool(IsMovingParameter, isMoving && !isDashing);
                playerAnimator.SetInteger(JumpPhaseParameter, jumpPhase);
                // LateUpdateでも論理状態と同じ描画フレームに姿勢を切り替える。時間は進めない。
                if (appliedJumpPhase != jumpPhase) playerAnimator.Update(0f);
                appliedJumpPhase = jumpPhase;
                return;
            }

            var jumpSprite = jumpPhase == 1 ? jumpRisePlayerSprite : jumpPhase == 2 ? jumpApexPlayerSprite : jumpPhase == 3 ? jumpFallPlayerSprite : null;
            SetPlayerSprite(jumpSprite != null ? jumpSprite : isDashing ? dashPlayerSprite : idlePlayerSprite);
        }

        private void ResetMovementAnimation()
        {
            isMoving = false;
            if (playerAnimator != null && playerAnimator.runtimeAnimatorController != null)
                playerAnimator.SetBool(IsMovingParameter, false);
        }

        private void UpdateMovementAnimation()
        {
            // 60Hz論理更新のない描画フレームも自動前進は継続中。
            // 座標差0や描画DeltaTimeでの速度推定を停止判定に使わない。
            isMoving = dungeonSystem != null && playerTransform != null
                && dungeonSystem.IsExplorationMoving
                && playerPositionUpdateEnabled && cameraFollowEnabled
                && dungeonSystem.PlayerHeight <= 0.001f && Olca.Unity.GameplayPause.DeltaTime > 0f;
        }

        private void SetPlayerSprite(Sprite sprite)
        {
            if (playerSprite == null || sprite == null || playerSprite.sprite == sprite) return;
            playerSprite.sprite = sprite;
        }

        private void UpdateCamera()
        {
            if (fieldCamera == null || playerTransform == null) return;
            Vector3 targetPos = new Vector3(
                playerTransform.position.x + CameraOffsetX,
                fieldCamera.transform.position.y,
                fieldCamera.transform.position.z);

            // 離れすぎた場合はターゲット位置に直接スナップ（ダッシュ通常ラグでは発動しない）
            float distX = Mathf.Abs(fieldCamera.transform.position.x - targetPos.x);
            if (distX > cameraMaxLagDistance)
            {
                Debug.Log($"[Camera] SNAP: camX={fieldCamera.transform.position.x:F2} → targetX={targetPos.x:F2} (distX={distX:F2}, maxLag={cameraMaxLagDistance:F2})");
                Vector3 snapped = fieldCamera.transform.position;
                snapped.x = targetPos.x;
                fieldCamera.transform.position = snapped;
            }

            bool isDashing = dungeonSystem != null && dungeonSystem.IsDashing;
            if (!isDashing)
            {
                // 通常移動: プレイヤーに直接追従（Lerpなし・ラグなし）
                fieldCamera.transform.position = targetPos;
                currentFollowSpeed = cameraFollowSpeed;  // ダッシュ開始時のLerp初期値としてリセット
            }
            else
            {
                // ダッシュ中: Lerpで追従（ダッシュ開始・終了の切り替えをなめらかに）
                currentFollowSpeed = Mathf.Lerp(currentFollowSpeed, cameraFollowSpeedDash, Olca.Unity.GameplayPause.DeltaTime * cameraFollowSpeedTransition);
                fieldCamera.transform.position = Vector3.Lerp(
                    fieldCamera.transform.position, targetPos,
                    Olca.Unity.GameplayPause.DeltaTime * currentFollowSpeed);
            }

            // 定期ログ（60フレームに1回）
            if (Time.frameCount % 60 == 0)
                Debug.Log($"[Camera] camX={fieldCamera.transform.position.x:F2}, playerX={playerTransform.position.x:F2}, targetX={targetPos.x:F2}, offset={CameraOffsetX:F2}, distX={distX:F2}");
        }

        private void OnSummonBattleEnd(BattleResult result)
        {
            // 敗北ではダンジョン復帰処理を通らないため、勝敗通知で確実に破棄する。
            if (currentBattle != null)
            {
                currentBattle.OnSummonAdded -= OnSummonAdded;
                currentBattle.OnBattleEnd -= OnSummonBattleEnd;
            }
            ClearSummonViews();
        }

        private void ClearSummonViews()
        {
            for (int i = enemyViews.Count - 1; i >= 0; i--)
            {
                var view = enemyViews[i];
                if (view != null && view.TrackedEnemy.IsSummoned)
                {
                    view.gameObject.SetActive(false);
                    Destroy(view.gameObject);
                    enemyViews.RemoveAt(i);
                }
            }
        }

        private void OnSummonAdded(Enemy summon)
        {
            var view = CreateEnemyView(summon, 0f);
            enemyViews.Add(view);
            view.OnBattleStarted(battleGridView);
        }

        private void UpdateEnemyViews()
        {
            foreach (var view in enemyViews)
                view?.Refresh();
        }

        /// <summary>探索・戦闘・報酬・終了を問わず、同じ更新で実状態を表示する。</summary>
        private UnityEngine.UI.Text instructionText;
        private InputBindingProfile displayedKeyboardProfile;
        private GamePhase displayedInstructionPhase;

        public static string FormatGameplayInstructions(InputBindingProfile profile, GamePhase phase)
        {
            if (profile == null) return string.Empty;
            string Keys(string context, params string[] actions)
            {
                var values = new List<string>();
                foreach (var action in actions)
                {
                    var key = profile.Bindings[("keyboard", context, action)];
                    values.Add(key ?? "未割当");
                }
                return string.Join(" / ", values);
            }
            if (phase == GamePhase.Battle)
                return $"{Keys("battle", "LeftPrimary", "LeftSecondary")}: 左  {Keys("battle", "RightPrimary", "RightSecondary")}: 右  " +
                    $"{Keys("battle", "UpPrimary", "UpSecondary")}: 上  {Keys("battle", "DownPrimary", "DownSecondary")}: 下\n" +
                    $"{Keys("battle", "AttackPrimary")}: 固有攻撃  {Keys("battle", "CardPrimary")}: カード使用  {Keys("battle", "CustomPrimary", "CustomSecondary")}: カスタム";
            if (phase == GamePhase.Dungeon)
                return $"自動前進  {Keys("exploration", "JumpPrimary")}: ジャンプ  {Keys("exploration", "DashPrimary", "DashSecondary")}: ダッシュ";
            return string.Empty;
        }

        public void RefreshGameplayInstructions(InputBindingProfile appliedProfile, GamePhase phase)
        {
            if (instructionText == null)
            {
                // 既存シーンの説明欄を再利用し、他シーンの同名UIを変更しない。
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                        if (text.name == "InstructionText") { instructionText = text; break; }
                if (instructionText == null) return;
                var rect = instructionText.rectTransform;
                rect.anchorMin = new Vector2(.05f, 0); rect.anchorMax = new Vector2(.95f, 0);
                rect.sizeDelta = new Vector2(0, 52); rect.anchoredPosition = new Vector2(0, 30);
            }
            if (ReferenceEquals(displayedKeyboardProfile, appliedProfile) && displayedInstructionPhase == phase) return;
            displayedKeyboardProfile = appliedProfile; displayedInstructionPhase = phase;
            instructionText.text = FormatGameplayInstructions(appliedProfile, phase);
        }

        public void RefreshDebugUI(GamePhase phase)
        {
            if (debugPhaseText != null) debugPhaseText.text = $"{phase} Phase";
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
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(from, to, t);
                await Olca.Unity.GameplayPause.NextFrame(ct);
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
