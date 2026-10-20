# FUTURE_WORK - 将来実装予定の項目

このドキュメントは、現在の実装が完了した後に着手する予定の改善項目をまとめたものです。

---

## 1. カスタム画面アニメーションのAnimator移行

### 概要
現在、カスタム画面の開閉アニメーションはUniTaskによるコードベースの実装となっています。これをUnity Animatorによる宣言的なアニメーションに移行します。

### 現在の実装
**ファイル**: [Assets/Olca/Presentation/Battle/BattleView.cs](../Assets/Olca/Presentation/Battle/BattleView.cs)

**対象メソッド**:
- `AnimateCustomScreenOpenAsync()` - カスタム画面を開くアニメーション
- `AnimateCustomScreenCloseAsync()` - カスタム画面を閉じるアニメーション
- `AnimateGaugeResetAsync()` - カスタムゲージリセットアニメーション

**現在のアニメーション仕様**:
- オーバーラン付きスライドイン/アウト
  - Phase 1: 画面外 → オーバーラン (0.25秒、EaseOutQuad)
  - Phase 2: オーバーラン → 定位置 (0.15秒、EaseInQuad)
- カスタムゲージのリセット (0.3秒、EaseInQuad)

### 移行後の実装方針

#### 1. Animator Controller作成
- **パス**: `Assets/Olca/Animations/CustomScreen.controller`
- **ステート**:
  - `Idle` (待機状態)
  - `Opening` (開くアニメーション)
  - `Open` (開いた状態)
  - `Closing` (閉じるアニメーション)
  - `Closed` (閉じた状態)

#### 2. Animation Clip作成
- `CustomScreen_Open.anim`
  - RectTransform.anchoredPosition.x のキーフレーム
  - -960 → +50 → 0 の2段階アニメーション
  - カーブエディタでEaseOutQuad/EaseInQuadを設定

- `CustomScreen_Close.anim`
  - RectTransform.anchoredPosition.x のキーフレーム
  - 0 → +50 → -960 の2段階アニメーション

- `CustomGauge_Reset.anim`
  - Slider.value のキーフレーム
  - 最大値 → 0 へのアニメーション (0.3秒)

#### 3. コード側の変更
```csharp
// BattleView.cs
[SerializeField] private Animator customScreenAnimator;

public void OpenCustomScreen()
{
    if (isCustomScreenOpen) return;
    isCustomScreenOpen = true;

    customScreenAnimator.SetTrigger("Open");
    if (customGaugeLabel != null) customGaugeLabel.SetActive(true);
}

public void CloseCustomScreen()
{
    if (!isCustomScreenOpen) return;
    isCustomScreenOpen = false;

    customScreenAnimator.SetTrigger("Close");
    // AnimationEventでOnCloseCompletedを呼ぶ
}

// AnimationEventから呼ばれる
private void OnCloseCompleted()
{
    if (customGaugeLabel != null) customGaugeLabel.SetActive(false);
    // ゲージリセットアニメーション開始
    customGaugeAnimator.SetTrigger("Reset");
}
```

#### 4. 削除するコード
- `AnimateCustomScreenOpenAsync()`
- `AnimateCustomScreenCloseAsync()`
- `AnimateGaugeResetAsync()`
- `EaseOutQuad()` / `EaseInQuad()` (カスタム画面用)
- `animationCts` (カスタム画面用CancellationToken)

### 利点
1. **可視性**: Animation Curveをエディタで確認・調整可能
2. **デザイナーフレンドリー**: プログラマー以外でも調整可能
3. **再利用性**: 他のUI要素にも同じアニメーションを適用可能
4. **パフォーマンス**: Animatorは最適化されている
5. **デバッグ性**: Animation Windowで再生・確認が容易

### 注意点
- **手札カードアニメーション**は**対象外**（物理シミュレーション的な動きのため、コードベースが適切）
- カスタム画面の開閉アニメーションのみが対象
- 既存のUniTask実装は動作しているため、急いで移行する必要はない

### 着手タイミング
**他の実装項目が一通り完了した後**

具体的には以下が完了してから:
- Phase 3: カード効果の視覚化
- Phase 4: サウンド統合
- Phase 5: ゲージ満タン時の演出強化
- バトルシステムの完成

### 関連ドキュメント
- [Phase2_アニメーション実装完了.md](Phase2_アニメーション実装完了.md) - 現在のアニメーション実装
- [CLAUDE.md](../CLAUDE.md) - Coroutine禁止ポリシー（Animatorは対象外）

---

## 2. エフェクト画像のSpriteAnimation移行

### 概要
現在、ヒットエフェクトとカード選択エフェクトはImageの色変更・スケール変更による実装となっています。これをSpriteAnimationベースのビジュアルエフェクトに置き換えます。

### 現在の実装

#### HitEffectView.cs
**ファイル**: [Assets/Olca/Presentation/Effects/HitEffectView.cs](../Assets/Olca/Presentation/Effects/HitEffectView.cs)

**現在のエフェクト**:
- フラッシュエフェクト（色を白に変更）
- シェイクエフェクト（位置をランダムに振動）
- スケールパルスエフェクト（一時的に拡大）

#### CardEffectView.cs
**ファイル**: [Assets/Olca/Presentation/Effects/CardEffectView.cs](../Assets/Olca/Presentation/Effects/CardEffectView.cs)

**現在のエフェクト**:
- ハイライトエフェクト（色と強度の変更）
- スケールアニメーション（選択時に拡大）
- フラッシュエフェクト（確定時に白く点滅）

### 移行後の実装方針

#### 1. SpriteAnimationベースのエフェクト

