# 新規明示定義の相性基本耐性

`ElementAffinity`は五行相克と陰陽を、受ける攻撃属性への基本耐性へ解決する。弱点−50、Player有利側＋50、Enemy有利側0、陰陽相互−50、同属性/無0。別倍率は追加せず、既存の装備加算・−100〜100 clamp・端数切捨てを使用する。弱点/有利の基準は明示引数で調整可能。`actualBaseResistances`は全基本値の置換であり、暗黙の加算や部分上書きではない。

`CurrentElementDefenseDefinition.ForPlayer/ForEnemy`を明示して新しく作るActorだけに適用。本人属性とstyleの整合・役割・旧Wind/未知値を検証してから生成する。旧constructor/SetDepartureStyle/復元/既存製品登録に導出を入れていない。既存DTOへ解決済み値を保存し、再読込時に最新基準へ置換しない。既存hashと旧数値も不変。

新規Playerは既存CoreFactoryへ渡し、通常基準と個別全体置換の2ケースを実FileCheckpointStoreへ保存して各3往復。Player/Enemyの実被弾、基本値不変、読込前後のファイルbytes不変を確認。新規Enemyは既存公開APIで隔離ルートへ登録し、管理hostの生成→実際の出撃/接触→初手5枚Custom→画面を隠す→敵状態→次/前の敵→閉鎖/再表示/戻るを実先頭Raycastで確認した。登録先・名前・HP等は検証fixtureで、製品マップ/性能の新規正式登録ではない。[実経路・保存](native-product-paths.json)。

|新規明示Enemy|FHD|4K|4:3|
|---|---|---|---|
|金：火−50/木0|[画像](metal-enemy-fhd.png)|[画像](metal-enemy-4k.png)|[画像](metal-enemy-4by3.png)|
|陰：陽−50|[画像](yin-enemy-fhd.png)|[画像](yin-enemy-4k.png)|[画像](yin-enemy-4by3.png)|

各組は同論理frame・状態、Canvas更新と実Screen/2RT寸法を確認した。明示データ未登録の旧画面をbeforeとして作っていない。6枚のSHA/寸法と独立原寸視覚レビュー、最終2sourceの独立コードレビューは必須指摘0。[画像](image-manifest.json) / [レビュー](independent-review.json)。

最終v1の関連28件33.08秒・全3740件275.19秒が成功、失敗/skip/inconclusive0、console error0。8×8のPlayer/Enemy表、個別調整、実被弾/装備加算/丸め、保存復元、未知値拒否、旧生成への自動非適用を確認。旧測定データ・18製品定義hashも前後一致。実640×360・2RT・専用TMPDIR/saveguard・新run開始/終了・最終source一致を確認。[関連](related-tests.json) / [全件](full-tests.json)。警告は[分類](warning-classification.json)して抑制していない。原本clean、928資産照合（従来URP2例外）、保存bytes、入力/lease/cache/EventSystemと低解像度/TMP復元は[cleanup](cleanup.json)。

長い再読込と初回ヘルパー準備は同じEditor137059/既存jobで回収した。起動時Pipeline main-thread timeoutの履歴は分類保存し、本体状態の読取完了後にfresh console読取のクライアント180秒timeoutで一度停止した。追加の消去やPlay/書込の再送はせず、読取だけの再確認でerror0を確認して同じPlayから続けた。[回復記録](console-read-recovery.json) / [起動履歴](native-startup-history.json)。

最終全件の180秒待受timeoutも、同じ実行のtest_statusで回収して保持し、再送しなかった。

通常Controllerの既存Player生成へは自動接続していない。明示Factoryの利用者が選ぶ境界であり、既存神依/神憑の自動移行でもない。S12の旧Wind対応表と旧checkpoint方針は未決のまま。S09/S10/S11/D05や製品データ割当、実build/物理入力/聴感/全HotReload、製品NPC/Itemskip/stat/極端旧長名、歴史的UGUI/Pipeline/CLI/UnityAI/登録外Sprite2095も独立して保持。[実装範囲](../../../Planning/外界探索入口の実装段階.md)。
