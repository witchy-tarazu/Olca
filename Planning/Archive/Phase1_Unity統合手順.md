# Phase 1: Unity統合手順書（改訂版）

## 概要

Presentation層のスクリプト実装が完了したので、Unityエディタ上でシーンとUI要素を構築します。

**新仕様**: カスタム画面開閉時にHP・ゲージ・カスタム画面パーツがスライドアニメーションする統合UI

**重要**: 本ドキュメントはUI構築手順のみを扱います。カスタム画面のゲームロジック（コスト計算、選択制限ルール等）については `Planning/バトルシステムについて` の「4.3. カードコストと選択ルール」を参照してください。

---

## 前提条件

- ✅ スクリプト実装完了
  - BattleController.cs
  - BattleView.cs
  - BattleFieldView.cs
  - EnemyView.cs
  - CustomScreenView.cs
  - HandCardView.cs
- ✅ コンパイルエラーなし

### UI実装方針

- **Textコンポーネント**: Legacy Text (`UI > Legacy > Text`) のみを使用します
  - TextMeshProは使用しません（導入の手間を省くため）
  - すべてのテキスト表示はLegacy Textで統一します
  - **注意**: Buttonを作成すると、デフォルトで子に `Text (TMP)` (TextMeshPro) が作成されます
    - この `Text (TMP)` を削除し、`UI > Legacy > Text` で新しいTextを作成してください
    - 詳細な手順は各ステップに記載されています

---

## UI構成の設計方針

### 通常状態（バトル中）
```
┌─────────────────────────┐
│ HP: 100/100 (左上)      │
│ ║                       │ ← ゲージ（縦置き、画面左端）
│ ║                       │
│ ║ [グリッド・敵表示]    │
│ ■                       │
└─────────────────────────┘
```

### カスタム画面表示状態（最新仕様）
```
┌─────────────────────────────────────┐────────────────┐
│     【選択カード表示エリア】          │  [HP: 100/100] │← HP表示
│    ┌──────────────────────────┐     │────────────────┘
│    │ 【イラスト】 │【簡易説明】 │     │    │
│    │             │            │     │ カ │
│    │             │            │     │ ー │← カスタムゲージ（準備中表示）
│    └──────────────────────────┘     │ ス │
│     【手札選択エリア（5x2グリッド）】 │ タ │
│    ┌──────────────────────────┐     │ ム │
│    │  [カード1] [カード2] ...  │     │ ゲ │
│    │  [カード5] [カード6] ...  │     │ ー │
│    └──────────────────────────┘     │ ジ │
│            [確定ボタン]              │    │
└─────────────────────────────────────┘────┘
```

### アニメーション（最新仕様）
**現時点では未実装**。今回は表示/非表示の切り替えのみ確認します。

**将来実装予定**（UniTask/Animatorを使用）:
- **カスタム画面全体のスライドイン/アウト**: 画面外（左側）から画面左へスライド、オーバーランしてポップな印象
- **手札カードの射出アニメーション**: 左方から射出され、回転しながらバウンドして定位置へ
- **カスタムゲージ上に文字表示**: 準備中を示す文字を表示
- **復帰時のゲージアニメーション**: 最大から0に向かって急速にアニメーション

---

## 作業手順

### ステップ1: シーン作成

#### 1-1. 新しいシーンを作成
1. Unityエディタで `File > New Scene` を選択
2. シーン名: `BattlePrototypeScene`
3. `Assets/Scenes/` フォルダに保存
   - フォルダがなければ作成してください

#### 1-2. Main Cameraの設定
- Position: (0, 0, -10)
- Projection: Orthographic
- Size: 5
- Background: 黒 (R:0, G:0, B:0)

---

### ステップ2: BattleControllerオブジェクトの作成

#### 2-1. 空のGameObjectを作成
1. Hierarchy右クリック > `Create Empty`
2. 名前を `BattleController` に変更

#### 2-2. BattleControllerスクリプトをアタッチ
1. BattleControllerオブジェクトを選択
2. Inspector > `Add Component` をクリック
3. "BattleController" と入力して選択

**重要:** この時点では参照は設定しません（後で設定します）

---

### ステップ3: BattleView（統合UI）の構築

#### 3-1. BattleView Canvasを作成
1. Hierarchy右クリック > `UI > Canvas`
2. 名前を `BattleView` に変更
3. Canvas設定:
   - Render Mode: `Screen Space - Overlay`
   - Canvas Scaler (Component):
     - UI Scale Mode: `Scale With Screen Size`
     - Reference Resolution: 1920 x 1080
     - Match: 0.5（Width/Heightのバランス）

#### 3-2. EventSystemの確認
- Canvasを作成すると自動的に `EventSystem` が作成されます
- そのまま残してください

#### 3-3. BattleViewスクリプトをアタッチ
1. BattleViewオブジェクトを選択
2. `Add Component` > "BattleView" を追加

---

### ステップ4: UIコンテナの作成（2つの主要エリア）

#### 4-1. BattleAreaContainer（グリッド・敵用）
1. BattleView右クリック > `Create Empty`
2. 名前を `BattleAreaContainer` に変更
3. Rect Transform:
   - Anchor Presets: Center-Stretch
   - Left: 0, Right: 0
   - Pos Y: -50
   - Height: 600

