using Olca.Core.Logic.CardEffects;
using System;
using System.Collections.Generic;
using System.Threading;
using Olca.Core.Interfaces;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// バトルシステムのメインロジック
    /// </summary>
    public class BattleSystem
    {
        private readonly PlayerCharacter player;
        public TransformationAttackDefinition ActivePlayerAttack => Transformations.ActiveAttack ?? player.DepartureStyle.Attack;
        public int BasicAttackRange => Transformations.ActiveId==null?1+(player.AttackGrowth?.RangeBonus??0):1;
        private Enemy FindBasicAttackTarget()
        {
            Enemy nearest=null;
            foreach(var enemy in enemies){
                int distance=enemy.GridPosition.X-player.GridPosition.X;
                if(!enemy.IsDead&&enemy.GridPosition.Y==player.GridPosition.Y&&distance>0&&distance<=BasicAttackRange&&
                    (nearest==null||enemy.GridPosition.X<nearest.GridPosition.X))nearest=enemy;
            }
            return nearest;
        }
        public int ResolveBasicChargeFrames(int fallback)
        {
            int frames=ActivePlayerAttack?.ChargeFrames??fallback;
            return Transformations.ActiveId==null?player.AttackGrowth?.ChargeFrames(frames)??frames:frames;
        }
        public int ResolveBasicIntervalFrames(int fallback)
        {
            int frames=ActivePlayerAttack?.IntervalFrames??fallback;
            return Transformations.ActiveId==null?player.AttackGrowth?.IntervalFrames(frames)??frames:frames;
        }
        public ElementType ActivePlayerElement => ActivePlayerAttack?.Element ?? ElementType.None;
        private readonly List<Enemy> enemies;
        private readonly List<EnemyAIEvent> enemyAIEvents=new List<EnemyAIEvent>();
        private readonly Dictionary<Enemy,Action<EnemyDamageNotice>> enemyDamageObservers=new Dictionary<Enemy,Action<EnemyDamageNotice>>();
        internal void ConfigureEnemySwitchRule(Enemy enemy,Func<Enemy,EnemyAIObservationFrame,int?> rule)=>enemyAI.ConfigureSwitchRule(enemy,rule);
        internal void NotifyEnemyCardUsed(Enemy enemy,string cardId){if(!IsGameOver)enemyAIEvents.Add(new EnemyAIEvent(EnemyAIEventKind.EnemyCardUsed,enemy,cardId));}
        private void OnPlayerCardUsedForAI(Card card){if(!IsGameOver)enemyAIEvents.Add(new EnemyAIEvent(EnemyAIEventKind.PlayerCardUsed,cardId:card.Id));}
        private readonly Dictionary<Enemy, Action> enemyDeathHandlers = new Dictionary<Enemy, Action>();
        private readonly Dictionary<Enemy, Action<Vector2Int>> enemyPositionHandlers = new Dictionary<Enemy, Action<Vector2Int>>();
        private Vector2Int previousPlayerPosition;
        private Vector2Int previousPlayerLogicalPosition;
        private readonly BattleField battleField;
        private readonly CustomGaugeSystem customGauge;
        private readonly CustomScreenSystem customScreen;
        private readonly CardUsageSystem cardUsage;
        private readonly PlayerMovementSystem playerMovement;
        private readonly PanelEffectSystem panelEffects;
        private readonly EnemyAISystem enemyAI;
        private readonly DelayedEffectSystem delayedEffects;
        private readonly IRandomProvider randomProvider;
        private bool checkpointRandom;
        internal SeededBattleRandom SimulationRandom => randomProvider as SeededBattleRandom;
        public BattleRandomStateDto CheckpointRandomState => checkpointRandom ? ((SeededBattleRandom)randomProvider).CaptureState() : null;
        internal void UseCheckpointRandom() { if (!(randomProvider is SeededBattleRandom)) throw new InvalidOperationException("復帰済み戦闘乱数が必要です"); checkpointRandom=true; }
        private readonly IGameLogger logger;

        private BattlePauseReason pauseReasons;
        private int hitStopRemainingFrames, pendingHitStopFrames;
        /// <summary>戦闘全体を止める残り論理フレーム。通常時計には加算しない。</summary>
        public int HitStopRemainingFrames => hitStopRemainingFrames;
        public bool IsHitStopped => hitStopRemainingFrames>0;
        private void RequestHitStop(int frames) => pendingHitStopFrames=Math.Max(pendingHitStopFrames,frames);
        private readonly List<DarkeningEffect> darkeningEffects=new List<DarkeningEffect>();
        private sealed class DarkeningEffect
        {
            public DarkeningSide Side;public int Window,Remaining;public Action Resolve;
        }
        public bool IsDarkening => darkeningEffects.Count>0;
        public DarkeningSide? ActiveDarkeningSide => IsDarkening?darkeningEffects[darkeningEffects.Count-1].Side:(DarkeningSide?)null;
        /// <summary>明示された時間定義だけを使う暗転入口。敵味方各一件、後入れ先出し。同陣営の古い予約は破棄する。</summary>
        public bool TryStartDarkeningEffect(DarkeningSide side,int interruptionFrames,int presentationFrames,Action resolve)
        {
            if(!Enum.IsDefined(typeof(DarkeningSide),side))throw new ArgumentOutOfRangeException(nameof(side));
            if(interruptionFrames<0||presentationFrames<=0||interruptionFrames>presentationFrames)throw new ArgumentOutOfRangeException(nameof(interruptionFrames));
            if(resolve==null)throw new ArgumentNullException(nameof(resolve));
            if(IsGameOver||pauseReasons!=BattlePauseReason.None)return false;
            if(IsDarkening){var current=darkeningEffects[darkeningEffects.Count-1];if(current.Side==side||current.Window==0)return false;}
            darkeningEffects.RemoveAll(effect=>effect.Side==side);
            darkeningEffects.Add(new DarkeningEffect{Side=side,Window=interruptionFrames,Remaining=presentationFrames,Resolve=resolve});
            return true;
        }
        private void AdvanceDarkeningFrame()
        {
            if(IsGameOver){darkeningEffects.Clear();return;}
            if(pauseReasons!=BattlePauseReason.None||!IsDarkening)return;
            var current=darkeningEffects[darkeningEffects.Count-1];
            if(current.Window>0)current.Window--;
            if(--current.Remaining>0)return;
            darkeningEffects.RemoveAt(darkeningEffects.Count-1);
            try {current.Resolve();}catch {darkeningEffects.Clear();throw;}
        }
        private BattleState state;
        private long actionTimeUnits;
        internal long ActionPresentationTimeUnits => actionTimeUnits;
        public Olca.Core.Logic.CardEffects.CardActionVisualState PlayerActionVisual { get; internal set; }
        internal bool OwnsPlayerAction(object owner) => owner != null && ReferenceEquals(playerActionOwner, owner);
        private long actionFrame;
        internal long ActionFrame => actionFrame;
        /// <summary>表示が参照する戦闘の論理時刻。停止中は進まない。</summary>
        public long LogicalFrame => actionFrame;
        /// <summary>召喚や終了時のリスト破棄に左右されない、開始時の評価対象数。</summary>
        public int InitialEvaluationEnemyCount { get; }
        public bool IsBossEncounter { get; }

        private readonly SimultaneousKillCounter simultaneousKills = new SimultaneousKillCounter();
        public int EnemyKillCount => simultaneousKills.KillCount;
        /// <summary>HP被弾数ではなく、実際に成立した強靭度の中断回数。麻痺の強制成立も含む。</summary>
        public int PlayerStaggerCount { get; private set; }
        public int MaximumSimultaneousKills => simultaneousKills.Maximum;
        public int SimultaneousKillGraceFrames => simultaneousKills.GraceFrames;
        /// <summary>計測開始前の調整入口。停止時間を除く戦闘の論理フレームだけを使用する。</summary>
        public void ConfigureSimultaneousKillGraceFrames(int frames)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            if (actionFrame != 0 || EnemyKillCount != 0 || IsGameOver) throw new InvalidOperationException("同時撃破猶予は戦闘の計測開始前に設定してください");
            simultaneousKills.GraceFrames = frames;
        }
        private int resolutionDepth;
        private bool resolutionRequested;

        /// <summary>プレイヤーキャラクター</summary>
        public PlayerCharacter Player => player;

        /// <summary>敵リスト</summary>
        public IReadOnlyList<Enemy> Enemies => enemies.AsReadOnly();

        /// <summary>バトルフィールド</summary>
        public BattleField Field => battleField;

        /// <summary>カスタムゲージ</summary>
        public CustomGaugeSystem CustomGauge => customGauge;

        /// <summary>カスタム画面システム</summary>
        public CustomScreenSystem CustomScreen => customScreen;
        public BattleStartEffects StartEffects { get; }
        public TransformationSelectionState Transformations { get; }

        /// <summary>カード使用システム</summary>
        public CardUsageSystem CardUsage => cardUsage;

        /// <summary>プレイヤー移動システム</summary>
        public PlayerMovementSystem PlayerMovement => playerMovement;

        /// <summary>既定の敵AI・プレイヤー基本攻撃・マグマが発生させる共通無敵。0は旧設定。</summary>
        public int DefaultHitInvincibilityMs { get; private set; }
        public void ConfigureDefaultHitInvincibility(int durationMs)
        {
            if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (actionTimeUnits != 0 || IsGameOver) throw new InvalidOperationException("共通被弾無敵は戦闘更新前に設定してください");
            DefaultHitInvincibilityMs = durationMs;
            panelEffects.ConfigureHitInvincibility(durationMs);
        }


        /// <summary>パネル効果システム</summary>
        public PanelEffectSystem PanelEffects => panelEffects;

        /// <summary>遅延効果システム</summary>
        public DelayedEffectSystem DelayedEffects => delayedEffects;

        private object playerActionOwner;
        private Action cancelPlayerAction;
        private bool basicAction;
        private long recoveryStarted;
        private int? recoveryCancelFrames;
        public PlayerAttackPhase PlayerActionPhase { get; private set; }
        public bool HasPlayerAction => playerActionOwner != null;
        public bool CanCancelPlayerRecovery => PlayerActionPhase == PlayerAttackPhase.Recovery &&
            recoveryCancelFrames.HasValue && actionTimeUnits - recoveryStarted >= recoveryCancelFrames.Value * 50L;
        public int RecoveryElapsedFrames => PlayerActionPhase == PlayerAttackPhase.Recovery ? (int)((actionTimeUnits - recoveryStarted) / 50) : 0;
        private PlayerAttackTiming basicTiming = new PlayerAttackTiming();
        private readonly Dictionary<string, PlayerAttackTiming> cardTimings = new Dictionary<string, PlayerAttackTiming>();
        public void ConfigureBasicAttackTiming(PlayerAttackTiming timing)
        { if (HasPlayerAction) throw new InvalidOperationException("動作中は時間定義を変更できません"); basicTiming = timing ?? throw new ArgumentNullException(nameof(timing)); }
        public void ConfigureCardAttackTiming(string id, PlayerAttackTiming timing)
        { if (HasPlayerAction) throw new InvalidOperationException("動作中は時間定義を変更できません"); if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("カードIDが必要です"); cardTimings[id] = timing ?? throw new ArgumentNullException(nameof(timing)); }
        internal PlayerAttackTiming CardTiming(Card card) => cardTimings.TryGetValue(card.Id, out var timing) ? timing : null;
        internal object BeginPlayerAction(bool basic, Action cancel)
        {
            if (HasPlayerAction) throw new InvalidOperationException("行動は同時に開始できません");
            playerActionOwner = new object(); basicAction = basic; cancelPlayerAction = cancel;
            PlayerActionPhase = PlayerAttackPhase.Startup; return playerActionOwner;
        }
        internal void SetPlayerActionPhase(object owner, PlayerAttackPhase phase, int? cancelAfter = null)
        {
            if (!ReferenceEquals(owner, playerActionOwner)) return;
            PlayerActionPhase = phase;
            if (phase == PlayerAttackPhase.Recovery) { recoveryStarted = actionTimeUnits; recoveryCancelFrames = cancelAfter; }
        }
        internal void EndPlayerAction(object owner)
        {
            if (!ReferenceEquals(owner, playerActionOwner)) return;
            if (ReferenceEquals(PlayerActionVisual?.ActionOwner, owner)) PlayerActionVisual = null;
            playerActionOwner = null; cancelPlayerAction = null; PlayerActionPhase = PlayerAttackPhase.None; recoveryCancelFrames = null;
        }
        public bool TryPreparePlayerAttack()
        {
            if (!CanPlayerBasicAttack) return false;
            if (HasPlayerAction) cancelPlayerAction?.Invoke();
            return !HasPlayerAction;
        }
        public void CancelBasicAction() => InterruptBasicAction();
        private void InterruptBasicAction() { if (basicAction && HasPlayerAction) cancelPlayerAction?.Invoke(); }
        public void CancelBasicStartupForFormChange()
        { if (PlayerActionPhase == PlayerAttackPhase.Startup) InterruptBasicAction(); }
        public bool TryStartPlayerBasicAttack(bool charged, int fallbackChargeMultiplier = 2)
        {
            if (fallbackChargeMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(fallbackChargeMultiplier));
            if (!TryPreparePlayerAttack()) return false;
            var attack = ActivePlayerAttack;
            long percent = attack != null ? (charged ? attack.ChargedPowerPercent : attack.NormalPowerPercent) : charged ? 100L * fallbackChargeMultiplier : 100;
            var timing = attack?.Timing ?? basicTiming;
            ResolveBasicActionAsync(percent, timing, Transformations.ActiveId).Forget();
            return true;
        }
        private async FrameTask ResolveBasicActionAsync(long percent, PlayerAttackTiming timing, string form)
        {
            var cts = new CancellationTokenSource(); var owner = BeginPlayerAction(true, () => cts.Cancel());
            try {
                await WaitForActionFramesAsync(timing.StartupFrames, cts.Token);
                if (IsGameOver || player.IsActionPrevented || Transformations.ActiveId != form) return;
                SetPlayerActionPhase(owner, PlayerAttackPhase.Executing);
                var target=FindBasicAttackTarget();if(target!=null)ApplyPlayerBasicAttack(target,percent);
                if (IsGameOver) return;
                SetPlayerActionPhase(owner, PlayerAttackPhase.Recovery, timing.CancelAfterFrames);
                await WaitForActionFramesAsync(timing.RecoveryFrames, cts.Token);
            } catch (OperationCanceledException) { }
            finally { EndPlayerAction(owner); cts.Dispose(); }
        }

        /// <summary>バトル状態</summary>
        public BattleState State => state;

        /// <summary>一時停止中か</summary>
        public bool IsUserPaused => (pauseReasons & BattlePauseReason.User) != 0;
        public bool IsPaused => pauseReasons != BattlePauseReason.None || IsDarkening || IsHitStopped;

        /// <summary>バトル終了したか</summary>
        public bool IsGameOver => state == BattleState.Victory || state == BattleState.Defeat;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public BattleSystem(
            PlayerCharacter player,
            List<Enemy> enemies,
            Deck deck,
            float customGaugeSpeed = 1.0f,
            IRandomProvider randomProvider = null,
            IGameLogger logger = null,
            ExceedSystem exceedSystem = null,
            TuningSystem tuningSystem = null,
            FusionSystem fusionSystem = null,
            IEnumerable<Vector2Int> initialPermanentHoles = null, ICardDatabase cardDatabase = null, IEnemyAIProvider enemyAIProvider = null)
        {
            StartEffects = new BattleStartEffects(this,logger);
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            InitialEvaluationEnemyCount = enemies.FindAll(e => !e.IsSummoned).Count;
            IsBossEncounter = enemies.Exists(e => !e.IsSummoned && e.EnemyId == "boss");

            this.battleField = new BattleField(initialPermanentHoles);
            this.customGauge = new CustomGaugeSystem(customGaugeSpeed);
            this.customScreen = new CustomScreenSystem(deck,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem);
            customScreen.Commands.AttachBattle(this, randomProvider);
            Transformations = new TransformationSelectionState(player, customScreen);
            Transformations.OnActivationPresentation += PauseForTransformation;
            customScreen.OnOpening += OnTransformationPhaseOpening;
            this.cardUsage = new CardUsageSystem(this, logger, cardDatabase);
            this.cardUsage.OnCardUsed+=OnPlayerCardUsedForAI;
            this.playerMovement = new PlayerMovementSystem(player, battleField, logger, () => !IsPaused && !IsGameOver && !HasPlayerAction && cardUsage.InFlightCard == null && !player.IsActionPrevented);
            this.panelEffects = new PanelEffectSystem(battleField, logger);
            this.enemyAI = new EnemyAISystem(logger);
            this.delayedEffects = new DelayedEffectSystem(this);
            this.randomProvider = randomProvider;
            this.logger = logger;
            this.state = BattleState.InProgress;

            battleField.SetOccupancyQuery(position =>
                (!player.IsDead && (player.GridPosition == position||player.GetLogicalPosition()==position)) ||
                this.enemies.Exists(enemy => !enemy.IsDead && (enemy.GridPosition == position||enemy.GetLogicalPosition()==position)));
            previousPlayerPosition = player.GridPosition;
            previousPlayerLogicalPosition = player.GetLogicalPosition();
            player.OnPositionChanged += OnPlayerPositionChanged;

            // プレイヤー死亡時の処理
            player.BattleElement = () => ActivePlayerAttack?.Element;
            player.IsOnGrass = () => battleField.GetPanelType(player.GridPosition) == PanelType.Grass;
            player.OnDamageProcessingStarted += BeginResolution;
            player.OnDamageProcessingCompleted += EndResolution;
            player.OnDeath += OnPlayerDeath;
            player.OnHitStopRequested += RequestHitStop;

            // プレイヤーのけぞり時の処理（カード効果の中断）
            player.OnStaggered += OnPlayerStaggered;

            // 初期参加者のAIは既存設定を維持する。
            foreach (var enemy in this.enemies)
                RegisterEnemy(enemy, new List<IEnemyAIAction> { new BasicAttackAction(attackDamage: 10, cooldown: 2.0f,
                    onHit: target => { if (!target.IsDead) target.SetInvincible(DefaultHitInvincibilityMs); }) });

            // 未登録は従来AI。factoryは個体ごとに実行し、初期カスタム/論理更新より前に定義を渡す。
            foreach(var enemy in this.enemies)enemyAIProvider?.Create(enemy,cardDatabase)?.Apply(this,enemy);

            // カスタム画面イベントの購読
            customScreen.OnConfirmed += OnCustomScreenConfirmed;
            customScreen.OnScreenClosed += OnCustomScreenClosed;

        }

        private void OnPlayerPositionChanged(Vector2Int position)
        {
            var previous = previousPlayerPosition;
            previousPlayerPosition = position;
            var logicalPosition = player.GetLogicalPosition();
            bool enteredLogicalPosition = previousPlayerLogicalPosition != logicalPosition;
            previousPlayerLogicalPosition = logicalPosition;
            if (!IsGameOver && !player.IsDead && (previous != position || enteredLogicalPosition))
            {
                BeginResolution();
                try
                {
                    // 一時移動先からの離脱・ヒビ破壊は従来の占有通知を維持する。
                    if (previous != position) panelEffects.ApplyOnLeaveEffect(player.TerrainState, previous);
                    // 一時ヒット座標だけの変更を新しい接地へ転用しない。
                    if (enteredLogicalPosition)
                    {
                        panelEffects.OnCharacterMoved(player);
                        if (enemies.Exists(enemy => !enemy.IsDead && enemy.GridPosition == logicalPosition))
                            panelEffects.ApplyOnEnemyEnterEffect(enemies, player, logicalPosition);
                        else panelEffects.ApplyOnEnterEffect(player, logicalPosition);
                    }
                }
                finally { EndResolution(); }
            }
        }

        /// <summary>確定した切替条件の解決元から呼ぶ。終了/未登録は拒否し、切替だけで停止時計を進めない。</summary>
        public bool TrySwitchEnemyActionPattern(Enemy enemy,List<IEnemyAIAction> actions)
            => !IsGameOver&&enemy!=null&&!enemy.IsDead&&enemies.Contains(enemy)&&enemyAI.TrySwitchActionPattern(enemy,actions);

        /// <summary>HP条件を通常AI更新へ接続する。停止中には評価も行動も進めない。</summary>
        public bool ConfigureEnemyHpPatterns(Enemy enemy,IReadOnlyList<EnemyHpActionPattern> patterns)
            => !IsGameOver&&enemy!=null&&!enemy.IsDead&&enemies.Contains(enemy)&&enemyAI.ConfigureHpPatterns(enemy,patterns);

        public bool ConfigureEnemyPatterns(Enemy enemy,IReadOnlyList<EnemyActionPattern> patterns,int initialPatternId)
            =>!IsGameOver&&enemy!=null&&!enemy.IsDead&&enemies.Contains(enemy)&&enemyAI.ConfigurePatterns(enemy,patterns,initialPatternId);
        public bool TrySwitchRegisteredEnemyPattern(Enemy enemy,int patternId)
            =>!IsGameOver&&enemy!=null&&!enemy.IsDead&&enemies.Contains(enemy)&&enemyAI.TrySwitchRegisteredPattern(enemy,patternId);

        private readonly Dictionary<object,HashSet<Vector2Int>> enemyAttackTelegraphs=new Dictionary<object,HashSet<Vector2Int>>();
        /// <summary>表示用の固定対象マス。重複攻撃でもマスごとに一つとし、所有者数を表示へ渡さない。</summary>
        public IReadOnlyList<Vector2Int> EnemyAttackTiles
        {
            get{var tiles=new HashSet<Vector2Int>();foreach(var entry in enemyAttackTelegraphs.Values)tiles.UnionWith(entry);var result=new List<Vector2Int>(tiles);result.Sort((a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y.CompareTo(b.Y));return result.AsReadOnly();}
        }
        internal void SetEnemyAttackTelegraph(object owner,IEnumerable<Vector2Int> tiles)
        {
            if(IsGameOver)return;
            var result=new HashSet<Vector2Int>();foreach(var tile in tiles)if(battleField.IsInBounds(tile))result.Add(tile);
            if(result.Count>0)enemyAttackTelegraphs[owner]=result;
        }
        internal void ClearEnemyAttackTelegraph(object owner){if(owner!=null)enemyAttackTelegraphs.Remove(owner);}

        private void RegisterEnemy(Enemy enemy, List<IEnemyAIAction> actions)
        {
            enemy.IsOnGrass = () => battleField.GetPanelType(enemy.GridPosition) == PanelType.Grass;
            enemyAI.RegisterEnemy(enemy, actions);
            Action<EnemyDamageNotice> observed=notice=>{if(!IsGameOver)enemyAIEvents.Add(new EnemyAIEvent(EnemyAIEventKind.DamageReceived,enemy,damage:notice));};
            enemy.OnDamageObserved+=observed;enemyDamageObservers.Add(enemy,observed);
            enemy.OnHitStopRequested += RequestHitStop;
            enemy.OnDamageProcessingStarted += BeginResolution;
            enemy.OnDamageProcessingCompleted += EndResolution;
            var previous = enemy.GridPosition;
            var previousLogical=enemy.GetLogicalPosition();
            Action<Vector2Int> moved = position =>
            {
                var from = previous;
                previous = position;
                bool enteredLogical=previousLogical!=enemy.GetLogicalPosition();previousLogical=enemy.GetLogicalPosition();
                if (!IsGameOver && !enemy.IsDead && (from != position||enteredLogical))
                {
                    BeginResolution();
                    try
                    {
                        if(from!=position)panelEffects.ApplyOnLeaveEffect(enemy.TerrainState, from);
                        if(enteredLogical){panelEffects.OnCharacterMoved(enemy);panelEffects.ApplyOnEnemyEnterEffect(enemies, player, enemy.GetLogicalPosition());}
                    }
                    finally { EndResolution(); }
                }
            };
            enemyPositionHandlers.Add(enemy, moved);
            enemy.OnPositionChanged += moved;
            Action handler = null;
            handler = () =>
            {
                // HP0確定後に敵個体を一度だけ数え、最終撃破も勝敗通知より前に反映する。
                simultaneousKills.Record(enemy, actionFrame, enemy.DeathImpact);
                enemyAIEvents.Add(new EnemyAIEvent(EnemyAIEventKind.EnemyDied,enemy));
                if(enemyDamageObservers.TryGetValue(enemy,out var observer)){enemy.OnDamageObserved-=observer;enemyDamageObservers.Remove(enemy);}
                // HP0は占有から既に除外されている。再通知で破壊を繰り返さない。
                enemy.OnPositionChanged -= moved;
                enemy.OnDeath -= handler;
                panelEffects.ApplyOnLeaveEffect(enemy.TerrainState, enemy.GridPosition);
                enemyAI.UnregisterEnemy(enemy);
                CheckVictoryCondition();
            };
            enemyDeathHandlers.Add(enemy, handler);
            enemy.OnDeath += handler;
        }

        /// <summary>呼び出し側が定義した召喚物とAIを、配置順の末尾に登録する。</summary>
        public void AddSummon(Enemy summon, List<IEnemyAIAction> actions)=>AddSummonCore(summon,actions,null);
        internal void AddConfiguredSummon(Enemy summon,EnemyAIRegistration definition)=>AddSummonCore(summon,definition.CopyInitialActions(),definition);
        private void AddSummonCore(Enemy summon,List<IEnemyAIAction> actions,EnemyAIRegistration definition)
        {
            if (summon == null) throw new ArgumentNullException(nameof(summon));
            if (IsGameOver) throw new InvalidOperationException("終了した戦闘には召喚できません");
            if (!summon.IsSummoned || summon.IsDead || !battleField.IsInBounds(summon.GridPosition) || enemies.Contains(summon))
                throw new ArgumentException("生存中で未登録の召喚物をフィールド内に指定してください", nameof(summon));
            // AI登録の検証に失敗した場合は参加者リストへ追加しない。
            RegisterEnemy(summon, actions);
            enemies.Add(summon);
            definition?.Apply(this,summon);
            if (statusFrameParticipants != null)
            { statusFrameParticipants.Add(summon); summon.BeginStatusFrame(!IsPaused); summon.BeginHpFrame(); }
            OnSummonAdded?.Invoke(summon);
        }

        /// <summary>表示層へ戦闘中の追加を通知する。</summary>
        public event Action<Enemy> OnSummonAdded;

        /// <summary>
        /// バトルを更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">前フレームからの経過時間（ミリ秒）</param>
        /// <param name="input">入力プロバイダー（オプショナル）</param>
        private bool logicalFrameTick;
        private List<Enemy> statusFrameParticipants;
        /// <summary>入力による付与も含め、状態異常の有効化・解除境界を一論理フレームにまとめる。</summary>
        private void PauseForTransformation() => Pause(BattlePauseReason.Transformation);
        public void RunLogicalFrame(Action action)
        {
            // 演出最終フレームまではHP/状態更新も停止。次フレームの集計開始前に再開する。
            if(statusFrameParticipants==null&&!Transformations.IsActivating&&(pauseReasons&BattlePauseReason.Transformation)!=0)
                Resume(BattlePauseReason.Transformation);
            if (statusFrameParticipants != null || IsGameOver) { action(); return; }
            var participants = new List<Enemy>(enemies);
            statusFrameParticipants = participants;
            BeginResolution();
            player.BeginHpFrame();
            foreach (var enemy in participants) { enemy.BeginStatusFrame(!IsPaused); enemy.BeginHpFrame(); }
            try
            {
                if (player.BeginParalysisFrame(!IsPaused)) { cardUsage.InterruptCurrentCard(); InterruptBasicAction(); playerMovement.InterruptNormalMove(); }
                player.BeginConfusionFrame(!IsPaused);
                player.BeginBlindnessFrame(!IsPaused);
                action();
            }
            finally
            {
                var committed = participants.ToArray();
                statusFrameParticipants = null;
                // 全員のHPを確定してから通知し、勝敗と保存境界はその後に処理する。
                player.CommitHpFrame();
                player.EndParalysisFrame(!IsPaused);
                player.EndConfusionFrame(!IsPaused);
                player.EndBlindnessFrame(!IsPaused);
                foreach (var enemy in committed) enemy.CommitHpFrame();
                foreach (var enemy in committed)
                {
                    if (player.IsDead) enemy.CancelStatusFrame();
                    else enemy.EndStatusFrame();
                }
                try
                {
                    player.PublishHpFrame();
                    foreach (var enemy in committed) enemy.PublishHpFrame();
                }
                finally { EndResolution(); }
            }
        }
        public void TickFrame(int compatibleMilliseconds, IInputProvider input = null)
        {
            // ヒットストップ中は暗転/変身演出も進めない。ユーザー/カスタム停止は残数も保持する。
            if(IsHitStopped)
            {
                if((pauseReasons&(BattlePauseReason.User|BattlePauseReason.CustomScreen))==0)hitStopRemainingFrames--;
                return;
            }
            // 暗転の時計だけを進める。この終了フレームも通常の戦闘時間へ加算しない。
            if(IsDarkening){AdvanceDarkeningFrame();return;}
            RunLogicalFrame(() => {
                logicalFrameTick = true;
                try {
                    if(!IsGameOver&&(pauseReasons&BattlePauseReason.User)==0)Transformations.AdvancePresentationFrame();
                    Tick(compatibleMilliseconds, input);
                }
                finally { logicalFrameTick = false; }
            });
        }

        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (IsPaused || IsGameOver) return;
            if (statusFrameParticipants == null && (player.HasParalysisState || player.HasPoisonState || player.HasConfusionState || player.HasBlindnessState || panelEffects.HasRecoveryPanels)) { RunLogicalFrame(() => Tick(deltaTimeMs, input)); return; }

            // 追加登録・通知は即時だが、Tick途中の召喚へ過去の経過時間を渡さない。
            // 状態異常とAIは同じ開始時参加者だけを更新する。
            var tickParticipants = enemies.ToArray();
            BeginResolution();
            try
            {
                actionTimeUnits += logicalFrameTick ? 50 : deltaTimeMs * 3L;
                if (logicalFrameTick) actionFrame++;
                AdvanceActionWaits();
                if (IsGameOver || IsPaused) return;
                // カスタムゲージを更新
                if (logicalFrameTick) customGauge.UpdateFrame(); else customGauge.Update(deltaTimeMs);

                // 入力遮断中も自動滑り・移動待機時間は進める。能動入力だけを分離する。
                playerMovement.Tick(deltaTimeMs, input, enemies, logicalFrameTick);
                panelEffects.Tick(deltaTimeMs, player, tickParticipants, logicalFrameTick);

                // 遅延効果を更新（投擲物の着弾など）
                delayedEffects.Update(deltaTimeMs, logicalFrameTick);

                // プレイヤーの無敵時間ティック処理
                if (logicalFrameTick) player.TickInvincibleFrame(); else player.TickInvincible(deltaTimeMs);
                player.TickStagger(deltaTimeMs, logicalFrameTick);
                player.TickParalysis(deltaTimeMs, logicalFrameTick);
                player.TickConfusion(deltaTimeMs, logicalFrameTick);
                player.TickBlindness(deltaTimeMs, logicalFrameTick);
                player.TickPoisons(deltaTimeMs, logicalFrameTick);
                player.TickProtection(deltaTimeMs, logicalFrameTick);
                player.TickPoiseRecovery(deltaTimeMs, logicalFrameTick);

                // 敵の状態異常ティック処理（毒ダメージ等）
                foreach (var enemy in tickParticipants)
                {
                    if (enemy.IsDead) continue;
                    enemy.TickProtection(deltaTimeMs, logicalFrameTick);
                    enemy.TickInvincible(deltaTimeMs, logicalFrameTick);
                    enemy.TickPoiseRecovery(deltaTimeMs, logicalFrameTick);
                    enemy.TickStatusesAndApply(deltaTimeMs, logicalFrameTick);
                }

                // この更新までの通知を一集合として判定し、条件優先は登録resolverが明示する。
                var observed=enemyAIEvents.ToArray();enemyAIEvents.Clear();
                enemyAI.Observe(new EnemyAIObservationFrame(actionFrame,observed),tickParticipants);
                // 敵のAI更新
                enemyAI.Tick(deltaTimeMs, player, battleField, randomProvider, tickParticipants, logicalFrameTick, this);
            }
            finally { EndResolution(); }
        }

        /// <summary>
        /// 一時停止（カスタム画面を開く時）
        /// </summary>
        public void Pause(BattlePauseReason reason = BattlePauseReason.CustomScreen)
        {
            pauseReasons |= reason;
            logger?.LogInfo("バトル一時停止");
        }

        /// <summary>
        /// 再開（カスタム画面を閉じた時）
        /// </summary>
        public void Resume(BattlePauseReason reason = BattlePauseReason.CustomScreen)
        {
            pauseReasons &= ~reason;
            logger?.LogInfo("バトル再開");
        }

        /// <summary>
        /// 先制攻撃を設定する（ダッシュ接敵の先制攻撃権）
        /// カスタムゲージを満タンにして、プレイヤーがすぐに行動できる状態にする
        /// </summary>
        public void SetFirstStrike()
        {
            customGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);
            logger?.LogInfo("[BattleSystem] 先制攻撃: カスタムゲージ満タン");
        }

        /// <summary>
        /// プレイヤーの基本攻撃
        /// </summary>
        public void PlayerBasicAttack(Enemy target)
        { if (!HasPlayerAction) ApplyPlayerBasicAttack(target, ActivePlayerAttack?.NormalPowerPercent ?? 100); }
        private void ApplyPlayerBasicAttack(Enemy target, long powerPercent)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.IsDead) return;

            if (IsGameOver || IsPaused || cardUsage.InFlightCard != null || playerMovement.IsNormalMoving || player.IsActionPrevented) return;
            BeginResolution();
            try
            {
                int damage = (int)Math.Min(int.MaxValue, (decimal)player.AttackPower * powerPercent / 100);
                using (var influence = new AttackInfluence())
                    target.TakeDamage(damage, ActivePlayerAttack?.Element ?? ElementType.None, hitInvincibilityMs: DefaultHitInvincibilityMs, influence: influence);

                logger?.LogInfo($"プレイヤーが{target.Name}に{damage}ダメージを与えた");
                OnPlayerAttack?.Invoke(target, damage);
            }
            finally { EndResolution(); }
        }

        // 暫定通常攻撃: 既存の右向きグリッドで前方射程内で最も近い生存敵1体。同居は配置順を維持する。
        public bool CanPlayerBasicAttack => !IsGameOver && !IsPaused && !player.IsDead &&
            (cardUsage.InFlightCard == null || CanCancelPlayerRecovery) && (!HasPlayerAction || CanCancelPlayerRecovery) &&
            !playerMovement.IsNormalMoving && !player.IsActionPrevented;
        public bool TryPlayerBasicAttackForward(int multiplier = 1)
        {
            if (multiplier < 1) throw new ArgumentOutOfRangeException(nameof(multiplier));
            long percent = (long)(ActivePlayerAttack?.NormalPowerPercent ?? 100) * multiplier;
            return TryPlayerBasicAttackForwardWithPercent(percent);
        }
        public bool TryPlayerBasicAttackForward(bool charged, int fallbackChargeMultiplier = 2)
        {
            if (fallbackChargeMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(fallbackChargeMultiplier));
            var attack = ActivePlayerAttack;
            long percent = attack != null ? (charged ? attack.ChargedPowerPercent : attack.NormalPowerPercent)
                : charged ? 100L * fallbackChargeMultiplier : 100;
            return TryPlayerBasicAttackForwardWithPercent(percent);
        }
        private bool TryPlayerBasicAttackForwardWithPercent(long powerPercent)
        {
            if (HasPlayerAction || !CanPlayerBasicAttack) return false;
            var target=FindBasicAttackTarget();if(target==null)return false;
            ApplyPlayerBasicAttack(target,powerPercent);return true;
        }

        private sealed class ActionWait
        {
            public long Deadline;
            public readonly FrameCompletionSource Completion = new FrameCompletionSource();
            public CancellationTokenRegistration Registration;
        }
        private readonly List<ActionWait> actionWaits = new List<ActionWait>();

        // Unity PlayerLoopのWaitUntilを使わず、期限に達した論理更新内で継続する。
        internal FrameTask WaitForActionAsync(float seconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsGameOver) return FrameTask.FromCanceled(new CancellationToken(true));
            if (seconds <= 0) return FrameTask.CompletedTask;
            // 1フレーム=50単位。float秒の表現誤差のみを除き、入力境界で一度だけ変換する。
            // 旧ms APIと交互に呼ばれても同じ時計で期限を判定する。
            var wait = new ActionWait {
                Deadline = actionTimeUnits + Math.Max(1, (long)Math.Ceiling(seconds * 3000d - 0.0005d))
            };
            actionWaits.Add(wait);
            if (ct.CanBeCanceled) wait.Registration = ct.Register(() => wait.Completion.TrySetCanceled(ct));
            return wait.Completion.Task;
        }
        internal FrameTask WaitForActionFramesAsync(int frames, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsGameOver) return FrameTask.FromCanceled(new CancellationToken(true));
            if (frames <= 0) return FrameTask.CompletedTask;
            var wait = new ActionWait { Deadline = actionTimeUnits + frames * 50L };
            actionWaits.Add(wait);
            if (ct.CanBeCanceled) wait.Registration = ct.Register(() => wait.Completion.TrySetCanceled(ct));
            return wait.Completion.Task;
        }
        private void AdvanceActionWaits()
        {
            // 継続内で追加された次の待機には、このフレームの時間を二重適用しない。
            foreach (var wait in actionWaits.ToArray())
            {
                if (wait.Completion.Task.Status != FrameTaskStatus.Pending || wait.Deadline <= actionTimeUnits || IsGameOver)
                {
                    actionWaits.Remove(wait); wait.Registration.Dispose();
                    if (IsGameOver) wait.Completion.TrySetCanceled(); else wait.Completion.TrySetResult();
                }
            }
        }

        /// <summary>
        /// カスタム画面を開く
        /// </summary>
        public void OpenCustomScreen(IRandomProvider random)
        {
            if (IsGameOver || !customGauge.IsFull)
            {
                logger?.LogWarning("カスタムゲージが満タンではありません");
                return;
            }

            if (customScreen.IsOpen || Transformations.IsActivating || IsDarkening) return;
            // 未使用カードを取得してカスタム画面に渡す
            var remainingCards = cardUsage.GetRemainingCards();
            customScreen.Open(remainingCards, checkpointRandom ? randomProvider : random);
            Pause();
        }

        private void OnTransformationPhaseOpening()
        {
            if (!IsGameOver) player.AdvanceTransformationPhase();
        }

        /// <summary>
        /// カスタム画面で確定ボタンが押された時の処理（アニメーション開始）
        /// </summary>
        private void OnCustomScreenConfirmed(List<Card> selectedCards)
        {
            // 実行中カードはCardUsageが保護し、ここでは待機分だけを置き換える。
            if (!IsGameOver)
            {
                cardUsage.EnqueueCards(selectedCards);
            }

            // ゲージをリセット
            customGauge.Reset();
        }

        /// <summary>
        /// カスタム画面を閉じた時の処理（アニメーション完了後）
        /// </summary>
        private void OnCustomScreenClosed(List<Card> selectedCards)
        {
            // 未確定で閉じる場合もゲージを消費する。通常の確定時リセットを重複通知しない。
            if (customGauge.CurrentGaugeMs != 0) customGauge.Reset();
            // バトル再開
            Resume();
        }

        /// <summary>
        /// 現在のカードを使用
        /// </summary>
        public bool UseCurrentCard()
        {
            return cardUsage.TryUseCurrentCard();
        }

        /// <summary>
        /// プレイヤー死亡時の処理
        /// </summary>
        private void OnPlayerDeath()
        {
            panelEffects.ApplyOnLeaveEffect(player.TerrainState, player.GridPosition);
            RequestResolution();
        }

        /// <summary>
        /// プレイヤーのけぞり時の処理（現在実行中のカード効果を中断）
        /// </summary>
        private void OnPlayerStaggered()
        {
            PlayerStaggerCount++;
            cardUsage.InterruptCurrentCard();
            InterruptBasicAction();
            playerMovement.InterruptNormalMove();
            logger?.LogInfo("プレイヤーがのけぞり - カード効果を中断");
        }

        /// <summary>
        /// 勝利条件チェック（全敵撃破）
        /// </summary>
        private void CheckVictoryCondition()
        {
            RequestResolution();
        }

        // 時間窓は作らず、既存Tickまたは同期ダメージ/反撃の呼出しが戻るまで判定を保留する。
        private void BeginResolution() => resolutionDepth++;

        private void EndResolution()
        {
            resolutionDepth--;
            ResolveBattleEnd();
            // 同一フレームの全ヒットとHP確定を終えてから停止。長さは加算しない。
            if(resolutionDepth==0)
            {
                if(!IsGameOver)hitStopRemainingFrames=Math.Max(hitStopRemainingFrames,pendingHitStopFrames);
                pendingHitStopFrames=0;
            }
        }

        private void RequestResolution()
        {
            if (IsGameOver) return;
            resolutionRequested = true;
            ResolveBattleEnd();
        }

        private void ResolveBattleEnd()
        {
            if (resolutionDepth != 0 || !resolutionRequested || IsGameOver) return;
            resolutionRequested = false;

            // 全体仕様第7節: 同じ処理単位で両者HP0なら敗北が優先。
            if (player.IsDead)
            {
                state = BattleState.Defeat;
            }
            else
            {
                if (!enemies.Exists(enemy => !enemy.IsSummoned) || enemies.Exists(enemy => !enemy.IsSummoned && !enemy.IsDead)) return;
                state = BattleState.Victory;
            }

            // 戦闘限定バフは勝敗通知より前に解除し、同じプレイヤーを使う次戦へ残さない。
            player.SetConcentrateBuff(1f);
            player.SetBarrier(0);
            panelEffects.Clear();
            player.TerrainState.Clear();
            foreach (var enemy in enemies)
            {
                enemy.TerrainState.Clear();
                enemy.ClearBattleParalysis();
            }
            cardUsage.Clear(); // 終了した戦闘の遅延完了が次のキューやUIを更新しない。
            ReleaseBattleParticipants();
            var result = state == BattleState.Defeat ? BattleResult.Defeat : BattleResult.Victory;
            logger?.LogInfo($"バトル終了: {result}");
            OnBattleEnd?.Invoke(result);
        }

        internal void AbandonForCheckpoint()
        {
            if (IsGameOver) return;
            state = BattleState.Defeat;
            cardUsage.Clear();
            ReleaseBattleParticipants();
            OnBattleEnd = null;
        }

        private void ReleaseBattleParticipants()
        {
            enemyAttackTelegraphs.Clear();
            simultaneousKills.Finish();
            hitStopRemainingFrames=pendingHitStopFrames=0;
            darkeningEffects.Clear();
            panelEffects.Clear();
            foreach (var entry in enemyDeathHandlers)
            {
                entry.Key.CancelStatusFrame();
                entry.Key.Guard.Clear();
                entry.Key.ClearProtection();
                entry.Key.SetConcentrateBuff(1f);
                entry.Key.ClearInvincibility();
                entry.Key.ClearPoiseRecovery();
                entry.Key.IsOnGrass = null;
                entry.Key.OnHitStopRequested -= RequestHitStop;
                entry.Key.OnDamageProcessingStarted -= BeginResolution;
                entry.Key.OnDamageProcessingCompleted -= EndResolution;
                entry.Key.OnDeath -= entry.Value;
                enemyAI.UnregisterEnemy(entry.Key);
            }
            enemyDeathHandlers.Clear();
            foreach(var entry in enemyDamageObservers)entry.Key.OnDamageObserved-=entry.Value;
            enemyDamageObservers.Clear();enemyAIEvents.Clear();cardUsage.OnCardUsed-=OnPlayerCardUsedForAI;
            foreach (var entry in enemyPositionHandlers)
                entry.Key.OnPositionChanged -= entry.Value;
            enemyPositionHandlers.Clear();
            player.OnPositionChanged -= OnPlayerPositionChanged;
            battleField.SetOccupancyQuery(null);
            battleField.SetProtectedPosition(null);
            battleField.ClearTemporaryOrigins();
            player.IsOnGrass = null;
            player.BattleElement = null;
            player.Guard.Clear();
            player.ClearBattleStatusResistances();
            player.ClearInvincibility();
            player.ClearStagger();
            player.ResetParalysis();
            player.ResetConfusion();
            player.ResetBlindness();
            InterruptBasicAction();
            player.ClearPoiseRecovery();
            player.SetAura(0);
            player.EndTransformationDefense();
            customScreen.OnOpening -= OnTransformationPhaseOpening;
            Transformations.OnActivationPresentation-=PauseForTransformation;
            Transformations.Dispose();
            player.OnDamageProcessingStarted -= BeginResolution;
            player.OnDamageProcessingCompleted -= EndResolution;
            player.OnHitStopRequested -= RequestHitStop;
            player.OnDeath -= OnPlayerDeath;
            player.OnStaggered -= OnPlayerStaggered;
            playerMovement.InterruptNormalMove();
            customScreen.OnConfirmed -= OnCustomScreenConfirmed;
            customScreen.OnScreenClosed -= OnCustomScreenClosed;
            enemies.RemoveAll(enemy => enemy.IsSummoned);
            battleField.ClearObstacles();
            delayedEffects.Clear();
            OnSummonAdded = null;
        }

        /// <summary>
        /// 生きている敵を取得
        /// </summary>
        public List<Enemy> GetAliveEnemies()
        {
            var aliveEnemies = new List<Enemy>();
            foreach (var enemy in enemies)
            {
                if (!enemy.IsDead)
                {
                    aliveEnemies.Add(enemy);
                }
            }
            return aliveEnemies;
        }

        /// <summary>
        /// プレイヤー攻撃時のイベント
        /// </summary>
        public event Action<Enemy, int> OnPlayerAttack;

        /// <summary>
        /// バトル終了時のイベント
        /// </summary>
        public event Action<BattleResult> OnBattleEnd;
    }

    /// <summary>
    /// バトルの状態
    /// </summary>
    public enum BattleState
    {
        /// <summary>進行中</summary>
        InProgress,

        /// <summary>勝利</summary>
        Victory,

        /// <summary>敗北</summary>
        Defeat
    }

    /// <summary>
    /// バトル結果
    /// </summary>
    public enum BattleResult
    {
        /// <summary>勝利</summary>
        Victory,

        /// <summary>敗北</summary>
        Defeat
    }
    [System.Flags]
    public enum BattlePauseReason { None = 0, CustomScreen = 1, User = 2, Transformation = 4 }
}

