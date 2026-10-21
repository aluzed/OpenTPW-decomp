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
  larger follow-up. **Finding (2026-10-21)**: the original UI is built from 3D widgets — `ui.wad` holds hundreds of
  `.MD2` models (one per button: `b_finance`, `b_loans`, `b_map`, `b_buystaff`…, frames `!frame`/`!f_plain`, sliders
  `slider_*`, windows `w_small/w_med/w_big/w_dialog`, panels `mainpanel`/`panel`/`res_panel`) with ~460 textures,
  and no layout file — positions/behaviour live in the executable. Rebuilding it means reversing the UI layer.

## Affected files

`source/OpenTPW/Client/GameSettings.cs`, `source/OpenTPW/Client/Renderer.cs`,
`source/OpenTPW/UI/Widgets/ParkStatsPanel.cs`.
