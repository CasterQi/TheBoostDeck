# Style library

Use this file when the user gives **no reference image and no usable style words**, or when the user names one of these directions. Each entry is a complete design brief: palette, typography, geometry, texture, icon language, and how modules should be treated.

## How to use

1. Pick one entry — never blend two entries into an average look.
2. Sample the palette exactly as written; if you shift a hue, shift the whole panel family consistently instead of inventing new single colors.
3. Keep the module hierarchy from `design-guide.md` (CPU/GPU primary, memory/storage supporting, network throughput, motherboard ambient). A style changes the surface, not the information order.
4. Reuse the style's type scale across both orientations; only the layout reflows.
5. If the user rejects the first direction, offer the next entry rather than iterating endlessly inside one style.
6. Rotation rule for unattended generation: `anime-cel` → `cyberpunk` → `deep-space-blueprint` → `minimal-clinical` → `racing-telemetry`, then continue down this list.

## anime-cel

- Keywords: 二次元、动画赛璐璐、角色卡、明亮描边。
- Background: `#0d1426` with a soft radial `#1b2a52`; panels `#16203c`.
- Accents: primary `#5b8cff`, secondary `#ff6f9c`, warning `#ffcf5c`.
- Text: `#f2f5ff` primary, `#9aa8c7` secondary.
- Type: rounded geometric sans (system fallback `"Segoe UI Variable", "Microsoft YaHei UI"`); display weight 700, labels 600, numerals tabular.
- Geometry: 14–18 px radii, 2 px borders in `rgba(91,140,255,.45)`, inner glow instead of shadows.
- Texture: one large soft gradient bloom behind the primary module; thin speed lines allowed at ≤8% opacity.
- Icons: rounded 2 px stroke, slightly chunky; no emoji.
- Modules: character-sheet feel — big outlined value, small caps label above, thin underline below.
- Suits: playful but readable HUDs, viewer-facing streams.
- Avoid: pastel overload, more than two accent hues, decorative mascots that push data off screen.

## cyberpunk

- Keywords: 赛博朋克、霓虹、故障感、工业暗场。
- Background: near-black `#070a12`; panels `#0e1420` with a 1 px `#233a4d` border.
- Accents: primary `#00e5ff`, secondary `#ff2e88`, warning `#ffd166`.
- Text: `#dff6ff`, secondary `#7f97ab`.
- Type: condensed uppercase display (`"Bahnschrift SemiCondensed", "Segoe UI"`), numerals tabular and wide-tracked.
- Geometry: 2–4 px radii, corner brackets instead of full frames, 8 px baseline grid, diagonal cuts as clip-path accents.
- Texture: scanline overlay ≤6%, chromatic offset only on the title, no permanent glitch animation.
- Icons: angular 1.5 px strokes with cut corners.
- Modules: framed readouts with corner ticks; primary value in accent, unit in muted small caps.
- Suits: high-energy gaming HUDs, RGB-adjacent setups.
- Avoid: neon on every element, animated glitch loops, magenta text on dark for body copy.

## kawaii-pastel

- Keywords: 可爱风、糖果色、圆润、软阴影。
- Background: `#1a1526` warm dark or `#fdf6fb` light variant (choose one, not both).
- Panels: `#2b2340` dark variant / `#ffffff` light variant, 20 px radii.
- Accents: primary `#ff9ec7`, secondary `#8fd6ff`, warning `#ffb347`.
- Text: `#fff3fa`, secondary `#c8b6d8`.
- Type: humanist rounded (`"Segoe UI Variable", "Microsoft YaHei UI"`), 600 labels, relaxed letter spacing.
- Geometry: very round (20–24 px), soft 1 px borders, gentle drop shadow `0 8px 24px rgba(0,0,0,.35)`.
- Texture: tiny dot grid at 4% opacity; optional sparkle glyphs in margins only.
- Icons: thick rounded 2.4 px strokes, filled accents sparingly.
- Modules: pill-shaped headers, values centered, secondary text lowercase.
- Suits: cozy desk setups, light-themed portrait screens.
- Avoid: full pastel backgrounds behind dense numerals; low-contrast pink on pink.

## minimal-clinical

- Keywords: 极简临床、白底、医用仪器、克制。
- Background: `#f4f7fa` light or `#111417` dark (pick one); panels flat with 1 px `#d7dee6` / `#242a31` border.
- Accents: primary `#0b6bcb`, warning `#c0562a`; no second decorative accent.
- Text: `#111417` / `#e6ebf0`, secondary `#5b6672` / `#93a0ad`.
- Type: neutral grotesque, single family, three weights, fixed numerals.
- Geometry: 4 px radii, no shadows, no gradients, 8 px grid.
- Texture: none. Whitespace is the texture.
- Icons: 1.5 px monoline.
- Modules: aligned baselines, labels left, values right, hairline separators.
- Suits: daytime readability, anti-RGB setups, portrait secondary monitors.
- Avoid: drop shadows, glowing accents, decorative dividers.