#### 4-2. CustomScreenContainer（HP・ゲージ・カスタム画面の統合コンテナ）
1. BattleView右クリック > `Create Empty`
2. 名前を `CustomScreenContainer` に変更
3. Rect Transform:
   - Anchor Presets: Middle-Left（画面左端基準）
   - Anchor Min: (0, 0.5), Anchor Max: (0, 0.5)
   - Pivot: (0, 0.5)
   - Pos X: 0（通常時）/ -960（画面外、将来のアニメーション用）
   - Pos Y: 0
   - Width: 960, Height: 1080
4. **初期状態**: SetActive(true)（表示）
   - **重要**: このコンテナ自体は常に表示されています。通常時はHP・ゲージのみ表示され、カスタム画面表示時に内部の子要素（手札エリア等）が表示されます。将来的にはこのコンテナ全体を画面外からスライドインさせます。

---

### ステップ5: CustomScreenContainer内のUI要素（HP・ゲージ）

#### 5-1. PlayerHpText（プレイヤーHP）
1. CustomScreenContainer右クリック > `UI > Legacy > Text`
2. 名前を `PlayerHpText` に変更
3. 設定:
   - Text: "HP: 100 / 100"
   - Font Size: 40
   - Alignment: Center & Middle
   - Color: 白 (R:1, G:1, B:1)
   - Rect Transform:
     - Anchor: Top-Right
     - Pos X: 860（CustomScreenContainerの右端）
     - Pos Y: -50
     - Width: 200, Height: 60

#### 5-2. CustomGaugeContainer（縦置きゲージコンテナ）
1. CustomScreenContainer右クリック > `Create Empty`
2. 名前を `CustomGaugeContainer` に変更
3. Rect Transform:
   - Anchor: Middle-Right
   - Pos X: 900（CustomScreenContainerの右端）
   - Pos Y: 0
   - Width: 60
   - Height: 500

##### 5-2-1. CustomGaugeSlider（縦置きゲージ本体）
1. CustomGaugeContainer右クリック > `UI > Slider`
2. 名前を `CustomGaugeSlider` に変更
3. 設定:
   - **Direction: Bottom To Top**（重要！下から上に増加）
   - Min Value: 0
   - Max Value: 1
   - Value: 0
   - Interactable: OFF（チェックを外す）
   - Rect Transform:
     - Anchor: Stretch（全体に拡大）
     - Left: 0, Right: 0, Top: 0, Bottom: 0

4. Sliderの子オブジェクト設定:
   - **Background**:
     - Image > Color: 灰色 (R:77, G:77, B:77, A:204)
   - **Fill Area > Fill**:
     - Image > Color: 黄色 (R:255, G:230, B:0, A:255)
   - **Handle Slide Area**: 削除または無効化

##### 5-2-2. CustomGaugeLabel（ゲージ上の準備中表示）
1. CustomGaugeContainer右クリック > `UI > Legacy > Text`
2. 名前を `CustomGaugeLabel` に変更
3. 設定:
   - Text: "準備中"
   - Font Size: 20
   - Alignment: Center & Middle
   - Color: 白 (R:255, G:255, B:255)
   - Rect Transform:
     - Anchor: Top-Center
     - Pos X: 0
     - Pos Y: -100
     - Width: 60, Height: 100
4. **初期状態**: SetActive(false)（非表示）

**アニメーション用メモ**: カスタム画面表示時のみ表示、ゲージの上に縦書き風に表示

---

### ステップ6: BattleAreaContainer内のUI要素

#### 6-1. BattleFieldView（グリッド）の作成
1. BattleAreaContainer右クリック > `Create Empty`
2. 名前を `BattleFieldView` に変更
3. Rect Transform:
   - Anchor: Center
   - Pos X: 0, Pos Y: -100
   - Width: 600, Height: 100
4. `Add Component` > "BattleFieldView" を追加

#### 6-2. GridContainer（パネル配置用）
1. BattleFieldView右クリック > `Create Empty`
2. 名前を `GridContainer` に変更
3. Rect Transform:
   - Anchor: Middle-Left
   - Pos X: 0, Pos Y: 0

#### 6-3. PlayerMarker（プレイヤー位置マーカー）
1. BattleFieldView右クリック > `UI > Image`
2. 名前を `PlayerMarker` に変更
3. 設定:
   - Color: 青 (R:51, G:128, B:255)
   - Rect Transform:
     - Width: 90, Height: 90
     - Pos X: 0, Pos Y: 60

#### 6-4. PanelPrefab（グリッドパネル）の作成
1. BattleFieldView右クリック > `UI > Image`
2. 名前を `PanelPrefab` に変更
3. 設定:
   - Color: 白 (R:1, G:1, B:1)
   - Rect Transform:
     - Width: 100, Height: 100
4. **Prefab化**:
   - `Assets/Prefabs/` フォルダを作成（なければ）
   - `PanelPrefab` をProject Windowの `Assets/Prefabs/` にドラッグ＆ドロップ
   - Hierarchy上の `PanelPrefab` は削除

