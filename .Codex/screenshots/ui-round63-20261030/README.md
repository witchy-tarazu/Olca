# ダンジョン内回復イベントのマーカー（Round63）

既定ルート54セルの回復イベントを、緑の四角と＋から、輪郭を持つ小さなしずく＋十字へ変更した。四角い囲いを外して面積と競合を抑え、ショップ袋とは形でも識別する。接近矢印・使用済みチェック・色／透明度の状態規則・root位置は従来どおり。回復だけの候補を持つ非Shop訪問に限り、その他の訪問や資源欠損時は従来の図形へ戻る。

## 素材と接続

OpenAI画像生成ツールで、既存の非キャラ素材 `Assets/Art/UI/Visits/shop_bag.png` の密度を参照し、独立した回復しずくを新規生成した。キャラ画像／拒否済み入力は使用していない。生成原本は [SourceFiles](../../../SourceFiles/Visits/recovery_drop_generated.png) に保持。承認済みの通常書出しで最大連結alpha成分を取り、比率を保つ最近傍縮小・6色パレット・硬い透過・基準位置合わせを行った。姿勢の回転／伸縮による代用ではない。

ゲーム用PNGは64×64、可視部分32×44、alpha0／255。Point／Clamp・PPU64で包装bytesから読み、Viewが所有するSprite／Textureだけを破棄する。Disableや画面内への再入場ではキャッシュを共有する。元背景、床、前景、ショップ素材、キャラscale／anchor、JikiTouraiは変更していない。[素材の出所・hash・書出し](asset-manifest.json)。

## 通常ルートと比較

既定マップ・デッキ・初手5枚は変更せず、実Raycast出撃→接敵→カスタム確定→報酬skip→ショップ退出→回復へ進んだ。最初の敵撃破は既存Core被弾入力、回復を観測するHP60の前提は隔離検証だけの `TakeDamage(40, affectsPoise:false)`。攻撃ボタンでの撃破や製品値の変更とは扱わない。

画像は同じ解像度・HP／状態・本体／camera表示位置を合わせた実画面。位置合わせは私有表示コードのみで、論理位置／保存を変更しない。接近時の40・使用後の＋20は既存の浮遊数値であり、撮影のために消去や時間短縮をしていない。浮遊数値の位相は描画待ちによって完全一致しない。画像取得中の隔離保存hash不変を確認した。

| 状態 | FHD 前／後 | 4K 前／後 | 4:3 前／後 |
|---|---|---|---|
| 接近 | [前](before-recovery-ready-fhd.png) / [後](after-recovery-ready-fhd.png) | [前](before-recovery-ready-4k.png) / [後](after-recovery-ready-4k.png) | [前](before-recovery-ready-4by3.png) / [後](after-recovery-ready-4by3.png) |
| 使用後・退出済み | [前](before-recovery-used-fhd.png) / [後](after-recovery-used-fhd.png) | [前](before-recovery-used-4k.png) / [後](after-recovery-used-4k.png) | [前](before-recovery-used-4by3.png) / [後](after-recovery-used-4by3.png) |

実ポーズ保持→接近／入場→選択取消→再選択／受取→連続確定→退出→実ポーズ画面の保存再読込→次接敵の初回カスタムを確認した。既存回復量20、HP60→80、取消のHP／保存不変、受取時Sequence4保持、退出時5への保存、復帰時Closed維持を検証した。ダンジョン外の全回復／deck reload／死亡復帰地点等の新文書とは別で、その実装を含まない。

## 範囲と限界

Core・トリガー・使用回数・回復量・保存契約・固定frame・入力領域・既存演出時間を変更しない。物理入力／実機buildは未確認。UGUIの過去のEditor再読込例外は原因未確定の履歴として残し、今回のマーカーで解消したとはしない。テストや独立レビューは美的完成／ユーザー承認の代わりではない。

初回の隔離fixtureでは通常被弾40が強靭度停止も作ったため保存ガードが正しく拒否した。既存保存は保持し、通常Play停止後、新しい隔離先でHPだけの前提へ修正した。製品側のガード／一時状態を消して回避していない。[検証前提の切り分け](harness-precondition.json)。

[実経路](actual-route.json)・[比較状態](matched-state.json)・[表示中保存不変](visual-save-invariance.json)・[最終Play console](play-console.json)・[Play警告分類](play-warnings.json)・[関連25件](targeted-tests.json)・[独立レビュー](review-results.json)。現在Playのconsole error0／warning1はUnity AI接続情報のread-only保存警告。relay履歴の重複stop警告とは件数を区別した。

次の具体的な候補は、同じ既定ルート51セルにある接触回復アイテム（`FieldHealCell_`の小さな四角＋）の視覚整理。今回の停止イベントとは高さ・接触用途・消去条件が異なるため、同じ見た目を無条件に流用せず、実画面と仕様から区別を確認する。ダンジョン外の新mechanicsには拡張しない。

## 最終検証

関連25件、最終全3,505件成功（256.37秒、失敗／skip0）。console error0／warning59。警告は実ログで報酬View未設定fixture50、設定読込失敗fixture4、報酬phase外2、使用可能カードなし2、pool上限1。期待値緩和／診断抑制なし。[全件・source hash](verification.json)・[警告分類](warning-classification.json)・[保護確認](cleanup.json)・[入力機器](input-cleanup.json)。

実640×360、専用TMPDIR／保存ガード、inactive controller、native Mouse／Keyboardのみを確認。原本clean、追跡Assetsと新規PNG／bytesは検証版と一致（既存の私有URP2設定を除く）。独立コード／素材／原寸視覚レビューの今回範囲で未解決指摘0。テスト結果を美的完成と同一視しない。
