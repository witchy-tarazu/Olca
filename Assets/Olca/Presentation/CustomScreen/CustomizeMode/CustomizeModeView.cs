using System;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.CustomScreen.CustomizeMode
{
    /// <summary>
    /// カスタマイズモードのUI管理。
    /// カードの組み合わせによって超越・調律・融合が自動判定されるため、
    /// モード選択は不要。カード選択→実行の単一フローを管理する。
    /// </summary>
    public class CustomizeModeView : MonoBehaviour
    {
        [Header("ボタン")]
        [SerializeField] private Button executeButton;  // 実行ボタン（有効な組み合わせが揃ったら有効）
        [SerializeField] private Button cancelButton;   // キャンセルボタン

        [Header("テキスト")]
        [SerializeField] private Text guideText;        // 操作ガイド・判定結果表示
        [SerializeField] private Text errorText;        // エラー表示

        private Font presentationFont;
        private float layoutAmount;
        public void ConfigurePresentation(Font font)
        {
            if(font!=null)presentationFont=font;
            var face=GetComponent<Image>();if(face!=null){face.sprite=Resources.Load<Sprite>("OlcaUI/description");face.type=Image.Type.Sliced;face.color=Color.white;}
            Olca.Presentation.Dungeon.UiLook.Button(executeButton);Olca.Presentation.Dungeon.UiLook.Button(cancelButton);
            ApplyLayout(layoutAmount);
        }
        public void ApplyLayout(float amount)
        {
            layoutAmount=Mathf.Clamp01(amount);var rect=(RectTransform)transform;rect.anchorMin=new Vector2(0,.5f);rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,Mathf.Lerp(-415,-740,layoutAmount));rect.sizeDelta=new Vector2(0,160);
            void Label(Text text,Color color){if(text==null)return;if(presentationFont!=null)text.font=presentationFont;text.color=color;text.raycastTarget=false;text.fontSize=Mathf.RoundToInt(Mathf.Lerp(26,34,layoutAmount));text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;var r=text.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(0,Mathf.Lerp(46,48,layoutAmount));r.sizeDelta=new Vector2(880,44);}
            Label(guideText,Color.white);Label(errorText,new Color(1,.8f,.67f));
            void Control(Button button,float x,string value){if(button==null)return;var r=(RectTransform)button.transform;r.anchorMin=r.anchorMax=new Vector2(x,.5f);r.anchoredPosition=new Vector2(0,-20);r.sizeDelta=new Vector2(400,Mathf.Lerp(72,84,layoutAmount));var text=button.GetComponentInChildren<Text>(true);if(text==null)return;if(presentationFont!=null)text.font=presentationFont;text.text=CustomDisplay.Katakana(value);text.fontSize=Mathf.RoundToInt(Mathf.Lerp(28,40,layoutAmount));text.resizeTextForBestFit=true;text.resizeTextMinSize=Mathf.RoundToInt(Mathf.Lerp(24,34,layoutAmount));text.resizeTextMaxSize=text.fontSize;text.alignment=TextAnchor.MiddleCenter;text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;text.rectTransform.offsetMin=new Vector2(12,6);text.rectTransform.offsetMax=new Vector2(-12,-6);}
            Control(executeButton,.25f,"加工を実行");Control(cancelButton,.75f,"取消して戻る");
        }

        /// <summary>カスタマイズモードがアクティブか</summary>
        public bool IsActive { get; private set; }

        /// <summary>実行ボタンが押された</summary>
        public event Action OnExecuteRequested;

        /// <summary>キャンセルされた</summary>
        public event Action OnCancelled;

        private void Awake()
        {
            if (executeButton != null)
                executeButton.onClick.AddListener(() => OnExecuteRequested?.Invoke());
            if (cancelButton != null)
                cancelButton.onClick.AddListener(() => OnCancelled?.Invoke());

            // 初期状態は非表示
            gameObject.SetActive(false);
        }

        /// <summary>
        /// カスタマイズモードを開始する
        /// </summary>
        public void Enter()
        {
            Olca.Presentation.Dungeon.UiLook.Button(executeButton);
            Olca.Presentation.Dungeon.UiLook.Button(cancelButton);
            ConfigurePresentation(presentationFont);
            IsActive = true;
            gameObject.SetActive(true);
            SetGuideText("素材を2枚選んでください");
            ClearError();
            UpdateExecuteButton(false);
        }

        /// <summary>
        /// カスタマイズモードを終了する
        /// </summary>
        public void Exit()
        {
            IsActive = false;
            gameObject.SetActive(false);
            ClearError();
        }

        /// <summary>
        /// 実行ボタンの有効/無効を更新する
        /// </summary>
        public void UpdateExecuteButton(bool interactable)
        {
            if (executeButton != null)
                executeButton.interactable = interactable;
        }

        /// <summary>
        /// 判定された操作種別に応じてガイドテキストを更新する
        /// </summary>
        public void UpdateOperationHint(string hint)
        {
            SetGuideText(hint);
        }

        /// <summary>
        /// エラーメッセージを表示する
        /// </summary>
        public void ShowError(string message)
        {
            if (errorText != null)
            {
                errorText.text = CustomDisplay.Katakana(message);
                if(guideText!=null)guideText.gameObject.SetActive(false);
                errorText.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// エラー表示をクリアする
        /// </summary>
        public void ClearError()
        {
            if(guideText!=null)guideText.gameObject.SetActive(true);
            if (errorText != null)
            {
                errorText.text = string.Empty;
                errorText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// キーボード入力処理（HandleInput から呼ばれる）
        /// </summary>
        public void HandleKeyInput()
        {
            if (!gameObject.activeInHierarchy) return;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnExecuteRequested?.Invoke();
            else if (Input.GetKeyDown(KeyCode.Escape))
                OnCancelled?.Invoke();
        }

        private void SetGuideText(string text)
        {
            if (guideText != null)
                guideText.text = CustomDisplay.Katakana(text);
        }

        private void OnDestroy()
        {
            if (executeButton != null) executeButton.onClick.RemoveAllListeners();
            if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
        }
    }
}
