using System;
using System.Collections.Generic;
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
    public class ModifiableCardEffect : ICardEffect
    {
        private readonly DamageTargetType baseTargetType;

        public ModifiableCardEffect(DamageTargetType targetType = DamageTargetType.Single)
        {
            this.baseTargetType = targetType;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

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
                e.EffectType == AdditionalEffectType.Spread);

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
        /// 前方Nマスにいる敵を取得
        /// </summary>
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
                    DealDamageToTargets(targets, damage, element);
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
                    foreach (var enemy in aliveEnemies)
                    {
                        if (enemy.GridPosition.Equals(pos))
                        {
                            affectedEnemies.Add(enemy);
                        }
                    }
                }
            }

            DealDamageToTargets(new List<Enemy>(affectedEnemies), damage, element);
        }

        /// <summary>
        /// 貫通する敵リストを取得
        /// </summary>
        private List<Enemy> GetPenetratingTargets(BattleContext context)
        {
            var result = new List<Enemy>();
            var playerPos = context.Player.GridPosition;
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
                case AdditionalEffectType.Poison:
                case AdditionalEffectType.Confusion:
                case AdditionalEffectType.Blind:
                    // 状態異常を適用（実装予定）
                    // TODO: 状態異常システム実装後に追加
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
                // 自分自身を再発動
                // 威力倍率を適用（effect.Powerが威力倍率%）
                int chainPower = context.UsedCard.Power;
                if (effect.Power > 0 && effect.Power != 100)
                {
                    chainPower = (context.UsedCard.Power * effect.Power) / 100;
                }

                // 無限ループを防ぐため、Modifierを空にしたカードを作成
                chainCard = new Domain.Card(
                    context.UsedCard.Id, context.UsedCard.Name, context.UsedCard.Cost, context.UsedCard.Rarity,
                    context.UsedCard.Category, context.UsedCard.Element, context.UsedCard.Series,
                    chainPower, // 威力（必要に応じて調整）
                    context.UsedCard.Description,
                    context.UsedCard.TargetType, context.UsedCard.PanelChangeType,
                    context.UsedCard.MoveDirection, context.UsedCard.MoveDistance,
                    modifier: null, // Modifierを空にして無限ループを防ぐ
                    context.UsedCard.MoveMotionTime,
                    context.UsedCard.AttackMotionTime, context.UsedCard.PenetrationMode,
                    context.UsedCard.RecoveryTime, context.UsedCard.ExtendRecoveryDuringDerivative
                );
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

                // チェインカードのデータベースアクセスも引き継ぐ
                chainContext.CardDatabase = context.CardDatabase;

                // チェインカードの効果を実行
                var chainEffect = CardEffectFactory.CreateEffect(chainCard);
                chainEffect.Execute(chainContext);
            }
        }
    }
}
