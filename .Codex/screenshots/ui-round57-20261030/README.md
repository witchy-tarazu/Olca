# 出撃スタイル画面の視覚整理（Round57）

出撃先とスタイルの残作業から、保存済みと変更候補が区別されず、少数候補時の白い空白が大きかったスタイル画面を最初のまとまりに選んだ。出撃先本体の視覚整理は今回未変更として次へ残す。

- 水鏡面材・JikiTouraiを既存交換／装備画面へ揃え、見出し・一覧・比較・保存操作を整理。行1450×78、行／ページ／確定／取消の入力位置を保持する。
- Controllerから保存済みStyleIdに対応するcurrent indexを供給し、未保存draftと分離する。ページ変更でも保存済みと候補を混同しない。既存呼出はoptional current=-1で互換を維持し、未登録値を勝手に基本へ置換しない。
- 少数候補は左右の比較面で現在／候補を表示し、同じindexなら右値を「変更なし」にする。初案の情報重複と大きな空白を原寸レビューで修正し、面の高さを最大190へ縮めた。比較面で読める場合は重複要約帯を省略し、密なページ／長名で省略する場合は要約と既存全文入口を保持する。
- 既存の登録名・属性・解放条件を表示へ接続。Basic名は保持し、余分なNone表記を増やさない。他の登録属性は現行5種の読みへ変換し、名前・条件ID・攻撃性能を変更しない。currentの対応は名前一致ではなく保存IDから判定する。
- 確定／再試行がスタイル保存だけを行う実Controller経路に合わせ、出撃開始と混同しない案内へ変更。通常説明を上部と下部へ分け、エラー解消後に不要な全文入口へfocusが残る退行を製品側で修正した。既存のStyleChoice_0へ戻る期待値は維持する。
- 今回は保存済みID・登録情報の表示接続であり、新規ラスタ・スタイル製品定義・能力値を作ったものではない。元背景・文字表記規則・保存互換・固定論理フレーム・戦闘時間を維持する。

根拠はゲーム全体仕様の「戦闘スタイル・属性」（出撃前変更、攻略中固定、属性と固有／タメ攻撃）、DepartureStyleDefinition、ClearReturnSession.SelectDepartureStyle、Controller.Open/Show/Close/RetryStyleSelection。解放条件・保存・出撃開始そのものの契約は変更しない。

## 原寸比較

実Unity Game Viewの未加工全画面。FHD1920×1080／4K3840×2160／4:3 1440×1080。検証用登録による1候補、7候補、長名、未解放、空、保存待ちを同じ表示入力で比較する。複数のテストスタイルは製品定義ではなく、性能や正式名称を新しく登録したものではない。

| 状態 | 前 | 後 |
| --- | --- | --- |
| 1候補 FHD | [before](before-single-fhd.png) | [after](after-single-fhd.png) |
| 1候補 4:3 | [before](before-single-4by3.png) | [after](after-single-4by3.png) |
| 多数候補 4:3 | [before](before-list-4by3.png) | [after](after-list-4by3.png) |
| 変更候補 4K | [before](before-candidate-4k.png) | [after](after-candidate-4k.png) |
| 未解放ページ 4:3 | [before](before-locked-4by3.png) | [after](after-locked-4by3.png) |
| 長名 FHD | [before](before-long-fhd.png) | [after](after-long-fhd.png) |
| 空 FHD | [before](before-empty-fhd.png) | [after](after-empty-fhd.png) |
| 保存待ち 4:3 | [before](before-pending-4by3.png) | [after](after-pending-4by3.png) |

長名・空・保存待ちは表示用コピーであり、製品の登録条件を緩めたり実セーブに障害を注入したりしていない。Controllerの登録口が要求するBasicを勝手に削除せず、空一覧はView境界として確認する。保存再試行の原子性は既存の実Controller回帰テストで確認する。

[全文閲覧](after-long-detail-4by3.png)、[保存後の出撃先](after-destination-saved-fhd.png)、[保存済み表示](after-saved-current-fhd.png)、[同保存先再読込](after-reloaded-fhd.png)はafter-only。各状態の3解像度を同梱する。

## 実操作・検証データ

実EventSystem Raycastで長い全文開閉2周、選択・ページ往復・未解放拒否・取消2周を確認。保存後に現在値へ反映し、同一スタイルの確定を2回繰り返しても保存世代や経済を増減しない。同じ保存先のPlay再起動で保存済みIDとdraftの一致を確認する。スタイル保存では出撃予約やcheckpointを作らず、通貨・所持・他の進行値を保持する。

初回private seedがControllerのレシピ定義を省いていたため、最初のスタイル保存で既存Stage→UpdateCollectionが既に所持していた結果レシピの公開も更新した。これはUI変更の新効果ではない。旧Root702と証跡を保持し、private初期化だけをControllerと同じfactory／recipe provider／collection definitionsへ揃えたRoot703で再確認した。製品保存コードや期待する経済規則は変更しない。

## 結果と限界

最終関連434件、全件成績・警告・原本保護・保存ガード・独立レビューは併設JSONへ記録する。初期のfocus退行1件と、配置変更後の新テストが旧要約帯を探した不整合1件は別記録に残す。既存テストの期待値を緩和せず、製品の不要入口を修正し、新テストは実際の現在／候補表示を検証した。

物理パッド／タッチ／IMEは未確認。今回のnative実操作は初期拠点のスタイル保存と再読込であり、出撃先画面本体の視覚整理・実Playの出撃確定は次の範囲に残す。既存の出撃反映／帰還後の回帰はUnityテストで確認する。攻撃本体アニメや製品固有絵の完成とは別で、拒否済み画像生成は再試行していない。

最終全件は実640×360・専用TMPDIR／保存ガードを確認し1回だけ起動して追跡する。画像後は低解像度と一時入力を復元する。レビュー0はこの範囲の結果であり、全画面の美的完成や無欠陥保証ではない。

最終全EditMode **3,493件成功**（285.47秒、失敗／skip0）。console error0／warning62を実ログ分類へ分離。
