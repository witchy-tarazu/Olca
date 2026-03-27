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
            IsActive = true;
            gameObject.SetActive(true);
            SetGuideText("カスタマイズ: カードを選んでください（ESC でキャンセル）");
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
                errorText.text = message;
                errorText.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// エラー表示をクリアする
        /// </summary>
        public void ClearError()
        {
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
                guideText.text = text;
        }

        private void OnDestroy()
        {
            if (executeButton != null) executeButton.onClick.RemoveAllListeners();
            if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
        }
    }
}
