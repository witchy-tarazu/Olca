using UnityEngine;
using Olca.Core.Interfaces;
using Olca.Unity;

namespace Olca.Unity
{
    /// <summary>
    /// フェーズ0の動作確認用スクリプト
    /// 空のGameObjectにアタッチして実行してください
    /// </summary>
    public class Phase0VerificationScript : MonoBehaviour
    {
        private IRandomProvider randomProvider;
        private ITimeProvider timeProvider;
        private IInputProvider inputProvider;
        private IGameLogger logger;

        private void Start()
        {
            // プロバイダーの初期化
            randomProvider = new UnityRandomProvider();
            timeProvider = new UnityTimeProvider();
            inputProvider = new UnityInputProvider();
            logger = new UnityLogger();

            logger.LogInfo("=== フェーズ0 動作確認開始 ===");

            // 乱数テスト
            int randomInt = randomProvider.Range(1, 100);
            float randomFloat = randomProvider.Range(0f, 1f);
            logger.LogInfo($"ランダム整数: {randomInt}");
            logger.LogInfo($"ランダム浮動小数点: {randomFloat}");

            // 時間テスト
            logger.LogInfo($"現在時刻: {timeProvider.Time}秒");
            logger.LogInfo($"デルタタイム: {timeProvider.DeltaTime}秒");

            logger.LogInfo("=== フェーズ0 動作確認完了 ===");
            logger.LogInfo("キーボード入力をテストしてください:");
            logger.LogInfo("- WASD: 移動");
            logger.LogInfo("- Space: ジャンプ");
            logger.LogInfo("- Shift: ダッシュ");
            logger.LogInfo("- Z: 固有攻撃（長押し1秒以上でタメ攻撃）");
            logger.LogInfo("- X: カード使用");
            logger.LogInfo("- C: カスタム画面");
        }

        private void Update()
        {
            // InputProviderの更新（長押し時間計測のため）
            if (inputProvider is UnityInputProvider unityInput)
            {
                unityInput.Update();
            }

            // 入力のログ出力（何か入力があった場合のみ）
            if (Mathf.Abs(inputProvider.Horizontal) > 0.1f || Mathf.Abs(inputProvider.Vertical) > 0.1f)
            {
                logger.LogInfo($"移動入力: ({inputProvider.Horizontal:F2}, {inputProvider.Vertical:F2})");
            }

            if (inputProvider.JumpButtonDown)
            {
                logger.LogInfo("ジャンプボタンが押されました！");
            }

            if (inputProvider.DashButton)
            {
                logger.LogInfo("ダッシュボタンが押されています");
            }

            if (inputProvider.BasicAttackButton)
            {
                float holdTime = inputProvider.BasicAttackButtonHoldTime;
                if (holdTime > 1.0f)
                {
                    logger.LogWarning($"固有攻撃タメ中: {holdTime:F2}秒（タメ攻撃発動可能！）");
                }
                else if (holdTime > 0.5f)
                {
                    logger.LogInfo($"固有攻撃ボタン長押し中: {holdTime:F2}秒");
                }
                else if (holdTime < 0.1f)
                {
                    // 押され始めの瞬間だけログ
                    logger.LogInfo("固有攻撃ボタンが押されました（Zキー）");
                }
            }

            if (inputProvider.CustomScreenButtonDown)
            {
                logger.LogInfo("カスタム画面ボタンが押されました！");
            }

            if (inputProvider.CardUseButtonDown)
            {
                logger.LogInfo("カード使用ボタンが押されました！");
            }
        }
    }
}
