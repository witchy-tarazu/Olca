using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using System.Linq;

namespace Olca.Tests.Battle
{
    /// <summary>
    /// 属性ダメージ計算の統合テスト
    /// </summary>
    [TestFixture]
    public class ElementalDamageTests
    {
        private PlayerCharacter player;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(0, 0));
            logger = new MockLogger();
        }

        private BattleSystem CreateBattle(Enemy enemy)
        {
            var enemies = new List<Enemy> { enemy };
            var cards = new List<Card>
            {
                new Card("atk", "攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 10, "攻撃"),
            };
            return new BattleSystem(player, enemies, new Deck(cards), logger: logger);
        }


        [Test]
        public void 個別ヒットと回復の表示通知は同フレームのHP一括更新と独立する()
        {
            var enemy = new Enemy("feedback", "テスト", 100, ElementType.None, new Vector2Int(3, 0));
            var battle = CreateBattle(enemy);
            var playerHits = new List<CombatFeedback>();
            var enemyHits = new List<CombatFeedback>();
            int playerUpdates = 0, enemyUpdates = 0;
            player.OnCombatFeedback += playerHits.Add;
            enemy.OnCombatFeedback += enemyHits.Add;
            player.OnHpChanged += (_, __) => playerUpdates++;
            enemy.OnHpChanged += (_, __) => enemyUpdates++;
            battle.RunLogicalFrame(() => {
                player.TakeDamage(10); player.TakeDamage(20); player.Heal(5);
                enemy.TakeDamage(7, ElementType.None); enemy.TakeDamage(11, ElementType.None); enemy.Heal(4);
                Assert.AreEqual(100, player.CurrentHp);
                Assert.AreEqual(100, enemy.CurrentHp);
            });
            Assert.AreEqual(75, player.CurrentHp); Assert.AreEqual(86, enemy.CurrentHp);
            Assert.AreEqual(1, playerUpdates); Assert.AreEqual(1, enemyUpdates);
            CollectionAssert.AreEqual(new[] { 10, 20, 5 }, playerHits.Select(h => h.Amount));
            CollectionAssert.AreEqual(new[] { 7, 11, 4 }, enemyHits.Select(h => h.Amount));
            Assert.AreEqual(CombatFeedbackKind.Healing, playerHits.Last().Kind);
            Assert.AreEqual(CombatFeedbackKind.Healing, enemyHits.Last().Kind);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 回復表示は実効量だけを各回復へ配分し満タン時には出さない(bool enemySide)
        {
            var enemy = new Enemy("feedback", "テスト", 100, ElementType.None, new Vector2Int(3, 0));
            var battle = CreateBattle(enemy); var hits = new List<CombatFeedback>();
            if (enemySide) enemy.OnCombatFeedback += hits.Add; else player.OnCombatFeedback += hits.Add;
            void Heal(int n) { if (enemySide) enemy.Heal(n); else player.Heal(n); }
            battle.RunLogicalFrame(() => Heal(100)); Assert.IsEmpty(hits);
            battle.RunLogicalFrame(() => {
                if (enemySide) enemy.TakeDamage(12, ElementType.None); else player.TakeDamage(12);
                Heal(5); Heal(100);
            });
            CollectionAssert.AreEqual(new[] { 5, 7 }, hits.Where(h => h.Kind == CombatFeedbackKind.Healing).Select(h => h.Amount));
            Assert.AreEqual(100, enemySide ? enemy.CurrentHp : player.CurrentHp);
        }

        [Test]
        public void 完全防御の表示はガードとオーラとバリアを区別し本体HPを減らさない()
        {
            var enemy = new Enemy("feedback", "テスト", 100, ElementType.None, new Vector2Int(3, 0));
            var hits = new List<CombatFeedback>(); enemy.OnCombatFeedback += hits.Add;
            enemy.Guard.Set(true, false); enemy.TakeDamage(9, ElementType.None); enemy.Guard.Set(false, false);
            enemy.SetAura(20); enemy.TakeDamage(9, ElementType.None); enemy.SetAura(0);
            enemy.SetBarrier(20); enemy.TakeDamage(9, ElementType.None);
            CollectionAssert.AreEqual(new[] { CombatFeedbackKind.Guard, CombatFeedbackKind.Immune, CombatFeedbackKind.BarrierDamage }, hits.Select(h => h.Kind));
            Assert.AreEqual(9, hits.Last().Amount); Assert.AreEqual(100, enemy.CurrentHp);
        }

        [Test]
        public void フレーム外の撃破通知でも表示をHP通知と購読解除より先に発行する()
        {
            var enemy = new Enemy("feedback", "テスト", 10, ElementType.None, new Vector2Int(3, 0));
            var order = new List<string>();
            enemy.OnCombatFeedback += hit => order.Add("hit:" + hit.Amount);
            enemy.OnHpChanged += (_, __) => order.Add("hp");
            enemy.OnDeath += () => order.Add("death");
            enemy.TakeDamage(12, ElementType.None);
            CollectionAssert.AreEqual(new[] { "hit:12", "hp", "death" }, order);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 本体命中のヒットストップは指定フレームだけ戦闘時計と更新を止める(bool playerSide)
        {
            var enemy = new Enemy("stop", "テスト", 100, ElementType.None, new Vector2Int(4, 0));
            var battle = CreateBattle(enemy);
            battle.RunLogicalFrame(() => {
                if(playerSide)player.TakeDamage(3,hitStopFrames:6);
                else enemy.TakeDamage(3,ElementType.None,hitStopFrames:6);
            });
            Assert.AreEqual(6,battle.HitStopRemainingFrames); Assert.IsTrue(battle.IsPaused);
            for(int n=0;n<6;n++){battle.TickFrame(16);Assert.AreEqual(0,battle.LogicalFrame);}
            Assert.IsFalse(battle.IsHitStopped);battle.TickFrame(16);Assert.AreEqual(1,battle.LogicalFrame);
        }

        [Test]
        public void 同一フレームの全ヒットは受理しヒットストップ長は加算せず最長を使う()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            int hpUpdates=0;enemy.OnHpChanged+=(_,__)=>hpUpdates++;
            battle.RunLogicalFrame(()=>{
                enemy.TakeDamage(3,ElementType.None,hitStopFrames:3);
                enemy.TakeDamage(4,ElementType.None,hitStopFrames:8);
                enemy.TakeDamage(5,ElementType.None,hitStopFrames:5);
                Assert.IsFalse(battle.IsHitStopped,"全ヒットの確定前に停止しない");
            });
            Assert.AreEqual(88,enemy.CurrentHp);Assert.AreEqual(1,hpUpdates);Assert.AreEqual(8,battle.HitStopRemainingFrames);
        }

        [TestCase(false,false)]
        [TestCase(false,true)]
        [TestCase(true,false)]
        [TestCase(true,true)]
        public void 完全防御だけでは停止せず同フレーム合算で防御を解除した攻撃だけ停止する(bool playerSide,bool aura)
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            if(playerSide){if(aura)player.SetAura(10);else player.SetBarrier(10);}
            else {if(aura)enemy.SetAura(10);else enemy.SetBarrier(10);}
            void Hit(int amount,int frames){if(playerSide)player.TakeDamage(amount,hitStopFrames:frames);else enemy.TakeDamage(amount,ElementType.None,hitStopFrames:frames);}
            battle.RunLogicalFrame(()=>Hit(4,20));Assert.IsFalse(battle.IsHitStopped);
            battle.RunLogicalFrame(()=>{Hit(3,15);Hit(7,6);});
            Assert.AreEqual(6,battle.HitStopRemainingFrames);Assert.AreEqual(100,player.CurrentHp);Assert.AreEqual(100,enemy.CurrentHp);
            Assert.IsFalse(playerSide?player.HasAura||player.HasBarrier:enemy.HasAura||enemy.HasBarrier);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 威力ゼロの剥離でも防御を解除すれば明示ヒットストップを発生させる(bool playerSide)
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            if(playerSide)player.SetAura(10);else enemy.SetBarrier(10);
            battle.RunLogicalFrame(()=>{
                if(playerSide)player.TakeDamage(0,attackProperties:AttackProperty.Wind,hitStopFrames:4);
                else enemy.TakeDamage(0,ElementType.None,AttackProperty.Wind,hitStopFrames:4);
            });
            Assert.AreEqual(4,battle.HitStopRemainingFrames);Assert.AreEqual(100,enemy.CurrentHp);Assert.AreEqual(100,player.CurrentHp);
        }

        [Test]
        public void ガードと耐性無効と設定ゼロはヒットストップを発生させない()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            enemy.Guard.Set(true,false);enemy.TakeDamage(5,ElementType.None,hitStopFrames:6);Assert.IsFalse(battle.IsHitStopped);
            enemy.Guard.Set(false,false);enemy.TakeDamage(5,ElementType.None);Assert.IsFalse(battle.IsHitStopped);
            var immune=new Enemy("immune","テスト",100,ElementType.None,new Vector2Int(4,0),resistances:new ElementResistances(fire:100));
            var immuneBattle=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{immune},new Deck(new List<Card>()));
            immune.TakeDamage(5,ElementType.Fire,hitStopFrames:6);Assert.IsFalse(immuneBattle.IsHitStopped);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>enemy.TakeDamage(1,ElementType.None,hitStopFrames:-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.TakeDamage(1,hitStopFrames:-1));
        }

        [Test]
        public void カスタムとユーザーポーズの重複はヒットストップ残数も保持し最後の解除で再開する()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            enemy.TakeDamage(1,ElementType.None,hitStopFrames:6);
            battle.Pause(BattlePauseReason.CustomScreen);battle.Pause(BattlePauseReason.User);
            for(int n=0;n<10;n++)battle.TickFrame(16);Assert.AreEqual(6,battle.HitStopRemainingFrames);
            battle.Resume(BattlePauseReason.User);battle.TickFrame(16);Assert.AreEqual(6,battle.HitStopRemainingFrames);
            battle.Resume(BattlePauseReason.CustomScreen);battle.TickFrame(16);Assert.AreEqual(5,battle.HitStopRemainingFrames);Assert.AreEqual(0,battle.LogicalFrame);
        }

        [Test]
        public void 暗転中のヒットストップは暗転時計も止め解除後に暗転を完了する()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);int resolved=0;
            Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,1,3,()=>resolved++));
            enemy.TakeDamage(1,ElementType.None,hitStopFrames:6);
            for(int n=0;n<6;n++)battle.TickFrame(16);Assert.AreEqual(0,resolved);Assert.IsTrue(battle.IsDarkening);
            for(int n=0;n<3;n++)battle.TickFrame(16);Assert.AreEqual(1,resolved);Assert.AreEqual(0,battle.LogicalFrame);
            battle.TickFrame(16);Assert.AreEqual(1,battle.LogicalFrame);
        }

        [Test]
        public void 変身演出と競合したヒットストップは演出時計を止め残数ゼロ後に再開する()
        {
            var battle=HandBonusBattle(out _);var forms=battle.Transformations;forms.EnablePresentation();forms.TryPreview("large");battle.CustomScreen.Confirm();battle.CustomScreen.Close();
            var enemy=battle.Enemies.First();enemy.TakeDamage(1,ElementType.None,hitStopFrames:6);
            for(int n=0;n<6;n++)battle.TickFrame(16);Assert.AreEqual(60,forms.ActivationFramesRemaining);Assert.AreEqual(0,battle.LogicalFrame);
            battle.TickFrame(16);Assert.AreEqual(59,forms.ActivationFramesRemaining);
        }

        [Test]
        public void 撃破で戦闘が終われば停止要求を破棄し次戦へ購読を残さない()
        {
            var enemy=new Enemy("stop","テスト",10,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            enemy.TakeDamage(10,ElementType.None,hitStopFrames:6);Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(battle.IsHitStopped);
            player.TakeDamage(1,hitStopFrames:9);Assert.IsFalse(battle.IsHitStopped);
        }

        [Test]
        public void カード効果の明示設定は全対象へ適用し一度の最長停止になる()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,3,"テスト");
            var effect=new DamageEffect(3,DamageTargetType.All,hitStopFrames:5);
            battle.RunLogicalFrame(()=>effect.Execute(new Olca.Core.Logic.CardEffects.BattleContext(battle,card)));
            Assert.AreEqual(97,enemy.CurrentHp);Assert.AreEqual(5,battle.HitStopRemainingFrames);
        }

        [Test]
        public void 敵の通常攻撃の明示設定はプレイヤーへの命中から停止を要求する()
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(1,0));var battle=CreateBattle(enemy);
            var action=new BasicAttackAction(3,0,hitStopFrames:7);
            var context=new AIContext(enemy,player,battle.Field,new MockRandomProvider(),logger);
            battle.RunLogicalFrame(()=>action.Execute(context));
            Assert.AreEqual(97,player.CurrentHp);Assert.AreEqual(7,battle.HitStopRemainingFrames);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 汎用カード攻撃と投擲着弾は明示した停止設定を命中まで保持する(bool projectile)
        {
            var enemy=new Enemy("stop","テスト",100,ElementType.None,new Vector2Int(4,0));var battle=CreateBattle(enemy);
            var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,3,"テスト");
            var effect=new ModifiableCardEffect(DamageTargetType.All,productBomb:projectile,hitStopFrames:7);
            battle.RunLogicalFrame(()=>effect.Execute(new Olca.Core.Logic.CardEffects.BattleContext(battle,card)));
            if(projectile){Assert.IsFalse(battle.IsHitStopped);for(int n=0;n<120&&!battle.IsHitStopped;n++)battle.TickFrame(16);}
            Assert.AreEqual(97,enemy.CurrentHp);Assert.AreEqual(7,battle.HitStopRemainingFrames);
        }

        private BattleSystem HandBonusBattle(out Deck deck, int bonus = 2)
        {
            deck = new Deck(Enumerable.Range(0,20).Select(i => new Card("fixture-"+i,"検証"+i,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,1,"検証")).ToList());
            var battle = new BattleSystem(player,new List<Enemy>{new Enemy("e","検証",1000,ElementType.None,new Vector2Int(5,0))},deck);
            battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("large",ElementResistances.Neutral,ElementType.Fire,bonus),new TransformationDefenseDefinition("small",ElementResistances.Neutral,ElementType.Water,1)});
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());return battle;
        }
        [TestCase(false,60)]
        [TestCase(true,12)]
        public void 変身演出は画面閉鎖後の論理フレームだけ進み戦闘と使用回数を二重更新しない(bool shortened,int frames)
        {
            var b=HandBonusBattle(out _);var t=b.Transformations;var cs=b.CustomScreen;t.EnablePresentation();t.ShortPresentation=shortened;
            Assert.IsTrue(t.TryPreview("large"));Assert.AreEqual(0,t.RemainingPhases);StringAssert.Contains("仮選択",t.CustomStatusText);
            cs.Confirm();Assert.AreEqual(3,t.RemainingPhases);Assert.AreEqual(1,t.UsedIds.Count);Assert.AreEqual(frames,t.ActivationFramesRemaining);
            for(int n=0;n<8;n++)b.TickFrame(16);Assert.AreEqual(frames,t.ActivationFramesRemaining,"カスタム閉鎖までは演出を進めない");
            cs.Close();t.ShortPresentation=!shortened;
            for(int n=0;n<frames;n++){b.TickFrame(16);Assert.IsTrue(b.IsPaused);Assert.AreEqual(0,b.CustomGauge.CurrentGaugeMs);}
            Assert.AreEqual(0,t.ActivationFramesRemaining);Assert.AreEqual(3,t.RemainingPhases);b.TickFrame(16);Assert.IsFalse(b.IsPaused);Assert.Greater(b.CustomGauge.CurrentGaugeMs,0);
            Assert.AreEqual(1,t.UsedIds.Count);Assert.AreEqual(3,t.RemainingPhases);
        }
        [Test]
        public void 変身演出はユーザーポーズで止まり入力によるカスタム再開で持続回数を消費しない()
        {
            var b=HandBonusBattle(out _);var t=b.Transformations;t.EnablePresentation();t.TryPreview("large");b.CustomScreen.Confirm();b.CustomScreen.Close();
            for(int n=0;n<10;n++)b.TickFrame(16);Assert.AreEqual(50,t.ActivationFramesRemaining);
            b.Pause(BattlePauseReason.User);for(int n=0;n<100;n++)b.TickFrame(16);Assert.AreEqual(50,t.ActivationFramesRemaining);
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());Assert.IsFalse(b.CustomScreen.IsOpen);Assert.AreEqual(3,t.RemainingPhases);
            b.Resume(BattlePauseReason.User);b.TickFrame(16);Assert.AreEqual(49,t.ActivationFramesRemaining);
        }
        private BattleSystem PresentedForm()
        {
            var b=HandBonusBattle(out _);b.Transformations.EnablePresentation();b.Transformations.TryPreview("large");b.CustomScreen.Confirm();b.CustomScreen.Close();
            for(int n=0;n<61;n++)b.TickFrame(16);return b;
        }
        [Test]
        public void 未確定取消は演出も回数も消費せず別変身の取消で継続表示へ戻る()
        {
            var b=HandBonusBattle(out _);var t=b.Transformations;t.EnablePresentation();t.TryPreview("large");t.TryCancelPending();
            b.CustomScreen.Confirm();b.CustomScreen.Close();Assert.IsFalse(t.HasActive);Assert.IsFalse(t.IsActivating);Assert.IsEmpty(t.UsedIds);
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());t.TryPreview("large");b.CustomScreen.Confirm();b.CustomScreen.Close();
            for(int n=0;n<61;n++)b.TickFrame(16);b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());
            Assert.AreEqual(2,t.RemainingPhases);t.TryPreview("small");StringAssert.Contains("small",t.CustomStatusText);t.TryCancelPending();
            Assert.AreEqual("large",t.ActiveId);Assert.AreEqual(2,t.RemainingPhases);Assert.IsFalse(t.IsActivating);Assert.IsFalse(t.IsUsed("small"));
        }
        [Test]
        public void 三回目のカスタムで残りゼロとなり解除演出は追加停止せず期限で消える()
        {
            var b=PresentedForm();var t=b.Transformations;
            for(int expected=2;expected>=0;expected--){b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());Assert.AreEqual(expected,t.RemainingPhases);b.CustomScreen.Confirm();b.CustomScreen.Close();}
            Assert.IsFalse(t.HasActive);Assert.IsTrue(t.IsUsed("large"));Assert.AreEqual(12,t.ReleaseFramesRemaining);Assert.IsFalse(b.IsPaused);
            StringAssert.Contains("0回",t.ActiveStatusText);for(int n=0;n<12;n++)b.TickFrame(16);Assert.AreEqual(0,t.ReleaseFramesRemaining);
        }
        [Test]
        public void 弱点解除と戦闘終了は実状態を優先し変身演出を残さない()
        {
            var b=PresentedForm();var t=b.Transformations;player.TakeDamage(1,attackElement:ElementType.Fire);
            Assert.IsFalse(t.HasActive);Assert.AreEqual(0,t.RemainingPhases);Assert.AreEqual(12,t.ReleaseFramesRemaining);Assert.IsFalse(b.IsPaused);
            foreach(var enemy in b.Enemies.ToArray())enemy.TakeDamage(enemy.MaxHp,ElementType.None);
            Assert.IsTrue(b.IsGameOver);Assert.AreEqual(0,t.ReleaseFramesRemaining);Assert.AreEqual(0,t.ActivationFramesRemaining);
        }
        [Test]
        public void 演出中のチェックポイント再開は途中演出も変身使用状態も引き継がない()
        {
            var f=new Olca.Application.Battle.SimpleCardFactory();var map=DungeonConfig.CreateTestDungeon(20,30,3);var deck=f.CreateStarterDeck();
            var cp=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);var random=new MockRandomProvider(0);
            var game=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,random,logger,checkpoints:cp,cardDatabase:f);
            for(int n=0;n<20&&game.CurrentBattle==null;n++)game.Tick(50);var b=game.CurrentBattle;Assert.IsNotNull(b);
            b.Transformations.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire)});b.Transformations.EnablePresentation();
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(random);b.Transformations.TryPreview("fixture");b.CustomScreen.Confirm();b.CustomScreen.Close();b.TickFrame(16);
            Assert.AreEqual(59,b.Transformations.ActivationFramesRemaining);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(cp.Definitions,cp.Store),random,logger,out var restored,out var error,cardDatabase:f),error);
            Assert.IsTrue(game.SuspendForCheckpoint());Assert.AreEqual(0,b.Transformations.ActivationFramesRemaining);
            restored.ActivateCheckpoint();Assert.IsNotNull(restored.CurrentBattle);
            Assert.IsFalse(restored.CurrentBattle.Player.HasTransformationDefense);Assert.AreEqual(0,restored.CurrentBattle.Transformations.ActivationFramesRemaining);Assert.IsEmpty(restored.CurrentBattle.Transformations.UsedIds);
        }
        private sealed class AnimationSettingFaultIo : Olca.Unity.CheckpointFileIo
        {
            public bool Fail;
            public override void MoveNew(string a,string b){if(Fail)throw new System.IO.IOException();base.MoveNew(a,b);}
            public override void Replace(string a,string b,string backup){if(Fail)throw new System.IO.IOException();base.Replace(a,b,backup);}
        }
        [Test]
        public void 変身演出設定は独立保存し再読込と失敗時の旧内容保持を行う()
        {
            var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-animation-"+System.Guid.NewGuid().ToString("N"));
            try{var io=new AnimationSettingFaultIo();var store=new Olca.Unity.TransformationPresentationFileStore(path,io);
                Assert.IsTrue(store.TryRead(out var shortened,out _));Assert.IsFalse(shortened);Assert.IsFalse(System.IO.Directory.Exists(path));
                Assert.IsTrue(store.TrySave(true,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);io.Fail=true;
                Assert.IsFalse(store.TrySave(false,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
                Assert.IsTrue(new Olca.Unity.TransformationPresentationFileStore(path).TryRead(out shortened,out error),error);Assert.IsTrue(shortened);
                io.Fail=false;Assert.IsTrue(store.TrySave(false,out error),error);Assert.IsTrue(store.TryRead(out shortened,out error));Assert.IsFalse(shortened);
            }finally{if(System.IO.Directory.Exists(path))System.IO.Directory.Delete(path,true);}
        }
        [Test]
        public void 変身追加ドローは選択分を二重計上せず取消再選択でも同じ個体を同順に引く()
        {
            var b=HandBonusBattle(out var deck);var screen=b.CustomScreen;var state=b.Transformations;
            var before=screen.CurrentHand.ToArray();screen.TrySelectCard(before[0]);
            Assert.IsTrue(state.TryPreview("large"));Assert.IsEmpty(screen.SelectedCards);
            Assert.AreEqual(12,screen.MaxHandSize);Assert.AreEqual(12,screen.CurrentHand.Count);Assert.AreEqual(8,deck.RemainingCount);
            var extra=screen.CurrentHand.Where(c=>!before.Contains(c)).ToArray();
            screen.TrySelectCard(extra[0]);Assert.IsTrue(state.TryPreview("large"));Assert.AreEqual(12,screen.CurrentHand.Count+screen.SelectedCards.Count);
            Assert.IsTrue(state.TryCancelPending());Assert.AreEqual("large",state.PendingId);
            Assert.IsTrue(state.TryCancelPending());Assert.IsNull(state.PendingId);Assert.AreEqual(10,screen.MaxHandSize);Assert.AreEqual(10,screen.CurrentHand.Count);Assert.AreEqual(10,deck.RemainingCount);
            Assert.IsTrue(state.TryPreview("large"));CollectionAssert.AreEqual(extra,screen.CurrentHand.Where(c=>!before.Contains(c)));
            Assert.IsTrue(state.TryPreview("small"));Assert.AreEqual(11,screen.CurrentHand.Count);Assert.AreEqual(9,deck.RemainingCount);
            Assert.IsTrue(state.TryCancelPending());Assert.AreEqual(10,deck.RemainingCount);
            state.TryPreview("large");CollectionAssert.AreEqual(extra,screen.CurrentHand.Where(c=>!before.Contains(c)));
        }
        [Test]
        public void 確定した追加分は取消再試行や上限低下で失わず使用済みを保持する()
        {
            var b=HandBonusBattle(out var deck);var s=b.CustomScreen;var t=b.Transformations;t.TryPreview("large");
            var extra=s.CurrentHand.Last();s.TrySelectCard(extra);s.Confirm();
            Assert.AreEqual(12,s.MaxHandSize);Assert.AreEqual(11,s.CurrentHand.Count);Assert.AreEqual(1,s.AvailableCards.Count);Assert.AreEqual(8,deck.RemainingCount);
            s.CancelConfirmation();s.Confirm();Assert.AreEqual(8,deck.RemainingCount);Assert.AreEqual(1,t.UsedIds.Count);
            s.Close();b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());
            Assert.AreEqual(12,s.CurrentHand.Count);Assert.AreEqual(7,deck.RemainingCount);
            t.TryPreview("small");Assert.AreEqual(11,s.MaxHandSize);Assert.AreEqual(12,s.CurrentHand.Count);Assert.AreEqual(7,deck.RemainingCount);
            t.TryCancelPending();Assert.AreEqual(12,s.MaxHandSize);Assert.AreEqual("large",t.ActiveId);
            t.TryPreview("small");s.Confirm();Assert.AreEqual(2,t.UsedIds.Count);Assert.AreEqual(11,s.MaxHandSize);Assert.AreEqual(12,s.CurrentHand.Count);
            player.EndTransformationDefense();Assert.AreEqual(10,s.MaxHandSize);Assert.AreEqual(12,s.CurrentHand.Count);Assert.IsNull(t.ActiveId);Assert.AreEqual(2,t.UsedIds.Count);
        }
        [Test]
        public void 変身期限切れは開き直しの補充前に上限を戻し超過分を捨てない()
        {
            var b=HandBonusBattle(out var deck);var s=b.CustomScreen;b.Transformations.TryPreview("large");s.Confirm();
            for(int i=0;i<2;i++){s.Close();b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());s.Confirm();}
            Assert.AreEqual(1,player.RemainingTransformationPhases);s.Close();
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());
            Assert.IsNull(b.Transformations.ActiveId);Assert.AreEqual(10,s.MaxHandSize);Assert.AreEqual(12,s.CurrentHand.Count);Assert.AreEqual(8,deck.RemainingCount);
        }
        [Test]
        public void 未確定画面閉鎖と強制解除は追加分だけ返し元手札を保持する()
        {
            var b=HandBonusBattle(out var deck);var s=b.CustomScreen;b.Transformations.TryPreview("large");
            s.TrySelectCard(s.CurrentHand.First());s.TrySelectCard(s.CurrentHand.Last());s.Close();
            Assert.AreEqual(10,s.CurrentHand.Count);Assert.AreEqual(10,deck.RemainingCount);Assert.IsEmpty(b.Transformations.UsedIds);
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());b.Transformations.TryPreview("large");s.Confirm();
            s.Close();b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());
            b.Transformations.TryPreview("small");player.TakeDamage(1,attackElement:ElementType.Fire);
            Assert.IsNull(b.Transformations.PendingId);Assert.IsNull(b.Transformations.ActiveId);Assert.AreEqual(10,s.MaxHandSize);Assert.AreEqual(12,s.CurrentHand.Count);Assert.IsTrue(b.Transformations.IsUsed("large"));
        }
        [Test]
        public void 変身手札加算の負値と実効上限のoverflowは部分変更を起こさない()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationDefenseDefinition("x",ElementResistances.Neutral,ElementType.Fire,-1));
            var b=HandBonusBattle(out var deck,int.MaxValue);var first=b.CustomScreen.CurrentHand[0];b.CustomScreen.TrySelectCard(first);
            Assert.Throws<System.OverflowException>(()=>b.Transformations.TryPreview("large"));Assert.AreEqual(10,b.CustomScreen.MaxHandSize);Assert.AreEqual(10,deck.RemainingCount);Assert.AreSame(first,b.CustomScreen.SelectedCards[0]);Assert.IsNull(b.Transformations.PendingId);
        }

        [Test]
        public void 追加ドロー由来の連鎖加工だけ取消し無関係な加工と元の山札個体を保持する()
        {
            Card Card(string id)=>new Card(id,id,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,1,"検証");
            var cards=new[]{"a","b","c","d","e","f"}.Select(Card).ToList();var deck=new Deck(cards);
            var factory=new MockCardFactory();foreach(var id in new[]{"x","y","z"})factory.Register(id,Card(id));
            var recipes=new MockFusionRecipeProvider();recipes.Register("x","e","a");recipes.Register("y","x","f");recipes.Register("z","c","d");
            var screen=new CustomScreenSystem(deck,maxHandSize:4,fusionSystem:new FusionSystem(recipes,factory));
            using(var state=new TransformationSelectionState(player,screen)) {
                state.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire,2)});
                screen.Open(new Card[0],new MockRandomProvider());state.TryPreview("fixture");
                var x=screen.ExecuteFusion(new[]{cards[4],cards[0]});Assert.IsTrue(x.IsSuccess);
                var y=screen.ExecuteFusion(new[]{x.ResultCard,cards[5]});Assert.IsTrue(y.IsSuccess);
                var z=screen.ExecuteFusion(new[]{cards[2],cards[3]});Assert.IsTrue(z.IsSuccess);
                Assert.IsTrue(state.TryCancelPending());
                CollectionAssert.AreEquivalent(new[]{"a","b","z"},screen.CurrentHand.Select(c=>c.Id));Assert.AreSame(z.ResultCard,screen.CurrentHand.Single(c=>c.Id=="z"));
                Assert.AreEqual(2,deck.RemainingCount);Assert.AreEqual(1,screen.CustomizationUndoCount);
                state.TryPreview("fixture");var extras=screen.CurrentHand.Where(c=>c.Id=="e"||c.Id=="f").ToArray();
                CollectionAssert.AreEqual(new[]{cards[4],cards[5]},extras);Assert.AreEqual(0,deck.RemainingCount);
            }
        }

        private static Card ConversionCard(string id) => new Card(id,id,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,10,"検証");
        [Test]
        public void 変換は一段だけで元個体と共有テンプレートを変えず取消再選択で二重適用しない()
        {
            var originals=Enumerable.Range(0,12).Select(i=>ConversionCard("c"+i)).ToList();var deck=new Deck(originals);var screen=new CustomScreenSystem(deck);
            var template=ConversionCard("c1");template.Modifier.BonusDamage=5;
            var rules=new[]{new TransformationCardConversion("c0",template),new TransformationCardConversion("c1",ConversionCard("c2"))};
            var definition=new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire,cardConversions:rules);
            template.Modifier.BonusDamage=999;
            using(var state=new TransformationSelectionState(player,screen)) {
                state.Configure(new[]{definition});screen.Open(new Card[0],new MockRandomProvider());screen.TrySelectCard(originals[0]);
                state.TryPreview("fixture");Assert.IsEmpty(screen.SelectedCards);var converted=screen.CurrentHand.Single(c=>c.Id=="c1");
                Assert.AreEqual(15,converted.GetFinalPower());Assert.AreNotSame(template,converted);Assert.AreNotSame(originals[1],converted);
                Assert.AreEqual(2,screen.CurrentHand.Count(c=>c.Id=="c2"),"c0→c1を同選択内でc2へ進めない");
                state.TryPreview("fixture");Assert.AreSame(converted,screen.CurrentHand.Single(c=>c.Id=="c1"));
                state.TryCancelPending();CollectionAssert.AreEquivalent(originals.Take(10),screen.CurrentHand);Assert.AreEqual(10,originals[0].GetFinalPower());
                state.TryPreview("fixture");Assert.AreEqual(15,screen.CurrentHand.Single(c=>c.Id=="c1").GetFinalPower());state.TryCancelPending();
                CollectionAssert.AreEqual(originals,deck.GetAllCards());
            }
        }
        [Test]
        public void 変換と追加ドローの依存加工だけ連鎖取消し無関係な加工を保持する()
        {
            var cards=new[]{"a","b","c","d","u","v","g","h"}.Select(ConversionCard).ToList();var deck=new Deck(cards);
            var factory=new MockCardFactory();foreach(var id in new[]{"x","y","z"})factory.Register(id,ConversionCard(id));
            var recipes=new MockFusionRecipeProvider();recipes.Register("x","q","b");recipes.Register("y","x","c");recipes.Register("z","d","u");
            var screen=new CustomScreenSystem(deck,maxHandSize:6,fusionSystem:new FusionSystem(recipes,factory));
            var template=ConversionCard("q");
            using(var state=new TransformationSelectionState(player,screen)) {
                state.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire,2,new[]{new TransformationCardConversion("a",template),new TransformationCardConversion("g",template)})});
                screen.Open(new Card[0],new MockRandomProvider());state.TryPreview("fixture");Assert.AreEqual(2,screen.CurrentHand.Count(c=>c.Id=="q"));
                var x=screen.ExecuteFusion(new[]{screen.CurrentHand[0],cards[1]});Assert.IsTrue(x.IsSuccess);
                var y=screen.ExecuteFusion(new[]{x.ResultCard,cards[2]});Assert.IsTrue(y.IsSuccess);
                var z=screen.ExecuteFusion(new[]{cards[3],cards[4]});Assert.IsTrue(z.IsSuccess);
                screen.TrySelectCard(y.ResultCard);state.TryCancelPending();Assert.IsNotNull(state.PendingId,"末尾選択解除が先");state.TryCancelPending();
                CollectionAssert.AreEquivalent(new[]{"a","b","c","z","v"},screen.CurrentHand.Select(c=>c.Id));
                Assert.AreSame(cards[0],screen.CurrentHand.Single(c=>c.Id=="a"));Assert.AreSame(z.ResultCard,screen.CurrentHand.Single(c=>c.Id=="z"));
                Assert.AreEqual(2,deck.RemainingCount);Assert.AreEqual(1,screen.CustomizationUndoCount);Assert.AreEqual(10,template.GetFinalPower());
                state.TryPreview("fixture");Assert.AreEqual(2,screen.CurrentHand.Count(c=>c.Id=="q"));state.TryCancelPending();
                CollectionAssert.AreEqual(new[]{cards[6],cards[7]},deck.GetRemainingCards());
            }
        }
        [Test]
        public void 確定変換は終了後も保持し継続中の新規ドローだけ変換する()
        {
            var cards=Enumerable.Range(0,20).Select(i=>ConversionCard("c"+i)).ToList();var deck=new Deck(cards);
            var b=new BattleSystem(player,new List<Enemy>{new Enemy("e","検証",9999,ElementType.None,new Vector2Int(5,0))},deck);
            var rules=cards.Select(c=>new TransformationCardConversion(c.Id,ConversionCard("converted"))).Concat(new[]{new TransformationCardConversion("converted",ConversionCard("twice"))}).ToArray();
            b.Transformations.Configure(new[]{new TransformationDefenseDefinition("one",ElementResistances.Neutral,ElementType.Fire,2,rules),new TransformationDefenseDefinition("two",ElementResistances.Neutral,ElementType.Water,1,new[]{new TransformationCardConversion("converted",ConversionCard("second"))})});
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());var s=b.CustomScreen;var t=b.Transformations;
            t.TryPreview("one");Assert.AreEqual(12,s.CurrentHand.Count(c=>c.Id=="converted"));var first=s.CurrentHand[0];s.TrySelectCard(first);s.Confirm();Assert.AreSame(first,s.AvailableCards[0]);s.Close();
            b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());Assert.AreEqual(12,s.CurrentHand.Count(c=>c.Id=="converted"),"既存手札をtwiceへ再変換しない");Assert.AreEqual(7,deck.RemainingCount);
            var baseline=s.CurrentHand.ToArray();t.TryPreview("two");Assert.IsTrue(s.CurrentHand.All(c=>c.Id=="second"));t.TryCancelPending();CollectionAssert.AreEqual(baseline,s.CurrentHand);
            t.TryPreview("two");s.Confirm();Assert.AreEqual(2,t.UsedIds.Count);player.EndTransformationDefense();Assert.IsTrue(s.CurrentHand.All(c=>c.Id=="second"));Assert.AreEqual(12,s.CurrentHand.Count);Assert.AreEqual(10,s.MaxHandSize);
            s.Close();b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());Assert.IsTrue(s.CurrentHand.All(c=>c.Id=="second"));CollectionAssert.AreEqual(cards,deck.GetAllCards());
        }
        [Test]
        public void 同IDの変換でも未確定閉鎖で元の加工済み個体へ戻り定義の重複を拒否する()
        {
            var original=ConversionCard("a");original.Modifier.BonusDamage=7;var screen=new CustomScreenSystem(new Deck(new List<Card>{original}));
            var target=ConversionCard("a");target.Modifier.BonusDamage=2;var rule=new TransformationCardConversion("a",target);
            Assert.Throws<System.ArgumentException>(()=>new TransformationDefenseDefinition("bad",ElementResistances.Neutral,ElementType.Fire,cardConversions:new[]{rule,rule}));
            using(var state=new TransformationSelectionState(player,screen)) {
                state.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire,cardConversions:new[]{rule})});screen.Open(new Card[0],new MockRandomProvider());state.TryPreview("fixture");
                Assert.AreEqual(12,screen.CurrentHand[0].GetFinalPower());screen.TrySelectCard(screen.CurrentHand[0]);screen.Close();
                Assert.AreSame(original,screen.CurrentHand[0]);Assert.AreEqual(17,original.GetFinalPower());Assert.IsEmpty(state.UsedIds);Assert.AreEqual(2,target.Modifier.BonusDamage);
            }
        }

        private BattleSystem CreateTransformationBattle(int secondFireResistance = 100)
        {
            var cards = new List<Card> {
                new Card("fixture-1", "検証1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証"),
                new Card("fixture-2", "検証2", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証") };
            var battle = new BattleSystem(player, new List<Enemy> { new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1,0)) }, new Deck(cards), logger: logger);
            battle.Transformations.Configure(new[] {
                new TransformationDefenseDefinition("fixture-a", new ElementResistances(fire:50), ElementType.Water),
                new TransformationDefenseDefinition("fixture-b", new ElementResistances(fire:secondFireResistance), ElementType.Fire) });
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            return battle;
        }
        [Test]
        public void オーラとの未規定競合で変身仮選択や使用済み状態を変更しない()
        {
            var battle = CreateTransformationBattle(); player.SetAura(10);
            Assert.IsFalse(battle.Transformations.TryPreview("fixture-a"));
            Assert.IsFalse(player.HasTransformationPreview); Assert.AreEqual(10, player.AuraThreshold);
            player.SetAura(0); Assert.IsTrue(battle.Transformations.TryPreview("fixture-a"));
            Assert.Throws<System.InvalidOperationException>(() => player.SetAura(10));
            Assert.IsTrue(player.HasTransformationPreview); Assert.IsFalse(player.HasAura);
        }

        private void ReopenTransformationScreen(BattleSystem battle)
        {
            battle.CustomScreen.Close();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            battle.OpenCustomScreen(new MockRandomProvider());
        }
        [Test]
        public void 変身一覧は使用済みも残し呼出元から変更できない()
        {
            var battle = CreateTransformationBattle();
            var state = battle.Transformations;
            var choices = state.AvailableDefinitions;
            CollectionAssert.AreEqual(new[] { "fixture-a", "fixture-b" }, choices.Select(d => d.Id));
            Assert.Throws<System.NotSupportedException>(() =>
                ((IList<TransformationDefenseDefinition>)choices).Clear());
            state.TryPreview("fixture-a");
            battle.CustomScreen.Confirm();
            Assert.AreEqual(2, choices.Count);
            Assert.IsTrue(state.IsUsed(choices[0].Id));
            ReopenTransformationScreen(battle);
            Assert.IsFalse(state.TryPreview(choices[0].Id));
            Assert.IsTrue(state.TryPreview(choices[1].Id));
        }

        [Test]
        public void 重複ID定義の失敗で一覧を部分公開せず元リストの変更も反映しない()
        {
            var battle = new BattleSystem(player, new List<Enemy> {
                new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0)) }, new Deck(new List<Card>()));
            var state = battle.Transformations;
            var definition = new TransformationDefenseDefinition("fixture", ElementResistances.Neutral, ElementType.Fire);
            Assert.IsEmpty(state.AvailableDefinitions);
            Assert.Throws<System.ArgumentException>(() => state.Configure(new[] { definition, definition }));
            Assert.IsEmpty(state.AvailableDefinitions);
            var source = new List<TransformationDefenseDefinition> { definition };
            state.Configure(source);
            source.Clear();
            Assert.AreEqual(1, state.AvailableDefinitions.Count);
            Assert.AreSame(definition, state.AvailableDefinitions[0]);
        }

        [Test]
        public void 仮選択は未消費で確定時だけ消費し確定再試行で二重消費しない()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            Assert.IsTrue(state.TryPreview("fixture-a"));
            Assert.AreEqual(50,player.ActiveResistances.Fire);Assert.IsTrue(player.HasTransformationPreview);
            Assert.IsFalse(player.HasTransformationDefense);Assert.IsEmpty(state.UsedIds);
            battle.CustomScreen.Confirm();
            Assert.AreEqual("fixture-a",state.ActiveId);Assert.IsNull(state.PendingId);
            Assert.IsFalse(player.HasTransformationPreview);Assert.AreEqual(3,player.RemainingTransformationPhases);
            Assert.AreEqual(1,state.UsedIds.Count);
            battle.CustomScreen.CancelConfirmation();
            Assert.IsFalse(state.TryPreview("fixture-b"));Assert.IsFalse(state.TryCancelPending());
            battle.CustomScreen.Confirm();Assert.AreEqual(1,state.UsedIds.Count);Assert.AreEqual(3,player.RemainingTransformationPhases);
        }
        [Test]
        public void 仮選択取消は末尾カードを先に外し継続変身と開始時残回数へ戻す()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();ReopenTransformationScreen(battle);
            Assert.AreEqual(2,player.RemainingTransformationPhases);
            var first=battle.CustomScreen.CurrentHand[0];var last=battle.CustomScreen.CurrentHand[1];
            battle.CustomScreen.TrySelectCard(first);battle.CustomScreen.TrySelectCard(last);
            Assert.IsTrue(state.TryPreview("fixture-b"));Assert.IsEmpty(battle.CustomScreen.SelectedCards);
            battle.CustomScreen.TrySelectCard(first);battle.CustomScreen.TrySelectCard(last);
            Assert.IsTrue(state.TryCancelPending());Assert.AreSame(first,battle.CustomScreen.SelectedCards[0]);
            Assert.AreEqual("fixture-b",state.PendingId);Assert.AreEqual(100,player.ActiveResistances.Fire);
            Assert.IsTrue(state.TryCancelPending());Assert.IsEmpty(battle.CustomScreen.SelectedCards);
            Assert.AreEqual("fixture-b",state.PendingId);
            Assert.IsTrue(state.TryCancelPending());Assert.IsNull(state.PendingId);
            Assert.AreEqual("fixture-a",state.ActiveId);Assert.AreEqual(50,player.ActiveResistances.Fire);
            Assert.AreEqual(2,player.RemainingTransformationPhases);Assert.IsFalse(state.IsUsed("fixture-b"));
            Assert.IsFalse(state.TryCancelPending(), "継続中の変身は任意解除できない");
        }
        [Test]
        public void 別変身確定は両ID使用済みで強制解除後に前変身へ戻さない()
        {
            var battle=CreateTransformationBattle(50);var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();ReopenTransformationScreen(battle);
            Assert.IsFalse(state.TryPreview("fixture-a"));Assert.IsTrue(state.TryPreview("fixture-b"));
            battle.CustomScreen.Confirm();Assert.AreEqual("fixture-b",state.ActiveId);
            Assert.AreEqual(3,player.RemainingTransformationPhases);Assert.AreEqual(2,state.UsedIds.Count);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            Assert.AreEqual(95,player.CurrentHp);Assert.IsNull(state.ActiveId);
            Assert.IsFalse(player.HasTransformationDefense);Assert.AreSame(player.Resistances,player.ActiveResistances);
            ReopenTransformationScreen(battle);Assert.IsFalse(state.TryPreview("fixture-a"));Assert.IsFalse(state.TryPreview("fixture-b"));
        }
        [Test]
        public void 期間切れも使用済みを維持し次戦は新しい使用集合になる()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            state.TryPreview("fixture-a");battle.CustomScreen.Confirm();
            for(int n=0;n<3;n++)ReopenTransformationScreen(battle);
            Assert.IsNull(state.ActiveId);Assert.IsTrue(state.IsUsed("fixture-a"));Assert.IsFalse(state.TryPreview("fixture-a"));
            battle.Enemies[0].TakeDamage(100,ElementType.None);
            Assert.IsFalse(state.TryPreview("fixture-b"));
            var next=CreateTransformationBattle();Assert.IsEmpty(next.Transformations.UsedIds);
            Assert.IsTrue(next.Transformations.TryPreview("fixture-a"));
        }
        [Test]
        public void 未確定で画面を閉じても消費せず仮反映だけ解除する()
        {
            var battle=CreateTransformationBattle();var state=battle.Transformations;
            Assert.IsFalse(state.TryPreview("undefined"));Assert.IsFalse(state.TryPreview(null));
            state.TryPreview("fixture-a");state.TryPreview("fixture-b");
            Assert.AreEqual("fixture-b",state.PendingId);Assert.IsEmpty(state.UsedIds);
            battle.CustomScreen.Cancel();Assert.IsNull(state.PendingId);Assert.IsFalse(player.HasTransformationPreview);
            Assert.IsEmpty(state.UsedIds);Assert.IsFalse(state.TryPreview("fixture-a"));
            battle.CustomScreen.Open(new Card[0],new MockRandomProvider());Assert.IsTrue(state.TryPreview("fixture-a"));
        }
        [Test]
        public void 空確定後も同じ画面の再試行では新しい変身を選べない()
        {
            var battle=CreateTransformationBattle();battle.CustomScreen.Confirm();battle.CustomScreen.CancelConfirmation();
            Assert.IsFalse(battle.Transformations.TryPreview("fixture-a"));
            battle.CustomScreen.Confirm();Assert.IsEmpty(battle.Transformations.UsedIds);
            ReopenTransformationScreen(battle);Assert.IsTrue(battle.Transformations.TryPreview("fixture-a"));
        }
        [Test]
        public void 不正定義と戦闘内の定義差替えを拒否する()
        {
            Assert.Throws<System.ArgumentException>(()=>new TransformationDefenseDefinition(" ",ElementResistances.Neutral,ElementType.Fire));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationDefenseDefinition("id",ElementResistances.Neutral,ElementType.None));
            var battle=CreateTransformationBattle();
            Assert.Throws<System.InvalidOperationException>(()=>battle.Transformations.Configure(new TransformationDefenseDefinition[0]));
        }

        [Test]
        public void 確定した画面を数えず次の三回目開始で耐性置換を解除する()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire: 50), ElementType.Water);
            int ended = 0; player.OnTransformationDefenseEnded += () => ended++;
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            battle.CustomScreen.Confirm();battle.CustomScreen.CancelConfirmation();battle.CustomScreen.Confirm();
            Assert.AreEqual(3, player.RemainingTransformationPhases, "重複Openや確定再試行で消費しない");
            for (int remaining = 2; remaining >= 0; remaining--)
            {
                battle.CustomScreen.Close();
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battle.OpenCustomScreen(new MockRandomProvider());
                Assert.AreEqual(remaining, player.RemainingTransformationPhases);
                Assert.AreEqual(remaining > 0, player.HasTransformationDefense);
                Assert.AreEqual(remaining == 0 ? 1 : 0, ended);
            }
            Assert.AreSame(player.Resistances, player.ActiveResistances);
        }

        [Test]
        public void 切替で三回へ戻り被弾解除で残回数も消える()
        {
            var battle = CreateBattle(new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0)));
            battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire: 50), ElementType.Water);
            battle.CustomScreen.Close();battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            Assert.AreEqual(2,player.RemainingTransformationPhases);
            player.BeginTransformationDefense(new ElementResistances(water: 50), ElementType.Fire);
            Assert.AreEqual(3,player.RemainingTransformationPhases);
            player.TakeDamage(1,attackElement:ElementType.Fire);
            Assert.AreEqual(0,player.RemainingTransformationPhases);
            battle.CustomScreen.Close();battle.CustomScreen.Open(new Card[0], new MockRandomProvider());
            Assert.AreEqual(0,player.RemainingTransformationPhases);
        }

        [Test]
        public void 戦闘終了で残回数を解除し次戦で古い購読を動かさない()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            player.BeginTransformationDefense(new ElementResistances(fire: 50),ElementType.Water);
            enemy.TakeDamage(100,ElementType.None);
            Assert.AreEqual(0,player.RemainingTransformationPhases);
            Assert.IsFalse(player.HasTransformationDefense);
            var next=CreateBattle(new Enemy("next","敵",100,ElementType.None,new Vector2Int(1,0)));
            next.CustomScreen.Open(new Card[0],new MockRandomProvider());
            player.BeginTransformationDefense(new ElementResistances(fire:50),ElementType.Water);
            battle.CustomScreen.Open(new Card[0],new MockRandomProvider());
            Assert.AreEqual(3,player.RemainingTransformationPhases);
            next.CustomScreen.Close();next.CustomScreen.Open(new Card[0],new MockRandomProvider());
            Assert.AreEqual(2,player.RemainingTransformationPhases);
        }

        [TestCase(-50, 1073741823)]
        [TestCase(int.MinValue, 600000000)]
        public void 負耐性の大ダメージを回復して再被弾しても強靭度累計が符号反転しない(int resistance,int damage)
        {
            var target = new PlayerCharacter(int.MaxValue, poise: int.MaxValue);
            target.SetElementResistances(new ElementResistances(fire: resistance));
            int staggered = 0;
            target.OnStaggered += () => staggered++;
            target.TakeDamage(damage, attackElement: ElementType.Fire);
            Assert.AreEqual(0, staggered);
            int received = int.MaxValue - target.CurrentHp;
            Assert.Greater(received, int.MaxValue / 2);
            target.Heal(received);
            target.TakeDamage(damage, attackElement: ElementType.Fire);
            Assert.AreEqual(1, staggered);
            Assert.AreEqual(0, target.AccumulatedDamage);
            Assert.Greater(target.CurrentHp, 0);
        }

        [TestCase(0, 7)][TestCase(50, 3)][TestCase(-50, 10)][TestCase(-100, 14)][TestCase(100, 0)][TestCase(-200, 14)]
        public void 属性と独立した耐性で最後に切り捨てる(int resistance, int expected)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Fire, Vector2Int.Zero,
                resistances: new ElementResistances(water: resistance));
            enemy.TakeDamage(7, ElementType.Water);
            Assert.AreEqual(100 - expected, enemy.CurrentHp);
            player.SetElementResistances(new ElementResistances(water: resistance));
            player.TakeDamage(7, attackElement: ElementType.Water);
            Assert.AreEqual(100 - expected, player.CurrentHp);
        }
        [Test]
        public void 自身属性から弱点を推測せず戦闘値だけ上下限で制限する()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Fire, Vector2Int.Zero);
            enemy.TakeDamage(10, ElementType.Water);Assert.AreEqual(90, enemy.CurrentHp);
            Assert.AreEqual(100, new ElementResistances(fire:101).Get(ElementType.Fire));
            Assert.DoesNotThrow(() => new ElementResistances(fire:int.MinValue));
            Assert.AreEqual(int.MaxValue,new ElementResistances(fire:int.MinValue).CalculateDamage(int.MaxValue,ElementType.Fire,true));
        }
        [TestCase(50, 7)][TestCase(-50, 21)][TestCase(-100, 28)][TestCase(100, 0)]
        public void 草の火倍率と耐性を途中丸めせず乗算する(int resistance,int expected)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.Water,new Vector2Int(1,0),resistances:new ElementResistances(fire:resistance));
            var battle=CreateBattle(enemy);battle.Field.ChangePanelType(enemy.GridPosition,PanelType.Grass);
            enemy.TakeDamage(7,ElementType.Fire);Assert.AreEqual(100-expected,enemy.CurrentHp);
            player.SetElementResistances(new ElementResistances(fire:resistance));battle.Field.ChangePanelType(player.GridPosition,PanelType.Grass);
            player.TakeDamage(7,attackElement:ElementType.Fire);Assert.AreEqual(100-expected,player.CurrentHp);
        }
        [TestCase(false, 50, 95)][TestCase(false, 99, 99)][TestCase(false, 100, 100)][TestCase(false, 150, 100)][TestCase(true, 50, 100)]
        public void 変身中の耐性で計算後に解除し無敵や耐性完全無効なら解除しない(bool invincible,int formResistance,int hp)
        {
            player.SetElementResistances(new ElementResistances(fire:-100));
            player.BeginTransformationDefense(new ElementResistances(fire:formResistance),ElementType.Fire);
            int endedHp=-1;player.OnTransformationDefenseEnded+=()=>endedHp=player.CurrentHp;
            if(invincible)player.SetInvincible(1000);
            player.TakeDamage(10,attackElement:ElementType.Fire);
            bool preserved = invincible || formResistance >= 100;
            Assert.AreEqual(hp,player.CurrentHp);Assert.AreEqual(preserved,player.HasTransformationDefense);
            Assert.AreEqual(preserved?-1:hp,endedHp);
            if(!preserved){player.TakeDamage(10,attackElement:ElementType.Fire);Assert.AreEqual(hp-20,player.CurrentHp);}
            else { player.TakeDamage(10,attackElement:ElementType.Fire); Assert.AreEqual(hp,player.CurrentHp); Assert.AreEqual(3,player.RemainingTransformationPhases); }
        }
        [Test]
        public void 解除用属性以外なら置換耐性を維持する()
        {
            player.BeginTransformationDefense(new ElementResistances(water:50),ElementType.Fire);
            player.TakeDamage(10,attackElement:ElementType.Water);
            Assert.AreEqual(95,player.CurrentHp);Assert.IsTrue(player.HasTransformationDefense);
        }

        [TestCase(AttackProperty.Sword, AttackProperty.Sword | AttackProperty.Break, true)]
        [TestCase(AttackProperty.Break, AttackProperty.Sword | AttackProperty.Break, true)]
        [TestCase(AttackProperty.Sword, AttackProperty.Break, false)]
        [TestCase(AttackProperty.Break, AttackProperty.None, false)]
        public void 単一の性質弱点は複合攻撃と一致し属性倍率を追加しない(AttackProperty weakness, AttackProperty attack, bool matches)
        {
            player.BeginTransformationDefense(new ElementResistances(fire: 50), new TransformationWeakness(weakness));
            int ended = 0; player.OnTransformationDefenseEnded += () => ended++;
            player.TakeDamage(10, attackElement: ElementType.Fire, attackProperties: attack);
            Assert.AreEqual(95, player.CurrentHp);
            Assert.AreEqual(5, player.AccumulatedDamage);
            Assert.AreEqual(!matches, player.HasTransformationDefense);
            Assert.AreEqual(matches ? 1 : 0, ended);
        }

        [TestCase(AttackProperty.Sword)] [TestCase(AttackProperty.Break)]
        public void 性質弱点のダメージと強靭度を変身中耐性で処理してから解除する(AttackProperty weakness)
        {
            var target = new PlayerCharacter(100, poise: 4);
            target.SetElementResistances(new ElementResistances(fire: -100));
            target.BeginTransformationDefense(new ElementResistances(fire: 50), new TransformationWeakness(weakness));
            var order = new List<string>();
            target.OnHpChanged += (hp, max) => order.Add("hp:" + hp + ":" + target.HasTransformationDefense);
            target.OnStaggered += () => order.Add("stagger:" + target.HasTransformationDefense);
            target.OnTransformationDefenseEnded += () => order.Add("end:" + target.CurrentHp);
            target.TakeDamage(10, attackElement: ElementType.Fire, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            CollectionAssert.AreEqual(new[] { "hp:95:True", "stagger:True", "end:95" }, order);
            Assert.AreEqual(0, target.AccumulatedDamage);
            Assert.AreEqual(-100, target.ActiveResistances.Fire);
        }

        [Test]
        public void 性質一致だけでは別の属性弱点を解除せず無属性の性質弱点は解除する()
        {
            player.BeginTransformationDefense(ElementResistances.Neutral, ElementType.Water);
            player.TakeDamage(1, attackElement: ElementType.Fire, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            Assert.IsTrue(player.HasTransformationDefense);
            player.BeginTransformationDefense(ElementResistances.Neutral, new TransformationWeakness(AttackProperty.Sword));
            player.TakeDamage(1, attackProperties: AttackProperty.Sword);
            Assert.IsFalse(player.HasTransformationDefense); Assert.AreEqual(98, player.CurrentHp);
        }

        [Test]
        public void ブレイク性質もバリアのHP吸収と強靭度保護を迂回しない()
        {
            player.SetBarrier(20);
            player.TakeDamage(10, attackProperties: AttackProperty.Break);
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(10, player.BarrierHp);
            Assert.AreEqual(0, player.AccumulatedDamage);
            player.TakeDamage(15, attackProperties: AttackProperty.Sword | AttackProperty.Break);
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(0, player.BarrierHp);
            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [Test]
        public void 不正な複数弱点と未知性質を副作用前に拒否する()
        {
            foreach (var invalid in new[] { AttackProperty.None, AttackProperty.Sword | AttackProperty.Break, (AttackProperty)4 })
                Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness(invalid));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness(ElementType.None));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TransformationWeakness((ElementType)99));
            int changed = 0; player.OnHpChanged += (hp, max) => changed++;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => player.TakeDamage(10, attackProperties: (AttackProperty)8));
            Assert.AreEqual(0, changed); Assert.AreEqual(100, player.CurrentHp);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BasicAttackAction(10, 100, attackProperties: (AttackProperty)8));
            var enemy = new Enemy("e", "検証", 100, ElementType.None, Vector2Int.Zero);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => enemy.TakeDamage(10, ElementType.None, (AttackProperty)8));
            Assert.AreEqual(100, enemy.CurrentHp);
        }

        [Test]
        public void 一時移動攻撃も最終属性と複合性質を使用して完全無効を維持する()
        {
            var enemy = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                resistances: new ElementResistances(water: 100));
            var battle = CreateBattle(enemy);
            var card = new Card("move", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 10, "検証", modifier: new CardModifier { ElementOverride = ElementType.Water },
                properties: AttackProperty.Sword | AttackProperty.Break);
            int hits = 0; enemy.OnDamaged += (damage, multiplier) => hits++;
            var effect = new TemporaryMoveAttackEffect(new Vector2Int(1, 0), 1, 0, 0, MovePenetrationMode.Invincible);
            effect.ExecuteAsync(new BattleContext(battle, card), System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(100, enemy.CurrentHp); Assert.AreEqual(0, hits);
            Assert.AreEqual(Vector2Int.Zero, player.GridPosition); Assert.IsFalse(player.IsInTemporaryMove);
        }

        [TestCase(false, false, 0)] [TestCase(false, false, 1)]
        [TestCase(false, true, 0)] [TestCase(false, true, 999)]
        [TestCase(true, false, 0)] [TestCase(true, false, 1)]
        [TestCase(true, true, 0)] [TestCase(true, true, 999)]
        public void 風性質は威力と耐久値によらず終端で防御を剥がし同フレームを防ぐ(bool enemySide, bool aura, int power)
        {
            var enemy = new Enemy("e", "敵", 2000, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            if (enemySide) { if (aura) enemy.SetAura(10000); else enemy.SetBarrier(10000); }
            else { if (aura) player.SetAura(10000); else player.SetBarrier(10000); }
            void Hit(int amount, AttackProperty property) {
                if (enemySide) enemy.TakeDamage(amount, ElementType.Fire, property, amount > 0 ? 99 : 0);
                else player.TakeDamage(amount, attackElement: ElementType.Fire, attackProperties: property, poiseDamage: amount > 0 ? 99 : 0);
            }
            battle.RunLogicalFrame(() => {
                Hit(power, AttackProperty.Wind); Hit(1, AttackProperty.None);
                Assert.IsTrue(enemySide ? enemy.HasBarrier || enemy.HasAura : player.HasBarrier || player.HasAura);
            });
            Assert.AreEqual(enemySide ? 2000 : 100, enemySide ? enemy.CurrentHp : player.CurrentHp);
            Assert.IsFalse(enemySide ? enemy.HasBarrier || enemy.HasAura : player.HasBarrier || player.HasAura);
            Assert.AreEqual(0, enemySide ? enemy.AccumulatedPoiseDamage : player.AccumulatedDamage);
            battle.RunLogicalFrame(() => Hit(1, AttackProperty.None));
            Assert.AreEqual(enemySide ? 1999 : 99, enemySide ? enemy.CurrentHp : player.CurrentHp);
        }

        [TestCase(false)] [TestCase(true)]
        public void 風属性だけでは剥がさずガードを通過した風性質だけが剥がす(bool aura)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy);
            if (aura) enemy.SetAura(10000); else enemy.SetBarrier(10000);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.Wind));
            Assert.IsTrue(enemy.HasAura || enemy.HasBarrier);
            enemy.Guard.Set(true, false);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind));
            Assert.IsTrue(enemy.HasAura || enemy.HasBarrier);
            battle.RunLogicalFrame(() => enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind | AttackProperty.Break));
            Assert.IsFalse(enemy.HasAura || enemy.HasBarrier); Assert.AreEqual(100, enemy.CurrentHp);
        }

        [TestCase(false)] [TestCase(true)]
        public void 敵の通常攻撃経路でも風性質がプレイヤー防御を剥がし付随効果と被弾無敵を発生させない(bool aura)
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy); int effects = 0;
            if (aura) player.SetAura(10000); else player.SetBarrier(10000);
            var attack = new BasicAttackAction(1, 100, attackProperties: AttackProperty.Wind,
                poiseDamage: 30, onHit: p => effects++, hitInvincibilityMs: 600);
            var context = new AIContext(enemy, player, battle.Field, new MockRandomProvider(), logger);
            battle.RunLogicalFrame(() => attack.Execute(context));
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(0, effects);
            Assert.IsFalse(player.HasBarrier || player.HasAura); Assert.IsFalse(player.IsInvincible);
            battle.RunLogicalFrame(() => attack.Execute(context));
            Assert.AreEqual(99, player.CurrentHp); Assert.AreEqual(1, effects); Assert.IsTrue(player.IsInvincible);
        }

        [Test]
        public void 無敵中の風性質は防御を剥がさない()
        {
            var enemy = new Enemy("e", "敵", 100, ElementType.None, new Vector2Int(1, 0));
            var battle = CreateBattle(enemy); enemy.SetBarrier(1000); player.SetAura(1000);
            enemy.SetInvincible(600); player.SetInvincible(600);
            battle.RunLogicalFrame(() => {
                enemy.TakeDamage(1, ElementType.None, AttackProperty.Wind);
                player.TakeDamage(1, attackProperties: AttackProperty.Wind);
            });
            Assert.AreEqual(1000, enemy.BarrierHp); Assert.AreEqual(1000, player.AuraThreshold);
        }

        // ==================== Enemy.TakeDamage の属性相性テスト ====================

        [Test]
        public void 水耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            enemy.TakeDamage(50, ElementType.Water);

            Assert.AreEqual(0, enemy.CurrentHp, "50×2倍=100ダメージでHPが0になるはず");
        }

        [Test]
        public void 火耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("wind_slime", "風スライム", 100, ElementType.Wind, new Vector2Int(1, 0), resistances: new ElementResistances(fire: -100));
            enemy.TakeDamage(50, ElementType.Fire);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 風耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("earth_slime", "土スライム", 100, ElementType.Earth, new Vector2Int(1, 0), resistances: new ElementResistances(wind: -100));
            enemy.TakeDamage(50, ElementType.Wind);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 土耐性マイナス100で2倍ダメージになる()
        {
            var enemy = new Enemy("water_slime", "水スライム", 100, ElementType.Water, new Vector2Int(1, 0));
            enemy = new Enemy("water", "水", 100, ElementType.Water, new Vector2Int(1, 0), resistances: new ElementResistances(earth: -100));
            enemy.TakeDamage(50, ElementType.Earth);

            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void 逆相性は等倍ダメージになる()
        {
            var enemy = new Enemy("water_slime", "水スライム", 100, ElementType.Water, new Vector2Int(1, 0));
            enemy.TakeDamage(50, ElementType.Fire); // 水の弱点は土、火は無効

            Assert.AreEqual(50, enemy.CurrentHp, "逆相性は等倍（1.0倍）のはず");
        }

        [Test]
        public void 攻撃側がNoneのとき等倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            enemy.TakeDamage(30, ElementType.None);

            Assert.AreEqual(70, enemy.CurrentHp, "None属性は1.0倍のはず");
        }

        [Test]
        public void 防御側がNoneのとき等倍ダメージになる()
        {
            var enemy = new Enemy("none_slime", "スライム", 100, ElementType.None, new Vector2Int(1, 0));
            enemy.TakeDamage(30, ElementType.Water);

            Assert.AreEqual(70, enemy.CurrentHp, "相手がNoneなら1.0倍のはず");
        }

        [Test]
        public void OnDamagedイベントで2倍の倍率が通知される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            float receivedMultiplier = 0;
            enemy.OnDamaged += (damage, mult) => receivedMultiplier = mult;

            enemy.TakeDamage(10, ElementType.Water);

            Assert.AreEqual(2.0f, receivedMultiplier, 0.001f, "OnDamagedイベントで倍率2.0が通知されるはず");
        }

        // ==================== ModifiableCardEffect との統合テスト ====================

        [Test]
        public void 水属性カードで火属性の敵に2倍ダメージになる()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            var card = new Card(
                "water_cannon", "ウォーターキャノン", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.Water, CardSeries.Canon,
                30, "水属性攻撃", DamageTargetType.Single
            );
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(40, enemy.CurrentHp, "30×2倍=60ダメージでHP100→40になるはず");
        }

        [Test]
        public void ElementOverrideが設定されたカードで上書き属性で相性計算される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            // 無属性カードに「水属性上書き」のModifierを付与
            var modifier = new CardModifier { ElementOverride = ElementType.Water };
            var card = new Card(
                "elemental_cannon", "エレメンタルキャノン", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, // 元は無属性
                30, "属性上書き攻撃", DamageTargetType.Single,
                modifier: modifier
            );
            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            // 水属性として処理 → 火属性の敵に2倍ダメージ
            Assert.AreEqual(40, enemy.CurrentHp, "ElementOverrideで水属性になり30×2=60ダメージのはず");
        }

        [Test]
        public void DamageEffectでElementOverrideが正しく適用される()
        {
            var enemy = new Enemy("fire_slime", "炎スライム", 100, ElementType.Fire, new Vector2Int(1, 0), resistances: new ElementResistances(water: -100));
            var battle = CreateBattle(enemy);

            // 無属性カードに水属性オーバーライドを付与
            var modifier = new CardModifier { ElementOverride = ElementType.Water };
            var card = new Card(
                "elemental_atk", "属性攻撃", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon,
                20, "属性上書き", DamageTargetType.Single,
                modifier: modifier
            );

            var effect = new DamageEffect(20, DamageTargetType.Single);
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            // GetFinalElement()でElementOverride(Water)が使われ、火属性の敵に2倍ダメージ
            Assert.AreEqual(60, enemy.CurrentHp, "DamageEffectでも20×2=40ダメージになるはず");
        }
    }

    [TestFixture]
    public class AudioVolumeSettingsTests
    {
        private string directory;
        private sealed class FaultIo:Olca.Unity.CheckpointFileIo {
            public bool Before,After;
            public override void MoveNew(string t,string d){if(Before)throw new System.IO.IOException();base.MoveNew(t,d);if(After)throw new System.IO.IOException();}
            public override void Replace(string t,string d,string b){if(Before)throw new System.IO.IOException();base.Replace(t,d,b);if(After)throw new System.IO.IOException();}
        }
        [SetUp] public void Setup()=>directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-audio-"+System.Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup(){if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        [Test] public void 音量設定のない旧設定は元音量を維持し他設定を書き換えない(){
            System.IO.Directory.CreateDirectory(directory);var keyboard=System.IO.Path.Combine(directory,"keyboard-gameplay.json");System.IO.File.WriteAllText(keyboard,"existing-keyboard");
            var store=new Olca.Unity.AudioVolumeFileStore(directory);Assert.IsTrue(store.TryRead(out var value,out var error),error);
            Assert.AreEqual(100,value.Master);Assert.AreEqual(100,value.Bgm);Assert.AreEqual(100,value.Effects);Assert.AreEqual(100,value.Voice);
            Assert.IsFalse(System.IO.File.Exists(store.ActivePath));Assert.AreEqual("existing-keyboard",System.IO.File.ReadAllText(keyboard));
        }
        [TestCase(0,100,100,100)] [TestCase(100,100,100,100)] [TestCase(37,81,52,0)]
        public void 四系統のゼロと上限と個別値を独立保存し再読込できる(int master,int bgm,int effects,int voice){
            var store=new Olca.Unity.AudioVolumeFileStore(directory);Assert.IsTrue(store.TrySave(new Olca.Unity.AudioVolumeSettings(master,bgm,effects,voice),out var error),error);
            Assert.IsTrue(new Olca.Unity.AudioVolumeFileStore(directory).TryRead(out var value,out error),error);
            Assert.AreEqual(master,value.Master);Assert.AreEqual(bgm,value.Bgm);Assert.AreEqual(effects,value.Effects);Assert.AreEqual(voice,value.Voice);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new Olca.Unity.AudioVolumeSettings(-1));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new Olca.Unity.AudioVolumeSettings(voice:101));
        }
        [TestCase(false)] [TestCase(true)] public void 保存前失敗は旧音量を保ち保存直後の例外は内容照合で確定する(bool after){
            var io=new FaultIo();var store=new Olca.Unity.AudioVolumeFileStore(directory,io);Assert.IsTrue(store.TrySave(new Olca.Unity.AudioVolumeSettings(20,30,40,50),out _));
            var bytes=System.IO.File.ReadAllBytes(store.ActivePath);io.Before=!after;io.After=after;
            Assert.AreEqual(after,store.TrySave(new Olca.Unity.AudioVolumeSettings(0,100,0,99),out _));
            if(!after){CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));io.Before=false;Assert.IsTrue(store.TrySave(new Olca.Unity.AudioVolumeSettings(0,100,0,99),out _));}
            Assert.IsTrue(store.TryRead(out var value,out _));Assert.AreEqual(0,value.Master);Assert.AreEqual(99,value.Voice);
        }
        [TestCase("broken")]
        [TestCase("{\"Version\":1,\"Master\":20,\"Bgm\":30,\"Effects\":40}")]
        [TestCase("{\"Version\":1,\"Master\":101,\"Bgm\":30,\"Effects\":40,\"Voice\":50}")]
        [TestCase("{\"Version\":2,\"Master\":20,\"Bgm\":30,\"Effects\":40,\"Voice\":50}")]
        public void 壊れた設定や未対応形式は上書きしない(string content){
            System.IO.Directory.CreateDirectory(directory);var store=new Olca.Unity.AudioVolumeFileStore(directory);System.IO.File.WriteAllText(store.ActivePath,content);
            Assert.IsFalse(store.TryRead(out var muted,out _));Assert.AreEqual(0,muted.Master,"未知の音量を最大音量で再生しない");Assert.IsFalse(store.TrySave(new Olca.Unity.AudioVolumeSettings(),out _));Assert.AreEqual(content,System.IO.File.ReadAllText(store.ActivePath));
        }
        [Test] public void 実ソースは全体と区分と元音量を掛け二重適用せずListenerと未登録ソースを変えない(){
            var parent=new UnityEngine.GameObject("audio-test");var existing=parent.AddComponent<UnityEngine.AudioSource>();existing.volume=.4f;float listener=UnityEngine.AudioListener.volume;
            var other=new UnityEngine.GameObject("unregistered").AddComponent<UnityEngine.AudioSource>();other.volume=.3f;
            try{using(var router=new Olca.Unity.GameplayAudioRouter(parent.transform)){
                router.Register(existing,Olca.Unity.GameplayAudioChannel.Effects);router.Apply(new Olca.Unity.AudioVolumeSettings(20,25,50,100));
                Assert.AreEqual(.05f,router.Bgm.volume,.00001f);Assert.AreEqual(.1f,router.Effects.volume,.00001f);Assert.AreEqual(.2f,router.Voice.volume,.00001f);Assert.AreEqual(.04f,existing.volume,.00001f);
                router.Register(existing,Olca.Unity.GameplayAudioChannel.Effects);router.Apply(new Olca.Unity.AudioVolumeSettings(20,25,50,100));Assert.AreEqual(.04f,existing.volume,.00001f);
                router.SetPaused(true);router.Apply(new Olca.Unity.AudioVolumeSettings(0));Assert.IsTrue(router.IsPaused);Assert.AreEqual(0,router.Bgm.volume);Assert.AreEqual(0,router.Effects.volume);Assert.AreEqual(0,router.Voice.volume);
                router.Apply(new Olca.Unity.AudioVolumeSettings());Assert.AreEqual(.4f,existing.volume,.00001f);router.SetPaused(false);Assert.IsFalse(router.IsPaused);
                Assert.AreEqual(listener,UnityEngine.AudioListener.volume);Assert.AreEqual(.3f,other.volume,.00001f);
            }Assert.AreEqual(.4f,existing.volume,.00001f);}finally{UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(other.gameObject);}
        }
        [Test] public void 音量スライダーの左右入力は一パーセント変更し上下は画面内の項目へ移動する(){
            var eventRoot=new UnityEngine.GameObject("audio-input-events");var events=eventRoot.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var root=new UnityEngine.GameObject("audio-input-view",typeof(UnityEngine.RectTransform));
            try{int saved=0;var view=root.AddComponent<Olca.Presentation.Field.GameplayAudioSettingsView>();view.Initialize(UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf"),value=>{saved++;return null;},()=>{});
                view.Open(new Olca.Unity.AudioVolumeSettings(20,50,25,10),null);var slider=root.GetComponentsInChildren<UnityEngine.UI.Slider>().Single(s=>s.name=="AudioSliderMaster");events.SetSelectedGameObject(slider.gameObject);Assert.LessOrEqual(slider.handleRect.rect.height,((UnityEngine.RectTransform)slider.transform).rect.height,"ハンドルが隣の行へはみ出さない");
                var move=new UnityEngine.EventSystems.AxisEventData(events){moveDir=UnityEngine.EventSystems.MoveDirection.Right,moveVector=UnityEngine.Vector2.right};
                UnityEngine.EventSystems.ExecuteEvents.Execute(slider.gameObject,move,UnityEngine.EventSystems.ExecuteEvents.moveHandler);Assert.AreEqual(21,slider.value);Assert.AreEqual(0,saved,"候補変更で保存しない");
                slider.value=100;UnityEngine.EventSystems.ExecuteEvents.Execute(slider.gameObject,move,UnityEngine.EventSystems.ExecuteEvents.moveHandler);Assert.AreEqual(100,slider.value);
                move.moveDir=UnityEngine.EventSystems.MoveDirection.Down;move.moveVector=UnityEngine.Vector2.down;UnityEngine.EventSystems.ExecuteEvents.Execute(slider.gameObject,move,UnityEngine.EventSystems.ExecuteEvents.moveHandler);
                Assert.AreEqual("AudioSliderBgm",events.currentSelectedGameObject.name);
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(eventRoot);}
        }
        [Test] public void 新規開始も独立音量ファイルを保持する(){
            var audio=new Olca.Unity.AudioVolumeFileStore(System.IO.Path.Combine(directory,"settings"));Assert.IsTrue(audio.TrySave(new Olca.Unity.AudioVolumeSettings(10,20,0,40),out _));var bytes=System.IO.File.ReadAllBytes(audio.ActivePath);
            var progress=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(System.IO.Path.Combine(directory,"progress")));var starter=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            Assert.IsTrue(progress.InitializeStarter(starter));Assert.IsTrue(progress.StartNewGame(progress.Snapshot.Revision,starter));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(audio.ActivePath));
        }
    }

    [TestFixture] public class DialogueSessionTests
    {
        private static DialogueScript Plain()=>new DialogueScript("fixture",new[]{new DialogueLine("あいうえお","検証"),new DialogueLine("次の文章")});
        [Test] public void 段階表示と全文表示と次送りは独立し終了時に履歴を消す(){var s=new DialogueSession(Plain());Assert.AreEqual("",s.VisibleText);s.AdvanceFrame();Assert.AreEqual("",s.VisibleText);s.AdvanceFrame();Assert.AreEqual("あ",s.VisibleText);Assert.IsFalse(s.Backlog.Single().Contains("い"));Assert.IsTrue(s.Decide());Assert.AreEqual("あいうえお",s.VisibleText);Assert.AreEqual(0,s.Index);s.Decide();Assert.AreEqual(1,s.Index);s.Decide();s.Decide();Assert.IsFalse(s.IsOpen);Assert.IsEmpty(s.Backlog);Assert.AreEqual("",s.VisibleText);Assert.IsFalse(s.Decide());}
        [Test] public void 日本語結合文字とサロゲートを途中分断せず長文と即時表示に対応する(){var text="あ\u3099漢😀終";var s=new DialogueSession(new DialogueScript("unicode",new[]{new DialogueLine(text)}));s.AdvanceFrame();s.AdvanceFrame();Assert.AreEqual("あ\u3099",s.VisibleText);for(int n=0;n<4;n++)s.AdvanceFrame();Assert.AreEqual("あ\u3099漢😀",s.VisibleText);var longText=new string('漢',4000);s=new DialogueSession(new DialogueScript("long",new[]{new DialogueLine(longText)}),DialogueTextSpeed.Instant);Assert.AreEqual(longText,s.VisibleText);}
        [Test] public void ポーズとバックログとスキップ確認中は時計も送りも止まる(){var s=new DialogueSession(Plain());s.IsPaused=true;for(int n=0;n<100;n++)s.AdvanceFrame();Assert.AreEqual(0,s.VisibleElements);Assert.IsFalse(s.Decide());s.SetSpeed(DialogueTextSpeed.Instant);Assert.AreEqual(0,s.VisibleElements);s.IsPaused=false;s.AdvanceFrame();Assert.IsTrue(s.TextComplete);s.ToggleBacklog();s.AdvanceFrame();Assert.IsFalse(s.Decide());Assert.AreEqual(0,s.Index);s.ToggleBacklog();s.RequestSkip();Assert.IsFalse(s.Decide());s.AdvanceFrame();Assert.AreEqual(0,s.Index);s.CancelSkip();Assert.AreEqual(0,s.Index);Assert.IsFalse(s.ConfirmSkip());s.RequestSkip();Assert.IsTrue(s.ConfirmSkip());Assert.AreEqual(DialogueEndReason.Skipped,s.EndReason);Assert.IsEmpty(s.Backlog);Assert.IsFalse(s.ConfirmSkip());}
        [Test] public void 早送りは分岐で停止し選択後に再開し未選択文章を履歴へ漏らさない(){var script=new DialogueScript("branch",new[]{new DialogueLine("前"),new DialogueLine("選択",choices:new[]{new DialogueChoice("A",2),new DialogueChoice("B",3)}),new DialogueLine("未選択"),new DialogueLine("選択後")});var s=new DialogueSession(script);Assert.IsFalse(s.RequestSkip());s.ToggleFastForward();for(int n=0;n<100;n++)s.AdvanceFrame();Assert.AreEqual(1,s.Index);Assert.IsTrue(s.WaitingForChoice);Assert.IsTrue(s.FastForward);Assert.IsFalse(s.Decide());Assert.IsFalse(s.Choose(-1));Assert.IsTrue(s.Choose(1));Assert.AreEqual(3,s.Index);Assert.IsTrue(s.FastForward);Assert.IsFalse(s.Backlog.Any(x=>x.Contains("未選択")));for(int n=0;n<100;n++)s.AdvanceFrame();Assert.IsFalse(s.IsOpen);Assert.AreEqual(DialogueEndReason.Completed,s.EndReason);Assert.IsEmpty(s.Backlog);}
        [Test] public void 早送り解除と中断は新しい会話へ状態を持ち越さない(){var s=new DialogueSession(Plain());s.ToggleFastForward();s.ToggleFastForward();for(int n=0;n<1000;n++)s.AdvanceFrame();Assert.AreEqual(0,s.Index);s.Interrupt();s.Interrupt();Assert.AreEqual(DialogueEndReason.Interrupted,s.EndReason);Assert.IsEmpty(s.Backlog);var next=new DialogueSession(Plain());Assert.IsFalse(next.FastForward);Assert.IsEmpty(next.Backlog);Assert.Throws<System.ArgumentException>(()=>new DialogueScript("loop",new[]{new DialogueLine("x",choices:new[]{new DialogueChoice("loop",0)})}));}
        [Test] public void 表示速度の旧省略と保存障害と再読込を独立設定で扱う(){var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-dialogue-"+System.Guid.NewGuid().ToString("N"));try{var io=new SpeedFaultIo();var store=new Olca.Unity.DialogueTextSettingsStore(dir,io);Assert.IsTrue(store.TryRead(out var speed,out _));Assert.AreEqual(DialogueTextSpeed.Normal,speed);Assert.IsTrue(store.TrySave(DialogueTextSpeed.Fast,out _));var old=System.IO.File.ReadAllBytes(store.ActivePath);io.Fail=true;Assert.IsFalse(store.TrySave(DialogueTextSpeed.Instant,out _));CollectionAssert.AreEqual(old,System.IO.File.ReadAllBytes(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TrySave(DialogueTextSpeed.Instant,out _));Assert.IsTrue(new Olca.Unity.DialogueTextSettingsStore(dir).TryRead(out speed,out _));Assert.AreEqual(DialogueTextSpeed.Instant,speed);System.IO.File.WriteAllText(store.ActivePath,"broken");Assert.IsFalse(store.TrySave(DialogueTextSpeed.Slow,out _));Assert.AreEqual("broken",System.IO.File.ReadAllText(store.ActivePath));}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}}
        private sealed class SpeedFaultIo:Olca.Unity.CheckpointFileIo{public bool Fail;public override void Replace(string t,string d,string b){if(Fail)throw new System.IO.IOException();base.Replace(t,d,b);}}
    }
}

