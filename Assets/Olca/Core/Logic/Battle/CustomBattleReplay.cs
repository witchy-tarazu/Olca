using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Integration;

namespace Olca.Core.Logic.Battle
{
    /// <summary>製品登録ではない固定fixture。同じ初期定義と明示コマンド列をUnity/CLIから再生する。</summary>
    public static class CustomBattleReplay
    {
        private static Card Sword(int power = 20) => new Card("sword", "ソード", 1, CardRarity.Normal,
            CardCategory.Attack, ElementType.None, CardSeries.Sword, power, "外部fixture", startupFrames: 2, recoveryFrames: 3);
        private static Card Material(string id) => new Card(id, id, 1, CardRarity.Normal,
            CardCategory.Defense, ElementType.None, CardSeries.None, 0, "外部fixture").WithTuningDefinition(
                new TuningGrantDefinition(id, fixedCostAdjustment: -1));
        public static BattleSimulation Create(uint seed = 123)
        {
            var settings = new BattleSimulationSettings();
            return new BattleSimulation(settings, seed, random => {
                var deck = new Deck(new List<Card> { Sword(), Sword(), Sword(21), Material("tune-a"), Material("tune-b"),
                    new Card("bomb", "ボム", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Bomb, 40, "外部fixture") });
                deck.Shuffle(random);
                var enemy = new Enemy("sample/enemy", "外部fixture敵", settings.EnemyHp, ElementType.Earth, new Vector2Int(2, 0));
                var factory = new FixtureFactory();
                return new BattleSystem(new PlayerCharacter(settings.PlayerHp, new Vector2Int(0, 0)), new List<Enemy> { enemy }, deck,
                    randomProvider: random, exceedSystem: new ExceedSystem(factory), tuningSystem: new TuningSystem(),
                    fusionSystem: new FusionSystem(new FixtureRecipes(), factory));
            }, true);
        }
        // 能力計算は行わず、この比較にだけ用いる明示定義を返す。製品のレシピ表には追加しない。
        private sealed class FixtureFactory : ICardFactory, IExceededCardDefinitionProvider
        {
            public Card CreateById(string id) => id == "fire_sword" ? new Card("fire_sword", "ファイアソード", 2, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.Sword, 60, "外部fixture", startupFrames: 2, recoveryFrames: 3) : null;
            public bool HasExceededDefinition(Card card, ExceedLevel level) => card.Id == "sword" && level == ExceedLevel.Lv1;
            public Card CreateExceeded(Card card, ExceedLevel level) => HasExceededDefinition(card, level) ? new Card("sword_lv1", "ソード★", 1,
                CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "外部fixture", exceedLevel: level) : null;
        }
        private sealed class FixtureRecipes : IFusionRecipeProvider
        {
            public string FindResultCardId(IReadOnlyList<string> ids) => ids.Count == 2 && ids.Contains("sword") && ids.Contains("bomb") ? "fire_sword" : null;
            public bool HasRecipeContaining(IReadOnlyList<string> ids) => ids.All(id => id == "sword" || id == "bomb");
        }

