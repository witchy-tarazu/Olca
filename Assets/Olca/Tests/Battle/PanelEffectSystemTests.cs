using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PanelEffectSystemTests
    {
        [Test]
        public void 一時ヒット座標の移動と復帰を新しい論理地形進入として扱わない()
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0));var temporary=new Vector2Int(2,0);
            var e=new Enemy("e","敵",50,ElementType.None,new Vector2Int(5,0));
            var battle=new BattleSystem(p,new System.Collections.Generic.List<Enemy>{e},new Deck());
            battle.Field.ChangePanelType(temporary,PanelType.Magma);int events=0;
            battle.PanelEffects.OnPanelEffectApplied+=(_,__)=>events++;
            battle.RunLogicalFrame(()=>p.MoveTemporarily(temporary));
            Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(PanelType.Magma,battle.Field.GetPanelType(temporary));
            Assert.AreEqual(new Vector2Int(1,0),p.GetLogicalPosition());
            battle.RunLogicalFrame(()=>p.ReturnToOriginalPosition());Assert.AreEqual(0,events);
            battle.RunLogicalFrame(()=>p.MoveTo(temporary));Assert.AreEqual(95,p.CurrentHp);Assert.AreEqual(1,events);
        }

        [TestCase(false)] [TestCase(true)]
        public void 通常移動外のプレイヤー位置変更も進入処理を一回だけ適用する(bool floating)
        {
            var p = new PlayerCharacter(100, new Vector2Int(1,0)); p.TerrainState.IsFloating = floating;
            var target = new Vector2Int(2,0);
            var battle = new BattleSystem(p, new System.Collections.Generic.List<Enemy>{new Enemy("e","敵",50,ElementType.None,new Vector2Int(5,0))},new Deck());
            battle.ConfigureDefaultHitInvincibility(600); battle.Field.ChangePanelType(target,PanelType.Magma);
            int events=0; battle.PanelEffects.OnPanelEffectApplied += (_,t)=>{if(t==PanelType.Magma)events++;};
            battle.RunLogicalFrame(()=>p.MoveTo(target));
            Assert.AreEqual(floating?100:95,p.CurrentHp); Assert.AreEqual(!floating,p.IsInvincible);
            Assert.AreEqual(1,events); Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(target));
            battle.Field.ChangePanelType(target,PanelType.Magma); p.MoveTo(target);
            Assert.AreEqual(1,events); Assert.AreEqual(PanelType.Magma,battle.Field.GetPanelType(target));
        }

        [Test]
        public void 同居位置へのプレイヤー進入も敵の判定を終えてからマグマを消費する()
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0));var target=new Vector2Int(2,0);
            var e=new Enemy("e","敵",50,ElementType.None,target);
            var battle=new BattleSystem(p,new System.Collections.Generic.List<Enemy>{e},new Deck());
            battle.Field.ChangePanelType(target,PanelType.Magma);
            battle.RunLogicalFrame(()=>p.MoveTo(target));
            Assert.AreEqual(95,p.CurrentHp);Assert.AreEqual(45,e.CurrentHp);Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(target));
        }

        [Test]
        public void 敵の移動先のマグマは同居全員を個別判定して一回だけ消費する()
        {
            var p = new PlayerCharacter(100, new Vector2Int(0, 0));
            var target = new Vector2Int(4, 0);
            var mover = new Enemy("m", "移動", 50, ElementType.None, new Vector2Int(3, 0), poiseDefinition: new EnemyPoiseDefinition(10, 18));
            var ground = new Enemy("g", "地上", 50, ElementType.None, target);
            var fire = new Enemy("f", "火", 50, ElementType.Fire, target);
            var floating = new Enemy("l", "浮遊", 50, ElementType.None, target); floating.TerrainState.IsFloating = true;
            var invincible = new Enemy("i", "無敵", 50, ElementType.None, target); invincible.SetInvincible(1000);
            var barrier = new Enemy("b", "バリア", 50, ElementType.None, target); barrier.SetBarrier(3);
            var battle = new BattleSystem(p, new System.Collections.Generic.List<Enemy>{mover,ground,fire,floating,invincible,barrier}, new Deck());
            battle.ConfigureDefaultHitInvincibility(600);
            battle.Field.ChangePanelType(target, PanelType.Magma);
            int events = 0; battle.PanelEffects.OnPanelEffectApplied += (_, type) => { if(type == PanelType.Magma) events++; };
            battle.RunLogicalFrame(() => mover.MoveTo(target));
            Assert.AreEqual(45, mover.CurrentHp); Assert.AreEqual(45, ground.CurrentHp);
            Assert.AreEqual(50, fire.CurrentHp); Assert.AreEqual(50, floating.CurrentHp); Assert.AreEqual(50, invincible.CurrentHp);
            Assert.AreEqual(50, barrier.CurrentHp); Assert.AreEqual(0, barrier.BarrierHp);
            Assert.AreEqual(0, mover.AccumulatedPoiseDamage); Assert.IsTrue(mover.IsInvincible); Assert.IsTrue(ground.IsInvincible);
            Assert.IsFalse(fire.IsInvincible); Assert.IsFalse(floating.IsInvincible);
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(target)); Assert.AreEqual(1, events);
            mover.MoveTo(new Vector2Int(3,0)); mover.MoveTo(target); Assert.AreEqual(1, events);
            Assert.AreEqual(100, p.CurrentHp);
        }

        [TestCase(false)] [TestCase(true)]
        public void マグマの全員判定は同フレーム回復と勝敗確定より先に完了する(bool heal)
        {
            var p = new PlayerCharacter(100, new Vector2Int(0,0)); var target = new Vector2Int(4,0);
            var first = new Enemy("a", "先", 5, ElementType.None, new Vector2Int(3,0));
            var second = new Enemy("b", "後", 5, ElementType.None, target);
            var battle = new BattleSystem(p, new System.Collections.Generic.List<Enemy>{first,second}, new Deck());
            battle.Field.ChangePanelType(target, PanelType.Magma); int ended = 0;
            battle.OnBattleEnd += _ => { ended++; Assert.IsTrue(first.IsDead); Assert.IsTrue(second.IsDead); Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(target)); };
            battle.RunLogicalFrame(() => { first.MoveTo(target); if(heal) second.Heal(1); });
            Assert.IsTrue(first.IsDead); Assert.AreEqual(heal?1:0,second.CurrentHp); Assert.AreEqual(heal?0:1,ended);
        }

        [TestCase(false)] [TestCase(true)]
        public void 共通無敵で被弾したプレイヤーは勝敗通知前に無敵を解除し次戦へ持ち越さない(bool defeat)
        {
            var p = new PlayerCharacter(100);
            var enemy = new Enemy("e", "敵", 50, ElementType.None, new Vector2Int(1, 0));
            var battle = new BattleSystem(p, new System.Collections.Generic.List<Enemy> { enemy }, new Deck());
            Assert.AreEqual(0, battle.DefaultHitInvincibilityMs);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => battle.ConfigureDefaultHitInvincibility(-1));
            battle.ConfigureDefaultHitInvincibility(600); battle.TickFrame(17);
            Assert.IsTrue(p.IsInvincible); Assert.AreEqual(90, p.CurrentHp);
            int ends = 0; battle.OnBattleEnd += _ => { ends++; Assert.IsFalse(p.IsInvincible); };
            if (defeat) {
                for (int f = 0; f < 36; f++) battle.TickFrame(17);
                battle.RunLogicalFrame(() => { p.TakeDamage(1000); p.SetInvincible(600); });
            } else enemy.TakeDamage(50, ElementType.None);
            Assert.AreEqual(1, ends); Assert.IsFalse(p.IsInvincible);
            Assert.AreEqual(0, p.InvincibleDurationMs);
        }

        [TestCase(0, false, false, 5, 0)]
        [TestCase(3, false, false, 0, 0)]
        [TestCase(8, false, false, 0, 3)]
        [TestCase(3, true, false, 0, 3)]
        [TestCase(3, false, true, 0, 3)]
        public void マグマはHP処理と地形消費を保ち強靭度だけ削らない(int barrier, bool invincible, bool floating, int hpLoss, int barrierLeft)
        {
            player.SetPoise(10);
            player.TakeDamage(8);
            player.SetBarrier(barrier);
            if (invincible) player.SetInvincible(1000);
            player.TerrainState.IsFloating = floating;
            int staggered = 0, effects = 0;
            player.OnStaggered += () => staggered++;
            panelEffects.OnPanelEffectApplied += (_, __) => effects++;
            field.ChangePanelType(player.GridPosition, PanelType.Magma);

            panelEffects.ApplyOnEnterEffect(player, player.GridPosition);

            Assert.AreEqual(92 - hpLoss, player.CurrentHp);
            Assert.AreEqual(barrierLeft, player.BarrierHp);
            Assert.AreEqual(8, player.AccumulatedDamage);
            Assert.AreEqual(0, staggered);
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(player.GridPosition));
            Assert.AreEqual(1, effects);
            player.SetBarrier(0);
            player.TickInvincible(1000);
            player.TakeDamage(2);
            Assert.AreEqual(1, staggered, "通常攻撃は従来どおり蓄積してのけぞる");
            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 通常移動のマグマで死亡した場合は同時敵全滅でも敗北を一回通知する(bool killEnemy)
        {
            var p = new PlayerCharacter(5, new Vector2Int(1, 0));
            var enemy = new Enemy("e", "敵", 50, ElementType.None, new Vector2Int(5, 0));
            var battle = CreateMagmaBattle(p, enemy);
            int deaths = 0, ends = 0;
            p.OnDeath += () => deaths++;
            p.OnHpChanged += (hp, _) => { if (killEnemy && hp == 0) enemy.TakeDamage(50, ElementType.None); };
            battle.OnBattleEnd += result => { ends++; Assert.AreEqual(BattleResult.Defeat, result); };
            battle.Tick(200, new MockInputProvider { Horizontal = 1 });
            Assert.AreEqual(0, p.CurrentHp);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(1, ends);
            Assert.AreEqual(BattleState.Defeat, battle.State);
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(new Vector2Int(2, 0)));
        }

        [Test]
        public void カスタム中はマグマへの移動もダメージも停止し閉じてから強靭度を保って再開する()
        {
            var p = new PlayerCharacter(100, new Vector2Int(1, 0));
            p.SetPoise(10); p.TakeDamage(8);
            var battle = CreateMagmaBattle(p, new Enemy("e", "敵", 50, ElementType.None, new Vector2Int(5, 0)));
            battle.SetFirstStrike();
            battle.OpenCustomScreen(new MockRandomProvider());
            Assert.IsTrue(battle.IsPaused);
            var input = new MockInputProvider { Horizontal = 1 };
            battle.Tick(200, input);
            Assert.AreEqual(new Vector2Int(1, 0), p.GridPosition);
            Assert.AreEqual(92, p.CurrentHp);
            Assert.AreEqual(PanelType.Magma, battle.Field.GetPanelType(new Vector2Int(2, 0)));
            battle.CustomScreen.Confirm();
            battle.Tick(200, input);
            Assert.AreEqual(new Vector2Int(1, 0), p.GridPosition, "確定後も閉じる演出完了までは停止する");
            battle.CustomScreen.Close();
            battle.Tick(200, input);
            Assert.AreEqual(new Vector2Int(2, 0), p.GridPosition);
            Assert.AreEqual(87, p.CurrentHp);
            Assert.AreEqual(8, p.AccumulatedDamage);
        }

        private static BattleSystem CreateMagmaBattle(PlayerCharacter p, Enemy enemy)
        {
            var card = new Card("c", "試験", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "試験");
            var battle = new BattleSystem(p, new System.Collections.Generic.List<Enemy> { enemy }, new Deck(new System.Collections.Generic.List<Card> { card }));
            battle.Field.ChangePanelType(new Vector2Int(2, 0), PanelType.Magma);
            enemy.ApplyStun(60000);
            return battle;
        }

        private PlayerCharacter player;
        private BattleField field;
        private PanelEffectSystem panelEffects;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            logger = new MockLogger();
            panelEffects = new PanelEffectSystem(field, logger);
        }

        [Test]
        public void Magmaパネルに移動すると即座にダメージを受ける()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            int initialHp = player.CurrentHp;

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.Less(player.CurrentHp, initialHp, "HPが減少するべき");
            Assert.AreEqual(initialHp - 5, player.CurrentHp, "5ダメージ受けるべき");
        }

        [Test]
        public void Magmaパネルはダメージ後に通常パネルに変化する()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos), "Magmaが通常パネルに変化するべき");
        }

        [Test]
        public void Magmaパネルから離れた後に再度戻っても通常パネルになっている()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            player.MoveTo(new Vector2Int(1, 0));

            // Act: Magmaに移動してダメージを受ける
            panelEffects.ApplyOnEnterEffect(player, magmaPos);
            player.MoveTo(magmaPos);
            int hpAfterFirstVisit = player.CurrentHp;

            // 別の位置に移動
            player.MoveTo(new Vector2Int(3, 0));

            // 元のMagma位置に戻る（既に通常パネルになっているはず）
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(95, hpAfterFirstVisit, "最初のダメージは発生する");
            Assert.AreEqual(95, player.CurrentHp, "通常パネルになっているので追加ダメージはない");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos), "パネルは通常のまま");
        }

        [Test]
        public void 複数のMagmaパネルに連続で移動するとそれぞれダメージを受ける()
        {
            // Arrange
            Vector2Int magmaPos1 = new Vector2Int(2, 0);
            Vector2Int magmaPos2 = new Vector2Int(3, 0);
            field.ChangePanelType(magmaPos1, PanelType.Magma);
            field.ChangePanelType(magmaPos2, PanelType.Magma);
            player.MoveTo(new Vector2Int(1, 0));
            int initialHp = player.CurrentHp;

            // Act: 最初のMagmaに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos1);
            player.MoveTo(magmaPos1);

            // 2つ目のMagmaに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos2);

            // Assert
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "10ダメージ（5×2回）受けるべき");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos1), "最初のMagmaは通常に");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(magmaPos2), "2つ目のMagmaも通常に");
        }

        [Test]
        public void Crackedパネルに乗っても変化しない()
        {
            // Arrange
            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);

            // Act: Crackedパネルに移動
            panelEffects.ApplyOnEnterEffect(player, crackedPos);

            // Assert
            Assert.AreEqual(PanelType.Cracked, field.GetPanelType(crackedPos), "Crackedパネルのまま変化しないべき");
        }

        [Test]
        public void Crackedパネルから離れるとHoleに変化する()
        {
            // Arrange
            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);
            player.MoveTo(crackedPos);

            // Act: Crackedパネルから離れる
            panelEffects.ApplyOnLeaveEffect(player, crackedPos);

            // Assert
            Assert.AreEqual(PanelType.Hole, field.GetPanelType(crackedPos), "CrackedがHoleに変化するべき");
        }

        [Test]
        public void CrackedパネルでOnPanelEffectAppliedイベントが発火する()
        {
            // Arrange
            Vector2Int? effectPosition = null;
            PanelType? effectPanelType = null;
            panelEffects.OnPanelEffectApplied += (pos, type) =>
            {
                effectPosition = pos;
                effectPanelType = type;
            };

            Vector2Int crackedPos = new Vector2Int(2, 0);
            field.ChangePanelType(crackedPos, PanelType.Cracked);
            player.MoveTo(crackedPos);

            // Act: Crackedパネルから離れる
            panelEffects.ApplyOnLeaveEffect(player, crackedPos);

            // Assert
            Assert.IsNotNull(effectPosition, "イベントが発火するべき");
            Assert.AreEqual(crackedPos, effectPosition.Value);
            Assert.AreEqual(PanelType.Cracked, effectPanelType.Value);
        }

        [Test]
        public void MagmaダメージでOnPanelEffectAppliedイベントが発火する()
        {
            // Arrange
            int eventCount = 0;
            panelEffects.OnPanelEffectApplied += (pos, type) =>
            {
                eventCount++;
            };

            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);

            // Act: Magmaパネルに移動
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(1, eventCount, "移動時にイベントが1回発火するべき");
        }

        [Test]
        public void プレイヤーが死亡している場合は効果を適用しない()
        {
            // Arrange
            Vector2Int magmaPos = new Vector2Int(2, 0);
            field.ChangePanelType(magmaPos, PanelType.Magma);
            player.TakeDamage(100); // プレイヤーを倒す
            Assert.IsTrue(player.IsDead);

            int hpBeforeTick = player.CurrentHp;

            // Act: 死亡しているプレイヤーでMagmaパネルに移動を試みる
            panelEffects.ApplyOnEnterEffect(player, magmaPos);

            // Assert
            Assert.AreEqual(hpBeforeTick, player.CurrentHp, "死亡している場合はダメージを受けないべき");
            Assert.AreEqual(PanelType.Magma, field.GetPanelType(magmaPos), "パネルも変化しないべき");
        }

        [Test]
        public void 通常パネルでは何も起こらない()
        {
            // Arrange
            Vector2Int normalPos = new Vector2Int(2, 0);
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(normalPos));
            player.MoveTo(normalPos);
            int initialHp = player.CurrentHp;

            // Act
            panelEffects.ApplyOnEnterEffect(player, normalPos);
            panelEffects.Tick(deltaTimeMs: 500, player);

            // Assert
            Assert.AreEqual(initialHp, player.CurrentHp, "通常パネルではダメージを受けないべき");
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(normalPos), "パネルタイプは変化しないべき");
        }
    }
}

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class BattleFloatTests
    {
        private PlayerCharacter player;
        private BattleField field;
        private PlayerMovementSystem movement;
        private System.Collections.Generic.List<Enemy> enemies;
        private MockInputProvider input;

        [SetUp]
        public void Setup()
        {
            player = new PlayerCharacter(100, new Vector2Int(1, 0));
            field = new BattleField();
            movement = new PlayerMovementSystem(player, field);
            enemies = new System.Collections.Generic.List<Enemy>();
            input = new MockInputProvider();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 穴上で解除しても滞在と退出ができ再進入はできない(bool permanent)
        {
            var hole = new Vector2Int(2, 0);
            field = new BattleField(permanent ? new[] { hole } : null);
            movement = new PlayerMovementSystem(player, field);
            if (!permanent) field.ChangePanelType(hole, PanelType.Hole);
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            player.TerrainState.IsFloating = true;
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            player.TerrainState.Clear();
            movement.Tick(200, input, enemies);
            Assert.AreEqual(hole, player.GridPosition);
            Assert.AreEqual(100, player.CurrentHp);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            movement.Tick(200, input, enemies);
            Assert.IsFalse(movement.TryMove(new Vector2Int(-1, 0), enemies));
        }

        [Test]
        public void 浮遊しても障害物と相手と範囲外は越えられない()
        {
            player.TerrainState.IsFloating = true;
            var next = new Vector2Int(2, 0);
            field.ChangePanelType(next, PanelType.Hole);
            field.PlaceObstacle(next, new FieldObstacle("rock", 10));
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            field.RemoveObstacle(next);
            enemies.Add(new Enemy("e", "敵", 20, ElementType.None, next));
            Assert.IsFalse(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsFalse(movement.TryMove(new Vector2Int(0, 1), enemies));
            player.MoveTo(new Vector2Int(0, 0));
            Assert.IsFalse(movement.TryMove(new Vector2Int(-1, 0), enemies));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void プレイヤーと敵で共通判定し個体間で状態を共有しない(bool floating)
        {
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            var other = new Enemy("other", "敵", 20, ElementType.None, new Vector2Int(5, 0));
            player.TerrainState.IsFloating = floating;
            enemy.TerrainState.IsFloating = floating;
            var hole = new Vector2Int(2, 0);
            field.ChangePanelType(hole, PanelType.Hole);
            Assert.AreEqual(floating, field.CanMoveTo(hole, player.TerrainState));
            Assert.AreEqual(floating, field.CanMoveTo(hole, enemy.TerrainState));
            Assert.AreEqual(!floating, enemy.TerrainState.ReceivesPanelEffects);
            Assert.IsFalse(other.TerrainState.IsFloating);
            field.PlaceObstacle(hole, new FieldObstacle("rock", 10));
            Assert.IsFalse(field.CanMoveTo(hole, enemy.TerrainState));
            Assert.IsFalse(field.CanMoveTo(new Vector2Int(6, 0), enemy.TerrainState));
        }

        [Test]
        public void 浮遊中は氷の滑りも草むら移動遅延も受けない()
        {
            player.TerrainState.IsFloating = true;
            field.ChangePanelType(player.GridPosition, PanelType.Grass);
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Ice);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.AreEqual(150, movement.CurrentCooldownMs);
            Assert.IsFalse(movement.IsSliding);
            movement.Tick(200, input, enemies);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
        }

        [Test]
        public void 滑り中に浮遊を付与すると滑りが止まり解除後は再び滑る()
        {
            field.ChangePanelType(new Vector2Int(2, 0), PanelType.Ice);
            field.ChangePanelType(new Vector2Int(3, 0), PanelType.Ice);
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsTrue(movement.IsSliding);
            player.TerrainState.IsFloating = true;
            Assert.IsFalse(movement.IsSliding);
            movement.Tick(200, input, enemies);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            player.TerrainState.Clear();
            Assert.IsTrue(movement.TryMove(new Vector2Int(1, 0), enemies));
            Assert.IsTrue(movement.IsSliding);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void マグマダメージとヒビ破壊から浮遊者を除外する(bool floating)
        {
            var effects = new PanelEffectSystem(field);
            player.TerrainState.IsFloating = floating;
            field.ChangePanelType(player.GridPosition, PanelType.Magma);
            effects.ApplyOnEnterEffect(player, player.GridPosition);
            Assert.AreEqual(floating ? 100 : 95, player.CurrentHp);
            field.ChangePanelType(player.GridPosition, PanelType.Cracked);
            effects.ApplyOnLeaveEffect(player, player.GridPosition);
            Assert.AreEqual(floating ? PanelType.Cracked : PanelType.Hole, field.GetPanelType(player.GridPosition));
        }

        [Test]
        public void 浮遊は直接攻撃への無敵にはならない()
        {
            player.TerrainState.IsFloating = true;
            player.TakeDamage(10);
            Assert.AreEqual(90, player.CurrentHp);
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            enemy.TerrainState.IsFloating = true;
            enemy.TakeDamage(10, ElementType.Fire);
            Assert.AreEqual(10, enemy.CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 勝敗確定時に両者の浮遊を通知より先に解除する(bool defeat)
        {
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(4, 0));
            enemies.Add(enemy);
            var deck = new Deck(new System.Collections.Generic.List<Card> { new Card("c", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト") });
            var battle = new BattleSystem(player, enemies, deck);
            player.TerrainState.IsFloating = enemy.TerrainState.IsFloating = true;
            bool notified = false;
            battle.OnBattleEnd += result => { notified = true; Assert.IsFalse(player.TerrainState.IsFloating); Assert.IsFalse(enemy.TerrainState.IsFloating); };
            if (defeat) player.TakeDamage(100); else enemy.TakeDamage(20, ElementType.None);
            Assert.IsTrue(notified);
        }

        [TestCase(false, 1)]
        [TestCase(true, 3)]
        public void 一時移動の経路も同じ穴判定を使う(bool floating, int expectedX)
        {
            player.TerrainState.IsFloating = floating;
            var keeper=new Enemy("keeper","テスト",100,ElementType.None,new Vector2Int(5,0));
            var battle=new BattleSystem(player,new System.Collections.Generic.List<Enemy>{keeper},new Deck());
            battle.Field.ChangePanelType(new Vector2Int(2,0),PanelType.Hole);
            var card=new Card("move_fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,0,"");
            var effect=new Olca.Core.Logic.CardEffects.Effects.TemporaryMoveAttackEffect(new Vector2Int(1,0),2,.1f,0,MovePenetrationMode.Blocking);
            using(var cancellation=new System.Threading.CancellationTokenSource()){
                var task=effect.ExecuteAsync(new Olca.Core.Logic.CardEffects.BattleContext(battle,card),cancellation.Token);
                Assert.AreEqual(expectedX,player.GridPosition.X);
                cancellation.Cancel();
                if(floating)Assert.Throws<System.OperationCanceledException>(()=>task.GetAwaiter().GetResult());else task.GetAwaiter().GetResult();
                Assert.AreEqual(new Vector2Int(1,0),player.GridPosition);
                Assert.IsFalse(player.IsInTemporaryMove);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void Battleの通常入力更新から地形効果の除外へ接続される(bool floating)
        {
            var deck = new Deck(new System.Collections.Generic.List<Card> { new Card("c", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト") });
            var battle = new BattleSystem(player, enemies, deck);
            player.TerrainState.IsFloating = floating;
            battle.Field.ChangePanelType(player.GridPosition, PanelType.Cracked);
            battle.Field.ChangePanelType(new Vector2Int(2, 0), PanelType.Magma);
            input.Horizontal = 1;
            battle.Tick(200, input);
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            Assert.AreEqual(floating ? 100 : 95, player.CurrentHp);
            Assert.AreEqual(floating ? PanelType.Cracked : PanelType.Hole, battle.Field.GetPanelType(new Vector2Int(1, 0)));
        }
    }
}

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PermanentHoleTests
    {
        private static readonly Vector2Int Hole = new Vector2Int(2, 0);

        [TestCase(PanelType.Normal)]
        [TestCase(PanelType.Ice)]
        [TestCase(PanelType.Grass)]
        [TestCase(PanelType.Magma)]
        [TestCase(PanelType.Cracked)]
        [TestCase(PanelType.Hole)]
        public void 永続穴は後発の地形変更を受けず変更通知もしない(PanelType replacement)
        {
            var field = new BattleField(new[] { Hole });
            int changed = 0;
            field.OnPanelChanged += (_, __) => changed++;
            field.SetProtectedPosition(Hole);
            field.ChangePanelType(Hole, replacement);
            Assert.AreEqual(PanelType.Hole, field.GetPanelType(Hole));
            Assert.IsTrue(field.IsPermanentHole(Hole));
            Assert.IsTrue(field.IsHole(Hole));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void 初期配置をコピーし重複指定や呼出側の配列変更に影響されない()
        {
            var positions = new[] { Hole, Hole };
            var field = new BattleField(positions);
            positions[0] = new Vector2Int(3, 0);
            Assert.IsTrue(field.IsPermanentHole(Hole));
            Assert.IsFalse(field.IsPermanentHole(positions[0]));
            Assert.IsFalse(field.IsPermanentHole(new Vector2Int(-1, 0)));
        }

        [TestCase(-1, 0)]
        [TestCase(6, 0)]
        [TestCase(2, 1)]
        public void 範囲外への永続穴配置は拒否する(int x, int y)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BattleField(new[] { new Vector2Int(x, y) }));
        }

        [Test]
        public void 通常穴とヒビ由来の穴は上書きでき永続穴だけ全体リセット後も残る()
        {
            var field = new BattleField(new[] { Hole });
            var ordinary = new Vector2Int(3, 0);
            var cracked = new Vector2Int(4, 0);
            field.ChangePanelType(ordinary, PanelType.Hole);
            field.ChangePanelType(ordinary, PanelType.Ice);
            Assert.AreEqual(PanelType.Ice, field.GetPanelType(ordinary));
            field.ChangePanelType(cracked, PanelType.Cracked);
            new PanelEffectSystem(field).ApplyOnLeaveEffect(new PlayerCharacter(100, cracked), cracked);
            Assert.AreEqual(PanelType.Hole, field.GetPanelType(cracked));
            Assert.IsFalse(field.IsPermanentHole(cracked));
            field.ChangePanelType(cracked, PanelType.Grass);
            Assert.AreEqual(PanelType.Grass, field.GetPanelType(cracked));
            field.ResetAllPanels();
            Assert.AreEqual(PanelType.Hole, field.GetPanelType(Hole));
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(ordinary));
            Assert.AreEqual(PanelType.Normal, field.GetPanelType(cracked));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 初期地形を渡したBattleの通常入力で永続穴への通行を判定する(bool floating)
        {
            var player = new PlayerCharacter(100, new Vector2Int(1, 0));
            player.TerrainState.IsFloating = floating;
            var deck = new Deck(new System.Collections.Generic.List<Card> { new Card("c", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.None, 10, "テスト") });
            var battle = new BattleSystem(player, new System.Collections.Generic.List<Enemy>(), deck, initialPermanentHoles: new[] { Hole });
            var input = new MockInputProvider { Horizontal = 1 };
            battle.Tick(200, input);
            Assert.AreEqual(floating ? Hole : new Vector2Int(1, 0), player.GridPosition);
            Assert.AreEqual(100, player.CurrentHp);
            battle.PanelEffects.Tick(600000, player);
            Assert.IsTrue(battle.Field.IsPermanentHole(Hole));
            Assert.AreEqual(PanelType.Hole, battle.Field.GetPanelType(Hole));
        }

        [Test]
        public void 永続穴でも敵の浮遊と障害物は個別判定する()
        {
            var field = new BattleField(new[] { Hole });
            var enemy = new Enemy("e", "敵", 20, ElementType.None, new Vector2Int(5, 0));
            Assert.IsFalse(field.CanMoveTo(Hole));
            Assert.IsFalse(field.CanMoveTo(Hole, enemy.TerrainState));
            enemy.TerrainState.IsFloating = true;
            Assert.IsTrue(field.CanMoveTo(Hole, enemy.TerrainState));
            field.PlaceObstacle(Hole, new FieldObstacle("rock", 10));
            Assert.IsFalse(field.CanMoveTo(Hole, enemy.TerrainState));
        }
    }
}

namespace Olca.Tests.Battle
{
    public class BattlePanelValuesTests
    {
        private static PlayerCharacter Player(ElementType element,int hp=50)
        {
            var p=new PlayerCharacter(100,new Vector2Int(1,0));p.TakeDamage(100-hp);
            if(element!=ElementType.None)p.SetDepartureStyle(new DepartureStyleDefinition("fixture-"+element,"既存属性fixture",null,new TransformationAttackDefinition(element)));
            return p;
        }
        private static BattleSystem Battle(PlayerCharacter p)=>new BattleSystem(p,new System.Collections.Generic.List<Enemy>{new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0))},new Deck());
        private static void Frames(BattleSystem b,int count){for(int i=0;i<count;i++)b.TickFrame(17);}
        [Test]
        public void 固定量は不変で未指定上書きとリセットでは既存値へ戻り永続穴には適用しない()
        {
            var at=new Vector2Int(1,0);var f=new BattleField(new[]{new Vector2Int(5,0)});
            Assert.AreEqual(5,f.GetPanelValues(at).Damage);f.ChangePanelType(at,PanelType.Magma,new BattlePanelValues(7,13));Assert.AreEqual(13,f.GetPanelValues(at).Damage);
            f.ChangePanelType(at,PanelType.Grass);Assert.AreEqual(5,f.GetPanelValues(at).Heal);f.ChangePanelType(at,PanelType.Grass,new BattlePanelValues(0,0));f.ResetAllPanels();Assert.AreEqual(5,f.GetPanelValues(at).Heal);
            f.ChangePanelType(new Vector2Int(5,0),PanelType.Magma,new BattlePanelValues(7,13));Assert.AreEqual(PanelType.Hole,f.GetPanelType(new Vector2Int(5,0)));Assert.AreEqual(5,f.GetPanelValues(new Vector2Int(5,0)).Damage);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new BattlePanelValues(-1,5));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new BattlePanelValues(5,-1));
        }
        [Test]
        public void パネル別被害は通常進入経路を通り既存の防御無敵消費と二重進入を保つ()
        {
            var p=Player(ElementType.None,100);var b=Battle(p);var two=new Vector2Int(2,0);var three=new Vector2Int(3,0);b.Field.ChangePanelType(two,PanelType.Magma,new BattlePanelValues(7,13));b.Field.ChangePanelType(three,PanelType.Magma,new BattlePanelValues(11,7));
            b.RunLogicalFrame(()=>p.MoveTo(two));Assert.AreEqual(87,p.CurrentHp);b.RunLogicalFrame(()=>p.MoveTo(three));Assert.AreEqual(80,p.CurrentHp);Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(two));p.MoveTo(three);Assert.AreEqual(80,p.CurrentHp);
            b.ConfigureDefaultHitInvincibility(600);b.Field.ChangePanelType(two,PanelType.Magma,new BattlePanelValues(0,9));b.RunLogicalFrame(()=>p.MoveTo(two));Assert.AreEqual(71,p.CurrentHp);Assert.IsTrue(p.IsInvincible);b.Field.ChangePanelType(three,PanelType.Magma,new BattlePanelValues(0,20));b.RunLogicalFrame(()=>p.MoveTo(three));Assert.AreEqual(71,p.CurrentHp);Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(three));
        }
        [TestCase(ElementType.Earth,PanelType.Grass,7)] [TestCase(ElementType.Fire,PanelType.Magma,11)]
        public void 属性一致だけがパネル固定量を六十論理フレームごとに回復し停止中は間隔を保つ(ElementType element,PanelType type,int heal)
        {
            var p=Player(element);var b=Battle(p);b.Field.ChangePanelType(p.GridPosition,type,new BattlePanelValues(heal,13));Frames(b,59);Assert.AreEqual(50,p.CurrentHp);b.Pause();Frames(b,100);Assert.AreEqual(50,p.CurrentHp);b.Resume();Frames(b,1);Assert.AreEqual(50+heal,p.CurrentHp);Assert.AreEqual(type,b.Field.GetPanelType(p.GridPosition));
        }
        [Test]
        public void 移動往復と上書きとフロートは回復間隔を独立にリセットする()
        {
            var p=Player(ElementType.Earth);var b=Battle(p);var at=p.GridPosition;b.Field.ChangePanelType(at,PanelType.Grass,new BattlePanelValues(7));Frames(b,59);
            b.RunLogicalFrame(()=>{p.MoveTo(new Vector2Int(0,0));p.MoveTo(at);});Frames(b,1);Assert.AreEqual(50,p.CurrentHp);Frames(b,59);Assert.AreEqual(57,p.CurrentHp);
            Frames(b,59);b.Field.ChangePanelType(at,PanelType.Grass,new BattlePanelValues(11));Frames(b,1);Assert.AreEqual(57,p.CurrentHp);Frames(b,59);Assert.AreEqual(68,p.CurrentHp);
            Frames(b,59);p.TerrainState.IsFloating=true;Frames(b,1);p.TerrainState.IsFloating=false;Frames(b,59);Assert.AreEqual(68,p.CurrentHp);Frames(b,1);Assert.AreEqual(79,p.CurrentHp);
        }
        [Test]
        public void 敵の同居は属性と浮遊を個別判定し回復量や間隔を共有しない()
        {
            var p=Player(ElementType.None);var at=new Vector2Int(4,0);var a=new Enemy("a","土A",100,ElementType.Earth,at);var c=new Enemy("c","土B",100,ElementType.Earth,at);var fire=new Enemy("f","火",100,ElementType.Fire,at);var floating=new Enemy("l","浮遊",100,ElementType.Earth,at);floating.TerrainState.IsFloating=true;
            foreach(var e in new[]{a,c,fire,floating})e.TakeDamage(50,ElementType.None);var b=new BattleSystem(p,new System.Collections.Generic.List<Enemy>{a,c,fire,floating},new Deck());b.Field.ChangePanelType(at,PanelType.Grass,new BattlePanelValues(9));Frames(b,60);Assert.AreEqual(59,a.CurrentHp);Assert.AreEqual(59,c.CurrentHp);Assert.AreEqual(50,fire.CurrentHp);Assert.AreEqual(50,floating.CurrentHp);Assert.AreEqual(50,p.CurrentHp);
        }
        [Test]
        public void 火属性だけのマグマは残して回復し非火属性進入時は全員を判定後消費する()
        {
            var p=Player(ElementType.Fire);var at=new Vector2Int(3,0);var fire=new Enemy("fire","火",100,ElementType.Fire,at);fire.TakeDamage(50,ElementType.None);var mover=new Enemy("m","他",100,ElementType.None,new Vector2Int(5,0));var b=new BattleSystem(p,new System.Collections.Generic.List<Enemy>{fire,mover},new Deck());b.Field.ChangePanelType(at,PanelType.Magma,new BattlePanelValues(7,13));b.RunLogicalFrame(()=>p.MoveTo(at));Assert.AreEqual(PanelType.Magma,b.Field.GetPanelType(at));Frames(b,60);Assert.AreEqual(57,p.CurrentHp);Assert.AreEqual(57,fire.CurrentHp);
            b.RunLogicalFrame(()=>mover.MoveTo(at));Assert.AreEqual(87,mover.CurrentHp);Assert.AreEqual(57,fire.CurrentHp);Assert.AreEqual(57,p.CurrentHp);Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(at));Frames(b,60);Assert.AreEqual(57,fire.CurrentHp);
        }
        [Test]
        public void 同居全員へ設定被害を固定してから消費し防御と同フレーム回復を維持する()
        {
            var p=Player(ElementType.Earth);var at=new Vector2Int(4,0);p.MoveTo(at);var mover=new Enemy("m","移動",100,ElementType.None,new Vector2Int(3,0));var ground=new Enemy("g","地上",100,ElementType.None,at);var fire=new Enemy("f","火",100,ElementType.Fire,at);var floating=new Enemy("l","浮遊",100,ElementType.None,at);floating.TerrainState.IsFloating=true;var barrier=new Enemy("b","防御",100,ElementType.None,at);barrier.SetBarrier(5);var immune=new Enemy("i","無敵",100,ElementType.None,at);immune.SetInvincible(1000);
            var b=new BattleSystem(p,new System.Collections.Generic.List<Enemy>{mover,ground,fire,floating,barrier,immune},new Deck());b.ConfigureDefaultHitInvincibility(600);b.Field.ChangePanelType(at,PanelType.Magma,new BattlePanelValues(7,13));int events=0;b.PanelEffects.OnPanelEffectApplied+=(_,__)=>events++;
            b.RunLogicalFrame(()=>{mover.MoveTo(at);p.Heal(7);});Assert.AreEqual(87,mover.CurrentHp);Assert.AreEqual(87,ground.CurrentHp);Assert.AreEqual(100,fire.CurrentHp);Assert.AreEqual(100,floating.CurrentHp);Assert.AreEqual(100,barrier.CurrentHp);Assert.AreEqual(0,barrier.BarrierHp);Assert.AreEqual(100,immune.CurrentHp);Assert.AreEqual(44,p.CurrentHp);Assert.AreEqual(1,events);Assert.AreEqual(PanelType.Normal,b.Field.GetPanelType(at));
        }
        [Test]
        public void 同フレーム満タンからの継続回復と毒は合算して早期切捨てしない()
        {
            var p=Player(ElementType.Earth,100);var b=Battle(p);b.Field.ChangePanelType(p.GridPosition,PanelType.Grass,new BattlePanelValues(7));p.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,10000,10),60);Frames(b,59);Assert.AreEqual(100,p.CurrentHp);Frames(b,1);Assert.AreEqual(97,p.CurrentHp);
        }
        [Test]
        public void 旧ミリ秒入口も同じ周期とHP集計を使い終了後は回復しない()
        {
            var p=Player(ElementType.Earth);var b=Battle(p);b.Field.ChangePanelType(p.GridPosition,PanelType.Grass,new BattlePanelValues(7));b.Tick(500);b.Tick(500);Assert.AreEqual(57,p.CurrentHp);b.RunLogicalFrame(()=>p.TakeDamage(1000));Assert.IsTrue(b.IsGameOver);b.Tick(5000);Assert.AreEqual(0,p.CurrentHp);
        }
        [Test]
        public void ゼロ量と非対応属性は回復もダメージ由来無敵も増やさない()
        {
            var p=Player(ElementType.Earth);var b=Battle(p);b.ConfigureDefaultHitInvincibility(600);var at=new Vector2Int(2,0);b.Field.ChangePanelType(at,PanelType.Magma,new BattlePanelValues(0,0));b.RunLogicalFrame(()=>p.MoveTo(at));Assert.AreEqual(50,p.CurrentHp);Assert.IsFalse(p.IsInvincible);b.Field.ChangePanelType(at,PanelType.Grass,new BattlePanelValues(0));Frames(b,60);Assert.AreEqual(50,p.CurrentHp);b.Field.ChangePanelType(at,PanelType.Magma,new BattlePanelValues(9));Frames(b,60);Assert.AreEqual(50,p.CurrentHp);
        }
    }
}
