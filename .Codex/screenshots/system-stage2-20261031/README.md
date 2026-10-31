# Bootstrapと互換ホスト寿命 第二段階

起動用BootstrapSceneを導入し、Rootから従来DungeonSceneを明示ロード/起動する段階です。Sceneから画面Prefabへの全面移行完了ではありません。

- Bootstrap path限定のruntime入口、GameHostManager、ResourceManagerのScene leaseを追加。Rootは一つだけ常駐し、必要なhostをStart前に非activeで捕捉→サービス/保存先注入→Activate→明示起動。
- カメラ/Listener/EventSystem/Core/Input/論理時計は互換host所有、Sound3音源はRoot所有。旧DungeonScene直Playは維持し、二重host起動を拒否。
- Sceneロードは取消不能なので結果leaseを必ず回収し、未起動のまま解放。解放失敗は所有を保持してClose再試行。外部Single終了済みleaseは追跡解除後に再起動。
- EditorBuildSettingsの標準入口をBoot先頭＋Dungeon有効へ再現。既存他Scene順序/flagを保ち、GUIDを固定せず、playModeStartSceneを変更しない。実ビルドは未実施。

[設計・移行計画](../../../Planning/GameSystem管理基盤と段階移行.md)

## 実画面

意図したデザイン変更はありません。背景/Jiki/水鏡/キャラ比率/床前景と操作領域の退行確認です。

|場面|FHD|4K|4:3|
|---|---|---|---|
|戦闘|[画像](battle-host-fhd.png)|[画像](battle-host-4k.png)|[画像](battle-host-4by3.png)|
|操作設定|[画像](keyboard-settings-fhd.png)|[画像](keyboard-settings-4k.png)|[画像](keyboard-settings-4by3.png)|
|確認Dialog|[画像](reset-dialog-fhd.png)|[画像](reset-dialog-4k.png)|[画像](reset-dialog-4by3.png)|

## 検証

- 最終関連11件は11成功、6.63秒、console error0。前版全件3552成功は最終版とは区別して保存。
- 最終全件は3553/3553成功、274.41秒、console error0。警告の実数は[分類](warning-classification.json)。
- 実640×360/RenderTexture、専用TMPDIR/saveguard、ソース一致を確認して実行。撮影後も低解像度に復元。
- Bootstrap自動起動、旧直接起動、同時要求/取消/欠損Scene/解放/再生成、旧入力と購読の解除、設定・Dialog・checkpointとSingle再入を確認。[実操作](native-operations.json)
- 原寸9枚とコードの独立再レビューで具体的未解決指摘0。レビュー範囲の結果であり全体無欠陥保証ではない。

## 残る範囲

Scene loaderは暫定的な互換host境界です。次にカメラ/EventSystem/UIRootと画面群をPrefab/Viewへ分離し、Core/Input/時計の所有も表示から分けます。検証Sceneは保持します。物理機器/OS/実機ビルド/聴感は未検証。FMOD無音で音声はAPI/状態確認のみ。歴史的UGUI例外、Pipeline timeout、UnityAI警告は[別分類](console-classification.json)で区別します。

Round78は33038c35b9e888ada5e093bec506988f620f1187、第一段階は7c609b3a332c645d6819dc2586eefd3e90b1a7d8で別公開済みです。外界・ボスラッシュの仕様取得を機能実装済みとは扱いません。
