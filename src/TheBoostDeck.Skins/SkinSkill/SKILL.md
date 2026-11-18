---
name: theboostdeck-skin-author
description: Create, redesign, or extend a TheBoostDeck performance HUD as a distributable .boostskin package. Use when the user asks for a custom gaming secondary-display dashboard, a skin/theme, a visual redesign from a reference image, or a telemetry layout for CPU, GPU, memory, storage, network, motherboard, fans, or temperatures. This exported skill contains this machine's complete sensor inventory, latest snapshot, safe derived-metric candidates, a reference-image workflow, and a built-in style library; read them before designing so sensors this machine cannot read are omitted instead of becoming empty widgets.
---

# TheBoostDeck Skin Author

Create a complete `.boostskin` package, not just a static mockup. The package must also carry a real preview image, because that is what every user sees in the skin library.

## Start with this machine

1. Read `references/platform-summary.md` first, then `references/platform.json`.
2. Treat every hardware and sensor name as literal source data. Do not translate, rename, or "clean up" names reported by the hardware layer.
3. Use `platform.json` to learn what this machine exposes. `snapshot` contains the canonical fields delivered to a running skin. `sensors` is the full raw inventory for discovery; a raw sensor that is not represented in `snapshot` is not available to the skin UI yet.
4. Build the design only from metrics marked `available` in the platform inventory. Metrics and hardware groups that are `unavailable` on this machine must be omitted from the layout; do not reserve empty cards, rows, or "N/A" slots for them.
5. Read `references/derived-metrics.md` before adding a computed value. `platform.json.derivedMetrics` lists formulas that can be calculated from this machine's direct `snapshot` fields; it is a starting point, not a live data feed.
6. Never invent a value, a sensor, a hardware capability, or a conversion factor that is not in the inventory or snapshot.

## Pick the design path first

Decide which path applies **before** styling, and say which one you took when you deliver.

| Input | Path |
| --- | --- |
| A reference image (design draft, screenshot, mood board, photo of an instrument) | **Path A** — read `references/from-reference.md` and follow its six steps in order |
| Named style, or usable style words ("cyberpunk", "二次元", "简洁一点") | **Path B** — read `references/styles.md`, pick the closest entry, adapt it |
| No image and no usable style words | **Path C** — ask once, then fall back to rotation |

**Path C in detail.** Ask a single question offering two or three concrete directions from `references/styles.md` (for example *deep-space-blueprint / cyberpunk / kawaii-pastel*), then wait. Do not ask an open "what style do you want?" and do not ask more than once. If the user does not answer, says "you decide", or the conversation continues without a choice, pick the next entry in the rotation listed in `styles.md`, state plainly which one you used and why it fits this machine's available metrics, and proceed. Never fall back to the default dark-cyan brief silently — that produces a second copy of the built-in skin.

## Workflow

1. Read the platform files. Mark which canonical metrics are `available`; keep only those metrics in the design and omit metrics/groups that are `unavailable` on this machine.
2. Choose the design path (A, B, or C above) and produce the visual direction: for path A the image inventory, palette samples, and module→metric mapping table; for path B/C the chosen style entry with its palette and type scale.
3. Resolve the module/metric mismatch explicitly. When the composition wants more modules than this machine can feed, fill with derived values (total compute power, total storage capacity, total storage used, total free storage, memory free, VRAM free, combined throughput) before cutting by hierarchy. Record what was dropped and why.
4. Plan both 16:9 landscape and 9:16 portrait from the same visual system. Both must aim to show the complete HUD in one screen inside the monitor work area with the taskbar excluded. Portrait is a re-composition, not a squeezed landscape row: merge, downgrade, and fold modules instead of shrinking type.
5. Build the skin:
   - Plain HTML, CSS, and JavaScript. No React, build step, bundler, or server.
   - Keep the main entry self-contained, or use relative local assets inside the package.
   - Use the runtime message contract in `references/protocol.md`, including the `?preview=1` preview mode described there.
   - Do not implement FPS; it is intentionally out of scope.
6. Generate the preview image — this step is mandatory, not optional:

```bash
python scripts/render_preview.py path/to/skin-folder --entry index.html --out path/to/skin-folder/preview.png
```

The script renders the skin in a local headless browser at 1280×720 and writes the PNG. Read `references/skin-format.md` if you need to point `skin.json.preview` at it. If no local browser can be found, render the preview with an available image tool that reproduces your actual layout, and say in the delivery message how it was produced. A skin without a preview is not deliverable.

7. Package the files into a ZIP and rename it to `.boostskin`.
8. Run the validator and fix every error before delivering:

```bash
python scripts/validate_boostskin.py path/to/your-skin.boostskin
```

