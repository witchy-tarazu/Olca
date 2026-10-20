using Olca.Core.Logic.CardEffects;
using System;
using System.Linq;
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
        public bool IsFloorCompleted { get; private set; }
        private readonly bool hasNextFloor;

        // ========== 接敵情報保持 ==========
        private ContactInfo currentContactInfo;
        private List<Card> pendingRewardChoices;

        // ========== 障害物通過済み管理 ==========
        private readonly HashSet<int> processedObstacleCells = new HashSet<int>();

        // ========== プロパティ ==========
        public float PlayerPosition => playerMovement.CurrentPosition;
        public float PlayerHeight => playerMovement.Height;
        public bool IsJumping => playerMovement.IsJumping;
        public bool IsDashJumping => playerMovement.IsDashJumping;
        public float JumpProgress => playerMovement.JumpProgress;
        public bool IsDashing => playerMovement.IsDashing;
        /// <summary>描画間の座標差によらない自動前進の有効状態。移動量や更新頻度は変更しない。</summary>
        public bool IsExplorationMoving => !IsInBattle && !IsDungeonCleared && !IsFloorCompleted
            && !IsCheckpointBlocked && !checkpointSuspended && !playerMovement.IsPaused && !playerCharacter.IsDead;
        public PlayerCharacter Player => playerCharacter;
        /// <summary>ダンジョンマップ（View側の敵スプライト初期配置に使用）</summary>
        public DungeonMap Map => map;
        internal int[] CheckpointProcessedObstacles => processedObstacleCells.OrderBy(i => i).ToArray();

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
        private readonly ICardDatabase cardDatabase;

        public DungeonSystem(
            DungeonConfig config,
            PlayerCharacter player,
            Deck initialDeck,
            IRandomProvider random,
            IGameLogger logger,
            ExceedSystem exceedSystem = null,
            TuningSystem tuningSystem = null,
            FusionSystem fusionSystem = null,
            ICardPool rewardPool = null,
            DungeonCheckpointSession checkpoints = null, RestoredCheckpoint restoration = null, ICardDatabase cardDatabase = null, bool hasNextFloor = false)
        {
            if (restoration?.Snapshot.Boundary == CheckpointBoundary.Terminal)
                throw new ArgumentException("終端チェックポイントからゲームモデルは再開できません");
            if (hasNextFloor && checkpoints == null)
                throw new ArgumentException("階層移動には次階層開始を保存するセッションが必要です");
            this.cardDatabase = cardDatabase;
            this.hasNextFloor = hasNextFloor;
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
            cardRewardSystem = new CardRewardSystem(initialDeck, random, rewardPool);
            obstacleSystem = new ObstacleSystem(logger);
            playerCharacter = player;
            randomProvider = random;
            this.logger = logger;
            this.exceedSystem = exceedSystem;
            this.tuningSystem = tuningSystem;
            this.fusionSystem = fusionSystem;
            rewardPoolForCheckpoint = rewardPool;

            // イベント購読
            contactSystem.OnEnemyContact += OnEnemyContact;

            IsInBattle = false;
            IsDungeonCleared = false;

            logger.LogInfo($"[DungeonSystem] ダンジョン初期化: {config.DungeonName}");
            checkpointSession = checkpoints;
            if (checkpoints != null)
            {
                if (restoration != null)
                {
                    checkpoints.Adopt(restoration); restoredBoundary = restoration.Snapshot;
                    playerMovement.SetPositionMilli(restoredBoundary.PositionMilli);
                    processedObstacleCells.UnionWith(restoredBoundary.ProcessedObstacleCells ?? new int[0]);
                    cardRewardSystem.RestoreObtained(restoration.Obtained);
                }
                else
                {
                    try { StageCheckpoint(checkpoints.Capture(this, CheckpointBoundary.Departure), () => playerMovement.Resume()); }
                    catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
                }
            }
        }

        /// <summary>
        /// 毎フレーム更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        private bool processingTick;
        private int encounterRevision;

        public void Tick(int deltaTimeMs) => TickStep(deltaTimeMs, false);
        public void TickFrame() => TickStep(0, true);
        private void TickStep(int deltaTimeMs, bool logicalFrame)
        {
            if (processingTick || IsInBattle || IsFloorCompleted || IsDungeonCleared || IsCheckpointBlocked || checkpointSuspended ||
                playerCharacter.CurrentHp <= 0 || playerMovement.IsPaused) return;
            processingTick = true;
            try
            {
                var step = logicalFrame ? playerMovement.PreviewFrame() : playerMovement.PreviewStep(deltaTimeMs);
                int control = playerMovement.ControlRevision, encounter = encounterRevision;
                var contacts = new List<ContactSystem.SweptContact>();
                for (int i = 0; i < map.TotalCellCount; i++)
                {
                    var cell = map.GetCell(i);
                    if (cell == null) continue;
                    if (cell.Obstacle != null && !processedObstacleCells.Contains(i) &&
                        step.TryCellRange(i, 0, out var first, out var last))
                        contacts.Add(new ContactSystem.SweptContact(cell, first, step));
                    var enemy = contactSystem.FindContact(step, cell);
                    if (enemy.HasValue) contacts.Add(enemy.Value);
                }
                // 同じ位置で接触するときは、従来どおり障害物の効果を先に確定する。
                contacts.Sort((a, b) => {
                    int order = a.Fraction.CompareTo(b.Fraction);
                    return order != 0 ? order : (a.Cell.Obstacle == null ? 1 : 0).CompareTo(b.Cell.Obstacle == null ? 1 : 0);
                });
                foreach (var contact in contacts)
                {
                    playerMovement.ApplyStep(step, contact.Position);
                    if (contact.Cell.Obstacle != null) CheckObstacles(contact.Cell);
                    else if (contactSystem.NotifyContact(contact.Cell, playerMovement)) return;
                    // 通知先で開始・終了・再開が同期実行されても、残りの移動を持ち越さない。
                    if (playerCharacter.CurrentHp <= 0 || IsInBattle || IsFloorCompleted || IsDungeonCleared || IsCheckpointBlocked ||
                        checkpointSuspended || playerMovement.IsPaused || control != playerMovement.ControlRevision ||
                        encounter != encounterRevision) return;
                }
                if (hasNextFloor && finalBoss == null && step.End >= (long)map.TotalCellCount * 1000)
                {
                    // 旧保存形式の位置範囲を維持し、終端の直前を完了境界として保存する。
                    playerMovement.ApplyStep(step, (long)map.TotalCellCount * 1000 - 1);
                    playerMovement.Pause();
                    try { StageCheckpoint(checkpointSession.Capture(this, CheckpointBoundary.Terminal), CompleteFloor); }
                    catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
                    return;
                }
                playerMovement.ApplyStep(step, step.End);
            }
            finally { processingTick = false; }
        }

        // ========== プレイヤーアクション ==========

        /// <summary>
        /// ジャンプ
        /// </summary>
        public void Jump()
        {
            if (IsInBattle || IsFloorCompleted || IsCheckpointBlocked || checkpointSuspended) return;
            playerMovement.Jump();
            logger.LogInfo("[Dungeon] ジャンプ！");
        }

        /// <summary>
        /// ダッシュ開始
        /// </summary>
        public void StartDash()
        {
            if (IsInBattle || IsFloorCompleted || IsCheckpointBlocked || checkpointSuspended) return;
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
            if (IsInBattle || IsFloorCompleted || IsDungeonCleared || IsCheckpointBlocked || checkpointSuspended || playerCharacter.CurrentHp <= 0) return;
            encounterRevision++;
            if (checkpointSession != null) { PlanCheckpointBattle(contactInfo); return; }
            IsInBattle = true;
            pendingRewardChoices = null;
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
                fusionSystem: fusionSystem, cardDatabase: cardDatabase
            );

            // バトル終了イベントを購読
            currentBattle.OnBattleEnd += OnBattleEndFromBattleSystem;

            logger.LogInfo($"[Dungeon→Battle] バトルシステム初期化完了");
            logger.LogInfo($"  - 参加敵数: {enemies.Count}");
            logger.LogInfo($"  - デッキ枚数: {cardRewardSystem.GetCurrentDeck().TotalCount}");

            // 終了購読とManager/Viewの開始処理を、即死し得る初撃より先に完了する。
            var startedBattle = currentBattle;
            OnBattleStart?.Invoke(contactInfo);

            // 開始通知中に敗北・終了した場合は初撃で結果を上書きしない。
            if (IsInBattle && ReferenceEquals(currentBattle, startedBattle) && !startedBattle.IsGameOver
                && contactInfo.IsDashContact && contactInfo.DashEffect.HasValue)
            {
                ApplyDashEffect(contactInfo.DashEffect.Value, enemies);
            }
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
                // 全体仕様第7節: ボスは主敵として直接接敵した場合だけ参加する。
                // MapCellのBoss判定と同じIDを用い、周囲の通常敵は従来どおり巻き込む。
                if (!enemy.IsDead && enemy.EnemyId != "boss" && enemy != contactInfo.Enemy && !enemies.Contains(enemy))
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
            if (checkpointSuspended) return;
            if (checkpointSession != null) { CompleteCheckpointBattle(victory); return; }
            // 通知先が同期再入しても、同じ戦闘の抽選・終了を繰り返さない。
            if (!IsInBattle) return;
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

                        // 通知先での即時選択を許可し、渡したリストの変更で候補を増やさせない。
                        pendingRewardChoices = new List<Card>(choices);
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
            if (checkpointSuspended || IsCheckpointBlocked) return;
            if (checkpointSession != null)
            {
                if (pendingRewardChoices == null || (selectedCard != null && !pendingRewardChoices.Contains(selectedCard))) return;
                SaveCheckpointReward(selectedCard); return;
            }
            if (IsFloorCompleted || IsDungeonCleared || pendingRewardChoices == null) return;
            if (selectedCard != null && !pendingRewardChoices.Contains(selectedCard)) return;

            // 獲得イベントからの再入より先に確定する。nullも一度だけのスキップ。
            pendingRewardChoices = null;
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
        /// 次階層開始の保存成功後に一度だけ階層完了を通知する。
        /// </summary>
        private void CompleteFloor()
        {
            if (IsFloorCompleted) return;
            IsFloorCompleted = true;
            playerMovement.Pause();
            OnFloorCompleted?.Invoke();
        }

        private void ReturnToDungeon()
        {
            if (IsFloorCompleted || IsDungeonCleared) return;

            // バトルシステムをクリーンアップ
            currentBattle = null;

            // 接敵リセット
            contactSystem.ResetContact();

            // HP回復なし（ダンジョンの特殊ルール）
            // プレイヤーHPはバトル終了時のまま維持
            logger.LogInfo($"[Dungeon] HP維持: {playerCharacter.CurrentHp}/{playerCharacter.MaxHp}");

            // 中間ボスは報酬完了で階層終了。旧単階層のボスなし保存には従来条件を維持する。
            if (finalBoss != null ? finalBossDefeated : !hasNextFloor && !map.HasAnyEnemy())
            {
                if (hasNextFloor) { CompleteFloor(); return; }
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
            if (checkpointSuspended || IsCheckpointBlocked) return;
            if (IsInBattle || IsFloorCompleted || IsDungeonCleared) return;
            playerMovement.ResetAirborneMovement();  // 復帰演出の着地後に、接敵前の空中状態を持ち越さない
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
        private void CheckObstacles(MapCell currentCell)
        {
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
                // 即時HPペナルティの強靭度削りは仮値0。探索停止は障害物自身の処理が担う。
                playerCharacter.TakeDamage(penaltyInfo.Damage, affectsPoise: false);
                logger.LogInfo($"[Dungeon] 障害物ダメージ: {penaltyInfo.Damage}");

            }

            // 外部通知から再入されても、確定済みのHP・デッキが観測されるようにする。
            OnObstaclePenalty?.Invoke(penaltyInfo);
            if (penaltyInfo.PenaltyType == ObstaclePenaltyType.ImmediateDamage)
            {
                // HP0以下でゲームオーバー
                if (playerCharacter.CurrentHp <= 0)
                {
                    logger.LogInfo("[Dungeon] 障害物でゲームオーバー");
                    if (checkpointSession == null) OnDungeonFailed?.Invoke();
                    else
                    {
                        try { playerMovement.Pause(); StageCheckpoint(checkpointSession.Capture(this, CheckpointBoundary.Terminal), () => OnDungeonFailed?.Invoke()); }
                        catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
                    }
                }
            }
        }

        private readonly DungeonCheckpointSession checkpointSession;
        private CheckpointDto pendingCheckpoint, restoredBoundary, activeCheckpoint;
        private Action pendingCheckpointCommit;
        private bool checkpointSuspended;
        private List<Card> plannedRewards;
        public bool IsCheckpointBlocked { get; private set; }
        public string CheckpointError { get; private set; }
        internal long CheckpointPositionMilli => playerMovement.CurrentPositionMilli;
        private void BlockCheckpoint(string error) { IsCheckpointBlocked = true; checkpointSession.HasUncommittedBoundary = true; CheckpointError = error; playerMovement.Pause(); }
        private void StageCheckpoint(CheckpointDto dto, Action commit)
        {
            pendingCheckpoint = dto.Copy(); pendingCheckpointCommit = commit;
            checkpointSession.HasUncommittedBoundary = true;
            IsCheckpointBlocked = true;
            RetryCheckpoint();
        }
        public bool RetryCheckpoint()
        {
            if (checkpointSuspended || pendingCheckpoint == null) return false;
            if (!checkpointSession.Store.TryWrite(pendingCheckpoint, out var error)) { BlockCheckpoint(error); return false; }
            checkpointSession.Accept(pendingCheckpoint);
            var commit = pendingCheckpointCommit;
            pendingCheckpoint = null; pendingCheckpointCommit = null; IsCheckpointBlocked = false; CheckpointError = null;
            commit();
            return true;
        }
        private void PlanCheckpointBattle(ContactInfo contact)
        {
            try
            {
                if (contact == null || !ReferenceEquals(map.GetCell(contact.EnemyCellIndex)?.Enemy, contact.Enemy))
                    throw new ArgumentException("接敵個体が現行マップにありません");
                var participants = GatherEnemiesForBattle(contact);
                var cells = map.GetAllEnemies().ToDictionary(pair => pair.enemy, pair => pair.cellIndex);
                var dto = checkpointSession.Capture(this, CheckpointBoundary.RewardCompleted);
                dto.Sequence = checkpointSession.Last.Sequence;
                dto.Player.GridX = 1; dto.Player.GridY = 0;
                for (int i = 0; i < participants.Count; i++)
                {
                    var state = dto.Enemies.Single(e => e.CellIndex == cells[participants[i]]);
                    state.GridX = i == 0 ? 4 : 3; state.GridY = 0;
                }
                var encounter = new CheckpointBattleDto { ContactCell = contact.EnemyCellIndex,
                    ParticipantCells = participants.Select(e => cells[e]).ToArray(), DashContact = contact.IsDashContact,
                    DashEffect = contact.DashEffect.HasValue ? (int?)contact.DashEffect.Value : null,
                    GridOriginX = contact.EnemyCellIndex - 4, HandLimit = new CustomScreenSystem(new Deck()).MaxHandSize };
                int seed = randomProvider.Range(0, int.MaxValue);
                if (!CheckpointBattlePlan.TryCreate(dto, encounter, checkpointSession.Definitions, seed, rewardPoolForCheckpoint,
                    card => checkpointSession.Definitions.CaptureKnownCard("candidate", card).DefinitionKey, out var plan, out var error))
                    throw new ArgumentException(error);
                var snapshot = plan.Snapshot;
                playerMovement.Pause();
                StageCheckpoint(snapshot, () => StartSavedBattle(snapshot));
            }
            catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
        }
        private readonly ICardPool rewardPoolForCheckpoint;
        private void StartSavedBattle(CheckpointDto snapshot)
        {
            if (IsInBattle || checkpointSuspended) return;
            if (!CheckpointCodec.TryRestore(snapshot, checkpointSession.Definitions, out var restored, out var error))
            { BlockCheckpoint(error); return; }
            var plan = snapshot.Battle;
            checkpointSession.Adopt(restored);
            cardRewardSystem.GetCurrentDeck().RestoreCheckpointOrder(restored.Deck.GetAllCards(), restored.FirstHand);
            cardRewardSystem.RestoreObtained(restored.Obtained);
            var participants = plan.ParticipantCells.Select(i => map.GetCell(i).Enemy).ToList();
            foreach (var e in snapshot.Enemies.Where(e => plan.ParticipantCells.Contains(e.CellIndex)))
                map.GetCell(e.CellIndex).Enemy.MoveTo(new Vector2Int(e.GridX, e.GridY));
            playerCharacter.MoveTo(new Vector2Int(snapshot.Player.GridX, snapshot.Player.GridY));
            currentContactInfo = new ContactInfo { Enemy = map.GetCell(plan.ContactCell).Enemy, EnemyCellIndex = plan.ContactCell,
                ContactPositionX = plan.GridOriginX, IsDashContact = plan.DashContact,
                DashEffect = plan.DashEffect.HasValue ? (ActionSystem.DashContactEffect?)plan.DashEffect.Value : null };
            plannedRewards = restored.RewardCandidates.ToList(); activeCheckpoint = snapshot.Copy(); pendingRewardChoices = null;
            IsInBattle = true; playerMovement.Pause();
            currentBattle = new BattleSystem(playerCharacter, participants, cardRewardSystem.GetCurrentDeck(), 2f,
                randomProvider, logger, exceedSystem, tuningSystem, fusionSystem, cardDatabase: cardDatabase);
            currentBattle.CustomScreen.RestoreCheckpointHand(restored.FirstHand);
            currentBattle.OnBattleEnd += OnBattleEndFromBattleSystem;
            var started = currentBattle;
            OnBattleStart?.Invoke(currentContactInfo);
            if (IsInBattle && ReferenceEquals(currentBattle, started) && !started.IsGameOver && plan.DashContact && currentContactInfo.DashEffect.HasValue)
                ApplyDashEffect(currentContactInfo.DashEffect.Value, participants);
        }
        public void ActivateCheckpoint()
        {
            if (restoredBoundary == null || checkpointSuspended) return;
            var snapshot = restoredBoundary; restoredBoundary = null;
            if (snapshot.Boundary == CheckpointBoundary.BattleStart) StartSavedBattle(snapshot);
            else playerMovement.Resume();
        }
        public void SuspendForCheckpoint()
        {
            checkpointSuspended = true; playerMovement.Pause();
            currentBattle?.AbandonForCheckpoint();
            contactSystem.OnEnemyContact -= OnEnemyContact;
        }
        private void CompleteCheckpointBattle(bool victory)
        {
            if (!IsInBattle) return;
            IsInBattle = false;
            if (!victory)
            {
                try { StageCheckpoint(checkpointSession.Capture(this, CheckpointBoundary.Terminal), () => {
                    OnBattleEnd?.Invoke(BattleResult.Defeat); OnDungeonFailed?.Invoke(); }); }
                catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
                return;
            }
            finalBossDefeated = finalBoss != null && finalBoss.IsDead;
            OnBattleEnd?.Invoke(BattleResult.Victory);
            if (activeCheckpoint.Battle.RewardOffered)
            {
                pendingRewardChoices = new List<Card>(plannedRewards);
                OnCardRewardOffered?.Invoke(new List<Card>(pendingRewardChoices));
            }
            else SaveCheckpointReward(null);
        }
        private void SaveCheckpointReward(Card selected)
        {
            if (IsCheckpointBlocked || checkpointSuspended) return;
            try
            {
                bool cleared = finalBoss != null ? finalBossDefeated : !hasNextFloor && !map.GetAllEnemies().Any(pair => !pair.enemy.IsDead);
                var boundary = cleared ? CheckpointBoundary.Terminal : CheckpointBoundary.RewardCompleted;
                var snapshot = checkpointSession.Capture(this, boundary, selected);
                StageCheckpoint(snapshot, () => {
                    pendingRewardChoices = null;
                    if (selected != null) cardRewardSystem.SelectCard(selected);
                    foreach (var pair in map.GetAllEnemies().Where(pair => pair.enemy.IsDead).ToArray()) map.RemoveEnemy(pair.enemy);
                    ReturnToDungeon();
                });
            }
            catch (ArgumentException ex) { BlockCheckpoint(ex.Message); }
        }

        // ========== イベント ==========
        public event Action<ContactInfo> OnBattleStart;
        public event Action<BattleResult> OnBattleEnd;        // バトル終了（勝敗通知）
        public event Action<List<Card>> OnCardRewardOffered;  // カード選択画面表示
        public event Action OnReturnToDungeon;                // ダンジョン復帰
        public event Action<PenaltyInfo> OnObstaclePenalty;   // 障害物ペナルティ
        public event Action OnDungeonFailed;
        public event Action OnDungeonCleared;
        public event Action OnFloorCompleted;
    }
}

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>内容を固定した復元定義。製品値を補完せず、呼出側が既存定義を登録する。</summary>
    public sealed class CheckpointDefinitions
    {
        public string Revision { get; }
        public string DungeonId { get; }
        public string MapHash { get; }
        public int CellCount { get; }
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, TuningGrantDefinition> grants = new Dictionary<string, TuningGrantDefinition>();
        private readonly Dictionary<int, Enemy> enemies = new Dictionary<int, Enemy>();
        private readonly Dictionary<int, float> heights = new Dictionary<int, float>();
        private readonly Dictionary<int, Obstacle> obstacles = new Dictionary<int, Obstacle>();
        // 既存の障害物なし/即時ダメージ保存は版1/2を維持。破棄型を含む定義だけ版3。
        public int SchemaVersion => obstacles.Values.Any(o => o.PenaltyType == ObstaclePenaltyType.CardDiscard) ? 3 : obstacles.Count == 0 ? 1 : 2;
        internal void ValidateProcessedObstacles(CheckpointDto dto)
        {
            if (SchemaVersion == 1)
            {
                CheckpointValues.Require(dto.ProcessedObstacleCells == null || dto.ProcessedObstacleCells.Length == 0, "版1に未対応の障害物状態があります");
                return;
            }
            var processed = dto.ProcessedObstacleCells;
            CheckpointValues.Require(processed != null && processed.Distinct().Count() == processed.Length &&
                processed.All(i => obstacles.ContainsKey(i) && i <= dto.PositionMilli / 1000) &&
                (dto.Boundary != CheckpointBoundary.Departure || processed.Length == 0), "障害物の通過状態が不整合です");
        }
        private static bool SupportedObstacle(MapCell cell) => cell.Type == CellType.Obstacle && cell.Enemy == null &&
            cell.Height == 0 && cell.Obstacle != null &&
            (cell.Obstacle.PenaltyType == ObstaclePenaltyType.ImmediateDamage || cell.Obstacle.PenaltyType == ObstaclePenaltyType.CardDiscard) &&
            cell.Obstacle.PenaltyValue >= 0 && !string.IsNullOrWhiteSpace(cell.Obstacle.ObstacleId) && cell.Obstacle.StatusEffectId == null;
        private static Obstacle CopyObstacle(Obstacle value) => value.PenaltyType == ObstaclePenaltyType.CardDiscard
            ? Obstacle.CreateCardDiscardObstacle(value.ObstacleId, value.Name, value.PenaltyValue)
            : new Obstacle(value.ObstacleId, value.Name, value.PenaltyValue);
        private readonly string dungeonName;
        private readonly int initialDeckSize;
        internal IReadOnlyDictionary<int, Enemy> Enemies => enemies;
        internal string CardCatalogHash => CheckpointValues.Hash(w => {
            w.Write(cards.Count);
            foreach (var entry in cards.OrderBy(c => c.Key, StringComparer.Ordinal)) {
                w.Write(entry.Key); w.Write(CheckpointValues.CardHash(entry.Value));
            }
        });

        public CheckpointDefinitions(string revision, DungeonConfig map)
        {
            if (string.IsNullOrWhiteSpace(revision) || map == null || string.IsNullOrWhiteSpace(map.DungeonId) ||
                map.TotalCellCount <= 0 || map.MapCells.Count != map.TotalCellCount)
                throw new ArgumentException("固定マップ定義が不完全です");
            Revision = revision; DungeonId = map.DungeonId; CellCount = map.TotalCellCount;
            dungeonName = map.DungeonName; initialDeckSize = map.InitialDeckSize;
            MapHash = CheckpointValues.Hash(w => {
                w.Write(DungeonId); w.Write(CellCount); w.Write(map.InitialDeckSize);
                for (int i = 0; i < CellCount; i++)
                {
                    var cell = map.MapCells[i];
                    if (cell == null || cell.Index != i) throw new ArgumentException("固定セルが不整合です");
                    if (cell.Type == CellType.Obstacle)
                    {
                        if (!SupportedObstacle(cell)) throw new ArgumentException("保存できる障害物は地上の即時ダメージ型またはカード破棄型です");
                        var obstacle = CopyObstacle(cell.Obstacle); obstacles.Add(i, obstacle);
                        w.Write((int)cell.Type); w.Write(cell.Height);
                        CheckpointValues.WriteObstacle(w, obstacle);
                        continue;
                    }
                    if (cell.Obstacle != null || (cell.Type != CellType.Empty && cell.Type != CellType.Enemy && cell.Type != CellType.Boss))
                        throw new ArgumentException("未対応の固定セルです");
                    CheckpointValues.Require(CheckpointValues.Finite(cell.Height) && cell.Height >= 0 &&
                        ((cell.Type == CellType.Empty) == (cell.Enemy == null)), "セル内容が不整合です");
                    w.Write((int)cell.Type); w.Write(cell.Height);
                    if (cell.Enemy == null) continue;
                    var e = cell.Enemy;
                    if (string.IsNullOrWhiteSpace(e.EnemyId) || e.MaxHp <= 0 || e.IsSummoned || e.Guard.IsActive || e.HasBarrier || e.HasAura || e.IsInvincible || (e.PoiseRecoveryDelayFrames != e.PoiseDefinition?.RecoveryDelayFrames || e.PoiseRecoveryRemainingFrames > 0) || e.CurrentHp != e.MaxHp || e.ActiveStatus != null || e.IsStunned || e.TerrainState.IsFloating || e.AccumulatedPoiseDamage != 0 || e.Poise != e.PoiseDefinition?.Maximum)
                        throw new ArgumentException("敵の標準定義が必要です");
                    enemies.Add(i, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element, e.GridPosition, resistances: e.Resistances, statusResistances: e.StatusResistances, poiseDefinition: e.PoiseDefinition));
                    heights.Add(i, cell.Height);
                    w.Write(e.EnemyId); w.Write(e.Name); w.Write(e.MaxHp); w.Write((int)e.Element);
                    // 中立の既存定義hashは維持し、設定済み耐性だけを定義照合に含める。
                    if (!e.Resistances.IsNeutral) { w.Write("resistances"); w.Write(e.Resistances.Fire); w.Write(e.Resistances.Water); w.Write(e.Resistances.Wind); w.Write(e.Resistances.Earth); }
                    // 未設定の旧マップhashは維持。設定値だけを保存復帰時の定義照合へ含める。
                    if (!e.StatusResistances.IsNeutral) { w.Write("timed-status-resistances"); w.Write(e.StatusResistances.Poison); w.Write(e.StatusResistances.Paralyze); }
                    if (e.PoiseDefinition != null) { w.Write("enemy-poise"); w.Write(e.PoiseDefinition.Maximum); w.Write(e.PoiseDefinition.StaggerFrames);
                        if (e.PoiseDefinition.RecoveryDelayFrames.HasValue) { w.Write("poise-recovery"); w.Write(e.PoiseDefinition.RecoveryDelayFrames.Value); } }
                    w.Write(e.GridPosition.X); w.Write(e.GridPosition.Y);
                }
            });
        }

        internal void ValidateCurrentMap(DungeonMap map)
        {
            if (map.TotalCellCount != CellCount) throw new ArgumentException("マップサイズが定義と一致しません");
            for (int i = 0; i < CellCount; i++)
            {
                var cell = map.GetCell(i);
                if (cell == null || cell.Index != i) throw new ArgumentException("固定セルが不整合です");
                if (obstacles.TryGetValue(i, out var obstacle))
                {
                    if (!SupportedObstacle(cell) || CheckpointValues.ObstacleHash(cell.Obstacle) != CheckpointValues.ObstacleHash(obstacle))
                        throw new ArgumentException("障害物の配置または定義が一致しません");
                    continue;
                }
                if (cell.Obstacle != null || (cell.Type != CellType.Empty && cell.Type != CellType.Enemy && cell.Type != CellType.Boss))
                    throw new ArgumentException("未対応セルを保存できません");
                if (cell.Enemy == null) continue;
                if (!enemies.TryGetValue(i, out var template) || cell.Enemy.EnemyId != template.EnemyId ||
                    cell.Enemy.Name != template.Name || cell.Enemy.MaxHp != template.MaxHp || cell.Enemy.Element != template.Element || !cell.Enemy.Resistances.HasSameValues(template.Resistances) || !cell.Enemy.StatusResistances.HasSameValues(template.StatusResistances) ||
                    !(cell.Enemy.PoiseDefinition == null ? template.PoiseDefinition == null : cell.Enemy.PoiseDefinition.HasSameValues(template.PoiseDefinition)) || cell.Height != heights[i])
                    throw new ArgumentException("敵個体・配置の定義が一致しません");
            }
        }

        // 攻略中に敵やセルが変化していても、固定した定義から新規攻略を生成する。
        public DungeonConfig CreateInitialMap() => RestoreMap(enemies.ToDictionary(p => p.Key, p => new Enemy(
            p.Value.EnemyId, p.Value.Name, p.Value.MaxHp, p.Value.Element, p.Value.GridPosition,
            resistances: p.Value.Resistances, statusResistances: p.Value.StatusResistances, poiseDefinition: p.Value.PoiseDefinition)));

        internal DungeonConfig RestoreMap(IReadOnlyDictionary<int, Enemy> restoredEnemies)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < CellCount; i++)
                cells.Add(obstacles.TryGetValue(i, out var obstacle) ? new MapCell(i, CopyObstacle(obstacle)) :
                    restoredEnemies.TryGetValue(i, out var enemy) && !enemy.IsDead
                    ? new MapCell(i, enemy, heights[i]) : new MapCell(i));
            return new DungeonConfig(DungeonId, dungeonName, CellCount, cells, initialDeckSize);
        }

        public void RegisterCard(string key, Card definition)
        {
            if (string.IsNullOrWhiteSpace(key) || definition == null || definition.TuningCount != 0 || definition.IsEffectCopy)
                throw new ArgumentException("未加工のカード定義と定義元・版付きキーが必要です");
            CheckpointValues.CardHash(definition); // 不完全な値は登録時にも拒否。
            cards.Add(key, definition.Clone());
        }
        /// <summary>既存の未加工定義だけを所持識別へ解決する。未定義/同じ識別の競合を補完しない。</summary>
        public bool TryResolveOwnedCard(string id, ExceedLevel level, CardRarity rarity, string name, out Card card)
        {
            card = null;
            if (level == ExceedLevel.Lv3) return false;
            var matches = cards.Values.Where(c => c.Id == id && c.ExceedLevel == level && c.Rarity == rarity && c.Name == name)
                .GroupBy(c => CheckpointValues.CardHash(c)).Select(g => g.First()).ToArray();
            if (matches.Length != 1) return false;
            card = matches[0].Clone(); return true;
        }
        public void RegisterGrant(string key, TuningGrantDefinition grant)
        {
            if (string.IsNullOrWhiteSpace(key) || grant == null) throw new ArgumentException("調律定義が必要です");
            grants.Add(key, grant);
        }
        /// <summary>457e1e6の未調律2定義だけを、検証用DTOコピー上で03e6a1bの定義へ解決する。</summary>
        internal void NormalizeLegacyDefenseCard(CheckpointCardDto value)
        {
            if (value == null || value.TuningCount != 0 || value.GrantKeys == null || value.GrantKeys.Length != 0)
                return;
            // 当時の全値を固定。IDだけの一致や、小カテゴリを除いた任意定義の一致では許可しない。
            foreach (bool shield in new[] { false, true })
            {
                string id = shield ? "shield" : "heal", name = shield ? "シールド" : "ヒール";
                int cost = shield ? 1 : 2, power = shield ? 15 : 20;
                string description = shield ? "防御" : "HP回復";
                var legacy = new Card(id, name, cost, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, power, description);
                string oldHash = CheckpointValues.CardHash(legacy);
                if (value.DefinitionKey != "initial/v1/" + oldHash || value.ValueHash != oldHash) continue;
                var current = new Card(id, name, cost, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, power, description,
                    subCategory: shield ? CardSubCategory.Barrier : CardSubCategory.Heal);
                string newHash = CheckpointValues.CardHash(current), newKey = "initial/v1/" + newHash;
                // 現在の登録定義も既知の新定義と完全一致する場合だけ受け入れる。
                if (!cards.TryGetValue(newKey, out var registered) || CheckpointValues.CardHash(registered) != newHash)
                    continue;
                value.DefinitionKey = newKey; value.ValueHash = newHash;
                return;
            }
        }

        internal Card RestoreCard(CheckpointCardDto value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.InstanceId) || value.DefinitionKey == null ||
                !cards.TryGetValue(value.DefinitionKey, out var definition) || value.GrantKeys == null ||
                value.TuningCount != value.GrantKeys.Length) throw new ArgumentException("カード定義または調律履歴が不整合です");
            var result = definition.Clone();
            foreach (var key in value.GrantKeys)
            {
                if (key == null || !grants.TryGetValue(key, out var grant)) throw new ArgumentException("旧・未定義の調律効果です");
                var evaluation = TuningEvaluator.Evaluate(result, grant);
                if (!evaluation.IsSuccess) throw new ArgumentException(evaluation.ErrorMessage);
                result = evaluation.ResultCard;
            }
            if (CheckpointValues.CardHash(result) != value.ValueHash) throw new ArgumentException("カード値と定義・履歴が一致しません");
            return result;
        }
        public CheckpointCardDto CaptureKnownCard(string instanceId, Card card)
        {
            var historyKeys = card.TuningHistory.Select(grant => grants.FirstOrDefault(pair =>
                CheckpointValues.GrantHash(pair.Value) == CheckpointValues.GrantHash(grant)).Key).ToArray();
            foreach (var key in cards.Keys)
            {
                try { return CaptureCard(instanceId, key, card, historyKeys); }
                catch (ArgumentException) { }
            }
            throw new ArgumentException("未登録のカード定義または調律履歴です: " + card.Id);
        }
        public CheckpointCardDto CaptureCard(string instanceId, string definitionKey, Card card, params string[] grantKeys)
        {
            if (card == null || card.IsEffectCopy || card.TuningCount != card.TuningHistory.Count)
                throw new ArgumentException("使用時コピーまたは不完全な旧履歴は保存できません");
            var dto = new CheckpointCardDto { InstanceId = instanceId, DefinitionKey = definitionKey,
                ValueHash = CheckpointValues.CardHash(card), TuningCount = card.TuningCount,
                GrantKeys = grantKeys == null ? null : (string[])grantKeys.Clone() };
            RestoreCard(dto);
            return dto;
        }
    }

    // Hashは改竄対策ではなく、同じID/版を誤って異なる定義へ解決することの検出用。
    internal static class CheckpointValues
    {
        internal static string Hash(Action<System.IO.BinaryWriter> write)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
        private static void Text(System.IO.BinaryWriter w, string value) { w.Write(value != null); if (value != null) w.Write(value); }
        private static void Modifier(System.IO.BinaryWriter w, CardModifier m)
        {
            if (m == null || m.AdditionalEffects == null) throw new ArgumentException("効果定義が不完全です");
            w.Write(m.ZeroCost); w.Write(m.CostAdjustment); w.Write(m.BonusDamage); w.Write(m.DamageMultiplier);
            w.Write(m.ElementOverride.HasValue); if (m.ElementOverride.HasValue) w.Write((int)m.ElementOverride.Value);
            w.Write(m.AdditionalEffects.Count);
            foreach (var e in m.AdditionalEffects)
            {
                if (e == null) throw new ArgumentException("追加効果がnullです");
                w.Write((int)e.EffectType); w.Write(e.Power); w.Write(e.Duration); w.Write(e.HitCount); w.Write((int)e.Trigger);
                Text(w, e.ChainCardId); w.Write((int)e.EffectOrigin); w.Write(e.DelayTime);
                w.Write(e.TargetOffset.X); w.Write(e.TargetOffset.Y); w.Write(e.CancelOnHole);
                e.ValidateHitInvincibilityStages();
                var stages = e.HitInvincibilityStages;
                if (stages != null) { w.Write("hit-invincibility-stages"); w.Write(stages.Length); foreach (bool enabled in stages) w.Write(enabled); }
            }
        }
        private static void Grant(System.IO.BinaryWriter w, TuningGrantDefinition grant)
        {
            w.Write(grant != null); if (grant == null) return;
            Text(w, grant.Id); w.Write(grant.FixedCostAdjustment); w.Write(grant.ZeroCost); w.Write(grant.Effects.Count);
            foreach (var effect in grant.Effects)
            {
                if (effect == null) throw new ArgumentException("調律効果がnullです");
                Text(w, effect.Id); w.Write(effect.MaxStage);
                for (int stage = 1; stage <= 4; stage++)
                {
                    bool found = effect.TryGetStage(stage, out var value); w.Write(found); if (found) Modifier(w, value);
                }
            }
        }
        internal static void WriteObstacle(System.IO.BinaryWriter writer, Obstacle obstacle)
        { Text(writer, obstacle.ObstacleId); Text(writer, obstacle.Name); writer.Write((int)obstacle.PenaltyType); writer.Write(obstacle.PenaltyValue); Text(writer, obstacle.StatusEffectId); }
        internal static string ObstacleHash(Obstacle obstacle) => Hash(writer => WriteObstacle(writer, obstacle));
        internal static string GrantHash(TuningGrantDefinition g) => Hash(w => Grant(w, g));
        internal static string CardHash(Card c) => Hash(w => {
            Text(w, c.Id); Text(w, c.Name); Text(w, c.Description);
            w.Write(c.Cost); w.Write((int)c.Rarity); w.Write((int)c.Category); w.Write((int)c.Element); Text(w, c.Series?.Name);
            w.Write(c.Power); w.Write((int)c.TargetType); Text(w, c.PanelChangeType); Text(w, c.MoveDirection); w.Write(c.MoveDistance);
            w.Write(c.MoveMotionTime); w.Write(c.AttackMotionTime); w.Write((int)c.PenetrationMode);
            w.Write(c.StartupTime); w.Write(c.RecoveryTime); w.Write(c.ExtendRecoveryDuringDerivative); w.Write(c.IsInstantEffect);
            w.Write((int)c.ExceedLevel); Modifier(w, c.Modifier); Modifier(w, c.GetTuningBaseModifier()); Grant(w, c.TuningDefinition);
            w.Write(c.TuningCount); foreach (var grant in c.TuningHistory) Grant(w, grant);
            // 未指定の既存定義hashは維持し、明示された効果種別の不一致を検出する。
            if (c.SubCategory != CardSubCategory.None) { w.Write("subcategory"); w.Write((int)c.SubCategory); }
            // 性質未指定の旧定義は同じbytes/hash。明示した性質だけ追加照合する。
            if (c.HitInvincibilityMs > 0) { w.Write("hit-invincibility-ms"); w.Write(c.HitInvincibilityMs); }
            if (c.PoiseDamage.HasValue) { w.Write("poise-damage"); w.Write(c.PoiseDamage.Value); }
            if (c.Properties != AttackProperty.None) { w.Write("attack-properties"); w.Write((int)c.Properties); }
        });
        internal static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>復元候補。検証が全件成功するまで通常ゲームの参照へ接続しない。</summary>
    public sealed class RestoredCheckpoint
    {
        private readonly CheckpointDto snapshot;
        public CheckpointDto Snapshot => snapshot.Copy();
        public PlayerCharacter Player { get; }
        public DungeonConfig Map { get; }
        public IReadOnlyDictionary<string, Card> Cards { get; }
        public IReadOnlyDictionary<int, Enemy> Enemies { get; }
        public Deck Deck { get; }
        public IReadOnlyList<Card> Obtained { get; }
        public IReadOnlyList<Card> FirstHand { get; }
        public IReadOnlyList<Card> RewardCandidates { get; }
        internal RestoredCheckpoint(CheckpointDto dto, Dictionary<string, Card> cards, Dictionary<int, Enemy> enemies, CheckpointDefinitions definitions)
        {
            snapshot = dto.Copy(); Cards = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Card>(cards);
            Enemies = new System.Collections.ObjectModel.ReadOnlyDictionary<int, Enemy>(enemies);
            Map = definitions.RestoreMap(enemies);
            var p = dto.Player;
            Player = new PlayerCharacter(p.MaxHp, new Vector2Int(p.GridX, p.GridY), p.AttackPower, p.CustomSpeed, p.Poise, p.StaggerFrames, p.PoiseRecoveryFrames);
            Player.SetElementResistances(new ElementResistances(p.FireResistance, p.WaterResistance, p.WindResistance, p.EarthResistance));
            Player.TakeDamage(p.MaxHp - p.Hp, affectsPoise: false);
            Deck = new Deck(Array.ConvertAll(dto.DeckOrder, id => cards[id]).ToList());
            Obtained = Array.AsReadOnly(Array.ConvertAll(dto.ObtainedOrder, id => cards[id]));
            FirstHand = Array.AsReadOnly(Array.ConvertAll(dto.Battle?.FirstHand ?? new string[0], id => cards[id]));
            Deck.RestoreCheckpointDrawn(FirstHand);
            RewardCandidates = Array.AsReadOnly(Array.ConvertAll(dto.Battle?.RewardCandidates ?? new string[0], id => cards[id]));
        }
    }

    public static class CheckpointCodec
    {
        public static bool TryRestore(CheckpointDto input, CheckpointDefinitions definitions, out RestoredCheckpoint restored, out string error)
        {
            restored = null; error = null;
            try
            {
                CheckpointValues.Require(input != null && definitions != null, "保存データまたは定義がありません");
                var d = input.Copy();
                CheckpointValues.Require(d.SchemaVersion == definitions.SchemaVersion && d.DefinitionsRevision == definitions.Revision &&
                    d.DungeonId == definitions.DungeonId && d.MapHash == definitions.MapHash, "保存版または定義版が一致しません");
                CheckpointValues.Require(!string.IsNullOrWhiteSpace(d.RunId) && d.Sequence > 0 && Enum.IsDefined(typeof(CheckpointBoundary), d.Boundary), "保存境界が不正です");
                CheckpointValues.Require(d.PositionMilli >= 0 && d.PositionMilli < (long)definitions.CellCount * 1000, "探索位置が範囲外です");
                definitions.ValidateProcessedObstacles(d);
                var p = d.Player;
                CheckpointValues.Require(p != null && p.MaxHp > 0 && p.Hp >= (d.Boundary == CheckpointBoundary.Terminal ? 0 : 1) && p.Hp <= p.MaxHp && p.AttackPower >= 0 && p.Poise > 0 && p.StaggerFrames >= 0 && (!p.PoiseRecoveryFrames.HasValue || p.PoiseRecoveryFrames > 0) &&
                    CheckpointValues.Finite(p.CustomSpeed) && p.CustomSpeed > 0, "プレイヤー値が不正です");
                // 既存保存形式は上限100を検証する。旧負値は原値のまま復元し戦闘時に制限。
                CheckpointValues.Require(p.FireResistance <= 100 && p.WaterResistance <= 100 &&
                    p.WindResistance <= 100 && p.EarthResistance <= 100, "保存された属性耐性が上限を超えています");
                CheckpointValues.Require(d.Cards != null && d.DeckOrder != null && d.ObtainedOrder != null && d.Enemies != null, "必須配列が欠落しています");
                var cards = new Dictionary<string, Card>();
                foreach (var c in d.Cards) { definitions.NormalizeLegacyDefenseCard(c); var card = definitions.RestoreCard(c); cards.Add(c.InstanceId, card); }
                ValidateReferences(d.DeckOrder, cards); ValidateReferences(d.ObtainedOrder, cards);
                var enemies = new Dictionary<int, Enemy>();
                foreach (var e in d.Enemies)
                {
                    CheckpointValues.Require(e != null && definitions.Enemies.TryGetValue(e.CellIndex, out _), "敵配置が未定義です");
                    var template = definitions.Enemies[e.CellIndex];
                    CheckpointValues.Require(e.EnemyId == template.EnemyId && e.Hp >= 0 && e.Hp <= template.MaxHp, "敵の値が不正です");
                    var enemy = new Enemy(template.EnemyId, template.Name, template.MaxHp, template.Element, new Vector2Int(e.GridX, e.GridY), resistances: template.Resistances, statusResistances: template.StatusResistances, poiseDefinition: template.PoiseDefinition);
                    // ダメージ処理・属性倍率・浮動小数の丸めを経由せず、新規候補のHPを復元する。
                    enemy.RestoreCheckpointHp(e.Hp);
                    CheckpointValues.Require(enemy.CurrentHp == e.Hp, "敵HPを復元できません");
                    enemies.Add(e.CellIndex, enemy);
                }
                CheckpointValues.Require(enemies.Count == definitions.Enemies.Count, "敵個体が欠落しています");
                if (d.Boundary == CheckpointBoundary.BattleStart)
                {
                    var b = d.Battle;
                    ValidateEncounter(b, p, enemies);
                    ValidateReferences(b.FirstHand, cards); ValidateReferences(b.RewardCandidates, cards);
                    bool boss = enemies[b.ContactCell].EnemyId.Contains("boss");
                    CheckpointValues.Require(boss ? b.RewardOffered : (!b.RewardOffered || b.RewardCandidates.Length == 3), "報酬件数と接敵種別が一致しません");
                    CheckpointValues.Require(b.FirstHand.Length == Math.Min(b.HandLimit, d.DeckOrder.Length) && b.FirstHand.All(id => d.DeckOrder.Contains(id)), "初回手札が不整合です");
                    CheckpointValues.Require(b.RewardOffered == (b.RewardCandidates.Length > 0) && b.RewardCandidates.Length <= 3 &&
                        b.RewardCandidates.All(id => !d.DeckOrder.Contains(id) && !d.ObtainedOrder.Contains(id)), "報酬候補が不整合です");
                }
                else CheckpointValues.Require(d.Battle == null, "探索境界に戦闘計画が混入しています");
                var referenced = new HashSet<string>(d.DeckOrder.Concat(d.ObtainedOrder).Concat(d.Battle?.RewardCandidates ?? new string[0]));
                CheckpointValues.Require(referenced.Count == cards.Count, "用途のないカード個体が混入しています");
                restored = new RestoredCheckpoint(d, cards, enemies, definitions);
                return true;
            }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            catch (OverflowException ex) { error = ex.Message; return false; }
        }
        internal static void ValidateEncounter(CheckpointBattleDto b, CheckpointPlayerDto p, IReadOnlyDictionary<int, Enemy> enemies)
        {
            CheckpointValues.Require(b != null && b.ParticipantCells != null && b.ParticipantCells.Length > 0 &&
                b.ParticipantCells.Distinct().Count() == b.ParticipantCells.Length && b.ParticipantCells.Contains(b.ContactCell), "接敵情報が不完全です");
            CheckpointValues.Require(b.ParticipantCells.All(i => enemies.ContainsKey(i) && !enemies[i].IsDead && ValidGrid(enemies[i].GridPosition.X, enemies[i].GridPosition.Y)) && ValidGrid(p.GridX, p.GridY), "戦闘参加者が不正です");
            CheckpointValues.Require(CheckpointValues.Finite(b.GridOriginX) && b.HandLimit >= 0 &&
                (!b.DashEffect.HasValue || (b.DashContact && Enum.IsDefined(typeof(ActionSystem.DashContactEffect), b.DashEffect.Value))), "戦闘開始値が不正です");
        }
        private static bool ValidGrid(int x, int y) => x >= 0 && x < BattleField.GRID_WIDTH && y >= 0 && y < BattleField.GRID_HEIGHT;
        private static void ValidateReferences(string[] ids, Dictionary<string, Card> cards)
        {
            CheckpointValues.Require(ids != null && ids.All(id => id != null && cards.ContainsKey(id)) && ids.Distinct().Count() == ids.Length,
                "カード個体の参照が欠落または重複しています");
        }
    }

    public interface ICheckpointReadStatus
    {
        string ReadError { get; }
        bool HasExistingData { get; }
    }

    public interface ICheckpointStore
    {
        bool TryWrite(CheckpointDto data, out string error);
        CheckpointDto Read();
    }

    /// <summary>メモリ内の保存先。失敗時は既存世代と呼出元の値を変更しない。</summary>
    public sealed class MemoryCheckpointStore : ICheckpointStore
    {
        private readonly CheckpointDefinitions definitions;
        private CheckpointDto current;
        public MemoryCheckpointStore(CheckpointDefinitions definitions) { this.definitions = definitions; }
        public bool TryWrite(CheckpointDto data, out string error)
        {
            if (!CheckpointCodec.TryRestore(data, definitions, out var candidate, out error)) return false;
            var snapshot = candidate.Snapshot;
            if (current != null && (snapshot.RunId != current.RunId || snapshot.Sequence <= current.Sequence))
            { error = "攻略IDまたは保存世代が競合しています"; return false; }
            current = snapshot;
            return true;
        }
        public CheckpointDto Read() => current?.Copy();
    }

    /// <summary>既存抽選をコピー上で一度実行する計画。内部の専用乱数のみを消費し、保存再試行ではSnapshotを再利用する。</summary>
    public sealed class CheckpointBattlePlan
    {
        private readonly CheckpointDto snapshot;
        public CheckpointDto Snapshot => snapshot.Copy();
        public int RandomCalls { get; }
        private CheckpointBattlePlan(CheckpointDto snapshot, int randomCalls) { this.snapshot = snapshot.Copy(); RandomCalls = randomCalls; }
        // Seedは計画生成用。復帰はSeedから再抽選せず、保存済みの具体的結果を使う。
        private sealed class PlanningRandom : IRandomProvider
        {
            private readonly Random random;
            public int Calls { get; private set; }
            public PlanningRandom(int seed) { random = new Random(seed); }
            public int Range(int min, int max) { Calls++; return random.Next(min, max); }
            public float Range(float min, float max) { Calls++; return min + (float)random.NextDouble() * (max - min); }
            public float Value => Range(0f, 1f);
        }
        public static bool TryCreate(CheckpointDto state, CheckpointBattleDto encounter, CheckpointDefinitions definitions,
            int planningSeed, ICardPool rewardPool, Func<Card, string> rewardDefinitionKey,
            out CheckpointBattlePlan plan, out string error)
        {
            plan = null;
            if (!CheckpointCodec.TryRestore(state, definitions, out var restored, out error)) return false;
            try
            {
                CheckpointValues.Require(encounter != null && rewardDefinitionKey != null &&
                    state.Boundary != CheckpointBoundary.BattleStart && restored.Enemies.ContainsKey(encounter.ContactCell), "開始計画の入力が不正です");
                var d = restored.Snapshot;
                d.Sequence = checked(d.Sequence + 1); d.Boundary = CheckpointBoundary.BattleStart;
                d.Battle = encounter.Copy(); d.Battle.FirstHand = new string[0]; d.Battle.RewardCandidates = new string[0];
                d.Battle.RewardOffered = false;
                CheckpointCodec.ValidateEncounter(d.Battle, d.Player, restored.Enemies);
                var random = new PlanningRandom(planningSeed);
                var deck = restored.Deck;
                deck.Shuffle(random);
                var reverse = restored.Cards.ToDictionary(pair => pair.Value, pair => pair.Key);
                d.DeckOrder = deck.GetAllCards().Select(c => reverse[c]).ToArray();
                d.Battle.FirstHand = deck.DrawCards(d.Battle.HandLimit, random).Select(c => reverse[c]).ToArray();
                var rewards = new CardRewardSystem(new Deck(), random, rewardPool);
                var enemy = restored.Enemies[encounter.ContactCell];
                d.Battle.RewardOffered = rewards.ShouldDropCard(enemy);
                if (d.Battle.RewardOffered)
                {
                    var choices = rewards.GenerateCardChoices(enemy);
                    var records = new List<CheckpointCardDto>(d.Cards);
                    d.Battle.RewardCandidates = new string[choices.Count];
                    for (int i = 0; i < choices.Count; i++)
                    {
                        string id = d.RunId + ":reward:" + d.Sequence + ":" + i;
                        var record = definitions.CaptureCard(id, rewardDefinitionKey(choices[i]), choices[i]);
                        records.Add(record); d.Battle.RewardCandidates[i] = id;
                    }
                    d.Cards = records.ToArray();
                }
                if (!CheckpointCodec.TryRestore(d, definitions, out _, out error)) return false;
                plan = new CheckpointBattlePlan(d, random.Calls); return true;
            }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            catch (InvalidOperationException ex) { error = ex.Message; return false; }
            catch (OverflowException ex) { error = ex.Message; return false; }
        }
    }
}

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>一攻略の保存状態。通常フローの参照と保存DTOを混在させない。</summary>
    public sealed class DungeonCheckpointSession
    {
        public CheckpointDefinitions Definitions { get; }
        public ICheckpointStore Store { get; }
        public string RunId { get; private set; }
        public CheckpointDto Last { get; private set; }
        internal bool HasUncommittedBoundary { get; set; }
        private readonly Dictionary<Card, string> identities = new Dictionary<Card, string>();
        public DungeonCheckpointSession(CheckpointDefinitions definitions, ICheckpointStore store)
        { Definitions = definitions; Store = store; RunId = Guid.NewGuid().ToString("N"); }
        public static DungeonCheckpointSession CreateDefault(DungeonConfig map, Deck deck)
        {
            var definitions = new CheckpointDefinitions("fixed-runtime-v1", map);
            foreach (var group in deck.GetAllCards().GroupBy(c => CheckpointValues.CardHash(c)))
                definitions.RegisterCard("initial/v1/" + group.Key, group.First());
            foreach (var rarity in new[] { CardRarity.Normal, CardRarity.Limited })
                foreach (var id in new[] { "sword", "heal", "shield", "fire", "ice" })
                    definitions.RegisterCard("fallback/v1/" + rarity + "/" + id, CardRewardSystem.CreateFallbackCard(id, rarity));
            return new DungeonCheckpointSession(definitions, new MemoryCheckpointStore(definitions));
        }
        internal string Identify(Card card)
        {
            if (!identities.TryGetValue(card, out var id)) { id = RunId + ":card:" + Guid.NewGuid().ToString("N"); identities.Add(card, id); }
            return id;
        }
        internal void Accept(CheckpointDto dto) { Last = dto.Copy(); RunId = dto.RunId; HasUncommittedBoundary = false; }
        internal void Adopt(RestoredCheckpoint restored)
        {
            identities.Clear(); foreach (var pair in restored.Cards) identities.Add(pair.Value, pair.Key);
            Accept(restored.Snapshot);
        }
        public bool TryRead(out RestoredCheckpoint restored, out string error)
        {
            restored = null;
            if (HasUncommittedBoundary) { error = "未保存の境界があります。先に同じ保存を再試行してください"; return false; }
            var dto = Store.Read();
            if (dto == null && Store is ICheckpointReadStatus status && status.ReadError != null)
            { error = status.ReadError; return false; }
            if (dto?.Boundary == CheckpointBoundary.Terminal) { error = "終了済みの攻略は再開できません"; return false; }
            return CheckpointCodec.TryRestore(dto, Definitions, out restored, out error);
        }
        internal CheckpointDto Capture(DungeonSystem dungeon, CheckpointBoundary boundary, Card extra = null)
        {
            if (dungeon.Player.HasPendingHpFrame || (dungeon.CurrentBattle != null && dungeon.CurrentBattle.Enemies.Any(e => e.HasPendingHpFrame)))
                throw new ArgumentException("未確定HPを含むフレーム中は保存できません");
            if (dungeon.CurrentBattle?.PlayerMovement.IsNormalMoving == true)
                throw new ArgumentException("通常移動途中の状態は保存できません");
            var player = dungeon.Player;
            Definitions.ValidateCurrentMap(dungeon.Map);
            if (player.IsStaggered || player.HasParalysisState || player.HasTransformationPreview || player.HasTransformationDefense || player.IsInTemporaryMove || player.TerrainState.IsFloating || player.HasBarrier || player.HasAura || (player.PoiseRecoveryDelayFrames != player.PoiseRecoveryDefinitionFrames || player.PoiseRecoveryRemainingFrames > 0) || player.IsConcentrating)
                throw new ArgumentException("未対応の一時状態を保存できません");
            var deck = dungeon.GetCurrentDeck().GetAllCards().ToList();
            var obtained = dungeon.GetObtainedCards().ToList();
            if (extra != null) { deck.Add(extra); obtained.Add(extra); }
            var all = deck.Concat(obtained).Distinct().ToArray();
            var mapped = dungeon.Map.GetAllEnemies().ToDictionary(pair => pair.cellIndex, pair => pair.enemy);
            var enemyStates = new List<CheckpointEnemyDto>();
            foreach (var pair in Definitions.Enemies)
            {
                var e = mapped.TryGetValue(pair.Key, out var live) ? live : null;
                if (e != null && (e.IsSummoned || (!e.IsDead && (e.Guard.IsActive || e.HasBarrier || e.HasAura || e.IsInvincible || (e.PoiseRecoveryDelayFrames != e.PoiseDefinition?.RecoveryDelayFrames || e.PoiseRecoveryRemainingFrames > 0) || e.ActiveStatus != null || e.IsStunned || e.TerrainState.IsFloating || e.AccumulatedPoiseDamage != 0 || e.Poise != e.PoiseDefinition?.Maximum))))
                    throw new ArgumentException("未対応の敵状態を保存できません");
                enemyStates.Add(new CheckpointEnemyDto { CellIndex = pair.Key, EnemyId = pair.Value.EnemyId,
                    Hp = e?.CurrentHp ?? 0, GridX = e?.GridPosition.X ?? pair.Value.GridPosition.X, GridY = e?.GridPosition.Y ?? pair.Value.GridPosition.Y });
            }
            if (mapped.Keys.Any(i => !Definitions.Enemies.ContainsKey(i))) throw new ArgumentException("未登録の敵配置です");
            return new CheckpointDto {
                SchemaVersion = Definitions.SchemaVersion,
                ProcessedObstacleCells = Definitions.SchemaVersion >= 2 ? dungeon.CheckpointProcessedObstacles : null,
                DefinitionsRevision = Definitions.Revision, MapHash = Definitions.MapHash, DungeonId = Definitions.DungeonId,
                RunId = RunId, Sequence = (Last?.Sequence ?? 0) + 1, Boundary = boundary, PositionMilli = dungeon.CheckpointPositionMilli,
                Player = new CheckpointPlayerDto { Hp = player.CurrentHp, MaxHp = player.MaxHp, AttackPower = player.AttackPower,
                    Poise = player.Poise, StaggerFrames = player.StaggerDurationFrames, PoiseRecoveryFrames = player.PoiseRecoveryDefinitionFrames, FireResistance = Math.Min(100, player.Resistances.Fire), WaterResistance = Math.Min(100, player.Resistances.Water),
                    WindResistance = Math.Min(100, player.Resistances.Wind), EarthResistance = Math.Min(100, player.Resistances.Earth), CustomSpeed = player.CustomSpeed, GridX = player.GridPosition.X, GridY = player.GridPosition.Y },
                Cards = all.Select(c => Definitions.CaptureKnownCard(Identify(c), c)).ToArray(),
                DeckOrder = deck.Select(Identify).ToArray(), ObtainedOrder = obtained.Select(Identify).ToArray(), Enemies = enemyStates.ToArray()
            };
        }
    }
}

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>提供された全階層を固定する。階層数・敵・報酬の製品値は生成しない。</summary>
    public sealed class FloorSequenceDefinitions
    {
        private readonly CheckpointDefinitions[] floors;
        public string RouteId { get; }
        public string Revision { get; }
        public string RouteHash { get; }
        public int FloorCount => floors.Length;
        public FloorSequenceDefinitions(string routeId, string revision, IEnumerable<DungeonConfig> maps,
            Deck initialDeck, IEnumerable<KeyValuePair<string, Card>> additionalCards = null)
        {
            if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(revision) || maps == null || initialDeck == null)
                throw new ArgumentException("階層列ID・版・全階層・初期カード定義が必要です");
            var list = maps.ToArray();
            if (list.Length == 0 || list.Any(m => m == null)) throw new ArgumentException("空の階層列や未解決の階層は使用できません");
            list[list.Length - 1].ValidateFinalFloorForDeparture();
            var extras = additionalCards?.ToArray() ?? new KeyValuePair<string, Card>[0];
            // 全階層のセル/敵/障害物とカード定義を、現在階層の生成より先に検証・複製する。
            floors = list.Select(map => DungeonCheckpointSession.CreateDefault(map, initialDeck).Definitions).ToArray();
            foreach (var floor in floors) foreach (var card in extras) floor.RegisterCard(card.Key, card.Value);
            RouteId = routeId; Revision = revision;
            RouteHash = CheckpointValues.Hash(w => {
                w.Write(RouteId); w.Write(Revision); w.Write(floors.Length);
                foreach (var floor in floors) {
                    w.Write(floor.DungeonId); w.Write(floor.MapHash); w.Write(floor.SchemaVersion); w.Write(floor.CardCatalogHash);
                }
            });
        }
        public DungeonConfig CreateInitialMap(int index)
        {
            var floor = Floor(index);
            return floor.RestoreMap(floor.Enemies.ToDictionary(p => p.Key, p => new Enemy(
                p.Value.EnemyId, p.Value.Name, p.Value.MaxHp, p.Value.Element, p.Value.GridPosition,
                resistances: p.Value.Resistances, statusResistances: p.Value.StatusResistances, poiseDefinition: p.Value.PoiseDefinition)));
        }
        public DungeonCheckpointSession CreateCheckpointSession(int index, FloorSequenceSession route)
            => new DungeonCheckpointSession(Floor(index), new FloorCheckpointStore(route, index, index + 1 < FloorCount));

        public bool TryResolveOwnedCard(string id, ExceedLevel level, CardRarity rarity, string name, out Card card)
            => Floor(0).TryResolveOwnedCard(id, level, rarity, name, out card);
        public DungeonCheckpointSession CreateDepartureSession()
            => new DungeonCheckpointSession(Floor(0), new MemoryCheckpointStore(Floor(0)));

        internal CheckpointDefinitions Floor(int index)
        {
            if (index < 0 || index >= floors.Length) throw new ArgumentException("現在階層が範囲外です");
            return floors[index];
        }
        internal FloorSequenceCheckpointDto Wrap(int index, CheckpointDto state) => new FloorSequenceCheckpointDto {
            RouteId = RouteId, Revision = Revision, RouteHash = RouteHash, FloorIndex = index, State = state?.Copy() };
        public bool TryRestore(FloorSequenceCheckpointDto input, out RestoredFloorCheckpoint restored, out string error)
        {
            restored = null; error = null;
            try {
                CheckpointValues.Require(input != null && input.FormatVersion == 1 && input.RouteId == RouteId &&
                    input.Revision == Revision && input.RouteHash == RouteHash, "階層列の保存版または定義が一致しません");
                var copy = input.Copy(); var floor = Floor(copy.FloorIndex);
                if (!CheckpointCodec.TryRestore(copy.State, floor, out var state, out error)) return false;
                copy.State = state.Snapshot;
                restored = new RestoredFloorCheckpoint(copy, state); return true;
            }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            catch (OverflowException ex) { error = ex.Message; return false; }
        }
        internal CheckpointDto NextStart(int index, CheckpointDto carried)
        {
            var floor = Floor(index); var next = carried.Copy();
            next.SchemaVersion = floor.SchemaVersion; next.DefinitionsRevision = floor.Revision;
            next.DungeonId = floor.DungeonId; next.MapHash = floor.MapHash;
            next.Sequence = checked(next.Sequence + 1); next.Boundary = CheckpointBoundary.Departure;
            next.PositionMilli = 0; next.Player.GridX = next.Player.GridY = 0; next.Battle = null;
            next.ProcessedObstacleCells = floor.SchemaVersion >= 2 ? new int[0] : null;
            next.Enemies = floor.Enemies.Select(pair => new CheckpointEnemyDto {
                CellIndex = pair.Key, EnemyId = pair.Value.EnemyId, Hp = pair.Value.MaxHp,
                GridX = pair.Value.GridPosition.X, GridY = pair.Value.GridPosition.Y }).ToArray();
            return next;
        }
    }

    /// <summary>通常の境界保存を階層保存へ接続。中間の生存Terminalだけ次階層の出撃保存へ置換する。</summary>
    internal sealed class FloorCheckpointStore : ICheckpointStore
    {
        private readonly FloorSequenceSession route;
        private readonly int index;
        private readonly bool hasNext;
        private CheckpointDto pending;
        private bool writing;
        internal FloorCheckpointStore(FloorSequenceSession route, int index, bool hasNext)
        { this.route = route; this.index = index; this.hasNext = hasNext; }
        public CheckpointDto Read() => route.Current?.FloorIndex == index ? route.Current.Snapshot.State : null;
        public bool TryWrite(CheckpointDto data, out string error)
        {
            if (writing) { error = "階層保存中です"; return false; }
            writing = true;
            try {
                bool saved;
                if (pending != null) saved = route.Retry(out error);
                else if (route.Current == null) saved = route.TryStart(data, out error);
                else if (hasNext && data.Boundary == CheckpointBoundary.Terminal && data.Player.Hp > 0)
                    saved = route.TryTransferToNextFloor(index, data, out error);
                else saved = route.TrySaveCurrent(index, data, out error);
                pending = !saved && route.HasPendingSave ? data.Copy() : null;
                return saved;
            }
            finally { writing = false; }
        }
    }

    /// <summary>検証済みの現在階層。通常Manager/Controllerへの接続は呼出側の責務。</summary>
    public sealed class RestoredFloorCheckpoint
    {
        private readonly FloorSequenceCheckpointDto snapshot;
        public FloorSequenceCheckpointDto Snapshot => snapshot.Copy();
        public int FloorIndex => snapshot.FloorIndex;
        public RestoredCheckpoint State { get; }
        internal RestoredFloorCheckpoint(FloorSequenceCheckpointDto snapshot, RestoredCheckpoint state)
        { this.snapshot = snapshot.Copy(); State = state; }
    }

    public interface IFloorSequenceStore
    {
        FloorSequenceCheckpointDto Read();
        string ReadError { get; }
        bool TryWrite(FloorSequenceCheckpointDto data, out string error);
    }

    /// <summary>
    /// 保存成功後だけ現在階層を交換する基盤。終了条件の判定・自動遷移・View更新は行わない。
    /// 渡す状態は既存Captureで検証した境界とし、未対応の実行中効果をDTOへ省略して渡さない。
    /// </summary>
    public sealed class FloorSequenceSession
    {
        private readonly FloorSequenceDefinitions definitions;
        private readonly IFloorSequenceStore store;
        private FloorSequenceCheckpointDto pending;
        private bool writing;
        public RestoredFloorCheckpoint Current { get; private set; }
        public bool HasPendingSave => pending != null;
        public FloorSequenceSession(FloorSequenceDefinitions definitions, IFloorSequenceStore store)
        { this.definitions = definitions ?? throw new ArgumentNullException(nameof(definitions)); this.store = store ?? throw new ArgumentNullException(nameof(store)); }
        public bool TryLoad(out string error)
        {
            if (writing || pending != null) { error = "未確定の階層保存があります"; return false; }
            writing = true;
            try {
                var data = store.Read();
                if (data == null && store.ReadError != null) { error = store.ReadError; return false; }
                if (!definitions.TryRestore(data, out var restored, out error)) return false;
                Current = restored; return true;
            }
            finally { writing = false; }
        }
        public bool TryStart(CheckpointDto departure, out string error)
        {
            if (writing || pending != null || Current != null || departure?.Boundary != CheckpointBoundary.Departure)
            { error = "新しい階層列を開始できる状態ではありません"; return false; }
            return Stage(definitions.Wrap(0, departure), out error);
        }
        public bool TrySaveCurrent(int expectedFloorIndex, CheckpointDto state, out string error)
        {
            if (!ValidateSource(expectedFloorIndex, state, out error)) return false;
            if (state.Sequence <= Current.Snapshot.State.Sequence) { error = "保存世代を進める必要があります"; return false; }
            return Stage(definitions.Wrap(expectedFloorIndex, state), out error);
        }
        /// <summary>呼出側で階層終了を判定した後の明示的な引継ぎ。最終階層から先へは進めない。</summary>
        public bool TryTransferToNextFloor(int expectedFloorIndex, CheckpointDto completed, out string error)
        {
            if (!ValidateSource(expectedFloorIndex, completed, out error)) return false;
            if (expectedFloorIndex + 1 >= definitions.FloorCount || completed.Player.Hp <= 0 ||
                (completed.Boundary != CheckpointBoundary.RewardCompleted && completed.Boundary != CheckpointBoundary.Terminal))
            { error = "次階層へ引き継げる完了境界ではありません"; return false; }
            try { return Stage(definitions.Wrap(expectedFloorIndex + 1, definitions.NextStart(expectedFloorIndex + 1, completed)), out error); }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            catch (OverflowException ex) { error = ex.Message; return false; }
        }
        private bool ValidateSource(int index, CheckpointDto state, out string error)
        {
            error = null;
            if (writing || pending != null || Current == null || index != Current.FloorIndex || state == null ||
                state.RunId != Current.Snapshot.State.RunId || state.Sequence < Current.Snapshot.State.Sequence)
            { error = "現在階層・保存世代が一致しないか、保存処理中です"; return false; }
            return definitions.TryRestore(definitions.Wrap(index, state), out _, out error);
        }
        private bool Stage(FloorSequenceCheckpointDto data, out string error)
        {
            if (!definitions.TryRestore(data, out var candidate, out error)) return false;
            pending = candidate.Snapshot;
            return Retry(out error);
        }
        public bool Retry(out string error)
        {
            error = null;
            if (writing || pending == null) { error = "再試行する保存がありません"; return false; }
            writing = true;
            try {
                // 候補は保存先にもコピーで渡す。失敗時は同じ候補を保持し、現モデルは交換しない。
                if (!definitions.TryRestore(pending, out var candidate, out error)) return false;
                if (!store.TryWrite(candidate.Snapshot, out error)) return false;
                Current = candidate; pending = null; return true;
            }
            finally { writing = false; }
        }
    }
}
