# 共通UI基盤とPrefabロード 第三段階

UIRuntimeManagerがResourceManagerのPrefab leaseでOverlay CanvasとEventSystem/InputSystemUIInputModuleをRoot配下に一つ所有します。ホストはCanvas自体を破棄せず、専用contentだけを借ります。終了時はDialog/View解除→選択/content解放。Rootの共通UIとSoundは再利用します。

操作設定はKeyboardSettings PrefabテンプレートをViewManager factoryからロードし、既存Initializeで内容/callbackを構築します。完全serialize済み画面Prefabへの全面移行ではありません。SceneのEventSystemだけを移し、旧直Playも同じRoot入口で動作します。カメラ/Listener・攻略Core/Input/固定時計はSceneホスト所有のままです。

[設計と移行残件](../../../Planning/GameSystem管理基盤と段階移行.md)

|場面|FHD|4K|4:3|
|---|---|---|---|
|戦闘|[画像](battle-host-fhd.png)|[画像](battle-host-4k.png)|[画像](battle-host-4by3.png)|
|操作設定|[画像](keyboard-settings-fhd.png)|[画像](keyboard-settings-4k.png)|[画像](keyboard-settings-4by3.png)|
|Dialog|[画像](reset-dialog-fhd.png)|[画像](reset-dialog-4k.png)|[画像](reset-dialog-4by3.png)|

意図したデザイン変更はありません。[第二段階の同場面](../system-stage2-20261031/README.md)に対し、元背景/Jiki/水鏡・足元・操作領域を維持する退行確認です。

最終関連4件成功。最終全件3557/3557成功、285.35秒、console error0。警告は[実分類](warning-classification.json)、実操作と資源数は[native検証](native-operations.json)、独立レビュー範囲は[レビュー](independent-review.json)を参照。撮影とテストを分け、全件前後に実640×360/TMPDIR/saveguardとソース一致を確認しています。

次はカメラ借用/返却とGameplay表示群のPrefab/View切出しです。外界/ボスラッシュの取得済み仕様は今回未実装。実ビルド/物理機器/OS/音声聴感は未検証。歴史的UGUI未解決、回復したPipeline timeoutとUnityAI警告は[別分類](console-classification.json)に保持し、製品最終測定と混同しません。