#### 6-5. EnemyContainer（敵配置用）
1. BattleAreaContainer右クリック > `Create Empty`
2. 名前を `EnemyContainer` に変更
3. Rect Transform:
   - Anchor: Center
   - Pos X: 0, Pos Y: 100

#### 6-6. EnemyViewPrefab（敵1体のUI）の作成
1. Hierarchy右クリック > `UI > Image`（一時的にルートに作成）
2. 名前を `EnemyViewPrefab` に変更
3. 設定:
   - Color: 赤 (R:1, G:0.4, B:0.4)
   - Rect Transform:
     - Width: 80, Height: 120

##### 6-6-1. 敵名前テキスト
1. EnemyViewPrefab右クリック > `UI > Legacy > Text`
2. 名前を `EnemyNameText` に変更
3. 設定:
   - Text: "スライム"
   - Font Size: 20
   - Alignment: Center & Top
   - Color: 白
   - Rect Transform:
     - Anchor: Top-Center
     - Pos Y: -5
     - Width: 80, Height: 25

##### 6-6-2. 敵HPテキスト
1. EnemyViewPrefab右クリック > `UI > Legacy > Text`
2. 名前を `HpText` に変更
3. 設定:
   - Text: "HP: 50 / 50"
   - Font Size: 16
   - Alignment: Center & Middle
   - Color: 白
   - Rect Transform:
     - Anchor: Center
     - Pos Y: 0
     - Width: 80, Height: 25

##### 6-6-3. 敵画像（仮）
- EnemyViewPrefab自体がImageコンポーネントを持っています
- Color: 赤 (R:1, G:0.4, B:0.4)

##### 6-6-4. EnemyViewスクリプトをアタッチ
1. EnemyViewPrefabを選択
2. `Add Component` > "EnemyView" を追加
3. Inspector上でコンポーネントを設定:
   - Enemy Name Text: `EnemyNameText` をドラッグ＆ドロップ
   - Hp Text: `HpText` をドラッグ＆ドロップ
   - Enemy Image: 自分自身（EnemyViewPrefab）の `Image` コンポーネント

##### 6-6-5. Prefab化
1. `EnemyViewPrefab` を `Assets/Prefabs/` にドラッグ＆ドロップ
2. Hierarchy上の `EnemyViewPrefab` は削除

---

### ステップ7: CustomScreenContainer内のUI要素（カスタム画面部分）

#### 7-1. CustomScreenViewスクリプトをアタッチ
1. CustomScreenContainerを選択
2. `Add Component` > "CustomScreenView" を追加

#### 7-2. SelectedCardDisplayArea（選択カード表示エリア）
1. CustomScreenContainer右クリック > `Create Empty`
2. 名前を `SelectedCardDisplayArea` に変更
3. Rect Transform:
   - Anchor: Top-Left
   - Pos X: 50, Pos Y: -100
   - Width: 700, Height: 300
4. **初期状態**: SetActive(true)（表示）
   - **重要**: CustomScreenContainer全体が画面外に配置されるため、このエリア自体のSetActive制御は不要です

##### 7-2-1. SelectedCardImage（選択カードイラスト）
1. SelectedCardDisplayArea右クリック > `UI > Image`
2. 名前を `SelectedCardImage` に変更
3. 設定:
   - Color: 白 (R:255, G:255, B:255)
   - Rect Transform:
     - Anchor: Middle-Left
     - Pos X: 150, Pos Y: 0
     - Width: 280, Height: 280

##### 7-2-2. SelectedCardDescription（選択カード説明）
1. SelectedCardDisplayArea右クリック > `UI > Legacy > Text`
2. 名前を `SelectedCardDescription` に変更
3. 設定:
   - Text: "カードを選択してください"
   - Font Size: 18
   - Alignment: Left & Top
   - Color: 白 (R:255, G:255, B:255)
   - Rect Transform:
     - Anchor: Middle-Right
     - Pos X: 500, Pos Y: 0
     - Width: 350, Height: 280

**注**: カードが選択されると、以下の形式で表示されます:
```
コスト: 1
威力: 30

カードの説明文がここに表示されます。
```

#### 7-3. HandCardContainer（手札配置用・5x2グリッド、手動配置）
1. CustomScreenContainer右クリック > `Create Empty`
2. 名前を `HandCardContainer` に変更
3. Rect Transform:
   - Anchor: Middle-Left
   - Pos X: 50, Pos Y: -150
   - Width: 800, Height: 320（150x2 + 10px間隔 + マージン）
4. **初期状態**: SetActive(true)（表示）
   - **重要**: CustomScreenContainer全体が画面外に配置されるため、このコンテナ自体のSetActive制御は不要です

#### 7-3-1. カード位置マーカー（10個の空オブジェクト）
1. HandCardContainer右クリック > `Create Empty`
2. 名前を `CardPosition0` に変更
3. Rect Transform:
   - Anchor: Top-Left
   - Pos X: 0, Pos Y: 0
   - Width: 150, Height: 150（正方形）
