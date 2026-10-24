using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Olca.Core.Domain;
using Olca.Presentation.Effects;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタマイズモードでのハイライト種別
    /// </summary>
    public enum CardHighlightType
    {
        /// <summary>ハイライトなし（通常状態）</summary>
        None,
        /// <summary>通常選択（デッキ選択用）</summary>
        Selected,
        /// <summary>超越素材として選択中</summary>
        ExceedMaterial,
        /// <summary>超越可能ペアが存在するカード（候補表示）</summary>
        ExceedCandidate,
        /// <summary>調律対象として選択中</summary>
        TuningTarget,
        /// <summary>調律素材として選択中</summary>
        TuningMaterial,
        /// <summary>融合素材として選択中</summary>
        FusionMaterial,
        /// <summary>現在の選択と組み合わせ不可（暗転表示）</summary>
        Incompatible,
    }

    /// <summary>
    /// 手札1枚のカードView
    /// </summary>
    public class HandCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Text costText;
        [SerializeField] private Image cardImage;
        [SerializeField] private Button cardButton;

        [Header("Effects")]
        [SerializeField] private CardEffectView cardEffectView;

        public Card Card { get; private set; }
        private CustomScreenView parentView;
        private CancellationTokenSource destroyCts;

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();

            // CardEffectViewが未設定なら自動で追加
            if (cardEffectView == null && cardImage != null)
            {
                cardEffectView = cardImage.gameObject.AddComponent<CardEffectView>();
                // 対象Imageを設定（リフレクションを使用）
                var targetImageField = typeof(CardEffectView).GetField("targetImage",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (targetImageField != null)
                {
                    targetImageField.SetValue(cardEffectView, cardImage);
                    // targetImageを設定した後、明示的に初期化を呼ぶ
                    cardEffectView.InitializeIfNeeded();
                }
            }
        }

        public void Initialize(Card card, CustomScreenView parent)
        {
            this.Card = card;
            this.parentView = parent;

            parent.ConfigureTouchDetails(cardButton, () => parent.ShowTouchCardDetails(this));
            RefreshArtwork();

            // カード情報表示（コストのみ）
            costText.text = $"{card.GetFinalCost()}";
            Olca.Presentation.Dungeon.UiLook.Label(costText);
            if(cardButton!=null)Olca.Presentation.Dungeon.UiLook.Button(cardButton);
            costText.color=Color.white;

            // クリックイベント
            cardButton.onClick.AddListener(() => parentView.OnCardClicked(card));
        }

        public void RefreshArtwork()
        {
            if(cardImage!=null)cardImage.sprite=parentView==null?null:parentView.GetCardIcon(Card?.Id);
        }

        /// <summary>
        /// 選択状態を更新（エフェクト付き）
        /// </summary>
        public void UpdateSelection(bool isSelected)
        {
            if (destroyCts.IsCancellationRequested) return;

            // エフェクトが有効な場合はアニメーション実行
            if (cardEffectView != null)
            {
                if (isSelected)
                {
                    cardEffectView.ShowHighlightAsync(destroyCts.Token).Forget();
                }
                else
                {
                    cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                }
            }
            else
            {
                // エフェクト無効時はシンプルな色変更
                cardImage.color = isSelected ? Color.yellow : Color.white;
            }
        }

        /// <summary>
        /// カスタマイズモードのハイライト種別を指定して選択状態を更新
        /// </summary>
        public void UpdateHighlight(CardHighlightType type)
        {
            if (destroyCts.IsCancellationRequested) return;

            if (cardEffectView != null)
            {
                switch (type)
                {
                    case CardHighlightType.None:
                        cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                        break;
                    case CardHighlightType.Selected:
                        // 通常選択は既存の黄色ハイライトを使用
                        cardEffectView.ShowHighlightAsync(destroyCts.Token).Forget();
                        break;
                    case CardHighlightType.ExceedMaterial:
                        // 超越素材: 金色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(1f, 0.84f, 0f)).Forget();
                        break;
                    case CardHighlightType.ExceedCandidate:
                        // 超越候補: 淡い金色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0.8f, 0.7f, 0.2f)).Forget();
                        break;
                    case CardHighlightType.TuningTarget:
                        // 調律対象: 青色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0.3f, 0.6f, 1f)).Forget();
                        break;
                    case CardHighlightType.TuningMaterial:
                        // 調律素材: 水色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0f, 0.9f, 0.9f)).Forget();
                        break;
                    case CardHighlightType.FusionMaterial:
                        // 融合素材: シアン
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0f, 1f, 0.8f)).Forget();
                        break;
                    case CardHighlightType.Incompatible:
                        // 選択不可: ハイライト非表示（暗転はcardImageで表現）
                        cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                        break;
                }
            }
            else
            {
                // エフェクト無効時はシンプルな色変更
                switch (type)
                {
                    case CardHighlightType.None:
                        cardImage.color = Color.white;
                        break;
                    case CardHighlightType.Selected:
                        cardImage.color = Color.yellow;
                        break;
                    case CardHighlightType.ExceedMaterial:
                        cardImage.color = new Color(1f, 0.84f, 0f);
                        break;
                    case CardHighlightType.ExceedCandidate:
                        cardImage.color = new Color(0.9f, 0.85f, 0.5f);
                        break;
                    case CardHighlightType.TuningTarget:
                        cardImage.color = new Color(0.3f, 0.6f, 1f);
                        break;
                    case CardHighlightType.TuningMaterial:
                        cardImage.color = new Color(0f, 0.9f, 0.9f);
                        break;
                    case CardHighlightType.FusionMaterial:
                        cardImage.color = new Color(0f, 1f, 0.8f);
                        break;
                    case CardHighlightType.Incompatible:
                        cardImage.color = new Color(0.35f, 0.35f, 0.35f, 0.7f);
                        break;
                }
            }

            // Incompatible の場合は cardImage を直接暗転（エフェクトあり・なし共通）
            if (type == CardHighlightType.Incompatible && cardEffectView != null)
            {
                if (cardImage != null)
                    cardImage.color = new Color(0.35f, 0.35f, 0.35f, 0.7f);
            }
            else if (type != CardHighlightType.Incompatible && cardEffectView != null)
            {
                // Incompatible 解除時に色をリセット（ハイライト自体の色はeffectViewが管理）
                if (cardImage != null && type == CardHighlightType.None)
                    cardImage.color = Color.white;
            }
        }

        /// <summary>
        /// カード確定時のエフェクトを再生
        /// </summary>
        public async UniTask PlayConfirmEffectAsync()
        {
            if (cardEffectView != null && !destroyCts.IsCancellationRequested)
            {
                await cardEffectView.PlayConfirmFlashAsync(destroyCts.Token);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => parentView?.OnCardPointerEntered(this);
        public void OnPointerExit(PointerEventData eventData) => parentView?.OnCardPointerExited(this);

        private void OnDestroy()
        {
            if (cardButton != null)
            {
                cardButton.onClick.RemoveAllListeners();
            }

            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}

namespace Olca.Presentation.CustomScreen
{
    [System.Serializable]
    public sealed class CardVisualEntry
    {
        public string CardId;
        public Sprite Illustration;
        public Sprite CustomIcon;
        public CardVisualEntry Copy()=>new CardVisualEntry{CardId=CardId,Illustration=Illustration,CustomIcon=CustomIcon};
    }
}

namespace Olca.Presentation.CustomScreen
{
    /// <summary>カスタム画面専用の剣記号。本絵の縮小・切抜きではなくUI図形として独立制作する。</summary>
    public static class SwordMiniIcon
    {
        public static Sprite Create() => Create(false);
        public static Sprite Create(bool fire)
        {
            const int size=256;var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float t=(x-85)*.7f+(y-112)*.714f,n=(x-85)*.714f-(y-112)*.7f;
                Color color=Color.clear;
                if(t>=0&&t<155&&Mathf.Abs(n)<11*(1-t/165))color=new Color(.08f,.42f,.52f,1);
                if(t>=0&&t<140&&Mathf.Abs(n)<3)color=new Color(1,1,1,1);
                if(Mathf.Abs(t)<5&&Mathf.Abs(n)<34)color=new Color(.13f,.62f,.72f,1);
                if(t< -5&&t> -34&&Mathf.Abs(n)<6)color=new Color(.15f,.55f,.65f,1);
                if((t+38)*(t+38)+n*n<35)color=new Color(.65f,.97f,1,1);
                // 既存融合の炎属性を短い炎の線で示す専用mini。本絵を加工しない。
                if(fire&&t>35&&t<145&&Mathf.Abs(n-(18+6*Mathf.Sin(t*.08f)))<5)color=new Color(.95f,.46f,.17f,1);
                pixels[y*size+x]=color;
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=fire?"FireSwordCustomIcon":"SwordCustomIcon",filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,true);
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);sprite.name=fire?"FireSwordCustomIcon":"SwordCustomIcon";sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
        }
    }
    /// <summary>本絵とは独立した回復・防御のUI記号。下部はコスト文字用に空ける。</summary>
    public static class SupportMiniIcon
    {
        public static Sprite Create(string cardId)
        {
            if(cardId!="heal"&&cardId!="shield"&&cardId!="bomb"&&cardId!="tuning")throw new System.ArgumentException("専用miniの対象外です",nameof(cardId));
            const int size=256;var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float dx=Mathf.Abs(x-128),dy=Mathf.Abs(y-164);
                Color color=Color.clear;
                if(cardId=="bomb"){
                    float radius=Mathf.Sqrt((x-128)*(x-128)+(y-149)*(y-149));
                    if(radius<59)color=radius>49?new Color(.12f,.55f,.64f,1):new Color(.4f,.8f,.85f,.5f);
                    if(Mathf.Abs(x-135)<7&&y>202&&y<230)color=new Color(.12f,.55f,.64f,1);
                    if(Mathf.Abs(x-158)+Mathf.Abs(y-226)<15)color=new Color(.9f,.65f,.2f,1);
                }else if(cardId=="tuning"){
                    // 二本の音叉と中央の軸。カード本絵には転用しない。
                    if((dx<43&&dx>30&&y>144&&y<228)||(y>140&&y<153&&dx<43)||(dx<7&&y>85&&y<150))color=new Color(.12f,.55f,.64f,1);
                }else if(cardId=="heal"){
                    // 太い十字で100px表示でも回復を識別できる。
                    if((dx<=20&&dy<=67)||(dx<=67&&dy<=20))color=new Color(.13f,.62f,.72f,1);
                    if((dx<=13&&dy<=60)||(dx<=60&&dy<=13))color=new Color(.08f,.42f,.52f,1);
                }else{
                    // 上辺と肩を持ち、下端へ細くなる盾。内側は透ける面にする。
                    float halfWidth=y>=184?66:Mathf.Lerp(0,66,Mathf.Clamp01((y-88)/96f));
                    if(y>=88&&y<=226&&dx<=halfWidth){
                        bool edge=dx>=halfWidth-9||y>=217;
                        color=edge?new Color(.08f,.42f,.52f,1):new Color(.2f,.82f,.9f,.25f);
                        if(dx<=4&&y>=113&&y<=207)color=new Color(.13f,.62f,.72f,1);
                    }
                }
                pixels[y*size+x]=color;
            }
            var name=cardId+"CustomIcon";
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,true);
            var sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);sprite.name=name;sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
        }
    }

}

