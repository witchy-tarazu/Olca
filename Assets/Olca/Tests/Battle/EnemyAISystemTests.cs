using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class EnemyAISystemTests
    {
        private EnemyAISystem aiSystem;
        private PlayerCharacter player;
        private BattleField field;
        private MockRandomProvider random;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            logger = new MockLogger();
            aiSystem = new EnemyAISystem(logger);
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            random = new MockRandomProvider();
        }

        [Test]
        public void 敵を登録できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act
            aiSystem.RegisterEnemy(enemy, actions);

            // Assert
            Assert.AreEqual(1, aiSystem.AIBehaviors.Count, "敵が1体登録されるべき");
        }

        [Test]
        public void 敵が登録解除できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };
            aiSystem.RegisterEnemy(enemy, actions);

            // Act
            aiSystem.UnregisterEnemy(enemy);

            // Assert
            Assert.AreEqual(0, aiSystem.AIBehaviors.Count, "敵が登録解除されるべき");
        }

        [Test]
        public void 隣接した敵がプレイヤーを攻撃する()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, initialHp, "プレイヤーがダメージを受けるべき");
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "10ダメージを受けるべき");
        }

        [Test]
        public void 離れた敵は攻撃しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(0, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(initialHp, player.CurrentHp, "離れているので攻撃されないべき");
        }

        [Test]
        public void クールダウン中は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン中に再度更新
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterFirstAttack, player.CurrentHp, "クールダウン中は攻撃しないべき");
        }

        [Test]
        public void クールダウン後に再度行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.5f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン時間経過
            aiSystem.Tick(deltaTimeMs: 600, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, hpAfterFirstAttack, "クールダウン後に再攻撃するべき");
            Assert.AreEqual(hpAfterFirstAttack - 10, player.CurrentHp, "再度10ダメージを受けるべき");
        }

        [Test]
        public void 死亡した敵は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 10, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 敵を倒す
            enemy.TakeDamage(10, ElementType.None);
            int hpBeforeUpdate = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 200, player, field, random);

            // Assert
            Assert.AreEqual(hpBeforeUpdate, player.CurrentHp, "死んだ敵は攻撃しないべき");
        }

        [Test]
        public void 複数の敵が同時に行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy1 = new Enemy("slime1", "スライム1", 50, ElementType.None, new Vector2Int(3, 0));
            var enemy2 = new Enemy("slime2", "スライム2", 50, ElementType.None, new Vector2Int(1, 0));

            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 5, cooldown: 1.0f)
            };

            aiSystem.RegisterEnemy(enemy1, actions);
            aiSystem.RegisterEnemy(enemy2, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "2体の敵が同時に攻撃するべき（5+5=10ダメージ）");
        }

        [Test]
        public void 死亡したプレイヤーは攻撃されない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            player.TakeDamage(90); // HP 10 残す
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 20, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃でプレイヤーを倒す
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            Assert.IsTrue(player.IsDead, "プレイヤーが死亡するべき");

            // Act: 死んだプレイヤーに再度攻撃を試みる
            int hpAfterDeath = player.CurrentHp;
            aiSystem.Tick(deltaTimeMs: 200, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterDeath, player.CurrentHp, "死んだプレイヤーは攻撃されないべき");
        }

        [Test]
        public void 空のアクションリストで登録するとエラー()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var emptyActions = new List<IEnemyAIAction>();

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() =>
            {
                aiSystem.RegisterEnemy(enemy, emptyActions);
            }, "空のアクションリストは許可されないべき");
        }

        [Test]
        public void nullの敵を登録するとエラー()
        {
            // Arrange
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
            {
                aiSystem.RegisterEnemy(null, actions);
            }, "null敵は許可されないべき");
        }
    }
}


