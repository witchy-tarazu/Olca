# 独立コードレビュー

既存equipment_save_reviewが読み取りでRenderOwnedDeparture差分と既存Core/Controller契約を確認。最終差分の具体的な未解決指摘0。

- profile/index/options対応とdraftKeys/OwnedDefinition.Keyによる同名別hashの数量対応を保持。
- 専用miniは解決済みoptionから借用し、非Raycast・preserveAspect。別カード代用や所有権変更なし。
- 長名の全文入口には名称・威力・採用／所持・未解決状態を含むfullRowを保持。
- 行群全体で15下げ、詳細／±／数量／miniの対応を保つ。見出しと先頭行を分離。
- pending/Live/旧callback/取消/ページfocus、通常編成と保存下書きの追加制限差を保持。
- 16文字／実幅／旧長名・IME／取消・dirtyと保存条件は変更なし。
- 所持Countの有効保存契約はDeck.csで1〜4。int型だけから任意の長数量を製品値として追加しない。保存下書き採用数は別契約なので局所bestFitを保持。

初回コンパイルのCustomDisplay名前空間漏れは明示指定で修正。最終明示recompileはerrors[]/compilationFailed=false。Core/保存/serialized field/asset参照の変更なし。
