# 第十七段階: 通貨/アイテム報酬overlayの管理寿命

CurrencyRewardPanel / ItemRewardPanelをViewManagerのresource-reward層へ登録し、実panelをResourceManager.InstanceLeaseで追跡。表示/閉鎖/親終了/host終了を管理contextへ揃えた。受取/スキップのchoiceは表示世代へ結び、旧UnityEventは次の報酬を実行しない。確認Popupを新設せず、必要なPopupはDialogManagerの責務として維持する。

報酬計算/上限/取得判定/保留計画/保存/固定フレームは変更なし。既存の内部UI組立と必要時Prefab分解は残る。

## 検証

- 関連509/509（53.6秒）、最終全3617/3617（276.78秒）、同Editor反復72件成功。失敗/skip/inconclusive0、最終console error0。
- 実640×360/両RT、専用TMPDIR/saveguard確認後に実行。警告59件は従来の負条件fixture5分類、抑制なし。
- 通常Bootstrap→出撃→接敵→Custom→報酬の実入口。勝利のみ既存被害API fixture、UIはraycast経路。未受取Itemでhost終了してbytes/保留を保持し、checkpoint再開で再提示。
- Disable/Enableで同choice保持、pause中の旧受取拒否、再開後の既存heal20でHP79→99。被害21は隔離検証fixtureで製品値変更なし。Item旧eventがCurrency待機へ作用せず、受取/スキップ両種の世代/親終了/交差切替は管理回帰でも確認。
- 通貨10の確定checkpointを書込前に一度だけ失敗させ、disk bytes/確定通貨0を保持。既存RetryCheckpointButtonで2試行（失敗1+成功1）、RewardCompletedのdisk/Last HP99・通貨10を確認。旧受取による重複なし。
- Close/Open/Single/欠損/取消/重複要求で旧panel/choice/menuを返却。活動中Root/Camera/Listener/EventSystem各1・音源3、host終了で共通Resource3。最終隔離4保存ファイルは全件後も同bytes。
- 9画像＋4K等倍局所の独立視覚レビュー必須0。独立コードレビュー必須0。

## 原寸画面

| 実状態 | FHD | 4K | 4:3 |
|---|---|---|---|
| アイテム報酬 | [fhd](item-reward-fhd.png) | [4k](item-reward-4k.png) | [4by3](item-reward-4by3.png) |
| 通貨報酬 | [fhd](currency-reward-fhd.png) | [4k](currency-reward-4k.png) | [4by3](currency-reward-4by3.png) |
| 報酬確定の保存再試行 | [fhd](reward-checkpoint-retry-fhd.png) | [4k](reward-checkpoint-retry-4k.png) | [4by3](reward-checkpoint-retry-4by3.png) |

保持logical frame/Canvas更新済みの実render。matched-beforeは未撮影、pixel同一や審美上の全面承認を主張しない。

## 証跡と境界

[全件](full-tests.json) / [関連](related-tests.json) / [同Editor](same-editor-regression.json) / [実報酬・保存](native-rewards.json) / [寿命](native-lifetime.json) / [cleanup](cleanup.json) / [コード](code-review.json) / [視覚](visual-review.json)。

PendingCurrencyは既存Coreが未保存checkpoint計画へ移すため0になり得る。その計画を再試行して確定する契約を維持。報酬保存HP99と、その後の探索/訪問を含むlive HP100は時点を区別する。host停止の既存Suspendが全状態で新checkpointを書けるとは仮定せず、実測after-save bytesを終了/テスト後と照合した。Itemスキップ等の未実施native経路は管理/Core回帰と区別する。

[初回・復旧履歴](recovery-history.json)と[native console](native-console-history.json)を保持。compile/Play開始の通常DomainReload、Pipeline timeout、UnityAI readonly警告は製品テスト結果と分ける。VM/Editor137059/model/認証変更なし、同じテスト/Play/不明な書込の重複なし。初回静的指摘2件（Disable取消/新旧context順）を動的検証前に修正。private helperのPlayer API引数誤りは実行0、背面PauseButtonはraycast拒否後に表示中PauseVisitを使用。[helper訂正](helper-corrections.json)。

登録外Sprite2095基準群、Stage15極端長名末尾、Stage16装備stat実値、歴史的UGUI/Pipeline/CLI/UnityAI、実build/物理機器/聴感/全HotReloadは未解決/未確認として保持。このFieldItem HP確認を装備stat確認と混同しない。

必須残件はCustomPadDetails（card/変身/combatant）、DialoguePanelとskip/backlog、Controller内部Initialize/必要時Prefab分解。次はCustom詳細PopupのDialogManager実寿命への移行が適切。新外界/依頼/ボスラッシュ/未決D05/S08や任意の見た目改善は別範囲。

[設計と段階移行](../../../Planning/GameSystem管理基盤と段階移行.md)
