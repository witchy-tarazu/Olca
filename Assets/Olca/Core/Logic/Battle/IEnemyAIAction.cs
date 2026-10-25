namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 敵AI行動のインターフェース
    /// Strategy パターンで異なる敵タイプの行動を実装
    /// 整数ベース（ミリ秒）で時間を管理
    /// </summary>
    public interface IEnemyAIAction
    {
        /// <summary>
        /// 行動可能かチェック
        /// </summary>
        /// <param name="context">AI判断に必要な情報</param>
        /// <returns>行動可能ならtrue</returns>
        bool CanExecute(AIContext context);

        /// <summary>
        /// 行動を実行
        /// </summary>
        /// <param name="context">AI判断に必要な情報</param>
        void Execute(AIContext context);

        /// <summary>
        /// 行動後のクールダウン時間（ミリ秒）を取得
        /// </summary>
        /// <returns>クールダウン時間（ミリ秒）</returns>
        int GetCooldownMs();

        /// <summary>
        /// 行動後のクールダウン時間（秒）を取得 - 互換性用
        /// </summary>
        /// <returns>クールダウン時間（秒）</returns>
        float GetCooldown();
    }
    /// <summary>継続行動だけが実装する論理寿命。期間/効果は行動の定義元が所有する。</summary>
    public interface IContinuingEnemyAIAction : IEnemyAIAction
    {
        bool IsRunning { get; }
        /// <summary>停止していないAI更新だけで呼ぶ。logicalFrame時は固定1フレームとして進める。</summary>
        void Tick(int milliseconds, AIContext context, bool logicalFrame);
        /// <summary>中断/死亡/戦闘離脱時に予約効果や一時状態を解放する。描画完了を待たない。</summary>
        void Cancel(AIContext context);
    }

}
