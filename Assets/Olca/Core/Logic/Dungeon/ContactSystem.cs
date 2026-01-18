using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// 接敵情報
    /// </summary>
    public class ContactInfo
    {
        public Enemy Enemy { get; set; }
        public bool IsDashContact { get; set; }
        public ActionSystem.DashContactEffect? DashEffect { get; set; }
    }

    /// <summary>
    /// 接敵判定システム
    /// </summary>
    public class ContactSystem
    {
        private readonly DungeonMap map;
        private const float CONTACT_DISTANCE = 2.0f;  // 非ダッシュ時の停止距離（マス）
        private const float DASH_CONTACT_DISTANCE = 0.5f;  // ダッシュ時の接触距離

        private Enemy lastContactedEnemy;  // 最後に接触した敵（連続判定防止）

        public ContactSystem(DungeonMap map)
        {
            this.map = map;
            lastContactedEnemy = null;
        }

        /// <summary>
        /// 接敵判定（毎フレーム呼び出し）
        /// </summary>
        public void CheckContact(PlayerMovement player)
        {
            // 接敵範囲内の敵を検索
            float checkDistance = player.IsDashing ? DASH_CONTACT_DISTANCE : CONTACT_DISTANCE;
            MapCell nextCell = map.GetCellAtPosition(player.CurrentPosition + checkDistance);

            if (nextCell != null && nextCell.Enemy != null)
            {
                // 既に接触済みの敵は無視
                if (nextCell.Enemy == lastContactedEnemy)
                {
                    return;
                }

                // 空中の敵は高さが足りないと接触しない
                if (nextCell.Height > 0f && player.Height < nextCell.Height - 0.2f)
                {
                    return;
                }

                // 接敵！
                lastContactedEnemy = nextCell.Enemy;

                ContactInfo contactInfo = new ContactInfo
                {
                    Enemy = nextCell.Enemy,
                    IsDashContact = player.IsDashing,
                    DashEffect = null  // ActionSystemで決定
                };

                OnEnemyContact?.Invoke(contactInfo);
            }
        }

        /// <summary>
        /// 接敵リセット（バトル終了後に呼ぶ）
        /// </summary>
        public void ResetContact()
        {
            lastContactedEnemy = null;
        }

        public event Action<ContactInfo> OnEnemyContact;
    }
}
