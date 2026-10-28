# UIブラッシュアップ比較（2026-10-28）

|対象|修正前FHD|修正後FHD|修正後4K|
|---|---|---|---|
|カスタム補助ボタンの白文字上書き|[前](custom-buttons-before-fhd.png)|[後](custom-buttons-after-fhd.png)|[4K](custom-buttons-after-4k.png)|
|キー設定中に残った図鑑入口|[前](keyboard-before-fhd.png)|[後](keyboard-after-fhd.png)|[4K](keyboard-after-4k.png)|
|図鑑「未入手」の漢字残り|[前](collection-before-fhd.png)|[後](collection-after-fhd.png)|[4K](collection-after-4k.png)|

[未公開レシピの修正後FHD](recipes-after-fhd.png)

カスタム補助ボタンはbbb1bdaの修正前と5f83188の修正後。キー設定/図鑑は5f83188の修正前と、この画像を含むcommitの修正後コード。詳細は[補助ボタン記録](../../../Planning/カスタム補助ボタンの可読性-20261028.md)・[今回監査](../../../Planning/既存UI監査と図鑑表示修正-20261028.md)。

Unity 6000.3.26f1の実GameView、既存batchmode/OpenGL条件。FHDは1920×1080、4Kは3840×2160。専用保存ガード下の検証用戦闘/カード/装備定義を使用し、個人保存や認証画面を含まない。PNG原本の切貼り・リサイズ・色加工なし。SHA256/寸法は[manifest](manifest.json)。通常GUI/実機/音の聴感の成功証跡ではない。

カスタム補助ボタンの修正後はLIFO操作後のためフォーカス位置が異なる。図鑑の修正前は戦闘中ポーズから、修正後は撤退後拠点から開いており、比較対象は未取得行の表記。性能値や取得状態を画像のために変更していない。

正式Library保存は接続先ツール一覧取得時の通信エラーで画像保存へ進めず、library_file_idは発行されていない。同じ操作を繰返さず、今回依頼された既存の同一リポジトリPNG共有手順で保存した。共有権限の変更や別サービスへの送信は行っていない。
