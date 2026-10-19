# キー割当モデルと実入力の接続前確認（2026-10-19）

調査基準は `74d7d4e`。本書は確定仕様の追加ではない。実入力接続は以下の2点の回答待ちで、今回製品コードは変更していない。

## 現行経路と制約

- UnityInputProvider.StartEventCapture → CaptureEvent → TimestampedLogicalInput → UnityLogicalLoop.PumpTimestamped → Controller.TickLogicalFrame。イベント生成時刻を基に60Hzへ割り当て、描画の追いつきでも一度だけ消費する。過去/未来イベント、入力保持の扱いはこの経路で管理する。
- BuildState/CaptureEventの現行キーボード割当は、左右=矢印+A/D、上下=矢印+W/S、攻撃=Z、探索ジャンプ/戦闘カード使用=Space、ダッシュ=左右Shift、カスタム画面=左右Alt。同一操作に複数の物理キーがある。
- InputBindingProfileはデバイス・場面・操作に対して1つの文字列割当を持つ。複数キーをそのまま1操作へ渡す解釈はまだ定義していない。
- 探索ジャンプと戦闘カード使用のように別場面で同じキーを共有する。入力は取得時にLogicalInputへ変換済みなので、遅延中の場面遷移を扱う際に現在の描画時点の場面だけでイベントを解釈し直さないこと。
- DungeonBattleController.SetUserPaused はReleaseInput、GameplayPause.SetPaused、QueueSnapshotを実行する。再開は全キー解放待ちと既存2論理フレームのバリアを通る。通常ポーズで論理時計そのものをリセットしてはならない。
- メニュー決定/キャンセルはUnityInputProviderのゲーム操作とは別で、DungeonScene/BattlePrototypeSceneのUI InputAction参照を使う（m_SubmitAction/m_CancelAction）。Coreモデルに必須操作を登録しただけではこの実入力は変更されない。
- 旧IInputProviderプロパティの一部（X/C）と、現在使われる時刻付き経路（Space/Alt）に差がある。最小接続は実際にControllerが使う時刻付き経路を対象にし、旧値を製品既定へ採用し直さない。

## 必要な判断と選択肢

1. **同一操作の複数キー**
   - 推奨: 現行の各物理キーを個別スロットとして編集し、変更していない割当は維持する。WASD/矢印・左右Shift/Altを残したまま変更できる。
   - 代替: 1操作の変更で、その操作の既存全キーを新しい1キーへ置き換える。例えば左Shiftの変更で右Shiftも効かなくなる。
   - Planning §18はどちらかを指定していない。既存操作の消失を伴うため推測しない。
2. **最初の接続範囲と切替境界**
   - 推奨: キーボードのゲーム操作のみ。適用はユーザーポーズ中に行い、再開時は既存の全キー解放待ちを使う。メニューの必須操作とコントローラ割当は現状のまま。この範囲なら押しっぱなしの再解釈や旧イベントの誤発火を既存境界で防げる。
   - 代替: コントローラやメニューも同時に接続する。その場合はゲーム操作だけでなくUI InputAction、アナログ軸、複合キー/ボタン割当まで対応範囲を広げる必要がある。
   - 任意時点の即時適用も要求する場合は、押下中キーとタメ保持、適用前に生成済みの未処理イベントをどう扱うか追加で定める必要がある。

## 回答後の最小手順

- 選ばれたスロット方式で現行割当を過不足なく表し、型付きのKey解決を検証する。未知スロット/キー・重複・必須操作の無効化は適用前に拒否する。
- 編集中/Discardではproviderを変更しない。適用した不変設定だけをproviderへ渡す。受理されていないメニュー/デバイスの変更を黙って無視して成功扱いしない。
- 適用境界で入力キュー・snapshot・既存pause barrierを整合させる。時間負債を消すStartNewTimelineを設定変更には使わない。
- 旧キー無効/新キー有効、交換、破棄、押下中適用、ポーズ再開、探索/戦闘/カスタム画面境界、必須保護をInputSystemイベントと通常Controllerで検証する。全EditModeを再実行する。
- 設定UI・保存形式・新しい既定キーは追加しない。

## 今回の回帰確認と区別

全EditMode939件を再実行して成功。既存InputSystemキーボードイベント/時刻付き入力テストも含む。通常Controllerの実LoadCheckpointButtonでdeck7/obtained1の別モデルへの復帰を確認し、個体ID・順序・獲得履歴と保存バイト列は不変。証跡は `/workspace/.olca-cloud/results/input-binding-connection-audit/`。

6枚は既存の加工・戦闘確認fixtureであり、正式な出撃編成ではない。実運用APIのTryConfigureDepartureDeckは所持カードと20枚等の共通制限を検証する。この区別を維持し、編成UIを追加しない。

現在の阻害は仕様判断待ちであり、ツール操作の拒否や結果不明ではない。過去のmaster公開拒否とは別件。pushの再試行・迂回は行わない。
