# Pixel stream and C64 display

## Design

The addon renders its own sanitized status-effect model into a visible pixel patch. Windows captures only the bottom-left 768×384 pixels of the foreground WoW client area through desktop GDI. The patch sits eight physical pixels from the bottom and left edges, below the normal chat window. The decoder searches that region, validates each frame, and reassembles a complete snapshot. Normal operation saves no screenshots, and no non-packet pixels are logged or sent anywhere. The Windows app communicates only with the configured C64 REST service; there is no return path into WoW.

The patch contains 64×5 cells. At the default two physical pixels per cell it occupies **128×10** pixels; three-pixel cells use **192×15**. The addon accounts for both physical screen height and UIParent's effective scale, maintaining integer physical cell sizes when the resolution or UI scale changes. This is the intended behavior; a full resolution, UI-scale, and mixed-DPI monitor matrix has not been verified in-game. See [screen compatibility](../README.md#screens-resolutions-and-scaling). Addon v0.5.0 uses protocol v3: each byte occupies two grayscale cells, high nibble first, selected from sixteen brightness levels. Data and calibration use identical grayscale colors in the 0–0.35 intensity range, leaving brightness headroom and avoiding mixed-channel color shifts under HDR. Marker colors remain full intensity. Each frame calibrates against the ramp as actually captured, without assuming a linear brightness/gamma response.

A recognizable marker pattern precedes the calibration ramp. The decoder rejects damaged data using checksums and reports whether markers, calibration levels, headers, or checksums failed. HDR can remain enabled: the new bottom-left grayscale stream decoded 30 of 30 captured packets and assembled a complete live five-effect snapshot with HDR enabled on the user's display. Obscuring windows, extreme brightness clipping, or color filters that collapse different brightness levels can still make the patch unreadable. During live troubleshooting, the original patch rendered 2-pixel cells about 3.75 pixels wide on a 1440-high display, and its upper eight calibration levels all captured as white. Lowering brightness recovered distinct levels, but the original red/green encoding still failed checksums with HDR on. SDR decoded successfully, confirming the need to eliminate mixed-color data encoding.

Frames advance every 200 ms. Protocol v3 carries 108 snapshot bytes per frame; typical seven-effect snapshots need several frames, usually about a second for a burst. The sender emits a burst when encoded effects change, then heartbeats; it repeats the latest snapshot three seconds after a completed burst for recovery. It finishes an in-progress burst before adopting the newest queued update. There is no dependency on receiving a delta history, acknowledgments, or an external addon library.

An effect expiration uses client uptime in milliseconds modulo 2³², as does the frame clock. The companion converts the signed modular difference to a deadline on its monotonic clock. This supports durations below about 24.8 days. A random/time-derived session identifier distinguishes addon reloads and login resets.

## Frame layout, versions 1–5

All integers are unsigned little-endian. Cells 0–6 are fixed RGB markers. Cell 7 is green for grayscale v3–v5 and magenta for legacy red/green v1/v2. Cells 8–23 are the calibration ramp and 24–31 are reserved black. In v3–v5, cells 32–319 carry a 36-byte header and up to **108** payload bytes at two grayscale nibble cells per byte. Legacy v1/v2 use one red/green cell per byte and carry up to **252** chunk bytes.

| Byte offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `C64W` magic. |
| 4 | 1 | Protocol version. The current addon sends `5` with the v4 packet layout. |
| 5 | 1 | Flags for combat `1`, current aura snapshot `2`, partial scan `4`, truncated payload `8`, world ready `16`, and aura data available `32` (v4+). |
| 6 | 2 | Frame sequence, incremented for each heartbeat/chunk. |
| 8 | 4 | Session identifier. |
| 12 | 2 | Snapshot identifier. |
| 14 | 2 | Chunk index, or `65535` for a heartbeat or context packet. |
| 16 | 2 | Total chunk count for the latest snapshot. |
| 18 | 2 | Complete snapshot byte length, 1–8192. |
| 20 | 2 | This frame's chunk length, 0–108 in v3; 0–252 in v1/v2. |
| 22 | 4 | Adler-32 of the complete snapshot. |
| 26 | 4 | Adler-32 of header bytes 0–25, then 30–35, then this chunk. |
| 30 | 4 | Current client uptime in milliseconds modulo 2³². |
| 34 | 1 | Packet kind in v4 and later. `0` is an aura chunk or heartbeat. `1` is a stand-alone context packet. Zero in earlier versions. |
| 35 | 1 | Reserved zero. |
| 36 | variable | Chunk bytes. |

