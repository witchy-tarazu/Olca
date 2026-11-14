#!/usr/bin/env python3
"""Olca専用、証跡付きの段階別公開。標準ライブラリのみ使用する。"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


class Rejected(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise Rejected(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(repo, *args, allowed=(0,)):
    p = subprocess.run(['git', '-c', 'core.quotePath=false',
                        '-c', 'core.whitespace=blank-at-eol,blank-at-eof,space-before-tab',
                        '-C', str(repo), *args],
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    require(p.returncode in allowed, 'git ' + ' '.join(args) + ': ' + p.stderr.decode(errors='replace'))
    return p.stdout


def value(repo, *args):
    return git(repo, *args).decode().strip()


def document(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def safe_path(name, approved_metadata=()):
    p = PurePosixPath(name)
    require(not p.is_absolute() and '..' not in p.parts and not any(ord(c) < 32 for c in name),
            'unsafe path: ' + name)
    low = [x.lower() for x in p.parts]
    require(not any(x in ('.aws', '.ssh', 'credentials', 'secrets', '__pycache__') or
                    x.startswith('.env') or x in ('id_rsa', 'id_ed25519', '.git-credentials',
                                                 'credentials.json', 'secrets.json', 'service-account.json',
                                                 'settings.local.json') or
                    x.endswith(('.pem', '.p12', '.pfx', '.key')) for x in low),
            'staging forbidden: ' + name)
    if any(x.endswith('.meta') for x in low):
        require(name.startswith('Assets/') and p.suffix == '.meta' and
                not any(x.endswith('.meta') for x in low[:-1]) and name in approved_metadata,
                'metadata not explicitly approved: ' + name)


def regular_bytes(root, name):
    path = Path(root) / name
    require(path.is_file() and not path.is_symlink() and
            Path(root).resolve() in path.resolve().parents, 'missing/unsafe file: ' + str(path))
    return path.read_bytes()


def metadata_facts(approval, repo, workspace):
    """明示承認とEditorのread-only登録証跡が揃うmetaだけを許可する。"""
    if approval is None:
        return None
    path = Path(approval).resolve()
    require(path.is_file() and not Path(approval).is_symlink(), 'metadata approval missing/unsafe')
    d = document(path)
    require(d['version'] == 1 and isinstance(d['approvalSource'], str) and d['approvalSource'].strip(),
            'metadata approval source required')
    entries, scope = d['metadata'], d['approvedFiles']
    require(isinstance(entries, dict) and bool(entries) and isinstance(scope, dict) and bool(scope),
            'metadata approval scope required')
    for name, digest in scope.items():
        safe_path(name, entries)
        require(re.fullmatch('[0-9a-f]{64}', digest) and sha(regular_bytes(repo, name)) == digest,
                'approved file bytes changed: ' + name)
    witness = d['editorEvidence']
    witness_path = Path(witness['path']).resolve()
    require(witness_path.is_file() and not Path(witness['path']).is_symlink() and
            sha(witness_path.read_bytes()) == witness['sha256'], 'Editor evidence changed/unsafe')
    editor = document(witness_path)
    require(editor['version'] == 1 and editor['provenance'] == 'UnityEditor.AssetDatabase' and
            isinstance(editor['editorVersion'], str) and editor['editorVersion'] and
            Path(editor['projectPath']).resolve() == Path(workspace).resolve(), 'Editor provenance/project required')
    # 他assetの同GUIDも拒否する。承認ファイルのbyteを変換・正規化しない。
    guid_maps = []
    for root in (repo, workspace):
        found = {}
        for meta in (Path(root) / 'Assets').rglob('*.meta'):
            require(not meta.is_symlink(), 'symlink metadata unsupported')
            for guid in re.findall(r'^guid: ([0-9a-fA-F]{32})$', meta.read_text(encoding='utf-8'), re.M):
                found.setdefault(guid.lower(), []).append(meta.relative_to(root).as_posix())
        guid_maps.append(found)
    checked = {}
    for name, entry in entries.items():
        safe_path(name, entries)
        require(name.endswith('.meta') and name.startswith('Assets/'), 'invalid metadata path')
        content = regular_bytes(repo, name)
        require(content == regular_bytes(workspace, name) and
                sha(content) == entry['sha256'] == scope.get(name), 'metadata bytes not approved/test workspace exact: ' + name)
        text = content.decode('utf-8')
        guids = re.findall(r'^guid: ([0-9a-f]{32})$', text, re.M)
        importers = re.findall(r'^([A-Za-z][A-Za-z0-9]*Importer): *$', text, re.M)
        require(text.startswith('fileFormatVersion: 2\n') and len(guids) == 1 and
                guids[0] == entry['guid'], 'invalid Unity metadata: ' + name)
        asset = name[:-5]
        safe_path(asset)
        minimal_script = asset.endswith('.cs') and text in (
            'fileFormatVersion: 2\nguid: ' + guids[0], 'fileFormatVersion: 2\nguid: ' + guids[0] + '\n')
        require(len(importers) == 1 or minimal_script, 'metadata importer missing: ' + name)
        importer = importers[0] if importers else 'MonoImporter'
        folder = entry['isFolder']
        require(type(folder) is bool and entry['assetPath'] == asset and
                ('folderAsset: yes' in text.splitlines()) == folder, 'metadata paired kind mismatch: ' + name)
        for root in (repo, workspace):
            paired = Path(root) / asset
            require(not paired.is_symlink() and (paired.is_dir() if folder else paired.is_file()),
                    'metadata paired asset missing/unsafe: ' + name)
        if not folder:
            require(regular_bytes(repo, asset) == regular_bytes(workspace, asset),
                    'metadata paired asset workspace differs: ' + name)
        require(all(g.get(guids[0]) == [name] for g in guid_maps), 'metadata GUID duplicate/missing: ' + name)
        observed = editor['assets'].get(name)
        require(observed is not None and observed['guid'] == guids[0] and observed['assetPath'] == asset and
                observed['metaSha256'] == entry['sha256'] and observed['isFolder'] is folder,
                'Editor registration proof mismatch: ' + name)
        runtime_importer = observed['importer']
        matched = runtime_importer == importer
        if runtime_importer == 'AssetImporter':
            matched = importer == 'DefaultImporter' and observed.get('mainAssetType') == 'DefaultAsset' and (
                folder or (asset.startswith('Assets/StreamingAssets/') and asset.endswith('.txt')))
        require(matched, 'Editor runtime/serialized importer mismatch: ' + name)
        checked[name] = dict(entry, importer=importer, runtimeImporter=runtime_importer,
                             mainAssetType=observed.get('mainAssetType'), minimalScriptFormat=minimal_script)
    return {'path': str(path), 'sha256': sha(path.read_bytes()), 'approvalSource': d['approvalSource'],
            'approvedFiles': scope, 'metadata': checked,
            'editorEvidence': {'path': str(witness_path), 'sha256': witness['sha256']}}


def unchanged_test_metadata(repo, workspace, evidence, base, staged, approved_metadata):
    """公開差分ではない既存metaを、4箇所の同一byte証明付きで検証記録に残す。"""
    proof = {}
    for name in ('full-tests', 'related-tests'):
        test = document(Path(evidence) / (name + '.json'))
        hashes = dict(test.get('productionSourceHashes', {}))
        for path, digest in test['sourceHashes'].items():
            require(path not in hashes or hashes[path] == digest, 'conflicting test hash: ' + path)
            hashes[path] = digest
        for path, digest in hashes.items():
            if not path.endswith('.meta') or path in approved_metadata:
                continue
            # 例外は検証記録だけ。今回追加/変更したmetaの承認を代替しない。
            safe_path(path, {path})
            require(path not in staged, 'staged metadata needs explicit approval: ' + path)
            tree = git(repo, 'ls-tree', '-z', base, '--', path).decode().split('\0')
            index = git(repo, 'ls-files', '--stage', '-z', '--', path).decode().split('\0')
            require(len(tree) == 2 and tree[1] == '' and len(index) == 2 and index[1] == '',
                    'existing test metadata not uniquely tracked: ' + path)
            head_info, head_path = tree[0].split('\t', 1)
            index_info, index_path = index[0].split('\t', 1)
            head_mode, kind, head_blob = head_info.split()
            index_mode, index_blob, stage = index_info.split()
            require(head_path == index_path == path and kind == 'blob' and stage == '0' and
                    head_mode == index_mode and head_mode in ('100644', '100755') and head_blob == index_blob,
                    'existing test metadata HEAD/index differs: ' + path)
            for root in (repo, workspace):
                cursor = Path(root)
                for part in PurePosixPath(path).parts:
                    cursor = cursor / part
                    require(not cursor.is_symlink(), 'symlink test metadata path: ' + path)
            content = git(repo, 'show', base + ':' + path)
            require(content == git(repo, 'show', ':' + path) == regular_bytes(repo, path) ==
                    regular_bytes(workspace, path) and sha(content) == digest,
                    'existing test metadata bytes/hash differ: ' + path)
            require(path not in proof or proof[path]['sha256'] == digest, 'conflicting test metadata hash: ' + path)
            proof[path] = {'sha256': digest, 'baseHead': base, 'blob': head_blob,
                           'proof': 'HEAD/index/repo/workspace byte-identical; not staged'}
    return proof


def evidence_facts(directory, workspace, approved_metadata=()):
    directory = Path(directory).resolve()
    names = ['full-tests', 'related-tests', 'cleanup', 'final-source-hashes', 'independent-review']
    docs = {n: document(directory / (n + '.json')) for n in names}
    hashes = {}
    for n in ('full-tests', 'related-tests'):
        d = docs[n]
        s = d['summary']
        require(type(s['total']) is int and s['total'] > 0 and type(s['passed']) is int and s['passed'] == s['total'] and
                all(type(s[k]) is int and s[k] == 0 for k in ('failed', 'skipped', 'inconclusive')),
                n + ': incomplete/failed tests')
        g = d['groundTruth']
        require(g['compilationFailed'] is False and g['compiling'] is False and
                type(g['consoleErrors']) is int and g['consoleErrors'] == 0 and
                d['consoleCounts']['error'] == 0, n + ': compile/console error')
        combined = dict(d.get('productionSourceHashes', {}))
        for path, digest in d['sourceHashes'].items():
            require(path not in combined or combined[path] == digest, 'conflicting test hash: ' + path)
            combined[path] = digest
        require(combined, n + ': missing tested hashes')
        for path, digest in combined.items():
            safe_path(path, approved_metadata)
            require(path.startswith('Assets/') and re.fullmatch('[0-9a-f]{64}', digest), 'invalid tested hash')
            require(sha(regular_bytes(workspace, path)) == digest, n + ': workspace hash changed: ' + path)
            if n == 'related-tests':
                require(hashes.get(path) == digest, 'related hash absent/different in full tests: ' + path)
            else:
                hashes[path] = digest
    final = docs['final-source-hashes']
    require(final and final == docs['full-tests']['sourceHashes'], 'final/full source hashes differ')
    c = docs['cleanup']
    require(c['originalClean'] is True and c['storageGuard'] is True and
            c['defaultStoreAbsent'] is True and c['differences'] == [] and c['consoleErrors'] == 0 and
            c['devices']['screen'] == [640, 360] and c['actualRender']['screen'] == [640, 360] and
            c['actualRender']['textures'] and
            all(t['width'] == 640 and t['height'] == 360 for t in c['actualRender']['textures']),
            'cleanup not verified')
    # TMPDIRは検証workspaceの兄弟save/tmpに固定。証跡の任意パスを信用しない。
    expected_tmp = (Path(workspace).resolve().parent / 'save' / 'tmp').resolve()
    require(all(Path(c[k]['temp']).resolve() == expected_tmp for k in ('devices', 'actualRender')),
            'cleanup TMPDIR is outside validation save/tmp')
    r = docs['independent-review']
    require(bool(r['reviewer']) and type(r['staticReview']['unresolvedConcreteFindings']) is int and
            r['staticReview']['unresolvedConcreteFindings'] == 0 and
            type(r['visualReview']['unresolvedRequiredFindings']) is int and
            r['visualReview']['unresolvedRequiredFindings'] == 0, 'independent review unresolved')
    return {'files': {n + '.json': sha((directory / (n + '.json')).read_bytes()) for n in names},
            'testedHashes': hashes, 'fullSummary': docs['full-tests']['summary'],
            'relatedSummary': docs['related-tests']['summary']}


def unchanged_unity_block(before, after, number):
    """類似Sceneブロックの差分誤対応でも、既存byteを変更せず証明する。"""
    prefix = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
    if not before.startswith(prefix) or not after.startswith(prefix) or '\r' in before or '\r' in after:
        return None
    def blocks(text):
        rows = text.splitlines(keepends=True)
        # stripped等の未対応headerを無視して一意性を推定しない。
        if any(row.startswith('---') and re.fullmatch(r'--- !u!\d+ &\d+\r?\n?', row) is None for row in rows):
            return None
        starts = [(i, row.rstrip('\r\n')) for i, row in enumerate(rows)
                  if re.fullmatch(r'--- !u!\d+ &\d+\r?\n?', row)]
        ids = [header.split('&')[1] for _, header in starts]
        if len(ids) != len(set(ids)):
            return None
        return [(start + 1, (starts[i + 1][0] if i + 1 < len(starts) else len(rows)),
                 header, ''.join(rows[start:starts[i + 1][0] if i + 1 < len(starts) else len(rows)]))
                for i, (start, header) in enumerate(starts)]
    prior, current = blocks(before), blocks(after)
    if prior is None or current is None:
        return None
    for first, last, header, content in current:
        if first <= number <= last:
            old = next((b for b in prior if b[2] == header), None)
            if old is not None and content == old[3]:
                return {'unityBlockHeader': header, 'unityBlockSha256': sha(content.encode('utf-8')),
                        'baseLine': old[0] + number - first}
    return None


# 同梱ライセンスの検証済み原本。一般TXTの改行/末尾空白を緩和しない。
VERBATIM_LICENSES = {
    'Assets/StreamingAssets/ThirdPartyNotices/DotGothic16-OFL.txt': {
        'sha256': 'b6630c61ea078cacd7fabe37d14ffe557a0b45b06683374a9aa9e24262993e33',
        'source': 'https://github.com/google/fonts/blob/main/ofl/dotgothic16/OFL.txt',
        'trailingAsciiSpaceLines': {21: 'fonts, including any derivative works, can be bundled, embedded, '}
    }
}


def whitespace_exceptions(repo, base, approved_metadata=(), approved_files=None):
    raw = git(repo, 'diff', '--cached', '--check', base, allowed=(0, 2)).decode('utf-8')
    lines = raw.splitlines()
    exceptions = []
    i = 0
    while i < len(lines):
        match = re.fullmatch(r'(Assets/[^\r\n]+\.(?:prefab|unity|meta|txt)):(\d+): trailing whitespace\.', lines[i])
        require(match is not None and i + 1 < len(lines), 'diff --check rejected: ' + raw)
        path, number = match.group(1), int(match.group(2))
        data = git(repo, 'show', ':' + path).decode('utf-8')
        content = data.splitlines()
        if path.endswith('.txt'):
            original = VERBATIM_LICENSES.get(path)
            digest = sha(data.encode('utf-8'))
            require(original is not None and approved_files is not None and
                    approved_files.get(path) == digest == original['sha256'],
                    'non-approved text whitespace: ' + lines[i])
            rows = data.splitlines(keepends=True)
            require(rows and all(row.endswith('\r\n') for row in rows) and
                    all(row.count('\r') == 1 and row.count('\n') == 1 for row in rows),
                    'verbatim license is not original CRLF')
            for row_number, row in enumerate(content, 1):
                require(row.rstrip(' \t') == row or original['trailingAsciiSpaceLines'].get(row_number) == row,
                        'unapproved original text trailing space')
            require(0 < number <= len(content) and lines[i + 1] == '+' + content[number - 1],
                    'text whitespace diagnostic differs from index')
            exceptions.append({'path': path, 'line': number, 'text': content[number - 1],
                               'sourceSha256': digest, 'source': original['source'],
                               'reason': 'Pinned unmodified OFL source CRLF; original line21 ASCII space preserved'})
            i += 2
            continue
        if path.endswith('.meta'):
            # 実測したEditor空scalarだけ。非空値/未知field/tab等は止める。
            empty_scalars = {'  userData: ', '  assetBundleName: ', '  assetBundleVariant: ',
                             '      customData: ', '      indices: ', '    customData: ', '    indices: ',
                             '  mipmapLimitGroupName: ', '  customCharacters: '}
            # Multiple Spriteのsheet本体IDはEditorが空で出力する。子Sprite IDには適用しない。
            sections = [j for j, row in enumerate(content) if row.startswith('  spriteSheet:')]
            sheet = sections[0] if len(sections) == 1 and content[sections[0]] == '  spriteSheet:' else -1
            sheet_end = next((j for j in range(sheet + 1, len(content))
                              if content[j].strip() and not content[j].lstrip().startswith('#') and
                              len(content[j]) - len(content[j].lstrip()) < 4), len(content)) if sheet >= 0 else -1
            empty_sheet_id = (isinstance(approved_metadata, dict) and
                              approved_metadata.get(path, {}).get('importer') == 'TextureImporter' and
                              [row for row in content if row.startswith('  spriteMode:')] == ['  spriteMode: 2'] and
                              sheet < number - 1 < sheet_end and
                              0 < number <= len(content) and content[number - 1] == '    spriteID: ')
            require(path in approved_metadata and 0 < number <= len(content) and
                    data.startswith('fileFormatVersion: 2\n') and
                    (content[number - 1] in empty_scalars or empty_sheet_id) and
                    lines[i + 1] == '+' + content[number - 1], 'non-approved metadata whitespace: ' + lines[i])
            exceptions.append({'path': path, 'line': number, 'text': content[number - 1],
                               'reason': ('Explicitly approved Editor Multiple Sprite sheet root empty spriteID; tested bytes preserved'
                                          if empty_sheet_id else 'Explicitly approved Editor importer empty scalar; tested bytes preserved')})
            i += 2
            continue
        require(data.startswith('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n') and
                0 < number <= len(content) and lines[i + 1] == '+' + content[number - 1],
                'non-approved whitespace: ' + lines[i])
        proof = None
        empty_metadata = re.fullmatch(r'  m_(?:Name|EditorClassIdentifier): +', content[number - 1]) is not None
        if not empty_metadata:
            require(content[number - 1] == '  m_LockedProperties: ', 'non-approved whitespace: ' + lines[i])
            prior = git(repo, 'show', base + ':' + path, allowed=(0, 128)).decode('utf-8')
            proof = unchanged_unity_block(prior, data, number)
            require(proof is not None, 'non-approved whitespace: ' + lines[i])
        item = {'path': path, 'line': number, 'text': content[number - 1],
                'reason': 'Unity YAML empty m_Name/m_EditorClassIdentifier scalar; tested bytes preserved' if empty_metadata else
                          'Complete Unity YAML block unchanged byte-for-byte from base with same unique type/fileID; diff alignment only'}
        if proof is not None:
            item.update(proof)
        exceptions.append(item)
        i += 2
    return exceptions


def existing_unity_scalars(repo):
    # 前段で導入済みの6行も、変更行の診断とは分けて根拠を残す。
    result = []
    files = git(repo, 'ls-files', '-z', 'Assets/Resources/OlcaSystem/*.prefab').decode().split('\0')[:-1]
    for path in files:
        text = git(repo, 'show', ':' + path).decode('utf-8')
        require(text.startswith('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'), 'invalid system prefab YAML: ' + path)
        for number, line in enumerate(text.splitlines(), 1):
            if line.rstrip() != line:
                require(re.fullmatch(r'  m_(?:Name|EditorClassIdentifier): +', line) is not None,
                        'non-approved system prefab whitespace: ' + path + ':' + str(number))
                result.append({'path': path, 'line': number, 'text': line,
                               'reason': 'Existing Unity YAML empty scalar; staged bytes preserved'})
    return result


def snapshot(repo, workspace, evidence, base, metadata_approval=None):
    require(value(repo, 'symbolic-ref', '--short', 'HEAD') == 'master', 'only master is supported')
    require(not git(repo, 'diff', '--name-only'), 'unstaged tracked changes')
    require(not git(repo, 'ls-files', '-u'), 'unmerged index')
    require(not (Path(value(repo, 'rev-parse', '--absolute-git-dir')) / 'MERGE_HEAD').exists(), 'merge in progress')
    files = git(repo, 'diff', '--cached', '--name-only', '-z', base).decode().split('\0')[:-1]
    require(files, 'nothing staged relative to receipt base')
    attrs = git(repo, 'check-attr', '--cached', '-z', 'whitespace', '--', *files).decode().split('\0')[:-1]
    require(all(attrs[i] == 'unspecified' for i in range(2, len(attrs), 3)),
            'custom whitespace attributes may suppress checks; explicit review required')
    metadata = metadata_facts(metadata_approval, repo, workspace)
    allowed_meta = metadata['metadata'] if metadata else {}
    if metadata:
        require(set(files) == set(metadata['approvedFiles']), 'staged scope differs from explicit approval')
    unchanged_meta = unchanged_test_metadata(repo, workspace, evidence, base, files, allowed_meta)
    facts = evidence_facts(evidence, workspace, set(allowed_meta) | set(unchanged_meta))
    if metadata:
        require(all(facts['testedHashes'].get(p) == row['sha256'] for p, row in allowed_meta.items()),
                'approved metadata absent from tested hashes')
        require(all(row['isFolder'] or facts['testedHashes'].get(row['assetPath']) ==
                    sha(regular_bytes(workspace, row['assetPath'])) for row in allowed_meta.values()),
                'metadata paired file absent from tested hashes')
    for path, digest in facts['testedHashes'].items():
        require(sha(regular_bytes(repo, path)) == digest, 'tested submission/workspace bytes differ: ' + path)
    staged = {}
    for path in files:
        safe_path(path, allowed_meta)
        mode = value(repo, 'ls-files', '-s', '--', path).split(' ', 1)[0]
        require(mode in ('100644', '100755'), 'deletion/symlink/submodule not supported: ' + path)
        blob = git(repo, 'show', ':' + path)
        require(blob == regular_bytes(repo, path), 'index/worktree bytes differ: ' + path)
        if metadata:
            require(metadata['approvedFiles'].get(path) == sha(blob), 'staged file outside explicit approval: ' + path)
        staged[path] = sha(blob)
        if path.startswith('Assets/'):
            require(facts['testedHashes'].get(path) == sha(blob), 'staged asset absent from tested hashes: ' + path)
            require(blob == regular_bytes(workspace, path), 'index/Unity workspace bytes differ: ' + path)
    return {'tree': value(repo, 'write-tree'), 'stagedHashes': staged, 'metadataApproval': metadata,
            'evidence': facts, 'unchangedTestMetadata': unchanged_meta, 'whitespaceExceptions': whitespace_exceptions(repo, base, allowed_meta, metadata['approvedFiles'] if metadata else None),
            'existingUnityEmptyScalars': existing_unity_scalars(repo)}


def receipt_store(repo):
    return Path(value(repo, 'rev-parse', '--absolute-git-dir')) / 'olca-verified-publish'


def check(repo, workspace, evidence, receipt, metadata_approval=None):
    repo = Path(repo).resolve()
    destination = Path(receipt).resolve()
    require(repo not in destination.parents or receipt_store(repo) in destination.parents,
            'receipt must be outside the worktree or under .git/olca-verified-publish')
    base = value(repo, 'rev-parse', 'HEAD')
    result = snapshot(repo, workspace, evidence, base, metadata_approval)
    result.update({'version': 1, 'repo': str(repo), 'workspace': str(Path(workspace).resolve()),
                   'evidenceDirectory': str(Path(evidence).resolve()), 'baseHead': base})
    raw = (json.dumps(result, ensure_ascii=False, sort_keys=True, indent=2) + '\n').encode()
    store = receipt_store(repo)
    store.mkdir(exist_ok=True)
    (store / (sha(raw) + '.json')).write_bytes(raw)
    Path(receipt).write_bytes(raw)
    return result


def approved(repo, receipt, committed=False):
    raw = Path(receipt).read_bytes()
    stored = receipt_store(repo) / (sha(raw) + '.json')
    require(stored.is_file() and stored.read_bytes() == raw, 'receipt modified/not issued by check')
    r = json.loads(raw)
    require(r['version'] == 1 and r['repo'] == str(Path(repo).resolve()), 'receipt belongs to another repo')
    current = snapshot(repo, r['workspace'], r['evidenceDirectory'], r['baseHead'],
                       r.get('metadataApproval', {}).get('path') if r.get('metadataApproval') else None)
    require(all(current[k] == r[k] for k in current), 'tree/evidence/conditions changed since check')
    if committed:
        parts = value(repo, 'rev-list', '--parents', '-n', '1', 'HEAD').split()
        require(parts[1:] == [r['baseHead']] and value(repo, 'rev-parse', 'HEAD^{tree}') == r['tree'],
                'commit tree/parent does not match receipt')
    else:
        require(value(repo, 'rev-parse', 'HEAD') == r['baseHead'], 'HEAD changed since check')
    return r


def commit(repo, receipt, message_file):
    require(Path(message_file).is_file() and Path(message_file).read_text().strip(), 'empty commit message')
    approved(repo, receipt)
    git(repo, 'commit', '--file', str(Path(message_file).resolve()))
    approved(repo, receipt, committed=True)
    return value(repo, 'rev-parse', 'HEAD')


def push(repo, receipt):
    r = approved(repo, receipt, committed=True)
    require(not git(repo, 'status', '--porcelain'), 'push requires a clean worktree (including untracked files)')
    require(value(repo, 'config', '--get-all', 'remote.origin.url') and
            not git(repo, 'config', '--get-all', 'remote.origin.pushurl', allowed=(0, 1)),
            'origin needs one URL and no alternate push URL')
    require(len(value(repo, 'config', '--get-all', 'remote.origin.url').splitlines()) == 1, 'multiple origin URLs')
    git(repo, 'fetch', '--no-tags', 'origin', '+refs/heads/master:refs/remotes/origin/master')
    require(value(repo, 'rev-parse', 'refs/remotes/origin/master') == r['baseHead'], 'remote advanced; rebase/retest/review first')
    # fetch待ちの間にもindex/証跡が変更され得るため、送信直前にもう一度検証する。
    approved(repo, receipt, committed=True)
    require(not git(repo, 'status', '--porcelain'), 'worktree changed during fetch')
    head = value(repo, 'rev-parse', 'HEAD')
    git(repo, 'push', 'origin', head + ':refs/heads/master')
    git(repo, 'fetch', '--no-tags', 'origin', '+refs/heads/master:refs/remotes/origin/master')
    require(value(repo, 'rev-parse', 'refs/remotes/origin/master') == head, 'post-push refetch mismatch; outcome uncertain')
    return head


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default='.')
    commands = parser.add_subparsers(dest='command', required=True)
    c = commands.add_parser('check')
    c.add_argument('--workspace', required=True)
    c.add_argument('--evidence-dir', required=True)
    c.add_argument('--metadata-approval', help='明示承認されたEditor生成metaの厳密manifest。省略時はmeta拒否')
    c.add_argument('--receipt', required=True)
    c = commands.add_parser('commit')
    c.add_argument('--receipt', required=True)
    c.add_argument('--message-file', required=True)
    c = commands.add_parser('push')
    c.add_argument('--receipt', required=True)
    a = parser.parse_args()
    try:
        if a.command == 'check':
            result = check(a.repo, a.workspace, a.evidence_dir, a.receipt, a.metadata_approval)
            print('CHECK OK tree=' + result['tree'])
        elif a.command == 'commit':
            print('COMMIT OK ' + commit(a.repo, a.receipt, a.message_file))
        else:
            print('PUSH VERIFIED ' + push(a.repo, a.receipt))
        return 0
    except (Rejected, OSError, ValueError, KeyError, TypeError) as exc:
        print('STOP: ' + str(exc), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
