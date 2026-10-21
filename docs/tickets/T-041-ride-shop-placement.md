# T-041 — Ride & shop placement (build tool)

- **Priority**: 🟡 Feature
- **Type**: Engine / UI
- **Status**: ✅ Done — clickable catalog + footprint preview (green/red) + place-on-click with cost
  charging + queue registration, a clickable manage UI, **sell/demolish** (rides + shops, with refund),
  and **rotation** (R, 90° steps). `SetupDevPark` is an empty park the player fills. Only nice-to-haves
  remain (drag-rotate handle, shop price controls). **2026-10-21: every stall the theme ships is buildable**
  — the catalog lists the theme's `shops/` archives (fries, ice cream, restaurant, gift / balloon / costume
  shops…) beside the default food/drink/toilet stalls, each with its authored name, price, cost of goods and
  build cost; the catalog rows shrink to fit (~27 items). A purchase applies the stall's `UsageInfo.*Effect`
  values (hunger/thirst relieved in proportion, mood and energy lifted, `LitterEffect`% chance of a dropped
  wrapper); gift/balloon/costume shops are impulse buys a passing visitor makes once per visit. Saves keep the
  stall's catalog key. Each theme's toilet resolves (`loo`/`horloo`; the "Super Toilet" `loo_big`/`royaloo`/`horsuloo`
  is buildable too where the theme has one), the dev park no longer sells its only
  food stall in the sell-test, and the generic autoplace places its stalls near the wanted tile.
  **Souvenirs show**: a visitor who buys a costume walks in the theme's costume sprite set
  (`esprites/<Theme>/Costumes` — jungle tiger SPR_TI, hallow SPR_HK, fantasy SPR_BK, space SPR_SK) and one who buys
  a balloon carries one (`esprites/Generic/Balloons/SPR_BL`: four colours, each a balloon + bursting frame, hotspot ~74 px above the
  feet, drawn at the kid's scale) until they leave.
- **Parent**: [T-038](T-038-park-management-ui.md). **Needs**: [T-040](T-040-build-mode-foundation.md).
  **Couples with**: [T-036](T-036-peep-pathfinding.md) (paths/queues).

## Done

- **Catalog** (`BuildCatalogItem`): the jungle rides (footprint from `RideShape`, **cost read from the
  `.sam`** `Upgrades[0].CostOfUpgrade`) + a food shop.
- **Clickable build/manage UI** (T-038): a shared `UI/Widgets/HudPanel` base (mouse→base-space mapping,
  hit-test, button drawing) backs two panels —
  - **`BuildPanel`** (right column): a button per catalog item that **selects on click**, so every item is
    mouse-reachable (the old number-key list capped at 1–9; the added drink stall had pushed `researcher`
    past key 9). Selected item highlighted green, unaffordable items dimmed red, re-click toggles off.
  - **`ManagePanel`** (bottom-left bar): clickable buttons for the previously keyboard-only manage actions
    — admission fee ±, take/repay loan, and (when a ride is selected via the Default tool) its ticket
    price ± and research/upgrade. Each calls the same `ParkFinances`/`Ride` methods as the keyboard
    shortcuts (those methods are covered by the T-042/T-044 tests); disabled actions are dimmed.
  `BuildMode` consults `HudPanel.PointerOverUi()` so a click on either panel doesn't also act on the tile
  behind it; the number-key/keyboard shortcuts still work. `ParkStatsPanel` no longer duplicates the
  catalog. Verified in-game: panels render (all 10 catalog items + the FEE−/FEE+/LOAN economy row), no
  exceptions. (Clicks couldn't be synthesised in this env — no `xdotool` — so the hit-test glue is by code
  review; the underlying actions are test-covered.)
- **Lobby vs in-park HUD split**: the lobby front-end (`LobbyLayout` — logo + Create-Player/Quit buttons)
  was being drawn over the loaded park. `Level.InPark` (set when the park loads, cleared on load failure)
  now selects the HUD: **in-park** → the build/manage/stats panels; **lobby** (or park-load failure) →
  the front-end. The cursor shows in both. Verified in-game: the park HUD renders alone, no front-end
  overlay; affordability dimming is now visible (e.g. `coaster1` $10000 dimmed at $9998 balance).
- **Footprint preview**: the selected item's `Width×Height` quad follows the cursor, **green** when
  `grid.CanPlace` & affordable, **red** otherwise.
- **Commit** (`CommitPlacement` → `SpawnRideAt`/`SpawnShopAt`): validates + `TryPlace`, spawns the
  `Ride`/`Shop`, **charges the cost** (`ParkFinances.PayBuild`, refused if unaffordable), and registers
  the ride's `RideQueue` into the shared list so peeps use it. Replaces the hardcoded dev layout.
- **Verified in-game** (deterministic `OPENTPW_AUTOPLACE` exercising the exact commit path): totem +
  monkey + shop placed, money debited by exactly their costs (3250+2000+500), 2 queues registered,
  peeps queue/ride/eat. Interactive select+preview+click dispatch verified via T-040.

## Done (sell / demolish)

- **Sell/demolish a ride** — select it (Default-tool click) then the **SELL** button (`ManagePanel`, shows
  the refund) or **Delete**. `Ride.Despawn()` tears down everything it owns: engine objects + light/particle
  proxies (`RideEngine.Despawn`), entrance/exit markers + queue-path quads (`Ride.OwnedEntities`), and the
  coaster track/train (`Ride.Track.Despawn` — pylons, ribbon, cars). `Level.DemolishRide` drops its
  `RideQueue` from the park list (peeps stop targeting it), frees its footprint **and** queue-path grid
  cells, and **refunds** `Ride.SellRefundFraction` (50%) of the build cost. `BuildMode.SellSelected`
  clears the selection (and any active track-laying ref). Verified via `OPENTPW_AUTOPLACE`: demolishing
  the **coaster** (track + train + queue) gave `rides 3→2, queues 3→2, refund 5000` (of 10000), no
  exceptions.
- **Shops are sellable too**: `Shop` gained a grid footprint (`TileX/Y/W/H`, `Covers`), `BuildCost` and
  `Despawn`; the Default tool selects a ride *or* (if none covers the tile) a shop, and `Level.DemolishShop`
  frees its cells + refunds 50%. The manage UI shows a `SELL <stall> $N` button for a selected shop.
  Verified via `OPENTPW_AUTOPLACE`: `sell-test shop Food Stall: shops 2→1, refund 250` (of 500).
- Note: peeps already en route to a sold ride hold its `RideQueue` and finish/re-route gracefully (the
  queue object lives until they drop it).

## Done (rotation)

- **Place a ride at an orientation** (R while placing, 90° CW per press; the dev catalog uses number/click
  selection then R to rotate). `RideShape.Rotated(turns)` rotates the footprint, cells, entrance/exit and
  track connectors (a point maps 90° CW to `(Height-1-y, x)`; odd turns swap W/H). The `Ride` ctor takes a
  rotation, stores the **rotated `Shape`**, and spins its **mesh** about the footprint centre (rotate each
  part's local offset + compose the yaw, captured by `RegisterBody` as the rest pose so animation composes
  on top). Placement threads the orientation through `BuildMode` → `commit` → `CommitPlacement` →
  `SpawnRideAt`: the preview footprint + grid reservation + tile dims all use the rotated W/H, and the
  entrance/exit markers + queue path follow the rotated entrance automatically. Shops are square and staff
  have no footprint, so they ignore rotation. The placement readout shows `ROTATE (R): N deg`.
  Unit-tested (`RideShapeTests.Rotated*`); verified in-game via `OPENTPW_AUTOPLACE`: `rotate-test: totem
  placed rot1, footprint 4x3` (upright 3×4), scene renders cleanly, no exceptions.

## Done (HUD polish)

- A shared `HudPanel.DrawBar` gauge helper backs two readouts: `ParkStatsPanel` now draws a translucent
  **backing panel** behind the stats (readable over the bright park) and a **PARK RATING** bar = average
  visitor happiness (`Peep.AverageHappiness`), colour-coded green/amber/red; `ManagePanel` renders the
  selected ride's **research** as a live progress bar instead of a flat % button. Verified in-game (rating
  bar always visible — e.g. amber `PARK RATING 56%`), no exceptions.

