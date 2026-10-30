using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// バトル画面全体のView
    /// BattleSystemの状態を表示する
    /// </summary>
    public class BattleView : MonoBehaviour, IBattleView
    {
        // ========== UI要素（カードUI・HP・ゲージのみ。フィールドはGameFieldViewが担当）==========
        [Header("プレイヤー情報")]
        [SerializeField] private Text playerHpText;
        [SerializeField] private RectTransform customGaugeContainer;  // 縦置きゲージコンテナ
        [SerializeField] private GameObject customGaugeLabel;

        [Header("カスタム画面コンテナ")]
        [SerializeField] private RectTransform customScreenContainer;  // HP・ゲージ・カスタム画面を統合したコンテナ

        [Header("アニメーション設定")]
        [SerializeField] private float animationDuration = 0.4f;  // アニメーション時間
        [SerializeField] private float overshootAmount = 50f;     // オーバーラン量

        // ========== 内部状態 ==========
        private BattleSystem battleSystem;
        private bool isCustomScreenOpen = false;
        private Image customScreenBackdrop;
        private bool customInspectionHidden;
        public void SetCustomInspectionHidden(bool hidden)
        {
            customInspectionHidden=hidden;
            if(customScreenBackdrop!=null)customScreenBackdrop.gameObject.SetActive(isCustomScreenOpen&&!hidden);
            if(customGaugeContainer!=null)customGaugeContainer.gameObject.SetActive(!hidden);
        }

        private CombatStatusIconStrip statusIcons;
        private void RefreshStatusIcons()
        {
            if(!isActiveAndEnabled||customInspectionHidden||battleSystem==null||battleSystem.IsGameOver||playerHpText==null||!playerHpText.isActiveAndEnabled){statusIcons?.Clear();return;}
            var entries=CombatStatusIcons.ForPlayer(battleSystem.Player);
            if(statusIcons==null){
                if(entries.Count==0)return;
                var obj=new GameObject("PlayerCombatStatusIcons",typeof(RectTransform));obj.transform.SetParent(transform,false);
                var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(0,1);
                statusIcons=obj.AddComponent<CombatStatusIconStrip>();statusIcons.Configure(4);
            }
            statusIcons.Show(entries);
            PositionStatusIcons();
        }

        // 文字/カスタムの縮尺を継承せず、HPの実外周に追従する読取専用ドック。
        private void PositionStatusIcons()
        {
            var viewport=transform as RectTransform;
            if(statusIcons==null||statusIcons.VisibleCount==0||playerHpText==null||viewport==null)return;
            var rect=(RectTransform)statusIcons.transform;
            playerHpText.rectTransform.GetWorldCorners(customLayoutCorners);
            float hpRight=float.NegativeInfinity,hpBottom=float.PositiveInfinity,hpTop=float.NegativeInfinity;
            foreach(var point in customLayoutCorners){var local=transform.InverseTransformPoint(point);hpRight=Mathf.Max(hpRight,local.x);hpBottom=Mathf.Min(hpBottom,local.y);hpTop=Mathf.Max(hpTop,local.y);}
            float right=hpRight;
            var bg=customScreenContainer!=null?customScreenContainer.Find("BgCustom") as RectTransform:null;
            if(bg!=null){bg.GetWorldCorners(customLayoutCorners);foreach(var point in customLayoutCorners)right=Mathf.Max(right,transform.InverseTransformPoint(point).x);}
            float progress=Mathf.InverseLerp(CUSTOM_SCREEN_HIDDEN_X,CUSTOM_SCREEN_VISIBLE_X,customSlideX);
            float closedX=hpRight-rect.rect.width;
            if(customGaugeContainer!=null){customGaugeContainer.GetWorldCorners(customLayoutCorners);foreach(var point in customLayoutCorners)closedX=Mathf.Max(closedX,transform.InverseTransformPoint(point).x+12);}
            float x=Mathf.Lerp(closedX,right+12,progress);
            float y=Mathf.Lerp(hpBottom-12,hpTop-12,progress);
            x=Mathf.Clamp(x,viewport.rect.xMin+8,viewport.rect.xMax-rect.rect.width-8);
            y=Mathf.Clamp(y,viewport.rect.yMin+rect.rect.height+8,viewport.rect.yMax-8);
            rect.anchoredPosition=new Vector2(x-viewport.rect.center.x,y-viewport.rect.center.y);
        }

        private CancellationTokenSource animationCts;

        private Slider gaugeSlider;
        private Image gaugeFill;
        private GaugeSpriteSet gaugeSprites;
        private readonly GaugeSpritePlayback gaugePlayback = new GaugeSpritePlayback();
        private bool gaugeResetting;

        private Text transformationStatus, transformationCaption;
        private Image transformationFrame, transformationSweep, transformationIcon, transformationEffect;
        private Olca.Presentation.CustomScreen.CustomScreenView TransformationVisualSource
            => customScreenContainer==null?null:customScreenContainer.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomScreenView>(true);
        private string TransformationName(string id)=>TransformationVisualSource?.GetTransformationName(id)??Olca.Presentation.CustomScreen.CustomDisplay.Katakana(id);
        private Text PresentationText(string name,Transform parent,Vector2 position,Vector2 size,int fontSize)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(parent,false);
            var text=obj.GetComponent<Text>();text.font=playerHpText!=null?playerHpText.font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;Olca.Presentation.Dungeon.UiLook.Label(text);
            var rect=text.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;return text;
        }
        private void EnsureTransformationVisual()
        {
            if(transformationStatus!=null)return;
            var hud=new GameObject("TransformationHudPanel",typeof(RectTransform),typeof(Image));hud.transform.SetParent(transform,false);
            var hr=(RectTransform)hud.transform;hr.anchorMin=hr.anchorMax=Vector2.one;hr.anchoredPosition=new Vector2(-550,-185);hr.sizeDelta=new Vector2(500,65);
            hud.GetComponent<Image>().color=Olca.Presentation.Dungeon.UiLook.Surface;hud.GetComponent<Image>().raycastTarget=false;
            transformationStatus=PresentationText("TransformationHud",hud.transform,Vector2.zero,new Vector2(480,60),26);
            var obj=new GameObject("TransformationPresentation",typeof(RectTransform),typeof(Image));obj.transform.SetParent(transform,false);
            var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(250,60);rect.sizeDelta=new Vector2(600,180);
            transformationFrame=obj.GetComponent<Image>();transformationFrame.raycastTarget=false;
            var edge=obj.AddComponent<Outline>();edge.effectColor=new Color(.4f,.9f,1f,.5f);edge.effectDistance=new Vector2(1,1);
            // 文字より背面の共通素材。進行は既存の変身時計だけを参照する。
            var effect=new GameObject("TransformationEffect",typeof(RectTransform),typeof(Image));effect.transform.SetParent(obj.transform,false);
            transformationEffect=effect.GetComponent<Image>();transformationEffect.raycastTarget=false;transformationEffect.preserveAspect=true;
            var er=transformationEffect.rectTransform;er.anchorMin=Vector2.zero;er.anchorMax=Vector2.one;er.offsetMin=er.offsetMax=Vector2.zero;
            transformationCaption=PresentationText("TransformationCaption",obj.transform,Vector2.zero,new Vector2(550,100),30);
            transformationCaption.color=new Color(.75f,.97f,1f,1f);
            var captionEdge=transformationCaption.gameObject.AddComponent<Outline>();captionEdge.effectColor=new Color(.02f,.07f,.12f,.9f);captionEdge.effectDistance=new Vector2(1,-1);
            var line=new GameObject("TransformationSweep",typeof(RectTransform),typeof(Image));line.transform.SetParent(obj.transform,false);
            transformationSweep=line.GetComponent<Image>();transformationSweep.color=new Color(.25f,.85f,1f,.55f);transformationSweep.raycastTarget=false;
            var lr=(RectTransform)line.transform;lr.anchorMin=lr.anchorMax=new Vector2(.5f,.5f);lr.sizeDelta=new Vector2(3,140);
        }
        private void ResetTransformationVisual()
        {
            if(transformationFrame!=null)transformationFrame.gameObject.SetActive(false);
            if(transformationStatus!=null)transformationStatus.transform.parent.gameObject.SetActive(false);
        }
        private void UpdateTransformationVisual()
        {
            if(battleSystem==null||battleSystem.IsGameOver){ResetTransformationVisual();return;}
            EnsureTransformationVisual();var state=battleSystem.Transformations;
            transformationStatus.transform.parent.gameObject.SetActive(state.HasActive||state.ReleaseFramesRemaining>0);
            transformationStatus.text=state.HasActive?TransformationName(state.ActiveId)+" ノコリ "+state.RemainingPhases+" カイ":"変身なし（残り0回）";
            var sprite=state.HasActive?TransformationVisualSource?.GetTransformationIcon(state.ActiveId):null;
            if(transformationIcon==null&&sprite!=null){
                var icon=new GameObject("TransformationHudIcon",typeof(RectTransform),typeof(Image));icon.transform.SetParent(transformationStatus.transform.parent,false);
                transformationIcon=icon.GetComponent<Image>();transformationIcon.preserveAspect=true;transformationIcon.raycastTarget=false;
                var rect=(RectTransform)icon.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(32,0);rect.sizeDelta=new Vector2(48,48);
            }
            if(transformationIcon!=null){transformationIcon.sprite=sprite;transformationIcon.gameObject.SetActive(sprite!=null);}
            transformationStatus.rectTransform.anchoredPosition=new Vector2(sprite!=null?26:0,0);
            transformationStatus.rectTransform.sizeDelta=new Vector2(sprite!=null?428:480,60);
            bool activation=state.IsActivating&&!battleSystem.CustomScreen.IsOpen;
            bool release=state.ReleaseFramesRemaining>0;
            transformationFrame.gameObject.SetActive(activation||release);if(!activation&&!release)return;
            float progress=activation?1f-state.ActivationFramesRemaining/(float)state.ActivationDurationFrames:1f-state.ReleaseFramesRemaining/12f;
            transformationFrame.color=new Color(.6f,.92f,1f,activation ? .08f+.12f*Mathf.Sin(Mathf.PI*progress):.16f*(1f-progress));
            transformationEffect.sprite=Olca.Presentation.Effects.TransformationEffectSprites.Get(activation);
            transformationEffect.enabled=transformationEffect.sprite!=null;
            transformationEffect.color=new Color(1,1,1,.75f*(1f-progress));
            transformationCaption.text=activation?"ヘンシン  "+TransformationName(state.ActiveId):"変身解除";
            transformationSweep.rectTransform.anchoredPosition=new Vector2(-270f+540f*progress,0);
            transformationSweep.color=new Color(.25f,.85f,1f,.5f*(1f-progress));
        }

        // ========== 定数 ==========
        private const float CUSTOM_SCREEN_HIDDEN_X = -900f;  // 左端接続を保ち、縦ゲージ枠を画面内に残す閉じ位置
        private const float CUSTOM_SCREEN_VISIBLE_X = 0f;    // カスタム画面表示時のX座標（画面内）

        private float customSlideX = CUSTOM_SCREEN_HIDDEN_X;
        private readonly Vector3[] customLayoutCorners = new Vector3[4];

        // パネルと張り出すHP/ゲージを一体で収める。閉状態のHUD寸法は変更しない。
        private void SetCustomSlide(float x)
        {
            customSlideX = x;
            RefreshCustomLayout();
        }

        private void LateUpdate(){RefreshCustomLayout();RefreshStatusIcons();}

        private void RefreshCustomLayout()
        {
            if (customScreenContainer == null) return;
            float fit = 1f;
            var viewport = customScreenContainer.parent as RectTransform;
            if (viewport != null && viewport.rect.width > 0f)
            {
                float right = customScreenContainer.rect.xMax;
                // 元背景の外周には、右側のHP枠とゲージも含まれる。
                var background = customScreenContainer.Find("BgCustom") as RectTransform;
                if (background != null)
                {
                    background.GetWorldCorners(customLayoutCorners);
                    foreach (var corner in customLayoutCorners)
                        right = Mathf.Max(right, customScreenContainer.InverseTransformPoint(corner).x);
                }
                if (right > 0f) fit = Mathf.Min(1f, viewport.rect.width * .5f / right);
            }
            float progress = Mathf.InverseLerp(CUSTOM_SCREEN_HIDDEN_X, CUSTOM_SCREEN_VISIBLE_X, customSlideX);
            float scale = Mathf.Lerp(1f, fit, progress);
            customScreenContainer.localScale = new Vector3(scale, scale, 1f);
            var position = customScreenContainer.anchoredPosition;
            position.x = customSlideX * scale;
            customScreenContainer.anchoredPosition = position;
            float aspect = viewport != null && viewport.rect.height > 0f ? viewport.rect.width / viewport.rect.height : 16f/9f;
            float narrow = Mathf.Clamp01((16f/9f-aspect)/(16f/9f-4f/3f));
            TransformationVisualSource?.ApplyNarrowLayout(narrow*progress);
            if(gaugeFill!=null&&gaugeSlider!=null&&gaugeSlider.fillRect!=null)
                gaugeFill.rectTransform.sizeDelta=((RectTransform)gaugeSlider.fillRect.parent).rect.size;
            PositionStatusIcons();
        }

        // ========== 初期化 ==========
        public void Initialize(BattleSystem battleSystem)
        {
            DetachBattleEvents();
            Olca.Presentation.Dungeon.UiLook.Label(playerHpText);
            if(playerHpText!=null)playerHpText.color=Color.white;
            if(customGaugeLabel!=null){var label=customGaugeLabel.GetComponent<Text>();if(label!=null){Olca.Presentation.Dungeon.UiLook.Label(label);label.fontSize=26;label.text="CUSTOM";}}
            ResetGaugeVisual();
            gaugeResetting = false;
            isCustomScreenOpen = false;
            this.battleSystem = battleSystem;
            EnsureGaugeVisual();
            EnsureCustomScreenBackdrop();
            customScreenBackdrop.gameObject.SetActive(false);

            // イベント購読
            battleSystem.CustomGauge.OnGaugeChanged += OnGaugeChanged;
            battleSystem.OnPlayerAttack += OnPlayerAttack;
            battleSystem.OnBattleEnd += OnBattleEnded;

            // 初期状態設定：カスタム画面コンテナを画面外に配置
            if (customScreenContainer != null)
            {
                SetCustomSlide(CUSTOM_SCREEN_HIDDEN_X);
            }

            // CustomGaugeLabelのみ初期状態で非表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);

            Refresh();
        }

        // カスタム画面の表示だけを暗くし、フィールドや入力処理は変更しない。
        private void EnsureCustomScreenBackdrop()
        {
            if (customScreenBackdrop != null) return;
            var backdrop = new GameObject("CustomScreenBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            customScreenBackdrop = backdrop.GetComponent<Image>();
            customScreenBackdrop.color = new Color(.02f,.08f,.12f,.38f);
            customScreenBackdrop.raycastTarget = false;
        }

        // 元の50×750の面内に描く。フィルは一定寸法のまま、Sliderの高さでクリップする。
        private void EnsureGaugeVisual()
        {
            if(gaugeSlider!=null||customGaugeContainer==null)return;
            gaugeSlider=customGaugeContainer.GetComponentInChildren<Slider>(true);
            if(gaugeSlider==null||gaugeSlider.fillRect==null)return;
            gaugeSprites=new GaugeSpriteSet();
            var original=gaugeSlider.fillRect.GetComponent<Image>();
            if(original!=null){original.enabled=false;original.raycastTarget=false;}
            if(gaugeSlider.fillRect.GetComponent<RectMask2D>()==null)gaugeSlider.fillRect.gameObject.AddComponent<RectMask2D>();
            var area=(RectTransform)gaugeSlider.fillRect.parent;
            var energy=new GameObject("GaugeEnergy",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            energy.transform.SetParent(gaugeSlider.fillRect,false);gaugeFill=energy.GetComponent<Image>();
            var rect=gaugeFill.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.pivot=new Vector2(.5f,0);
            rect.anchoredPosition=Vector2.zero;rect.sizeDelta=area.rect.size;
            gaugeFill.sprite=gaugeSprites.Fill;gaugeFill.color=Color.white;gaugeFill.type=Image.Type.Simple;gaugeFill.raycastTarget=false;
            var background=gaugeSlider.transform.Find("Background")?.GetComponent<Image>();
            if(background!=null){background.sprite=gaugeSprites.Track;background.color=Color.white;background.type=Image.Type.Simple;background.raycastTarget=false;}
        }
        private void Update() {Olca.Unity.BattleVisualClock.SetPaused(this,battleSystem!=null&&(battleSystem.IsDarkening||battleSystem.IsHitStopped));UpdateGaugeVisual();UpdateTransformationVisual();foreach(var label in GetComponentsInChildren<UnityEngine.UI.Text>(true))label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(label.text);}
        private void UpdateGaugeVisual()
        {
            if(gaugeFill==null)return;
            bool full=isActiveAndEnabled&&battleSystem!=null&&!battleSystem.IsGameOver&&battleSystem.CustomGauge.IsFull&&!gaugeResetting;
            int frame=gaugePlayback.Sample(full,battleSystem?.LogicalFrame??0);
            gaugeFill.sprite=frame<0?gaugeSprites.Fill:gaugeSprites.FullFrames[frame];
        }
        private void ResetGaugeVisual()
        {
            gaugePlayback.Reset();
            if(gaugeFill!=null&&gaugeSprites!=null)gaugeFill.sprite=gaugeSprites.Fill;
        }

        private void OnBattleEnded(BattleResult result) {statusIcons?.Clear();Olca.Unity.BattleVisualClock.SetPaused(this,false);ResetGaugeVisual();ResetTransformationVisual();}

        /// <summary>撤退等でセッションを破棄する。古い開閉処理が再表示しないよう所有権を先に外す。</summary>
        public void EndSessionPresentation()
        {
            var previous = animationCts;
            animationCts = null;
            previous?.Cancel(); previous?.Dispose();
            TransformationVisualSource?.StopHandPresentation();
            isCustomScreenOpen = false; gaugeResetting = false;
            if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(false);
            if (customScreenContainer != null) {
                SetCustomSlide(CUSTOM_SCREEN_HIDDEN_X);
            }
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);
            DetachBattleEvents(); battleSystem = null;statusIcons?.Clear();
            Olca.Unity.BattleVisualClock.SetPaused(this,false);
            ResetGaugeVisual(); ResetTransformationVisual();
        }

        private void OnDisable() {statusIcons?.Clear();Olca.Unity.BattleVisualClock.SetPaused(this,false);ResetGaugeVisual();ResetTransformationVisual();}

        private void DetachBattleEvents()
        {
            if (battleSystem == null) return;
            battleSystem.CustomGauge.OnGaugeChanged -= OnGaugeChanged;
            battleSystem.OnPlayerAttack -= OnPlayerAttack;
            battleSystem.OnBattleEnd -= OnBattleEnded;
        }

        // ========== 更新 ==========
        public void Refresh()
        {
            if (battleSystem == null) return;

            RefreshStatusIcons();
            UpdateTransformationVisual();
            // プレイヤーHP表示（数値のみ）
            var player = battleSystem.Player;
            if (playerHpText != null)
                playerHpText.text = $"{player.CurrentHp}";

            // カスタムゲージ表示（カスタム画面が開いていない場合のみ）
            if (!isCustomScreenOpen && customGaugeContainer != null)
            {
                var slider = customGaugeContainer.GetComponentInChildren<Slider>();
                if (slider != null)
                    slider.value = battleSystem.CustomGauge.NormalizedGauge;
            }
            UpdateGaugeVisual();
            // フィールド・敵の更新は GameFieldView.RefreshBattleField() が担当
        }

        // ========== カスタム画面制御（UniTaskベース） ==========
        /// <summary>
        /// カスタム画面を開く（UniTaskベースのアニメーション）
        /// Phase2: オーバーランを含むスライドインアニメーション
        /// </summary>
        public void OpenCustomScreen()
        {
            if (isCustomScreenOpen) return;
            isCustomScreenOpen = true;
            EnsureCustomScreenBackdrop();
            customScreenBackdrop.gameObject.SetActive(!customInspectionHidden);

            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            animationCts = new CancellationTokenSource();

            // アニメーション実行（非同期）
            AnimateCustomScreenOpenAsync(animationCts.Token).Forget();
        }

        /// <summary>
        /// カスタム画面を閉じる（UniTaskベースのアニメーション）
        /// Phase2: オーバーランを含むスライドアウトアニメーション
        /// アニメーション完了まで待つ
        /// </summary>
        public int CloseDurationFrames => Olca.Core.Logic.Integration.LogicalFrameClock.DurationFrames(animationDuration);

        public async UniTask CloseCustomScreenAsync()
        {
            // 勝敗による強制終了でも、画面外で手札を生成し続けない。
            TransformationVisualSource?.StopHandPresentation();
            if (!isCustomScreenOpen) return;
            isCustomScreenOpen = false;
            if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(false);

            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            var closingCts = new CancellationTokenSource();
            animationCts = closingCts;

            // アニメーション実行（完了まで待つ）
            gaugeResetting = true;
            ResetGaugeVisual();
            try
            {
                await AnimateCustomScreenCloseAsync(closingCts.Token);
            }
            catch
            {
                if (ReferenceEquals(animationCts, closingCts))
                {
                    isCustomScreenOpen = true;
                    if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(!customInspectionHidden);
                }
                throw;
            }
            finally
            {
                if (ReferenceEquals(animationCts, closingCts)) gaugeResetting = false;
            }
        }

        // ========== IBattleView 明示的実装 ==========
        /// <summary>
        /// IBattleView.CloseCustomScreenAsync の実装。
        /// UniTask版をTask に変換して返す。
        /// </summary>
        async System.Threading.Tasks.Task IBattleView.CloseCustomScreenAsync()
            => await CloseCustomScreenAsync();

        // ========== アニメーション実装（UniTask） ==========
        /// <summary>
        /// カスタム画面を開くアニメーション
        /// 画面外 → オーバーラン → 定位置
        /// </summary>
        private async UniTask AnimateCustomScreenOpenAsync(CancellationToken ct)
        {
            if (customScreenContainer == null) return;

            // 「準備中」ラベルを表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(true);

            // Phase 1: 画面外 → オーバーラン (0.25秒、EaseOutQuad)
            float phase1Duration = animationDuration * 0.625f; // 0.25秒
            float elapsed = 0f;

            while (elapsed < phase1Duration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / phase1Duration;
                float easedT = EaseOutQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_HIDDEN_X, CUSTOM_SCREEN_VISIBLE_X + overshootAmount, easedT);
                SetCustomSlide(currentX);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // Phase 2: オーバーラン → 定位置 (0.15秒、EaseInQuad)
            float phase2Duration = animationDuration * 0.375f; // 0.15秒
            elapsed = 0f;

            while (elapsed < phase2Duration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / phase2Duration;
                float easedT = EaseInQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X + overshootAmount, CUSTOM_SCREEN_VISIBLE_X, easedT);
                SetCustomSlide(currentX);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // 最終位置を保証
            SetCustomSlide(CUSTOM_SCREEN_VISIBLE_X);
        }

        /// <summary>
        /// カスタム画面を閉じるアニメーション
        /// 定位置 → オーバーラン → 画面外
        /// </summary>
        private async UniTask AnimateCustomScreenCloseAsync(CancellationToken ct)
        {
            if (customScreenContainer == null) return;

            // Phase 1: 定位置 → オーバーラン (0.15秒、EaseOutQuad)
            float phase1Duration = animationDuration * 0.375f; // 0.15秒
            float elapsed = 0f;

            while (elapsed < phase1Duration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / phase1Duration;
                float easedT = EaseOutQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X, CUSTOM_SCREEN_VISIBLE_X + overshootAmount, easedT);
                SetCustomSlide(currentX);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // Phase 2: オーバーラン → 画面外 (0.25秒、EaseInQuad)
            float phase2Duration = animationDuration * 0.625f; // 0.25秒
            elapsed = 0f;

            while (elapsed < phase2Duration)
            {
                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / phase2Duration;
                float easedT = EaseInQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X + overshootAmount, CUSTOM_SCREEN_HIDDEN_X, easedT);
                SetCustomSlide(currentX);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // 最終位置を保証
            SetCustomSlide(CUSTOM_SCREEN_HIDDEN_X);

            // 「準備中」ラベルを非表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);

            // カスタムゲージを0にリセット（急速アニメーション）
            await AnimateGaugeResetAsync(ct);
        }

        /// <summary>
        /// カスタムゲージを最大から0へ急速にアニメーション
        /// </summary>
        private async UniTask AnimateGaugeResetAsync(CancellationToken ct)
        {
            if (customGaugeContainer == null) return;

            var slider = customGaugeContainer.GetComponentInChildren<Slider>();
            if (slider == null) return;

            // 消費量はモデルが確定済み。描画時間による独自の減少アニメーションを作らない。
            ct.ThrowIfCancellationRequested();
            slider.value=battleSystem?.CustomGauge.NormalizedGauge??0;
            await UniTask.CompletedTask;
        }

        // ========== イージング関数 ==========
        /// <summary>
        /// EaseOutQuad: 速く始まり、ゆっくり終わる
        /// </summary>
        private float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>
        /// EaseInQuad: ゆっくり始まり、速く終わる
        /// </summary>
        private float EaseInQuad(float t)
        {
            return t * t;
        }

        // ========== イベントハンドラー ==========
        private void OnGaugeChanged(float newValue)
        {
            UpdateGaugeVisual();
            if (!isCustomScreenOpen && customGaugeContainer != null)
            {
                var slider = customGaugeContainer.GetComponentInChildren<Slider>();
                if (slider != null)
                {
                    slider.value = newValue;
                }
            }
        }

        private void OnPlayerAttack(Enemy target, int damage)
        {
            Debug.Log($"プレイヤーが{target.Name}に{damage}ダメージ！");
            // ダメージ数値表示（後で実装）
        }

        private void OnDestroy()
        {
            statusIcons?.Clear();
            // アニメーション用CancellationTokenをキャンセル
            animationCts?.Cancel();
            animationCts?.Dispose();

            DetachBattleEvents();
            ResetGaugeVisual();
            gaugeSprites?.Dispose();gaugeSprites=null;
        }
    }
}

