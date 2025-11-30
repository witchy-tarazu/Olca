using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.Card
{
    /// <summary>
    /// カード効果実行時のコンテキスト
    /// </summary>
    public class BattleContext
    {
        /// <summary>バトルシステム</summary>
        public BattleSystem Battle { get; }

        /// <summary>プレイヤーキャラクター</summary>
        public PlayerCharacter Player => Battle.Player;

        /// <summary>敵リスト</summary>
        public IReadOnlyList<Enemy> Enemies => Battle.Enemies;

        /// <summary>バトルフィールド</summary>
        public BattleField Field => Battle.Field;

        /// <summary>使用したカード</summary>
        public Domain.Card UsedCard { get; }

        public BattleContext(BattleSystem battle, Domain.Card usedCard)
        {
            Battle = battle;
            UsedCard = usedCard;
        }
    }
}
