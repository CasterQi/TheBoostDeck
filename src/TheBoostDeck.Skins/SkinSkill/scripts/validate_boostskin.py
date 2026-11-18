#!/usr/bin/env python3
"""Validate a TheBoostDeck .boostskin package before delivery.

Structure checks (ZIP safety, manifest, sizes) are errors. Content checks
(snapshot field names, required control messages, offline assets, preview mode,
extreme overlays) are reported as errors or warnings so the author can fix the
package before a user ever sees it.

Usage:
    python validate_boostskin.py path/to/skin.boostskin
    python validate_boostskin.py path/to/skin.boostskin --platform references/platform.json

Exit codes: 0 valid (warnings allowed), 2 invalid.
"""

from __future__ import annotations

import argparse
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
MAX_SCANNED_SOURCE_BYTES = 8 * 1024 * 1024
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")
PREVIEW_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".gif"}
SOURCE_EXTENSIONS = {".html", ".htm", ".js", ".mjs", ".css"}

SNAPSHOT_REF = re.compile(
    r"\b(?:snapshot|snap)\.([A-Za-z0-9_]+(?:\[[0-9]+\])?"
    r"(?:\.[A-Za-z0-9_]+(?:\[[0-9]+\])?)*)"
)
JS_PROPERTIES = {
    "length", "forEach", "map", "filter", "find", "findIndex", "some", "every",
    "reduce", "slice", "indexOf", "includes", "concat", "join", "keys", "values",
    "entries", "push", "pop", "shift", "unshift", "sort",
}
REMOTE_PATTERNS = [
    (re.compile(r"<script[^>]+src\s*=\s*[\"']https?://", re.I), "remote script"),
    (re.compile(r"<link[^>]+href\s*=\s*[\"']https?://", re.I), "remote stylesheet or icon"),
    (re.compile(r"@import\s+(?:url\()?[\"']?https?://", re.I), "remote CSS @import"),
    (re.compile(r"url\(\s*[\"']?https?://", re.I), "remote asset in CSS url()"),
    (re.compile(r"<img[^>]+src\s*=\s*[\"']https?://", re.I), "remote image"),
]
Z_INDEX = re.compile(r"z-index\s*:\s*(-?\d+)", re.I)


class Report:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def error(self, message: str) -> None:
        self.errors.append(message)

    def warn(self, message: str) -> None:
        self.warnings.append(message)


def safe_name(name: str) -> bool:
    if not name or "\\" in name or "?" in name or "#" in name:
        return False
    if name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        return False
    parts = PurePosixPath(name).parts
    return all(part not in ("", ".", "..") for part in parts)


def collect_snapshot_paths(node, prefix: str, paths: set[str]) -> None:
    if isinstance(node, dict):
        for key, value in node.items():
            path = f"{prefix}.{key}" if prefix else key
            paths.add(path)
            collect_snapshot_paths(value, path, paths)
    elif isinstance(node, list):
        array_path = f"{prefix}[]"
        paths.add(array_path)
        for item in node:
            collect_snapshot_paths(item, array_path, paths)


def load_platform(path: Path, report: Report) -> set[str] | None:
    if not path.is_file():
        report.warn(f"platform reference not found ({path}); snapshot field names were not checked")
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        report.warn(f"could not read {path}: {exc}")
        return None
    snapshot = data.get("snapshot")
    if not isinstance(snapshot, dict) or not snapshot:
        report.warn(f"{path} has no usable snapshot section; field names were not checked")
        return None
    paths: set[str] = set()
    collect_snapshot_paths(snapshot, "", paths)
    return paths


