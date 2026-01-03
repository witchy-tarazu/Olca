# マスターデータで追加効果を設定する方法

## 概要

カードの追加効果（派生効果）は、**CardMasterで直接設定できます**。別カードを作る必要はありません（複雑な派生効果を除く）。

---

## CardMasterの追加効果フィールド

```csharp
[MemoryTable("cards"), MessagePackObject(true)]
public class CardMaster
{
    // ... 基本フィールド ...

    /// <summary>
    /// 追加効果のタイプ（カンマ区切りで複数指定可能）
    /// 例: "ChainCard", "PanelIceAll", "MultiHit"
    /// </summary>
    public string AdditionalEffectTypes { get; set; }

    /// <summary>
    /// チェイン発動するカードID
    /// - 空文字列またはnull: 自分自身を再発動
    /// - カードID指定: 別カードを発動（爆風カードなど）
    /// </summary>
    public string ChainCardId { get; set; }

    /// <summary>
    /// 追加効果の威力
    /// - ChainCardで自分自身を再発動する場合: 威力倍率（%）
    ///   例: 60 = 60%の威力（元が25なら15ダメージ）
    /// - 状態異常の場合: 継続ダメージ量
    /// </summary>
    public int AdditionalEffectPower { get; set; }

    /// <summary>
    /// 追加効果の持続時間（秒）
    /// </summary>
    public float AdditionalEffectDuration { get; set; }

    /// <summary>
    /// ヒット回数（多段ヒット、チェイン回数など）
    /// </summary>
    public int AdditionalEffectHitCount { get; set; }

    /// <summary>
    /// 派生効果のトリガータイミング
    /// "Immediate", "OnHit", "OnDamage"
    /// </summary>
    public string DerivativeTrigger { get; set; }
}
```

---

## マスターデータ設定例

### パターン1: 自分自身を再発動（同じ威力）

**ダブルソード（25ダメージ × 2回）**

```json
{
    "Id": "double_sword",
    "Name": "ダブルソード",
    "Cost": 3,
    "Category": "Attack",
    "Power": 25,
    "TargetType": "Single",
    "AdditionalEffectTypes": "ChainCard",
    "ChainCardId": "",
    "AdditionalEffectHitCount": 1,
    "DerivativeTrigger": "OnHit"
}
```

**動作:**
- 1回目: 25ダメージ
- 2回目（チェイン）: 25ダメージ
- **合計: 50ダメージ**
- **カード使用回数: 1回のみ**（重要！）

---

### パターン2: 別カードを発動（スプレッドガン）

**メインカード:**
```json
{
    "Id": "spread_gun",
    "Name": "スプレッドガン",
    "Cost": 2,
    "Category": "Attack",
    "Power": 30,
    "TargetType": "Single",
    "AdditionalEffectTypes": "ChainCard",
    "ChainCardId": "explosion_wave",
    "DerivativeTrigger": "OnHit"
}
```

**爆風カード（子カード）:**
```json
{
    "Id": "explosion_wave",
    "Name": "爆風",
    "Cost": 0,
    "Category": "Attack",
    "Power": 10,
    "TargetType": "FrontTile",
    "AdditionalEffectTypes": "PanelIce",
    "DerivativeTrigger": "Immediate"
}
```

**動作:**
- スプレッドガン本体: 30ダメージ（単体）
- 爆風: 10ダメージ（前方1マス範囲）+ 氷パネル化
- **カード使用回数: 1回のみ**（スプレッドガンのみカウント）

---

### パターン3: 複数効果の組み合わせ

**ファイアトルネード（画面全体マグマ化 + 多段ヒット）**

```json
{
    "Id": "fire_tornado",
    "Name": "ファイアトルネード",
    "Cost": 5,
    "Category": "Attack",
    "Power": 40,
    "TargetType": "All",
    "AdditionalEffectTypes": "PanelMagmaAll,MultiHit",
    "AdditionalEffectHitCount": 3,
    "DerivativeTrigger": "Immediate"
}
```

**動作:**
- 画面全体をマグマパネル化
- 全敵に40ダメージ × 3ヒット

---

### パターン4: 全パネル変化のみ

**アイスエイジ（画面全体を氷パネルに）**

```json
{
    "Id": "ice_age",
    "Name": "アイスエイジ",
    "Cost": 4,
    "Category": "Field",
    "Power": 0,
    "TargetType": "All",
    "AdditionalEffectTypes": "PanelIceAll",
    "DerivativeTrigger": "Immediate"
}
```

---

## カード使用回数のカウントについて

### 重要: チェイン効果はカード使用としてカウントされない

**例: ダブルソードを1回使用した場合**

```
「ダブルソード」使用
→ 1回目の攻撃（25ダメージ）
→ チェイン効果で2回目の攻撃（15ダメージ）
```

**カード使用回数:** 1回
**デッキ消費:** 1枚
**統計データ:** 「ダブルソード使用: 1回」

これにより、以下のような仕様に対応できます:
- 「カードを10回使用したらボーナス」← チェインは含まない
- 「特定のカードを3回使ったら」← チェインは含まない
- デッキ切れの判定 ← チェインはデッキを消費しない

