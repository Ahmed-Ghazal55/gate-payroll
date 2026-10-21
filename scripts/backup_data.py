"""Create a consistent SQLite backup plus session keys, outside published web assets."""
from pathlib import Path
import argparse, datetime, os, sqlite3, tempfile, zipfile

p=argparse.ArgumentParser(); p.add_argument('--data-dir', required=True, type=Path)
p.add_argument('--output-dir', required=True, type=Path); a=p.parse_args()
database=a.data_dir.resolve()/'payroll.db'
if not database.is_file(): p.error('No payroll.db in the specified data directory')
a.output_dir.mkdir(parents=True,exist_ok=True)
output=a.output_dir/('payroll-backup-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')+'.zip')
with tempfile.TemporaryDirectory(prefix='payroll-backup-') as temp:
    backup=Path(temp)/'payroll.db'
    with sqlite3.connect(database.as_uri()+'?mode=ro',uri=True) as source, sqlite3.connect(backup) as target:
        source.backup(target)
        if target.execute('PRAGMA integrity_check').fetchone()[0]!='ok': raise RuntimeError('Backup integrity check failed')
    with zipfile.ZipFile(output,'x',zipfile.ZIP_DEFLATED) as archive:
        archive.write(backup,'payroll.db')
        keys=a.data_dir/'keys'
        if keys.is_dir():
            for key in keys.glob('*.xml'): archive.write(key,'keys/'+key.name)
os.chmod(output,0o600)
print(f'Consistent private backup saved: {output}')
