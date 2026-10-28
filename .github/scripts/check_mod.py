#!/usr/bin/env python3
"""Checks that Character Manager can be released as it stands. Standard library only.

    check_mod.py [BASE_REF]

Always: mod.toml has a name and a SemVer version, and its [StarMap] EntryAssembly is the assembly the project builds.
With BASE_REF (a pull request's target): a change to anything the release zip is made from needs a higher version than
the target's and than every tag, and release-notes/v<version>.md. The build itself is checked by the workflow.
"""
import re
import subprocess
import sys
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD_TOML = "mod/CharacterManager/mod.toml"
SHIPPED = ("src/", "CharacterManager.csproj", "mod/", "README.md", "LICENSE")  # what the zip is built from
SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")
errors = []


def error(msg):
    errors.append(msg)
    print(f"::error::{msg}")


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout


def semver(v):
    m = SEMVER.match(v or "")
    return tuple(map(int, m.groups())) if m else None


def check_manifest():
    mod = tomllib.loads((ROOT / MOD_TOML).read_text())
    if not mod.get("name"):
        error(f"{MOD_TOML} has no name")
    version = mod.get("version")
    if semver(version) is None:
        error(f"{MOD_TOML}: version '{version}' is not MAJOR.MINOR.PATCH")
    entry = mod.get("StarMap", {}).get("EntryAssembly")
    built = re.search(r"<AssemblyName>(.+?)</AssemblyName>", (ROOT / "CharacterManager.csproj").read_text())
    if not built or entry != built.group(1):
        error(f"{MOD_TOML}: [StarMap] EntryAssembly '{entry}' is not the assembly the project builds "
              f"('{built.group(1) if built else '?'}')")
    return version


def check_against(base, version):
    changed = [f for f in git("diff", "--name-only", f"{base}...HEAD").split() if f.startswith(SHIPPED)]
    if not changed:
        print("No shipped file changed: no release needed.")
        return
    base_version = tomllib.loads(git("show", f"{base}:{MOD_TOML}")).get("version")
    tags = [semver(t[1:]) for t in git("tag", "--list", "v*").split()]
    newest = max([t for t in tags if t] + [semver(base_version) or (0, 0, 0)])
    if semver(version) is None or semver(version) <= newest:
        error(f"shipped files changed ({', '.join(changed[:5])}{'...' if len(changed) > 5 else ''}): "
              f"raise version in {MOD_TOML} above {'.'.join(map(str, newest))}")
    notes = ROOT / "release-notes" / f"v{version}.md"
    if not notes.is_file() or not notes.read_text().strip():
        error(f"release-notes/v{version}.md is missing or empty")


version = check_manifest()
if len(sys.argv) > 1:
    check_against(sys.argv[1], version)
if errors:
    sys.exit(1)
print("The mod checks out.")
