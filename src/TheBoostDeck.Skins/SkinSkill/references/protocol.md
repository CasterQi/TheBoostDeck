# Host protocol

The skin runs inside WebView2 at `https://skin.theboostdeck/<entry>`. Use `window.chrome.webview` when it exists; in a plain browser, guard every call so the page still renders with sample data.

## Preview mode (`?preview=1`)

Every skin must render a complete, presentable state when no host is present, because the packaged preview image is captured outside the host:

- When `window.chrome.webview` is absent, render the whole layout immediately with representative sample values instead of blanks or zeros.
- When the URL contains `?preview=1`, use the same sample data even if the host exists, and keep the page static — no animation loops.
- Sample values must stay plausible for the exported platform's units: CPU 35–70 %, GPU 40–95 %, memory 40–80 %, storage 30–85 %, temperatures 35–85 °C. Never show absurd or placeholder numbers.
- The preview state must match the live look — same hierarchy, typography, palette, spacing. Do not add a "sample data" watermark; it would end up in the thumbnail.
- Preview must not depend on network access or remote assets.

## Host to skin

### `snapshot`

Sent at the host's current telemetry interval and immediately after navigation completes. The interval defaults to one second and can be read from `settings.refreshIntervalMs`; snapshots follow that cadence after the initial send.

```json
{
  "type": "snapshot",
  "data": {
    "timestamp": "2026-11-17T12:34:56.789+08:00",
    "machineName": "DESKTOP",
    "cpu": {
      "name": "CPU name",
      "usage": { "value": 12.4, "unit": "%", "status": "available", "reason": null, "source": "..." },
      "temperature": { "value": 48.0, "unit": "°C", "status": "available", "reason": null, "source": "..." },
      "power": { "value": 65.0, "unit": "W", "status": "available", "reason": null, "source": "..." },
      "frequency": { "value": 4200.0, "unit": "MHz", "status": "available", "reason": null, "source": "..." },
      "fanSpeed": { "value": null, "unit": "RPM", "status": "unavailable", "reason": "...", "source": null }
    },
    "gpu": {
      "name": "GPU name",
      "kind": "discrete",
      "usage": { "...": "same MetricValue shape" },
      "memoryUsed": { "...": "MetricValue" },
      "memoryTotal": { "...": "MetricValue" },
      "memoryUsage": { "...": "MetricValue" }
    },
    "memory": { "...": "same MetricValue shape" },
    "storage": {
      "driveCount": 3,
      "totalCapacity": { "value": 2048.0, "unit": "GB", "status": "available", "reason": null, "source": "..." },
      "usedTotal": { "value": 1100.0, "unit": "GB", "status": "available", "reason": null, "source": "..." },
      "drives": [
        {
          "name": "Drive label",
          "label": "Drive label",
          "used": { "...": "MetricValue" },
          "total": { "...": "MetricValue" },
          "usage": { "...": "MetricValue" }
        }
      ],
      "readRate": { "...": "MetricValue" },
      "writeRate": { "...": "MetricValue" }
    },
    "network": { "name": "Active adapter", "download": {}, "upload": {} },
    "system": {
      "motherboardTemperature": {},
      "power": {},
      "powerSource": "AC",
      "fans": [
        {
          "name": "CPU Fan",
          "hardware": "Nuvoton NCT6798D",
          "role": "cpu",
          "speed": { "value": 1200, "unit": "RPM", "status": "available" }
        }
      ]
    },
    "sensorDriver": { "name": "PawnIO", "status": "ready", "ready": true, "version": "..." }
  }
}
```

Every leaf metric follows `MetricValue`:

- `value`: number or `null`.
- `unit`: string.
- `status`: `available` or `unavailable`.
- `reason`: string or `null`.
- `source`: string or `null`.

`CpuMetrics.Name` and `GpuMetrics.Name` are strings. `GpuMetrics.Kind` is one of `discrete`, `integrated`, `none`, or `unknown`; use it to decide whether the GPU module earns its space (`none` means no physical GPU was enumerated, and every GPU metric will be unavailable). `DriveMetrics.Name` and `.Label` are strings. `StorageMetrics.DriveCount`, `.TotalCapacity`, and `.UsedTotal` summarize the fixed volumes, so a skin does not have to re-sum them. `SystemMetrics.Fans` lists every readable non-GPU fan as `{ name, hardware, role, speed }`, ordered CPU-first; `role` is `cpu` or `system`, and GPU fans stay in `gpu.fanSpeed`. A `0` value is a real reading, not a failure: the CPU fan may legitimately sit at 0, while system headers reading 0 are usually unpopulated and should be skipped when showing extras. Treat a missing value, a null value, or a non-`available` status as unavailable. If the metric was already unavailable in the platform inventory, do not create a widget for it. For an unexpected runtime loss of a previously available metric, show a compact no-data state; never fabricate a number.

### `settings`

```json
{
  "type": "settings",
  "topmost": true,
  "visible": true,
  "maximized": false,
  "language": "zh-CN",
  "refreshIntervalMs": 1000,
  "zoomPercent": 100,
  "startup": false,
  "version": "0.1.1",
  "buildDate": "0a20"
}
```

`refreshIntervalMs` is the current telemetry sampling interval. Use it for labels or cadence hints; do not drive a separate polling loop from it. `startup`, `version`, and `buildDate` are host preferences/metadata; skins may display them but must not try to change startup or version state themselves.

`language` is `zh-CN` or `en-US` and drives every label the skin owns. Render both string sets and switch on this value; ignore `navigator.language`. Hardware-reported sensor names stay verbatim in whatever language the sensor uses, even when it differs from `language`.

