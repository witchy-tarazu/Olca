# Round73 戦闘操作案内とタメ状態の階層

攻撃・カードの実割当を既存ボタン内に集約し、上段はカスタム／ポーズの補助案内1行へ整理した。「固有攻撃」とカードを区別し、下段はタメ途中の進行量と通常解放、完了後の「離して攻撃」、停止保持・再押下再開を状態に応じて表示する。硬直取消可能な窓では攻撃可能なボタンと矛盾する「再使用待ち」を表示しない。

キーボード／パッドは既存profileと入力機器追従を使用する。キーボードEscapeは既存の固定ポーズキー。タッチには未実装のカスタム操作を案内せず、既存の右上ポーズを示す。新しい操作・チャージ規則や画像素材の追加ではなく、既存JikiTourai・水鏡部品の表示接続を整えた。入力領域・背景・キャラ比率／アンカー・性能・固定フレーム・保存形式は変更しない。

比較元は9c942e83（b0faae0／6f6eb8fは文書のみ）。通常の隔離出撃→敵接触→初期カスタム決定を使用。最終比較は実入力のまま論理frame1821・タメ60/60へ揃えた。途中例はframe125・タメ29/60で、別実行の30/60画像と厳密一致を主張しない。各状態の全比率でもCoreframe・タメ量・保持状態を固定し、Canvas／実描画更新を待って取得した。下記は加工していない原寸PNG。

| 状態 / 出力 | 変更前 | 最終変更後 |
|---|---|---|
| タメ60/60・frame1821 / fhd | [原寸](ui-round73-before-ready-fhd.png) | [原寸](ui-round73-final-ready-fhd.png) |
| タメ60/60・frame1821 / 4k | [原寸](ui-round73-before-ready-4k.png) | [原寸](ui-round73-final-ready-4k.png) |
| タメ60/60・frame1821 / 4by3 | [原寸](ui-round73-before-ready-4by3.png) | [原寸](ui-round73-final-ready-4by3.png) |

最終ソースの追加表示：

- charge: [fhd](ui-round73-final-charge-fhd.png) / [4k](ui-round73-final-charge-4k.png) / [4by3](ui-round73-final-charge-4by3.png)
- idle: [fhd](ui-round73-final-idle-fhd.png) / [4k](ui-round73-final-idle-4k.png) / [4by3](ui-round73-final-idle-4by3.png)
- resume-pending: [fhd](ui-round73-final-resume-pending-fhd.png) / [4k](ui-round73-final-resume-pending-4k.png) / [4by3](ui-round73-final-resume-pending-4by3.png)
- pad: [fhd](ui-round73-final-pad-fhd.png) / [4k](ui-round73-final-pad-4k.png) / [4by3](ui-round73-final-pad-4by3.png)
- touch: [fhd](ui-round73-final-touch-fhd.png) / [4k](ui-round73-final-touch-4k.png) / [4by3](ui-round73-final-touch-4by3.png)

実操作は短押し／保持／解放、pauseで60/60保持→復帰後の再押下待ち→再開、カスタム開閉とカード使用による解除、Jへの再割当と復元、仮想Gamepad・Touchscreenの実InputSystem/UIInputModule経由60frame保持・解放を確認。最終ソースでは「続きから」のRaycast操作で保存境界を再読込し初手5枚とHUD復帰を確認した。機器除去時に既存の自動pauseが働いた状態は確認して操作を止め、二重押下しなかった。実機／物理IME・パッド・タッチは未確認。

独立コードレビューは未解決0。原寸視覚レビューの4:3でカードボタン2行がやや密という軽微指摘に、文字や入力領域を縮めず行間を調整し再確認。最終レビュー結果はreview.jsonへ記録する。初期の未settle通常画像と別frameの候補比較は最終比較には使わず保持した。撮影freeze後の同じ押下を継続しようとした検証失敗は新しい中立→押下へ直したもので、製品のチャージ変更はしていない。

関連384件成功。最終ソースは実640×360のGameView/RenderTexture・専用TMPDIR・saveguard確認後に全3516件成功、error0。warning59は既存fixtureの診断に分類し抑制しない。PlayのUnityAI接続情報保存警告は別分類。機器・割当を復元し、最終低解像度、EventSystem、保存ガード、原本clean、tracked Assets一致（既存private URP2件を除く）を確認した。

同位置・同外見の敵本体識別と固定タッチHUDによる本体下端の一部遮蔽は未解決。歴史的UGUI例外も解決扱いにしない。D05・工場登録拡張・ダンジョン外の新機能は今回未実装。安全pullで取得したb0faae0の初回／再挑戦HP・状態引継ぎと6f6eb8fの外界報酬／未取得ドロップは文書更新であり、HUDの実装完了とは区別する。

次候補は既知の同位置敵の視覚的対応と本体下端の遮蔽を、入力位置／元スケールを守って改善するまとまり。今回を見た目の最終承認や全体の無欠陥保証とはしない。
