import ftplib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import monsterasp_ftps as deploy


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


if __name__ == '__main__':
    unittest.main()
