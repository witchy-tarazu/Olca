# カード関連 Enum 定義

> 2026-10-19追記: 最新の呼称は[ゲーム全体仕様](ゲーム全体仕様.md)を参照。無印〜★★★はカードレベル、そのまとまりはシリーズ、スタンダード/リミテッド/アルティメットはレギュレーション。旧ノーマルはスタンダードへ改名。以下の既存enum識別子は未移行のため、仕様上の呼称とは区別する。

> このドキュメントはカードマスターデータで使用するenumの値一覧です。
> 値を追加する場合は、対応するセクションに箇条書きで追記してください。
>
> **記述形式:**
> - `値名` — 説明

---

## CardCategory（大カテゴリ）

カードの基本的な役割分類。`CardMaster.Category` に使用。

- `Attack` — 攻撃系。敵にダメージを与えるカード
- `Defense` — 防御・回復系。HPを回復したりバリアを張るカード
- `Field` — フィールド操作系。パネルを変化させるカード
- `Support` — 補助・強化系。調律素材や集中など補助効果を持つカード
- `StatusAilment` — 状態異常系
- `Tuning` — 旧案の調律素材専用区分（最新仕様では採用しない）。調律素材可否はカード別に設定し、直接使用も可能とする。既存コードの識別子整理は別途行う。

---

## CardSubCategory（小カテゴリ）

大カテゴリ内でのさらに細かい種別。`CardMaster.SubCategory` に使用。
カードIDの命名規則に依存しない型安全な効果判定のために使用する。

- `None` — 小カテゴリ指定なし（大カテゴリのデフォルト動作）
- `Heal` — HP回復。`Defense` カテゴリで使用
- `Barrier` — ダメージ吸収バリアを付与。`Defense` カテゴリで使用
- `Concentrate` — 集中。次の攻撃の威力倍率をアップ。`Support` カテゴリで使用

---

## CardRarity（レアリティ）

デッキ構築時の枚数制限に影響する。`CardMaster.Rarity` に使用。

- `Normal` — ノーマル。同名カードをデッキに4枚まで入れられる
- `Limited` — リミテッド。同名カードはデッキに1枚まで、かつ合計5枚まで
- `Ultimate` — アルティメット。同名カードはデッキに1枚まで、かつ合計1枚のみ

---

## ElementType（属性）

カードの属性。相性が良い属性に対してダメージが2倍になる。`CardMaster.Element` に使用。

- `None` — 無属性
- `Fire` — 火属性
- `Water` — 水属性
- `Wind` — 風属性
- `Earth` — 土属性

> **属性相性表:** TODO

---

## SeriesType（系統）

同系統のカードを連続して選択するとコストボーナスが発生する。`CardMaster.Series` に使用。

- `None` — 系統なし
- `Sword` — ソード系
- `Bomb` — ボム系
- `Canon` — キャノン系

---

## DamageTargetType（攻撃対象タイプ）

攻撃がどの範囲・対象に当たるかを定義する。`CardBehaviorMaster.TargetType` に使用。
射程マス数は `CardBehaviorMaster.Range` で別途指定する。

- `Single` — 射出系。射程内で最初に当たった敵1体のみに命中する
- `Penetrating` — 貫通射出系。射程内の経路上にいる全敵に命中する
- `FrontTiles` — 直接届く系。前方Nマス（`Range` で指定）の全マス・全敵に同時に当たる
- `All` — 全体。フィールド上のすべての敵に当たる

---

## ProjectileType（弾道タイプ）

弾の飛び方を定義する。Presentation層のアニメーションにも影響する。`CardBehaviorMaster.ProjectileType` に使用。

- `None` — 弾のビジュアルなし。ロングソード系など即時・近接の攻撃に使用
- `Straight` — 直線飛翔。キャノン・アロー系など直線に飛ぶ弾に使用
- `Parabola` — 放物線。ボム系など弧を描いて着弾する弾に使用

---

## MoveDirection（移動方向）

一時移動カードの移動方向。`CardBehaviorMaster.MoveDirection` に使用。

- `Right` — 右方向
- `Left` — 左方向
- `Up` — 上方向
- `Down` — 下方向

---

## MovePenetrationMode（移動貫通モード）

一時移動カードが移動中に障害物・敵と衝突したときの挙動。`CardBehaviorMaster.PenetrationMode` に使用。

- `Blocking` — 障害物・敵の手前で止まる

---

## AdditionalEffectType（追加効果タイプ）

`CardEffectMaster.EffectType` に使用。1レコード1効果。

### 攻撃・ダメージ

- `Attack` — 追加の攻撃ダメージ。`Power` にダメージ量を指定する。多段攻撃の2段目以降に使用

### 回復・防御

- `Heal` — HPを回復する。`Power` に回復量を指定する
- `Barrier` — ダメージ吸収バリアを付与する。`Power` にバリアの耐久値を指定する

### 射撃補助

- `Spread` — スプレッド。着弾地点から前後1マスに爆風を発生させる
- `Penetration` — 貫通。弾が敵を貫通して後方にも当たる

### 多段・連鎖

- `MultiHit` — 多段ヒット。`HitCount` に回数を指定する（全段同じ威力）
- `ChainCard` — 別カードを連鎖発動する。`ChainCardId` に発動するカードのIDを指定する。空の場合は自分自身を再発動

### 状態異常

- `Paralyze` — 麻痺を付与する。`DurationFrames` に継続フレーム数を指定する
- `Poison` — 毒を付与する。`Power` に1ティックあたりのダメージ量、`DurationFrames` に継続フレーム数を指定する
- `Confusion` — 混乱を付与する。`DurationFrames` に継続フレーム数を指定する
- `Blind` — 盲目を付与する。`DurationFrames` に継続フレーム数を指定する

### パネル変化（対象マスのみ）

- `PanelIce` — 命中したマスを氷パネルに変化させる
- `PanelMagma` — 命中したマスをマグマパネルに変化させる
- `PanelGrass` — 命中したマスを草パネルに変化させる
- `PanelCracked` — 命中したマスをひびありパネルに変化させる
- `PanelHole` — 命中したマスを穴パネルに変化させる

### パネル変化（画面全体）

- `PanelIceAll` — 画面全体を氷パネルに変化させる
- `PanelMagmaAll` — 画面全体をマグマパネルに変化させる
- `PanelGrassAll` — 画面全体を草パネルに変化させる
- `PanelCrackedAll` — 画面全体をひびありパネルに変化させる
- `PanelHoleAll` — 画面全体を穴パネルに変化させる

### 投擲・遅延

- `DelayedProjectile` — 遅延着弾。`CardBehaviorMaster` の `DelayFrames` / `LandingOffsetX` / `LandingOffsetY` と連動して動作する

---

## DerivativeTrigger（発動トリガー）

追加効果がいつ発動するかを定義する。`CardEffectMaster.Trigger` に使用。

- `Immediate` — カード使用直後に即時発動する（ダメージ処理より前）
- `Sequential` — 前の段の処理が完了したあとに発動する。多段攻撃の2段目以降に使用
- `OnHit` — 敵にヒットしたときに発動する
- `OnDamage` — 実際にダメージを与えたときに発動する（バリア等で無効化された場合は発動しない）
- `AfterActionIfHit` — カードのすべての処理が終わったあと、1体でもヒットしていれば発動する
- `AfterActionIfDamaged` — カードのすべての処理が終わったあと、1体でも実ダメージを与えていれば発動する
