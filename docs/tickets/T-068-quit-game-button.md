# T-068 — "Quit Game" button does nothing

- **Priority**: 🟠 Medium (bug)
- **Type**: UI
- **Status**: ✅ Done — `PurpleButton.OnClick` + `Renderer.RequestQuit()`; Quit Game exits with code 0
  (verified with an automated click). Also fixed: `Input.MouseLeftPressed` now counts a press from the
  frame's mouse events, so a quick click whose down + up land in one frame (touchpad tap) is no longer lost.

## Observed (2026-10-21)

Clicking the lobby's **Quit Game** button plays the click sound but the game keeps running. `LobbyLayout.cs:36`
creates `new PurpleButton( "#QuitGame" )` with no action, and nothing in `source/OpenTPW` requests an exit.
Closing the window (Alt+F4) does exit cleanly (code 0).

## Scope

1. Give `PurpleButton` an optional click action (or wire it in `LobbyLayout`).
2. Add a clean-exit path (the same one the window-close event uses) and call it from Quit Game.

## Acceptance criteria

- Clicking Quit Game in the lobby closes the game with exit code 0 (verified with an automated click).
