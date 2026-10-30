# 消えた全文入口のフォーカスと削除確認（2026-10-29）

同じ表示用データ・同じ操作での前後比較。通常のUnity GameView PNG原本で、切抜き・縮小・合成なし。寸法とSHA256はmanifest参照。

- focus：長いエラーの全文を開閉し、エラーを解消した表示へ更新。修正前は消えた入口に近い「手動保存」へフォーカスが移動。修正後は画面が指定する基本スタイルへ戻る。有効な全文入口やページ移動のnearestは維持。
- delete：非常に長い検証用デッキ名で、確認文が途中に隠れる状態を再現。修正後は名前だけをスクロールし、末尾と固定警告、確認・取消を同時に読める。追加ボタンなし。スクリーンショットは名前の末尾までwheelで移動した状態。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| focus | 4by3 | [before](focus-before-4by3.png) | [after](focus-after-4by3.png) |
| focus | fhd | [before](focus-before-fhd.png) | [after](focus-after-fhd.png) |
| focus | 4k | [before](focus-before-4k.png) | [after](focus-after-4k.png) |
| delete | 4by3 | [before](delete-before-4by3.png) | [after](delete-after-4by3.png) |
| delete | fhd | [before](delete-before-fhd.png) | [after](delete-after-fhd.png) |
| delete | 4k | [before](delete-before-4k.png) | [after](delete-after-4k.png) |

デッキはViewだけに渡した仮profileで、元の製品データ・既存セーブを変更していない。確認の決定イベントや実削除、実保存障害の注入は実行しない。名前スクロール・矢印の左右移動・取消2周、古い操作の拒否を確認し、専用セーブのハッシュは不変。名前の長さ制限や削除経済の新仕様は加えていない。

Editorのpointer／wheel／navigationイベントでの確認であり、実機pad確認とは区別する。PageUpDownの既存bindingを利用し、矢印は従来の確認／取消ナビゲーションへ維持。高解像度画像を性能時間の保証としない。最終全件は640×360固定で別に実行し、結果は Planning/既存UI監査と図鑑表示修正-20261028.md の「消えた入口と長文確認」節へ記録する。
