from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import package_web
import verify_deployed_payload as verify

class NextPackagingTests(unittest.TestCase):
    def test_absent_source_skips_but_present_source_requires_a_built_entry(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            self.assertFalse(package_web.package(root))
            (root/'web').mkdir();(root/'web/package.json').write_text('{}')
            with self.assertRaisesRegex(RuntimeError,'index.html'):package_web.package(root)

    def test_export_and_http_verification_include_app_entry_and_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'web/out/_next').mkdir(parents=True)
            (root/'web/package.json').write_text('{}');(root/'web/out/index.html').write_text('<html>synthetic</html>')
            (root/'web/out/_next/test.js').write_text('synthetic')
            self.assertTrue(package_web.package(root))
            bundle=root/'Payroll.Api'
            self.assertTrue((bundle/'wwwroot/app/_next/test.js').is_file())
            import io
            def response(request,timeout):
                relative=request.full_url.split('https://synthetic.invalid/',1)[1].split('?',1)[0]
                return io.BytesIO((bundle/'wwwroot'/relative).read_bytes())
            with patch.object(verify.urllib.request,'urlopen',side_effect=response):
                routes=verify.verify_http_assets(bundle,'https://synthetic.invalid')
            self.assertIn('/app/index.html',routes);self.assertIn('/app/_next/test.js',routes)

    def test_partial_app_payload_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'wwwroot/app').mkdir(parents=True)
            with self.assertRaisesRegex(RuntimeError,'/app/index.html'):verify.required_app_entry(root)
