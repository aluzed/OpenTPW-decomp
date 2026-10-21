# T-065 — Player-facing HUD + debug overlay toggle

- **Priority**: 🟠 Medium (UX)
- **Type**: UI
- **Status**: ✅ Core done — the always-on HUD shows only player essentials (date, weather, money, visitors,
  admission, challenge, ticket goals, loan, park rating, contextual build/manage info). The debug dump (income
  breakdown, staff/litter counts, crowd thoughts, save-slot hints, FPS counter) is off by default and toggled
  with the **backtick** key, persisted as `GameSettings.ShowDebug` (also drives `Renderer.ShowFps`).
  Commit `5fe21e2`.

## Remaining

- The HUD is still text lines; a graphical TPW-style HUD (money counter, icons, the original panel art) is a
  larger follow-up.

## Affected files

`source/OpenTPW/Client/GameSettings.cs`, `source/OpenTPW/Client/Renderer.cs`,
`source/OpenTPW/UI/Widgets/ParkStatsPanel.cs`.
