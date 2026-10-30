# Play中の再コンパイル後のグリッド予告初期化（Round59）

前単位で観測したIndexOutOfRangeExceptionを、同じ検証Editorと隔離保存先で再現し、WorldBattleGridView.RefreshTelegraphsの未生成判定だけを修正した。元背景・フォント・世界座標／スケール・6マスの規則・論理フレーム・予告時間・保存形式・Unity参照は変更しない。新しい画像や製品値の登録はない。

## 実際の再現と原因

- 初回のMCP recompileはup_to_dateで再読込が発生しなかった。検証コピーの既存C#へコメントだけを追加し、通常のUnityスクリプト再コンパイルを起こした。原本／masterの未修正版ロジックは変えていない。コメント差分だけであるhash記録は[baseline-probe](baseline-probe.json)。
- [再読込前](baseline-before.json)は床Renderer6／周辺床12／未作成attackMarkers=null、予告子オブジェクトなし。再読込後は床6／周辺12を保ったまま、attackMarkersが長さ0で復元された（[再読込後](baseline-after.json)）。
- 旧コードはnullだけで未生成を判定したため、空配列を生成済みとして6要素ループへ入り、GameFieldView.OnEnable→BindTelegraphs→RefreshTelegraphsで同じ配列例外が発生した（[console](baseline-console.json)）。コンパイルは成功しており、例外箇所は製品の再初期化経路。保存JSONのhashは不変だった。
- 修正は空配列を未生成のnullへ戻す1判定と理由コメントのみ。予告なしの遅延生成を維持し、通常の生成済み6要素には触れない。例外のcatch／警告抑制、保存ガードや期待値の緩和は行わない。

## 修正後の実再コンパイル

同じ640×360・専用保存先で一時停止中に2状態を確認した。検証用コメントは最後の再コンパイル前に除去し、検証コピーを公開対象のソースへ完全一致させている。

| 状態 | 結果 |
| --- | --- |
| 未作成→再読込 | [前](fixed1-before.json)／[後](fixed1-after.json)。例外0、不要な先行生成なし |
| 生成済み6個→再読込 | [前](fixed2-before.json)／[後](fixed2-after.json)。同じ床Renderer参照と6個のマーカー参照・位置を保持、古い表示を解除 |
| 再bind／Show／Hide 2周 | [循環結果](fixed2-rebind-cycles.json)。必要な予告を再表示し、重複・参照置換なし |
| 保存と最終ソース | [結果](reload-result.json)。保存ファイルhash不変、検証コメント除去・最終ソース一致 |
| 新規の通常Play | [結果](normal-play-result.json)。実EventSystem Raycastによる出撃／撤退から帰還、console error0 |

再読込後は通常のRefreshも呼び、描画直前の更新を確認する。生成済みケースはCoreの時間を進めず、既存公開View APIへ検証用BattleField／予告マスを与えた表示検証。新規Playの出撃・撤退はControllerの通常UI経路を使う。攻略モデル全体をホットリロードで保存復元する新契約の検証ではない。

## 回帰・保護・限界

観測した空配列復元を再現する回帰を1件追加。Bind(null)、予告なしの非生成、必要時の遅延生成、位置、解除、Hide／Show再利用と重複防止を確認する。関連は予告3件＋既存UI／床・スケール等378件＝381件成功。実再コンパイルのconsoleは各error0で、Unity AI接続情報のread-only警告は環境／ツール警告として全件のテスト警告と分ける。play-warnings.jsonの履歴streamと現在console件数も分離する。

最終全件はactual640×360・専用TMPDIR／保存ガードを確認し、同じ実行を終了まで追跡した。検証対象のsource hash、console、全件警告分類、独立レビューは併設JSONへ記録。原本clean／検証copyの全追跡Assets一致（既存の検証用URP2設定だけ除外）と低解像度・入力機器の復元はcleanup.json／input-cleanup.json。

物理パッド・タッチ・IME・実機buildは未確認。画面の新デザインや視覚素材の追加は今回の範囲外で、既存挙動と再読込寿命の修正である。次の視覚候補は、予告枠と床色（特にマグマ等）を実画面で読み分けやすくする改善。無欠陥や全画面の美的完成を保証するものではない。

最終全 **3,495件成功**（262.04秒、失敗／skip0）。console error0／warning62を実ログで分類。

公開前pullで追加仕様b2882a8を含むmerge23dccffを取得（[記録](spec-update.json)）。ダンジョン外の空中カード・固有攻撃／落下設定、地上ダッシュ、可変ジャンプ、壁蹴り・壁滑りの文書更新であり、今回の実装対象へ追加していない。a317766からの差分は仕様書だけで、検証済みのコードは不変。

push直前の競合更新ba229b5も通常mergeで保持。地上ダッシュの押下開始方式・ダッシュジャンプの横飛距離／残像・地上ダッシュ共通残像・二段ジャンプの装備能力化という4項目の文書追加であり、今回未実装。コード差分はなく最終検証済みsource hashも再照合した。
