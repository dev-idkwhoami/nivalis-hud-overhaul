#!/usr/bin/env bash
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
action="${1:-}"
if [[ $# != 1 || ! "$action" =~ ^(install|package|clean)$ ]]; then
    echo 'Usage: bash tools/mod_artifacts.sh install|package|clean' >&2
    exit 1
fi
if [[ "$action" == clean ]]; then
    rm -rf -- "$root/bin" "$root/src/obj" "$root/tests/bin" "$root/tests/obj"
    echo 'Removed generated HUD Overhaul output'
    exit
fi

dotnet="${DOTNET:-dotnet}"
project="$root/src/HudOverhaul.csproj"
assembly="$("$dotnet" msbuild "$project" -nologo -getProperty:AssemblyName)"
version="$("$dotnet" msbuild "$project" -nologo -getProperty:Version)"
for value in "$assembly" "$version"; do
    [[ "$value" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]] || { echo 'Invalid assembly name or version' >&2; exit 1; }
done
binary="$root/bin/$assembly.dll"
[[ -f "$binary" ]] || { echo 'Mod DLL missing; run make build first' >&2; exit 1; }

if [[ "$action" == install ]]; then
    [[ -n "${GAME_PATH:-}" && -f "$GAME_PATH/BepInEx/interop/Assembly-CSharp.dll" ]] || {
        echo 'Set GAME_PATH to a game installation with BepInEx IL2CPP references' >&2; exit 1;
    }
    destination="$GAME_PATH/BepInEx/plugins"
    mkdir -p -- "$destination"
    cp -a -- "$root/dependencies/." "$destination/"
    cp -- "$binary" "$destination/$assembly.dll"
    rm -f -- "$destination/hud-overhaul/$assembly.dll" \
        "$destination/Nivalis.ConditionOrder.dll" "$destination/condition-order/Nivalis.ConditionOrder.dll"
    echo "Installed $assembly.dll — restart the game to load it."
    exit
fi

output="${PACKAGE_DIR:-$root/bin}"
mkdir -p -- "$output"
output="$(cd -- "$output" && pwd)"
staging="$(mktemp -d "$output/.package.XXXXXXXX")"
trap 'rm -rf -- "$staging"' EXIT
mkdir -p -- "$staging/files/BepInEx/plugins"
cp -a -- "$root/dependencies/." "$staging/files/BepInEx/plugins/"
cp -- "$binary" "$staging/files/BepInEx/plugins/$assembly.dll"
"$dotnet" msbuild "$root/tools/Package.proj" -nologo -target:Package \
    "-p:PackageSource=$staging/files" "-p:PackageArchive=$staging/archive.zip"
mv -f -- "$staging/archive.zip" "$output/$assembly-$version.zip"
echo "Package: $output/$assembly-$version.zip"
echo 'Extract into the game root after installing BepInEx.'