4. 同様に `CardPosition1` から `CardPosition9` まで作成
5. **配置**:
   - 行0（上段）: CardPosition0-4
     - Pos X: 0, 160, 320, 480, 640
     - Pos Y: 0
   - 行1（下段）: CardPosition5-9
     - Pos X: 0, 160, 320, 480, 640
     - Pos Y: -160（カード間隔: 10px）
6. **用途**: コード内でこれらの位置を参照してカードを配置
   - Phase1: 即座に配置
   - Phase2: アニメーション後の目標位置として使用

#### 7-3-2. 壁マーカー（Phase2用、アニメーション衝突判定用）
1. HandCardContainer右クリック > `Create Empty`
2. 名前を `WallMarkers` に変更
3. **用途**: Phase2でカードの壁衝突アニメーションの境界を定義

##### 7-3-2-1. RightWall（右の壁）
1. WallMarkers右クリック > `Create Empty`
2. 名前を `RightWall` に変更
3. Rect Transform:
   - Anchor: Top-Left
   - Pos X: 790, Pos Y: 0
   - Width: 10, Height: 320

##### 7-3-2-2. TopWall（上の壁）
1. WallMarkers右クリック > `Create Empty`
2. 名前を `TopWall` に変更
3. Rect Transform:
   - Anchor: Top-Left
   - Pos X: 0, Pos Y: 50
   - Width: 800, Height: 10

##### 7-3-2-3. BottomWall（下の壁）
1. WallMarkers右クリック > `Create Empty`
2. 名前を `BottomWall` に変更
3. Rect Transform:
   - Anchor: Top-Left
   - Pos X: 0, Pos Y: -270（150x2 + 10px間隔 - 50px上マージン）
   - Width: 800, Height: 10

**注**: これらの壁マーカーはPhase1では使用せず、Phase2のアニメーション実装時に衝突判定の境界として使用します。カードは画面外左側から右方向に射出されるため、左の壁は不要です

#### 7-4. SelectionIndicator（選択状態インジケーター）
1. CustomScreenContainer右クリック > `Create Empty`
2. 名前を `SelectionIndicator` に変更
3. Rect Transform:
   - Anchor: Bottom-Center
   - Pos X: 0, Pos Y: 150
   - Width: 300, Height: 50
4. **初期状態**: SetActive(true)（表示）
   - **重要**: CustomScreenContainer全体が画面外に配置されるため、このインジケーター自体のSetActive制御は不要です

##### 7-4-1. Icon0-4（選択状態アイコン5つ）
**Phase1ではImageコンポーネントを使用します。将来的に画像を差し替えることで容易にデザイン変更が可能です。**

1. SelectionIndicator右クリック > `UI > Image`
2. 名前を `Icon0` に変更
3. 設定:
   - Source Image: None（Phase1では設定なし、デフォルトの白四角）
   - Color: グレー (R:128, G:128, B:128)（未選択状態）
   - Image Type: Simple
   - Rect Transform:
     - Anchor: Middle-Left
     - Pos X: 25, Pos Y: 0
     - Width: 50, Height: 50
4. 同様に `Icon1` から `Icon4` まで作成
5. **配置**:
   - Icon0: Pos X: 25
   - Icon1: Pos X: 85（25 + 50 + 10）
   - Icon2: Pos X: 145（85 + 50 + 10）
   - Icon3: Pos X: 205（145 + 50 + 10）
   - Icon4: Pos X: 265（205 + 50 + 10）
   - ※ 各アイコンの間隔: 10px

**表示ルール**:
- 選択枚数に応じて左から順に黄色（R:255, G:230, B:0）に変化
- 例: 3枚選択時 → Icon0-2が黄色、Icon3-4がグレー

**将来の画像差し替え手順**:
- Phase2以降でデザインが決まったら、各IconのSource Imageに画像を設定するだけで差し替え完了
- 例: 丸いアイコン画像、星形アイコン画像など

#### 7-5. CostDisplay（コスト表示）
1. CustomScreenContainer右クリック > `UI > Legacy > Text`
2. 名前を `CostDisplay` に変更
3. 設定:
   - Text: "コスト: 0 / 5"
   - Font Size: 28
   - Alignment: Center & Middle
   - Color: 白 (R:255, G:255, B:255)
   - Rect Transform:
     - Anchor: Bottom-Center
     - Pos X: 0, Pos Y: 220
     - Width: 300, Height: 40
4. **初期状態**: SetActive(true)（表示）
   - **重要**: CustomScreenContainer全体が画面外に配置されるため、このテキスト自体のSetActive制御は不要です

**表示ルール**:
- 「コスト: {現在の合計コスト} / {カードコスト上限}」の形式で表示
- 例: 「コスト: 3 / 5」（合計コスト3、上限5）
- カードコスト上限を超えた場合は赤色（R:255, G:100, B:100）で表示

#### 7-6. ConfirmButton（確定ボタン）
1. CustomScreenContainer右クリック > `UI > Button`
2. 名前を `ConfirmButton` に変更
3. 設定:
   - Rect Transform:
     - Anchor: Bottom-Center
     - Pos X: 0, Pos Y: 80
     - Width: 250, Height: 70
