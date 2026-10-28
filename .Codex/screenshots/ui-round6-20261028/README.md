# 出撃先・スタイル・交換の表示と遷移（2026-10-28）

Unity6000.3.26f1、既存batchmode/OpenGLのGame View原本PNG。FHD1920×1080、4K3840×2160、4:3は1440×1080、21:9は2520×1080。画像は伸縮加工していない。背景・JikiTourai・基準配置の方向性を保持する。

|画面/比率|修正前|最終ソースの修正後|
|---|---|---|
|dungeon / 4by3|[前](dungeon-before-4by3.png)|[後](dungeon-after-4by3.png)|
|dungeon / wide|[前](dungeon-before-wide.png)|[後](dungeon-after-wide.png)|
|dungeon / fhd|[前](dungeon-before-fhd.png)|[後](dungeon-after-fhd.png)|
|dungeon / 4k|[前](dungeon-before-4k.png)|[後](dungeon-after-4k.png)|
|style / 4by3|[前](style-before-4by3.png)|[後](style-after-4by3.png)|
|style / wide|[前](style-before-wide.png)|[後](style-after-wide.png)|
|style / fhd|[前](style-before-fhd.png)|[後](style-after-fhd.png)|
|style / 4k|[前](style-before-4k.png)|[後](style-after-4k.png)|
|exchange / 4by3|[前](exchange-before-4by3.png)|[後](exchange-after-4by3.png)|
|exchange / wide|[前](exchange-before-wide.png)|[後](exchange-after-wide.png)|
|exchange / fhd|[前](exchange-before-fhd.png)|[後](exchange-after-fhd.png)|
|exchange / 4k|[前](exchange-before-4k.png)|[後](exchange-after-4k.png)|

交換の再試行は基準画面でも下端を越えていたため、他の下部操作と離したまま画面内へ配置した。待機表示の確認：[4by3](exchange-blocked-after-4by3.png) / [wide](exchange-blocked-after-wide.png) / [fhd](exchange-blocked-after-fhd.png) / [4k](exchange-blocked-after-4k.png)

待機表示はViewへ検証用blocked/errorを渡した表示試験。実保存を失敗させる操作や保存復旧を行った証拠ではない。初回の表示直後の補助レイキャストはレイアウト安定前に一致せず、描画フレームを待った4解像度の画面内判定とレイキャストで再確認した。

出撃先7件は既存登録マップ/デッキへの参照を複製したページ検証リスト。スタイル候補も選択/未解放試験用の外部定義で、製品へ追加していない。交換は既存の製品候補を使用。キャンセル操作で通貨やカードを消費していない。

初回拠点と、通常出撃→撤退→帰還後の両経路で、ページ/選択取消/入口フォーカス復帰を各2回確認。帰還後に有効表示のスタイル/交換が開かなかった不具合も修正し、未確定の出撃先を保って元画面へ戻る。交換候補の同一ラウンド維持と保存JSONバイト不変を確認。Editorの実UIレイキャスト/PointerDown・Up・Clickイベントであり、物理機器操作とは区別する。

全製品長文・縦長/極端な比率・通常GUI黒画面・実機入力・聴感は未確認。今回の3画面をゲーム全体の無欠陥保証とはしない。
