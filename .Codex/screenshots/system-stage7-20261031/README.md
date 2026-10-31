# Bootstrap単独起動とPrefab host・報酬View 第七段階

本番起動はBootstrapScene → GameSystemRoot → GameHostManager → ResourceManagerの非active DungeonHost Prefabです。DungeonSceneをロードしません。hostはController/ContactAnimatorの1 GameObjectのみ。Camera/Listener/UIRoot/EventSystem/Sound、World/BattleCustom/HUD/操作設定と報酬画面は別所有です。DI→Activate→既存Startを明示順で行います。

CardSelectionCanvasは26 GameObjectのCardReward PrefabとしてViewManagerのcard-reward層へ移行。起動時に個別取得して欠損を攻略/保存開始前に検出し、containerは従来の報酬phaseだけ表示します。phaseごとのlazy生成完了とは扱いません。Popupは引き続きDialogManagerです。

[設計と残依存](../../../Planning/GameSystem管理基盤と段階移行.md)。旧DungeonScene直起動とStartCompatibilityHostAsync/Scene lease APIは検証互換として保持。BuildSettingsは2Scene有効のままですが、本番起動で必要なのはBootstrapのみ。Core/Input/固定時計/攻略状態はhost Controllerに残り、View単体の破棄から分離しています。UI内部Initialize、静的素材Catalog、phase別lazy取得等も残ります。全体移行・システム安定化完了の宣言ではありません。

## 比較

|場面|FHD|4K|4:3|
|---|---|---|---|
|旧Scene直起動 Custom|[前](before-custom-fhd.png) / [後](after-custom-fhd.png)|[前](before-custom-4k.png) / [後](after-custom-4k.png)|[前](before-custom-4by3.png) / [後](after-custom-4by3.png)|
|旧Scene直起動 戦闘|[前](before-battle-host-fhd.png) / [後](after-battle-host-fhd.png)|[前](before-battle-host-4k.png) / [後](after-battle-host-4k.png)|[前](before-battle-host-4by3.png) / [後](after-battle-host-4by3.png)|
|Prefab管理起動|[FHD](after-managed-battle-host-fhd.png)|[4K](after-managed-battle-host-4k.png)|[4:3](after-managed-battle-host-4by3.png)|
|操作設定|[FHD](after-keyboard-settings-fhd.png)|[4K](after-keyboard-settings-4k.png)|[4:3](after-keyboard-settings-4by3.png)|
|確認Dialog|[FHD](after-reset-dialog-fhd.png)|[4K](after-reset-dialog-4k.png)|[4:3](after-reset-dialog-4by3.png)|
|独立報酬View|[FHD](after-card-reward-fhd.png)|[4K](after-card-reward-4k.png)|[4:3](after-card-reward-4by3.png)|

前画像はStage6後画像の引用で[出所](before-provenance.json)にhashを記録。新before撮影やidle差を含むpixel同一とは主張しません。報酬以外の後15枚は破棄待機一行修正前です。active表示の残り11ファイルは同一で、破棄の修正後実経路と最終テストを別確認しています。[独立原寸レビュー](independent-review.json)を参照。

実ボス：[FHD](after-actual-boss-fhd.png) / [4K](after-actual-boss-4k.png) / [4:3](after-actual-boss-4by3.png)。通常の報酬・探索経路から接触し、IsBossGauge/GaugeVisibleを確認しました。

## 実装・検証

[通常Unity書出し](asset-export.json)・[再import](asset-reimport.json)と[空scalar表現正規化](prefab-normalization.json)をテスト前に確認。旧Sceneは変更せず、host設定48項目は[既定値を含むnative比較](host-native-config.json)で一致。metaを手作業で作成していません。

関連467/467（38.92秒）、最終全3575/3575（269.82秒）、skip0/error0。実640×360・専用TMPDIR/save guardで最終source12のhash一致を確認。[同Editor EventSystem反復](event-regression.json)・[全件直後](post-full-event-registry.json)はstatic一覧を全clearせずnull/登録0を確認。警告は[実分類](warning-classification.json)、console履歴は[別記録](console-classification.json)。期待値や保存ガードを緩和していません。

[実経路](native-lifecycle.json)：直起動、BootstrapのみのPrefab生成、設定/Dialog/取消/確定/保存復帰、Single再入、ホスト終了・実Prefab作成後取消・欠損・同時再生成。Root/Camera/Listener/EventSystem各1、稼働中resources8/Scene lease0、終了後host/World/pool/Battle/Custom/reward0、Rootのcache3。旧Core購読/入力/Camera参照と生成Spriteを返却。[Single保存再比較](single-save-proof.json)は実保存先の非空checkpoint hashで、Dialogを開いたまま再入→checkpoint再開の不変を確認しています。初回helperの別保存先比較は証拠に採用せず、この再比較で置き換えました。

最初のCloseは次Updateでnative Destroy完了を早く判定し失敗しました。[初回](initial-close-failure.json)・[後観察](initial-close-later-observation.json)・[retry](initial-close-retry.json)を保持。LastPostLateUpdate待ちへ最小修正し、strict root-null検査を維持。修正後はtimeScale0で初回Close、取消・再生成を実確認しています。ゲーム論理時間/性能を変更していません。

[報酬実経路](reward-native.json)：既存damageAPIでfixture勝利→通常報酬UI、開いたままhost終了でphase/deck/保存不変・View/callback返却、checkpoint再開→実選択/報酬確定→通常訪問→次接触。同じViewの再利用と旧購読解除を確認。物理攻撃による勝利ではありません。最初のカード表示待機は、保存済み計画がカードなし/アイテムありなのに共通CardReward phaseからカード画面を推定した検証側の前提違いで期限切れでした。抽選や保存計画を変更せず通常報酬を経て次戦のカード報酬で確認しました。

停止後は本番Bootstrapが残るため、旧検証helperのDungeonScene前提も一度拒否されました。テスト開始前に明示的に互換Sceneを開いてController非activeを確認し、既存guard/assertを維持しました。

同じVM/Editor PID137059を保持し、Domain Reload後のPipeline timeoutは通常待機・読み取りで復旧しました。不明な解像度要求/Play/テストを重複再送せず、テスト開始前に実RT640を再確認。UnityAI接続保存警告・FMOD無音と製品consoleは分けています。歴史的UGUI、物理機器/OS、実build/聴感は未確認/未解決。

並行文書6f84d1c/07fd835/e87c72d/10dc6c1を保護。消耗品上限/無効果時使用不可、探索カードとの相違、依頼一覧/複数受注/達成判定・報告・納品と原子的保存、一度きり/反復設定・途中破棄なし・時間/物語期限なし・時間停止しない達成通知は文書取得であり、今回実装していません。D05/S08/外界新機能/ボスラッシュも混在させていません。

検証・cleanup・独立review・最終hashを先に確定し、check→commit→通常pushを別操作、失敗時停止とrefetch exact SHAを維持します。

最新b30c5fe/5f8f342の本文も保護しました。ストーリー目的の共通一覧、未公開地形を見せない目的地マーカー、選択1件の方向/別マップ出口ナビ・非表示切替、分岐あり/複数エンド未定を記録。文書取得だけで、新しい探索・依頼機能の実装ではありません。