## tactical-green

- Keywords: 军事、磷光绿、雷达、战术终端。
- Background: `#0a0f0b`; panels `#0f1712` with `#1f3326` borders.
- Accents: primary `#7dff9b`, secondary `#c9ff6b`, warning `#ffb02e`.
- Text: `#c8ffd8`, secondary `#6f9c7c`.
- Type: monospaced throughout (`"Cascadia Mono", "Consolas"`), uppercase labels, tabular numerals.
- Geometry: 0 radii, bracket corners, dotted leaders between label and value.
- Texture: subtle CRT vignette, 1 px grid lines at 4%.
- Icons: 1 px technical strokes, radar ticks.
- Modules: terminal rows, `NAME ......... VALUE`, status lamp dots.
- Suits: simulation, mil-sim, low-light rooms.
- Avoid: rounded corners, colored illustrations, more than two greens.

## vaporwave

- Keywords: 蒸汽波、粉紫渐变、落日、复古数字。
- Background: vertical `#1b0f33` → `#2b1145`; panels translucent `rgba(58,26,84,.7)`.
- Accents: primary `#ff71ce`, secondary `#01cdfe`, warning `#fffb96`.
- Text: `#fff5ff`, secondary `#b79ad6`.
- Type: wide-tracked uppercase for labels, one italic display accent for the title only.
- Geometry: 10 px radii, 1 px neon borders with outer glow `0 0 18px`.
- Texture: horizon grid at the bottom edge ≤10% opacity, no animated sun.
- Icons: 1.6 px strokes with gradient caps.
- Modules: chrome-style headers, values in gradient text (max two modules, keep the rest solid).
- Suits: aesthetic-first builds, mood-driven setups.
- Avoid: gradient text on every value, purple on purple at low contrast.

## deep-space-blueprint

- Keywords: 深空、工程蓝图、网格、仪表。
- Background: `#05101c`; panels `#0a1a2b` with `#123a55` hairlines.
- Accents: primary `#4bc9ff`, secondary `#7ce6d5`, warning `#ff9f5c`.
- Text: `#e2f4ff`, secondary `#7fa3bd`.
- Type: technical sans + tabular numerals; labels uppercase at 10 px, +0.14em tracking.
- Geometry: 8 px radii, full hairline frames, dimension ticks at frame corners.
- Texture: blueprint grid at 5% opacity across the whole surface.
- Icons: 1.4 px schematic strokes.
- Suits: the default TheBoostDeck look — safe, precise, instrument-like.
- Avoid: heavy glows, illustration, more than one grid layer.

## pixel-retro

- Keywords: 像素、8-bit、复古街机、硬边。
- Background: `#101018`; panels `#1a1a2a` with 2 px stepped borders (`box-shadow` steps, no anti-aliased frames).
- Accents: primary `#ffcc00`, secondary `#38b6ff`, warning `#ff5d5d`.
- Text: `#ffffff`, secondary `#a0a0b8`.
- Type: bitmap-style (`"Press Start 2P"` is not bundled — use `"Cascadia Mono"` fallback), uppercase only, small sizes kept ≥10 px.
- Geometry: 0 radii, 2 px hard outlines, 4 px spacing multiples.
- Texture: dither pattern at 4%, no gradients at all.
- Icons: 16×16 pixel glyphs, one color per icon.
- Modules: scoreboard rows, blinking only on state change.
- Suits: retro builds, low-resolution secondary panels.
- Avoid: anti-aliased gradients, sub-pixel hairlines, long body copy.

## steampunk-brass

- Keywords: 蒸汽朋克、黄铜、机械、压力表。
- Background: `#150f0a`; panels `#241a12` with `#4a3521` borders.
- Accents: primary `#c9a15a`, secondary `#8fb9a8`, warning `#d96b4a`.
- Text: `#f0e2cc`, secondary `#a89478`.
- Type: slab-ish display (`"Georgia", "Microsoft YaHei UI"`), engraved-label feel, numerals tabular.
- Geometry: 6 px radii, double-line frames, rivet dots at corners.
- Texture: brushed-metal gradient at ≤12%, vignette.
- Icons: 1.8 px strokes with circular housings.
- Modules: gauge-dial framing, needle marks as static decoration only.
- Suits: themed builds, warmer palettes.
- Avoid: real photographs of gears, animated rotating dials.

## neon-noir

- Keywords: 霓虹暗街、电影感、暗红蓝对比。
- Background: `#0b0c10`; panels `#141821`.
- Accents: primary `#39d0ff`, secondary `#ff5a36`, warning `#ffc857`.
- Text: `#f2f6fa`, secondary `#8592a0`.
- Type: narrow grotesque, one italic display cut for the title.
- Geometry: 6 px radii, 1 px borders, long thin horizontal rules.
- Texture: side light bar as the only highlight; no background imagery.
- Icons: 1.5 px strokes, minimal fill.
- Suits: cinematic gaming setups, ultrawide stretches.
- Avoid: full-body glow, three competing accent hues.

