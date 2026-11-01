# 第十二段階: pause View / 撤退確認 Dialog

PauseOverlayをViewManagerのsystem-pause層、RetreatConfirmationをDialogManagerへ接続。表示寿命とcontext世代で旧クリックを遮断し、Dialog取消元フォーカスを親メニュー再Bindへ一度だけ引き継ぐ。時間・保存・撤退計算と見た目は既存契約を維持。既存panelのResourceManager lease接続であり、必要時Prefab化や全画面移行の完了ではない。

最終関連488/488（48.71秒）、全3596/3596（269.42秒）、同Editor2周30件成功。console error0、警告59件は従来fixture5分類。初回Pointer取消のフォーカス上書き失敗を隠さず保存、修正後に実Pointer経路で再確認。保存/再入/解放と残る未確認は各JSON参照。

|画面|FHD|4K|4:3|
|---|---|---|---|
|pause|[原寸](pause-fhd.png)|[原寸](pause-4k.png)|[原寸](pause-4by3.png)|
|撤退確認|[原寸](retreat-confirmation-fhd.png)|[原寸](retreat-confirmation-4k.png)|[原寸](retreat-confirmation-4by3.png)|

新たな同条件before撮影はないため完全画素一致とはしない。独立コード/視覚レビュー範囲の必須指摘0。checkpoint案内、帰還共有画面、装備系、progressAction、報酬overlay、詳細/削除確認、会話skip/backlog等は管理寿命の必須残件。登録外Sprite2095群の生成元は未特定、今回一括削除していない。実build/物理機器/聴感/全HotReload・歴史的UGUIは未確認/未解決。Pipeline/CLI/UnityAI履歴は製品テスト結果と別管理。

[設計と残対象](../../../Planning/GameSystem管理基盤と段階移行.md)
