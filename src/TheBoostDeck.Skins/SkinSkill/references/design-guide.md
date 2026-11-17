# Design guide

Use this as the default visual brief unless the user gives a stronger reference image or brand direction.

## Visual language

TheBoostDeck is a gaming secondary-display HUD, not a web dashboard. It should feel like a piece of instrumentation: dark, quiet, precise, and readable from a short distance.

- Dark near-black or deep blue background with a restrained cyan/teal accent.
- A warm accent for warnings only; do not use it as the main brand color.
- Thin separators and hairline borders; avoid a stack of identical rounded cards.
- A large primary value per module, a compact label, and a quiet secondary line for source/status.
- Use a consistent 4/8 px spacing rhythm and a small type scale. Do not shrink type to force content into one screen.
- Use tabular numerals for changing values so the layout does not jitter.
- Icons should be inline SVG and share one stroke language; do not use emoji.

## Information hierarchy

1. CPU and GPU are the primary story: usage, temperature, power, frequency, and fan when available.
2. Memory and storage are supporting capacity stories.
3. Network is a throughput story.
4. Motherboard temperature, system power, and power source are ambient context.
5. FPS is out of scope. Do not reserve space for it.

## Responsive behavior

Treat both orientations as designed states, not a desktop page squeezed into a phone.

- Landscape 16:9: a horizontal band of primary modules, with supporting modules below or beside.
- Portrait 9:16: one primary stack with compact supporting rows; avoid two tiny columns.
- Use CSS grid with `minmax(0, 1fr)` and `min-width: 0` on flex/grid children.
- Prefer container queries or aspect-ratio media queries over a single fixed breakpoint.
- No horizontal scrolling. Vertical scrolling is a fallback, not the intended reading order.
- Keep the top drag region clear; the host window is frameless.

## Missing data

Platform availability decides the widget set, not runtime luck:

- Metrics marked `unavailable` in `platform.json` are absent on this machine. Omit them from the design entirely: no empty card, no "N/A" slot, no reserved row. If a whole hardware group is unavailable, omit the group and rebalance the remaining modules.
- Never build a live widget from a raw sensor that is not represented in `snapshot`; it will never receive a live value.
- If a metric was available at export but drops out during runtime, collapse the widget or show one compact no-data state. This is the only case that needs a temporary placeholder.
- Never show `0` for a missing reading. `0` is reserved for a real zero, such as a fan reporting 0 RPM.
- Never translate hardware-provided sensor names; only translate your own labels.

## Motion and cost

- Animate only on entry, state change, or direct user action.
- Avoid infinite decorative animation, particle systems, and always-running requestAnimationFrame loops.
- No external fonts, scripts, or images unless the user explicitly accepts the tradeoff; the package should work offline.
- Keep the page lightweight enough for a always-on secondary display.
