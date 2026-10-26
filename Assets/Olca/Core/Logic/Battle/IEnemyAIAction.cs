using Olca.Core.Domain;
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

    /// <summary>敵IDに対応する定義を戦闘生成ごとに新規生成する。未登録はnullで既定AIを維持する。</summary>
    public interface IEnemyAIProvider
    {
        EnemyAIRegistration Create(Enemy enemy, Olca.Core.Logic.CardEffects.ICardDatabase cards);
    }

    /// <summary>行動の状態を個体/再戦/復帰で共有しないfactory登録。製品値は呼出し元が供給する。</summary>
    public sealed class EnemyAIRegistry : IEnemyAIProvider
    {
        private readonly System.Collections.Generic.Dictionary<string,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>> factories;
        public EnemyAIRegistry(System.Collections.Generic.IReadOnlyDictionary<string,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>> factories)
        {
            if(factories==null)throw new System.ArgumentNullException(nameof(factories));
            this.factories=new System.Collections.Generic.Dictionary<string,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration>>(System.StringComparer.Ordinal);
            foreach(var entry in factories){if(string.IsNullOrWhiteSpace(entry.Key)||entry.Value==null)throw new System.ArgumentException("敵IDとfactoryが必要です",nameof(factories));this.factories.Add(entry.Key,entry.Value);}
        }
        public EnemyAIRegistration Create(Enemy enemy,Olca.Core.Logic.CardEffects.ICardDatabase cards)
            =>factories.TryGetValue(enemy.EnemyId,out var factory)?factory(enemy,cards)??throw new System.InvalidOperationException("登録済みの敵定義がnullです"):null;
    }

    /// <summary>戦闘開始時に再供給する定義。可変の実行状態や製品値をsaveへ書き込まない。</summary>
    public sealed class EnemyAIRegistration
    {
        private readonly EnemyActionPattern[] patterns;
        private readonly EnemyHpActionPattern[] hpPatterns;
        public int InitialPatternId { get; }
        private readonly System.Func<Enemy,EnemyAIObservationFrame,int?> switchRule;
        internal System.Collections.Generic.List<IEnemyAIAction> CopyInitialActions()=>System.Array.Find(patterns,v=>v.Id==InitialPatternId).CopyActions();
        public EnemyAIRegistration(System.Collections.Generic.IEnumerable<EnemyActionPattern> patterns,int initialPatternId,System.Collections.Generic.IEnumerable<EnemyHpActionPattern> hpPatterns=null,System.Func<Enemy,EnemyAIObservationFrame,int?> switchRule=null)
        {
            this.patterns=patterns==null?throw new System.ArgumentNullException(nameof(patterns)):new System.Collections.Generic.List<EnemyActionPattern>(patterns).ToArray();
            this.hpPatterns=hpPatterns==null?null:new System.Collections.Generic.List<EnemyHpActionPattern>(hpPatterns).ToArray();
            // 不正な定義を、召喚個体や戦闘への登録より前に拒否する。
            var ids=new System.Collections.Generic.HashSet<int>();
            foreach(var value in this.patterns)if(value==null||!ids.Add(value.Id))throw new System.ArgumentException("行動定義IDの欠落/重複です");
            if(!ids.Contains(initialPatternId))throw new System.ArgumentException("初期IDが未登録です");
            foreach(var value in this.patterns)if(value.EndMode==EnemyPatternEndMode.Next&&!ids.Contains(value.NextPatternId.Value))throw new System.ArgumentException("終了先IDが未登録です");
            if(this.hpPatterns!=null){int last=-1;foreach(var value in this.hpPatterns){if(value==null||value.MaximumHpBasisPoints<=last||value.RegisteredPatternId.HasValue&&!ids.Contains(value.RegisteredPatternId.Value))throw new System.ArgumentException("HP条件が不正です");last=value.MaximumHpBasisPoints;}if(last!=10000||!this.hpPatterns[this.hpPatterns.Length-1].IncludesBoundary)throw new System.ArgumentException("最大HPまでの条件が必要です");}
            if(this.hpPatterns!=null&&switchRule!=null)throw new System.ArgumentException("HPと他条件を併用する場合は一つのresolverで優先を明示してください");
            InitialPatternId=initialPatternId;this.switchRule=switchRule;
        }
        internal void Apply(BattleSystem battle,Enemy enemy)
        {
            if(!battle.ConfigureEnemyPatterns(enemy,patterns,InitialPatternId))throw new System.InvalidOperationException("敵の行動定義を登録できません");
            if(hpPatterns!=null&&!battle.ConfigureEnemyHpPatterns(enemy,hpPatterns))throw new System.InvalidOperationException("敵のHP条件を登録できません");
            battle.ConfigureEnemySwitchRule(enemy,switchRule);
        }
    }

    public enum EnemyAIEventKind { EnemyCardUsed, PlayerCardUsed, DamageReceived, EnemyDied }
    /// <summary>実際に発生した通知。属性/毒/影響元を残し、攻撃回数の製品上の数え方を決めない。</summary>
    public sealed class EnemyAIEvent
    {
        public EnemyAIEventKind Kind { get; }
        public Enemy Source { get; }
        public string CardId { get; }
        public EnemyDamageNotice? Damage { get; }
        internal EnemyAIEvent(EnemyAIEventKind kind,Enemy source=null,string cardId=null,EnemyDamageNotice? damage=null){Kind=kind;Source=source;CardId=cardId;Damage=damage;}
    }
    /// <summary>停止していない論理更新の通知集合。登録resolverが競合優先/回数/期限を一度だけ判定する。</summary>
    public sealed class EnemyAIObservationFrame
    {
        public long LogicalFrame { get; }
        public System.Collections.Generic.IReadOnlyList<EnemyAIEvent> Events { get; }
        internal EnemyAIObservationFrame(long frame,EnemyAIEvent[] events){LogicalFrame=frame;Events=System.Array.AsReadOnly(events);}
    }

    /// <summary>敵カードから明示された個体とAIを新規生成する。配置/上限/値は製品定義が指定する。</summary>
    public sealed class EnemySummonDefinition
    {
        private readonly System.Func<Olca.Core.Logic.CardEffects.BattleContext,Enemy> create;
        private readonly System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration> createAI;
        public EnemySummonDefinition(System.Func<Olca.Core.Logic.CardEffects.BattleContext,Enemy> create,System.Func<Enemy,Olca.Core.Logic.CardEffects.ICardDatabase,EnemyAIRegistration> createAI)
        {this.create=create??throw new System.ArgumentNullException(nameof(create));this.createAI=createAI??throw new System.ArgumentNullException(nameof(createAI));}
        internal void Execute(Olca.Core.Logic.CardEffects.BattleContext context)
        {
            if(context.ActionCancellation.IsCancellationRequested||context.Battle.IsGameOver||context.EnemySource==null||context.EnemySource.IsDead)return;
            var enemy=create(context)??throw new System.InvalidOperationException("召喚個体がnullです");
            var registration=createAI(enemy,context.CardDatabase)??throw new System.InvalidOperationException("召喚AIがnullです");
            context.Battle.AddConfiguredSummon(enemy,registration);
        }
    }
    public sealed class EnemySummonEffect : Olca.Core.Logic.CardEffects.ICardEffect
    {
        private readonly EnemySummonDefinition definition;
        public EnemySummonEffect(EnemySummonDefinition definition){this.definition=definition??throw new System.ArgumentNullException(nameof(definition));}
        public void Execute(Olca.Core.Logic.CardEffects.BattleContext context)=>definition.Execute(context);
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
