# 装備・パーツ編集の視覚整理（Round54）

- 装飾品／所持パーツ、配置盤面、選択パーツと操作、保存状態を面で分ける。既存の水鏡面材とJikiTouraiを使用し、全面の淡い板と背面HUDの透けを整理した。元背景アセットは変更しない。
- 装備中／候補、配置数／所持数と入力フォーカスを分離。登録されたPartShape.Cellsを等比の一覧miniへ、RotatedCellsを回転プレビューへ接続。製品の専用絵を推測で作らず、実際の形状がわかる表示を補完した。
- 盤面の座標をセル外の軸へ移し、未解放X・配置個体番号・選択輪郭を整理。ページ番号と最終ページの操作を表示。4:3は1920基準面を画面幅へ収め、補助文字を局所増量する。等比形状と入力意味を保持する。
- 長い登録IDはgrapheme単位で一覧末尾を明示し、focus／hoverで読み取り専用の全文欄へ表示。登録IDをrich textとして解釈せず、情報表示だけでは装備を切り替えない。ポインタを全文欄へ移しても表示を維持し、必要な縦スクロールを使える。別の短名行か再描画で閉じる。
- 候補なしは空欄にせず明示する。Core、Controller、装備／配置規則、能力集計、消費・入手、保存契約と取消は変更しない。Catalogは製品名称・専用絵・説明を持たないため、能力値や正式素材をUI側で創作しない。

## 比較画像

実Unity Game Viewの全画面。FHD=1920×1080、4K=3840×2160、4:3=1440×1080。比較は同じ検証用登録定義・所持状態を使用し、製品値を追加したものではない。

| 状態 | 修正前 | 修正後 |
| --- | --- | --- |
| 通常 FHD | [before](before-normal-fhd.png) | [after](after-normal-fhd.png) |
| 選択形状 4K | [before](before-selected-4k.png) | [after](after-selected-4k.png) |
| 通常 4:3 | [before](before-normal-4by3.png) | [after](after-normal-4by3.png) |
| 長名 4:3 | [before](before-dense-4by3.png) | [after](after-dense-4by3.png) |
| 所持なし FHD | [before](before-empty-fhd.png) | [after](after-empty-fhd.png) |

各状態の3解像度を同梱。全文欄は修正後のみ[4:3](after-dense-name-4by3.png)。画像編集合成なし。名称・盤面寸法・所持数・形状は隔離fixtureであり、新しい製品設定ではない。装備能力や個別アイコンを捏造したものでもない。

## 保護と限界

背景・フォント・保存互換・固定論理フレーム・既存能力値・効果時間を維持。反転入口は追加しない。表示上のmini／形状補完であり、攻撃本体アニメや製品固有の装飾品絵の完成とは別である。物理パッド・タッチ端末・IMEは未確認。仮想パッドと実EventSystem Raycastの検証を実機確認と混同しない。

## 検証

最終結果・実操作・保存再読込・独立レビューは併設JSONへ記録。最終全件は実640×360、専用TMPDIR／保存ガードで実行し、画像確認後は低解像度と入力デバイスを復元する。途中のprivate検証helperのコンパイル／初期デッキ／ページ所在の設定ミスは製品例外と分け、修正後の実経路結果だけを成功根拠にする。

実Raycastで装備・配置・回転・重なり／未解放拒否・解除・取消を2周確認し、取消前後の保存hash不変。保存後の再表示と古い保存ボタン3回の無効化、装備解除の保存、同じ保存先でEditor Playを停止／再開した通常Controllerの復帰を確認。装備1件・配置2件が一致し、仮想パッドEast取消でも保存hash不変だった（actual-results／reload-result）。保存障害注入なし。

独立コード／原寸視覚レビューで具体指摘を修正し、本範囲の最終未解決0。Play後はerror0／warning1で、警告は既存Unity AI接続情報のread-only保存先。認証や権限設定は変更しない。全件テストの警告は別記する。

最終関連42件、全EditMode **3,486件成功**（257.95秒、失敗／skip0）。console error0／warning62を実ログ分類へ分離。最終ソースの全件要求は1回で同一runを追跡。表示後の長さ判定を揃える前の別ソースで3485成功（274.29秒）があり、最終結果とは分けて記録する。CoreとControllerは無変更。

最終ソースで追加の全文欄ホイールを実Raycastで確認。270文字の隔離IDでhover→一覧からポインタ退出→全文欄wheel→取消を通し、全文維持・スクロール移動・装備0件・保存hash不変を確認した（scroll-result）。[長文スクロールFHD](after-long-scroll-fhd.png)はこのストレスfixtureの修正後のみ。漢字を読みへ変換した後に長くなるIDも全文条件へ含め、生ID／保存値は変えていない。

公開前に実640×360、専用TMPDIR／保存ガード、Controller inactive、全tracked Assets一致（既存private URP2件を除く）、原本cleanを確認。仮想入力は残らずnative Mouse／Keyboardのみ。

after-reloadedの3画像は実保存の再読込後のみ（装備1件・配置2件）。初期配置1件のbefore／after比較とは分離する。最終ソースの原寸画像も再取得し、selected 4:3／長名4K／wheel途中FHDを独立再確認、追加指摘0。
