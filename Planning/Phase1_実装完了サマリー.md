# Phase 1: Presentation層実装完了サマリー

## 実装完了日
2026-01-11

---

## 完了内容

### ✅ スクリプト実装（コンパイルエラーなし）

#### 1. BattleView.cs（拡張版）
**パス**: `Assets/Olca/Presentation/Battle/BattleView.cs`

**機能**:
- バトル画面全体の表示管理
- **アニメーション制御機能を追加**:
  - カスタム画面開閉時のスライドアニメーション
  - HP表示の位置移動（左上 ⇔ 上部中央）
  - **カスタムゲージの位置移動（画面左端 ⇔ 中央寄り）**
  - イージング（EaseOutCubic / EaseInCubic）適用

**新規追加プロパティ**:
```csharp
[SerializeField] private RectTransform customGaugeContainer;  // ゲージコンテナ（縦置き）
[SerializeField] private GameObject customGaugeLabel;         // カスタム中表示ラベル
[SerializeField] private RectTransform customScreenContainer; // カスタム画面コンテナ
[SerializeField] private float animationDuration = 0.3f;      // アニメーション時間
```

**新規追加メソッド**:
- `OpenCustomScreen()` - カスタム画面を開くアニメーション
- `CloseCustomScreen()` - カスタム画面を閉じるアニメーション
- `AnimateCustomScreenOpen()` - 開くアニメーションのコルーチン
- `AnimateCustomScreenClose()` - 閉じるアニメーションのコルーチン

**アニメーション座標**:
| 要素 | 通常状態 | カスタム画面表示時 |
|------|---------|-------------------|
| PlayerHpText | X: 100 | X: 960 |
| CustomGaugeContainer | X: 50 | X: 480 |
| CustomScreenContainer | X: -960 | X: 0 |

#### 2. BattleController.cs（修正）
**パス**: `Assets/Olca/Presentation/Battle/BattleController.cs`

**変更箇所**:
```csharp
// 修正前
private void OnGaugeFull()
{
    battleSystem.OpenCustomScreen(randomProvider);
    battleView.gameObject.SetActive(false);
    customScreenView.gameObject.SetActive(true);
}

// 修正後
private void OnGaugeFull()
{
    battleSystem.OpenCustomScreen(randomProvider);
    battleView.OpenCustomScreen();  // アニメーション呼び出し
}
```

```csharp
// 修正前
public void OnCustomScreenClosed()
{
    battleView.gameObject.SetActive(true);
    customScreenView.gameObject.SetActive(false);
}

// 修正後
public void OnCustomScreenClosed()
{
    battleView.CloseCustomScreen();  // アニメーション呼び出し
}
```

#### 3. その他のスクリプト（変更なし）
- BattleFieldView.cs
- EnemyView.cs
- CustomScreenView.cs
- HandCardView.cs

---

## UI構成の設計（改訂版）

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

### カスタム画面表示状態
```
┌─────────────────────────┐
│       HP: 100/100 (上部中央) │ ← 左上から移動
│                         │
│ [カスタム]  ║  [グリッド] │ ← 左から出現
│ [画面]     ■  [敵表示]  │ ← ゲージは中央に押し出される
│ [手札]                  │
└─────────────────────────┘
```

### アニメーションの流れ

#### 開く（0.3秒、EaseOutCubic）
1. カスタム画面が左（X: -960）から中央（X: 0）へスライドイン
2. HPが左上（X: 100）から上部中央（X: 960）へ移動
3. **ゲージが画面左端（X: 50）から中央寄り（X: 480）へ移動**
4. ゲージラベル「カスタム中...」が表示

#### 閉じる（0.3秒、EaseInCubic）
1. カスタム画面が中央（X: 0）から左（X: -960）へスライドアウト
2. HPが上部中央（X: 960）から左上（X: 100）へ戻る
3. **ゲージが中央寄り（X: 480）から画面左端（X: 50）へ戻る**
4. ゲージラベルが非表示

---

## Unity エディタ作業（ユーザー作業）

### 必要な作業

手順書に従ってUnityエディタ上で以下を実施してください:

[Planning/Phase1_Unity統合手順_改訂版.md](Phase1_Unity統合手順_改訂版.md) を参照

#### 主要な設定項目

1. **CustomGaugeContainerの作成**（縦置きゲージ）
   - Anchor: Middle-Left
   - 初期位置: X: 50（画面左端）
   - Size: Width: 60, Height: 500
   - Sliderの Direction: **Bottom To Top**

