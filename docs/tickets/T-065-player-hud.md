# T-065 — Player-facing HUD + debug overlay toggle

- **Priority**: 🟠 Medium (UX)
- **Type**: UI
- **Status**: ✅ Done — the original's HUD and windows (see the end); was: core done — the always-on HUD shows only player essentials (date, weather, money, visitors,
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
training). The park readout (top left) puts the matching icons before its lines: `b_money`, `b_svisit`, `b_entry`,
`b_loan`, `b_speeps` (thoughts), `b_ssatis` (rating). The 3D widget panels themselves (and their UI camera) remain
to do.

## 2026-10-21 — the original's HUD and windows

Rebuilt on captures of the PC version (docs/10-ui-reference.md) and `ui.wad`'s own art:

- **HUD** (`ParkHud`): the balance with the `$` glyph, cash digits, trend arrow and purchase flash; the golden
  tickets; the handheld (`mainpanel.MD2` face — its casing, studs and button recesses drawn around it, as the
  original's shading showed them —, beacon, date LCD, happiness capsule from `meter.wct`, six buttons) and the hint
  strip with the game's own hover hints (`UIHELPTEXT.str`) and what the advisor says. The advisor stands upright
  in the theme's outfit (`levels/<theme>/advisor.wad`), facing the player.
- **Windows** (`GameWindow`: a `ui.wad` frame, yellow title, black insets, red→green gauges, round buttons):
  buy (`w_big`), ride / shop, park information, financial information, loans, staff, save / load (`w_park`),
  research lab (`w_res`), map (`w_map`), options (`w_small`), and messages — golden ticket, challenge,
  bankruptcy — in `w_dialog`. The old text panels are the debug overlay only; the bottom bar is gone.
- Text is drawn at whole pixels and whole-number scales (the point-sampled fonts lost the dot of "!").

## 2026-10-22 — the windows come alive

- Turning 3D previews in the buy and ride / shop windows (`ItemPreview`, placed through the view-projection —
  `ScreenSpace`); the advisor is anchored the same way, and stands in the lobby too (which now floats on its sea).
- Ride window: call a mechanic, open / close the gate, ride it (camera on the lead car, the tour bird flies you over
  the park), move (shops too); shop window: customers / takings / profit last month, gate, move.
- Hire list: named candidates (`*_NAMES.str`) with grade and wage; staff windows show the name.
- Visitor window (click a visitor): name, thought, happiness and needs, money.
- The camcorder (handheld's camera button): walk the park at eye height.
- The original's keys (B H F I R Space G C), Esc closes windows, the game's own UI sounds (`sfUiHD.sdt` / `UIHD.sdt`)
  and its hints for every window button (`UIHELPTEXT.str`).

## 2026-10-22 (night) — the remaining windows and tools

- The park's lists (all items, all staff, all visitors; A / S / V): click an entry to open it and fly to it.
- Ride window: the ride's duration; sideshow window: prize cost and chance of winning; shop window: quality of goods,
  the special ingredient (fat / salt / ice / sugar, with its effect on visitors) and "apply to all stalls of this
  type". UITEXT indices 38–40 and 69 were off by one (37 is a second "Scrap value").
- Staff training budgets (UITEXT 147–149): a monthly budget per kind of staff, spent on training when the month
  closes; opened from the staff list.
- The queue tool (ride window's queue button): lay the queue a piece at a time in any direction from its open end,
  with the world's straights, bend and open end; saved with the park.

Still to do: the advisor's gesture animations (`Advisor_<theme>m*.MD2` — the ride keyframe container but with extra
record types, addressed against the global 25-mesh model; see T-046) and the exact UI camera (positions are
measured, not read from `tp.exe`).
