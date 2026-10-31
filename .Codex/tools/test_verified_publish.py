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


if __name__ == '__main__':
    unittest.main()
