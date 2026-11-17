# Host protocol

The skin runs inside WebView2 at `https://skin.theboostdeck/<entry>`. Use `window.chrome.webview` when it exists; in a plain browser, guard every call so the page still renders with sample data.

## Host to skin

### `snapshot`

Sent on a one-second cadence and immediately after navigation completes.

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
    "gpu": { "...": "same MetricValue shape" },
    "memory": { "...": "same MetricValue shape" },
    "storage": {
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
    "system": { "motherboardTemperature": {}, "power": {}, "powerSource": "AC" },
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

`CpuMetrics.Name` and `GpuMetrics.Name` are strings. `DriveMetrics.Name` and `.Label` are strings. Treat a missing value, a null value, or a non-`available` status as unavailable. If the metric was already unavailable in the platform inventory, do not create a widget for it. For an unexpected runtime loss of a previously available metric, show a compact no-data state; never fabricate a number.

### `settings`

```json
{
  "type": "settings",
  "topmost": true,
  "visible": true,
  "maximized": false,
  "language": "zh-CN",
  "refreshIntervalMs": 1000
}
```

`refreshIntervalMs` is the current telemetry sampling interval. Use it for labels or cadence hints; do not drive a separate polling loop from it.

### `collection-error`

```json
{ "type": "collection-error", "messageKey": "collectionFailed" }
```

This means the collector is degraded. Keep the last known values if useful, but mark them stale; never present them as live.

### `skin-catalog`

Sent after `{ type: "request-skins" }`. Render the list in the skin's manager panel and send `set-skin` when the user chooses one.

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

`previewUrl` is a data URL and may be `null`; show a neutral placeholder when it is missing or fails to load.

### `skin-action`

Sent after a skin manager operation:

```json
{ "type": "skin-action", "action": "select", "ok": true, "message": "Skin switched." }
```

`action` is `select`, `import`, `export`, or `export-skill`. When `ok` is `false`, keep the manager panel open and show the message.

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

Skin manager actions (required for the skin's visible skin-switch entry; import/export may remain inside that panel):

- `{ type: "request-skins" }` → host sends `skin-catalog`.
- `{ type: "set-skin", id: "..." }`
- `{ type: "import-skin" }`
- `{ type: "export-skin", id: "..." }`

## Required controls

Every custom skin must provide:

1. A visible skin manager/switch entry. Copy the shared `assets/skin-manager/` component, include its HTML/CSS/JS in the package, and mount it from the trigger. It sends `request-skins`, renders the returned `skin-catalog`, and sends `set-skin`. The host does not provide a shared manager popup; the shared Skill component is the reusable panel. Import/export actions live in the same panel instead of separate top-level buttons.
2. A visible hide or exit entry. Send `hide-window` or `close-window`; do not rely only on the tray icon.
3. A visible drag region. Send `begin-drag`; it must work in both landscape and portrait.

A single control hub may combine the skin manager and hide/exit entries. Position is free; the controls must remain visible and reachable in both orientations.

## Data boundary

The `snapshot` payload is the live data contract. The exported `platform.json` also contains a `sensors` array with every raw sensor that the hardware collector can enumerate. That array is discovery metadata: a raw sensor not represented in `snapshot` is not currently delivered to the skin. Do not build a live widget that reads an unsupported raw field.

## Derived values

A skin may compute presentation values from two or more canonical `snapshot` fields. The host does not publish these derived values; the skin owns the calculation. `platform.json.derivedMetrics` is a generated candidate list, and `references/derived-metrics.md` documents the allowed formulas.

Derived values must:

- use only `available` fields from the same snapshot;
- use the host's units and avoid invented conversion assumptions;
- be recomputed on every snapshot;
- be labeled as derived in the UI;
- omit the derived widget when any required input is already unavailable in the platform inventory; if a previously available input drops out at runtime, hide/collapse it or use a compact no-data state.

Do not derive FPS, fan RPM from fan control, total PSU power, per-core values from aggregate CPU load, or any value from a raw-only sensor.
