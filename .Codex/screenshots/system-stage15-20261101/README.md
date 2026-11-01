# 第十五段階: 編成破棄・保存デッキ削除のDialog寿命

帰還共有View内の編成破棄と保存デッキ削除確認を、実際の親contextを使うDialogManagerのticketへ移した。ViewはPopupの生成・終端解放を担い、DialogManagerが決定/取消/scroll/親終了を所有する。背面contentと編成草稿は保持し、表示だけ停止する。旧Scene/単体Viewの互換分岐は残す。既存の文言・配置・面材と外枠、保存形式・タイミング・計算・固定frameは変更しない。

取消後は再生成された一覧から起点focusを解決する。内容再描画は進行中ticketだけ取消し、閉じた同frameの背面入力遮断を消さない。編成破棄で拠点へ戻った直後の旧手動保存/新規開始入口も遮断する。削除保存失敗時は従来どおり確認を閉じ、pending一覧の既存再試行へ戻る。確認内の永続retryへ仕様変更していない。

実経路はBootstrap→出撃→敵接触→Custom→pause→撤退→帰還。未保存編成と保存デッキ草稿の取消/破棄、取消focus、同frame旧保存イベント、削除取消、書込前の一度失敗→一覧retryを確認した。失敗時の実在4保存ファイルbytesは不変で、失敗/再試行は2回だけ。検証用にGUID契約に従うA/Bを隔離先に作り、削除対象はこの2件のみ。元のスターターデッキ・所持数・通貨を維持した。表示往復/旧callback/親終了/host再入で追加保存なし。

親ViewManager.CloseCurrent、host Close/Open、欠損/取消ロード、同時再開、Bootstrap Single再入を通し、閉じたticketが新しいDialogを閉じたり保存したりしないことを確認。Root/Camera/Listener/EventSystem各1・音源3を維持し、host終了でView/購読/Camera参照を解放。帰還だけで使った非表示Customの所有Sprite6/Texture5を破棄し、import Texture生存を確認した。

長名は保存内容を変えない1000文字の表示fixtureから実削除入口を押したもの。仮想padで左右選択、RBのPageDown、取消を確認し、finallyで機器を除去。既存の切断自動pauseは実ResumeButtonで復帰した。物理padの証明ではなく、1000文字の末尾到達を先頭写真から推定しない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|編成破棄|[原寸](discard-confirm-fhd.png)|[原寸](discard-confirm-4k.png)|[原寸](discard-confirm-4by3.png)|
|保存デッキ削除|[原寸](delete-confirm-fhd.png)|[原寸](delete-confirm-4k.png)|[原寸](delete-confirm-4by3.png)|
|書込前失敗後の一覧retry|[原寸](delete-save-retry-fhd.png)|[原寸](delete-save-retry-4k.png)|[原寸](delete-save-retry-4by3.png)|
|長い旧名（表示fixture）|[原寸](delete-long-name-fhd.png)|[原寸](delete-long-name-4k.png)|[原寸](delete-long-name-4by3.png)|
|隔離A/B削除後の既存一覧|[原寸](saved-decks-after-delete-fhd.png)|[原寸](saved-decks-after-delete-4k.png)|[原寸](saved-decks-after-delete-4by3.png)|

写真は最終ソースの同状態/保持frame/Canvas更新済みの3比率確認。新たなmatched-before撮影やpixel同一証明ではない。独立視覚レビューは15画像と4K等倍局所を確認し必須指摘0。独立コードレビュー、関連501件と同Editor反復56件は成功。最終全3609件も成功（271.89秒）、実行後error0。warning59は既存の負条件fixture5分類で抑制なし。cleanupでは実640×360/両RT/TMP、原本clean・928対象Asset一致（既存private URP2例外）、EventSystem/生成registry/lease0、注入/捕捉キーなし、native Mouse/Keyboardのみ、実在4保存ファイルがstop/最終test後も不変を確認。GameplayPauseのIsPaused=false/awaitRelease=true/2frame neutral待ちは既存契約として維持する。

初回コンパイルは新規テストのusing System.Linq漏れ2件を修正。初回関連500/501は、短名に作られない任意ScrollRectを新規テストが要求したfixture前提誤り。長名fixtureへ直しassert/製品3sourceは維持した。実操作helperも旧ボタン名・無効ID・実フォントで収まる300文字を訂正してから、実在する入口/有効GUID/1000文字overflowで検証し直した。製品コードや保存仕様をhelperへ合わせて変えていない。初回失敗・Pipelineの再接続/主スレッドtimeout・UnityAI保存warningは別証跡に残す。Editor/VM/モデル/認証は交換していない。

必要な残移行はCustomizationLoadoutView/UnlockView/AcquisitionView、CurrencyRewardPanel/ItemRewardPanel、CustomPadDetails、DialoguePanelのskip/backlog、内部Initialize/必要時Prefab分解。任意の見た目改善や未実装外界・依頼等は別で、この段階を全移行完了としない。登録外Sprite2095基準群、歴史的UGUI/Pipeline/CLI/UnityAI、全HotReload・実build・物理入力・聴感未確認を保持する。

[設計・必須残対象](../../../Planning/GameSystem管理基盤と段階移行.md)
