# 詳細モーダルの余白と長文リサイズ（2026-10-28）

Unity6000.3.26f1、既存batchmode/OpenGLのGame View原本PNG。専用saveで通常撤退→帰還→カード詳細から取得。背景/JikiTourai/本文と操作仕様を維持。

|比率|修正前|修正後|
|---|---|---|
|4by3|[前](details-before-4by3.png)|[後](details-after-4by3.png)|
|wide|[前](details-before-wide.png)|[後](details-after-wide.png)|
|fhd|[前](details-before-fhd.png)|[後](details-after-fhd.png)|
|4k|[前](details-before-4k.png)|[後](details-after-4k.png)|

FHD1920×1080、4K3840×2160、4:3は1440×1080、21:9は2520×1080。元画像は伸縮加工していない。横長の詳細は上端余白が不足し戻るの下端が欠けていた。見出しと戻るを端からの余白へ追従させ、基準16:9位置は保持する。本文スクロールの割合を保ち、幅の変更後は折返しに必要な高さを再計算する。

[長文を開いたまま幅を縮め、末尾へ到達](details-long-bottom-4by3.png)。これは検証専用の長文カードを詳細Viewへ渡した表示確認で、製品カードや保存へ登録していない。JikiTouraiの実Playでは本文高1964→2044.19、preferredHeight2024.19へ追従、スクロール割合と末尾「サイゴのギョウ」の表示を確認した。

帰還/編成からの開閉・リサイズ回帰、実レイキャスト、閉じた後のカードへのフォーカス復帰2回、保存JSONハッシュ不変を確認。Editorの実Button/Controller呼出しであり物理入力の確認ではない。全製品長文・極端な比率・通常GUI・実機/聴感は未確認。
