# GameSystem管理基盤と段階移行

ユーザー指定に基づく段階移行。第一段階は下記範囲で実装・検証済みであり、起動Sceneのみへの全面移行完了ではない。

## 現状と第一段階

`DungeonScene`の`DungeonBattleController`が起動・攻略・各UI・入力・音声を束縛している。`BattleSessionController`には既存のAttach/Detach、`MenuPadScope`にはモーダルと入力解放、`GameplayAudioRouter`にはBGM/SE/音量の既存処理がある。これらを再実装せず所有者を段階的に明確化する。Scene切替基盤やAddressables導入を前提にしない。検証用Sceneは保持する。

第一段階の通常起動は既存DungeonSceneを互換ホストとし、Controllerから永続`GameSystemRoot`を一つ取得する。代表画面は既存の操作設定、代表Dialogはその初期化確認。新しい起動専用Scene・全画面Prefab化は後続。

|責務|所有者と境界|
|---|---|
|初期化順と共通寿命|ApplicationのGameSystemRoot / GameSystem。ゲーム計算や全画面の詳細処理は持たない|
|保存読込|SaveManagerがガード確認済みの保存先Contextを提供し既存Storeへ委譲。形式・原子保存・保存成功後適用を保持|
|素材と生成物|ResourceManager。共有Resources参照をキャッシュし、生成画面をleaseで解放。共有import素材は破棄しない|
|画面単位|ViewManager。遅延生成、再使用、遷移世代、取消、親終了時の解放|
|ポップアップ|DialogManager。既存UIを借り、結果・取消・多重要求・入力遮断・フォーカス・親終了を管理。画面本体は所有しない|
|音声|SoundManagerが既存GameplayAudioRouterを一つ所有。音量・同一BGM再利用・pause/resumeを維持。画面遷移だけで作り直さない|
|攻略状態の接続|GameSystem.SessionがCore managerと入力を借用。checkpoint復帰後はBind更新。表示破棄と状態寿命を分離する接続境界|
|入力と時計|第一段階ではController所有の既存InputProvider / UnityLogicalLoop / GameplayPauseを保持。重複するInputManagerやTimeManagerを新設しない|
|設定|既存設定データとStoreを保持。設定全体を集める新Managerはまだ不要|

## 切替例と保証する範囲

pauseから操作設定を開くと、ViewManagerがResourceManager経由で必要時だけ既存画面を生成する。初期化確認はDialogManagerへ要求し、確認時もまず編集用draftだけ変更する。保存操作が既存Storeで成功してから入力へ適用する。取消は適用済み状態を保持する。親画面終了はDialogをParentClosedで閉じ、遅れた古いcallbackを無効化する。

同じ画面のロード中要求は共有し、別画面への要求は古い世代を取消する。取消後に届いた生成物は破棄し、ロード失敗では従前画面を維持する。Dialogの多重要求・閉じたframeの親入力遮断・フォーカス復帰は画面遷移とは別に管理する。画面の保存処理が移行前と同じ確定順になることを優先する。

## 次段階と残る依存

1. 第一段階の通常起動、操作設定、Dialog、保存再読込、音声/入力の単一所有、失敗/取消/再試行を安定させる。
2. 既存Scene内参照を棚卸しし、起動専用Sceneから代表画面Prefabをロードする縦切りへ移す。Unityのmeta/参照生成は既存リポジトリ規則に従う。既存Scene直起動と検証Sceneを残す。
3. 他の拠点画面・popupを一群ずつ移行。現在はゲームパッド/音量画面、攻略Canvas、カメラ、EventSystem、battle/field view、checkpoint/floor Store等が旧Controller/Scene依存として残る。
4. 攻略セッションの所有を表示から分離し、後に外界/ダンジョンの退避復元を接続する。外界機能の実装済みを意味しない。

Asset解放は現段階ではRootキャッシュ寿命と生成物leaseで管理し、全既存Resources呼出しの統合や参照計数は未移行。BGMフェードは現行ルーターにないため新しい時間値を追加しない。ビルド起動Scene設定は追跡対象外のProjectSettingsから製品構成を断定しない。

## 第一段階の検証記録

- Round78表示修正は33038c35b9e888ada5e093bec506988f620f1187で別公開・remote照合済み。
- 最終関連14件: 14成功、8.40秒、console error/warning 0。起動後半の失敗からのrollback/再試行、Root再取得、遅延ロードの取消/再要求/破棄、Dialog世代/親終了/同frame入力遮断、実設定Viewのフォーカス復帰を確認。
- 最終全件: 3542成功、269.18秒、console error 0。警告59件は既存fixtureの意図した診断5分類で、抑制や期待値緩和は行っていない。
- 通常Play: Root1・EventSystem1・音源3を再使用。操作設定は必要時だけ生成し、閉じ直しても同じ画面。取消後Reset起点へ復帰し、仮想パッドEastの取消保持も親画面へ漏れない。
- 設定の変更保存/ディスク一致/初期配置保存復元、親終了によるDialog閉鎖、checkpoint再開後のSession/Core/入力参照を確認。Play終了後Root/管理画面0、元の入力機器へ復元。
- FHD/4K/4:3の操作設定・初期化Dialog計6枚を原寸確認し、独立視覚レビュー必須指摘0。コード独立再レビューも今回範囲の具体的未解決0であり、全ゲームの無欠陥保証ではない。
- 初期fixtureのEventSystem登録、具象Store注入、EditMode破棄通知に関する失敗は原記録を保持。実Playで検出した取消後フォーカスの退行も修正・再確認した。
- 起動後のPipeline timeoutは実状態を読んで続行し、起動/テストを重複実行していない。Native期間のツールtimeout2件・UnityAI接続情報保存警告1件と、最終全件測定期間を分離。歴史的UGUI例外を解決済みとはしない。
- 詳細は[第一段階の証跡](../.Codex/screenshots/system-stage1-20261031/README.md)。物理機器/OS/実機ビルド/実音聴感は未検証。FMOD無音環境では音声APIと状態のみ確認。

## 並行仕様更新（取得のみ、今回未実装）

4a481b8 / 98772f8では壁滑り/壁蹴り・二段ジャンプの落下距離リセット、攻撃別空中ダッシュのリセット、外界poiseによる被弾中断区別を確認。d101a71では外界ノックバック方向/強さ・攻撃別被弾後無敵・耐性/状態/バリア/オーラ共通判定を確認した。

d00b198までの外界接触ダメージ/通り抜け、追従/固定カメラ、ボス封鎖、初回/未撃破再挑戦演出、撃破済み再戦の復元規則も保護した。再戦入口は別途決定。S08落下閾値/式は未決のままであり、文書取得を外界機能の実装完了とは扱わない。

6a9e1d7までのボスラッシュ間のHP/手札/山札/使用済み/共通ゲージ引継ぎ、敗北時の通常/探索内復帰区別、初回/反復報酬、最後に倒したボスの次から中断再開する規則も取得済み文書として保護。今回これらのゲーム状態・保存機構は実装していない。
