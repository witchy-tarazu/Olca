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
通常Controllerの入力は既存Input Systemの生成時刻付きイベントから採取する。追いつき処理の前に、イベント時刻までの有効経過時間から入力フレームを割り当てる。同じフレームの押下・解放は押下エッジと最終保持状態を併合し、翌フレームへ遅延させない。ポーズ中の入力は破棄し、復帰時の状態から既存の全キー解放待ちと2フレーム遮断を処理する。
決定性の検証範囲は同じイベント時刻列・初期状態・乱数seed/呼出し順で、対象フレームの処理前に届いたイベント。既に処理済みの時刻への遅着は巻き戻さず次の未処理フレームへ置き、LateInputEventsに数える。OS/デバイス側の欠落、異なる時計の校正誤差、任意の物理入力や保存復帰後の全乱数列一致は保証しない。
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

## 障害物のチェックポイント

地上の即時ダメージ型とカード破棄型を保存・復元する。障害物なしは版1、即時ダメージ型のみは版2を維持し、破棄型を含むマップは版3とする。未対応の状態異常型・空中配置・負値・不正な通過情報は拒否する。
破棄は現在デッキの枚数を上限とし、獲得履歴を削除しない。残デッキ順と獲得順は一意インスタンスIDで別々に保存し、同じ個体が両方にある場合は復元後も同じ参照にする。通過済み障害物は再適用しない。保存タイミングは既存の戦闘開始・報酬確定等を維持し、障害物通過時の新しい自動保存は追加しない。
