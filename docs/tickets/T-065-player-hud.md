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

## 2026-10-21 — layout data is in the widget models

The `ui.wad` widget `.MD2`s carry their own placement: e.g. `mainpanel` mesh `base` at (238, −1245, 7) (a 400×520
plate), `panel` = four 675×426 cards `pan_money`/`pan_info`/`pan_buy`/`pan_staff` stacked along −Z, `panelend` their
end caps, `b_money` a 118×118 button with `disabled`/`hilite`/`hidown`/`helddown`/`down` state meshes,
`w_small`/`w_big` window frames, `res_panel` the research bar. They live in a 3D "UI world" seen by a fixed UI camera;
with that camera's parameters (in `tp.exe`, still to find) the original HUD could be rendered from these models
directly. The HUD is otherwise localized now (UITEXT labels follow `GameLanguage`).

## 2026-10-21 — the original's icons on the HUD buttons

The HUD now uses the game's own button art from `ui.wad` (`stexture/b_*.wct`, 51×51 with alpha; a `d` suffix is the
pressed look, `l` lit, `g` greyed — `HudPanel.Icon`/`DrawIconButton`): the build menu's tabs are the original
category icons (`b_srides`, `b_sshows`, `b_sshops`, `b_sfeature`, `b_shandy`; the selected one in its `d` look),
and the manage bar's buttons carry theirs (`b_minus`/`b_plus` for prices and the fee, `b_loan`, `b_open` for the
gates, `b_fire`, `b_move`, `b_resrch`, `b_upgrade`, `b_delete_login` — the bomb — for selling, `b_staffcost` for
training). The 3D widget panels themselves (and their UI camera) remain to do.
