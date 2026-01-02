using System.Threading;
using Cysharp.Threading.Tasks;

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
        UniTask ExecuteAsync(BattleContext context, CancellationToken cancellationToken);
    }
}
