# T-061 — Save-slot menu + save metadata

- **Priority**: 🟢 Low (UX) — builds directly on [T-059](T-059-save-load.md).
- **Type**: Engine / UI
- **Status**: ✅ Done — a clickable **save/load menu** over the 3 slots from T-059, each row
  showing a one-line **summary** of the saved park (money, in-game date, ride/shop counts, total visitors) so
  the player can tell slots apart at a glance, with per-slot **SAVE** / **LOAD** buttons. The summary is driven
  by a new `SaveGame.Meta` block captured at save time + a pure `SaveGame.Summary()` (older saves fall back to a
  summary derived from the placements). Toggled with **F8**; works in a park. Unit-tested (`Summary`/`Meta`
  round-trip).

## Context

T-059 gave us native save/load with 3 slots, but the only UI is the keyboard (F5 save / F9 load / F6 cycle) and
a single HUD line. There's no way to see what's in a slot before overwriting/loading it, and no mouse-driven
menu. This ticket adds a small slot menu + the metadata that makes it useful — the last bit of the "save-slot
UI" called out in T-059's remaining list.

## Scope

1. `SaveGame.Meta` (money, Year/Month/Day, visitors, ride/shop counts), captured in `Level.CaptureSave`.
2. A pure `SaveGame.Summary()` for one-line slot labels (fallback for pre-Meta saves from the placements).
3. A `SavePanel` overlay (F8) listing the slots with their summary + SAVE/LOAD buttons, wired to
   `Level.CaptureSave`/`ApplySave` + `SaveGame` file I/O; registered in `HudPanel.PointerOverUi`.
4. Unit-test the summary + metadata round-trip.

## Acceptance criteria

- The player can open a menu, see each slot's park summary, and save to / load from any slot by clicking;
  the summary + metadata are unit-tested.

## Affected files (anticipated)

`source/OpenTPW/World/SaveGame.cs` (Meta + Summary), `source/OpenTPW/UI/Widgets/SavePanel.cs` (new),
`source/OpenTPW/World/Level.cs` (capture Meta + add the panel), `source/OpenTPW.Tests/SaveGameTests.cs`.
