using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Integration;

namespace Olca.Core.Logic.Battle
{
    /// <summary>プラットフォームのRandom実装を使わず、seedと状態を明示する乱数。</summary>
    public sealed class SeededBattleRandom : IRandomProvider
    {
        public uint State { get; private set; }
        public long Draws { get; private set; }
        public SeededBattleRandom(uint seed) { State = seed == 0 ? 0x9e3779b9u : seed; }
        private uint Next()
        {
            uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x; Draws++; return x;
        }
        public int Range(int min, int max)
        {
            if (min >= max) throw new ArgumentOutOfRangeException(nameof(max));
            uint width = (uint)((long)max - min), threshold = unchecked(0u - width) % width, value;
            do { value = Next(); } while (value < threshold);
            return (int)((long)min + value % width);
        }
        public float Value => (Next() >> 8) * (1f / 16777216f);
        public float Range(float min, float max)
        {
            if (float.IsNaN(min) || float.IsNaN(max) || float.IsInfinity(min) || float.IsInfinity(max) || min > max) throw new ArgumentOutOfRangeException();
            return min + (max - min) * Value;
        }
    }

    public sealed class BattleSimulationSettings
    {
        public int PlayerHp { get; }
        public int EnemyHp { get; }
        public int ChargeFrames { get; }
        public int IntervalFrames { get; }
        public BattleSimulationSettings(int playerHp = 100, int enemyHp = 120, int chargeFrames = 15, int intervalFrames = 12)
        {
            if (playerHp < 1 || enemyHp < 1 || chargeFrames < 1 || intervalFrames < 1) throw new ArgumentOutOfRangeException();
            PlayerHp = playerHp; EnemyHp = enemyHp; ChargeFrames = chargeFrames; IntervalFrames = intervalFrames;
        }
    }
    public sealed class BattleFrameCommand
    {
        public LogicalInput Input { get; }
        public bool? UserPaused { get; }
        public CustomScreenCommand Custom { get; }
        public BattleFrameCommand(LogicalInput input = null, bool? userPaused = null, CustomScreenCommand custom = null)
        { Input = (input ?? new LogicalInput()).Copy(); UserPaused = userPaused; Custom = custom; }
    }
    public sealed class BattleFrameObservation
    {
        public long Step { get; }
        public long LogicalFrame { get; }
        public int PlayerHp { get; }
        public BattleState State { get; }
        public string Canonical { get; }
        public IReadOnlyList<string> Events { get; }
        internal BattleFrameObservation(long step, BattleSystem battle, SeededBattleRandom random, BasicAttackInputState attack, string[] events)
        {
            Step = step; LogicalFrame = battle.LogicalFrame; PlayerHp = battle.Player.CurrentHp; State = battle.State; Events = Array.AsReadOnly(events);
            var p = battle.Player.GridPosition;
            string enemies = string.Join(";", battle.Enemies.Select(e => e.EnemyId + ":" + e.CurrentHp + ":" + e.GridPosition.X + ":" + e.GridPosition.Y + ":" + e.GetStatus(StatusEffectType.Poison)?.RemainingFrames));
            string panels = string.Join("", Enumerable.Range(0, BattleField.GRID_HEIGHT).SelectMany(y => Enumerable.Range(0, BattleField.GRID_WIDTH).Select(x => ((int)battle.Field.GetPanelType(new Vector2Int(x, y))).ToString(CultureInfo.InvariantCulture))));
            Canonical = string.Join("|", step, LogicalFrame, battle.State, battle.IsPaused, PlayerHp, p.X, p.Y, enemies,
                battle.CardUsage.RemainingCount, battle.CardUsage.InFlightCard?.Id ?? "-", battle.PlayerActionPhase,
                attack.ChargeFrames, attack.CooldownFrames, battle.DelayedEffects.ActiveEffectCount, panels, random.State, random.Draws, string.Join(";", events), battle.CustomScreen.Commands.Observe().Canonical);
        }
    }
    /// <summary>実ゲームと同じBattleSystemへの整数フレーム入口。製品データ/保存/UIを持たない。</summary>
    public sealed class BattleSimulation
    {
        public BattleSystem Battle { get; }
        public SeededBattleRandom Random { get; }
        public BattleSimulationSettings Settings { get; }
        private readonly BasicAttackInputState attack;
        private readonly List<string> events = new List<string>();
        public long StepIndex { get; private set; }
        private bool stepping;
        public BattleSimulation(BattleSimulationSettings settings, uint seed, BasicAttackInputState attack = null)
            : this(settings, seed, rng => CreateSampleBattle(settings, rng), false, attack) { }