namespace Olca.Presentation.CustomScreen
{
    /// <summary>詳細用のタッチ長押し。UIの実時間だけを観測し、ゲームの論理フレームは進めない。</summary>
    public sealed class CustomDetailHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler, IPointerMoveHandler, ICancelHandler
    {
        // 未指定の操作感の暫定値。画面の見た目やゲームルールから独立させる。
        public const double HoldSeconds = .5;
        private Button button;
        private System.Func<bool> canOpen;
        private System.Action open;
        private System.Collections.Generic.HashSet<CustomDetailHold> group;
        private PointerEventData press;
        private Vector2 origin;
        private double began;
        private bool canceled;

        public void Configure(Button target, System.Func<bool> valid, System.Action show,
            System.Collections.Generic.HashSet<CustomDetailHold> peers)
        {
            Release(); button=target; canOpen=valid; open=show; group=peers;
            if(GetComponent<Olca.Unity.TouchUiCancelGuard>()==null)gameObject.AddComponent<Olca.Unity.TouchUiCancelGuard>();
        }
        private bool Valid()=>isActiveAndEnabled && button!=null && button.IsActive() && button.IsInteractable() && canOpen!=null && canOpen();
        public void OnPointerDown(PointerEventData data)
        {
            // 新Input Systemのタッチだけ。マウス・パッドの既存クリックは変更しない。
            if(!Olca.Unity.TouchUiCancelGuard.IsTouchPointer(data))return;
            if(press!=null){CancelPress();data.eligibleForClick=false;return;}
            if(!Valid()){data.eligibleForClick=false;return;}
            press=data;origin=data.position;began=Time.realtimeSinceStartupAsDouble;canceled=false;group.Add(this);
            if(group.Count>1)foreach(var hold in group)hold.CancelPress();
        }
        private void Update()=>Advance(Time.realtimeSinceStartupAsDouble);
        private void Advance(double now)
        {
            if(press==null||canceled)return;
            if(!press.eligibleForClick||!Valid()||press.dragging){CancelPress();return;}
            if(Olca.Unity.TouchUiCancelGuard.PressedTouchCount()>1){foreach(var hold in group)hold.CancelPress();return;}
            if(now-began<HoldSeconds)return;
            // InputSystemUIInputModuleが解放時に発行するクリックを成立前に無効化する。
            CancelPress();open();
        }
        public void OnPointerMove(PointerEventData data)
        {
            if(press==null||press.pointerId!=data.pointerId)return;
            int threshold=EventSystem.current!=null?EventSystem.current.pixelDragThreshold:10;
            if(data.dragging||(data.position-origin).sqrMagnitude>threshold*threshold)CancelPress();
        }
        public void OnPointerExit(PointerEventData data){if(press?.pointerId==data.pointerId)CancelPress();}
        public void OnPointerUp(PointerEventData data)
        {
            if(press?.pointerId!=data.pointerId)return;
            Advance(Time.realtimeSinceStartupAsDouble);Release(false);
        }
        public void OnCancel(BaseEventData data)=>CancelPress();
        public void CancelPress(){canceled=true;if(press!=null)press.eligibleForClick=false;}
        private void Release(bool cancel=true){if(cancel)CancelPress();group?.Remove(this);press=null;}
        private void OnDisable()=>Release();
        private void OnApplicationFocus(bool focused){if(!focused)CancelPress();}
    }
}
