using System.Collections.Generic;
using UnityEngine;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Unity;
using Olca.Presentation.CustomScreen;
using Cysharp.Threading.Tasks;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// バトルシステムのコントローラー
    /// Core層のBattleSystemとPresentation層のViewを仲介
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        // ========== 依存関係 ==========
        private BattleSystem battleSystem;
        private UnityInputProvider inputProvider;  // UnityInputProviderに変更（Update呼び出しのため）
        private IRandomProvider randomProvider;
        private IGameLogger logger;

        // ========== View参照 ==========
        [SerializeField] private BattleView battleView;
        [SerializeField] private CustomScreenView customScreenView;

        // ========== 初期化 ==========
        private void Start()
        {
            // プロバイダー生成
            inputProvider = new UnityInputProvider();
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();

            // テスト用のバトルセットアップ
            var player = new PlayerCharacter(maxHp: 100, initialPosition: Vector2Int.Zero, attackPower: 10);
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("goblin", "ゴブリン", 80, ElementType.None, new Vector2Int(5, 0))
            };

            var deck = CreateTestDeck();

            // カスタムゲージの設定
            // ゲージ最大値: 10.0（CustomGaugeSystem.MAX_GAUGE）
            // 速度: 2.0/秒（デフォルト）→ 5秒で満タン
            float customGaugeSpeed = 2.0f;  // 1秒あたりの増加量（10.0 ÷ 2.0 = 5秒で満タン）

            // BattleSystem生成
            battleSystem = new BattleSystem(player, enemies, deck,
                customGaugeSpeed, randomProvider, logger);

            // イベント購読
            battleSystem.CustomGauge.OnGaugeFull += OnGaugeFull;
            battleSystem.OnBattleEnd += OnBattleEnd;

            // View初期化
            battleView.Initialize(battleSystem);
            customScreenView.Initialize(battleSystem.CustomScreen);
            // Phase1: CustomScreenContainerは常にアクティブ。位置制御で表示/非表示を切り替える

            // バトル開始時: カスタムゲージを満タンにして、強制的にカスタム画面を開く
            battleSystem.CustomGauge.SetGauge(Olca.Core.Logic.Battle.CustomGaugeSystem.MAX_GAUGE);
            battleSystem.OpenCustomScreen(randomProvider);
            battleView.OpenCustomScreen();
        }

        // ========== メインループ ==========
        private void Update()
        {
            if (battleSystem == null) return;

            // 入力プロバイダーの状態を更新（立ち上がり検出用）
            inputProvider?.Update();

            // Core層のTickを呼ぶ（deltaTimeとinputを渡す）
            battleSystem.Tick(Time.deltaTime, inputProvider);

            // View更新（必要に応じて）
            battleView.Refresh();

            // ========== Altキー入力でカスタム画面を開く ==========
            // ゲージが満タンで、カスタム画面が閉じている時のみ有効
            if ((Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)) &&
                battleSystem.CustomGauge.IsFull && !battleSystem.CustomScreen.IsOpen)
            {
                battleSystem.OpenCustomScreen(randomProvider);
                battleView.OpenCustomScreen();
            }

            // ========== テスト用: スペースキーでキューの次のカードを使用 ==========
            // カスタム画面が閉じていて、キューにカードがある時のみ有効
            if (Input.GetKeyDown(KeyCode.Space) && !battleSystem.CustomScreen.IsOpen)
            {
                TestUseNextCardInQueue();
            }
        }

        // ========== イベントハンドラー ==========
        private void OnGaugeFull()
        {
            logger.LogInfo("カスタムゲージ満タン！準備完了");
            // 自動では開かない。ユーザーがAltキーを押すまで待つ
        }

        private void OnBattleEnd(BattleResult result)
        {
            logger.LogInfo($"バトル終了: {result}");

            if (result == BattleResult.Victory)
            {
                Debug.Log("勝利！");
            }
            else
            {
                Debug.Log("敗北...");
            }
        }

        // ========== カスタム画面が閉じられた時 ==========
        public void OnCustomScreenClosed()
        {
            // カスタム画面を閉じる（アニメーション完了まで待つ）
            CloseCustomScreenAsync().Forget();
        }

        /// <summary>
        /// カスタム画面を閉じる処理（アニメーション完了後にバトル再開）
        /// </summary>
        private async UniTaskVoid CloseCustomScreenAsync()
        {
            // アニメーション完了まで待つ
            await battleView.CloseCustomScreenAsync();

            // アニメーション完了後、カスタム画面の閉じる処理を完了
            // （IsOpenをfalseにして、カスタムゲージが溜まり始める）
            battleSystem.CustomScreen.CompleteClose();

            // カスタムゲージをリセット
            battleSystem.CustomGauge.Reset();
        }

        // ========== テスト用: キューの次のカードを使用 ==========
        private void TestUseNextCardInQueue()
        {
            if (battleSystem == null) return;

            var cardUsage = battleSystem.CardUsage;

            // キューが空の場合
            if (cardUsage.IsEmpty)
            {
                logger.LogInfo("[テスト] 使用可能なカードがありません");
                return;
            }

            // キューの現在のカードを取得
            var currentCard = cardUsage.CurrentCard;
            logger.LogInfo($"[テスト] {currentCard.Name}を使用します（残り{cardUsage.RemainingCount}枚）");

            // カードを使用（Core層のTryUseCurrentCard()を呼ぶ）
            bool success = cardUsage.TryUseCurrentCard();

            if (success)
            {
                logger.LogInfo($"[テスト] {currentCard.Name}を使用しました");
            }
            else
            {
                logger.LogWarning($"[テスト] {currentCard.Name}の使用に失敗しました");
            }

            // View更新（ダメージエフェクトが自動的に再生される）
            battleView.Refresh();
        }

        // ========== テスト用データ ==========
        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                    targetType: Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,
                    startupTime: 0.2f, recoveryTime: 0.3f),

                new Card("heal", "ヒール", 1, CardRarity.Normal, CardCategory.Support,
                    ElementType.None, CardSeries.None, 20, "HP回復",
                    targetType: Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,
                    startupTime: 0.1f, recoveryTime: 0.2f)
            };

            return new Deck(cards);
        }

        private void OnDestroy()
        {
            if (battleSystem != null)
            {
                battleSystem.CustomGauge.OnGaugeFull -= OnGaugeFull;
                battleSystem.OnBattleEnd -= OnBattleEnd;
            }
        }
    }
}
