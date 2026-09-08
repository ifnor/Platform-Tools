import importlib.util
from io import BytesIO
from pathlib import Path
import tempfile
import tarfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('release', Path(__file__).with_name('verify-release.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)

class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'):
            prefix = f'PlatformTools-1.2.3-{rid}'
            if rid.startswith('win'):
                with zipfile.ZipFile(self.root / (prefix + '-portable.zip'), 'w') as z:
                    z.writestr('PlatformTools.exe', b'app')
                    z.writestr('tools/cloudflared.exe', b'tool')
            else:
                with tarfile.open(self.root / (prefix + '-portable.tar.gz'), 'w:gz') as t:
                    for name in ('./PlatformTools', './tools/cloudflared'):
                        info = tarfile.TarInfo(name); info.size = 3
                        t.addfile(info, BytesIO(b'app'))
                for suffix in (('.deb', '.AppImage') if rid.startswith('linux') else ('.dmg',)):
                    (self.root / (prefix + suffix)).write_bytes(b'fixture')
        (self.root / 'PlatformTools-1.2.3-win-x64-Setup.exe').write_bytes(b'fixture')
    def tearDown(self):
        self.temporary.cleanup()
    def test_complete_matrix_writes_checksums(self):
        release.verify('1.2.3', self.root)
        self.assertEqual(13, len((self.root / 'SHA256SUMS.txt').read_text().splitlines()))
    def test_missing_platform_blocks_publication(self):
        (self.root / 'PlatformTools-1.2.3-osx-arm64.dmg').unlink()
        with self.assertRaises(ValueError): release.verify('1.2.3', self.root)
    def test_credentials_block_publication(self):
        with zipfile.ZipFile(self.root / 'PlatformTools-1.2.3-win-x64-portable.zip', 'a') as z:
            z.writestr('config/.cloudflared/cert.pem', 'test credential')
        with self.assertRaises(ValueError): release.verify('1.2.3', self.root)
    def test_missing_bundled_tool_blocks_publication(self):
        with zipfile.ZipFile(self.root / 'PlatformTools-1.2.3-win-x64-portable.zip', 'w') as z:
            z.writestr('PlatformTools.exe', 'app')
        with self.assertRaises(ValueError): release.verify('1.2.3', self.root)

if __name__ == '__main__': unittest.main()
