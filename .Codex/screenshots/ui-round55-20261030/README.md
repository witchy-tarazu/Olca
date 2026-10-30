# マス解放・装備取得の視覚整理（Round55）

- 所持アイテム／固定領域盤面／選択形状と実行、取得候補／内訳と所持数／確定を分け、水鏡の面材・JikiTourai・余白と見出しを装備編集へ揃えた。元背景アセットは変更しない。背面HUDが透ける全面の淡い板を整理した。
- 一覧名、所持数、候補・選択中・受領済み・所持済み・上限の状態を列揃え。長いIDは表示変換後grapheme単位で省略し、選択内容の読み取り専用本文は全文をスクロールできる。IDをrich textとして解釈しない。
- マス解放は登録された固定領域を等比プレビューへ接続し、対象数／新規解放数／表示中数を分ける。選択時にその領域の既存盤面ページを表示し、手動ページ移動も保持。座標はセル外の軸、未解放記号は暗面で白色にする。プレイヤーが任意セルを指定する機能は追加しない。
- 装備取得はControllerから既存Catalogを表示へ供給し、登録PartShape.CellsとUnlockItemsの固定形状を等比miniへ接続。種類・個数・所持数を内訳へ表示する。正式な装飾品絵・能力説明・製品データを捏造せず、新規ラスタやゲーム性能値は登録しない。既存3引数Open APIは維持する。
- 受領直後は受領した内容を表示し、上限到達後の再表示では「候補を選ぶ」矛盾案内を完了案内へ修正。候補なし／全候補受領不可／保存待ちを区別する。判定と原子的受領は既存Coreに従う。

## 比較画像

実Unity Game View、未加工の全画面。FHD=1920×1080、4K=3840×2160、4:3=1440×1080。通常・選択・長名・解放済み・取得済み・上限・空の同条件を比較する。検証用登録定義と隔離保存先を使用し、製品設定ではない。

| 状態 | 前 | 後 |
| --- | --- | --- |
| 解放対象 FHD | [before](before-unlock-selected-fhd.png) | [after](after-unlock-selected-fhd.png) |
| 解放対象 4:3 | [before](before-unlock-selected-4by3.png) | [after](after-unlock-selected-4by3.png) |
| 長い解放名 4K | [before](before-unlock-long-4k.png) | [after](after-unlock-long-4k.png) |
| 取得内訳 FHD | [before](before-acquisition-selected-fhd.png) | [after](after-acquisition-selected-fhd.png) |
| 長い取得名 4:3 | [before](before-acquisition-long-4by3.png) | [after](after-acquisition-long-4by3.png) |
| 受領後 4K | [before](before-acquisition-received-4k.png) | [after](after-acquisition-received-4k.png) |
| 遠い固定領域 4:3 | [before](before-unlock-offscreen-4by3.png) | [after](after-unlock-offscreen-4by3.png) |
| 上限後に再表示 FHD | [before](before-acquisition-reopened-cap-fhd.png) | [after](after-acquisition-reopened-cap-fhd.png) |

「遠い固定領域」と「上限後に再表示」の前画像は、面材整理後・それぞれの問題修正前の対象比較。全体スタイルの旧版と混同しない。

保存待ちは到達可能な**書込前の表示用コピー**へ既存失敗通知を与えたafter-onlyの[解放](after-unlock-pending-fhd.png)／[取得](after-acquisition-pending-fhd.png)。実セーブの障害注入やその画面での再試行は行っていない。初期の到達しない事後状態fixtureは比較に採用しない。原子的保存失敗・再試行は既存CoreテストとUI pending callbackテストで確認する。

同じ保存先のPlay再起動後は[解放済み](after-unlock-reloaded-fhd.png)／[取得上限](after-acquisition-reloaded-fhd.png)。初期画面とは保存済み解放数が違うためafter-only。各状態の3解像度を同梱する。

## 契約・保護・限界

根拠はゲーム全体仕様§19、CustomizationCatalog／CustomizationAcquisitionPlan／Availability／既存保存契約。対象APIには価格・支払い・資金不足状態がなく、0価格や無料などの新経済は追加しない。解放は所持数を減らさず適用IDを記録する既存契約を維持し、装飾品の重複取得不可／パーツの重複所持可／提示前保存と再抽選防止を変更しない。

Core・保存形式・能力・入手表・固定論理フレーム・効果時間は無変更。取消と閲覧で保存不変、連続確定の二重解放／二重受領なし、同保存先再起動後の盤面・装備・配置・受領履歴を確認。検証用仮想パッド解放で仕様どおり切断ポーズが発生し、通常の再開ボタンで復帰・保存不変を確認した。最初の次画面クリックが遮られた事象はこのポーズによるもので、製品例外とは分ける。実Raycastと仮想パッドの確認を物理入力機器と混同しない。物理パッド／タッチ／IMEは未確認。形状参照接続は製品固有絵や攻撃本体モーションの完成とは別である。

## 最終検証

最終全件・警告分類・保存復帰・独立レビュー・原本保護の結果は併設JSONに記録。実640×360、専用TMPDIR／保存ガードを確認して最終変更後の全件を1回だけ開始し、そのrunを追跡。native画像後は低解像度を復元する。改善はこの範囲の結果であり、全画面の美的完成や無欠陥の保証ではない。

最終関連43件、全EditMode **3,490件成功**（269.74秒、失敗／skip0）。console error0／warning62は実ログ分類へ分離。

初回の別ソース全件は3,489成功／1失敗（292.22秒）。旧候補行幅1500を固定したUiOwnershipRefactorTestsが新620と不一致だった。新配置620×80・座標(-620,255)を明示検証へ更新し、font・旧入力寿命・確定回数の期待値は維持した。初回の失敗はprior-source-tests.jsonへ分離し、未完の成功扱いや期待値削除はしない。
