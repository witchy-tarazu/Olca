using System;
using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// バトルフィールド上に設置される障害物（ロックキューブ等）
    /// </summary>
    public class FieldObstacle
    {
        /// <summary>障害物ID</summary>
        public string ObstacleId { get; }

        /// <summary>残耐久値</summary>
        public int Hp { get; private set; }

        /// <summary>破壊済みか</summary>
        public bool IsDestroyed => Hp <= 0;

        public FieldObstacle(string obstacleId, int hp)
        {
            ObstacleId = obstacleId ?? throw new ArgumentNullException(nameof(obstacleId));
            if (hp <= 0) throw new ArgumentException("耐久値は1以上である必要があります", nameof(hp));
            Hp = hp;
        }

        /// <summary>ダメージを受ける</summary>
        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;
            Hp -= damage;
            if (Hp < 0) Hp = 0;
        }
    }

    /// <summary>
    /// 6x1グリッドのバトルフィールド
    /// </summary>
    public class BattleField
    {
        public const int GRID_WIDTH = 6;
        public const int GRID_HEIGHT = 1;

        private readonly PanelType[,] panels;
        private readonly bool[,] permanentHoles = new bool[GRID_WIDTH, GRID_HEIGHT];
        private Vector2Int? protectedPosition;
        private readonly Dictionary<string, FieldObstacle> obstacles = new Dictionary<string, FieldObstacle>();

        public BattleField(IEnumerable<Vector2Int> initialPermanentHoles = null)
        {
            panels = new PanelType[GRID_WIDTH, GRID_HEIGHT];

            // 初期化: すべて通常パネル
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                for (int y = 0; y < GRID_HEIGHT; y++)
                {
                    panels[x, y] = PanelType.Normal;
                }
            }

            // 永続穴は初期配置でのみ指定でき、通常の地形変更からは作らない。
            if (initialPermanentHoles == null) return;
            foreach (var position in initialPermanentHoles)
            {
                if (!IsInBounds(position))
                    throw new ArgumentOutOfRangeException(nameof(initialPermanentHoles), "永続穴の位置がグリッド範囲外です");
                permanentHoles[position.X, position.Y] = true;
                panels[position.X, position.Y] = PanelType.Hole;
            }
        }

        /// <summary>初期配置された上書き不可の穴か。範囲外は初期配置ではない。</summary>
        public bool IsPermanentHole(Vector2Int position)
            => IsInBounds(position) && permanentHoles[position.X, position.Y];

        /// <summary>
        /// 指定位置に移動可能か
        /// </summary>
        public bool CanMoveTo(Vector2Int position, BattleTerrainState terrain = null)
        {
            // グリッド範囲外
            if (!IsInBounds(position)) return false;

            // 浮遊は穴だけを許可し、範囲・障害物の制限は維持する。
            PanelType panelType = GetPanelType(position);
            if (terrain != null ? !terrain.CanEnter(panelType) : panelType == PanelType.Hole) return false;

            // 障害物があるマスも移動不可
            if (HasObstacle(position)) return false;

            return true;
        }

        /// <summary>
        /// グリッド範囲内か
        /// </summary>
        public bool IsInBounds(Vector2Int position)
        {
            return position.X >= 0 && position.X < GRID_WIDTH &&
                   position.Y >= 0 && position.Y < GRID_HEIGHT;
        }

        /// <summary>
        /// パネルタイプを変更
        /// パネル保護機能: 保護位置が穴パネルに変更されようとした場合、ヒビパネルにする
        /// </summary>
        public void ChangePanelType(Vector2Int position, PanelType type)
        {
            if (!IsInBounds(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");
            }

            // 後発の地形効果や復元処理では、初期配置の永続穴を上書きしない。
            if (IsPermanentHole(position)) return;

            // パネル保護: 保護位置が穴パネルに変更されようとした場合、ヒビパネルにする
            if (protectedPosition.HasValue && position == protectedPosition.Value)
            {
                PanelType currentType = panels[position.X, position.Y];

                // 元々穴パネルでない場合のみ保護
                if (currentType != PanelType.Hole && type == PanelType.Hole)
                {
                    type = PanelType.Cracked;
                }
            }

            panels[position.X, position.Y] = type;
            OnPanelChanged?.Invoke(position, type);
        }

        /// <summary>
        /// パネルタイプを取得
        /// </summary>
        public PanelType GetPanelType(Vector2Int position)
        {
            if (!IsInBounds(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");
            }

            return panels[position.X, position.Y];
        }

        /// <summary>
        /// 指定位置が穴パネルかどうかを判定
        /// </summary>
        /// <param name="position">判定する位置</param>
        /// <returns>穴パネルならtrue</returns>
        public bool IsHole(Vector2Int position)
        {
            if (!IsInBounds(position))
            {
                // 範囲外は穴扱い（投擲物が落下する）
                return true;
            }

            return GetPanelType(position) == PanelType.Hole;
        }

        /// <summary>
        /// 永続穴を残し、変更可能なパネルを通常に戻す
        /// </summary>
        public void ResetAllPanels()
        {
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                for (int y = 0; y < GRID_HEIGHT; y++)
                {
                    panels[x, y] = permanentHoles[x, y] ? PanelType.Hole : PanelType.Normal;
                }
            }
        }

        /// <summary>
        /// 指定位置に障害物を設置可能か（グリッド内 かつ 既存障害物なし）
        /// </summary>
        public bool CanPlaceObstacle(Vector2Int position)
        {
            if (!IsInBounds(position)) return false;
            string key = ObstacleKey(position);
            return !obstacles.ContainsKey(key);
        }

        /// <summary>
        /// 指定位置に障害物を設置する
        /// </summary>
        public void PlaceObstacle(Vector2Int position, FieldObstacle obstacle)
        {
            if (obstacle == null) throw new ArgumentNullException(nameof(obstacle));
            if (!IsInBounds(position))
                throw new ArgumentOutOfRangeException(nameof(position), "位置がグリッド範囲外です");

            obstacles[ObstacleKey(position)] = obstacle;
        }

        /// <summary>
        /// 指定位置の障害物を取得（存在しない場合はnull）
        /// </summary>
        public FieldObstacle GetObstacle(Vector2Int position)
        {
            obstacles.TryGetValue(ObstacleKey(position), out var obstacle);
            return obstacle;
        }

        /// <summary>
        /// 指定位置の障害物を削除する
        /// </summary>
        public void RemoveObstacle(Vector2Int position)
        {
            obstacles.Remove(ObstacleKey(position));
        }

        /// <summary>
        /// 障害物があるか（移動不可判定に使用）
        /// </summary>
        public bool HasObstacle(Vector2Int position)
        {
            return obstacles.ContainsKey(ObstacleKey(position));
        }

        /// <summary>戦闘終了時に設置物を破棄する。破壊ダメージや撃破通知は発生させない。</summary>
        public void ClearObstacles() => obstacles.Clear();

        private static string ObstacleKey(Vector2Int position) => $"{position.X},{position.Y}";

        /// <summary>
        /// パネル保護を設定（一時移動の移動元を保護）
        /// </summary>
        public void SetProtectedPosition(Vector2Int? position)
        {
            protectedPosition = position;
        }

        /// <summary>
        /// パネル変更時のイベント
        /// </summary>
        public event Action<Vector2Int, PanelType> OnPanelChanged;
    }
}
