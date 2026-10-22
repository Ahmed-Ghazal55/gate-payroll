import ftplib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import monsterasp_ftps as deploy
import ftps_chain as chain
from types import SimpleNamespace


class FakeFtp:
    def __init__(self):
        self.files = {'App_Data/payroll.db': b'private', 'appsettings.Local.json': b'private-settings'}
        self.writes = []

    def retrbinary(self, command, callback):
        name = command.removeprefix('RETR ')
        if name not in self.files:
            raise ftplib.error_perm('550 File not found')
        callback(self.files[name])

    def storbinary(self, command, data):
        name = command.removeprefix('STOR ')
        self.files[name] = data.read()
        self.writes.append(name)

    def delete(self, name):
        del self.files[name]
        self.writes.append(name)

    def mlsd(self, directory='', facts=None):
        if not directory:
            yield 'App_Data', {'type': 'dir'}
            for name, content in self.files.items():
                if '/' not in name:
                    yield name, {'type': 'file', 'size': str(len(content)), 'modify': '20261022000000'}
        else:
            for name, content in self.files.items():
                if name.startswith(directory + '/'):
                    yield name.split('/')[-1], {'type': 'file', 'size': str(len(content)), 'modify': '20261022000000'}


class FtpsLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.state = patch.object(deploy, 'STATE', Path(self.temp.name) / 'state.json')
        self.state.start()
        self.ftp = FakeFtp()

    def tearDown(self):
        self.state.stop()
        self.temp.cleanup()

    def test_success_changes_only_owned_maintenance_marker(self):
        before = self.ftp.files.copy()
        deploy.prepare(self.ftp, wait=lambda _: None)
        deploy.finish(self.ftp)
        self.assertEqual(before, self.ftp.files)
        self.assertEqual(['app_offline.htm', 'app_offline.htm'], self.ftp.writes)

    def test_operator_marker_is_preserved(self):
        self.ftp.files[deploy.MARKER] = b'Manual maintenance'
        with self.assertRaisesRegex(RuntimeError, 'operator'):
            deploy.prepare(self.ftp, wait=lambda _: None)
        self.assertEqual([], self.ftp.writes)

    def test_foreign_marker_is_never_removed(self):
        deploy.prepare(self.ftp, wait=lambda _: None)
        self.ftp.files[deploy.MARKER] = b'Changed by operator'
        with self.assertRaisesRegex(RuntimeError, 'ownership'):
            deploy.finish(self.ftp)
        self.assertIn(deploy.MARKER, self.ftp.files)

    def test_protected_changes_stop_resume(self):
        deploy.prepare(self.ftp, wait=lambda _: None)
        self.ftp.files['App_Data/payroll.db'] += b'changed'
        with self.assertRaisesRegex(RuntimeError, 'Protected runtime'):
            deploy.finish(self.ftp)
        self.assertIn(deploy.MARKER, self.ftp.files)

    def test_missing_credentials_fail_before_network(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(deploy.ftplib, 'FTP_TLS') as factory:
            with self.assertRaisesRegex(RuntimeError, 'MONSTERASP_FTP_PASSWORD'):
                deploy.connect()
            factory.assert_not_called()

    def test_connection_requires_tls_and_existing_root(self):
        env = dict(zip(['MONSTERASP_FTP_SERVER', 'MONSTERASP_FTP_USERNAME', 'MONSTERASP_FTP_PASSWORD'], ['synthetic.invalid', 'synthetic', 'synthetic']))
        with patch.dict(os.environ, env), patch.object(deploy.ftplib, 'FTP_TLS') as factory:
            ftp = factory.return_value
            self.assertIs(deploy.connect(), ftp)
            ftp.prot_p.assert_called_once()
            ftp.cwd.assert_called_once_with('wwwroot')
            self.assertIsNotNone(factory.call_args.kwargs['context'])

    def test_health_retries_until_expected_release(self):
        responses = [io.BytesIO(json.dumps(d).encode()) for d in [{'ok': True, 'version': '0.5.1'}, {'ok': True, 'version': '0.5.2'}]]
        with patch.object(deploy.urllib.request, 'urlopen', side_effect=responses) as request:
            deploy.health(wait=lambda _: None)
            self.assertEqual(2, request.call_count)


class IntermediateChainTests(unittest.TestCase):
    def test_issuer_url_allowlist(self):
        self.assertTrue(chain.allowed_url('http://cacerts.digicert.com/issuer.crt'))
        for url in ['file:///etc/passwd','http://169.254.169.254/latest/','https://digicert.com.attacker.invalid/a','https://user:password@crt.sectigo.com/a']:
            self.assertFalse(chain.allowed_url(url))

    def test_omitted_intermediate_must_verify_with_roots_and_hostname(self):
        leaf=b'-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----'
        intermediate=b'-----BEGIN CERTIFICATE-----\nissuer\n-----END CERTIFICATE-----'
        responses=[SimpleNamespace(returncode=0,stdout=leaf),SimpleNamespace(returncode=1,stdout=b''),SimpleNamespace(returncode=0,stdout=b'CA Issuers - URI:http://crt.sectigo.com/issuer.crt'),SimpleNamespace(returncode=0,stdout=b'OK')]
        with patch.object(chain,'run',side_effect=responses) as openssl,patch.object(chain,'download_ca',return_value=intermediate):
            self.assertEqual(intermediate,chain.verified_intermediates('synthetic.invalid'))
            verification=openssl.call_args_list[-1].args[0]
            self.assertIn('-untrusted',verification)
            self.assertEqual('synthetic.invalid',verification[verification.index('-verify_hostname')+1])
            self.assertEqual('sslserver',verification[verification.index('-purpose')+1])
            self.assertTrue('-CAfile' in verification or '-CApath' in verification)

    def test_untrusted_chain_never_returns_certificates(self):
        leaf=b'-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----'
        responses=[SimpleNamespace(returncode=0,stdout=leaf),SimpleNamespace(returncode=1,stdout=b''),SimpleNamespace(returncode=0,stdout=b'')]
        with patch.object(chain,'run',side_effect=responses),patch.object(chain,'download_ca') as download:
            with self.assertRaisesRegex(RuntimeError,'cannot be validated'):
                chain.verified_intermediates('synthetic.invalid')
            download.assert_not_called()


if __name__ == '__main__':
    unittest.main()
