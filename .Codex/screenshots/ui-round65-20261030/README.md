# カスタム画面の視覚階層（Round65）

フォーカスの明るい枠が選択済みに見える一方、使用順とコストの関係が弱かったため、画面全体を整理した。上に「フォーカス」、中央に表示手札の枚数、下に「シヨウジュン n/5」と合計コストを並べ、手札と使用順で同じ番号を示す。控えめなガラス枠と読み取り面を使い、キャラの透けが文字・アイコンと競合しないようにした。既存の青白い枠・元BgCustom素材／色・世界背景・JikiTourai・本絵／miniの参照は維持する。

## 視覚レビューと反復

初回候補は見出し・番号の対応が改善したが、独立原寸批評で「キャラの透けがまだ強い」「4:3の使用順番号が小さい」と指摘された。読み取り面の濃度を上げ、比率変更時にも番号を更新する版へ修正。追加面と見出しはIsOpenに連動し、閉じたHUDへ残さない。新しいラスタ生成や拒否済み生成の再試行はない。既存のwindow／button-selected資源を借用する。

| 状態 | FHD 前／後 | 4K 前／後 | 4:3 前／後 |
|---|---|---|---|
| 通常初期5枚・選択0 | [前](before-initial-five-fhd.png) / [後](after-initial-five-fhd.png) | [前](before-initial-five-4k.png) / [後](after-initial-five-4k.png) | [前](before-initial-five-4by3.png) / [後](after-initial-five-4by3.png) |
| 旧10枚復帰・選択5 | [前](before-legacy-ten-selected-fhd.png) / [後](after-legacy-ten-selected-fhd.png) | [前](before-legacy-ten-selected-4k.png) / [後](after-legacy-ten-selected-4k.png) | [前](before-legacy-ten-selected-4by3.png) / [後](after-legacy-ten-selected-4by3.png) |

画像は実GameViewの原寸。通常は新規出撃から接敵・初期カスタムまで進み、旧10枚は過去の隔離BattleStart保存を複製し、実「続きから」で復帰した。表示5枚／10枚は固定inputHandに対応し、選択で詰め直される内部の残り手札とは区別する。旧データ原本を変更せず、カード順・HandLimit10の互換を保つ。新規の初手5・選択上限5・最大表示10の5×2・左端接続とwindow＋gauge左半分・正方形art／mini・LIFOは不変。

## 動作と範囲

追加Graphicはすべて非入力。手札／取消領域／決定のhit Rectとcallbackを変えず、選択0〜5、上限拒否、領域の空き部分からのLIFO、空解除、表示を隠す／戻す、自己詳細からの復帰、長文スクロール、低コストの明示fixture、決定を確認する。低コストは一時的なMaxCost1を既存APIへ入力し、元値へ戻す検証で製品値を登録しない。長文も表示だけのfixtureで原文へ戻す。

最初の新規回帰は可変CurrentHandから次の対象を引いており固定表示順とずれたため失敗した。固定した初期参照へ修正し、モデルのコストは表示前後で不変と確認する。既存の連繋計算や期待値を変更していない。私有計測でも空SpriteにC#のnull条件演算子を使った読み取りをUnityのnull判定へ直した。製品例外の握り潰しではない。

固定論理frame・カード計算・入力契約・演出時間・保存形式・ダッシュcamera・整理した出撃先・前景下端遮蔽・本体scale／anchorは不変。過去UGUI例外は原因未確定のまま。物理タッチ／pad／IME・実機buildは未確認。実操作は合成PointerEventの実Raycast経路であり物理機器の確認とは区別する。テストや独立レビューは美的完成／ユーザー承認の代わりではない。

追加の原寸レビューで読み取り面の右上矩形が元HPプレートとの斜め接合にかかる点も確認し、面だけ右端を40論理unit内側へ収めた。左端・本文・イラスト・入力領域を動かさず、撮り直した最終6画像を独立再評価し、今回の静止視覚レビュー範囲の未解決指摘0を確認した。


## 証跡

- [独立コードレビュー](code-review.md)／[原寸視覚レビューと指摘履歴](visual-review.md)
- [実操作範囲](operations.json)／[前後の同じカード順](matched-state.json)／[旧セーブ原本保護](save-preservation.json)
- [閉じたHUD](closed-fhd.png)：追加した面・見出し・番号は残留しない。
- [最終ソース・全件結果](final-test-result.json)／[全件警告分類](warnings.json)／[Play警告分類](play-warnings.json)／[後片付け](cleanup.json)

実Playの決定は空選択の確定を確認。選択済みカードを実行して効果が終わるまでの全種類再検証や物理入力機器の確認は今回行っていない。選択の計算・契約は関連回帰／全件と分けて記録する。


## 最終検証結果

関連487件／最終全3507件成功（256.44秒）、compilation/error0。全件は実640×360／専用TMPDIR・保存ガード・最終source一致を確認して一度だけ起動し、呼出側180秒期限後も同じ実行の結果を回収した。warning59は50報酬View未設定fixture、4設定読込失敗fixture、2誤フェーズ、2使用可能カードなし、1プール上限の既存検証診断。Playはerror0、現在groundTruth warning1（Unity AI接続情報のread-only保存）、保持ログには同警告2回とPipelineの既停止通知1回。件数の母集団を混同せず、診断は抑制していない。

原本プロジェクトclean、全追跡assetは検証専用URP2件を除きsubmission一致、実640×360／controller inactive／専用TMPDIR・save guardを復元。入力機器はnative Mouse/Keyboardだけ。

次の具体候補はカスタム内の加工選択・素材と結果の情報階層で、今回の通常選択と同じ見出し・余白の言語が合うかを実画面で評価する。製品値・拒否済み本体生成・ゲーム仕様へは広げない。
