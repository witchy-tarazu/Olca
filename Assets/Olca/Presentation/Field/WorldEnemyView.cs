using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Presentation.Battle;
using Olca.Presentation.Effects;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// World Space上の敵スプライト
    /// ダンジョン探索中・バトル中を問わず同一オブジェクトが継続して存在する
    /// - ダンジョン中: ダンジョンマップ上の配置座標に表示。HPは非表示
    /// - バトル中: WorldBattleGridView のグリッド座標に移動。HPを表示
    /// EnemyView と組み合わせて使用する（視覚表現は EnemyView に委譲）
    /// </summary>
    public class WorldEnemyView : MonoBehaviour
    {
        [SerializeField] private EnemyView enemyView;  // 視覚表現コンポーネント

        private Enemy enemy;
        private float dungeonWorldX;       // 換算済みのダンジョン配置World X
        private WorldBattleGridView gridView;  // バトル中のみセットされる
        private int previousHp;
        private int pendingContactDamage;     // バトル開始前に適用されたダッシュダメージ（フェーズ3で発動）
        private bool battleStarted,feedbackSubscribed,hitReactionRunning;
        private readonly System.Collections.Generic.List<CombatFeedback> pendingContactFeedback=new System.Collections.Generic.List<CombatFeedback>();
        private void SubscribeFeedback(){if(enemy!=null&&!feedbackSubscribed&&isActiveAndEnabled){enemy.OnCombatFeedback+=OnCombatFeedback;enemy.OnDeath+=OnEnemyDeath;feedbackSubscribed=true;}}
        private void UnsubscribeFeedback(){if(enemy!=null&&feedbackSubscribed){enemy.OnCombatFeedback-=OnCombatFeedback;enemy.OnDeath-=OnEnemyDeath;}feedbackSubscribed=false;}
        private void OnEnable()=>SubscribeFeedback();
        private void OnCombatFeedback(CombatFeedback feedback)
        {
            if(!isActiveAndEnabled||hiddenForBattle)return;
            if(!battleStarted){pendingContactFeedback.Add(feedback);return;}
            PresentFeedback(feedback);
        }
        private void PresentFeedback(CombatFeedback feedback)
        {
            if(!hiddenByBlindness)
            {
                // 可視本体の近くから出す。名前/HPとの重なりはPoolの群単位の描画補正に任せる。
                var body=enemyView?.BodyRenderer;float top=transform.position.y+.4f,x=transform.position.x;
                if(body!=null&&body.sprite!=null){
                    var bounds=DefensiveFieldVisual.GetBodyBounds(body);top=bounds.max.y;x=bounds.center.x;
                    // 通常敵は直上のHP群へ寄せる。LineRendererの保守的なboundsで余白を水増ししない。
                    if(!enemyView.IsBossGauge)foreach(var renderer in GetComponentsInChildren<Renderer>()){
                        if(!renderer.enabled||renderer.forceRenderingOff)continue;
                        if(renderer is LineRenderer line){
                            float halfWidth=Mathf.Max(line.startWidth,line.endWidth)*.5f;
                            for(int i=0;i<line.positionCount;i++){var point=line.GetPosition(i);if(!line.useWorldSpace)point=line.transform.TransformPoint(point);top=Mathf.Max(top,point.y+halfWidth);}
                        }else if(renderer.GetComponent<TextMesh>()!=null)top=Mathf.Max(top,renderer.bounds.max.y);
                    }
                }
                else foreach(var renderer in GetComponentsInChildren<Renderer>())
                    if(renderer.enabled&&!renderer.forceRenderingOff&&!(enemyView!=null&&enemyView.IsFixedHudRenderer(renderer)))top=Mathf.Max(top,renderer.bounds.max.y);
                // 4:3へ切り替わった際のHP円拡大と文字の縁まで余白を保つ。
                float padding=body!=null&&body.sprite!=null ? 0.32f : 0.2f;
                var position=new Vector3(x,top+padding,transform.position.z-.5f);
                damageNumberPool?.ShowFeedback(feedback,position,CancellationToken.None,this);
            }
            if(feedback.Kind==CombatFeedbackKind.Damage&&feedback.Amount>0)
                PlayDamageEffectAsync(feedback.Amount,(destroyCts??=new CancellationTokenSource()).Token).Forget();
        }

        [SerializeField,Min(0f)] private float deathFadeDuration=.3f;
        private bool deathStarted,terminalDeathPresentation;
        private int deathVersion;
        private Color? preDeathColor;
        private CancellationTokenSource deathCts;
        private UniTaskCompletionSource deathCompletion;
        public bool IsDeathPresentationComplete => deathStarted&&deathCompletion!=null&&deathCompletion.Task.Status!=UniTaskStatus.Pending;
        private void OnEnemyDeath()
        {
            if(deathStarted||enemy==null)return;
            preDeathColor=enemyView?.BodyRenderer!=null?(Color?)enemyView.BodyRenderer.color:null;
            deathStarted=true;destroyCts?.Cancel();enemyView?.SetDeathPresentationActive(true);enemyView?.HideHp();
            deathCts=new CancellationTokenSource();deathCompletion=new UniTaskCompletionSource();
            FadeDeathAsync(deathVersion,deathCompletion,deathCts.Token).Forget();
        }
        /// <summary>終端演出だけは停止済み戦闘の時計から切り離す。</summary>
        public async UniTask WaitForDeathPresentationAsync(bool terminal,CancellationToken ct)
        {
            terminalDeathPresentation|=terminal;
            if(enemy==null||!enemy.IsDead)return;
            OnEnemyDeath();
            await deathCompletion.Task.AttachExternalCancellation(ct);
        }
        private async UniTask FadeDeathAsync(int version,UniTaskCompletionSource completion,CancellationToken ct)
        {
            var body=enemyView?.BodyRenderer;var color=body!=null?body.color:Color.white;
            try
            {
                float elapsed=0f;
                while(body!=null&&elapsed<deathFadeDuration)
                {
                    elapsed+=terminalDeathPresentation?Olca.Unity.GameplayPause.DeltaTime:Olca.Unity.BattleVisualClock.DeltaTime;
                    var faded=color;faded.a=color.a*(1f-Mathf.Clamp01(elapsed/deathFadeDuration));body.color=faded;
                    await Olca.Unity.GameplayPause.NextFrame(ct);
                }
            }
            catch(System.OperationCanceledException){}
            finally
            {
                if(this!=null&&version==deathVersion){enemyView?.SetDeathPresentationActive(false);if(enemy!=null&&enemy.IsDead)gameObject.SetActive(false);}
                completion.TrySetResult();
            }
        }
        private void ResetDeathPresentation()
        {
            deathVersion++;deathCts?.Cancel();deathCts?.Dispose();deathCts=null;
            if(preDeathColor.HasValue&&enemyView?.BodyRenderer!=null)enemyView.BodyRenderer.color=preDeathColor.Value;
            preDeathColor=null;
            deathStarted=terminalDeathPresentation=false;deathCompletion=null;enemyView?.SetDeathPresentationActive(false);
        }

        private bool positionAnimating;       // trueの間はRefreshによる位置上書きを禁止
        private bool hiddenForBattle;         // バトル不参加のため一時非表示中
        private CancellationTokenSource destroyCts;
        private WorldDamageNumberPool damageNumberPool;

        /// <summary>この View が追跡している Enemy（ContactAnimator のルックアップ用）</summary>
        public Enemy TrackedEnemy => enemy;

        /// <summary>アニメーション中フラグ。trueの間 Refresh は位置を更新しない。</summary>
        public void SetPositionAnimating(bool value) { positionAnimating = value; }

        private bool hiddenByBlindness;
        private readonly System.Collections.Generic.Dictionary<Renderer,bool> blindRendererStates = new System.Collections.Generic.Dictionary<Renderer,bool>();
        public bool IsHiddenByBlindness => hiddenByBlindness;
        /// <summary>AI/購読/当たり判定を止めず、敵本体と名前/HPのRendererだけ遮蔽する。</summary>
        public void SetHiddenByBlindness(bool hidden)
        {
            hiddenByBlindness=hidden;
            if(hidden){
                foreach(var renderer in GetComponentsInChildren<Renderer>(true)){
                    if(!blindRendererStates.ContainsKey(renderer))blindRendererStates.Add(renderer,renderer.forceRenderingOff);
                    renderer.forceRenderingOff=true;
                }
            }else{
                foreach(var pair in blindRendererStates)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
                blindRendererStates.Clear();
            }
            damageNumberPool?.SetOwnerVisible(this,!hidden);
        }
        private void OnDisable(){UnsubscribeFeedback();pendingContactFeedback.Clear();SetHiddenByBlindness(false);}

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();
        }

        // ==================== 初期化 ====================

        /// <summary>ダメージ数値プールを設定する（GameFieldView から呼ぶ）</summary>
        public void SetDamageNumberPool(WorldDamageNumberPool pool)
        {
            damageNumberPool?.SetOwnerVisible(this,true);
            damageNumberPool = pool;
            damageNumberPool?.SetOwnerVisible(this,!hiddenByBlindness);
        }

        /// <summary>ダンジョン配置時に呼ぶ</summary>
        public void Initialize(Enemy enemy, float dungeonX)
        {
            ResetDeathPresentation();
            destroyCts?.Cancel();destroyCts?.Dispose();destroyCts=new CancellationTokenSource();hitReactionRunning=false;
            UnsubscribeFeedback();pendingContactFeedback.Clear();battleStarted=false;
            SetHiddenByBlindness(false);
            this.enemy = enemy;
            this.dungeonWorldX = dungeonX;
            this.gridView = null;
            this.previousHp = enemy.CurrentHp;
            if(!enemy.IsDead&&!gameObject.activeSelf)gameObject.SetActive(true);
            SubscribeFeedback();
            SetStrongEnemy(false);

            // Prefab 経由では Sprite がシリアライズされないためランタイムで生成
            var bodyRenderer = enemyView?.BodyRenderer;
            if (bodyRenderer != null && bodyRenderer.sprite == null)
                bodyRenderer.sprite = CreateWhiteSprite();

            enemyView?.Initialize(enemy);
            UpdatePosition();
        }

        private GameObject strongMarker;
        private bool strongEnemy;
        /// <summary>探索中の強敵だけ、頭上の固定ダイヤで接触前に区別する。</summary>
        public void SetStrongEnemy(bool strong)
        {
            strongEnemy = strong;
            if (strong && strongMarker == null)
            {
                strongMarker = new GameObject("StrongEnemyMarker");
                strongMarker.transform.SetParent(transform, false);
                strongMarker.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                strongMarker.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                strongMarker.transform.localScale = new Vector3(0.22f, 0.22f, 1f);
                var renderer = strongMarker.AddComponent<SpriteRenderer>();
                renderer.sprite = CreateWhiteSprite();
                renderer.color = new Color(0.35f, 0.95f, 1f, 0.9f);
                renderer.sortingOrder = 5;
            }
            if (strongMarker != null) strongMarker.SetActive(strong && gridView == null && enemy != null && !enemy.IsDead);
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        // ==================== バトル遷移 ====================

        /// <summary>バトル開始時 - グリッド座標モードに切り替え（グリッド範囲内の敵のみ）</summary>
        public void OnBattleStarted(WorldBattleGridView grid, bool participates = true)
        {
            positionAnimating = false;
            if (enemy == null) return;

            // バトル開始前（ダッシュ接触時）に適用済みのダメージをここで保存し、
            // Refresh() による自動エフェクト発動を防ぐ。
            // エフェクトは TriggerPendingDamageEffect() でノックバックと同時に発動させる。
            battleStarted=true;
            pendingContactDamage = pendingContactFeedback.Count==0?previousHp-enemy.CurrentHp:0;
            previousHp = enemy.CurrentHp;
            // GridPosition.X がグリッド範囲外の敵はバトルに参加しない → 一時非表示
            if (!participates || enemy.GridPosition.X < 0 || enemy.GridPosition.X >= 6)
            {
                hiddenForBattle = true;
                enemyView?.HideHp();
                gameObject.SetActive(false);
                return;
            }
            if (strongMarker != null) strongMarker.SetActive(false);
            gridView = grid;
            enemyView?.ShowHp();
            UpdatePosition();
        }

        /// <summary>バトル終了時 - ダンジョン座標モードに戻す。撃破済みの敵は非表示にする。</summary>
        public void OnBattleEnded()
        {
            battleStarted=false;pendingContactFeedback.Clear();pendingContactDamage=0;
            SetHiddenByBlindness(false);
            gridView = null;
            SetStrongEnemy(strongEnemy);
            enemyView?.HideHp();
            if (hiddenForBattle)
            {
                hiddenForBattle = false;
                if (enemy != null && !enemy.IsDead)
                    gameObject.SetActive(true);
            }
            // 撃破済みの敵は非表示（カード選択中に背後に映らないよう）
            if (enemy != null && enemy.IsDead)
            {
                gameObject.SetActive(false);
                return;
            }
            UpdatePosition();
        }

        // ==================== 毎フレーム更新 ====================

        /// <summary>位置・状態を更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (enemy == null) return;
            if (hiddenForBattle) return;

            enemyView?.Refresh();
            if (enemy.IsDead) return;

            if (!positionAnimating) UpdatePosition();

            previousHp = enemy.CurrentHp;
        }

        // ==================== エフェクト ====================

        /// <summary>
        /// ダッシュ接触ダメージのエフェクトを発動する（ContactAnimator のノックバック開始時に呼ぶ）
        /// OnBattleStarted() で保存した pendingContactDamage を消化する
        /// </summary>
        public void TriggerPendingDamageEffect()
        {
            if(pendingContactDamage>0)pendingContactFeedback.Add(new CombatFeedback(CombatFeedbackKind.Damage,pendingContactDamage));
            pendingContactDamage=0;
            var pending=pendingContactFeedback.ToArray();pendingContactFeedback.Clear();
            foreach(var feedback in pending)PresentFeedback(feedback);
        }

        private async UniTaskVoid PlayDamageEffectAsync(int damage,CancellationToken ct)
        {
            var bodyRenderer=enemyView?.BodyRenderer;
            if(bodyRenderer==null||hitReactionRunning)return;
            hitReactionRunning=true;
            int version=deathVersion;var scale=transform.localScale;var color=bodyRenderer.color;
            try {await UniTask.WhenAll(FlashEffectAsync(bodyRenderer,ct),ScalePulseEffectAsync(ct));}
            finally {if(this!=null&&version==deathVersion){transform.localScale=scale;if(bodyRenderer!=null)bodyRenderer.color=color;hitReactionRunning=false;}}
        }

        private async UniTask FlashEffectAsync(SpriteRenderer bodyRenderer, CancellationToken ct)
        {
            if(bodyRenderer==null||ct.IsCancellationRequested)return;
            var originalColor = bodyRenderer.color;
            for (int i = 0; i < 2; i++)
            {
                if(bodyRenderer==null||ct.IsCancellationRequested)return;
                bodyRenderer.color = Color.white;
                await Olca.Unity.BattleVisualClock.Delay(50, cancellationToken: ct);
                if(bodyRenderer==null||ct.IsCancellationRequested)return;
                bodyRenderer.color = originalColor;
                await Olca.Unity.BattleVisualClock.Delay(50, cancellationToken: ct);
            }
        }

        private async UniTask ScalePulseEffectAsync(CancellationToken ct)
        {
            if(this==null||ct.IsCancellationRequested)return;
            var originalScale = transform.localScale;
            var peakScale = originalScale * 1.25f;
            float halfDuration = 0.08f;
            float elapsed = 0f;

            // 拡大フェーズ
            while (elapsed < halfDuration)
            {
                // await中の取消/破棄後は、次の描画アクセスへ進まない。
                if(this==null||ct.IsCancellationRequested)return;
                elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                transform.localScale = Vector3.Lerp(originalScale, peakScale, elapsed / halfDuration);
                await Olca.Unity.BattleVisualClock.NextFrame(ct);
            }

            elapsed = 0f;

            // 縮小フェーズ
            while (elapsed < halfDuration)
            {
                // await中の取消/破棄後は、次の描画アクセスへ進まない。
                if(this==null||ct.IsCancellationRequested)return;
                elapsed += Olca.Unity.BattleVisualClock.DeltaTime;
                transform.localScale = Vector3.Lerp(peakScale, originalScale, elapsed / halfDuration);
                await Olca.Unity.BattleVisualClock.NextFrame(ct);
            }

            if(this!=null&&!ct.IsCancellationRequested)transform.localScale = originalScale;
        }

        // ==================== 座標計算 ====================

        private void UpdatePosition()
        {
            float worldX = (gridView != null && enemy != null)
                ? gridView.GridIndexToWorldX(enemy.GridPosition.X)
                : dungeonWorldX;

            transform.position = new Vector3(worldX, transform.position.y, transform.position.z);
        }

        private void OnDestroy()
        {
            ResetDeathPresentation();
            UnsubscribeFeedback();
            SetHiddenByBlindness(false);
            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
