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

    public enum EnemyPatternEndMode { Loop, Stop, Next }

    /// <summary>外部定義の行動列と終端。Nextの参照先は登録時に検証する。</summary>
    public sealed class EnemyActionPattern
    {
        public int Id { get; }
        public EnemyPatternEndMode EndMode { get; }
        public int? NextPatternId { get; }
        private readonly System.Collections.Generic.List<IEnemyAIAction> actions;
        public EnemyActionPattern(int id,System.Collections.Generic.IEnumerable<IEnemyAIAction> actions,EnemyPatternEndMode endMode,int? nextPatternId=null)
        {
            if(!System.Enum.IsDefined(typeof(EnemyPatternEndMode),endMode))throw new System.ArgumentOutOfRangeException(nameof(endMode));
            if(endMode==EnemyPatternEndMode.Next?!nextPatternId.HasValue:nextPatternId.HasValue)throw new System.ArgumentException("Nextだけに終了先IDを指定してください",nameof(nextPatternId));
            this.actions=actions==null?throw new System.ArgumentNullException(nameof(actions)):new System.Collections.Generic.List<IEnemyAIAction>(actions);
            if(this.actions.Count==0||this.actions.Exists(a=>a==null))throw new System.ArgumentException("行動列が必要です",nameof(actions));
            Id=id;EndMode=endMode;NextPatternId=nextPatternId;
        }
        internal System.Collections.Generic.List<IEnemyAIAction> CopyActions()=>new System.Collections.Generic.List<IEnemyAIAction>(actions);
    }

    /// <summary>HP上限と行動列。境界を含むかも定義元が明示し、サンプル値を製品へ登録しない。</summary>
    public sealed class EnemyHpActionPattern
    {
        public int MaximumHpBasisPoints { get; }
        public bool IncludesBoundary { get; }
        private readonly System.Collections.Generic.List<IEnemyAIAction> actions;
        public int? RegisteredPatternId { get; }
        private EnemyHpActionPattern(int maximum,bool includesBoundary,int id)
        {
            if(maximum<0||maximum>10000)throw new System.ArgumentOutOfRangeException(nameof(maximum));
            MaximumHpBasisPoints=maximum;IncludesBoundary=includesBoundary;RegisteredPatternId=id;
        }
        public static EnemyHpActionPattern ForRegisteredPattern(int maximumHpBasisPoints,bool includesBoundary,int patternId)
            =>new EnemyHpActionPattern(maximumHpBasisPoints,includesBoundary,patternId);
        public EnemyHpActionPattern(int maximumHpBasisPoints,bool includesBoundary,System.Collections.Generic.IEnumerable<IEnemyAIAction> actions)
        {
            if(maximumHpBasisPoints<0||maximumHpBasisPoints>10000)throw new System.ArgumentOutOfRangeException(nameof(maximumHpBasisPoints));
            this.actions=actions==null?throw new System.ArgumentNullException(nameof(actions)):new System.Collections.Generic.List<IEnemyAIAction>(actions);
            if(this.actions.Count==0||this.actions.Exists(a=>a==null))throw new System.ArgumentException("行動列が必要です",nameof(actions));
            MaximumHpBasisPoints=maximumHpBasisPoints;IncludesBoundary=includesBoundary;
        }
        internal System.Collections.Generic.List<IEnemyAIAction> CopyActions()=>new System.Collections.Generic.List<IEnemyAIAction>(actions);
    }

}
