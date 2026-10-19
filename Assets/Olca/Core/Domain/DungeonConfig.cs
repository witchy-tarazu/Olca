using System.Collections.Generic;

namespace Olca.Core.Domain
{
    /// <summary>
    /// ダンジョン設定データ
    /// </summary>
    public class DungeonConfig
    {
        public string DungeonId { get; }
        public string DungeonName { get; }
        public int TotalCellCount { get; }         // 総マス数
        public int InitialDeckSize { get; }        // 初期デッキ枚数（デフォルト20枚）
        public List<MapCell> MapCells { get; }     // マス配置データ

        public DungeonConfig(
            string dungeonId,
            string dungeonName,
            int totalCellCount,
            List<MapCell> mapCells,
            int initialDeckSize = 20)
        {
            DungeonId = dungeonId;
            DungeonName = dungeonName;
            TotalCellCount = totalCellCount;
            MapCells = mapCells ?? new List<MapCell>();
            InitialDeckSize = initialDeckSize;
        }

        /// <summary>
        /// テスト用の簡易ダンジョン生成
        /// </summary>
        /// <param name="cellCount">総マス数</param>
        /// <param name="enemySpacing">敵と敵の間隔（セル数）</param>
        /// <param name="firstEnemyCell">最初の敵が出現するセルインデックス</param>
        public static DungeonConfig CreateTestDungeon(int cellCount = 20, int enemySpacing = 30, int firstEnemyCell = 10)
        {
            var cells = new List<MapCell>();

            for (int i = 0; i < cellCount; i++)
            {
                // 最後のマスにボスを配置
                if (i == cellCount - 1)
                {
                    var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(i, 0));
                    cells.Add(new MapCell(i, boss));
                }
                // firstEnemyCell以降、enemySpacingマスごとに通常敵を配置
                else if (i >= firstEnemyCell && (i - firstEnemyCell) % enemySpacing == 0)
                {
                    var enemy = new Enemy($"enemy_{i}", "スライム", 50, ElementType.None, new Vector2Int(i, 0));
                    cells.Add(new MapCell(i, enemy));
                }
                // それ以外は空白
                else
                {
                    cells.Add(new MapCell(i));
                }
            }

            return new DungeonConfig(
                dungeonId: "test_dungeon",
                dungeonName: "テストダンジョン",
                totalCellCount: cellCount,
                mapCells: cells
            );
        }
    }
}

namespace Olca.Core.Domain
{
    // 保存形式の値だけを持つDTO。戦闘中の参照・非同期処理・Undo操作は含めない。
    public enum CheckpointBoundary { Departure, BattleStart, RewardCompleted, Terminal }

    public sealed class CheckpointCardDto
    {
        public string InstanceId;
        public string DefinitionKey;
        public string ValueHash;
        public int TuningCount;
        public string[] GrantKeys = new string[0];
        public CheckpointCardDto Copy() => new CheckpointCardDto { InstanceId = InstanceId,
            DefinitionKey = DefinitionKey, ValueHash = ValueHash, TuningCount = TuningCount,
            GrantKeys = GrantKeys == null ? null : (string[])GrantKeys.Clone() };
    }

    public sealed class CheckpointPlayerDto
    {
        public int Hp, MaxHp, AttackPower, Poise;
        public int FireResistance, WaterResistance, WindResistance, EarthResistance;
        public float CustomSpeed;
        public int GridX, GridY;
        public CheckpointPlayerDto Copy() => (CheckpointPlayerDto)MemberwiseClone();
    }

    public sealed class CheckpointEnemyDto
    {
        // 固定マップでは配置セルが個体識別子。同種敵のEnemyIdだけで同一視しない。
        public int CellIndex, Hp, GridX, GridY;
        public string EnemyId;
        public CheckpointEnemyDto Copy() => (CheckpointEnemyDto)MemberwiseClone();
    }

    public sealed class CheckpointBattleDto
    {
        public int ContactCell;
        public int[] ParticipantCells = new int[0];
        public bool DashContact;
        public int? DashEffect;
        public float GridOriginX;
        public int HandLimit;
        public string[] FirstHand = new string[0];
        public bool RewardOffered;
        public string[] RewardCandidates = new string[0];
        public CheckpointBattleDto Copy() => new CheckpointBattleDto {
            ContactCell = ContactCell, ParticipantCells = ParticipantCells == null ? null : (int[])ParticipantCells.Clone(),
            DashContact = DashContact, DashEffect = DashEffect, GridOriginX = GridOriginX, HandLimit = HandLimit,
            FirstHand = FirstHand == null ? null : (string[])FirstHand.Clone(), RewardOffered = RewardOffered,
            RewardCandidates = RewardCandidates == null ? null : (string[])RewardCandidates.Clone() };
    }

    public sealed class CheckpointDto
    {
        public int SchemaVersion = 1;
        public string DefinitionsRevision, DungeonId, MapHash, RunId;
        public long Sequence, PositionMilli;
        public CheckpointBoundary Boundary;
        public CheckpointPlayerDto Player;
        public CheckpointCardDto[] Cards = new CheckpointCardDto[0];
        public string[] DeckOrder = new string[0];
        // 取得後に破棄されたカードも履歴に残るため、DeckOrderとは独立した参照列。
        public string[] ObtainedOrder = new string[0];
        public CheckpointEnemyDto[] Enemies = new CheckpointEnemyDto[0];
        // 版2のみ。版1の既存保存では欠落/nullを維持する。
        public int[] ProcessedObstacleCells;
        public CheckpointBattleDto Battle;
        public CheckpointDto Copy() => new CheckpointDto {
            SchemaVersion = SchemaVersion, DefinitionsRevision = DefinitionsRevision, DungeonId = DungeonId,
            MapHash = MapHash, RunId = RunId, Sequence = Sequence, PositionMilli = PositionMilli, Boundary = Boundary,
            Player = Player?.Copy(), Cards = Cards == null ? null : System.Array.ConvertAll(Cards, c => c?.Copy()),
            DeckOrder = DeckOrder == null ? null : (string[])DeckOrder.Clone(),
            ObtainedOrder = ObtainedOrder == null ? null : (string[])ObtainedOrder.Clone(),
            Enemies = Enemies == null ? null : System.Array.ConvertAll(Enemies, e => e?.Copy()),
            ProcessedObstacleCells = ProcessedObstacleCells == null ? null : (int[])ProcessedObstacleCells.Clone(), Battle = Battle?.Copy() };
    }
}
