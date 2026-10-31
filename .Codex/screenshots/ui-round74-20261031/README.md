# Round74 重なる敵HP群と本体下端の遮蔽

同じ可視投影の通常敵に、個別HPをまとめる細い共通幹と先頭名の重なり数（×2等）を加えた。論理Yが異なっても既存のXのみの投影で同じ場所に見える場合を扱う。敵座標・当たり判定・本体scale/pivot・足位置は動かさない。分離・Hide・死亡・非描画・boss再初期化で表示を残さない。識別IDを各本体へ増やしたものではない。

下端の主な遮蔽は前景でなくタッチHUDの上側空白だった。FHDの実測は前景Opaque上端134.86px、可視本体の足先208.29px、旧HUD上端240px。旧HUDは31.71px重なっていた。元のRoot500×210と全ButtonRectを保ち、非入力の背景を500×170・中心Y−20へ分離、補助案内を既存font18のまま下へまとめた。新背景上端200pxで足先との余白は8.29px。元の前景・床・ボス固定HP・R73の操作案内は維持する。既存水鏡素材を再利用し、新しい画像の生成は行っていない。

以下は同じ隔離fixture・seed・Coreframe・Canvas更新済みの原寸PNG。比較元はe739537。通常3敵→既存Approach行動→既存7damage/12frame予兆→fixture撃破→実報酬skip→次戦boss。fixtureは既存CreateTestDungeon(6,1,2)で、製品へ新性能値を登録していない。前後でモデル位置・本体root/scale・HP・Spriteが同一であることもJSONで確認した。

|場面・出力|変更前|変更後|
|---|---|---|
| coincident / frame60 / fhd | [原寸](ui-round74-before-coincident-fhd.png) | [原寸](ui-round74-after-coincident-fhd.png) |
| coincident / frame60 / 4k | [原寸](ui-round74-before-coincident-4k.png) | [原寸](ui-round74-after-coincident-4k.png) |
| coincident / frame60 / 4by3 | [原寸](ui-round74-before-coincident-4by3.png) | [原寸](ui-round74-after-coincident-4by3.png) |
| separated / frame90 / fhd | [原寸](ui-round74-before-separated-fhd.png) | [原寸](ui-round74-after-separated-fhd.png) |
| separated / frame90 / 4k | [原寸](ui-round74-before-separated-4k.png) | [原寸](ui-round74-after-separated-4k.png) |
| separated / frame90 / 4by3 | [原寸](ui-round74-before-separated-4by3.png) | [原寸](ui-round74-after-separated-4by3.png) |
| warning / frame98 / fhd | [原寸](ui-round74-before-warning-fhd.png) | [原寸](ui-round74-after-warning-fhd.png) |
| warning / frame98 / 4k | [原寸](ui-round74-before-warning-4k.png) | [原寸](ui-round74-after-warning-4k.png) |
| warning / frame98 / 4by3 | [原寸](ui-round74-before-warning-4by3.png) | [原寸](ui-round74-after-warning-4by3.png) |
| boss / frame90 / fhd | [原寸](ui-round74-before-boss-fhd.png) | [原寸](ui-round74-after-boss-fhd.png) |
| boss / frame90 / 4k | [原寸](ui-round74-before-boss-4k.png) | [原寸](ui-round74-after-boss-4k.png) |
| boss / frame90 / 4by3 | [原寸](ui-round74-before-boss-4by3.png) | [原寸](ui-round74-after-boss-4by3.png) |
| custom / frame0 / fhd | [原寸](ui-round74-before-custom-fhd.png) | [原寸](ui-round74-after-custom-fhd.png) |
| custom / frame0 / 4k | [原寸](ui-round74-before-custom-4k.png) | [原寸](ui-round74-after-custom-4k.png) |
| custom / frame0 / 4by3 | [原寸](ui-round74-before-custom-4by3.png) | [原寸](ui-round74-after-custom-4by3.png) |

保存再読込後：[fhd](ui-round74-after-boss-reload-fhd.png) / [4k](ui-round74-after-boss-reload-4k.png) / [4by3](ui-round74-after-boss-reload-4by3.png)

実際のpause／resume・「続きから」のRaycast、初手5枚、カスタム非表示／復帰／詳細往復、旧登録解放とboss再登録（normal0/boss1/HUD1）を確認。被弾時の個別HP、2体／6体、非描画とboss再初期化、HUDの入力Rectと探索面復帰は関連回帰で確認した。関連445成功、最終全3520成功。全件前に実640×360のGameView/RenderTexture・専用TMPDIR/saveguardを確認し、同じ1実行を追跡した。

最終全件のconsole error0・warning59は既存fixture5分類（warnings.json）。Playでは起動時のUnity.Pipeline /api/exec main-thread60000ms timeoutがerror1、UnityAI接続情報保存のread-only警告1。起動要求の結果を観測して復帰し、同じfixtureを重複起動していない。生ログをnative-errors.jsonに残し、製品例外や歴史的UGUI例外とは区別する。UI修正でPipelineを変更／警告抑制していない。

独立コードレビュー0、原寸coincident/bossの各3比率・前後12枚の視覚レビュー必須0。動的な全経路は主担当の検証で、静止画だけを動作確認としない。完全同位置・同外見の「どの個体の描画か」そのものは一枚に重なる限り特定できない。今回は重なる生存敵数と個別HP群の所属を明確化した範囲。複数同位置bossの視覚、物理機器・実機buildは未確認。歴史的UGUI例外は未解決のまま。

仕様の本文照合は[未決仕様引継ぎ](../../../Planning/未決仕様引継ぎ.md)の決定済み境界欄へ追記した。初回は探索HP／状態を持込・正常帰還は終了時のもの、敗北／任意撤退は外界死亡復帰、中断はcheckpoint継続。再挑戦だけ全快開始・帰還で入場前の探索HP／状態を復元し、帰還自体の自動回復はしない。初回クリアの固有出口も設定可能。外界通常敵は物理回収の永続通貨のみで未回収は死亡／終了／別mapで消失、ボス／一回敵は個別報酬と未取得保持。20b66cd/b0faae0/6f6eb8fは文書仕様で、Round74で新外界導線を実装した意味ではない。旧ClearReturnSessionの全快プレイヤー生成／保存検証を新導線へ一律流用しない。

D05／工場登録拡張・新探索機能・拒否済みキャラ／剣生成は今回対象外。最終640×360、native Mouse/Keyboard、EventSystem、保存guard、原本clean、tracked Assets一致（既存private URP2件を除く）を確認。今回を最終的な見た目の承認や無欠陥保証とはしない。

次候補：ガード／麻痺／バリア等の本体演出と攻撃予兆が同時に出る実戦全景の主従。既存素材を活かし、個体の被弾反応との対応と過密さを整理する。