        /// <summary>初期状態と既存の定義を呼出元から注入する。同じコア以外に判定を持たせない。</summary>
        public BattleSimulation(BattleSimulationSettings settings, uint seed, Func<IRandomProvider, BattleSystem> createBattle,
            bool openInitialCustom, BasicAttackInputState attack = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings)); Random = new SeededBattleRandom(seed); this.attack = attack ?? new BasicAttackInputState();
            Battle = (createBattle ?? throw new ArgumentNullException(nameof(createBattle)))(Random) ?? throw new ArgumentException("初期戦闘が必要です");
            Battle.Player.OnTransformationDefenseEnded += () => events.Add("transformation-ended");
            Battle.Transformations.OnPreviewChanged += () => events.Add("transformation-preview:" + (Battle.Transformations.PendingId ?? "-"));
            Battle.Transformations.OnActivationPresentation += () => events.Add("transformation-activated:" + Battle.Transformations.ActiveId);
            Battle.Player.OnHpChanged += (hp, max) => events.Add("player-hp:" + hp);
            foreach (var enemy in Battle.Enemies) {
                enemy.OnHpChanged += (hp, max) => events.Add("enemy-hp:" + enemy.EnemyId + ":" + hp);
                enemy.OnDamaged += (damage, multiplier) => events.Add("enemy-hit:" + damage + ":" + multiplier.ToString("R", CultureInfo.InvariantCulture));
            }
            Battle.OnPlayerAttack += (target, damage) => events.Add("basic:" + target.EnemyId + ":" + damage);
            Battle.CardUsage.OnCardUsed += card => events.Add("card:" + card.Id);
            Battle.OnBattleEnd += result => events.Add("end:" + result);
            if (openInitialCustom) {
                // 製品の初戦カスタム開始と同じ既存手順。通常の再開コマンドにはゲージ条件を適用する。
                Battle.CustomGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);
                var opened = Battle.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Open));
                if (!opened.Accepted) throw new ArgumentException(opened.Reason);
            }
        }
        private static BattleSystem CreateSampleBattle(BattleSimulationSettings settings, IRandomProvider random)
        {
            var player = new PlayerCharacter(settings.PlayerHp, new Vector2Int(0, 0));
            var enemy = new Enemy("sample/enemy", "CLI検証敵", settings.EnemyHp, ElementType.Earth, new Vector2Int(2, 0));
            var deck = new Deck();
            deck.AddCard(new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 20, "外部シナリオ", startupFrames: 2, recoveryFrames: 3));
            deck.AddCard(new Card("heal", "回復", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 5, "外部シナリオ", startupFrames: 1, recoveryFrames: 2));
            deck.Shuffle(random);
            var battle = new BattleSystem(player, new List<Enemy> { enemy }, deck, randomProvider: random);
            battle.ConfigureBasicAttackTiming(new PlayerAttackTiming(2, 3)); battle.CardUsage.EnqueueCards(deck.GetAllCards().ToList());
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, damagePerTick: 1, poisonIntervalFrames: 30));
            battle.Field.ChangePanelType(new Vector2Int(2, 0), PanelType.Grass, new BattlePanelValues(heal: 1));
            return battle;
        }
        public BattleFrameObservation Step(BattleFrameCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (stepping) throw new InvalidOperationException("Stepは再入できません");
            var input = command.Input.Copy();
            stepping = true;
            try {
                StepIndex = checked(StepIndex + 1); events.Clear();
                if (command.UserPaused == true) { Battle.Pause(BattlePauseReason.User); attack.Suspend(); }
                if (command.UserPaused == false) Battle.Resume(BattlePauseReason.User);
                if (command.Custom != null) {
                    // 操作受付は論理フレームとは別。確定/復帰を受け付けたStepでも戦闘時間は進めない。
                    var result = Battle.CustomScreen.Commands.Execute(command.Custom);
                    if (Battle.IsPaused) attack.Suspend();
                    events.AddRange(result.Events); events.Add("custom-state:" + result.Canonical);
                    return new BattleFrameObservation(StepIndex, Battle, Random, attack, events.ToArray());
                }
                if (input.CustomScreenButtonDown) {
                    // 通常戦闘入力の受付拒否は、その論理フレーム自体を失わせない。
                    var result = Battle.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Open));
                    events.AddRange(result.Events);
                }
                int milliseconds = (int)(StepIndex * 1000 / 60 - (StepIndex - 1) * 1000 / 60);
                Battle.RunLogicalFrame(() => {
                    attack.Tick(Battle, input, Settings.ChargeFrames, Settings.IntervalFrames);
                    if (!Battle.IsPaused && input.CardUseButtonDown) Battle.CardUsage.TryUseCurrentCardAsync().Forget();
                    Battle.TickFrame(milliseconds, input);
                });
                return new BattleFrameObservation(StepIndex, Battle, Random, attack, events.ToArray());
            } finally { stepping = false; }
        }
        /// <summary>製品データではなく、同一ソースの再現性を比較する固定入力列。</summary>
        public static BattleFrameCommand SampleCommand(int frame)
        {
            var input = new LogicalInput { HasInputSample = true };
            if (frame == 1) { input.Horizontal = input.HorizontalDown = 1; }
            if (frame == 20 || frame == 30) input.CardUseButtonDown = true;
            if (frame >= 40 && frame < 50 || frame >= 70 && frame < 80) input.BasicAttackButton = true;
            if (frame == 40 || frame == 70) input.BasicAttackButtonDown = true;
            if (frame == 80) input.BasicAttackButtonUp = true;
            if (frame >= 100) {
                if ((frame - 100) % 16 == 0) input.BasicAttackButton = input.BasicAttackButtonDown = true;
                if ((frame - 100) % 16 == 1) input.BasicAttackButtonUp = true;
            }
            return new BattleFrameCommand(input, frame == 50 ? true : frame == 65 ? false : (bool?)null);
        }

        public static string SampleTrace(uint seed = 123, int frames = 240, int batchSize = 1, Action renderWork = null, BasicAttackInputState attack = null)
        {
            if (frames < 1 || frames > 100000 || batchSize < 1) throw new ArgumentOutOfRangeException();
            var simulation = new BattleSimulation(new BattleSimulationSettings(), seed, attack);
            var trace = new System.Text.StringBuilder();
            for (int start = 1; start <= frames; start += batchSize) {
                renderWork?.Invoke();
                for (int frame = start; frame < start + batchSize && frame <= frames; frame++) trace.Append(simulation.Step(SampleCommand(frame)).Canonical).Append('\n');
            }
            return trace.ToString();
        }
    }
}
