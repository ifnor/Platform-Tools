"""Validate the full native build matrix before publishing a release."""
import hashlib
from pathlib import Path, PurePosixPath
import sys
import tarfile
import zipfile


def verify(version, directory):
    root = Path(directory)
    expected = set()
    for rid in ('win-x64', 'win-arm64'):
        expected.add(f'PlatformTools-{version}-{rid}-portable.zip')
    expected.add(f'PlatformTools-{version}-win-x64-Setup.exe')
    for rid in ('linux-x64', 'linux-arm64'):
        for suffix in ('-portable.tar.gz', '.deb', '.AppImage'):
            expected.add(f'PlatformTools-{version}-{rid}{suffix}')
    for rid in ('osx-x64', 'osx-arm64'):
        for suffix in ('-portable.tar.gz', '.dmg'):
            expected.add(f'PlatformTools-{version}-{rid}{suffix}')
    actual = {p.name for p in root.iterdir() if p.name != 'SHA256SUMS.txt'}
    if actual != expected:
        raise ValueError(f'Missing: {expected - actual}; unexpected: {actual - expected}')
    checksums = []
    for name in sorted(expected):
        path = root / name
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f'Empty or invalid package: {name}')
        entries = None
        if name.endswith('.zip'):
            with zipfile.ZipFile(path) as archive:
                if archive.testzip():
                    raise ValueError(f'Corrupt archive: {name}')
                entries = archive.namelist()
        elif name.endswith('.tar.gz'):
            with tarfile.open(path) as archive:
                entries = archive.getnames()
        if entries is not None:
            for entry in entries:
                parts = PurePosixPath(entry.replace('\\', '/')).parts
                if any(p.lower() in ('config', '.cloudflared', '..') for p in parts) or entry.lower().endswith('.pem'):
                    raise ValueError(f'Runtime data in {name}: {entry}')
            normalized = {e.replace('\\', '/').removeprefix('./') for e in entries}
            exe = 'PlatformTools.exe' if name.endswith('.zip') else 'PlatformTools'
            cloudflared = 'tools/cloudflared.exe' if name.endswith('.zip') else 'tools/cloudflared'
            if not {exe, cloudflared} <= normalized:
                raise ValueError(f'Missing executable in {name}')
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        checksums.append(f'{digest}  {name}\n')
    (root / 'SHA256SUMS.txt').write_text(''.join(checksums), encoding='utf-8')
    print(f'Validated {len(expected)} packages; wrote SHA256SUMS.txt')


if __name__ == '__main__':
    verify(sys.argv[1], sys.argv[2])