The validator checks package structure, manifest, preview presence and size, offline-only assets, required control messages, and that every `snapshot.<field>` used by the UI exists in `references/platform.json`. It reports warnings for risky patterns such as extreme `z-index` overlays.

9. Report the path and the decision table from `references/from-reference.md` §Step 6: visual source, palette, modules kept/dropped, derived fills, UI language, control treatment, and both-aspect status.

## Language

1. **UI labels follow the host language.** Read `settings.language` from the `settings` message (`zh-CN` or `en-US`) and render your own labels, unit captions, and status text in that language. Provide both strings; switch on the message, not on the browser locale.
2. **Text inside the reference image is layout evidence, not language evidence.** It tells you where a label sits, how large it is, and how heavy it is — it does not decide the UI language. A Chinese user may hand you an English mockup; the UI still follows `settings.language`.
3. **Hardware-provided sensor names are never translated.** An Ethernet adapter reported as `以太网` stays `以太网` even in an English UI. Raw sensor names keep the language the sensor reports.
4. Numeric formats follow the language only for thousands separators and decimal punctuation; units (`W`, `°C`, `GB/s`, `RPM`) stay in their standard symbol form in both languages.

## Runtime contract in one paragraph

The host sends JSON messages through `window.chrome.webview` when available: `snapshot`, `settings`, and `collection-error` to the skin. `settings` includes the language, refresh interval, topmost/startup state, and version/build metadata; use `language` and `refreshIntervalMs` for presentation and cadence labels, never to start a second polling loop. The skin sends window actions (`begin-drag`, `toggle-topmost`, `hide-window`, `toggle-maximize`, `close-window`), language selection (`set-language`), the host control-center entry `open-settings`, and optionally the direct skin shortcut `open-skin-manager`. The host owns and renders the shared control center (General as the landing page, then Skins and About); the skin must not embed a settings or manager UI, and must not expect `skin-catalog` / `skin-action`. Treat every message as untrusted input and update the DOM defensively. `snapshot` is the only supported source for live values; the detailed field shape is in `references/protocol.md`.

## Runtime degradation (use this exact pattern)

Availability is known at design time; runtime dropouts are the only case that needs a temporary state.

```js
function setMetric(el, metric, format) {
  const valueEl = el.querySelector("[data-value]");
  const ok = metric && metric.status === "available" && Number.isFinite(metric.value);
  if (!ok) {
    el.classList.add("is-stale");        // dim the module, keep its footprint
    valueEl.textContent = "—";           // never 0, never a fake number
    return;
  }
  el.classList.remove("is-stale");
  valueEl.textContent = format ? format(metric.value) : String(metric.value);
}
```

```css
.is-stale { opacity: .45; }
.is-stale [data-value] { font-variant-numeric: tabular-nums; }
```

- A metric that was `unavailable` at export must not exist in the DOM at all.
- A metric that disappears at runtime keeps its slot and shows `—` in the dimmed state above; do not reflow the whole grid on every dropout.
- A derived value whose inputs go stale disappears into the same dimmed state — never recompute from stale inputs and never show `0`.
- **Hardware groups collapse as a group.** Read `gpu.kind`: when it is `none`, remove the whole GPU module (panel, pedal, gauge, VRAM block, trend chart) and let the remaining modules take the space; when it is `integrated`, keep usage/temperature/clock but drop VRAM (iGPU has no dedicated memory) instead of painting `— / — GB`.
- **Portrait does not rescue a dead module.** Do not rely on a media query to reflow a group whose data does not exist; it stays a hole in both orientations.

## Mandatory controls

Every skin must expose this exact minimum set, no matter how minimal the visual design is. Do **not** ask the user whether to include them; they are non-negotiable. Ask only about visual treatment, placement, or optional controls.

