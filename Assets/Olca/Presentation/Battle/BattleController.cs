using System.Collections.Generic;
using UnityEngine;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Unity;
using Olca.Presentation.CustomScreen;
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

            // BattleSystem生成
            battleSystem = new BattleSystem(player, enemies, deck,
                customGaugeSpeed: 1.0f, randomProvider, logger);

            // イベント購読
            battleSystem.CustomGauge.OnGaugeFull += OnGaugeFull;
            battleSystem.OnBattleEnd += OnBattleEnd;

            // View初期化
            battleView.Initialize(battleSystem);
            customScreenView.Initialize(battleSystem.CustomScreen);
            // Phase1: CustomScreenContainerは常にアクティブ。位置制御で表示/非表示を切り替える
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
        }

        // ========== イベントハンドラー ==========
        private void OnGaugeFull()
        {
            logger.LogInfo("カスタムゲージ満タン！");

            // カスタム画面を開く（アニメーション付き）
            battleSystem.OpenCustomScreen(randomProvider);
            battleView.OpenCustomScreen();
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
            // カスタム画面を閉じる（アニメーション付き）
            battleView.CloseCustomScreen();
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
