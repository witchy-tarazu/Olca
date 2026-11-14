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

- `full-tests.json` / `related-tests.json`: 正数total=passed、failed/skipped/inconclusive=0、groundTruthのcompilationFailed/compiling=falseとconsoleErrors=0、consoleCounts.error=0、sourceHashes（任意のproductionSourceHashesを含む）。関連hashは全件hash集合に含まれ、同じパスのhashが一致する必要があります。
- `final-source-hashes.json`: full-tests.sourceHashesと完全一致。
- `cleanup.json`: originalClean/storageGuard/defaultStoreAbsent=true、differences=[]、consoleErrors=0、devices/actualRenderのscreenと全textureが640×360。双方のtempはworkspaceの兄弟`save/tmp`と一致。
- `independent-review.json`: reviewerあり、staticReview.unresolvedConcreteFindingsとvisualReview.unresolvedRequiredFindingsが整数0。

全tested hashの現在のsubmissionと検証workspaceの実ファイル、全stagedファイルとindex blobを照合します。staged Assetsはすべて全件tested hash集合に必要です。追加のドキュメント/Pythonもindexとworktreeが一致する必要があります。未stageのtracked変更、競合、merge、master以外、Asset検証漏れ、削除/symlink/submodule、未承認`.meta`・`.env*`・既知の資格情報パス/鍵拡張子を拒否します。明示承認済metadataの限定手順は下記を参照してください。資格情報の網羅的検出器ではないため、差分の人手確認も必要です。

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

## 明示承認済みEditor登録metadataの限定手順

既定では`.meta`を拒否する。ユーザーが対象pathを明示承認し、検証済みのEditor登録原本を含める場合だけ、`check`へ`--metadata-approval /absolute/path/metadata-approval.json`を指定する。全体の検査を無効化するオプションではない。承認manifestは公開範囲の全staged path/hashを列挙し、集合の一部だけをstageしても拒否する。metadataを証跡hashから外したり、receiptを書換えたり、失敗後に手動commit/pushへ進まない。

manifestの形式は次のとおり（値は例であり、実公開には実測値と実際の承認を記録する）。

```json
{
  "version": 1,
  "approvalSource": "実際のユーザー承認と具体的な対象範囲の参照",
  "approvedFiles": {"Assets/Example.cs": "64桁SHA256", "Assets/Example.cs.meta": "64桁SHA256"},
  "editorEvidence": {"path": "/absolute/path/editor-readback.json", "sha256": "64桁SHA256"},
  "metadata": {
    "Assets/Example.cs.meta": {
      "assetPath": "Assets/Example.cs", "sha256": "64桁SHA256",
      "guid": "32桁小文字GUID", "isFolder": false
    }
  }
}
```

Editor証跡は同じ検証projectでAssetDatabaseをread-only照合した結果を使う。`version:1`、`provenance:"UnityEditor.AssetDatabase"`、`editorVersion`、検証workspaceと一致する`projectPath`、`assets[metaPath]`の`assetPath/guid/isFolder/metaSha256/importer/mainAssetType`を持つ。GUIDの正逆引きも主担当が照合する。CLIは登録証跡のhash、実metaのGUID／Importer／対応assetまたはfolder、repoとworkspaceの一致、両treeのGUID重複、全件tested hashへのmetaと対応fileの収載を検査する。folderは実directoryの存在を検査する。Editor生成物をAIが書換え・正規化して合わせない。

既存の最小script metadataは、`.cs.meta`かつ内容が厳密に`fileFormatVersion: 2`と`guid: lower32`の2行だけの場合（末尾LFの有無だけ許容）、同じEditorの`MonoImporter`登録証跡との一致に限り扱う。他のImporter欠損・不正GUID・重複GUIDは拒否する。資格情報／unsafe pathはmanifestに書いても許可しない。

Importerの空scalarに対する`git diff --check`例外も、承認metadata・検証済みbytesに限る。今回の実Editor原本に存在する次の完全一致行だけをreceiptへpath／行番号／原文／理由付きで記録する（末尾ASCII spaceは1個）。非空値、未知field、tab、別の字下げは拒否する。

- 2space字下げ: `userData`、`assetBundleName`、`assetBundleVariant`、`mipmapLimitGroupName`、`customCharacters`
- 4または6space字下げ: `customData`、`indices`

manifest全体のhashとEditor証跡hashをreceiptに束縛し、commit前・push前・fetch後にも同じ条件を再検証する。承認範囲・登録証跡・GUID・metadata・対応asset・index・worktreeの変更は停止条件。既存の資格情報拒否、Scene/Prefabの限定空白検査、remote進展、clean worktree、通常push/refetch照合は維持する。ゲート更新前のreceiptは流用せず新しいcheckを行う。

この承認manifestは実際のユーザー承認とEditor原本を主担当／独立reviewが確認するためのローカル記録であり、外部署名や権限付与機構ではない。CLI更新自体もコードレビューと一時repoの回帰検証を済ませてから適用する。

Unity APIのruntime型が汎用`AssetImporter`になる既知形式は、serialized `DefaultImporter`＋実folder＋main type `DefaultAsset`、またはserialized `DefaultImporter`＋実`Assets/StreamingAssets/`配下`.txt`file＋main type `DefaultAsset`の場合だけ照合する。他の汎用Importerやmain type不一致は拒否する。記録をserialized型へ偽装しない。

### 同梱OFL原文の固定バイト照合

`Assets/StreamingAssets/ThirdPartyNotices/DotGothic16-OFL.txt`だけは、公式から取得した原文SHA256 `b6630c61ea078cacd7fabe37d14ffe557a0b45b06683374a9aa9e24262993e33`・承認manifest・全件tested hash・repo/workspaceの完全一致に限り、原本の93行CRLFと21行目末尾のASCII space1個を変更せず扱う。対象path/hashはCLIの固定表に置き、`git diff --check`の各診断を原文行番号/hash/source/理由付きでreceiptへ記録する。一般TXTや別hash、未知の末尾space/tab、改行変換を許す機能ではない。ライセンスや資産を正規化して検査を通さない。

### 既存で未変更のmetadataを検証hashへ含める場合

全件テストのhash一覧に既公開のmetadataが残っていても、今回の公開差分へ追加しない。検証記録だけの別集合`unchangedTestMetadata`に、基準HEAD・indexのblobとSHA256を記録し、modeの一致、およびrepo・検証workspaceの全byte・主張SHA256の一致を検証する。今回stagedのmeta、新規/追跡外、いずれかの変更、不正path、symlink（祖先も含む）はこの証明を使えない。

この集合はstaged metadataの承認には使わない。新規/変更metadataの明示承認、承認範囲とstaged集合の一致、Editor証跡、GUID、空白規則は従来どおり維持する。検証hashを削って通す手順でもない。check・commit前・push前・再fetch後に、同じ基準HEADから同じ証明を再計算する。

複数Spriteの実Editor原本については、承認済TextureImporter・`spriteMode: 2`・一意な`spriteSheet`節の直下にある厳密な4space `spriteID: ` の空scalarだけも記録付きで保持する。Single Sprite・別Importer/別節・子Sprite欄・非空値・tabには適用しない。metadataの全byte/SHA/Editor登録/明示承認の検証は維持する。
