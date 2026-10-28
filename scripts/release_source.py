"""Build an allowlisted source bundle; never collect operational data or secrets."""
from pathlib import Path
import argparse, hashlib, json, re, zipfile

ROOT = Path(__file__).resolve().parents[1]
EXACT = {'.gitignore', '.env.example', 'README_AR.md', 'DEPLOYMENT_AR.md',
         'DATA_IMPORT_AR.md', 'UPGRADE_V05_AR.md', 'UPGRADE_V07_AR.md', 'RELEASE_STATUS_AR.md', 'API_CONTRACT.md', 'TABLE_STANDARD_AR.md',
         'Payroll.Api/Payroll.Api.csproj', 'Payroll.Api/Resources/target-coa.v1.json',
         'tools/generate_target_coa.py', 'Payroll.Api/appsettings.json',
         'Payroll.Api/appsettings.Production.json', 'Payroll.Api/web.config',
         'Payroll.Api/wwwroot/index.html', 'Payroll.Api/wwwroot/app.css',
         'Frontend/tsconfig.json', 'RiderForm/package.json', 'RiderForm/package-lock.json',
         'RiderForm/tsconfig.json', 'RiderForm/web/rider-form.html',
         'Run-Local.ps1', 'Publish-MonsterASP.ps1', 'run-local.sh', 'publish-monsterasp.sh',
         '.github/workflows/build-and-deploy.yml'}
PATTERNS = ['Payroll.Api/*.cs', 'Frontend/src/*.ts', 'RiderForm/src/*.ts',
            'tools/StoreMigration/*.cs', 'tools/StoreMigration/*.csproj', 'tests/*.mjs', 'tests/Engine.Tests/*.cs', 'tests/Engine.Tests/*.csproj', 'scripts/*.py', 'scripts/fonts-manifest.json']

def files():
    result = {ROOT / p for p in EXACT if (ROOT / p).is_file()}
    for pattern in PATTERNS:
        result.update(p for p in ROOT.glob(pattern) if p.is_file())
    return sorted(result)

def validate(selected):
    required = ['Payroll.Api/Resources/target-coa.v1.json', 'tools/generate_target_coa.py', 'Payroll.Api/Program.cs', 'Payroll.Api/HostingSecurity.cs',
                'RiderForm/src/rider-form.ts', 'RiderForm/package-lock.json',
                'tests/production.mjs', 'tests/enterprise.mjs', 'Payroll.Api/UserAccess.cs', 'Payroll.Api/FormBuilder.cs', 'Payroll.Api/InquiryApi.cs', 'scripts/check_publish.py']
    for name in required:
        assert ROOT / name in selected, 'Missing release source: ' + name
    settings = json.loads((ROOT / 'Payroll.Api/appsettings.Production.json').read_text())
    assert settings['Payroll']['BootstrapKey'] == '', 'Production bootstrap secret must be absent'
    for path in selected:
        rel = path.relative_to(ROOT)
        assert not path.is_symlink(), 'Symlinks cannot enter a source bundle'
        assert not set(rel.parts) & {'data', 'App_Data', 'node_modules', 'private-data', 'bin', 'obj'}
        assert path.suffix not in {'.db', '.xlsx', '.sql', '.csv', '.pfx', '.key', '.pem'}
        text = path.read_text(encoding='utf-8')
        assert not re.search(r'SA[0-9]{22}', text.replace('SA0380000000608010167519', '').replace('SA0080000000608010167519', '')), 'Unexpected bank account in release source: ' + str(rel)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--check', action='store_true')
    parser.add_argument('--output', default=str(ROOT / 'artifacts/gate-payroll-source.zip'))
    args = parser.parse_args(); selected = files(); validate(selected)
    if args.check:
        print(f'PASS source boundary checks: {len(selected)} allowlisted files')
    else:
        output = Path(args.output); output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path in selected: archive.write(path, str(path.relative_to(ROOT)))
        manifest = [{'path':str(p.relative_to(ROOT)), 'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in selected]
        output.with_suffix('.manifest.json').write_text(json.dumps(manifest, indent=2))
        print(f'Created source bundle: {output} ({len(selected)} files)')