namespace Olca.Tests.Battle
{
    public class SimultaneousKillTests
    {
        private static BattleSystem Battle(int count=4, int hp=10)
        {
            var enemies=Enumerable.Range(0,count).Select(i=>new Enemy("same-id","テスト",hp,ElementType.None,new Vector2Int(3+i%3,0))).ToList();
            return new BattleSystem(new PlayerCharacter(10000,new Vector2Int(0,0)),enemies,new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
        }
        private static void Step(BattleSystem battle,int frames)
        {for(int i=0;i<frames;i++)battle.TickFrame(17);}
        private static void Kill(BattleSystem battle,int index,AttackInfluence influence=null)
            =>battle.Enemies[index].TakeDamage(1000,ElementType.None,influence:influence);

        [TestCase(0,2)] [TestCase(6,2)] [TestCase(7,1)]
        public void 猶予は最初の撃破から六論理フレーム以内で境界を含む(int gap,int expected)
        {
            var battle=Battle();Assert.AreEqual(0,battle.EnemyKillCount);Assert.AreEqual(0,battle.MaximumSimultaneousKills);Assert.AreEqual(6,battle.SimultaneousKillGraceFrames);
            Kill(battle,0);Step(battle,gap);Kill(battle,1);
            Assert.AreEqual(2,battle.EnemyKillCount);Assert.AreEqual(expected,battle.MaximumSimultaneousKills);
        }
        [Test]
        public void 追加撃破で起点を延長せず別の組を合算しない()
        {
            var battle=Battle(7);Kill(battle,0);Step(battle,5);Kill(battle,1);Step(battle,5);Kill(battle,2);
            Assert.AreEqual(2,battle.MaximumSimultaneousKills);
            Step(battle,7);battle.RunLogicalFrame(()=>{Kill(battle,3);Kill(battle,4);Kill(battle,5);});
            Assert.AreEqual(6,battle.EnemyKillCount);Assert.AreEqual(3,battle.MaximumSimultaneousKills);
        }
        [TestCase(0)] [TestCase(2)] [TestCase(12)]
        public void 猶予は更新開始前だけ調整でき途中で組の境界を書き換えない(int grace)
        {
            var battle=Battle();battle.ConfigureSimultaneousKillGraceFrames(grace);Assert.AreEqual(grace,battle.SimultaneousKillGraceFrames);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>battle.ConfigureSimultaneousKillGraceFrames(-1));
            Kill(battle,0);Assert.Throws<System.InvalidOperationException>(()=>battle.ConfigureSimultaneousKillGraceFrames(99));
            Step(battle,grace);Kill(battle,1);Assert.AreEqual(2,battle.MaximumSimultaneousKills);
            var advanced=Battle();Step(advanced,1);Assert.Throws<System.InvalidOperationException>(()=>advanced.ConfigureSimultaneousKillGraceFrames(99));
        }
        [Test]
        public void 同じIDでも敵個体を数え多段と再通知で同じ敵を重複計上しない()
        {
            var battle=Battle(3);int ended=0,maximumAtEnd=0;
            battle.OnBattleEnd+=result=>{ended++;maximumAtEnd=battle.MaximumSimultaneousKills;};
            battle.RunLogicalFrame(()=>{foreach(var enemy in battle.Enemies){enemy.TakeDamage(6,ElementType.None);enemy.TakeDamage(6,ElementType.None);enemy.TakeDamage(6,ElementType.None);}});
            Assert.AreEqual(3,battle.EnemyKillCount);Assert.AreEqual(3,maximumAtEnd);Assert.AreEqual(1,ended);Assert.IsTrue(battle.IsGameOver);
            Kill(battle,0);Step(battle,100);Assert.AreEqual(3,battle.EnemyKillCount);Assert.AreEqual(3,battle.MaximumSimultaneousKills);Assert.AreEqual(1,ended);
        }
        [Test]
        public void 存続中の同じ攻撃判定だけは猶予を超えて合流し命中後の破棄でも記録を失わない()
        {
            var battle=Battle();using(var source=new AttackInfluence()){
                Kill(battle,0,source);Step(battle,20);
                battle.RunLogicalFrame(()=>{Kill(battle,1,source);Kill(battle,2,source);source.Dispose();Assert.AreEqual(1,battle.EnemyKillCount);});
            }
            Assert.AreEqual(3,battle.EnemyKillCount);Assert.AreEqual(3,battle.MaximumSimultaneousKills);
        }
        [Test]
        public void 別判定は他の判定の存続を借用せず別組を結合もしない()
        {
            var battle=Battle(6);using(var a=new AttackInfluence())using(var b=new AttackInfluence()){
                Kill(battle,0,a);Step(battle,18);Kill(battle,1,b);Assert.AreEqual(1,battle.MaximumSimultaneousKills);
                Step(battle,2);Kill(battle,2,a);Assert.AreEqual(2,battle.MaximumSimultaneousKills);
                Step(battle,2);Kill(battle,3);Assert.AreEqual(3,battle.MaximumSimultaneousKills,"18/20/22の組。0の組とは合算しない");
                Step(battle,3);Kill(battle,4);Assert.AreEqual(3,battle.MaximumSimultaneousKills,"追加撃破で18の起点を延長しない");
            }
        }
        [Test]
        public void 終了済み判定や同じカードの再実行を同じ判定の延命として扱わない()
        {
            var battle=Battle();var source=new AttackInfluence();Kill(battle,0,source);source.Dispose();Step(battle,20);Kill(battle,1,source);
            Assert.AreEqual(1,battle.MaximumSimultaneousKills);
            var card=new Card("same-card","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,10,"テスト");
            var effect=new DamageEffect(10);var context=new BattleContext(battle,card);
            Step(battle,7);effect.Execute(context);Step(battle,7);effect.Execute(context);
            Assert.AreEqual(4,battle.EnemyKillCount);Assert.AreEqual(1,battle.MaximumSimultaneousKills);
        }
        [TestCase(false,2)] [TestCase(true,1)]
        public void HP集計は回復も含む致死境界のヒットを保持し後続の過剰ヒットへ帰属を移さない(bool healing,int expected)
        {
            var battle=Battle();using(var a=new AttackInfluence())using(var b=new AttackInfluence()){
                Kill(battle,0,a);Step(battle,20);var target=battle.Enemies[1];
                battle.RunLogicalFrame(()=>{target.TakeDamage(10,ElementType.None,influence:a);if(healing)target.Heal(10);target.TakeDamage(healing?10:1,ElementType.None,influence:b);});
            }
            Assert.AreEqual(0,battle.Enemies[1].CurrentHp);Assert.AreEqual(2,battle.EnemyKillCount);Assert.AreEqual(expected,battle.MaximumSimultaneousKills);
        }
        [TestCase("user")] [TestCase("custom")] [TestCase("darkening")] [TestCase("hitstop")]
        public void 停止中の実更新回数を猶予へ加算せず再開後の論理時計だけで境界を判定する(string stop)
        {
            var battle=Battle();Kill(battle,0);Step(battle,3);
            if(stop=="user"||stop=="custom")battle.Pause(stop=="user"?BattlePauseReason.User:BattlePauseReason.CustomScreen);
            else if(stop=="darkening")Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,0,60,()=>{}));
            else battle.Enemies[1].TakeDamage(1,ElementType.None,hitStopFrames:60);
            Step(battle,60);Assert.AreEqual(3,battle.LogicalFrame);
            if(stop=="user"||stop=="custom")battle.Resume(stop=="user"?BattlePauseReason.User:BattlePauseReason.CustomScreen);
            Step(battle,3);Kill(battle,1);Assert.AreEqual(2,battle.MaximumSimultaneousKills);
            Step(battle,1);Kill(battle,2);Assert.AreEqual(2,battle.MaximumSimultaneousKills);Assert.AreEqual(7,battle.LogicalFrame);
        }
        [Test]
        public void ヒットストップと暗転とカスタムを重ねても計測時計を進めない()
        {
            var battle=Battle();Kill(battle,0);Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Opponent,0,12,()=>{}));
            battle.Enemies[1].TakeDamage(1,ElementType.None,hitStopFrames:8);battle.Pause(BattlePauseReason.CustomScreen);Step(battle,90);
            Assert.AreEqual(8,battle.HitStopRemainingFrames);Assert.AreEqual(0,battle.LogicalFrame);battle.Resume(BattlePauseReason.CustomScreen);
            Step(battle,20);Assert.AreEqual(0,battle.LogicalFrame);Assert.IsFalse(battle.IsDarkening);Kill(battle,1);Assert.AreEqual(2,battle.MaximumSimultaneousKills);
        }
        [Test]
        public void 同一フレームの相討ちは敗北を優先し全敵の計測を勝敗通知前に確定する()
        {
            var battle=Battle(2);int countAtEnd=0;BattleResult? result=null;
            battle.OnBattleEnd+=r=>{result=r;countAtEnd=battle.MaximumSimultaneousKills;};
            battle.RunLogicalFrame(()=>{Kill(battle,0);Kill(battle,1);battle.Player.TakeDamage(10000);});
            Assert.AreEqual(BattleResult.Defeat,result);Assert.AreEqual(2,countAtEnd);Assert.AreEqual(2,battle.EnemyKillCount);Assert.AreEqual(0,battle.LogicalFrame);
        }
        [TestCase(false)] [TestCase(true)]
        public void 汎用攻撃と投擲の全対象撃破はHP確定後に一度ずつ数える(bool projectile)
        {
            var battle=Battle(2);var card=new Card("same-card","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Canon,20,"テスト");
            var effect=new ModifiableCardEffect(DamageTargetType.All,productBomb:projectile,hitStopFrames:20);
            battle.RunLogicalFrame(()=>effect.Execute(new BattleContext(battle,card)));
            if(projectile)Step(battle,90);
            Assert.AreEqual(2,battle.EnemyKillCount);Assert.AreEqual(2,battle.MaximumSimultaneousKills);Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(battle.IsHitStopped);
        }
        [Test]
        public void ガードと非致死ヒットと毒だけでは撃破数を増やさない()
        {
            var battle=Battle();battle.Enemies[0].Guard.Set(true,false);Kill(battle,0);battle.Enemies[1].TakeDamage(1,ElementType.None);
            battle.Enemies[2].ApplyStatus(new StatusEffect(StatusEffectType.Poison,3000,20,poisonIntervalFrames:1));Step(battle,3);
            Assert.AreEqual(0,battle.EnemyKillCount);Assert.AreEqual(0,battle.MaximumSimultaneousKills);Assert.AreEqual(1,battle.Enemies[2].CurrentHp);
        }
    }
}

