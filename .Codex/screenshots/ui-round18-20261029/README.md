# 全文更新と他画面への復帰（2026-10-29）

隔離したViewデータによる再現。製品データ・本物のセーブは変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- stale：カード情報を更新した後、更新前の詳細ボタンの保持イベントを実行。修正前は新しい一覧の上に古いカード全文が復活する。修正後は拒否し、現在の詳細操作から新しい情報を開ける。
- return：レシピ詳細から他画面へ表示を切り替え、以前の戻る操作を実行。修正前は古い図鑑が復活する。修正後は現在のスタイル画面を保持する。
- 入力を所有する現在の画面／モーダルだけがcallbackを実行する。非表示・破棄済み・無効・背面の操作は拒否。レイアウト・背景・フォントは変更していない。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| stale | 4by3 | [before](stale-before-4by3.png) | [after](stale-after-4by3.png) |
| stale | fhd | [before](stale-before-fhd.png) | [after](stale-after-fhd.png) |
| stale | 4k | [before](stale-before-4k.png) | [after](stale-after-4k.png) |
| return | 4by3 | [before](return-before-4by3.png) | [after](return-after-4by3.png) |
| return | fhd | [before](return-before-fhd.png) | [after](return-after-fhd.png) |
| return | 4k | [before](return-before-4k.png) | [after](return-after-4k.png) |

2周の実Play検証で、旧詳細・背面ページ／戻る／cancel・非表示中のcloseの拒否、所持カード詳細の更新、閉じた入口への焦点復帰を確認。さらに全文を実際に開いたまま更新する経路を2周確認。業務callback0・専用セーブハッシュ不変。

pointer／EventSystemと保持イベントを用いた再現で、物理pad・キーボードや実保存障害を検証した扱いにしない。高解像度画像は表示検証であり、全件テストは640×360の統一条件で実施。最終結果は Planning/既存UI監査と図鑑表示修正-20261028.md の本節参照。
