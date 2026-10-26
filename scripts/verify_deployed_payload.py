"""Read-only proof of the exact public payload; never retrieves a database or session key."""
import hashlib
import io
import json
from pathlib import Path
import urllib.parse
import urllib.request
import monsterasp_ftps as lifecycle

def selected_files(root):
    root=Path(root)
    files=sorted(p for p in root.rglob('*') if p.is_file())
    if not files:raise RuntimeError('Deployment payload is empty.')
    for path in files:
        relative=path.relative_to(root)
        if path.is_symlink() or any(part.lower() in {'app_data','private','private-data'} for part in relative.parts) or path.name.lower() in {'appsettings.local.json','app_offline.htm'}:
            raise RuntimeError('Protected runtime content must not enter the public payload.')
    return files

def digest_file(path):
    digest=hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda:source.read(1024*1024),b''):digest.update(block)
    return digest.hexdigest()

def verify_bundle(ftp,root):
    root=Path(root);files=selected_files(root);manifest=[];total=0
    ftp.voidcmd('TYPE I')
    for path in files:
        relative=path.relative_to(root).as_posix();digest=hashlib.sha256();received=0
        def block(data):
            nonlocal received
            received+=len(data);digest.update(data)
        ftp.retrbinary('RETR '+relative,block,blocksize=64*1024)
        expected=digest_file(path)
        if received!=path.stat().st_size or digest.hexdigest()!=expected:raise RuntimeError('Public payload mismatch: '+relative)
        total+=received;manifest.append({'path':relative,'bytes':received,'sha256':expected})
    return {'files':len(files),'bytes':total,'manifest':manifest}

def verify_http_assets(root,base='https://payroll.marketingalbawaba.com'):
    root=Path(root);verified=[]
    for path in selected_files(root):
        relative=path.relative_to(root)
        if not relative.parts or relative.parts[0]!='wwwroot':continue
        route='/'+'/'.join(urllib.parse.quote(p) for p in relative.parts[1:])
        request=urllib.request.Request(base+route+'?release=0.7.0',headers={'Cache-Control':'no-cache','Accept-Encoding':'identity'})
        with urllib.request.urlopen(request,timeout=30) as response:
            actual=hashlib.sha256(response.read()).hexdigest()
        if actual!=digest_file(path):raise RuntimeError('HTTPS asset mismatch: '+route)
        verified.append(route)
    return verified

def main():
    root=Path('artifacts/monsterasp');selected_files(root)
    ftp=lifecycle.connect()
    try:
        if lifecycle.read_marker(ftp) is not None:raise RuntimeError('An IIS maintenance marker remains active.')
        proof=verify_bundle(ftp,root)
        # Only release metadata is read from App_Data. No record bodies or secret contents are downloaded.
        checkpoint=io.BytesIO();ftp.retrbinary('RETR App_Data/release-backups/0.7.0/checkpoint.json',checkpoint.write)
        metadata=json.loads(checkpoint.getvalue());before=metadata.get('before',{})
        if metadata.get('version')!='0.7.0' or not before.get('recordsSha256'):raise RuntimeError('Release checkpoint metadata is incomplete.')
        proof['checkpoint']={'version':metadata['version'],'counts':before.get('counts',{}),'recordsSha256':before['recordsSha256']}
    finally:ftp.close()
    proof['httpsAssets']=verify_http_assets(root)
    proof['status']='verified'
    Path('artifacts/deployment-verification-v070.json').write_text(json.dumps(proof,indent=2))
    print(f"PASS full deployed payload: {proof['files']} files / {proof['bytes']} bytes, SHA256 exact; HTTPS assets={len(proof['httpsAssets'])}; private checkpoint metadata verified. No remote writes.")

if __name__=='__main__':main()
