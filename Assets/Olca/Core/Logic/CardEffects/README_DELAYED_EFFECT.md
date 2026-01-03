# 遅延効果システム設計書

## 概要

投擲式カードなど、カード使用から効果発動までに時間差がある効果を管理するシステム。

## アーキテクチャ

### Core層の責務

- `DelayedEffectSystem`: 遅延効果の登録、時間経過管理、発動タイミング制御
- `DelayedEffect`: 個別の遅延効果データ（残り時間、着弾位置、実行処理）
- パネル状態判定によるキャンセル処理

### Application層の責務

- `BattleController`から`DelayedEffectSystem.Update(deltaTime)`を呼び出し

### Presentation層の責務

- 投擲物の放物線アニメーション表示
- 着弾エフェクトの表示

## クラス設計

### DelayedEffectSystem.cs

```csharp
namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 遅延効果を管理するシステム
    /// </summary>
    public class DelayedEffectSystem
    {
        private readonly List<DelayedEffect> activeEffects = new List<DelayedEffect>();
        private readonly BattleSystem battleSystem;

        public DelayedEffectSystem(BattleSystem battleSystem)
        {
            this.battleSystem = battleSystem;
        }

        /// <summary>
        /// 遅延効果を登録
        /// </summary>
        public void RegisterDelayedEffect(DelayedEffect effect)
        {
            activeEffects.Add(effect);
        }

        /// <summary>
        /// 時間経過を更新し、発動タイミングに達した効果を実行
        /// </summary>
        public void Update(float deltaTime)
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                effect.RemainingTime -= deltaTime;

                if (effect.RemainingTime <= 0)
                {
                    // 着弾位置が穴パネルかチェック
                    if (effect.CancelOnHole && battleSystem.Field.IsHole(effect.TargetPosition))
                    {
                        // 穴パネルに落下 → キャンセル
                        effect.OnCancelled?.Invoke();
                    }
                    else
                    {
                        // 効果を実行
                        effect.Execute();
                    }

                    activeEffects.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 現在アクティブな遅延効果のリストを取得（表示層用）
        /// </summary>
        public IReadOnlyList<DelayedEffect> GetActiveEffects() => activeEffects;

        /// <summary>
        /// すべての遅延効果をクリア（バトル終了時など）
        /// </summary>
        public void Clear()
        {
            activeEffects.Clear();
        }
    }
}
```

### DelayedEffect.cs

```csharp
namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 個別の遅延効果データ
    /// </summary>
    public class DelayedEffect
    {
        /// <summary>残り時間（秒）</summary>
        public float RemainingTime { get; set; }

        /// <summary>着弾位置（絶対座標）</summary>
        public Vector2Int TargetPosition { get; set; }

        /// <summary>穴パネルで無効化されるか</summary>
        public bool CancelOnHole { get; set; }

        /// <summary>着弾時に実行する処理</summary>
        public Action ExecuteAction { get; set; }

        /// <summary>キャンセル時に実行する処理（オプション）</summary>
        public Action OnCancelled { get; set; }

        /// <summary>
        /// 効果を実行
        /// </summary>
        public void Execute()
        {
            ExecuteAction?.Invoke();
        }
    }
}
```

## カードモディファイアの拡張

### AdditionalEffect に遅延関連フィールドを追加

```csharp
public class AdditionalEffect
{
    // 既存のフィールド...

    /// <summary>
    /// 遅延時間（秒）
    /// EffectTypeがDelayedProjectileの場合に使用
    /// </summary>
    public float DelayTime { get; set; }

    /// <summary>
    /// 着弾地点（プレイヤー位置からの相対座標）
    /// EffectTypeがDelayedProjectileの場合に使用
    /// </summary>
    public Vector2Int TargetOffset { get; set; }

    /// <summary>
    /// 穴パネルで無効化されるか
    /// EffectTypeがDelayedProjectileの場合に使用
    /// </summary>
    public bool CancelOnHole { get; set; }
}
```

### AdditionalEffectType に新規追加

```csharp
public enum AdditionalEffectType
{
    // 既存の効果...

    /// <summary>遅延投擲効果（一定時間後に着弾地点で発動）</summary>
    DelayedProjectile
}
```

## ModifiableCardEffect.cs の拡張

### ApplyAdditionalEffect に DelayedProjectile 処理を追加

```csharp
private void ApplyAdditionalEffect(
    BattleContext context,
    AdditionalEffect effect,
    List<Enemy> targets,
    int damage,
    ElementType element)
{
    switch (effect.EffectType)
    {
        // 既存の処理...

        case AdditionalEffectType.DelayedProjectile:
            // 遅延投擲効果を登録
            RegisterDelayedProjectile(context, effect);
            break;

        default:
            DealDamageToTargets(targets, damage, element);
            break;
    }
}
```

### 遅延投擲効果の登録処理

