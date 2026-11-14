#!/usr/bin/env python3
"""Compile against an installed Unity editor, then run isolated managed logic checks (no Unity license needed).
This does not run the Unity importer, physics, shaders, scenes, disk saves, or LAN.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--editor', required=True, type=Path, help='Path to the installed Unity executable')
    parser.add_argument('--only', choices=['all', 'compile', 'logic'], default='all')
    parser.add_argument('--results', type=Path, help='JSON record outside the checkout')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    root = repo / 'UnityProject'
    data = args.editor.resolve().parent / 'Data'
    compiler = next((data / 'DotNetSdk/sdk').glob('*/Roslyn/bincore/csc.dll'))
    dotnet = data / 'NetCoreRuntime/dotnet'
    cache = data / 'Resources/PackageManager/ProjectTemplates/libcache/com.unity.template.urp-blank-17.2.6/ScriptAssemblies'
    if not cache.is_dir():
        raise SystemExit('Expected bundled URP 17.2.6 template reference cache is missing; use Unity 6000.6.7f1.')
    references = {}
    for folder in [data / 'NetStandard/ref/2.1.0', data / 'NetStandard/compat/2.1.0/shims/netfx',
                   data / 'Managed', data / 'Managed/UnityEngine', cache]:
        for path in sorted(folder.glob('*.dll')):
            if path.name.startswith('Assembly-CSharp') or '.Tests.' in path.name or path.name.endswith('.Tests.dll'):
                continue
            references[path.name] = path
    sources = sorted((root / 'Assets/SevernyOkrug').rglob('*.cs'))
    env = dict(os.environ)
    # Ubuntu 26 ships ICU 78; optional when running on a different distribution.
    env.setdefault('DOTNET_ICU_VERSION_OVERRIDE', '78')
    record = {'notice': __doc__, 'revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip(),
              'sources': {str(p.relative_to(repo)): sha(p) for p in sources},
              'references': {str(p): sha(p) for p in references.values()}, 'checks': []}
    failed = False
    with tempfile.TemporaryDirectory(prefix='severny-v14-checks-') as scratch:
        work = Path(scratch)

        def compile_to(name, files, refs, defines=(), executable=False):
            nonlocal failed
            out = work / (name + '.dll')
            rsp = work / (name + '.rsp')
            options = ['-nologo', '-nostdlib+', '-target:' + ('exe' if executable else 'library'),
                       '-langversion:9.0', '-out:"' + str(out) + '"']
            if defines:
                options += ['-define:' + ','.join(defines)]
            options += ['-reference:"' + str(p) + '"' for p in refs]
            options += ['"' + str(p) + '"' for p in files]
            rsp.write_text('\n'.join(options) + '\n')
            command = [str(dotnet), str(compiler), '-noconfig', '@' + str(rsp)]
            result = subprocess.run(command, cwd=repo, env=env, text=True, capture_output=True)
            messages = (result.stdout + result.stderr).splitlines()
            errors = [m for m in messages if 'error ' in m]
            warnings = [m for m in messages if 'warning ' in m]
            record['checks'].append({'name': name, 'command': command, 'response': options, 'cwd': str(repo),
                                     'exit': result.returncode, 'errors': errors, 'warnings': warnings})
            print(f'{name}: exit {result.returncode}, {len(errors)} errors, {len(warnings)} warnings', flush=True)
            if errors:
                print('\n'.join(errors))
            failed |= result.returncode != 0
            return out

        for editor, new_input in ([] if args.only == 'logic' else [(True, True), (False, True), (True, False), (False, False)]):
            name = ('editor' if editor else 'player') + ('-new-input' if new_input else '-legacy-input')
            files = [p for p in sources if editor or 'Editor' not in p.relative_to(root).parts]
            defines = ['UNITY_6000_0_OR_NEWER', 'UNITY_6000_6_OR_NEWER', 'UNITY_STANDALONE', 'UNITY_STANDALONE_LINUX']
            if editor:
                defines += ['UNITY_EDITOR', 'UNITY_EDITOR_LINUX']
            defines += ['ENABLE_INPUT_SYSTEM' if new_input else 'ENABLE_LEGACY_INPUT_MANAGER']
            compile_to(name, files, references.values(), defines)
        if args.only != 'compile':
            test_files = [repo / 'Tools/tests/V14HostDoubles.cs', repo / 'Tools/tests/V14LogicChecks.cs']
            test_files += [root / 'Assets/SevernyOkrug' / p for p in ['Scripts/Items/Inventory.cs', 'Scripts/Items/ItemDatabase.cs',
                           'Scripts/Raid/QuestSystem.cs', 'Scripts/Weapons/PenetrationRules.cs']]
            record['logicInputs'] = {str(p.relative_to(repo)): sha(p) for p in test_files}
            runtime_refs = next((data / 'DotNetSdk/packs/Microsoft.NETCore.App.Ref').glob('*/ref/net8.0'))
            test_dll = compile_to('logic-checks', test_files, sorted(runtime_refs.glob('*.dll')), executable=True)
            test_dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0',
                'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}, 'rollForward': 'LatestPatch'}}))
            if test_dll.exists():
                command = [str(dotnet), str(test_dll)]
                result = subprocess.run(command, cwd=repo, env=env, text=True, capture_output=True)
                print(result.stdout + result.stderr, end='', flush=True)
                record['checks'].append({'name': 'logic-execution', 'command': command, 'cwd': str(repo), 'exit': result.returncode,
                                         'output': result.stdout + result.stderr})
                failed |= result.returncode != 0

    if args.results:
        args.results.parent.mkdir(parents=True, exist_ok=True)
        args.results.write_text(json.dumps(record, indent=2))
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
