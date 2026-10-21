# T-070 — Lobby world names overlap the button column

- **Priority**: 🟢 Low (visual)
- **Type**: UI
- **Status**: ✅ Done — `LobbyCameraMode` pulled back (150 → 240, height 95) and aimed right of the cluster
  (`ScreenShift`), so the islands orbit mostly through the left of the view; `LobbyWorldSelect` gives an island
  no label and no click while it projects under the button column (x ≥ 730), which also fixes a click on a
  button entering the world behind it. Verified over a full orbit (7 captures). Islands still pass behind the
  buttons briefly — an orbit around a spread cluster can't avoid it; the original layout is not yet compared.

## Observed (2026-10-21, 1280×720 window)

`LobbyWorldSelect` draws each world's name over its projected island. As the islands orbit, labels such as
"Lost Kingdom" / "Halloween World" land under the right-hand `PurpleButton` column (drawn partly behind/over
the "Create New Player" buttons), and the island itself can be hidden by the buttons, making it unclickable.
The background is a flat cyan sky.

## Scope

1. Frame the lobby camera / island cluster in the left part of the screen (the buttons own the right side), or
   compare with the original layout and reproduce it.
2. Check whether the original lobby has a sky backdrop that isn't rendered.

## Acceptance criteria

- At 1280×720 no world name or island is covered by the button column during the orbit.
