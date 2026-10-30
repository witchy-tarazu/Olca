using NUnit.Framework;
using System.Linq;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Tests.Mocks;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class DungeonSystemTests
    {
        // 獲得履歴・障害物の単体fixture。報酬選択の受付とは分離する。
        private static void GrantFixtureReward(DungeonSystem dungeon, Card card)
        {
            var rewards = (CardRewardSystem)typeof(DungeonSystem).GetField("cardRewardSystem",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(dungeon);
            rewards.SelectCard(card);
        }

        [Test]
        public void 移動表示の論理状態は未更新の描画フレームで失われず接敵で停止する()
        {
            var dungeon = CreateDungeonSystem();
            float position = dungeon.PlayerPosition;
            Assert.IsTrue(dungeon.IsExplorationMoving);
            for (int render = 0; render < 4; render++) {
                Assert.AreEqual(position, dungeon.PlayerPosition);
                Assert.IsTrue(dungeon.IsExplorationMoving, "描画のみのフレームでidleへ戻さない");
            }
            dungeon.StartDash(); Assert.IsTrue(dungeon.IsExplorationMoving);
            dungeon.StopDash(); Assert.IsTrue(dungeon.IsExplorationMoving, "ダッシュ解除後も自動歩行を続ける");
            for (int frame = 0; frame < 1000 && !dungeon.IsInBattle; frame++) dungeon.TickFrame();
            Assert.IsTrue(dungeon.IsInBattle); Assert.IsFalse(dungeon.IsExplorationMoving);
        }

        [Test]
        public void 移動表示は死亡時に止まる()
        {
            var dungeon = CreateDungeonSystem(); Assert.IsTrue(dungeon.IsExplorationMoving);
            dungeon.Player.TakeDamage(1000); Assert.IsFalse(dungeon.IsExplorationMoving);
        }

        [Test]
        public void プレイヤー毒は探索で自動進行し停止中は保存値も変わらず接敵と勝利で間隔だけ戻る()
        {
            var d=CreateDungeonSystem();var p=d.Player;
            p.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,60000,2),60);
            d.TickFrame();var a=p.CapturePoisons()[0];Assert.AreEqual(179950,a.RemainingUnits);Assert.AreEqual(2950,a.NextDamageUnits);
            var movement=(Olca.Core.Logic.Dungeon.PlayerMovement)typeof(DungeonSystem).GetField("playerMovement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(d);
            movement.Pause();for(int i=0;i<20;i++)d.TickFrame();Assert.AreEqual(a.RemainingUnits,p.CapturePoisons()[0].RemainingUnits);
            movement.Resume();int frames=1;
            while(!d.IsInBattle&&frames<2000){d.TickFrame();frames++;}
            Assert.IsTrue(d.IsInBattle);var start=p.CapturePoisons()[0];
            Assert.AreEqual(180000-frames*50,start.RemainingUnits);Assert.AreEqual(3000,start.NextDamageUnits);
            d.CurrentBattle.TickFrame(16);var before=p.CapturePoisons()[0];
            foreach(var e in d.CurrentBattle.Enemies.ToArray())e.TakeDamage(99999,ElementType.None);
            Assert.IsFalse(d.IsInBattle);var end=p.CapturePoisons()[0];
            Assert.AreEqual(before.RemainingUnits,end.RemainingUnits);Assert.AreEqual(3000,end.NextDamageUnits);
        }

        [Test]
        public void 併存戦闘の終了で麻痺だけ解除し毒を報酬保存と探索復帰へ持ち越す()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-coexisting-" + System.Guid.NewGuid().ToString("N"));
            try {
                var map = DungeonConfig.CreateTestDungeon(20, 30, 3);
                var deck = new Deck(new System.Collections.Generic.List<Card> { CardRewardSystem.CreateFallbackCard("sword", CardRarity.Normal) });
                var defaults = DungeonCheckpointSession.CreateDefault(map, deck);
                var files = new Olca.Unity.FileCheckpointStore(defaults.Definitions, directory);
                var session = new DungeonCheckpointSession(defaults.Definitions, files); var p = new PlayerCharacter(100);
                var game = new Olca.Core.Logic.Integration.DungeonBattleManager(map, p, deck, new MockRandomProvider(), new MockLogger(), checkpoints: session);
                p.ApplyPoison("fixture", new StatusEffect(StatusEffectType.Poison, 60000, 2), 60);
                for (int f = 0; f < 300 && game.CurrentBattle == null; f++) game.TickFrame(17);
                Assert.IsNotNull(game.CurrentBattle);
                p.ApplyParalysis(6); game.TickFrame(17); Assert.IsTrue(p.IsParalyzed);
                var before = p.CapturePoisons()[0];
                Assert.AreEqual(CheckpointBoundary.BattleStart, files.Read().Boundary, "途中状態の保存境界を増やさない");
                var battle = game.CurrentBattle;
                battle.RunLogicalFrame(() => { foreach (var enemy in battle.Enemies.ToArray()) enemy.TakeDamage(99999, ElementType.None); });
                if (game.CurrentPhase == GamePhase.CardReward) game.SelectCardReward(null);
                Assert.AreEqual(GamePhase.Dungeon, game.CurrentPhase); Assert.IsFalse(p.IsParalyzed); Assert.AreEqual(0, p.ParalysisRemainingFrames);
                Assert.AreEqual(before.RemainingUnits, p.CapturePoisons()[0].RemainingUnits);
                Assert.AreEqual(3000, p.CapturePoisons()[0].NextDamageUnits);
                var saved = files.Read(); Assert.AreEqual(CheckpointBoundary.RewardCompleted, saved.Boundary);
                Assert.AreEqual(before.RemainingUnits, saved.Player.Poisons.Single().RemainingUnits);
                var bytes = System.IO.File.ReadAllBytes(files.ActivePath);
                Assert.IsTrue(game.SuspendForCheckpoint());
                var reopened = new DungeonCheckpointSession(defaults.Definitions, new Olca.Unity.FileCheckpointStore(defaults.Definitions, directory));
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(reopened, new MockRandomProvider(), new MockLogger(), out var resumed, out var error), error);
                resumed.ActivateCheckpoint(); Assert.IsFalse(resumed.Dungeon.Player.IsParalyzed);
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(files.ActivePath));
                Assert.AreEqual(before.RemainingUnits, resumed.Dungeon.Player.CapturePoisons()[0].RemainingUnits);
                resumed.TickFrame(17);
                Assert.AreEqual(before.RemainingUnits - 50, resumed.Dungeon.Player.CapturePoisons()[0].RemainingUnits);
                Assert.AreEqual(2950, resumed.Dungeon.Player.CapturePoisons()[0].NextDamageUnits);
            } finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void 変身追加ドロー後のファイル再開は戦闘開始手札へ戻り二重ドローしない()
        {
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-hand-bonus-"+System.Guid.NewGuid().ToString("N"));
            try {
                var map=DungeonConfig.CreateTestDungeon(20,30,3);
                var deck=new Deck(Enumerable.Range(0,20).Select(_=>CardRewardSystem.CreateFallbackCard("sword",CardRarity.Normal).WithAttackTiming(new PlayerAttackTiming(3,12,6))).ToList());
                var defaults=DungeonCheckpointSession.CreateDefault(map,deck);var store=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);var session=new DungeonCheckpointSession(defaults.Definitions,store);
                var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
                for(int f=0;f<300&&game.CurrentBattle==null;f++)game.TickFrame(17);
                Assert.IsNotNull(game.CurrentBattle);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
                void Configure(BattleSystem b){b.Transformations.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire,2,new[]{new TransformationCardConversion("sword",CardRewardSystem.CreateFallbackCard("heal",CardRarity.Normal).WithAttackTiming(new PlayerAttackTiming(4,8,2)))},attack:new TransformationAttackDefinition(ElementType.Fire,chargeFrames:6))});b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());}
                Configure(game.CurrentBattle);game.CurrentBattle.Transformations.TryPreview("fixture");Assert.AreEqual(7,game.CurrentBattle.CustomScreen.CurrentHand.Count);Assert.IsTrue(game.CurrentBattle.CustomScreen.CurrentHand.All(c=>c.Id=="heal"&&c.StartupFrames==4&&c.RecoveryCancelAfterFrames==2));game.CurrentBattle.CustomScreen.Confirm();Assert.IsTrue(game.CurrentBattle.Transformations.IsUsed("fixture"));
                game.CurrentBattle.CustomScreen.Close();var attackInput=new Olca.Application.Battle.BasicAttackInputState();
                for(int f=0;f<6;f++)attackInput.Tick(game.CurrentBattle,new LogicalInput{BasicAttackButtonDown=f==0,BasicAttackButton=true},60,12);
                Assert.AreEqual(6,attackInput.ChargeFrames);
                var changedDeck=new Deck(deck.GetAllCards().Select(c=>c.WithAttackTiming(new PlayerAttackTiming(3,12,7))).ToList());
                var changed=DungeonCheckpointSession.CreateDefault(map,changedDeck);
                Assert.IsFalse(CheckpointCodec.TryRestore(store.Read(),changed.Definitions,out _,out _),"同IDでも明示した窓が異なる定義へ解釈し直さない");
                Assert.IsTrue(game.SuspendForCheckpoint());CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
                for(int repeat=0;repeat<2;repeat++) {
                    var reopened=new DungeonCheckpointSession(defaults.Definitions,new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory));
                    Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(reopened,new MockRandomProvider(),new MockLogger(),out var resumed,out var error),error);resumed.ActivateCheckpoint();
                    Configure(resumed.CurrentBattle);
                    attackInput.Tick(resumed.CurrentBattle,new LogicalInput{BasicAttackButtonUp=true},60,12);
                    Assert.AreEqual(0,attackInput.ChargeFrames);Assert.IsFalse(attackInput.IsCharging);Assert.IsNull(resumed.CurrentBattle.Transformations.ActiveAttack);
                    var screen=resumed.CurrentBattle.CustomScreen;
                    Assert.AreEqual(5,screen.CurrentHand.Count);Assert.IsTrue(screen.CurrentHand.All(c=>c.Id=="sword"&&c.StartupFrames==3&&c.RecoveryCancelAfterFrames==6));Assert.AreEqual(5,screen.MaxHandSize);Assert.IsEmpty(resumed.CurrentBattle.Transformations.UsedIds);
                    resumed.CurrentBattle.Transformations.TryPreview("fixture");Assert.AreEqual(7,screen.CurrentHand.Count);Assert.AreEqual(7,screen.CurrentHand.Distinct().Count());Assert.IsTrue(screen.CurrentHand.All(c=>c.Id=="heal"));
                    resumed.CurrentBattle.Transformations.TryCancelPending();Assert.AreEqual(5,screen.CurrentHand.Count);Assert.IsTrue(screen.CurrentHand.All(c=>c.Id=="sword"&&c.StartupFrames==3&&c.RecoveryCancelAfterFrames==6));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));resumed.SuspendForCheckpoint();
                }
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }

        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);
            mockLogger = new MockLogger();
        }

        /// <summary>
        /// テスト用のダンジョンシステムを作成
        /// </summary>
        private DungeonSystem CreateDungeonSystem(int cellCount = 20)
        {
            var config = DungeonConfig.CreateTestDungeon(cellCount);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            return new DungeonSystem(config, player, deck, mockRandom, mockLogger);
        }

        /// <summary>
        /// 通常敵（スライム、index 3）とボス（index 9）を配置したダンジョンを作成
        /// ボスのみになる CreateTestDungeon(10) の代替として使用する
        /// </summary>
        private DungeonSystem CreateDungeonSystemWithEarlyNormalEnemy()
        {
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0), new MapCell(1), new MapCell(2),
                new MapCell(3, new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))),
                new MapCell(4), new MapCell(5), new MapCell(6), new MapCell(7), new MapCell(8),
                new MapCell(9, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(9, 0))),
            };
            var config = new DungeonConfig("test_dungeon", "テストダンジョン", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            return new DungeonSystem(config, player, deck, mockRandom, mockLogger);
        }

        /// <summary>
        /// テスト用のデッキを作成
        /// </summary>
        private Deck CreateTestDeck()
        {
            var cards = new System.Collections.Generic.List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復")
            };

            return new Deck(cards);
        }

        [Test]
        public void Tick_プレイヤーが前進する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();
            float initialPosition = dungeon.PlayerPosition;

            // Act - 整数ベース版: 1000ms = 1秒
            dungeon.Tick(deltaTimeMs: 1000);

            // Assert
            Assert.Greater(dungeon.PlayerPosition, initialPosition, "プレイヤーが前進している");
        }

        [Test]
        public void Tick_バトル中は更新されない()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();

            // 強制的にバトル状態にする（リフレクションまたは接敵させる）
            // 今回は接敵まで進める
            for (int i = 0; i < 100; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            float positionDuringBattle = dungeon.PlayerPosition;

            // Act
            dungeon.Tick(deltaTimeMs: 1000);  // 1000ms = 1秒

            // Assert
            Assert.AreEqual(positionDuringBattle, dungeon.PlayerPosition, "バトル中は移動しない");
        }

        [Test]
        public void 敵との接敵時_バトルが開始される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            bool battleStarted = false;
            ContactInfo receivedContactInfo = null;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                battleStarted = true;
                receivedContactInfo = contactInfo;
            };

            // Act: 敵がいるマスまで進める（5マス目に敵がいる）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (battleStarted) break;
            }

            // Assert
            Assert.IsTrue(battleStarted, "バトルが開始された");
            Assert.IsNotNull(receivedContactInfo, "接敵情報が渡された");
            Assert.IsNotNull(receivedContactInfo.Enemy, "敵情報が含まれている");
            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");
        }

        [Test]
        public void ダッシュ中の接敵_ダッシュ効果が設定される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            ContactInfo receivedContactInfo = null;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                receivedContactInfo = contactInfo;
            };

            // Act: ダッシュしながら敵に接近
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (receivedContactInfo != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedContactInfo, "接敵情報が渡された");
            Assert.IsTrue(receivedContactInfo.IsDashContact, "ダッシュ接敵フラグが立っている");
            Assert.IsNotNull(receivedContactInfo.DashEffect, "ダッシュ効果が設定されている");
        }

        [Test]
        public void ジャンプ中に通常敵へ接近すると接敵せず回避できる()
        {
            // Arrange
            var enemy = new Enemy("enemy_5", "スライム", 50, ElementType.None, new Vector2Int(5, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy),
                new MapCell(6),
                new MapCell(7),
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig("jump_avoid_test", "ジャンプ回避テスト", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var dungeon = new DungeonSystem(config, player, CreateTestDeck(), mockRandom, mockLogger);
            bool battleStarted = false;
            dungeon.OnBattleStart += _ => battleStarted = true;

            // Act: 接触範囲の全区間を回避高度で通過する（下降中の接触は別テスト）。
            dungeon.Tick(deltaTimeMs: 200);
            dungeon.Jump();
            for (int i = 0; i < 5; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
            }

            // Assert
            Assert.IsFalse(battleStarted, "ジャンプ中なので通常敵を回避して接敵しない");
            Assert.IsFalse(dungeon.IsInBattle, "回避時はバトル状態にならない");
            Assert.Greater(dungeon.PlayerPosition, 4f, "敵の手前まで進んでいる");
        }

        [Test]
        public void Jump_ジャンプできる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();
            float initialHeight = dungeon.PlayerHeight;

            // Act
            dungeon.Jump();
            dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒、ジャンプ更新

            // Assert
            Assert.Greater(dungeon.PlayerHeight, initialHeight, "ジャンプして高さが上がっている");
        }

        [Test]
        public void OnBattleCompleted_勝利後にダンジョンが再開される()
        {
            // Arrange: index 3 に通常敵、index 9 にボス（通常敵撃破後も移動継続できる構成）
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            // カードドロップなし（通常敵の閾値0.6未満にならない）
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsFalse(dungeon.IsInBattle, "バトル状態が解除された");

            // ResumePlayerMovement() を呼ぶまでは移動しない（遷移アニメーション待ち）
            float positionAfterBattle = dungeon.PlayerPosition;
            dungeon.Tick(deltaTimeMs: 1000);
            Assert.AreEqual(positionAfterBattle, dungeon.PlayerPosition, "ResumePlayerMovement前は移動しない");

            // ResumePlayerMovement() 後は再び移動できる
            dungeon.ResumePlayerMovement();
            dungeon.Tick(deltaTimeMs: 1000);  // 1000ms = 1秒
            Assert.Greater(dungeon.PlayerPosition, positionAfterBattle, "ResumePlayerMovement後は移動できる");
        }

        [Test]
        public void OnBattleCompleted_敗北時にゲームオーバーイベントが発火する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            bool gameOverFired = false;

            dungeon.OnDungeonFailed += () => gameOverFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            // Act: バトル敗北
            dungeon.OnBattleCompleted(victory: false);

            // Assert
            Assert.IsTrue(gameOverFired, "ゲームオーバーイベントが発火した");
        }

        [Test]
        public void OnBattleCompleted_カードドロップ時に選択画面が表示される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            System.Collections.Generic.List<Card> offeredCards = null;

            dungeon.OnCardRewardOffered += (cards) =>
            {
                offeredCards = cards;
            };

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            // mockRandomが0.5を返すように設定（ドロップ率60%は成功）
            mockRandom.SetFixedValue(0.5f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsNotNull(offeredCards, "カード選択画面が表示された");
            Assert.Greater(offeredCards.Count, 0, "カード選択肢が提示された");
        }

        [Test]
        public void OnCardSelectionCompleted_カードがデッキに追加される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            int initialDeckSize = dungeon.GetCurrentDeck().TotalCount;

            Card selectedCard = null;
            dungeon.OnCardRewardOffered += cards => selectedCard = cards[0];
            dungeon.OnBattleCompleted(true);
            Assert.IsNotNull(selectedCard);

            // Act: 実際に提示された候補を確定
            dungeon.OnCardSelectionCompleted(selectedCard);

            // Assert
            Assert.AreEqual(initialDeckSize + 1, dungeon.GetCurrentDeck().TotalCount,
                "デッキにカードが追加された");
        }

        [Test]
        public void GetObtainedCards_初期デッキは獲得履歴に含めない()
        {
            var dungeon = CreateDungeonSystem();

            Assert.AreEqual(2, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(0, dungeon.GetObtainedCards().Count);
        }

        [Test]
        public void GetObtainedCards_報酬取得で増加しスキップでは変化しない()
        {
            var dungeon = CreateDungeonSystem();
            var reward = CreateTestDeck().GetAllCards()[0];

            dungeon.OnCardSelectionCompleted(null);
            Assert.AreEqual(0, dungeon.GetObtainedCards().Count);
            GrantFixtureReward(dungeon, reward);
            GrantFixtureReward(dungeon, reward);
            dungeon.OnCardSelectionCompleted(null);

            Assert.AreEqual(2, dungeon.GetObtainedCards().Count, "同種カードも取得枚数として数える");
            Assert.AreEqual(4, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void GetObtainedCards_新規ダンジョンには獲得履歴を持ち越さない()
        {
            var previous = CreateDungeonSystem();
            GrantFixtureReward(previous, CreateTestDeck().GetAllCards()[0]);

            var next = CreateDungeonSystem();

            Assert.AreEqual(1, previous.GetObtainedCards().Count);
            Assert.AreEqual(0, next.GetObtainedCards().Count);
            Assert.AreEqual(2, next.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void GetObtainedCards_入手したカード一覧を取得できる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            var card1 = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act
            GrantFixtureReward(dungeon, card1);

            // Assert
            Assert.AreEqual(1, dungeon.GetObtainedCards().Count, "入手カード一覧に1枚追加");
            Assert.AreEqual(card1, dungeon.GetObtainedCards()[0]);
        }

        [Test]
        public void Tick_障害物に接触_ペナルティイベントが発火する()
        {
            // Arrange
            var obstacle = new Obstacle("spike", "トゲ", 15);  // 即時ダメージ障害物
            // マップはインデックスでセルを取得するため、インデックス3に障害物を配置するなら
            // それ以前のセルも必要
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2),  // 空白
                new MapCell(3, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            PenaltyInfo receivedPenalty = null;
            dungeon.OnObstaclePenalty += (penalty) => receivedPenalty = penalty;

            // Act: 障害物位置まで進める（位置3に到達）
            for (int i = 0; i < 50; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (receivedPenalty != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedPenalty, "ペナルティイベントが発火");
            Assert.AreEqual(ObstaclePenaltyType.ImmediateDamage, receivedPenalty.PenaltyType);
            Assert.AreEqual(15, receivedPenalty.Damage);
        }

        [Test]
        public void Tick_障害物をジャンプで回避_ペナルティが発生しない()
        {
            // Arrange
            // 障害物を位置2に配置（より近い位置でテスト）
            var obstacle = new Obstacle("spike", "トゲ", 15);  // 即時ダメージ障害物
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            PenaltyInfo receivedPenalty = null;
            dungeon.OnObstaclePenalty += (penalty) => receivedPenalty = penalty;

            // Act: 障害物の手前でジャンプして飛び越える
            // BASE_SPEED_MILLI_PER_SEC=10000, deltaTimeMs=100 → 1Tickで1.0移動
            // JUMP_DURATION_MS=500 → 5Tickでジャンプ終了（その間に1.0*5=5.0マス移動）
            // 位置2に到達するまで約2Tick
            // 位置0.9以上でジャンプ開始すれば、次のTick（位置2通過時）にまだジャンプ中
            bool jumped = false;
            for (int i = 0; i < 30; i++)
            {
                // 位置0.9付近でジャンプ開始（速度10/s・100ms刻みの場合、1Tick=1.0マス）
                if (!jumped && dungeon.PlayerPosition >= 0.9f)
                {
                    dungeon.Jump();
                    jumped = true;
                }
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.PlayerPosition > 3.0f) break;
            }

            // デバッグ: ログを確認
            string logInfo = string.Join("\n", mockLogger.InfoLogs);

            // Assert
            Assert.IsNull(receivedPenalty, $"ジャンプで回避したのでペナルティなし\nLogs:\n{logInfo}");
        }

        [Test]
        public void 障害物ダメージでゲームオーバー()
        {
            // Arrange
            var obstacle = new Obstacle("spike", "トゲ", 150);  // 即死ダメージ障害物
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2),  // 空白
                new MapCell(3, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            bool gameOverFired = false;
            dungeon.OnDungeonFailed += () => gameOverFired = true;

            // Act: 障害物位置まで進める
            for (int i = 0; i < 50; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (gameOverFired) break;
            }

            // Assert
            Assert.IsTrue(gameOverFired, "HPが0以下になりゲームオーバー");
            Assert.LessOrEqual(player.CurrentHp, 0);
        }

        private DungeonSystem CreateCardDiscardDungeon(Deck initialDeck, int discardCount)
        {
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0), new MapCell(1),
                new MapCell(2, Obstacle.CreateCardDiscardObstacle("trap", "カード破棄罠", discardCount)),
                new MapCell(3), new MapCell(4),
                new MapCell(5, new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(5, 0)))
            };
            var config = new DungeonConfig("discard_test", "カード破棄テスト", 6, cells);
            return new DungeonSystem(config, new PlayerCharacter(maxHp: 100), initialDeck, mockRandom, mockLogger);
        }

        [TestCase(0, 2)]
        [TestCase(1, 1)]
        [TestCase(5, 0)]
        public void Tick_カード破棄障害物_要求枚数を残数まで適用する(int requested, int expected)
        {
            var initialDeck = CreateTestDeck();
            var dungeon = CreateCardDiscardDungeon(initialDeck, requested);

            dungeon.Tick(deltaTimeMs: 200);

            Assert.AreEqual(2f, dungeon.PlayerPosition);
            Assert.AreEqual(expected, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(expected, dungeon.GetCurrentDeck().RemainingCount);
            Assert.AreEqual(2, initialDeck.TotalCount, "初期デッキは変更しない");
            Assert.AreEqual(100, dungeon.Player.CurrentHp, "カード破棄ではHPを変更しない");
        }

        [Test]
        public void Tick_カード破棄障害物_空デッキでも処理できる()
        {
            var dungeon = CreateCardDiscardDungeon(new Deck(), 5);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;

            dungeon.Tick(deltaTimeMs: 200);

            Assert.AreEqual(1, penalties, "空デッキでも障害物接触を処理する");
            Assert.AreEqual(0, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(0, dungeon.GetCurrentDeck().RemainingCount);
        }

        [Test]
        public void Tick_固定乱数でカード破棄_初期デッキを保全し次戦の手札に反映する()
        {
            var initialDeck = CreateTestDeck();
            var survivor = initialDeck.GetAllCards()[0];
            var discarded = initialDeck.GetAllCards()[1];
            mockRandom.SetFixedValue(1);
            var dungeon = CreateCardDiscardDungeon(initialDeck, 1);

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.AreEqual(new[] { survivor }, dungeon.GetCurrentDeck().GetAllCards());
            CollectionAssert.AreEqual(new[] { survivor, discarded }, initialDeck.GetAllCards());
            for (int i = 0; i < 10 && !dungeon.IsInBattle; i++)
                dungeon.Tick(deltaTimeMs: 100);
            Assert.IsTrue(dungeon.IsInBattle);
            dungeon.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            dungeon.CurrentBattle.OpenCustomScreen(mockRandom);
            CollectionAssert.AreEqual(new[] { survivor }, dungeon.CurrentBattle.CustomScreen.CurrentHand);
        }

        [Test]
        public void Tick_カード破棄障害物_ドロー済みカードも除去して履歴を整合させる()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            var deck = dungeon.GetCurrentDeck();
            var drawn = deck.DrawCards(1, mockRandom)[0];
            var survivor = deck.GetAllCards()[1];

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.DoesNotContain(deck.GetAllCards(), drawn);
            Assert.AreEqual(1, deck.RemainingCount);
            CollectionAssert.AreEqual(new[] { survivor }, deck.DrawCards(5, mockRandom));
            deck.ResetDrawHistory();
            CollectionAssert.AreEqual(new[] { survivor }, deck.DrawCards(5, mockRandom));
        }

        [Test]
        public void Tick_カード破棄障害物_同じセルで再処理しない()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;

            dungeon.Tick(deltaTimeMs: 200);
            dungeon.Tick(deltaTimeMs: 0);
            dungeon.Tick(deltaTimeMs: 10);

            Assert.AreEqual(1, penalties);
            Assert.AreEqual(1, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void Tick_カード破棄障害物をジャンプ回避_着地後も破棄しない()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;
            dungeon.Tick(deltaTimeMs: 100);
            dungeon.Jump();

            dungeon.Tick(deltaTimeMs: 100);
            Assert.Greater(dungeon.PlayerHeight, 0f);
            for (int i = 0; i < 5; i++) dungeon.Tick(deltaTimeMs: 100);

            Assert.AreEqual(0, penalties);
            Assert.AreEqual(2, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void Tick_獲得カードを破棄_クリア報酬用の獲得履歴に残る()
        {
            var initialDeck = CreateTestDeck();
            var dungeon = CreateCardDiscardDungeon(initialDeck, 1);
            var reward = new Card("reward", "獲得カード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "報酬");
            GrantFixtureReward(dungeon, reward);
            dungeon.ResumePlayerMovement();
            mockRandom.SetFixedValue(2);

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.AreEqual(initialDeck.GetAllCards(), dungeon.GetCurrentDeck().GetAllCards());
            CollectionAssert.AreEqual(new[] { reward }, dungeon.GetObtainedCards());
            Assert.AreEqual(2, initialDeck.TotalCount);
        }

        // ========== フェーズ5: バトルシステム統合テスト ==========

        [Test]
        public void 接敵時_BattleSystemが初期化される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            // Act: 敵がいるマスまで進める（5マス目に敵がいる）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");
            Assert.IsNotNull(dungeon.CurrentBattle, "BattleSystemが初期化されている");
        }

        [Test]
        public void 接敵時_プレイヤーHPが共有されている()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // プレイヤーに事前ダメージ
            player.TakeDamage(30);
            int hpBeforeBattle = player.CurrentHp;  // 70

            // Act: 敵がいるマスまで進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(dungeon.CurrentBattle, "BattleSystemが存在する");
            Assert.AreEqual(hpBeforeBattle, dungeon.CurrentBattle.Player.CurrentHp,
                "BattleSystemのプレイヤーHPがダンジョンと同じ（参照共有）");
        }

        [Test]
        public void ダッシュ接敵時_敵に初期ダメージが入る()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            Enemy contactedEnemy = null;
            int enemyHpBeforeBattle = 0;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                contactedEnemy = contactInfo.Enemy;
            };

            // 敵のHPを記録するために、まず通常接敵を試す
            // テストダンジョンの敵は5マス目にいるはず
            var testEnemy = config.MapCells[5].Enemy;
            if (testEnemy != null)
            {
                enemyHpBeforeBattle = testEnemy.CurrentHp;
            }

            // Act: ダッシュしながら敵に接近
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(contactedEnemy, "敵と接敵した");
            if (enemyHpBeforeBattle > 0)
            {
                Assert.Less(contactedEnemy.CurrentHp, enemyHpBeforeBattle,
                    "ダッシュ接敵で敵にダメージが入っている");
            }
        }

        [Test]
        public void 通常接敵時_プレイヤーは左中央敵は右中央に配置される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            ContactInfo contactInfo = null;
            dungeon.OnBattleStart += (info) => contactInfo = info;

            // Act: 通常接敵（ダッシュなし）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert: バトルグリッド上の位置は常に固定（左中央=1、右中央=4）
            Assert.IsNotNull(contactInfo, "接敵した");
            Assert.IsFalse(contactInfo.IsDashContact, "通常接敵");
            Assert.AreEqual(1, player.GridPosition.X, "プレイヤーは左側中央（グリッド1）");
            Assert.AreEqual(4, contactInfo.Enemy.GridPosition.X, "敵は右側中央（グリッド4）");
        }

        [Test]
        public void ダッシュ接敵時_プレイヤーは左中央敵は右中央に配置される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            ContactInfo contactInfo = null;
            dungeon.OnBattleStart += (info) => contactInfo = info;

            // Act: ダッシュ接敵
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert: バトルグリッド上の位置は常に固定（左中央=1、右中央=4）
            // ノックバック量はダンジョンマップ上の移動量で表現され、グリッド位置には影響しない
            Assert.IsNotNull(contactInfo, "接敵した");
            Assert.IsTrue(contactInfo.IsDashContact, "ダッシュ接敵");
            Assert.AreEqual(1, player.GridPosition.X, "プレイヤーは左側中央（グリッド1）");
            Assert.AreEqual(4, contactInfo.Enemy.GridPosition.X, "敵は右側中央（グリッド4）");
        }

        [Test]
        public void バトル勝利後_HPが回復しない()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態");

            // バトル中にダメージを受ける
            player.TakeDamage(30);
            int hpAfterDamage = player.CurrentHp;  // 70

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.AreEqual(hpAfterDamage, player.CurrentHp, "バトル後もHPは回復しない");
        }

        [Test]
        public void バトル終了後_CurrentBattleがnullになる()
        {
            // Arrange: index 3 に通常敵、index 9 にボス
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsNotNull(dungeon.CurrentBattle, "バトル中はCurrentBattleがある");

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // 無報酬のため選択待ちを挟まない。

            // Assert
            Assert.IsNull(dungeon.CurrentBattle, "バトル終了後はCurrentBattleがnull");
        }

        [Test]
        public void バトル終了時_OnBattleEndイベントが発火する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            BattleResult? receivedResult = null;

            dungeon.OnBattleEnd += (result) => receivedResult = result;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsNotNull(receivedResult, "OnBattleEndが発火した");
            Assert.AreEqual(BattleResult.Victory, receivedResult, "勝利結果");
        }

        [Test]
        public void ダンジョン復帰時_OnReturnToDungeonイベントが発火する()
        {
            // Arrange: index 3 に通常敵、index 9 にボス（通常敵撃破後も復帰できる構成）
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();
            bool returnEventFired = false;

            dungeon.OnReturnToDungeon += () => returnEventFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsTrue(returnEventFired, "OnReturnToDungeonが発火した");
        }

        [Test]
        public void 全敵撃破後_ダンジョンクリアイベントが発火する()
        {
            // Arrange: 敵が1体だけのダンジョン
            var enemy = new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(5, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy),  // 唯一の敵
                new MapCell(6),
                new MapCell(7),
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig("clear_test", "クリアテスト", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            bool clearFired = false;
            dungeon.OnDungeonCleared += () => clearFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            // ボスはカードを100%ドロップするため、選択肢を提示された後にスキップ
            System.Collections.Generic.List<Card> rewardCards = null;
            dungeon.OnCardRewardOffered += (cards) => rewardCards = cards;

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // カード報酬をスキップ（選択なし）
            if (rewardCards != null)
            {
                dungeon.OnCardSelectionCompleted(null);
            }

            // Assert
            Assert.IsTrue(clearFired, "ダンジョンクリアイベントが発火した");
            Assert.IsTrue(dungeon.IsDungeonCleared, "IsDungeonClearedがtrueになった");
        }

        [Test]
        public void ダンジョンクリア後_Tickしても移動しない()
        {
            // Arrange: 敵が1体だけのダンジョン
            var enemy = new Enemy("enemy_1", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3, enemy),
                new MapCell(4),
            };
            var config = new DungeonConfig("clear_test2", "クリアテスト2", 5, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // 接敵
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // バトル勝利
            dungeon.OnBattleCompleted(victory: true);
            Assert.IsTrue(dungeon.IsDungeonCleared, "最後の敵が無報酬なら選択待ちなしでクリアする");

            float posAfterClear = dungeon.PlayerPosition;

            // Act: クリア後にTick
            dungeon.Tick(deltaTimeMs: 1000);

            // Assert: 移動しない
            Assert.AreEqual(posAfterClear, dungeon.PlayerPosition, "クリア後は移動しない");
        }

        [Test]
        public void 画面内の複数敵がバトルに参加する()
        {
            // Arrange: 複数の敵を配置したダンジョン
            var enemy1 = new Enemy("goblin", "ゴブリン", 50, ElementType.None, new Vector2Int(5, 0));
            var enemy2 = new Enemy("slime", "スライム", 30, ElementType.None, new Vector2Int(7, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy1),  // 敵1
                new MapCell(6),
                new MapCell(7, enemy2),  // 敵2（画面内）
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig(
                dungeonId: "multi_enemy",
                dungeonName: "複数敵ダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // Act: 敵1に接敵するまで進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(dungeon.CurrentBattle, "バトル開始");
            Assert.GreaterOrEqual(dungeon.CurrentBattle.Enemies.Count, 2,
                "画面内の複数敵がバトルに参加している");
        }
    }
}

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class CheckpointCoreTests
    {
        private Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions;
        private DungeonConfig map;
        private Card standard, fallback;
        private CheckpointDto departure;
        private sealed class CountingRandom : Olca.Core.Interfaces.IRandomProvider
        {
            public int Calls;
            public int Range(int min, int max) { Calls++; return min; }
            public float Range(float min, float max) { Calls++; return min; }
            public float Value { get { Calls++; return 0; } }
        }
        [SetUp]
        public void SetUp()
        {
            map = DungeonConfig.CreateTestDungeon(10, 30, 3);
            definitions = new CheckpointDefinitions("fixture-v1", map);
            standard = new Card("sword", "同名", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "通常");
            fallback = new Card("sword", "同名", 2, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.Sword, 45, "別定義");
            definitions.RegisterCard("standard/v1/sword", standard);
            definitions.RegisterCard("fallback/v1/sword", fallback);
            // 現行フォールバック抽選が実際に生成する値を登録。製品プールは作らない。
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                definitions.RegisterCard("reward/v1/" + id, new Card(id, id + "_card", 1, CardRarity.Normal,
                    CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "Fallback card"));
            departure = new CheckpointDto {
                DefinitionsRevision = definitions.Revision, DungeonId = definitions.DungeonId, MapHash = definitions.MapHash,
                RunId = "run", Sequence = 1, Boundary = CheckpointBoundary.Departure,
                Player = new CheckpointPlayerDto { Hp = 93, MaxHp = 100, AttackPower = 10, Poise = 30, CustomSpeed = 1, GridX = 0, GridY = 0 },
                Cards = new[] { definitions.CaptureCard("a", "standard/v1/sword", standard), definitions.CaptureCard("b", "standard/v1/sword", standard.Clone()),
                    definitions.CaptureCard("c", "fallback/v1/sword", fallback), definitions.CaptureCard("discarded", "fallback/v1/sword", fallback) },
                DeckOrder = new[] { "c", "b", "a" }, ObtainedOrder = new[] { "discarded", "c" },
                Enemies = new[] { new CheckpointEnemyDto { CellIndex = 3, EnemyId = "enemy_3", Hp = 50, GridX = 4 },
                    new CheckpointEnemyDto { CellIndex = 9, EnemyId = "boss", Hp = 200, GridX = 5 } }
            };
        }
        [Test]
        public void プレイヤー毒は旧保存互換を保ちファイル復帰へ正確に持ち越す()
        {
            Assert.IsTrue(CheckpointCodec.TryRestore(departure,definitions,out var legacy,out var error),error);
            Assert.IsEmpty(legacy.Player.CapturePoisons());
            departure.Player.Poisons=new[]{new PlayerPoisonDto{Kind="fixture",Damage=7,IntervalFrames=6,RemainingUnits=2997,NextDamageUnits=197}};
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-player-poison-"+System.Guid.NewGuid().ToString("N"));
            try {
                var store=new Olca.Unity.FileCheckpointStore(definitions,directory);
                Assert.IsTrue(store.TryWrite(departure,out error),error);
                var data=new Olca.Unity.FileCheckpointStore(definitions,directory).Read();
                Assert.IsTrue(CheckpointCodec.TryRestore(data,definitions,out var restored,out error),error);
                Assert.AreEqual(2997,restored.Player.CapturePoisons()[0].RemainingUnits);
                Assert.AreEqual(197,restored.Player.CapturePoisons()[0].NextDamageUnits);
                var copy=data.Copy();copy.Player.Poisons[0].RemainingUnits=1;Assert.AreEqual(2997,data.Player.Poisons[0].RemainingUnits);
                var bytes=System.IO.File.ReadAllBytes(store.ActivePath);data.Player.Poisons[0].NextDamageUnits=301;
                Assert.IsFalse(store.TryWrite(data,out error));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
                data.Player.Poisons=new[]{departure.Player.Poisons[0],departure.Player.Poisons[0]};
                Assert.IsFalse(CheckpointCodec.TryRestore(data,definitions,out _,out error));
            } finally {if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }

        [TestCase(0)] [TestCase(50)] [TestCase(150)]
        public void 敵の時間耐性は通常定義とファイル保存復帰へ伝わり異なる定義を拒否する(int resistance)
        {
            var configured = DungeonConfig.CreateTestDungeon(10, 30, 3);
            for (int i = 0; i < configured.MapCells.Count; i++) {
                var cell = configured.MapCells[i]; var e = cell.Enemy; if (e == null) continue;
                configured.MapCells[i] = new MapCell(i, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element, e.GridPosition,
                    statusResistances: new TimedStatusResistances(poison: resistance, paralyze: 50)), cell.Height);
            }
            var configuredDefinitions = new CheckpointDefinitions("fixture-v1", configured);
            configuredDefinitions.RegisterCard("standard/v1/sword", standard);
            configuredDefinitions.RegisterCard("fallback/v1/sword", fallback);
            var dto = departure.Copy(); dto.MapHash = configuredDefinitions.MapHash;
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-status-map-" + System.Guid.NewGuid().ToString("N"));
            try {
                var store = new Olca.Unity.FileCheckpointStore(configuredDefinitions, directory);
                Assert.IsTrue(store.TryWrite(dto, out var error), error);
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var read = new Olca.Unity.FileCheckpointStore(configuredDefinitions, directory);
                Assert.IsTrue(CheckpointCodec.TryRestore(read.Read(), configuredDefinitions, out var restored, out error), error);
                var enemy = restored.Enemies[3];
                Assert.AreEqual(resistance, enemy.StatusResistances.Poison); Assert.AreEqual(50, enemy.StatusResistances.Paralyze);
                enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
                Assert.AreEqual(30, enemy.ActiveStatus.RemainingFrames);
                Assert.IsFalse(CheckpointCodec.TryRestore(read.Read(), definitions, out _, out _));
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
                var route = new FloorSequenceDefinitions("status-fixture", "v1", new[] { configured }, new Deck());
                Assert.AreEqual(resistance, route.CreateInitialMap(0).MapCells[3].Enemy.StatusResistances.Poison);
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void 時間耐性未指定の旧階層マップハッシュを変更しない()
        {
            // a5caaed の実行時保存から取得した2階層の旧hash。
            Assert.AreEqual("6F62727233C3B0E3001BD1A7C522B3431389AF797E7CCEE76717732B6B79E09F",
                new CheckpointDefinitions("fixed-runtime-v1", DungeonConfig.CreateTestDungeon(6, 30, 100)).MapHash);
            Assert.AreEqual("7292E95285520F6E55A2856F410B9FB607BB2E24290EA1DBAE118726AB555682",
                new CheckpointDefinitions("fixed-runtime-v1", DungeonConfig.CreateTestDungeon(20, 7, 5)).MapHash);
        }

        [TestCase(AttackProperty.None)]
        [TestCase(AttackProperty.Sword)]
        [TestCase(AttackProperty.Sword | AttackProperty.Break)]
        [TestCase(AttackProperty.Wind)]
        [TestCase(AttackProperty.Wind | AttackProperty.Break)]
        public void 攻撃性質の保存復帰は個体順序と獲得参照を維持し別性質定義を拒否する(AttackProperty properties)
        {
            Card Make(AttackProperty value) => new Card("sword", "同名", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.Sword, 45, "別定義", properties: value);
            var card = Make(properties);
            definitions.RegisterCard("property/v1", card);
            departure.Cards[2] = definitions.CaptureCard("c", "property/v1", card);
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-properties-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(store.TryWrite(departure, out var error), error);
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var readStore = new Olca.Unity.FileCheckpointStore(definitions, directory);
                var roundtrip = readStore.Read(); Assert.IsNotNull(roundtrip, readStore.ReadError);
                Assert.IsTrue(CheckpointCodec.TryRestore(roundtrip, definitions, out var restored, out error), error);
                Assert.AreEqual(properties, restored.Deck.GetAllCards()[0].Properties);
                Assert.AreEqual(ElementType.Fire, restored.Deck.GetAllCards()[0].Element);
                Assert.AreSame(restored.Cards["c"], restored.Obtained[1]);
                Assert.AreSame(restored.Cards["c"], restored.Deck.GetAllCards()[0]);
                CollectionAssert.AreEqual(departure.DeckOrder, roundtrip.DeckOrder);
                CollectionAssert.AreEqual(departure.ObtainedOrder, roundtrip.ObtainedOrder);
                Assert.AreEqual(93, restored.Player.CurrentHp);
                var changed = new CheckpointDefinitions("fixture-v1", map);
                changed.RegisterCard("standard/v1/sword", standard);
                changed.RegisterCard("fallback/v1/sword", fallback);
                changed.RegisterCard("property/v1", Make(properties == AttackProperty.None ? AttackProperty.Break : AttackProperty.None));
                Assert.IsFalse(CheckpointCodec.TryRestore(roundtrip, changed, out _, out _));
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath), "復帰や定義不一致でも保存原本を書き換えない");
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void 性質未指定のソードは導入前保存の定義ハッシュと一致する()
        {
            // a5caaed の実行時保存から取得した旧値。旧カタログを明示して互換性を検証する。
            var sword = new Olca.Application.Battle.SimpleCardFactory(legacyPoise: true).CreateById("sword");
            Assert.IsNull(sword.PoiseDamage);
            definitions.RegisterCard("legacy-sword", sword);
            Assert.AreEqual(AttackProperty.None, sword.Properties);
            Assert.AreEqual("6FF05396427C959B85CF868B3F3B186894D17E8DD4CBF04214C0F6A85B236A72",
                definitions.CaptureKnownCard("legacy", sword).ValueHash);
        }

        [Test]
        public void 属性耐性の保存復帰と上限検証()
        {
            departure.Player.FireResistance=-50;departure.Player.WaterResistance=100;
            departure.Player.WindResistance=50;departure.Player.EarthResistance=-200;
            var store=new MemoryCheckpointStore(definitions);Assert.IsTrue(store.TryWrite(departure,out var error),error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),definitions,out var restored,out error),error);
            Assert.IsTrue(restored.Player.Resistances.HasSameValues(new ElementResistances(-50,100,50,-200)));
            Assert.AreEqual(93,restored.Player.CurrentHp);
            departure.Player.FireResistance=101;
            Assert.IsFalse(CheckpointCodec.TryRestore(departure,definitions,out _,out _));
        }

        [TestCase("wallet")] [TestCase("amount")] [TestCase("count")]
        public void 不正な通貨財布と戦闘開始額を復帰で拒否する(string invalid)
        {
            var snapshot = FindPlan(true).Snapshot;
            if (invalid == "wallet") snapshot.Currency = -1;
            if (invalid == "amount") snapshot.Battle.CurrencyAmounts = new[] { -1 };
            if (invalid == "count") snapshot.Battle.CurrencyAmounts = new int[0];
            Assert.IsFalse(CheckpointCodec.TryRestore(snapshot, definitions, out _, out _));
        }

        [Test]
        public void 重み付き戦闘開始計画は保存後の重み変更でも候補を再抽選しない()
        {
            bool saved = false;
            var cards = new[] { "sword", "heal", "shield", "fire", "ice" }
                .Select(id => CardRewardSystem.CreateFallbackCard(id, CardRarity.Normal)).ToList();
            var pool = new Olca.Tests.Mocks.SimpleCardPool(cards, c => {
                Assert.IsFalse(saved, "保存復帰では重みを参照しない");
                return c.Id == "heal" ? 10 : c.Id == "fire" ? 0 : 1;
            });
            CheckpointBattlePlan plan = null;
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, seed, pool,
                    c => "reward/v1/" + c.Id, out plan, out var error), error);
                if (plan.Snapshot.Battle.RewardOffered) break;
            }
            Assert.IsTrue(plan.Snapshot.Battle.RewardOffered);
            var snapshot = plan.Snapshot;
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-weighted-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(store.TryWrite(snapshot, out var error), error); saved = true;
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var reopened = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(), definitions, out var restored, out error), error);
                Assert.AreEqual(3, restored.RewardCandidates.Count);
                Assert.IsFalse(restored.RewardCandidates.Any(c => c.Id == "fire"));
                CollectionAssert.AreEqual(snapshot.Battle.RewardCandidates, restored.Snapshot.Battle.RewardCandidates);
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 部分一致IDの新規通常報酬と旧保存一候補を再抽選せず復帰する(bool legacy)
        {
            map.MapCells[3] = new MapCell(3, new Enemy("miniboss", "通常敵", 50, ElementType.None, new Vector2Int(3, 0)));
            var updated = new CheckpointDefinitions("fixture-v1", map);
            updated.RegisterCard("standard/v1/sword", standard);
            updated.RegisterCard("fallback/v1/sword", fallback);
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                updated.RegisterCard("reward/v1/" + id, CardRewardSystem.CreateFallbackCard(id, CardRarity.Normal));
            var oldReward = CardRewardSystem.CreateFallbackCard("sword", CardRarity.Limited);
            updated.RegisterCard("legacy/boss/sword", oldReward);
            departure.MapHash = updated.MapHash; departure.Enemies[0].EnemyId = "miniboss";
            definitions = updated;
            var snapshot = FindPlan(legacy).Snapshot;
            if (legacy)
            {
                var keep = snapshot.Battle.RewardCandidates[0];
                var remove = snapshot.Battle.RewardCandidates.Skip(1).ToArray();
                snapshot.Battle.RewardCandidates = new[] { keep };
                snapshot.Cards = snapshot.Cards.Where(c => !remove.Contains(c.InstanceId))
                    .Select(c => c.InstanceId == keep ? definitions.CaptureCard(keep, "legacy/boss/sword", oldReward) : c).ToArray();
            }
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(snapshot, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            CollectionAssert.AreEqual(snapshot.Battle.RewardCandidates, restored.Snapshot.Battle.RewardCandidates);
            Assert.AreEqual(legacy ? 1 : 0, restored.RewardCandidates.Count);
            if (legacy) Assert.AreEqual(CardRarity.Limited, restored.RewardCandidates[0].Rarity);
        }

        private CheckpointBattleDto Encounter() => new CheckpointBattleDto {
            ContactCell = 3, ParticipantCells = new[] { 3 }, HandLimit = 2, GridOriginX = -1,
            DashContact = true, DashEffect = (int)ActionSystem.DashContactEffect.InitialDamage };

        [TestCase(-5000,false)] [TestCase(5000,true)]
        public void 外部報酬率を反映した開始計画はDTOに結果だけ保持し復元と別補正で再抽選しない(int bonus,bool offered)
        {
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,Encounter(),definitions,3,null,c=>"reward/v1/"+c.Id,out var plan,out var error,normalRewardRateBonusBasisPoints:bonus),error);
            var saved=plan.Snapshot;Assert.AreEqual(offered,saved.Battle.RewardOffered);Assert.AreEqual(departure.SchemaVersion,saved.SchemaVersion);
            var store=new MemoryCheckpointStore(definitions);Assert.IsTrue(store.TryWrite(saved,out error),error);
            Assert.IsFalse(store.TryWrite(saved,out error),"同一世代の再保存は拒否");
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,Encounter(),definitions,3,null,c=>"reward/v1/"+c.Id,out var opposite,out error,normalRewardRateBonusBasisPoints:-bonus),error);
            Assert.AreEqual(!offered,opposite.Snapshot.Battle.RewardOffered);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),definitions,out var restored,out error),error);
            Assert.AreEqual(offered,restored.Snapshot.Battle.RewardOffered);CollectionAssert.AreEqual(saved.Battle.RewardCandidates,restored.Snapshot.Battle.RewardCandidates);
            CollectionAssert.AreEqual(saved.Battle.FirstHand,restored.Snapshot.Battle.FirstHand);
        }
        [Test]
        public void 開始計画の無補正引数省略と明示ゼロは同じ保存結果と乱数状態になる()
        {
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,Encounter(),definitions,3,null,c=>"reward/v1/"+c.Id,out var legacy,out var error),error);
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,Encounter(),definitions,3,null,c=>"reward/v1/"+c.Id,out var zero,out error,normalRewardRateBonusBasisPoints:0),error);
            CollectionAssert.AreEqual(PortableCheckpointJson.Serialize(legacy.Snapshot),PortableCheckpointJson.Serialize(zero.Snapshot));
        }

        private CheckpointBattlePlan FindPlan(bool offered)
        {
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, seed, null,
                    c => "reward/v1/" + c.Id, out var plan, out var error), error);
                if (plan.Snapshot.Battle.RewardOffered == offered) return plan;
            }
            Assert.Fail("fixture内で必要な抽選結果が得られませんでした"); return null;
        }

        [TestCase(CheckpointBoundary.Departure)]
        [TestCase(CheckpointBoundary.RewardCompleted)]
        public void 境界の往復で個体と定義と順序HP敵配置を維持する(CheckpointBoundary boundary)
        {
            departure.Boundary = boundary;
            if (boundary == CheckpointBoundary.RewardCompleted) departure.Enemies[0].Hp = 0;
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(departure, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            Assert.AreEqual(93, restored.Player.CurrentHp);
            Assert.AreEqual(100, restored.Player.MaxHp);
            Assert.AreEqual(45, restored.Deck.GetAllCards()[0].Power);
            Assert.AreEqual(ElementType.Fire, restored.Deck.GetAllCards()[0].Element);
            Assert.AreNotSame(restored.Cards["a"], restored.Cards["b"]);
            Assert.AreSame(restored.Cards["c"], restored.Obtained[1]);
            Assert.IsFalse(restored.Deck.GetAllCards().Contains(restored.Cards["discarded"]));
            Assert.AreEqual(departure.Enemies[0].Hp, restored.Enemies[3].CurrentHp);
            Assert.AreEqual(10, restored.Map.TotalCellCount);
            if (boundary == CheckpointBoundary.RewardCompleted) Assert.IsNull(restored.Map.MapCells[3].Enemy);
            else Assert.AreSame(restored.Enemies[3], restored.Map.MapCells[3].Enemy);
            Assert.AreEqual(new Vector2Int(4, 0), restored.Enemies[3].GridPosition);
            CollectionAssert.AreEqual(departure.DeckOrder, restored.Snapshot.DeckOrder);
            CollectionAssert.AreEqual(departure.ObtainedOrder, restored.Snapshot.ObtainedOrder);
            // 保存前・読出後・復元後の編集はいずれも保存先に漏れない。
            departure.Player.Hp = 1; store.Read().Cards[0].GrantKeys = null;
            restored.Cards["a"].Modifier.BonusDamage = 999;
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var again, out error), error);
            Assert.AreEqual(93, again.Player.CurrentHp); Assert.AreEqual(30, again.Cards["a"].GetFinalPower());
        }

        [Test]
        public void 開始計画の抽選は一回で保存再試行と復元では乱数を使わない()
        {
            var plan = FindPlan(true);
            string error;
            int calls = plan.RandomCalls; Assert.Greater(calls, 0);
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(plan.Snapshot, out error), error);
            Assert.IsFalse(store.TryWrite(plan.Snapshot, out error)); // 同一世代の二重確定
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            Assert.AreEqual(2, restored.FirstHand.Count); Assert.AreEqual(1, restored.Deck.RemainingCount);
            Assert.AreEqual(3, restored.RewardCandidates.Count);
            Assert.AreNotSame(restored.RewardCandidates[0], restored.RewardCandidates[1]);
            var remaining = restored.Deck.DrawCards(3, new MockRandomProvider());
            Assert.IsFalse(remaining.Exists(c => restored.FirstHand.Contains(c)));
            Assert.AreEqual(calls, plan.RandomCalls);
            Assert.IsTrue(restored.Snapshot.Battle.DashContact);
            Assert.AreEqual(-1, restored.Snapshot.Battle.GridOriginX);
            Assert.AreEqual(CheckpointBoundary.Departure, departure.Boundary);
            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, departure.DeckOrder);
            var edited = plan.Snapshot; edited.Battle.FirstHand[0] = "bad";
            Assert.AreNotEqual("bad", plan.Snapshot.Battle.FirstHand[0]);
        }

        [Test]
        public void 報酬なしも確定結果として保存する()
        {
            var plan = FindPlan(false);
            string error;
            Assert.IsFalse(plan.Snapshot.Battle.RewardOffered);
            Assert.IsEmpty(plan.Snapshot.Battle.RewardCandidates);
            Assert.IsTrue(CheckpointCodec.TryRestore(plan.Snapshot, definitions, out _, out error), error);
        }

        [TestCase("schema")][TestCase("revision")][TestCase("map")][TestCase("definition")]
        [TestCase("hash")][TestCase("history")][TestCase("duplicate")][TestCase("missing")]
        [TestCase("hp")][TestCase("enemy")][TestCase("boundary")][TestCase("null")]
        public void 不正データの保存と復元は既存世代と入力を変更しない(string fault)
        {
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(departure, out _));
            var bad = departure.Copy(); bad.Sequence = 2;
            switch (fault) {
                case "schema": bad.SchemaVersion++; break;
                case "revision": bad.DefinitionsRevision = "old"; break;
                case "map": bad.MapHash = "old"; break;
                case "definition": bad.Cards[0].DefinitionKey = "unknown"; break;
                case "hash": bad.Cards[0].ValueHash = "wrong"; break;
                case "history": bad.Cards[0].TuningCount = 1; break;
                case "duplicate": bad.Cards[1].InstanceId = "a"; break;
                case "missing": bad.DeckOrder[0] = "unknown"; break;
                case "hp": bad.Player.Hp = 101; break;
                case "enemy": bad.Enemies = new[] { bad.Enemies[0] }; break;
                case "boundary": bad.Boundary = CheckpointBoundary.BattleStart; break;
                case "null": bad.Cards = null; break;
            }
            Assert.IsFalse(store.TryWrite(bad, out _));
            Assert.IsFalse(CheckpointCodec.TryRestore(bad, definitions, out var restored, out _));
            Assert.IsNull(restored); Assert.AreEqual(1, store.Read().Sequence); Assert.AreEqual(2, bad.Sequence);
            Assert.AreEqual(93, store.Read().Player.Hp);
        }

        [Test]
        public void 調律履歴を往復し旧履歴と定義改変を推測で補わない()
        {
            var grant = new TuningGrantDefinition("bonus", new[] { new TuningEffectDefinition("bonus", 1,
                new System.Collections.Generic.Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 3 } } }) });
            definitions.RegisterGrant("grant/v1/bonus", grant);
            var tuned = TuningEvaluator.Evaluate(standard, grant).ResultCard;
            departure.Cards[0] = definitions.CaptureCard("a", "standard/v1/sword", tuned, "grant/v1/bonus");
            Assert.IsTrue(CheckpointCodec.TryRestore(departure, definitions, out var restored, out var error), error);
            Assert.AreEqual(33, restored.Cards["a"].GetFinalPower()); Assert.AreEqual(1, restored.Cards["a"].TuningHistory.Count);
            Assert.Throws<System.ArgumentException>(() => definitions.CaptureCard("legacy", "standard/v1/sword", standard.ApplyTuning(fallback)));
            var changed = new CheckpointDefinitions("fixture-v1", map);
            changed.RegisterCard("standard/v1/sword", fallback);
            Assert.IsFalse(CheckpointCodec.TryRestore(departure, changed, out _, out _));
        }

        [TestCase("hand")][TestCase("reward")][TestCase("participant")][TestCase("grid")][TestCase("dash")]
        public void 戦闘計画の不整合は復元も上書きも拒否する(string fault)
        {
            var plan = FindPlan(true);
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(plan.Snapshot, out _));
            var bad = plan.Snapshot; bad.Sequence++;
            switch (fault) {
                case "hand": bad.Battle.FirstHand[0] = bad.Battle.FirstHand[1]; break;
                case "reward": bad.Battle.RewardOffered = false; break;
                case "participant": bad.Enemies[0].Hp = 0; break;
                case "grid": bad.Enemies[0].GridX = 6; break;
                case "dash": bad.Battle.DashEffect = 99; break;
            }
            Assert.IsFalse(store.TryWrite(bad, out _)); Assert.AreEqual(plan.Snapshot.Sequence, store.Read().Sequence);
        }

        [Test]
        public void 空デッキとボス確定報酬も復元できる()
        {
            departure.DeckOrder = new string[0]; departure.ObtainedOrder = new string[0]; departure.Cards = new CheckpointCardDto[0];
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                definitions.RegisterCard("boss/v1/" + id, new Card(id, id + "_card", 1, CardRarity.Limited,
                    CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "Fallback card"));
            var encounter = Encounter(); encounter.ContactCell = 9; encounter.ParticipantCells = new[] { 9 };
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "boss/v1/" + c.Id, out var plan, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(plan.Snapshot, definitions, out var restored, out error), error);
            Assert.IsEmpty(restored.FirstHand); Assert.AreEqual(0, restored.Deck.TotalCount);
            Assert.IsTrue(restored.Snapshot.Battle.RewardOffered); Assert.AreEqual(1, restored.RewardCandidates.Count);
        }

        [Test]
        public void 計画生成後の定義不足は入力と既存保存を変更しない()
        {
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(departure, out _));
            var encounter = Encounter(); encounter.ContactCell = 9; encounter.ParticipantCells = new[] { 9 };
            Assert.IsFalse(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "unknown", out var plan, out _));
            Assert.IsNull(plan); Assert.AreEqual(1, store.Read().Sequence); Assert.AreEqual(1, departure.Sequence);
            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, departure.DeckOrder);
        }

        [Test]
        public void 同じ計画Seedで別候補を作っても結果が一致し失敗試行は乱数を共有しない()
        {
            CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null,
                c => "reward/v1/" + c.Id, out var first, out var error);
            Assert.NotNull(first, error);
            CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null, c => "unknown", out _, out _);
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null,
                c => "reward/v1/" + c.Id, out var second, out error), error);
            CollectionAssert.AreEqual(first.Snapshot.DeckOrder, second.Snapshot.DeckOrder);
            CollectionAssert.AreEqual(first.Snapshot.Battle.FirstHand, second.Snapshot.Battle.FirstHand);
            CollectionAssert.AreEqual(first.Snapshot.Cards.Select(c => c.ValueHash), second.Snapshot.Cards.Select(c => c.ValueHash));
            Assert.AreEqual(first.RandomCalls, second.RandomCalls);
        }

        [Test]
        public void 不正接敵は抽選前に失敗し旧保存を維持する()
        {
            var encounter = Encounter(); encounter.ParticipantCells = new[] { 9 };
            Assert.IsFalse(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "reward/v1/" + c.Id, out var plan, out _));
            Assert.IsNull(plan); Assert.AreEqual(1, departure.Sequence);
        }
    }
}

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class SweptDungeonContactTests
    {
        private static MapCell EnemyCell(int index, float height = 0, bool boss = false) => new MapCell(index,
            new Enemy(boss ? "boss" : "enemy_" + index, "接触fixture", 50, ElementType.None, new Vector2Int(index, 0)), height);
        private static MapCell Trap(int index, int damage = 15) => new MapCell(index, new Obstacle("trap_" + index, "接触fixture", damage));
        private static DungeonConfig Config(params MapCell[] placed)
        {
            var cells = new System.Collections.Generic.List<MapCell>(); for (int i = 0; i < 40; i++) cells.Add(new MapCell(i));
            cells[39] = EnemyCell(39, boss: true); foreach (var cell in placed) cells[cell.Index] = cell;
            return new DungeonConfig("swept-fixture", "接触fixture", 40, cells);
        }
        private static DungeonSystem Create(params MapCell[] placed) => new DungeonSystem(Config(placed), new PlayerCharacter(100), new Deck(), new MockRandomProvider(0, 1), new MockLogger());
        [TestCase(false, 6)] [TestCase(true, 6)]
        [TestCase(false, 16)] [TestCase(true, 16)]
        public void ダッシュジャンプの空中と着地後の接敵は区間分割によらず通常距離で止まる(bool split, int cell)
        {
            var game = Create(EnemyCell(cell, height: cell == 6 ? 1.5f : 0f)); ContactInfo contact = null;
            game.OnBattleStart += c => contact = c; game.StartDash(); game.Jump();
            if (split) { for (int i = 0; i < 90 && !game.IsInBattle; i++) game.TickFrame(); }
            else game.Tick(1500);
            Assert.IsNotNull(contact); Assert.IsFalse(contact.IsDashContact); Assert.IsNull(contact.DashEffect);
            Assert.AreEqual(cell - 1f, game.PlayerPosition, .001f);
            Assert.AreEqual(50, game.CurrentBattle.Enemies[0].CurrentHp);
            // 接敵で停止したジャンプを、戦闘復帰時に再開しない。
            game.CurrentBattle.Enemies[0].TakeDamage(50, ElementType.None);
            Assert.IsFalse(game.IsDungeonCleared);
            game.ResumePlayerMovement(); Assert.IsFalse(game.IsJumping); Assert.IsFalse(game.IsDashJumping);
            Assert.IsFalse(game.IsDashing); Assert.AreEqual(0, game.PlayerHeight);
        }
        [TestCase(false, 4f)][TestCase(true, 3.5f)]
        public void 大きな経過時間でも最初の敵の接触位置で止まり余剰移動を捨てる(bool dash, float expected)
        {
            var game = Create(EnemyCell(5), EnemyCell(12)); int starts = 0; game.OnBattleStart += _ => starts++;
            if (dash) game.StartDash(); game.Tick(1000);
            Assert.AreEqual(1, starts); Assert.IsTrue(game.IsInBattle); Assert.AreEqual(expected, game.PlayerPosition, .001f);
            game.Tick(1000); Assert.AreEqual(1, starts); Assert.AreEqual(expected, game.PlayerPosition, .001f);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 可変フレームでも障害物を通過順に一度ずつ処理して敵で止まる(int schedule)
        {
            var game = Create(Trap(2), Trap(4), EnemyCell(8), Trap(10)); var events = new System.Collections.Generic.List<string>();
            game.OnObstaclePenalty += p => events.Add("trap"); game.OnBattleStart += _ => events.Add("battle");
            int[][] frames = { new[] { 1000 }, new[] { 16, 333, 7, 500, 900 }, new[] { 100, 40, 260, 1, 1000 } };
            foreach (int ms in frames[schedule]) game.Tick(ms);
            CollectionAssert.AreEqual(new[] { "trap", "trap", "battle" }, events); Assert.AreEqual(70, game.Player.CurrentHp);
            Assert.AreEqual(7f, game.PlayerPosition, .001f);
        }
        [TestCase(false, 85)][TestCase(true, 100)]
        public void 敵と障害物の前後はセル番号ではなく既存接触距離の位置順で決まる(bool dash, int hp)
        {
            var game = Create(Trap(4), EnemyCell(5)); if (dash) game.StartDash(); game.Tick(1000);
            Assert.IsTrue(game.IsInBattle); Assert.AreEqual(hp, game.Player.CurrentHp);
        }
        [Test] public void 障害物通知からTickが再入しても移動と同じ罠を二重処理しない()
        {
            var game = Create(Trap(2), EnemyCell(5)); int hits = 0, starts = 0;
            game.OnObstaclePenalty += _ => { hits++; game.Tick(1000); }; game.OnBattleStart += _ => starts++;
            game.Tick(1000); Assert.AreEqual(1, hits); Assert.AreEqual(1, starts); Assert.AreEqual(85, game.Player.CurrentHp);
            Assert.AreEqual(4f, game.PlayerPosition, .001f);
        }
        [Test] public void 障害物の効果を確定してから通知し再入による戦闘開始にも反映する()
        {
            var game = Create(Trap(2), EnemyCell(8)); int notifiedHp = 0;
            game.OnObstaclePenalty += _ => { notifiedHp = game.Player.CurrentHp; game.StartBattle(new ContactInfo {
                Enemy = game.Map.GetCell(8).Enemy, EnemyCellIndex = 8, ContactPositionX = game.PlayerPosition }); };
            game.Tick(1000); Assert.AreEqual(85, notifiedHp); Assert.IsTrue(game.IsInBattle);
            Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void 開始通知中に戦闘が完了して再開されても外側Tickの残りを進めない()
        {
            var game = Create(EnemyCell(5), EnemyCell(20)); int starts = 0;
            game.OnBattleStart += _ => { starts++; game.CurrentBattle.Enemies[0].TakeDamage(50, ElementType.None); game.ResumePlayerMovement(); game.Tick(1000); };
            game.Tick(1000); Assert.AreEqual(1, starts); Assert.AreEqual(4f, game.PlayerPosition, .001f);
        }
        [Test] public void 致死障害物以降の敵へ進まず敗北を一度だけ通知する()
        {
            var game = Create(Trap(2, 150), EnemyCell(3)); int starts = 0, deaths = 0;
            game.OnBattleStart += _ => starts++; game.OnDungeonFailed += () => deaths++;
            game.Tick(1000); game.Tick(1000);
            Assert.AreEqual(1, deaths); Assert.AreEqual(0, starts); Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void ジャンプ中に通過した通常敵を終点の着地高度で接触扱いしない()
        {
            var game = Create(EnemyCell(3), EnemyCell(8, boss: true)); int contacted = -1;
            game.OnBattleStart += c => contacted = c.EnemyCellIndex; game.Jump(); game.Tick(1000);
            Assert.AreEqual(8, contacted); Assert.AreEqual(7f, game.PlayerPosition, .001f);
        }
        [Test] public void 空中敵は区間途中の到達高度で接触し着地後の終点高度を使わない()
        {
            var game = Create(EnemyCell(3, 1.5f)); int contacted = -1;
            game.OnBattleStart += c => contacted = c.EnemyCellIndex; game.Jump(); game.Tick(1000);
            Assert.AreEqual(3, contacted); Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void 敵の接触範囲内で下降して回避高度を下回った時点を見落とさない()
        {
            var game = Create(EnemyCell(5)); game.Jump(); game.Tick(1000);
            Assert.IsTrue(game.IsInBattle); Assert.Greater(game.PlayerPosition, 4.4f); Assert.Less(game.PlayerPosition, 4.5f);
        }
        [Test] public void 後退区間でも最初に横切る既存前方検出範囲を一度だけ通知する()
        {
            var map = new DungeonMap(Config(EnemyCell(5), EnemyCell(15))); var detector = new ContactSystem(map);
            var movement = new PlayerMovement(20f); int cell = -1, calls = 0;
            detector.OnEnemyContact += c => { cell = c.EnemyCellIndex; calls++; detector.CheckContact(movement); };
            movement.Update(-2000); detector.CheckContact(movement); detector.CheckContact(movement);
            Assert.AreEqual(15, cell); Assert.AreEqual(1, calls); Assert.That(movement.CurrentPosition, Is.InRange(14f, 15f));
        }
        [Test] public void 後退で複数障害物を順に処理し方向を戻しても二重被弾しない()
        {
            var game = Create(Trap(15), Trap(10));
            var movement = (PlayerMovement)typeof(DungeonSystem).GetField("playerMovement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game);
            movement.SetPosition(20f);
            var hp = new System.Collections.Generic.List<int>(); game.OnObstaclePenalty += _ => hp.Add(game.Player.CurrentHp);
            game.Tick(-1500); game.Tick(1500);
            CollectionAssert.AreEqual(new[] { 85, 70 }, hp); Assert.AreEqual(20f, game.PlayerPosition);
        }
        [Test] public void 最大経過時間でもセル数に応じて処理して最初の敵で停止する()
        {
            var game = Create(Trap(2), EnemyCell(8)); game.Tick(int.MaxValue);
            Assert.IsTrue(game.IsInBattle); Assert.AreEqual(85, game.Player.CurrentHp); Assert.AreEqual(7f, game.PlayerPosition);
        }
        [Test] public void ジャンプ回避した障害物を終点の着地高度で再処理しない()
        {
            var game = Create(Trap(2), EnemyCell(8)); game.Jump(); game.Tick(1000);
            Assert.AreEqual(100, game.Player.CurrentHp); Assert.IsTrue(game.IsInBattle);
        }
        [TestCase(0)][TestCase(1)]
        public void 下降中の接触位置はフレーム分割に依存しない(int partition)
        {
            var game = Create(EnemyCell(5)); game.Jump();
            foreach (int ms in partition == 0 ? new[] { 1000 } : new[] { 17, 111, 33, 239, 100, 500 }) game.Tick(ms);
            Assert.IsTrue(game.IsInBattle); Assert.That(game.PlayerPosition, Is.InRange(4.458f, 4.460f));
        }
    }
}


namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class EnemyDashContactTests
    {
        private static DungeonConfig Map(EnemyDashContactDefinition definition=null,bool boss=true,int enemyHp=200)
        {var m=DungeonConfig.CreateTestDungeon(5,30,4);m.MapCells[4]=new MapCell(4,new Enemy(boss?"boss":"foe","旧dash",enemyHp,ElementType.None,new Vector2Int(4,0),dashContact:definition));return m;}
        private static DungeonBattleManager Game(DungeonConfig map,DungeonCheckpointSession session=null,int hp=100)
            =>new DungeonBattleManager(map,new PlayerCharacter(hp),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
        private static void Contact(DungeonBattleManager g,bool dash,bool jump=false)
        {if(dash)g.StartDash();if(jump)g.Jump();for(int i=0;i<120&&g.CurrentBattle==null&&!g.Dungeon.IsCheckpointBlocked;i++)g.TickFrame(17);}
        [TestCase(DashContactOutcome.InitialDamage,23,false)] [TestCase(DashContactOutcome.Stun,45,false)] [TestCase(DashContactOutcome.FirstStrike,0,false)] [TestCase(DashContactOutcome.PlayerDamage,7,false)]
        [TestCase(DashContactOutcome.InitialDamage,23,true)] [TestCase(DashContactOutcome.Stun,45,true)] [TestCase(DashContactOutcome.FirstStrike,0,true)] [TestCase(DashContactOutcome.PlayerDamage,7,true)]
        public void 通常接触は適用せずダッシュだけ接触先の個別結果を一度適用する(DashContactOutcome outcome,int value,bool dash)
        {
            var g=Game(Map(new EnemyDashContactDefinition(outcome,value)));int starts=0;ContactInfo info=null;g.OnBattleStarted+=(_,c)=>{starts++;info=c;};Contact(g,dash);Assert.IsNotNull(g.CurrentBattle);var e=g.CurrentBattle.Enemies[0];Assert.AreEqual(dash,info.IsDashContact);Assert.AreEqual(dash,(bool)info.DashEffect.HasValue);Assert.AreEqual(dash?(int?)value:null,info.DashValue);
            Assert.AreEqual(dash&&outcome==DashContactOutcome.PlayerDamage?100-value:100,g.CurrentBattle.Player.CurrentHp);Assert.AreEqual(dash&&outcome==DashContactOutcome.InitialDamage?200-value:200,e.CurrentHp);Assert.AreEqual(dash&&outcome==DashContactOutcome.Stun,e.IsStunned);if(dash&&outcome==DashContactOutcome.FirstStrike)Assert.IsTrue(g.CurrentBattle.CustomGauge.IsFull);
            int hp=g.CurrentBattle.Player.CurrentHp,ehp=e.CurrentHp;g.SetUserPaused(true);for(int i=0;i<20;i++)g.TickFrame(17);g.Dungeon.StartBattle(info);g.ActivateCheckpoint();for(int i=0;i<20;i++)g.TickFrame(17);Assert.AreEqual(1,starts);Assert.AreEqual(hp,g.CurrentBattle.Player.CurrentHp);Assert.AreEqual(ehp,e.CurrentHp);g.SetUserPaused(false);
            if(dash&&outcome==DashContactOutcome.Stun){g.CurrentBattle.CustomScreen.Close();for(int i=0;i<value-1;i++)g.TickFrame(17);Assert.IsTrue(e.IsStunned);g.TickFrame(17);Assert.IsFalse(e.IsStunned);}
        }
        [TestCase(DashContactOutcome.InitialDamage,23)] [TestCase(DashContactOutcome.Stun,45)] [TestCase(DashContactOutcome.FirstStrike,0)] [TestCase(DashContactOutcome.PlayerDamage,7)]
        public void 開始前保存からの新規モデル再開は同じ初撃を再現し重複しない(DashContactOutcome outcome,int value)
        {
            var map=Map(new EnemyDashContactDefinition(outcome,value));var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new MemoryCheckpointStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=Game(map,session);Contact(g,true);Assert.IsNotNull(g.CurrentBattle,g.Dungeon.CheckpointError);var snapshot=store.Read();Assert.AreEqual(value,snapshot.Battle.DashValue);Assert.AreEqual((int)outcome,snapshot.Battle.DashEffect);Assert.AreEqual(100,snapshot.Player.Hp);Assert.AreEqual(200,snapshot.Enemies[0].Hp);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(g.SuspendForCheckpoint());restored.ActivateCheckpoint();restored.ActivateCheckpoint();Assert.AreEqual(g.CurrentBattle.Player.CurrentHp,restored.CurrentBattle.Player.CurrentHp);Assert.AreEqual(g.CurrentBattle.Enemies[0].CurrentHp,restored.CurrentBattle.Enemies[0].CurrentHp);Assert.AreEqual(outcome==DashContactOutcome.Stun,restored.CurrentBattle.Enemies[0].IsStunned);Assert.AreEqual(snapshot.Sequence,store.Read().Sequence);CollectionAssert.AreEqual(snapshot.Battle.FirstHand,store.Read().Battle.FirstHand);CollectionAssert.AreEqual(snapshot.Battle.RewardCandidates,store.Read().Battle.RewardCandidates);
            var invalid=snapshot.Copy();invalid.Battle.DashValue=value+1;Assert.IsFalse(CheckpointCodec.TryRestore(invalid,initial.Definitions,out _,out _));invalid=snapshot.Copy();invalid.Battle.DashValue=null;Assert.IsFalse(CheckpointCodec.TryRestore(invalid,initial.Definitions,out _,out _));invalid=snapshot.Copy();invalid.Battle.DashContact=false;Assert.IsFalse(CheckpointCodec.TryRestore(invalid,initial.Definitions,out _,out _));
            var fresh=initial.Definitions.CreateInitialMap().MapCells[4].Enemy;Assert.IsTrue(fresh.DashContact.HasSameValues(new EnemyDashContactDefinition(outcome,value)));Assert.AreEqual(200,fresh.CurrentHp);
        }
        private sealed class FailingStore:ICheckpointStore
        {private readonly MemoryCheckpointStore inner;public bool Fail;public FailingStore(CheckpointDefinitions d){inner=new MemoryCheckpointStore(d);}public CheckpointDto Read()=>inner.Read();public bool TryWrite(CheckpointDto d,out string error){if(Fail){error="fixture failure";return false;}return inner.TryWrite(d,out error);}}
        [Test]
        public void 開始保存失敗中は初撃せず再試行後に一度だけ発生する()
        {
            var map=Map(new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,7));var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new FailingStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=Game(map,session);long seq=store.Read().Sequence;store.Fail=true;int starts=0;g.OnBattleStarted+=(_,__)=>starts++;Contact(g,true);Assert.IsTrue(g.Dungeon.IsCheckpointBlocked);Assert.IsNull(g.CurrentBattle);Assert.AreEqual(100,g.Dungeon.Player.CurrentHp);Assert.AreEqual(seq,store.Read().Sequence);for(int i=0;i<100;i++)g.TickFrame(17);Assert.IsFalse(g.RetryCheckpoint());Assert.AreEqual(0,starts);store.Fail=false;Assert.IsTrue(g.RetryCheckpoint());Assert.AreEqual(93,g.CurrentBattle.Player.CurrentHp);Assert.AreEqual(1,starts);Assert.IsFalse(g.RetryCheckpoint());g.ActivateCheckpoint();Assert.AreEqual(93,g.CurrentBattle.Player.CurrentHp);Assert.AreEqual(1,starts);
        }
        [Test]
        public void 接触先の定義を巻込へ一度適用し巻込先の別定義を重ねない()
        {
            var cells=new System.Collections.Generic.List<MapCell>();for(int i=0;i<8;i++)cells.Add(new MapCell(i));cells[4]=new MapCell(4,new Enemy("a","接触",200,ElementType.None,new Vector2Int(4,0),dashContact:new EnemyDashContactDefinition(DashContactOutcome.InitialDamage,23)));cells[5]=new MapCell(5,new Enemy("b","巻込",200,ElementType.None,new Vector2Int(5,0),dashContact:new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,9)));var g=Game(new DungeonConfig("mixed","混在",8,cells));Contact(g,true);Assert.AreEqual(2,g.CurrentBattle.Enemies.Count);Assert.IsTrue(g.CurrentBattle.Enemies.All(e=>e.CurrentHp==177));Assert.AreEqual(100,g.CurrentBattle.Player.CurrentHp);
        }
        [Test]
        public void 空中接触はダッシュ結果を使わず非ボスのジャンプ回避も維持する()
        {
            var g=Game(Map(new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,7)));Contact(g,true,true);Assert.IsNotNull(g.CurrentBattle);Assert.AreEqual(100,g.CurrentBattle.Player.CurrentHp);Assert.AreEqual(200,g.CurrentBattle.Enemies[0].CurrentHp);
            g=Game(Map(new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,7),false));g.StartDash();g.Jump();for(int i=0;i<40;i++)g.TickFrame(17);Assert.IsNull(g.CurrentBattle);Assert.AreEqual(100,g.Dungeon.Player.CurrentHp);
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void 致死の初撃でも開始通知と終了通知を一度だけ行う(bool playerDamage,bool checkpoints)
        {
            var def=new EnemyDashContactDefinition(playerDamage?DashContactOutcome.PlayerDamage:DashContactOutcome.InitialDamage,300);var map=Map(def);DungeonCheckpointSession session=checkpoints?DungeonCheckpointSession.CreateDefault(map,new Deck()):null;var g=Game(map,session);int starts=0,ends=0;BattleSystem started=null;g.OnBattleStarted+=(b,__)=>{starts++;started=b;};g.OnBattleEnded+=()=>ends++;Contact(g,true);Assert.IsNotNull(started);Assert.IsTrue(started.IsGameOver);if(playerDamage&&checkpoints)Assert.AreEqual(CheckpointBoundary.Terminal,session.Store.Read().Boundary);Assert.AreEqual(1,starts);Assert.AreEqual(1,ends);for(int i=0;i<5;i++)g.TickFrame(17);Assert.AreEqual(1,ends);
        }
        [Test]
        public void 未設定の旧hashと初撃値を保ち不正設定や不整合の新値だけ拒否する()
        {
            var map=Map();var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());Assert.AreEqual("FD6B09962EE734F318E3FC8905339460CBB37E720D5FAFD732E88CDE71665090",initial.Definitions.MapHash);var store=new MemoryCheckpointStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=Game(map,session);Contact(g,true);Assert.AreEqual(185,g.CurrentBattle.Enemies[0].CurrentHp);Assert.IsNull(store.Read().Battle.DashValue);var copy=store.Read().Copy();copy.Battle.DashValue=15;Assert.IsFalse(CheckpointCodec.TryRestore(copy,initial.Definitions,out _,out _));Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);g.SuspendForCheckpoint();restored.ActivateCheckpoint();Assert.AreEqual(185,restored.CurrentBattle.Enemies[0].CurrentHp);
            Assert.Throws<System.ArgumentException>(()=>new EnemyDashContactDefinition((DashContactOutcome)99,0));Assert.Throws<System.ArgumentException>(()=>new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,-1));Assert.Throws<System.ArgumentException>(()=>new EnemyDashContactDefinition(DashContactOutcome.Stun,0));Assert.Throws<System.ArgumentException>(()=>new EnemyDashContactDefinition(DashContactOutcome.FirstStrike,1));Assert.AreNotEqual(initial.Definitions.MapHash,DungeonCheckpointSession.CreateDefault(Map(new EnemyDashContactDefinition(DashContactOutcome.InitialDamage,15)),new Deck()).Definitions.MapHash);
        }
        [Test]
        public void 敵別初撃と接触位置は描画fpsや追いつき処理で変化しない()
        {
            System.Collections.Generic.List<string> Trace(int fps){var g=Game(Map(new EnemyDashContactDefinition(DashContactOutcome.PlayerDamage,7)));g.StartDash();var c=new LogicalFrameClock();var result=new System.Collections.Generic.List<string>();long wall=0;void Tick(long f,int ms){if(f==2)g.SetUserPaused(true);if(f==6)g.SetUserPaused(false);g.TickFrame(ms);result.Add(g.CurrentPhase+":"+g.Dungeon.PlayerPosition+":"+g.Dungeon.Player.CurrentHp+":"+g.CurrentBattle?.Enemies[0].CurrentHp);}if(fps==0){c.Accumulate(LogicalFrameClock.WallTicksPerSecond);c.Drain(Tick);}else for(int i=1;c.Frame<60;i++){long t=(long)i*LogicalFrameClock.WallTicksPerSecond/fps;c.Accumulate(t-wall);wall=t;c.Drain(Tick);}return result;}
            CollectionAssert.AreEqual(Trace(60),Trace(30));CollectionAssert.AreEqual(Trace(60),Trace(120));CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}

