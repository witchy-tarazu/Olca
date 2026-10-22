using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using System.Linq;

namespace Olca.Tests.CardTests
{
    /// <summary>
    /// CustomScreenSystemのテスト
    /// </summary>
    [TestFixture]
    public class CustomScreenSystemTests
    {
        private static Card StageMaterial(string id, bool missingSecond = false)
        {
            var stages = new Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 3 } } };
            if (!missingSecond) stages.Add(2, new CardModifier { BonusDamage = 8 });
            return ZeroCostCard(id, material: true).WithTuningDefinition(new TuningGrantDefinition(id,
                new[] { new TuningEffectDefinition(id + ":bonus", 2, stages) }, fixedCostAdjustment: 1));
        }
        private static CustomScreenSystem StageScreen(params Card[] cards)
        {
            var cs = new CustomScreenSystem(new Deck(cards.ToList()), maxHandSize:20,maxCost:100,tuningSystem:new TuningSystem());
            cs.Open(new Card[0],new MockRandomProvider(0));return cs;
        }

        [Test]
        public void 段階プレビューと確定は一致し二回Undoで回数履歴効果を段階ごと戻す()
        {
            var target=ZeroCostCard("target",4);var a=StageMaterial("a");var b=StageMaterial("b");var cs=StageScreen(target,a,b);
            var initial=cs.ExecuteCustomization(new[]{target,a});Assert.IsTrue(initial.CanExecute, initial.Reason);
            var first=cs.CurrentHand.Single(c=>c.Id=="target");var hand=cs.CurrentHand.ToArray();
            var preview=cs.PreviewCustomization(new[]{first,b});Assert.IsTrue(preview.CanExecute, preview.Reason);
            Assert.AreEqual(target.GetFinalPower()+16,preview.ResultCard.GetFinalPower());CollectionAssert.AreEqual(hand,cs.CurrentHand);
            var second=cs.ExecuteCustomization(new[]{first,b}).ResultCard;Assert.AreEqual(preview.ResultCard.GetFinalPower(),second.GetFinalPower());
            Assert.AreEqual(2,second.TuningHistory.Count);Assert.AreEqual(6,second.GetFinalCost());
            Assert.IsTrue(cs.TryUndoCustomization());var restored=cs.CurrentHand.Single(c=>c.Id=="target");
            Assert.AreEqual(1,restored.TuningCount);Assert.AreEqual(1,restored.TuningHistory.Count);Assert.AreEqual(target.GetFinalPower()+3,restored.GetFinalPower());
            Assert.IsTrue(cs.TryUndoCustomization());restored=cs.CurrentHand.Single(c=>c.Id=="target");
            Assert.AreEqual(0,restored.TuningCount);Assert.IsEmpty(restored.TuningHistory);Assert.AreEqual(target.GetFinalPower(),restored.GetFinalPower());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 必要段階未定義はプレビュー通常確定直接確定の全経路で無消費(bool oldHistory)
        {
            var target=ZeroCostCard("target",4);var first=StageMaterial("first",true);var next=StageMaterial("next");
            target=oldHistory?target.ApplyTuning(first):new TuningSystem().Execute(target,first).ResultCard;
            var other=ZeroCostCard("other",1);var cs=StageScreen(target,next,other);cs.TrySelectCard(other);
            var hand=cs.CurrentHand.ToArray();var selection=cs.SelectedCards.ToArray();int notices=0;cs.OnTuned+=_=>notices++;
            var preview=cs.PreviewCustomization(new[]{target,next});Assert.AreEqual(CustomizationPreviewStatus.Undefined,preview.Status);Assert.IsNull(preview.ResultCard);
            Assert.AreEqual(CustomizationPreviewStatus.Undefined,cs.ExecuteCustomization(new[]{target,next}).Status);
            var direct=cs.ExecuteTuning(target,next);Assert.IsTrue(direct.IsUndefined);Assert.IsNull(direct.ResultCard);
            CollectionAssert.AreEqual(hand,cs.CurrentHand);CollectionAssert.AreEqual(selection,cs.SelectedCards);
            Assert.AreEqual(1,target.TuningCount);Assert.AreEqual(0,cs.CustomizationUndoCount);Assert.AreEqual(0,notices);
        }

        [TestCase(2, -8, false, 1)]
        [TestCase(2, 3, false, 9)]
        [TestCase(2, -2, false, 4)]
        [TestCase(2, 100, true, 0)]
        public void 固定コストのみの再調律は非破壊プレビューと確定が一致しUndoで回数も戻る(int first, int second, bool zero, int expected)
        {
            var target = ZeroCostCard("target", 4);
            var a = ZeroCostCard("a", material: true); a.Modifier.CostAdjustment = first;
            var b = ZeroCostCard("b", material: true); b.Modifier.CostAdjustment = second; b.Modifier.ZeroCost = zero;
            var cs = UndoScreen(target, a, b);
            Assert.IsTrue(cs.ExecuteCustomization(new[] { target, a }).CanExecute);
            var once = cs.CurrentHand.Single(c => c.Id == "target"); var hand = cs.CurrentHand.ToArray();
            var preview = cs.PreviewCustomization(new[] { once, b });
            Assert.IsTrue(preview.CanExecute, preview.Reason); Assert.AreEqual(expected, preview.ResultCard.GetFinalCost());
            CollectionAssert.AreEqual(hand, cs.CurrentHand); Assert.AreEqual(1, cs.CustomizationUndoCount);
            Assert.AreEqual(1, once.TuningCount); Assert.IsFalse(once.HasStageDependentTuning);
            var confirmed = cs.ExecuteCustomization(new[] { b, once }); Assert.IsTrue(confirmed.CanExecute);
            Assert.AreEqual(expected, confirmed.ResultCard.GetFinalCost()); Assert.AreEqual(2, confirmed.ResultCard.TuningCount);
            Assert.IsFalse(confirmed.ResultCard.HasStageDependentTuning); Assert.IsTrue(cs.TryUndoCustomization());
            var restored = cs.CurrentHand.Single(c => c.Id == "target"); Assert.AreEqual(1, restored.TuningCount);
            Assert.AreEqual(4 + first, restored.GetFinalCost()); Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(0, cs.CurrentHand.Single(c => c.Id == "target").TuningCount);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 固定コストと段階対象を混ぜる再調律は過去と追加どちらに段階対象があっても未定義(bool stageFirst)
        {
            var target = ZeroCostCard("target", 4);
            var cost = ZeroCostCard("cost", material: true); cost.Modifier.CostAdjustment = 2;
            var stage = ZeroCostCard("stage", material: true); stage.Modifier.BonusDamage = 7;
            var cs = UndoScreen(target, cost, stage);
            var once = cs.ExecuteTuning(target, stageFirst ? stage : cost).ResultCard;
            var second = stageFirst ? cost : stage;
            var preview = cs.PreviewCustomization(new[] { once, second });
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, preview.Status); Assert.IsNull(preview.ResultCard);
            Assert.IsFalse(cs.ExecuteCustomization(new[] { once, second }).CanExecute);
            Assert.AreEqual(1, cs.CustomizationUndoCount);
        }

        [Test]
        public void 段階対象の合算値が相殺されても履歴を保持して再調律を許可しない()
        {
            var a = ZeroCostCard("a", material: true); a.Modifier.BonusDamage = 7;
            var b = ZeroCostCard("b", material: true); b.Modifier.BonusDamage = -7;
            var target = ZeroCostCard("target", 4).ApplyTuning(a).ApplyTuning(b).Clone();
            Assert.AreEqual(0, target.Modifier.BonusDamage); Assert.IsTrue(target.HasStageDependentTuning);
            var cost = ZeroCostCard("cost", material: true); cost.Modifier.CostAdjustment = 1;
            var cs = UndoScreen(target, cost);
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, cs.PreviewCustomization(new[] { target, cost }).Status);
        }

        [Test]
        public void 元のカード固有効果は調律付与履歴と区別して固定コストの再調律を許可する()
        {
            var target = ZeroCostCard("target", 4);
            target.Modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Poison, Power = 2, Duration = 4 });
            var a = ZeroCostCard("a", material: true); a.Modifier.CostAdjustment = 1;
            var b = ZeroCostCard("b", material: true); b.Modifier.CostAdjustment = 1;
            var cs = UndoScreen(target, a, b); var once = cs.ExecuteTuning(target, a).ResultCard;
            var preview = cs.PreviewCustomization(new[] { once, b });
            Assert.IsTrue(preview.CanExecute, preview.Reason); Assert.AreEqual(6, preview.ResultCard.GetFinalCost());
            Assert.AreEqual(4, preview.ResultCard.Modifier.AdditionalEffects.Single().Duration);
            Assert.IsFalse(preview.ResultCard.HasStageDependentTuning);
        }

        private CustomScreenSystem PreviewScreen(out Deck deck, out Card[] cards, out MockCardFactory factory,
            out MockFusionRecipeProvider recipes)
        {
            var attack = ZeroCostCard("sword", 4);
            var same = attack.Clone();
            var material = ZeroCostCard("tune", material: true);
            material.Modifier.CostAdjustment = -2;
            material.Modifier.BonusDamage = 7;
            material.Modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Poison, Power = 2, Duration = 4 });
            cards = new[] { attack, same, material, ZeroCostCard("bomb", 3), ZeroCostCard("other", 2) };
            deck = new Deck(cards.ToList());
            factory = new MockCardFactory(); recipes = new MockFusionRecipeProvider();
            factory.Register("sword_lv1", new Card("sword_lv1", "定義済み上位", 5, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.Sword, 70, "テスト定義", exceedLevel: ExceedLevel.Lv1));
            factory.Register("fusion", ZeroCostCard("fusion", 6));
            recipes.Register("fusion", "sword", "bomb");
            var cs = new CustomScreenSystem(deck, exceedSystem: new ExceedSystem(factory),
                tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier), fusionSystem: new FusionSystem(recipes, factory));
            cs.Open(new Card[0], new MockRandomProvider(0));
            return cs;
        }

        [TestCase(CustomizationOperation.Exceed, 1)]
        [TestCase(CustomizationOperation.Tuning, 2)]
        [TestCase(CustomizationOperation.Fusion, 3)]
        public void 加工プレビューを繰り返しても素材デッキ選択履歴通知を変えず確定結果と一致する(CustomizationOperation operation, int second)
        {
            var cs = PreviewScreen(out var deck, out var cards, out var factory, out var recipes);
            cs.TrySelectCard(cards[4]);
            var hand = cs.CurrentHand.ToArray(); var drawn = deck.RemainingCount;
            int notices = 0; cs.OnExceeded += _ => notices++; cs.OnTuned += _ => notices++; cs.OnFused += _ => notices++;
            CustomizationPreview preview = null;
            for (int i = 0; i < 4; i++) preview = cs.PreviewCustomization(new[] { cards[0], cards[second] });
            Assert.IsTrue(preview.CanExecute, preview.Reason); Assert.AreEqual(operation, preview.Operation);
            CollectionAssert.AreEqual(hand, cs.CurrentHand); CollectionAssert.AreEqual(new[] { cards[4] }, cs.SelectedCards);
            Assert.AreEqual(5, deck.TotalCount); Assert.AreEqual(drawn, deck.RemainingCount);
            Assert.AreEqual(0, cs.CustomizationUndoCount); Assert.AreEqual(0, notices); Assert.IsEmpty(cs.AvailableCards);
            Assert.AreEqual(0, cards[0].TuningCount); Assert.AreEqual(0, cards[0].Modifier.BonusDamage);
            Assert.AreEqual(-2, cards[2].Modifier.CostAdjustment); Assert.AreEqual(4, cards[2].Modifier.AdditionalEffects[0].Duration);
            var confirmed = cs.ExecuteCustomization(new[] { cards[0], cards[second] });
            Assert.IsTrue(confirmed.CanExecute); Assert.AreEqual(1, notices); Assert.AreEqual(1, cs.CustomizationUndoCount);
            Assert.AreEqual(preview.ResultCard.Id, confirmed.ResultCard.Id);
            Assert.AreEqual(preview.ResultCard.Name, confirmed.ResultCard.Name);
            Assert.AreEqual(preview.ResultCard.GetFinalCost(), confirmed.ResultCard.GetFinalCost());
            Assert.AreEqual(preview.ResultCard.GetFinalPower(), confirmed.ResultCard.GetFinalPower());
            Assert.AreEqual(preview.ResultCard.TuningCount, confirmed.ResultCard.TuningCount);
            Assert.AreEqual(preview.ResultCard.ExceedLevel, confirmed.ResultCard.ExceedLevel);
            Assert.AreEqual(preview.ResultCard.Modifier.AdditionalEffects.Count, confirmed.ResultCard.Modifier.AdditionalEffects.Count);
            if (operation == CustomizationOperation.Tuning)
            {
                var e = confirmed.ResultCard.Modifier.AdditionalEffects.Single();
                Assert.AreEqual(AdditionalEffectType.Poison, e.EffectType); Assert.AreEqual(2, e.Power); Assert.AreEqual(4, e.Duration);
            }
        }

        [Test]
        public void プレビュー結果の編集は素材登録定義と次回結果へ漏れない()
        {
            var cs = PreviewScreen(out _, out var cards, out var factory, out _);
            var tuned = cs.PreviewCustomization(new[] { cards[2], cards[0] });
            tuned.ResultCard.Modifier.AdditionalEffects[0].Duration = 99;
            tuned.ResultCard.Modifier.CostAdjustment = 99;
            var next = cs.PreviewCustomization(new[] { cards[0], cards[2] });
            Assert.AreEqual(4, next.ResultCard.Modifier.AdditionalEffects[0].Duration);
            Assert.AreEqual(2, next.ResultCard.GetFinalCost()); Assert.AreEqual(4, cards[0].GetFinalCost());
            var fused = cs.PreviewCustomization(new[] { cards[0], cards[3] }); fused.ResultCard.Modifier.BonusDamage = 999;
            Assert.AreEqual(0, factory.CreateById("fusion").Modifier.BonusDamage);
        }

        [Test]
        public void 未登録上位は既存ファクトリの仮性能をプレビューせず確定も素材を消費しない()
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var a = factory.CreateById("bomb"); var b = a.Clone(); var deck = new Deck(new List<Card> { a, b });
            var cs = new CustomScreenSystem(deck, exceedSystem: new ExceedSystem(factory)); cs.Open(new Card[0], random);
            var preview = cs.PreviewCustomization(new[] { a, b });
            Assert.AreEqual(CustomizationOperation.Exceed, preview.Operation);
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, preview.Status); Assert.IsNull(preview.ResultCard);
            Assert.IsFalse(cs.ExecuteCustomization(new[] { a, b }).CanExecute); Assert.AreEqual(2, cs.CurrentHand.Count);
            Assert.AreEqual(0, cs.CustomizationUndoCount);
        }

        [Test]
        public void 再調律は未定義段階として分け既存Undoを変更せず元に戻せる()
        {
            var cs = PreviewScreen(out _, out var cards, out _, out _);
            var result = cs.ExecuteTuning(cards[0], cards[2]).ResultCard;
            Assert.AreEqual(1, result.TuningCount); Assert.AreEqual(1, result.Clone().TuningCount);
            var source = ZeroCostCard("second-tune", material: true); source.Modifier.BonusDamage = 4;
            // 別カードは手札にないため先に所有権で拒否する。
            Assert.AreEqual(CustomizationPreviewStatus.Invalid, cs.PreviewCustomization(new[] { result, source }).Status);
            var nextScreen = UndoScreen(result, source);
            var preview = nextScreen.PreviewCustomization(new[] { result, source });
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, preview.Status); Assert.IsNull(preview.ResultCard);
            Assert.IsTrue(preview.Reason.Contains("段階表")); Assert.AreEqual(1, cs.CustomizationUndoCount);
            Assert.IsTrue(cs.TryUndoCustomization()); Assert.AreEqual(0, cs.CurrentHand.First(c => c.Id == cards[0].Id && c.TuningCount == 0).TuningCount);
        }

        [Test]
        public void 空の付与効果と融合先不足は実行不可レシピとは区別する()
        {
            var cs = PreviewScreen(out _, out var cards, out _, out var recipes);
            cards[2].Modifier.CostAdjustment = 0; cards[2].Modifier.BonusDamage = 0; cards[2].Modifier.AdditionalEffects.Clear();
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, cs.PreviewCustomization(new[] { cards[0], cards[2] }).Status);
            recipes.Register("missing", "sword", "other");
            var missing = cs.PreviewCustomization(new[] { cards[0], cards[4] });
            Assert.AreEqual(CustomizationPreviewStatus.Undefined, missing.Status); Assert.IsNull(missing.ResultCard);
            var invalid = cs.PreviewCustomization(new[] { cards[3], cards[4] });
            Assert.AreEqual(CustomizationPreviewStatus.Invalid, invalid.Status); Assert.IsNotEmpty(invalid.Reason);
        }

        [Test]
        public void 同一素材の二重指定と使用予定カードと画面確定後はプレビュー不可()
        {
            var cs = PreviewScreen(out _, out var cards, out _, out _);
            Assert.IsFalse(cs.PreviewCustomization(new[] { cards[0], cards[0] }).CanExecute);
            Assert.IsFalse(cs.PreviewCustomization(new[] { cards[0] }).CanExecute);
            Assert.IsFalse(cs.PreviewCustomization(null).CanExecute);
            cs.TrySelectCard(cards[0]);
            Assert.IsFalse(cs.PreviewCustomization(new[] { cards[0], cards[1] }).CanExecute);
            cs.Confirm(); Assert.IsFalse(cs.PreviewCustomization(new[] { cards[3], cards[4] }).CanExecute);
        }

        [Test]
        public void 表示後に素材が使用予定へ移った場合は再検証し古い結果を確定しない()
        {
            var cs = PreviewScreen(out _, out var cards, out _, out _);
            Assert.IsTrue(cs.PreviewCustomization(new[] { cards[0], cards[3] }).CanExecute);
            cs.TrySelectCard(cards[3]);
            Assert.IsFalse(cs.ExecuteCustomization(new[] { cards[0], cards[3] }).CanExecute);
            Assert.AreEqual(0, cs.CustomizationUndoCount); Assert.Contains(cards[0], cs.CurrentHand.ToList());
        }

        [TestCase(4, 2, false, 6)]
        [TestCase(4, -2, false, 2)]
        [TestCase(4, -8, false, 1)]
        [TestCase(4, 0, false, 4)]
        [TestCase(0, 0, false, 0)]
        [TestCase(0, 2, false, 2)]
        [TestCase(0, -2, false, 1)]
        [TestCase(4, 2, true, 0)]
        [TestCase(4, -8, true, 0)]
        [TestCase(0, 2, true, 0)]
        public void 調律コストは定義値と増減を分け通常最低1と専用0優先を適用する(int baseCost, int adjustment, bool zero, int expected)
        {
            var card = ZeroCostCard("c", baseCost, zero);
            card.Modifier.CostAdjustment = adjustment;
            Assert.AreEqual(expected, card.GetFinalCost());
            Assert.AreEqual(baseCost, card.Cost);
        }

        [Test]
        public void 複数調律は定義増減量を合算してから最低値を適用し複製や元カードを共有しない()
        {
            var target = ZeroCostCard("target", 4);
            var down = ZeroCostCard("down", material: true); down.Modifier.CostAdjustment = -8;
            var up = ZeroCostCard("up", material: true); up.Modifier.CostAdjustment = 7;
            var first = target.ApplyTuning(down);
            var second = first.ApplyTuning(up);
            Assert.AreEqual(1, first.GetFinalCost()); Assert.AreEqual(-8, first.Modifier.CostAdjustment);
            Assert.AreEqual(3, second.GetFinalCost()); Assert.AreEqual(-1, second.Modifier.CostAdjustment);
            Assert.AreEqual(3, target.ApplyTuning(up).ApplyTuning(down).GetFinalCost());
            Assert.AreEqual(0, target.Modifier.CostAdjustment); Assert.AreEqual(4, target.GetFinalCost());
            var clone = second.Clone(); clone.Modifier.CostAdjustment = 10;
            Assert.AreEqual(-1, second.Modifier.CostAdjustment);
            Assert.AreEqual(0, second.ApplyTuning(ZeroCostCard("zero", dedicated: true)).GetFinalCost());
            Assert.AreEqual(0, target.ApplyTuning(down).ApplyTuning(up).ApplyTuning(ZeroCostCard("zero", dedicated: true)).ApplyTuning(up).GetFinalCost());
        }

        [Test]
        public void 増減合計0は元の定義コストへ戻り基礎0と専用0の区別を維持する()
        {
            var target = ZeroCostCard("base-zero", 0);
            var up = ZeroCostCard("up", material: true); up.Modifier.CostAdjustment = 2;
            var down = ZeroCostCard("down", material: true); down.Modifier.CostAdjustment = -2;
            var result = target.ApplyTuning(up).ApplyTuning(down);
            Assert.AreEqual(0, result.Modifier.CostAdjustment);
            Assert.AreEqual(0, result.GetFinalCost()); Assert.IsFalse(result.Modifier.ZeroCost);
        }

        [Test]
        public void 調律コストの選択と末尾解除と複数加工Undoで各段階の増減量を復元する()
        {
            var target = ZeroCostCard("target", 4);
            var up = ZeroCostCard("up", material: true); up.Modifier.CostAdjustment = 2;
            var down = ZeroCostCard("down", material: true); down.Modifier.CostAdjustment = -8;
            var next = ZeroCostCard("next", 2);
            var cs = UndoScreen(target, up, down, next);
            var first = cs.ExecuteTuning(target, up).ResultCard;
            var second = cs.ExecuteTuning(first, down).ResultCard;
            cs.TrySelectCard(second); cs.TrySelectCard(next);
            Assert.AreEqual(2, cs.GetTotalUsedCost());
            Assert.IsFalse(cs.TryDeselectCard(second));
            Assert.IsTrue(cs.TryDeselectCard(next)); Assert.AreEqual(1, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TrySelectCard(next));
            Assert.IsTrue(cs.TryUndoCustomization()); Assert.AreEqual(0, cs.GetTotalUsedCost());
            var restored = cs.CurrentHand.Single(c => c.Id == "target");
            Assert.AreEqual(2, restored.Modifier.CostAdjustment); Assert.AreEqual(6, restored.GetFinalCost());
            Assert.IsTrue(cs.TryUndoCustomization());
            restored = cs.CurrentHand.Single(c => c.Id == "target");
            Assert.AreEqual(0, restored.Modifier.CostAdjustment); Assert.AreEqual(4, restored.GetFinalCost());
        }

        [Test]
        public void 調律コストを選択上限と確定キューへ反映し確定後Undoしない()
        {
            var target = ZeroCostCard("target", 2);
            var up = ZeroCostCard("up", material: true); up.Modifier.CostAdjustment = 3;
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy>(),
                new Deck(new List<Card> { target, up }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            battle.SetFirstStrike(); battle.OpenCustomScreen(random);
            var cs = battle.CustomScreen; cs.SetMaxCost(4);
            var tuned = cs.ExecuteTuning(target, up).ResultCard;
            Assert.IsFalse(cs.TrySelectCard(tuned)); Assert.AreEqual(0, cs.GetTotalUsedCost());
            cs.SetMaxCost(5); Assert.IsTrue(cs.TrySelectCard(tuned)); Assert.AreEqual(5, cs.GetTotalUsedCost());
            cs.Confirm(); Assert.AreSame(tuned, battle.CardUsage.CurrentCard);
            Assert.AreEqual(5, battle.CardUsage.CurrentCard.GetFinalCost());
            Assert.AreEqual(3, battle.CardUsage.CurrentCard.Modifier.CostAdjustment);
            Assert.IsFalse(cs.TryUndoCustomization()); Assert.AreEqual(2, target.GetFinalCost());
        }

        private static Card ZeroCostCard(string id, int cost = 2, bool dedicated = false, bool material = false)
            => new Card(id, id, cost, CardRarity.Normal, material ? CardCategory.Tuning : CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "試験", modifier: new CardModifier { ZeroCost = dedicated });

        [TestCase(false, 3)]
        [TestCase(true, 2)]
        public void 専用0化だけが同系統割引の最低1より優先し元から0のカードと区別される(bool dedicated, int expected)
        {
            var prefix = ZeroCostCard("prefix");
            var zero = ZeroCostCard("zero", 0, dedicated);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { prefix, zero }));
            cs.Open(new Card[0], random);
            Assert.IsTrue(cs.TrySelectCard(prefix));
            Assert.IsTrue(cs.TrySelectCard(zero));
            Assert.AreEqual(expected, cs.GetTotalUsedCost());
            Assert.AreEqual(0, zero.Cost);
            Assert.AreEqual(dedicated, zero.Modifier.ZeroCost);
        }

        [Test]
        public void 専用0化カードを先頭に選んでも次の同系統割引に数え末尾解除と再選択で再計算する()
        {
            var zero = ZeroCostCard("zero", 7, true); var next = ZeroCostCard("next");
            var cs = new CustomScreenSystem(new Deck(new List<Card> { zero, next }), maxCost: 1);
            cs.Open(new Card[0], random);
            Assert.IsTrue(cs.TrySelectCard(zero)); Assert.AreEqual(0, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TrySelectCard(next)); Assert.AreEqual(1, cs.GetTotalUsedCost());
            Assert.IsFalse(cs.TryDeselectCard(zero));
            Assert.IsTrue(cs.TryDeselectCard(next)); Assert.AreEqual(0, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TryDeselectCard(zero));
            Assert.IsFalse(cs.TrySelectCard(next), "0化カードを外せば先頭の通常コスト2は上限1を超える");
            Assert.IsTrue(cs.TrySelectCard(zero)); Assert.IsTrue(cs.TrySelectCard(next));
            Assert.AreEqual(1, cs.GetTotalUsedCost()); Assert.AreEqual(7, zero.Cost);
        }

        [Test]
        public void 専用0化調律のUndoで素材と基礎コストを戻し選択済み結果以降を解除する()
        {
            var target = ZeroCostCard("target", 4); var material = ZeroCostCard("material", 1, true, true);
            var prefix = ZeroCostCard("prefix"); var suffix = ZeroCostCard("suffix");
            var cs = UndoScreen(target, material, prefix, suffix);
            cs.TrySelectCard(prefix);
            var tuned = cs.ExecuteTuning(target, material).ResultCard;
            Assert.NotNull(tuned); Assert.AreEqual(0, tuned.GetFinalCost()); Assert.AreEqual(4, tuned.Cost);
            Assert.IsFalse(target.Modifier.ZeroCost);
            cs.TrySelectCard(tuned); cs.TrySelectCard(suffix);
            Assert.AreEqual(3, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TryUndoCustomization());
            CollectionAssert.AreEqual(new[] { prefix }, cs.SelectedCards);
            var restored = cs.CurrentHand.Single(c => c.Id == "target");
            Assert.IsFalse(restored.Modifier.ZeroCost); Assert.AreEqual(4, restored.GetFinalCost());
            Assert.IsTrue(cs.CurrentHand.Single(c => c.Id == "material").Modifier.ZeroCost);
            Assert.AreEqual(2, cs.GetTotalUsedCost());
        }

        [Test]
        public void 専用0化調律は重ねがけや複製で維持され元素材へ漏れず確定キューへ渡る()
        {
            var target = ZeroCostCard("target", 4); var material = ZeroCostCard("material", 1, true, true);
            var next = ZeroCostCard("next");
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy>(),
                new Deck(new List<Card> { target, material, next }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            battle.SetFirstStrike(); battle.OpenCustomScreen(random);
            var cs = battle.CustomScreen;
            var tuned = cs.ExecuteTuning(target, material).ResultCard;
            var copy = tuned.Clone(); copy.Modifier.ZeroCost = false;
            Assert.IsTrue(tuned.Modifier.ZeroCost); Assert.AreEqual(4, copy.GetFinalCost());
            Assert.AreEqual(0, tuned.ApplyTuning(ZeroCostCard("ordinary")).GetFinalCost());
            cs.TrySelectCard(tuned); cs.TrySelectCard(next); cs.Confirm();
            CollectionAssert.AreEqual(new[] { tuned, next }, battle.CardUsage.GetRemainingCards());
            Assert.AreEqual(0, battle.CardUsage.CurrentCard.GetFinalCost());
            Assert.AreEqual(4, target.GetFinalCost());
            Assert.IsFalse(cs.TryUndoCustomization());
        }

        private MockRandomProvider random;
        private Deck deck;
        private CustomScreenSystem customScreen;

        [SetUp]
        public void SetUp()
        {
            random = new MockRandomProvider(0);

            // テスト用カードを作成
            var cards = new List<Card>
            {
                CreateTestCard("card1", "ソード", 3, CardSeries.Sword),
                CreateTestCard("card2", "ワイドソード", 4, CardSeries.Sword),
                CreateTestCard("card3", "ロングソード", 5, CardSeries.Sword),
                CreateTestCard("card4", "ボム", 3, CardSeries.Bomb),
                CreateTestCard("card5", "ヒール", 2, CardSeries.None),
                CreateTestCard("card6", "シールド", 2, CardSeries.None),
            };

            deck = new Deck(cards);
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10);
        }

        // Open()メソッド用の空リストヘルパー
        private List<Card> EmptyRemainingCards() => new List<Card>();

        private Card CreateTestCard(string id, string name, int cost, CardSeries series)
        {
            return new Card(
                id,
                name,
                cost,
                CardRarity.Normal,
                CardCategory.Attack,
                ElementType.None,
                series,
                10,
                "テストカード"
            );
        }

        [Test]
        public void Open_手札が上限までドローされる()
        {
            // Act
            customScreen.Open(EmptyRemainingCards(), random);

            // Assert
            Assert.IsTrue(customScreen.IsOpen);
            Assert.AreEqual(5, customScreen.CurrentHand.Count);
        }

        [Test]
        public void TrySelectCard_コスト内ならカードを選択できる()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);
            var card = customScreen.CurrentHand[0];

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, customScreen.SelectedCards.Count);
            Assert.Contains(card, (System.Collections.ICollection)customScreen.SelectedCards);
        }

        [Test]
        public void TrySelectCard_系統ボーナスでコストが減る()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);

            // ソード系のカードを3枚選択（コスト3, 4, 5 → 3, 3, 4 = 10）
            var sword1 = customScreen.CurrentHand[0]; // ソード (3)
            var sword2 = customScreen.CurrentHand[1]; // ワイドソード (4 → 3)
            var sword3 = customScreen.CurrentHand[2]; // ロングソード (5 → 4)

            // Act
            customScreen.TrySelectCard(sword1);
            int costAfter1 = customScreen.RemainingCost;

            customScreen.TrySelectCard(sword2);
            int costAfter2 = customScreen.RemainingCost;

            customScreen.TrySelectCard(sword3);
            int costAfter3 = customScreen.RemainingCost;

            // Assert
            Assert.AreEqual(7, costAfter1); // 10 - 3 = 7
            Assert.AreEqual(4, costAfter2); // 7 - 3 = 4 (系統ボーナスで-1)
            Assert.AreEqual(0, costAfter3); // 4 - 4 = 0 (系統ボーナスで-1)
        }

        [Test]
        public void TrySelectCard_コストオーバーなら選択できない()
        {
            // Arrange
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 5);
            customScreen.Open(EmptyRemainingCards(), random);

            var card = CreateTestCard("expensive", "高コスト", 6, CardSeries.None);
            deck.AddCard(card);

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void Confirm_未選択カードは次回も手札に残る()
        {
            // Arrange: 初回Open（5枚ドロー）
            customScreen.Open(EmptyRemainingCards(), random);
            var card = customScreen.CurrentHand[0];
            customScreen.TrySelectCard(card);

            // Act: 1回目の確定（1枚選択）
            customScreen.Confirm();
            customScreen.Close(); // アニメーション完了を模擬

            // Assert: 閉じた後は未選択の4枚が手札に残っている
            Assert.IsFalse(customScreen.IsOpen);
            Assert.AreEqual(4, customScreen.CurrentHand.Count);

            // Act: 2回目のOpen
            customScreen.Open(EmptyRemainingCards(), random);

            // Assert: 前回の未選択カード4枚 + 新規ドロー1枚 = 5枚
            // （仕様: 選択しなかったカードは次回も手札に残り、手札上限まで追加ドロー）
            Assert.AreEqual(5, customScreen.CurrentHand.Count);
        }

        [Test]
        public void Cancel_選択カードが手札に戻る()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);
            var card = customScreen.CurrentHand[0];
            customScreen.TrySelectCard(card);

            // Act
            customScreen.Cancel();

            // Assert
            Assert.IsFalse(customScreen.IsOpen);
            Assert.AreEqual(0, customScreen.SelectedCards.Count);
        }

        private static object[][] SeriesCostCases => new[]
        {
            new object[] { CardSeries.Sword, CardSeries.Sword, CardSeries.Sword, 7 },
            new object[] { CardSeries.Sword, CardSeries.Bomb, CardSeries.Sword, 9 },
            new object[] { CardSeries.None, CardSeries.None, CardSeries.None, 7 },
            new object[] { CardSeries.None, CardSeries.Bomb, CardSeries.None, 8 }
        };

        [TestCaseSource(nameof(SeriesCostCases))]
        public void 選択コスト_連続系列のみ割引しNoneは既存扱いを維持する(
            CardSeries first, CardSeries second, CardSeries third, int expected)
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, first), CreateTestCard("b", "B", 3, second),
                CreateTestCard("c", "C", 3, third)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(expected, cs.GetTotalUsedCost());
            Assert.AreEqual(cs.MaxCost - expected, cs.RemainingCost);
            List<Card> confirmed = null;
            cs.OnConfirmed += result => confirmed = new List<Card>(result);
            cs.Confirm();
            cs.Close();
            CollectionAssert.AreEqual(cards, confirmed);
            CollectionAssert.AreEqual(cards, cs.AvailableCards);
        }

        [TestCase(8, false)]
        [TestCase(9, true)]
        public void 非連続の選択可否_割引なしの合計と一致する(int limit, bool allowed)
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 3, CardSeries.Bomb);
            var c = CreateTestCard("c", "C", 3, CardSeries.Sword);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: limit);
            cs.Open(EmptyRemainingCards(), random);
            Assert.IsTrue(cs.TrySelectCard(a));
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.AreEqual(allowed, cs.TrySelectCard(c));
            Assert.AreEqual(allowed ? 9 : 6, cs.GetTotalUsedCost());
            Assert.AreEqual(!allowed, cs.CurrentHand.Contains(c));
            Assert.AreEqual(allowed ? 3 : 2, cs.SelectedCards.Count);
        }

        // ゲーム全体仕様第5節: 使用順の変更は末尾からの取り消しで行う。
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        public void 選択解除_末尾だけ成功し拒否時は順序コスト手札イベントを変更しない(int index, bool allowed)
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Bomb),
                CreateTestCard("c", "C", 3, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            var hand = cs.CurrentHand.ToArray();
            int deselected = 0, selected = 0, failed = 0;
            cs.OnCardDeselected += _ => deselected++;
            cs.OnCardSelected += _ => selected++;
            cs.OnSelectionFailed += _ => failed++;
            Assert.AreEqual(allowed, cs.TryDeselectCard(cards[index]));
            Assert.AreEqual(allowed ? 6 : 9, cs.GetTotalUsedCost());
            Assert.AreEqual(allowed ? 1 : 0, deselected);
            Assert.AreEqual(0, selected);
            Assert.AreEqual(0, failed);
            if (!allowed)
            {
                CollectionAssert.AreEqual(cards, cs.SelectedCards);
                CollectionAssert.AreEqual(hand, cs.CurrentHand);
            }
            else
            {
                Assert.IsTrue(cs.TrySelectCard(cards[index]));
                CollectionAssert.AreEqual(cards, cs.SelectedCards);
                Assert.AreEqual(9, cs.GetTotalUsedCost());
            }
        }

        [Test]
        public void 末尾解除を繰り返すと全解除でき同参照の先頭を取り違えない()
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 3, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, a }));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in new[] { a, b, a }) Assert.IsTrue(cs.TrySelectCard(card));
            var events = new List<Card>();
            cs.OnCardDeselected += card => events.Add(card);
            Assert.IsTrue(cs.TryDeselectCard(a));
            CollectionAssert.AreEqual(new[] { a, b }, cs.SelectedCards);
            Assert.AreEqual(6, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TryDeselectCard(b));
            Assert.IsTrue(cs.TryDeselectCard(a));
            Assert.IsFalse(cs.TryDeselectCard(a));
            Assert.AreEqual(0, cs.SelectedCards.Count);
            Assert.AreEqual(0, cs.GetTotalUsedCost());
            Assert.AreEqual(10, cs.RemainingCost);
            CollectionAssert.AreEqual(new[] { a, b, a }, events);
            Assert.AreEqual(3, cs.CurrentHand.Count);
        }

        [Test]
        public void 連続選択_割引で上限まで選択でき割引後も最低一とする()
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Sword),
                CreateTestCard("c", "C", 0, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()), maxCost: 6);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(6, cs.GetTotalUsedCost());
            Assert.AreEqual(0, cs.RemainingCost);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 連続割引_候補と合計は最低一で予算超過を拒否する(int baseCost)
        {
            var a = CreateTestCard("a", "A", 1, CardSeries.Sword);
            var b = CreateTestCard("b", "B", baseCost, CardSeries.Sword);
            var c = CreateTestCard("c", "C", baseCost, CardSeries.Sword);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 2);
            cs.Open(EmptyRemainingCards(), random);
            Assert.IsTrue(cs.TrySelectCard(a));
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.AreEqual(2, cs.GetTotalUsedCost());
            Assert.AreEqual(0, cs.RemainingCost);
            Assert.IsFalse(cs.TrySelectCard(c));
            CollectionAssert.AreEqual(new[] { a, b }, cs.SelectedCards);
        }

        [Test]
        public void 中間解除を拒否し末尾から選び直した時だけ予算を再判定する()
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 0, CardSeries.Bomb);
            var c = CreateTestCard("c", "C", 3, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 5);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in new[] { a, b, c }) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.IsFalse(cs.TryDeselectCard(b));
            Assert.AreEqual(5, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.CanConfirm());
            Assert.IsTrue(cs.TryDeselectCard(c));
            Assert.IsTrue(cs.TryDeselectCard(b));
            Assert.IsFalse(cs.TrySelectCard(c), "系統割引がなくなり3+3で予算超過");
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.IsTrue(cs.TrySelectCard(c));
            cs.Confirm();
            CollectionAssert.AreEqual(new[] { a, b, c }, cs.AvailableCards);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 異段階超越_両順序で手札と選択を変更せず候補にしない(bool reverse)
        {
            var a = new Card("sword", "ソード", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "", exceedLevel: ExceedLevel.Normal);
            var b = new Card("sword", "ソード★", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 20, "", exceedLevel: ExceedLevel.Lv1);
            var selected = CreateTestCard("other", "選択済み", 1, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, selected }), exceedSystem: new ExceedSystem(new MockCardFactory()));
            cs.Open(EmptyRemainingCards(), random);
            cs.TrySelectCard(selected);
            var hand = cs.CurrentHand.ToArray();
            var selection = cs.SelectedCards.ToArray();
            var first = reverse ? b : a;
            var second = reverse ? a : b;
            int events = 0;
            cs.OnExceeded += _ => events++;
            Assert.IsFalse(cs.CanExceed(first, second));
            Assert.IsFalse(cs.IsCompatibleWithCurrentSelection(new[] { first }, second));
            var result = cs.ExecuteExceed(first, second);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("同じ超越段階のカード2枚が必要です", result.ErrorMessage);
            CollectionAssert.AreEqual(hand, cs.CurrentHand);
            CollectionAssert.AreEqual(selection, cs.SelectedCards);
            Assert.AreEqual(0, events);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void 同段階同参照の2枚_上限以外は候補と実行が一致する(ExceedLevel level)
        {
            var card = new Card("sword", "ソード", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "", exceedLevel: level);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { card, card }), exceedSystem: new ExceedSystem(new MockCardFactory()));
            cs.Open(EmptyRemainingCards(), random);
            Assert.AreEqual(2, cs.CurrentHand.Count);
            bool allowed = level < ExceedLevel.Lv3;
            Assert.AreEqual(allowed, cs.CanExceed(card, card));
            Assert.AreEqual(allowed, cs.IsCompatibleWithCurrentSelection(new[] { card }, card));
            var result = cs.ExecuteExceed(card, card);
            Assert.AreEqual(allowed, result.IsSuccess);
            Assert.AreEqual(allowed ? 1 : 2, cs.CurrentHand.Count);
            if (allowed) Assert.AreEqual((ExceedLevel)((int)level + 1), cs.CurrentHand[0].ExceedLevel);
            else StringAssert.Contains("最上位", result.ErrorMessage);
            Assert.IsFalse(cs.CanExceed(null, card));
            Assert.IsFalse(cs.CanExceed(card, null));
            Assert.IsFalse(cs.IsCompatibleWithCurrentSelection(new[] { card }, null));
        }

        // ===== カスタマイズ操作（超越・調律・融合）=====

        private CustomScreenSystem CreateCustomScreenWithSystems()
        {
            var mockFactory = new MockCardFactory();
            var mockRecipe = new MockFusionRecipeProvider();
            var exceed = new ExceedSystem(mockFactory);
            var tuning = new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier);
            var fusion = new FusionSystem(mockRecipe, mockFactory);
            return new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10,
                exceedSystem: exceed, tuningSystem: tuning, fusionSystem: fusion);
        }

        [Test]
        public void ExecuteExceed_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var cardA = customScreen.CurrentHand[0];
            var cardB = customScreen.CurrentHand[1];

            var result = customScreen.ExecuteExceed(cardA, cardB);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("超越システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteExceed_同名カード2枚_手札が変化する()
        {
            var cs = CreateCustomScreenWithSystems();
            // 同名カードを2枚手札に持つデッキを作成
            var sameCards = new List<Card>
            {
                CreateTestCard("sword", "ソード", 2, CardSeries.Sword),
                CreateTestCard("sword", "ソード", 2, CardSeries.Sword),
            };
            var testDeck = new Deck(sameCards);
            var mockFactory = new MockCardFactory();
            var cs2 = new CustomScreenSystem(testDeck, maxHandSize: 5, maxCost: 10,
                exceedSystem: new ExceedSystem(mockFactory));
            cs2.Open(EmptyRemainingCards(), random);

            var handBefore = cs2.CurrentHand.Count;
            var cardA = cs2.CurrentHand[0];
            var cardB = cs2.CurrentHand[1];

            var result = cs2.ExecuteExceed(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            // 素材2枚→超越カード1枚（手札-1）
            Assert.AreEqual(handBefore - 1, cs2.CurrentHand.Count);
            Assert.IsTrue(cs2.CurrentHand.Contains(result.ResultCard));
        }

        [Test]
        public void ExecuteExceed_手札にないカード_エラー()
        {
            var cs = CreateCustomScreenWithSystems();
            cs.Open(EmptyRemainingCards(), random);
            var outsideCard = CreateTestCard("outside", "外のカード", 1, CardSeries.None);

            var result = cs.ExecuteExceed(outsideCard, cs.CurrentHand[0]);

            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void ExecuteTuning_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var target = customScreen.CurrentHand[0];
            var tuning = customScreen.CurrentHand[1];

            var result = customScreen.ExecuteTuning(target, tuning);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("調律システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteTuning_調律カード消費_対象カード強化()
        {
            // 調律カードと攻撃カードを含むデッキ
            var modifier = new CardModifier { BonusDamage = 5 };
            var tuningCard = new Card("t1", "チューナー", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "", modifier: modifier);
            var attackCard = CreateTestCard("atk", "アタック", 2, CardSeries.Sword);

            var testCards = new List<Card> { attackCard, tuningCard };
            var testDeck = new Deck(testCards);
            var cs = new CustomScreenSystem(testDeck, maxHandSize: 5, maxCost: 10,
                tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            cs.Open(EmptyRemainingCards(), random);

            var handBefore = cs.CurrentHand.Count;
            var result = cs.ExecuteTuning(cs.CurrentHand[0], cs.CurrentHand[1]);

            Assert.IsTrue(result.IsSuccess);
            // 調律カードが消費されて手札-1
            Assert.AreEqual(handBefore - 1, cs.CurrentHand.Count);
            // 強化されたカードが手札にある
            Assert.IsTrue(cs.CurrentHand.Contains(result.ResultCard));
        }

        [Test]
        public void ExecuteFusion_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var materials = new List<Card> { customScreen.CurrentHand[0], customScreen.CurrentHand[1] };

            var result = customScreen.ExecuteFusion(materials);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("融合システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteFusion_素材2枚_手札が変化する()
        {
            var mockFactory = new MockCardFactory();
            var resultCard = new Card("dragon", "ドラゴン", 5, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.None, 50, "");
            mockFactory.Register("dragon", resultCard);

            var mockRecipe = new MockFusionRecipeProvider();
            mockRecipe.Register("dragon", "card1", "card2");

            var cs = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10,
                fusionSystem: new FusionSystem(mockRecipe, mockFactory));
            cs.Open(EmptyRemainingCards(), random);

            var handBefore = cs.CurrentHand.Count;
            var mat1 = cs.CurrentHand.First(c => c.Id == "card1");
            var mat2 = cs.CurrentHand.First(c => c.Id == "card2");

            var result = cs.ExecuteFusion(new List<Card> { mat1, mat2 });

            Assert.IsTrue(result.IsSuccess);
            // 素材2枚→融合カード1枚（手札-1）
            Assert.AreEqual(handBefore - 1, cs.CurrentHand.Count);
            Assert.IsTrue(cs.CurrentHand.Contains(result.ResultCard));
        }
        [Test]
        public void 同ID別定義の超越拒否は無消費で同定義の加工取消後も再判定できる()
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var a = factory.CreateById("sword"); var b = a.Clone();
            var different = new Card(a.Id, "sword_card", a.Cost, a.Rarity, a.Category, a.Element, a.Series, a.Power, "Fallback");
            var screen = new CustomScreenSystem(new Deck(new List<Card> { a, different, b }), maxHandSize:20, maxCost:100,
                exceedSystem:new ExceedSystem(factory), fusionSystem:new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(), factory));
            screen.Open(new Card[0], new MockRandomProvider(0));
            var original = screen.CurrentHand.ToArray();
            var preview = screen.PreviewCustomization(new[] { a, different });
            Assert.IsFalse(preview.CanExecute); StringAssert.Contains("同じ定義", preview.Reason);
            Assert.IsFalse(screen.ExecuteCustomization(new[] { a, different }).CanExecute);
            Assert.IsFalse(screen.ExecuteExceed(a, different).IsSuccess);
            CollectionAssert.AreEqual(original, screen.CurrentHand); Assert.AreEqual(0, screen.CustomizationUndoCount);
            Assert.IsTrue(screen.ExecuteCustomization(new[] { a, b }).CanExecute);
            Assert.IsTrue(screen.TryUndoCustomization());
            Assert.AreEqual(3, screen.CurrentHand.Count);
            var same = screen.CurrentHand.Where(c => c.Name == a.Name).ToArray();
            var other = screen.CurrentHand.Single(c => c.Name == different.Name);
            Assert.IsFalse(screen.PreviewCustomization(new[] { same[0], other }).CanExecute);
            Assert.IsTrue(screen.PreviewCustomization(same).CanExecute);
        }

        private static Card OrdinaryTuningSource(Card target=null)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();
            return f.CreateById("bomb").WithTuningDefinition(new TuningGrantDefinition("ordinary-source",new[]{new TuningEffectDefinition("bonus",1,
                new Dictionary<int,CardModifier>{{1,new CardModifier{BonusDamage=3}}},new TuningTargetCondition(category:CardCategory.Attack,series:CardSeries.Sword))},
                targetCondition:target==null?null:new TuningTargetCondition(definitionHash:CollectionDisplay.DefinitionHash(target))));
        }
        private static CustomScreenSystem EligibilityScreen(params Card[] cards)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var screen=new CustomScreenSystem(new Deck(cards.ToList()),maxHandSize:20,maxCost:100,
                tuningSystem:new TuningSystem(f.GetTuningDefinition),exceedSystem:new ExceedSystem(f),fusionSystem:new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(f),f));
            screen.Open(new Card[0],new MockRandomProvider(0));return screen;
        }
        [TestCase(false)]
        [TestCase(true)]
        public void 通常カードの適性と対象条件を候補プレビュー確定とUndoで共通に扱う(bool reverse)
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var target=f.CreateById("sword");var source=OrdinaryTuningSource(target);
            var other=new Card(target.Id,target.Name,target.Cost,target.Rarity,target.Category,target.Element,target.Series,99,target.Description);
            var screen=EligibilityScreen(target,source,other);var pair=reverse?new[]{source,target}:new[]{target,source};
            Assert.IsTrue(screen.IsCompatibleWithCurrentSelection(new[]{pair[0]},pair[1]));
            Assert.IsFalse(screen.IsCompatibleWithCurrentSelection(new[]{source},other));
            var before=screen.CurrentHand.ToArray();Assert.IsFalse(screen.PreviewCustomization(new[]{source,other}).CanExecute);
            Assert.IsFalse(screen.ExecuteCustomization(new[]{source,other}).CanExecute);CollectionAssert.AreEqual(before,screen.CurrentHand);Assert.AreEqual(0,screen.CustomizationUndoCount);
            var preview=screen.PreviewCustomization(pair);Assert.AreEqual(CustomizationOperation.Tuning,preview.Operation);Assert.IsTrue(preview.CanExecute,preview.Reason);
            CollectionAssert.AreEqual(before,screen.CurrentHand,"プレビュー取消は無消費");
            Assert.IsFalse(screen.ExecuteFusion(pair).IsSuccess,"既存のソード＋ボムレシピへ迂回しない");
            var result=screen.ExecuteCustomization(pair);Assert.IsTrue(result.CanExecute,result.Reason);Assert.AreEqual(33,result.ResultCard.GetFinalPower());
            Assert.IsTrue(screen.TryUndoCustomization());var restored=screen.CurrentHand.Single(c=>c.Id==target.Id&&c.Power==30);
            var restoredSource=screen.CurrentHand.Single(c=>c.TuningDefinition!=null);
            Assert.IsTrue(screen.IsTuningMaterial(restoredSource));Assert.IsTrue(screen.PreviewCustomization(new[]{restored,restoredSource}).CanExecute);
            Assert.IsFalse(screen.PreviewCustomization(new[]{other,restoredSource}).CanExecute);Assert.AreEqual(3,screen.CurrentHand.Count);
        }
        [Test]
        public void 適性カードも通常選択と単独使用ができ付与効果を自身の性能に混ぜない()
        {
            var source=OrdinaryTuningSource();var screen=EligibilityScreen(source);var state=new TuningSelectionState();
            Assert.IsTrue(state.TryToggle(source));Assert.AreSame(source,state.TuningCard);Assert.IsNull(state.TargetCard);
            Assert.IsTrue(screen.TrySelectCard(source));screen.Confirm();Assert.AreEqual(40,screen.AvailableCards.Single().GetFinalPower());
            Assert.AreEqual(CardCategory.Attack,source.Category);Assert.AreEqual(0,source.TuningCount);
        }
        [Test]
        public void 適性同士は調律せず異シリーズだけ融合でき同定義ペアは超越を優先する()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var source=OrdinaryTuningSource();
            var sword=f.CreateById("sword").WithTuningDefinition(source.TuningDefinition);var same=sword.Clone();
            var screen=EligibilityScreen(source,sword,same);Assert.IsFalse(screen.ExecuteTuning(sword,source).IsSuccess);
            var fusion=screen.PreviewCustomization(new[]{source,sword});Assert.AreEqual(CustomizationOperation.Fusion,fusion.Operation);Assert.IsTrue(fusion.CanExecute,fusion.Reason);
            Assert.AreEqual(CustomizationOperation.Exceed,screen.PreviewCustomization(new[]{sword,same}).Operation);
            var mixed=new FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(f),f);
            Assert.IsFalse(mixed.Validate(new[]{source,f.CreateById("sword")}).IsValid);
        }
        [Test]
        public void 調律超越再調律と逆順Undoで適性と付与回数を復元し即時ドローしない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var a=f.CreateById("sword");var b=a.Clone();var t=OrdinaryTuningSource();var t2=t.Clone();
            var screen=EligibilityScreen(a,b,t,t2);var first=screen.ExecuteCustomization(new[]{a,t});Assert.IsTrue(first.CanExecute,first.Reason);
            var tuned=screen.CurrentHand.Single(c=>c.TuningCount==1);Assert.AreEqual(33,tuned.GetFinalPower());
            var second=screen.ExecuteCustomization(new[]{tuned,b});Assert.IsTrue(second.CanExecute,second.Reason);
            var upper=screen.CurrentHand.Single(c=>c.ExceedLevel==ExceedLevel.Lv1);Assert.AreEqual(45,upper.GetFinalPower());Assert.AreEqual(0,upper.TuningCount);
            var next=screen.ExecuteCustomization(new[]{upper,t2});Assert.IsTrue(next.CanExecute,next.Reason);Assert.AreEqual(48,next.ResultCard.GetFinalPower());Assert.AreEqual(1,screen.CurrentHand.Count);
            Assert.IsTrue(screen.TryUndoCustomization());Assert.AreEqual(45,screen.CurrentHand.Single(c=>c.ExceedLevel==ExceedLevel.Lv1).GetFinalPower());
            Assert.IsTrue(screen.TryUndoCustomization());Assert.AreEqual(33,screen.CurrentHand.Single(c=>c.TuningCount==1).GetFinalPower());
            Assert.IsTrue(screen.TryUndoCustomization());Assert.AreEqual(4,screen.CurrentHand.Count);Assert.AreEqual(2,screen.CurrentHand.Count(screen.IsTuningMaterial));
            Assert.IsTrue(screen.CurrentHand.Where(c=>c.Id=="sword").All(c=>c.Power==30&&c.TuningCount==0));
        }

        private Card UndoCard(string id, bool tuning = false, ExceedLevel level = ExceedLevel.Normal) =>
            new Card(id, id, 2, CardRarity.Normal, tuning ? CardCategory.Tuning : CardCategory.Attack,
                ElementType.Fire, CardSeries.Sword, 10, "Undo素材", modifier: new CardModifier { BonusDamage = tuning ? 5 : 2 }, exceedLevel: level);

        private CustomScreenSystem UndoScreen(params Card[] cards)
        {
            var factory = new MockCardFactory();
            factory.Register("f", UndoCard("f"));
            var recipes = new MockFusionRecipeProvider();
            recipes.Register("f", "a", "b");
            var screen = new CustomScreenSystem(new Deck(cards.ToList()), maxHandSize: 20, maxCost: 100,
                exceedSystem: new ExceedSystem(factory), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier),
                fusionSystem: new FusionSystem(recipes, factory));
            screen.Open(EmptyRemainingCards(), random);
            return screen;
        }

        [TestCase("tuning")]
        [TestCase("fusion")]
        [TestCase("exceed")]
        public void Undo_加工ごとに素材の状態と順序を復元する(string operation)
        {
            var first = UndoCard("a", level: ExceedLevel.Lv1);
            var second = UndoCard(operation == "exceed" ? "a" : operation == "fusion" ? "b" : "t", operation == "tuning", operation == "exceed" ? ExceedLevel.Lv1 : ExceedLevel.Normal);
            var other = UndoCard("other");
            var cs = UndoScreen(first, other, second);
            Card result = operation == "tuning" ? cs.ExecuteTuning(first, second).ResultCard :
                operation == "fusion" ? cs.ExecuteFusion(new[] { first, second }).ResultCard : cs.ExecuteExceed(first, second).ResultCard;
            Assert.NotNull(result);
            Assert.AreEqual(2, cs.CurrentHand.Count);
            Assert.AreEqual(1, cs.CustomizationUndoCount);
            int events = 0;
            cs.OnCustomizationUndone += () => events++;
            Assert.IsTrue(cs.TryUndoCustomization());
            CollectionAssert.AreEqual(new[] { first.Id, "other", second.Id }, cs.CurrentHand.Select(c => c.Id));
            Assert.AreEqual(first.ExceedLevel, cs.CurrentHand[0].ExceedLevel);
            Assert.AreEqual(first.Power, cs.CurrentHand[0].Power);
            Assert.AreEqual(first.Modifier.BonusDamage, cs.CurrentHand[0].Modifier.BonusDamage);
            Assert.AreSame(other, cs.CurrentHand[1]);
            Assert.AreNotSame(first, cs.CurrentHand[0]);
            Assert.AreEqual(1, events);
            Assert.IsFalse(cs.TryUndoCustomization());
            Assert.AreEqual(1, events);
        }

        [Test]
        public void Undo_選択済み生成物から後ろだけを末尾順に解除する()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true);
            var prefix = UndoCard("prefix"); var suffix1 = UndoCard("suffix1"); var suffix2 = UndoCard("suffix2");
            var cs = UndoScreen(a, t, prefix, suffix1, suffix2);
            Assert.IsTrue(cs.TrySelectCard(prefix));
            var result = cs.ExecuteTuning(a, t).ResultCard;
            Assert.IsTrue(cs.TrySelectCard(result));
            Assert.IsTrue(cs.TrySelectCard(suffix1));
            Assert.IsTrue(cs.TrySelectCard(suffix2));
            var deselected = new List<Card>();
            cs.OnCardDeselected += deselected.Add;
            Assert.IsTrue(cs.TryUndoCustomization());
            CollectionAssert.AreEqual(new[] { suffix2, suffix1, result }, deselected);
            CollectionAssert.AreEqual(new[] { prefix }, cs.SelectedCards);
            CollectionAssert.AreEquivalent(new[] { "a", "t", "suffix1", "suffix2" }, cs.CurrentHand.Select(c => c.Id));
            Assert.AreEqual(prefix.Cost, cs.GetTotalUsedCost());
        }

        [Test]
        public void Undo_調律融合超越の連続加工を逆順に全て復元する()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var b = UndoCard("b"); var f = UndoCard("f");
            var cs = UndoScreen(a, t, b, f);
            var tuned = cs.ExecuteTuning(a, t).ResultCard;
            var fused = cs.ExecuteFusion(new[] { tuned, b }).ResultCard;
            Assert.IsTrue(cs.ExecuteExceed(fused, f).IsSuccess);
            Assert.AreEqual(3, cs.CustomizationUndoCount);
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(2, cs.CurrentHand.Count);
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(3, cs.CurrentHand.Count);
            Assert.AreEqual(7, cs.CurrentHand.Single(c => c.Id == "a").Modifier.BonusDamage);
            Assert.IsTrue(cs.TryUndoCustomization());
            CollectionAssert.AreEqual(new[] { "a", "t", "b", "f" }, cs.CurrentHand.Select(c => c.Id));
            Assert.AreEqual(2, cs.CurrentHand[0].Modifier.BonusDamage);
            Assert.AreEqual(0, cs.CustomizationUndoCount);
        }

        [Test]
        public void Undo_同じカードへの連続調律を一段ずつ戻す()
        {
            var a = UndoCard("a"); var t1 = UndoCard("t1", true).WithTuningDefinition(new TuningGrantDefinition("undo",
                new[] { new TuningEffectDefinition("bonus", 1, new Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 5 } } }) })); var t2 = t1.Clone();
            var cs = UndoScreen(a, t1, t2);
            var first = cs.ExecuteTuning(a, t1).ResultCard;
            cs.ExecuteTuning(first, t2);
            Assert.IsTrue(cs.TryUndoCustomization());
            var restoredFirst = cs.CurrentHand.Single(c => c.Id == "a");
            Assert.AreEqual(7, restoredFirst.Modifier.BonusDamage);
            Assert.IsTrue(cs.TrySelectCard(restoredFirst));
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.IsEmpty(cs.SelectedCards);
            Assert.AreEqual(2, cs.CurrentHand.Single(c => c.Id == "a").Modifier.BonusDamage);
        }

        [Test]
        public void Undo_加工失敗は履歴を増やさず選択済み素材を消費しない()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var b = UndoCard("b");
            var cs = UndoScreen(a, t, b);
            cs.TrySelectCard(a);
            Assert.IsFalse(cs.ExecuteTuning(a, t).IsSuccess);
            Assert.IsFalse(cs.ExecuteFusion(new[] { a, b }).IsSuccess);
            Assert.IsFalse(cs.ExecuteExceed(a, b).IsSuccess);
            Assert.AreEqual(0, cs.CustomizationUndoCount);
            Assert.IsFalse(cs.TryUndoCustomization());
            Assert.AreSame(a, cs.SelectedCards[0]);
            Assert.IsTrue(cs.TryDeselectCard(a));
            Assert.IsFalse(cs.ExecuteExceed(a, b).IsSuccess);
            Assert.AreEqual(0, cs.CustomizationUndoCount);
        }

        [TestCase("close")]
        [TestCase("cancel")]
        [TestCase("complete")]
        public void Undo_閉じる方法にかかわらず履歴を破棄する(string close)
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var cs = UndoScreen(a, t);
            cs.ExecuteTuning(a, t);
            if (close == "close") cs.Close();
            else if (close == "cancel") cs.Cancel();
            else { cs.Confirm(); cs.CompleteClose(); }
            Assert.AreEqual(0, cs.CustomizationUndoCount);
            Assert.IsFalse(cs.TryUndoCustomization());
            cs.Open(EmptyRemainingCards(), random);
            Assert.IsFalse(cs.TryUndoCustomization());
            Assert.AreEqual(7, cs.CurrentHand[0].Modifier.BonusDamage);
        }

        [Test]
        public void Undo_確定処理中は履歴とカードを変更しない()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var cs = UndoScreen(a, t);
            var result = cs.ExecuteTuning(a, t).ResultCard;
            cs.Confirm();
            Assert.IsFalse(cs.TryUndoCustomization());
            Assert.AreEqual(1, cs.CustomizationUndoCount);
            Assert.IsFalse(cs.ExecuteTuning(result, t).IsSuccess);
            cs.CancelConfirmation();
            Assert.IsTrue(cs.TryUndoCustomization());
        }

        [Test]
        public void Undo_確定済み使用キューへ移った生成物を変更しない()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var cs = UndoScreen(a, t);
            var result = cs.ExecuteTuning(a, t).ResultCard;
            cs.TrySelectCard(result); cs.Confirm(); cs.CancelConfirmation();
            Assert.IsFalse(cs.TryUndoCustomization());
            Assert.AreSame(result, cs.AvailableCards[0]);
            Assert.AreEqual(1, cs.CustomizationUndoCount);
        }

        [Test]
        public void Undo_元素材と結果への変更から復元状態を隔離する()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var cs = UndoScreen(a, t);
            var result = cs.ExecuteTuning(a, t).ResultCard;
            a.Modifier.BonusDamage = 900; t.Modifier.BonusDamage = 800; result.Modifier.BonusDamage = 700;
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(2, cs.CurrentHand[0].Modifier.BonusDamage);
            Assert.AreEqual(5, cs.CurrentHand[1].Modifier.BonusDamage);
            cs.CurrentHand[0].Modifier.BonusDamage = 600;
            Assert.AreEqual(900, a.Modifier.BonusDamage);
        }

        [Test]
        public void Undo_加工後に無関係な選択を解除してもカードを失わない()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true); var other = UndoCard("other");
            var cs = UndoScreen(a, t, other);
            cs.TrySelectCard(other); cs.ExecuteTuning(a, t); cs.TryDeselectCard(other);
            Assert.IsTrue(cs.TryUndoCustomization());
            CollectionAssert.AreEquivalent(new[] { "a", "t", "other" }, cs.CurrentHand.Select(c => c.Id));
        }

        [Test]
        public void Undo_同じ参照の素材二枠も二枚へ復元する()
        {
            var a = UndoCard("a"); var cs = UndoScreen(a, a);
            Assert.IsTrue(cs.ExecuteExceed(a, a).IsSuccess);
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(2, cs.CurrentHand.Count);
            Assert.AreNotSame(cs.CurrentHand[0], cs.CurrentHand[1]);
        }

        [Test]
        public void Undo_加工と復元は追加ドローやデッキ構成変更をしない()
        {
            var a = UndoCard("a"); var t = UndoCard("t", true);
            var source = new Deck(new List<Card> { a, t, UndoCard("remaining") });
            var cs = new CustomScreenSystem(source, maxHandSize: 2, tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            cs.Open(EmptyRemainingCards(), random);
            cs.ExecuteTuning(a, t);
            Assert.AreEqual(1, source.RemainingCount);
            Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(1, source.RemainingCount);
            Assert.AreEqual(3, source.TotalCount);
            Assert.AreEqual(2, a.Modifier.BonusDamage);
        }
    }
}