namespace Olca.Tests.Battle
{
    public class PlayerStaggerMetricTests
    {
        private static BattleSystem Battle(PlayerCharacter player=null,Enemy enemy=null)
            =>new BattleSystem(player??new PlayerCharacter(1000,new Vector2Int(0,0),poise:10,staggerDurationFrames:18),
                new List<Enemy>{enemy??new Enemy("stagger-fixture","テスト",1000,ElementType.None,new Vector2Int(4,0))},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
        private static void Step(BattleSystem battle,int frames)
        {for(int i=0;i<frames;i++)battle.TickFrame(17);}
        [Test]
        public void HP被弾数ではなく蓄積を削り切った成立だけを数え同フレーム多段は一度になる()
        {
            var battle=Battle();var player=battle.Player;
            battle.RunLogicalFrame(()=>{player.TakeDamage(1,poiseDamage:0);player.TakeDamage(1,poiseDamage:4);player.TakeDamage(1,poiseDamage:4);});
            Assert.AreEqual(0,battle.PlayerStaggerCount);Assert.AreEqual(8,player.AccumulatedDamage);
            battle.RunLogicalFrame(()=>{player.TakeDamage(1,poiseDamage:5);player.TakeDamage(1,poiseDamage:50);});
            Assert.AreEqual(1,battle.PlayerStaggerCount);Assert.AreEqual(0,player.AccumulatedDamage);Assert.AreEqual(18,player.StaggerRemainingFrames);
            Step(battle,1);Assert.AreEqual(17,player.StaggerRemainingFrames);
            battle.RunLogicalFrame(()=>player.TakeDamage(1,poiseDamage:10));Assert.AreEqual(2,battle.PlayerStaggerCount);Assert.AreEqual(18,player.StaggerRemainingFrames);
        }
        [Test]
        public void 麻痺の付与フレームは中断せず次の有効フレームに強靭度をリセットして一度数える()
        {
            var battle=Battle();var player=battle.Player;player.TakeDamage(0,poiseDamage:5);
            battle.RunLogicalFrame(()=>player.ApplyParalysis(60));
            Assert.AreEqual(0,battle.PlayerStaggerCount);Assert.AreEqual(5,player.AccumulatedDamage);Assert.IsFalse(player.IsStaggered);
            Step(battle,1);Assert.AreEqual(1,battle.PlayerStaggerCount);Assert.AreEqual(0,player.AccumulatedDamage);Assert.IsTrue(player.IsParalyzed);Assert.IsTrue(player.IsStaggered);
            Step(battle,3);Assert.AreEqual(1,battle.PlayerStaggerCount);
            player.ApplyParalysis(60);Step(battle,1);Assert.AreEqual(2,battle.PlayerStaggerCount);Assert.AreEqual(18,player.StaggerRemainingFrames);
        }
        [TestCase(false)] [TestCase(true)]
        public void 同じフレームの付与と解除は解除優先で強靭度を削らず計上もしない(bool clearFirst)
        {
            var battle=Battle();var p=battle.Player;p.TakeDamage(0,poiseDamage:5);
            battle.RunLogicalFrame(()=>{if(clearFirst)p.ClearParalysis();p.ApplyParalysis(30);if(!clearFirst)p.ClearParalysis();});
            Step(battle,2);Assert.AreEqual(0,battle.PlayerStaggerCount);Assert.AreEqual(5,p.AccumulatedDamage);Assert.IsFalse(p.IsParalyzed);Assert.IsFalse(p.IsStaggered);
        }
        [Test]
        public void 有効化する麻痺と通常の強靭度削りは同じ終端判定で一回に集約する()
        {
            var battle=Battle();int notifications=0;battle.Player.OnStaggered+=()=>notifications++;
            battle.Player.ApplyParalysis(30);
            battle.RunLogicalFrame(()=>{battle.TickFrame(17);battle.Player.TakeDamage(0,poiseDamage:20);battle.Player.TakeDamage(0,poiseDamage:20);});
            Assert.AreEqual(1,notifications);Assert.AreEqual(1,battle.PlayerStaggerCount);Assert.AreEqual(0,battle.Player.AccumulatedDamage);Assert.IsTrue(battle.Player.IsParalyzed);
        }
        [TestCase("user")] [TestCase("custom")] [TestCase("darkening")] [TestCase("hitstop")]
        public void 麻痺による成立は停止中に消費せず有効更新の再開時だけ数える(string stop)
        {
            var battle=Battle();battle.Player.ApplyParalysis(30);
            if(stop=="user"||stop=="custom")battle.Pause(stop=="user"?BattlePauseReason.User:BattlePauseReason.CustomScreen);
            else if(stop=="darkening")Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,0,10,()=>{}));
            else battle.Enemies[0].TakeDamage(1,ElementType.None,hitStopFrames:10);
            Step(battle,10);Assert.AreEqual(0,battle.PlayerStaggerCount);Assert.AreEqual(0,battle.LogicalFrame);
            if(stop=="user"||stop=="custom")battle.Resume(stop=="user"?BattlePauseReason.User:BattlePauseReason.CustomScreen);
            Step(battle,1);Assert.AreEqual(1,battle.PlayerStaggerCount);Assert.AreEqual(1,battle.LogicalFrame);
        }
        [Test]
        public void 勝敗通知より前に計測を確定し次戦へ購読や累計を残さない()
        {
            var player=new PlayerCharacter(1000,poise:10,staggerDurationFrames:18);var battle=Battle(player);int atEnd=-1;
            battle.OnBattleEnd+=_=>atEnd=battle.PlayerStaggerCount;
            battle.RunLogicalFrame(()=>{player.TakeDamage(1,poiseDamage:20);battle.Enemies[0].TakeDamage(1000,ElementType.None);});
            Assert.AreEqual(1,atEnd);Assert.AreEqual(1,battle.PlayerStaggerCount);Assert.IsTrue(battle.IsGameOver);
            var next=Battle(player);Assert.AreEqual(0,next.PlayerStaggerCount);next.RunLogicalFrame(()=>player.TakeDamage(1,poiseDamage:20));
            Assert.AreEqual(1,next.PlayerStaggerCount);Assert.AreEqual(1,battle.PlayerStaggerCount);
        }
        [Test]
        public void 同一フレームの死亡はのけぞりとして計上しない()
        {
            var battle=Battle();battle.RunLogicalFrame(()=>{battle.Player.ApplyParalysis(30);battle.Player.TakeDamage(1000,poiseDamage:20);});
            Assert.IsTrue(battle.IsGameOver);Assert.AreEqual(0,battle.PlayerStaggerCount);
        }
        [TestCase(false)] [TestCase(true)]
        public void 敵も麻痺有効化時にだけ強靭度を削り切り同フレームの通常成立を重ねない(bool normalHit)
        {
            var enemy=new Enemy("stagger","テスト",1000,ElementType.None,new Vector2Int(4,0),poiseDefinition:new EnemyPoiseDefinition(10,18));
            var battle=Battle(enemy:enemy);int count=0;enemy.OnStaggered+=()=>count++;enemy.TakeDamage(0,ElementType.None,poiseDamage:5);
            battle.RunLogicalFrame(()=>enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000)));
            Assert.AreEqual(0,count);Assert.AreEqual(5,enemy.AccumulatedPoiseDamage);
            battle.RunLogicalFrame(()=>{battle.TickFrame(17);if(normalHit)enemy.TakeDamage(0,ElementType.None,poiseDamage:20);});
            Assert.AreEqual(1,count);Assert.AreEqual(0,enemy.AccumulatedPoiseDamage);Assert.IsTrue(enemy.IsParalyzed);Assert.AreEqual(18,enemy.StaggerRemainingFrames);
            Step(battle,2);Assert.AreEqual(1,count);
        }
        [TestCase(false)] [TestCase(true)]
        public void 敵の同フレーム解除と強靭度未定義の相手に仮の強靭度を作らない(bool configured)
        {
            var enemy=new Enemy("stagger","テスト",1000,ElementType.None,new Vector2Int(4,0),poiseDefinition:configured?new EnemyPoiseDefinition(10,18):null);
            var battle=Battle(enemy:enemy);int count=0;enemy.OnStaggered+=()=>count++;
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));if(configured)enemy.ClearStatus(StatusEffectType.Paralyze);});
            Step(battle,2);Assert.AreEqual(0,count);Assert.AreEqual(configured?(int?)10:null,enemy.Poise);Assert.IsFalse(enemy.IsStaggered);
        }
    }
}