namespace Olca.Tests.Dungeon
{
    public class FieldItemFlowTests
    {
        private sealed class FaultStore : ICheckpointStore {
            public readonly MemoryCheckpointStore Inner;public bool Fail;
            public FaultStore(CheckpointDefinitions definitions){Inner=new MemoryCheckpointStore(definitions);}
            public bool TryWrite(CheckpointDto d,out string error){if(Fail){error="field fixture save failure";return false;}return Inner.TryWrite(d,out error);}
            public CheckpointDto Read()=>Inner.Read();
        }
        private sealed class FaultIo : Olca.Unity.CheckpointFileIo {
            public string Fault;
            public override void Replace(string temporary,string destination,string backup){if(Fault=="before")throw new System.IO.IOException("field fixture before");base.Replace(temporary,destination,backup);if(Fault=="after")throw new System.IO.IOException("field fixture after");}
        }
        private static DungeonBattleManager Start(int hp,out DungeonCheckpointSession session,out FaultStore store,bool spike=false,int[] at=null)
        {
            var baseMap=DungeonConfig.CreateTestDungeon(8,100,7);foreach(int i in at??new[]{1,2})baseMap.MapCells[i]=new MapCell(i,new FieldItem("heal","ヒール",20));
            if(spike)baseMap.MapCells[3]=new MapCell(3,new Obstacle("existing-spike","既存トゲ",30));
            var map=new DungeonConfig(baseMap.DungeonId,baseMap.DungeonName,8,baseMap.MapCells,visits:new[]{new DungeonVisitDefinition(4,false,new[]{new DungeonVisitOption("heal",0,heal:1)},1)});
            var defaults=DungeonCheckpointSession.CreateDefault(map,new Deck());store=new FaultStore(defaults.Definitions);session=new DungeonCheckpointSession(defaults.Definitions,store);
            var player=new PlayerCharacter(100);if(hp<100)player.TakeDamage(100-hp);
            return new DungeonBattleManager(map,player,new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
        }
        [TestCase(60,100)][TestCase(100,100)]
        public void 複数接触は満タンでも一回ずつ消費し境界保存前にHPと取得を進め直さない(int hp,int expected)
        {
            var game=Start(hp,out var session,out var store);var d=game.Dungeon;long seq=store.Read().Sequence;d.Tick(1000);
            Assert.AreEqual(expected,d.Player.CurrentHp);Assert.IsTrue(d.IsItemCollected(1));Assert.IsTrue(d.IsItemCollected(2));Assert.IsNotNull(d.ActiveVisit);
            for(int i=0;i<10;i++)d.Tick(1000);Assert.AreEqual(expected,d.Player.CurrentHp);Assert.AreEqual(seq,store.Read().Sequence);Assert.IsEmpty(store.Read().CollectedItemCells);
            Assert.IsTrue(d.ExitVisit());CollectionAssert.AreEqual(new[]{1,2},store.Read().CollectedItemCells);Assert.AreEqual(4,store.Read().SchemaVersion);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var restored,out var error),error);Assert.AreEqual(expected,restored.Player.CurrentHp);Assert.IsNotNull(restored.Map.MapCells[1].Item);
            var copied=store.Read().Copy();copied.CollectedItemCells[0]=99;Assert.AreEqual(1,store.Read().CollectedItemCells[0]);
        }
        [Test]
        public void 同フレームの複数回復と障害物ダメージは合算し途中の過回復と死亡を確定しない()
        {
            var game=Start(5,out _,out _,spike:true);game.Dungeon.Tick(1000);
            Assert.AreEqual(15,game.Dungeon.Player.CurrentHp);Assert.IsFalse(game.Dungeon.Player.IsDead);Assert.IsNotNull(game.Dungeon.ActiveVisit);
        }
        [Test]
        public void 同フレームの毒と取得回復も既存HP集計へ一度だけ渡す()
        {
            var game=Start(5,out _,out _,at:new[]{0});var d=game.Dungeon;d.Player.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,10000,10),60);d.Tick(1000);
            Assert.AreEqual(15,d.Player.CurrentHp);Assert.IsTrue(d.IsItemCollected(0));Assert.IsFalse(d.Player.IsDead);
        }
        [Test]
        public void ポーズと停止選択は取得を進めず復帰後の取得済み地点は再処理しない()
        {
            var game=Start(60,out _,out _);game.SetUserPaused(true);for(int i=0;i<100;i++)game.TickFrame(17);Assert.AreEqual(60,game.Dungeon.Player.CurrentHp);Assert.IsFalse(game.Dungeon.IsItemCollected(1));
            game.SetUserPaused(false);for(int i=0;i<100&&game.Dungeon.ActiveVisit==null;i++)game.TickFrame(17);Assert.AreEqual(100,game.Dungeon.Player.CurrentHp);Assert.IsTrue(game.Dungeon.IsItemCollected(1));
            float pos=game.Dungeon.PlayerPosition;game.SetUserPaused(true);for(int i=0;i<50;i++)game.TickFrame(17);game.SetUserPaused(false);for(int i=0;i<50;i++)game.TickFrame(17);Assert.AreEqual(pos,game.Dungeon.PlayerPosition);Assert.AreEqual(100,game.Dungeon.Player.CurrentHp);
        }
        [Test]
        public void ジャンプ中に地上から離れたセルの回復を遠隔取得しない()
        {
            var game=Start(60,out _,out _,at:new[]{1});game.Dungeon.Jump();for(int i=0;i<30&&game.Dungeon.PlayerPosition<2;i++)game.TickFrame(17);
            Assert.IsFalse(game.Dungeon.IsItemCollected(1));Assert.AreEqual(60,game.Dungeon.Player.CurrentHp);
        }
        [Test]
        public void 保存失敗と旧地点ロードはHPと取得列を一緒に扱い再試行で二重回復しない()
        {
            var game=Start(60,out var session,out var store);var d=game.Dungeon;d.Tick(1000);store.Fail=true;Assert.IsFalse(d.ExitVisit());Assert.IsTrue(d.IsCheckpointBlocked);
            for(int i=0;i<10;i++)d.Tick(1000);Assert.AreEqual(100,d.Player.CurrentHp);Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var before,out var error),error);Assert.AreEqual(60,before.Player.CurrentHp);Assert.IsEmpty(before.Snapshot.CollectedItemCells);
            store.Fail=false;Assert.IsTrue(game.RetryCheckpoint());Assert.IsFalse(game.RetryCheckpoint());Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,store),new MockRandomProvider(),new MockLogger(),out var resumed,out error),error);
            resumed.Dungeon.ActivateCheckpoint();for(int i=0;i<3;i++)resumed.TickFrame(17);Assert.AreEqual(100,resumed.Dungeon.Player.CurrentHp);Assert.IsTrue(resumed.Dungeon.IsItemCollected(1));Assert.IsTrue(resumed.Dungeon.IsItemCollected(2));
        }
        [TestCase("before")][TestCase("after")]
        public void 実ファイル保存前後のエラーと再起動でも保存済み取得とHPを一度だけ復元する(string fault)
        {
            Start(60,out var original,out var seed);var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-field-"+System.Guid.NewGuid().ToString("N"));
            try{var io=new FaultIo();var disk=new Olca.Unity.FileCheckpointStore(original.Definitions,directory,io);Assert.IsTrue(disk.TryWrite(seed.Read(),out var error),error);
                Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(original.Definitions,disk),new MockRandomProvider(),new MockLogger(),out var game,out error),error);game.Dungeon.ActivateCheckpoint();game.Dungeon.Tick(1000);io.Fault=fault;
                if(!game.Dungeon.ExitVisit()){Assert.IsTrue(game.Dungeon.IsCheckpointBlocked);io.Fault=null;Assert.IsTrue(game.RetryCheckpoint());}
                var reopened=new Olca.Unity.FileCheckpointStore(original.Definitions,directory);Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(),original.Definitions,out var result,out error),error);Assert.AreEqual(100,result.Player.CurrentHp);CollectionAssert.AreEqual(new[]{1,2},result.Snapshot.CollectedItemCells);
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [TestCase("missing")][TestCase("duplicate")][TestCase("unknown")][TestCase("ahead")][TestCase("version")][TestCase("departure")]
        public void 不正取得列と版を拒否する(string kind)
        {
            var game=Start(60,out var session,out var store);game.Dungeon.Tick(1000);Assert.IsTrue(game.Dungeon.ExitVisit());var bad=store.Read();
            if(kind=="missing")bad.CollectedItemCells=null;if(kind=="duplicate")bad.CollectedItemCells=new[]{1,1};if(kind=="unknown")bad.CollectedItemCells=new[]{99};if(kind=="ahead"){bad.PositionMilli=0;bad.CollectedItemCells=new[]{2};}if(kind=="version")bad.SchemaVersion=1;if(kind=="departure")bad.Boundary=CheckpointBoundary.Departure;
            Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));
        }
        [Test]
        public void 画面内表示は取得後と無効化とモデル交換で消え描画では回復しない()
        {
            var game=Start(60,out _,out _);var root=new UnityEngine.GameObject("FieldItemVisualTest");var cameraObject=new UnityEngine.GameObject("FieldItemCamera");
            try{var camera=cameraObject.AddComponent<UnityEngine.Camera>();camera.orthographic=true;camera.orthographicSize=3;camera.aspect=1;camera.transform.position=new UnityEngine.Vector3(2.5f,0,-10);
                var view=root.AddComponent<Olca.Presentation.Field.GameFieldView>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;view.GetType().GetField("fieldCamera",flags).SetValue(view,camera);view.Initialize(game.Dungeon);
                Assert.IsNotNull(root.transform.Find("FieldHealCell_1"));for(int i=0;i<100;i++)view.RefreshFieldItemVisuals();Assert.AreEqual(60,game.Dungeon.Player.CurrentHp);Assert.AreEqual(1,root.GetComponentsInChildren<UnityEngine.SpriteRenderer>().Count(x=>x.name=="HealVertical"&&x.transform.parent.name=="FieldHealCell_1"));
                game.Dungeon.Tick(1000);view.RefreshFieldItemVisuals();Assert.IsNull(root.transform.Find("FieldHealCell_1"));Assert.IsNull(root.transform.Find("FieldHealCell_2"));
                var replacement=Start(60,out _,out _);view.Initialize(replacement.Dungeon);Assert.IsNotNull(root.transform.Find("FieldHealCell_1"));view.enabled=false;view.GetType().GetMethod("OnDisable",flags).Invoke(view,null);view.RefreshFieldItemVisuals();Assert.IsNull(root.transform.Find("FieldHealCell_1"));
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}

namespace Olca.Tests.Dungeon
{
    public class ExplorationGroundTests
    {
        private sealed class FaultStore:ICheckpointStore {
            public readonly MemoryCheckpointStore Inner;public bool Fail;
            public FaultStore(CheckpointDefinitions d){Inner=new MemoryCheckpointStore(d);}
            public CheckpointDto Read()=>Inner.Read();
            public bool TryWrite(CheckpointDto d,out string error){if(Fail){error="ground-save-fixture";return false;}return Inner.TryWrite(d,out error);}
        }
        private static DungeonBattleManager Start(ElementType element,ExplorationTerrainCell[] terrain,out DungeonCheckpointSession session,out FaultStore store,int hp=60,bool spike=false)
        {
            var m=DungeonConfig.CreateTestDungeon(30,100,29);if(spike)m.MapCells[0]=new MapCell(0,new Obstacle("spike","既存トゲ",10));
            var map=new DungeonConfig(m.DungeonId,m.DungeonName,30,m.MapCells,visits:new[]{new DungeonVisitDefinition(10,false,new[]{new DungeonVisitOption("existing-heal",0,heal:1)},1)},explorationTerrain:terrain);
            var defs=DungeonCheckpointSession.CreateDefault(map,new Deck()).Definitions;store=new FaultStore(defs);session=new DungeonCheckpointSession(defs,store);
            var p=new PlayerCharacter(100);p.TakeDamage(100-hp);
            if(element!=ElementType.None)p.SetDepartureStyle(new DepartureStyleDefinition("fixture-"+element,"属性検証fixture",null,new TransformationAttackDefinition(element)));
            return new DungeonBattleManager(map,p,new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
        }
        private static PlayerMovement Movement(DungeonSystem d)=>(PlayerMovement)typeof(DungeonSystem).GetField("playerMovement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(d);
        [TestCase(ElementType.Fire,PanelType.Magma,65)][TestCase(ElementType.Earth,PanelType.Grass,65)]
        [TestCase(ElementType.None,PanelType.Grass,60)][TestCase(ElementType.None,PanelType.Magma,55)][TestCase(ElementType.Earth,PanelType.Magma,55)]
        public void 属性に応じた接地効果は同じセルで繰り返さず踏み直し時だけ再適用する(ElementType element,PanelType type,int hp)
        {
            var g=Start(element,new[]{new ExplorationTerrainCell(0,type)},out _,out _);var d=g.Dungeon;
            d.TickFrame();Assert.AreEqual(hp,d.Player.CurrentHp);for(int i=0;i<4;i++)d.TickFrame();Assert.AreEqual(hp,d.Player.CurrentHp);
            d.TickFrame();Movement(d).SetPosition(0);d.TickFrame();Assert.AreEqual(hp+(hp-60),d.Player.CurrentHp);
        }
        [Test]
        public void 満タンとフロートとポーズは継続回復や二重処理を作らない()
        {
            var g=Start(ElementType.Fire,new[]{new ExplorationTerrainCell(0,PanelType.Magma)},out _,out _,100);var d=g.Dungeon;
            d.TickFrame();d.Player.TakeDamage(10);d.TickFrame();Assert.AreEqual(90,d.Player.CurrentHp);
            g.SetUserPaused(true);for(int i=0;i<60;i++)g.TickFrame(17);Assert.AreEqual(90,d.Player.CurrentHp);g.SetUserPaused(false);g.TickFrame(17);Assert.AreEqual(90,d.Player.CurrentHp);
            var other=Start(ElementType.None,new[]{new ExplorationTerrainCell(0,PanelType.Magma)},out _,out _).Dungeon;
            other.Player.TerrainState.IsFloating=true;other.TickFrame();Assert.AreEqual(60,other.Player.CurrentHp);
            other.Player.TerrainState.IsFloating=false;other.TickFrame();Assert.AreEqual(55,other.Player.CurrentHp);
        }
        [TestCase(false,5)][TestCase(true,10)]
        public void ジャンプ中は通過セルを避け着地セルだけ一回回復する(bool dash,int landing)
        {
            var terrain=Enumerable.Range(0,landing+1).Select(i=>new ExplorationTerrainCell(i,PanelType.Grass)).ToArray();
            var d=Start(ElementType.Earth,terrain,out _,out _).Dungeon;if(dash)d.StartDash();d.Jump();
            for(int i=0;i<29;i++)d.TickFrame();Assert.AreEqual(60,d.Player.CurrentHp);
            d.TickFrame();Assert.AreEqual(65,d.Player.CurrentHp);Assert.AreEqual(landing,(int)d.PlayerPosition);
        }
        [Test]
        public void ダッシュで異なる草むらマグマへ進んでも各セルは一回で地形を消費しない()
        {
            var d=Start(ElementType.Fire,new[]{new ExplorationTerrainCell(0,PanelType.Magma),new ExplorationTerrainCell(1,PanelType.Grass),new ExplorationTerrainCell(2,PanelType.Magma)},out _,out _).Dungeon;
            d.StartDash();for(int i=0;i<8;i++)d.TickFrame();Assert.AreEqual(70,d.Player.CurrentHp);
            Assert.AreEqual(PanelType.Magma,d.Map.GetTerrain(0).Type);Assert.AreEqual(PanelType.Grass,d.Map.GetTerrain(1).Type);
        }
        [Test]
        public void 毒と地形回復とトゲは同フレームに合算し満タンで先に切り詰めない()
        {
            var d=Start(ElementType.Earth,new[]{new ExplorationTerrainCell(0,PanelType.Grass)},out _,out _,100,true).Dungeon;
            d.Player.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,10000,10),1);d.TickFrame();Assert.AreEqual(85,d.Player.CurrentHp);
            var dead=Start(ElementType.Earth,new[]{new ExplorationTerrainCell(0,PanelType.Grass)},out _,out _,5,true).Dungeon;int notices=0;dead.OnDungeonFailed+=()=>notices++;
            dead.TickFrame();Assert.AreEqual(0,dead.Player.CurrentHp);Assert.IsTrue(dead.Player.IsDead);dead.TickFrame();Assert.AreEqual(1,notices);
        }
        [Test]
        public void 接地済みセルの保存復帰と保存失敗再試行は同じ回復を受け直さない()
        {
            var g=Start(ElementType.Fire,new[]{new ExplorationTerrainCell(10,PanelType.Magma)},out var session,out var store);var d=g.Dungeon;
            d.Tick(1000);Assert.IsNotNull(d.ActiveVisit);Assert.AreEqual(65,d.Player.CurrentHp);store.Fail=true;Assert.IsFalse(d.ExitVisit());
            d.TickFrame();Assert.AreEqual(65,d.Player.CurrentHp);store.Fail=false;Assert.IsTrue(g.RetryCheckpoint());
            Assert.AreEqual(5,store.Read().SchemaVersion);Assert.AreEqual(10,store.Read().TerrainContactCell);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var resumed,out var error),error);
            resumed.ActivateCheckpoint();resumed.TickFrame(17);Assert.AreEqual(65,resumed.Dungeon.Player.CurrentHp);
            var bad=store.Read();bad.TerrainContactCell=9;Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));
        }
        [Test]
        public void 新しい階層では接地記録を空にし新規セルの回復を一回だけ受ける()
        {
            var g=Start(ElementType.Fire,new[]{new ExplorationTerrainCell(0,PanelType.Magma)},out var session,out _);g.Dungeon.TickFrame();
            var captured=(CheckpointDto)typeof(DungeonCheckpointSession).GetMethod("Capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(session,new object[]{g.Dungeon,CheckpointBoundary.RewardCompleted,null});
            var map=session.Definitions.CreateInitialMap();var floors=new FloorSequenceDefinitions("fixture-ground","v1",new[]{map,map},new Deck());
            var next=(CheckpointDto)typeof(FloorSequenceDefinitions).GetMethod("NextStart",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(floors,new object[]{1,captured});
            Assert.IsNull(next.TerrainContactCell);Assert.AreEqual(CheckpointBoundary.Departure,next.Boundary);
        }
        [Test]
        public void 接敵時に地形種類を引き継ぎ戦闘中の変更を探索や再開へ持ち越さない()
        {
            var m=DungeonConfig.CreateTestDungeon(20,100,5);var map=new DungeonConfig(m.DungeonId,m.DungeonName,20,m.MapCells,explorationTerrain:new[]{new ExplorationTerrainCell(3,PanelType.Grass)});
            var session=DungeonCheckpointSession.CreateDefault(map,new Deck());var g=new DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
            g.Dungeon.Tick(1000);Assert.IsNotNull(g.CurrentBattle);Assert.AreEqual(PanelType.Grass,g.CurrentBattle.Field.GetPanelType(new Vector2Int(2,0)));
            g.CurrentBattle.Field.ChangePanelType(new Vector2Int(2,0),PanelType.Normal);Assert.AreEqual(PanelType.Grass,g.Dungeon.Map.GetTerrain(3).Type);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);
            restored.ActivateCheckpoint();restored.TickFrame(17);Assert.AreEqual(PanelType.Grass,restored.CurrentBattle.Field.GetPanelType(new Vector2Int(2,0)));
        }
        [Test]
        public void 地形ありのファイル保存と読込は定義と接地記録を保持し旧地形なし版を変えない()
        {
            var g=Start(ElementType.Fire,new[]{new ExplorationTerrainCell(10,PanelType.Magma)},out var session,out _);g.Dungeon.Tick(1000);Assert.IsTrue(g.Dungeon.ExitVisit());
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-ground-"+System.Guid.NewGuid().ToString("N"));
            try{var file=new Olca.Unity.FileCheckpointStore(session.Definitions,dir);Assert.IsTrue(file.TryWrite(session.Store.Read(),out var error),error);var saved=file.Read();Assert.AreEqual(10,saved.TerrainContactCell);Assert.IsTrue(CheckpointCodec.TryRestore(saved,session.Definitions,out var restored,out error),error);Assert.AreEqual(1,restored.Map.ExplorationTerrain.Count);Assert.AreEqual(65,restored.Player.CurrentHp);}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
            var old=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(),new Deck());Assert.AreEqual(1,old.Definitions.SchemaVersion);
            Assert.Throws<System.ArgumentException>(()=>new DungeonConfig("invalid","重複",2,new System.Collections.Generic.List<MapCell>{new MapCell(0),new MapCell(1)},explorationTerrain:new[]{new ExplorationTerrainCell(0,PanelType.Grass),new ExplorationTerrainCell(0,PanelType.Magma)}));
        }
    }
}

