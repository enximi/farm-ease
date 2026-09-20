"""Package each independent mod and a complete set, without game binaries or user settings."""
from pathlib import Path
import json
import shutil
import zipfile

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / 'dist'
MODS = ('TravelEase', 'GardenEase', 'FishingEase', 'StorageEase')
DIST.mkdir(exist_ok=True)
package_files = []
for name in MODS:
    source = ROOT / 'mods' / name
    manifest = json.loads((source / 'manifest.json').read_text())
    target = DIST / name
    target.mkdir(exist_ok=True)
    files = [source / 'bin/Release/net6.0' / f'{name}.dll', source / 'manifest.json', source / 'README.md']
    for path in files:
        shutil.copy2(path, target / path.name)
    archive = DIST / f"{name}-{manifest['Version']}.zip"
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for path in files:
            installed = target / path.name
            z.write(installed, f'{name}/{path.name}')
            package_files.append(installed)
    print(f'安装包：{archive}')

with zipfile.ZipFile(DIST / 'FarmEase-Collection.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for path in package_files:
        z.write(path, str(path.relative_to(DIST)))
    z.write(ROOT / 'INSTALL.md', 'INSTALL.md')

with zipfile.ZipFile(DIST / 'FarmEase-Collection-source.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for name in ('build.sh', 'Directory.Build.props', 'README.md', 'INSTALL.md', 'VERIFICATION.md', 'AGENTS.md', '.gitignore'):
        z.write(ROOT / name, name)
    for folder in ('mods', 'shared', 'scripts'):
        for path in sorted((ROOT / folder).rglob('*')):
            if path.is_file() and not {'bin', 'obj', '__pycache__'}.intersection(path.relative_to(ROOT).parts):
                z.write(path, str(path.relative_to(ROOT)))
print(f'合集：{DIST / "FarmEase-Collection.zip"}')
