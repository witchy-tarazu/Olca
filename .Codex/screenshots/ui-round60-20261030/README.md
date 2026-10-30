# 実プレイで指摘された4点の修正（Round60）

初期手札、ダッシュ中の揺れ、出撃先選択の混雑、前景と床の継ぎ目を対象にする。予告枠／マグマの予定作業は中断し、無関係な素材追加は行わない。元背景、JikiTourai、本体／床のスケールと座標、固定論理移動、既存保存形式は保持。

## 原因と変更

- **初期手札**：仕様「バトルシステムについて」50〜59行の既定上限5／能力で6・7／残った手札を保って上限まで補充に対し、CustomScreenSystemの既定値が10だった。最大表示10枠とゲーム上限を分離し、通常の新規戦闘を5へ修正。保存済みHandLimitを構築時から渡し、旧10枚の戦闘・変身の基底はその保存値のまま復帰する。固定10枚の外部再生／テストfixtureは明示10で契約維持。
- **ダッシュの揺れ**：実際の出撃／ダッシュ入力から描画フレームごとに記録。同じ論理時計・本体位置・絵のままカメラだけ進む2箇所を再現。探索中のカメラ追従を本体と同じ60Hz論理フレームで進め、描画だけのフレームでは進めない。接敵の外部補間時計は維持。移動速度／距離／絵／pivotを変更していない。連続測定は[dash-before.csv](dash-before.csv)／[dash-after.csv](dash-after.csv)。修正後の同論理フレーム2組はカメラ移動0。
- **ステージ選択**：通常のScene初期画面（全補助操作あり）で、4つの装飾区画、重複する行き先名、選択肢ごとの冗長な見出し、1ページだけの無効な前後操作が競合していた。単層化し、行き先→敵／地形→出撃の順へ左揃え。選択状態とクリア／未解放条件は保持し、出撃に対し取消／スタイル／交換／常設補助を控えめにする。詳細／長文の既存入口、ID、選択／保存／出撃callbackは変えない。
- **前景**：実Sceneの床下端と不透明な埋め帯の上端はほぼ接触だけで、岩の最高峰だけを合わせたため谷側に長い水平切断線が残った。2画素余裕だけの初案は独立実画像レビューで不十分と判断して再修正。元PNGの全幅連続不透明帯を床の実描画下端へ合わせ、峰と谷の形が下端を覆うようにする。原絵、等倍、parallax係数、論理グリッドは不変。帯を揃えた第2案にも元絵の両端22列が黒い板として残る指摘が出たため、元絵を変えず等倍タイルを96px重ねる配置へ修正。alpha輪郭から、両端高さ27行が隣接する岩31行以上／36行以上に隠れる有効幅85〜103pxを調べ、区間内の96pxを採用した。112px案は実際の輪郭検算で不成立と分かり採用していない。

## 通常経路と保存

Sceneの敵位置／間隔／デッキ／手札をfixtureで上書きせず、新規開始→実EventSystem Raycast出撃→探索ダッシュ→50セル目の接敵→最初のカスタムを確認。新規20枚デッキから5枚／上限5。修正前の隔離された実保存へ「続きから」で戻り、10枚／上限10とFirstHandの定義順が一致した。原保存の4JSONはhash不変で別保全。個人保存やJSON本文は公開しない。

ステージとカスタムの比較は同じScene／解像度／状態。前景比較はダッシュ後の本体とカメラの**表示位置だけ**を修正前へ合わせ、モデル位置不変を確認して取得。旧カスタム10枚と新5枚は仕様修正の前後であり、同じ枚数を演出したfixtureではない。

## 検証・限界

最終全件は3,503件成功（275.36秒、失敗／skip0）。console error0／warning59を実ログで分類（報酬View未設定fixture50、設定読込失敗fixture4、報酬phase外2、使用可能カードなし2、pool上限1）。警告の抑制や期待値の緩和はしていない。通常PlayのUnity AI接続情報read-only警告1件は別に記録。物理パッド・タッチ・IME・実機buildは今回確認していない。連続測定は動画撮影ではなく、実描画フレームの位置／pivot／sprite記録。Passing testsだけで視覚完成とは扱わない。

最新pullでe8c27a6（badc022を含む）の文書更新を保持。ダンジョン外のダッシュ方向変更、空中左右調整、基本無敵なし／装備で付与、ゲージ消費なし／2連続後の短いcooldownと連続回数reset（具体時間未決）の記述であり、今回のダンジョンUI／表示修正と別。文書取得を新機能実装済みとしない。

## 比較画像

| 対象 | FHD 前／後 | 4K 前／後 | 4:3 前／後 |
|---|---|---|---|
| ステージ選択 | [前](before-stage-fhd.png) / [後](after-stage-fhd.png) | [前](before-stage-4k.png) / [後](after-stage-4k.png) | [前](before-stage-4by3.png) / [後](after-stage-4by3.png) |
| 前景・床下端 | [前](before-foreground-fhd.png) / [後](after-foreground-fhd.png) | [前](before-foreground-4k.png) / [後](after-foreground-4k.png) | [前](before-foreground-4by3.png) / [後](after-foreground-4by3.png) |
| 初回カスタム | [前](before-initial-custom-fhd.png) / [後](after-initial-custom-fhd.png) | [前](before-initial-custom-4k.png) / [後](after-initial-custom-4k.png) | [前](before-initial-custom-4by3.png) / [後](after-initial-custom-4by3.png) |

前景は床と本体／カメラの表示位置を合わせ、モデル位置不変を確認して撮影。原寸PNGを開いて比較できる。

## 全件で検出した固定fixture依存

最初の全件は3,503件中20件失敗（[記録](first-full-failures.json)）。5件はCustomBattleReplayが初回6素材を必須にする固定再生列で、上限10を明示して元のコマンド／期待値を維持した。通常プレイの既定5枚へ影響しない。

残る15件は、6枚デッキの初手抽選が6回から5回になり、seed7の通常敵50%ドロップが「なし」へ変わったことによる。HUD／帰還fixtureで保存計画RewardOffered=false・候補0・phaseDungeon・保存停止なしを実確認した（[probe](reward-failure-probe.json)）。報酬ありを前提に取得／喪失／保存を検証する2つのfixtureだけseed0を明示し、勝利操作前にRewardOfferedをassertする。元の取得数／保存／再試行の期待値は保持。製品の率・乱数順・保存済み結果は変更しない。最終全件は再コンパイル後のソースで再実行する。


[最終全件とsource hash](verification.json)・[警告分類](warning-classification.json)・[通常操作](normal-operations.json)・[最終初回カスタム](final-normal-flow.json)・[旧10枚復帰](legacy-verification.json)・[独立レビュー](review-results.json)・[保護確認](cleanup.json)・[入力機器](input-cleanup.json)を併設。19ファイルの最終sourceは検証copy／公開copyで一致し、視覚確認した17ファイルは最後のfixture修正でも不変。原本clean、追跡Assets876件一致（既存の検証用URP2設定を除く）、実640×360、専用TMPDIRと保存ガード、native Mouse/Keyboardのみを確認した。公開前pullはe8c27a6と一致し、追加差分なし。

今回の4件に対する確認であり、全画面の美的完成や未実装のダンジョン外移動、物理入力の動作を保証するものではない。予告枠／マグマ床の予定改善はこの優先修正中には進めていない。
