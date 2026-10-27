# 状態・補助アイコン（2026-10-27）

OpenAI画像生成ツールで人物参照なしの3×3アイコン図案を新規作成し、承認済みの最近傍・限定パレット・透過調整で個別64×64 PNGへ書き出した仮素材。既存の正式/ユーザー素材の置換ではない。元図案は検証環境で保持。セル境界に入った隣絵の小片は透過領域として除去し、各アイコンの形を確認した。

|ID|対応|図柄|
|---|---|---|
|Poison|StatusEffectType.Poison（毒種別間で共用）|緑の毒滴/ドクロ|
|Paralyze|StatusEffectType.Paralyze|黄の稲妻|
|Confusion|StatusEffectType.Confusion|マゼンタの渦|
|Blind|StatusEffectType.Blind|斜線の目|
|Guard|既存Guard.IsActive|シアンの盾|
|Barrier|既存HasBarrier|青の六角バブル|
|Aura|既存HasAura|紫の輪/白い核|
|Float|既存TerrainState.IsFloating|羽と上向き印|
|Concentrate|既存IsConcentrating|橙の照準|

全ファイルはalpha0/255の真透過。各図柄は紺の輪郭と少数段階の色で小表示を想定する。キャラ/背景/JikiTouraiやカード本絵は変更しない。

Resources/OlcaCombatStatus/*.bytesは各PNGと完全同一の実行時包装。CombatStatusIconCatalogが色版/グレー版を非圧縮32bit/Pointで共有する。既定画像import圧縮に依存せず、.metaを手動編集/公開しない。Rendererは共有Spriteを破棄しない。

CombatStatusIconStripはグレー版の上へ残量に応じた色版を重ねる。残時間の数値や入力判定は追加しない。PlayerはHP近傍の4列、ボスは各固定ゲージの隣の縦列、通常敵は非表示。時間のない補助は解除まで全色で表示する。新しい能力や製品値を定義したものではない。
