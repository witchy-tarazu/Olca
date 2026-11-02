# 第24段階: 会話skip共通確認と履歴Dialog

会話skipは共通 `ConfirmationDialog` を使用。親 `DialogueView` は通常の会話進行と開始/結果連携に絞り、専用 `DialogueBacklogDialog` が公開済み履歴の本文、ページ、スクロール、閉じる入力を所有する。`DialogManager` は共通ticket/寿命/入力遮断のみ。既読/選択/台本/保存は既存Session/Controllerを維持する。Pause中はSessionの履歴/skip状態を変えずPopupを閉じ、読取位置を保持して新ticketで復元する。

## 検証

- Application 544、Integration 605、同Editor反復94、最終全3656件成功。全件 246.37 秒、console error0。警告59は既存fixture診断5分類。[分類記録](warning-classification.json)。起動時UnityAIの接続情報保存警告1は [起動履歴](native-startup-console-history.json) に分け、実操作終了時はerror/warning0。
- Bootstrap実入口から隔離台本を開始し、確認取消/skip一度だけ・選択肢と既存skip不可・履歴長文末尾/複数ページ・Pause/他確認往復・元focus/同frame旧入力・親終了/CloseOpen/Single再入/旧callbackを確認。[会話実操作](native-dialogue.json) / [寿命](native-lifetime.json)。
- 閲覧/取消では実隔離保存bytes、会話選択/進行とworld状態不変。意図したhost中断保存は既存checkpoint境界として別計測。
- 最終ソース一致、実640×360/専用TMPDIR/save guard、原本保護・928資産・importフォント生存、生成lease/EventSystem/機器/注入/proof解除と640復元。[cleanup](cleanup.json)
- 独立 [コードレビュー](code-review.json) / [原寸視覚レビュー](visual-review.json)。

初回関連543件は541成功・2失敗。Popupが親全体ではなく内容だけを覆う構成に変わり、Pause時に親CanvasGroupが無い経路で既存 `GetComponent() ?? AddComponent()` のCLR判定がUnity missing-objectと食い違った。Unity生存判定へ最小修正し期待値は維持。[初回失敗](initial-related-failure.json)。v2実RaycastでPause遮蔽も発見し、外部共用操作の前面順をOwnedPopup側で毎frame維持する最小修正と既存2ケースの回帰確認を追加。旧Bindでは前の復帰先が次会話へ残ることも公開UI APIで再現し、新Bindだけ復帰参照をクリアする1ケースを追加。Pauseの読取位置保持は維持。[遮蔽証跡](native-occlusion-before-fix.json)。再読込/初回helper待ちは既存jobを回収、再送/Editor再起動なし。[tool履歴](tool-history.json)

## 原寸比較

各画像は実GameView、論理状態停止とCanvas更新・両RenderTextureサイズ確認後に取得。beforeは公開Stage19の同じ隔離文面/配置/解像度の参照画像で、別起動のため全world同frameの比較とは主張しない。画像はリンクから原寸を閲覧できる。

|状態|FHD|4K|4:3|
|---|---|---|---|
|通常会話|[前](before-conversation-fhd.png) / [後](conversation-fhd.png)|[前](before-conversation-4k.png) / [後](conversation-4k.png)|[前](before-conversation-4by3.png) / [後](conversation-4by3.png)|
|履歴末尾|[前](before-backlog-tail-fhd.png) / [後](backlog-tail-fhd.png)|[前](before-backlog-tail-4k.png) / [後](backlog-tail-4k.png)|[前](before-backlog-tail-4by3.png) / [後](backlog-tail-4by3.png)|
|skip確認|[前](before-skip-confirm-fhd.png) / [後](skip-confirm-fhd.png)|[前](before-skip-confirm-4k.png) / [後](skip-confirm-4k.png)|[前](before-skip-confirm-4by3.png) / [後](skip-confirm-4by3.png)|
|複数履歴→長文末尾|[後](backlog-multiple-tail-fhd.png)|[後](backlog-multiple-tail-4k.png)|[後](backlog-multiple-tail-4by3.png)|

## 残件・限界

残必須はCustom詳細の固有表示/操作所有。基盤全体完了とはしない。実製品NPC会話登録でなく隔離台本の既存公開入口を検証。実build/物理入力/聴感/全HotReload、製品装備stat実値・Itemskip・極端な旧長名・登録外Sprite2095、歴史的UGUI/Pipeline/CLI/UnityAIは別の未確認/問題として保持。新外界仕様や未決値の実装は混ぜない。[最新仕様保護](spec-update.json) / [残件](required-followup.json)。
