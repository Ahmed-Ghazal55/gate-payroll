"""Small IIS deployment lifecycle around FTP-Deploy-Action; never write runtime data."""
import ftplib
import hashlib
import io
import json
import os
from pathlib import Path
import ssl
import sys
import time
import urllib.request
from ftps_chain import verified_intermediates

MARKER = 'app_offline.htm'
PREFIX = b'<!-- Gate Payroll automated deployment -->'
STATE = Path('artifacts/ftps-lifecycle.json')
HEALTH = 'https://payroll.marketingalbawaba.com/health'


def connect():
    names = ['MONSTERASP_FTP_SERVER', 'MONSTERASP_FTP_USERNAME', 'MONSTERASP_FTP_PASSWORD']
    values = [os.environ.get(name, '') for name in names]
    missing = [name for name, value in zip(names, values) if not value]
    if missing:
        raise RuntimeError('Missing repository secrets: ' + ', '.join(missing))
    context=ssl.create_default_context()
    chain_path=Path('artifacts/ftps-ca-chain.pem')
    if chain_path.exists():
        context.load_verify_locations(cafile=str(chain_path))
    for attempt in range(2):
        ftp = ftplib.FTP_TLS(context=context, timeout=60)
        stage='TLS/login'
        try:
            ftp.connect(values[0], 21)
            ftp.login(values[1], values[2])
            print('PASS authenticated FTPS control channel.')
            stage='data-channel encryption'
            ftp.prot_p()
            stage='existing wwwroot directory'
            ftp.cwd('wwwroot')
            return ftp
        except ssl.SSLCertVerificationError as error:
            ftp.close()
            if attempt or error.verify_code not in {20,21}:
                raise
            chain=verified_intermediates(values[0])
            if not chain:
                raise RuntimeError('FTPS certificate verification failed without an omitted intermediate.')
            chain_path.parent.mkdir(parents=True,exist_ok=True)
            chain_path.write_bytes(chain)
            context.load_verify_locations(cafile=str(chain_path))
            # Share the verified CA intermediates with the requested Node FTPS action.
            if os.environ.get('GITHUB_ENV'):
                with open(os.environ['GITHUB_ENV'],'a',encoding='utf-8') as output:
                    output.write('NODE_EXTRA_CA_CERTS='+str(chain_path.resolve())+'\n')
        except ftplib.error_perm as error:
            ftp.close()
            message=str(error)
            for value in values:
                message=message.replace(value,'[redacted]')
            raise RuntimeError('FTPS permission error during '+stage+': '+message) from None
        except Exception:
            ftp.close()
            raise
    raise RuntimeError('FTPS TLS verification did not complete.')


def marker_bytes():
    owner = os.environ.get('GITHUB_RUN_ID', 'local') + ':' + os.environ.get('GITHUB_RUN_ATTEMPT', '1')
    return PREFIX + ('\n<!-- ' + owner + ' -->\n<!doctype html><meta charset="utf-8"><title>Gate Payroll</title><p>جاري تحديث الموقع. Please try again shortly.</p>').encode()


def read_marker(ftp):
    data = io.BytesIO()
    try:
        ftp.retrbinary('RETR ' + MARKER, data.write)
    except ftplib.error_perm as error:
        if str(error).startswith('550'):
            return None
        raise
    return data.getvalue()


def protected_fingerprint(ftp):
    """Hash metadata only. Do not download the database, keys or private settings."""
    entries = []

    def walk(directory):
        for name, facts in ftp.mlsd(directory):
            if name in {'.', '..'} or facts.get('type') in {'cdir', 'pdir'}:
                continue
            path = directory + '/' + name
            entries.append((path, facts.get('size', ''), facts.get('modify', '')))
            if facts.get('type') == 'dir':
                walk(path)

    for name, facts in ftp.mlsd():
        if name.lower() == 'app_data':
            walk(name)
        elif name.lower() == 'appsettings.local.json':
            entries.append((name, facts.get('size', ''), facts.get('modify', '')))
    return hashlib.sha256(json.dumps(sorted(entries)).encode()).hexdigest()


def prepare(ftp, wait=time.sleep):
    previous = read_marker(ftp)
    if previous is not None and not previous.startswith(PREFIX):
        raise RuntimeError('An operator maintenance marker already exists; it was not changed.')
    # Verify metadata capability before taking a live application offline.
    protected_fingerprint(ftp)
    marker = marker_bytes()
    try:
        ftp.storbinary('STOR ' + MARKER, io.BytesIO(marker))
        wait(15)
        STATE.parent.mkdir(parents=True, exist_ok=True)
        STATE.write_text(json.dumps({'marker_sha256': hashlib.sha256(marker).hexdigest(), 'protected': protected_fingerprint(ftp)}))
    except Exception:
        # No application files have been transferred during prepare. Restore prior availability.
        if read_marker(ftp) == marker:
            if previous is None:
                ftp.delete(MARKER)
            else:
                ftp.storbinary('STOR ' + MARKER, io.BytesIO(previous))
        raise
    print('PASS FTPS preflight and IIS maintenance marker; protected metadata recorded.')


def finish(ftp):
    state = json.loads(STATE.read_text())
    marker = read_marker(ftp)
    if marker is None or hashlib.sha256(marker).hexdigest() != state['marker_sha256']:
        raise RuntimeError('Maintenance marker ownership changed; nothing was removed.')
    if protected_fingerprint(ftp) != state['protected']:
        raise RuntimeError('Protected runtime metadata changed; maintenance retained for inspection.')
    ftp.delete(MARKER)
    print('PASS runtime data/settings metadata unchanged; owned maintenance marker removed.')


def health(wait=time.sleep):
    for _ in range(30):
        try:
            request = urllib.request.Request(HEALTH, headers={'Cache-Control': 'no-cache'})
            with urllib.request.urlopen(request, timeout=15) as response:
                result = json.load(response)
            if result.get('ok') is True and result.get('version') == '0.6.1' and result.get('dataPreservationVerified') is True and result.get('releaseCheckpointPresent') is True:
                print('PASS live HTTPS /health: ok=true, version=0.6.1, dataPreservationVerified=true, releaseCheckpointPresent='+str(result.get('releaseCheckpointPresent')))
                return
        except (OSError, ValueError):
            pass
        wait(2)
    raise RuntimeError('Live HTTPS /health did not confirm version 0.6.1.')


def main():
    command = sys.argv[1] if len(sys.argv) > 1 else ''
    if command == 'health':
        health()
        return
    if command not in {'prepare', 'finish', 'check'}:
        raise RuntimeError('Use check, prepare, finish or health.')
    ftp = connect()
    try:
        if command == 'check':
            print('PASS verified FTPS TLS/login/existing root; no files changed.')
        else:
            (prepare if command == 'prepare' else finish)(ftp)
    finally:
        ftp.close()


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Never log credentials, URLs containing them, or remote private filenames.
        detail = ('TLS certificate verification '+str(error.verify_code)+': '+error.verify_message) if isinstance(error, ssl.SSLCertVerificationError) else str(error) if isinstance(error, (RuntimeError,ftplib.error_perm)) else type(error).__name__
        for name in ['MONSTERASP_FTP_SERVER','MONSTERASP_FTP_USERNAME','MONSTERASP_FTP_PASSWORD']:
            value=os.environ.get(name,'')
            if value: detail=detail.replace(value,'[redacted]')
        print('Deployment lifecycle failed: ' + detail, file=sys.stderr)
        sys.exit(1)