```csharp
/// <summary>
/// 遅延投擲効果を登録
/// </summary>
private void RegisterDelayedProjectile(BattleContext context, AdditionalEffect effect)
{
    // 着弾位置を計算（プレイヤー位置 + 相対オフセット）
    Vector2Int targetPosition = context.GetEffectivePlayerPosition() + effect.TargetOffset;

    var delayedEffect = new DelayedEffect
    {
        RemainingTime = effect.DelayTime,
        TargetPosition = targetPosition,
        CancelOnHole = effect.CancelOnHole,
        ExecuteAction = () =>
        {
            // 着弾時の処理
            ExecuteProjectileLanding(context, effect, targetPosition);
        },
        OnCancelled = () =>
        {
            // キャンセル時のログ（デバッグ用）
#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[DelayedProjectile] 穴パネルに落下してキャンセル: {targetPosition}");
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
    System.Diagnostics.Debug.WriteLine($"[DelayedProjectile] 着弾: {landingPosition}");
#endif

    // 着弾地点の敵を取得
    var landingTargets = context.Battle.Enemies
        .Where(e => !e.IsDead && e.GridPosition == landingPosition)
        .ToList();

    // 直接ダメージ（Power > 0 の場合）
    if (context.UsedCard.Power > 0)
    {
        DealDamageToTargets(landingTargets, context.UsedCard.Power, context.UsedCard.Element);
    }

    // チェイン効果を発動（爆風など）
    if (!string.IsNullOrEmpty(effect.ChainCardId))
    {
        // 着弾地点から派生効果を発動
        ExecuteChainEffectAtPosition(context, effect, landingPosition);
    }
}

/// <summary>
/// 指定位置からチェイン効果を実行
/// </summary>
private void ExecuteChainEffectAtPosition(BattleContext context, AdditionalEffect effect, Vector2Int position)
{
    var chainCard = context.GetCardById(effect.ChainCardId);
    if (chainCard == null) return;

    var chainContext = new BattleContext(context.Battle, chainCard);
    chainContext.TemporaryPlayerPosition = position; // 着弾位置から発動
    chainContext.CardDatabase = context.CardDatabase;

    var chainEffect = CardEffectFactory.CreateEffect(chainCard);
    chainEffect.Execute(chainContext);
}
```

## BattleSystem.cs の拡張

```csharp
public class BattleSystem
{
    // 既存のフィールド...

    /// <summary>遅延効果管理システム</summary>
    public DelayedEffectSystem DelayedEffects { get; private set; }

    public BattleSystem(PlayerCharacter player, List<Enemy> enemies, Deck deck,
        IRandomProvider random = null, IGameLogger logger = null)
    {
        // 既存の初期化...

        DelayedEffects = new DelayedEffectSystem(this);
    }

    public void Update(float deltaTime)
    {
        // 既存の更新処理...

        // 遅延効果の更新
        DelayedEffects.Update(deltaTime);
    }
}
```

## 使用例

### ボムカード（Power=0、爆風のみ）

```csharp
var explosionCard = new Card("explosion", "爆風", 1, CardRarity.Normal,
    CardCategory.Attack, ElementType.Fire, CardSeries.None, 30, "爆風",
    DamageTargetType.FrontBackTile); // 前後1マス攻撃

var modifier = new CardModifier();
modifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.DelayedProjectile,
    DelayTime = 1.5f,
    TargetOffset = new Vector2Int(3, 0), // 前方3マス
    CancelOnHole = true,
    ChainCardId = "explosion",
    Trigger = DerivativeTrigger.Immediate // 即座に登録
});

var bomb = new Card("bomb", "ボム", 1, CardRarity.Normal,
    CardCategory.Attack, ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
    DamageTargetType.None, // 直接ダメージなし
    modifier: modifier);
```

### ホウガンカード（Power>0、直接ダメージあり）

```csharp
var modifier = new CardModifier();
modifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.DelayedProjectile,
    DelayTime = 1.2f,
    TargetOffset = new Vector2Int(3, 0),
    CancelOnHole = true,
    // ChainCardId なし（パネル破壊は別途実装）
    Trigger = DerivativeTrigger.Immediate
});

var hougan = new Card("hougan", "ホウガン", 1, CardRarity.Normal,
    CardCategory.Attack, ElementType.None, CardSeries.None, 50, "砲丸投げ",
    DamageTargetType.Single, // 着弾地点に直接ダメージ
    modifier: modifier);
```

## テスト方針

### EditModeテスト

1. **遅延効果登録テスト**: 投擲カード使用時に遅延効果が正しく登録される
2. **時間経過テスト**: 指定時間後に効果が発動する
3. **穴パネルキャンセルテスト**: 着弾地点が穴パネルの場合に効果がキャンセルされる
4. **直接ダメージテスト**: Power>0の場合、着弾時にダメージが入る
5. **チェイン効果テスト**: 着弾位置から爆風などの派生効果が発動する
6. **早期行動再開テスト**: 投擲モーション終了後、着弾前に次のカードが使用できる

## 実装順序

1. `DelayedEffectSystem.cs` と `DelayedEffect.cs` を作成
2. `AdditionalEffectType.DelayedProjectile` を追加
3. `AdditionalEffect` に遅延関連フィールドを追加
4. `BattleSystem` に `DelayedEffectSystem` を統合
5. `ModifiableCardEffect` に遅延投擲処理を実装
6. `BattleField` に `IsHole(Vector2Int)` メソッドを追加
7. EditModeテストを作成
8. 仕様書を更新（完了）
