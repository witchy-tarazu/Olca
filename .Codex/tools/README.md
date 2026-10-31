# 検証済み差分の段階別公開

`verified_publish.py` はOlcaの証跡形式に限定した標準Python CLIです。各コマンドを**別々に実行し、終了コード0と出力を確認してから**次へ進みます。シェルの `;` 連結や、失敗後の手動commit/pushによる迂回はしません。公開の明示承認は別途必要です。

```sh
python .Codex/tools/verified_publish.py --repo . check --workspace /workspace/olca-validation-20261024/project --evidence-dir .Codex/screenshots/system-stage4-20261031 --receipt /tmp/olca-publish-receipt.json
```

`CHECK OK` 後にreceiptのtree/baseHead、検証条件、`whitespaceExceptions` をレビューします。続いて、あらかじめ作ったメッセージファイルを指定します。

```sh
python .Codex/tools/verified_publish.py --repo . commit --receipt /tmp/olca-publish-receipt.json --message-file /tmp/olca-commit-message.txt
```

`COMMIT OK` 後に、公開承認の範囲内で次を実行します。

```sh
python .Codex/tools/verified_publish.py --repo . push --receipt /tmp/olca-publish-receipt.json
```

`PUSH VERIFIED` は通常push後の再fetchでorigin/masterとの一致まで確認した場合だけ表示します。通信失敗などでpush後の結果が不明なら、自動再試行せずremoteの実状態を確認してください。remote進展時は停止し、通常の統合・再検証・review・新receiptへ戻します。force pushはありません。送信先は単一URLのorigin/masterのみで、別push URLは拒否します。

## 必須証跡と停止条件

証跡dirにはStage3で採用した以下の5ファイルが必要です。新しい検証結果は同じ構造で記録し、過去の成功値をコピーして補完しません。

- `full-tests.json` / `related-tests.json`: 正数total=passed、failed/skipped/inconclusive=0、groundTruthのcompilationFailed/compiling=falseとconsoleErrors=0、consoleCounts.error=0、sourceHashes（任意のproductionSourceHashesを含む）。関連hashは全件hash集合と一致する必要があります。
- `final-source-hashes.json`: full-tests.sourceHashesと完全一致。
- `cleanup.json`: originalClean/storageGuard/defaultStoreAbsent=true、differences=[]、consoleErrors=0、devices/actualRenderのscreenと全textureが640×360。双方のtempはworkspaceの兄弟`save/tmp`と一致。
- `independent-review.json`: reviewerあり、staticReview.unresolvedConcreteFindingsとvisualReview.unresolvedRequiredFindingsが整数0。

全tested hashの現在のsubmissionと検証workspaceの実ファイル、全stagedファイルとindex blobを照合します。staged Assetsはすべて全件tested hash集合に必要です。追加のドキュメント/Pythonもindexとworktreeが一致する必要があります。未stageのtracked変更、競合、merge、master以外、Asset検証漏れ、削除/symlink/submodule、`.meta`・`.env*`・既知の資格情報パス/鍵拡張子を拒否します。資格情報の網羅的検出器ではないため、差分の人手確認も必要です。

`git diff --cached --check <baseHEAD>` は実行し、エラーを丸ごと無効化しません。core.whitespaceの標準3検査を明示し、stagedファイルのwhitespace属性上書きは拒否します。通常の許容例外はAssets下の`.prefab`/`.unity`で、先頭が厳密に次の2行の場合の、追加/変更行 `^  m_(?:Name|EditorClassIdentifier): +$` です。空のUnity scalarの末尾**ASCII space**だけを許容します。非空値、タブ、別フィールド、異なるヘッダ、その他のcheck診断は停止します。

```text
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
```

許容した各行のパス/番号/原文/理由をreceiptへ記録します。正規化せずテスト済みバイトを維持します。

前段から既存の`Assets/Resources/OlcaSystem/*.prefab`も同じ厳密条件で調べ、変更行の`whitespaceExceptions`と別に`existingUnityEmptyScalars`へ記録します。Stage3由来のEventSystem 46/47/61/62行、UIRoot 74/97行の6行もここに含まれます。

## receiptと実行境界

receiptはworktree外へ出力し、同一バイトを`.git/olca-verified-publish/<SHA256>.json`にも保管します。commit/pushは発行済みreceiptとの一致に加え、tree・証跡ファイルhash・実ファイル・空白例外などを再検証します。commit前はHEAD=baseHEAD、push前はHEADの唯一のparent=baseHEADかつcommit tree=receipt tree、clean worktree（untracked含む）が必要です。fetch後も再確認します。

この仕組みは誤操作に対するローカルゲートで、同じ権限で`.git`やプログラムを書き換える悪意ある実行者への署名基盤ではありません。証跡の真偽自体は主担当/独立reviewの責任です。実行中は他プロセスの編集・stage・commitを停止してください。Git hooksは無効化しません。hookで差分を変えた場合など、commit後再照合に失敗したらpushを実行しません。

`check`はGit refを変更しません（tree計算でGit objectとローカルreceiptは作成）。`commit`だけがcommitを作成し、`push`だけがfetch/pushします。このCLIはstageを行いません。

## 独立ローカルテスト

```sh
PYTHONDONTWRITEBYTECODE=1 python -m unittest discover -s .Codex/tools -p 'test_verified_publish.py' -v
```

一時repoと一時bare remoteだけで動作します。実プロジェクトのcommit/pushやUnity操作は行いません。

## 既存Sceneブロックの差分誤対応

大量の類似YAMLブロック削除では、Gitが不変Materialを追加行として対応させることがあります。追加の限定判定は、診断行が厳密に`  m_LockedProperties: `（末尾ASCII space1個）であり、HEAD/indexがLFのUnity YAML、全headerが対応する`--- !u!数字 &数字`形式で各fileIDが両側一意、同type/fileIDのheaderから次header/EOFまでの全byteが一致する場合だけです。未対応header（stripped等）、CR、重複ID、新規/変更block/他fieldは拒否します。receiptへindex行/base対応行/header/block SHAと不変理由を記録し、資産のテスト済みbyteを維持します。diff --checkは実行し続け、他の診断を無効化しません。