namespace Olca.Core.Logic.Battle
{
    public enum DarkeningSide { Player, Opponent }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>起点を延長しない撃破組。組同士を結合せず最大数だけを保持する。</summary>
    internal sealed class SimultaneousKillCounter
    {
        private sealed class Group
        {
            public long FirstFrame;
            public int Count;
            public readonly System.Collections.Generic.HashSet<AttackInfluence> Influences = new System.Collections.Generic.HashSet<AttackInfluence>();
        }
        private readonly System.Collections.Generic.List<Group> groups = new System.Collections.Generic.List<Group>();
        private readonly System.Collections.Generic.HashSet<Enemy> killed = new System.Collections.Generic.HashSet<Enemy>();
        private bool finished;
        public int GraceFrames { get; set; } = 6;
        public int KillCount { get; private set; }
        public int Maximum { get; private set; }
        public void Record(Enemy enemy, long frame, AttackImpact impact)
        {
            if (finished || !killed.Add(enemy)) return;
            KillCount++;
            bool joined = false;
            foreach (var group in groups) {
                bool inWindow = frame - group.FirstFrame <= GraceFrames;
                bool sameLiveHitbox = impact.WasActive && group.Influences.Contains(impact.Influence);
                if (!inWindow && !sameLiveHitbox) continue;
                Add(group, impact); joined = true;
            }
            if (!joined) {
                var group = new Group { FirstFrame = frame };
                groups.Add(group); Add(group, impact);
            }
        }
        private void Add(Group group, AttackImpact impact)
        {
            group.Count++;
            if (impact.WasActive) group.Influences.Add(impact.Influence);
            Maximum = System.Math.Max(Maximum, group.Count);
        }
        public void Finish()
        { finished = true; groups.Clear(); killed.Clear(); }
    }
}

