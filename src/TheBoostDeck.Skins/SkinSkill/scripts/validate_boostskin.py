#!/usr/bin/env python3
"""Validate a TheBoostDeck .boostskin package before delivery.

Usage:
    python validate_boostskin.py path/to/skin.boostskin
"""

from __future__ import annotations

import json
import re
import sys
import zipfile
from pathlib import Path, PurePosixPath

MAX_PACKAGE_BYTES = 256 * 1024 * 1024
MAX_ENTRIES = 2000
MAX_ENTRY_BYTES = 64 * 1024 * 1024
MAX_TOTAL_UNCOMPRESSED_BYTES = 512 * 1024 * 1024
MAX_PREVIEW_BYTES = 2 * 1024 * 1024
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")
PREVIEW_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".gif"}


def fail(message: str) -> int:
    print(f"ERROR: {message}", file=sys.stderr)
    return 2


def safe_name(name: str) -> bool:
    if not name or "\\" in name or "?" in name or "#" in name:
        return False
    if name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        return False
    parts = PurePosixPath(name).parts
    return all(part not in ("", ".", "..") for part in parts)


def main() -> int:
    if len(sys.argv) != 2:
        return fail("usage: python validate_boostskin.py <skin.boostskin>")

    package_path = Path(sys.argv[1])
    if not package_path.is_file():
        return fail(f"file not found: {package_path}")
    if package_path.stat().st_size > MAX_PACKAGE_BYTES:
        return fail("package exceeds the 256 MB limit")

    try:
        with zipfile.ZipFile(package_path) as archive:
            entries = [info for info in archive.infolist() if not info.is_dir()]
            if not entries:
                return fail("archive is empty")
            if len(entries) > MAX_ENTRIES:
                return fail(f"archive has more than {MAX_ENTRIES} entries")

            names: set[str] = set()
            total = 0
            for info in entries:
                if not safe_name(info.filename):
                    return fail(f"unsafe archive path: {info.filename!r}")
                if info.filename in names:
                    return fail(f"duplicate archive path: {info.filename!r}")
                names.add(info.filename)
                if info.file_size > MAX_ENTRY_BYTES:
                    return fail(f"entry exceeds the 64 MB limit: {info.filename!r}")
                total += info.file_size
                if total > MAX_TOTAL_UNCOMPRESSED_BYTES:
                    return fail("uncompressed content exceeds the 512 MB limit")

            if "skin.json" not in names:
                return fail("skin.json must be at the archive root")

            try:
                manifest = json.loads(archive.read("skin.json").decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                return fail(f"skin.json is not valid UTF-8 JSON: {exc}")

            if not isinstance(manifest, dict):
                return fail("skin.json must contain a JSON object")
            if manifest.get("schemaVersion") != 1:
                return fail("schemaVersion must be 1")

            skin_id = manifest.get("id")
            if not isinstance(skin_id, str) or not ID_PATTERN.fullmatch(skin_id):
                return fail("id must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")
            if skin_id == "classic":
                return fail("id 'classic' is reserved for the built-in skin")

            if not isinstance(manifest.get("name"), str) or not manifest["name"].strip():
                return fail("name must be a non-empty string")

            entry = manifest.get("entry", "index.html")
            if not isinstance(entry, str) or not safe_name(entry):
                return fail("entry must be a safe relative path")
            if entry not in names:
                return fail(f"entry does not exist in the archive: {entry!r}")
            if not entry.lower().endswith((".html", ".htm")):
                return fail("entry must point to an HTML file")

            preview = manifest.get("preview")
            if preview is not None:
                if not isinstance(preview, str) or not safe_name(preview):
                    return fail("preview must be a safe relative path")
                if preview not in names:
                    return fail(f"preview does not exist in the archive: {preview!r}")
                if Path(preview).suffix.lower() not in PREVIEW_EXTENSIONS:
                    return fail("preview must be PNG, JPG/JPEG, WEBP, or GIF")
                if archive.getinfo(preview).file_size > MAX_PREVIEW_BYTES:
                    return fail("preview exceeds the 2 MB limit")

            print(
                "OK: "
                f"{skin_id} ({manifest['name']}), entry={entry}, "
                f"{len(entries)} file(s), {total} uncompressed byte(s)"
            )
            return 0
    except zipfile.BadZipFile:
        return fail("file is not a valid ZIP archive")
    except OSError as exc:
        return fail(f"could not read package: {exc}")


if __name__ == "__main__":
    raise SystemExit(main())
