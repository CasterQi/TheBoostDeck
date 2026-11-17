---
name: theboostdeck-skin-author
description: Create, redesign, or extend a TheBoostDeck performance HUD as a distributable .boostskin package. Use when the user asks for a custom gaming secondary-display dashboard, a skin/theme, a visual redesign from a reference image, or a telemetry layout for CPU, GPU, memory, storage, network, motherboard, fans, or temperatures. This exported skill contains this machine's complete sensor inventory and latest snapshot; read it before designing so unavailable sensors do not become broken widgets.
---

# TheBoostDeck Skin Author

Create a complete `.boostskin` package, not just a static mockup.

## Start with this machine

1. Read `references/platform-summary.md` first, then `references/platform.json`.
2. Treat every hardware and sensor name as literal source data. Do not translate, rename, or "clean up" names reported by the hardware layer.
3. Use `platform.json` to learn what this machine exposes. `snapshot` contains the canonical fields delivered to a running skin. `sensors` is the full raw inventory for discovery; a raw sensor that is not represented in `snapshot` is not available to the skin UI yet.
4. Never invent a value, a sensor, or a hardware capability that is not in the inventory.

## Deliverable

- A ZIP archive with the `.boostskin` extension.
- `skin.json` must be at the archive root, with `index.html` as the entry unless the manifest specifies another safe relative path.
- Include a `preview` image when possible: PNG, JPG/JPEG, WEBP, or GIF, at most 2 MB.
- Keep all runtime resources inside the package. Use relative paths and local assets; do not depend on CDN or network access.
- Use `scripts/validate_boostskin.py` before delivery:

```bash
python scripts/validate_boostskin.py path/to/your-skin.boostskin
```

## Workflow

1. Read the platform files. Note which canonical metrics are `available` and which are unavailable.
2. Decide the visual direction from the user's reference image or description. When the request is underspecified, choose a coherent direction rather than producing a generic card grid.
3. Plan the layout for both 16:9 landscape and 9:16 portrait. The layout must remain usable when a metric or an entire hardware group is unavailable.
4. Build the skin:
   - Use plain HTML, CSS, and JavaScript. No React, build step, bundler, or server.
   - Keep the main entry self-contained, or use relative local assets inside the package.
   - Use the runtime message contract in `references/protocol.md`.
   - Do not implement FPS; it is intentionally out of scope.
5. Package the files into a ZIP and rename it to `.boostskin`.
6. Run the validator and fix every error before delivering.
7. Report the final path and mention any metrics the layout deliberately omits because this machine does not expose them.

## Runtime contract in one paragraph

The host sends JSON messages through `window.chrome.webview` when available: `snapshot`, `settings`, and `collection-error`. The skin sends window actions (`begin-drag`, `toggle-topmost`, `hide-window`, `toggle-maximize`, `close-window`) and language selection (`set-language`). Treat every message as untrusted input and update the DOM defensively. `snapshot` is the only supported source for live values; the detailed field shape is in `references/protocol.md`.

## Design requirements

- Read `references/design-guide.md` before styling. Match the TheBoostDeck visual language unless the user explicitly asks for another direction.
- Prioritize a high-signal HUD: large primary values, quiet secondary labels, readable at a glance from a secondary display.
- Landscape and portrait must both be first-class. Use CSS grid/flex and container queries or aspect-ratio media queries; never hardcode a single viewport size.
- A missing metric must degrade to a deliberate placeholder such as `—` or "No data", with a short reason when useful. Do not show zero unless zero is a real reading.
- Keep resource usage low: no continuous animation loops that are not needed, no large dependencies, no polling faster than the host snapshot cadence.
- Use the platform's language conventions. Preserve raw sensor names; UI labels may use the user's language.

## Package rules

- `schemaVersion` must be `1`.
- `id` must match `^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$` and must not be `classic`.
- Reject absolute paths, `..`, drive letters, query strings, and URLs in package paths.
- Do not exceed: 256 MB package, 2000 entries, 64 MB per entry, 512 MB uncompressed total.
- Read `references/skin-format.md` for the complete manifest and packaging rules.

## Quality gate

Before finishing, confirm:

1. Every live value in the UI comes from `snapshot`, with a safe unavailable state.
2. The layout has been checked at a landscape and a portrait aspect ratio.
3. No sensor or hardware name has been translated or fabricated.
4. The archive has `skin.json` at its root and the entry file exists.
5. `validate_boostskin.py` passes.