1. **Settings or skin switch — at least one of the two.** Send `{ "type": "open-settings" }` (host control center, General landing page) and/or `{ "type": "open-skin-manager" }` (same host panel, Skins tab). One button is enough; `open-settings` already reaches the skin library. Do not copy, embed, or restyle either panel.
2. **Maximize / restore — required.** Send `{ "type": "toggle-maximize" }`. The control must be visible at all times and must reflect the real window state: while `settings.maximized` is `true` it shows a **restore** glyph and label, otherwise a **maximize** glyph. Listen for the `settings` message the host sends after every state change and swap the icon/label — never leave a stale "maximize" icon on a maximized window.
3. **Minimize or close — at least one of the two.** Send `{ "type": "hide-window" }` (the host's minimize/collect action: the window is tucked away and can be restored from the tray or taskbar) or `{ "type": "close-window" }` (exit). Both may be present. A skin that offers only close is compliant, but the tray icon must never be the only way back from a full-screen skin.
4. **Drag region — required, one path only.** A visible drag area that sends `{ "type": "begin-drag" }` so the frameless window can be moved. **Recommended:** make the entire title/header strip the drag surface, excluding buttons, links, inputs, and other interactive controls; a narrower visible handle is also acceptable, but ship **one** drag path with a consistent cursor and hint. Use `cursor: grab` on the strip and `cursor: grabbing` while pressed (`:active`), plus a tooltip such as "拖动窗口 / Drag to move". Start the drag from a `mousedown` handler that posts `begin-drag` and returns early for `e.target.closest("button")`. Set `user-select:none` on the HUD (`html,body`) so text selection never interferes with dragging, and `pointer-events:none` on decorative SVGs inside buttons so the button itself is always the event target. The top 6 px belongs to resizing. **Decorative layers must never intercept input:** every purely decorative pseudo-element or absolutely-positioned ornament (glows, radial-gradient halos, blur, scanlines, corner brackets, specular streaks) must set `pointer-events:none`. A large glow — e.g. an 80%-wide circular `::before` with `filter:blur` — overflows its container and, in portrait single-column layouts where the container goes full-width, can cover the entire title bar and buttons, silently blocking drag and clicks while looking purely visual.

A single control hub may combine the settings/skin entry with hide/exit in one small menu. Position is free: no required top-right corner, place the controls wherever they support the composition and stay reachable in both 16:9 and 9:16. When space is extremely tight, collapse the labels to icons — but never drop a required capability.

**When the reference image has no room for controls**, use one of the treatments below instead of pasting a high-contrast button block over the composition. Full detail is in `references/from-reference.md` §Step 5:

1. Low-contrast icon capsule in a title strip: 1 px hairline, icon-only, 55–70% opacity, full opacity on hover/focus.
2. Thin edge control rail outside the 6 px resize band, expanding on hover.
3. Hover-reveal cluster that stays as a real hit target (opacity change only — never `display:none`) and remains keyboard reachable.

A single visible control hub is acceptable: one button opening a small menu containing “Settings / Switch skin” and “Hide/Exit”. There is no required top-right position; place controls wherever they fit, as long as they stay visible and reachable in both 16:9 and 9:16.

## Host-reserved window edges

The window is frameless and the WebView2 surface covers the whole client area, so the host injects a transparent resize overlay into every skin page after navigation. The overlay is invisible and needs no skin code, but it owns the outermost pixels:

- **Edges: 6 CSS px** and **corners: 12 CSS px** on each side. Inside that zone the cursor becomes a resize cursor, and pressing starts a window resize instead of clicking the skin.
- Keep every interactive control (buttons, links, inputs, selects, scrollable lists, drag handles) at least **16 px** away from the window edge, and further from the corners. Never make a control's only hit area a 6 px sliver at the edge.
- Backgrounds, frames, glows, and decorative art may still bleed to the edge: the band is transparent, so visuals are unaffected — only pointer input is captured.
- The top 6 px belongs to resizing, so a drag surface starts below it. A full header strip is fine; the band simply wins on the outermost 6 px.
- Do not build your own resize grips, resize messaging, or a full-window overlay with an extreme `z-index`. The host overlay sits at the maximum CSS `z-index`; a skin that paints over the edges breaks resizing for everyone.
- When the window is maximized the host hides the band on its own; the skin does not need to track window state for this.

## Host-owned control center

The settings and skin manager are host capabilities, not skin features. Every skin only exposes a visible trigger that sends `{ "type": "open-settings" }`; an optional direct shortcut can send `{ "type": "open-skin-manager" }` to land on the Skins tab. The host renders the shared General/Skins/About panel, previews, switching, import/export, export-skill, Esc/backdrop close, and language handling on its own layer. The trigger position and visual shell are yours; do not copy, embed, or restyle the panel in the skin package.

This keeps skin packages small, upgrades the settings and manager once for every skin, and prevents each skin from drifting into its own copy.

## Derived values

The host does not send every useful display value. You may compute additional values from canonical `snapshot` fields when the formula is deterministic, every input is available in that same snapshot, and the result is labeled as derived in the UI.

- Start from `platform.json.derivedMetrics` and `references/derived-metrics.md`.
- Good examples are free memory (`total - used`), free VRAM, drive free space, total free storage, total storage capacity and total storage used (sums across drives), combined network throughput, and CPU + GPU power.
- Prefer a direct host metric when one exists; use the derivation only as a fallback or a genuinely new presentation value.
- Label a derived value: a small "derived" mark, a tooltip, or a detail line. Never present an estimate as a measured sensor reading.
- Never derive FPS, fan RPM from PWM, total PSU power, per-core values from aggregate load, or a value from a raw sensor that is not in `snapshot`.
- If an input is unavailable in the platform inventory, omit the derived value from the design. If a previously available input drops out during runtime, use the runtime degradation pattern above; never substitute zero.
  - Exception for sums: a two-part total such as CPU + GPU power may treat the missing part as `0`, mark the result as partial in the UI, and disappear only when both parts are unavailable. Do not apply this exception to ratios, averages, or any formula where zero changes the meaning.

## Design requirements

- Read `references/design-guide.md` for structure and craft rules; it is the fallback visual brief only when path C receives no answer **and** you deliberately chose the built-in default. Prefer `references/styles.md` for path C, and the user's image for path A.
- Prioritize a high-signal HUD: large primary values, quiet secondary labels, readable at a glance from a secondary display.
- Landscape and portrait must both be first-class. Use CSS grid/flex and container queries or aspect-ratio media queries; never hardcode a single viewport size. Aim to show the whole HUD in one screen at both 16:9 and 9:16, assuming the window occupies the monitor work area with the taskbar excluded. Use vertical scrolling only as a fallback for unusually small windows, not as the default portrait layout.
- **Widget set comes from platform availability.** Omit metrics marked `unavailable` on this machine; do not reserve empty cards, rows, or "N/A" slots. Omit an entire hardware group when all its metrics are unavailable, and rebalance the available modules.
- **Never assume a fixed drive count.** `storage.drives` is dynamic (a laptop may have one volume, a workstation six). Cap the visible list to what the layout can actually hold and append a `+N` indicator (or make the list scroll); never silently clip extra volumes, and do not hardcode exactly four slots.
- **Expansion is opt-in, never a reason to add a module.** If the design already shows fans (a cooling panel, a fan row, a footer chip), extra fans may reuse that existing slot as a compact `+N`; if the skin does not show fans at all, do not add a fan module just because `system.fans` has data. The same rule applies to extra drives: extend or summarize the existing storage area, never invent a second storage block. A HUD that stays beautiful with less data beats one that grows a new panel per sensor.
- **Treat extra fans as an expansion slot, not a grid.** `system.fans` can carry a dozen entries. Keep the primary CPU/GPU fan treatment as-is, then show the extras in one compact line or strip (`SYS 1200 · 980 · +3`), capped to the space you actually have. Do not duplicate the CPU fan into the extras list, and do not grow the main layout per fan. Ignore extra entries reading exactly `0` RPM — those are normally unpopulated motherboard headers; the CPU fan itself may legitimately read `0` and must still be shown.
- Keep resource usage low: no continuous animation loops that are not needed, no large dependencies, no polling faster than the host snapshot cadence. When showing a sampling-rate label, read `settings.refreshIntervalMs` instead of hardcoding `1s`.
- Offline only: no CDN, no remote fonts, no external images. Bundle what you need into the package.

## Package rules

- `schemaVersion` must be `1`.
- `id` must match `^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$` and must not be `classic`.
- Reject absolute paths, `..`, drive letters, query strings, and URLs in package paths.
- Do not exceed: 256 MB package, 2000 entries, 64 MB per entry, 512 MB uncompressed total.
- Read `references/skin-format.md` for the complete manifest and packaging rules.

## Quality gate

Before finishing, confirm:

1. The design path is explicit (A/B/C), and for path A the image inventory, palette samples, and module→metric mapping exist.
2. Every live widget is backed by a metric marked `available` in the platform inventory; no permanently unavailable metric is shown, and no empty slot was left to match the reference.
3. Derived values used to fill a sparse machine are labeled as derived and use only available inputs.
4. Both 16:9 and 9:16 aim to show the complete HUD in one screen within the monitor work area (taskbar excluded), with portrait re-composed rather than squeezed.
5. UI labels follow `settings.language`; reference-image text was not used as a language decision; raw sensor names are untranslated.
6. Content inside the reference image does not force an English UI for a Chinese host, and vice versa.
7. Every metric has a runtime degradation path (`—` + dimmed), and nothing was `unavailable` at export yet still rendered.
8. The package contains a real `preview` image that reflects the actual layout, at most 2 MB.
9. No sensor or hardware name has been translated or fabricated.
10. The archive has `skin.json` at its root and the entry file exists.
11. `validate_boostskin.py` passes with no errors.
12. Every computed value uses only available `snapshot` inputs, has a documented formula, and is labeled as derived.
13. Any displayed sampling-rate label follows `settings.refreshIntervalMs`; no hardcoded one-second assumption.
14. The skin includes the mandatory control set: at least one of `open-settings` / `open-skin-manager`, a `toggle-maximize` that flips to restore on `settings.maximized`, at least one of `hide-window` / `close-window`, and a visible `begin-drag` region — with a deliberate visual treatment (low-contrast capsule, edge rail, or hover-reveal cluster) when the reference leaves no room.
15. Clear of the host resize band: no interactive control sits within 16 px of the window edge or inside the 12 px corners, and nothing paints an overlay above the host resize band.
16. Offline only: no remote script, stylesheet, font, or image.