## glass-aurora

- Keywords: 玻璃拟态、极光渐变、柔和光晕。
- Background: dark `#050b14` with a slow-free static aurora gradient (`#113a5c`, `#3b1f5e`).
- Panels: `rgba(255,255,255,.06)` with `blur(14px)` and 1 px `rgba(255,255,255,.12)`.
- Accents: primary `#6de3ff`, secondary `#a78bfa`, warning `#ffb86b`.
- Text: `#f2f8ff`, secondary `#a9bcd0`.
- Type: geometric sans, medium weights, generous line height.
- Geometry: 16–20 px radii, no visible frames beyond the blur edge.
- Texture: one aurora blob behind the header only; no animated blobs.
- Icons: 1.4 px strokes with soft opacity.
- Suits: modern soft aesthetics, wallpaper-friendly setups.
- Avoid: blur on every panel (cost), gradient text, low contrast on small labels.

## e-ink-paper

- Keywords: 电子墨水、纸面、单色、阅读优先。
- Background: `#f5f3ee`; panels `#ffffff` with 1 px `#d8d4cb`.
- Accents: primary `#1f4d3d`, warning `#8a4b2a`; everything else grayscale.
- Text: `#1d1d1b`, secondary `#6b6862`.
- Type: serif display + sans body; numerals tabular serif.
- Geometry: 2 px radii, no shadows, 10 px grid.
- Texture: very light paper grain at 3%.
- Icons: 1.5 px monoline, single ink color.
- Modules: printed-report layout — rule lines, column headers, footnotes for derived values.
- Suits: daylight users, OLED burn-in concerns, quiet desks.
- Avoid: any glow, any gradient, low-contrast gray body text.

## street-brutalist

- Keywords: 粗野主义、高对比、大字、硬朗网格。
- Background: `#0e0e0e`; panels `#171717` with 2 px `#f0f0f0` borders on one axis only.
- Accents: primary `#e8ff00`, secondary `#ff4d2e`, warning `#ffffff`.
- Text: `#ffffff`, secondary `#9c9c9c`.
- Type: heavy condensed display, oversized labels, tight tracking; body minimal.
- Geometry: 0 radii, thick 2 px rules, asymmetric padding.
- Texture: none; contrast is the decoration.
- Icons: 2 px blocky strokes.
- Suits: bold statement builds, screenshot-friendly designs.
- Avoid: many small labels (unreadable at distance), decorative gradients.

## racing-telemetry

- Keywords: 赛车遥测、转速表、赛道蓝、红区。
- Background: `#0a0d10`; panels `#11161b` with `#1c2732` borders.
- Accents: primary `#21e6c1`, secondary `#ffd166`, warning `#ff3b30`.
- Text: `#eaf6f4`, secondary `#7c8b93`.
- Type: condensed italic for values, upright small caps for labels; numerals strictly tabular.
- Geometry: 8 px radii, angular speed-line separators, redline tick marks on temperature/power bars.
- Texture: faint carbon weave at 3%, only under module headers.
- Icons: 1.4 px strokes, arrow motifs.
- Suits: overclocking, temperature/power-heavy layouts.
- Avoid: fake lap times, animated needles, red as the whole brand color.

## ink-wash

- Keywords: 国风、水墨、留白、宣纸。
- Background: `#f3efe6`; panels `#fbf8f1` with soft `#ddd5c6` edges.
- Accents: primary `#2f4f4f`, secondary `#8b3a2f`, warning `#b8860b`.
- Text: `#22201d`, secondary `#6a6257`.
- Type: serif display, thin weight for the title, sans body for numerals.
- Geometry: 4 px radii, brush-stroke dividers as thin SVG lines, generous margins.
- Texture: one ink-wash blob behind the title at ≤10%, static.
- Icons: calligraphic 1.6 px strokes.
- Modules: vertical rhythm like a scroll; labels in small vertical text where space allows (never for primary values).
- Suits: calm, daylight, culturally themed builds.
- Avoid: heavy gradients, neon accents, dense grids.

## mono-terminal

- Keywords: 单色终端、命令行、等宽、绿色或琥珀。
- Background: `#050505`; panels `#0b0b0b`.
- Accents: primary `#33ff66` or `#ffb000` (choose one); warning `#ff4444`.
- Text: `#d8ffd8` / `#ffe0b0`, secondary `#6f8a6f`.
- Type: strictly monospaced, uppercase labels, aligned columns.
- Geometry: 0 radii, 1 px dotted separators, caret-style cursors as static glyphs.
- Texture: scanlines ≤5%, vignette.
- Icons: ASCII-style glyphs instead of SVG where practical.
- Modules: stacked `KEY: VALUE` lines with fixed-width alignment.
- Suits: minimal resource users, novelty builds.
- Avoid: proportional fonts, colored illustrations, glow on more than one value.
