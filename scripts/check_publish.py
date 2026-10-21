from pathlib import Path
import argparse, json

def check(root):
    required = ['Payroll.Api.exe', 'Payroll.Api.dll', 'web.config', 'wwwroot/index.html',
                'wwwroot/app.css', 'wwwroot/app.js', 'wwwroot/table-tools.js',
                'wwwroot/bootstrap.js', 'wwwroot/rider-form.html', 'wwwroot/rider-form.js']
    for name in required:
        assert (root / name).is_file(), 'Missing published file: ' + name
    for path in root.rglob('*'):
        if not path.is_file(): continue
        rel = path.relative_to(root)
        assert not set(rel.parts) & {'data', 'App_Data', 'keys', 'private-data'}, 'Runtime data in payload: ' + str(rel)
        assert path.name != 'appsettings.Local.json', 'Secret settings in payload'
        assert path.suffix.lower() not in {'.db', '.sqlite', '.xlsx', '.csv', '.sql', '.pfx', '.pem', '.key'}
    assert json.loads((root/'appsettings.Production.json').read_text())['Payroll']['BootstrapKey'] == ''
    print('PASS clean Windows deployment payload and required web assets')

if __name__ == '__main__':
    p=argparse.ArgumentParser(); p.add_argument('directory', type=Path); args=p.parse_args(); check(args.directory)
