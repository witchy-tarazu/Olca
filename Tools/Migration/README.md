# 環境移行チェックポイント（2026-10-23）

ゲームコードは `a7a616c61851b089c769fe271cdcade1e231b4fc` 時点。フォント復元済み、新UI素材・背景・配置を維持。名称／アイコンの次工程は調査のみで未実装。

## Unityと設定

現在の原本と検証コピーの `ProjectVersion.txt` は **6000.3.26f1 / de8167e7440c**。READMEに残っていた6000.0.60f1は古い記載であり、この移行では使用しない。Editor、CLI、ライセンス、認証状態はリポジトリに含めない。

`ProjectSettings/` は既存のignore方針を維持し、復元用のZIPをこのディレクトリで追跡する。

- `ProjectSettings-baseline.zip`: 保護原本の27ファイル。新しい通常コピーの復元はこちらを基本にする。
- `ProjectSettings-validation.zip`: 検証済みコピーの27ファイル。baselineとの差はTimeManagerのEditor自動シリアライズ形式変換のみ。旧 `Fixed Timestep: 0.02` が整数／rate表現になっている。今回の移行作業で時間設定は変更していない。
- `manifest.json`: ZIPと各ファイル、確認画像のSHA256・サイズ。

両ZIPのProjectSettings.assetからUnity CloudのプロジェクトID・組織IDを除き、cloudEnabledを0にした。これは移行成果物だけへの処置であり、既存環境の設定は変更していない。Cloud連携・認証・ライセンスを移植するものではない。ゲームに必要なその他の設定は元の値を保持している。

## 新しい通常コピーへの復元

Unityを起動する前の新しいチェックアウトで実行する。既存の原本・検証コピーには実行しない。ProjectSettingsが既にあれば停止し、上書きしない。以下はbaselineを復元する例。

```bash
python3 - <<'PY'
from pathlib import Path, PurePosixPath
import hashlib, json, zipfile
base = Path('Tools/Migration')
name = 'ProjectSettings-baseline.zip'
expected = json.loads((base / 'manifest.json').read_text())['archives'][name]
archive = base / name
assert hashlib.sha256(archive.read_bytes()).hexdigest() == expected['sha256']
assert not Path('ProjectSettings').exists(), '既存設定へは上書きしません'
with zipfile.ZipFile(archive) as z:
    files = {entry['path']: entry for entry in expected['files']}
    assert sorted(z.namelist()) == sorted(files)
    contents = []
    for name, entry in files.items():
        path = PurePosixPath(name)
        assert not path.is_absolute() and '..' not in path.parts
        assert path.parts[0] == 'ProjectSettings'
        data = z.read(name)
        assert len(data) == entry['bytes']
        assert hashlib.sha256(data).hexdigest() == entry['sha256']
        contents.append((Path(name), data))
    for path, data in contents:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('xb') as stream:
            stream.write(data)
print('ProjectSettings 27件を検証・復元しました')
PY
```

必要な検証コピーは通常コピーで作り、Editorに既存.metaを生成し直させない。検証用設定・セーブは新しい専用ルートを指定し、Play後・Controller有効化前・domain reload後の既存保存先ガードを必ず成立させる。共通設定や個人セーブへフォールバックしない。

## 含めないもの・保全の限界

- 自動import差分3件（UniversalRP.asset、UniversalRenderPipelineGlobalSettings.asset、packages-lock.json）は今回も除外。ソースの設定や依存を自動解決結果で置き換えない。
- Unityのユーザー設定、認証情報、ライセンス、Xauthority、proxy／CA、個人セーブ、旧検証セーブ、Library／キャッシュ、ローカルログはコミットしない。
- 元設定の非公開コピーとimport差分は旧環境の `/workspace/.olca-cloud/results/migration-checkpoint-20261023/` に保全した。これは環境削除後に残る保証のある外部バックアップではない。旧セーブや検証証跡が必要なら、環境を破棄する前に別途許可された非公開の保存方法を決める必要がある。
- このチェックポイントでは環境削除・再作成、Editor／VM停止、ライセンス返却を実施しない。

## 検証

ZIPのCRC・全27件のサイズ／SHA256・復元後のbytes一致、2画像の元ファイルとのbytes一致を検証。C#・Scene・Prefab・素材の変更はなく、全EditModeを再実行する区切りではない。直近ゲームコードのUnity MCP結果は2538/2538成功（フォント復元時）。新環境のEditor起動・import・Unity MCP検証は別途必要。
