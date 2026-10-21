"""Write private hosting settings. Never print a generated bootstrap secret."""
import argparse, json, os, re, secrets
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--host', required=True, help='Real HTTPS hostname, without scheme/path')
p.add_argument('--data-dir', required=True, help='Durable absolute directory on the server')
p.add_argument('--output', type=Path, default=Path('private-data/appsettings.Local.json'))
p.add_argument('--trusted-proxy', action='append', default=[])
a=p.parse_args()
if not re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?', a.host): p.error('Supply a hostname only')
if not (Path(a.data_dir).is_absolute() or re.match(r'^[A-Za-z]:[/\\]',a.data_dir)): p.error('Data directory must be absolute')
if a.output.exists(): p.error('Output already exists; refusing to replace existing secrets')
config={'AllowedHosts':a.host,'Payroll':{'DataDir':a.data_dir,'BootstrapKey':secrets.token_hex(32)},'PAYROLL_KNOWN_PROXIES':','.join(a.trusted_proxy)}
a.output.parent.mkdir(parents=True,exist_ok=True)
fd=os.open(a.output,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
with os.fdopen(fd,'w') as f: json.dump(config,f,indent=2)
print(f'Private settings saved to {a.output}. Upload separately to the server application root; never commit this file.')
