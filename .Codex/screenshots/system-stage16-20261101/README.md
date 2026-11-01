# 第十六段階: 装備3Viewの管理寿命

CustomizationLoadoutView（装備編集）、CustomizationUnlockView（マス解放）、CustomizationAcquisitionView（装備受領）をViewManagerのcustomization層へ移行。ResourceManager.InstanceLeaseと有効contextを通し、閉鎖/再表示/host終端で入力購読・モデル・旧callbackを返す。画面内inline詳細は維持し、存在しないPopupやManagerを追加していない。内部Initialize組立は残る。

保存前後・取消・装備適用の既存順序は変更しない。独立レビューで帰還Dialog終了同frameの入口、閉鎖focus、Open前の親focus捕捉順を修正し、最終5sourceで検証し直した。

## 検証

- 関連507/507、装備処理84/84、最終全3615/3615（347.61秒）。失敗/skip/inconclusive 0、console error0。source hashはsubmission/検証project/結果と一致。
- 同Editor反復68件。各回EventSystem登録0・生成cache/lease残留0、global clear未使用。
- 実640×360/両RT、専用TMPDIR/saveguardを確認して実行。warning59は従来の負条件fixture5分類、抑制なし（warning-classification.json）。
- 実Bootstrapの定義未登録disabled状態と、既存Stage55 catalog/provider注入の隔離hostを区別。実入口で編集取消/仮想pad取消/パーツ配置/装飾品解除/解放/受領、3境界の書込前失敗/無効取消/旧入力拒否/同計画Retryを確認。各faultは失敗1+成功1の2試行、disk/snapshotは失敗時不変、成功後persisted一致。
- host終了/CloseOpen/Single再入/欠損取消/重複要求で3画面と旧menu入力・モデル・contextを解放。Root/Camera/Listener/EventSystem各1・音源3。隔離progress2ファイルは最終終了と全テスト後も同bytes。
- 今回取得した非表示Custom resolverの所有Sprite1/Texture0は破棄、import Texture生存。mini5を作っていない今回へ旧6/5実測を流用しない。既存専用回帰は維持。
- 原寸18画像と4K局所を独立視覚確認し必須0、独立コードレビュー必須0。

## 原寸画像

| 実状態 | FHD | 4K | 4:3 |
|---|---|---|---|
| 装備草稿 | [fhd](equipment-loadout-fhd.png) | [4k](equipment-loadout-4k.png) | [4by3](equipment-loadout-4by3.png) |
| 装備保存失敗・再試行 | [fhd](loadout-save-retry-fhd.png) | [4k](loadout-save-retry-4k.png) | [4by3](loadout-save-retry-4by3.png) |
| 解放対象 | [fhd](equipment-unlock-fhd.png) | [4k](equipment-unlock-4k.png) | [4by3](equipment-unlock-4by3.png) |
| 解放保存失敗・再試行 | [fhd](unlock-save-retry-fhd.png) | [4k](unlock-save-retry-4k.png) | [4by3](unlock-save-retry-4by3.png) |
| 受領対象 | [fhd](equipment-acquisition-fhd.png) | [4k](equipment-acquisition-4k.png) | [4by3](equipment-acquisition-4by3.png) |
| 受領保存失敗・再試行 | [fhd](acquisition-save-retry-fhd.png) | [4k](acquisition-save-retry-4k.png) | [4by3](acquisition-save-retry-4by3.png) |

同じ状態の保持frame/Canvas更新後の実render。既存の明示fixtureであり製品新性能を創作していない。matched-beforeは未撮影、pixel同一の主張はしない。

## 証跡と限界

[全件](full-tests.json) / [関連](related-tests.json) / [装備処理](customization-tests.json) / [同Editor](same-editor-regression.json) / [保存実操作](native-saves.json) / [寿命実操作](native-lifetime.json) / [cleanup](cleanup.json) / [コード](code-review.json) / [視覚](visual-review.json)。

拠点providerは再Openで再解決される既存契約。同context保存計画は再Stageせずrevision/内容を保持。helperのprovider一回限定・header focus必須は過剰な前提として撤回し、既存ConfirmDungeonSelection fallbackと起点cacheを区別した。既実行Openは重複送信せず続行。[helper訂正](native-helper-corrections.json)。途中processのafterBytesを再構成しておらず該当nullを保持、当時のin-process比較とpersisted照合、終端2ファイルhashは別証拠。

[初回/復旧履歴](source-and-recovery-history.json)と[native console履歴](native-console-history.json)に再compile/reload後のPipeline timeout1、UnityAI readonly接続情報warning1を保持。正常終了窓・最終テストerror0と混同せず、VM/Editor/model/認証変更なし。

fixtureは製品stat/能力値を持たず、実製品効果値までは未確認。既存84件の計算回帰と供給元parts/accessory一致を区別。Stage15極端な長名末尾、登録外Sprite2095、歴史的UGUI/Pipeline/CLI、全HotReload/実build/物理機器/聴感は未解決/未確認として保持。

必須残件はCurrencyRewardPanel/ItemRewardPanel、CustomPadDetails（card/変身/combatant）、DialoguePanelとskip/backlog、Controller内部Initialize/必要時Prefab分解。次は報酬overlayの実寿命を閉じるまとまりが適切。追加外界/依頼/ボスラッシュと未決D05/S08、任意の見た目改修は別範囲。

[設計・段階移行](../../../Planning/GameSystem管理基盤と段階移行.md)
