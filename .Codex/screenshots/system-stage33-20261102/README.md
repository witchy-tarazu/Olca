# 外界探索: 独立デッキの初期手札をHUDへ接続

確定した20枚構成/所持・同名・レギュレーション制限/初期手札3枚を、Dungeonと独立したRoot状態として登録し表示する最小接続。既存DepartureDeckRulesの検証とclone、初期履歴が空の専用Deck.DrawCardsを一度だけ使用。既存Deck.Shuffleは使用履歴も戻すため、手札引き直しへ転用せず変更していない。

**製品デッキの登録元は未接続**。通常入口はデッキ未登録と空枠を表示し、Dungeonデッキを黙って流用/配布しない。実検証は既存sword/bomb/heal/shield/Normal fireフォールバック各4枚、独立seed33を明記した隔離fixtureから実入口へ開く。性能/価格/カードコストを新しく決めていない。選択/使用/補充/手札引き直し/ゲージ消費/編成・複数保存/外界保存は未実装。[範囲と履歴](history-and-limits.json)。

[実登録・入口・HUD・入力・保存](native-lifecycle.json) / [host/Single再入と解放](native-host-lifetime.json)。Viewの再表示は通常マップ間移動の全回復/デッキ再装填とは別で、そのマップ境界処理は未接続。Viewが再生成されても同じ手札個体・順序・残り17枚を保持。閉鎖後自然Destroyは閉じたまま、表示中自然Destroyの復旧も保護。実Raycastの入口/戻る/Pauseとtimestamped keyboard移動を確認し、通常操作の旧Dungeon状態/実保存bytesは不変。host Suspendは従来の別保存境界。新しいカード操作キーやボタンを作っていない。

閉鎖後の撮影helperがRootにCardsがあることをView表示中と誤認し、UI読取だけで一度停止した。製品の閉鎖は正しかったため、probe条件をpresented併用に直し、同じPlayから再開。初期登録/Destroyを繰り返していない。元の局所save digestは未serializeだったが、破棄直後までの比較assertは通過しており、[再開時のdigest基準](capture-recovery-baseline.json)とその後の比較を別区間として記録する。

専用miniを既存hostのresolverから借用し、正方形・非raycastで表示。閉鎖/破棄で参照を外し、他Viewの生成Spriteやimport資産をDestroyしない。名前は表示だけ既存漢字→カタカナ変換、元名/ひらがな/Latinを保持。初回独立レビューの変換漏れ1件は[履歴](initial-code-review.md)を保持してv2で修正し、最終テストへ流用していない。

12枚は各行の論理frame/位置/ゲージ/手札個体・内容を固定しCanvas更新後の実RTで取得。[manifest](image-manifest.json)。前段階の旧版画像を撮り直した比較とは扱わず、未登録/登録/閉鎖/復旧の実状態を示す。元背景/JikiTourai/本体比率・足位置・移動60Hzを維持。

|状態|FHD|4K|4:3|
|---|---|---|---|
|unregistered|[画像](unregistered-fhd.png)|[画像](unregistered-4k.png)|[画像](unregistered-4by3.png)|
|open-before|[画像](open-before-fhd.png)|[画像](open-before-4k.png)|[画像](open-before-4by3.png)|
|closed-after-destroy|[画像](closed-after-destroy-fhd.png)|[画像](closed-after-destroy-4k.png)|[画像](closed-after-destroy-4by3.png)|
|active-recovered|[画像](active-recovered-fhd.png)|[画像](active-recovered-4k.png)|[画像](active-recovered-4by3.png)|

最終Core40/Application556/Integration605、同Editor反復130、全3704件成功（324.95秒）。実640×360/2RT・専用TMPDIR/saveguard、最終15source hash一致。[全件](full-tests.json) / [反復](same-editor-regression.json) / [独立コード](independent-code-review.md) / [原寸視覚レビュー](visual-review.json)。console error0、警告は[分類](warning-classification.json)して抑制なし。[cleanup](cleanup.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。
