"""Copy an optional Next static export into the .NET web root, before publish."""
from pathlib import Path
import shutil


def package(root):
    root = Path(root)
    if not (root / 'web/package.json').is_file():
        print('SKIP Next export: web/package.json is absent')
        return False
    output = root / 'web/out'
    if not (output / 'index.html').is_file():
        raise RuntimeError('Next export must contain web/out/index.html before publish.')
    if any(path.is_symlink() for path in output.rglob('*')):
        raise RuntimeError('Next export must not contain symlinks.')
    shutil.copytree(output, root / 'Payroll.Api/wwwroot/app', dirs_exist_ok=True)
    print('PASS packaged Next static export at /app')
    return True


if __name__ == '__main__':
    package(Path(__file__).resolve().parents[1])
