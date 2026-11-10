# 初回正常クリアの外界Runtime退避・cold復元（Stage147 / 2026-11-10）

## 候補と区切り

最新masterと全体仕様§2（入場前位置、外界時計停止、手札/山札/使用済み・ゲージ維持、初回終了HP/状態引継ぎ）を照合した。既存の初回クリアHP・毒はStage139、再挑戦RuntimeはStage141で接続済みだが、`OutsideDepartureVitals.Validate`がRuntimeをRetryだけに制限し、Controllerも初回は捕捉していなかった。新しい初回正常クリアで帰還後に再起動すると、保存できた終了個体に対し出撃位置・時計・カード・ゲージがない不足を今回閉じる。

候補比較:

- 探索製品デッキの装備登録はS10の切替/同ID編集/選択中削除3点が未決。最初の1回だけの禁止ルールやスターター自動登録を作らない。
- 初回敗北/任意撤退の死亡復帰は仕様確定だが、復帰点・全回復・解除・リロードの結果を同一帰還計画へ固定する独立単位が必要。今回の正常クリアへ混ぜない。
- 旧null Runtimeには元の位置/時計/手札の情報がないため、自動で推測・移行しない。従来の互換復帰を維持し、旧計画を読み直しただけで新しい指紋を作らない。

## 接続と互換

- 通常の入口→費用確認後に初回でも既存`OutsideEntry.Runtime`へ出撃前Runtimeを捕捉する。Template必須と初回3状態持込み安全停止（S13）は維持する。
- `Begin`の初回生存クリアだけがこのRuntimeを採用し、既存の`OutsideEnd`（終了HP/毒と固定成長差）と対応させる。再挑戦は既存`OutsideRetry`を使い続ける。
- 新規初回Runtimeにだけ`outside-first-runtime-v1`の指紋タグを使用。旧retryのタグ/計算と、旧Choosing/Awarded/receiptのnull Runtime指紋は変更しない。既存Flow/receiptがある場合は後付け出撃Runtimeを取り込まない。
- Runtimeは初回終了個体又は再挑戦個体が必要で、初回終了個体はClearedだけ。出撃・Flow・receipt・帰還保存の対応検証を維持する。
- 個体とRuntimeは既存の帰還1回の書込で同時確定。失敗は旧保存/旧個体を保持し、同計画の再試行/再起動で費用・報酬・成長を二重反映しない。保存形式/Version/旧定義hash、新Manager、Scene/素材は追加しない。
- genericなcold復元が地形/カード/カメラを先に検証し、位置/時計/手札/選択/山札/使用済み/ゲージをまとめて適用。通常View/host再入は保存を再適用せず、現在の同じSessionを保持する。帰還時のアイテムCDリセットは既存仕様のまま。

## 検証

関連310件（Management102/Session88/Return120）、最終全4,177件成功（259.54秒）、同Editor UI408成功。失敗/skip/inconclusive0、最終277ソース・変更3件をhash固定。独立コード・原寸FHD/4K/4:3・Player視覚レビューの必須指摘0。

通常タイトル→探索→入口→費用確認→初回出撃→クリア/選択/Awarded→帰還保存失敗で旧Actor/保存不変→同計画Retry→初回終了HP37+成長5=42/最大105・end139毒と入場時Runtimeを一括確定。位置1188000（19.8world）、frame606、ゲージ4980（83）、手札3枚/選択2/山札17を保持し、warm Cards同一/旧Confirmと旧Actor無作用を確認。その後移動・選択変更→host Close/Bootstrap Single再入で同じActor/Cardsと現在位置を保持し、保存再適用なし・巻戻りなし・新規書込なしを確認した。

手札の試験は既存の所持スターター20枚を明示した隔離fixture初回登録で、S10製品デッキ選択を解決した意味ではない。戦闘は既存フェーズ/報酬UIを通し敵ダメージを加速したfixtureであり、Playerの全戦闘を通常操作でクリアした主張ではない。

同じEditorで新規通常Linux64Mono Playerをbuild（23.845540999999997秒、161639432 bytes、errors0、Pipeline RuntimePipelineConfig未設定警告1）。保存失敗後のAwarded計画を隔離XDGへ移し、通常UIで帰還確定→位置・時計・ゲージ・手札の保存snapshot完全一致と終了HP+成長、追加receiptなしを確認。正常終了→再起動→探索でprofile完全一致、Pause中入力無保存を確認し2プロセスexit0。Playerに診断登録コードは入れず、既知20枚のfixture状態だけを読み込んだ。画像は生時計再開後で、HP39やゲージ84/94の変化を、保存値HP42/ゲージ83と区別する。

原保存7・設定27・先行PCdraft8・guardを保護、1470資産の差分は承認3ソースだけ。仮想機器・故障注入・古いcallback参照を解放し、生成cache/borrowers/pendingNativeDestroy0、EventSystem登録0、clock所有0、Pause/Input barrier残留なし、profiler off。inactive互換Scene・実640×360両RT・専用TMPDIR/saveguardへ復元。全件呼出期限後も同じrunを回収し、重複送信しない。

初回の新規4回帰は、fixture経験値50に初回報酬を加えたため想定より1段階多く成長し、期待HP42に対し47となった。製品の計算を変えず、fixture経験値0とlevel2明示assertに訂正し、終了37＋成長5＝42/最大125の期待値を維持した。初回4失敗を保存する。Native手順の過去「Titleでhost起動済み」前提と再開Pythonのパス変数不足も実行状態を読んで訂正し、製品の条件や期待値を変えていない。

## 残る範囲

初回敗北/撤退の外界死亡復帰、ダンジョン別の初回出口設定、旧null Runtimeの情報不足、S10製品デッキ登録元、S13解除前3状態捕捉、未実装世界要素の退避は別残件。今回を外界全状態/全帰還経路の完成としない。製品マップ・能力解放・戦闘性能・未決値を決めていない。物理入力/聴感/全HotReload、歴史的UGUI/Pipeline/CLI/UnityAIと登録外Sprite基準群を保持する。
