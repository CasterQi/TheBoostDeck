# From reference image to skin

This is the required path whenever the user supplies a reference image (a design draft, a screenshot of another HUD, a mood board, a photo of a physical instrument). Do the four steps in order and keep the intermediate results — the final reply reports them.

## Step 1 — Image inventory

Read the image and write down, before writing any code:

1. **Module count and grid** — how many readout blocks, how many columns/rows, where the primary block sits.
2. **Palette** — 4–6 sampled colors with roles: background, panel surface, primary accent, secondary accent, warning, text, muted text. Name them; approximate hex is fine, but keep the relationships (contrast, saturation) of the source.
3. **Type scale** — relative sizes of title, value, label, meta. Convert to a 3–5 step scale, e.g. 44/26/12/10 px.
4. **Shape language** — radii, border weight, corner brackets, dividers, decorative marks.
5. **Density** — average gap between modules, panel padding, whether values are left- or right-aligned.
6. **Where controls could live** — usually nowhere in the image; note candidate spots (title strip, corner, footer bar).
7. **Anything the image implies but cannot work** — tiny labels, text over imagery, glow that hides numerals.

If the image is unreadable (blurred, cropped, abstract), say so and switch to the no-reference flow in `styles.md` instead of guessing.

## Step 2 — Map modules to metrics

Build an explicit mapping table. Column 1 is the image module, column 2 is the metric, column 3 is how it renders.

| Image module | Metric | Rendering |
| --- | --- | --- |
| Large central dial | `cpu.usage` + `cpu.temperature` | value + secondary line |
| Row of three small readouts | `gpu.usage`, `gpu.temperature`, `gpu.power` | compact triple |
| Bar strip | `memory.usage`, `storage.drives[0].usage` | two horizontal bars |

Rules:

1. Only map metrics that `platform.json` marks `available`, or derived metrics whose every input is available (see `derived-metrics.md`).
2. Raw sensor names from `platform.json.sensors` are inventory only — they never back a live widget.
3. Prefer the metric the image semantically suggests over the metric that happens to be numerically convenient.
4. Hardware names stay in the language the sensor reports. Your own labels follow the UI language rule in `SKILL.md`.

## Step 3 — Resolve the mismatch

### The image has more modules than the machine has metrics

Fill the gap in this order, and stop as soon as the layout is complete:

1. **Derived values.** The host exports a `derivedMetrics` list; use it. Common fills:
   - total compute power = `cpu.power + gpu.power` (label as partial compute power, never PSU draw)
   - total storage capacity = sum of every drive `total`
   - total storage used = sum of every drive `used`
   - total free storage, memory free, VRAM free, combined network throughput
2. **Split one host metric into two honest readouts** — e.g. one module shows `cpu.usage`, its sibling shows `cpu.temperature`; a single gauge becomes value + peak-hold.
3. **Fold two small modules into one** with a primary value and a secondary line.
4. **Cut by hierarchy.** `design-guide.md` order decides what survives: CPU/GPU first, memory/storage second, network third, motherboard/power ambient last.

Never leave an empty slot, never repeat the same metric in two modules to fill space, never label a derived value as a measured sensor.

### The machine has more metrics than the image has modules

Keep the image's composition. Add the surplus only in the secondary region the image already implies (a footer strip, a side column, a quiet row). Do not promote a new metric above a module the image emphasized. If there is no secondary region, cut the surplus instead of inventing a new block.

### The image shows a metric this machine does not have

Omit that module entirely and reflow the neighbours so the remaining composition still reads as designed. Do not draw a placeholder, do not show `0`, do not keep an empty frame to preserve symmetry.

### Two image modules want the same metric

Give the metric to the module that sits on the image's primary axis; the second module becomes a derived or related readout, or is dropped.

## Step 4 — Derive the second orientation

The reference is usually one aspect. Produce both.

1. **Keep** the primary module, the palette, the type scale, and the shape language.
2. **Merge**: modules that were side-by-side in landscape become one row with two values when each value stays readable at ≥2/3 of the landscape type size; otherwise they become a single primary value with a secondary line.
3. **Downgrade**: decorative bars/charts become thin value strips or inline sparklines; ambient modules shrink to one text line each.
4. **Fold**: ambient modules (motherboard temperature, power source, uptime) move to a compact footer row. If even that does not fit, drop the lowest-priority ambient item — do not shrink type below 10 px and do not create routine vertical scrolling.
5. **Reorder, don't stretch**: portrait is a stack, not a squeezed landscape row. Reading order top→bottom follows the hierarchy.
6. Both orientations must fit one screen inside the monitor work area, taskbar excluded.

## Step 5 — Controls when the image has none

Every skin still needs a visible control hub, a visible hide/exit, and a drag region. When the image leaves no room, use one of these, in this order of preference:

1. **Low-contrast capsule** — a 28–32 px pill in a corner of the title strip, 1 px hairline border, icon-only, text color at 55–70% opacity. Hover/focus raises it to full opacity.
2. **Edge control rail** — a 24 px vertical strip on one side, visible only as a faint glyph cluster; expands on hover. Keep it outside the 6 px edge band.
3. **Hover-reveal cluster** — controls fade in on hover over the title area, but must exist as real hit targets at all times (opacity, not `display:none`), and must be reachable by keyboard.

Do not place a high-contrast button block that fights the reference's composition. Do not hide the entry behind right-click only; a tray icon is not a substitute.

## Step 6 — Report the decisions

In the final reply, give a short decision table so the user can judge without reading code:

| Item | Decision |
| --- | --- |
| Visual source | reference image `xxx.png` / style `cyberpunk` |
| Palette | background `#0b0c10`, accent `#39d0ff`, warning `#ffc857` |
| Modules kept | 6 of 8 from the image |
| Modules dropped | e.g. power draw (no sensor), fan RPM (reports 0 on this board) |
| Derived fills | total compute power, total storage capacity |
| UI language | `zh-CN` from `settings.language` |
| Controls | low-contrast capsule, top-right, outside the 16 px safe margin |
| Both aspects | 16:9 one screen, 9:16 one screen |