---

## 派生効果のトリガータイミング（DerivativeTrigger）

派生効果は、以下の3つのタイミングで発動できます：

### Immediate（即座に発動）
- **発動タイミング**: カード使用直後、ダメージ処理の前
- **用途**: パネル変化全体、多段ヒット、無条件チェイン
- **例**: `"DerivativeTrigger": "Immediate"`

```json
{
    "Id": "ice_age",
    "AdditionalEffectTypes": "PanelIceAll",
    "DerivativeTrigger": "Immediate"
}
```

### OnHit（ヒット時に派生）
- **発動タイミング**: 敵にヒットした後
- **対象**: ヒットした敵のみ
- **用途**: スプレッドガン→爆風、チェイン効果
- **例**: `"DerivativeTrigger": "OnHit"`

```json
{
    "Id": "spread_gun",
    "AdditionalEffectTypes": "ChainCard",
    "ChainCardId": "explosion_wave",
    "DerivativeTrigger": "OnHit"
}
```

### OnDamage（ダメージ時に即座に派生）
- **発動タイミング**: ダメージを与えた直後
- **対象**: 実際にダメージが入った敵のみ（シールドで無効化された場合は発動しない）
- **用途**: 状態異常付与（毒、麻痺など）、パネル変化（敵の位置のみ）
- **例**: `"DerivativeTrigger": "OnDamage"`

```json
{
    "Id": "poison_arrow",
    "AdditionalEffectTypes": "Poison",
    "AdditionalEffectDuration": 5.0,
    "DerivativeTrigger": "OnDamage"
}
```

### AfterActionIfHit（行動終了時・ヒット条件付き）
- **発動タイミング**: カードのすべての処理が完了した後
- **発動条件**: 1体でも敵にヒットしていた場合のみ発動
- **用途**: 全体攻撃後の画面全体パネル変化、条件付き追加効果
- **例**: `"DerivativeTrigger": "AfterActionIfHit"`

```json
{
    "Id": "flame_bomb",
    "Name": "フレイムボム",
    "Power": 20,
    "TargetType": "All",
    "AdditionalEffectTypes": "PanelMagmaAll",
    "DerivativeTrigger": "AfterActionIfHit"
}
```

**動作:**
- 全敵に20ダメージ
- 1体でもヒットしていれば、行動終了時に画面全体をマグマパネルに変化

### AfterActionIfDamaged（行動終了時・ダメージ条件付き）
- **発動タイミング**: カードのすべての処理が完了した後
- **発動条件**: 1体でも敵にダメージを与えていた場合のみ発動
- **対象**: 実際にダメージが入った敵のみ
- **用途**: 多段ヒット後のパネル変化、ダメージ確認後の状態異常付与
- **例**: `"DerivativeTrigger": "AfterActionIfDamaged"`

```json
{
    "Id": "ice_burst",
    "Name": "アイスバースト",
    "Power": 15,
    "TargetType": "All",
    "AdditionalEffectTypes": "MultiHit,PanelIce",
    "AdditionalEffectHitCount": 3,
    "DerivativeTrigger": "Immediate,AfterActionIfDamaged"
}
```

**動作:**
- 全敵に15ダメージ × 3ヒット（Immediate: MultiHit）
- 行動終了時、ダメージが入った敵の位置を氷パネルに変化（AfterActionIfDamaged: PanelIce）

---

## 利用可能な AdditionalEffectType 一覧

### 基本的な攻撃効果
- `Spread`: スプレッド効果（着弾地点から前後1マスに爆風）
- `Penetration`: 貫通効果
- `MultiHit`: 多段ヒット効果

### 状態異常（ダメージ時派生）
- `Paralyze`: 麻痺
- `Poison`: 毒
- `Confusion`: 混乱
- `Blind`: 盲目

### パネル変化（対象のパネルのみ）
- `PanelIce`: 氷パネル
- `PanelMagma`: マグマパネル
- `PanelGrass`: 草パネル
- `PanelCracked`: ひび割れパネル
- `PanelHole`: 穴パネル

### パネル変化（画面全体）
- `PanelIceAll`: 画面全体を氷パネルに
- `PanelMagmaAll`: 画面全体をマグマパネルに
- `PanelGrassAll`: 画面全体を草パネルに
- `PanelCrackedAll`: 画面全体をひび割れパネルに
- `PanelHoleAll`: 画面全体を穴パネルに

### チェイン効果
- `ChainCard`: 別のカードを連鎖発動

---

## まとめ

| ケース | 別カード必要? | 設定例 |
|--------|--------------|--------|
| 自分自身を再発動（同じ威力） | ❌ 不要 | `ChainCardId=""` |
| 単純な追加効果 | ❌ 不要 | `AdditionalEffectTypes="PanelIceAll"` |
| 複雑な派生効果（爆風など） | ✅ 必要 | `ChainCardId="explosion_wave"` + 爆風カード定義 |

**推奨:** シンプルな効果はマスターデータで完結させ、カード数を最小限に保つ。

---

更新日: 2026-01-03
