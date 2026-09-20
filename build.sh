#!/bin/bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")" && pwd)"
game_dir="${STARDEW_GAME_PATH:-/Applications/Stardew Valley.app/Contents/MacOS}"
dotnet_bin="${STARDEW_DOTNET:-}"
if [ "$#" -gt 1 ] || { [ "$#" -eq 1 ] && [ "$1" != '--install' ]; }; then
    printf '%s\n' '用法：./build.sh [--install]' >&2
    exit 1
fi
if [ -z "$dotnet_bin" ]; then
    if command -v dotnet >/dev/null 2>&1; then
        dotnet_bin="$(command -v dotnet)"
    elif [ -x "$project_dir/.work/dotnet/dotnet" ]; then
        dotnet_bin="$project_dir/.work/dotnet/dotnet"
    else
        printf '%s\n' '请安装 .NET 8 SDK，或用 STARDEW_DOTNET 指定 dotnet 可执行文件。' >&2
        exit 1
    fi
fi
if [ ! -f "$game_dir/StardewModdingAPI.dll" ]; then
    printf '%s\n' '找不到 SMAPI；可用 STARDEW_GAME_PATH 指定包含游戏 DLL 的目录。' >&2
    exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
cd "$project_dir"
for mod_name in TravelEase GardenEase FishingEase StorageEase; do
    "$dotnet_bin" build "mods/$mod_name/$mod_name.csproj" -c Release --nologo -p:GamePath="$game_dir"
done
python3 scripts/package.py
if [ "${1:-}" = '--install' ]; then
    if pgrep -x StardewModdingAPI >/dev/null || pgrep -x 'Stardew Valley' >/dev/null || pgrep -x StardewValley >/dev/null; then
        printf '%s\n' '构建和打包已完成；请退出游戏后再执行 --install。' >&2
        exit 2
    fi
    python3 scripts/install.py --mods-path "$game_dir/Mods"
fi