        /// <summary>初期観測から個体を指定し、以降は固定された同じコマンドをそのまま再生する。</summary>
        public static IReadOnlyList<BattleFrameCommand> Commands(uint seed = 123)
        {
            var simulation = Create(seed);
            var hand = simulation.Battle.CustomScreen.CurrentHand;
            long[] swords = hand.Where(c => c.Id == "sword" && c.Power == 20).Select(simulation.Battle.CustomScreen.Commands.HandleOf).ToArray();
            long other = simulation.Battle.CustomScreen.Commands.HandleOf(hand.Single(c => c.Id == "sword" && c.Power == 21));
            long a = simulation.Battle.CustomScreen.Commands.HandleOf(hand.Single(c => c.Id == "tune-a"));
            long b = simulation.Battle.CustomScreen.Commands.HandleOf(hand.Single(c => c.Id == "tune-b"));
            long bomb = simulation.Battle.CustomScreen.Commands.HandleOf(hand.Single(c => c.Id == "bomb"));
            var commands = new List<BattleFrameCommand>();
            void Add(CustomCommandKind kind, params long[] ids) {
                var command = new BattleFrameCommand(custom: new CustomScreenCommand(kind, ids)); commands.Add(command); simulation.Step(command);
            }
            long Find(Func<Card, bool> condition) => simulation.Battle.CustomScreen.Commands.HandleOf(simulation.Battle.CustomScreen.CurrentHand.First(condition));
            Add(CustomCommandKind.Open); // 二重開始
            Add(CustomCommandKind.ReturnToBattle); // 未確定
            Add(CustomCommandKind.Select, swords[0]); Add(CustomCommandKind.Select, bomb);
            Add(CustomCommandKind.Deselect, swords[0]); // 末尾以外
            Add(CustomCommandKind.ExecuteCustomization, swords[0], a); // 使用予定は素材不可
            Add(CustomCommandKind.Deselect, bomb); Add(CustomCommandKind.Deselect, swords[0]);
            Add(CustomCommandKind.PreviewCustomization, swords[0], swords[0]); // 同じ個体二重指定
            Add(CustomCommandKind.PreviewCustomization, swords[0], other); // 同ID別定義
            Add(CustomCommandKind.PreviewCustomization, swords[0], a); Add(CustomCommandKind.CancelCustomization);
            Add(CustomCommandKind.ExecuteCustomization, swords[0], a);
            Add(CustomCommandKind.ExecuteCustomization, swords[0], a); // 消費済み再実行
            Add(CustomCommandKind.ExecuteCustomization, Find(c => c.Id == "sword" && c.TuningCount == 1), b);
            Add(CustomCommandKind.Select, Find(c => c.Id == "sword" && c.TuningCount == 2)); Add(CustomCommandKind.Select, bomb);
            Add(CustomCommandKind.Undo); // 結果以降を解除
            Add(CustomCommandKind.Undo); // 元素材復元。復元された新参照も観測ハンドルから指定する。
            long restored = Find(c => c.Id == "sword" && c.Power == 20 && simulation.Battle.CustomScreen.Commands.HandleOf(c) != swords[1]);
            Add(CustomCommandKind.ExecuteCustomization, restored, swords[1]);
            Add(CustomCommandKind.Undo);
            Add(CustomCommandKind.ExecuteCustomization, Find(c => c.Id == "sword" && c.Power == 20), bomb);
            Add(CustomCommandKind.Undo);
            Add(CustomCommandKind.ExecuteCustomization, Find(c => c.Id == "sword" && c.Power == 20), Find(c => c.Id == "tune-a"));
            Add(CustomCommandKind.Select, Find(c => c.Id == "sword" && c.TuningCount == 1)); Add(CustomCommandKind.Confirm); Add(CustomCommandKind.Confirm);
            commands.Add(new BattleFrameCommand()); // 確定演出待ちも時間停止
            Add(CustomCommandKind.ReturnToBattle); Add(CustomCommandKind.ReturnToBattle);
            Add(CustomCommandKind.Open); // ゲージ未充填
            commands.Add(new BattleFrameCommand(new LogicalInput { Horizontal = 1, HorizontalDown = 1, HasInputSample = true }));
            for (int i = 0; i < 12; i++) commands.Add(new BattleFrameCommand());
            commands.Add(new BattleFrameCommand(new LogicalInput { CardUseButtonDown = true, HasInputSample = true }));
            for (int i = 0; i < 12; i++) commands.Add(new BattleFrameCommand());
            return commands.AsReadOnly();
        }
        public static string Trace(uint seed = 123, int batch = 1, Action renderWork = null)
        {
            if (batch < 1) throw new ArgumentOutOfRangeException(nameof(batch));
            var simulation = Create(seed); var commands = Commands(seed); var trace = new StringBuilder();
            for (int start = 0; start < commands.Count; start += batch) {
                renderWork?.Invoke();
                for (int i = start; i < Math.Min(commands.Count, start + batch); i++) trace.Append(simulation.Step(commands[i]).Canonical).Append('\n');
            }
            return trace.ToString();
        }
    }
}
