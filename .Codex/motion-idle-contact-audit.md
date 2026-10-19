> 公開範囲の訂正: 5aa4c0cに含めた接敵準備フックは後続コミットで分離。現在の公開実装は移動中idle修正のみ。フックは /workspace/.olca-cloud/contact-hooks-pending.patch に保全し、専用素材と同時に接続する。以下のフック説明は準備内容の記録。

# 移動中idle混入の修正と接敵素材の準備

## 移動表示の不具合

GameFieldViewが描画フレーム間のTransform座標差でisMovingを毎回falseから判定していた。60Hz論理更新を挟まない描画では差0、また高速描画では論理移動量が25×描画DeltaTimeの上限を超え、動いていてもIdleへ遷移し得る。

修正はDungeonSystemの自動前進の有効状態を公開し、表示側がその状態を参照するもの。論理座標/速度/入力/カメラは変更しない。接敵・死亡・階層終了・保存停止・移動停止ではfalse、ジャンプと表示移動中は既存の優先条件を維持。Dashも移動停止時には表示を継続しない。

AnimatorのMove/Dashループは有効、Move曲線は専用8枚を参照。ループ境界にIdle素材を挿入する処理はない。通常入力の自動歩行/Shiftダッシュと描画動画で検証する。

## 宙返り・ノックバック素材（接続準備済み・素材未配置）

通常経路はDungeonBattleController.OnBattleStarted → TransitionToBattleAsync → ContactAnimator.PlayAsync。通常接敵は前進0.1秒、衝撃停止0.1秒、後退0.35秒。ダッシュ接敵は突進0.2秒、衝撃停止0.1秒、宙返り0.2秒。現物はGameplayPause.DeltaTimeを使う表示ループであり、論理フレーム固定ではない。60Hz換算は各6/6/21と12/6/12フレーム。既存の時間・位置・放物線・カメラを維持する。

Transform回転は行っていない。回転は素材内の姿勢として描く。親側で専用宙返り6枚を制作中。0.2秒を6区分し、60Hz換算で各2フレーム相当として表示する。ノックバック3枚は被弾反り／後退反り／着地踏ん張り。ContactVisualPhaseはImpact、Knockback、DashRush、Somersault、None。既存の時間ループから姿勢進捗を通知し、GameFieldViewが専用Sprite配列を選ぶ。表示中だけ通常Animatorを停止し、finallyで元の有効状態とSpriteへ復元する。通常move/dash/jumpだけで完成扱いにしない。

基準画像: Assets/Art/Sprites/Characters/Heroine/Idle/heroine_idle_01.png。256×256 RGBA、PPU100、Point、左下原点pivot(127,14)、表示倍率(-1,1,1)、flipXなし。パレットは肌#C18666/#A5613A、服#433F3E/#5A5554/#34302F、髪#FEFEFD/#A69799。素材は親側で準備中。姿勢で体格・倍率・足元基準を変えない。

## 別作業の保全

ガード横断検証の追加差分は /workspace/.olca-cloud/guard-boundaries-pending.patch に保存。死亡後のガード残留1件を再現、1461/1462成功。今回は見た目修正を優先し、機能修正/公開は未実施。

## 描画検証と限界

通常Controllerの自動歩行・InputSystemのShift押下・規定距離ダッシュ終了・停止を実描画。after.csvは移動区間Idle0、Move73、Dash2、停止区間Idle12（切替前Move1）。実キャプチャ88枚を記録時刻で並べた1920×1080動画 after.mp4 を保存。停止は表示判定確認のため移動モデルを明示Pauseするfixture。ダッシュは押しっぱなし継続でなく既存の規定距離方式であり、この操作仕様は変えていない。

修正前動画では低い描画頻度のため一瞬のidleは捕捉できていない。修正前の連続表示評価Falseログは一時停止直後のDeltaTime0も影響するため、単独で原因を実証した扱いにしない。修正後は正のDeltaTime(0.04258437)で位置更新なしの連続表示評価でもmoving=Trueを確認。旧式の座標差0→falseと、修正後の論理状態継続の差を検証する。

証跡は /workspace/.olca-cloud/results/motion-idle/。接敵の専用6/3枚の見た目確認は素材受領後の残件。親側生成中の素材は本コミットに含めない。

最終全Unity MCPテスト1458/1458成功（30.68秒）。検証Editor現在error0/既知warning10、描画Editor現在error0/warning1。専用素材未設定のため接敵ビジュアル完成の報告は行わない。

分離後の全Unity MCPは1457/1457成功（31.06秒）、error0/既知warning10。接敵準備用テスト1件もパッチへ分離。動画共有blobはfb8c7339339247006e48d65c569a2f00f06396e6（UTF-8 Base64）、撮影コードblobは3269ee7c15a65a92abc370ecd76bb6899c02d236。実取得平均17.4618fps、88枚/4.982304秒、1920×1080。MP4 SHA256: 0866f3a3d8de37892ad0952b7d79fd11c2732f8669c472c2241ebea05891307e。
