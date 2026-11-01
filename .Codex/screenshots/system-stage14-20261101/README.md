# 第十四段階: 帰還共有Viewと直属の読取専用Dialog

帰還共有ClearReturnViewの主画面と図鑑をViewManagerの別層へ接続し、実panelのInstanceLease・表示context・閉鎖と終了時の後片付けを管理する。ReadonlyCardDetailsはDialogManagerの一ボタンNoticeへ移し、scrollと戻る入力を一本化。progressAction確認は実帰還contextを親にする。計算・保存/適用順・選択規則・見た目は維持した。

実終了検証では、帰還だけで素材resolverとして使った未表示Customの本体破棄後にmini Sprite5個/Texture5個が残る問題を発見。既存の解放をOnDestroy依存から明示終端へ移し、帰還・図鑑を先にHideする。最終resolver検証では既存APIからfull-art Sprite1個も取得し、所有Sprite計6個/Texture5個の破棄、import Texture生存、二重Dispose安全を確認した。初回失敗と、Close自体は実行済みだったことをinitial-native-cache-failure.jsonへ保持する。

最終関連497件・全3605件と同Editor反復を確認。厳密な実640×360/両RT・専用TMPDIR/save guardを使用。全件のクライアント待機期限後も既存ジョブのstatusから結果を回収し、重複実行していない。独立コード・最終15画像/4K局所レビューの必須指摘0。

|状態|FHD|4K|4:3|
|---|---|---|---|
|帰還保存の再試行|[原寸](return-save-retry-fhd.png)|[原寸](return-save-retry-4k.png)|[原寸](return-save-retry-4by3.png)|
|帰還共有画面|[原寸](return-hub-fhd.png)|[原寸](return-hub-4k.png)|[原寸](return-hub-4by3.png)|
|所持カード詳細|[原寸](owned-detail-fhd.png)|[原寸](owned-detail-4k.png)|[原寸](owned-detail-4by3.png)|
|図鑑|[原寸](card-collection-fhd.png)|[原寸](card-collection-4k.png)|[原寸](card-collection-4by3.png)|
|長文Notice（表示fixture）|[原寸](long-notice-fhd.png)|[原寸](long-notice-4k.png)|[原寸](long-notice-4by3.png)|

実経路は敵接触→Custom→pause→撤退、進行store書込前の一度失敗→同計画再試行、帰還contextの手動保存Dialog同ticket再試行、詳細/図鑑/ページ/取消focus、同frame旧入力、host Close/Open・欠損取消・Single再入。保存失敗では進行ファイルの元bytesを比較し、撤退前の正規checkpoint保存とは分ける。表示だけの往復や閉じたcallback/再入で保存bytes不変。長文は80行の隔離表示fixtureから実全文入口を押し、scrollイベント後にlive表示へ戻した。末尾到達の実画面をこの先頭画像から推定しない。

後片付け時のGameplayPauseはIsPaused=false、awaitRelease=true、resumeFrames=2。既存のneutral待ちを強制解除せず保持し、モーダルや機器の残留とは区別する。

写真は最終ソースの退行確認で、新しく撮ったmatched-beforeやpixel同一証明ではない。フレーム停止helper由来のtimeScale0を含むため、通常の全pause復帰をこの実Playだけで証明したとはしない。固定frame/pause契約の回帰は別途全件で確認。Native履歴のPipeline timeout1件・UnityAI保存警告1件は製品エラーと分けて保持し、最終テスト/cleanupのconsole0と混同しない。

既存Controller内部組立は残り、全画面の必要時Prefab化完了とはしない。次はKeepEditingDeparture / ConfirmDeleteDeck確認。装備3View、CurrencyRewardPanel / ItemRewardPanel、CustomPadDetails、DialoguePanelとskip/backlogも残る。登録外Sprite2095基準群の一括削除や漏洩全治は主張しない。実build/物理入力/聴感/全HotReload、歴史的UGUI/Pipeline/CLI/UnityAIは未確認/未解決を保持。新外界・依頼・未決値の実装は混ぜていない。

[設計と残対象](../../../Planning/GameSystem管理基盤と段階移行.md)