A chunk's offset is `index × 108` for v3, or `index × 252` for v1/v2. The final chunk can be shorter. Unused cells retain old values and are ignored using the checksum-validated length. Adler-32 detects transmission damage; it is not a cryptographic authentication mechanism.

Reassembly is keyed by session, snapshot ID, snapshot checksum, length, and protocol version. All chunks and the complete checksum must be valid before applying a current snapshot. Partial and truncated lists update readable fields while preserving unmatched auras. Complete lists confirm removals, including an empty list. Combat and signal status apply immediately even if a replacement snapshot is still incomplete.

## Snapshot payload

The first byte is an effect count, maximum 64. Each effect follows this layout:

| Size | Field |
| --- | --- |
| 4 | Spell ID, zero if unavailable. |
| 4 in v4+ | Aura instance ID, zero if unavailable. |
| 1 | Flags: harmful `1`, permanent `2`, known expiration `4`, known stacks `8`, known duration `16` (v2/v3). |
| 1 | UTF-8 name byte length, maximum 160. |
| 4 | Expiration uptime milliseconds modulo 2³²; meaningful only when known. |
| 2 | Stack/application count. |
| 4 in v2/v3; absent in v1 | Total duration in milliseconds, meaningful only when known. This drives the Camp Benefits bar. |
| 1 | Description count, maximum 24. |
| variable | UTF-8 name. |
| variable | For each description: a two-byte UTF-8 length followed by text, maximum 512 bytes per line. |

Camp Benefits is prioritized, followed by harmful effects, then other buffs. Only Camp Benefits carries description lines. Description text is capped at 2,048 bytes per effect. Strings are truncated at UTF-8 boundaries. Entire effect records are omitted if the 8,192-byte snapshot limit is reached; the truncated flag is set. The UI identifies partial/truncated results. Numeric unknowns remain unknown and never become fabricated live timers.

The companion also accepts a separate human-readable JSON snapshot via `preview --snapshot` and `send --snapshot` for offline layout work. That JSON is not the pixel wire format. `IPixelSource`, the protocol/reassembly layer, the status model, rendering, and `IUltimateClient` remain independent.

## Context payload (v4 and later)

Context packets carry the active aura snapshot header and a context payload covered by the frame checksum. The first byte holds death status in bits 0–1 (`0` unknown, `1` alive, `2` dead or ghost) and presence flags for level-up `4`, quest turn-in `8`, and zone change `16`. Present entries follow in that order. Each starts with a four-byte event clock, followed by a two-byte level or a one-byte UTF-8 length and title (up to 54 bytes for a quest or 36 for a zone). The latest entry of each kind repeats for up to 30 seconds. The companion uses the event clock to deduplicate and limit notice lifetimes.

The largest context packet is 107 payload bytes. The addon takes at most one of every two frames for context during aura bursts and repeats context about once per second when it stays unchanged. The companion admits events up to five seconds old during continuous capture and drops prior events when reconnecting. A timed notice starts its display duration after the C64 acknowledges the write. Capture runs separately from C64 writes. `watch --diagnostics true` reports frame rates, gaps, and payload counts.

## C64 theme and transport

The dashboard uses an original 2 KB RAM charset for text, frames, icons, and bars. ASCII-position uppercase letters/digits/punctuation remain readable; the upper half contains inverse glyphs. Screen cells carry explicit codes, colors, and optional semantic tile identities. See [the charset mapping and previews](charset.md) for the exact allocation and layout. This remains 40x25 character text, with no bitmap, raster tricks, interrupts, or resident C64 code.

The console checks `GET /v1/version`, pauses with `PUT /v1/machine:pause`, reads CIA2 bank state through `machine:readmem`, uploads glyph RAM, and selects its bank and pointers. Defaults use charset `$2000`, screen `$0400`, and `$D018=$18`. It configures standard text mode, disables sprites, and sets palette registers, then writes color RAM and screen codes through `PUT /v1/machine:writemem?address=...&data=...`. Every write is split into chunks of at most 128 bytes, the documented PUT limit. Sprite-pointer bytes are untouched. Normal CPU I/O mapping is required.

On the user's C64 Ultimate firmware 1.1.0, POST uploads intermittently became persistently unavailable: even a one-byte write returned HTTP 404 with `Could not read data from attachment`, while version queries, memory reads, and PUT writes still worked. The firmware POST path loads a temporary attachment file before performing DMA. PUT bypasses that attachment path entirely, without resetting or resuming the CPU. The precise cause of the firmware's temporary-file failure has not been established. HTTP errors now include the firmware's JSON error details, and watch reports the display online only after a successful send.

