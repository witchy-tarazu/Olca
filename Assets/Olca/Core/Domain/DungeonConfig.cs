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
        /// 新規出撃の最終階層を検証する。旧保存の読込や中間階層の定義には適用しない。
        /// 現在の単階層出撃では、その1階層が最終階層となる。
        /// </summary>
        public void ValidateFinalFloorForDeparture()
        {
            if (TotalCellCount <= 0 || MapCells.Count != TotalCellCount)
                throw new System.ArgumentException("最終階層のマップ定義が不完全です");
            bool hasBoss = false;
            for (int i = 0; i < MapCells.Count; i++)
            {
                var cell = MapCells[i];
                if (cell == null || cell.Index != i)
                    throw new System.ArgumentException("最終階層のセル配置が不整合です");
                if (cell.Type != CellType.Boss) continue;
                if (cell.Enemy == null || cell.Enemy.IsDead || cell.Enemy.IsSummoned)
                    throw new System.ArgumentException("最終階層には生存した本体ボスの定義が必要です");
                hasBoss = true;
            }
            if (!hasBoss) throw new System.ArgumentException("最終階層にはボスが必要です。ボスのいない中間階層を単階層出撃に設定できません");
        }

        /// <summary>
        /// テスト用の簡易ダンジョン生成
        /// </summary>
        /// <param name="cellCount">総マス数</param>
        /// <param name="enemySpacing">敵と敵の間隔（セル数）</param>
        /// <param name="firstEnemyCell">最初の敵が出現するセルインデックス</param>
        // 省略時は旧マップ定義を維持。通常シーンの仮値はControllerから供給する。
        public static DungeonConfig CreateTestDungeon(int cellCount = 20, int enemySpacing = 30, int firstEnemyCell = 10,
            EnemyPoiseDefinition enemyPoise = null, EnemyPoiseDefinition bossPoise = null)
        {
            var cells = new List<MapCell>();

            for (int i = 0; i < cellCount; i++)
            {
                // 最後のマスにボスを配置
                if (i == cellCount - 1)
                {
                    var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(i, 0), poiseDefinition: bossPoise);
                    cells.Add(new MapCell(i, boss));
                }
                // firstEnemyCell以降、enemySpacingマスごとに通常敵を配置
                else if (i >= firstEnemyCell && (i - firstEnemyCell) % enemySpacing == 0)
                {
                    var enemy = new Enemy($"enemy_{i}", "スライム", 50, ElementType.None, new Vector2Int(i, 0), poiseDefinition: enemyPoise);
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
        public int? PoiseRecoveryFrames; // 旧保存の省略/nullは自然回復未設定。残り時間ではなく定義
        public PlayerPoisonDto[] Poisons; // 旧保存の省略/nullは毒なし
        public int StaggerFrames; // 旧保存の省略/0は時間付きのけぞり未設定
        public int FireResistance, WaterResistance, WindResistance, EarthResistance;
        public float CustomSpeed;
        public int GridX, GridY;
        public CheckpointPlayerDto Copy() {
            var copy=(CheckpointPlayerDto)MemberwiseClone();
            copy.Poisons=Poisons==null ? null : System.Array.ConvertAll(Poisons,p=>p?.Copy()); return copy;
        }
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
        public int[] CurrencyAmounts; // 旧保存の欠落は通貨報酬0として扱う。
        public bool DashContact;
        public int? DashEffect;
        public float GridOriginX;
        public int HandLimit;
        public string[] FirstHand = new string[0];
        public bool RewardOffered;
        public string[] RewardCandidates = new string[0];
        public CheckpointBattleDto Copy() => new CheckpointBattleDto {
            CurrencyAmounts = CurrencyAmounts == null ? null : (int[])CurrencyAmounts.Clone(),
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
        public long Currency;
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
            MapHash = MapHash, RunId = RunId, Sequence = Sequence, PositionMilli = PositionMilli, Currency = Currency, Boundary = Boundary,
            Player = Player?.Copy(), Cards = Cards == null ? null : System.Array.ConvertAll(Cards, c => c?.Copy()),
            DeckOrder = DeckOrder == null ? null : (string[])DeckOrder.Clone(),
            ObtainedOrder = ObtainedOrder == null ? null : (string[])ObtainedOrder.Clone(),
            Enemies = Enemies == null ? null : System.Array.ConvertAll(Enemies, e => e?.Copy()),
            ProcessedObstacleCells = ProcessedObstacleCells == null ? null : (int[])ProcessedObstacleCells.Clone(), Battle = Battle?.Copy() };
    }
}

namespace Olca.Core.Domain
{
    /// <summary>階層列用の別形式。既存の単階層DTOと保存版は変更しない。</summary>
    public sealed class FloorSequenceCheckpointDto
    {
        public int FormatVersion = 1;
        public string RouteId, Revision, RouteHash;
        public int FloorIndex;
        public CheckpointDto State;
        public FloorSequenceCheckpointDto Copy() => new FloorSequenceCheckpointDto {
            FormatVersion = FormatVersion, RouteId = RouteId, Revision = Revision, RouteHash = RouteHash,
            FloorIndex = FloorIndex, State = State?.Copy() };
    }
}
