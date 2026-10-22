using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Olca.Core.Domain;
using Olca.Core.Logic.Integration;

namespace Olca.Core.Logic.Battle
{
    /// <summary>既存の変身/防御/攻撃fixture値による固定比較。製品登録や解放条件を追加しない。</summary>
    public static class TransformationBattleReplay
    {
        public static BattleSimulation Create(uint seed = 123, bool weakness = true, BasicAttackInputState attack = null)
        {
            var settings = new BattleSimulationSettings();
            return new BattleSimulation(settings, seed, random => {
                Card Sword(string id) => new Card(id, id, 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 20, "外部fixture");
                Card Material(string id) => new Card(id, id, 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 0, "外部fixture")
                    .WithTuningDefinition(new TuningGrantDefinition(id, fixedCostAdjustment: -1));
                var deck = new Deck(new List<Card> { Sword("sword"), Sword("sword"), Material("tune-a"), Material("tune-b") });
                for (int i = 0; i < 8; i++) deck.AddCard(new Card("heal", "回復", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 5, "外部fixture"));
                deck.Shuffle(random);
                var player = new PlayerCharacter(100, new Vector2Int(0, 0));
                var battle = new BattleSystem(player, new List<Enemy> { new Enemy("sample/enemy", "外部fixture敵", 120, ElementType.Earth, new Vector2Int(2, 0)) },
                    deck, randomProvider: random, tuningSystem: new TuningSystem());
                battle.Transformations.Configure(new[] {
                    new TransformationDefenseDefinition("fixture-a", new ElementResistances(fire:50), ElementType.Fire, 2,
                        new[] { new TransformationCardConversion("sword", Sword("converted")) }, new TransformationAttackDefinition(ElementType.Water,150,300,3,3,new PlayerAttackTiming(1,1))),
                    new TransformationDefenseDefinition("fixture-b", ElementResistances.Neutral, ElementType.Water, 1,
                        new[] { new TransformationCardConversion("converted", Sword("second")) }, new TransformationAttackDefinition(ElementType.Fire,125,250,4,4,new PlayerAttackTiming(1,1))) });
                battle.Transformations.EnablePresentation(); battle.Transformations.ShortPresentation = true;
                if (weakness) {
                    player.SetBarrier(20);
                    battle.AddSummon(new Enemy("sample/fire", "外部fixture弱点攻撃", 120, ElementType.Fire, new Vector2Int(1, 0), isSummoned:true),
                        new List<IEnemyAIAction> { new BasicAttackAction(10,1000,attackElement:ElementType.Fire) });
                }
                return battle;
            }, true, attack);
        }
        public static IReadOnlyList<BattleFrameCommand> Commands(uint seed = 123, bool weakness = true)
        {
            var simulation = Create(seed, weakness); var screen = simulation.Battle.CustomScreen; var commands = new List<BattleFrameCommand>();
            void Add(BattleFrameCommand command) { commands.Add(command); simulation.Step(command); }
            void Command(CustomCommandKind kind, params long[] cards) => Add(new BattleFrameCommand(custom:new CustomScreenCommand(kind,cards)));
            void Form(string id) => Add(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.PreviewTransformation,id)));
            void Frames(int count) { for(int i=0;i<count;i++)Add(new BattleFrameCommand()); }
            long Find(string id, int tuning = -1) => screen.Commands.HandleOf(screen.CurrentHand.First(c=>c.Id==id&&(tuning<0||c.TuningCount==tuning)));
            Form("missing"); Form("fixture-a");
            Command(CustomCommandKind.Select,Find("converted")); Command(CustomCommandKind.CancelTransformation); // 選択解除だけ
            Command(CustomCommandKind.ExecuteCustomization,Find("converted",0),Find("tune-a"));
            Command(CustomCommandKind.ExecuteCustomization,Find("converted",1),Find("tune-b"));
            Command(CustomCommandKind.CancelTransformation); // 変換由来の加工連鎖/追加ドローを復元
            Form("fixture-a"); Command(CustomCommandKind.Confirm); Command(CustomCommandKind.Confirm); Command(CustomCommandKind.ReturnToBattle);
            Add(new BattleFrameCommand(userPaused:true)); Frames(5); Add(new BattleFrameCommand(userPaused:false)); Frames(12);
            if (weakness) {
                Frames(140); Command(CustomCommandKind.CancelTransformation); // 継続/終了済み変身の任意解除は不可
                Form("fixture-a"); // 閉画面/使用済みは再使用不可
            } else {
                // 持続回数を進めるため、通常の600フレーム充填を待つ。ゲージの値は改変しない。
                for (int phase=0;phase<3;phase++) {
                    while(!simulation.Battle.CustomGauge.IsFull)Add(new BattleFrameCommand());
                    Command(CustomCommandKind.Open);
                    if(phase==0){Form("fixture-b");Command(CustomCommandKind.CancelTransformation);}
                    Form("fixture-a"); // 使用済みを拒否、元変身の回数を消費し直さない
                    Command(CustomCommandKind.Confirm);Command(CustomCommandKind.ReturnToBattle);
                }
            }
            return commands.AsReadOnly();
        }
        public static string Trace(uint seed = 123, int batch = 1, Action renderWork = null)
        {
            if(batch<1)throw new ArgumentOutOfRangeException(nameof(batch));
            var output=new StringBuilder();
            foreach(bool weakness in new[]{true,false}){
                var simulation=Create(seed,weakness);var commands=Commands(seed,weakness);
                for(int start=0;start<commands.Count;start+=batch){renderWork?.Invoke();
                    for(int i=start;i<Math.Min(commands.Count,start+batch);i++)output.Append(weakness?"weakness|":"expiry|").Append(simulation.Step(commands[i]).Canonical).Append('\n');}
            }
            return output.ToString();
        }
    }
}