The transport caches glyph/screen/color/palette writes and sends small changed ranges. A state probe every 15 seconds triggers a complete repair only if hardware state differs. Failures invalidate the hardware cache. No automatic CPU resume is sent. The live-expiration flag clears if acquisition has not produced a snapshot within 15 seconds, even while pixel heartbeats continue. Protocol v1 remains readable, but its unknown total durations cannot produce a meaningful progress bar.

## Sources and policy review

Reviewed November 12, 2026:

- [PixelBridge](https://www.curseforge.com/wow/addons/pixelbridge) documents a read-only addon-to-desktop pixel strip for Forever. [LibSerpix](https://github.com/alex-berliner/LibSerpix) documents another pixel serialization approach. Their documentation informed the approach; this implementation does not copy their code or require either addon.
- Blizzard's [UI Add-On Development Policy](https://eu.forums.blizzard.com/en/wow/t/wow-user-interface-add-on-development-policy/1642), [secret-value explanation](https://news.blizzard.com/en-us/article/24246290/combat-philosophy-and-addon-disarmament-in-midnight), and [EULA](https://www.blizzard.com/en-us/legal/2c72a35c-bf1b-4ae6-99ec-80624e1b429c/blizzard-end-user-license-agreement) were reviewed for the requested export investigation. They impose addon requirements and restrictions on protected data, automation, and unauthorized software; the existence of other pixel bridges is not explicit Blizzard approval of this particular project. This implementation uses the normal addon UI sandbox and ordinary visible-screen capture, with no gameplay decisions/input or recovery of secret combat information.
- [Microsoft's GDI capture example](https://learn.microsoft.com/en-us/windows/win32/gdi/capturing-an-image) and [CreateDIBSection documentation](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection) support desktop bitmap capture and its resource lifecycle. The app uses thread DPI awareness, desktop `BitBlt`, a top-down 32-bit DIB, and `GdiFlush`; GDI resources are released on every capture.
- [Ultimate REST documentation](https://1541u-documentation.readthedocs.io/en/latest/api/api_calls.html) and [firmware routes](https://github.com/GideonZ/1541ultimate/blob/master/software/api/route_machine.cc) document pause and DMA writes. The [Commodore Programmer's Reference Guide](https://csclub.uwaterloo.ca/~pbarfuss/C64_Programmers_Reference_Guide.pdf), especially Appendix B, defines screen codes and the standard memory layout.
- Aura API candidates were checked against Blizzard's [Forever UI source export](https://github.com/reckhou/WoWBlzCode/tree/main/Forever/BlizzardInterfaceCode/Interface/AddOns/Blizzard_APIDocumentationGenerated), the installed AutoBiographer `5.1.3`, [ForeverXPBar](https://github.com/yurristudio/ForeverXP), and [Iyo4Ever's camp tracker](https://www.curseforge.com/wow/addons/iyo4ever/files/9481037). An actual-client report subsequently verified the helpful-aura path and two Camp Benefits descriptions. Live v3 capture was later verified on one HDR setup, as described above. Harmful-aura acquisition and combat handling remain unverified in-game.
- Forever's UI source documents the quest title and turn-in APIs, level-up payload, death status, login flags, and zone text used by the context collector. [Quest](https://raw.githubusercontent.com/reckhou/WoWBlzCode/main/Forever/BlizzardInterfaceCode/Interface/AddOns/Blizzard_APIDocumentationGenerated/QuestLogDocumentation.lua) · [Player and unit](https://raw.githubusercontent.com/reckhou/WoWBlzCode/main/Forever/BlizzardInterfaceCode/Interface/AddOns/Blizzard_APIDocumentationGenerated/UnitDocumentation.lua) · [World entry](https://raw.githubusercontent.com/reckhou/WoWBlzCode/main/Forever/BlizzardInterfaceCode/Interface/AddOns/Blizzard_APIDocumentationGenerated/SystemDocumentation.lua) · [Zone text](https://raw.githubusercontent.com/reckhou/WoWBlzCode/main/Forever/BlizzardInterfaceCode/Interface/AddOns/Blizzard_Minimap/Mainline/Minimap.lua)

SavedVariables persist too infrequently for a live dashboard. Game logs do not supply this structured buff/tooltip model, addon messages remain inside the game, and manual copying is only suitable for diagnostics. Those mechanisms are not used as live transport.
