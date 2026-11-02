# 通常状態の表示整理

通常状態は無属性・神依していないという525bff5/970bdbbの確定仕様に合わせ、拠点/帰還結果/出撃スタイル一覧のbasic表示を「通常状態 / 無属性（カムイなし）」へ揃えた。表示専用変換だけであり、互換ID `basic`・定義名「基本 / 無属性」・保存Capture/定義hash・属性/戦闘性能を変更しない。その他の登録名/元のひらがな/Latin、漢字部分だけのカタカナ化、JikiTourai/元背景/水鏡/配置は維持する。キックが使えるという説明や攻撃接続・素材生成は行っていない。

通常Bootstrap拠点の製品入口からスタイル画面を実Raycastで開き、通常状態の選択/取消/確定/再表示、取消/確定後の古い操作の拒否を確認した。[実操作](native-product-paths.json)。同じbasic確定は既存の無書込経路を使い、保存bytes/進行revision/所持/既存Dungeonと外界Core/旧定義Captureを維持。物理入力や新攻撃入力の検証ではない。host終了の既存Suspend保存は別境界として記録し、Stop/全件後に保存bytesが変わらないことを確認した。

[旧拠点の実画面](../system-stage35-20261102/base-fhd.png) / [旧選択の実画面](../ui-round57-20261030/after-single-fhd.png)は過去版の参考。今回同時取得したmatched before/afterとは扱わない。下記9枚は最終v2の画面（拠点/選択は実入口、帰還は保存しないレイアウトfixture）で、各状態の論理frame/プロフィール/性能を固定しCanvas更新と実Screen/2RTを確認した。[manifest](image-manifest.json)。

|画面|FHD|4K|4:3|
|---|---|---|---|
|拠点|[画像](hub-fhd.png)|[画像](hub-4k.png)|[画像](hub-4by3.png)|
|状態選択|[画像](style-fhd.png)|[画像](style-4k.png)|[画像](style-4by3.png)|
|帰還レイアウトfixture|[画像](return-layout-fhd.png)|[画像](return-layout-4k.png)|[画像](return-layout-4by3.png)|

最終4sourceでUI関連382/Integration620、全3719件成功（282.26秒、失敗/skip/inconclusive0）。実640×360/2RT・専用TMPDIR/saveguardと新規RunStarted/RunFinishedログを確認。console error0、警告は[分類](warning-classification.json)し抑制なし。[全件](full-tests.json) / [関連](related-tests.json) / [独立コード](independent-code-review.md) / [原寸視覚レビュー](visual-review.json) / [cleanup](cleanup.json)。

キックの未決規則/製品定義/不足素材は[未決引継ぎS11](../../../Planning/未決仕様引継ぎ.md)、今回範囲と実装段階は[設計記録](../../../Planning/外界探索入口の実装段階.md)。S09/S10は別で未決のまま。帰還は実際の報酬/保存を進めない明示レイアウトfixture3枚で、現所持・攻撃と同じ表示helperを使って確認（未帰還の初期profileのReturnedHp=0によるHP0/100はfixture由来で、実PlayerHP100は不変）。正常帰還transactionの実入口到達とは区別し、既存Integration回帰も確認。実build/物理入力/聴感/全HotReload、歴史的UGUI/Pipeline/CLI/UnityAI/登録外Sprite2095等は別の限界として保持。[履歴/限界](history-and-limits.json)。
