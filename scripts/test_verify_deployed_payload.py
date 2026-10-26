import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
from verify_deployed_payload import verify_bundle,verify_http_assets

class ReadOnlyFtp:
    def __init__(self,files):self.files=files;self.reads=[]
    def voidcmd(self,command):
        if command!='TYPE I':raise AssertionError('unexpected FTP mutation')
    def retrbinary(self,command,callback,blocksize=8192):
        path=command.removeprefix('RETR ');self.reads.append(path);callback(self.files[path])

class PayloadVerificationTests(unittest.TestCase):
    def test_exact_public_payload_is_hashed_without_private_reads(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot').mkdir();(root/'wwwroot/app.js').write_bytes(b'public');(root/'Payroll.Api.dll').write_bytes(b'binary')
            ftp=ReadOnlyFtp({'wwwroot/app.js':b'public','Payroll.Api.dll':b'binary'});proof=verify_bundle(ftp,root)
            self.assertEqual(proof['files'],2);self.assertEqual(proof['bytes'],12);self.assertEqual(set(ftp.reads),{'wwwroot/app.js','Payroll.Api.dll'})
    def test_same_size_corrupt_public_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'app.js').write_bytes(b'right');ftp=ReadOnlyFtp({'app.js':b'wrong'})
            with self.assertRaises(RuntimeError):verify_bundle(ftp,root)
    def test_protected_content_is_rejected_before_any_transfer(self):
        for protected in ['App_Data/payroll.db','appsettings.Local.json','App_Data/keys/key.xml']:
            with self.subTest(path=protected),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);path=root/protected;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'private');ftp=ReadOnlyFtp({})
                with self.assertRaises(RuntimeError):verify_bundle(ftp,root)
                self.assertEqual(ftp.reads,[])

class HttpsAssetTests(unittest.TestCase):
    def test_compressed_sidecars_are_verified_by_ftp_and_only_original_is_requested_over_https(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot').mkdir()
            for name,data in [('app.js',b'original'),('app.js.br',b'brotli'),('app.js.gz',b'gzip')]:
                (root/'wwwroot'/name).write_bytes(data)
            ftp=ReadOnlyFtp({'wwwroot/'+p.name:p.read_bytes() for p in (root/'wwwroot').iterdir()})
            self.assertEqual(verify_bundle(ftp,root)['files'],3)
            with patch('verify_deployed_payload.urllib.request.urlopen',return_value=io.BytesIO(b'original')) as opened:
                self.assertEqual(verify_http_assets(root),['/app.js']);self.assertEqual(opened.call_count,1)
    def test_an_actual_asset_named_gz_without_an_original_is_not_silently_ignored(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot').mkdir();(root/'wwwroot/download.gz').write_bytes(b'archive')
            with patch('verify_deployed_payload.urllib.request.urlopen',return_value=io.BytesIO(b'archive')):
                self.assertEqual(verify_http_assets(root),['/download.gz'])
    def test_http_404_identifies_asset_and_still_fails_verification(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot').mkdir();(root/'wwwroot/app.js').write_bytes(b'expected')
            with patch('verify_deployed_payload.urllib.request.urlopen',side_effect=urllib.error.HTTPError('https://example/app.js',404,'Not Found',{},None)):
                with self.assertRaisesRegex(RuntimeError,'HTTPS asset request failed: /app.js'):
                    verify_http_assets(root)
    def test_http_hash_mismatch_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot').mkdir();(root/'wwwroot/app.js').write_bytes(b'expected')
            with patch('verify_deployed_payload.urllib.request.urlopen',return_value=io.BytesIO(b'corrupt')):
                with self.assertRaisesRegex(RuntimeError,'HTTPS asset mismatch: /app.js'):verify_http_assets(root)

if __name__=='__main__':unittest.main()
