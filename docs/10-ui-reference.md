# Original in-game UI — reference

Measured from captures of the PC version (Lost Kingdom, 4:3, 960×720 crops of a 720p recording; see
`iso/ui-reference/`, git-ignored — the captures are the publisher's). Positions are given as fractions of the 4:3
screen so they carry to any resolution.

## Always-on HUD

There are no text panels on screen during play — only these:

| Element | Where | What |
|---|---|---|
| **Money** | top left, ~6 % from the left, ~5 % from the top | a large green-yellow `$` glyph and the balance in the big outlined cash digits (`Cash_alpha.wct`), ~6 % of the screen high; a green up / red down arrow after it (`cashtrend`) for the trend; spending pops up as a smaller yellow `$ 650` under it that fades |
| **Golden keys / tickets** | top right | `1 ×` and a golden key (`gkey`), then golden tickets (`gtick`) — counts only |
| **Handheld** | bottom left, ~25 % wide × 40 % high | `mainpanel.MD2`: a blue walkie-talkie with a red beacon on top, the date in a green LCD (`4.1.2000`), a vertical happiness thermometer (smiley top / frown bottom), and six round gold buttons — finance (till), info (i), awards (coin), advice (bulb), map (green radar), postcard (camera) |
| **Hint bar** | bottom centre, ~45 % wide, one line | a translucent dark strip with small white centred text — the context hint ("Left-click to buy", "Small Toilet: Left-click to view/edit…") |
| **Advisor** | bottom right, ~20 % wide | the bug, **upright and full-body**: head (wearing the theme's hat — a safari hat on Lost Kingdom), white gloves gesturing either side, the black ball body below; he faces the player squarely, never tilted with the camera, and has **no speech box** — he speaks (voice + lip-sync), the text goes to the hint bar / a message window |

## Windows

Every window (`w_big` / `w_small` / `w_dialog`) is a rounded **dark-blue bezel** framing a **green wavy-patterned
panel**, with a column of round buttons down the bezel's right edge (close = red ✕, confirm = blue ✓, others =
context icons), the title centred at the top in yellow, `Cash $ 50000` top right. Content sits in **black inset
boxes** with thin teal borders; text is small white, labels left / values right, gauges are red→yellow→green bars.

- **Buy Ride / Shop / Sideshow / Feature** (one window, tabs): preview box (3D model turning) top left, a stats box
  under it (Number owned, Excitement, Safe capacity, Reliability, Working life as bars), on the right four round
  category tabs (rides / sideshows / shops / features) above a list with a teal header (`Name` | `Price` | ·),
  unresearched items in red (`??? Mystery Ride! ???  1 gt`).
- **Ride / shop info**: preview top left, stats box top right (Users last month, Age, Excitement, Reliability, State
  of repair, Remaining life, Scrap value), a row of three slider boxes (Speed / Capacity / Duration, or Quality /
  Amount / Sale price), a row of round action buttons at the bottom (sell = bomb, move, queue, ride it, repair,
  upgrade, open/close gate).
- **Hire staff**: preview, a candidates list (`Candidate Name` | `Monthly Wage`), an energy bar.
- **Message**: a small dark-blue rounded box (`w_dialog`) with white text and a ✓.

The game view stays visible around the windows; nothing else is drawn over the park.
