using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// パネル効果システム
    /// Magma即時ダメージ、Cracked破壊など、パネルタイプに応じた効果を管理
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class PanelEffectSystem
    {
        private readonly BattleField field;
        private readonly IGameLogger logger;

        private int hitInvincibilityMs;
        internal void ConfigureHitInvincibility(int durationMs) => hitInvincibilityMs = durationMs;

        public const int RecoveryIntervalFrames = 60; // 継続回復の暫定周期。描画時間は使わない。
        private sealed class RecoveryClock { public Vector2Int Position; public long Revision, Units; }
        private readonly Dictionary<object,RecoveryClock> recoveryClocks = new Dictionary<object,RecoveryClock>();
        internal bool HasRecoveryPanels => field.HasRecoveryPanels;
        internal void Clear() => recoveryClocks.Clear();
        internal void OnCharacterMoved(object character)=>recoveryClocks.Remove(character);

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PanelEffectSystem(BattleField field, IGameLogger logger = null)
        {
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.logger = logger;
        }

        /// <summary>
        /// パネル効果を更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        /// <remarks>
        /// 属性一致の非フロート対象だけを周期回復する。停止の判断はBattleSystemが所有する。
        /// </remarks>
        public void Tick(int deltaTimeMs, PlayerCharacter player) => Tick(deltaTimeMs,player,Array.Empty<Enemy>(),false);
        internal void Tick(int deltaTimeMs,PlayerCharacter player,IReadOnlyList<Enemy> enemies,bool logicalFrame)
        {
            long units=logicalFrame?50:Math.Max(0,deltaTimeMs)*3L;
            var active=new HashSet<object>();
            if(player!=null){active.Add(player);TickRecovery(player,player.GetLogicalPosition(),player.ActiveElement,player.TerrainState,player.IsDead,player.Heal,units);}
            foreach(var enemy in enemies){active.Add(enemy);TickRecovery(enemy,enemy.GridPosition,enemy.Element,enemy.TerrainState,enemy.IsDead,enemy.Heal,units);}
            foreach(var key in recoveryClocks.Keys.Where(k=>!active.Contains(k)).ToArray())recoveryClocks.Remove(key);
        }
        private void TickRecovery(object owner,Vector2Int position,ElementType element,BattleTerrainState terrain,bool dead,Action<int> heal,long units)
        {
            if(dead||!terrain.ReceivesPanelEffects||!field.IsInBounds(position)){recoveryClocks.Remove(owner);return;}
            var type=field.GetPanelType(position);var values=field.GetPanelValues(position);
            if(values.Heal==0||!((type==PanelType.Grass&&element==ElementType.Earth)||(type==PanelType.Magma&&element==ElementType.Fire))){recoveryClocks.Remove(owner);return;}
            long revision=field.GetPanelRevision(position);
            if(!recoveryClocks.TryGetValue(owner,out var clock)||clock.Position!=position||clock.Revision!=revision)
                recoveryClocks[owner]=clock=new RecoveryClock{Position=position,Revision=revision};
            clock.Units+=units;long pulses=clock.Units/(RecoveryIntervalFrames*50L);clock.Units%=RecoveryIntervalFrames*50L;
            if(pulses>0)heal((int)Math.Min(int.MaxValue,pulses*values.Heal));
        }

        /// <summary>
        /// プレイヤーが移動した時の即時効果を適用
        /// </summary>
        public void ApplyOnEnterEffect(PlayerCharacter player, Vector2Int position)
        {
            if (player == null) return;

            PanelType panelType = field.GetPanelType(position);

            switch (panelType)
            {
                case PanelType.Magma:
                    // Magmaパネルで即座にダメージを与え、通常パネルに変化
                    ApplyMagmaDamage(player, position);
                    break;

                // Ice、Grassは移動システム側で処理
                // Crackedは離れる時に処理（ApplyOnLeaveEffect）
            }
        }

        /// <summary>敵の移動先にいる対象全員を、パネル消費前に個別判定する。</summary>
        public void ApplyOnEnemyEnterEffect(IReadOnlyList<Enemy> enemies, PlayerCharacter player, Vector2Int position)
        {
            if (field.GetPanelType(position) != PanelType.Magma) return;
            var occupants = enemies.Where(e => !e.IsDead && e.GridPosition == position).ToArray();
            int damage=field.GetPanelValues(position).Damage;
            if (occupants.Length == 0) return;
            // 火属性だけが乗るマグマは継続回復のため維持する。非火属性の消費条件は既存のまま。
            bool playerPresent=player!=null&&!player.IsDead&&player.GetLogicalPosition()==position;
            if(occupants.All(e=>e.Element==ElementType.Fire)&&(!playerPresent||player.ActiveElement==ElementType.Fire))return;
            // ダメージ通知による死亡・占有変更に影響されないよう対象を先に固定する。
            foreach (var enemy in occupants)
                if (damage>0 && enemy.Element != ElementType.Fire && enemy.TerrainState.ReceivesPanelEffects)
                    enemy.TakeDamage(damage, ElementType.None, poiseDamage: 0, hitInvincibilityMs: hitInvincibilityMs);
            if (playerPresent)
                ApplyMagmaPlayerDamage(player,damage);
            field.ChangePanelType(position, PanelType.Normal);
            OnPanelEffectApplied?.Invoke(position, PanelType.Magma);
        }

        private void ApplyMagmaPlayerDamage(PlayerCharacter player,int damage)
        {
            if (damage>0 && player.ActiveElement != ElementType.Fire && player.TerrainState.ReceivesPanelEffects && player.TryTakeDamage(damage, affectsPoise: false)
                && !player.IsDead) player.SetInvincible(hitInvincibilityMs);
        }

        /// <summary>
        /// プレイヤーがパネルから離れた時の効果を適用
        /// </summary>
        public void ApplyOnLeaveEffect(PlayerCharacter player, Vector2Int fromPosition)
        {
            if (player != null) ApplyOnLeaveEffect(player.TerrainState, fromPosition);
        }

        /// <summary>占有更新後に呼ぶ。最後の非フロート対象の離脱だけがヒビを破壊する。</summary>
        public void ApplyOnLeaveEffect(BattleTerrainState terrain, Vector2Int fromPosition)
        {
            if (terrain == null || !terrain.ReceivesPanelEffects || !field.IsInBounds(fromPosition)
                || field.IsOccupied(fromPosition)) return;

            if (field.GetPanelType(fromPosition) == PanelType.Cracked)
            {
                CrackPanel(fromPosition);
                logger?.LogInfo($"最後の対象がCrackedパネルから離れて破壊: {fromPosition}");
            }
        }

        /// <summary>
        /// Crackedパネルを破壊してHoleに変化
        /// </summary>
        private void CrackPanel(Vector2Int position)
        {
            field.ChangePanelType(position, PanelType.Hole);
            OnPanelEffectApplied?.Invoke(position, PanelType.Cracked);
        }

        /// <summary>
        /// Magmaパネルの即時ダメージと通常パネルへの変化
        /// </summary>
        private void ApplyMagmaDamage(PlayerCharacter player, Vector2Int position)
        {
            if (player.IsDead || player.ActiveElement==ElementType.Fire) return;

            // 消費条件は既存のまま、浮遊者をダメージ対象から除外する。
            int hpBefore = player.CurrentHp;
            ApplyMagmaPlayerDamage(player,field.GetPanelValues(position).Damage);
            field.ChangePanelType(position, PanelType.Normal);
            logger?.LogInfo($"Magmaパネルで{hpBefore - player.CurrentHp}ダメージ、通常パネルに変化: {position}");
            OnPanelEffectApplied?.Invoke(position, PanelType.Magma);
        }

        /// <summary>
        /// パネル効果が適用された時のイベント
        /// </summary>
        public event Action<Vector2Int, PanelType> OnPanelEffectApplied;
    }
}
