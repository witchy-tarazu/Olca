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

## 既存検証コピーのビルド前後の保全（2026-11-07 Stage100）

上の全27件復元スクリプトは新規コピー専用のままとする。既存コピーの復旧へ流用しない。

- ビルド前は `ProjectSettings/` と変更され得る資産の**実体とSHA256**を、リポジトリ外の非公開先へ控える。ハッシュだけでは復元元にならない。個人設定・Cloud情報・セーブを含む控えを、そのままGitへ追加しない。
- ビルド後は元プロジェクト・検証コピー・Git管理ファイルを別々に照合する。Unityの自動再保存も無変更とは扱わず、変更後の実体と差分を保全する。Stage99では検証コピーの `ProjectSettings.asset` と `Assets/DefaultVolumeProfile.asset` にシリアライズ差分が生じた。
- 正確な事前実体がある場合に限り、確認済みの対象ファイルだけを検証コピーへ戻す。設定全体を古いZIPで上書きしたり、未記録の既定値を推測したりしない。復元後はファイルのSHA256とEditorの読取値を確認し、確認中にSave/SetDirtyで再保存しない。
- Stage100では両ZIPの `ProjectSettings/ProjectSettings.asset`（22,168 bytes、SHA256 `347eed0afb4c2bed3fe816626b00690c60e48e10e02e9ca04a38d0bfdf0800d2`）がStage99の事前記録と完全一致したため、この**1ファイルだけ**を復元できた。この一致は当該時点の証拠であり、今後のコピーにも無条件で同じZIPを使えるという意味ではない。
- 復元後のidleと読取APIで一致しても、将来の明示保存・ビルドでUnityが再正規化しない保証にはしない。Player成果物、変更後設定、復元結果を区別して記録し、必要な成果物1組と代表画像・小さな証跡に絞る。

## 含めないもの・保全の限界

- 自動import差分3件（UniversalRP.asset、UniversalRenderPipelineGlobalSettings.asset、packages-lock.json）は今回も除外。ソースの設定や依存を自動解決結果で置き換えない。
- Unityのユーザー設定、認証情報、ライセンス、Xauthority、proxy／CA、個人セーブ、旧検証セーブ、Library／キャッシュ、ローカルログはコミットしない。
- 元設定の非公開コピーとimport差分は旧環境の `/workspace/.olca-cloud/results/migration-checkpoint-20261023/` に保全した。これは環境削除後に残る保証のある外部バックアップではない。旧セーブや検証証跡が必要なら、環境を破棄する前に別途許可された非公開の保存方法を決める必要がある。
- このチェックポイントでは環境削除・再作成、Editor／VM停止、ライセンス返却を実施しない。

## 検証

ZIPのCRC・全27件のサイズ／SHA256・復元後のbytes一致、2画像の元ファイルとのbytes一致を検証。C#・Scene・Prefab・素材の変更はなく、全EditModeを再実行する区切りではない。直近ゲームコードのUnity MCP結果は2538/2538成功（フォント復元時）。新環境のEditor起動・import・Unity MCP検証は別途必要。
