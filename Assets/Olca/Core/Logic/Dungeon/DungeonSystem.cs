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

        // ========== 接敵情報保持 ==========
        private ContactInfo currentContactInfo;
        private List<Card> pendingRewardChoices;

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
            FusionSystem fusionSystem = null,
            ICardPool rewardPool = null)
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
            cardRewardSystem = new CardRewardSystem(initialDeck, random, rewardPool);
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
                fusionSystem: fusionSystem
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
            if (IsDungeonCleared || pendingRewardChoices == null) return;
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
        private readonly string dungeonName;
        private readonly int initialDeckSize;
        internal IReadOnlyDictionary<int, Enemy> Enemies => enemies;

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
                    if (cell == null || cell.Index != i || cell.Obstacle != null ||
                        (cell.Type != CellType.Empty && cell.Type != CellType.Enemy && cell.Type != CellType.Boss))
                        throw new ArgumentException("この保存版では固定敵・空白セルのみ対応します");
                    CheckpointValues.Require(CheckpointValues.Finite(cell.Height) && cell.Height >= 0 &&
                        ((cell.Type == CellType.Empty) == (cell.Enemy == null)), "セル内容が不整合です");
                    w.Write((int)cell.Type); w.Write(cell.Height);
                    if (cell.Enemy == null) continue;
                    var e = cell.Enemy;
                    if (string.IsNullOrWhiteSpace(e.EnemyId) || e.MaxHp <= 0 || e.IsSummoned || e.CurrentHp != e.MaxHp || e.ActiveStatus != null || e.IsStunned || e.TerrainState.IsFloating)
                        throw new ArgumentException("敵の標準定義が必要です");
                    enemies.Add(i, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element, e.GridPosition));
                    heights.Add(i, cell.Height);
                    w.Write(e.EnemyId); w.Write(e.Name); w.Write(e.MaxHp); w.Write((int)e.Element);
                    w.Write(e.GridPosition.X); w.Write(e.GridPosition.Y);
                }
            });
        }

        internal DungeonConfig RestoreMap(IReadOnlyDictionary<int, Enemy> restoredEnemies)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < CellCount; i++)
                cells.Add(restoredEnemies.TryGetValue(i, out var enemy) && !enemy.IsDead
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
        public void RegisterGrant(string key, TuningGrantDefinition grant)
        {
            if (string.IsNullOrWhiteSpace(key) || grant == null) throw new ArgumentException("調律定義が必要です");
            grants.Add(key, grant);
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
        internal static string CardHash(Card c) => Hash(w => {
            Text(w, c.Id); Text(w, c.Name); Text(w, c.Description);
            w.Write(c.Cost); w.Write((int)c.Rarity); w.Write((int)c.Category); w.Write((int)c.Element); Text(w, c.Series?.Name);
            w.Write(c.Power); w.Write((int)c.TargetType); Text(w, c.PanelChangeType); Text(w, c.MoveDirection); w.Write(c.MoveDistance);
            w.Write(c.MoveMotionTime); w.Write(c.AttackMotionTime); w.Write((int)c.PenetrationMode);
            w.Write(c.StartupTime); w.Write(c.RecoveryTime); w.Write(c.ExtendRecoveryDuringDerivative); w.Write(c.IsInstantEffect);
            w.Write((int)c.ExceedLevel); Modifier(w, c.Modifier); Modifier(w, c.GetTuningBaseModifier()); Grant(w, c.TuningDefinition);
            w.Write(c.TuningCount); foreach (var grant in c.TuningHistory) Grant(w, grant);
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
            Player = new PlayerCharacter(p.MaxHp, new Vector2Int(p.GridX, p.GridY), p.AttackPower, p.CustomSpeed, p.Poise);
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
                CheckpointValues.Require(d.SchemaVersion == 1 && d.DefinitionsRevision == definitions.Revision &&
                    d.DungeonId == definitions.DungeonId && d.MapHash == definitions.MapHash, "保存版または定義版が一致しません");
                CheckpointValues.Require(!string.IsNullOrWhiteSpace(d.RunId) && d.Sequence > 0 && Enum.IsDefined(typeof(CheckpointBoundary), d.Boundary), "保存境界が不正です");
                CheckpointValues.Require(d.PositionMilli >= 0 && d.PositionMilli < (long)definitions.CellCount * 1000, "探索位置が範囲外です");
                var p = d.Player;
                CheckpointValues.Require(p != null && p.MaxHp > 0 && p.Hp > 0 && p.Hp <= p.MaxHp && p.AttackPower >= 0 && p.Poise > 0 &&
                    CheckpointValues.Finite(p.CustomSpeed) && p.CustomSpeed > 0, "プレイヤー値が不正です");
                CheckpointValues.Require(d.Cards != null && d.DeckOrder != null && d.ObtainedOrder != null && d.Enemies != null, "必須配列が欠落しています");
                var cards = new Dictionary<string, Card>();
                foreach (var c in d.Cards) { var card = definitions.RestoreCard(c); cards.Add(c.InstanceId, card); }
                ValidateReferences(d.DeckOrder, cards); ValidateReferences(d.ObtainedOrder, cards);
                var enemies = new Dictionary<int, Enemy>();
                foreach (var e in d.Enemies)
                {
                    CheckpointValues.Require(e != null && definitions.Enemies.TryGetValue(e.CellIndex, out _), "敵配置が未定義です");
                    var template = definitions.Enemies[e.CellIndex];
                    CheckpointValues.Require(e.EnemyId == template.EnemyId && e.Hp >= 0 && e.Hp <= template.MaxHp, "敵の値が不正です");
                    var enemy = new Enemy(template.EnemyId, template.Name, template.MaxHp, template.Element, new Vector2Int(e.GridX, e.GridY));
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
