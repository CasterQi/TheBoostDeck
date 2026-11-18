#!/usr/bin/env python3
"""Render a .boostskin preview image with a local headless browser.

The skin entry must support ?preview=1 (see references/protocol.md): it renders
representative sample values when the host is absent, so the screenshot looks
like the real HUD instead of an empty skeleton.

Usage:
    python render_preview.py <skin-folder> [--entry index.html] [--out preview.png]
                             [--width 1280] [--height 720] [--browser path/to/msedge]

Exit codes: 0 success, 2 failure.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

MAX_PREVIEW_BYTES = 2 * 1024 * 1024

WINDOWS_CANDIDATES = [
    r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
    r"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
    r"%LocalAppData%\Google\Chrome\Application\chrome.exe",
    r"%LocalAppData%\Microsoft\Edge\Application\msedge.exe",
]

POSIX_CANDIDATES = [
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
]


def find_browser(explicit: str | None) -> Path | None:
    if explicit:
        candidate = Path(explicit).expanduser()
        return candidate if candidate.is_file() else None

    for name in ("msedge", "chrome", "google-chrome", "chromium"):
        found = shutil.which(name)
        if found:
            return Path(found)

    candidates = WINDOWS_CANDIDATES if os.name == "nt" else POSIX_CANDIDATES
    for raw in candidates:
        candidate = Path(os.path.expandvars(raw))
        if candidate.is_file():
            return candidate
    return None


def file_url(entry: Path) -> str:
    return entry.resolve().as_uri() + "?preview=1"


def try_render(browser: Path, mode: str, url: str, out: Path, width: int, height: int) -> bool:
    profile = tempfile.mkdtemp(prefix="tbd-preview-")
    command = [
        str(browser),
        f"--headless={mode}",
        "--disable-gpu",
        "--hide-scrollbars",
        "--no-first-run",
        "--no-default-browser-check",
        "--force-device-scale-factor=1",
        "--virtual-time-budget=5000",
        f"--user-data-dir={profile}",
        f"--window-size={width},{height}",
        f"--screenshot={out}",
        url,
    ]
    try:
        subprocess.run(
            command,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=90,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    finally:
        shutil.rmtree(profile, ignore_errors=True)

    return out.is_file() and out.stat().st_size > 1024


def main() -> int:
    parser = argparse.ArgumentParser(description="Render a TheBoostDeck skin preview image.")
    parser.add_argument("skin_folder", help="folder containing the unpacked skin")
    parser.add_argument("--entry", default="index.html", help="entry HTML file relative to the folder")
    parser.add_argument("--out", default=None, help="output PNG path (default: <folder>/preview.png)")
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument("--browser", default=None, help="explicit browser executable")
    args = parser.parse_args()

    folder = Path(args.skin_folder).resolve()
    entry = (folder / args.entry).resolve()
    if not entry.is_file():
        print(f"ERROR: entry not found: {entry}", file=sys.stderr)
        return 2

    out = Path(args.out).resolve() if args.out else folder / "preview.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()

    browser = find_browser(args.browser)
    if browser is None:
        print(
            "ERROR: no Chromium-based browser found. Pass --browser <msedge.exe> or "
            "render the preview with another image tool that reproduces the real layout.",
            file=sys.stderr,
        )
        return 2

    url = file_url(entry)
    rendered = False
    for mode in ("new", "old"):
        if try_render(browser, mode, url, out, args.width, args.height):
            rendered = True
            break

    if not rendered:
        print(
            "ERROR: the browser did not produce a screenshot. Check that the entry loads "
            "standalone and supports ?preview=1.",
            file=sys.stderr,
        )
        return 2

    size = out.stat().st_size
    if size > MAX_PREVIEW_BYTES:
        print(
            f"WARNING: preview is {size} bytes, above the 2 MB package limit. "
            "Reduce it to PNG-8, JPG, or WEBP before packaging.",
            file=sys.stderr,
        )

    print(f"{browser}: {out} ({size} bytes, {args.width}x{args.height})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
