import io
from pathlib import Path
import tempfile
import unittest
from verify_deployed_payload import verify_bundle

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

if __name__=='__main__':unittest.main()
