from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def text(element: ET.Element | None) -> str:
    return (element.text or "").strip() if element is not None and element.text else ""


def main() -> None:
    project_root = Path(sys.argv[1]).resolve()
    assets_path = project_root / "src/TheBoostDeck.App/obj/project.assets.json"
    output_path = project_root / "docs/dependency-inventory.md"
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    package_folders = [Path(value) for value in assets["packageFolders"]]
    rows: list[tuple[str, str, str, str, str]] = []

    for key, library in sorted(assets.get("libraries", {}).items(), key=lambda item: item[0].lower()):
        if library.get("type") != "package":
            continue
        name, version = key.rsplit("/", 1)
        relative_nuspec = f"{name.lower()}.nuspec"
        nuspec = next((folder / library["path"] / relative_nuspec for folder in package_folders
                       if (folder / library["path"] / relative_nuspec).exists()), None)
        license_value = "Not declared in package metadata"
        project_url = ""
        if nuspec:
            root = ET.parse(nuspec).getroot()
            metadata = next((child for child in root if child.tag.endswith("metadata")), None)
            if metadata is not None:
                license_element = next((child for child in metadata if child.tag.endswith("license")), None)
                license_url_element = next((child for child in metadata if child.tag.endswith("licenseUrl")), None)
                project_url_element = next((child for child in metadata if child.tag.endswith("projectUrl")), None)
                license_value = text(license_element) or text(license_url_element) or license_value
                project_url = text(project_url_element)
        rows.append((name, version, license_value, project_url, str(nuspec) if nuspec else "Not found"))

    lines = [
        "# Dependency inventory",
        "",
        "Generated from `src/TheBoostDeck.App/obj/project.assets.json` and local NuGet package metadata.",
        "This inventory covers NuGet packages in the application dependency graph. The self-contained .NET runtime is covered separately in `THIRD-PARTY-NOTICES.md`.",
        "",
        "| Package | Version | Declared license | Project |",
        "|---|---|---|---|",
    ]
    for name, version, license_value, project_url, _ in rows:
        license_cell = license_value.replace("|", "\\|")
        project_cell = f"[Repository]({project_url})" if project_url else "Not declared"
        lines.append(f"| {name} | {version} | {license_cell} | {project_cell} |")
    lines.extend(["", "## Metadata locations", "", "Local paths below are build-machine details and are not required by end users.", ""])
    for name, version, _, _, nuspec in rows:
        lines.append(f"- `{name} {version}`: `{nuspec}`")
    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {output_path} with {len(rows)} packages")


if __name__ == "__main__":
    main()
