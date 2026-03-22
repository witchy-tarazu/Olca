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
