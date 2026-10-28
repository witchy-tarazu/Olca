using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
namespace Olca.Unity
{
    /// <summary>個別の確定フィードバックだけを一度ずつ購読する。表示や時計を所有しない。</summary>
    public sealed class CombatFeedbackSubscription : IDisposable
    {
        private readonly Action<CombatFeedback> receive;
        private readonly HashSet<Enemy> enemies = new HashSet<Enemy>();
        private BattleSystem battle;
        private bool disposed;
        public CombatFeedbackSubscription(Action<CombatFeedback> receive) { this.receive = receive ?? throw new ArgumentNullException(nameof(receive)); }
        public void Bind(BattleSystem value)
        {
            if (disposed || ReferenceEquals(battle, value)) return;
            if (battle != null) { battle.Player.OnCombatFeedback -= Forward; battle.OnSummonAdded -= AddEnemy; }
            foreach (var enemy in enemies) enemy.OnCombatFeedback -= Forward;
            enemies.Clear(); battle = value;
            if (battle == null) return;
            battle.Player.OnCombatFeedback += Forward; battle.OnSummonAdded += AddEnemy;
            foreach (var enemy in battle.Enemies) AddEnemy(enemy);
        }
        private void AddEnemy(Enemy enemy) { if (enemy != null && enemies.Add(enemy)) enemy.OnCombatFeedback += Forward; }
        private void Forward(CombatFeedback feedback) { if (!disposed && battle != null && !battle.IsGameOver) receive(feedback); }
        public void Dispose() { if (disposed) return; Bind(null); disposed = true; }
    }
}
