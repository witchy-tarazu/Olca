using System.Linq;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] private Sprite[] somersaultSprites;
        private Sprite[] generatedSomersaultSprites;

        // 通常起動でも専用6コマを供給する。Sceneの明示参照があればそちらを優先する。
        private void EnsureSomersaultSprites()
        {
            if (somersaultSprites != null && somersaultSprites.Length > 0) return;
            var textures = new Texture2D[6];
            for (int i = 0; i < textures.Length; i++) {
                textures[i] = Resources.Load<Texture2D>("OlcaCharacterMotion/BackJump/backjump_" + (i + 1).ToString("00"));
                if (textures[i] == null || textures[i].width != 256 || textures[i].height != 256) return;
            }
            var frames = new Sprite[textures.Length];
            for (int i = 0; i < frames.Length; i++) {
                textures[i].filterMode = FilterMode.Point;
                frames[i] = Sprite.Create(textures[i], new Rect(0, 0, 256, 256), new Vector2(127f / 256f, 14f / 256f), 100f);
                frames[i].name = "backjump_" + (i + 1).ToString("00");
            }
            generatedSomersaultSprites = frames;
            somersaultSprites = frames;
        }

        private Sprite contactPoseSprite;
        private Sprite contactPreviousSprite;
        private bool contactAnimatorWasEnabled;
        private bool contactPoseActive;
        private ActionPoseRenderer actionPoseRenderer;
        private ProjectilePoseRenderer projectilePoseRenderer;
        public void ConfigureProjectileAppearances(IEnumerable<ProjectileAppearance> appearances)
        {
            if (projectilePoseRenderer == null) projectilePoseRenderer = new ProjectilePoseRenderer(transform);
            projectilePoseRenderer.Configure(appearances);
        }

        /// <summary>明示登録された共用素材をアクション表示へ接続する。未登録素材は代用しない。</summary>
        public void ConfigureActionAppearances(IEnumerable<ActionAppearance> appearances)
        {
            if (actionPoseRenderer == null) actionPoseRenderer = new ActionPoseRenderer(playerSprite, playerAnimator);
            actionPoseRenderer.Configure(appearances);
        }

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
            Sprite[] frames = phase==Olca.Presentation.Dungeon.ContactVisualPhase.Somersault?somersaultSprites:contactKnockbackSprites;
            Sprite sprite = frames == null || frames.Length == 0 ? null : frames[SelectContactFrame(phase, progress, frames.Length)];
            if(sprite==null){if(contactPoseActive)SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase.None,0f);return;} // 未登録は別モーションや回転で代替しない。
            if (!contactPoseActive) {
                actionPoseRenderer?.Clear();
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
        private bool battleMoveHidden, battleMoveRendererWasEnabled;
        public static bool IsBattleMoveVisible(bool moving, float progress)
            => !moving || progress < .2f || progress >= .8f;
        private void SetBattleMoveVisibility(bool visible)
        {
            if (playerSprite == null) return;
            if (!visible && !battleMoveHidden) { battleMoveRendererWasEnabled = playerSprite.enabled; battleMoveHidden = true; }
            if (battleMoveHidden) playerSprite.enabled = visible && battleMoveRendererWasEnabled;
            if (visible) battleMoveHidden = false;
        }


        [Header("カメラ")]
        [SerializeField] private Camera fieldCamera;
        [SerializeField] private float cameraFollowSpeed = 50f;            // 通常時の追従速度
        [SerializeField] private float cameraFollowSpeedDash = 15f;       // ダッシュ時の追従速度（プレイヤーが前に出る演出）
        [SerializeField] private float cameraFollowSpeedTransition = 3f;  // 速度切替のなめらかさ（小さいほどゆっくり）

        private float currentFollowSpeed;  // 実際に使用する追従速度（Lerpで遷移）
        private DungeonSystem cameraObservedDungeon;
        private long cameraObservedFrame;
        private Vector3 cameraObservedTarget;
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
        private Olca.Presentation.Effects.DefensiveFieldVisual defensiveField;
        private Olca.Presentation.Effects.ActorConditionVisual guardVisual, paralysisVisual;
        private void RefreshConditionVisuals()
        {
            var player = currentBattle?.Player ?? dungeonSystem?.Player;
            if (!isActiveAndEnabled || player == null || player.IsDead || currentBattle?.IsGameOver == true ||
                dungeonSystem == null || dungeonSystem.IsDungeonCleared || dungeonSystem.IsFloorCompleted || dungeonSystem.IsInBattle && currentBattle == null) { guardVisual?.Clear(); paralysisVisual?.Clear(); return; }
            if (guardVisual == null && player.Guard.IsActive) guardVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Guard);
            if (paralysisVisual == null && player.IsParalyzed) paralysisVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Paralyze);
            guardVisual?.Refresh(playerSprite, player.Guard.IsActive); paralysisVisual?.Refresh(playerSprite, player.IsParalyzed);
        }
        private void RefreshDefensiveField()
        {
            var player = currentBattle?.Player ?? dungeonSystem?.Player;
            if (!isActiveAndEnabled || player == null || player.IsDead || currentBattle?.IsGameOver == true ||
                dungeonSystem == null || dungeonSystem.IsDungeonCleared || dungeonSystem.IsFloorCompleted || dungeonSystem.IsInBattle && currentBattle == null) { defensiveField?.Clear(); return; }
            if (defensiveField == null && (player.HasBarrier || player.HasAura)) defensiveField = new Olca.Presentation.Effects.DefensiveFieldVisual();
            defensiveField?.Refresh(playerSprite, player.HasBarrier, player.HasAura);
        }
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

        private readonly Dictionary<int,GameObject> fieldItemVisuals=new Dictionary<int,GameObject>();
        private void RemoveFieldItemVisual(int cell)
        {
            if(!fieldItemVisuals.TryGetValue(cell,out var root))return;fieldItemVisuals.Remove(cell);
            if(root==null)return;root.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(root);else DestroyImmediate(root);
        }
        private void ClearFieldItemVisuals(){foreach(var cell in new List<int>(fieldItemVisuals.Keys))RemoveFieldItemVisual(cell);}
        /// <summary>未取得の画面内アイテムだけを表示する。取得後は即時に消し、描画で効果を適用しない。</summary>
        public void RefreshFieldItemVisuals()
        {
            if(!isActiveAndEnabled||dungeonSystem==null||fieldCamera==null||dungeonSystem.IsInBattle||dungeonSystem.IsDungeonCleared||dungeonSystem.IsFloorCompleted||dungeonSystem.Player.IsDead){ClearFieldItemVisuals();return;}
            float distance=Mathf.Abs(fieldCamera.transform.position.z),a=fieldCamera.ViewportToWorldPoint(new Vector3(0,0,distance)).x,b=fieldCamera.ViewportToWorldPoint(new Vector3(1,0,distance)).x;
            var retained=new HashSet<int>();
            for(int i=0;i<dungeonSystem.Map.TotalCellCount;i++){
                var cell=dungeonSystem.Map.GetCell(i);if(cell?.Item==null||dungeonSystem.IsItemCollected(i))continue;
                float x=LogicalToWorld(i+.5f),half=LogicalToWorld(.2f);if(x-half<Mathf.Min(a,b)||x+half>Mathf.Max(a,b))continue;
                retained.Add(i);if(fieldItemVisuals.ContainsKey(i))continue;
                if(obstacleWhiteSprite==null){obstacleWhiteSprite=CreateWhiteSprite();obstacleWhiteSprite.name="ExplorationPrimitive";}
                var root=new GameObject("FieldHealCell_"+i);root.transform.SetParent(transform,false);root.transform.position=new Vector3(x,CharacterDisplayOffsetY+LogicalToWorld(.45f),0);fieldItemVisuals.Add(i,root);
                void Part(string name,float px,float py,float w,float h,float alpha){var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(LogicalToWorld(px),LogicalToWorld(py),0);go.transform.localScale=new Vector3(LogicalToWorld(w),LogicalToWorld(h),1);var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=obstacleWhiteSprite;renderer.color=new Color(.35f,1f,.7f,alpha);renderer.sortingLayerID=playerSprite!=null?playerSprite.sortingLayerID:0;renderer.sortingOrder=playerSprite!=null?playerSprite.sortingOrder-1:2;}
                Part("Face",0,0,.34f,.34f,.08f);Part("Top",0,.17f,.34f,.014f,.6f);Part("Bottom",0,-.17f,.34f,.014f,.6f);Part("Left",-.17f,0,.014f,.34f,.6f);Part("Right",.17f,0,.014f,.34f,.6f);Part("HealHorizontal",0,0,.24f,.055f,1);Part("HealVertical",0,0,.055f,.24f,1);
            }
            foreach(var cell in new List<int>(fieldItemVisuals.Keys))if(!retained.Contains(cell))RemoveFieldItemVisual(cell);
        }

        private sealed class VisitVisual
        {
            public GameObject Root,Near,Check;
            public readonly List<(SpriteRenderer Renderer,float Alpha)> Parts=new List<(SpriteRenderer,float)>();
        }
        private readonly Dictionary<int,VisitVisual> visitVisuals=new Dictionary<int,VisitVisual>();
        private Sprite visitShopSprite;
        // bytes包装から生成したSprite/TextureだけをこのViewが所有する。
        private Sprite GetVisitShopSprite()
        {
            if(visitShopSprite!=null)return visitShopSprite;
            var source=Resources.Load<TextAsset>("OlcaVisits/shop_bag");if(source==null)return null;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!texture.LoadImage(source.bytes,true)){
                if(UnityEngine.Application.isPlaying)Destroy(texture);else DestroyImmediate(texture);return null;
            }
            texture.name="VisitShopIcon";texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.hideFlags=HideFlags.HideAndDontSave;
            visitShopSprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),64,0,SpriteMeshType.FullRect);
            visitShopSprite.name="VisitShopIcon";visitShopSprite.hideFlags=HideFlags.HideAndDontSave;
            return visitShopSprite;
        }
        private void DisposeVisitShopSprite()
        {
            if(visitShopSprite==null)return;var texture=visitShopSprite.texture;
            if(UnityEngine.Application.isPlaying){Destroy(visitShopSprite);Destroy(texture);}else{DestroyImmediate(visitShopSprite);DestroyImmediate(texture);}
            visitShopSprite=null;
        }

        private void RemoveVisitVisual(int cell)
        {
            if(!visitVisuals.TryGetValue(cell,out var visual))return;visitVisuals.Remove(cell);
            if(visual.Root==null)return;visual.Root.SetActive(false);
            if(UnityEngine.Application.isPlaying)Destroy(visual.Root);else DestroyImmediate(visual.Root);
        }
        private void ClearVisitVisuals(){ClearFieldItemVisuals();foreach(var cell in new List<int>(visitVisuals.Keys))RemoveVisitVisual(cell);}
        private VisitVisual CreateVisitVisual(DungeonVisitDefinition definition)
        {
            if(obstacleWhiteSprite==null){obstacleWhiteSprite=CreateWhiteSprite();obstacleWhiteSprite.name="ExplorationPrimitive";}
            var visual=new VisitVisual{Root=new GameObject("VisitCell_"+definition.Cell)};visual.Root.transform.SetParent(transform,false);
            visual.Root.transform.position=new Vector3(LogicalToWorld(definition.Cell+.5f),CharacterDisplayOffsetY+LogicalToWorld(1.65f),0);
            void Part(string name,float x,float y,float width,float height,float alpha,Transform parent=null,float angle=0){
                var go=new GameObject(name);go.transform.SetParent(parent??visual.Root.transform,false);go.transform.localPosition=new Vector3(LogicalToWorld(x),LogicalToWorld(y),0);
                go.transform.localScale=new Vector3(LogicalToWorld(width),LogicalToWorld(height),1);go.transform.localRotation=Quaternion.Euler(0,0,angle);
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=obstacleWhiteSprite;renderer.sortingLayerID=playerSprite!=null?playerSprite.sortingLayerID:0;
                renderer.sortingOrder=playerSprite!=null?playerSprite.sortingOrder-1:2;visual.Parts.Add((renderer,alpha));
            }
            Part("Face",0,0,.46f,.4f,.08f);Part("Top",0,.2f,.46f,.014f,.9f);Part("Bottom",0,-.2f,.46f,.014f,.9f);
            Part("Left",-.23f,0,.014f,.4f,.9f);Part("Right",.23f,0,.014f,.4f,.9f);
            if(definition.IsShop){
                var sprite=GetVisitShopSprite();
                if(sprite!=null){Part("ShopIcon",0,0,.4f,.4f,1);visual.Parts[visual.Parts.Count-1].Renderer.sprite=sprite;}
                else{
                    // 包装がない場合だけ従来の識別図形を保つ。
                    Part("BagBase",0,-.07f,.24f,.018f,1);Part("BagLeft",-.12f,.01f,.018f,.17f,1);Part("BagRight",.12f,.01f,.018f,.17f,1);
                    Part("BagHandle",0,.12f,.12f,.018f,1);Part("HandleLeft",-.06f,.09f,.018f,.06f,1);Part("HandleRight",.06f,.09f,.018f,.06f,1);
                }
            }else{Part("RecoveryHorizontal",0,0,.25f,.055f,1);Part("RecoveryVertical",0,0,.055f,.25f,1);}
            visual.Near=new GameObject("Near");visual.Near.transform.SetParent(visual.Root.transform,false);
            Part("ArrowLeft",-.04f,-.29f,.11f,.018f,.95f,visual.Near.transform,-35);Part("ArrowRight",.04f,-.29f,.11f,.018f,.95f,visual.Near.transform,35);
            visual.Check=new GameObject("Check");visual.Check.transform.SetParent(visual.Root.transform,false);
            Part("CheckShort",-.05f,-.29f,.1f,.025f,1,visual.Check.transform,-45);Part("CheckLong",.04f,-.27f,.19f,.025f,1,visual.Check.transform,45);
            return visual;
        }
        /// <summary>画面内の既存停止地点だけを表示。時間・ゲーム状態・保存を変更しない。</summary>
        public void RefreshVisitVisuals()
        {
            RefreshFieldItemVisuals();
            if(!isActiveAndEnabled||dungeonSystem==null||fieldCamera==null||dungeonSystem.IsInBattle||dungeonSystem.IsDungeonCleared||dungeonSystem.IsFloorCompleted||dungeonSystem.Player.IsDead){ClearVisitVisuals();return;}
            float distance=Mathf.Abs(fieldCamera.transform.position.z);
            float a=fieldCamera.ViewportToWorldPoint(new Vector3(0,0,distance)).x,b=fieldCamera.ViewportToWorldPoint(new Vector3(1,0,distance)).x;
            float left=Mathf.Min(a,b),right=Mathf.Max(a,b);var retained=new HashSet<int>();
            foreach(var definition in dungeonSystem.Map.Visits){
                float center=LogicalToWorld(definition.Cell+.5f),half=LogicalToWorld(.24f);if(center-half<left||center+half>right)continue;
                var state=dungeonSystem.GetVisitDisplayState(definition.Cell);if(!state.HasValue)continue;
                if(!visitVisuals.TryGetValue(definition.Cell,out var visual)){visual=CreateVisitVisual(definition);visitVisuals.Add(definition.Cell,visual);}retained.Add(definition.Cell);
                bool done=state==DungeonVisitDisplayState.Closed||state==DungeonVisitDisplayState.Used;
                bool near=!done&&definition.Cell-dungeonSystem.PlayerPosition>=0&&definition.Cell-dungeonSystem.PlayerPosition<=2;
                visual.Near.SetActive(near&&state==DungeonVisitDisplayState.Ready);visual.Check.SetActive(done);
                var tint=done?new Color(.48f,.61f,.65f):definition.IsShop?new Color(.25f,.85f,.95f):new Color(.35f,1f,.7f);
                if(state==DungeonVisitDisplayState.Active)tint=Color.Lerp(tint,Color.white,.55f);
                float opacity=done?.45f:near||state==DungeonVisitDisplayState.Active?1:.62f;
                foreach(var part in visual.Parts){var color=tint;color.a=part.Alpha*opacity;part.Renderer.color=color;}
            }
            foreach(var cell in new List<int>(visitVisuals.Keys))if(!retained.Contains(cell))RemoveVisitVisual(cell);
        }

        private sealed class ObstacleVisual
        {
            public GameObject Root; public Obstacle Definition;
            public readonly List<(SpriteRenderer Renderer,float Alpha)> Parts = new List<(SpriteRenderer,float)>();
        }
        private readonly Dictionary<int,ObstacleVisual> obstacleVisuals = new Dictionary<int,ObstacleVisual>();
        private Sprite obstacleWhiteSprite;
        public const int ObstacleFadeDurationFrames = 12;
        private void RemoveObstacleVisual(int cell)
        {
            if (!obstacleVisuals.TryGetValue(cell,out var visual)) return;
            obstacleVisuals.Remove(cell);
            if (visual.Root == null) return;
            visual.Root.SetActive(false);
            if (UnityEngine.Application.isPlaying) Destroy(visual.Root); else DestroyImmediate(visual.Root);
        }
        private void ClearObstacleVisuals()
        { foreach (var cell in new List<int>(obstacleVisuals.Keys)) RemoveObstacleVisual(cell); }
        private ObstacleVisual CreateObstacleVisual(int cell,Obstacle definition,float height)
        {
            if (obstacleWhiteSprite == null) { obstacleWhiteSprite = CreateWhiteSprite(); obstacleWhiteSprite.name="ObstaclePrimitive"; }
            var visual = new ObstacleVisual { Root=new GameObject("ObstacleCell_"+cell), Definition=definition };
            visual.Root.transform.SetParent(transform,false);
            visual.Root.transform.position = new Vector3(LogicalToWorld(cell),CharacterDisplayOffsetY+LogicalToWorld(height),0);
            void Part(string name,float x,float y,float width,float h,float alpha)
            {
                var go=new GameObject(name);go.transform.SetParent(visual.Root.transform,false);
                go.transform.localPosition=new Vector3(LogicalToWorld(x),LogicalToWorld(y),0);
                go.transform.localScale=new Vector3(LogicalToWorld(width),LogicalToWorld(h),1);
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=obstacleWhiteSprite;
                renderer.sortingLayerID=playerSprite!=null?playerSprite.sortingLayerID:0;
                renderer.sortingOrder=playerSprite!=null?playerSprite.sortingOrder-1:2;
                visual.Parts.Add((renderer,alpha));
            }
            // 幅は論理セル内。左端が既存接触位置、薄い面は占有セルだけを示す。
            Part("Area",.5f,.22f,.92f,.34f,.10f);
            Part("Lower",.5f,.05f,.96f,.018f,.85f);Part("Upper",.5f,.39f,.96f,.018f,.7f);
            Part("Contact",.02f,.25f,.025f,.48f,1f);Part("FarEdge",.98f,.22f,.018f,.34f,.7f);
            if (definition.PenaltyType==ObstaclePenaltyType.CardDiscard) {
                Part("CardA",.46f,.23f,.16f,.13f,.8f);Part("CardB",.54f,.29f,.16f,.13f,.55f);
            } else if (definition.PenaltyType==ObstaclePenaltyType.StatusEffect) {
                Part("StatusHorizontal",.5f,.25f,.18f,.04f,.95f);Part("StatusVertical",.5f,.25f,.04f,.18f,.95f);
            } else { Part("Warning",.5f,.29f,.04f,.13f,1);Part("Dot",.5f,.17f,.04f,.04f,1); }
            return visual;
        }
        /// <summary>読み取り専用モデルから描画を同期。表示完了を判定/消費の条件にしない。</summary>
        public void RefreshObstacleVisuals()
        {
            if (dungeonSystem==null || fieldCamera==null || dungeonSystem.IsInBattle || dungeonSystem.IsDungeonCleared || dungeonSystem.IsFloorCompleted || dungeonSystem.Player.IsDead) { ClearObstacleVisuals(); ClearVisitVisuals();return; }
            float distance=Mathf.Abs(fieldCamera.transform.position.z);
            float left=fieldCamera.ViewportToWorldPoint(new Vector3(0,0,distance)).x;
            float right=fieldCamera.ViewportToWorldPoint(new Vector3(1,0,distance)).x;
            int first=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(left,right)/CellSize));
            int last=Mathf.Min(dungeonSystem.Map.TotalCellCount-1,Mathf.CeilToInt(Mathf.Max(left,right)/CellSize)-1);
            var retained=new HashSet<int>();
            for(int cell=first;cell<=last;cell++) {
                var model=dungeonSystem.Map.GetCell(cell);if(model?.Obstacle==null)continue;
                bool active=dungeonSystem.ActiveObstacleCell==cell;
                bool consumed=dungeonSystem.IsObstacleConsumed(cell);
                obstacleVisuals.TryGetValue(cell,out var visual);
                if(visual!=null && !ReferenceEquals(visual.Definition,model.Obstacle)){RemoveObstacleVisual(cell);visual=null;}
                // 読込済み/再入場した消費済みセルには消滅演出を再生成しない。
                if(visual==null && consumed && !active)continue;
                float opacity=1;
                if(consumed && !active) {
                    var start=dungeonSystem.GetObstacleFadeStartFrame(cell);
                    opacity=start.HasValue?Mathf.Clamp01(1-(dungeonSystem.ObstacleVisualFrame-start.Value)/(float)ObstacleFadeDurationFrames):0;
                    if(opacity<=0)continue;
                }
                if(visual==null){visual=CreateObstacleVisual(cell,model.Obstacle,model.Height);obstacleVisuals.Add(cell,visual);}
                retained.Add(cell);
                Color tint=active?new Color(.91f,.3f,.3f):model.Obstacle.PenaltyType==ObstaclePenaltyType.CardDiscard?new Color(.2f,.85f,.95f):model.Obstacle.PenaltyType==ObstaclePenaltyType.StatusEffect?new Color(.7f,.5f,1):new Color(1,.65f,.15f);
                float pulse=active && dungeonSystem.ObstacleVisualFrame%30<15?.8f:1;
                foreach(var part in visual.Parts){var color=tint;color.a=part.Alpha*opacity*pulse;part.Renderer.color=color;}
            }
            foreach(var cell in new List<int>(obstacleVisuals.Keys))if(!retained.Contains(cell))RemoveObstacleVisual(cell);
        }

        public void RefreshEnemyPerception()
        {
            bool blind=currentBattle!=null && !currentBattle.IsGameOver && currentBattle.Player.IsBlind;
            foreach(var view in enemyViews)if(view!=null)view.SetHiddenByBlindness(blind);
        }
        private void RestoreEnemyPerception()
        {foreach(var view in enemyViews)if(view!=null)view.SetHiddenByBlindness(false);}

        private PlayerCharacter damageTrackedPlayer;
        private CancellationTokenSource playerDamageCts;
        private Vector3 playerNormalScale;
        private Color playerNormalColor;
        private float playerHitElapsed = -1f;

        private readonly Dictionary<int,GameObject> terrainVisuals=new Dictionary<int,GameObject>();
        private void ClearTerrainVisuals()
        {
            foreach(var go in terrainVisuals.Values){if(go==null)continue;go.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(go);else DestroyImmediate(go);}terrainVisuals.Clear();
        }
        // 元の床の模様を透かす氷面。Spriteは呼出元から借用し、rootだけを既存地形表示が所有する。
        private static GameObject CreateIcePattern(Transform parent,Sprite sprite,int sortingLayer)
        {
            var root=new GameObject("IcePattern");root.transform.SetParent(parent,false);
            void Part(string name,Vector2 at,Vector2 size,Color color,float angle=0)
            {
                var part=new GameObject(name);part.transform.SetParent(root.transform,false);part.transform.localPosition=new Vector3(at.x,at.y,-.001f);part.transform.localScale=new Vector3(size.x,size.y,1);part.transform.localRotation=Quaternion.Euler(0,0,angle);
                var renderer=part.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=color;renderer.sortingLayerID=sortingLayer;renderer.sortingOrder=1;
            }
            Part("FrostLeft",new Vector2(-.28f,.13f),new Vector2(.16f,.12f),new Color(.46f,.8f,.94f,.08f),8);
            Part("FrostRight",new Vector2(.2f,-.06f),new Vector2(.18f,.1f),new Color(.65f,.87f,.96f,.07f),-12);
            Part("FrostMiddle",new Vector2(-.02f,-.13f),new Vector2(.13f,.075f),new Color(.58f,.84f,.94f,.075f),20);
            Part("SurfaceEdge",new Vector2(0,.48f),new Vector2(.98f,.025f),new Color(.73f,.92f,1,.65f));
            var points=new[]{new Vector2(-.43f,.12f),new Vector2(-.17f,-.04f),new Vector2(-.03f,.11f),new Vector2(.14f,.03f),new Vector2(.39f,-.17f)};
            void Crack(Vector2 a,Vector2 b,int index){var delta=b-a;Part("IceCrack"+index,(a+b)*.5f,new Vector2(delta.magnitude,.016f),new Color(.68f,.88f,.96f,.7f),Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);}
            for(int i=1;i<points.Length;i++)Crack(points[i-1],points[i],i);
            Crack(points[2],new Vector2(.05f,.29f),5);return root;
        }

        private void RefreshTerrainVisuals()
        {
            if(!isActiveAndEnabled||dungeonSystem==null){ClearTerrainVisuals();return;}
            foreach(var terrain in dungeonSystem.Map.ExplorationTerrain){
                var type=dungeonSystem.Map.GetTerrainType(terrain.Cell);
                bool visible=type!=PanelType.Normal&&currentBattle==null&&Mathf.Abs(terrain.Cell-dungeonSystem.PlayerPosition)<6;
                if(!terrainVisuals.TryGetValue(terrain.Cell,out var root)){
                    if(!visible)continue;
                    if(obstacleWhiteSprite==null)obstacleWhiteSprite=CreateWhiteSprite();
                    root=new GameObject("ExplorationTerrain_"+terrain.Cell);root.transform.SetParent(transform,false);
                    root.transform.position=new Vector3(LogicalToWorld(terrain.Cell+.5f),(battleGridView!=null?battleGridView.GroundY:-.25f)+.02f,0);
                    root.transform.localScale=new Vector3(LogicalToWorld(.92f),LogicalToWorld(.06f),1);
                    var sr=root.AddComponent<SpriteRenderer>();sr.sprite=obstacleWhiteSprite;sr.sortingLayerID=playerSprite!=null?playerSprite.sortingLayerID:0;sr.sortingOrder=1;
                    sr.color=terrain.Type==PanelType.Magma?new Color(1f,.3f,.12f,.6f):terrain.Type==PanelType.Ice?new Color(.3f,.8f,1f,.5f):new Color(.2f,1f,.5f,.5f);terrainVisuals.Add(terrain.Cell,root);
                }
                var renderer=root.GetComponent<SpriteRenderer>();
                renderer.color=type==PanelType.Hole?new Color(.02f,.04f,.08f,.95f):type==PanelType.Cracked?new Color(1f,.7f,.2f,.8f):type==PanelType.Magma?new Color(1f,.3f,.12f,.6f):type==PanelType.Ice?new Color(.3f,.8f,1f,.5f):new Color(.2f,1f,.5f,.5f);
                root.transform.localScale=new Vector3(LogicalToWorld(.92f),LogicalToWorld(type==PanelType.Hole?.16f:.06f),1);
                // 接地帯の上端を床上端へ合わせ、元の亀裂位置は保持する。
                float surfaceDrop=.02f+LogicalToWorld(.03f);
                root.transform.position=new Vector3(LogicalToWorld(terrain.Cell+.5f),(battleGridView!=null?battleGridView.GroundY:-.25f)+.02f-(type==PanelType.Magma||type==PanelType.Ice?surfaceDrop:0),0);
                var magma=root.transform.Find("MagmaPattern");
                if(type==PanelType.Magma){
                    // 帯の上へ張り出さず、元の床面の内側へ亀裂を表示する。
                    renderer.color=new Color(.55f,.23f,.09f,.5f);
                    if(magma==null)magma=WorldBattleGridView.CreateMagmaPattern(root.transform,obstacleWhiteSprite,renderer.sortingLayerID).transform;
                    magma.localScale=new Vector3(1,4,1);magma.localPosition=new Vector3(0,-1.1f+surfaceDrop/LogicalToWorld(.06f),-.001f);
                }
                if(magma!=null)magma.gameObject.SetActive(type==PanelType.Magma);
                var ice=root.transform.Find("IcePattern");
                if(type==PanelType.Ice){
                    renderer.color=new Color(.28f,.66f,.85f,.27f);
                    if(ice==null)ice=CreateIcePattern(root.transform,obstacleWhiteSprite,renderer.sortingLayerID).transform;
                    ice.localScale=new Vector3(1,4,1);ice.localPosition=new Vector3(0,-1.5f,-.001f);
                }
                if(ice!=null)ice.gameObject.SetActive(type==PanelType.Ice);
                root.SetActive(visible);
            }
        }

        private readonly Dictionary<DelayedEffect, GameObject> bombVisuals = new Dictionary<DelayedEffect, GameObject>();
        private void ClearBombVisuals()
        {
            projectilePoseRenderer?.Clear();
            foreach (var root in bombVisuals.Values) {
                if (root == null) continue;
                root.SetActive(false);
                if (UnityEngine.Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            }
            bombVisuals.Clear();
        }
        private void RefreshBombVisuals()
        {
            if (!isActiveAndEnabled || currentBattle == null || currentBattle.IsGameOver || battleGridView == null) { ClearBombVisuals(); return; }
            projectilePoseRenderer?.Refresh(currentBattle, p => new Vector3(battleGridView.GridIndexToWorldX(p.X), battleGridView.GroundY, 0),
                LogicalToWorld, playerSprite != null ? playerSprite.sortingLayerID : 0, 6);
            var active = currentBattle.DelayedEffects.GetActiveEffects();
            foreach (var effect in active) {
                if (effect.BlastRadius != 1 || !effect.SourcePosition.HasValue || bombVisuals.ContainsKey(effect)) continue;
                if (obstacleWhiteSprite == null) obstacleWhiteSprite = CreateWhiteSprite();
                var root = new GameObject("BombProjectile"); root.transform.SetParent(transform, false);
                for (int i=0;i<4;i++) {
                    var part=new GameObject(i==0?"Body":"Blast"+(i-1));part.transform.SetParent(root.transform,false);
                    var renderer=part.AddComponent<SpriteRenderer>();
                    Sprite prop; bool illustrated = SharedAttackPropCatalog.TryGetSprite(i==0?SharedAttackPropCatalog.Bomb:SharedAttackPropCatalog.Blast, out prop);
                    renderer.sprite=illustrated?prop:obstacleWhiteSprite;renderer.color=illustrated?Color.white:new Color(.3f,1f,1f,.85f);
                    renderer.sortingLayerID=playerSprite!=null?playerSprite.sortingLayerID:0;renderer.sortingOrder=6;
                }
                bombVisuals.Add(effect,root);
            }
            foreach (var pair in bombVisuals.ToArray()) {
                var effect=pair.Key;var root=pair.Value;bool flying=active.Contains(effect);
                long elapsed=currentBattle.LogicalFrame-effect.CompletedFrame;
                bool blast=!flying&&effect.WasExecuted&&elapsed>=0&&elapsed<6;
                if (!flying&&!blast) {
                    root.SetActive(false); if(UnityEngine.Application.isPlaying)Destroy(root);else DestroyImmediate(root);bombVisuals.Remove(effect);continue;
                }
                var body=root.transform.GetChild(0);body.gameObject.SetActive(flying && projectilePoseRenderer?.Owns(effect) != true);
                float targetX=battleGridView.GridIndexToWorldX(effect.TargetPosition.X);
                float ground=battleGridView.GroundY;
                if(flying){
                    float t=effect.Progress;float startX=battleGridView.GridIndexToWorldX(effect.SourcePosition.Value.X);
                    body.position=new Vector3(Mathf.Lerp(startX,targetX,t),ground+LogicalToWorld(.45f+4f*t*(1f-t)*1.4f),0);
                    bool illustrated=body.GetComponent<SpriteRenderer>().sprite!=obstacleWhiteSprite;
                    // 旧1x1四角を45度傾けた可視高さを保つ。画像の余白で投射物を縮めない。
                    body.localScale=Vector3.one*LogicalToWorld(illustrated ? .18f*Mathf.Sqrt(2f)/SharedAttackPropCatalog.BombVisibleHeight : .18f);
                    body.rotation=illustrated?Quaternion.identity:Quaternion.Euler(0,0,45);
                }
                for(int i=0;i<3;i++){
                    var part=root.transform.GetChild(i+1);int x=effect.TargetPosition.X+i-1;
                    part.gameObject.SetActive(blast&&x>=0&&x<BattleField.GRID_WIDTH);
                    if(!part.gameObject.activeSelf)continue;
                    part.position=new Vector3(battleGridView.GridIndexToWorldX(x),ground+LogicalToWorld(.38f),0);
                    var renderer=part.GetComponent<SpriteRenderer>();bool illustrated=renderer.sprite!=obstacleWhiteSprite;
                    // 透明余白を除く素材の可視領域を、既存の1タイル内の爆風範囲へ合わせる。
                    part.localScale=new Vector3(LogicalToWorld(.82f)/(illustrated?SharedAttackPropCatalog.BlastVisibleWidth:1f),LogicalToWorld(.18f)/(illustrated?SharedAttackPropCatalog.BlastVisibleHeight:1f),1);
                    float alpha=.75f*(1f-elapsed/6f);
                    renderer.color=illustrated?new Color(1,1,1,alpha):new Color(.4f,1f,1f,alpha);
                }
            }
        }

        private DungeonSystem dungeonSystem;
        private BattleSystem currentBattle; // バトル中のプレイヤー座標更新用
        private readonly List<WorldEnemyView> enemyViews = new List<WorldEnemyView>();
        private bool cameraFollowEnabled = true;
        private float? battleCameraTargetX = null;  // バトル開始時のカメラ移動先（スムーズlerp用）
        private bool playerPositionUpdateEnabled = true;  // 復帰ジャンプ中はfalseにしてY座標の上書きを防ぐ

        // HPの一括通知とは独立した、個々の命中/回復/防御結果を表示する。
        private void BindPlayerDamage(PlayerCharacter player)
        {
            UnbindPlayerDamage();
            damageTrackedPlayer = player;
            if (player == null || !isActiveAndEnabled) return;
            if (playerSprite != null)
            {
                playerNormalScale = playerSprite.transform.localScale;
                playerNormalColor = playerSprite.color;
            }
            player.OnCombatFeedback += OnPlayerCombatFeedback;
        }

        private void OnPlayerCombatFeedback(CombatFeedback feedback)
        {
            if(!isActiveAndEnabled||playerSprite==null)return;
            playerDamageCts ??= new CancellationTokenSource();
            var bounds=playerSprite.bounds;
            var position=new Vector3(bounds.center.x,bounds.max.y+.2f,bounds.center.z-.5f);
            worldDamageNumberPool?.ShowFeedback(feedback,position,playerDamageCts.Token,damageTrackedPlayer);
            if(feedback.Kind!=CombatFeedbackKind.Damage)return;
            // 数値は各ヒット、点滅/倍率は同じ基準から更新して多段で累積させない。
            playerHitElapsed=0f;UpdatePlayerHitEffect(0f);
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

        private void StopPlayerDamageFeedback(bool cancelNumbers=true)
        {
            if(cancelNumbers){playerDamageCts?.Cancel();playerDamageCts?.Dispose();playerDamageCts=null;}
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
                damageTrackedPlayer.OnCombatFeedback -= OnPlayerCombatFeedback;
            damageTrackedPlayer = null;
            StopPlayerDamageFeedback();
        }

        private void OnEnable()
        {
            cameraObservedDungeon = null;
            if (dungeonSystem != null) BindPlayerDamage(dungeonSystem.Player);
            battleGridView?.BindTelegraphs(currentBattle);
            RefreshObstacleVisuals(); RefreshVisitVisuals();
        }

        private void OnDisable() { worldDamageNumberPool?.ClearAll(); guardVisual?.Clear(); paralysisVisual?.Clear(); defensiveField?.Clear(); actionPoseRenderer?.Clear(); if(contactPoseActive)SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase.None,0f); battleGridView?.BindTelegraphs(null); battleGridView?.ShowTelegraphs(System.Array.Empty<Olca.Core.Domain.Vector2Int>()); ClearTerrainVisuals(); ClearBombVisuals(); RestoreEnemyPerception(); ClearObstacleVisuals(); ClearVisitVisuals(); RestoreObstaclePose(); SetBattleMoveVisibility(true); UnbindPlayerDamage(); }
        private void OnDestroy()
        {
            guardVisual?.Dispose(); paralysisVisual?.Dispose(); defensiveField?.Dispose();
            actionPoseRenderer?.Dispose();
            projectilePoseRenderer?.Dispose();
            if (generatedSomersaultSprites != null) foreach (var frame in generatedSomersaultSprites) {
                if (frame == null) continue;
                if (UnityEngine.Application.isPlaying) Destroy(frame); else DestroyImmediate(frame);
            }
            generatedSomersaultSprites = null;
            ClearTerrainVisuals(); ClearBombVisuals();
            ClearObstacleVisuals(); ClearVisitVisuals();
            DisposeVisitShopSprite();
            if(obstacleWhiteSprite!=null){var texture=obstacleWhiteSprite.texture;if(UnityEngine.Application.isPlaying){Destroy(obstacleWhiteSprite);Destroy(texture);}else{DestroyImmediate(obstacleWhiteSprite);DestroyImmediate(texture);}}
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
            EnsureSomersaultSprites();
            if(outcomePlayerColor.HasValue&&playerSprite!=null)playerSprite.color=outcomePlayerColor.Value;
            outcomePlayerColor=null;
            RestoreEnemyPerception();
            ClearTerrainVisuals(); ClearObstacleVisuals(); ClearVisitVisuals();
            ApplyCharacterDisplayOffset();
            dungeonSystem = dungeon;
            cameraObservedDungeon = null;
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
            RefreshObstacleVisuals(); RefreshVisitVisuals();
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
            RefreshObstacleVisuals(); RefreshVisitVisuals();
            RefreshEnemyPerception(); RefreshBombVisuals(); RefreshTerrainVisuals();
            battleGridView?.ShowTelegraphs(currentBattle!=null&&!currentBattle.IsGameOver?currentBattle.EnemyAttackTiles:System.Array.Empty<Olca.Core.Domain.Vector2Int>());
            // Controllerの論理状態から姿勢を選ぶ。描画だけのフレームでも移動姿勢を維持。
            UpdateMovementAnimation();
            UpdatePlayerSprite();
            UpdatePlayerHitEffect(Olca.Unity.BattleVisualClock.DeltaTime);
            RefreshDefensiveField();
            RefreshConditionVisuals();
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
                view.SetStrongEnemy(dungeonSystem.Map.StrongEnemyCells.Contains(cellIndex));
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
            actionPoseRenderer?.Clear();
            ClearBombVisuals();
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
            battleGridView?.BindTelegraphs(battle);

            Debug.Log($"[Camera] OnBattleStarted: playerPos={playerTransform?.position.x:F2}, gridColumn={battle.Player.GridPosition.X}, originX={originX:F2}, gridCenterX={battleGridView?.GridCenterWorldX:F2}, cameraX={fieldCamera?.transform.position.x:F2}, CameraOffsetX={CameraOffsetX:F2}");

            // カメラ追従を停止し、グリッド中央へのスムーズ移動を開始
            cameraFollowEnabled = false;
            if (battleGridView != null)
                battleCameraTargetX = battleGridView.GridCenterWorldX;

            foreach (var enemy in battle.Enemies)
                if (enemy.IsSummoned && GetEnemyView(enemy) == null) OnSummonAdded(enemy);

            // 全敵スプライトをバトルグリッド座標に切り替え
            foreach (var view in enemyViews)
                view?.OnBattleStarted(battleGridView, battle.Enemies.Contains(view.TrackedEnemy));
        }

        /// <summary>
        /// バトル終了時の処理
        /// - バトルグリッドを非表示
        /// - 生きている敵スプライトをダンジョン座標モードに戻す
        /// - カメラ追従を再開
        /// </summary>
        private Color? outcomePlayerColor;
        [SerializeField,Min(0f)] private float defeatFadeDuration=.3f;
        public async UniTask PlayBattleOutcomeAsync(BattleResult result,CancellationToken ct)
        {
            if(result==BattleResult.Victory)
            {
                var tasks=new System.Collections.Generic.List<UniTask>();
                foreach(var view in enemyViews)
                    if(view!=null&&view.TrackedEnemy!=null&&view.TrackedEnemy.IsDead)tasks.Add(view.WaitForDeathPresentationAsync(true,ct));
                await UniTask.WhenAll(tasks);
                return;
            }
            StopPlayerDamageFeedback(false);
            if(playerSprite==null)return;
            outcomePlayerColor=playerSprite.color;var initial=playerSprite.color;float elapsed=0f;
            while(elapsed<defeatFadeDuration)
            {
                elapsed+=Olca.Unity.GameplayPause.DeltaTime;
                var color=initial;color.a=initial.a*(1f-Mathf.Clamp01(elapsed/defeatFadeDuration));playerSprite.color=color;
                await Olca.Unity.GameplayPause.NextFrame(ct);
            }
        }

        public void OnBattleEnded()
        {
            // 終了した対象の数値を、報酬・帰還や次の戦闘へ持ち越さない。
            worldDamageNumberPool?.ClearAll();
            guardVisual?.Clear(); paralysisVisual?.Clear(); defensiveField?.Clear();
            actionPoseRenderer?.Clear();
            ClearBombVisuals();
            SetBattleMoveVisibility(true);
            StopPlayerDamageFeedback(false);
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
            RefreshVisitVisuals();
            RefreshDefensiveField();
            RefreshConditionVisuals();
        }

        /// <summary>バトルフェーズ更新（毎フレーム）</summary>
        public void RefreshBattleField()
        {
            // バトル中のプレイヤー位置をグリッド座標からワールド座標に変換して反映
            if (playerTransform != null && battleGridView != null && currentBattle != null)
            {
                var movement = currentBattle.PlayerMovement;
                // 消失中に、モデルが中間フレームで確定した座標へ切り替える。走行補間は使わない。
                float worldX = battleGridView.GridIndexToWorldX(currentBattle.Player.GridPosition.X);
                SetBattleMoveVisibility(IsBattleMoveVisible(movement.IsNormalMoving, movement.NormalMoveProgress));
                playerTransform.position = new Vector3(worldX, playerTransform.position.y, 0f);
            }

            // 通常接敵アニメーション中: プレイヤーに追従（cameraFollowEnabled=true）
            if (cameraFollowEnabled) UpdateCamera();

            // バトル開始後: グリッド中央へスムーズ移動（cameraFollowEnabled=false かつ目標あり）
            if (!cameraFollowEnabled && battleCameraTargetX.HasValue && fieldCamera != null)
            {
                Vector3 camPos = fieldCamera.transform.position;
                camPos.x = Mathf.Lerp(camPos.x, battleCameraTargetX.Value, Olca.Unity.BattleVisualClock.DeltaTime * cameraFollowSpeed);
                fieldCamera.transform.position = camPos;
            }

            battleGridView?.Refresh();
            RefreshEnemyPerception();
            foreach (var view in enemyViews)
                view?.Refresh();
            // ControllerとViewのLateUpdate順に依存せず、更新済みの論理状態で同じ描画を確定する。
            UpdateMovementAnimation();
            UpdatePlayerSprite();
            RefreshDefensiveField();
            RefreshConditionVisuals();
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
            cameraObservedDungeon = null;
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

        private bool obstaclePoseActive, obstacleAnimatorWasEnabled;
        private void RestoreObstaclePose()
        {
            if (!obstaclePoseActive) return;
            obstaclePoseActive = false;
            if (playerAnimator != null) playerAnimator.enabled = obstacleAnimatorWasEnabled;
        }
        private void UpdatePlayerSprite()
        {
            // 取消・終了は停止中にも反映し、古い武器を残さない。
            if (currentBattle?.PlayerActionVisual == null) actionPoseRenderer?.Clear();
            if(Olca.Unity.BattleVisualClock.IsPaused)return;
            if (dungeonSystem != null && dungeonSystem.IsObstacleStopped && !dungeonSystem.IsInBattle) {
                if (!obstaclePoseActive) { obstaclePoseActive = true; obstacleAnimatorWasEnabled = playerAnimator != null && playerAnimator.enabled; }
                if (playerAnimator != null) playerAnimator.enabled = false;
                // 既存ののけぞりSpriteだけを使う。未設定時はidleで停止し、専用素材完成とは扱わない。
                var frames = contactKnockbackSprites;
                SetPlayerSprite(frames != null && frames.Length > 0
                    ? frames[System.Math.Min(frames.Length - 1, dungeonSystem.ObstacleStopElapsedFrames / 6)] : idlePlayerSprite);
                return;
            }
            RestoreObstaclePose();
            if (contactPoseActive && contactPoseSprite != null) { SetPlayerSprite(contactPoseSprite); return; }
            if (actionPoseRenderer?.Apply(currentBattle?.PlayerActionVisual) == true) return;
            int jumpPhase = SelectJumpAnimationPhase(dungeonSystem != null && (dungeonSystem.IsJumping||dungeonSystem.IsTerrainRecovering)
                && !dungeonSystem.IsInBattle && playerPositionUpdateEnabled && cameraFollowEnabled,
                dungeonSystem?.IsTerrainRecovering==true?.8f:dungeonSystem?.JumpProgress ?? 0f);
            // 接敵後の宙返り中はダッシュフラグが残る場合があるため、
            // バトル中でないこともダッシュ表示の条件にする。
            bool isDashing = dungeonSystem != null
                && dungeonSystem.IsDashing
                && dungeonSystem.IsExplorationMoving && playerPositionUpdateEnabled && cameraFollowEnabled
                && Olca.Unity.BattleVisualClock.DeltaTime > 0f;

            // Animatorを設定した場合は、状態だけを渡してスプライトの再生を任せる。
            // 未設定のシーンでは従来のモック画像表示を使用する。
            if (playerAnimator != null && playerAnimator.runtimeAnimatorController != null)
            {
                bool movementChanged = playerAnimator.GetBool(IsMovingParameter) != (isMoving && !isDashing);
                playerAnimator.SetBool(IsRunningParameter, isDashing);
                playerAnimator.SetBool(IsMovingParameter, isMoving && !isDashing);
                playerAnimator.SetInteger(JumpPhaseParameter, jumpPhase);
                // LateUpdateでも論理状態と同じ描画フレームに姿勢を切り替える。時間は進めない。
                if (appliedJumpPhase != jumpPhase || movementChanged) playerAnimator.Update(0f);
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
            isMoving = playerTransform != null && Olca.Unity.BattleVisualClock.DeltaTime > 0f && (currentBattle == null && dungeonSystem != null && dungeonSystem.IsExplorationMoving && playerPositionUpdateEnabled && cameraFollowEnabled
                && dungeonSystem.PlayerHeight <= 0.001f);
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
            long frame = dungeonSystem != null ? dungeonSystem.ObstacleVisualFrame : 0;
            bool sameDungeon = ReferenceEquals(cameraObservedDungeon, dungeonSystem);
            long elapsedFrames = sameDungeon ? System.Math.Max(0, frame - cameraObservedFrame) : 0;
            Vector3 previousTarget = sameDungeon ? cameraObservedTarget : targetPos;
            cameraObservedDungeon = dungeonSystem;
            cameraObservedFrame = frame;
            cameraObservedTarget = targetPos;

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
            else if (currentBattle != null || !playerPositionUpdateEnabled)
            {
                // 接敵演出の外部位置補間は、探索の論理移動とは別の既存時計を維持する。
                currentFollowSpeed = Mathf.Lerp(currentFollowSpeed, cameraFollowSpeedDash, Olca.Unity.BattleVisualClock.DeltaTime * cameraFollowSpeedTransition);
                fieldCamera.transform.position = Vector3.Lerp(fieldCamera.transform.position, targetPos,
                    Olca.Unity.BattleVisualClock.DeltaTime * currentFollowSpeed);
            }
            else
            {
                // 位置と同じ論理フレームだけ追従を進める。描画だけのフレームで
                // カメラが追いつくと、静止した本体が画面上で逆方向へ揺れてしまう。
                // 複数フレームを一度に描く場合も、既存の追従係数を60Hzで適用する。
                const float step = 1f / 60f;
                for (long i = 1; i <= elapsedFrames; i++)
                {
                    currentFollowSpeed = Mathf.Lerp(currentFollowSpeed, cameraFollowSpeedDash, step * cameraFollowSpeedTransition);
                    var stepTarget = Vector3.Lerp(previousTarget, targetPos, (float)((double)i / elapsedFrames));
                    fieldCamera.transform.position = Vector3.Lerp(fieldCamera.transform.position, stepTarget, step * currentFollowSpeed);
                }
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
            // 末尾の召喚も終端演出の待機対象に残し、OnBattleEndedで一度だけ破棄する。
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
        private InputBindingProfile displayedGamepadProfile;
        private GamePhase displayedInstructionPhase;
        private Olca.Unity.InputGuideDevice? displayedInputDevice;

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
            string Pad(string context,string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad",context,action)]);
            if (phase == GamePhase.Battle)
                return $"{Keys("battle", "LeftPrimary", "LeftSecondary")}: 左  {Keys("battle", "RightPrimary", "RightSecondary")}: 右  " +
                    $"{Keys("battle", "UpPrimary", "UpSecondary")}: 上  {Keys("battle", "DownPrimary", "DownSecondary")}: 下  パッド: Lスティック / {Pad("battle","LeftPrimary")} 左 / {Pad("battle","RightPrimary")} 右 / {Pad("battle","UpPrimary")} 上 / {Pad("battle","DownPrimary")} 下\n" +
                    $"{Keys("battle", "AttackPrimary")}: 固有攻撃 ({Pad("battle","AttackPrimary")})  {Keys("battle", "CardPrimary")}: カード使用 ({Pad("battle","CardPrimary")})  {Keys("battle", "CustomPrimary", "CustomSecondary")}: カスタム ({Pad("battle","CustomPrimary")}) / ポーズ {Pad("battle","Pause")}";
            if (phase == GamePhase.Dungeon)
                return $"自動前進  {Keys("exploration", "JumpPrimary")}: ジャンプ  {Keys("exploration", "DashPrimary", "DashSecondary")}: ダッシュ  パッド: {Pad("exploration","JumpPrimary")} ジャンプ / {Pad("exploration","DashPrimary")} ダッシュ / {Pad("exploration","Pause")} ポーズ";
            return string.Empty;
        }

        public static string FormatGameplayInstructionsForDevice(InputBindingProfile profile,GamePhase phase,Olca.Unity.InputGuideDevice device)
        {
            if(profile==null||(phase!=GamePhase.Battle&&phase!=GamePhase.Dungeon))return string.Empty;
            if(device==Olca.Unity.InputGuideDevice.Touch)return "タッチ: 画面のボタンで操作";
            string Key(string context,params string[] actions)=>string.Join(" / ",actions.Select(a=>profile.Bindings[("keyboard",context,a)]??"ミワリアテ"));
            string Pad(string context,string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(Olca.Unity.UnityInputProvider.ActiveGamepadProfile.Bindings[("gamepad",context,action)]);
            if(device==Olca.Unity.InputGuideDevice.Gamepad){
                if(phase==GamePhase.Dungeon)return $"ジドウゼンシン  {Pad("exploration","JumpPrimary")}: ジャンプ  {Pad("exploration","DashPrimary")}: ダッシュ  {Pad("exploration","Pause")}: ポーズ";
                return $"Lスティック / {Pad("battle","LeftPrimary")} / {Pad("battle","RightPrimary")} / {Pad("battle","UpPrimary")} / {Pad("battle","DownPrimary")}: イドウ\n{Pad("battle","AttackPrimary")}: コユウコウゲキ  {Pad("battle","CardPrimary")}: カード  {Pad("battle","CustomPrimary")}: カスタム  {Pad("battle","Pause")}: ポーズ";
            }
            if(phase==GamePhase.Dungeon)return $"ジドウゼンシン  {Key("exploration","JumpPrimary")}: ジャンプ  {Key("exploration","DashPrimary","DashSecondary")}: ダッシュ  Escape: ポーズ";
            return $"{Key("battle","LeftPrimary","LeftSecondary")}: ヒダリ  {Key("battle","RightPrimary","RightSecondary")}: ミギ  {Key("battle","UpPrimary","UpSecondary")}: ウエ  {Key("battle","DownPrimary","DownSecondary")}: シタ\n{Key("battle","AttackPrimary")}: コユウコウゲキ  {Key("battle","CardPrimary")}: カード  {Key("battle","CustomPrimary","CustomSecondary")}: カスタム  Escape: ポーズ";
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
            if (ReferenceEquals(displayedKeyboardProfile, appliedProfile) && ReferenceEquals(displayedGamepadProfile,Olca.Unity.UnityInputProvider.ActiveGamepadProfile) && displayedInstructionPhase == phase && displayedInputDevice==Olca.Unity.InputGuideActivity.Current) return;
            displayedKeyboardProfile = appliedProfile;displayedGamepadProfile=Olca.Unity.UnityInputProvider.ActiveGamepadProfile; displayedInstructionPhase = phase;displayedInputDevice=Olca.Unity.InputGuideActivity.Current;
            instructionText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(FormatGameplayInstructionsForDevice(appliedProfile, phase,displayedInputDevice.Value));
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


namespace Olca.Presentation.Field
{
    // ゲーム操作の既存スロットを編集する画面。重複/必須判定と保存は既存モデル・Controllerへ委譲する。
    public sealed class GameplayKeyboardSettingsView : MonoBehaviour
    {
        private bool gamepad;private string captureTopology;
        private InputBindingProfile Defaults()=>gamepad?Olca.Unity.UnityInputProvider.CreateDefaultGamepadProfile():Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();
        private InputBindingProfile applied;
        private InputBindingEdit edit;
        private System.Func<InputBindingProfile, string> save;
        private readonly Dictionary<(string Device, string Context, string Action), UnityEngine.UI.Text> labels
            = new Dictionary<(string, string, string), UnityEngine.UI.Text>();
        private (string Device, string Context, string Action)? listening;
        private bool awaitRelease;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Button saveButton;
        private GameObject resetConfirmation;
        private Font font;
        private Olca.Unity.MenuPadScope menu;private bool navigationDirty;private int assignedFrame=-1;private string normalFocus="CloseKeyboardSettings";
        private void InvalidateNavigation(){var name=menu?.FocusName;if(name!=null&&name!="CancelCapture"&&name!="ConfirmKeyboardReset"&&name!="CancelKeyboardReset")normalFocus=name;menu?.Suspend();navigationDirty=true;}
        private void CancelListening(){listening=null;Refresh("キー入力を取り消しました。");}
        private void LateUpdate(){if(menu==null)menu=new Olca.Unity.MenuPadScope(()=>Olca.Unity.MenuPadScope.IsTopmost(transform),keyboardOwned:()=>listening.HasValue);if(navigationDirty){navigationDirty=false;Transform root=resetConfirmation.activeSelf?resetConfirmation.transform:listening.HasValue?transform.Find("CancelCapture"):transform;menu.Bind(root,resetConfirmation.activeSelf?"CancelKeyboardReset":listening.HasValue?"CancelCapture":normalFocus,()=>{if(resetConfirmation.activeSelf){resetConfirmation.SetActive(false);Refresh("初期化を取り消しました。");}else if(listening.HasValue)CancelListening();else DiscardAndClose();});}if(gamepad&&listening.HasValue){if(Input.GetKeyDown(KeyCode.Escape))CancelListening();}else if(assignedFrame!=Time.frameCount)menu.Tick();}
        private void OnDisable(){menu?.Suspend();}
        private void OnDestroy(){menu?.Dispose();}

        public void Initialize(Font font, System.Func<InputBindingProfile, string> save, bool gamepad=false)
        {
            this.font = font; this.save = save;this.gamepad=gamepad;
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            gameObject.AddComponent<UnityEngine.UI.Image>().color = Olca.Presentation.Dungeon.UiLook.Surface;
            MakeText("Title", gamepad?"パッド設定（キーボードと独立保存）":"キーボード設定（ゲーム操作）", transform, new Vector2(0, 455), new Vector2(1700, 75), 38);
            status = MakeText("Status", "", transform, new Vector2(0, 360), new Vector2(1740, 100), 26);
            var defaults = Defaults();
            int[] rows = { 0, 0, 0, 0 };
            if(gamepad){string[] names={"探索","戦闘","メニュー","カスタム"};for(int n=0;n<4;n++)MakeText("Group"+n,names[n],transform,new Vector2((n-1.5f)*450,285),new Vector2(430,50),28);}
            else {
            MakeText("ExploreTitle", "探索", transform, new Vector2(-560, 285), new Vector2(520, 50), 28);
            MakeText("MoveTitle", "戦闘：移動", transform, new Vector2(0, 285), new Vector2(520, 50), 28);
            MakeText("ActionTitle", "戦闘：行動", transform, new Vector2(560, 285), new Vector2(520, 50), 28);
            }
            foreach (var slot in defaults.Bindings.Keys)
            {
                int column = gamepad?System.Array.IndexOf(new[]{"exploration","battle","menu","custom"},slot.Context):slot.Context == "exploration" ? 0 :
                    (slot.Action.StartsWith("Left") || slot.Action.StartsWith("Right") || slot.Action.StartsWith("Up") || slot.Action.StartsWith("Down") ? 1 : 2);
                float x = gamepad?(column-1.5f)*450:(column-1)*560, y = 215 - rows[column]++ * (gamepad?40:70);
                MakeText("Action_" + slot.Action, Label(slot.Action), transform, new Vector2(x - (gamepad?145:165), y), new Vector2(gamepad?150:180, gamepad?38:55), gamepad?20:24);
                var button = MakeButton("Bind_" + slot.Context + "_" + slot.Action, "", transform, new Vector2(x + 15, y), new Vector2(gamepad?155:175, gamepad?38:54));
                labels.Add(slot, button.GetComponentInChildren<UnityEngine.UI.Text>());
                button.onClick.AddListener(() => BeginCapture(slot));
                MakeButton("Clear_" + slot.Context + "_" + slot.Action, "解除", transform, new Vector2(x + (gamepad?155:195), y), new Vector2(gamepad?85:100, gamepad?38:54))
                    .onClick.AddListener(() => { listening = null; Assign(slot, null); });
            }
            MakeButton("CancelCapture", gamepad?"ボタン入力を取消":"キー入力を取消", transform, new Vector2(-560, gamepad?-345:-175), new Vector2(400, 55))
                .onClick.AddListener(() => { listening = null; Refresh("キー入力を取り消しました。"); });
            saveButton = MakeButton("SaveKeyboardSettings", "保存・適用", transform, new Vector2(-560, -420), new Vector2(400, 70));
            saveButton.onClick.AddListener(Save);
            MakeButton("ResetKeyboardSettings", "初期配置へ戻す", transform, new Vector2(0, -420), new Vector2(400, 70))
                .onClick.AddListener(() => { listening = null; resetConfirmation.SetActive(true); Refresh(""); });
            MakeButton("CloseKeyboardSettings", "破棄して戻る", transform, new Vector2(560, -420), new Vector2(400, 70))
                .onClick.AddListener(DiscardAndClose);
            resetConfirmation = new GameObject("ResetKeyboardConfirmation", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            resetConfirmation.transform.SetParent(transform, false);
            var rect = (RectTransform)resetConfirmation.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            resetConfirmation.GetComponent<UnityEngine.UI.Image>().color = Olca.Presentation.Dungeon.UiLook.Surface;
            MakeText("ResetQuestion", "初期配置へ戻しますか？\n保存・適用するまでは変更を破棄できます。", resetConfirmation.transform,
                new Vector2(0, 100), new Vector2(1500, 150), 34);
            MakeButton("ConfirmKeyboardReset", "初期配置へ戻す", resetConfirmation.transform, new Vector2(-280, -80), new Vector2(450, 75))
                .onClick.AddListener(() => {
                    edit = Defaults().BeginEdit();
                    resetConfirmation.SetActive(false); Refresh("初期配置を表示しました。保存・適用で確定します。");
                });
            MakeButton("CancelKeyboardReset", "やめる", resetConfirmation.transform, new Vector2(280, -80), new Vector2(450, 75))
                .onClick.AddListener(() => { resetConfirmation.SetActive(false); Refresh("初期化を取り消しました。"); });
            resetConfirmation.SetActive(false); gameObject.SetActive(false);
        }

        public void Open(InputBindingProfile profile)
        {
            applied = profile; edit = profile.BeginEdit(); listening = null;normalFocus="CloseKeyboardSettings";
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            resetConfirmation.SetActive(false); gameObject.SetActive(true);
            transform.SetAsLastSibling(); // ポーズの遮蔽面より前へ出し、設定の操作を受け取る。
            Refresh(gamepad?"変更するボタンを選んでください。同じ場面の割り当ては交換されます。":"変更するキーを選んでください。同じ場面の割り当ては交換されます。");
        }
        public void BeginCapture((string Device, string Context, string Action) slot)
        {
            listening = slot; awaitRelease = true;captureTopology=Olca.Unity.UnityInputProvider.GamepadTopology();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            Refresh(gamepad?"全ボタン・スティックを戻し、新しいボタンを1つ押してください。取消はマウス／Esc。":"押しているキーを離してから、新しいキーを1つ押してください。");
        }
        private void Update()
        {
            if (!gameObject.activeSelf || !listening.HasValue) return;
            var keys = gamepad?Olca.Unity.UnityInputProvider.CaptureGamepadBinding():Olca.Unity.UnityInputProvider.CaptureBindingKey();
            if(gamepad&&captureTopology!=Olca.Unity.UnityInputProvider.GamepadTopology()){captureTopology=Olca.Unity.UnityInputProvider.GamepadTopology();awaitRelease=true;return;}
            if (awaitRelease) { if (keys.HeldCount == 0) awaitRelease = false; return; }
            if (keys.HeldCount == 0) return;
            if (keys.HeldCount != 1) { awaitRelease = true; Refresh("新しいキーを1つだけ押してください。"); return; }
            var slot = listening.Value; listening = null;assignedFrame=Time.frameCount; Assign(slot, keys.Key);
        }
        private void Assign((string Device, string Context, string Action) slot, string key)
        {
            Refresh(edit.TryAssign(slot, key, out var error) ? "変更しました。保存・適用で確定します。" : error);
        }
        private void Save()
        {
            if (listening.HasValue) return;
            var candidate = edit.Preview();
            string error = save(candidate);
            if (error != null) { Refresh("保存できませんでした。変更はまだ適用されていません。\n再試行するか、破棄して戻ってください。"); return; }
            applied = candidate; edit = candidate.BeginEdit(); Refresh("保存・適用しました。");
        }
        public void DiscardAndClose()
        {
            listening = null; if (applied != null) edit = applied.BeginEdit();
            gameObject.SetActive(false);
        }
        private void Refresh(string message)
        {
            foreach (var pair in labels) pair.Value.text = gamepad?Olca.Unity.UnityInputProvider.GamepadBindingLabel(edit.Draft[pair.Key]):edit.Draft[pair.Key] ?? "未割当";
            status.text = message; saveButton.interactable = !listening.HasValue;InvalidateNavigation();
        }
        private static string Label(string action)
        {
            bool second = action.EndsWith("Secondary"); string suffix = second ? " 2" : " 1";
            if(action=="Pause")return "ポーズ";
            if(action=="Decide")return "決定";if(action=="Cancel")return "取消";if(action=="Confirm")return "確定";if(action=="Customize")return "加工";if(action=="Undo")return "Undo";if(action=="Details")return "詳細";if(action=="Transform")return "変身";if(action=="PageUp")return "前ページ";if(action=="PageDown")return "次ページ";
            if (action.StartsWith("Dash")) return "ダッシュ" + suffix;
            if (action.StartsWith("Left")) return "左移動" + suffix;
            if (action.StartsWith("Right")) return "右移動" + suffix;
            if (action.StartsWith("Up")) return "上移動" + suffix;
            if (action.StartsWith("Down")) return "下移動" + suffix;
            if (action.StartsWith("Custom")) return "カスタム" + suffix;
            return action == "JumpPrimary" ? "ジャンプ" : action == "AttackPrimary" ? "固有攻撃" : "カード使用";
        }
        private UnityEngine.UI.Text MakeText(string name, string text, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text)); obj.transform.SetParent(parent, false);
            var view = obj.GetComponent<UnityEngine.UI.Text>(); view.text = text; view.font = font; view.fontSize = fontSize;
            view.alignment = TextAnchor.MiddleCenter; Olca.Presentation.Dungeon.UiLook.Label(view);
            view.rectTransform.anchorMin = view.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            view.rectTransform.anchoredPosition = position; view.rectTransform.sizeDelta = size; return view;
        }
        private UnityEngine.UI.Button MakeButton(string name, string text, Transform parent, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size;
            obj.GetComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.2f, 0.36f, 1f);
            var button = obj.GetComponent<UnityEngine.UI.Button>(); button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            MakeText("Label", text, obj.transform, Vector2.zero, size, 24); Olca.Presentation.Dungeon.UiLook.Button(button); return button;
        }
    }

    /// <summary>音量候補は保存成功まで適用しない。無音のままでも設定操作できる。</summary>
    public sealed class GameplayAudioSettingsView : MonoBehaviour
    {
        private readonly Slider[] sliders=new Slider[4];private readonly Text[] values=new Text[4];
        private System.Func<Olca.Unity.AudioVolumeSettings,string> save;private System.Action close;private Text status;
        private Font font;private Button saveButton,cancelButton;private Olca.Unity.MenuPadScope menu;
        private void LateUpdate(){menu?.Tick();}
        private void OnDisable(){menu?.Suspend();}
        private void OnDestroy(){menu?.Dispose();}
        private Text Label(string name,string value,Vector2 position,Vector2 size,int fontSize){var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(transform,false);
            var text=obj.GetComponent<Text>();text.font=font;text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;text.text=value;Olca.Presentation.Dungeon.UiLook.Label(text);
            text.rectTransform.anchoredPosition=position;text.rectTransform.sizeDelta=size;return text;}
        private Button Button(string name,string value,Vector2 position){var obj=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));obj.transform.SetParent(transform,false);
            var rect=(RectTransform)obj.transform;rect.anchoredPosition=position;rect.sizeDelta=new Vector2(460,80);var button=obj.GetComponent<Button>();
            var text=Label(name+"Label",value,Vector2.zero,new Vector2(440,70),30);text.transform.SetParent(obj.transform,false);Olca.Presentation.Dungeon.UiLook.Button(button);return button;}
        private Image SliderImage(string name,Transform parent,Color color,Vector2 size){var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent,false);var image=obj.GetComponent<Image>();image.color=color;image.rectTransform.sizeDelta=size;return image;}
        public void Initialize(Font font,System.Func<Olca.Unity.AudioVolumeSettings,string> save,System.Action close)
        {
            this.font=font;this.save=save;this.close=close;var rect=(RectTransform)transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            gameObject.AddComponent<Image>().color=Olca.Presentation.Dungeon.UiLook.Surface;
            Label("AudioTitle","音量設定",new Vector2(0,420),new Vector2(1600,80),38);
            status=Label("AudioStatus","",new Vector2(0,310),new Vector2(1600,110),26);
            string[] ids={"Master","Bgm","Effects","Voice"},names={"全体","BGM","効果音","ボイス"};
            for(int n=0;n<4;n++){
                int index=n;float y=170-n*105;Label("AudioLabel"+ids[n],names[n],new Vector2(-600,y),new Vector2(300,60),30);
                var obj=new GameObject("AudioSlider"+ids[n],typeof(RectTransform),typeof(Image),typeof(Slider));obj.transform.SetParent(transform,false);
                var sr=(RectTransform)obj.transform;sr.anchoredPosition=new Vector2(0,y);sr.sizeDelta=new Vector2(760,60);obj.GetComponent<Image>().color=new Color(.1f,.3f,.4f,.03f);
                SliderImage("Track",obj.transform,new Color(.2f,.35f,.42f,.7f),new Vector2(760,16));
                var area=new GameObject("FillArea",typeof(RectTransform));area.transform.SetParent(obj.transform,false);((RectTransform)area.transform).sizeDelta=new Vector2(730,16);
                var fill=SliderImage("Fill",area.transform,new Color(.25f,.72f,.83f,.9f),Vector2.zero);fill.raycastTarget=false;
                fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
                var handles=new GameObject("HandleArea",typeof(RectTransform));handles.transform.SetParent(obj.transform,false);((RectTransform)handles.transform).sizeDelta=new Vector2(730,60);
                var handle=SliderImage("Handle",handles.transform,new Color(.7f,.94f,1f),new Vector2(28,48));
                var slider=obj.GetComponent<Slider>();slider.minValue=0;slider.maxValue=100;slider.wholeNumbers=true;slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;handle.rectTransform.sizeDelta=new Vector2(28,-12);slider.targetGraphic=handle;
                var colors=slider.colors;colors.selectedColor=new Color(.2f,.7f,.85f);colors.highlightedColor=new Color(.4f,.9f,1f);slider.colors=colors;
                sliders[n]=slider;values[n]=Label("AudioValue"+ids[n],"",new Vector2(550,y),new Vector2(180,60),30);
                slider.onValueChanged.AddListener(v=>values[index].text=((int)v)+" %");
            }
            saveButton=Button("SaveAudioSettings","保存・適用",new Vector2(-300,-350));cancelButton=Button("CancelAudioSettings","破棄して戻る",new Vector2(300,-350));
            saveButton.onClick.AddListener(()=>{var error=this.save(new Olca.Unity.AudioVolumeSettings((int)sliders[0].value,(int)sliders[1].value,(int)sliders[2].value,(int)sliders[3].value));status.text=error??"音量設定を保存し、再生経路へ適用しました。";});
            cancelButton.onClick.AddListener(()=>this.close());
            for(int n=0;n<4;n++)sliders[n].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=n==0?(Selectable)cancelButton:sliders[n-1],selectOnDown=n==3?(Selectable)saveButton:sliders[n+1],selectOnLeft=null,selectOnRight=null};
            saveButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=sliders[3],selectOnLeft=cancelButton,selectOnRight=cancelButton};
            cancelButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=sliders[3],selectOnLeft=saveButton,selectOnRight=saveButton};gameObject.SetActive(false);
        }
        public void Open(Olca.Unity.AudioVolumeSettings value,string error){int[] current={value.Master,value.Bgm,value.Effects,value.Voice};for(int n=0;n<4;n++){sliders[n].SetValueWithoutNotify(current[n]);values[n].text=current[n]+" %";}
            status.text=error??"0 %は消音、100 %は元の音量です。左右キーでも調整できます。\n保存・適用するまで音量は変わりません。試聴音は鳴らしません。";
            gameObject.SetActive(true);transform.SetAsLastSibling();if(menu==null)menu=new Olca.Unity.MenuPadScope(()=>Olca.Unity.MenuPadScope.IsTopmost(transform));menu.Bind(transform,"AudioSliderMaster",()=>close());}
        public void DiscardAndClose(){if(this!=null)gameObject.SetActive(false);}
    }

    public sealed class DialogueView : MonoBehaviour
    {
        private DialogueSession owner;private Font font;private Text title,body;private ScrollRect scroll;
        private Button next,fast,history,skip,pause;private readonly List<Button> choices=new List<Button>();
        private Olca.Unity.MenuPadScope menu;private string navigationKey;private bool wasBacklog;
        private void LateUpdate(){menu?.Tick();}
        private void OnDisable(){menu?.Suspend();navigationKey=null;}
        private void OnDestroy(){menu?.Dispose();}
        private void CancelMenu(){if(owner==null||!owner.IsOpen||owner.IsPaused)return;if(owner.SkipConfirmationOpen)owner.CancelSkip();else if(owner.BacklogOpen)owner.ToggleBacklog();Refresh();}
        private System.Action pauseAction;private long rendered=-1;private int choiceNode=-1,page;
        private Text Label(string name,Transform parent,Vector2 pos,Vector2 size,int fontSize){var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(parent,false);var text=obj.GetComponent<Text>();text.font=font;text.fontSize=fontSize;text.supportRichText=false;text.alignment=TextAnchor.MiddleCenter;Olca.Presentation.Dungeon.UiLook.Label(text);text.rectTransform.anchoredPosition=pos;text.rectTransform.sizeDelta=size;return text;}
        private Button Make(string name,string label,Vector2 pos,Vector2 size){var obj=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));obj.transform.SetParent(transform,false);var rect=(RectTransform)obj.transform;rect.anchoredPosition=pos;rect.sizeDelta=size;var button=obj.GetComponent<Button>();var text=Label(name+"Label",obj.transform,Vector2.zero,size-new Vector2(16,8),26);text.text=label;Olca.Presentation.Dungeon.UiLook.Button(button);return button;}
        public void Initialize(Font font,System.Action pauseAction){this.font=font;this.pauseAction=pauseAction;var rect=(RectTransform)transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;gameObject.AddComponent<Image>().color=Olca.Presentation.Dungeon.UiLook.Surface;
            title=Label("DialogueTitle",transform,new Vector2(0,400),new Vector2(1400,100),32);
            var area=new GameObject("DialogueScroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));area.transform.SetParent(transform,false);var ar=(RectTransform)area.transform;ar.anchoredPosition=new Vector2(0,100);ar.sizeDelta=new Vector2(1500,440);area.GetComponent<Image>().color=new Color(.2f,.5f,.6f,.035f);
            body=Label("DialogueText",area.transform,Vector2.zero,new Vector2(1480,440),30);body.alignment=TextAnchor.UpperLeft;body.verticalOverflow=VerticalWrapMode.Overflow;body.rectTransform.anchorMin=body.rectTransform.anchorMax=new Vector2(0,1);body.rectTransform.pivot=new Vector2(0,1);body.rectTransform.anchoredPosition=new Vector2(10,-10);
            scroll=area.GetComponent<ScrollRect>();scroll.viewport=ar;scroll.content=body.rectTransform;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            next=Make("DialogueNext","決定",new Vector2(-600,-360),new Vector2(350,75));fast=Make("DialogueFast","早送り",new Vector2(-200,-360),new Vector2(350,75));history=Make("DialogueHistory","バックログ",new Vector2(200,-360),new Vector2(350,75));skip=Make("DialogueSkip","スキップ",new Vector2(600,-360),new Vector2(350,75));pause=Make("DialoguePause","ポーズ",new Vector2(790,455),new Vector2(260,60));var pauseRect=(RectTransform)pause.transform;pauseRect.anchorMin=pauseRect.anchorMax=Vector2.one;pauseRect.anchoredPosition=new Vector2(-170,-85);
            pause.onClick.AddListener(()=>this.pauseAction());gameObject.SetActive(false);
        }
        private void OnEnable(){rendered=-1;navigationKey=null;}
        public void Bind(DialogueSession value){menu?.Suspend();navigationKey=null;owner=value;rendered=-1;choiceNode=-1;page=0;gameObject.SetActive(value!=null);if(value==null){body.text="";title.text="";foreach(var b in choices)b.gameObject.SetActive(false);}Refresh();}
        private void Action(Button button,string label,bool enabled,System.Action<DialogueSession> action){button.gameObject.SetActive(true);button.interactable=enabled;button.GetComponentInChildren<Text>().text=label;button.onClick.RemoveAllListeners();var captured=owner;long version=captured.Version;button.onClick.AddListener(()=>{if(ReferenceEquals(owner,captured)&&captured.Version==version&&captured.IsOpen)action(captured);});}
        public void Refresh(){if(this==null||owner==null||!owner.IsOpen)return;if(rendered==owner.Version)return;rendered=owner.Version;
            foreach(var b in choices)b.gameObject.SetActive(false);fast.gameObject.SetActive(true);skip.gameObject.SetActive(true);next.gameObject.SetActive(true);history.gameObject.SetActive(true);
            string text;bool resetScroll=false;
            if(owner.SkipConfirmationOpen){title.text="会話のスキップ確認";text="この会話を終了しますか？";Action(next,"スキップして終了",true,s=>s.ConfirmSkip());Action(history,"取消して戻る",true,s=>s.CancelSkip());fast.gameObject.SetActive(false);skip.gameObject.SetActive(false);resetScroll=true;}
            else if(owner.BacklogOpen){var entries=owner.Backlog;page=Mathf.Clamp(page,0,Mathf.Max(0,entries.Count-1));title.text="バックログ "+(entries.Count==0?0:page+1)+" / "+entries.Count;text=entries.Count==0?"表示済みの文章はありません。":entries[page];Action(next,"前の文章",page>0,s=>{page--;rendered=-1;Refresh();});Action(fast,"次の文章",page+1<entries.Count,s=>{page++;rendered=-1;Refresh();});Action(history,"会話に戻る",true,s=>s.ToggleBacklog());skip.gameObject.SetActive(false);resetScroll=true;}
            else{title.text=owner.Current.Speaker;text=owner.VisibleText;Action(next,!owner.TextComplete?"全文表示":owner.Index==owner.Script.Lines.Count-1?"閉じる":"次へ",!owner.WaitingForChoice,s=>s.Decide());Action(fast,owner.FastForward?(owner.WaitingForChoice?"早送り：選択待ち":"早送り：ON"):"早送り：OFF",true,s=>s.ToggleFastForward());Action(history,"バックログ",true,s=>{page=Mathf.Max(0,s.Backlog.Count-1);s.ToggleBacklog();});Action(skip,owner.Script.CanSkip?"スキップ":"分岐のためスキップ不可",owner.Script.CanSkip,s=>s.RequestSkip());
                if(owner.WaitingForChoice){if(choiceNode!=owner.Index){foreach(var b in choices)Destroy(b.gameObject);choices.Clear();choiceNode=owner.Index;for(int n=0;n<owner.Current.Choices.Count;n++)choices.Add(Make("DialogueChoice"+n,"",new Vector2((n-(owner.Current.Choices.Count-1)*.5f)*370,-200),new Vector2(350,80)));}
                    for(int n=0;n<choices.Count;n++){int index=n;Action(choices[n],owner.Current.Choices[n].Label,true,s=>s.Choose(index));}
                }
            }
            // 描画直前のカナ変換で折返しが増えるため、最終本文を先に測定する。
            text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text);
            if(body.text!=text){body.text=text;body.rectTransform.sizeDelta=new Vector2(1480,Mathf.Max(420,body.preferredHeight+20));if(resetScroll)scroll.verticalNormalizedPosition=1;}
            string key=owner.Index+":"+owner.SkipConfirmationOpen+":"+owner.BacklogOpen+":"+owner.WaitingForChoice+":"+page;
            if(menu==null)menu=new Olca.Unity.MenuPadScope(()=>owner!=null&&owner.IsOpen&&!owner.IsPaused&&Olca.Unity.MenuPadScope.IsTopmost(transform));
            if(navigationKey!=key){string preferred=owner.SkipConfirmationOpen?"DialogueHistory":owner.BacklogOpen?(wasBacklog?menu.FocusName:"DialogueHistory"):owner.WaitingForChoice?"DialogueChoice0":"DialogueNext";navigationKey=key;wasBacklog=owner.BacklogOpen;menu.Bind(transform,preferred,CancelMenu,scroll,directionScroll:false);}

        }
    }
}
