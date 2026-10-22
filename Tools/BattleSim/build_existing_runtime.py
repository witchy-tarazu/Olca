#!/usr/bin/env python3
"""SDKがない既存環境向け。明示されたMicrosoft .NET/Roslynだけで同じCoreソースをコンパイルする。"""
import argparse
import json
from pathlib import Path
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--runtime-root', required=True, type=Path)
p.add_argument('--compiler-root', required=True, type=Path)
p.add_argument('--output-root', required=True, type=Path)
p.add_argument('--forbidden-root', required=True, type=Path)
a = p.parse_args()
root = a.output_root.absolute()
for path in [root, *root.parents]:
    if path.is_symlink():
        raise SystemExit('output symlink refused')
root = root.resolve()
forbidden = a.forbidden_root.resolve()
if root == forbidden or forbidden in root.parents or root in forbidden.parents:
    raise SystemExit('default settings area must be disjoint from dedicated output root')
repo = Path(__file__).resolve().parents[2]
if root == repo or root in repo.parents or repo / 'Assets' in root.parents:
    raise SystemExit('source area is not a dedicated output root')
runtime = a.runtime_root.resolve()
compiler = a.compiler_root.resolve()
frameworks = list((runtime / 'shared' / 'Microsoft.NETCore.App').glob('*'))
if len(frameworks) != 1:
    raise SystemExit('exactly one explicit framework version is required')
framework = frameworks[0]
refs = sorted(framework.glob('*.dll'))
if not refs or any(x.name.startswith('Unity') for x in refs):
    raise SystemExit('framework contains Unity references or is empty')
sources = sorted((repo / 'Assets/Olca/Core').rglob('*.cs')) + [Path(__file__).with_name('Program.cs')]
output = root / 'bin'
output.mkdir(parents=True, exist_ok=True)
assembly = output / 'Olca.BattleSim.dll'
# 引数ファイルはコンパイラ用。シェル展開やUnityのdll/スタブを挿入しない。
def quote(path):
    return '"' + str(path).replace('"', '\\"') + '"'
arguments = ['-nostdlib+', '-target:exe', '-langversion:9', '-define:OLCA_PORTABLE', '-out:' + quote(assembly)]
arguments += ['-r:' + quote(x) for x in refs]
arguments += [quote(x) for x in sources]
rsp = root / 'compile.rsp'
rsp.write_text('\n'.join(arguments) + '\n')
subprocess.run([str(runtime / 'dotnet'), str(compiler / 'csc.dll'), '-noconfig', '@' + str(rsp)], check=True)
(assembly.with_suffix('.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions': {'tfm': 'net6.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': framework.name}}}, indent=2) + '\n')
(root / 'compile-manifest.json').write_text(json.dumps({'sources': [str(x.relative_to(repo)) for x in sources], 'references': [x.name for x in refs], 'unityReferences': [], 'compiler': str(compiler), 'runtime': str(runtime)}, indent=2) + '\n')
print(str(assembly))