def check_snapshot_fields(source: str, known: set[str], report: Report) -> None:
    roots = {path.split(".")[0].split("[")[0] for path in known}
    seen: set[str] = set()
    for match in SNAPSHOT_REF.finditer(source):
        raw = re.sub(r"\[\d+\]", "[]", match.group(1))
        if raw in seen:
            continue
        seen.add(raw)
        root = raw.split(".")[0].split("[")[0]
        if root not in roots:
            report.error(
                f"snapshot.{raw}: '{root}' is not a field in this machine's snapshot"
            )
            continue
        if raw in known:
            continue
        if raw.split(".")[-1] in JS_PROPERTIES:
            continue
        report.warn(f"snapshot.{raw} is not a documented snapshot field; verify the spelling")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate a TheBoostDeck skin package.")
    parser.add_argument("package", help="path to the .boostskin archive")
    parser.add_argument(
        "--platform",
        default=None,
        help="path to references/platform.json (default: next to this script)",
    )
    args = parser.parse_args()

    report = Report()

    package_path = Path(args.package)
    if not package_path.is_file():
        print(f"ERROR: file not found: {package_path}", file=sys.stderr)
        return 2
    if package_path.stat().st_size > MAX_PACKAGE_BYTES:
        print("ERROR: package exceeds the 256 MB limit", file=sys.stderr)
        return 2

    platform_path = (
        Path(args.platform).expanduser()
        if args.platform
        else Path(__file__).resolve().parent.parent / "references" / "platform.json"
    )
    known_snapshot_paths = load_platform(platform_path, report)

    try:
        with zipfile.ZipFile(package_path) as archive:
            entries = [info for info in archive.infolist() if not info.is_dir()]
            if not entries:
                print("ERROR: archive is empty", file=sys.stderr)
                return 2
            if len(entries) > MAX_ENTRIES:
                print(f"ERROR: archive has more than {MAX_ENTRIES} entries", file=sys.stderr)
                return 2

            names: set[str] = set()
            total = 0
            for info in entries:
                if not safe_name(info.filename):
                    print(f"ERROR: unsafe archive path: {info.filename!r}", file=sys.stderr)
                    return 2
                if info.filename in names:
                    print(f"ERROR: duplicate archive path: {info.filename!r}", file=sys.stderr)
                    return 2
                names.add(info.filename)
                if info.file_size > MAX_ENTRY_BYTES:
                    print(f"ERROR: entry exceeds the 64 MB limit: {info.filename!r}", file=sys.stderr)
                    return 2
                total += info.file_size
                if total > MAX_TOTAL_UNCOMPRESSED_BYTES:
                    print("ERROR: uncompressed content exceeds the 512 MB limit", file=sys.stderr)
                    return 2

            if "skin.json" not in names:
                print("ERROR: skin.json must be at the archive root", file=sys.stderr)
                return 2

            try:
                manifest = json.loads(archive.read("skin.json").decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                print(f"ERROR: skin.json is not valid UTF-8 JSON: {exc}", file=sys.stderr)
                return 2

            if not isinstance(manifest, dict):
                print("ERROR: skin.json must contain a JSON object", file=sys.stderr)
                return 2
            if manifest.get("schemaVersion") != 1:
                print("ERROR: schemaVersion must be 1", file=sys.stderr)
                return 2

            skin_id = manifest.get("id")
            if not isinstance(skin_id, str) or not ID_PATTERN.fullmatch(skin_id):
                print("ERROR: id must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", file=sys.stderr)
                return 2
            if skin_id == "classic":
                print("ERROR: id 'classic' is reserved for the built-in skin", file=sys.stderr)
                return 2

            if not isinstance(manifest.get("name"), str) or not manifest["name"].strip():
                print("ERROR: name must be a non-empty string", file=sys.stderr)
                return 2

            entry = manifest.get("entry", "index.html")
            if not isinstance(entry, str) or not safe_name(entry):
                print("ERROR: entry must be a safe relative path", file=sys.stderr)
                return 2
            if entry not in names:
                print(f"ERROR: entry does not exist in the archive: {entry!r}", file=sys.stderr)
                return 2
            if not entry.lower().endswith((".html", ".htm")):
                print("ERROR: entry must point to an HTML file", file=sys.stderr)
                return 2

            # Preview is mandatory: users judge a skin by its thumbnail.
            preview = manifest.get("preview")
            if not preview:
                report.error(
                    "skin.json must declare 'preview' (run scripts/render_preview.py and point it at the PNG)"
                )
            else:
                if not isinstance(preview, str) or not safe_name(preview):
                    report.error("preview must be a safe relative path")
                elif preview not in names:
                    report.error(f"preview does not exist in the archive: {preview!r}")
                else:
                    if Path(preview).suffix.lower() not in PREVIEW_EXTENSIONS:
                        report.error("preview must be PNG, JPG/JPEG, WEBP, or GIF")
                    if archive.getinfo(preview).file_size > MAX_PREVIEW_BYTES:
                        report.error("preview exceeds the 2 MB limit")

            source_names = [
                name for name in names
                if Path(name).suffix.lower() in SOURCE_EXTENSIONS
            ]
            scanned = 0
            chunks: list[str] = []
            for name in source_names:
                if scanned + archive.getinfo(name).file_size > MAX_SCANNED_SOURCE_BYTES:
                    report.warn(f"source scan skipped the rest of the archive at {name}")
                    break
                raw = archive.read(name)
                scanned += len(raw)
                try:
                    chunks.append(raw.decode("utf-8", errors="replace"))
                except MemoryError:
                    break
            source = "\n".join(chunks)

            if source:
                if "open-settings" not in source and "open-skin-manager" not in source:
                    report.error(
                        "missing a settings/skin entry: send open-settings or open-skin-manager"
                    )
                if "toggle-maximize" not in source:
                    report.error(
                        "missing the required maximize/restore control: send toggle-maximize"
                    )
                elif "maximized" not in source:
                    report.warn(
                        "toggle-maximize is present but settings.maximized is never read; "
                        "the button will not flip to a restore glyph"
                    )
                if "hide-window" not in source and "close-window" not in source:
                    report.error("missing a minimize/exit entry: send hide-window or close-window")
                if "begin-drag" not in source:
                    report.error("missing a drag region sending begin-drag")
                if "user-select" not in source:
                    report.error(
                        "HUD must set user-select:none; selectable text makes WebView2 "
                        "enter a selection gesture that blocks window drag and the grab cursor"
                    )

                if "preview=1" not in source:
                    report.warn(
                        "entry does not handle ?preview=1; the packaged preview may be captured "
                        "from an empty state"
                    )

                for pattern, label in REMOTE_PATTERNS:
                    if pattern.search(source):
                        report.error(f"remote dependency found ({label}); the package must work offline")

                for match in Z_INDEX.finditer(source):
                    try:
                        value = int(match.group(1))
                    except ValueError:
                        continue
                    if value >= 1_000_000:
                        report.error(
                            f"z-index {value} is at or above the host resize overlay; "
                            "it will break window resizing"
                        )
                        break

                if known_snapshot_paths:
                    check_snapshot_fields(source, known_snapshot_paths, report)

            for message in report.warnings:
                print(f"WARNING: {message}", file=sys.stderr)
            if report.errors:
                for message in report.errors:
                    print(f"ERROR: {message}", file=sys.stderr)
                return 2

            print(
                "OK: "
                f"{skin_id} ({manifest['name']}), entry={entry}, preview={preview}, "
                f"{len(entries)} file(s), {total} uncompressed byte(s)"
            )
            return 0
    except zipfile.BadZipFile:
        print("ERROR: file is not a valid ZIP archive", file=sys.stderr)
        return 2
    except OSError as exc:
        print(f"ERROR: could not read package: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
