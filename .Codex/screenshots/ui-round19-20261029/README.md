# 図鑑の表示・入力境界（2026-10-29）

通常のControllerから図鑑を開き、同じ図鑑Viewへ隔離した既知カード／レシピを表示して再現。製品定義・実セーブは変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- cards：4:3で詳細ボタンの右端が画面外へ切れる。図鑑に既存の1920×1080基準配置と等比縮尺を適用し、横長時も高さに収める。
- recipe：レシピ結果の性能も同じ基準で表示し、往復・リサイズ後の位置と入力を保持する。
- FHDは図鑑／レシピの元の配置・サイズを維持。背景・JikiTourai・表示文・製品性能・保存形式は変更しない。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| cards | 4by3 | [before](cards-before-4by3.png) | [after](cards-after-4by3.png) |
| cards | fhd | [before](cards-before-fhd.png) | [after](cards-after-fhd.png) |
| cards | 4k | [before](cards-before-4k.png) | [after](cards-after-4k.png) |
| recipe | 4by3 | [before](recipe-before-4by3.png) | [after](recipe-after-4by3.png) |
| recipe | fhd | [before](recipe-before-fhd.png) | [after](recipe-after-fhd.png) |
| recipe | 4k | [before](recipe-before-4k.png) | [after](recipe-after-4k.png) |

[保存待ち4:3](pending-after-4by3.png)／[保存待ちFHD](pending-after-fhd.png)。再試行の従来下端は基準画面を2.5越えていたため、Y=-510・高さ50へ局所修正。独立レビューと境界testで確認し、修正後は画面下端に5の余白・戻るとの間隔を保つ。保存障害は注入していない。

通常経路では右上補助ボタンは図鑑中に非表示となり、タブとの重なりはない。前周のclearReturnViewへ直接表示したfixtureでの近接と区別し、不要なtoolbar変更は行っていない。

pointer／EventSystemでボタン中心と左右端のraycast、タブ・詳細・保存待ち・他画面への反復復帰、古い／背面入力拒否を確認。業務callback0・専用セーブハッシュ不変。実機入力・実保存障害・高解像度での時間保証は未確認。最終全件は640×360の統一条件で別途確認し、結果はPlanningの本節参照。
