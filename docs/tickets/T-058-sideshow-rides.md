# T-058 — Sideshow rides (stalls / games)

- **Priority**: 🟢 Low (content)
- **Type**: Engine
- **Status**: ✅ Done (placeable from the catalog, peeps play them, takings credited; verified in-game)
- **Related**: [T-032](T-032-ride-engine.md) (ride engine), [T-041](T-041-ride-shop-placement.md) (placement).

## Done

- **Catalog**: the 5 jungle `sideshow/*.wad` stalls (`puzzle`, `squark`, `hyenas`, `junspray`, `arc2x3`) are
  added to the build catalog exactly like rides — their wad carries shape/mesh/script, so `RideShape.Load`
  + `SpawnRideAt` place them with no new placement infra.
- **`Ride.IsSideshow`**: auto-detected from the `sideshow/` path (holds even if the `.sam` is absent); when set,
  the ride reads its authored `UsageInfo.InitPricePerUse`/`InitCostOfGoods`/`InitChanceOfLoosing` (defaults
  10/30/70 from the shared `SideShow.sam`) and drives `TicketPrice` from the play price.
- **Takings** (`SideshowEconomy`, pure + 5 unit tests): a peep always pays the price; with the complementary
  chance they win and the park pays out a prize costing `costOfGoods`, so net = `price` (loss) or
  `price − costOfGoods` (win) — the authored jungle numbers give the house a small +1/play edge.
- **Peep seam**: when a peep reaches a sideshow's front and a slot is free it calls `Ride.PlaySideshow()`
  (rolls win/lose) and credits `ParkFinances.TakeSideshowTakings(net, won)` instead of `TakeRideTicket` —
  with `SideshowRevenue` + played/won counters (folded into `TotalIncome`, shown on the stats HUD).
- **Queue**: sideshow shapes mark only an *exit* cell (no entrance), so `SpawnQueuePath` falls back to the exit
  cell — a stall still forms a queue and earns.
- Verified in-game: the autoplaced `puzzle`/`squark` stalls drew peeps who queued + played; `SIDESHOW` takings
  accrued (`played 3, won 0 → 30`).

- **Win effect**: a winning play spawns the stall's authored particle burst (`Info.CreateParticleEffect`,
  default 80 = `Create2`) over the stand via the ride engine (verified in-game on `puzzle` + `squark`).
- **Indoor shelter** (with T-056): sideshows are `ISIndoors 1` → `Ride.IsIndoors` exempts peeps queued there
  from the bad-weather mood penalty and biases ride choice toward them when it's raining.

## Remaining (polish)

- A dedicated win *sound* (the burst is visual only) — no win-sound asset is named in the `.sam`.
- The VM `EVT_SIDESHOW_WIN` (212) script path is still just a constant; the economic win already drives takings
  + the burst via the peep-play path.

## Context

Sideshows are the small games/stalls (coconut shy, puzzle, etc.) — a distinct catalog from rides + shops.
OpenTPW has only VM string stubs (`SideshowTakings`, `EventSideshowWin`); none are placeable. The ride engine
+ catalog already model scripted placeable objects, so sideshows are a new catalog subtype, not new infra.

## What we know (RE recon)

- **Assets:** per-level `sideshow/*.wad` — jungle: `arc2x3`, `hyenas`, `junspray`, `puzzle`, `squark`; space:
  `lunar`, `arcade`, `scipuzz`, `marsmoon`; etc. Each is a normal ride-style WAD (`.MD2` + `.RSE` + `.sam`).
- **VM:** `EVENT`'s sideshow-win path + `SideshowTakings` already exist as opcode/string stubs.
- The `.sam`/`.RSE`/`.MD2` loaders, the build catalog, placement, queue + income tracking are all done.

## Scope

1. Add sideshows to the build catalog (load each `sideshow/<name>` like a ride: shape, mesh, script).
2. Treat a sideshow as a `Ride` subtype (or a thin variant): placement, a small queue, a play interaction,
   and **takings** routed to `ParkFinances` (`SideshowTakings`); wire the `EventSideshowWin` payout.
3. Verify one sideshow (e.g. jungle `puzzle`/`squark`) places, runs its script, and earns money in-game.

## Acceptance criteria

- At least one sideshow per level is placeable from the catalog, peeps use it, and it earns takings.

## Affected files (anticipated)

`source/OpenTPW/World/Level.cs` (catalog), `source/OpenTPW/World/Ride.cs` (or a `Sideshow` variant),
`VM/Handlers/*` (sideshow-win/takings), tests for the takings logic.

## Update (2026-10-21)

A game's takings now use the player's price (`TicketPrice`, what the visitor pays) — they used the authored
`InitPricePerUse` whatever the player set. Visitors judge a game's price against the authored one like a stall's
(`EntryFeeOpinion.StallPriceMood`: ≥ 2× refused, > 1.25× grudging, ≤ 0.75× pleased), and the advisor's
`PricingSideshowsTooExpensive` (TAG_SYSTEM 111) speaks when complaints pile up.
