#!/usr/bin/env python3
"""Write a GUID-preserving unitypackage without launching Unity, then verify every archived payload.
The result is an unverified import candidate, not a Unity-exported or play-tested build.
"""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def add(tar, name, data):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = 0o644
    info.mtime = 0
    tar.addfile(info, io.BytesIO(data))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    project = repo / 'UnityProject'
    root = project / 'Assets/SevernyOkrug'
    args.output.mkdir(parents=True, exist_ok=True)
    package = args.output / 'SevernyOkrug_v14_preview.unitypackage'
    entries = []
    seen = set()
    metas = sorted(root.rglob('*.meta')) + [root.with_suffix('.meta')]
    for meta in metas:
        asset = meta.with_suffix('')
        if not asset.exists():
            raise ValueError('Orphan metadata: ' + str(meta))
        match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(), re.M)
        if not match or match[1] in seen:
            raise ValueError('Missing/duplicate GUID: ' + str(meta))
        guid = match[1]
        seen.add(guid)
        entries.append({'path': asset.relative_to(project).as_posix(), 'guid': guid,
                        'folder': asset.is_dir(), 'metadataSHA256': digest(meta.read_bytes()),
                        'assetSHA256': None if asset.is_dir() else digest(asset.read_bytes())})
    for path in root.rglob('*'):
        if path.is_file() and path.suffix != '.meta' and not path.with_suffix(path.suffix + '.meta').is_file():
            raise ValueError('Asset has no metadata: ' + str(path))
    with package.open('wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as compressed, tarfile.open(fileobj=compressed, mode='w|') as tar:
        for entry in entries:
            asset = project / entry['path']
            add(tar, entry['guid'] + '/pathname', entry['path'].encode())
            add(tar, entry['guid'] + '/asset.meta', asset.with_suffix(asset.suffix + '.meta').read_bytes())
            if not entry['folder']:
                add(tar, entry['guid'] + '/asset', asset.read_bytes())
    # Read the complete archive back. No filesystem extraction, Unity import, or packaged script execution.
    expected = {e['guid']: e for e in entries}
    verified = set()
    with tarfile.open(package, 'r:gz') as tar:
        for member in tar:
            if not member.isfile() or member.name in verified:
                raise ValueError('Unexpected or duplicate archive member: ' + member.name)
            verified.add(member.name)
            guid, kind = member.name.split('/')
            entry = expected[guid]
            payload = tar.extractfile(member).read()
            if kind == 'pathname':
                assert payload.decode() == entry['path']
            elif kind == 'asset.meta':
                assert digest(payload) == entry['metadataSHA256']
            elif kind == 'asset':
                assert not entry['folder'] and digest(payload) == entry['assetSHA256']
            else:
                raise ValueError(kind)
    assert len(verified) == sum(2 if e['folder'] else 3 for e in entries)
    # Project settings and UPM manifest cannot be supplied by an ordinary Assets unitypackage.
    archive = args.output / 'SevernyOkrug_v14_project_preview.zip'
    project_files = sorted(p for folder in ['Assets', 'Packages', 'ProjectSettings'] for p in (project / folder).rglob('*') if p.is_file())
    project_files.append(repo / 'README_v14.md')
    zip_entries = []
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path in project_files:
            name = 'SevernyOkrug_v14/' + (path.name if path == repo / 'README_v14.md' else path.relative_to(project).as_posix())
            payload = path.read_bytes()
            info = zipfile.ZipInfo(name)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            z.writestr(info, payload)
            zip_entries.append({'path': name, 'sha256': digest(payload)})
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        for entry in zip_entries:
            assert digest(z.read(entry['path'])) == entry['sha256']
    manifest = {'generatedBy': 'CodeRabbit', 'taskId': 'c8db922a-648a-4ff5-aba3-51866a894377',
                'notice': __doc__, 'unityVersion': '6000.6.7f1', 'assets': entries, 'projectFiles': zip_entries,
                'artifacts': [{'file': p.name, 'bytes': p.stat().st_size, 'sha256': digest(p.read_bytes())} for p in [package, archive]],
                'checks': {'unitypackagePayloadRoundTrip': True, 'projectZIPCRCAndPayloads': True,
                           'unityImport': 'not run: editor license unavailable', 'runtime': 'not run'}}
    (args.output / 'v14-manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False))
    (args.output / 'SHA256SUMS.txt').write_text('# Generated by CodeRabbit; v14 preview artifacts.\n' + ''.join(e['sha256'] + '  ' + e['file'] + '\n' for e in manifest['artifacts']))
    print(json.dumps({'assets': len(entries), 'verifiedMembers': len(verified), 'projectFiles': len(zip_entries),
                      'artifacts': manifest['artifacts'], 'notice': manifest['notice']}, indent=2))


if __name__ == '__main__':
    main()