namespace Olca.Core.Logic.Battle
{
    public enum BattleEvaluationGrade { Fair, Good, Great, Excellent }

    /// <summary>確定時の独立値。表示や後続の報酬選択から戦闘時計を再参照しない。</summary>
    public sealed class BattleEvaluationResult
    {
        public long LogicalFrames { get; }
        public int Staggers { get; }
        public int MaximumSimultaneousKills { get; }
        public int InitialEnemyCount { get; }
        public bool IsBoss { get; }
        public int Points { get; }
        public BattleEvaluationGrade Grade { get; }
        internal BattleEvaluationResult(long frames,int staggers,int maximum,int enemies,bool boss,int points,BattleEvaluationGrade grade)
        { LogicalFrames=frames;Staggers=staggers;MaximumSimultaneousKills=maximum;InitialEnemyCount=enemies;IsBoss=boss;Points=points;Grade=grade; }
    }

    /// <summary>§2で許可された調整用の暫定配点。製品確定値や報酬補正を含まない。</summary>
    public sealed class BattleEvaluationRules
    {
        public int TimePoints { get; }
        public int StaggerPoints { get; }
        public int SimultaneousPoints { get; }
        public int FullTimeFrames { get; }
        public int ZeroTimeFrames { get; }
        public int StaggerLoss { get; }
        public int ExcellentThreshold { get; }
        public int GreatThreshold { get; }
        public int GoodThreshold { get; }
        public static BattleEvaluationRules ProvisionalNormal => new BattleEvaluationRules(50,30,20,1800,7200,10,90,75,50);
        public static BattleEvaluationRules ProvisionalBoss => new BattleEvaluationRules(50,30,20,3600,14400,10,90,75,50);
        public BattleEvaluationRules(int timePoints,int staggerPoints,int simultaneousPoints,int fullTimeFrames,int zeroTimeFrames,
            int staggerLoss,int excellentThreshold,int greatThreshold,int goodThreshold)
        {
            if(timePoints<0||staggerPoints<0||simultaneousPoints<0||(long)timePoints+staggerPoints+simultaneousPoints!=100)
                throw new System.ArgumentException("評価配点の合計は100が必要です");
            if(fullTimeFrames<0||zeroTimeFrames<=fullTimeFrames||staggerLoss<=0)
                throw new System.ArgumentException("評価の時間と減点設定が不正です");
            if(goodThreshold<1||greatThreshold<=goodThreshold||excellentThreshold<=greatThreshold||excellentThreshold>100)
                throw new System.ArgumentException("評価境界は100以下の降順が必要です");
            TimePoints=timePoints;StaggerPoints=staggerPoints;SimultaneousPoints=simultaneousPoints;
            FullTimeFrames=fullTimeFrames;ZeroTimeFrames=zeroTimeFrames;StaggerLoss=staggerLoss;
            ExcellentThreshold=excellentThreshold;GreatThreshold=greatThreshold;GoodThreshold=goodThreshold;
        }
        public BattleEvaluationGrade GradeForPoints(int points)
        {
            if(points<0||points>100)throw new System.ArgumentOutOfRangeException(nameof(points));
            return points>=ExcellentThreshold?BattleEvaluationGrade.Excellent:points>=GreatThreshold?BattleEvaluationGrade.Great:
                points>=GoodThreshold?BattleEvaluationGrade.Good:BattleEvaluationGrade.Fair;
        }
        public BattleEvaluationResult Evaluate(long frames,int staggers,int maximum,int enemies,bool boss=false)
        {
            if(frames<0||staggers<0||maximum<0||enemies<=0)throw new System.ArgumentException("評価の計測値が不正です");
            // 時間は上限で先にクランプし、long最大値でも積算をオーバーフローさせない。
            int time=frames<=FullTimeFrames?TimePoints:frames>=ZeroTimeFrames?0:
                (int)((ZeroTimeFrames-frames)*TimePoints/(ZeroTimeFrames-(long)FullTimeFrames));
            int stagger=(int)System.Math.Max(0,StaggerPoints-(long)staggers*StaggerLoss);
            // 単体戦には不可能な複数撃破を要求しない。召喚は開始時の分母へ加算しない。
            int simultaneous=enemies==1?SimultaneousPoints:
                (int)(System.Math.Max(0L,System.Math.Min(maximum,(long)enemies)-1)*SimultaneousPoints/(enemies-1L));
            int points=time+stagger+simultaneous;
            return new BattleEvaluationResult(frames,staggers,maximum,enemies,boss,points,GradeForPoints(points));
        }
        public BattleEvaluationResult Evaluate(BattleSystem battle)
        {
            if(battle==null)throw new System.ArgumentNullException(nameof(battle));
            return Evaluate(battle.LogicalFrame,battle.PlayerStaggerCount,battle.MaximumSimultaneousKills,battle.InitialEvaluationEnemyCount,battle.IsBossEncounter);
        }
    }
}