## Remaining (follow-up)

- None blocking — T-041 covers catalog/preview/place/manage/sell/rotate + HUD readouts. **Shop price controls
  now done** (PRICE± buttons / `,`·`.` keys / HUD on a selected food-or-drink stall; toilets stay free;
  persisted through save/load). Remaining nice-to-have: a real drag-to-rotate handle.

## Context

Rides and shops are placed by the hardcoded `SetupDevPark`. This ticket adds the player build tool:
pick a ride/shop from a catalog, preview its footprint, place it (charging the cost), rotate it, and
sell/demolish it.

## Reference (original)

`ACTION_SET_RIDE`(5) selects the ride to place, `ACTION_SET_RIDE_ROTATION`(10) rotates it,
`ACTION_LMB_DOWN/UP`(15/20) commit, `ACTION_SET_RIDE_QUEUE`(35) lays the queue. Placement validates the
grid cell type ("Cannot place … the cell is of type %d"). We already have `RideShape` (footprint +
entrance/exit), `PlacementGrid.TryPlace`, ride cost in the `.sam` (`Upgrades[0].CostOfUpgrade`), and
`ParkFinances`.

## Work

1. **Catalog menu**: list available rides (`levels/*/rides/*`) and shops; selecting one enters a
   "place" tool (T-040 mode) with that item.
2. **Footprint preview**: show the `RideShape` footprint on the grid under the cursor, green/red for
   valid/invalid (`grid.CanPlace`), with rotation (`SET_RIDE_ROTATION`).
3. **Commit**: on LMB, `grid.TryPlace` + spawn the `Ride`/`Shop`, **charge the cost** via
   `ParkFinances` (refuse if unaffordable), drop on terrain. Replace `SetupDevPark`'s fixed layout.
4. **Queue/path stub**: place the entrance queue path (`SET_RIDE_QUEUE`); full path graph is
   [T-036](T-036-peep-pathfinding.md).
5. **Sell/demolish**: select a placed object (T-040) → remove it, clear its grid cells, partial refund.

## Acceptance criteria

- The player picks a ride from the menu, sees a valid/invalid footprint preview, places it (money
  drops by its cost), rotates and sells it; peeps then queue for the newly placed ride.

## Affected files

`source/OpenTPW/World/Build/*`, `source/OpenTPW/World/Rides/Ride.cs`, `RideShape.cs`,
`source/OpenTPW/World/Shop.cs`, `World/Terrain/PlacementGrid.cs`, `ParkFinances.cs`, `Level.cs`, UI.
