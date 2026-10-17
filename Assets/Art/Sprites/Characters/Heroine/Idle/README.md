# プレイヤー待機アニメーション

- 再生順: `heroine_idle_01.png` ～ `heroine_idle_10.png`（昇順）
- 想定フレームレート: **10fps**（1枚あたり0.1秒）
- 想定ループ時間: **1秒**（10枚を順番に再生）
- 各画像のサイズ: 256 × 256 px
- コピー元: `F:\おっぱい練習\成果物\ドット\idle_base`
- 元の `タイムライン1_0000.png` ～ `タイムライン1_0009.png` を上記の連番名へ対応させてコピー。

## Unityへの登録

- 待機Clip: `Assets/Art/Animations/Player/Player_Idle.anim`
- ダッシュClip: `Assets/Art/Animations/Player/Player_Dash.anim`（既存の1枚絵）
- Controller: `Assets/Art/Animations/Player/PlayerAnimator.controller`
- 登録先: `Assets/Scenes/DungeonScene.unity` の `DungeonRoot/Player/PlayerSprite`
- `GameFieldView` が `IsDashing && !IsInBattle` をAnimatorの `isRunning` に渡す。
  ダッシュ中はDash、それ以外は接敵の宙返り中も含めIdleをループする。
- 今後はClipのSpriteキーを差し替えることで素材を更新できる。
- Pivotは各Spriteの **Bottom Center（足元中央）**、Pixels Per Unitは **100** に揃える。
  現在の10枚は足元の切り出し位置が共通のため、この設定で既存の待機・ダッシュ画像と接地位置が一致する。
  差し替え時もSprite EditorでPivotを確認し、Transformの位置で補正しない。

登録処理のソースは `Tools/EditorTasks/RegisterHeroineAnimation.cs`。
既存ClipやControllerの上書きを防ぐため、新規登録用として扱う。
`.meta` ファイルの生成はUnity Editor側で行う。
