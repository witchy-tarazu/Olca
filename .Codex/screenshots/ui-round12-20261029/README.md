# 保存デッキ一覧の長文エラー・空一覧（2026-10-29）

同じView専用検証データによる前後比較。Unity GameViewのPNG原本を切抜き・縮小・合成せず保存。寸法・SHA256はmanifest参照。

- 4:3タイトル：修正前は左側の「はじめから」に隠れる。修正後は実toolbar間へ文字枠だけを移し、font36とY位置を維持。固定説明は初期解像度のフォント丸めにかかわらず右上操作からの余白を確保。
- empty：行を選択した後、空一覧へ更新。従来は空である案内がなく、図鑑にフォーカスが移っていた。修正後は案内を表示し、新しいデッキへ戻る。有効な再読込や外部操作の選択は保持。
- empty-error／filled-error：40行のエラーは従来917文字中65文字しか表示されなかった。既存の読取専用全文表示へつなぎ、保存待ちの既定は再試行に維持。行あり／空一覧を分けて確認。
- details：既存モーダルで末尾まで読み、開いたまま比率変更して閉じても入口へ復帰。ゲーム業務callbackは0。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| empty | 4by3 | [before](empty-before-4by3.png) | [after](empty-after-4by3.png) |
| empty | fhd | [before](empty-before-fhd.png) | [after](empty-after-fhd.png) |
| empty | 4k | [before](empty-before-4k.png) | [after](empty-after-4k.png) |
| empty-error | 4by3 | [before](empty-error-before-4by3.png) | [after](empty-error-after-4by3.png) |
| empty-error | fhd | [before](empty-error-before-fhd.png) | [after](empty-error-after-fhd.png) |
| empty-error | 4k | [before](empty-error-before-4k.png) | [after](empty-error-after-4k.png) |
| filled-error | 4by3 | [before](filled-error-before-4by3.png) | [after](filled-error-after-4by3.png) |
| filled-error | fhd | [before](filled-error-before-fhd.png) | [after](filled-error-after-fhd.png) |
| filled-error | 4k | [before](filled-error-before-4k.png) | [after](filled-error-after-4k.png) |

全文末尾：[FHD](details-after-fhd.png)／[4K](details-after-4k.png)。

Viewに渡した隔離profileだけを使用し、実デッキ削除・保存障害注入・製品データ変更は行っていない。専用セーブファイルは操作前後でハッシュ不変。新規・再読込・再試行の業務操作は実行せず、全文のpointer開閉と選択状態の復帰を確認した。実機pad確認ではない。

背景、JikiTourai、既存配置の方向、保存形式を維持。高解像度表示確認と、640×360固定での最終全件テストは別に実施。結果は Planning/既存UI監査と図鑑表示修正-20261028.md の保存デッキ一覧節を参照。
