# 長い一覧名の内側余白（2026-10-29）

出撃先・スタイル・トークン交換の行で、長い名称が左右の装飾枠に接する状態を改善。対象行の文字領域だけ左右32／上下6だけ内側へ収め、ボタン外形・位置・クリック領域・背景・JikiTourai・既存best-fitを維持した。

画像は通常のUnity GameViewから取得したPNG原本。切抜き・縮小・合成なし。`manifest.json` に寸法とSHA256を記録。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| dungeon | 4by3 | [before](dungeon-before-4by3.png) | [after](dungeon-after-4by3.png) |
| dungeon | fhd | [before](dungeon-before-fhd.png) | [after](dungeon-after-fhd.png) |
| dungeon | 4k | [before](dungeon-before-4k.png) | [after](dungeon-after-4k.png) |
| style | 4by3 | [before](style-before-4by3.png) | [after](style-after-4by3.png) |
| style | fhd | [before](style-before-fhd.png) | [after](style-after-fhd.png) |
| style | 4k | [before](style-before-4k.png) | [after](style-after-4k.png) |
| exchange | 4by3 | [before](exchange-before-4by3.png) | [after](exchange-after-4by3.png) |
| exchange | fhd | [before](exchange-before-fhd.png) | [after](exchange-after-fhd.png) |
| exchange | 4k | [before](exchange-before-4k.png) | [after](exchange-after-4k.png) |

前後で同じ26件・同じ長い名称の検証入力を使用。出撃先0／基本スタイル／交換未選択の1/6ページで比較している。通常定義の複製を使うルートと行動値なしのスタイル、Viewだけに渡す仮商品であり、製品データの追加・購入・性能値変更はない。修正前はmaster `6d26c67` と同じC#、修正後は今回の最終2C#と一致。

3解像度・3一覧の実Canvasで全文の生成文字数一致、JikiTourai、文字領域の内包、ボタン中心のraycastを確認。新しい内側余白部分でも同じ行へ入力が届くことを3一覧で確認。選択・取消・再表示の反復、ページ保持、旧ボタンへの二重入力抑止、専用セーブのハッシュ不変も検証。Editorのポインターイベントであり、物理padの実機確認とは区別する。

関連27ケース（Jikiの長文9＋既存18）は成功。最終全件は開始前から640×360に統一し、高解像度の画像確認と分ける。結果は `Planning/既存UI監査と図鑑表示修正-20261028.md` の同日「長い一覧名」節を参照。今回の実画像確認を4K高負荷での時間保証とは扱わない。
