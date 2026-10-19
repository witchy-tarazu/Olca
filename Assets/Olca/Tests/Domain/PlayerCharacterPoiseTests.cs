using NUnit.Framework;
using Olca.Core.Domain;

namespace Olca.Tests.Domain
{
    /// <summary>
    /// PlayerCharacter の強靭度システムのテスト
    /// </summary>
    [TestFixture]
    public class PlayerCharacterPoiseTests
    {
        [TestCase(50, 5)][TestCase(-100, 20)][TestCase(0, 10)]
        public void 明示した強靭度削り量は属性によるHP軽減増幅と独立する(int resistance, int hpDamage)
        {
            var player = new PlayerCharacter(100, poise: 30);
            player.SetElementResistances(new ElementResistances(fire: resistance));
            player.TakeDamage(10, attackElement: ElementType.Fire, poiseDamage: 7);
            Assert.AreEqual(100 - hpDamage, player.CurrentHp); Assert.AreEqual(7, player.AccumulatedDamage);
            player.TakeDamage(1, poiseDamage: 0); Assert.AreEqual(7, player.AccumulatedDamage);
        }

        [TestCase("immune")][TestCase("invincible")][TestCase("barrier")][TestCase("disabled")]
        public void 無効化された攻撃は明示削り量を通さない(string prevention)
        {
            var player = new PlayerCharacter(100, poise: 30); int staggered = 0; player.OnStaggered += () => staggered++;
            if (prevention == "immune") player.SetElementResistances(new ElementResistances(fire: 100));
            if (prevention == "invincible") player.SetInvincible(1000);
            if (prevention == "barrier") player.SetBarrier(20);
            player.TakeDamage(10, affectsPoise: prevention != "disabled", attackElement: ElementType.Fire, poiseDamage: 30);
            Assert.AreEqual(0, player.AccumulatedDamage); Assert.AreEqual(0, staggered);
            Assert.AreEqual(prevention == "disabled" ? 90 : 100, player.CurrentHp);
        }

        [Test]
        public void 明示削り0は強靭度0でものけぞらず負値は副作用前に拒否する()
        {
            var player = new PlayerCharacter(100, poise: 0); int staggered = 0; player.OnStaggered += () => staggered++;
            player.TakeDamage(10, poiseDamage: 0); Assert.AreEqual(0, staggered); Assert.AreEqual(90, player.CurrentHp);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => player.TakeDamage(10, poiseDamage: -1));
            Assert.AreEqual(90, player.CurrentHp); Assert.AreEqual(0, player.AccumulatedDamage);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Olca.Core.Logic.Battle.BasicAttackAction(10, 100, poiseDamage: -1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Olca.Core.Logic.Battle.BasicAttackAction(poiseDamage: -1));
        }

        [TestCase(false)][TestCase(true)]
        public void 敵AIの整数版と旧float版は明示削り量を同じように渡す(bool legacy)
        {
            var player = new PlayerCharacter(100, poise: 30);
            var enemy = new Enemy("fixture", "検証", 10, ElementType.None, new Vector2Int(1, 0));
            var action = legacy ? new Olca.Core.Logic.Battle.BasicAttackAction(10, .1f, poiseDamage: 7)
                : new Olca.Core.Logic.Battle.BasicAttackAction(10, 100, poiseDamage: 7);
            action.Execute(new Olca.Core.Logic.Battle.AIContext(enemy, player, new Olca.Core.Logic.Battle.BattleField(), new Olca.Tests.Mocks.MockRandomProvider(), new Olca.Tests.Mocks.MockLogger()));
            Assert.AreEqual(90, player.CurrentHp); Assert.AreEqual(7, player.AccumulatedDamage);
        }

