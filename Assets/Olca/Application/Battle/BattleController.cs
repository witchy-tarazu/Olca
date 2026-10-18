using System.Collections.Generic;
using UnityEngine;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Unity;
using Olca.Presentation.Battle;
using Olca.Presentation.CustomScreen;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Application.Battle
{
    /// <summary>
    /// バトル単体シーン用のコントローラー（スタンドアロンバトル確認用）。
    /// Core層のBattleSystemとPresentation層のViewを仲介し、
    /// バトルセッション共通操作はBattleSessionControllerに委譲する。
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        // ========== View参照 ==========
        [SerializeField] private BattleView battleView;
        [SerializeField] private CustomScreenView customScreenView;

        // ========== 内部状態 ==========
        private BattleSystem battleSystem;
        private UnityInputProvider inputProvider;
        private IRandomProvider randomProvider;
        private IGameLogger logger;
        private BattleSessionController battleSession;

        // ========== 初期化 ==========
        private void Start()
        {
            inputProvider = new UnityInputProvider();
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();

            // テスト用バトルセットアップ
            var player = new PlayerCharacter(maxHp: 100, initialPosition: Vector2Int.Zero, attackPower: 10);
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("goblin", "ゴブリン", 80, ElementType.None, new Vector2Int(5, 0))
            };

            // ゲージ速度: 2.0/秒 → 5秒で満タン
            battleSystem = new BattleSystem(player, enemies, CreateTestDeck(),
                customGaugeSpeed: 2.0f, randomProvider, logger);

            battleSystem.OnBattleEnd += OnBattleEnd;

            // View初期化
            battleView.Initialize(battleSystem);
            customScreenView.Initialize(battleSystem.CustomScreen);

            // バトルセッション共通操作をBattleSessionControllerに委譲
            battleSession = new BattleSessionController(battleView, customScreenView, randomProvider, logger);
            battleSession.Attach(battleSystem);

            // バトル開始時: ゲージを満タンにしてカスタム画面を即時オープン
            battleSystem.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battleSession.TryOpenCustomScreen();
        }

        // ========== メインループ ==========
        private void Update()
        {
            if (battleSystem == null) return;

            inputProvider?.Update();
            int deltaTimeMs = Mathf.RoundToInt(Time.deltaTime * 1000f);
            battleSystem.Tick(deltaTimeMs, inputProvider);
            battleView.Refresh();

            // Altキーでカスタム画面を手動オープン
            if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))
                battleSession.TryOpenCustomScreen();

            // テスト用: スペースキーでキューの次のカードを使用
            if (Input.GetKeyDown(KeyCode.Space) && !battleSystem.CustomScreen.IsOpen)
                battleSession?.UseCardFromInput();
        }

        // ========== バトル終了 ==========
        private void OnBattleEnd(BattleResult result)
        {
            logger.LogInfo($"バトル終了: {result}");
            battleSession.Detach();
        }

        private void OnDestroy()
        {
            battleSession?.Detach();
            if (battleSystem != null)
                battleSystem.OnBattleEnd -= OnBattleEnd;
        }

        // ========== テスト用データ ==========
        private Deck CreateTestDeck()
        {
            return new Deck(new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
                    targetType: Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,
                    startupTime: 0.2f, recoveryTime: 0.3f),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復",
                    targetType: Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,
                    startupTime: 0.1f, recoveryTime: 0.2f)
            });
        }
    }
}
