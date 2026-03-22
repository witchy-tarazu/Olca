# アーキテクチャ設計

## レイヤー構造

```
Assets/Olca/
├── Core/              # Unity非依存ゲームロジック
│   ├── Domain/       # ドメインモデル（Card, Deck, PlayerCharacter, Enemy等）
│   ├── Logic/        # ビジネスロジック
│   │   ├── Battle/   # BattleSystem, CustomGaugeSystem, CardUsageSystem等
│   │   ├── CardEffects/ # ICardEffect, CardEffectFactory等
│   │   ├── Dungeon/  # DungeonSystem, DungeonMap, PlayerMovement等
│   │   ├── Integration/ # DungeonBattleManager
│   │   └── Player/   # プレイヤー管理
│   └── Interfaces/   # IRandomProvider, ITimeProvider, IInputProvider, ILogger
├── Application/      # ロジックとビューの仲介（Controller）
│   └── Dungeon/      # DungeonBattleController
├── Presentation/     # Unity依存のView層（MonoBehaviour継承）
│   ├── Battle/       # BattleController, BattleView, EnemyView等
│   ├── CustomScreen/ # CustomScreenView, HandCardView等
│   ├── Dungeon/      # DungeonView
│   └── Effects/      # CardEffectView, DamageNumberPool等
├── Unity/            # Unity固有機能のラッパー
│   ├── UnityRandomProvider.cs
│   ├── UnityTimeProvider.cs
│   ├── UnityInputProvider.cs
│   └── UnityLogger.cs
└── Tests/            # NUnitテスト
    ├── Battle/       # BattleSystemMovementTests等
    ├── Card/         # CardUsageSystemTests, CustomScreenSystemTests等
    ├── CardEffects/  # CardEffectSystemTests等
    ├── Domain/       # CardMasterParseTests等
    ├── Dungeon/      # DungeonSystemTests, PlayerMovementTests等
    ├── Integration/  # DungeonBattleManagerTests
    └── Mocks/        # MockRandomProvider, MockTimeProvider等
```

## アセンブリ定義
- `Olca.Core.asmdef` → Core層
- `Olca.Presentation.asmdef` → Presentation層
- `Olca.Unity.asmdef` → Unity層
- `Olca.Tests.asmdef` → テスト層

## 設計原則
1. **ロジックと表示の完全分離**: Core層はUnity非依存
2. **依存性注入**: コンストラクタインジェクション優先
3. **Coroutine禁止**: UniTask/async-awaitを使用
4. **インターフェース抽象化**: Unity固有機能はIRandomProvider等でラップ
5. **イベント駆動**: Update内でのポーリングを避け、eventを活用

## 名前空間の競合対策
- `Vector2Int`: `UnityEngine.Vector2Int` vs `Olca.Core.Domain.Vector2Int`
  → エイリアスか完全修飾名で解決
- テスト名前空間: `namespace Olca.Tests.CardTests`（型名と競合しない形式）

## 仕様書の役割分担（Planning/配下）

1. **ゲームシステム仕様** (`Planning/バトルシステムについて`, `Planning/ダンジョンについて`)
   - ゲームロジック・ルール・計算式（コスト計算、選択制限ルール、シリーズボーナス等）
   - **実装時はこちらを参照してロジックを組む**

2. **ビジュアル・UI仕様** (`Planning/ビジュアル・アート/`)
   - 画面レイアウト、アニメーション、演出、色・サイズ・配置等の視覚要素
   - **ゲームロジックは含まない（UI/UXのみ）**

注意: カスタム画面実装時は両方を参照すること:
- ゲームロジック → `Planning/バトルシステムについて` の「4.3. カードコストと選択ルール」
- ビジュアル → `Planning/ビジュアル・アート/カスタム画面仕様.md`