4. **子のTextをLegacy Textに置き換え**:
   - デフォルトで作成される `Text (TMP)` を削除
   - ConfirmButton右クリック > `UI > Legacy > Text` で新しいTextを作成
   - 名前を `Text` に変更
   - 設定:
     - Text: "確定"
     - Font Size: 36
     - Alignment: Center & Middle
     - Color: 白 (R:255, G:255, B:255)
     - Rect Transform:
       - Anchor: Stretch（全体に拡大）
       - Left: 0, Right: 0, Top: 0, Bottom: 0
5. **初期状態**: SetActive(true)（表示）
   - **重要**: CustomScreenContainer全体が画面外に配置されるため、このボタン自体のSetActive制御は不要です

#### 7-7. HandCardViewPrefab（手札1枚のUI）の作成
1. Hierarchy右クリック > `UI > Image`（一時的にルートに作成）
2. 名前を `HandCardViewPrefab` に変更
3. 設定:
   - Color: 白 (R:242, G:242, B:242)
   - Rect Transform:
     - Width: 150, Height: 150（正方形）

##### 7-7-1. コストテキスト
1. HandCardViewPrefab右クリック > `UI > Legacy > Text`
2. 名前を `CostText` に変更
3. 設定:
   - Text: "1"
   - Font Size: 36
   - Alignment: Center & Middle
   - Color: 青 (R:51, G:77, B:204)
   - Rect Transform:
     - Anchor: Top-Center
     - Pos Y: -30
     - Width: 150, Height: 50

**注**: カードにはコストのみを表示します。威力やその他の情報は画面上部の選択カード表示エリアに表示されます。

##### 7-7-2. カード画像（仮）
- HandCardViewPrefab自体がImageコンポーネントを持っています
- Color: 白 (R:242, G:242, B:242)

##### 7-7-3. Buttonコンポーネントを追加
1. HandCardViewPrefabを選択
2. `Add Component` > "Button" を追加
3. Button設定:
   - Interactable: ON
   - Transition: Color Tint
   - Normal Color: 白 (R:255, G:255, B:255)
   - Highlighted Color: 薄黄色 (R:255, G:255, B:179)
   - Pressed Color: 黄色 (R:255, G:230, B:0)
   - Selected Color: 薄黄色 (R:255, G:255, B:204)

##### 7-7-4. HandCardViewスクリプトをアタッチ
1. HandCardViewPrefabを選択
2. `Add Component` > "HandCardView" を追加
3. Inspector上でコンポーネントを設定:
   - Cost Text: `CostText` をドラッグ＆ドロップ
   - Card Image: 自分自身の `Image` コンポーネント
   - Card Button: 自分自身の `Button` コンポーネント

##### 7-7-5. Prefab化
1. `HandCardViewPrefab` を `Assets/Prefabs/` にドラッグ＆ドロップ
2. Hierarchy上の `HandCardViewPrefab` は削除

---

### ステップ8: コンポーネント参照の設定

#### 8-1. BattleFieldViewの参照設定
1. BattleFieldViewオブジェクトを選択
2. Inspector > BattleFieldView (Script)で以下を設定:
   - Panel Prefab: `Assets/Prefabs/PanelPrefab` をドラッグ＆ドロップ
   - Grid Container: `GridContainer` をドラッグ＆ドロップ
   - Player Marker: `PlayerMarker` をドラッグ＆ドロップ

#### 8-2. CustomScreenViewの参照設定
1. CustomScreenContainerを選択
2. Inspector > CustomScreenView (Script)で以下を設定:
   - Hand Card Container: `HandCardContainer` をドラッグ＆ドロップ
   - Hand Card Prefab: `Assets/Prefabs/HandCardViewPrefab` をドラッグ＆ドロップ
   - Confirm Button: `ConfirmButton` をドラッグ＆ドロップ
   - Cost Display: `CostDisplay` をドラッグ＆ドロップ
   - Card Position Markers: サイズを10に設定
     - Element 0: `CardPosition0` をドラッグ＆ドロップ
     - Element 1: `CardPosition1` をドラッグ＆ドロップ
     - ... (Element 9まで同様に設定)
   - Right Wall: `WallMarkers/RightWall` をドラッグ＆ドロップ
   - Top Wall: `WallMarkers/TopWall` をドラッグ＆ドロップ
   - Bottom Wall: `WallMarkers/BottomWall` をドラッグ＆ドロップ
   - Selected Card Image: `SelectedCardDisplayArea/SelectedCardImage` （Imageコンポーネント）をドラッグ＆ドロップ
   - Selected Card Description: `SelectedCardDisplayArea/SelectedCardDescription` （Textコンポーネント）をドラッグ＆ドロップ

#### 8-3. BattleViewの参照設定
1. BattleViewオブジェクトを選択
2. Inspector > BattleView (Script)で以下を設定:
   - Player Hp Text: `PlayerHpText` （Textコンポーネント）をドラッグ＆ドロップ
   - Custom Gauge Container: `CustomGaugeContainer` （RectTransform）をドラッグ＆ドロップ
   - Custom Gauge Label: `CustomGaugeLabel` をドラッグ＆ドロップ
   - **Custom Screen Container**: `CustomScreenContainer` （RectTransform）をドラッグ＆ドロップ
   - Battle Field View: `BattleFieldView` をドラッグ＆ドロップ
   - Enemy Container: `EnemyContainer` をドラッグ＆ドロップ
   - Enemy View Prefab: `Assets/Prefabs/EnemyViewPrefab` をドラッグ＆ドロップ