**ヒットエフェクト**:
- 複数フレームのスプライトシート（ヒット時の衝撃波・火花など）
- Animatorによる再生制御
- エフェクト完了時にイベント通知

**カード選択エフェクト**:
- 複数フレームのスプライトシート（グロー・オーラなど）
- 選択時はループ再生
- 選択解除時はフェードアウト

#### 2. 実装アプローチ

```csharp
// HitEffectView.cs (改良版)
[SerializeField] private Animator hitAnimator;
[SerializeField] private Image effectImage;  // エフェクト専用Image

public async UniTask PlayHitEffectAsync(CancellationToken ct)
{
    effectImage.gameObject.SetActive(true);
    hitAnimator.SetTrigger("Hit");

    // アニメーション完了を待つ
    await UniTask.WaitUntil(() => !hitAnimator.IsInTransition(0), cancellationToken: ct);

    effectImage.gameObject.SetActive(false);
}
```

#### 3. 必要なアセット

**スプライトシート**:
- `HitEffect_SpriteSheet.png` (8フレーム想定)
- `CardGlow_SpriteSheet.png` (4フレーム想定、ループ用)

**Animation Clips**:
- `HitEffect_Hit.anim` (0.2秒、ワンショット)
- `CardGlow_Loop.anim` (0.4秒、ループ)
- `CardGlow_FadeOut.anim` (0.15秒、フェードアウト)

#### 4. 利点

1. **ビジュアル品質**: 専用のエフェクト画像で表現力向上
2. **アーティストフレンドリー**: スプライトを差し替えるだけでビジュアル変更可能
3. **パフォーマンス**: Animatorの最適化を活用
4. **拡張性**: 多様なエフェクトを追加しやすい

#### 5. 段階的な移行

**Step 1**: 既存のコードベースエフェクトはそのまま動作させる
**Step 2**: エフェクト用スプライトアセットを作成
**Step 3**: Animatorベースの実装を追加（既存と並行稼働）
**Step 4**: テストして問題なければ既存のコードベースエフェクトを削除

### 着手タイミング
**アートアセットが準備できた後**

具体的には以下が完了してから:
- エフェクト用スプライトシートの作成
- Phase 4以降の機能実装完了
- バトルシステムの安定動作確認

### 関連ドキュメント
- [Phase3_カード効果の視覚化完了.md](Phase3_カード効果の視覚化完了.md) - 現在のエフェクト実装

---

## 3. プレイヤーへのダメージエフェクト実装

### 概要
現在、敵へのダメージエフェクト（ヒットエフェクト + ダメージ数値表示）のみ実装されています。プレイヤーが敵から攻撃を受けた際のエフェクトも実装します。

### 現在の実装状況
- **敵へのダメージ**: ヒットエフェクト ✅ + ダメージ数値表示 ✅
- **プレイヤーへのダメージ**: エフェクトなし ❌ + ダメージ数値表示なし ❌

### 実装が必要な内容

#### 1. プレイヤー用ヒットエフェクト
**ファイル**: 新規作成 `Assets/Olca/Presentation/Battle/PlayerView.cs`

プレイヤーのHP表示エリアに`HitEffectView`を追加し、ダメージを受けた際にエフェクトを再生します。

```csharp
public class PlayerView : MonoBehaviour
{
    [SerializeField] private Image playerImage;  // プレイヤーのImage
    [SerializeField] private HitEffectView hitEffectView;

    private PlayerCharacter player;
    private int previousHp;

    public void Initialize(PlayerCharacter player)
    {
        this.player = player;
        this.previousHp = player.CurrentHp;
    }

    public void RefreshWithDamageDetection(DamageNumberPool damageNumberPool)
    {
        int currentHp = player.CurrentHp;
        int damage = previousHp - currentHp;

        if (damage > 0)
        {
            ShowDamageEffectAsync(damage, damageNumberPool).Forget();
        }

        previousHp = currentHp;
    }
}
```

#### 2. 敵の攻撃イベントのハンドリング
**ファイル**: [Assets/Olca/Core/Logic/Battle/BattleSystem.cs](../Assets/Olca/Core/Logic/Battle/BattleSystem.cs)

敵の攻撃イベント `OnEnemyAttack` を追加（プレイヤー攻撃の `OnPlayerAttack` と同様）。

#### 3. BattleViewへの統合
**ファイル**: [Assets/Olca/Presentation/Battle/BattleView.cs](../Assets/Olca/Presentation/Battle/BattleView.cs)

- PlayerViewを追加
- `OnEnemyAttack`イベントを購読
- プレイヤーのHP変化を検出してエフェクトを再生

### 着手タイミング
**Phase 3完了後、Phase 4実装前**

具体的には以下が完了してから:
- Phase 3: カード効果の視覚化完了
- 敵へのダメージエフェクトの動作確認完了
- バトルシステムの基本動作が安定

### 関連ドキュメント
- [Phase3_カード効果の視覚化完了.md](Phase3_カード効果の視覚化完了.md) - 現在のエフェクト実装
- [HitEffectView.cs](../Assets/Olca/Presentation/Effects/HitEffectView.cs) - ヒットエフェクトの実装

---

## 将来追加予定の項目（未定）

以下は、プロジェクトの進行状況に応じて追加される可能性があります:

- UIアニメーションの統一（Animator移行の拡大）
- パフォーマンス最適化（オブジェクトプール等）
- エフェクトシステムの拡張
- セーブ/ロードの拡張（チェックポイント・成功/敗北/撤退帰還・次回出撃の保存は実装済み。ショップ/イベント等の未接続部分は別途）

---

作成日: 2026-01-13
最終更新: 2026-01-13
