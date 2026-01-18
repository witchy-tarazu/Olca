using UnityEngine;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Unity;
using Olca.Presentation.Dungeon;
using Olca.Presentation.Battle;
using System.Collections.Generic;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Application.Dungeon
{
    /// <summary>
    /// ダンジョンとバトルの統合コントローラー
    /// ロジック層とプレゼンテーション層を繋ぐ
    /// </summary>
    public class DungeonBattleController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DungeonView dungeonView;
        [SerializeField] private BattleView battleView;
        [SerializeField] private Camera mainCamera;

        [Header("Settings")]
        [SerializeField] private int dungeonCellCount = 20;
        [SerializeField] private int playerMaxHp = 100;
        [SerializeField] private int playerAttackPower = 10;

        private DungeonBattleManager manager;
        private UnityRandomProvider randomProvider;
        private UnityLogger logger;
        private UnityInputProvider inputProvider;

        private void Start()
        {
            // Unity依存サービスの初期化
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();

            // ゲームデータを生成
            var dungeonConfig = DungeonConfig.CreateTestDungeon(dungeonCellCount);
            var playerCharacter = new PlayerCharacter(
                maxHp: playerMaxHp,
                initialPosition: new Vector2Int(0, 0),
                attackPower: playerAttackPower);
            var initialDeck = CreateInitialDeck();

            // マネージャー初期化
            manager = new DungeonBattleManager(
                dungeonConfig,
                playerCharacter,
                initialDeck,
                randomProvider,
                logger);

            // イベント購読
            manager.OnBattleStarted += OnBattleStarted;
            manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;

            // Viewの初期化
            dungeonView?.Initialize(manager.Dungeon);
            battleView?.gameObject.SetActive(false);

            logger.LogInfo("[DungeonBattleController] 初期化完了");
        }

        private void Update()
        {
            if (manager == null) return;

            // 入力処理
            HandleInput();

            // ロジック更新
            manager.Tick(Time.deltaTime, inputProvider);

            // View更新
            UpdateViews();
        }

        /// <summary>
        /// 入力処理
        /// </summary>
        private void HandleInput()
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    // ダンジョン中の入力
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        manager.Jump();
                    }

                    if (Input.GetKey(KeyCode.LeftShift))
                    {
                        manager.StartDash();
                    }
                    else
                    {
                        manager.StopDash();
                    }
                    break;

                case GamePhase.Battle:
                    // バトル中の入力（InputProviderが処理）
                    break;
            }
        }

        /// <summary>
        /// View更新
        /// </summary>
        private void UpdateViews()
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    dungeonView?.UpdateView();
                    break;

                case GamePhase.Battle:
                    battleView?.Refresh();
                    break;
            }
        }

        /// <summary>
        /// バトル開始時のカメラ遷移
        /// </summary>
        private void OnBattleStarted(BattleSystem battle, ContactInfo contactInfo)
        {
            logger.LogInfo("[Controller] バトル開始 - カメラ遷移");

            // ダンジョンViewを非表示
            dungeonView?.gameObject.SetActive(false);

            // バトルViewを表示
            battleView?.gameObject.SetActive(true);
            battleView?.Initialize(battle);

            // カメラをバトル位置に移動（仮実装）
            // TODO: アニメーション付きカメラ遷移
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            }
        }

        /// <summary>
        /// ゲームオーバー時
        /// </summary>
        private void OnGameOver()
        {
            logger.LogInfo("[Controller] ゲームオーバー");
            // TODO: ゲームオーバー画面表示
        }

        /// <summary>
        /// ダンジョンクリア時
        /// </summary>
        private void OnDungeonCleared(IReadOnlyList<Card> obtainedCards)
        {
            logger.LogInfo($"[Controller] ダンジョンクリア！ 獲得カード: {obtainedCards.Count}枚");
            // TODO: クリア画面表示
        }

        /// <summary>
        /// 初期デッキを生成
        /// </summary>
        private Deck CreateInitialDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復"),
                new Card("shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 15, "防御")
            };

            return new Deck(cards);
        }

        private void OnDestroy()
        {
            // イベント購読解除
            if (manager != null)
            {
                manager.OnBattleStarted -= OnBattleStarted;
                manager.OnGameOver -= OnGameOver;
                manager.OnDungeonClearCompleted -= OnDungeonCleared;
            }
        }
    }
}
