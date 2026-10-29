# 同名カード・レシピの対応監査（2026-10-29）

画像は同名項目0／1の行・性能の対応比較。今回確認した範囲で誤対応は再現せず、製品コード・見た目の変更はない。通常Controllerから開いた図鑑Viewへ、既存CollectionDisplay経由の隔離データを供給して確認した。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

| 位置 | 検証用ID | 名前 | 威力 | 知識 |
| --- | --- | --- | --- | --- |
| 0 | shared | おなじナマエ | 11 | hash付きの既知定義 |
| 1 | shared | おなじナマエ | 28 | 0と異なるhashの既知定義 |
| 2 | shared | おなじナマエ | 45 | 未登録の別hash・性能入口なし |

9定義は同じ名前でhashが異なり、既知6／未知3。上記はテスト用の値で製品データへ登録していない。項目0と1は同じID／名前でも、本文のカクニンNo_0／No_1と異なる威力へ正しく対応する。

| 表示 | 解像度 | 項目0（威力11） | 項目1（威力28） |
| --- | --- | --- | --- |
| card | 4by3 | [0](card-0-4by3.png) | [1](card-1-4by3.png) |
| card | fhd | [0](card-0-fhd.png) | [1](card-1-fhd.png) |
| card | 4k | [0](card-0-4k.png) | [1](card-1-4k.png) |
| recipe | 4by3 | [0](recipe-0-4by3.png) | [1](recipe-1-4by3.png) |
| recipe | fhd | [0](recipe-0-fhd.png) | [1](recipe-1-fhd.png) |
| recipe | 4k | [0](recipe-0-4k.png) | [1](recipe-1-4k.png) |

長い同名行の全文：[FHD](long-info-fhd.png)／[4K](long-info-4k.png)。

Controllerの供給順は読み取り監査で確認。CardRowsと詳細が同じLv3除外／DefinitionHash集約順で、旧記録だけの行にはnull詳細を対応させる。レシピは同じ行のResult／Performanceを使い、名前検索で結び付けていない。

通常1周・保存待ち1周で、カードとレシピの性能0／1、未登録の同名同ID定義の非表示、逆順更新後の新index1→元定義7（威力130）、旧カード／レシピ／戻る拒否、全文入口への復帰を確認。業務callback0・専用セーブハッシュ不変。保存待ちはView fixtureで、実保存障害は起こしていない。

製品値・公開条件・背景・JikiTourai・保存／Unity参照は維持。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果はPlanningの本節参照。
