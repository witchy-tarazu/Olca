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
通常進行は`TickFrame`を使い、時間制御へ16/17msの差分を戻さない。旧`Tick(int milliseconds)`は互換入口として維持する。両入口を共用する時間・速度計算は1/3000秒単位（1ms=3、1論理フレーム=50）で精度を保持し、表示用msへの丸め値を再入力しない。移動・ゲージは整数除算の余りを次の更新へ繰り越す。入力は論理フレームに割り当てて一度だけ消費する。
保存再試行は待機時間と同期I/O時間の前後で時計の観測点を区切り、既に獲得した未処理フレームを消さずに保持する。
描画停止中の物理入力は現在のInputポーリングでは時刻付き収集できない。決定性の検証は同じ論理入力列・初期状態・乱数seed/呼出し順の範囲で行い、任意の物理入力や保存復帰後の全乱数列一致を保証しない。
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
