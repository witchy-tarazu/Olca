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
        private readonly PlayerCharacter playerCharacter;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        // ========== 状態管理 ==========
        public bool IsInBattle { get; private set; }
        public bool IsDungeonCleared { get; private set; }

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
            IRandomProvider random,
            IGameLogger logger)
        {
            // 依存関係の初期化
            map = new DungeonMap(config);
            playerMovement = new PlayerMovement(initialPosition: 0f);
            contactSystem = new ContactSystem(map);
            actionSystem = new ActionSystem();
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
        /// 毎フレーム更新（deltaTimeを外部から受け取る）
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsInBattle || IsDungeonCleared) return;

            // プレイヤー移動更新
            playerMovement.Update(deltaTime);

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

                // TODO: カードドロップ判定（フェーズ3で実装）

                // 敵をマップから削除
                // contactSystem.GetLastContactedEnemy() が必要だが、
                // 今は簡易実装として、外部から敵を渡してもらう形にする
            }
            else
            {
                logger.LogInfo("[Dungeon] バトル敗北 - ゲームオーバー");
                OnDungeonFailed?.Invoke();
                return;
            }

            // 接敵リセット
            contactSystem.ResetContact();

            // プレイヤー移動を再開
            playerMovement.Resume();

            logger.LogInfo("[Dungeon] ダンジョン再開");
        }

        // ========== イベント ==========
        public event Action<ContactInfo> OnBattleStart;
        public event Action OnDungeonFailed;
        public event Action OnDungeonCleared;
    }
}
