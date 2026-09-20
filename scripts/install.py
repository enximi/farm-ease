"""Install the independent mods, migrate FarmEase settings once, and retain a rollback copy."""
from pathlib import Path
from datetime import datetime
import argparse
import json
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
NAMES = ('TravelEase', 'GardenEase', 'FishingEase', 'StorageEase')
MENU_KEYS = ('MenuButton', 'KeyboardMenuButton', 'RepeatDelayMilliseconds', 'RepeatIntervalMilliseconds', 'PanelWidth')
TRAVEL_KEYS = ('HomeButton', 'KeyboardHomeButton', 'HomeHoldMilliseconds')
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mods-path', type=Path, required=True)
args = parser.parse_args()
mods = args.mods_path.resolve()

for process in ('StardewModdingAPI', 'Stardew Valley', 'StardewValley'):
    if subprocess.run(['pgrep', '-x', process], stdout=subprocess.DEVNULL).returncode == 0:
        raise SystemExit('请先退出游戏；安装包已保留。')

legacy = mods / 'FarmEase'
if legacy.exists():
    if json.loads((legacy / 'manifest.json').read_text())['UniqueID'] != 'zzz.FarmEase':
        raise SystemExit('FarmEase 目录不是预期的旧 Mod，已停止安装。')
    legacy_config = json.loads((legacy / 'config.json').read_text()) if (legacy / 'config.json').exists() else {}
    if not isinstance(legacy_config, dict):
        raise SystemExit('旧 FarmEase 配置不是 JSON 对象，已停止安装并保留原文件。')
else:
    legacy_config = {}

mods.mkdir(parents=True, exist_ok=True)
backup = ROOT / '.work/backups' / datetime.now().strftime('installed-%Y%m%d-%H%M%S-%f')
backup.mkdir(parents=True)
menu_path = mods / '.farm-ease-menu.json'
if menu_path.exists():
    shutil.copy2(menu_path, backup / menu_path.name)
installed = []
moved = []
created_menu = False
with tempfile.TemporaryDirectory(prefix='install-', dir=ROOT / '.work') as temporary:
    stage = Path(temporary)
    for name in NAMES:
        source = ROOT / 'dist' / name
        manifest = json.loads((source / 'manifest.json').read_text())
        target = stage / name
        existing = mods / name
        if existing.exists():
            if json.loads((existing / 'manifest.json').read_text())['UniqueID'] != manifest['UniqueID']:
                raise SystemExit(f'{name} 目录属于其他 Mod，已停止安装。')
            shutil.copytree(existing, target)
        else:
            target.mkdir()
        for filename in (manifest['EntryDll'], 'manifest.json', 'README.md'):
            shutil.copy2(source / filename, target / filename)
    travel_config = stage / 'TravelEase/config.json'
    if not travel_config.exists() and legacy_config:
        travel_config.write_text(json.dumps({k: legacy_config[k] for k in TRAVEL_KEYS if k in legacy_config}, indent=2) + '\n')

    try:
        for name in (*NAMES, 'FarmEase'):
            existing = mods / name
            if existing.exists():
                shutil.move(str(existing), str(backup / name))
                moved.append(name)
        for name in NAMES:
            shutil.move(str(stage / name), str(mods / name))
            installed.append(name)
        if not menu_path.exists() and legacy_config:
            # Shared settings survive removal of whichever mod currently provides the menu.
            created_menu = True
            menu_path.write_text(json.dumps({k: legacy_config[k] for k in MENU_KEYS if k in legacy_config}, indent=2) + '\n')
    except Exception:
        for name in installed:
            shutil.rmtree(mods / name)
        for name in moved:
            shutil.move(str(backup / name), str(mods / name))
        if created_menu:
            menu_path.unlink(missing_ok=True)
        raise
print(f'已安装四个独立 Mod：{mods}')
print(f'旧版本及原配置备份：{backup}')
print('存档未修改；旧 FarmEase 已移出 Mods，不会重复提供菜单或传送。')