namespace Olca.Presentation.Battle
{
    /// <summary>単純なUI図形から生成する固定外形の独立Sprite。既存イラストの加工はしない。</summary>
    public sealed class GaugeSpriteSet : System.IDisposable
    {
        public const int Width=128,Height=1920;
        public Sprite Track{get;}
        public Sprite Fill{get;}
        public Sprite[] FullFrames{get;}
        private readonly System.Collections.Generic.List<Sprite> owned=new System.Collections.Generic.List<Sprite>();
        public GaugeSpriteSet()
        {
            Track=Create("GaugeTrack",-2);Fill=Create("GaugeFill",-1);
            FullFrames=new Sprite[GaugeSpritePlayback.FrameCount];
            for(int i=0;i<FullFrames.Length;i++)FullFrames[i]=Create("GaugeFull_"+i,i);
        }
        private static bool Inside(int x,int y,int inset)
        {
            const int corner=14;
            return x>=inset&&x<Width-inset&&y>=inset&&y<Height-inset&&
                x+y>=inset*2+corner&&Width-1-x+y>=inset*2+corner&&
                x+Height-1-y>=inset*2+corner&&Width-1-x+Height-1-y>=inset*2+corner;
        }
        private Sprite Create(string name,int frame)
        {
            var pixels=new Color32[Width*Height];
            for(int y=0;y<Height;y++){
                float delta=Mathf.Abs(y-frame*Height/8f);delta=Mathf.Min(delta,Height-delta);
                float light=frame>=0?Mathf.Exp(-delta*delta/(260f*260f)):0;
                for(int x=0;x<Width;x++){
                    Color color=Color.clear;
                    if(frame==-2&&Inside(x,y,4)){
                        color=new Color(.055f,.13f,.18f,.7f);
                        if(!Inside(x,y,7))color=new Color(.33f,.88f,.94f,.85f);
                        else if(!Inside(x,y,14)&&Inside(x,y,12))color=new Color(.28f,.69f,.76f,.45f);
                        if(y%(Height/12)<3&&(x<26||x>Width-27))color=new Color(.5f,.94f,.98f,.55f);
                    }else if(frame!=-2&&Inside(x,y,16)){
                        float middle=1-Mathf.Abs(x-Width*.5f)/(Width*.5f);
                        color=new Color(.06f+.10f*middle,.33f+.19f*middle,.43f+.18f*middle,.82f);
                        if(x%24<2)color=Color.Lerp(color,new Color(.3f,.82f,.88f,.9f),.3f);
                        if(frame>=0)color=Color.Lerp(color,new Color(.68f,1f,1f,.98f),light*.65f);
                    }
                    pixels[y*Width+x]=color;
                }
            }
            var texture=new Texture2D(Width,Height,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,true);
            var sprite=Sprite.Create(texture,new Rect(0,0,Width,Height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);sprite.name=name;sprite.hideFlags=HideFlags.HideAndDontSave;owned.Add(sprite);return sprite;
        }
        public void Dispose()
        {
            foreach(var sprite in owned){var texture=sprite.texture;if(Application.isPlaying){Object.Destroy(sprite);Object.Destroy(texture);}else{Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}}
            owned.Clear();
        }
    }
}
