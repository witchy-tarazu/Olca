# Round76 着弾・被弾数値の主従と対象への対応

通常3敵とボスで、個別ヒットと回復が大きな白列となり本体・名前・HPより目立っていた。JikiTouraiの字形と表示内容を保ち、浮遊数値だけcharacterSize .05→.035、細い暗縁を加えた。合算せず7/11/3/+4等を各効果のまま表示する。元のprefab参照・フォント・色・0.8秒の寿命・上昇／fade時計を維持し、縁は同じ字形／透明度から再利用して同期する。

段間は新しい文字の実外周で測り、画面端補正を除いた同ownerの残存数値の上へ余白を空けて積む。先頭だけ返却された連続表示で、active数から空いた段を誤用しない。数字はsorting20、暗縁はその直後方とし、既存状態や着弾色の上でも文字を読めるようにした。R75の予兆／状態の描画順を変更したものではない。

縮小だけの初期候補では列と本体の距離が残るとの独立視覚指摘があり、起点も再調整した。通常敵は可視本体と有効な名前／HP線の実外周を使い、LineRendererの保守的boundsで余白を増やさない。固定HPのボスは可視本体直上から表示し、名前との重なりは既存Poolの群単位回避へ任せる。4:3で最下段7がHP円へ接触する視覚指摘を受け、既知本体の出生余白を.20→.32worldへ再調整した。本体素材なしの旧Viewは.20の従来フォールバックを保つ。本体／足位置／pulse・敵名／HP群・床端／前景・入力領域は変更していない。

起点回帰の破棄時に、既存pulseが破棄済みViewへアクセスするMissingReferenceExceptionを検出した。待機後のthis／Renderer／取消確認を追加し、倍率・回数・時間は変えていない。失敗をLogAssert期待や抑制で隠さず、同じ破棄回帰を再実行した。テスト用の明示HP線に自動生成円も混ざるセットアップ不一致は分離し、厳密な起点期待値は維持した。

新しい画像生成は行わず、既存JikiTouraiと既存prefabを使用。拒否済みキャラ／剣生成を再試行していない。ダメージ・回復計算、合算順、判定、攻撃発生・状態期間・固定フレーム・保存形式を変更していない。

比較元はa0b99e4。同一隔離fixture／seed7／既存CreateTestDungeon(6,1,2)、CoreframeとCanvas更新後の原寸PNG。検証用7/11/3ダメージと4回復を既存Battle.RunLogicalFrame／CombatFeedback経路へ与えたもので、製品性能値を登録していない。個別表示数、HP、モデルgrid/HP、Sprite、画面上の本体アンカーとscale、前景とHUDの位置をJSONでも照合する。再現fixtureの世界原点が共通に平行移動した場合は差を保存し、個体間の相対配置と投影位置を厳密に比較する。生のworld座標まで同一とは記さない。

|場面・frame・出力|変更前|変更後|
|---|---|---|
| dense / 90 / fhd | [原寸](ui-round76-before-dense-fhd.png) | [原寸](ui-round76-after-dense-fhd.png) |
| dense / 90 / 4k | [原寸](ui-round76-before-dense-4k.png) | [原寸](ui-round76-after-dense-4k.png) |
| dense / 90 / 4by3 | [原寸](ui-round76-before-dense-4by3.png) | [原寸](ui-round76-after-dense-4by3.png) |
| single / 90 / fhd | [原寸](ui-round76-before-single-fhd.png) | [原寸](ui-round76-after-single-fhd.png) |
| single / 90 / 4k | [原寸](ui-round76-before-single-4k.png) | [原寸](ui-round76-after-single-4k.png) |
| single / 90 / 4by3 | [原寸](ui-round76-before-single-4by3.png) | [原寸](ui-round76-after-single-4by3.png) |
| boss-dense / 120 / fhd | [原寸](ui-round76-before-boss-dense-fhd.png) | [原寸](ui-round76-after-boss-dense-fhd.png) |
| boss-dense / 120 / 4k | [原寸](ui-round76-before-boss-dense-4k.png) | [原寸](ui-round76-after-boss-dense-4k.png) |
| boss-dense / 120 / 4by3 | [原寸](ui-round76-before-boss-dense-4by3.png) | [原寸](ui-round76-after-boss-dense-4by3.png) |
| boss-single / 120 / fhd | [原寸](ui-round76-before-boss-single-fhd.png) | [原寸](ui-round76-after-boss-single-fhd.png) |
| boss-single / 120 / 4k | [原寸](ui-round76-before-boss-single-4k.png) | [原寸](ui-round76-after-boss-single-4k.png) |
| boss-single / 120 / 4by3 | [原寸](ui-round76-before-boss-single-4by3.png) | [原寸](ui-round76-after-boss-single-4by3.png) |

時間差連続表示：[FHD](ui-round76-after-serial-feedback-fhd.png)／[4K](ui-round76-after-serial-feedback-4k.png)／[4:3](ui-round76-after-serial-feedback-4by3.png)。

保存再読込後：[FHD](ui-round76-after-reload-feedback-fhd.png)／[4K](ui-round76-after-reload-feedback-4k.png)／[4:3](ui-round76-after-reload-feedback-4by3.png)。

実AIの予兆→命中（player58→51）、成立したポインターの攻撃ボタンRaycast（boss176→166、個別10表示）、時間差の連続表示、数値表示中のpause／resume、checkpoint初手5枚とカスタム詳細往復、次戦ボス、自然消去を確認。汎用ヘルパーにeligibleForClickが無かった試行は製品に正しく拒否されたため、通常のクリック成立条件で再確認した。製品入力を変更／迂回したものではない。主担当の動作検証と独立原寸視覚評価は別の証跡とする。

独立コード5ファイルと原寸after12枚／before代表比較のレビューは、今回範囲の必須未解決0。4:3の接触指摘を修正して再レビューした結果であり、最初の候補をそのまま承認したものではない。

最終ソースで関連467件／全3527件成功（256.63秒）。実640×360のGameView/RenderTexture・専用TMPDIR・saveguardを確認して同じ全件実行を追跡し、ソースハッシュを照合。最終console error0／warning59は既存fixture5分類（50＋4＋2＋2＋1）。Playでは回復したPipeline startup timeoutがerror1、UnityAI read-only警告1。キャッシュの過去warn3（fixture上限／already edit／UnityAI）と現在値を分けて保存。Pipelineの起動／reload timeout・socket切断とUnityAI read-only警告の履歴は消去せず、製品側のテスト成功や歴史的UGUI例外と混同しない。

完全重複個体の固有識別、複数同位置boss、物理機器／実機build、歴史的UGUIは未解決／未検証。極端な別owner群の衝突一般を解決したものではない。新探索仕様は文書のみ、D05／工場拡張も別件。今回を最終的な見た目の承認や全ゲーム無欠陥保証とはしない。

並行文書更新c449140／merge69bbfc3を保護して取り込み、[引継ぎ](../../../Planning/未決仕様引継ぎ.md)の旧「外界通常敵は通貨のみ」を訂正した。通貨＋経験値、撃破時の直接経験値加算／即時成長、最大HP増加分だけ現在HPも増加。通貨の物理回収／未回収消失は別規則。別マップ復帰で通常敵再出現、ダンジョン往復だけでは再出現なし。文書取得であり今回の外界ロジック実装ではない。

次候補：独立飛翔体・着弾素材のサイズと発動前後の読み取りを実戦全景で整理する。本体攻撃モーション不足はプロップ／独立エフェクトと分けて扱う。