#### 8-4. BattleControllerの参照設定
1. BattleControllerオブジェクトを選択
2. Inspector > BattleController (Script)で以下を設定:
   - Battle View: `BattleView` をドラッグ＆ドロップ
   - Custom Screen View: `CustomScreenContainer` をドラッグ＆ドロップ

---

### ステップ9: シーンの保存

**重要**: Phase1では位置制御でカスタム画面の表示/非表示を切り替えます。

#### 動作の仕組み:
- **CustomScreenContainer**の位置（Pos X）を切り替えることで表示/非表示を制御
  - 非表示時: Pos X = -960（画面外左側）
  - 表示時: Pos X = 0（画面内）
- Phase1: BattleView.csで座標を即座に切り替え（アニメーションなし）
- Phase2: UniTask/DOTweenでスライドイン/アウトアニメーション実装予定

#### 確認事項:
- すべてのUI要素がSetActive(true)（表示状態）になっていることを確認
- CustomScreenContainerの初期Pos Xは任意（BattleView.Initializeで-960に設定されます）

シーンを保存してください（Ctrl + S）。

**将来のアニメーション実装予定**: UniTask/Animatorを使用して、以下のアニメーションを実装します:
- CustomScreenContainer全体を画面外からスライドイン/アウト（オーバーランでポップな印象）
- 手札カードを左方から射出、回転しながらバウンドして定位置へ
- カスタムゲージ上に「準備中」文字を表示
- 復帰時にゲージが最大から0に向かって急速にアニメーション

---

### ステップ10: 動作確認

#### 10-1. シーンを保存
- `Ctrl + S` でシーンを保存

#### 10-2. Play実行
1. Playボタンを押してゲームを実行
2. 確認項目:

   **通常状態（バトル中）**:
   - [ ] プレイヤーHP「HP: 100 / 100」が左上に表示される
   - [ ] **カスタムゲージが画面左端に縦置きで表示され、下から上に増加する**
   - [ ] グリッドパネル（6個）が横並びで表示される
   - [ ] プレイヤーマーカー（青い四角）が左端に表示される
   - [ ] 敵が2体（スライム、ゴブリン）表示される
   - [ ] 移動入力（矢印キー）でプレイヤーマーカーが動く

   **カスタム画面表示時**:
   - [ ] カスタムゲージが満タンになるとカスタム画面が表示される
   - [ ] カスタムゲージコンテナ上に「準備中」ラベルが表示される
   - [ ] 選択カード表示エリアが表示される
   - [ ] 手札が5x2グリッドで表示される（最大10枚）
   - [ ] カードをクリックすると選択状態（黄色）になる
   - [ ] 選択状態インジケーターが選択枚数を表示する（最大5枚）
   - [ ] 確定ボタンでカスタム画面が非表示になる
   - [ ] 「準備中」ラベルが非表示になる
   - [ ] カスタムゲージが0にリセットされる

#### 10-3. エラー確認
- Consoleウィンドウにエラーがないか確認
- エラーがあれば内容を報告してください

---

## トラブルシューティング

### よくあるエラー

#### 1. NullReferenceException
**原因**: コンポーネント参照が設定されていない

**対処法**:
- ステップ8の参照設定を再確認
- Inspector上で赤い「None」表示がないか確認
- **特にCustomGaugeContainerがRectTransformとして設定されているか確認**

#### 2. カスタムゲージが増加しない、または縦に表示されない
**原因**: Sliderの設定が間違っている

**対処法**:
- CustomGaugeSliderの Min Value: 0, Max Value: 1 を確認
- **Direction: Bottom To Top が設定されているか確認**
- Interactableがオフになっているか確認
- CustomGaugeContainerのサイズが Width: 60, Height: 500 になっているか確認

#### 3. カードが表示されない
**原因**: HandCardPrefabの参照ミス

**対処法**:
- CustomScreenViewの Hand Card Prefab に正しいPrefabが設定されているか確認

#### 4. カスタム画面が表示されない
**原因**: CustomScreenContainerの初期状態が間違っている

**対処法**:
- CustomScreenContainerは SetActive(true)（常に表示）になっているか確認
- **Phase1では、カスタム画面の子要素（HandCardContainer、SelectedCardDisplayArea、ConfirmButton等）はすべてSetActive(true)で常に表示状態にしておきます**
- CustomScreenContainer全体の位置（Pos X）で表示/非表示を制御します
- CustomGaugeLabelのみ、初期状態でSetActive(false)にしておきます
- BattleControllerの参照でCustomScreenViewが正しく設定されているか確認

---

## 完了チェックリスト

Phase 1完了の条件:

- [×] BattlePrototypeSceneが作成され保存されている
- [×] プレイヤーHP、カスタムゲージが表示される
- [×] 6x1グリッドとプレイヤーマーカーが表示される
- [×] 敵が2体表示される
- [×] カスタムゲージが満タンでカスタム画面が表示される
- [×] カスタム画面で手札が表示され、カード選択ができる
- [×] 確定ボタンでカスタム画面が非表示になる
- [×] コンソールにエラーがない

