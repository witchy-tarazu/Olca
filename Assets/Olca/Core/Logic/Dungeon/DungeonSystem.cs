using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// ダンジョンシステム全体の管理クラス
    /// Unity非依存で、ロジックのみを扱う
    /// </summary>
    public class DungeonSystem
    {
        // ========== 依存関係 ==========
        private readonly DungeonMap map;
        private readonly PlayerMovement playerMovement;
        private readonly ContactSystem contactSystem;
        private readonly ActionSystem actionSystem;
        private readonly CardRewardSystem cardRewardSystem;
        private readonly ObstacleSystem obstacleSystem;
        private readonly PlayerCharacter playerCharacter;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        // ========== 状態管理 ==========
        public bool IsInBattle { get; private set; }
        public bool IsDungeonCleared { get; private set; }

        // ========== 接敵情報保持 ==========
        private ContactInfo currentContactInfo;

        // ========== 障害物通過済み管理 ==========
        private readonly HashSet<int> processedObstacleCells = new HashSet<int>();

        // ========== プロパティ ==========
        public float PlayerPosition => playerMovement.CurrentPosition;
        public float PlayerHeight => playerMovement.Height;
        public bool IsDashing => playerMovement.IsDashing;
        public PlayerCharacter Player => playerCharacter;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DungeonSystem(
            DungeonConfig config,
            PlayerCharacter player,
            Deck initialDeck,
            IRandomProvider random,
            IGameLogger logger)
        {
            // 依存関係の初期化
            map = new DungeonMap(config);
            playerMovement = new PlayerMovement(initialPosition: 0f);
            contactSystem = new ContactSystem(map);
            actionSystem = new ActionSystem();
            cardRewardSystem = new CardRewardSystem(initialDeck, random);
            obstacleSystem = new ObstacleSystem(logger);
            playerCharacter = player;
            randomProvider = random;
            this.logger = logger;

            // イベント購読
            contactSystem.OnEnemyContact += OnEnemyContact;

            IsInBattle = false;
            IsDungeonCleared = false;

            logger.LogInfo($"[DungeonSystem] ダンジョン初期化: {config.DungeonName}");
        }

        /// <summary>
        /// 毎フレーム更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        public void Tick(int deltaTimeMs)
        {
            if (IsInBattle || IsDungeonCleared) return;

            // プレイヤー移動更新
            playerMovement.Update(deltaTimeMs);

            // 障害物判定
            CheckObstacles();

            // 接敵判定
            contactSystem.CheckContact(playerMovement);
        }

        // ========== プレイヤーアクション ==========

        /// <summary>
        /// ジャンプ
        /// </summary>
        public void Jump()
        {
            if (IsInBattle) return;
            playerMovement.Jump();
            logger.LogInfo("[Dungeon] ジャンプ！");
        }

        /// <summary>
        /// ダッシュ開始
        /// </summary>
        public void StartDash()
        {
            if (IsInBattle) return;
            playerMovement.StartDash();
        }

        /// <summary>
        /// ダッシュ停止
        /// </summary>
        public void StopDash()
        {
            playerMovement.StopDash();
        }

        // ========== 接敵処理 ==========

        /// <summary>
        /// 敵との接敵時
        /// </summary>
        private void OnEnemyContact(ContactInfo contactInfo)
        {
            IsInBattle = true;
            currentContactInfo = contactInfo;  // 接敵情報を保持

            // プレイヤー移動を停止
            playerMovement.Pause();

            // ダッシュ接敵の場合、効果を設定
            if (contactInfo.IsDashContact)
            {
                contactInfo.DashEffect = actionSystem.GetDashEffect();
                logger.LogInfo($"[Dungeon] ダッシュ接敵！効果: {contactInfo.DashEffect}");
            }
            else
            {
                logger.LogInfo($"[Dungeon] 通常接敵");
            }

            logger.LogInfo($"[Dungeon] 接敵: {contactInfo.Enemy.Name}");

            // バトル開始イベント発火
            OnBattleStart?.Invoke(contactInfo);
        }

        /// <summary>
        /// バトル終了後の処理（外部から呼ばれる）
        /// </summary>
        public void OnBattleCompleted(bool victory)
        {
            IsInBattle = false;

            if (victory)
            {
                logger.LogInfo("[Dungeon] バトル勝利");

                // カードドロップ判定
                if (currentContactInfo != null && currentContactInfo.Enemy != null)
                {
                    Enemy defeatedEnemy = currentContactInfo.Enemy;

                    if (cardRewardSystem.ShouldDropCard(defeatedEnemy))
                    {
                        // カード選択肢を生成
                        List<Card> choices = cardRewardSystem.GenerateCardChoices(defeatedEnemy);
                        logger.LogInfo($"[Dungeon] カードドロップ！{choices.Count}枚から選択");

                        // カード選択画面を表示（イベント発火）
                        OnCardRewardOffered?.Invoke(choices);
                        // カード選択が完了するまで、ダンジョンには戻らない
                        return;
                    }
                    else
                    {
                        logger.LogInfo("[Dungeon] カードドロップなし");
                    }

                    // 敵をマップから削除
                    map.RemoveEnemy(defeatedEnemy);
                }
            }
            else
            {
                logger.LogInfo("[Dungeon] バトル敗北 - ゲームオーバー");
                OnDungeonFailed?.Invoke();
                return;
            }

            // ダンジョン再開
            ReturnToDungeon();
        }

        /// <summary>
        /// カード選択完了後、ダンジョンに戻る
        /// </summary>
        public void OnCardSelectionCompleted(Card selectedCard)
        {
            if (selectedCard != null)
            {
                cardRewardSystem.SelectCard(selectedCard);
                logger.LogInfo($"[Dungeon] カード獲得: {selectedCard.Name}");
            }

            // 敵をマップから削除
            if (currentContactInfo != null && currentContactInfo.Enemy != null)
            {
                map.RemoveEnemy(currentContactInfo.Enemy);
            }

            // ダンジョン再開
            ReturnToDungeon();
        }

        /// <summary>
        /// ダンジョンに戻る（内部処理）
        /// </summary>
        private void ReturnToDungeon()
        {
            // 接敵リセット
            contactSystem.ResetContact();

            // プレイヤー移動を再開
            playerMovement.Resume();

            logger.LogInfo("[Dungeon] ダンジョン再開");
        }

        /// <summary>
        /// 現在のデッキを取得
        /// </summary>
        public Deck GetCurrentDeck() => cardRewardSystem.GetCurrentDeck();

        /// <summary>
        /// 入手したカード一覧を取得
        /// </summary>
        public IReadOnlyList<Card> GetObtainedCards() => cardRewardSystem.ObtainedCards;

        // ========== 障害物処理 ==========

        /// <summary>
        /// 障害物判定
        /// </summary>
        private void CheckObstacles()
        {
            MapCell currentCell = map.GetCellAtPosition(playerMovement.CurrentPosition);
            if (currentCell == null) return;

            // 既に処理済みの障害物セルはスキップ
            if (processedObstacleCells.Contains(currentCell.Index))
            {
                return;
            }

            var result = obstacleSystem.CheckObstacleContact(
                currentCell,
                playerMovement.CurrentPosition,
                playerMovement.Height);

            if (result.IsContact)
            {
                // 接触した時点で処理済みとしてマーク（回避成功・失敗に関わらず）
                processedObstacleCells.Add(currentCell.Index);

                if (!result.IsAvoided)
                {
                    // ペナルティ適用
                    ApplyObstaclePenalty(result.Obstacle);
                }
            }
        }

        /// <summary>
        /// 障害物ペナルティを適用
        /// </summary>
        private void ApplyObstaclePenalty(Obstacle obstacle)
        {
            var penaltyInfo = obstacleSystem.CreatePenaltyInfo(obstacle);

            // ペナルティイベント発火（外部で処理）
            OnObstaclePenalty?.Invoke(penaltyInfo);

            // 即時ダメージの場合はプレイヤーHPを減らす
            if (penaltyInfo.PenaltyType == ObstaclePenaltyType.ImmediateDamage)
            {
                playerCharacter.TakeDamage(penaltyInfo.Damage);
                logger.LogInfo($"[Dungeon] 障害物ダメージ: {penaltyInfo.Damage}");

                // HP0以下でゲームオーバー
                if (playerCharacter.CurrentHp <= 0)
                {
                    logger.LogInfo("[Dungeon] 障害物でゲームオーバー");
                    OnDungeonFailed?.Invoke();
                }
            }
        }

        // ========== イベント ==========
        public event Action<ContactInfo> OnBattleStart;
        public event Action<List<Card>> OnCardRewardOffered;  // カード選択画面表示
        public event Action<PenaltyInfo> OnObstaclePenalty;   // 障害物ペナルティ
        public event Action OnDungeonFailed;
        public event Action OnDungeonCleared;
    }
}
