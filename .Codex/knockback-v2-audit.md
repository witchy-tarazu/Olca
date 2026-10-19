# 通常接敵ノックバック v2

親が頭身を比較確認した3姿勢だけを採用。宙返りと不採用v1は含めない。入力blobは21d14c782ee7abb21ea5e5ba5471720c947b3c83（UTF-8 Base64 ZIP）、33772 bytes、SHA256 a95d0e6acacae2300ca15cdbb1c1d00af8b23fa487d78b67b5856014a0cddafe。manifestの3 PNG hashを全照合し、256×256 RGBA・alpha0/255を確認。画像は取得後に加工していない。

Unity生成metaでPPU100、Point、非圧縮、pivot(127,14)を設定。通常接敵は衝撃01→後退02→着地03を既存進捗から選ぶ。描画中だけAnimatorを停止し、finallyで元の有効状態と通常表示を復元する。ダッシュ接敵・宙返り・帰還ジャンプは変更しない。通常ノックバック0.35秒、X EaseOutQuad、Y EaseInQuad、距離、着地点補正、カメラ追従は変更なし。Transformの倍率・回転・ローカル位置による補正なし。元idle SHA256 493fb6c38a97e3531be705702aeab7649b2de222119c39cadfaa83cbe6f75f43を維持。

Unity MCP全EditMode 1532/1532成功（32.96秒）。前後consoleErrors=0、compilationFailed=false、既存warning10。結果 /workspace/.olca-cloud/results/knockback-v2/editmode-tests.json、console-before.json、console-after.json。

描画用コピーの通常DungeonBattleControllerから実接敵を確認。3姿勢すべてを観測し、最後はheroine_idle_10_0、Animator有効へ復帰。scale(-1,1,1)、ローカル位置・回転不変。着地前後は足元の高さが揃い、記録Y=0で跳ねなし。顔・頭身は受領素材の姿勢として表示され、伸縮なし。終了後は通常戦闘idle、接敵前はmove。描画用console groundTruth errors0、warning1。履歴エントリーには過去操作のエラーも含まれるため現在値と区別する。

## 収録証跡

- 動画 /workspace/.olca-cloud/results/knockback-v2/normal.mp4、native1920×1080、2914761 bytes。
- SHA256 278c9f4a2e134f5fee48c89f55cef5558e1502f7fc953e51c56016fd35d1663b。
- 共有blob f7928756452a71525dea778088150de8025946ea はMP4のBase64をUTF-8で保存。一度復号してMP4にする。
- 撮影コード /workspace/.olca-cloud/render-check/KnockbackV2Probe.cs。UTF-8 blob 180f7d3122018e09f29ffe11aac23842f827a24d。SHA256 1015fac424172b2383f5b315e1d580845710bcfba3d3f367492bda98a4ab85cf。
- 195 capture、実測区間11.507486439秒、平均16.858590364 fps、間隔0.038517297～0.139713994秒。normal.csvとnormal-times.txtに各時刻を保存。
- 動画は実測間隔をconcatへ渡しVFRで符号化。末尾50ms保持と1枚複製を含み196 encoded frames、stream duration11.536秒、avg_frame_rate1750/103、time_base1/16000。r_frame_rate2000/1は実測fpsではない。フレーム補間・縮尺変更なし。
- RenderTextureは最後に描画された画面を読むため、同じ行のCPU状態と画像には最大1描画更新程度の差がある。実画面0174/0178/0182/0184で01/02/03/idleを確認した。

宙返りの専用素材・演出は未完成のまま。テスト環境の自動URP/lock差分は公開しない。