---

## Phase 2の予定（アニメーション実装・最新仕様）

Phase 1完了後、以下を実装します:

### アニメーション実装（最新仕様）
- **カスタム画面スライドイン/アウト**
  - CustomScreenContainer: 画面外（Pos X: -960）から画面左（Pos X: 0）へスライド
  - スライドイン時: 一瞬右側にオーバーランしてからポップな印象を付ける
  - スライドアウト時: 画面外へ移動する前に一瞬右へ移動してポップな印象

- **手札カードの射出アニメーション**
  - HandCardContainer内で直接アニメーションを実行
  - 各カードを左方（画面外）から射出し、等速直線運動で矩形内の壁に2回衝突して定位置へ
  - **衝突パターン**:
    - パターンA: 右の壁 → 上/下の壁 → 定位置
    - パターンB: 上/下の壁 → 下/上の壁 or 右の壁 → 定位置
  - **パターン選択ルール**: 定位置に応じて自然な衝突経路を選択
    - 左上エリア（行0, 列0-2）: 右の壁 → 下の壁が自然
    - 左下エリア（行1, 列0-2）: 右の壁 → 上の壁が自然
    - 右上エリア（行0, 列3-4）: 上の壁 → 右の壁が自然
    - 右下エリア（行1, 列3-4）: 下の壁 → 右の壁が自然
  - 回転しながら移動し、定位置に収まる際には回転角が0度になる
  - **実装方法**: 物理シミュレーション（等速直線運動 + 壁反射）

- **カスタムゲージ上に文字表示**
  - CustomGaugeLabel: 「準備中」を表示

- **復帰時のゲージアニメーション**
  - カスタムゲージが最大から0に向かって急速にアニメーション

### 実装方法の選択肢

#### カスタム画面スライドイン/アウト
1. **DOTween**（推奨）: `Ease.OutBack`でオーバーラン効果を実現
2. **Animation Clip**: Unity標準のAnimation機能

#### 手札カードの射出アニメーション（壁衝突）
1. **物理シミュレーション + UniTask**（推奨）:
   ```csharp
   // 等速直線運動 + 壁での完全反射（反射係数1.0）
   Vector2 velocity = initialVelocity;
   Vector2 position = startPosition;
   int bounceCount = 0;

   while (bounceCount < 2)
   {
       position += velocity * Time.deltaTime;

       // 壁との衝突判定と反射
       if (position.x > rightWall) {
           position.x = rightWall;
           velocity.x = -velocity.x;
           bounceCount++;
       }
       if (position.y > topWall || position.y < bottomWall) {
           position.y = Mathf.Clamp(position.y, bottomWall, topWall);
           velocity.y = -velocity.y;
           bounceCount++;
       }

       await UniTask.Yield();
   }
   ```

2. **ベジェ曲線による経路計算**:
   - 開始位置、2つの衝突点、終了位置を計算
   - 3次ベジェ曲線で滑らかな経路を生成
   - DOTweenの`DOPath`で経路に沿って移動

3. **Animation Clip**: 手動でキーフレームを設定（調整が大変）

### Phase2実装時の詳細ガイド

#### カード位置の取得（Phase1から必要）

```csharp
// CustomScreenView.cs
[SerializeField] private Transform[] cardPositionMarkers; // カード位置マーカー（10個）

/// <summary>
/// カード位置マーカーからRectTransform位置を取得
/// </summary>
private Vector2 GetCardPosition(int index)
{
    if (cardPositionMarkers == null || index < 0 || index >= cardPositionMarkers.Length)
    {
        // フォールバック: マーカーが設定されていない場合は手動計算
        Debug.LogWarning($"CardPositionMarker[{index}] が設定されていません。デフォルト位置を使用します。");
        int row = index / 5;
        int col = index % 5;
        return new Vector2(col * 160f, -row * 160f); // 150px + 10px間隔
    }

    var markerRect = cardPositionMarkers[index] as RectTransform;
    return markerRect.anchoredPosition;
}

/// <summary>
/// カードを配置（Phase1: 即座に配置、Phase2: アニメーション後に配置）
/// </summary>
private void PlaceCard(HandCardView cardView, int index)
{
    var rectTransform = cardView.GetComponent<RectTransform>();
    rectTransform.anchoredPosition = GetCardPosition(index);
    rectTransform.rotation = Quaternion.identity;
}
```

**利点**:
- Unity Editor上でカード位置を調整可能（コード変更不要）
- 視覚的に配置を確認できる
- Phase2でアニメーション実装時も、目標位置として同じメソッドを使用

#### 初速度と衝突点の計算方法（Phase2用）

