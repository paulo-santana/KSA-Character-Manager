#!/usr/bin/env python3
"""Fails when the game assemblies in ksa-build-libs are older than the newest KSA build anyone can see.

A mod built against old assemblies runs on the game it was built for and may throw MissingMethodException on newer ones
(0.5.0 did: SetAnimation gained a parameter). Run after the ksa-build-libs checkout: check_libs.py LIBS

The newest build is read from the game's own version server and from Borea's index of game versions; a source that cannot be
reached is skipped with a warning, and the check passes only if at least one answered.
"""
import json
import re
import sys
import urllib.request
from pathlib import Path

SOURCES = (
    ("the game's version server", "http://ksa-master1.rocketwerkz.com:8082/version", lambda text: [json.loads(text)["Version"]]),
    ("Borea's index", "https://raw.githubusercontent.com/KSAModding/content-index-releases/main/game-versions.json",
     lambda text: json.loads(text)["versions"]),
)


def revision(version):
    match = re.search(r"(\d{4}\.\d+\.\d+\.(\d+))\s*$", version.strip())
    return (int(match.group(2)), match.group(1)) if match else None


def main(libs):
    mine = revision((Path(libs) / "GAME_VERSION").read_text())
    if mine is None:
        sys.exit(f"{libs}/GAME_VERSION has no build number")
    newest, seen = mine, 0
    for name, url, parse in SOURCES:
        try:
            with urllib.request.urlopen(url, timeout=15) as response:
                found = [r for r in map(revision, parse(response.read().decode())) if r]
        except Exception as error:  # a source that is down must not block a release by itself
            print(f"warning: could not read {name}: {error}")
            continue
        if not found:
            print(f"warning: {name} gave no build number")
        else:
            seen += 1
            newest = max(newest, max(found))
            print(f"{name}: newest build {max(found)[1]}")
    if seen == 0:
        sys.exit("could not reach any source of game versions")
    print(f"build-libs: {mine[1]}")
    if newest > mine:
        sys.exit(f"ksa-build-libs holds game {mine[1]} but {newest[1]} is out: run tools/update.sh in ksa-build-libs against the "
                 "new game, commit and push, then run this again. A mod built against older assemblies can crash on a newer game.")
    print("the build libs are the newest game build")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "build-libs")
