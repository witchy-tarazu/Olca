# 保全済み改善の公開単位

対象: Stage51〜53、63〜67、70〜76、79〜80、83。共通ファイルに変更が重なるため、全3821件で検証した最終20ファイルを一体で反映する。未検証の新規変更は含めない。

## 変更

|Stage|目的|
|---|---|
|51|探索編成の同一表示内snapshotを共有|
|52–53|音量設定の背景とキー割当文字の可読性|
|63|共通詳細の表示組立を整理|
|64|Custom終了処理を既存Releaseへ統一|
|65–66|詳細/インライン説明の解像度変更時の読取位置保持|
|67|漢字を含まない表示文字の変換fast path|
|70|カメラ診断ログの明示opt-in|
|71/73|fallback/敵marker生成Sprite・Textureの所有と解放|
|72|探索編成のcallbackを責務ごとに分離|
|74|カード演出の旧非同期要求が新要求へ作用しないよう所有を管理|
|75|HUD再生成時に現在の操作案内を反映|
|76|ダメージ数値群の上端補正計算を分離|
|79|探索手札の直接選択・初期左端・表示再生成時の保持|
|80|探索巡回と24/9論理frame保持・旧設定のメモリ補完と交換/保存互換|
|83|未登録手札の通常入口にも実割当/機器に追従する移動・Jump案内|

Custom、GameField、Outsideの各機能はそれぞれ同一ファイルを共有する。テストのみ/中間ファイルだけの分割公開は行わない。元背景、JikiTourai、保存互換、固定60Hzを保全。

## 検証と復帰時照合

- 2026-11-05 Stage83: 全3821成功（255.85秒）、console error0。関連は初回3+管理95+Custom388の486実行（新3は重複）、反復6成功。gateのrelated-testsは実際の最終Custom388 runをそのまま要約。
- full-testsのfreshRun log offset362466015とSHAを保持。全件を再投入せずwrapper timeout後に同runを回収。
- 2026-11-07: submission/project双方の20source SHA、25packet、保存manifestを再照合。Editor/未完runなし。新しいUnityテストやlive描画測定を行ったとは扱わない。
- 取込remoteは68eea39まで。remote差分はPlanning2文書だけで、検証済みコード/資産の変更なし。
- 元project clean、隔離保存（通常flow5/guide3）不変、過去実640×360両RT/TMP/saveguard/生成資源・EventSystem cleanupを原証跡として保持。
- 既知warning59とServerDiscovery環境warning1は分離済み。初回失敗やPipeline等の履歴を解決済み扱いしない。

## 表示比較

[変更前](outside-before.png) / [変更後](outside-after.png)。実FHD。world位置/frameが僅かに異なるため全画面pixel一致比較とはしない。4K/4:3は当時の描画・glyph計測、独立画像レビューはFHD。

## 未実装・限界

カード使用とS10の切替/編集反映/選択中削除、外界効果登録は別件。Touchの未接続移動を案内しない。実build/物理入力/聴感/全HotReload、歴史的UGUIや登録外Sprite群の解決を主張しない。ポーズ後に巡回ボタンを保持する間の同pad移動は既存neutral保護を維持。

公開ゲート用5JSONは元の成功値を変更せず形式を揃えたもの。provenance.jsonに元ファイルSHAを記録。commit/pushは別操作で実施する。
