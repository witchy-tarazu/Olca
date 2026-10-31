# Round75 状態演出と攻撃予兆の優先順位

麻痺の放電とガードの外周を本体へ寄せ、ガード／麻痺／バリア／オーラの濃さを抑えた。防御外周は本体を動かさず上へ寄せ、足場と予兆下端を空ける。敵本体の位置・Sprite・scale・pivot・HP、状態の寿命や性能は変更していない。

予兆の薄い面は従来どおり床側のorder2、角枠と注意記号だけorder6へ分離した。ボスや持続状態演出に危険範囲の輪郭が隠れない優先順位とした。範囲・色・形・生成数・発動／終了frameは不変。既存の専用素材を再利用し、画像生成や製品性能値の追加はない。

以下は38f43edを比較元とする同一seed／隔離fixture／Coreframe／Canvas更新済みの原寸画像。既存CreateTestDungeon(6,1,2)、通常3敵と次戦ボス、既存状態APIと検証用7damage／12frame予兆を用いた。製品カードへ検証値を登録していない。

|場面・Coreframe・出力|変更前|変更後|
|---|---|---|
| states / 90 / fhd | [原寸](ui-round75-before-states-fhd.png) | [原寸](ui-round75-after-states-fhd.png) |
| states / 90 / 4k | [原寸](ui-round75-before-states-4k.png) | [原寸](ui-round75-after-states-4k.png) |
| states / 90 / 4by3 | [原寸](ui-round75-before-states-4by3.png) | [原寸](ui-round75-after-states-4by3.png) |
| warning / 99 / fhd | [原寸](ui-round75-before-warning-fhd.png) | [原寸](ui-round75-after-warning-fhd.png) |
| warning / 99 / 4k | [原寸](ui-round75-before-warning-4k.png) | [原寸](ui-round75-after-warning-4k.png) |
| warning / 99 / 4by3 | [原寸](ui-round75-before-warning-4by3.png) | [原寸](ui-round75-after-warning-4by3.png) |
| impact / 132 / fhd | [原寸](ui-round75-before-impact-fhd.png) | [原寸](ui-round75-after-impact-fhd.png) |
| impact / 132 / 4k | [原寸](ui-round75-before-impact-4k.png) | [原寸](ui-round75-after-impact-4k.png) |
| impact / 132 / 4by3 | [原寸](ui-round75-before-impact-4by3.png) | [原寸](ui-round75-after-impact-4by3.png) |
| cleared / 175 / fhd | [原寸](ui-round75-before-cleared-fhd.png) | [原寸](ui-round75-after-cleared-fhd.png) |
| cleared / 175 / 4k | [原寸](ui-round75-before-cleared-4k.png) | [原寸](ui-round75-after-cleared-4k.png) |
| cleared / 175 / 4by3 | [原寸](ui-round75-before-cleared-4by3.png) | [原寸](ui-round75-after-cleared-4by3.png) |
| boss-states / 120 / fhd | [原寸](ui-round75-before-boss-states-fhd.png) | [原寸](ui-round75-after-boss-states-fhd.png) |
| boss-states / 120 / 4k | [原寸](ui-round75-before-boss-states-4k.png) | [原寸](ui-round75-after-boss-states-4k.png) |
| boss-states / 120 / 4by3 | [原寸](ui-round75-before-boss-states-4by3.png) | [原寸](ui-round75-after-boss-states-4by3.png) |
| boss-warning / 128 / fhd | [原寸](ui-round75-before-boss-warning-fhd.png) | [原寸](ui-round75-after-boss-warning-fhd.png) |
| boss-warning / 128 / 4k | [原寸](ui-round75-before-boss-warning-4k.png) | [原寸](ui-round75-after-boss-warning-4k.png) |
| boss-warning / 128 / 4by3 | [原寸](ui-round75-before-boss-warning-4by3.png) | [原寸](ui-round75-after-boss-warning-4by3.png) |
| boss-impact / 162 / fhd | [原寸](ui-round75-before-boss-impact-fhd.png) | [原寸](ui-round75-after-boss-impact-fhd.png) |
| boss-impact / 162 / 4k | [原寸](ui-round75-before-boss-impact-4k.png) | [原寸](ui-round75-after-boss-impact-4k.png) |
| boss-impact / 162 / 4by3 | [原寸](ui-round75-before-boss-impact-4by3.png) | [原寸](ui-round75-after-boss-impact-4by3.png) |
| boss-cleared / 205 / fhd | [原寸](ui-round75-before-boss-cleared-fhd.png) | [原寸](ui-round75-after-boss-cleared-fhd.png) |
| boss-cleared / 205 / 4k | [原寸](ui-round75-before-boss-cleared-4k.png) | [原寸](ui-round75-after-boss-cleared-4k.png) |
| boss-cleared / 205 / 4by3 | [原寸](ui-round75-before-boss-cleared-4by3.png) | [原寸](ui-round75-after-boss-cleared-4by3.png) |

保存再読込：[FHD](ui-round75-after-boss-reload-fhd.png)／[4K](ui-round75-after-boss-reload-4k.png)／[4:3](ui-round75-after-boss-reload-4by3.png)。状態解除後：[FHD](ui-round75-after-reload-cleared-fhd.png)。

撮影の実時間待ちでは目標99frameを102まで越えた試行があり、比較には不採用。既存隔離7502保存先の再利用時はcheckpoint選択待ちを確認し、保存を削除せず実際の「続きから」で復帰した。状態の自然終了も、論理更新直後の旧描画を判断材料にせず通常描画更新後の消去を確認した。以後は既存UnityLogicalLoop.Advanceの記録再生用経路をbudget1で用い、通常Controller.TickLogicalFrameを通して目標frameで停止した。これはAssets外の検証手順で、製品時計・期間を変更するものではない。前後のframe・HP・敵位置・Sprite・root/scaleを別JSONで照合する。停止中の撮影はtimeScale0で保持し、Canvasを更新、解像度変更でCoreframeが進まないことを確認した。

関連464件、最終全3524件成功。実640×360のGameView/RenderTexture、専用TMPDIRとsaveguardを確認して同じ全件実行を追跡し、最終ソースハッシュも一致。実pause／resume、checkpoint再読込の初手5枚、カスタムの非表示／復帰／詳細往復、通常戦からボス、状態解除と表示消去を確認した。描画変更だけであり、製品の状態・攻撃時間や入力契約は変更していない。

独立コードレビュー6ファイルの必須未解決0。通常3敵6枚・ボス状態／予兆12枚の独立原寸視覚レビューも必須0。動的経路は主担当の確認であり、静止画レビューだけを操作確認とはしない。

Playの現在consoleはerror0／UnityAI警告1。Pipelineキャッシュには過去timeout error1、fixture pool上限1・edit mode再停止1・2起動分UnityAI警告2の計warning4が残る。最終全件のerror0／warning59は既存fixture5分類で、生記録を保存し抑制していない。回復したPipeline timeoutと製品側の結果は区別する。過去UGUI例外は未解決で、今回の成功から解決済みとしない。

完全重複個体の固有識別、複数同位置ボス、物理機器／実機buildは未検証。新探索仕様は文書のみで今回の表示修正へ実装を混ぜていない。D05／工場拡張／拒否済みキャラ・剣生成も対象外。原背景・JikiTourai・R74の足先余白／前景遮蔽／HPグループを維持。見た目の最終承認や無欠陥保証ではない。

次候補：予兆から発動後の被弾表示と既存の独立攻撃エフェクトの主従。専用本体モーション不足はプロップ／独立エフェクト完了と分けて扱う。
