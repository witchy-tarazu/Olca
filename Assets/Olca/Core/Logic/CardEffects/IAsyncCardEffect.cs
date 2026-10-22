using System.Threading;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.CardEffects
{
    /// <summary>
    /// 非同期で実行されるカード効果
    /// </summary>
    public interface IAsyncCardEffect
    {
        /// <summary>
        /// カード効果を非同期で実行します
        /// </summary>
        /// <param name="context">バトルコンテキスト</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        FrameTask ExecuteAsync(BattleContext context, CancellationToken cancellationToken);
    }
}
