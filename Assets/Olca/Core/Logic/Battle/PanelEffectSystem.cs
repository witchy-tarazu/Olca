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

        private const int MAGMA_DAMAGE = 5; // マグマダメージ量

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
        /// 現在は継続効果がないため、このメソッドは何もしません。
        /// 将来的に継続効果が追加される場合はここで処理します。
        /// </remarks>
        public void Tick(int deltaTimeMs, PlayerCharacter player)
        {
            // 現在は継続効果なし
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
            if (occupants.Length == 0) return;
            // ダメージ通知による死亡・占有変更に影響されないよう対象を先に固定する。
            foreach (var enemy in occupants)
                if (enemy.Element != ElementType.Fire && enemy.TerrainState.ReceivesPanelEffects)
                    enemy.TakeDamage(MAGMA_DAMAGE, ElementType.None, poiseDamage: 0, hitInvincibilityMs: hitInvincibilityMs);
            if (player != null && !player.IsDead && player.GridPosition == position)
                ApplyMagmaPlayerDamage(player);
            field.ChangePanelType(position, PanelType.Normal);
            OnPanelEffectApplied?.Invoke(position, PanelType.Magma);
        }

        private void ApplyMagmaPlayerDamage(PlayerCharacter player)
        {
            if (player.TerrainState.ReceivesPanelEffects && player.TryTakeDamage(MAGMA_DAMAGE, affectsPoise: false)
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
            if (player.IsDead) return;

            // 消費条件は既存のまま、浮遊者をダメージ対象から除外する。
            int hpBefore = player.CurrentHp;
            ApplyMagmaPlayerDamage(player);
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