namespace Olca.Tests.Dungeon
{
    public class ExplorationFallTests
    {
        private sealed class FaultStore:ICheckpointStore {
            public readonly MemoryCheckpointStore Inner;public bool Fail;
            public FaultStore(CheckpointDefinitions defs){Inner=new MemoryCheckpointStore(defs);}
            public CheckpointDto Read()=>Inner.Read();
            public bool TryWrite(CheckpointDto d,out string error){if(Fail){error="fall-fixture";return false;}return Inner.TryWrite(d,out error);}
        }
        private static DungeonBattleManager Start(ExplorationTerrainCell[] terrain,out DungeonCheckpointSession session,out FaultStore store,int hp=100,bool heal=false,int itemCell=0)
        {
            var m=DungeonConfig.CreateTestDungeon(30,100,29);
            if(heal)m.MapCells[itemCell]=new MapCell(itemCell,new FieldItem("heal","既存回復",20));
            var map=new DungeonConfig(m.DungeonId,m.DungeonName,30,m.MapCells,visits:new[]{new DungeonVisitDefinition(10,false,new[]{new DungeonVisitOption("heal",0,heal:1)},1)},explorationTerrain:terrain);
            var defs=DungeonCheckpointSession.CreateDefault(map,new Deck()).Definitions;store=new FaultStore(defs);session=new DungeonCheckpointSession(defs,store);
            var p=new PlayerCharacter(100);p.TakeDamage(100-hp);return new DungeonBattleManager(map,p,new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
        }
        private static PlayerMovement Movement(DungeonSystem d)=>(PlayerMovement)typeof(DungeonSystem).GetField("playerMovement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(d);
        private static void Recover(DungeonSystem d){for(int i=0;i<30;i++)d.TickFrame();Assert.IsFalse(d.IsTerrainRecovering);}
        [Test]
        public void ヒビは進入で割れず通常離脱と離陸で永続する穴へ変わる()
        {
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked)},out _,out _).Dungeon;
            d.TickFrame();Assert.AreEqual(PanelType.Cracked,d.Map.GetTerrainType(0));d.Tick(200);Assert.AreEqual(PanelType.Hole,d.Map.GetTerrainType(0));
            for(int i=0;i<100;i++)d.TickFrame();Assert.AreEqual(PanelType.Hole,d.Map.GetTerrainType(0));
            var air=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked)},out _,out _).Dungeon;air.Jump();Assert.AreEqual(PanelType.Hole,air.Map.GetTerrainType(0));air.TickFrame();Assert.IsFalse(air.IsTerrainRecovering);
        }
        [Test]
        public void フロートは連続穴とヒビを無効化し能力消失後の接地だけ落下する()
        {
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked),new ExplorationTerrainCell(1,PanelType.Hole),new ExplorationTerrainCell(2,PanelType.Hole)},out _,out _).Dungeon;
            d.Player.TerrainState.IsFloating=true;d.Tick(300);Assert.AreEqual(100,d.Player.CurrentHp);Assert.AreEqual(PanelType.Cracked,d.Map.GetTerrainType(0));
            Movement(d).SetPosition(2);d.Player.TerrainState.IsFloating=false;d.TickFrame();Assert.IsTrue(d.IsTerrainRecovering);Assert.AreEqual(90,d.Player.CurrentHp);
        }
        [Test]
        public void 落下は一度だけ被害を与え停止中は時計を止め安全地点を通常化して復帰する()
        {
            var g=Start(new[]{new ExplorationTerrainCell(2,PanelType.Grass),new ExplorationTerrainCell(4,PanelType.Hole)},out _,out _);var d=g.Dungeon;
            d.Tick(600);Assert.IsTrue(d.IsTerrainRecovering);Assert.AreEqual(90,d.Player.CurrentHp);float pos=d.PlayerPosition;d.Jump();d.StartDash();Assert.IsFalse(d.IsJumping||d.IsDashing);
            d.TickFrame();float height=d.PlayerHeight;long clock=d.ObstacleVisualFrame;g.SetUserPaused(true);for(int i=0;i<100;i++)g.TickFrame(17);
            Assert.AreEqual(height,d.PlayerHeight);Assert.AreEqual(pos,d.PlayerPosition);Assert.AreEqual(clock,d.ObstacleVisualFrame);g.SetUserPaused(false);
            for(int i=0;i<11;i++)d.TickFrame();Assert.AreEqual(2,d.PlayerPosition);Assert.AreEqual(PanelType.Normal,d.Map.GetTerrainType(2));Assert.Greater(d.PlayerHeight,0);
            for(int i=0;i<18;i++)d.TickFrame();Assert.IsFalse(d.IsTerrainRecovering);Assert.AreEqual(90,d.Player.CurrentHp);d.TickFrame();Assert.Greater(d.PlayerPosition,2);
        }
        [Test]
        public void ダッシュは単穴だけ通れ連続二セル目または穴上の終了で落ち消費を戻さない()
        {
            var d=Start(new[]{new ExplorationTerrainCell(1,PanelType.Hole),new ExplorationTerrainCell(2,PanelType.Hole)},out _,out _).Dungeon;d.StartDash();d.Tick(120);
            Assert.IsTrue(d.IsTerrainRecovering);Assert.AreEqual(2,d.PlayerPosition);Assert.IsFalse(d.IsDashing);Assert.AreEqual(90,d.Player.CurrentHp);
            var single=Start(new[]{new ExplorationTerrainCell(1,PanelType.Hole)},out _,out _).Dungeon;single.StartDash();single.Tick(120);Assert.IsFalse(single.IsTerrainRecovering);Assert.AreEqual(100,single.Player.CurrentHp);
            var ended=Start(new[]{new ExplorationTerrainCell(1,PanelType.Hole)},out _,out _).Dungeon;ended.StartDash();ended.Tick(60);ended.StopDash();ended.TickFrame();Assert.IsTrue(ended.IsTerrainRecovering);
        }
        [Test]
        public void ジャンプ中は穴を越え着地点が穴ならダッシュジャンプでも落下する()
        {
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Hole)},out _,out _).Dungeon;d.Jump();d.Tick(400);Assert.IsFalse(d.IsTerrainRecovering);Assert.AreEqual(100,d.Player.CurrentHp);
            var landing=Start(Enumerable.Range(0,15).Select(i=>new ExplorationTerrainCell(i,PanelType.Hole)).ToArray(),out _,out _).Dungeon;landing.StartDash();landing.Jump();landing.Tick(600);Assert.IsTrue(landing.IsTerrainRecovering);Assert.AreEqual(90,landing.Player.CurrentHp);
        }
        [Test]
        public void 復帰中は毒と表示時計を止め死亡時は復帰せず終端保存する()
        {
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Hole)},out _,out _).Dungeon;d.Player.ApplyPoison("existing",new StatusEffect(StatusEffectType.Poison,10000,1),60);d.TickFrame();long clock=d.ObstacleVisualFrame;int hp=d.Player.CurrentHp;Recover(d);Assert.AreEqual(hp,d.Player.CurrentHp);Assert.AreEqual(clock,d.ObstacleVisualFrame);
            var dead=Start(new[]{new ExplorationTerrainCell(0,PanelType.Hole)},out _,out var store,10).Dungeon;dead.TickFrame();Assert.IsTrue(dead.Player.IsDead);Assert.IsFalse(dead.IsTerrainRecovering);Assert.AreEqual(CheckpointBoundary.Terminal,store.Read().Boundary);dead.Tick(10000);Assert.AreEqual(0,dead.Player.CurrentHp);
        }
        [Test]
        public void 同フレームの取得回復と落下被害を合算し途中死亡を起こさない()
        {
            var d=Start(new[]{new ExplorationTerrainCell(1,PanelType.Hole)},out _,out _,5,true).Dungeon;d.Tick(200);Assert.AreEqual(15,d.Player.CurrentHp);Assert.IsTrue(d.IsTerrainRecovering);Assert.IsFalse(d.Player.IsDead);
        }
        [Test]
        public void 保存失敗は前境界を保持し再試行後は地形とHPだけ復元して落下を再生しない()
        {
            var g=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked),new ExplorationTerrainCell(4,PanelType.Hole)},out var session,out var store);var d=g.Dungeon;
            d.Tick(600);Assert.IsTrue(d.IsTerrainRecovering);Assert.AreEqual(100,store.Read().Player.Hp);Assert.IsEmpty(store.Read().TerrainChanges);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var prior,out var error),error);Assert.AreEqual(100,prior.Player.CurrentHp);
            Recover(d);Movement(d).SetPosition(9);d.Tick(200);Assert.IsNotNull(d.ActiveVisit);store.Fail=true;Assert.IsFalse(d.ExitVisit());Assert.IsTrue(d.IsCheckpointBlocked);Assert.AreEqual(100,store.Read().Player.Hp);
            store.Fail=false;Assert.IsTrue(d.RetryCheckpoint());Assert.AreEqual(90,store.Read().Player.Hp);Assert.AreEqual(6,store.Read().SchemaVersion);Assert.AreEqual(PanelType.Hole,store.Read().TerrainChanges.Single(t=>t.Cell==0).Type);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var restored,out error),error);
            var loaded=new DungeonSystem(restored.Map,restored.Player,restored.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:restored);loaded.TickFrame();Assert.AreEqual(90,loaded.Player.CurrentHp);Assert.IsFalse(loaded.IsTerrainRecovering);Assert.AreEqual(PanelType.Hole,loaded.Map.GetTerrainType(0));
            var bad=store.Read();bad.TerrainChanges[0].Type=PanelType.Magma;Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));Assert.AreEqual(PanelType.Hole,store.Read().TerrainChanges[0].Type);
        }
        [Test]
        public void 中断は落下途中を保存せず直前境界へ戻して地形とHPをまとめて巻き戻す()
        {
            var g=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked),new ExplorationTerrainCell(2,PanelType.Hole)},out var session,out var store);var d=g.Dungeon;d.Tick(300);Assert.IsTrue(d.IsTerrainRecovering);
            Assert.IsTrue(g.SuspendForCheckpoint());for(int i=0;i<100;i++)g.TickFrame(17);Assert.AreEqual(90,d.Player.CurrentHp);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var restored,out var error),error);var loaded=new DungeonSystem(restored.Map,restored.Player,restored.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:restored);loaded.ActivateCheckpoint();Assert.AreEqual(0,loaded.PlayerPosition);Assert.AreEqual(100,loaded.Player.CurrentHp);Assert.AreEqual(PanelType.Cracked,loaded.Map.GetTerrainType(0));Assert.IsFalse(loaded.IsTerrainRecovering);
        }
        [Test]
        public void 復帰可能な非穴セルのない定義と永続穴の上書き保存は拒否する()
        {
            var m=DungeonConfig.CreateTestDungeon(4,100,3);
            Assert.Throws<System.ArgumentException>(()=>new DungeonConfig(m.DungeonId,m.DungeonName,4,m.MapCells,explorationTerrain:Enumerable.Range(0,3).Select(i=>new ExplorationTerrainCell(i,PanelType.Hole)).ToArray()));
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked),new ExplorationTerrainCell(4,PanelType.Hole)},out var session,out var store).Dungeon;d.Tick(600);Recover(d);Movement(d).SetPosition(9);d.Tick(200);Assert.IsNotNull(d.ActiveVisit);Assert.IsTrue(d.ExitVisit());var bad=store.Read();bad.TerrainChanges=new[]{new CheckpointTerrainStateDto{Cell=4,Type=PanelType.Normal}};Assert.IsFalse(CheckpointCodec.TryRestore(bad,session.Definitions,out _,out _));
        }
        [Test]
        public void 戦闘開始と保存復帰はパネル固定量を定義から渡し戦闘中の消費を探索へ戻さない()
        {
            var g=Start(new[]{new ExplorationTerrainCell(25,PanelType.Grass,7),new ExplorationTerrainCell(26,PanelType.Magma,11,13)},out var session,out var store);var d=g.Dungeon;
            d.StartBattle(new ContactInfo{Enemy=d.Map.GetCell(29).Enemy,EnemyCellIndex=29});var b=d.CurrentBattle;Assert.IsNotNull(b);Assert.AreEqual(7,b.Field.GetPanelValues(new Vector2Int(0,0)).Heal);Assert.AreEqual(13,b.Field.GetPanelValues(new Vector2Int(1,0)).Damage);
            b.RunLogicalFrame(()=>b.Player.MoveTo(new Vector2Int(2,0)));b.RunLogicalFrame(()=>b.Player.MoveTo(new Vector2Int(1,0)));Assert.AreEqual(87,b.Player.CurrentHp);Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(new Vector2Int(1,0)));Assert.AreEqual(PanelType.Magma,d.Map.GetTerrainType(26));
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),session.Definitions,out var restored,out var error),error);var loaded=new DungeonSystem(restored.Map,restored.Player,restored.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:restored);loaded.ActivateCheckpoint();Assert.AreEqual(100,loaded.Player.CurrentHp);Assert.AreEqual(13,loaded.CurrentBattle.Field.GetPanelValues(new Vector2Int(1,0)).Damage);Assert.AreEqual(PanelType.Magma,loaded.CurrentBattle.Field.GetPanelType(new Vector2Int(1,0)));
        }
        [Test]
        public void 地形変更は実ファイル保存から候補の再抽選なしで復元する()
        {
            var d=Start(new[]{new ExplorationTerrainCell(0,PanelType.Cracked)},out var session,out var store).Dungeon;d.Tick(1400);Assert.IsNotNull(d.ActiveVisit);Assert.IsTrue(d.ExitVisit());
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-fall-"+System.Guid.NewGuid().ToString("N"));
            try{var file=new Olca.Unity.FileCheckpointStore(session.Definitions,dir);Assert.IsTrue(file.TryWrite(store.Read(),out var error),error);var saved=file.Read();Assert.AreEqual(PanelType.Hole,saved.TerrainChanges.Single().Type);Assert.IsTrue(CheckpointCodec.TryRestore(saved,session.Definitions,out _,out error),error);}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [Test]
        public void 落下前の取得済み履歴は復帰先より先でも保存と再開に保持する()
        {
            var d=Start(new[]{new ExplorationTerrainCell(4,PanelType.Hole)},out var session,out var store,100,true,3).Dungeon;d.Tick(600);Recover(d);Assert.IsTrue(d.IsItemCollected(3));Assert.AreEqual(2,d.PlayerPosition);
            var capture=(CheckpointDto)typeof(DungeonCheckpointSession).GetMethod("Capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(session,new object[]{d,CheckpointBoundary.RewardCompleted,null});
            Assert.Greater(capture.CollectedItemCells.Single(),capture.PositionMilli/1000);Assert.IsTrue(CheckpointCodec.TryRestore(capture,session.Definitions,out _,out var error),error);
        }
    }
}
