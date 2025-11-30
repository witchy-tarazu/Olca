using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using Card = Olca.Core.Domain.Card;

namespace Olca.Tests.Card
{
    /// <summary>
    /// フェーズ2統合テスト
    /// カスタムシステム全体の動作を検証
    /// </summary>
    [TestFixture]
    public class Phase2IntegrationTests
    {
        private MockRandomProvider random;
        private MockLogger logger;
        private BattleSystem battleSystem;

        [SetUp]
        public void SetUp()
        {
            random = new MockRandomProvider(0);
            logger = new MockLogger();

            // プレイヤー作成
            var player = new PlayerCharacter(maxHp: 100, attackPower: 10);

            // 敵作成
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("goblin", "ゴブリン", 80, ElementType.Fire, new Vector2Int(5, 0))
            };

            // デッキ作成（系統ボーナステスト用）
            var cards = new List<Card>
            {
                CreateCard("sword1", "ソード", 3, CardSeries.Sword, 20),
                CreateCard("sword2", "ワイドソード", 4, CardSeries.Sword, 30),
                CreateCard("sword3", "ロングソード", 5, CardSeries.Sword, 40),
                CreateCard("bomb1", "ボム", 3, CardSeries.Bomb, 25),
                CreateCard("heal1", "ヒール", 2, CardSeries.None, 30, CardCategory.Defense),
            };
            var deck = new Deck(cards);

            // バトルシステム作成
            battleSystem = new BattleSystem(player, enemies, deck, logger: logger);
        }

        private Card CreateCard(string id, string name, int cost, CardSeries series,
            int power, CardCategory category = CardCategory.Attack)
        {
            return new Card(
                id, name, cost, CardRarity.Normal, category,
                ElementType.None, series, power, "テストカード"
            );
        }

        [Test]
        public void 統合テスト_カスタム画面からカード使用まで()
        {
            // Arrange: カスタムゲージを満タンにする
            battleSystem.CustomGauge.SetGauge(1.0f);
            Assert.IsTrue(battleSystem.CustomGauge.IsFull, "ゲージが満タンになっていない");

            // Act 1: カスタム画面を開く
            battleSystem.OpenCustomScreen(random);

            // Assert 1: カスタム画面が開いている
            Assert.IsTrue(battleSystem.CustomScreen.IsOpen, "カスタム画面が開かない");
            Assert.IsTrue(battleSystem.IsPaused, "バトルが一時停止していない");
            Assert.AreEqual(5, battleSystem.CustomScreen.CurrentHand.Count, "手札が5枚でない");

            // Act 2: カードを選択（系統ボーナスを利用）
            var customScreen = battleSystem.CustomScreen;

            // ソード系3枚を連続選択（コスト: 3, 3, 4 = 10）
            var sword1 = customScreen.CurrentHand[0]; // ソード
            var sword2 = customScreen.CurrentHand[1]; // ワイドソード
            var sword3 = customScreen.CurrentHand[2]; // ロングソード

            bool select1 = customScreen.TrySelectCard(sword1);
            bool select2 = customScreen.TrySelectCard(sword2);
            bool select3 = customScreen.TrySelectCard(sword3);

            // Assert 2: 系統ボーナスで3枚選択できた
            Assert.IsTrue(select1, "1枚目が選択できない");
            Assert.IsTrue(select2, "2枚目が選択できない（系統ボーナスが機能していない可能性）");
            Assert.IsTrue(select3, "3枚目が選択できない（系統ボーナスが機能していない可能性）");
            Assert.AreEqual(3, customScreen.SelectedCards.Count, "選択枚数が不正");
            Assert.AreEqual(0, customScreen.RemainingCost, "残りコストが0でない");

            // Act 3: カスタム画面を確定
            customScreen.Confirm();

            // Assert 3: カスタム画面が閉じて、カードがキューに入った
            Assert.IsFalse(customScreen.IsOpen, "カスタム画面が閉じていない");
            Assert.IsFalse(battleSystem.IsPaused, "バトルが再開していない");
            Assert.AreEqual(3, battleSystem.CardUsage.RemainingCount, "カードキューに3枚入っていない");

            // Act 4: カードを順番に使用
            var enemyHpBefore = battleSystem.Enemies[0].CurrentHp;

            bool use1 = battleSystem.UseCurrentCard(); // ソード（20ダメージ）
            bool use2 = battleSystem.UseCurrentCard(); // ワイドソード（30ダメージ）
            bool use3 = battleSystem.UseCurrentCard(); // ロングソード（40ダメージ）

            // Assert 4: すべてのカードが使用され、ダメージが入った
            Assert.IsTrue(use1, "1枚目のカードが使用できない");
            Assert.IsTrue(use2, "2枚目のカードが使用できない");
            Assert.IsTrue(use3, "3枚目のカードが使用できない");
            Assert.IsTrue(battleSystem.CardUsage.IsEmpty, "カードキューが空になっていない");

            var enemyHpAfter = battleSystem.Enemies[0].CurrentHp;
            int totalDamage = enemyHpBefore - enemyHpAfter;
            Assert.AreEqual(90, totalDamage, "合計ダメージが90でない（20+30+40）");

            // Assert 5: ゲージがリセットされている
            Assert.AreEqual(0f, battleSystem.CustomGauge.CurrentGauge, "ゲージがリセットされていない");
        }

