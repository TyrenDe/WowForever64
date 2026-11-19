# Fantasy character-mode UI

The dashboard uploads an original 2 KB font/tile set into C64 RAM and selects it with the VIC-II. The C64 CPU stays paused. This is ordinary high-resolution 40×25 character mode: one foreground color per cell, one global background, no bitmap mode, sprites, raster effects, interrupt code, or resident 6502 program.

## Preview and exported art

The PNG preview uses the **same 2,048 glyph bytes, 1,000 screen codes, and 1,000 color values** as the hardware upload. It draws bits directly, with nearest-neighbor integer scaling, rather than substituting a PC font. It includes a modest C64 border and a fixed C64 palette; the Ultimate's configured palette and display aspect ratio can look slightly different.

- [Dashboard](../artifacts/fantasy-dashboard.png): the earlier user-provided seven-effect example.
- The earlier combat preview shows a frozen timer. The current renderer uses a red sword and amber estimated timers. [Combat preview](../artifacts/fantasy-combat.png)
- [Affliction art demo](../artifacts/fantasy-affliction-demo.png): an explicitly synthetic affliction and a partially filled bar, for visual review only.
- [All 256 glyphs](../artifacts/fantasy-atlas.png): labeled by hexadecimal codepoint.
- [Raw 2 KB charset](../artifacts/fantasy-charset.bin).

Regenerate the main assets from the repository root:

```powershell
dotnet run --project src/WowForever.C64.Console -c Release -- preview --snapshot samples/status-effects.json --output artifacts/fantasy-dashboard.png
dotnet run --project src/WowForever.C64.Console -c Release -- charset --output artifacts/fantasy-atlas.png
dotnet run --project src/WowForever.C64.Console -c Release -- charset --output artifacts/fantasy-charset.bin
```

`preview` still prints a diagnostic text view. The PNG is the authoritative visual preview, including custom icons, per-cell colors, and inverse glyphs. `--page 1` previews/sends the second page, using a zero-based index.

## Architecture

- `FantasyCharset` generates original text, frame, icon, and bar artwork. No Commodore ROM dump or external font is required.
- `CharacterSet` owns a validated, immutable copy of exactly 256×8 bytes and can load a `.bin` file.
- `CharacterCell` contains an explicit byte code, foreground color, and optional semantic `TileId`. `TextScreen` owns exactly 40×25 cells; it is not a string buffer.
- `TilePainter` composes strict-bounds borders, panels, separators, ornamented headers, indicators, 2×2 icons, and pixel-stepped progress bars. Text writing is a convenience over cells.
- `DashboardRenderer` composes a pinned Camp Benefits feature panel and a lower aura panel. It does not perform transport or VIC setup.
- `VicConfiguration` validates placement and calculates register values. `VicController` owns register setup. `C64Display` sequences paused initialization, glyph upload, and changed character/color ranges through the existing `IUltimateClient`.
- `ScreenPreview` renders the actual bit patterns and writes PNG using .NET's built-in compression, with no image-library dependency.

## Glyph strategy and codepoints

Each glyph is eight bytes, top row first; bit 7 is its leftmost pixel. A set bit uses the cell's foreground color and a clear bit uses the global background. Bytes for code `c` begin at `c × 8` in the charset.

Codes `$20–$5F` are an original uppercase face using **ASCII positions**: space `$20`, punctuation `$21–$2F`, digits `$30–$39`, punctuation `$3A–$40`, and letters `$41–$5A`. Slots `$5B–$5F` contain `[ \ ] ↑ _`; `$5E` doubles as the semantic `UpArrow` tile rather than a plain caret. Lowercase input folds to uppercase. Unsupported characters become `?`. This intentionally replaces the earlier PETSCII/ROM screen-code mapping; existing screen dumps must be composed again with this renderer.

The other 64 lower-half slots contain UI tiles. The generator provides inverse glyphs at `$80–$FF`, with `$DE` assigned to a down arrow for negative Camp Benefits. The renderer uses a red minus with older external charsets.

| Code | Tile | Code | Tile |
| --- | --- | --- | --- |
| `$00` | Outer top-left | `$10` | Separator left join |
| `$01` | Outer top edge | `$11` | Separator center |
| `$02` | Outer top-right | `$12` | Separator right join |
| `$03` | Outer left rail | `$13` | Title left ornament |
| `$04` | Outer right rail | `$14` | Title right ornament |
| `$05` | Outer bottom-left | `$15` | Filled diamond |
| `$06` | Outer bottom edge | `$16` | Hollow diamond |
| `$07` | Outer bottom-right | `$17` | Blessing/sparkle |
| `$08` | Panel top-left | `$18` | Hourglass |
| `$09` | Panel top edge | `$19` | Small shield |
| `$0A` | Panel top-right | `$1A` | Small sword |
| `$0B` | Panel left rail | `$1B` | Small skull |
| `$0C` | Panel right rail | `$1C` | Small flame |
| `$0D` | Panel bottom-left | `$1D` | Live gem |
| `$0E` | Panel bottom edge | `$1E` | Pause indicator |
| `$0F` | Panel bottom-right | `$1F` | Lost-signal indicator |

| Code | Tile |
| --- | --- |
| `$60–$68` | Bar interior with 0–8 filled pixel columns |
| `$69–$6A` | Left/right bar caps |
| `$6B` | Chain link |
| `$6C` | Rune |
| `$6D–$70` | 16×16 campfire: top-left, top-right, bottom-left, bottom-right |
| `$71–$74` | 16×16 shield, same tile order |
| `$75–$78` | 16×16 sword, same tile order |
| `$79–$7C` | 16×16 skull, same tile order |
| `$7D` | Heart |
| `$7E` | Left arrow |
| `$7F` | Pound sign |

