# フォント復元後の確認画像

- [FHD（1920×1080）](font-restored-fhd.png)
- [4K（3840×2160）](font-restored-4k.png)

撮影対象コード: `a7a616c61851b089c769fe271cdcade1e231b4fc` と同内容のフォント復元。Unity 6000.3.26f1、専用保存先ガード成立済みのGUI検証コピー、セル49の初戦カスタム画面。HP100／敵HP50／手札10。ScreenCaptureで取得し、リサイズ・画像編集なし。

JikiTouraiの指定参照を復元し、OSフォントの強制差替えを撤去。BgCustom背景、現行シアンのUI部品、配置・文字サイズ・ウェイト・autosize・マテリアルは保持。ゲーム画面のみで認証画面・個人情報は含まない。物理スマホ／物理パッド／ビルドの確認画像ではない。

Library保存はツール一覧取得段階の通信エラーで未実行。以前のFHD Base64 blobは存在するが、今回の2枚は通常のPNGとしてこのブランチに保存する。結果不明だった4K Base64 blobは正規照会で404を確認し、同じ送信を再実行していない。PNGのSHA256は `Tools/Migration/manifest.json` を参照。
