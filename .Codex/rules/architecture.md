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

## deltaTime受け渡しパターン

```csharp
// Core層: deltaTimeを外部から受け取る
public void Tick(float deltaTime) { ... }

// Application/Presentation層: Unity側で渡す
private void Update() { coreSystem.Tick(Time.deltaTime); }
```

## インターフェース抽象化

Unity固有機能は必ずインターフェースでラップする。

- 乱数: `IRandomProvider`
- 時間: `ITimeProvider`
- 入力: `IInputProvider`
- ログ: `IGameLogger`

## 仕様書の参照先

- ゲームロジック: `Planning/バトルシステムについて`、`Planning/ダンジョンについて`
- ビジュアル: `Planning/ビジュアル・アート/`（ゲームロジックは含まない）
