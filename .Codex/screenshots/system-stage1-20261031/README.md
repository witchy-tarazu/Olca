# GameSystem管理基盤 第一段階

Round78は33038c35b9e888ada5e093bec506988f620f1187で別公開済み。この単位はシステム管理へ切り替えた最初の縦切りであり、全Scene移行ではありません。

- GameSystemRoot / GameSystem と SaveManager・ResourceManager・ViewManager・DialogManager・SoundManagerを追加。
- 既存DungeonSceneを互換起動ホストとして、操作設定画面を遅延生成・再使用し、初期化確認をDialogManagerへ分離。
- 保存形式・保存成功後の適用・固定時計・UI外観は保持。入力/時計/Coreは現Controller所有を維持し、Sessionは借用参照を束縛。
- SoundManagerは既存Routerの単一寿命を管理。画面往復では3音源を再生成せず、ownerのpauseを開始曲指定前に同期。

詳しい現状依存・責務・移行順は[管理基盤計画](../../../Planning/GameSystem管理基盤と段階移行.md)を参照。

## 実画面

意図したデザイン変更はありません。既存JikiTourai・水鏡面・配置を維持した正常経路の画像です。静止画の入力遮断・保存成立は下記の操作記録と区別します。

|場面|FHD|4K|4:3|
|---|---|---|---|
|操作設定|[画像](keyboard-settings-fhd.png)|[画像](keyboard-settings-4k.png)|[画像](keyboard-settings-4by3.png)|
|初期化確認|[画像](reset-dialog-fhd.png)|[画像](reset-dialog-4k.png)|[画像](reset-dialog-4by3.png)|

## 検証

- 最終関連14件: 14成功、8.40秒、console error/warning 0。
- 最終全件: 3542成功 / 3542、269.18秒、console error 0。警告は[分類](warning-classification.json)に実数を記録。
- テスト前の実640×360 / RenderTexture・専用TMPDIR・保存ガード・最終ソース一致を確認。高解像度撮影後も640×360へ復元。
- 通常Raycastクリックによる画面開閉、初期化取消/確定、変更保存とディスク一致、初期配置保存復元、親終了時Dialog閉鎖、checkpoint再開を確認。
- 仮想パッドEastの取消保持はDialogだけを閉じ、親画面へ漏れない。仮想機器は除去し元のMouse/Keyboard/EventSystemを確認。
- Play中Root1 / EventSystem1 / 画面lease0→1、往復で同じ画面と3音源を再使用。checkpoint後SessionのCore/入力参照一致。終了後Root/管理画面0。
- 独立コード・原寸6枚視覚再レビューで具体的未解決指摘0。これは当該範囲の結果であり無欠陥保証ではない。

## 失敗・限界を分ける

初期のEditMode fixture登録、具象Store注入、破棄通知不足によるテスト失敗は原記録を保持し修正。実Playで見つかった取消後フォーカス復帰も修正し実View回帰と通常操作で再確認しました。

起動直後のPipeline要求timeoutは実状態を確認して続行し、同じ起動/テストを重複送信していません。Native期間のツールtimeout2件とUnityAI接続情報保存警告1件は[別分類](console-classification.json)に保持し、最終全件の期間測定と混同しません。歴史的UGUI例外の解決は主張しません。

物理パッド/タッチ/OS・実機ビルド・実音の聴感は未検証です。FMOD無音環境ではAPI/状態のみ確認しました。

## 次段階

専用BootstrapSceneとView Prefab分離、残る画面/Dialog群、カメラ/EventSystem、攻略状態所有、残StoreとResources呼出しの段階移行が残ります。既存検証Sceneは保持します。外界・ボスラッシュの新仕様は6a9e1d7まで文書を保護しただけで未実装、落下閾値/式や再戦入口等は別途決定です。
