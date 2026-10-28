# 保存デッキ画面の再監査（2026-10-28）

Unity 6000.3.26f1、既存batchmode/OpenGLのGame View原本PNG。専用保存で通常の撤退→帰還→保存デッキから表示。製品データや保存契約を変更していない。

|比率|修正前|修正後|
|---|---|---|
|FHD 1920×1080|[前](saved-deck-before-fhd.png)|[後](saved-deck-after-fhd.png)|
|4K 3840×2160|[前](saved-deck-before-4k.png)|[後](saved-deck-after-4k.png)|
|4:3 1440×1080|[前](saved-deck-before-4by3.png)|[後](saved-deck-after-4by3.png)|
|21:9 2520×1080|[前](saved-deck-before-wide.png)|[後](saved-deck-after-wide.png)|

横長時にタイトル・図鑑入口・再読込が欠けていた。基準16:9の通常表示位置を保持し、見出しは上端、ページと保存操作は下端へ追従する。エラーは通常説明と同じ枠へ排他的に表示し、重なりを防ぐ。背景・JikiTourai・ボタン素材と操作仕様を維持。

保存デッキの開閉、名前変更から編集継続/破棄、再読込を各2回確認し、保存JSONハッシュは不変。検証はButton/InputFieldのEditor呼出しであり、物理入力確認ではない。通常GUI黒画面・縦長/極端な比率・全長文の組合せは未確認。ハッシュはmanifest.json。
