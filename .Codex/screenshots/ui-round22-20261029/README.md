# 空・未公開のレシピと詳細復帰（2026-10-29）

通常のControllerから図鑑を開き、隔離した空／未公開／公開済みレシピで表示と入力を確認。製品定義・知識・実セーブは変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- empty：修正前は空白と1/1だけで空かどうか分かりにくい。件数0のときだけ「レシピはありません。」を表示する。カード側も種別に応じた同じ案内を使う。
- hidden：修正前はレシピ内容を保持した再表示でもカードタブへ焦点が戻る。修正後は表示中タブを初期焦点にする。明示された詳細・ページの戻り先は維持する。
- 未公開行は既存CollectionDisplay.RecipeRowsを通し、Result／Performanceがnullのまま。秘密名・性能入口・結果画像は表示しない。未公開情報を公開する仕様変更は行っていない。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| empty | 4by3 | [before](empty-before-4by3.png) | [after](empty-after-4by3.png) |
| empty | fhd | [before](empty-before-fhd.png) | [after](empty-after-fhd.png) |
| empty | 4k | [before](empty-before-4k.png) | [after](empty-after-4k.png) |
| hidden | 4by3 | [before](hidden-before-4by3.png) | [after](hidden-after-4by3.png) |
| hidden | fhd | [before](hidden-before-fhd.png) | [after](hidden-after-fhd.png) |
| hidden | 4k | [before](hidden-before-4k.png) | [after](hidden-after-4k.png) |

公開済み行の変換漏れ：[中間指摘時](return-before.png)／[修正後・詳細から3/3へ復帰](return-after.png)／[通常Controllerで再度開いたレシピタブ](reopen-after.png)。公開済み画像中の「ヒミツ」は検証用の名称で、未公開状態には現れない。

通常1周・保存待ち1周で、空→未公開→公開済み詳細→未公開／空→他画面を確認し、図鑑を閉じて通常Controllerから再開を確認。旧詳細・旧戻る・背面／他画面の旧入力は拒否し、業務callback0・専用セーブハッシュ不変。保存待ちはView fixtureで、実保存障害は起こしていない。

背景・JikiTourai・漢字だけの読替え・保存形式は維持。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果と一時的なPipelineログ／開始要求のタイムアウトはPlanningの本節参照。
