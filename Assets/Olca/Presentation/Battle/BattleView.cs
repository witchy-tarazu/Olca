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
        private CancellationTokenSource animationCts;

        private Slider gaugeSlider;
        private Image gaugeFill;
        private GaugeSpriteSet gaugeSprites;
        private readonly GaugeSpritePlayback gaugePlayback = new GaugeSpritePlayback();
        private bool gaugeResetting;

        // ========== 定数 ==========
        private const float CUSTOM_SCREEN_HIDDEN_X = -900f;  // 左端接続を保ち、縦ゲージ枠を画面内に残す閉じ位置
        private const float CUSTOM_SCREEN_VISIBLE_X = 0f;    // カスタム画面表示時のX座標（画面内）

        // ========== 初期化 ==========
        public void Initialize(BattleSystem battleSystem)
        {
            DetachBattleEvents();
            Olca.Presentation.Dungeon.UiLook.Label(playerHpText);
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
                var anchoredPos = customScreenContainer.anchoredPosition;
                anchoredPos.x = CUSTOM_SCREEN_HIDDEN_X;
                customScreenContainer.anchoredPosition = anchoredPos;
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
        private void Update() => UpdateGaugeVisual();
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

        private void OnBattleEnded(BattleResult result) => ResetGaugeVisual();

        private void OnDisable() => ResetGaugeVisual();

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
            customScreenBackdrop.gameObject.SetActive(true);

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
                    if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(true);
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
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

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
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // 最終位置を保証
            customScreenContainer.anchoredPosition = new Vector2(CUSTOM_SCREEN_VISIBLE_X, customScreenContainer.anchoredPosition.y);
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
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

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
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            // 最終位置を保証
            customScreenContainer.anchoredPosition = new Vector2(CUSTOM_SCREEN_HIDDEN_X, customScreenContainer.anchoredPosition.y);

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
