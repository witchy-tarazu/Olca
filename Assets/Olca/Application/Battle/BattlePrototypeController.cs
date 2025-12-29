using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Interfaces;
using Olca.Unity;
using System.Collections.Generic;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Application.Battle
{
    /// <summary>
    /// フェーズ1動作確認用のバトルコントローラー
    /// </summary>
    public class BattlePrototypeController : MonoBehaviour
    {
        private BattleSystem battleSystem;
        private ITimeProvider timeProvider;
        private IInputProvider inputProvider;
        private IRandomProvider randomProvider;
        private IGameLogger logger;

        private void Start()
        {
            // プロバイダー初期化
            timeProvider = new UnityTimeProvider();
            inputProvider = new UnityInputProvider();
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();

            // プレイヤー作成
            var player = new PlayerCharacter(maxHp: 100, attackPower: 15, customSpeed: 0.5f);

            // 敵作成
            var enemies = new List<Enemy>
            {
                new Enemy("slime_01", "スライム", maxHp: 30, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("fire_enemy", "ファイアー", maxHp: 50, ElementType.Fire, new Vector2Int(5, 0))
            };

            // デッキ作成
            var initialCards = new List<Card>
            {
                new Card("cannon", "キャノン", 2, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "前方の敵に40ダメージ"),
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 20, "前方1マスに斬撃"),
                new Card("recover", "リカバリー", 3, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 30, "HP30回復"),
                new Card("water_ball", "ウォーターボール", 2, CardRarity.Normal, CardCategory.Attack, ElementType.Water, CardSeries.None, 30, "水属性攻撃"),
                new Card("fire_sword", "フレイムソード", 3, CardRarity.Limited, CardCategory.Attack, ElementType.Fire, CardSeries.Sword, 50, "火属性斬撃"),
            };
            var deck = new Deck(initialCards);

            // バトルシステム初期化
            battleSystem = new BattleSystem(player, enemies, deck, customGaugeSpeed: 0.5f, randomProvider: randomProvider, logger: logger);

            // イベント登録
            battleSystem.CustomGauge.OnGaugeFull += OnCustomGaugeFull;
            battleSystem.OnBattleEnd += OnBattleEnd;
            player.OnHpChanged += OnPlayerHpChanged;

            foreach (var enemy in enemies)
            {
                enemy.OnDeath += () => logger.LogInfo($"{enemy.Name}を撃破！");
            }

            logger.LogInfo("=== バトルプロトタイプ開始 ===");
            logger.LogInfo($"プレイヤーHP: {player.CurrentHp}/{player.MaxHp}");
            logger.LogInfo($"敵の数: {enemies.Count}");
            logger.LogInfo("");
            logger.LogInfo("操作方法:");
            logger.LogInfo("- Z: スライムに攻撃");
            logger.LogInfo("- X: ファイアーに攻撃");
            logger.LogInfo("- C: カスタム画面（ゲージ満タン時）");
            logger.LogInfo("- Space: プレイヤーにダメージ（テスト用）");
        }

        private void Update()
        {
            if (battleSystem.IsGameOver) return;

            // InputProviderの更新
            if (inputProvider is UnityInputProvider unityInput)
            {
                unityInput.Update();
            }

            // バトルシステム更新
            battleSystem.Tick(timeProvider.DeltaTime);

            // 入力処理
            HandleInput();
        }

        private void HandleInput()
        {
            var aliveEnemies = battleSystem.GetAliveEnemies();

            // Z: 最初の敵に攻撃
            if (Input.GetKeyDown(KeyCode.Z) && aliveEnemies.Count > 0)
            {
                battleSystem.PlayerBasicAttack(aliveEnemies[0]);
            }

            // X: 2番目の敵に攻撃
            if (Input.GetKeyDown(KeyCode.X) && aliveEnemies.Count > 1)
            {
                battleSystem.PlayerBasicAttack(aliveEnemies[1]);
            }

            // C: カスタム画面
            if (inputProvider.CustomScreenButtonDown && battleSystem.CustomGauge.IsFull)
            {
                OpenCustomScreen();
            }

            // Space: プレイヤーにダメージ（テスト用）
            if (Input.GetKeyDown(KeyCode.Space))
            {
                battleSystem.Player.TakeDamage(10);
                logger.LogWarning("プレイヤーが10ダメージを受けた（テスト）");
            }
        }

        private void OnCustomGaugeFull()
        {
            logger.LogInfo("*** カスタムゲージ満タン！Cキーでカスタム画面を開けます ***");
        }

        private void OpenCustomScreen()
        {
            logger.LogInfo("=== カスタム画面を開く ===");

            var random = new UnityRandomProvider();
            battleSystem.OpenCustomScreen(random);

            // カードリストを表示
            var customScreen = battleSystem.CustomScreen;
            logger.LogInfo($"ドロー可能なカード（{customScreen.CurrentHand.Count}枚）:");
            foreach (var card in customScreen.CurrentHand)
            {
                logger.LogInfo($"  - {card}");
            }

            logger.LogInfo($"最大コスト: {customScreen.MaxHandSize}");
            logger.LogInfo("※実際のゲームではUIで選択しますが、今回は自動で全選択します");

            // テスト: 自動的にコスト内で選択
            var selectedCount = 0;
            foreach (var card in new List<Card>(customScreen.CurrentHand))
            {
                if (customScreen.TrySelectCard(card))
                {
                    logger.LogInfo($"選択: {card.Name}");
                    selectedCount++;
                }
                else
                {
                    break; // コスト超過
                }
            }

            logger.LogInfo($"合計{selectedCount}枚選択");
            customScreen.Confirm();

            logger.LogInfo("=== カスタム画面を閉じる ===");
        }

        private void OnPlayerHpChanged(int current, int max)
        {
            logger.LogInfo($"プレイヤーHP: {current}/{max} ({(float)current / max * 100:F0}%)");
        }

        private void OnBattleEnd(BattleResult result)
        {
            if (result == BattleResult.Victory)
            {
                logger.LogInfo("========== 勝利！ ==========");
            }
            else
            {
                logger.LogError("========== 敗北... ==========");
            }
        }

        private void OnGUI()
        {
            // 画面上部にステータス表示
            GUILayout.BeginArea(new Rect(10, 10, 400, 300));
            GUILayout.Label($"<size=16><b>バトルプロトタイプ（フェーズ1）</b></size>");
            GUILayout.Space(10);

            if (battleSystem != null)
            {
                var player = battleSystem.Player;
                GUILayout.Label($"プレイヤーHP: {player.CurrentHp}/{player.MaxHp}");
                GUILayout.Label($"カスタムゲージ: {battleSystem.CustomGauge.CurrentGauge:P0}");

                GUILayout.Space(10);
                GUILayout.Label("<b>敵一覧:</b>");
                int index = 1;
                foreach (var enemy in battleSystem.Enemies)
                {
                    if (!enemy.IsDead)
                    {
                        GUILayout.Label($"  {index}. {enemy.Name} - HP:{enemy.CurrentHp}/{enemy.MaxHp} ({enemy.Element})");
                    }
                    else
                    {
                        GUILayout.Label($"  {index}. {enemy.Name} - <color=red>撃破済み</color>");
                    }
                    index++;
                }

                if (battleSystem.IsGameOver)
                {
                    GUILayout.Space(20);
                    if (battleSystem.State == BattleState.Victory)
                    {
                        GUILayout.Label("<size=20><color=green><b>勝利！</b></color></size>");
                    }
                    else
                    {
                        GUILayout.Label("<size=20><color=red><b>敗北...</b></color></size>");
                    }
                }
            }

            GUILayout.EndArea();
        }
    }
}