namespace Olca.Tests.Battle
{
    public class PlayerGuardTests
    {
        private static BattleSystem Battle(PlayerCharacter p=null)
            =>new BattleSystem(p??new PlayerCharacter(100,new Vector2Int(3,0),poise:30,staggerDurationFrames:18),
                new List<Enemy>{new Enemy("guard-source","テスト",1000,ElementType.None,new Vector2Int(4,0))},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
        [TestCase(false,AttackProperty.None)] [TestCase(true,AttackProperty.None)]
        [TestCase(false,AttackProperty.Sword)] [TestCase(true,AttackProperty.Sword)]
        [TestCase(false,AttackProperty.Wind)] [TestCase(true,AttackProperty.Wind)]
        public void 前後の敵AI攻撃をガードしHP削り付随効果無敵停止とバリア消費を防ぐ(bool behind,AttackProperty properties)
        {
            var b=Battle();var p=b.Player;var e=b.Enemies[0];e.MoveTo(new Vector2Int(behind?2:4,0));p.Guard.Set(true,true);p.SetBarrier(40);int effects=0;
            var feedback=new List<CombatFeedback>();p.OnCombatFeedback+=feedback.Add;
            var attack=new BasicAttackAction(50,100,attackProperties:properties,poiseDamage:100,onHit:_=>effects++,hitInvincibilityMs:600,hitStopFrames:6);
            var context=new AIContext(e,p,b.Field,new MockRandomProvider(),new MockLogger());Assert.IsTrue(attack.CanExecute(context));
            b.RunLogicalFrame(()=>{attack.Execute(context);attack.Execute(context);});
            Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(0,p.AccumulatedDamage);Assert.AreEqual(0,effects);Assert.AreEqual(40,p.BarrierHp);
            Assert.IsFalse(p.IsInvincible);Assert.IsFalse(p.IsStaggered);Assert.IsFalse(b.IsHitStopped);Assert.IsTrue(p.Guard.IsActive);Assert.AreEqual(2,feedback.Count);Assert.IsTrue(feedback.All(v=>v.Kind==CombatFeedbackKind.Guard));
        }
        [TestCase(false)] [TestCase(true)]
        public void ブレイクはガードだけを通し防御消滅フレームも全段を防いで次フレームから受理する(bool aura)
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,false);if(aura)p.SetAura(10);else p.SetBarrier(10);int effects=0;
            var e=b.Enemies[0];var context=new AIContext(e,p,b.Field,new MockRandomProvider(),new MockLogger());
            var attack=new BasicAttackAction(5,100,attackProperties:AttackProperty.Break,poiseDamage:20,onHit:_=>effects++,hitInvincibilityMs:600,hitStopFrames:6);
            b.RunLogicalFrame(()=>{attack.Execute(context);attack.Execute(context);attack.Execute(context);});
            Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(0,effects);Assert.IsFalse(p.HasAura||p.HasBarrier);Assert.IsFalse(p.IsInvincible);Assert.AreEqual(0,p.AccumulatedDamage);Assert.IsTrue(p.Guard.IsActive);Assert.AreEqual(6,b.HitStopRemainingFrames);
            b.RunLogicalFrame(()=>attack.Execute(context));Assert.AreEqual(95,p.CurrentHp);Assert.AreEqual(1,effects);Assert.IsTrue(p.IsInvincible);Assert.IsTrue(p.Guard.IsActive);
        }
        [TestCase(false)] [TestCase(true)]
        public void 風性質はガード通過後だけ防御を剥がし通常風では消耗しない(bool aura)
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,false);if(aura)p.SetAura(100);else p.SetBarrier(100);
            b.RunLogicalFrame(()=>p.TakeDamage(1,attackProperties:AttackProperty.Wind));Assert.IsTrue(p.HasAura||p.HasBarrier);
            b.RunLogicalFrame(()=>p.TakeDamage(1,attackProperties:AttackProperty.Wind|AttackProperty.Break));
            Assert.IsFalse(p.HasAura||p.HasBarrier);Assert.AreEqual(100,p.CurrentHp);Assert.IsTrue(p.Guard.IsActive);
        }
        [TestCase(false)] [TestCase(true)]
        public void ブレイクは解除せず中断指定があるガードだけ強靭度合算の終端で解除する(bool interrupt)
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,interrupt);
            b.RunLogicalFrame(()=>{
                p.TakeDamage(1,attackProperties:AttackProperty.Break,poiseDamage:15);
                p.TakeDamage(100,poiseDamage:100);
                p.TakeDamage(1,attackProperties:AttackProperty.Break,poiseDamage:15);
                Assert.IsTrue(p.Guard.IsActive,"中断の終端確定前は全ヒットにガードを適用");
            });
            Assert.AreEqual(98,p.CurrentHp);Assert.AreEqual(1,b.PlayerStaggerCount);Assert.IsTrue(p.IsStaggered);Assert.AreEqual(!interrupt,p.Guard.IsActive);
            b.RunLogicalFrame(()=>p.TakeDamage(1,poiseDamage:0));Assert.AreEqual(interrupt?97:98,p.CurrentHp);
        }
        [Test]
        public void 毒と元威力ゼロの状態専用攻撃は通し麻痺の強制中断時に行動ガードを解除する()
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,true);p.SetBarrier(10);p.TakePoisonDamage(2);Assert.AreEqual(98,p.CurrentHp);Assert.AreEqual(10,p.BarrierHp);
            int effects=0;var attack=new BasicAttackAction(0,100,onHit:target=>{effects++;target.ApplyParalysis(30);},hitInvincibilityMs:600);
            b.RunLogicalFrame(()=>attack.Execute(new AIContext(b.Enemies[0],p,b.Field,new MockRandomProvider(),new MockLogger())));
            Assert.AreEqual(1,effects);Assert.IsTrue(p.Guard.IsActive);Assert.IsFalse(p.IsInvincible);Assert.AreEqual(0,b.PlayerStaggerCount);
            b.TickFrame(17);Assert.IsFalse(p.Guard.IsActive);Assert.IsTrue(p.IsParalyzed);Assert.AreEqual(1,b.PlayerStaggerCount);
        }
        [Test]
        public void 既存無敵と属性無効をブレイクで貫通せず非受理の弱点ヒットは既存変身を解除しない()
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,false);p.SetInvincible(600);
            Assert.IsFalse(p.TryTakeDamage(10,attackProperties:AttackProperty.Break,poiseDamage:100));Assert.AreEqual(100,p.CurrentHp);Assert.IsTrue(p.Guard.IsActive);
            var target=new PlayerCharacter(100);target.Guard.Set(true,false);target.BeginTransformationDefense(new ElementResistances(fire:50),ElementType.Fire);
            Assert.IsFalse(target.TryTakeDamage(20,attackElement:ElementType.Fire));Assert.IsTrue(target.HasTransformationDefense);Assert.AreEqual(100,target.CurrentHp);
            Assert.IsTrue(target.TryTakeDamage(20,attackElement:ElementType.Fire,attackProperties:AttackProperty.Break));Assert.AreEqual(90,target.CurrentHp);Assert.IsFalse(target.HasTransformationDefense);Assert.IsTrue(target.Guard.IsActive);
            target.SetElementResistances(new ElementResistances(fire:100));Assert.IsFalse(target.TryTakeDamage(20,attackElement:ElementType.Fire,attackProperties:AttackProperty.Break));Assert.AreEqual(90,target.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void 勝敗通知と死亡通知より前にガードを解除し次戦やチェックポイント離脱へ残さない(bool victory)
        {
            var b=Battle();var p=b.Player;p.Guard.Set(true,false);bool? atEnd=null,atDeath=null;p.OnDeath+=()=>atDeath=p.Guard.IsActive;b.OnBattleEnd+=_=>atEnd=p.Guard.IsActive;
            b.RunLogicalFrame(()=>{if(victory)b.Enemies[0].TakeDamage(10000,ElementType.None);else p.TakeDamage(10000,attackProperties:AttackProperty.Break);});
            Assert.AreEqual(false,atEnd);if(!victory)Assert.AreEqual(false,atDeath);Assert.IsFalse(p.Guard.IsActive);
            var next=Battle();next.Player.Guard.Set(true,true);typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(next,null);Assert.IsFalse(next.Player.Guard.IsActive);
            var standalone=new PlayerCharacter(10);standalone.Guard.Set(true,false);standalone.TakeDamage(10,attackProperties:AttackProperty.Break);Assert.IsFalse(standalone.Guard.IsActive);
        }
        [Test]
        public void 保存未対応のガードを黙って欠落させず拒否し解除後の既存保存を許可する()
        {
            var config=DungeonConfig.CreateTestDungeon(10);var deck=new Deck(new List<Card>());var session=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(config,deck);
            var m=new Olca.Core.Logic.Integration.DungeonBattleManager(config,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
            var stored=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Store.Read());var capture=session.GetType().GetMethod("Capture",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);m.Dungeon.Player.Guard.Set(true,false);
            var ex=Assert.Throws<System.Reflection.TargetInvocationException>(()=>capture.Invoke(session,new object[]{m.Dungeon,CheckpointBoundary.RewardCompleted,null}));Assert.IsInstanceOf<System.ArgumentException>(ex.InnerException);
            CollectionAssert.AreEqual(stored,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Store.Read()));m.Dungeon.Player.Guard.Clear();Assert.IsNotNull(capture.Invoke(session,new object[]{m.Dungeon,CheckpointBoundary.RewardCompleted,null}));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void ガードの攻撃受理と停止再開の結果は描画頻度によらず一致する(int fps)
        {
            string Replay(int rendering){
                var b=Battle();var p=b.Player;p.Guard.Set(true,true);var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var trace=new List<string>();long previous=0;
                void Step(long frame,int ms){
                    if(frame==1)b.Pause(BattlePauseReason.User);if(frame==10)b.Resume(BattlePauseReason.User);
                    if(frame==11)b.Pause(BattlePauseReason.CustomScreen);if(frame==20)b.Resume(BattlePauseReason.CustomScreen);
                    if(frame==21)Assert.IsTrue(b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,4,()=>{}));
                    if(frame==27)b.RunLogicalFrame(()=>b.Enemies[0].TakeDamage(1,ElementType.None,hitStopFrames:4));
                    if(frame==30||frame==31)b.RunLogicalFrame(()=>p.TakeDamage(1,attackProperties:AttackProperty.Break,poiseDamage:15));
                    if(frame==32)b.RunLogicalFrame(()=>p.TakeDamage(1,poiseDamage:0));
                    b.TickFrame(ms);trace.Add($"{b.LogicalFrame}:{p.CurrentHp}:{p.Guard.IsActive}:{b.PlayerStaggerCount}:{b.IsDarkening}:{b.HitStopRemainingFrames}");
                }
                for(int render=1;render<=rendering;render++){long now=render*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/rendering;clock.Accumulate(now-previous);previous=now;clock.Drain(Step,3);}while(clock.PendingFrames>0)clock.Drain(Step,3);
                Assert.AreEqual(97,p.CurrentHp);Assert.IsFalse(p.Guard.IsActive);return string.Join("|",trace);
            }
            Assert.AreEqual(Replay(60),Replay(fps));
        }
        [Test]
        public void 自分詳細は実際のガード所有状態だけを表示する()
        {
            var p=new PlayerCharacter(100);Assert.IsFalse(Olca.Presentation.CustomScreen.CustomDisplay.CombatantDetails(p).Contains("ガード"));p.Guard.Set(true,false);
            StringAssert.Contains("ガード",Olca.Presentation.CustomScreen.CustomDisplay.CombatantDetails(p));p.Guard.Clear();Assert.IsFalse(Olca.Presentation.CustomScreen.CustomDisplay.CombatantDetails(p).Contains("ガード"));
        }
    }
}

