# 接触回復アイテムの専用瓶（Round64）

既定51セルの回復アイテムを、四角い＋のプレースホルダーから、小さなふた付き回復瓶へ変更した。停止地点のしずく／ショップ袋とは形・既存の表示高さで区別し、接触で消える拾得物として読みやすくする。囲いを外し、描画の可視幅・高さを抑えた。元のroot位置・tint・sorting・camera・床／前景・本体scale／anchorは不変。

## 素材

OpenAI画像生成ツールで、既存の非キャラ `shop_bag.png` と `recovery_drop.png` を参照し、独立した瓶＋十字の単体を新規生成。キャラ画像や拒否済み入力は使用していない。[生成原本](../../../SourceFiles/Pickups/heal_vial_generated.png)を保全し、承認済みの書出し（最大連結alpha成分・比率保持の最近傍・共通6色パレットからの調整・硬い透過・基準位置）でゲーム用へ整えた。原本1254×1254、出力64×64、可視38×44、実使用5色、alpha0／255。

PNGとResourcesのbytesを同一に保持し、Point／Clamp・PPU64で読み込む。Shop／回復訪問と同じView所有loaderを利用し、複数pickup・再表示では共有、OnDestroyで所有Sprite／Textureだけ解放する。素材欠損時の従来表示を保持。[出所・hash・書出し](asset-manifest.json)。

## 仕様と通常ルート

[全体仕様§13](../../../Planning/ゲーム全体仕様.md)の「接触で自動取得・即時発動」「HP満タンでも消費して消える」に従う。既存値20、取得済み履歴、通常境界の保存、再開互換を変更しない。ダンジョン外の回復／deck reload／死亡復帰地点の新文書とは別の既存ダンジョン内機能である。

既定マップ・敵位置・デッキ・初手5枚を変更せず、実Raycast出撃→接敵→カスタム確定→報酬skip→既定pickupへ進む。最初の敵撃破とHP60の前提（既存Coreの40被弾・強靭度影響なし）だけは隔離検証入力と明示し、攻撃ボタンによる撃破検証や製品値変更とは扱わない。

原寸の同解像度・HP／取得状態・actor／camera表示位置を合わせて比較。表示位置合わせは私有コードだけで論理位置／保存を変えない。既存の＋20浮遊数値を消去／短縮せず、描画待ちによる位相差は完全一致しない。画像取得中の隔離保存hash不変を確認した。

| 状態 | FHD 前／後 | 4K 前／後 | 4:3 前／後 |
|---|---|---|---|
| 接近・未取得 | [前](before-pickup-ready-fhd.png) / [後](after-pickup-ready-fhd.png) | [前](before-pickup-ready-4k.png) / [後](after-pickup-ready-4k.png) | [前](before-pickup-ready-4by3.png) / [後](after-pickup-ready-4by3.png) |
| 接触・消費後 | [前](before-pickup-collected-fhd.png) / [後](after-pickup-collected-fhd.png) | [前](before-pickup-collected-4k.png) / [後](after-pickup-collected-4k.png) | [前](before-pickup-collected-4by3.png) / [後](after-pickup-collected-4by3.png) |

## 限界

固定論理frame・回復値・判定／消費／保存形式・報酬・入力・背景／JikiTourai・Round60の4修正を維持する。物理入力／実機buildは未確認。過去のEditor再読込UGUI例外は原因未確定で、今回解消したとはしない。テストと独立レビューはユーザーの美的承認を代替しない。

## 実経路の確認

撮影中のポーズで位置・論理時計・未取得状態は進まない。接触でHP60→80、瓶は消え、即時取得では保存番号3を維持。既存のショップ退出境界で4へ保存後、実ポーズ画面の「読込」でHP80／取得済み／瓶なしを復元し、その後の接敵・初回カスタムでpickup残留0を確認した。[実経路](actual-route.json)・[比較状態](matched-state.json)・[表示中保存不変](visual-save-invariance.json)・[関連22件](targeted-tests.json)・[独立レビュー](review-results.json)。

私有検証コードのMap型参照は実行前のコンパイル診断で修正。最初の再読込入口では撮影用user-pauseを明示解除し忘れたため、pauseボタンが再開となり読込ボタンがなかった。読込が未実行であることと現在状態を確認し、既存の正しい入口から復元した。製品の保存／pause規則を変更していない。[検証側の切り分け](harness-notes.json)。

## 次のまとまり候補

[次接敵の現在画面（未変更・FHD）](next-custom-reference-fhd.png)では、カスタムの手札ミニアイコン／選択済み空枠と、中央余白・確定操作の見え方を全体として再調整する余地がある。次は単体小アイコンの追加ではなく、既定の左半画面・5×2手札・本絵比率・LIFO・操作位置を守った選択フィードバックと画面構成を一つの視覚単位として検討する候補。今回のpickup改善で、この画面や攻撃本体連番を完成扱いにはしない。

別の新規隔離先でHPを下げずに同じ通常ルートを通り、満タン100のまま接触で消費、保存境界後の実読込でも取得済み／瓶なし／HP表示100を確認した。[満タン実経路](actual-full-hp.json)。

[最終Play console](play-console.json)・[Play警告分類](play-warnings.json)。

## 最終検証

関連22件、最終全3,506件成功（245.69秒、失敗／skip0）。console error0／warning59。警告は実ログで報酬View未設定fixture50、設定読込失敗fixture4、報酬phase外2、使用可能カードなし2、pool上限1。期待値緩和／診断抑制なし。[全件・source hash](verification.json)・[警告分類](warning-classification.json)・[保護確認](cleanup.json)・[入力機器](input-cleanup.json)。

原本clean、追跡Assetsと新規PNG／bytesは検証版と一致（既存の私有URP2設定を除く）。実640×360・inactive controller・専用TMPDIR／保存ガード・native Mouse／Keyboardのみへ復元。独立コード／素材／原寸視覚レビューは今回範囲で未解決指摘0。最終通常Playの現在警告はUnity AI接続情報のread-only保存警告で、製品素材警告とは区別した。