`TileId.cs` is the exact symbolic mapping. Font and glyph data are separate from screen/color buffers. Text remains available for titles, effect names, times, status text, and any future character/zone fields, though the current collector remains status-effects-only.

## Final layout polish

The normal active-enchantments banner is removed. Camp Benefits starts two rows higher at row 4, and the lower section now has nine visible aura rows (15–23), with five camp-detail rows retained. Coordinates are zero-based. Combat, missing-signal, partial-data, and waiting notices replace the decorative ribbon at row 2 only when needed, so warnings remain visible without reserving another row.

Recognized English bonus phrases are compacted generically: `Sharpening Wheel: Strength increased by 6.` becomes `STRENGTH +6`. Source prefixes are omitted, while unknown wording is retained after the prefix rather than guessing values. Positive camp lines use the green up-arrow at `$5E`. This is presentation only; the acquired descriptions remain unchanged. All three headings are solid white, with small cyan title flourishes; the shield and sword remain bronze without white tip highlights. Active effects use green status values; timed values are cyan and harmful effects remain red.

## Memory configuration

Default settings in `appsettings.json`:

```json
"display": {
  "screenAddress": 1024,
  "charsetAddress": 8192,
  "charsetFile": null
}
```

JSON addresses are decimal. The defaults are screen `$0400` and charset `$2000`, in VIC bank zero. Screen memory occupies 1,024 reserved bytes, with only the first 1,000 displayed cells written. Charset memory occupies `$2000–$27FF`. Color RAM is always `$D800–$DBE7`, independent of the selected VIC bank.

Placement validation requires screen alignment to 1,024 bytes, charset alignment to 2,048 bytes, both in one 16 KB VIC bank, and no overlap, including screen sprite-pointer space. To avoid CPU ROM/I/O/system pages, supported RAM is `$0400–$9FFF` or `$C000–$CFFF`. The VIC ROM-shadow ranges `$1000–$1FFF` and `$9000–$9FFF` are excluded. For example, charset `$2800` is valid with the default screen; screen `$4400` and charset `$6000` are a valid bank-one pair.

`charsetFile` can select a different exactly-2,048-byte file with the same mapping. Relative settings paths resolve beside the selected JSON configuration. `--charset <file.bin>` overrides it and resolves from the working directory. A malformed size or invalid layout is rejected before any C64 writes.

## Initialization and updates

1. Pause through the Ultimate API. No automatic resume is sent.
2. Read CIA2 `$DD00/$DD02`, then set `$D011=$0B` to hide the display during initialization, with bitmap and extended-color modes cleared.
3. Set inverted bank-select bits 0–1 and make them outputs, preserving unrelated bits.
4. Upload 2,048 glyph bytes to charset RAM when needed.
5. Set `$D016=$08` for 40-column single-color text; disable sprites through `$D015=0`; set `$D018` and palette.
6. Upload color RAM and screen codes, then set `$D011=$1B` to show the 25-row text display.

The pointer is `((screenOffset / 1024) << 4) | ((charsetOffset / 2048) << 1)`, relative to the VIC bank. Defaults produce `$D018=$18`, selecting screen `$0400` and charset `$2000`.

Normal updates compare cells and colors with the last successful upload. Small changed runs are written at their actual addresses; short gaps are coalesced. Highly fragmented/large changes use a full buffer. Charset data is uploaded only on initial setup, charset changes, or invalidated hardware state. Every 15 seconds the live loop probes bank, text mode, pointers, palette, a screen sentinel, and a font sentinel. A healthy probe does not force a repaint; a detected reset or failure triggers a complete repair. The probe is not a full-memory integrity check.

The normal CPU I/O mapping must remain visible; start from ordinary C64 BASIC with the Ultimate menu closed. Display RAM is dedicated to this dashboard and overwrites any BASIC/program data in those configured regions. Reset the C64 to return to normal use. No timing dependency exists on the C64 CPU, timers, or interrupt service routines.

## Hardware validation after visual review

No WoW session is necessary for the first custom-font check:

```powershell
dotnet run --project src/WowForever.C64.Console -c Release -- send --snapshot samples/status-effects.json
```

This sends a **static sample**, not live gameplay data. Check the custom lettering, ornamental frame, campfire/shield/sword icons, green effect diamonds, and Camp Benefits bar against the preview. Confirm that the CPU stays paused and no cursor appears. A second send can replace the sample without resuming the CPU.

For context notifications, install the current addon, use `/reload`, and restart the companion with `watch`. The companion accepts v1–v4 packets and flags their older version. Duration bars require a known total duration.

## Verification performed

No automated test suite is included. During development, Release builds and Lua syntax checks passed. Manual checks confirmed exact 2 KB export/loading, all 1,024 inverse rows, default and alternate-bank pointer calculations, rejection of ROM-shadow/overlapping layouts, and actual 1,000-cell previews. A temporary local HTTP stub recorded the paused upload/configuration sequence and showed that changing one timer digit caused a **single-byte screen write**, without a charset upload. The custom character-mode dashboard was subsequently confirmed on a physical C64 Ultimate. These observations do not establish compatibility with every hardware, firmware, or display configuration; see the [project disclaimer](../README.md#disclaimer).

The memory design follows Commodore's original [graphics and bank description](https://www.devili.iki.fi/Computers/Commodore/C64/Programmers_Reference/Chapter_3/page_101.html), [bank and screen pointers](https://www.devili.iki.fi/Computers/Commodore/C64/Programmers_Reference/Chapter_3/page_102.html), and [RAM character memory table](https://www.devili.iki.fi/Computers/Commodore/C64/Programmers_Reference/Chapter_3/page_104.html). Transport uses the documented [Ultimate pause/readmem/writemem API](https://1541u-documentation.readthedocs.io/en/latest/api/api_calls.html).
