using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;

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
        private readonly Enemy finalBoss;
        private bool finalBossDefeated;
        private readonly PlayerMovement playerMovement;
        private readonly ContactSystem contactSystem;
        private readonly ActionSystem actionSystem;
        private readonly CardRewardSystem cardRewardSystem;
        private readonly ObstacleSystem obstacleSystem;
        private readonly PlayerCharacter playerCharacter;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        // ========== バトルシステム統合 ==========
        private BattleSystem currentBattle;

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
        /// <summary>ダンジョンマップ（View側の敵スプライト初期配置に使用）</summary>
        public DungeonMap Map => map;

        /// <summary>
        /// 現在のバトルシステム（バトル中のみ有効）
        /// </summary>
        public BattleSystem CurrentBattle => currentBattle;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        private readonly ExceedSystem exceedSystem;
        private readonly TuningSystem tuningSystem;
        private readonly FusionSystem fusionSystem;

        public DungeonSystem(
            DungeonConfig config,
            PlayerCharacter player,
            Deck initialDeck,
            IRandomProvider random,
            IGameLogger logger,
            ExceedSystem exceedSystem = null,
            TuningSystem tuningSystem = null,
            FusionSystem fusionSystem = null)
        {
            // 依存関係の初期化
            map = new DungeonMap(config);
            // 現行マップのボス種別を使い、最奥のボスをクリア対象として保持する。
            // 撃破後はマスが空になるため、敵の参照を初期化時に確定する。
            int finalBossCell = -1;
            foreach (var cell in config.MapCells)
            {
                if (cell != null && cell.Type == CellType.Boss && cell.Index > finalBossCell)
                {
                    finalBoss = cell.Enemy;
                    finalBossCell = cell.Index;
                }
            }
            playerMovement = new PlayerMovement(initialPosition: 0f);
            actionSystem = new ActionSystem();
            contactSystem = new ContactSystem(map, actionSystem);
            cardRewardSystem = new CardRewardSystem(initialDeck, random);
            obstacleSystem = new ObstacleSystem(logger);
            playerCharacter = player;
            randomProvider = random;
            this.logger = logger;
            this.exceedSystem = exceedSystem;
            this.tuningSystem = tuningSystem;
            this.fusionSystem = fusionSystem;

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
            // ダッシュ接敵の場合、効果を設定
            if (contactInfo.IsDashContact)
            {
                contactInfo.DashEffect = actionSystem.GetDashEffect();
            }

            // バトル開始
            StartBattle(contactInfo);
        }

        // ========== バトル統合（フェーズ5） ==========

        /// <summary>
        /// バトルを開始する
        /// </summary>
        /// <param name="contactInfo">接敵情報</param>
        public void StartBattle(ContactInfo contactInfo)
        {
            IsInBattle = true;
            currentContactInfo = contactInfo;

            // プレイヤー移動を停止
            playerMovement.Pause();

            logger.LogInfo($"[Dungeon→Battle] 接敵開始");
            logger.LogInfo($"  - 敵: {contactInfo.Enemy.Name}");
            logger.LogInfo($"  - ダッシュ: {contactInfo.IsDashContact}");
            logger.LogInfo($"  - プレイヤーHP: {playerCharacter.CurrentHp}/{playerCharacter.MaxHp}");

            // 画面内の敵を集める
            List<Enemy> enemies = GatherEnemiesForBattle(contactInfo);

            // ダンジョンの位置からバトルグリッドに変換
            SetupBattlePositions(contactInfo, enemies);

            // グリッド原点を「敵セル - 4」に統一
            // 通常接敵: 敵がセル4 → originX = cellIndex - 4 → 敵は cellIndex に留まる
            // ダッシュ接敵: 敵がセル5 → originX = cellIndex - 4 → 敵は cellIndex+1 に見える（ノックバック表現）
            const int GRID_ORIGIN_OFFSET = 4;
            contactInfo.ContactPositionX = contactInfo.EnemyCellIndex - GRID_ORIGIN_OFFSET;

            // バトル開始前にデッキをシャッフル（2戦目以降のドロー履歴リセット）
            cardRewardSystem.GetCurrentDeck().Shuffle(randomProvider);

            // バトルシステムを初期化
            // 注意: プレイヤーのHPはダンジョン探索中のまま（参照共有）
            currentBattle = new BattleSystem(
                playerCharacter,
                enemies,
                cardRewardSystem.GetCurrentDeck(),
                customGaugeSpeed: 2.0f,
                randomProvider,
                logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem
            );

            // ダッシュ接敵の効果を適用
            if (contactInfo.IsDashContact && contactInfo.DashEffect.HasValue)
            {
                ApplyDashEffect(contactInfo.DashEffect.Value, enemies);
            }

            // バトル終了イベントを購読
            currentBattle.OnBattleEnd += OnBattleEndFromBattleSystem;

            logger.LogInfo($"[Dungeon→Battle] バトルシステム初期化完了");
            logger.LogInfo($"  - 参加敵数: {enemies.Count}");
            logger.LogInfo($"  - デッキ枚数: {cardRewardSystem.GetCurrentDeck().TotalCount}");

            // バトル開始イベント発火
            OnBattleStart?.Invoke(contactInfo);
        }

        /// <summary>
        /// 画面内の敵を集めてバトルに参加させる
        /// </summary>
        private List<Enemy> GatherEnemiesForBattle(ContactInfo contactInfo)
        {
            List<Enemy> enemies = new List<Enemy>();

            // 接敵した敵を追加
            enemies.Add(contactInfo.Enemy);

            // バトルグリッド内（6セル幅）に収まる敵のみ巻き込む
            // originX = enemyCellIndex - 4 なのでグリッド範囲は [enemyCellIndex-4, enemyCellIndex+2]
            // プレイヤー位置からの検索範囲はグリッド右端（enemyCellIndex+2）まで
            const float SCREEN_RANGE = 4.0f;
            List<Enemy> nearbyEnemies = map.GetEnemiesInRange(
                playerMovement.CurrentPosition,
                SCREEN_RANGE
            );

            foreach (var enemy in nearbyEnemies)
            {
                if (!enemy.IsDead && enemy != contactInfo.Enemy && !enemies.Contains(enemy))
                {
                    enemies.Add(enemy);
                    logger.LogInfo($"  - 巻き込み: {enemy.Name}");
                }
            }

            return enemies;
        }

        /// <summary>
        /// ダンジョンの位置関係からバトルグリッド（6x1）に位置を変換
        /// </summary>
        private void SetupBattlePositions(ContactInfo contactInfo, List<Enemy> enemies)
        {
            // プレイヤーは左側（0〜2マス目）、敵は右側（3〜5マス目）に配置
            int playerGridX;
            int primaryEnemyGridX;

            // バトルグリッド上の位置は常に固定（左中央・右中央）
            // ノックバック量はダンジョンマップ上の移動量で表現する
            playerGridX = 1;        // 左側中央（グリッド0〜2の中央）
            primaryEnemyGridX = 4;  // 右側中央（グリッド3〜5の中央）

            // プレイヤー位置を設定
            playerCharacter.MoveTo(new Vector2Int(playerGridX, 0));
            logger.LogInfo($"  - プレイヤー位置: ({playerGridX}, 0)");

            // メインの敵位置を設定
            contactInfo.Enemy.MoveTo(new Vector2Int(primaryEnemyGridX, 0));

            // 他の敵は隣接マスに配置
            int nextEnemyX = primaryEnemyGridX - 1;
            for (int i = 1; i < enemies.Count; i++)
            {
                if (nextEnemyX >= 3)  // 敵エリア（3〜5）に収める
                {
                    enemies[i].MoveTo(new Vector2Int(nextEnemyX, 0));
                    logger.LogInfo($"  - {enemies[i].Name}位置: ({nextEnemyX}, 0)");
                    nextEnemyX--;
                }
            }
        }

        /// <summary>
        /// ダッシュ接敵の効果をバトルに適用
        /// </summary>
        private void ApplyDashEffect(ActionSystem.DashContactEffect effect, List<Enemy> enemies)
        {
            switch (effect)
            {
                case ActionSystem.DashContactEffect.InitialDamage:
                    // 全敵に初期ダメージ
                    const int DASH_DAMAGE = 15;  // TODO: プレイヤー攻撃力に応じて調整
                    foreach (var enemy in enemies)
                    {
                        enemy.TakeDamage(DASH_DAMAGE, ElementType.None);
                        logger.LogInfo($"  - {enemy.Name}に{DASH_DAMAGE}ダメージ！（ダッシュ接敵）");
                    }
                    break;

                case ActionSystem.DashContactEffect.Stun:
                    // 全敵に3秒スタンを付与
                    const int STUN_DURATION_MS = 3000;
                    foreach (var enemy in enemies)
                    {
                        enemy.ApplyStun(STUN_DURATION_MS);
                    }
                    logger.LogInfo($"  - 敵をスタン（{STUN_DURATION_MS / 1000}秒）！（ダッシュ接敵）");
                    break;

                case ActionSystem.DashContactEffect.FirstStrike:
                    // カスタムゲージを満タンにして先制攻撃権を付与
                    currentBattle.SetFirstStrike();
                    logger.LogInfo("  - 先制攻撃！カスタムゲージ満タン（ダッシュ接敵）");
                    break;
            }
        }

        /// <summary>
        /// BattleSystemからのバトル終了イベントハンドラ
        /// </summary>
        private void OnBattleEndFromBattleSystem(BattleResult result)
        {
            // イベント購読解除
            if (currentBattle != null)
            {
                currentBattle.OnBattleEnd -= OnBattleEndFromBattleSystem;
            }

            // OnBattleCompletedを呼び出す（内部でOnBattleEndが発火される）
            OnBattleCompleted(result == BattleResult.Victory);
        }

        /// <summary>
        /// バトル終了後の処理（外部から呼ばれる）
        /// </summary>
        public void OnBattleCompleted(bool victory)
        {
            IsInBattle = false;

            // バトル終了イベント発火
            BattleResult result = victory ? BattleResult.Victory : BattleResult.Defeat;
            OnBattleEnd?.Invoke(result);

            if (victory)
            {
                logger.LogInfo("[Dungeon] バトル勝利");

                // 外部からの勝利通知も従来どおり接敵主敵の撃破として扱う。
                if (finalBoss != null && ReferenceEquals(currentContactInfo?.Enemy, finalBoss))
                    finalBossDefeated = true;

                // 接敵主敵だけでなく、この戦闘で撃破した巻込み敵も探索マップから除去する。
                if (currentBattle != null)
                {
                    foreach (var enemy in currentBattle.Enemies)
                    {
                        if (enemy.IsDead)
                        {
                            if (ReferenceEquals(enemy, finalBoss)) finalBossDefeated = true;
                            map.RemoveEnemy(enemy);
                        }
                    }
                }

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
            if (IsDungeonCleared) return;

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
        /// ※プレイヤー移動の再開は行わない。呼び出し元が遷移完了後に ResumePlayerMovement() を呼ぶこと。
        /// </summary>
        private void ReturnToDungeon()
        {
            if (IsDungeonCleared) return;

            // バトルシステムをクリーンアップ
            currentBattle = null;

            // 接敵リセット
            contactSystem.ResetContact();

            // HP回復なし（ダンジョンの特殊ルール）
            // プレイヤーHPはバトル終了時のまま維持
            logger.LogInfo($"[Dungeon] HP維持: {playerCharacter.CurrentHp}/{playerCharacter.MaxHp}");

            // ボス撃破と報酬完了でクリア。ボスなしのマップは従来の全敵撃破条件を維持する。
            if (finalBoss != null ? finalBossDefeated : !map.HasAnyEnemy())
            {
                IsDungeonCleared = true;
                logger.LogInfo("[Dungeon] クリア条件達成 - ダンジョンクリア！");
                OnDungeonCleared?.Invoke();
                return;
            }

            // ダンジョン復帰イベント（移動再開は ResumePlayerMovement() で別途行う）
            OnReturnToDungeon?.Invoke();

            logger.LogInfo("[Dungeon] ダンジョン復帰準備完了（移動再開待ち）");
        }

        /// <summary>
        /// プレイヤー移動を再開する
        /// バトル→ダンジョン遷移アニメーション完了後に呼び出すこと
        /// </summary>
        public void ResumePlayerMovement()
        {
            if (IsInBattle || IsDungeonCleared) return;
            playerMovement.StopDash();  // バトル前のダッシュ状態を引き継がないようリセット
            playerMovement.Resume();
            logger.LogInfo("[Dungeon] ダンジョン移動再開");
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

            if (penaltyInfo.PenaltyType == ObstaclePenaltyType.CardDiscard)
            {
                // 攻略中デッキのみ変更し、クリア報酬用の獲得履歴は残す。
                int discardedCount = cardRewardSystem.GetCurrentDeck()
                    .DiscardRandomCards(penaltyInfo.CardDiscardCount, randomProvider);
                logger.LogInfo($"[Dungeon] 障害物によるカード破棄: {discardedCount}枚");
            }

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
        public event Action<BattleResult> OnBattleEnd;        // バトル終了（勝敗通知）
        public event Action<List<Card>> OnCardRewardOffered;  // カード選択画面表示
        public event Action OnReturnToDungeon;                // ダンジョン復帰
        public event Action<PenaltyInfo> OnObstaclePenalty;   // 障害物ペナルティ
        public event Action OnDungeonFailed;
        public event Action OnDungeonCleared;
    }
}
