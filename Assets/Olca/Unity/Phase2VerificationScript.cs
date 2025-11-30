using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using System.Collections.Generic;

namespace Olca.Unity
{
    /// <summary>
    /// フェーズ2の動作確認スクリプト
    /// カスタムシステムとカード使用の動作を検証
    /// </summary>
    public class Phase2VerificationScript : MonoBehaviour
    {
        private BattleSystem battleSystem;
        private UnityRandomProvider randomProvider;
        private UnityLogger logger;

        private void Start()
        {
            logger = new UnityLogger();
            randomProvider = new UnityRandomProvider();

            InitializeBattle();
            RunVerification();
        }

        /// <summary>
        /// バトルシステムを初期化
        /// </summary>
        private void InitializeBattle()
        {
            logger.LogInfo("=== フェーズ2: カスタムシステム検証開始 ===");

            // プレイヤーキャラクター作成
            var player = new PlayerCharacter(
                maxHp: 100,
                attackPower: 10
            );

            // 敵作成
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 30, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("goblin", "ゴブリン", 50, ElementType.Fire, new Vector2Int(5, 0))
            };

            // デッキ作成
            var deck = CreateTestDeck();

            // バトルシステム作成
            battleSystem = new BattleSystem(player, enemies, deck, customGaugeSpeed: 2.0f, logger: logger);

            // イベント購読
            battleSystem.CustomScreen.OnScreenOpened += OnCustomScreenOpened;
            battleSystem.CustomScreen.OnCardSelected += OnCardSelected;
            battleSystem.CustomScreen.OnScreenClosed += OnCustomScreenClosed;
            battleSystem.CardUsage.OnCardUsed += OnCardUsed;

            logger.LogInfo($"プレイヤー HP: {player.CurrentHp}/{player.MaxHp}");
            logger.LogInfo($"敵: {enemies.Count}体");
            logger.LogInfo($"デッキ: {deck.TotalCount}枚");
        }

        /// <summary>
        /// テスト用デッキを作成
        /// </summary>
        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                // ソード系統
                new Card("sword1", "ソード", 3, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 20, "前方の敵に20ダメージ"),
                new Card("sword2", "ワイドソード", 4, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方の敵に30ダメージ"),
                new Card("sword3", "ロングソード", 5, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 40, "前方の敵に40ダメージ"),

                // ボム系統
                new Card("bomb1", "ボム", 3, CardRarity.Normal, CardCategory.Attack,
                    ElementType.Fire, CardSeries.Bomb, 25, "全体に25ダメージ"),

                // 回復
                new Card("heal1", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 30, "HPを30回復"),

                // 追加のカード
                new Card("cannon1", "キャノン", 4, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Canon, 35, "貫通攻撃"),
            };

            return new Deck(cards);
        }

        /// <summary>
        /// 動作確認
        /// </summary>
        private void RunVerification()
        {
            logger.LogInfo("\n--- 1. カスタムゲージのテスト ---");

            // カスタムゲージを満タンにする
            battleSystem.CustomGauge.SetGauge(1.0f);
            logger.LogInfo($"カスタムゲージ: {battleSystem.CustomGauge.CurrentGauge:F2} (満タン: {battleSystem.CustomGauge.IsFull})");

            logger.LogInfo("\n--- 2. カスタム画面を開く ---");
            battleSystem.OpenCustomScreen(randomProvider);
        }

        /// <summary>
        /// カスタム画面が開いた時の処理
        /// </summary>
        private void OnCustomScreenOpened(List<Card> drawnCards)
        {
            logger.LogInfo($"カスタム画面オープン: {drawnCards.Count}枚ドロー");

            foreach (var card in drawnCards)
            {
                logger.LogInfo($"  - {card.Name} (コスト:{card.Cost}, 系統:{card.Series})");
            }

            logger.LogInfo("\n--- 3. カードを選択 ---");

            // カードを選択（系統ボーナスのテスト）
            var hand = battleSystem.CustomScreen.CurrentHand;

            for (int i = 0; i < System.Math.Min(3, hand.Count); i++)
            {
                var card = hand[0]; // 常に先頭を選択
                bool success = battleSystem.CustomScreen.TrySelectCard(card);

                if (success)
                {
                    logger.LogInfo($"選択成功: {card.Name} (残りコスト: {battleSystem.CustomScreen.RemainingCost})");
                }
                else
                {
                    logger.LogWarning($"選択失敗: {card.Name}");
                }
            }

            logger.LogInfo($"\n選択完了: {battleSystem.CustomScreen.SelectedCards.Count}枚");

            // カスタム画面を閉じる
            battleSystem.CustomScreen.Confirm();
        }

        /// <summary>
        /// カードが選択された時の処理
        /// </summary>
        private void OnCardSelected(Card card)
        {
            logger.LogInfo($"[イベント] カード選択: {card.Name}");
        }

        /// <summary>
        /// カスタム画面が閉じた時の処理
        /// </summary>
        private void OnCustomScreenClosed(List<Card> selectedCards)
        {
            logger.LogInfo($"\nカスタム画面クローズ: {selectedCards.Count}枚選択");

            logger.LogInfo("\n--- 4. カードを使用 ---");

            // すべてのカードを使用
            while (!battleSystem.CardUsage.IsEmpty)
            {
                battleSystem.UseCurrentCard();
            }

            logger.LogInfo("\n=== フェーズ2検証完了 ===");
            LogBattleStatus();
        }

        /// <summary>
        /// カードが使用された時の処理
        /// </summary>
        private void OnCardUsed(Card card)
        {
            logger.LogInfo($"[イベント] カード使用: {card.Name}");
        }

        /// <summary>
        /// バトル状況をログ出力
        /// </summary>
        private void LogBattleStatus()
        {
            logger.LogInfo("\n--- バトル状況 ---");
            logger.LogInfo($"プレイヤーHP: {battleSystem.Player.CurrentHp}/{battleSystem.Player.MaxHp}");

            foreach (var enemy in battleSystem.Enemies)
            {
                logger.LogInfo($"敵: {enemy.Name} HP:{enemy.CurrentHp}/{enemy.MaxHp} (生存:{!enemy.IsDead})");
            }

            logger.LogInfo($"バトル状態: {battleSystem.State}");
        }

        private void Update()
        {
            // バトルシステムの更新
            if (battleSystem != null && !battleSystem.IsGameOver)
            {
                battleSystem.Tick(Time.deltaTime);
            }
        }
    }
}
