using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 全敵のAIを統括管理するシステム
    /// BattleSystem.Tick()から呼び出される
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class EnemyAISystem
    {
        private readonly List<EnemyAIBehavior> aiBehaviors;
        private readonly IGameLogger logger;

        /// <summary>管理しているAI行動リスト（読み取り専用）</summary>
        public IReadOnlyList<EnemyAIBehavior> AIBehaviors => aiBehaviors.AsReadOnly();

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public EnemyAISystem(IGameLogger logger = null)
        {
            this.aiBehaviors = new List<EnemyAIBehavior>();
            this.logger = logger;
        }

        /// <summary>
        /// 敵をAI管理に登録
        /// </summary>
        /// <param name="enemy">登録する敵</param>
        /// <param name="actions">AI行動リスト（優先順位順）</param>
        public void RegisterEnemy(Enemy enemy, List<IEnemyAIAction> actions)
        {
            if (enemy == null)
                throw new ArgumentNullException(nameof(enemy));

            if (actions == null || actions.Count == 0)
                throw new ArgumentException("AI行動リストは空であってはいけません", nameof(actions));

            var behavior = new EnemyAIBehavior(enemy, actions);
            aiBehaviors.Add(behavior);

            logger?.LogInfo($"敵 {enemy.Name} をAI管理に登録");
        }

        /// <summary>
        /// 敵をAI管理から除外（死亡時に呼び出される）
        /// </summary>
        /// <param name="enemy">除外する敵</param>
        public void UnregisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            var behavior = aiBehaviors.Find(b => b.Enemy == enemy);
            if (behavior != null)
            {
                aiBehaviors.Remove(behavior);
                logger?.LogInfo($"敵 {enemy.Name} をAI管理から除外");
            }
        }

        /// <summary>
        /// 全敵のAIを更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="player">プレイヤーキャラクター</param>
        /// <param name="field">バトルフィールド</param>
        /// <param name="random">乱数プロバイダー</param>
        public void Tick(int deltaTimeMs, PlayerCharacter player, BattleField field, IRandomProvider random,
            IList<Enemy> tickParticipants = null)
        {
            if (player == null || field == null) return;

            // 従来の逆登録順を維持し、行動中の追加・死亡による登録解除で走査をずらさない。
            var behaviors = aiBehaviors.ToArray();
            for (int i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                if (!aiBehaviors.Contains(behavior)
                    || (tickParticipants != null && !tickParticipants.Contains(behavior.Enemy))) continue;

                // 敵ごとのコンテキストを作成
                var context = new AIContext(behavior.Enemy, player, field, random, logger);

                // AI更新
                behavior.Update(deltaTimeMs, context);
            }
        }

    }
}
