# Derived metrics

The host sends only the canonical `snapshot` fields. A skin may calculate additional values from those direct fields; this is a client-side presentation concern, not a new host data source. The generated `platform.json` includes a `derivedMetrics` candidate list with formulas and availability at export time.

## Rules

1. Derive only when the formula is deterministic and every input is a canonical `snapshot` field that is `available` in the same snapshot.
2. Use the units from the snapshot. Do not assume conversion factors, base-2 vs base-10 storage units, or a power-supply constant that the host did not provide.
3. Prefer a direct host field when one exists. For example, use `memory.usage` instead of re-deriving used percentage.
4. Recompute on every `snapshot`; never keep a stale derived value.
5. Mark the value as derived in the UI (a small label, tooltip, or detail line). Never present an estimate as a measured sensor value.
6. If an input is marked `unavailable` in the platform inventory, omit the derived value from the layout. If a previously available input drops out at runtime, hide or collapse the widget, or show a compact no-data state; never substitute zero.
7. Do not derive from `platform.json.sensors` or another raw-only sensor at runtime. Those entries are inventory data, not a live feed.

## Safe starting formulas

| Derived value | Formula | Inputs | Notes |
| --- | --- | --- | --- |
| Memory free | `memory.total - memory.used` | `memory.total`, `memory.used` | GB; clamp only for display if the host reports an inconsistent total |
| Memory free percent | `(memory.total - memory.used) / memory.total * 100` | same | Prefer `memory.usage` for the used percentage when available |
| VRAM free | `gpu.memoryTotal - gpu.memoryUsed` | `gpu.memoryTotal`, `gpu.memoryUsed` | GB |
| VRAM free percent | `(gpu.memoryTotal - gpu.memoryUsed) / gpu.memoryTotal * 100` | same | Prefer `gpu.memoryUsage` when available |
| Drive free | `storage.drives[i].total - storage.drives[i].used` | drive total, used | GB; one value per drive |
| Drive free percent | `(total - used) / total * 100` | drive total, used | Prefer the host drive usage when available |
| Total free storage | sum of every drive free value | every drive total, used | Only when every drive is available |
| Total storage capacity | sum of every drive `total` | every drive total | Use it to fill a composition that expects more modules than this machine reports |
| Total storage used | sum of every drive `used` | every drive used | Pairs with total capacity; show usage percent against the capacity |
| Combined network throughput | `network.download + network.upload` | download, upload | B/s; this is not an internet speed test |
| CPU + GPU power | `cpu.power + gpu.power` | CPU power, GPU power | Label it as a partial compute-power estimate, never as total PSU draw |

## Filling a composition on a sparse machine

When a reference image asks for more modules than this machine can supply, derived values are the first filler. Use them in this order: total compute power (CPU + GPU), total storage capacity, total storage used, total free storage, memory free, VRAM free, combined network throughput. Every filler still needs its inputs available in the same snapshot, and must be labeled as derived. Never repeat one metric in two modules to fill slots.

A skin may define another deterministic formula when the user asks for it, but it must satisfy the rules above and show its inputs in the UI or documentation.

## Do not derive

- FPS, frame time, or latency from utilization.
- Fan RPM from fan control percentage.
- Power from temperature or utilization without a documented hardware model.
- Total system or PSU power from CPU + GPU alone.
- Per-core values from an aggregate CPU load.
- Network utilization percentage without a known link speed.
- Battery runtime without a known battery capacity and discharge model.
- Any value from a raw sensor that is not represented in `snapshot`.

## JavaScript helper

```js
const available = metric =>
  metric?.status === "available" && Number.isFinite(metric.value);

function deriveFree(total, used) {
  if (!available(total) || !available(used)) return null;
  return total.value - used.value;
}

function derivePercent(part, total) {
  if (!Number.isFinite(part) || !available(total) || total.value <= 0) return null;
  return part / total.value * 100;
}
```

Use the nullable result to omit the derived widget when its inputs are unavailable on this machine. Only a metric that was available at export and then disappears at runtime may use a compact temporary no-data state. Never turn a missing derived result into `0`.
