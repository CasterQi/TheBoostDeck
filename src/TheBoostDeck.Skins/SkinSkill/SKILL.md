---
name: theboostdeck-skin-author
description: Create, redesign, or extend a TheBoostDeck performance HUD as a distributable .boostskin package. Use when the user asks for a custom gaming secondary-display dashboard, a skin/theme, a visual redesign from a reference image, or a telemetry layout for CPU, GPU, memory, storage, network, motherboard, fans, or temperatures. This exported skill contains this machine's complete sensor inventory, latest snapshot, and safe derived-metric candidates; read them before designing so sensors this machine cannot read are omitted instead of becoming empty widgets.
---

# TheBoostDeck Skin Author

Create a complete `.boostskin` package, not just a static mockup.

## Start with this machine

1. Read `references/platform-summary.md` first, then `references/platform.json`.
2. Treat every hardware and sensor name as literal source data. Do not translate, rename, or "clean up" names reported by the hardware layer.
3. Use `platform.json` to learn what this machine exposes. `snapshot` contains the canonical fields delivered to a running skin. `sensors` is the full raw inventory for discovery; a raw sensor that is not represented in `snapshot` is not available to the skin UI yet.
4. Build the design only from metrics marked `available` in the platform inventory. Metrics and hardware groups that are `unavailable` on this machine must be omitted from the layout; do not reserve empty cards, rows, or "N/A" slots for them.
5. Read `references/derived-metrics.md` before adding a computed value. `platform.json.derivedMetrics` lists formulas that can be calculated from this machine's direct `snapshot` fields; it is a starting point, not a live data feed.
6. Never invent a value, a sensor, a hardware capability, or a conversion factor that is not in the inventory or snapshot.

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

1. Read the platform files. Mark which canonical metrics are `available`; keep only those metrics in the design and omit metrics/groups that are `unavailable` on this machine.
2. Decide the visual direction from the user's reference image or description. When the request is underspecified, choose a coherent direction rather than producing a generic card grid.
3. Plan the layout for both 16:9 landscape and 9:16 portrait, composed from the available metrics only. If a previously available metric drops out during runtime, the layout must collapse or show a compact no-data state without breaking the composition.
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

## Derived values

The host does not send every useful display value. You may compute additional values from canonical `snapshot` fields when the formula is deterministic, every input is available in that same snapshot, and the result is labeled as derived in the UI.

- Start from `platform.json.derivedMetrics` and `references/derived-metrics.md`.
- Good examples are free memory (`total - used`), free VRAM, drive free space, combined network throughput, and CPU + GPU power.
- Prefer a direct host metric when one exists; use the derivation only as a fallback or a genuinely new presentation value.
- Never derive FPS, fan RPM from PWM, total PSU power, per-core values from aggregate load, or a value from a raw sensor that is not in `snapshot`.
- If an input is unavailable in the platform inventory, omit the derived value from the design. If a previously available input drops out during runtime, hide or collapse the derived widget, or show a compact no-data state; never substitute zero.

## Design requirements

- Read `references/design-guide.md` before styling. Match the TheBoostDeck visual language unless the user explicitly asks for another direction.
- Prioritize a high-signal HUD: large primary values, quiet secondary labels, readable at a glance from a secondary display.
- Landscape and portrait must both be first-class. Use CSS grid/flex and container queries or aspect-ratio media queries; never hardcode a single viewport size.
- **Widget set comes from platform availability.** Omit metrics marked `unavailable` on this machine; do not reserve empty cards, rows, or "N/A" slots. Omit an entire hardware group when all its metrics are unavailable, and rebalance the available modules.
- A metric that was available at export but drops out during runtime is a different case: collapse it or show one compact no-data state. Do not show zero unless zero is a real reading.
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

1. Every live widget is backed by a metric marked `available` in the platform inventory; no permanently unavailable metric is shown.
2. The layout has been checked at a landscape and a portrait aspect ratio.
3. No sensor or hardware name has been translated or fabricated.
4. The archive has `skin.json` at its root and the entry file exists.
5. `validate_boostskin.py` passes.
6. Every computed value uses only available `snapshot` inputs, has a documented formula, and is labeled as derived.
