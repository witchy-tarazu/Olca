# 帰還・編成本体の画面内配置（2026-10-28）

Unity6000.3.26f1、既存batchmode/OpenGLの実Game View原本PNG。専用保存の通常撤退帰還→保存デッキ→編成から取得。背景/JikiTouraiと既存の配置方向を維持する。

|画面/比率|修正前|修正後|
|---|---|---|
|return / 4by3|[前](return-before-4by3.png)|[後](return-after-4by3.png)|
|return / wide|[前](return-before-wide.png)|[後](return-after-wide.png)|
|return / fhd|[前](return-before-fhd.png)|[後](return-after-fhd.png)|
|return / 4k|[前](return-before-4k.png)|[後](return-after-4k.png)|
|editor / 4by3|[前](editor-before-4by3.png)|[後](editor-after-4by3.png)|
|editor / wide|[前](editor-before-wide.png)|[後](editor-after-wide.png)|
|editor / fhd|[前](editor-before-fhd.png)|[後](editor-after-fhd.png)|
|editor / 4k|[前](editor-before-4k.png)|[後](editor-after-4k.png)|

FHD1920×1080、4K3840×2160、4:3は1440×1080、21:9は2520×1080。画像は伸縮加工していない。

帰還・編成の内容だけを既存1920×1080基準の配置で保持し、表示幅か高さが不足するときだけ等比で収める。16:9では倍率1、全体CanvasScalerは変更しない。編成のタイトル/名前欄/規則/妥当性の隙間も確保。保存/ゲーム性能/元の文字列は変更していない。

前画像は直前の周で保存した同じ帰還/編成Viewの実画像。returnは前周補助ボタン修正後、editorはその前の取得だが編成Viewは同一で、今回対象外の拠点補助ボタンは編集中非表示。原本名はmanifestへ記録する。

各解像度でタイトルと全操作の画面内判定、実レイキャストでの入力位置を確認。編集継続/破棄/再読込を各2回、名前復帰と保存JSONハッシュ不変を確認。Button/ControllerのEditor呼出しであり物理機器入力ではない。

通常GUI、縦長/極端な比率、全製品長文の組合せ、物理機器と聴感は未確認。今回の2画面修正を、他の選択/交換/詳細画面まで解消済みとはしない。