namespace Olca.Tests.Battle
{
    public class PlayerStatusResistanceTests
    {
        private static StatusEffect Poison(int frames,int damage=3,int durationResistancePercent=0) => new StatusEffect(StatusEffectType.Poison,frames*1000/60,damage,durationResistancePercent);
        private static BattleSystem Battle(PlayerCharacter p) => new BattleSystem(p,new List<Enemy>{new Enemy("e","テスト",1000,ElementType.None,new Vector2Int(4,0))},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
        [TestCase(0,101)] [TestCase(1,99)] [TestCase(50,50)] [TestCase(99,1)] [TestCase(100,0)] [TestCase(150,0)]
        public void 全状態は付与時の合算耐性だけで時間短縮し端数を切り捨てる(int resistance,int expected)
        {
            var p=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(resistance,resistance,resistance,resistance));var b=Battle(p);
            var poison=Poison(101,damage:3);
            p.ApplyPoison("poison",poison,10);p.ApplyParalysis(101);p.ApplyConfusion(101);p.ApplyBlindness(101);
            Assert.IsFalse(p.IsParalyzed||p.IsConfused||p.IsBlind);b.RunLogicalFrame(()=>{});
            Assert.AreEqual(expected,p.ParalysisRemainingFrames);Assert.AreEqual(expected,p.ConfusionRemainingFrames);Assert.AreEqual(expected,p.BlindRemainingFrames);
            var v=p.InspectStatuses().FirstOrDefault(x=>x.Type==StatusEffectType.Poison);Assert.AreEqual(expected,v?.RemainingFrames??0);if(v!=null){Assert.AreEqual(3,v.Damage);Assert.AreEqual(10,v.IntervalFrames);}
            Assert.AreEqual(101,poison.RemainingFrames,"付与元を変更しない");
        }
        [Test]
        public void 基礎と戦闘と既存付与元の耐性を加算し付与後の変更は遡及しない()
        {
            var p=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(20,20,20,20));var b=Battle(p);p.SetBattleStatusResistances(new TimedStatusResistances(30,30,30,30));
            p.ApplyPoison("poison",Poison(100,3,durationResistancePercent:10),10);p.ApplyParalysis(100);p.ApplyConfusion(100,10);p.ApplyBlindness(100,10);
            p.SetBattleStatusResistances(new TimedStatusResistances(100,100,100,100));b.RunLogicalFrame(()=>{});
            Assert.AreEqual(50,p.ParalysisRemainingFrames);Assert.AreEqual(40,p.ConfusionRemainingFrames);Assert.AreEqual(40,p.BlindRemainingFrames);Assert.AreEqual(40,p.InspectStatuses().First(v=>v.Type==StatusEffectType.Poison).RemainingFrames);
            p.ApplyPoison("poison",Poison(200,3),10);p.ApplyParalysis(200);p.ApplyConfusion(200);p.ApplyBlindness(200);b.RunLogicalFrame(()=>{});
            Assert.AreEqual(50,p.ParalysisRemainingFrames);Assert.AreEqual(40,p.ConfusionRemainingFrames);Assert.AreEqual(40,p.BlindRemainingFrames);
            Assert.AreEqual(120,p.ActiveStatusResistances.Poison);Assert.AreEqual(20,p.StatusResistances.Poison);
        }
        [Test]
        public void 同種再付与と同フレーム解除は既存予約境界を維持し毒上位種も共通耐性を使う()
        {
            var p=new PlayerCharacter(100);var b=Battle(p);p.SetBattleStatusResistances(new TimedStatusResistances(50,50,50,50));
            p.ApplyPoison("poison",Poison(100,3),10);p.ApplyPoison("venom",Poison(100,4),12);p.ApplyParalysis(100);p.ApplyConfusion(100);p.ApplyBlindness(100);b.RunLogicalFrame(()=>{});
            b.RunLogicalFrame(()=>{p.ApplyPoison("poison",Poison(60,3),10);p.ApplyParalysis(60);p.ApplyConfusion(60);p.ApplyBlindness(60);});Assert.AreEqual(30,p.ParalysisRemainingFrames);Assert.AreEqual(30,p.ConfusionRemainingFrames);Assert.AreEqual(30,p.BlindRemainingFrames);
            b.RunLogicalFrame(()=>{p.ApplyParalysis(100);p.ClearParalysis();p.ApplyConfusion(100);p.ClearConfusion();p.ApplyBlindness(100);p.ClearBlindness();p.ApplyPoison("poison",Poison(100,3),10);p.ClearPoisons();});
            Assert.IsEmpty(p.InspectStatuses());
        }
        [TestCase(false)] [TestCase(true)]
        public void 戦闘耐性は勝敗通知前と離脱で解除し基礎値と付与済み毒を再短縮しない(bool win)
        {
            var p=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(poison:25));var b=Battle(p);p.SetBattleStatusResistances(new TimedStatusResistances(25,100,100,100));p.ApplyPoison("poison",Poison(100,3),10);b.RunLogicalFrame(()=>{});
            bool? cleared=null;b.OnBattleEnd+=_=>cleared=p.BattleStatusResistances.IsNeutral;b.RunLogicalFrame(()=>{if(win)b.Enemies[0].TakeDamage(10000,ElementType.None);else p.TakeDamage(10000);});Assert.AreEqual(true,cleared);Assert.AreEqual(25,p.ActiveStatusResistances.Poison);
            if(win)Assert.AreEqual(50,p.InspectStatuses().First(v=>v.Type==StatusEffectType.Poison).RemainingFrames);
            var next=Battle(p=new PlayerCharacter(100));p.SetBattleStatusResistances(new TimedStatusResistances(poison:50));typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(next,null);Assert.IsTrue(p.ActiveStatusResistances.IsNeutral);
        }
        [Test]
        public void 敵と同じ元効果軽減加算で毒時間を計算し復元済み毒へ再適用しない()
        {
            var r=new TimedStatusResistances(poison:35);var p=new PlayerCharacter(100,statusResistances:r);var b=Battle(p);var enemy=new Enemy("x","テスト",100,ElementType.None,new Vector2Int(4,0),statusResistances:r);var effect=new StatusEffect(StatusEffectType.Poison,2000,3,15,poisonIntervalFrames:10);
            p.ApplyPoison("poison",effect,10);enemy.ApplyStatus(effect);b.RunLogicalFrame(()=>{});Assert.AreEqual(enemy.InspectStatuses().First().RemainingFrames,p.InspectStatuses().First().RemainingFrames);
            var dto=p.CapturePoisons();var restored=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(poison:100));restored.RestorePoisons(dto);Assert.AreEqual(p.InspectStatuses().First().RemainingFrames,restored.InspectStatuses().First().RemainingFrames);
        }
        [Test]
        public void 未保存の耐性を黙って欠落させずCaptureを拒否し戦闘解除後は既存保存できる()
        {
            var config=DungeonConfig.CreateTestDungeon(10);var deck=new Deck(new List<Card>());var session=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(config,deck);var m=new Olca.Core.Logic.Integration.DungeonBattleManager(config,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);var p=m.Dungeon.Player;
            var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Store.Read());var capture=session.GetType().GetMethod("Capture",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);p.SetBattleStatusResistances(new TimedStatusResistances(poison:50));
            var ex=Assert.Throws<System.Reflection.TargetInvocationException>(()=>capture.Invoke(session,new object[]{m.Dungeon,CheckpointBoundary.RewardCompleted,null}));Assert.IsInstanceOf<System.ArgumentException>(ex.InnerException);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(session.Store.Read()));p.SetBattleStatusResistances(TimedStatusResistances.Neutral);Assert.IsNotNull(capture.Invoke(session,new object[]{m.Dungeon,CheckpointBoundary.RewardCompleted,null}));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void 短縮済み状態時計は停止暗転ヒットストップと描画頻度によらず一致する(int fps)
        {
            string Replay(int rendering){var p=new PlayerCharacter(100);p.SetBattleStatusResistances(new TimedStatusResistances(50,50,50,50));var b=Battle(p);p.ApplyPoison("poison",Poison(120,2),10);p.ApplyConfusion(120);p.ApplyBlindness(120);var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long previous=0;var trace=new List<string>();
                void Step(long frame,int ms){if(frame==1)b.Pause(BattlePauseReason.CustomScreen);if(frame==10)b.Resume(BattlePauseReason.CustomScreen);if(frame==11)b.Pause(BattlePauseReason.User);if(frame==20)b.Resume(BattlePauseReason.User);if(frame==21)Assert.IsTrue(b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,4,()=>{}));if(frame==30)b.RunLogicalFrame(()=>b.Enemies[0].TakeDamage(1,ElementType.None,hitStopFrames:6));b.TickFrame(ms);trace.Add($"{b.LogicalFrame}:{p.CurrentHp}:{p.ConfusionRemainingFrames}:{p.BlindRemainingFrames}:{b.HitStopRemainingFrames}");}
                for(int i=1;i<=rendering;i++){long now=i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/rendering;clock.Accumulate(now-previous);previous=now;clock.Drain(Step,3);}while(clock.PendingFrames>0)clock.Drain(Step,3);Assert.Less(p.CurrentHp,100);return string.Join("|",trace);}
            Assert.AreEqual(Replay(60),Replay(fps));
        }
        [Test]
        public void 自分詳細は合算した実値だけをカナで表示し外部入力不正値を拒否する()
        {
            var p=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(10,20,30,40));p.SetBattleStatusResistances(new TimedStatusResistances(5,10,15,20));var text=Olca.Presentation.CustomScreen.CustomDisplay.CombatantDetails(p);StringAssert.Contains("ドク 15 / マヒ 30 / コンラン 45 / モウモク 60",text);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text,"[一-鿿]"));
            Assert.Throws<System.ArgumentNullException>(()=>p.SetBattleStatusResistances(null));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TimedStatusResistances(-1));
            p.SetBattleStatusResistances(new TimedStatusResistances(int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue));p.ApplyParalysis(int.MaxValue);p.ApplyConfusion(int.MaxValue,int.MaxValue);p.ApplyBlindness(int.MaxValue,int.MaxValue);Assert.IsFalse(p.IsParalyzed||p.IsConfused||p.IsBlind);
        }
    }
}
