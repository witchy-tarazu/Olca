# カード効果システム: 派生効果と硬直時間の実装

## 概要

「カード効果について」の仕様（5章: 派生効果、6章: 硬直時間）に対応するため、以下の機能を実装しました。

---

## 1. 硬直時間システム

### 1.1. CardMasterに追加したフィールド

```csharp
/// <summary>
/// カード使用後の硬直時間（秒）
/// この時間中、プレイヤーは移動や他のカード使用ができない
/// </summary>
public float RecoveryTime { get; set; }

/// <summary>
/// 派生効果中も硬直を継続するか
/// true: 派生効果終了まで硬直が続く（例: 三段突き）
/// false: 派生効果中でも動ける（例: スプレッドガン）
/// </summary>
public bool ExtendRecoveryDuringDerivative { get; set; }
```

### 1.2. Cardクラスへの対応

- `RecoveryTime` プロパティを追加（デフォルト: 0.5秒）
- `ExtendRecoveryDuringDerivative` プロパティを追加（デフォルト: false）
- コンストラクタ、ApplyTuning()、FromMaster()、Clone() すべてに対応

### 1.3. 使用例

```csharp
// CardMasterデータ例1: スプレッドガン
{
    Id: "spread_gun",
    Name: "スプレッドガン",
    Power: 20,
    RecoveryTime: 0.4f,  // 硬直時間: 0.4秒
    ExtendRecoveryDuringDerivative: false  // 爆風中も動ける
}

// CardMasterデータ例2: 三段突き
{
    Id: "triple_slash",
    Name: "三段突き",
    Power: 30,
    RecoveryTime: 0.6f,  // 硬直時間: 0.6秒
    ExtendRecoveryDuringDerivative: true  // 3回目の攻撃まで硬直
}
```

---

## 2. 派生効果システム

### 2.1. 派生効果の種類

**ヒット時派生** と **ダメージ時派生** の2種類があります。

#### ヒット時派生（OnHit）
- **概要**: 命中時に別の攻撃に派生する効果
- **例**: スプレッドガン（着弾地点から前後1マスに爆風が広がる）

#### ダメージ時派生（OnDamage）
- **概要**: ダメージを与えた際に追加効果が発動
- **例**: ポイズンアロー（ダメージを与えた敵に毒状態を付与）

### 2.2. AdditionalEffect クラスの拡張

```csharp
public class AdditionalEffect
{
    public AdditionalEffectType EffectType { get; set; }
    public int Power { get; set; }
    public float Duration { get; set; }
    public int HitCount { get; set; }

    /// <summary>
    /// 派生効果のトリガータイミング
    /// </summary>
    public DerivativeTrigger Trigger { get; set; }
}

public enum DerivativeTrigger
{
    Immediate,  // 即座に発動（派生なし）
    OnHit,      // ヒット時に派生
    OnDamage    // ダメージ時に派生
}
```

### 2.3. AdditionalEffectType の拡張

新たに以下の効果タイプを追加しました：

```csharp
public enum AdditionalEffectType
{
    // ========== 基本的な攻撃効果 ==========
    None,
    Spread,         // スプレッド効果【ヒット時派生】
    Penetration,    // 貫通効果
    MultiHit,       // 多段ヒット効果

    // ========== 状態異常（ダメージ時派生） ==========
    Paralyze,       // 麻痺【ダメージ時派生】
    Poison,         // 毒【ダメージ時派生】
    Confusion,      // 混乱【ダメージ時派生】NEW
    Blind,          // 盲目【ダメージ時派生】NEW

    // ========== パネル変化 ==========
    PanelIce,
    PanelMagma,
    PanelGrass,     // NEW
    PanelCracked,   // NEW
    PanelHole       // NEW
}
```

### 2.4. 使用例

#### 例1: スプレッドガン（ヒット時派生）

```csharp
var spreadModifier = new CardModifier();
spreadModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.Spread,
    Power = 10,  // 爆風のダメージ
    Trigger = DerivativeTrigger.OnHit  // ヒット時に派生
});

var spreadGun = new Card(
    "spread_gun", "スプレッドガン", 2, CardRarity.Normal,
    CardCategory.Attack, ElementType.None, CardSeries.None, 20, "着弾地点から爆風",
    modifier: spreadModifier,
    recoveryTime: 0.4f,
    extendRecoveryDuringDerivative: false  // 爆風中も動ける
);
```

#### 例2: ポイズンアロー（ダメージ時派生）

```csharp
var poisonModifier = new CardModifier();
poisonModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.Poison,
    Power = 5,        // 継続ダメージ（毎秒）
    Duration = 3.0f,  // 3秒間毒状態
    Trigger = DerivativeTrigger.OnDamage  // ダメージ時に派生
});

var poisonArrow = new Card(
    "poison_arrow", "ポイズンアロー", 3, CardRarity.Limited,
    CardCategory.Attack, ElementType.None, CardSeries.None, 15, "毒を付与する矢",
    modifier: poisonModifier,
    recoveryTime: 0.5f
);
```

---

## 3. 現在の実行接続（2026-10-21）

通常入力はCardUsageSystem.TryUseCurrentCardAsyncを通る。ModifiableCardEffectはOnHit/OnDamage等を既存経路で実行し、BattleContextが同じ使用から発生したチェインの前後隙を追跡する。子・孫・並列の最終後隙を待ち、ExtendRecoveryDuringDerivative=trueの元カードだけ、その後に自分の後隙とキャンセル窓を開始する。falseでは既存の独立効果を待たない。未指定の秒値、個別フレーム値、製品既定値は変更しない。

投擲の飛翔と着弾後の派生は別寿命。全体仕様§7/カード効果仕様6.4に従い、発射済み効果を待たず元の後隙終了で次の行動が可能。暗転系は前後隙待機を省略する。通常APIでは発生後の中断は使用済みとし、登録済みの派生を取消さず旧動作の制限だけを解除する。新しい動作は旧コールバックの完了に影響されない。

DelayedEffectSystemは実行・穴落下・Clearで寿命を一度だけ終了し、例外時もfinallyで解放する。時計は既存60Hz論理フレームを使い、ポーズ/カスタムでは進行しない。描画のTime.deltaTimeによる別の硬直タイマーは使用しない。同期Executeは従来の直接効果入口であり、通常入力の行動制限を新設するものではない。

保存形式は変更せずBattleStartへ戻る。旧戦闘の待機/遅延を新戦闘へ持ち越さない。検証結果と仮判断・GUI範囲はPlanning/暫定実装一覧.md末尾を参照。新しい連続技や製品の付与元は今回追加していない。