namespace Olca.Tests.Battle
{
    [NUnit.Framework.TestFixture]
    public class PerceptionStatusTests
    {
        private Enemy enemy;
        private PlayerCharacter player;
        private BattleSystem battle;
        private int hits;
        private void Setup(int cooldown=0,TimedStatusResistances resistance=null)
        {
            hits=0;player=new PlayerCharacter(100,new Vector2Int(1,0));
            var keeper=new Enemy("keeper","検証",500,ElementType.None,new Vector2Int(5,0));
            battle=new BattleSystem(player,new List<Enemy>{keeper},new Deck());
            enemy=new Enemy("affected","検証",500,ElementType.None,new Vector2Int(2,0),isSummoned:true,statusResistances:resistance);
            battle.AddSummon(enemy,new List<IEnemyAIAction>{new BasicAttackAction(10,cooldown,poiseDamage:0,onHit:_=>hits++)});
        }
        private void Frames(int count){for(int i=0;i<count;i++)battle.TickFrame(17);}
        private AIContext Context()=>new AIContext(enemy,player,battle.Field,new MockRandomProvider(),null);
        [NUnit.Framework.TestCase(1,0)] [NUnit.Framework.TestCase(-1,0)]
        public void 混乱は決定済み方向だけ反転し基本攻撃は逆方向へ空振りする(int x,int y)
        {
            var e=new Enemy("e","検証",100,ElementType.None,new Vector2Int(2,0));var p=new PlayerCharacter(100,new Vector2Int(2+x,y));
            var context=new AIContext(e,p,new BattleField(),new MockRandomProvider(),null);int called=0;var action=new BasicAttackAction(10,200,onHit:_=>called++);
            e.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000));Assert.IsTrue(context.HasVisiblePlayer);Assert.IsTrue(action.CanExecute(context));
            Assert.AreEqual(new Vector2Int(-x,-y),context.ResolveDirectionalInput(new Vector2Int(x,y)));action.Execute(context);Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(0,called);
            Assert.AreEqual(new Vector2Int(2,0),e.GridPosition);Assert.AreEqual(new Vector2Int(2+x,y),p.GridPosition);
            e.ClearStatus(StatusEffectType.Confusion);action.Execute(context);Assert.AreEqual(90,p.CurrentHp);Assert.AreEqual(1,called);
        }
        [NUnit.Framework.Test]
        public void 盲目は対象の位置情報を隠し直接実行も遠隔攻撃も通さず対象不要行動は残す()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));var context=Context();var action=new BasicAttackAction(10,0);var free=new FreeAction();
            Assert.IsFalse(context.HasVisiblePlayer);Assert.AreEqual(int.MaxValue,context.GetDistanceToPlayer());Assert.IsFalse(context.IsPlayerOnLeft());Assert.IsFalse(context.IsPlayerOnRight());Assert.IsFalse(context.IsPlayerAbove());Assert.IsFalse(context.IsPlayerBelow());
            Assert.IsFalse(action.CanExecute(context));action.Execute(context);Assert.AreEqual(100,player.CurrentHp);
            var ai=new EnemyAIBehavior(enemy,new List<IEnemyAIAction>{action,free});ai.Update(17,context,true);Assert.AreEqual(1,free.Count);
            enemy.ClearStatus(StatusEffectType.Blind);Assert.IsTrue(context.HasVisiblePlayer);Assert.AreEqual(1,context.GetDistanceToPlayer());Assert.IsTrue(context.IsPlayerOnLeft());ai.Update(17,context,true);Assert.AreEqual(90,player.CurrentHp);Assert.AreEqual(1,free.Count);
            enemy.MoveTo(new Vector2Int(4,0));action.Execute(context);Assert.AreEqual(90,player.CurrentHp,"直接入口でも隣接条件を飛び越えない");
        }
        private sealed class FreeAction:IEnemyAIAction
        {
            public int Count;public bool CanExecute(AIContext context)=>true;public void Execute(AIContext context){Count++;}public int GetCooldownMs()=>0;public float GetCooldown()=>0;
        }
        [NUnit.Framework.Test]
        public void 混乱の空振りも既存クールダウンを消費し期限解除で連射へ変わらない()
        {
            Setup(200);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1));Frames(1);Assert.AreEqual(0,hits);Assert.IsFalse(enemy.IsConfused);
            Frames(11);Assert.AreEqual(0,hits);Frames(1);Assert.AreEqual(1,hits);Assert.AreEqual(90,player.CurrentHp);
        }
        [NUnit.Framework.TestCase(StatusEffectType.Blind)] [NUnit.Framework.TestCase(StatusEffectType.Confusion)]
        public void 付与解除はフレーム境界で有効化し同フレーム取消と期限最終フレームを守る(StatusEffectType type)
        {
            Setup();battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(type,1000));battle.TickFrame(17);});Assert.AreEqual(1,hits,"新付与は次フレームから");Frames(1);Assert.AreEqual(1,hits);
            battle.RunLogicalFrame(()=>{enemy.ClearStatus(type);battle.TickFrame(17);});Assert.AreEqual(1,hits,"解除フレームは開始時状態を維持");Frames(1);Assert.AreEqual(2,hits);
            Setup();battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(type,1000));enemy.ClearStatus(type);battle.TickFrame(17);});Frames(1);Assert.AreEqual(2,hits,"取消付与を翌フレームへ残さない");
            Setup();enemy.ApplyStatus(new StatusEffect(type,1));Frames(1);Assert.AreEqual(0,hits,"最終フレームのAIにも状態を適用");Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.Test]
        public void 盲目と混乱は独立解除し麻痺中断や直接位置変更へ操作反転を漏らさない()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));Frames(1);Assert.AreEqual(0,hits);
            enemy.ClearStatus(StatusEffectType.Paralyze);Frames(1);Assert.AreEqual(0,hits);Assert.IsTrue(enemy.IsConfused);Assert.IsTrue(enemy.IsBlind);
            enemy.ClearStatus(StatusEffectType.Blind);Frames(1);Assert.AreEqual(0,hits);Assert.IsTrue(Context().HasVisiblePlayer);enemy.MoveTo(new Vector2Int(3,0));Assert.AreEqual(new Vector2Int(3,0),enemy.GridPosition,"位置指定は方向入力ではない");
            enemy.MoveTo(new Vector2Int(2,0));enemy.ClearStatus(StatusEffectType.Confusion);Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.TestCase(false)] [NUnit.Framework.TestCase(true)]
        public void 停止とカスタム復帰は両状態の時間と敵行動待ちを維持する(bool custom)
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,100));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,100));Frames(2);
            if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());}else battle.Pause(BattlePauseReason.User);
            Frames(30);Assert.AreEqual(4,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);Assert.AreEqual(4,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);Assert.AreEqual(0,hits);
            if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);Frames(4);Assert.AreEqual(0,hits);Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.TestCase(StatusEffectType.Blind,75,15)] [NUnit.Framework.TestCase(StatusEffectType.Confusion,50,30)] [NUnit.Framework.TestCase(StatusEffectType.Blind,100,0)]
        public void 対象の個別耐性は期間だけ短縮し無効付与と別状態へ漏らさない(StatusEffectType type,int resistance,int frames)
        {
            Setup(resistance:new TimedStatusResistances(confusion:type==StatusEffectType.Confusion?resistance:0,blind:type==StatusEffectType.Blind?resistance:0));var effect=new StatusEffect(type,1000);enemy.ApplyStatus(effect);
            Assert.AreEqual(60,effect.RemainingFrames);Assert.AreEqual(frames,enemy.GetStatus(type)?.RemainingFrames??0);
            var combined=TimedStatusResistances.Combine(new TimedStatusResistances(confusion:30,blind:40),new TimedStatusResistances(confusion:20,blind:60));Assert.AreEqual(50,combined.Confusion);Assert.AreEqual(100,combined.Blind);Assert.AreEqual(0,combined.Poison);
            var source=new StatusEffect(StatusEffectType.Confusion,1000,durationResistancePercent:25);Setup(resistance:new TimedStatusResistances(confusion:25));enemy.ApplyStatus(source);Assert.AreEqual(30,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);Assert.AreEqual(45,source.RemainingFrames);
            var current=enemy.GetStatus(StatusEffectType.Confusion);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000,durationResistancePercent:100));Assert.AreSame(current,enemy.GetStatus(StatusEffectType.Confusion));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TimedStatusResistances(confusion:-1));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TimedStatusResistances(blind:-1));
        }
        [NUnit.Framework.TestCase(StatusEffectType.Confusion)] [NUnit.Framework.TestCase(StatusEffectType.Blind)]
        public void 同じ付与定義を渡した別の敵と残時間を共有しない(StatusEffectType type)
        {
            Setup();var definition=new StatusEffect(type,1000);enemy.ApplyStatus(definition);var other=new Enemy("other","検証",100,ElementType.None,new Vector2Int(4,0));other.ApplyStatus(definition);Frames(2);
            Assert.AreEqual(58,enemy.GetStatus(type).RemainingFrames);Assert.AreEqual(60,other.GetStatus(type).RemainingFrames);Assert.AreEqual(60,definition.RemainingFrames);
        }
        private static Card PerceptionCard()
        {
            var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Confusion,Trigger=DerivativeTrigger.OnDamage,Duration=1});modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Blind,Trigger=DerivativeTrigger.OnDamage,Duration=1});
            return new Card("perception","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"既存付与",modifier:modifier);
        }
        [NUnit.Framework.Test]
        public void 通常カードから両状態を付与し同種更新と個別解除を敵AIへ反映する()
        {
            Setup();var card=PerceptionCard();battle.RunLogicalFrame(()=>{battle.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());battle.TickFrame(17);});Assert.AreEqual(1,hits);Assert.IsTrue(enemy.IsBlind);Assert.IsTrue(enemy.IsConfused);
            Frames(2);Assert.AreEqual(1,hits);Assert.AreEqual(58,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,100));battle.TickFrame(17);});Assert.AreEqual(6,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);Assert.AreEqual(57,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);
            enemy.ClearStatus(StatusEffectType.Blind);Frames(1);Assert.AreEqual(1,hits,"混乱だけでも逆方向へ空振り");enemy.ClearStatus(StatusEffectType.Confusion);Frames(1);Assert.AreEqual(2,hits);
        }
        private static DungeonConfig Map(TimedStatusResistances resistance)
        {
            var map=DungeonConfig.CreateTestDungeon(5,30,4);map.MapCells[4]=new MapCell(4,new Enemy("boss","旧耐性",200,ElementType.None,new Vector2Int(4,0),statusResistances:resistance));return map;
        }
        [NUnit.Framework.Test]
        public void 旧耐性の定義hashは不変で新耐性だけ照合へ加え保存再開は開始状態へ戻る()
        {
            var old=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Map(new TimedStatusResistances(20,30)),new Deck()).Definitions;
            Assert.AreEqual("E4E2D89CF56E33DAB5F9A9AF943F731D5BF893A4778750534FD5CD942FBE6739",old.MapHash);
            var map=Map(new TimedStatusResistances(confusion:50,blind:50));var card=PerceptionCard();var deck=new Deck(new List<Card>{card,card.Clone(),card.Clone(),card.Clone(),card.Clone()});var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);
            string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-perception-"+System.Guid.NewGuid().ToString("N"));
            try{
                var file=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,file);var game=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(500),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
                for(int i=0;game.CurrentBattle==null&&i<100;i++)game.TickFrame(17);var b=game.CurrentBattle;Assert.IsNotNull(b);b.CustomScreen.Close();b.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(b.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.IsTrue(b.Enemies[0].IsBlind);Assert.IsTrue(b.Enemies[0].IsConfused);var bytes=System.IO.File.ReadAllBytes(file.ActivePath);
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(game.SuspendForCheckpoint());restored.ActivateCheckpoint();var e=restored.CurrentBattle.Enemies[0];Assert.IsFalse(e.IsBlind);Assert.IsFalse(e.IsConfused);Assert.AreEqual(50,e.StatusResistances.Blind);Assert.AreEqual(50,e.StatusResistances.Confusion);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));
                var changed=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Map(new TimedStatusResistances(confusion:75,blind:50)),deck).Definitions;Assert.IsNull(new Olca.Unity.FileCheckpointStore(changed,directory).Read());CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [NUnit.Framework.Test]
        public void 知覚状態の期限と敵行動トレースは描画速度や追いつきで変わらない()
        {
            List<string> Trace(int fps){Setup(200);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,500));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,200));var trace=new List<string>();var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long wall=0;
                void Tick(long frame,int ms){if(frame==8)battle.Pause(BattlePauseReason.User);if(frame==14)battle.Resume(BattlePauseReason.User);battle.TickFrame(ms);trace.Add(player.CurrentHp+":"+enemy.IsBlind+":"+enemy.IsConfused+":"+hits);}
                if(fps==0){clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);clock.Drain(Tick);}else for(int i=1;clock.Frame<60;i++){long next=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}return trace;}
            NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(30));NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(120));NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}