```csharp
// カードの定位置に応じた衝突パターンを決定
public (Vector2 firstBouncePoint, Vector2 secondBouncePoint) CalculateBouncePoints(
    Vector2 targetPosition,
    Rect containerRect,
    int row,
    int col)
{
    Vector2 firstBounce, secondBounce;

    // 左上エリア（行0, 列0-2）: 右の壁 → 下の壁
    if (row == 0 && col <= 2)
    {
        firstBounce = new Vector2(containerRect.xMax, Random.Range(containerRect.yMin + 50, targetPosition.y - 50));
        secondBounce = new Vector2(targetPosition.x + Random.Range(20, 80), containerRect.yMax);
    }
    // 左下エリア（行1, 列0-2）: 右の壁 → 上の壁
    else if (row == 1 && col <= 2)
    {
        firstBounce = new Vector2(containerRect.xMax, Random.Range(targetPosition.y + 50, containerRect.yMax - 50));
        secondBounce = new Vector2(targetPosition.x + Random.Range(20, 80), containerRect.yMin);
    }
    // 右上エリア（行0, 列3-4）: 上の壁 → 右の壁
    else if (row == 0 && col >= 3)
    {
        firstBounce = new Vector2(Random.Range(containerRect.xMin + 50, targetPosition.x - 50), containerRect.yMax);
        secondBounce = new Vector2(containerRect.xMax, targetPosition.y + Random.Range(20, 80));
    }
    // 右下エリア（行1, 列3-4）: 下の壁 → 右の壁
    else
    {
        firstBounce = new Vector2(Random.Range(containerRect.xMin + 50, targetPosition.x - 50), containerRect.yMin);
        secondBounce = new Vector2(containerRect.xMax, targetPosition.y - Random.Range(20, 80));
    }

    return (firstBounce, secondBounce);
}

// 初速度を計算（逆算）
public Vector2 CalculateInitialVelocity(
    Vector2 startPosition,
    Vector2 firstBouncePoint,
    float totalDuration)
{
    // 第1衝突点までの時間を総時間の1/3と仮定
    float timeToFirstBounce = totalDuration / 3f;
    return (firstBouncePoint - startPosition) / timeToFirstBounce;
}
```

---

## 補足資料

### UI階層構造（最終形・最新仕様）

```
BattleView (Canvas)
├── BattleAreaContainer
│   ├── BattleFieldView
│   │   ├── GridContainer
│   │   └── PlayerMarker
│   └── EnemyContainer
└── CustomScreenContainer（HP・ゲージ・カスタム画面を統合、画面左端基準）
    ├── PlayerHpText（CustomScreenContainerの右上）
    ├── CustomGaugeContainer（縦置きゲージ、CustomScreenContainerの右端）
    │   ├── CustomGaugeSlider（Direction: Bottom To Top）
    │   └── CustomGaugeLabel（「準備中」表示）
    ├── SelectedCardDisplayArea（カスタム画面表示時のみ）
    │   ├── SelectedCardImage
    │   └── SelectedCardDescription
    ├── HandCardContainer（カスタム画面表示時のみ、5x2グリッド、手動配置）
    ├── SelectionIndicator（カスタム画面表示時のみ、選択状態表示）
    └── ConfirmButton（カスタム画面表示時のみ）
```

**注**: AnimationTempContainerは削除され、HandCardContainer内で直接アニメーション実行

### 表示状態まとめ

| 要素 | 通常状態 | カスタム画面表示時 |
|------|---------|-------------------|
| CustomScreenContainer | 表示（Pos X: 0） | 表示（Pos X: 0、将来はスライドイン） |
| PlayerHpText | 表示 | 表示 |
| CustomGaugeContainer | 表示 | 表示 |
| CustomGaugeSlider | 表示 | 表示 |
| CustomGaugeLabel | 非表示 | 表示（「準備中」） |
| SelectedCardDisplayArea | 表示（画面外） | 表示 |
| HandCardContainer | 表示（画面外） | 表示（5x2グリッド） |
| SelectionIndicator | 表示（画面外） | 表示（0-5枚選択） |
| CostDisplay | 表示（画面外） | 表示 |
| ConfirmButton | 表示（画面外） | 表示 |

**設計意図**:
- CustomScreenContainer全体を画面左端基準で配置し、将来的にこのコンテナ全体を画面外からスライドインできるようにしています
- **Phase1では、カスタム画面要素（SelectedCardDisplayArea、HandCardContainer、SelectionIndicator、CostDisplay、ConfirmButton）はすべてSetActive(true)で常に表示状態ですが、CustomScreenContainer全体が画面外（Pos X: 0、将来は-960）にあるため、視覚的には非表示です**
- HP・ゲージ・カスタム画面が一体化した構造で、カスタム画面表示時も画面内に見える位置にあります
- 手札は5x2グリッド（最大10枚）で表示し、選択可能枚数は0-5枚です
- CustomGaugeLabelのみ、表示/非表示の制御が必要です（カスタム画面表示時のみ「準備中」を表示）
- **Grid Layout Groupを使わない理由**:
  - Phase2で壁衝突アニメーションを実装する際、Grid Layout Groupが自動配置を上書きしてしまう
  - 手動配置により、アニメーション完了後に位置を固定できる
  - カード枚数に応じた柔軟なレイアウト調整が可能

---

## 作成日時
- 2026-01-12（最新仕様対応版）: カスタム画面仕様に基づき大幅更新
- 2026-01-11（改訂版）
