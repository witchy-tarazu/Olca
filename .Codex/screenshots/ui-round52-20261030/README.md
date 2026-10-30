# 削除選択・帰還後所持カード・多商品ショップの表示改善（Round52）

## 表示と素材参照

- 削除候補をカード名、専用mini、コスト／威力、持込／攻略中取得の列へ分離。赤茶色の「削除対象」と細い選択線は、現在の入力フォーカスとは別に確定対象を示す。末尾ページは背景面だけを最終行の下まで短くし、ページ・確定・取消の操作位置を固定。確定前は無変更である案内、取消と確定の位置・取引条件は保持。
- 帰還後所持一覧は専用mini、名称／レア度、枚数、既存詳細入口を列揃え。ValueHashを含む既存識別子で解決できた定義だけを表示し、絵や製品定義を捏造しない。4件以下は余白と下部操作を詰め、5件以上は8件ページングの高さを固定。ページを往復しても出撃先の名称を保持。実帰還時の外部補助操作に合わせて見出し面の幅を取り、成長情報面と一覧面を分離。既存補助ボタンの位置・文字サイズは維持した。
- 6商品以上は商品名・説明・価格・状態・miniを同一ScrollRectへまとめ、既存商品定義を最後まで閲覧できるようにした。スクロールバー／ホイールの位置を同じフォーカスが巻き戻す不具合を修正。方向ナビはスクロール後の実座標で更新し、新しい選択だけを可視範囲へ追従。
- 新規ラスタ生成は行っていない。不足していたのは削除／所持行への既存専用miniの参照接続であり、カード本絵の拡大代用やキャラクター攻撃本体素材の完成ではない。

## 比較画像

すべて実Unity Game Viewの画面全体。FHD=1920×1080、4K=3840×2160、4:3=1440×1080。同一の隔離表示fixtureを修正前後で使用した。normal/denseは画面を評価するためのデータであり、製品の商品表・価格・能力値を追加したものではない。実取引／実帰還の保存確認は別の証跡に分離する。

| 対象 | 修正前 | 修正後 |
| --- | --- | --- |
| 削除6枚 FHD | [before](before-removal-normal-fhd.png) | [after](after-removal-normal-fhd.png) |
| 実削除末尾ページ FHD | [before](before-removal-last-fhd.png) | [after](after-removal-last-fhd.png) |
| 削除長名 4:3 | [before](before-removal-dense-4by3.png) | [after](after-removal-dense-4by3.png) |
| 同一帰還保存・外部操作あり FHD | [before](before-returned-live-fhd.png) | [再読込後](live-returned-fhd.png) |
| 同一帰還保存・外部操作あり 4:3 | [before](before-returned-live-4by3.png) | [再読込後](live-returned-4by3.png) |
| 所持3件 FHD | [before](before-owned-normal-fhd.png) | [after](after-owned-normal-fhd.png) |
| 所持10件 4:3 | [before](before-owned-dense-4by3.png) | [after](after-owned-dense-4by3.png) |
| 店8商品 FHD | [before](before-shopmany-normal-fhd.png) | [after](after-shopmany-normal-fhd.png) |

同名の4k画像を含め、各状態の3解像度は本ディレクトリに保存。画像寸法・hashはimage-manifest.jsonに記録する。比較画像は編集合成していない。

## 保護条件と確認限界

背景・JikiTourai・既存の漢字部分のみカタカナ置換を維持。価格、削除規則、入手履歴、通貨・報酬・帰還保存タイミング、固定論理フレーム、保存形式は変更しない。表示fixtureは専用保存先で実行し、閲覧前後のファイルhashを比較する。

物理パッド・タッチ端末・IMEは未確認。仮想GamepadとEventSystemの実Raycast経路による確認を物理機器の確認とは扱わない。拒否済み画像生成の再試行は行っていない。攻撃本体モーション・未決製品値の残件は本単位に含めない。

## 最終検証

最終ソースを明示recompile後、関連374件＋入力5件、全EditMode3,480件成功（247.71秒、失敗／skip0）。console error0、警告62は[実ログ分類](warning-classification.json)へ分離。640×360、専用TMPDIRと保存ガードで実行し、画像QAとは分離。古いDLLでの途中テスト／画像は最終成功の根拠から除外した。最終ソースの全件要求は1回で同一runを追跡。追加視覚修正前には別ソースで全3480成功が2回あり、並行起動はしていない（prior-source-tests.json）。[検証条件・source hash](verification.json)。

実Raycastと仮想Gamepadで8商品末尾へ移動、ホイール／バー位置保持を確認。隔離の実8商品取引で末尾選択・取消・連打確定・退出保存を確認。別の実取引では削除2ページ目の7枚目を取消／確定、残デッキと取得履歴、回復後の次戦カスタムを確認。実クリア→1枚持帰り→経験値100／受領1回→帰還保存→所持詳細3往復で保存hash不変を確認。製品の入手表や価格を増やしたものではない。

独立コードレビューとネイティブFHD／4K／4:3視覚レビューの本範囲の未解決指摘0。少数所持の余白・枠接触・暗背景のページ番号は再評価して修正した。これはゲーム全体の無欠陥／美的完成の保証ではない。