        [Test]
        public void 統合テスト_回復カードの動作()
        {
            // Arrange
            battleSystem.Player.TakeDamage(50); // HPを50減らす
            Assert.AreEqual(50, battleSystem.Player.CurrentHp);

            battleSystem.CustomGauge.SetGauge(1.0f);
            battleSystem.OpenCustomScreen(random);

            // Act: ヒールカードを選択して使用
            var healCard = FindCardByName(battleSystem.CustomScreen.CurrentHand, "ヒール");
            Assert.IsNotNull(healCard, "ヒールカードが見つからない");

            battleSystem.CustomScreen.TrySelectCard(healCard);
            battleSystem.CustomScreen.Confirm();

            battleSystem.UseCurrentCard();

            // Assert: HPが回復している
            Assert.AreEqual(80, battleSystem.Player.CurrentHp, "HPが30回復していない");
        }

        [Test]
        public void 統合テスト_同名カードは選択できない()
        {
            // Arrange: 同名カードを2枚追加
            var deck = new Deck(new List<Card>
            {
                CreateCard("sword1", "ソード", 3, CardSeries.Sword, 20),
                CreateCard("sword2", "ソード", 3, CardSeries.Sword, 20), // 同名
            });

            var player = new PlayerCharacter(100);
            var enemies = new List<Enemy> { new Enemy("enemy", "敵", 50, ElementType.None, new Vector2Int(4, 0)) };
            var battle = new BattleSystem(player, enemies, deck, logger: logger);

            battle.CustomGauge.SetGauge(1.0f);
            battle.OpenCustomScreen(random);

            // Act
            var card1 = battle.CustomScreen.CurrentHand[0];
            var card2 = battle.CustomScreen.CurrentHand[1];

            bool select1 = battle.CustomScreen.TrySelectCard(card1);
            bool select2 = battle.CustomScreen.TrySelectCard(card2);

            // Assert
            Assert.IsTrue(select1, "1枚目が選択できない");
            Assert.IsFalse(select2, "同名カードが選択できてしまった");
        }

        [Test]
        public void 統合テスト_コスト超過で選択できない()
        {
            // Arrange
            battleSystem.CustomGauge.SetGauge(1.0f);
            battleSystem.OpenCustomScreen(random);

            // Act: すべてのカードを選択しようとする（合計コストは17）
            var customScreen = battleSystem.CustomScreen;
            var selectedCount = 0;

            foreach (var card in new List<Card>(customScreen.CurrentHand))
            {
                if (customScreen.TrySelectCard(card))
                {
                    selectedCount++;
                }
            }

            // Assert: コスト10以下なので、すべては選択できない
            Assert.Less(selectedCount, 5, "5枚すべて選択できてしまった");
            Assert.LessOrEqual(customScreen.RemainingCost, 10, "残りコストが不正");
        }

        private Card FindCardByName(IReadOnlyList<Card> cards, string name)
        {
            foreach (var card in cards)
            {
                if (card.Name == name)
                {
                    return card;
                }
            }
            return null;
        }
    }
}
