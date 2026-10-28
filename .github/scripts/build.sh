#!/usr/bin/env bash
# Builds the mod against the assemblies in LIBS (a checkout of ksa-build-libs) and packs the release zip:
# CharacterManager/ with the dll, mod.toml, README.md and LICENSE. Usage: build.sh LIBS ZIP
set -euo pipefail
libs="$(realpath "$1")"
zip="$2"
dotnet build -c Release -p:GameDir="$libs/game" -p:StarMapDir="$libs/starmap"
stage="$(mktemp -d)"
mkdir "$stage/CharacterManager"
cp mod/CharacterManager/CharacterManager.dll mod/CharacterManager/mod.toml README.md LICENSE "$stage/CharacterManager/"
rm -f "$zip"
(cd "$stage" && zip -qr - CharacterManager) > "$zip"
unzip -l "$zip"
