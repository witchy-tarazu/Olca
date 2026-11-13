# Input System 専用化（Stage248）

ユーザーの Input Manager 廃止予定警告への対応。既存の Input System 1.19.0 を使い、Active Input Handling を Both から Input System のみにする。パッケージの更新や保存キー割当の移行は行わない。

## 範囲

- 製品の60 Hz時刻付き入力は既存の処理を保全。残っていたカード選択・加工・入力設定取消と旧 IInputProvider 互換読取を Input System のキー・軸読取へ接続する。
- Enter とテンキー Enter を区別して両方を扱う。既存互換軸のキーボード±1、スティックdead zone .19、最大絶対値の採用を保つ。
- 中立待ち用の物理ボタン読取ではスティックの合成方向ボタンを除外する。
- UIアセンブリに Unity.InputSystem の明示参照を追加する。
- ProjectSettings は既存どおり復元ZIPで管理する。両ZIPの activeInputHandler だけを2から1へ変更し、他26ファイルを保持する。archive・全entryのSHAをmanifestに記録する。
- 既存コピーではPlayしていないEditor初期化時に一度更新。最初の元設定を Library/OlcaInputSystemMigration/ProjectSettings-before.asset に保全できた場合だけ変更する。既存backupは上書きしない。Editorの再起動を案内し、自動終了しない。再起動までは専用化完了と扱わない。

## 確認範囲

- 再起動した6000.3.26f1 Editorで ENABLE_INPUT_SYSTEM 有効・ENABLE_LEGACY_INPUT_MANAGER 無効をテストで確認。
- 追加3件、既存設定・入力109件、カード選択・加工UI453件が成功。最終全4,865件も成功。ソース278ファイル（C#と変更asmdef）は検証コピーと一致。
- EditModeのInput SystemはEditor専用更新となり、ゲーム中のwasPressedThisFrameとは異なる。保持・別Enter・複数キーボード・軸・仮想pad/mouseをEditModeで検証し、Downは新しいPlayerの通常操作で別確認した。
- 新Linux Player265をビルド成功（エラー0）。隔離保存コピーでタイトル継続、探索左右移動・扉操作、デッキ名文字入力→保存、通常出撃、戦闘の方向選択→Space選択→Escape取消→再選択→Enter確定、終了・再起動からチェックポイント再開を確認。既存の試用報酬品質装飾品は未対応の既定ダンジョンを安全停止するため、隔離コピーの通常装備画面で外して検証した。
- 実入力検証はXTestによるOSキーボード・マウス合成。物理パッド、物理タッチ、日本語IMEの変換操作、Playerの全リバインド項目は未確認。仮想パッド・キー割当保存の自動回帰と区別する。
- Player開発接続用のRuntimePipelineConfig未登録警告は残る。旧Input Manager警告とは別で、Player内の開発接続を無効にする既存設定。

落下安全高度の追加調整、通常攻撃本体素材、神依の見た目、外界経験値の接続は別の作業であり、この移行の完成範囲に含めない。
