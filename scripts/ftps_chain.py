"""Recover omitted public CA intermediates, validating against existing system roots."""
import re
import ssl
import subprocess
import tempfile
from pathlib import Path
import urllib.parse
import urllib.request

CA_DOMAINS = ('digicert.com', 'sectigo.com', 'comodoca.com', 'usertrust.com', 'trust-provider.com', 'globalsign.com', 'letsencrypt.org', 'pki.goog', 'zerossl.com', 'entrust.net', 'certum.pl', 'amazontrust.com', 'microsoft.com', 'ssl.com')


def allowed_url(url):
    parsed = urllib.parse.urlsplit(url)
    host = (parsed.hostname or '').lower()
    return parsed.scheme in {'http', 'https'} and not parsed.username and not parsed.password and any(host == d or host.endswith('.' + d) for d in CA_DOMAINS)


class CaRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not allowed_url(newurl):
            raise RuntimeError('CA download redirected outside the permitted issuer domains.')
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def run(args, data=b''):
    return subprocess.run(['openssl', *args], input=data, capture_output=True, timeout=30)


def download_ca(url):
    if not allowed_url(url):
        raise RuntimeError('Issuer URL is outside the permitted public CA domains.')
    with urllib.request.build_opener(CaRedirects()).open(url, timeout=20) as response:
        data = response.read(1024 * 1024 + 1)
    if len(data) > 1024 * 1024:
        raise RuntimeError('Issuer certificate download exceeds the limit.')
    for encoding in ('DER', 'PEM'):
        result = run(['x509', '-inform', encoding, '-outform', 'PEM'], data)
        if result.returncode == 0:
            return result.stdout
    raise RuntimeError('Issuer response was not an X.509 certificate.')


def verified_intermediates(host):
    # This connection reads public certificates only; no FTP credentials are sent.
    result = run(['s_client', '-starttls', 'ftp', '-connect', host + ':21', '-servername', host, '-showcerts'])
    certificates = re.findall(rb'-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----', result.stdout, re.S)
    if not certificates:
        raise RuntimeError('FTPS public certificate could not be inspected.')
    with tempfile.TemporaryDirectory() as directory:
        leaf = Path(directory) / 'leaf.pem'
        chain = Path(directory) / 'chain.pem'
        leaf.write_bytes(certificates[0])
        current = certificates[0]
        intermediates = list(certificates[1:])
        seen = set()
        for _ in range(6):
            args = ['verify', '-purpose', 'sslserver', '-verify_hostname', host]
            paths = ssl.get_default_verify_paths()
            if paths.cafile:
                args += ['-CAfile', paths.cafile]
            if paths.capath:
                args += ['-CApath', paths.capath]
            if intermediates:
                chain.write_bytes(b'\n'.join(intermediates))
                args += ['-untrusted', str(chain)]
            if run([*args, str(leaf)]).returncode == 0:
                print('PASS FTPS chain validated with system roots and hostname; omitted intermediates recovered.')
                return b'\n'.join(intermediates)
            aia = run(['x509', '-noout', '-ext', 'authorityInfoAccess'], current)
            urls = re.findall(r'CA Issuers\s*-\s*URI:(\S+)', aia.stdout.decode(errors='replace'))
            candidate = next((u for u in urls if allowed_url(u) and u not in seen), None)
            if candidate is None:
                raise RuntimeError('FTPS chain cannot be validated against system roots; obtain the complete trusted chain from the hosting provider.')
            seen.add(candidate)
            current = download_ca(candidate)
            intermediates.append(current)
    raise RuntimeError('FTPS issuer chain exceeded the verification depth.')
