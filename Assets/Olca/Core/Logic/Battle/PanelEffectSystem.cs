using System;
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

        /// <summary>
        /// プレイヤーがパネルから離れた時の効果を適用
        /// </summary>
        public void ApplyOnLeaveEffect(PlayerCharacter player, Vector2Int fromPosition)
        {
            if (player == null) return;

            PanelType panelType = field.GetPanelType(fromPosition);

            switch (panelType)
            {
                case PanelType.Cracked:
                    // Crackedパネルから離れるとHoleに変化
                    CrackPanel(fromPosition);
                    logger?.LogInfo($"Crackedパネルから離れて破壊: {fromPosition}");
                    break;
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

            player.TakeDamage(MAGMA_DAMAGE);
            field.ChangePanelType(position, PanelType.Normal);
            logger?.LogInfo($"Magmaパネルで{MAGMA_DAMAGE}ダメージ、通常パネルに変化: {position}");
            OnPanelEffectApplied?.Invoke(position, PanelType.Magma);
        }

        /// <summary>
        /// パネル効果が適用された時のイベント
        /// </summary>
        public event Action<Vector2Int, PanelType> OnPanelEffectApplied;
    }
}
