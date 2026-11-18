# Design guide

This file defines **structure and craft**, not a fixed look. Read it always; take the visual language from the user's reference image (`from-reference.md`) or from `styles.md`. Use the palette and typography described below only as the built-in fallback when no other direction exists — it is the same system as the built-in classic skin, so reproducing it makes the new skin redundant.

## Visual language

TheBoostDeck is a gaming secondary-display HUD, not a web dashboard. It should feel like a piece of instrumentation: dark, quiet, precise, and readable from a short distance.

- Dark near-black or deep blue background with a restrained cyan/teal accent.
- A warm accent for warnings only; do not use it as the main brand color.
- Thin separators and hairline borders; avoid a stack of identical rounded cards.
- A large primary value per module, a compact label, and a quiet secondary line for source/status.
- Use a consistent 4/8 px spacing rhythm and a small type scale. Do not shrink type to force content into one screen.
- Use tabular numerals for changing values so the layout does not jitter.
- Icons should be inline SVG and share one stroke language; do not use emoji.

## Controls

Controls are part of the HUD's usability, not optional decoration. Every skin must expose this minimum set:

- **settings or skin switch — at least one:** `open-settings` and/or `open-skin-manager`;
- **maximize / restore — always:** `toggle-maximize`, with the glyph and label flipping according to `settings.maximized`;
- **minimize or close — at least one:** `hide-window` (the host's minimize/collect action) or `close-window`;
- **drag region — always:** `begin-drag`. **Recommended:** use the entire title/header strip as the drag surface, keeping buttons, links, and other interactive controls excluded from the drag hit area; this is a suggestion, not a fixed layout rule.

A single control hub may contain the settings/skin switch and hide/exit actions, so a minimal skin only needs one visible control. A direct `open-skin-manager` shortcut is optional but useful when the design has room for it. Do not assume the top-right corner: place the hub wherever it supports the composition and remains reachable in both 16:9 and 9:16. If space is tight, merge controls into a compact menu instead of removing them.

### When the reference has no room for controls

Most reference images contain no UI chrome. Do not paste a high-contrast button block over the composition. Use one of these, in preference order:

1. **Low-contrast capsule** — 28–32 px pill in a title strip, 1 px hairline, icon only, text/icon at 55–70% opacity, full opacity on hover and keyboard focus.
2. **Edge control rail** — a thin vertical or horizontal rail, positioned outside the 6 px resize band, faint at rest, expanding on hover.
3. **Hover-reveal cluster** — controls fade in over a title area, but stay real hit targets at all times (opacity only, never `display:none`) and remain reachable by keyboard.

The functions must be discoverable without hovering an invisible area: the entry must be visible at rest at low opacity, not hidden until the pointer happens to find it.

The settings and skin manager are host-owned. A visible trigger sends `open-settings`; the host opens its control center with General as the landing page, plus Skins and About tabs. The Skins tab contains the catalog, preview cards, switching, import/export, export-skill, Esc/backdrop close, and language handling. Do not copy, embed, or restyle any settings/manager panel in the skin.

Optional controls such as topmost, language, and maximize can be added when the design or user asks for them. Import/export and skill export live in the host control center, not as separate top-level buttons.

## Information hierarchy

1. CPU and GPU are the primary story: usage, temperature, power, frequency, and fan when available.
2. Memory and storage are supporting capacity stories.
3. Network is a throughput story.
4. Motherboard temperature, system power, and power source are ambient context.
5. FPS is out of scope. Do not reserve space for it.

## Responsive behavior

Treat both orientations as designed states, not a desktop page squeezed into a phone.

- Landscape 16:9: a horizontal band of primary modules, with supporting modules below or beside.
- Portrait 9:16: a primary stack with compact supporting rows; pair supporting modules only when each column stays readable, rather than forcing a two-tiny-column grid.
- Fit the work area: 16:9 and 9:16 should each aim to show the complete HUD in one screen when the window occupies the monitor work area. Treat taskbar height as unavailable space; do not make routine vertical scrolling the portrait reading order.
- When the available height is tight, first tighten gaps, padding, chart heights, and module pairing; do not shrink type below a readable size. If the window is unusually small, allow vertical scrolling as a fallback rather than hiding required metrics.
- Use CSS grid with `minmax(0, 1fr)` and `min-width: 0` on flex/grid children.
- Prefer container queries or aspect-ratio media queries over a single fixed breakpoint.
- No horizontal scrolling. Vertical scrolling is a fallback, not the intended reading order.
- Keep the top drag region clear; the host window is frameless. If the whole title/header strip is used as the drag surface, interactive controls inside it must remain clickable and must be excluded from the drag hit area.

### From one aspect to the other

When you only have a landscape reference, derive portrait deliberately:

1. **Keep** the primary module, palette, type scale, shape language.
2. **Merge** side-by-side modules into one row of two values only while each value stays readable at ≥2/3 of the landscape size; otherwise collapse them into a primary value plus a secondary line.
3. **Downgrade** bars, gauges, and charts into value strips or inline sparklines.
4. **Fold** ambient items (motherboard temperature, power source, uptime) into one compact footer row; if it still does not fit, drop the lowest-priority ambient item.
5. **Reorder** into a stack that follows the hierarchy, rather than stretching a landscape row.
6. Never shrink type below 10 px to make it fit; never let the taskbar area count as usable space.

## Missing data

Platform availability decides the widget set, not runtime luck:

- Metrics marked `unavailable` in `platform.json` are absent on this machine. Omit them from the design entirely: no empty card, no "N/A" slot, no reserved row. If a whole hardware group is unavailable, omit the group and rebalance the remaining modules.
- Never build a live widget from a raw sensor that is not represented in `snapshot`; it will never receive a live value.
- If a metric was available at export but drops out during runtime, collapse the widget or show one compact no-data state. This is the only case that needs a temporary placeholder.
- Never show `0` for a missing reading. `0` is reserved for a real zero, such as a fan reporting 0 RPM.
- Never translate hardware-provided sensor names; only translate your own labels.
- A metric that disappears at runtime keeps its slot and renders `—` in a dimmed state; use the standard pattern in `SKILL.md` §Runtime degradation instead of inventing per-skin behaviour.

## Language

- UI labels, unit captions, and status text follow `settings.language` (`zh-CN` / `en-US`); render both string sets and switch on the host message, not on `navigator.language`.
- Text inside a reference image is layout evidence only — position, size, weight. It never decides the UI language.
- Sensor names stay exactly as the hardware reports them, even when that language differs from the UI (a Chinese Windows reporting `以太网` shows `以太网` in an English UI).
- Units stay symbolic in both languages (`W`, `°C`, `GB`, `RPM`); only separators and decimal punctuation follow the language.

## Motion and cost

- Animate only on entry, state change, or direct user action.
- Avoid infinite decorative animation, particle systems, and always-running requestAnimationFrame loops.
- No external fonts, scripts, or images unless the user explicitly accepts the tradeoff; the package should work offline.
- Keep the page lightweight enough for a always-on secondary display.
