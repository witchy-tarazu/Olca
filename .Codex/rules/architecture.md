---
paths:
  - "Assets/Olca/Core/**/*.cs"
  - "Assets/Olca/Application/**/*.cs"
  - "Assets/Olca/Presentation/**/*.cs"
  - "Assets/Olca/Unity/**/*.cs"
---

# アーキテクチャルール

## レイヤー構造と責務

| レイヤー | 場所 | Unity依存 | 責務 |
|---------|------|-----------|------|
| Core | `Core/` | 禁止 | ゲームロジック・ドメインモデル |
| Application | `Application/` | 可（薄く） | ロジックとビューの仲介 |
| Presentation | `Presentation/` | 可 | 表示のみ（MonoBehaviour） |
| Unity | `Unity/` | 可 | Unity固有APIのラッパー |

## Core層の禁止事項

Core層（`Core/`配下）では以下を使用しない。

- `MonoBehaviour`、`Transform`、`GameObject`
- `Coroutine` / `IEnumerator` / `StartCoroutine`（プロジェクト全体で禁止）
- `UnityEngine.Random`（`IRandomProvider`を使う）
- `[SerializeField]` などのUnity固有属性
- `UnityEngine`名前空間のクラス全般

## Coroutineの代替

```csharp
// 禁止
StartCoroutine(AnimateOpen());

// 代替: UniTask または async/await
public async UniTask AnimateOpenAsync(CancellationToken ct)
{
    await transform.DOAnchorPosX(960f, 0.3f).ToUniTask(cancellationToken: ct);
}
```

## 固定論理フレームの更新

ゲーム進行は描画から独立した60 Hzの論理フレームで更新する。`Update`から描画の`Time.deltaTime`をそのままCoreへ渡さない。
`UnityLogicalLoop`が単調時計の経過量を蓄積し、`LogicalFrameClock`で入力→ゲーム更新を順に実行する。追いつき処理の上限を超えた分は保持し、捨てない。
既存の`Tick(int milliseconds)`は互換境界であり、60回で正確に1000msとなる整数差分を渡す（16ms固定や描画ごとの丸めは禁止）。入力は論理フレームに割り当てて一度だけ消費する。
表示の補間・非同期アニメーション完了通知をゲームの時間や効果発動の根拠にしない。ポーズ中の時間は加算せず、既に獲得した未処理フレームは保持する。

## インターフェース抽象化

Unity固有機能は必ずインターフェースでラップする。

- 乱数: `IRandomProvider`
- 時間: `ITimeProvider`
- 入力: `IInputProvider`
- ログ: `IGameLogger`

## 仕様書の参照先

- ゲームロジック: `Planning/バトルシステムについて`、`Planning/ダンジョンについて`
- ビジュアル: `Planning/ビジュアル・アート/`（ゲームロジックは含まない）
