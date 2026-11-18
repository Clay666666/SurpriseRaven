"""Offline RitsuLib 0.6.14 runtime and main-Mod payload validation.

This module only reads the selected runtime/game/payload. It never loads a DLL,
extracts assets, starts a game process, or modifies the installed runtime.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any

VERSION = "0.6.14"
GAME_COMPAT_TARGETS = {"0.111.0": "0.111.0", "0.113.1": "0.113.0"}
MOD_ID = "SurpriseRaven"
SHARED = {"STS2-RitsuLib.Shared", "STS2-RitsuLib.Ui", "STS2-RitsuLib.Settings"}
VARIANT_MODULES = {"STS2-RitsuLib.Runtime", "STS2-RitsuLib"}
VARIANTS = {"0.107.1", "0.111.0", "0.113.0"}


class ValidationError(ValueError):
    pass


def safe_path(root: Path, relative: str = "") -> Path:
    """Reject traversal and reparse points before accessing package files."""
    root = root.absolute()
    relative_path = PurePosixPath(relative.replace("\\", "/"))
    if (relative_path.is_absolute() or ".." in relative_path.parts
            or ":" in relative or "\x00" in relative):
        raise ValidationError(f"Unsafe runtime path: {relative!r}")
    path = root.joinpath(*relative_path.parts)
    for current in [path, *path.parents]:
        if current.exists() or current.is_symlink():
            attributes = current.lstat()
            if (stat.S_ISLNK(attributes.st_mode)
                    or getattr(attributes, "st_file_attributes", 0) & 0x400):
                raise ValidationError(f"Reparse points are not allowed: {current}")
    if path != root and root not in path.parents:
        raise ValidationError(f"Path escapes runtime root: {path}")
    return path


def require_file(root: Path, relative: str) -> Path:
    path = safe_path(root, relative)
    if not path.is_file() or path.stat().st_size == 0:
        raise ValidationError(f"Required file is missing or empty: {path}")
    return path


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValidationError(f"Expected a JSON object: {path}")
    return value


def digest(path: Path) -> str:
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(chunk)
    return checksum.hexdigest()


def regular_tree(root: Path):
    """Inspect each directory before entering it; never follow package links."""
    pending = [safe_path(root)]
    while pending:
        path = pending.pop()
        safe_path(root, str(path.relative_to(root)))
        yield path
        if path.is_dir():
            pending.extend(path.iterdir())


def check_assembly(runtime: Path, directory: str, record: dict[str, Any]) -> dict[str, str]:
    name, expected = record.get("assembly"), record.get("sha256")
    if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9_.-]+", name):
        raise ValidationError(f"Invalid assembly name in runtime manifest: {name!r}")
    if not isinstance(expected, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", expected):
        raise ValidationError(f"Invalid SHA256 for {name}")
    relative = f"{directory}/{name}.dll"
    path = require_file(runtime, relative)
    actual = digest(path)
    if actual != expected.lower():
        raise ValidationError(f"SHA256 mismatch: {relative}")
    return {"path": relative, "sha256": actual}


def check_assets(path: Path) -> int:
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if not entries:
            raise ValidationError("RitsuLib assets.zip is empty")
        seen: set[str] = set()
        for entry in entries:
            name = entry.filename.replace("\\", "/")
            parts = PurePosixPath(name)
            if (parts.is_absolute() or ".." in parts.parts or ":" in name
                    or "\x00" in name or stat.S_ISLNK(entry.external_attr >> 16)):
                raise ValidationError(f"Unsafe assets.zip entry: {entry.filename!r}")
            normalized = str(parts).casefold()
            if normalized in seen:
                raise ValidationError(f"Duplicate assets.zip entry: {entry.filename!r}")
            seen.add(normalized)
        bad_entry = archive.testzip()
        if bad_entry:
            raise ValidationError(f"CRC mismatch in assets.zip: {bad_entry}")
    return len(entries)


def check_payload(directory: Path, mod_id: str = MOD_ID) -> dict[str, Any]:
    payload = safe_path(directory)
    manifest_path = require_file(payload, f"{mod_id}.json")
    manifest = read_json(manifest_path)
    if manifest.get("id") != mod_id or manifest.get("pck_name") != mod_id:
        raise ValidationError("Main Mod identity and PCK name must match its payload")
    if manifest.get("has_dll") is not True or manifest.get("has_pck") is not True:
        raise ValidationError("Main Mod must declare both its DLL and PCK")
    dependencies = manifest.get("dependencies", [])
    if (not isinstance(dependencies, list)
            or any(not isinstance(item, dict) or not isinstance(item.get("id"), str)
                   or not item["id"] for item in dependencies)):
        raise ValidationError("Mod dependencies must use the supported game APIs' object format")
    if not any(item["id"] == "STS2-RitsuLib" for item in dependencies):
        raise ValidationError("Main Mod manifest is missing its STS2-RitsuLib dependency")
    if len(dependencies) != 1:
        raise ValidationError("STS2-RitsuLib must be the only dependency in the player package")
    dll = require_file(payload, f"{mod_id}.dll")
    pck = require_file(payload, f"{mod_id}.pck")
    with pck.open("rb") as stream:
        if stream.read(4) != b"GDPC":
            raise ValidationError("Main Mod PCK has an invalid header")
    expected_files = {manifest_path, dll, pck}
    for path in regular_tree(payload):
        if path != payload and path not in expected_files:
            raise ValidationError(f"Unexpected entry must not be bundled in the main Mod: {path.name}")
    return {"payload_directory": str(payload), "mod_id": mod_id,
            "main_dll_sha256": digest(dll), "main_pck_sha256": digest(pck),
            "main_manifest_sha256": digest(manifest_path)}


def validate(runtime: Path, game: Path, payload: Path | None = None, *,
             report: dict[str, Any] | None = None) -> dict[str, Any]:
    # Update one report incrementally so the CLI retains observed identities even
    # when a later compatibility or integrity check fails.
    if report is None:
        report = {}
    report.update(status="checking", runtime_directory=str(runtime.absolute()),
                  game_directory=str(game.absolute()), expected_version=VERSION,
                  version=None, game_version=None, game_api=None, compat_target=None,
                  game_dll_sha256=None)
    game = safe_path(game)
    release = read_json(require_file(game, "release_info.json"))
    game_version = str(release.get("version", "")).removeprefix("v")
    compat_target = GAME_COMPAT_TARGETS.get(game_version)
    report.update(game_version=game_version, game_api=game_version,
                  game_release=release, compat_target=compat_target)
    report["game_dll_sha256"] = digest(require_file(game, "data_sts2_windows_x86_64/sts2.dll"))
    runtime = safe_path(runtime)
    metadata = read_json(require_file(runtime, "mod_manifest.json"))
    report["version"] = metadata.get("version")
    if compat_target is None:
        raise ValidationError(
            f"This integration targets games {', '.join(GAME_COMPAT_TARGETS)}; found {game_version!r}")
    # The headless runner copies this entire package, including optional viewer
    # files; reject links anywhere in the source before approving that copy.
    for _ in regular_tree(runtime):
        pass
    game_runtime = read_json(require_file(game, "data_sts2_windows_x86_64/sts2.runtimeconfig.json"))
    if game_runtime.get("runtimeOptions", {}).get("tfm") != "net9.0":
        raise ValidationError("The installed game must use net9.0")
    if metadata.get("id") != "STS2-RitsuLib" or metadata.get("version") != VERSION:
        raise ValidationError(f"Expected STS2-RitsuLib {VERSION}, found {metadata.get('id')!r} {metadata.get('version')!r}")
    if metadata.get("has_dll") is not True:
        raise ValidationError("RitsuLib runtime manifest must declare has_dll=true")
    loader = require_file(runtime, "STS2-RitsuLib.dll")
    with loader.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValidationError("RitsuLib loader is not a PE binary")
    variants_manifest = require_file(runtime, "ritsulib-variants.manifest")
    variants = read_json(variants_manifest)
    if variants.get("schema") != 2:
        raise ValidationError("Expected RitsuLib variant manifest schema 2")
    shared = variants.get("shared", [])
    if (not isinstance(shared, list) or any(not isinstance(item, dict) for item in shared)
            or len(shared) != len(SHARED) or {item.get("assembly") for item in shared} != SHARED):
        raise ValidationError("The three shared RitsuLib modules are missing or duplicated")
    checked = [check_assembly(runtime, "shared", item) for item in shared]
    branches = variants.get("variants", [])
    if (not isinstance(branches, list) or any(not isinstance(item, dict) for item in branches)
            or len(branches) != len(VARIANTS)
            or {item.get("compatTarget") for item in branches} != VARIANTS):
        raise ValidationError("The locked RitsuLib runtime must contain all three compatibility variants")
    for branch in branches:
        target = branch["compatTarget"]
        files = branch.get("files", [])
        if (not isinstance(files, list) or any(not isinstance(item, dict) for item in files)
                or len(files) != 2 or {item.get("assembly") for item in files} != VARIANT_MODULES):
            raise ValidationError(f"Invalid module set for compatibility variant {target}")
        checked.extend(check_assembly(runtime, f"compat/{target}", item) for item in files)
    selected = require_file(runtime, f"compat/{compat_target}/compat-target.txt")
    if selected.read_text(encoding="utf-8-sig").strip() != compat_target:
        raise ValidationError("Selected RitsuLib compatibility target does not match the game")
    assets = require_file(runtime, "assets.zip")
    report.update({
        "loader_sha256": digest(loader), "variant_manifest_sha256": digest(variants_manifest),
        "checked_assemblies": checked, "assets_entries": check_assets(assets),
        "assets_sha256": digest(assets),
    })
    if payload is not None:
        report.update(check_payload(payload))
    report["status"] = "passed"
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", required=True, type=Path)
    parser.add_argument("--game", required=True, type=Path)
    parser.add_argument("--payload", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report: dict[str, Any] = {}
    try:
        validate(args.runtime, args.game, args.payload, report=report)
        code = 0
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, RuntimeError) as error:
        report.update(status="failed", error=str(error))
        code = 1
    report["checked_at_utc"] = datetime.now(timezone.utc).isoformat()
    output = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    print(output, end="")
    return code


if __name__ == "__main__":
    sys.exit(main())
