# WoW Forever C64 addon

Source lives in `addon/WowForeverC64/`. The installed copy lives under the Forever client's `Interface/AddOns/WowForeverC64/` directory. Edit the workspace source and run this from the repository root to install/update it:

```powershell
.\scripts\Install-Addon.ps1
```

The installer discovers `_forever_` from Blizzard's registry entry and copies only this addon's six files. Use `-WowPath` to select another `_forever_` client folder. Load it on your next login, or `/reload` if already in-game. The TOC uses interface `16001`, confirmed by the user's build `70888` report.

## Commands

| Command | Result |
| --- | --- |
| `/c64` or `/c64 show` | Toggle the status-effects panel. |
| `/c64 hide` | Hide the panel without stopping acquisition or the stream. |
| `/c64 refresh` | Refresh outside combat, subject to secrecy guards. |
| `/c64 report` | Open a fixed, selectable diagnostic report; Ctrl+C copies it. |
| `/c64 bridge off` / `on` | Hide/show the stream patch. |
| `/c64 bridge 2` / `3` | Select two- or three-pixel cells; this size is saved. |

Addon v0.5.0 sends pixel protocol v3, using two calibrated grayscale cells per byte for HDR compatibility. It preserves the v2 snapshot fields, including total duration for the C64 progress bar. The companion also accepts older v1/v2 packets when visible in its capture area. Data/calibration brightness leaves capture headroom, and resolution/UI scale changes are handled automatically. HDR was verified on one setup; other displays and mixed-DPI configurations remain YMMV. See [screen compatibility](../README.md#screens-resolutions-and-scaling) and the [project disclaimer](../README.md#disclaimer).

The default patch is 128×10 physical pixels, eight pixels from the bottom-left edges below chat. Larger cells produce 192×15 pixels with the same capacity. The status panel can be closed while the patch remains visible. Start the companion with `dotnet run --project src/WowForever.C64.Console -c Release -- watch`, then return to WoW. After upgrading from the original top-left color patch, both `/reload` in WoW and restart the companion.

## Collection

The addon collects helpful and harmful player status effects only. It no longer queries character identity, class, level, XP, or location. Ordered tooltip text is preserved, while the bridge transmits detail text only for Camp Benefits to keep the stream compact. Other effects carry names, IDs, stacks, expiration data, and total duration when readable.

Acquisition uses `C_UnitAuras` and `C_TooltipInfo`, not process memory. Every returned field is checked with `issecretvalue` before comparison, arithmetic, formatting, or persistence. `C_Secrets.ShouldAurasBeSecret()` must return false, and each aura index is gated separately. Missing required guards or API errors stop the scan. No alternate access method is tried to recover secret values.

`PLAYER_REGEN_DISABLED` immediately freezes the snapshot, invalidates live expiration values, and stops acquisition. Combat-time stream packets encode only the existing sanitized snapshot plus combat/heartbeat flags. `PLAYER_REGEN_ENABLED` refreshes acquisition. `UNIT_AURA`'s update payload is never inspected. Events are debounced by 250 ms, with a five-second acquisition refresh for delayed tooltip data; pixel transmission uses a separate 200 ms timer and does not query aura data.

An in-progress chunk burst finishes before the newest queued snapshot is sent, avoiding starvation if effects change frequently. Combat flags are carried in every frame, so freezing timers does not wait for full snapshot assembly. Periodic retransmissions let the Windows app attach late or recover dropped chunks.

`WowForeverC64DB` is a per-character SavedVariable containing the current sanitized snapshot, diagnostic counters, optional copied report, and cell-size setting. The game normally writes it at logout/UI reload. It is not a live file bridge, and the addon cannot force arbitrary file writes or open a Windows/network socket. Login/reload starts a fresh live snapshot; data from an earlier client uptime is not reused. Logout marks the saved snapshot offline.

## Verification so far

The user supplied a v0.2.0 report from Forever `1.60.1`, build `70888`, interface `16001`, on November 12, 2026. It showed seven readable buffs, `Aura scan: ok`, and no API errors or secret omissions across 64 refreshes. Camp Benefits was spell `1229741`, duration 7,200 seconds, with approximately 7,186.7 seconds remaining. Its tooltip included:

- Sharpening Wheel: Strength increased by 6.
- First Aid Kit: Stamina increased by 3.

These are observations, not hardcoded effect defaults. A tooltip field can contain several descriptions separated by blank lines; display code splits those lines. The bridge preserves their wording and bounds transmitted data rather than inventing missing effects. Completeness for other camp combinations remains unverified.

Optional right-side tooltip text and the terminating nil line produce `absent-or-wrong-type` diagnostics normally. A `missing-api`, `api-error`, or `secret-omitted` result needs separate interpretation. Game plural markup can appear in the raw report; timers use numeric expiration fields instead.

That report contained no combat transitions. Combat behavior and harmful-effect queries have not yet been confirmed in-game. Live Windows capture of the v3 grayscale bridge was subsequently verified with HDR enabled: 30 of 30 packets decoded from the new bottom-left position, assembling a complete live five-effect snapshot. If something fails during use, `/c64 report` and the companion's diagnostics provide the starting point.

See [the bridge documentation](../docs/bridge.md) for the binary layout and research references. No third-party addon libraries or tests are included.