        [Test]
        public void プレイヤー期間未指定は旧通知だけを維持し負の期間は拒否する()
        {
            var old = new PlayerCharacter(100); int notices = 0; old.OnStaggered += () => notices++;
            old.TakeDamage(1, poiseDamage: 30); Assert.AreEqual(1, notices);
            Assert.IsFalse(old.IsStaggered); Assert.AreEqual(0, old.StaggerRemainingFrames);
            var configured = new PlayerCharacter(100, staggerDurationFrames: 6);
            configured.TakeDamage(1, poiseDamage: 30); Assert.IsTrue(configured.IsStaggered); Assert.AreEqual(6, configured.StaggerRemainingFrames);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerCharacter(100, staggerDurationFrames: -1));
        }

        // ========== 基本動作 ==========

        [Test]
        public void 初期状態_累計ダメージは0()
        {
            var player = new PlayerCharacter(100, poise: 30);

            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [Test]
        public void 初期状態_強靭度はデフォルト30()
        {
            var player = new PlayerCharacter(100);

            Assert.AreEqual(30, player.Poise);
        }

        // ========== のけぞり発生条件 ==========

        [Test]
        public void TakeDamage_累計ダメージが強靭度未満ではのけぞらない()
        {
            var player = new PlayerCharacter(100, poise: 30);
            bool staggered = false;
            player.OnStaggered += () => staggered = true;

            player.TakeDamage(20);  // 20 < 30

            Assert.IsFalse(staggered, "累計ダメージが強靭度未満ではのけぞらない");
            Assert.AreEqual(20, player.AccumulatedDamage);
        }

        [Test]
        public void TakeDamage_累計ダメージが強靭度に達するとのけぞりが発生する()
        {
            var player = new PlayerCharacter(100, poise: 30);
            bool staggered = false;
            player.OnStaggered += () => staggered = true;

            player.TakeDamage(30);  // 30 >= 30

            Assert.IsTrue(staggered, "累計ダメージが強靭度以上でのけぞり発生");
        }

        [Test]
        public void TakeDamage_複数回ダメージで累計が強靭度を超えるとのけぞる()
        {
            var player = new PlayerCharacter(100, poise: 30);
            bool staggered = false;
            player.OnStaggered += () => staggered = true;

            player.TakeDamage(15);  // 累計15 < 30
            Assert.IsFalse(staggered, "まだのけぞらない");

            player.TakeDamage(15);  // 累計30 >= 30

            Assert.IsTrue(staggered, "累計が30に達してのけぞり");
        }

        [Test]
        public void TakeDamage_のけぞり後は累計ダメージがリセットされる()
        {
            var player = new PlayerCharacter(100, poise: 30);

            player.TakeDamage(30);  // のけぞり発生

            Assert.AreEqual(0, player.AccumulatedDamage, "のけぞり後は累計ダメージがリセット");
        }

        [Test]
        public void TakeDamage_のけぞり後も追加ダメージで再度のけぞる()
        {
            var player = new PlayerCharacter(100, poise: 30);
            int staggerCount = 0;
            player.OnStaggered += () => staggerCount++;

            player.TakeDamage(30);  // 1回目のけぞり
            player.TakeDamage(30);  // 2回目のけぞり

            Assert.AreEqual(2, staggerCount, "リセット後も再度のけぞる");
        }

        [Test]
        public void TakeDamage_forceStagger指定時は即座にのけぞりが発生する()
        {
            var player = new PlayerCharacter(100, poise: 30);
            bool staggered = false;
            player.OnStaggered += () => staggered = true;

            player.TakeDamage(1, forceStagger: true);  // 1 < 30 だが強制のけぞり

            Assert.IsTrue(staggered, "forceStaggerで即のけぞり");
        }

        [Test]
        public void TakeDamage_死亡時はのけぞりイベントが発生しない()
        {
            var player = new PlayerCharacter(10, poise: 30);
            bool staggered = false;
            player.OnStaggered += () => staggered = true;

            player.TakeDamage(100);  // 即死ダメージ

            Assert.IsFalse(staggered, "死亡時はのけぞらない");
        }

        // ========== ResetPoiseAccumulation ==========

        [Test]
        public void ResetPoiseAccumulation_累計ダメージが0になる()
        {
            var player = new PlayerCharacter(100, poise: 30);
            player.TakeDamage(20);
            Assert.AreEqual(20, player.AccumulatedDamage, "前提: 累計20");

            player.ResetPoiseAccumulation();

            Assert.AreEqual(0, player.AccumulatedDamage, "リセット後は0");
        }

        [TestCase(150, 30)]
        [TestCase(50, 30)]
        [TestCase(30, 29)]
        [TestCase(20, 19)]
        [TestCase(1, 0)]
        public void SetPoise_削れた量を維持し最大値低下で残り1未満なら補正する(int maximum, int expectedDamage)
        {
            var player = new PlayerCharacter(1000, poise: 100);
            player.TakeDamage(30);
            int staggerCount = 0;
            player.OnStaggered += () => staggerCount++;

            player.SetPoise(maximum);

            Assert.AreEqual(maximum, player.Poise);
            Assert.AreEqual(expectedDamage, player.AccumulatedDamage);
            Assert.AreEqual(970, player.CurrentHp);
            Assert.AreEqual(0, staggerCount, "最大値変更だけではのけぞらない");
            player.TakeDamage(maximum - expectedDamage);
            Assert.AreEqual(1, staggerCount, "補正後の残りを削った攻撃でのけぞる");
            Assert.AreEqual(0, player.AccumulatedDamage);
        }

        [Test]
        public void SetPoise_低下後に増加しても補正済みの削れた量を維持する()
        {
            var player = new PlayerCharacter(1000, poise: 100);
            player.TakeDamage(30);
            player.SetPoise(20);
            player.SetPoise(150);
            Assert.AreEqual(19, player.AccumulatedDamage);
            Assert.AreEqual(131, player.Poise - player.AccumulatedDamage);
        }

        // ========== SetPoise ==========

        [Test]
        public void SetPoise_強靭度が更新される()
        {
            var player = new PlayerCharacter(100, poise: 30);

            player.SetPoise(50);

            Assert.AreEqual(50, player.Poise);
        }

        [Test]
        public void SetPoise_負の値は0になる()
        {
            var player = new PlayerCharacter(100, poise: 30);

            player.SetPoise(-10);

            Assert.AreEqual(0, player.Poise);
        }
    }
}
