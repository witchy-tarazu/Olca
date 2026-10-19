using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 調律効果に対応した汎用ダメージ効果
    /// Modifierの追加効果（スプレッド、貫通、多段ヒット等）を自動的に適用する
    /// </summary>
    /// <summary>
    /// 調律効果に対応した汎用カード効果
    /// Modifierの追加効果（スプレッド、貫通、多段ヒット、パネル変化等）を自動的に適用する
    /// 攻撃カードとフィールド操作カードの両方で使用可能
    /// </summary>
    public class ModifiableCardEffect : ICardEffect, IAsyncCardEffect
    {
        private readonly DamageTargetType baseTargetType;

        public ModifiableCardEffect(DamageTargetType targetType = DamageTargetType.Single)
        {
            this.baseTargetType = targetType;
        }

        /// <summary>
        /// 使用動作だけを待機する。発射済みの投擲・派生効果は独立して進行する。
        /// </summary>
        public async UniTask ExecuteAsync(BattleContext context, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ct.ThrowIfCancellationRequested();
            var card = context.UsedCard;
            bool effectResolved = false;
            try
            {
                if (!card.IsInstantEffect && card.StartupTime > 0)
                    await context.Battle.WaitForActionAsync(card.StartupTime, ct);

                ct.ThrowIfCancellationRequested();
                ExecuteCardEffect(context);
                effectResolved = true;

                if (!card.IsInstantEffect && card.RecoveryTime > 0)
                    await context.Battle.WaitForActionAsync(card.RecoveryTime, ct);
            }
            catch (OperationCanceledException) when (effectResolved)
            {
                // 発動後は使用済み。後隙の中断で適用済み効果を取り消したり再使用させない。
            }
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var card = context.UsedCard;

            // 暗転系カードは前隙・後隙なしで即座に発動
            if (card.IsInstantEffect)
            {
                ExecuteCardEffect(context);
                return;
            }

            // 前隙がある場合は遅延登録
            if (card.StartupTime > 0)
            {
                // 前隙遅延効果を登録
                var startupEffect = new Battle.DelayedEffect(Battle.DelayedEffectType.Startup, (int)(card.StartupTime * 1000f))
                {
                    ExecuteAction = () =>
                    {
                        // 前隙終了後にヒット判定（ダメージ処理）
                        ExecuteCardEffect(context);

                        // 後隙がある場合は遅延登録
                        if (card.RecoveryTime > 0)
                        {
                            var recoveryEffect = new Battle.DelayedEffect(Battle.DelayedEffectType.Recovery, (int)(card.RecoveryTime * 1000f))
                            {
                                ExecuteAction = () =>
                                {
                                    // 後隙終了（行動可能になる）
#if UNITY_EDITOR || DEBUG
                                    System.Diagnostics.Debug.WriteLine($"[ModifiableCardEffect] 後隙終了: {card.Name}");
#endif
                                }
                            };
                            context.Battle.DelayedEffects.RegisterDelayedEffect(recoveryEffect);
                        }
                    }
                };
                context.Battle.DelayedEffects.RegisterDelayedEffect(startupEffect);
            }
            else
            {
                // 前隙がない場合は即座に発動
                ExecuteCardEffect(context);

                // 後隙がある場合は遅延登録
                if (card.RecoveryTime > 0)
                {
                    var recoveryEffect = new Battle.DelayedEffect(Battle.DelayedEffectType.Recovery, (int)(card.RecoveryTime * 1000f))
                    {
                        ExecuteAction = () =>
                        {
                            // 後隙終了（行動可能になる）
#if UNITY_EDITOR || DEBUG
                            System.Diagnostics.Debug.WriteLine($"[ModifiableCardEffect] 後隙終了: {card.Name}");
#endif
                        }
                    };
                    context.Battle.DelayedEffects.RegisterDelayedEffect(recoveryEffect);
                }
            }
        }

        /// <summary>
        /// カード効果の実行（前隙後のヒット判定・ダメージ処理）
        /// </summary>
        private void ExecuteCardEffect(BattleContext context)
        {
            var card = context.UsedCard;
            var modifier = card.Modifier;

            // 最終的な威力を計算
            int finalDamage = card.GetFinalPower();
            ElementType finalElement = card.GetFinalElement();

            // 基本的な対象を取得
            var targets = GetBaseTargets(context, baseTargetType);

            // 効果をトリガータイミングで分類
            var immediateEffects = new List<AdditionalEffect>();
            var onHitEffects = new List<AdditionalEffect>();
            var onDamageEffects = new List<AdditionalEffect>();
            var afterActionIfHitEffects = new List<AdditionalEffect>();
            var afterActionIfDamagedEffects = new List<AdditionalEffect>();

            foreach (var effect in modifier.AdditionalEffects)
            {
                switch (effect.Trigger)
                {
                    case DerivativeTrigger.Immediate:
                        immediateEffects.Add(effect);
                        break;
                    case DerivativeTrigger.OnHit:
                        onHitEffects.Add(effect);
                        break;
                    case DerivativeTrigger.OnDamage:
                        onDamageEffects.Add(effect);
                        break;
                    case DerivativeTrigger.AfterActionIfHit:
                        afterActionIfHitEffects.Add(effect);
                        break;
                    case DerivativeTrigger.AfterActionIfDamaged:
                        afterActionIfDamagedEffects.Add(effect);
                        break;
                }
            }

            // Immediate効果の中にダメージ処理を含むものがあるかチェック
            bool damageHandledByImmediate = immediateEffects.Exists(e =>
                e.EffectType == AdditionalEffectType.MultiHit ||
                e.EffectType == AdditionalEffectType.Penetration ||
                e.EffectType == AdditionalEffectType.Spread ||
                e.EffectType == AdditionalEffectType.DelayedProjectile); // 遅延投擲は着弾時にダメージ処理

            // 1. Immediate効果を先に実行（パネル変化など）
            foreach (var effect in immediateEffects)
            {
                ApplyAdditionalEffect(context, effect, ref targets, finalDamage, finalElement);
            }

            // 2. ダメージ処理
            List<Enemy> hitTargets = new List<Enemy>();
            List<Enemy> damagedTargets = new List<Enemy>();

            bool needsHitTracking = onHitEffects.Count > 0 || onDamageEffects.Count > 0
                || afterActionIfHitEffects.Count > 0 || afterActionIfDamagedEffects.Count > 0;

            // Immediate効果でダメージ処理が完了している場合は、通常のダメージ処理をスキップ
            if (!damageHandledByImmediate)
            {
                // 移動・死亡通知で対象位置が変わる前に、今回当たる草むらを確定する。
                var burningPanels = card.Category == CardCategory.Attack && finalElement == ElementType.Fire
                    ? GetBaseHitPanels(context, targets).Where(p => context.Field.GetPanelType(p) == PanelType.Grass).ToArray()
                    : new Vector2Int[0];
                if (needsHitTracking)
                {
                    // ヒット判定とダメージを個別に記録
                    foreach (var target in targets)
                    {
                        if (target.IsDead) continue;

                        hitTargets.Add(target);
                        int hpBefore = target.CurrentHp;
                        target.TakeDamage(finalDamage, finalElement);

                        // 実際にダメージが入った場合
                        if (target.CurrentHp < hpBefore || target.IsDead)
                        {
                            damagedTargets.Add(target);
                        }
                    }
                }
                else
                {
                    // 派生効果がない場合は通常のダメージ処理
                    DealDamageToTargets(targets, finalDamage, finalElement);
                }
                // 同一マスの全員へ地形補正を判定した後に燃やす。耐性100%でも消費する。
                foreach (var panel in burningPanels)
                    if (context.Field.GetPanelType(panel) == PanelType.Grass)
                        context.Field.ChangePanelType(panel, PanelType.Normal);
            }
            else if (needsHitTracking)
            {
                // ダメージ処理は既に完了しているが、ヒット/ダメージ追跡が必要
                // Immediate効果で処理された敵をhitTargets/damagedTargetsに追加
                foreach (var target in targets)
                {
                    if (target.IsDead || target.CurrentHp < target.MaxHp)
                    {
                        hitTargets.Add(target);
                        damagedTargets.Add(target);
                    }
                }
            }

            // 3. OnHit効果を実行（ヒットした敵に対して）
            foreach (var effect in onHitEffects)
            {
                ApplyDerivativeEffect(context, effect, hitTargets, finalDamage, finalElement);
            }

            // 4. OnDamage効果を実行（ダメージを与えた敵に対して）
            foreach (var effect in onDamageEffects)
            {
                ApplyDerivativeEffect(context, effect, damagedTargets, finalDamage, finalElement);
            }

            // 5. 行動終了時：ヒット条件を満たしていれば発動
            if (afterActionIfHitEffects.Count > 0 && hitTargets.Count > 0)
            {
                foreach (var effect in afterActionIfHitEffects)
                {
                    ApplyDerivativeEffect(context, effect, hitTargets, finalDamage, finalElement);
                }
            }

            // 6. 行動終了時：ダメージ条件を満たしていれば発動
            if (afterActionIfDamagedEffects.Count > 0 && damagedTargets.Count > 0)
            {
                foreach (var effect in afterActionIfDamagedEffects)
                {
                    ApplyDerivativeEffect(context, effect, damagedTargets, finalDamage, finalElement);
                }
            }
        }

        /// <summary>
        /// 基本的な対象リストを取得
        /// </summary>
        private List<Enemy> GetBaseTargets(BattleContext context, DamageTargetType targetType)
        {
            var aliveEnemies = context.Battle.GetAliveEnemies();
            var targets = new List<Enemy>();

            if (aliveEnemies.Count == 0) return targets;

            switch (targetType)
            {
                case DamageTargetType.Single:
                    targets.Add(aliveEnemies[0]);
                    break;

                case DamageTargetType.All:
                    targets.AddRange(aliveEnemies);
                    break;

                case DamageTargetType.FrontTile:
                    targets.AddRange(GetEnemiesInTileRange(context, 1));
                    break;

                case DamageTargetType.FrontTwoTiles:
                    targets.AddRange(GetEnemiesInTileRange(context, 2));
                    break;

                case DamageTargetType.FrontThreeTiles:
                    targets.AddRange(GetEnemiesInTileRange(context, 3));
                    break;
            }

            return targets;
        }

        /// <summary>
        /// 基本攻撃の命中パネルを取得。前方マス攻撃は空マスも含める。
        /// </summary>
        private IEnumerable<Vector2Int> GetBaseHitPanels(BattleContext context, List<Enemy> targets)
        {
            int range = baseTargetType == DamageTargetType.FrontTile ? 1 :
                baseTargetType == DamageTargetType.FrontTwoTiles ? 2 :
                baseTargetType == DamageTargetType.FrontThreeTiles ? 3 : 0;
            if (range > 0)
            {
                var origin = context.GetEffectivePlayerPosition();
                for (int offset = 1; offset <= range; offset++)
                {
                    var position = new Vector2Int(origin.X + offset, origin.Y);
                    if (context.Field.IsInBounds(position)) yield return position;
                }
            }
            else
                foreach (var position in targets.Select(t => t.GridPosition).Distinct())
                    if (context.Field.IsInBounds(position)) yield return position;
        }

        private List<Enemy> GetEnemiesInTileRange(BattleContext context, int range)
        {
            var result = new List<Enemy>();
            var playerPos = context.GetEffectivePlayerPosition();  // TemporaryPlayerPositionを考慮
            var aliveEnemies = context.Battle.GetAliveEnemies();

            for (int i = 1; i <= range; i++)
            {
                Vector2Int targetPos = new Vector2Int(playerPos.X + i, playerPos.Y);
                foreach (var enemy in aliveEnemies)
                {
                    if (enemy.GridPosition.Equals(targetPos))
                    {
                        result.Add(enemy);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 追加効果を適用
        /// </summary>
        private void ApplyAdditionalEffect(
            BattleContext context,
            AdditionalEffect effect,
            ref List<Enemy> targets,
            int damage,
            ElementType element)
        {
            switch (effect.EffectType)
            {
                case AdditionalEffectType.Spread:
                    // スプレッド効果: 着弾地点から前後1マスに拡散
                    ApplySpreadEffect(context, targets, damage, element);
                    break;

                case AdditionalEffectType.Penetration:
                    // 貫通効果: プレイヤーより前方の敵全てを対象に
                    targets = GetPenetratingTargets(context);
                    ApplyPenetratingDamage(context, targets, damage, element);
                    break;

                case AdditionalEffectType.MultiHit:
                    // 多段ヒット効果
                    int hitCount = effect.HitCount > 0 ? effect.HitCount : 3;
                    DealMultiHitDamage(targets, damage, element, hitCount);
                    break;

                case AdditionalEffectType.PanelIce:
                    // パネル変化: Ice
                    ApplyPanelChange(context, targets, PanelType.Ice);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelMagma:
                    // パネル変化: Magma
                    ApplyPanelChange(context, targets, PanelType.Magma);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelGrass:
                    // パネル変化: Grass
                    ApplyPanelChange(context, targets, PanelType.Grass);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelCracked:
                    // パネル変化: Cracked
                    ApplyPanelChange(context, targets, PanelType.Cracked);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelHole:
                    // パネル変化: Hole
                    ApplyPanelChange(context, targets, PanelType.Hole);
                    DealDamageToTargets(targets, damage, element);
                    break;

                // ========== 全パネル変化 ==========
                case AdditionalEffectType.PanelIceAll:
                    ApplyPanelChangeAll(context, PanelType.Ice);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelMagmaAll:
                    ApplyPanelChangeAll(context, PanelType.Magma);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelGrassAll:
                    ApplyPanelChangeAll(context, PanelType.Grass);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelCrackedAll:
                    ApplyPanelChangeAll(context, PanelType.Cracked);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelHoleAll:
                    ApplyPanelChangeAll(context, PanelType.Hole);
                    DealDamageToTargets(targets, damage, element);
                    break;

                // ========== 遅延効果 ==========
                case AdditionalEffectType.DelayedProjectile:
                    // 遅延投擲効果: 即座に登録のみ（実行は DelayedEffectSystem が担当）
                    RegisterDelayedProjectile(context, effect);
                    // 投擲物自体のダメージは着弾時に処理されるため、ここではダメージなし
                    break;

                default:
                    DealDamageToTargets(targets, damage, element);
                    break;
            }
        }

        /// <summary>
        /// スプレッド効果を適用
        /// </summary>
        private void ApplySpreadEffect(BattleContext context, List<Enemy> baseTargets, int damage, ElementType element)
        {
            if (baseTargets.Count == 0) return;

            var affectedEnemies = new HashSet<Enemy>();
            var hitPanels = new HashSet<Vector2Int>();
            var aliveEnemies = context.Battle.GetAliveEnemies();

            // 各ターゲットの着弾地点から前後1マスに拡散
            foreach (var target in baseTargets)
            {
                Vector2Int center = target.GridPosition;
                var spreadPositions = new[]
                {
                    center,
                    new Vector2Int(center.X + 1, center.Y),
                    new Vector2Int(center.X - 1, center.Y)
                };

                foreach (var pos in spreadPositions)
                {
                    if (context.Field.IsInBounds(pos)) hitPanels.Add(pos);
                    foreach (var enemy in aliveEnemies)
                    {
                        if (enemy.GridPosition.Equals(pos))
                        {
                            affectedEnemies.Add(enemy);
                        }
                    }
                }
            }

            var burningPanels = element == ElementType.Fire
                ? hitPanels.Where(p => context.Field.GetPanelType(p) == PanelType.Grass).ToArray()
                : new Vector2Int[0];
            DealDamageToTargets(new List<Enemy>(affectedEnemies), damage, element);
            // 拡散の全対象へ補正を判定した後、敵がいない命中マスも含めて燃やす。
            foreach (var panel in burningPanels)
                if (context.Field.GetPanelType(panel) == PanelType.Grass)
                    context.Field.ChangePanelType(panel, PanelType.Normal);
        }

        private void ApplyPenetratingDamage(BattleContext context, List<Enemy> targets, int damage, ElementType element)
        {
            var burningPanels = new List<Vector2Int>();
            var origin = context.GetEffectivePlayerPosition();
            if (element == ElementType.Fire)
            {
                for (int x = origin.X + 1; x < BattleField.GRID_WIDTH; x++)
                {
                    var panel = new Vector2Int(x, origin.Y);
                    if (context.Field.IsInBounds(panel) && context.Field.GetPanelType(panel) == PanelType.Grass)
                        burningPanels.Add(panel);
                }
            }
            DealDamageToTargets(targets, damage, element);
            // 通過する全対象に地形補正を判定した後、空マスも含めて燃焼する。
            foreach (var panel in burningPanels)
                if (context.Field.GetPanelType(panel) == PanelType.Grass)
                    context.Field.ChangePanelType(panel, PanelType.Normal);
        }

        /// <summary>
        /// 貫通する敵リストを取得
        /// </summary>
        private List<Enemy> GetPenetratingTargets(BattleContext context)
        {
            var result = new List<Enemy>();
            var playerPos = context.GetEffectivePlayerPosition();
            var aliveEnemies = context.Battle.GetAliveEnemies();

            foreach (var enemy in aliveEnemies)
            {
                if (enemy.GridPosition.X > playerPos.X && enemy.GridPosition.Y == playerPos.Y)
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// 対象リストにダメージを与える
        /// </summary>
        private void DealDamageToTargets(List<Enemy> targets, int damage, ElementType element)
        {
            foreach (var target in targets)
            {
                if (!target.IsDead)
                {
                    target.TakeDamage(damage, element);
                }
            }
        }

        /// <summary>
        /// 多段ヒットダメージを与える
        /// </summary>
        private void DealMultiHitDamage(List<Enemy> targets, int damagePerHit, ElementType element, int hitCount)
        {
            foreach (var target in targets)
            {
                for (int i = 0; i < hitCount; i++)
                {
                    if (!target.IsDead)
                    {
                        target.TakeDamage(damagePerHit, element);
                    }
                }
            }
        }

        /// <summary>
        /// パネル変化を適用（対象の位置のみ）
        /// </summary>
        private void ApplyPanelChange(BattleContext context, List<Enemy> targets, PanelType panelType)
        {
            foreach (var target in targets)
            {
                context.Field.ChangePanelType(target.GridPosition, panelType);
            }
        }

        /// <summary>
        /// 画面全体のパネルを変化
        /// </summary>
        private void ApplyPanelChangeAll(BattleContext context, PanelType panelType)
        {
            // BattleFieldの全グリッドを変更
            for (int x = 0; x < Battle.BattleField.GRID_WIDTH; x++)
            {
                for (int y = 0; y < Battle.BattleField.GRID_HEIGHT; y++)
                {
                    context.Field.ChangePanelType(new Vector2Int(x, y), panelType);
                }
            }
        }

        /// <summary>
        /// 派生効果を適用（OnHit/OnDamage用）
        /// ダメージ処理は既に完了しているため、効果のみを適用
        /// </summary>
        private void ApplyDerivativeEffect(
            BattleContext context,
            AdditionalEffect effect,
            List<Enemy> targets,
            int damage,
            ElementType element)
        {
#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[ApplyDerivativeEffect] Type={effect.EffectType}, targets.Count={targets.Count}");
#endif
            switch (effect.EffectType)
            {
                case AdditionalEffectType.ChainCard:
                    // チェイン効果: ヒットした敵に対して別カードを発動
#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ApplyDerivativeEffect] ChainCard効果実行");
#endif
                    ExecuteChainEffect(context, effect, targets);
                    break;

                case AdditionalEffectType.Spread:
                    // スプレッド効果: 着弾地点から前後1マスに拡散
                    ApplySpreadEffect(context, targets, damage, element);
                    break;

                case AdditionalEffectType.Paralyze:
                    // 麻痺: 行動不能状態を付与
                    foreach (var t in targets)
                    {
                        if (!t.IsDead)
                        {
                            int durationMs = effect.Duration > 0 ? (int)(effect.Duration * 1000) : 3000;
                            t.ApplyStatus(new Domain.StatusEffect(Domain.StatusEffectType.Paralyze, durationMs));
                        }
                    }
                    break;

                case AdditionalEffectType.Poison:
                    // 毒: 継続ダメージを付与
                    foreach (var t in targets)
                    {
                        if (!t.IsDead)
                        {
                            int durationMs = effect.Duration > 0 ? (int)(effect.Duration * 1000) : 5000;
                            int damagePerTick = effect.Power > 0 ? effect.Power : 5;
                            t.ApplyStatus(new Domain.StatusEffect(Domain.StatusEffectType.Poison, durationMs, damagePerTick));
                        }
                    }
                    break;

                case AdditionalEffectType.Confusion:
                    // 混乱: 操作反転状態を付与
                    foreach (var t in targets)
                    {
                        if (!t.IsDead)
                        {
                            int durationMs = effect.Duration > 0 ? (int)(effect.Duration * 1000) : 3000;
                            t.ApplyStatus(new Domain.StatusEffect(Domain.StatusEffectType.Confusion, durationMs));
                        }
                    }
                    break;

                case AdditionalEffectType.Blind:
                    // 盲目: 命中率低下状態を付与
                    foreach (var t in targets)
                    {
                        if (!t.IsDead)
                        {
                            int durationMs = effect.Duration > 0 ? (int)(effect.Duration * 1000) : 3000;
                            t.ApplyStatus(new Domain.StatusEffect(Domain.StatusEffectType.Blind, durationMs));
                        }
                    }
                    break;

                case AdditionalEffectType.PanelIce:
                    ApplyPanelChange(context, targets, PanelType.Ice);
                    break;

                case AdditionalEffectType.PanelMagma:
                    ApplyPanelChange(context, targets, PanelType.Magma);
                    break;

                case AdditionalEffectType.PanelGrass:
                    ApplyPanelChange(context, targets, PanelType.Grass);
                    break;

                case AdditionalEffectType.PanelCracked:
                    ApplyPanelChange(context, targets, PanelType.Cracked);
                    break;

                case AdditionalEffectType.PanelHole:
                    ApplyPanelChange(context, targets, PanelType.Hole);
                    break;

                // ========== 全パネル変化 ==========
                case AdditionalEffectType.PanelIceAll:
                    ApplyPanelChangeAll(context, PanelType.Ice);
                    break;

                case AdditionalEffectType.PanelMagmaAll:
                    ApplyPanelChangeAll(context, PanelType.Magma);
                    break;

                case AdditionalEffectType.PanelGrassAll:
                    ApplyPanelChangeAll(context, PanelType.Grass);
                    break;

                case AdditionalEffectType.PanelCrackedAll:
                    ApplyPanelChangeAll(context, PanelType.Cracked);
                    break;

                case AdditionalEffectType.PanelHoleAll:
                    ApplyPanelChangeAll(context, PanelType.Hole);
                    break;
            }
        }

        /// <summary>
        /// チェイン効果を実行（別のカードを連鎖発動）
        /// </summary>
        private void ExecuteChainEffect(BattleContext context, AdditionalEffect effect, List<Enemy> hitTargets)
        {
#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] 開始 hitTargets={hitTargets.Count}, ChainId={effect.ChainCardId}, Origin={effect.EffectOrigin}");
#endif
            // チェイン発動するカードIDを取得
            string chainCardId = effect.ChainCardId;
            bool isSelfChain = string.IsNullOrEmpty(chainCardId);

            Domain.Card chainCard;

            if (isSelfChain)
            {
                // 段階などの定義を保持し、Modifier除去と基本威力倍率の既存動作を維持する。
                chainCard = context.UsedCard.CreateSelfChainCopy(effect.Power);
            }
            else
            {
                // 別カードを発動
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] GetCardById('{chainCardId}')");
#endif
                chainCard = context.GetCardById(chainCardId);
                if (chainCard == null)
                {
#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] カード未発見: {chainCardId}");
#endif
                    // カードが見つからない場合はスキップ
                    return;
                }
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] カード取得成功: {chainCard.Name}");
#endif
            }

            // 発動位置を決定
            bool useHitPosition = effect.EffectOrigin == ChainEffectOrigin.HitPosition;
#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] useHitPosition={useHitPosition}");
#endif

            if (useHitPosition)
            {
                // ヒット位置から発動: ヒットした各敵の位置を基準に発動
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] ヒット位置モード開始");
#endif
                foreach (var target in hitTargets)
                {
                    if (target.IsDead)
                    {
#if UNITY_EDITOR || DEBUG
                        System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] {target.Name}は死亡済みスキップ");
#endif
                        continue;
                    }

#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] {target.Name}位置{target.GridPosition}からチェイン");
#endif
                    // チェインカード用の新しいBattleContextを作成
                    var chainContext = new BattleContext(context.Battle, chainCard);

                    // ヒットした敵の位置を一時的なプレイヤー位置として設定
                    chainContext.TemporaryPlayerPosition = target.GridPosition;

                    // チェインカードのデータベースアクセスも引き継ぐ
                    chainContext.CardDatabase = context.CardDatabase;

                    // チェインカードの効果を実行
                    var chainEffect = CardEffectFactory.CreateEffect(chainCard);
#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] チェイン効果Execute開始");
#endif
                    chainEffect.Execute(chainContext);
#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffect] チェイン効果Execute完了");
#endif
                }
            }
            else
            {
                // プレイヤー位置から発動: 元の位置を基準に発動
                // 自分自身を再発動する場合はhitTargetsの数だけ実行しない（1回だけ実行）
                var chainContext = new BattleContext(context.Battle, chainCard);

                // 多段の派生でも、実プレイヤーではなく直前のカードの発動位置を継承する。
                chainContext.TemporaryPlayerPosition = context.GetEffectivePlayerPosition();

                // チェインカードのデータベースアクセスも引き継ぐ
                chainContext.CardDatabase = context.CardDatabase;

                // チェインカードの効果を実行
                var chainEffect = CardEffectFactory.CreateEffect(chainCard);
                chainEffect.Execute(chainContext);
            }
        }

        /// <summary>
        /// 遅延投擲効果を登録
        /// </summary>
        private void RegisterDelayedProjectile(BattleContext context, AdditionalEffect effect)
        {
            // 着弾位置を計算（プレイヤー位置 + 相対オフセット）
            Vector2Int playerPos = context.GetEffectivePlayerPosition();
            Vector2Int targetPosition = new Vector2Int(
                playerPos.X + effect.TargetOffset.X,
                playerPos.Y + effect.TargetOffset.Y
            );

#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[RegisterDelayedProjectile] 投擲登録: 着弾位置={targetPosition}, 遅延={effect.DelayTime}秒");
#endif

            var delayedEffect = new Battle.DelayedEffect((int)(effect.DelayTime * 1000f), targetPosition, effect.CancelOnHole)
            {
                ExecuteAction = () =>
                {
                    // 着弾時の処理
                    ExecuteProjectileLanding(context, effect, targetPosition);
                },
                OnCancelled = () =>
                {
                    // キャンセル時のログ（デバッグ用）
#if UNITY_EDITOR || DEBUG
                    System.Diagnostics.Debug.WriteLine($"[RegisterDelayedProjectile] 穴パネルに落下してキャンセル: {targetPosition}");
#endif
                }
            };

            context.Battle.DelayedEffects.RegisterDelayedEffect(delayedEffect);
        }

        /// <summary>
        /// 投擲物着弾時の処理
        /// </summary>
        private void ExecuteProjectileLanding(BattleContext context, AdditionalEffect effect, Vector2Int landingPosition)
        {
#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[ExecuteProjectileLanding] 着弾: {landingPosition}, Power={context.UsedCard.Power}");
#endif

            // 着弾地点の敵を取得
            var landingTargets = context.Battle.Enemies
                .Where(e => !e.IsDead && e.GridPosition == landingPosition)
                .ToList();

            // 直接ダメージ（Power > 0 の場合）
            if (context.UsedCard.Power > 0)
            {
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteProjectileLanding] 直接ダメージ: {context.UsedCard.Power}, ターゲット数={landingTargets.Count}");
#endif
                var element = context.UsedCard.GetFinalElement();
                bool burnsGrass = element == ElementType.Fire && context.Field.IsInBounds(landingPosition)
                    && context.Field.GetPanelType(landingPosition) == PanelType.Grass;
                DealDamageToTargets(landingTargets, context.UsedCard.Power, element);
                // 着弾時の全対象を判定してから燃焼。直接攻撃は空の着弾マスにも当たる。
                if (burnsGrass && context.Field.GetPanelType(landingPosition) == PanelType.Grass)
                    context.Field.ChangePanelType(landingPosition, PanelType.Normal);
            }

            // チェイン効果を発動（爆風など）
            if (!string.IsNullOrEmpty(effect.ChainCardId))
            {
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteProjectileLanding] チェイン効果実行: {effect.ChainCardId}");
#endif
                ExecuteChainEffectAtPosition(context, effect, landingPosition);
            }
        }

        /// <summary>
        /// 指定位置からチェイン効果を実行
        /// </summary>
        private void ExecuteChainEffectAtPosition(BattleContext context, AdditionalEffect effect, Vector2Int position)
        {
            var chainCard = context.GetCardById(effect.ChainCardId);
            if (chainCard == null)
            {
#if UNITY_EDITOR || DEBUG
                System.Diagnostics.Debug.WriteLine($"[ExecuteChainEffectAtPosition] カード未発見: {effect.ChainCardId}");
#endif
                return;
            }

            var chainContext = new BattleContext(context.Battle, chainCard);
            chainContext.TemporaryPlayerPosition = position; // 着弾位置から発動
            chainContext.CardDatabase = context.CardDatabase;

            var chainEffect = CardEffectFactory.CreateEffect(chainCard);
            chainEffect.Execute(chainContext);
        }
    }
}
