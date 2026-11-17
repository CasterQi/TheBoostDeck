# .boostskin format

A `.boostskin` file is a ZIP archive. The archive root must contain `skin.json`; all runtime resources are relative to that root.

## Manifest

```json
{
  "schemaVersion": 1,
  "id": "your-skin-id",
  "name": "Your Skin",
  "version": "1.0.0",
  "author": "optional",
  "description": "optional",
  "entry": "index.html",
  "preview": "preview.png",
  "window": {
    "width": 1440,
    "height": 900,
    "minWidth": 720,
    "minHeight": 405,
    "background": "#0b1118"
  }
}
```

### Fields

- `schemaVersion`: must be `1`.
- `id`: required; `^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$`; `classic` is reserved for the built-in skin.
- `name`: display name.
- `version`: defaults to `1.0.0`.
- `author` and `description`: optional metadata.
- `entry`: relative HTML path; defaults to `index.html`.
- `preview`: optional thumbnail path; PNG, JPG, JPEG, WEBP, or GIF; maximum 2 MB when read by the manager.
- `window`: optional initial bounds and background. The host remains responsible for the actual window; do not assume a fixed size.

## Path and size limits

- Package: 256 MB maximum.
- Entries: 2000 maximum.
- One entry: 64 MB maximum uncompressed.
- Total uncompressed size: 512 MB maximum.
- Reject absolute paths, `..` segments, drive letters, query strings, and URL-like resource paths.
- Use forward slashes in ZIP entries and relative resource paths in HTML/CSS/JS.

## Entry and URL mapping

The manager exposes the skin root at:

```text
https://skin.theboostdeck/<entry>
```

Relative references from `index.html` continue to resolve normally. Load JavaScript, CSS, images, and fonts from inside the package; do not depend on remote CDNs because the skin must work offline.

## Preview

The manager reads `preview` bytes and falls back to a placeholder when the file is missing or invalid. A good preview is a real screenshot of the skin at its intended aspect ratio, not a mockup with invented values.

## Suggested archive

```text
skin.json
index.html
preview.png
assets/
  fonts/
  images/
```

For a minimal package, `skin.json`, `index.html`, and `preview.png` are enough.
