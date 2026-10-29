# 長い説明・条件・保存エラーの全文（2026-10-29）

固定高の説明や未解放条件に長文を渡すと途中で欠けることを再現。短文は既存表示を保ち、長文は右上補助操作を避けた領域へ折り返す。収まらない場合だけ「ゼンブン」から既存のスクロール詳細画面を利用し、文字サイズを無制限に縮小しない。

前後で同じView表示用入力を使用。長いスタイル名、未解放条件、40行のエラーは安全な検証用文字列で、製品の新しいデータではない。保存を失敗させる操作・権限変更・実ファイル障害注入はしていない。カード詳細のフォント・特性アイコン・スクロール処理を維持し、非カード本文には特性アイコンを付けない。

画像はUnity GameViewのPNG原本（切抜き・縮小・合成なし）。寸法とSHA256はmanifest参照。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| info | 4by3 | [before](info-before-4by3.png) | [after](info-after-4by3.png) |
| info | fhd | [before](info-before-fhd.png) | [after](info-after-fhd.png) |
| info | 4k | [before](info-before-4k.png) | [after](info-after-4k.png) |
| dungeon-error | 4by3 | [before](dungeon-error-before-4by3.png) | [after](dungeon-error-after-4by3.png) |
| dungeon-error | fhd | [before](dungeon-error-before-fhd.png) | [after](dungeon-error-after-fhd.png) |
| dungeon-error | 4k | [before](dungeon-error-before-4k.png) | [after](dungeon-error-after-4k.png) |
| style-error | 4by3 | [before](style-error-before-4by3.png) | [after](style-error-after-4by3.png) |
| style-error | fhd | [before](style-error-before-fhd.png) | [after](style-error-after-fhd.png) |
| style-error | 4k | [before](style-error-before-4k.png) | [after](style-error-after-4k.png) |
| exchange-error | 4by3 | [before](exchange-error-before-4by3.png) | [after](exchange-error-after-4by3.png) |
| exchange-error | fhd | [before](exchange-error-before-fhd.png) | [after](exchange-error-after-fhd.png) |
| exchange-error | 4k | [before](exchange-error-before-4k.png) | [after](exchange-error-after-4k.png) |

全文表示・末尾の例：

- [出撃説明4K](info-details-after-4k.png)
- [未解放ダンジョン条件4K](locked-details-after-4k.png)
- [未解放スタイル条件4K](style-locked-details-after-4k.png)
- [保存エラー末尾FHD](dungeon-error-details-after-fhd.png) / [4K](dungeon-error-details-after-4k.png)
- [スタイル保存エラー末尾4K](style-error-details-after-4k.png)
- [交換保存エラー末尾4K](exchange-error-details-after-4k.png)

初回配置は右上の装備ボタンとFHDでraycastが衝突したため修正。最終配置で全文入口・既存補助操作・未解放行内の入口がそれぞれ入力を受けることを確認。開閉2周・スクロール末尾・リサイズ・元の全文入口へのフォーカス復帰を確認し、業務callbackは0回、専用セーブのファイルハッシュは不変。Editorイベントの検証で、物理pad確認や本物の保存失敗確認を意味しない。

最終全件は640×360固定。高解像度の画像確認を実時間の性能保証とは扱わない。結果は Planning/既存UI監査と図鑑表示修正-20261028.md の「長い説明・条件・エラー」節を参照。
