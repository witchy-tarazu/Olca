# 外界探索: 水平カメラの追従/固定設定

仕様§1の基本追従・部屋ごとの固定設定を、既存外界の水平表示へ接続。Root Sessionが不変policyを保持し、非表示時だけ設定できる。Viewはその方針を投影し、旧追従の倍率/端余白/狭いマップ中央配置を維持する。新Manager/Camera/Listenerは追加しない。

固定中心12は明示検証値。実操作証明ではRoot Sessionへ非表示時に登録して既存Bootstrap拠点の「ガイカイ タンサク」入口を使った。設定メニューや製品ボス部屋の登録を追加したとは扱わない。縦カメラ/同一View内の部屋trigger切替/製品マップ/移動制限/衝突や新ゲームルールは未実装。[範囲/限界](scope-and-limits.json)。

[実入力・固定床/キャラ移動・設定拒否・支持・Pause・再生成・保存不変](native-camera.json) / [Root/host寿命](native-lifetime.json) / [実640動画](fixed-motion-640.mp4)。InputSystem仮想keyboardとEventSystem実Raycast。Stage30のnativeパッド操作は未実施（既存回帰とは区別）。物理機器ではない。固定画面でもCore座標・時計を直接書き換えていない。終了時はFollowへ戻した。host Suspendは既存の別保存境界。

下記18枚は各行のframe/位置/高さ/支持を固定しCanvas更新後の実RTで取得。[manifestと実投影測定](image-manifest.json)。固定中は床phaseとカメラが動かず本体が移動、追従中はカメラが移動することを実測。既存の検証足場ラベルが本体脚の背面で一部隠れる状態は制限として記録し、製品表示の完成とは扱わない。元背景/JikiTourai/Idle-Move-JumpのPPU/pivot/足元は維持。[変更前Stage29](../system-stage29-20261102/README.md)は既定Followのみ。旧版に固定状態があるとは主張しない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|camera-follow-start|[画像](camera-follow-start-fhd.png)|[画像](camera-follow-start-4k.png)|[画像](camera-follow-start-4by3.png)|
|camera-follow-move|[画像](camera-follow-move-fhd.png)|[画像](camera-follow-move-4k.png)|[画像](camera-follow-move-4by3.png)|
|camera-fixed-start|[画像](camera-fixed-start-fhd.png)|[画像](camera-fixed-start-4k.png)|[画像](camera-fixed-start-4by3.png)|
|camera-fixed-move|[画像](camera-fixed-move-fhd.png)|[画像](camera-fixed-move-4k.png)|[画像](camera-fixed-move-4by3.png)|
|camera-fixed-platform|[画像](camera-fixed-platform-fhd.png)|[画像](camera-fixed-platform-4k.png)|[画像](camera-fixed-platform-4by3.png)|
|camera-follow-restored|[画像](camera-follow-restored-fhd.png)|[画像](camera-follow-restored-4k.png)|[画像](camera-follow-restored-4by3.png)|

Core29/Application553/Integration605、同Editor反復102、最終全3690件成功（335.57秒）。[全件](full-tests.json) / [反復](same-editor-regression.json) / [独立コード/原寸レビュー](independent-review.json)。実640×360/2RT・専用TMPDIR/saveguard、最終15hash一致。console error0、警告は[分類](warning-classification.json)して抑制していない。

[cleanup](cleanup.json)で元プロジェクトclean/928資産照合（既存privateURP2例外のみ）、保存bytes、仮想機器/proof/注入/callback/lease/cache/EventSystemと実640/TMPを確認。[ツール履歴](tool-history.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。歴史的問題やbuild/物理入力等の未確認は今回の完了と分離して保持。
