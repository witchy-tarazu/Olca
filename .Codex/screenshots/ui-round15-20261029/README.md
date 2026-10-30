# 編成の長い検証文とカード行境界（2026-10-29）

View専用隔離データを使用。検証文1197文字中57、長いカード行436文字中111しか見えず、行末が詳細ボタンへ重なる状態を再現。PNG原本は切抜き・縮小・合成なし。寸法・SHA256はmanifest参照。

- 検証文と長い行だけ既存の読取専用全文へ接続。行の幅と全文入口の余白を調整し、元のカード詳細／追加／削除ボタンは維持。
- 未解決カードは行の名前・所持／編成数・未解決表示を閲覧できる。定義を捏造せず、カード詳細は無効のまま。
- 保存待ちの初期フォーカスは再試行を維持し、追加の入口へ飛ばさない。全文開閉では業務callback0。

| 解像度 | 修正前 | 修正後 | 保存待ち |
| --- | --- | --- | --- |
| 4by3 | [before](boundary-before-4by3.png) | [after](boundary-after-4by3.png) | [pending](pending-after-4by3.png) |
| fhd | [before](boundary-before-fhd.png) | [after](boundary-after-fhd.png) | [pending](pending-after-fhd.png) |
| 4k | [before](boundary-before-4k.png) | [after](boundary-after-4k.png) | [pending](pending-after-4k.png) |

全文：[検証文](validation-after.png)／[未解決行](unresolved-after.png)。既存の[カード詳細](card-after.png)も維持。

通常／保存待ちそれぞれ2周、全文末尾と閉じた入口へのフォーカスを確認。通常時の追加ボタン左端／削除ボタン右端で正しいcallbackが1回ずつ発火し、保存待ちは同じ操作を拒否。実データの追加・削除や保存は行わず、専用セーブハッシュは不変。View用定義／欠損例を製品へ登録しない。

実機pad・物理入力は未確認。高解像度画像を時間性能の保証としない。統一640×360の最終全件結果は Planning/既存UI監査と図鑑表示修正-20261028.md の検証メッセージとカード行節を参照。