2. **CustomScreenContainerの初期位置**
   - **重要**: Anchor: Middle-Left, Pos X: **-960**（画面外）

3. **BattleViewの参照設定**
   - Player Hp Text: RectTransform
   - **Custom Gauge Container**: RectTransform（新規）
   - Custom Gauge Label: GameObject
   - Custom Screen Container: RectTransform

---

## 動作確認項目

### ✅ 基本動作
- [ ] プレイヤーHP「HP: 100 / 100」が左上に表示される
- [ ] **カスタムゲージが画面左端に縦置きで表示される**
- [ ] **ゲージが下から上に増加する（Direction: Bottom To Top）**
- [ ] グリッドパネル（6個）が横並びで表示される
- [ ] プレイヤーマーカー（青い四角）が左端に表示される
- [ ] 敵が2体（スライム、ゴブリン）表示される

### ✅ アニメーション動作
- [ ] カスタムゲージが満タンになるとアニメーション開始
- [ ] **カスタム画面が左からスライドインする**
- [ ] **HPが上部中央に移動する**
- [ ] **ゲージが中央寄りに押し出される**
- [ ] **ゲージラベル「カスタム中...」が表示される**
- [ ] アニメーションがスムーズ（0.3秒、イージングあり）

### ✅ カスタム画面
- [ ] 手札が表示される
- [ ] カードをクリックすると選択状態（黄色）になる
- [ ] 確定ボタンでアニメーション開始
- [ ] **カスタム画面が左へスライドアウトする**
- [ ] **HPが左上に戻る**
- [ ] **ゲージが画面左端に戻る**
- [ ] **ゲージラベルが非表示になる**

---

## 技術詳細

### イージング関数

#### EaseOutCubic（開く時）
```csharp
float easedT = 1f - Mathf.Pow(1f - t, 3f);
```
- 速く始まり、ゆっくり終わる
- 視覚的に自然なスライドイン

#### EaseInCubic（閉じる時）
```csharp
float easedT = t * t * t;
```
- ゆっくり始まり、速く終わる
- スムーズなスライドアウト

### Coroutineによるアニメーション

```csharp
private IEnumerator AnimateCustomScreenOpen()
{
    float elapsed = 0f;
    while (elapsed < animationDuration)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / animationDuration);
        float easedT = 1f - Mathf.Pow(1f - t, 3f);

        // 各UI要素の位置をLerpで更新
        playerHpText.anchoredPosition = new Vector2(
            Mathf.Lerp(HP_NORMAL_X, HP_CUSTOM_X, easedT),
            playerHpText.anchoredPosition.y
        );

        yield return null;
    }

    // 最終位置を保証
    playerHpText.anchoredPosition = new Vector2(HP_CUSTOM_X, playerHpText.anchoredPosition.y);
}
```

---

## 次のステップ（Phase 2）

Phase 1完了後、以下の機能を追加予定:

### カード効果の視覚化
- ダメージ数値表示（TextMesh）
- ヒットエフェクト（Particle System）
- カード使用時のエフェクト

### 敵の行動表示
- 敵の攻撃予告表示
- 敵のターンアニメーション

### サウンド
- BGM
- SE（攻撃、カード選択、UI操作）

### ドット絵アセット統合
- プレイヤースプライト
- 敵スプライト
- パネルスプライト
- カードイラスト

---

## トラブルシューティング

### ゲージが縦に表示されない
**対処法**: Sliderの Direction を **Bottom To Top** に設定

### アニメーションが動作しない
**対処法**:
1. BattleViewの参照が正しく設定されているか確認
2. customGaugeContainer、customScreenContainer が RectTransform で設定されているか確認

### ゲージが押し出されない
**対処法**: customGaugeContainer の初期位置が X: 50 になっているか確認

---

## 完了チェックリスト

- [x] BattleView.csにアニメーション機能実装
- [x] BattleController.csでアニメーション呼び出し
- [x] コンパイルエラーなし
- [ ] Unityエディタ上でUI配置（ユーザー作業）
- [ ] 動作確認（ユーザー作業）

---

## 参考資料

- [Phase1_Unity統合手順_改訂版.md](Phase1_Unity統合手順_改訂版.md) - 詳細な手順書
- [CLAUDE.md](../CLAUDE.md) - アーキテクチャ原則
- [開発状況.md](開発状況.md) - プロジェクト全体の進捗

---

作成日: 2026-01-11
