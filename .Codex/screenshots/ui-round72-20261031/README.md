# Round72 敵本体と名前・HPの対応

通常敵の名前と円形HPを可視本体の近くへ揃え、重なる組だけ上方へ分離した。ボス名も可視範囲に接続し、右固定の縦HP・強靭度・状態欄は既存配置を保つ。操作盤は既存Face色を保持しalphaを0.68倍にして、背後の本体端を読みやすくした。ボタン位置・入力領域は同じ。

元画像の透明余白ではなく、既存 `Resources/OlcaDefensiveFields/actor-bounds.json` の校正をHUDへ接続した。新しい画像や性能値の追加ではない。通常敵のHP円・数値・右外周の強靭度、背景・JikiTourai・キャラのscale/pivot、床/前景、固定論理フレーム、入力と保存の契約は維持する。

比較の基準ソースは `9003c9ae643620ef14a413abf39e83e554b5e6d0`。同じ隔離初期データ・seed・画面状態・出力寸法で取得したnative PNGをリンクする。旧ソースを実際に検証projectへ読み込み、取得後は最終submissionへ戻して明示recompileした。旧ソースと新ソースの異なる実行間で完全同一の時刻を主張せず、各取得内は論理frameを保持して全比率を撮影する。

| 状態・出力 | 変更前（元ソース） | 変更後 |
|---|---|---|
| 通常・カスタム / fhd | [原寸](ui-round72-before-final-normal-custom-fhd.png) | [原寸](ui-round72-after-normal-custom-fhd.png) |
| 通常・カスタム / 4k | [原寸](ui-round72-before-final-normal-custom-4k.png) | [原寸](ui-round72-after-normal-custom-4k.png) |
| 通常・カスタム / 4by3 | [原寸](ui-round72-before-final-normal-custom-4by3.png) | [原寸](ui-round72-after-normal-custom-4by3.png) |
| 通常戦闘 / fhd | [原寸](ui-round72-before-final-normal-play-fhd.png) | [原寸](ui-round72-after-normal-play-fhd.png) |
| 通常戦闘 / 4k | [原寸](ui-round72-before-final-normal-play-4k.png) | [原寸](ui-round72-after-normal-play-4k.png) |
| 通常戦闘 / 4by3 | [原寸](ui-round72-before-final-normal-play-4by3.png) | [原寸](ui-round72-after-normal-play-4by3.png) |
| 3敵・同位置含む / fhd | [原寸](ui-round72-before-final-multi-play-fhd.png) | [原寸](ui-round72-after-multi-play-fhd.png) |
| 3敵・同位置含む / 4k | [原寸](ui-round72-before-final-multi-play-4k.png) | [原寸](ui-round72-after-multi-play-4k.png) |
| 3敵・同位置含む / 4by3 | [原寸](ui-round72-before-final-multi-play-4by3.png) | [原寸](ui-round72-after-multi-play-4by3.png) |
| 接近後 / fhd | [原寸](ui-round72-before-final-multi-approach-fhd.png) | [原寸](ui-round72-after-multi-approach-fhd.png) |
| 接近後 / 4k | [原寸](ui-round72-before-final-multi-approach-4k.png) | [原寸](ui-round72-after-multi-approach-4k.png) |
| 接近後 / 4by3 | [原寸](ui-round72-before-final-multi-approach-4by3.png) | [原寸](ui-round72-after-multi-approach-4by3.png) |
| 予兆表示中 / fhd | [原寸](ui-round72-before-final-multi-warning-fhd.png) | [原寸](ui-round72-after-multi-warning-fhd.png) |
| 予兆表示中 / 4k | [原寸](ui-round72-before-final-multi-warning-4k.png) | [原寸](ui-round72-after-multi-warning-4k.png) |
| 予兆表示中 / 4by3 | [原寸](ui-round72-before-final-multi-warning-4by3.png) | [原寸](ui-round72-after-multi-warning-4by3.png) |
| 次戦ボス / fhd | [原寸](ui-round72-before-final-boss-play-fhd.png) | [原寸](ui-round72-after-boss-play-fhd.png) |
| 次戦ボス / 4k | [原寸](ui-round72-before-final-boss-play-4k.png) | [原寸](ui-round72-after-boss-play-4k.png) |
| 次戦ボス / 4by3 | [原寸](ui-round72-before-final-boss-play-4by3.png) | [原寸](ui-round72-after-boss-play-4by3.png) |

