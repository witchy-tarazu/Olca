# 既定探索ルートの氷地形（Round62）

既定の氷2区画（56・57セル）を、床より浮いた水色の線から、床内の淡い縁・小さな霜の断片・角張った亀裂へ整理した。元の石模様を透かし、既存の床と前景へなじませる。大きな半透明面は独立原寸レビューで「貼り付けたガラス片」と指摘されたため、小さく薄い3断片へ修正した。

描画部品は既存のwhiteSpriteを借用して作り、既存terrain rootの子として再利用する。地形変更・非表示・破棄は既存rootの寿命へ従う。新しいラスタ画像、元Spriteの編集、キャラ生成は行っていない。Core・氷の加速／滑り・接触・ダメージ・保存形式・入力領域・床／本体scale・背景・JikiTourai・Round60の4修正は不変。変更C#はGameFieldViewの1ファイルのみ。

## 実経路と比較

既定マップ、敵位置、デッキ、初手5枚を上書きせず、新規開始→実Raycast出撃→通常接敵→カスタム確定→報酬のskip／shop・回復イベント退出→既定氷へ進んだ。検証時間を抑えるため、最初の敵の撃破だけは既存Core被弾入力で解決した。攻撃ボタンでの撃破検証とは扱わず、マップや製品能力値は変えない。

同じ解像度・HP100・既定Ice状態・本体／cameraの表示位置をそろえて比較した。表示位置合わせは私有検証コードだけで行い、モデル位置や保存は変えない。画像取得後は追従を戻し、実進行で氷へ接地・通過した。ポーズ保持、実ポーズ画面からの保存再読込、その後の実接敵と初回カスタムでIcePatternの残留0を確認した。表示合わせ中の隔離保存6ファイルはhash不変。

| FHD 前／後 | 4K 前／後 | 4:3 前／後 |
|---|---|---|
| [前](before-default-ice-fhd.png) / [後](after-default-ice-fhd.png) | [前](before-default-ice-4k.png) / [後](after-default-ice-4k.png) | [前](before-default-ice-4by3.png) / [後](after-default-ice-4by3.png) |

PNGは実画面の原寸。テスト成功や独立レビューは、全体の美的完成／ユーザーの見た目承認を意味しない。

## UGUI例外の切り分け

Round61の記録はUGUI `Selectable.OnEnable:519`の登録配列書込で、一部`Slider.OnEnable`、通常再コンパイル直後と`UiProof.Prepare→OpenScene`の経路で発生した。製品stack frameは見られないが、製品無関与までは断定しない。最終2回の全件開始offset以降に同例外は0件。

テスト／私有AgentScriptsによるSelectableの静的配列・件数の書換えは見つからなかった。Packageの配列拡張は「件数＝容量」の場合だけで、負の件数や容量超過・長さ0などがあれば失敗し得るが、発生時の値がなく原因は未確定。今回は通常Play前3／160、通常Edit再コンパイル後4／10、試作の保存再読込後3／160・次接敵15／160と正常範囲だった。静的値のリセット、例外抑制、package変更、再現目的の繰返しPlay再コンパイルは行っていない。従来の例外を解消したとは報告しない。

## 仕様確認

開始・区切りの安全なfetch／FF pullで、09caf39を含むmaster文書を保持。現本文は次を確認した。文書取得であり、この地形表示単位で新たに実装していない。

- aa3256e：dash反転でも残時間をreset／延長しない。攻撃別にdash継続／停止を設定。壁滑り攻撃は基本的に壁と反対へ。
- 3a47430／b08a621：壁滑り・壁蹴りは初期能力ではなく、同じ特殊アイテムで同時・永続解放し装備不要。同じ壁の繰返し蹴り可、適格壁へ入力中だけ滑る。
- ダンジョン外の装飾品／パーツはpauseから随時変更。探索デッキ編集は回復／reload専用地点／拠点だけ。
- 99d4224／09caf39：通常回復地点はdeck reload＋HP全回復＋全状態解除＋auto save＋死亡復帰地点更新＋通常敵再出現。reload専用地点はdeck reload／編集だけで、その他の効果はない。

氷表示のために未決のdash cooldown等、性能値・新しい探索機能を追加していない。物理入力・実機buildは未確認。拒否済み画像生成の再試行も行っていない。

## 最終検証

既存の地形27件・移動40件が最終調整後も成功。最終全3,504件成功（262.10秒、失敗／skip0）、console error0／warning59。警告は実ログで報酬View未設定fixture50、設定読込失敗fixture4、報酬phase外2、使用可能カードなし2、pool上限1に分類。期待値緩和・診断抑制なし。画像取得のFHD／4K／4:3と、全件実行の実640×360は分けた。

最終通常Playの現在警告1はUnity AI接続情報のread-only保存警告で、製品素材警告とは区別。UGUIは[既存stack](../ui-round61-20261030/editor-ugui-exception-excerpt.log)・[独立読取調査](ugui-readonly-review.json)・[今回の正常な観測値](ugui-current-observations.json)を残し、全作業期間がerrorなしとは扱わない。

[全件とsource hash](verification.json)・[警告分類](warning-classification.json)・[比較状態](matched-state.json)・[実進行／保存再読込／次接敵](actual-route.json)・[表示中保存不変](visual-save-invariance.json)・[最終Play console](play-console.json)・[独立レビュー](review-results.json)・[保護確認](cleanup.json)・[入力機器](input-cleanup.json)。原本clean、追跡Assets一致（既存の私有URP2設定を除く）、実640×360、専用TMPDIR／保存ガード、native Mouse／Keyboardのみを復元確認。

次の有益な候補は、既定ルートにもあるダンジョン内回復イベントのマーカー（現在の四角い＋記号）の材質・視認性。既存のshop袋素材や元背景を無関係に上書きせず、ダンジョン外の新しい回復地点仕様とは分けて見直す。
