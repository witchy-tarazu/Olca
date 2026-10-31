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


def safe_path(name):
    p = PurePosixPath(name)
    require(not p.is_absolute() and '..' not in p.parts and not any(ord(c) < 32 for c in name),
            'unsafe path: ' + name)
    low = [x.lower() for x in p.parts]
    require(not any(x in ('.aws', '.ssh', 'credentials', 'secrets', '__pycache__') or
                    x.startswith('.env') or x in ('id_rsa', 'id_ed25519', '.git-credentials',
                                                 'credentials.json', 'secrets.json', 'service-account.json',
                                                 'settings.local.json') or
                    x.endswith(('.meta', '.pem', '.p12', '.pfx', '.key')) for x in low),
            'staging forbidden: ' + name)


def regular_bytes(root, name):
    path = Path(root) / name
    require(path.is_file() and not path.is_symlink() and
            Path(root).resolve() in path.resolve().parents, 'missing/unsafe file: ' + str(path))
    return path.read_bytes()


def evidence_facts(directory, workspace):
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
            safe_path(path)
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


def whitespace_exceptions(repo, base):
    raw = git(repo, 'diff', '--cached', '--check', base, allowed=(0, 2)).decode('utf-8')
    lines = raw.splitlines()
    exceptions = []
    i = 0
    while i < len(lines):
        match = re.fullmatch(r'(Assets/[^\r\n]+\.(?:prefab|unity)):(\d+): trailing whitespace\.', lines[i])
        require(match is not None and i + 1 < len(lines), 'diff --check rejected: ' + raw)
        path, number = match.group(1), int(match.group(2))
        data = git(repo, 'show', ':' + path).decode('utf-8')
        content = data.splitlines()
        require(data.startswith('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n') and
                0 < number <= len(content) and lines[i + 1] == '+' + content[number - 1] and
                re.fullmatch(r'  m_(?:Name|EditorClassIdentifier): +', content[number - 1]) is not None,
                'non-approved whitespace: ' + lines[i])
        exceptions.append({'path': path, 'line': number, 'text': content[number - 1],
                           'reason': 'Unity YAML empty m_Name/m_EditorClassIdentifier scalar; tested bytes preserved'})
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


def snapshot(repo, workspace, evidence, base):
    require(value(repo, 'symbolic-ref', '--short', 'HEAD') == 'master', 'only master is supported')
    require(not git(repo, 'diff', '--name-only'), 'unstaged tracked changes')
    require(not git(repo, 'ls-files', '-u'), 'unmerged index')
    require(not (Path(value(repo, 'rev-parse', '--absolute-git-dir')) / 'MERGE_HEAD').exists(), 'merge in progress')
    files = git(repo, 'diff', '--cached', '--name-only', '-z', base).decode().split('\0')[:-1]
    require(files, 'nothing staged relative to receipt base')
    attrs = git(repo, 'check-attr', '--cached', '-z', 'whitespace', '--', *files).decode().split('\0')[:-1]
    require(all(attrs[i] == 'unspecified' for i in range(2, len(attrs), 3)),
            'custom whitespace attributes may suppress checks; explicit review required')
    facts = evidence_facts(evidence, workspace)
    for path, digest in facts['testedHashes'].items():
        require(sha(regular_bytes(repo, path)) == digest, 'tested submission/workspace bytes differ: ' + path)
    staged = {}
    for path in files:
        safe_path(path)
        mode = value(repo, 'ls-files', '-s', '--', path).split(' ', 1)[0]
        require(mode in ('100644', '100755'), 'deletion/symlink/submodule not supported: ' + path)
        blob = git(repo, 'show', ':' + path)
        require(blob == regular_bytes(repo, path), 'index/worktree bytes differ: ' + path)
        staged[path] = sha(blob)
        if path.startswith('Assets/'):
            require(facts['testedHashes'].get(path) == sha(blob), 'staged asset absent from tested hashes: ' + path)
            require(blob == regular_bytes(workspace, path), 'index/Unity workspace bytes differ: ' + path)
    return {'tree': value(repo, 'write-tree'), 'stagedHashes': staged,
            'evidence': facts, 'whitespaceExceptions': whitespace_exceptions(repo, base),
            'existingUnityEmptyScalars': existing_unity_scalars(repo)}


def receipt_store(repo):
    return Path(value(repo, 'rev-parse', '--absolute-git-dir')) / 'olca-verified-publish'


def check(repo, workspace, evidence, receipt):
    repo = Path(repo).resolve()
    destination = Path(receipt).resolve()
    require(repo not in destination.parents or receipt_store(repo) in destination.parents,
            'receipt must be outside the worktree or under .git/olca-verified-publish')
    base = value(repo, 'rev-parse', 'HEAD')
    result = snapshot(repo, workspace, evidence, base)
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
    current = snapshot(repo, r['workspace'], r['evidenceDirectory'], r['baseHead'])
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
    c.add_argument('--receipt', required=True)
    c = commands.add_parser('commit')
    c.add_argument('--receipt', required=True)
    c.add_argument('--message-file', required=True)
    c = commands.add_parser('push')
    c.add_argument('--receipt', required=True)
    a = parser.parse_args()
    try:
        if a.command == 'check':
            result = check(a.repo, a.workspace, a.evidence_dir, a.receipt)
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