追加確認：

- multi-custom: [fhd](ui-round72-after-multi-custom-fhd.png) / [4k](ui-round72-after-multi-custom-4k.png) / [4by3](ui-round72-after-multi-custom-4by3.png)
- normal-states: [fhd](ui-round72-after-normal-states-fhd.png) / [4k](ui-round72-after-normal-states-4k.png) / [4by3](ui-round72-after-normal-states-4by3.png)
- multi-states: [fhd](ui-round72-after-multi-states-fhd.png) / [4k](ui-round72-after-multi-states-4k.png) / [4by3](ui-round72-after-multi-states-4by3.png)
- boss-custom: [fhd](ui-round72-after-boss-custom-fhd.png) / [4k](ui-round72-after-boss-custom-4k.png) / [4by3](ui-round72-after-boss-custom-4by3.png)
- boss-states: [fhd](ui-round72-after-boss-states-fhd.png) / [4k](ui-round72-after-boss-states-4k.png) / [4by3](ui-round72-after-boss-states-4by3.png)
- normal-reload: [fhd](ui-round72-after-normal-reload-fhd.png) / [4k](ui-round72-after-normal-reload-4k.png) / [4by3](ui-round72-after-normal-reload-4by3.png)

検証は通常の出撃→接触→初期カスタム→Raycast決定、表示隠す/戻す、pause/復帰、隔離checkpoint読込と初手5枚復帰を含む。複数敵・bossは既存 `CreateTestDungeon(6,1,2)` の検証用定義を使う（通常50/boss200）。状態付与、Approach行動、既存の7damage/12frame予兆の検証入力、直接撃破はfixtureであり製品設定へ登録していない。次戦へは既存報酬のRaycast skipと自動接触で進む。予兆の自然終了と旧HP組の解放も確認した。

最初の未settle画像と、4:3が発動後になっていた初期予兆画像は不採用。Canvasを実更新し、取得全体でCore pauseを保持して取り直した。準備呼出の期限超過は起動完了と扱わず、実状態を確認してから未実行工程へ進んだ。全件の重複起動はしていない。

関連441件成功。最終ソースは実GameView/RenderTexture640×360・専用TMPDIR・save guardを確認して全3514件成功。consoleはerror0、warning59で、既存fixtureの診断メッセージに分類し抑制していない。PlayのUnityAI接続情報保存警告は環境/ツール側として別扱い。詳細はJSON証跡を参照。

独立コード・原寸視覚レビューの今回範囲で必須未解決0。完全同位置・同外見の本体識別と、固定入力位置のためのtouchHUD/本体下端の一部重なりは残る。6体同位置は自動回帰、実画像は3敵と単独boss。物理機器・実機buildは未確認。歴史的UGUI例外は未解決のまま区別し、この検証をゲーム全体の無欠陥や見た目の最終承認とはしない。

最終640×360、native Mouse/Keyboardのみ、EventSystem、保存ガード、原本checkoutのclean状態、検証projectとsubmissionのtracked Assets一致を確認（既存のprivate URP2件は従来どおり別管理）。

次候補：戦闘の操作案内・使用待機/タメ状態の情報量と主従を、実際の入力割当と通常戦闘全景で整理する。

公開前の仕様更新：`68ceb65a62b28ab2e39083ae4a626d2d1a5f2279` までfast-forwardで取り込んだ。差分はゲーム全体仕様25行のみで、ダンジョン外の死亡/進行保存・マップ/ファストトラベル・入口/再挑戦・帰還時の探索状態復元。文書取得であり、この機能の実装完了とは扱わない。今回の戦闘HUD規則に変更はなく、検証済みC#ハッシュは取り込み後も一致した。
