"""一時ローカルrepoだけで公開ゲートの停止条件を検証する。"""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('verified_publish', Path(__file__).with_name('verified_publish.py'))
vp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vp)


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.repo = self.root / 'repo'
        self.workspace = self.root / 'project'
        self.evidence = self.root / 'evidence'
        for p in (self.repo, self.workspace, self.evidence):
            p.mkdir()
        self.remote = self.root / 'remote.git'
        self.run_git(self.root, 'init', '--bare', str(self.remote))
        self.run_git(self.repo, 'init', '-b', 'master')
        self.run_git(self.repo, 'config', 'user.name', 'Temporary Test')
        self.run_git(self.repo, 'config', 'user.email', 'test@example.invalid')
        (self.repo / 'base.txt').write_text('base\n')
        self.run_git(self.repo, 'add', 'base.txt')
        self.run_git(self.repo, 'commit', '-m', 'base')
        self.base = vp.value(self.repo, 'rev-parse', 'HEAD')
        self.run_git(self.repo, 'remote', 'add', 'origin', str(self.remote))
        self.run_git(self.repo, 'push', 'origin', 'master')
        self.receipt = self.root / 'receipt.json'
        self.message = self.root / 'message.txt'
        self.message.write_text('test: isolated publish\n')
        self.prepare('Assets/test.cs', 'valid\n')

    def run_git(self, repo, *args):
        subprocess.run(['git', '-C', str(repo), *args], check=True, capture_output=True)

    def prepare(self, path, text):
        for root in (self.repo, self.workspace):
            p = root / path
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text)
        self.run_git(self.repo, 'add', path)
        hashes = {path: vp.sha(text.encode())}
        s = {'total': 1, 'passed': 1, 'failed': 0, 'skipped': 0, 'inconclusive': 0}
        test = {'summary': s, 'groundTruth': {'compilationFailed': False, 'compiling': False, 'consoleErrors': 0},
                'consoleCounts': {'error': 0}, 'sourceHashes': hashes}
        cleanup = {'originalClean': True, 'storageGuard': True, 'defaultStoreAbsent': True,
                   'differences': [], 'consoleErrors': 0,
                   'devices': {'screen': [640, 360], 'temp': str(self.root / 'save/tmp')},
                   'actualRender': {'screen': [640, 360], 'temp': str(self.root / 'save/tmp'),
                                    'textures': [{'width': 640, 'height': 360}]}}
        review = {'reviewer': 'independent', 'staticReview': {'unresolvedConcreteFindings': 0},
                  'visualReview': {'unresolvedRequiredFindings': 0}}
        for name, data in [('full-tests', test), ('related-tests', test), ('final-source-hashes', hashes),
                           ('cleanup', cleanup), ('independent-review', review)]:
            (self.evidence / (name + '.json')).write_text(json.dumps(data))

    def check(self):
        return vp.check(self.repo, self.workspace, self.evidence, self.receipt)

    def assert_stops(self, action):
        calls = []
        original = vp.git
        def observed(repo, *args, **kwargs):
            if args[0] in ('commit', 'push', 'fetch'):
                calls.append(args[0])
            return original(repo, *args, **kwargs)
        before = vp.value(self.repo, 'rev-parse', 'HEAD')
        remote_before = vp.value(self.remote, 'rev-parse', 'master')
        with patch.object(vp, 'git', observed), self.assertRaises(vp.Rejected):
            action()
        self.assertEqual(calls, [])
        self.assertEqual(vp.value(self.repo, 'rev-parse', 'HEAD'), before)
        self.assertEqual(vp.value(self.remote, 'rev-parse', 'master'), remote_before)

    def test_unchanged_unity_block_has_exact_base_line_proof(self):
        prefix = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
        removed = '--- !u!1 &1\nGameObject:\n  m_Name: Removed\n'
        kept = '--- !u!21 &2\nMaterial:\n  m_LockedProperties: \n'
        proof = vp.unchanged_unity_block(prefix + removed + kept, prefix + kept, 5)
        self.assertIsNotNone(proof)
        self.assertEqual(proof['unityBlockHeader'], '--- !u!21 &2')
        self.assertEqual(proof['unityBlockSha256'], vp.sha(kept.encode()))
        self.assertEqual(proof['baseLine'], 8)

    def test_unity_block_proof_rejects_changed_new_or_duplicate_block(self):
        prefix = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
        kept = '--- !u!21 &2\nMaterial:\n  m_LockedProperties: \n'
        before = prefix + kept
        for after in (prefix + kept.replace('Properties: ', 'Properties: x '),
                      prefix + kept.replace('&2', '&3'),
                      before + kept, prefix + kept + '  extra: 1\n', before.replace('Material:\n', 'Material:\r\n')):
            self.assertIsNone(vp.unchanged_unity_block(before, after, 5))
        stripped = prefix + '--- !u!114 &2 stripped\nMonoBehaviour: {}\n' + kept
        self.assertIsNone(vp.unchanged_unity_block(stripped, stripped, 7))
        self.assertIsNone(vp.unchanged_unity_block('', before, 5))
        self.assertIsNone(vp.unchanged_unity_block(before, before, 1))

    def existing_test_metadata(self):
        path = 'Assets/old.asset.meta'
        text = 'fileFormatVersion: 2\nguid: ' + 'a' * 32 + '\nDefaultImporter: {}\n'
        self.run_git(self.repo, 'rm', '--cached', 'Assets/test.cs')
        for root in (self.repo, self.workspace):
            (root / path).write_text(text)
        self.run_git(self.repo, 'add', path)
        self.run_git(self.repo, 'commit', '-m', 'previously published metadata')
        self.base = vp.value(self.repo, 'rev-parse', 'HEAD')
        self.run_git(self.repo, 'push', 'origin', 'master')
        self.run_git(self.repo, 'add', 'Assets/test.cs')
        self.add_test_hash(path, vp.sha(text.encode()))
        return path

    def add_test_hash(self, path, digest):
        for name in ('full-tests', 'related-tests', 'final-source-hashes'):
            file = self.evidence / (name + '.json')
            data = json.loads(file.read_text())
            (data if name == 'final-source-hashes' else data['sourceHashes'])[path] = digest
            file.write_text(json.dumps(data))

    def test_preexisting_test_metadata_retained_with_proof_through_publish(self):
        path = self.existing_test_metadata()
        receipt = self.check()
        self.assertNotIn(path, receipt['stagedHashes'])
        self.assertEqual(receipt['unchangedTestMetadata'][path]['baseHead'], self.base)
        self.assertEqual(receipt['unchangedTestMetadata'][path]['sha256'], vp.sha((self.repo / path).read_bytes()))
        head = vp.commit(self.repo, self.receipt, self.message)
        self.assertEqual(vp.push(self.repo, self.receipt), head)

    def test_preexisting_test_metadata_workspace_change_stops_commit(self):
        path = self.existing_test_metadata()
        self.check()
        (self.workspace / path).write_text('changed\n')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_staged_preexisting_metadata_still_needs_approval(self):
        path = self.existing_test_metadata()
        for root in (self.repo, self.workspace):
            (root / path).write_text('changed\n')
        self.run_git(self.repo, 'add', path)
        self.add_test_hash(path, vp.sha(b'changed\n'))
        self.assert_stops(self.check)

    def test_new_unstaged_test_metadata_cannot_claim_preexisting_proof(self):
        path = 'Assets/new.asset.meta'
        for root in (self.repo, self.workspace):
            (root / path).write_text('new\n')
        self.add_test_hash(path, vp.sha(b'new\n'))
        self.assert_stops(self.check)

    def test_preexisting_metadata_claimed_hash_must_match_base(self):
        path = self.existing_test_metadata()
        self.add_test_hash(path, '0' * 64)
        self.assert_stops(self.check)

    def test_preexisting_metadata_index_change_cannot_be_hidden(self):
        path = self.existing_test_metadata()
        (self.repo / path).write_text('staged different\n')
        self.run_git(self.repo, 'add', path)
        (self.repo / path).write_bytes((self.workspace / path).read_bytes())
        self.assert_stops(lambda: vp.unchanged_test_metadata(self.repo, self.workspace, self.evidence, self.base, [], {}))

    def test_preexisting_metadata_repo_bytes_and_symlinks_rejected(self):
        path = self.existing_test_metadata()
        target = self.root / 'copy.meta'
        target.write_bytes((self.repo / path).read_bytes())
        for root in (self.repo, self.workspace):
            with self.subTest(root=root):
                original = (root / path).read_bytes()
                (root / path).unlink()
                (root / path).symlink_to(target)
                self.assert_stops(lambda: vp.unchanged_test_metadata(self.repo, self.workspace, self.evidence, self.base, [], {}))
                (root / path).unlink()
                (root / path).write_bytes(original)
        (self.repo / path).write_text('changed without staging\n')
        self.assert_stops(lambda: vp.unchanged_test_metadata(self.repo, self.workspace, self.evidence, self.base, [], {}))

    def test_preexisting_metadata_ancestor_symlink_rejected(self):
        self.existing_test_metadata()
        for root in (self.repo, self.workspace):
            with self.subTest(root=root):
                assets = root / 'Assets'
                saved = root / 'actual-assets'
                assets.rename(saved)
                assets.symlink_to(saved, target_is_directory=True)
                try:
                    self.assert_stops(lambda: vp.unchanged_test_metadata(self.repo, self.workspace, self.evidence, self.base, [], {}))
                finally:
                    assets.unlink()
                    saved.rename(assets)

    def test_unsafe_test_metadata_path_stays_rejected(self):
        self.add_test_hash('Assets/../.env.meta', '0' * 64)
        self.assert_stops(self.check)

    def test_success_check_commit_push_refetch(self):
        self.check()
        head = vp.commit(self.repo, self.receipt, self.message)
        self.assertEqual(vp.push(self.repo, self.receipt), head)
        self.assertEqual(vp.value(self.remote, 'rev-parse', 'master'), head)

    def test_check_failure_does_not_issue_receipt_or_mutate(self):
        self.prepare('Assets/test.cs', 'invalid trailing  \n')
        self.assert_stops(self.check)
        self.assertFalse(self.receipt.exists())

    def test_local_whitespace_config_cannot_disable_check(self):
        self.run_git(self.repo, 'config', 'core.whitespace', '-blank-at-eol,-blank-at-eof,-space-before-tab')
        self.prepare('Assets/test.cs', 'bad trailing  \n')
        self.assert_stops(self.check)

    def test_whitespace_attribute_cannot_disable_check(self):
        (self.repo / '.gitattributes').write_text('*.cs -whitespace\n')
        self.run_git(self.repo, 'add', '.gitattributes')
        self.assert_stops(self.check)

    def test_modified_receipt_prevents_commit_and_push(self):
        self.check()
        self.receipt.write_text(self.receipt.read_text() + ' ')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))
        self.assert_stops(lambda: vp.push(self.repo, self.receipt))

    def test_changed_tree_prevents_commit(self):
        self.check()
        (self.repo / 'extra.txt').write_text('extra\n')
        self.run_git(self.repo, 'add', 'extra.txt')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_changed_tree_after_commit_prevents_push(self):
        self.check()
        vp.commit(self.repo, self.receipt, self.message)
        (self.repo / 'extra.txt').write_text('extra\n')
        self.run_git(self.repo, 'add', 'extra.txt')
        self.assert_stops(lambda: vp.push(self.repo, self.receipt))

    def test_evidence_change_prevents_commit(self):
        self.check()
        p = self.evidence / 'cleanup.json'
        d = json.loads(p.read_text())
        d['storageGuard'] = False
        p.write_text(json.dumps(d))
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_workspace_changed_prevents_commit(self):
        self.check()
        (self.workspace / 'Assets/test.cs').write_text('changed\n')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_staged_asset_without_test_hash_rejected(self):
        (self.repo / 'Assets/extra.cs').write_text('extra\n')
        self.run_git(self.repo, 'add', 'Assets/extra.cs')
        self.assert_stops(self.check)

    def test_meta_and_env_rejected(self):
        for name in ('Assets/a.meta', '.env', 'credentials/token.txt'):
            with self.subTest(name=name):
                p = self.repo / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text('not secret\n')
                self.run_git(self.repo, 'add', name)
                self.assert_stops(self.check)
                self.run_git(self.repo, 'rm', '--cached', name)
                p.unlink()

    def test_unity_exact_empty_scalars_allowed_and_recorded(self):
        self.run_git(self.repo, 'rm', '--cached', 'Assets/test.cs')
        (self.repo / 'Assets/test.cs').unlink()
        text = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &1\n  m_Name: \n  m_EditorClassIdentifier: \n'
        self.prepare('Assets/test.prefab', text)
        receipt = self.check()
        self.assertEqual([e['line'] for e in receipt['whitespaceExceptions']], [4, 5])
        vp.commit(self.repo, self.receipt, self.message)
        vp.push(self.repo, self.receipt)
        self.assertEqual((self.repo / 'Assets/test.prefab').read_text(), text)

    def test_regex_rejects_nonempty_tabs_wrong_header_and_other_fields(self):
        self.run_git(self.repo, 'rm', '--cached', 'Assets/test.cs')
        (self.repo / 'Assets/test.cs').unlink()
        header = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
        for text in (header + '  m_Name: value \n', header + '  m_Name:\t\n',
                     '  m_Name: \n', header + '  m_Other: \n', header + '  m_Name: \n\n'):
            with self.subTest(text=text):
                self.prepare('Assets/test.prefab', text)
                self.assert_stops(self.check)

    def test_existing_system_prefab_exceptions_are_recorded(self):
        path = 'Assets/Resources/OlcaSystem/EventSystem.prefab'
        p = self.repo / path
        p.parent.mkdir(parents=True)
        p.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n  m_Name: \n')
        self.run_git(self.repo, 'add', path)
        # fixtureの既存baseへ追加し、その後のcheckで変更外の行も記録する。
        self.run_git(self.repo, 'commit', '-m', 'existing system prefab')
        self.base = vp.value(self.repo, 'rev-parse', 'HEAD')
        self.prepare('Assets/test.cs', 'new valid\n')
        result = self.check()
        self.assertEqual(result['whitespaceExceptions'], [])
        self.assertEqual(result['existingUnityEmptyScalars'][0]['line'], 3)

    def test_failed_tests_and_review_and_cleanup_fail_closed(self):
        for name, key, bad in [('full-tests', 'summary', {'total': 1, 'passed': 0, 'failed': 1, 'skipped': 0, 'inconclusive': 0}),
                               ('related-tests', 'consoleCounts', {'error': 1}),
                               ('cleanup', 'originalClean', False),
                               ('independent-review', 'staticReview', {'unresolvedConcreteFindings': 1})]:
            with self.subTest(name=name):
                self.prepare('Assets/test.cs', 'valid\n')
                p = self.evidence / (name + '.json')
                d = json.loads(p.read_text())
                d[key] = bad
                p.write_text(json.dumps(d))
                self.assert_stops(self.check)

    def test_remote_advance_stops_before_push(self):
        self.check()
        vp.commit(self.repo, self.receipt, self.message)
        other = self.root / 'other'
        self.run_git(self.root, 'clone', str(self.remote), str(other))
        self.run_git(other, 'config', 'user.name', 'Other')
        self.run_git(other, 'config', 'user.email', 'other@example.invalid')
        (other / 'new.txt').write_text('remote\n')
        self.run_git(other, 'add', 'new.txt')
        self.run_git(other, 'commit', '-m', 'remote advanced')
        self.run_git(other, 'push', 'origin', 'master')
        with patch.object(vp, 'git', wraps=vp.git) as observed, self.assertRaises(vp.Rejected):
            vp.push(self.repo, self.receipt)
        self.assertFalse(any(c.args[1] == 'push' for c in observed.call_args_list))


    def metadata_fixture(self, folder=False, text_asset=False, texture=False):
        asset = 'Assets/sprite.png' if texture else 'Assets/Folder' if folder else 'Assets/StreamingAssets/note.txt' if text_asset else 'Assets/test.cs'
        name = asset + '.meta'
        text = 'fileFormatVersion: 2\nguid: ' + 'a' * 32 + '\n'
        if folder:
            text += 'folderAsset: yes\n'
        text += ('TextureImporter' if texture else 'DefaultImporter' if folder or text_asset else 'MonoImporter') + ':\n  userData: \n'
        if texture:
            text += '  spriteMode: 2\n  spriteSheet:\n    sprites: []\n    spriteID: \n'
        for root in (self.repo, self.workspace):
            if texture:(root / asset).write_text('PNG fixture only\n')
            if folder:
                (root / asset).mkdir()
            if text_asset:
                (root / asset).parent.mkdir(parents=True, exist_ok=True)
                (root / asset).write_text('Text fixture\n')
            (root / name).write_text(text)
        self.run_git(self.repo, 'add', name)
        if text_asset or texture:self.run_git(self.repo, 'add', asset)
        for filename in ('full-tests', 'related-tests'):
            path = self.evidence / (filename + '.json')
            d = vp.document(path)
            d['sourceHashes'][name] = vp.sha(text.encode())
            if text_asset or texture:d['sourceHashes'][asset] = vp.sha((self.workspace / asset).read_bytes())
            path.write_text(json.dumps(d))
        (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
        observed = {'assetPath': asset, 'guid': 'a' * 32, 'isFolder': folder,
                    'metaSha256': vp.sha(text.encode()), 'importer': 'TextureImporter' if texture else 'AssetImporter' if folder or text_asset else 'MonoImporter',
                    'mainAssetType': 'Texture2D' if texture else 'DefaultAsset' if folder or text_asset else 'MonoScript'}
        witness = self.root / 'editor-witness.json'
        witness.write_text(json.dumps({'version': 1, 'provenance': 'UnityEditor.AssetDatabase',
                                      'editorVersion': 'test fixture only', 'projectPath': str(self.workspace), 'assets': {name: observed}}))
        files = vp.git(self.repo, 'diff', '--cached', '--name-only', '-z', self.base).decode().split('\0')[:-1]
        approval = {'version': 1, 'approvalSource': 'isolated test fixture, not real approval',
                    'approvedFiles': {f: vp.sha((self.repo / f).read_bytes()) for f in files},
                    'metadata': {name: {'assetPath': asset, 'guid': 'a' * 32, 'isFolder': folder,
                                        'sha256': vp.sha(text.encode())}},
                    'editorEvidence': {'path': str(witness), 'sha256': vp.sha(witness.read_bytes())}}
        self.metadata_approval = self.root / 'metadata-approval.json'
        self.metadata_approval.write_text(json.dumps(approval))
        return name, witness

    def metadata_check(self):
        return vp.check(self.repo, self.workspace, self.evidence, self.receipt, self.metadata_approval)

    def test_explicit_editor_metadata_check_commit_push_revalidated(self):
        name, _ = self.metadata_fixture()
        result = self.metadata_check()
        self.assertIn(name, result['metadataApproval']['metadata'])
        head = vp.commit(self.repo, self.receipt, self.message)
        self.assertEqual(vp.push(self.repo, self.receipt), head)

    def test_explicit_editor_folder_metadata_allowed(self):
        self.metadata_fixture(folder=True)
        self.metadata_check()

    def test_metadata_even_with_proof_requires_explicit_argument(self):
        self.metadata_fixture()
        self.assert_stops(self.check)

    def test_metadata_changed_after_check_stops(self):
        name, _ = self.metadata_fixture()
        self.metadata_check()
        for root in (self.repo, self.workspace):
            (root / name).write_text((root / name).read_text() + 'extra: 1\n')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_metadata_approval_change_after_check_stops(self):
        self.metadata_fixture()
        self.metadata_check()
        self.metadata_approval.write_text(self.metadata_approval.read_text() + ' ')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_editor_registration_evidence_tamper_stops(self):
        _, witness = self.metadata_fixture()
        self.metadata_check()
        witness.write_text(witness.read_text() + ' ')
        self.assert_stops(lambda: vp.commit(self.repo, self.receipt, self.message))

    def test_duplicate_guid_in_other_asset_stops(self):
        self.metadata_fixture()
        (self.workspace / 'Assets/other.meta').write_text('fileFormatVersion: 2\nguid: ' + 'A' * 32 + '\n')
        self.assert_stops(self.metadata_check)

    def test_missing_paired_asset_stops(self):
        self.metadata_fixture()
        (self.workspace / 'Assets/test.cs').unlink()
        self.assert_stops(self.metadata_check)

    def test_editor_importer_mismatch_stops(self):
        _, witness = self.metadata_fixture()
        d = vp.document(witness)
        next(iter(d['assets'].values()))['importer'] = 'TextureImporter'
        witness.write_text(json.dumps(d))
        approval = vp.document(self.metadata_approval)
        approval['editorEvidence']['sha256'] = vp.sha(witness.read_bytes())
        self.metadata_approval.write_text(json.dumps(approval))
        self.assert_stops(self.metadata_check)

    def test_metadata_path_does_not_bypass_credentials_guard(self):
        self.metadata_fixture()
        d = vp.document(self.metadata_approval)
        d['approvedFiles']['.env.meta'] = '0' * 64
        d['metadata']['.env.meta'] = next(iter(d['metadata'].values()))
        self.metadata_approval.write_text(json.dumps(d))
        self.assert_stops(self.metadata_check)

    def test_staged_file_outside_explicit_scope_stops(self):
        self.metadata_fixture()
        (self.repo / 'extra.md').write_text('not approved\n')
        self.run_git(self.repo, 'add', 'extra.md')
        self.assert_stops(self.metadata_check)

    def test_approved_metadata_not_in_tested_hashes_stops(self):
        name, _ = self.metadata_fixture()
        for file in ('full-tests', 'related-tests'):
            path = self.evidence / (file + '.json')
            d = vp.document(path)
            del d['sourceHashes'][name]
            path.write_text(json.dumps(d))
        (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
        self.assert_stops(self.metadata_check)



    def test_partial_staged_scope_rejected(self):
        self.metadata_fixture()
        self.run_git(self.repo, 'reset', '--', 'Assets/test.cs')
        self.assert_stops(self.metadata_check)

    def test_minimal_script_meta_requires_editor_importer_proof(self):
        name, witness = self.metadata_fixture()
        text = 'fileFormatVersion: 2\nguid: ' + 'a' * 32
        for root in (self.repo, self.workspace):
            (root / name).write_text(text)
        self.run_git(self.repo, 'add', name)
        approval = vp.document(self.metadata_approval)
        approval['metadata'][name]['sha256'] = approval['approvedFiles'][name] = vp.sha(text.encode())
        observed = vp.document(witness)
        observed['assets'][name]['metaSha256'] = vp.sha(text.encode())
        witness.write_text(json.dumps(observed))
        approval['editorEvidence']['sha256'] = vp.sha(witness.read_bytes())
        self.metadata_approval.write_text(json.dumps(approval))
        for filename in ('full-tests', 'related-tests'):
            path = self.evidence / (filename + '.json')
            d = vp.document(path);d['sourceHashes'][name] = vp.sha(text.encode());path.write_text(json.dumps(d))
        (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
        self.metadata_check()

    def test_editor_multiple_sprite_root_empty_id_preserved_and_published(self):
        name, _ = self.metadata_fixture(texture=True)
        result = self.metadata_check()
        rows = [x for x in result['whitespaceExceptions'] if x['path'] == name and x['text'] == '    spriteID: ']
        self.assertEqual(len(rows), 1)
        self.assertIn('Multiple Sprite sheet root', rows[0]['reason'])
        head = vp.commit(self.repo, self.receipt, self.message)
        self.assertEqual(vp.push(self.repo, self.receipt), head)

    def test_sheet_id_exception_rejects_wrong_context_or_nonempty_values(self):
        name, witness = self.metadata_fixture(texture=True)
        original = (self.repo / name).read_text()
        cases = [original.replace('spriteMode: 2', 'spriteMode: 1'),
                 original.replace('  spriteMode: 2', '  spriteMode: 2\n  spriteMode: 1'),
                 original.replace('  spriteMode: 2', '  spriteMode: 2\n  spriteMode: 2'),
                 original + '  spriteSheet: duplicate\n',
                 original.replace('  spriteSheet:', '  otherSection:'),
                 original.replace('    spriteID: ', 'OtherSection:\n    spriteID: '),
                 original.replace('    spriteID: ', '  other_section:\n    spriteID: '),
                 original.replace('    spriteID: ', '   unexpectedIndent:\n    spriteID: '),
                 original.replace('    spriteID: ', '      spriteID: '),
                 original.replace('    spriteID: ', '    spriteID: nonempty '),
                 original.replace('    spriteID: ', '    spriteID: \t'),
                 original.replace('TextureImporter:', 'MonoImporter:')]
        for text in cases:
            with self.subTest(text=text):
                for root in (self.repo, self.workspace):(root / name).write_text(text)
                self.run_git(self.repo, 'add', name)
                a = vp.document(self.metadata_approval)
                a['metadata'][name]['sha256'] = a['approvedFiles'][name] = vp.sha(text.encode())
                w = vp.document(witness);w['assets'][name]['metaSha256'] = vp.sha(text.encode())
                w['assets'][name]['importer'] = 'MonoImporter' if 'MonoImporter:' in text else 'TextureImporter'
                witness.write_text(json.dumps(w));a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes())
                self.metadata_approval.write_text(json.dumps(a))
                self.add_test_hash(name, vp.sha(text.encode()))
                self.assert_stops(self.metadata_check)

    def test_metadata_nonempty_unknown_or_tab_whitespace_rejected(self):
        name, witness = self.metadata_fixture()
        for ending in ('  userData: nonempty ', '  unknownField: ', '  userData: \t'):
            with self.subTest(ending=ending):
                text = 'fileFormatVersion: 2\nguid: ' + 'a' * 32 + '\nMonoImporter:\n' + ending + '\n'
                for root in (self.repo, self.workspace):(root / name).write_text(text)
                self.run_git(self.repo, 'add', name)
                a = vp.document(self.metadata_approval);a['metadata'][name]['sha256'] = a['approvedFiles'][name] = vp.sha(text.encode())
                w = vp.document(witness);w['assets'][name]['metaSha256'] = vp.sha(text.encode());witness.write_text(json.dumps(w));a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes());self.metadata_approval.write_text(json.dumps(a))
                for filename in ('full-tests', 'related-tests'):
                    path = self.evidence / (filename + '.json');d = vp.document(path);d['sourceHashes'][name] = vp.sha(text.encode());path.write_text(json.dumps(d))
                (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
                self.assert_stops(self.metadata_check)



    def test_other_editor_project_witness_rejected(self):
        _, witness = self.metadata_fixture()
        d = vp.document(witness);d['projectPath'] = str(self.root / 'other-project');witness.write_text(json.dumps(d))
        a = vp.document(self.metadata_approval);a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes());self.metadata_approval.write_text(json.dumps(a))
        self.assert_stops(self.metadata_check)



    def test_registered_text_importer_generic_runtime_mapping_allowed(self):
        self.metadata_fixture(text_asset=True)
        self.metadata_check()

    def test_generic_runtime_importer_requires_known_main_type(self):
        _, witness = self.metadata_fixture(folder=True)
        d = vp.document(witness);next(iter(d['assets'].values()))['mainAssetType'] = 'TextAsset';witness.write_text(json.dumps(d))
        a = vp.document(self.metadata_approval);a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes());self.metadata_approval.write_text(json.dumps(a))
        self.assert_stops(self.metadata_check)



    def test_streaming_text_generic_importer_rejects_unproven_main_type(self):
        _, witness = self.metadata_fixture(text_asset=True)
        d = vp.document(witness);next(iter(d['assets'].values()))['mainAssetType'] = 'TextAsset';witness.write_text(json.dumps(d))
        a = vp.document(self.metadata_approval);a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes());self.metadata_approval.write_text(json.dumps(a))
        self.assert_stops(self.metadata_check)



    def test_unobserved_serialized_textscript_mapping_rejected(self):
        name, witness = self.metadata_fixture(text_asset=True)
        text = (self.repo / name).read_text().replace('DefaultImporter:', 'TextScriptImporter:')
        for root in (self.repo, self.workspace):(root / name).write_text(text)
        self.run_git(self.repo, 'add', name)
        a = vp.document(self.metadata_approval);a['metadata'][name]['sha256'] = a['approvedFiles'][name] = vp.sha(text.encode())
        w = vp.document(witness);w['assets'][name]['metaSha256'] = vp.sha(text.encode());witness.write_text(json.dumps(w));a['editorEvidence']['sha256'] = vp.sha(witness.read_bytes());self.metadata_approval.write_text(json.dumps(a))
        for filename in ('full-tests', 'related-tests'):
            path = self.evidence / (filename + '.json');d = vp.document(path);d['sourceHashes'][name] = vp.sha(text.encode());path.write_text(json.dumps(d))
        (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
        self.assert_stops(self.metadata_check)



    def license_fixture(self, content='License fixture\r\nSecond line\r\n', allowed=None):
        self.metadata_fixture()
        name = 'Assets/StreamingAssets/ThirdPartyNotices/DotGothic16-OFL.txt'
        for root in (self.repo, self.workspace):
            path = root / name;path.parent.mkdir(parents=True, exist_ok=True);path.write_bytes(content.encode())
        self.run_git(self.repo, 'add', name)
        digest = vp.sha(content.encode())
        a = vp.document(self.metadata_approval);a['approvedFiles'][name] = digest;self.metadata_approval.write_text(json.dumps(a))
        for filename in ('full-tests', 'related-tests'):
            path = self.evidence / (filename + '.json');d = vp.document(path);d['sourceHashes'][name] = digest;path.write_text(json.dumps(d))
        (self.evidence / 'final-source-hashes.json').write_text(json.dumps(vp.document(self.evidence / 'full-tests.json')['sourceHashes']))
        return {name: {'sha256': digest, 'source': 'isolated fixture only', 'trailingAsciiSpaceLines': allowed or {}}}

    def test_pinned_verbatim_crlf_license_allowed_and_recorded(self):
        originals = self.license_fixture()
        with patch.object(vp, 'VERBATIM_LICENSES', originals):
            result = self.metadata_check()
        self.assertTrue(any('Pinned unmodified OFL' in row['reason'] for row in result['whitespaceExceptions']))

    def test_pinned_original_ascii_space_line_preserved(self):
        originals = self.license_fixture('License fixture\r\nOriginal space \r\n', {2: 'Original space '})
        with patch.object(vp, 'VERBATIM_LICENSES', originals):
            self.metadata_check()

    def test_unpinned_or_changed_license_still_rejected(self):
        self.license_fixture()
        self.assert_stops(self.metadata_check)

    def test_crlf_whitespace_does_not_allow_arbitrary_space_or_tab(self):
        originals = self.license_fixture('License fixture \r\nSecond line\r\n')
        with patch.object(vp, 'VERBATIM_LICENSES', originals):
            self.assert_stops(self.metadata_check)

    def test_verbatim_pin_does_not_accept_other_line_endings(self):
        originals = self.license_fixture('License fixture\nSecond line \n', {2: 'Second line '})
        with patch.object(vp, 'VERBATIM_LICENSES', originals):
            self.assert_stops(self.metadata_check)


if __name__ == '__main__':
    unittest.main()