`zoomPercent` (50–200) is the host's WebView zoom, set from the control center slider or the tray menu. It is informational: do not apply your own scaling on top of it, since the whole page is already zoomed by the host.

### `collection-error`

```json
{ "type": "collection-error", "messageKey": "collectionFailed" }
```

This means the collector is degraded. Keep the last known values if useful, but mark them stale; never present them as live.

### `skin-catalog`

Sent to the host manager page after it requests the list. External skins do not render this message.

```json
{
  "type": "skin-catalog",
  "activeId": "classic",
  "skins": [
    {
      "id": "classic",
      "name": "Classic HUD",
      "version": "built-in",
      "author": "TheBoostDeck",
      "description": "Built-in adaptive HUD.",
      "isBuiltIn": true,
      "canExport": false,
      "previewUrl": "data:image/png;base64,..."
    }
  ]
}
```

`previewUrl` is a data URL and may be `null`; the host manager shows a neutral placeholder when it is missing or fails to load.

### `skin-action`

Sent to the host manager page after a skin manager operation:

```json
{ "type": "skin-action", "action": "select", "ok": true, "message": "Skin switched." }
```

`action` is `select`, `import`, `export`, or `export-skill`. When `ok` is `false`, the host manager keeps the panel open and shows the message.

## Skin to host

Send an object through `window.chrome.webview.postMessage(...)`:

```js
window.chrome?.webview?.postMessage({ type: "toggle-topmost" });
```

Window actions:

- `{ type: "begin-drag" }`
- `{ type: "toggle-topmost" }`
- `{ type: "hide-window" }`
- `{ type: "toggle-maximize" }`
- `{ type: "close-window" }`
- `{ type: "set-language", language: "zh-CN" | "en-US" }`

Settings and skin manager actions:

- `{ "type": "open-settings" }` → the host opens its shared control center (General as the landing page, then Skins and About). The Skins tab contains the catalog, previews, switching, import, export, delete, and export-skill. This is the required entry for every skin.
- `{ "type": "open-skin-manager" }` → optional direct shortcut that opens the same host panel on the Skins tab. The skin does not receive `skin-catalog` or `skin-action`; those messages belong to the host panel.

## Required controls

Every custom skin must provide this minimum set:

1. **Settings or skin switch — at least one of the two.** `{ type: "open-settings" }` opens the shared control center on General; `{ type: "open-skin-manager" }` opens the same panel on the Skins tab. One button satisfies the requirement. Do not copy or embed settings/manager HTML/CSS/JS in the skin package.
2. **Maximize / restore — required.** `{ type: "toggle-maximize" }` toggles the window; render **restore** while `settings.maximized` is `true` and **maximize** otherwise. The host re-sends `settings` on every state change, so update the glyph from that message rather than tracking your own flag.
3. **Minimize or close — at least one of the two.** `{ type: "hide-window" }` is the host's minimize/collect action (the window is tucked away and restored from the tray or taskbar; the host also treats a native minimize the same way). `{ type: "close-window" }` exits the app. Never rely on the tray icon as the only exit from a full-screen skin.
4. **Visible drag region — required.** `{ type: "begin-drag" }`, working in both landscape and portrait. **Recommended:** the entire title/header strip, excluding buttons, links, inputs, and other interactive controls from the drag hit area. A narrower strip, edge grip, or dedicated handle is also acceptable if visible and reachable.

Position is free, and a single hub may combine the settings/skin entry with hide/exit. When space is tight, drop labels before dropping any required capability.

A single control hub may combine the control-center and hide/exit entries. Position is free; the controls must remain visible and reachable in both orientations.

## Host-reserved window edges

The host has no window frame and WebView2 fills the whole client area, so the host injects an invisible resize overlay into every skin page after navigation. It needs no skin code, but its pixels are reserved:

- 6 CSS px along each edge and 12 CSS px at each corner belong to window resizing. The cursor changes there, and pressing resizes the window instead of activating skin content.
- Keep interactive controls at least 16 px from the window edge and clear of the corners; never expose a control whose only hit area is a thin edge sliver.
- Decorative art, frames, and backgrounds may still run to the edge — the overlay is transparent and only captures pointer input.
- The top 6 px resizes, so a drag surface starts below it. The overlay sits at the maximum CSS `z-index`; do not paint above it or implement your own resize grips.
- The host hides the overlay while the window is maximized; the skin does not manage that state.

## Data boundary

The `snapshot` payload is the live data contract. The exported `platform.json` also contains a `sensors` array with every raw sensor that the hardware collector can enumerate. That array is discovery metadata: a raw sensor not represented in `snapshot` is not currently delivered to the skin. Do not build a live widget that reads an unsupported raw field.

## Derived values

A skin may compute presentation values from two or more canonical `snapshot` fields. The host does not publish these derived values; the skin owns the calculation. `platform.json.derivedMetrics` is a generated candidate list, and `references/derived-metrics.md` documents the allowed formulas.

Derived values must:

- use only `available` fields from the same snapshot;
- use the host's units and avoid invented conversion assumptions;
- be recomputed on every snapshot;
- be labeled as derived in the UI;
- omit the derived widget when every required input is unavailable in the platform inventory; for a sum such as CPU+GPU power, a single missing component may be treated as `0` and the result labelled partial (`CPU + GPU 功耗，GPU 未提供`), but the widget must disappear when both inputs are missing;
- for a previously available input that drops out at runtime, hide/collapse the widget or use a compact no-data state.

Do not derive FPS, fan RPM from fan control, total PSU power, per-core values from aggregate CPU load, or any value from a raw-only sensor.
