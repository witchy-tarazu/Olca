# 生成Sprite/Texture所有 第十一段階

既存6系統をResourceManagerのRoot保持と利用側leaseへ接続。通常View/host切替では共有し、最後のRootと借用がなくなった世代の生成Sprite→Textureを解放します。import素材はBuilderの所有へ登録しません。[設計・正確なUI残件](../../../Planning/GameSystem管理基盤と段階移行.md)。

対象はSharedAttackPropCatalog、CombatStatusIconCatalog、AttackPropertyIconCatalog、ActorConditionVisual、DefensiveFieldVisual、TransformationEffectSprites。[所有対応](ownership-map.json)と[最終コードレビュー](code-review.json)。非表示中に参照を持つView/Visualは借用を保持し、参照解除後に返却。非Mono Visualも生成Rendererへ所有を紐付け、描画不成立の未束縛借用を返します。timeScaleに依存しない次描画frameの回収はCore時計を進めず、Root世代の付け替え/早期破棄/二重破棄を避けます。外部raw Sprite利用はRootまたは明示scopeを保つ契約です。

[実取得](edit-lifecycle.json)：6系統7Sprite/Textureを共有、Root返却後も同ID生存、最終借用返却で直接native参照全null、import生存。[Play遅延所有者](native-late-owner.json)：timeScale0でRoot終了後3frame生存→Rendererを後から破棄→4frame後、Inspectより前に全7組がnull。登録cache/borrow/pending0。

[全系統利用後host](loaded-host.json)：Root pin1で16組を共有し、Closeでborrow0、再Openで全ID維持、隔離保存10ファイルhash不変。[通常Root終了と再起動](normal-root-restart.json)：Play中の実GameSystemRoot通常Destroyで16Sprite/16TextureがInspect前に全null、Root/Camera/Listener/EventSystem/音源0、import生存。Bootstrap再起動で世代2→3、6系統を再取得。アプリ終了hookだけで成功扱いにはしていません。[代表入力/Single/保存復帰/失敗取消](native-base.json)、[保存比較](save-invariance.json)。

|表示|FHD|4K|4:3|
|---|---|---|---|
|特性mini|[原寸](properties-custom-fhd.png)|[原寸](properties-custom-4k.png)|[原寸](properties-custom-4by3.png)|
|独立飛翔体|[原寸](projectiles-fhd.png)|[原寸](projectiles-4k.png)|[原寸](projectiles-4by3.png)|
|guard/麻痺/防御|[原寸](guard-defense-status-fhd.png)|[原寸](guard-defense-status-4k.png)|[原寸](guard-defense-status-4by3.png)|
|変身開始|[原寸](transformation-fhd.png)|[原寸](transformation-4k.png)|[原寸](transformation-4by3.png)|
|変身解除|[原寸](transformation-release-fhd.png)|[原寸](transformation-release-4k.png)|[原寸](transformation-release-4by3.png)|

[画像manifest](image-manifest.json)。原寸15枚＋4K等倍局所を[独立視覚レビュー](visual-review.json)し今回の必須未解決0。24既存素材のbyte/寸法/LoadImage/pivot/PPU/Point/Clamp/gray計算は[静的照合](material-comparison.json)で不変。同一beforeを新撮影していないため、完全画素一致・美観全体の完成は主張しません。元背景/JikiTourai/スケール/アンカー/描画順/入力・保存・計算・固定frameを変更していません。

最終関連486/486（40.47秒）、全3594/3594（264.11秒）、失敗/skip/inconclusive0、console error0。[同Editor反復26件](same-editor-regression.json)も成功、各回EventSystem current CLR/Unity null・登録/Scene0、生成cache/borrow/pending0、global clearなし。新8回帰で共有/世代/複数Root/生成失敗/import保護/非表示・native所有者/初回不成立・Renderer再生成を確認。19sourceのsubmission/project/関連/全件/独立review hash一致。[警告59件](warning-classification.json)は従来fixture5分類と一致、抑制なし。

**Editor全体のnative漏れ解消とはしません。** 新registry外の同名Sprite群2095件が基準IDとして残ります。[旧群の境界](historical-unregistered-baseline.json)。生成元/漏洩原因は未確定で、import/未知利用者を誤破棄しないため同名一括Destroyをしません。今回の世代は登録IDの直接nullと世代差分で回収を立証し、registry0だけで旧群まで消えたとは扱いません。

[cleanup](cleanup.json)：原本clean、実640×360、専用TMPDIR/saveguard、仮想機器除去、EventSystem解放、最終source/資産一致。通常待機で回復した[Pipeline/検証ヘルパー/UnityAI履歴](console-history.json)は製品結果と別に保存。VM/Editor PID137059/model/認証を維持、テストやfixtureを重複起動せず、sourceは全件後不変です。

次の必須まとまりはpause/checkpoint等の未管理View/確認popupです。帰還/編成/会話/装備/詳細等の正確な残一覧は設計文書を継続。新外界/ボスラッシュ/未決D05/S08は実装せず、全HotReload・歴史的UGUI・実build・物理機器・聴感は未確認/未解決として残します。基盤全体完了や全ゲーム無欠陥保証とはしません。

公開はfailclosed check→receipt確認→commit→通常push/refetchを別操作で行います。
