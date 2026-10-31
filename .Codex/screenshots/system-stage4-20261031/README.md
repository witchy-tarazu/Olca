# Camera借用とGameplay HUD View 第四段階

管理起動はRootのCameraRuntimeManagerからCamera/AudioListener/既存URP設定Prefabを排他借用します。GameField/背景へ明示注入し、終了は旧参照解除→Rental返却→Prefab基準の位置/投影/行列/aspect/RT/Listener復元と非active化。追従/固定フレームの計算は既存GameFieldに維持。旧Scene直PlayはScene Cameraを使い、管理起動中は互換Camera/Listenerを無効化します。

探索HUDを内部Text/CanvasGroup参照を持つDungeonHud Prefabへ切り出しました。ResourceManager生成物leaseをViewManagerのgameplay層で所有し、設定Viewと非排他的に表示します。親Disposeは全層/current/pending/後着生成を破棄します。HUDのorder5・Font・戦闘フェード・報酬中枚数欄は既存契約です。

[設計と残るScene依存](../../../Planning/GameSystem管理基盤と段階移行.md)

|場面|FHD|4K|4:3|
|---|---|---|---|
|戦闘|[画像](battle-host-fhd.png)|[画像](battle-host-4k.png)|[画像](battle-host-4by3.png)|
|操作設定|[画像](keyboard-settings-fhd.png)|[画像](keyboard-settings-4k.png)|[画像](keyboard-settings-4by3.png)|
|Dialog|[画像](reset-dialog-fhd.png)|[画像](reset-dialog-4k.png)|[画像](reset-dialog-4by3.png)|

意図したデザイン変更はありません。[前段同場面](../system-stage3-20261031/README.md)と原寸比較。全体の美術完成を意味しません。

関連7/7成功7.86秒。最終全件3564/3564成功284.12秒、console error0。警告は[実分類](warning-classification.json)。[実経路](native-operations.json)にはCamera/Listener/RootUI数・返却参照・基準復元・同一Camera再利用・保存byte不変・Prefab View再生成を記録。[独立レビュー](independent-review.json)は今回範囲の具体的未解決0です。

[公開CLI](../../tools/README.md)はcheck/commit/pushを分離し、失敗時に停止します。[隔離ローカルGit負テスト16件](publication-tests.json)でreceipt/tree/証跡/未検証Asset/空白/remote進展拒否を確認。Unity空Name/ClassIdentifier scalarのspaceは厳密に許容理由と行をreceiptへ記録し、全検査を無効化しません。HUD Textの空scalar/折返し末尾spaceは[実値一致を確認して正規化](hud-normalization.json)。検証済み最終バイトを維持して公開します。

初回全件は3562/3564成功・2失敗。Scene直配置HUDを検索した既存2fixtureを本番Resources Prefab生成へ移行し、HP・進行度の期待値を維持して再実行しました。[初回失敗の記録](initial-full-failures.json)。原寸レビュー後の変更はこのfixtureのみで、[製品14ファイルの同一性](visual-final-binding.json)を確認しています。

Core/Input/時計・Battle/Custom/役者/床/背景のScene依存、操作設定内部Initializeは残ります。外界/ボスラッシュの取得文書を実装完了とは扱いません。実build/物理機器/OS/聴感は未検証。破壊的なengine unload失敗注入はせず隔離lease回帰でretryを検証。歴史的UGUI未解決とPipeline/UnityAI履歴は[別記録](console-classification.json)です。
