# T-072 — Placement grid has the wrong scale and origin

- **Priority**: 🟠 Medium (correctness: everything placed on the grid is offset from the terrain)
- **Type**: Engine / world
- **Status**: ✅ Done —
  - `PlacementGrid.FromLevelSettings( standard )`: `MapInfo.HeightfieldWidth/Height` tiles of
    `PlacementGrid.WorldTileSize` = 10, from `HeightfieldXStart/YStart` (0,0 everywhere; the .sam comment calls it
    the "Heightfield offset from map mesh"). Cross-checked: the entrance tile (48,17) lands on the gate (x 480,
    y 170) and ride models are authored at this scale (totem 3×4 tiles = 30×40 units, monkey 4×4 = 40×40).
  - **Rides were drawn off their footprint**: their meshes are corner-anchored (0..w·10) while `Ride.Position` is
    the footprint centre; `Ride` now shifts by half the upright footprint before rotating.
  - **Water/unbuildable tiles come from the `.lnd` mask** (`PlacementGrid.MarkScenery`): the scenery region at
    the entrance (nearest to the gate tile) is a walkable reserved forecourt, every other hole is water. The old
    height sampling only found ~80 scattered tiles; jungle now has 539 water + 552 entrance tiles (river + lake).
  - `ParkGround.FillUncoveredHoles`: a `.lnd` hole with no terrain triangle over it (the jungle river's thin
    arms) is ground again, so it no longer shows the sky (and is walkable).
  - Autoplace / camera use `PlacementGrid.FindOpenAnchor()` (biggest open lawn) instead of the grid centre — a
    river on jungle. The car-ride diagnostic never placed anything (the main autoplace had spent the cash):
    now free + `CommitPlacementNear`.
  - Also fixed: `3d.shader` (queue paths) fogged to pure sky colour beyond 30 units — the "cyan strips".
  - Verified in-game on all 4 themes (0 exceptions); unit-tested (`PlacementGridTests`, `LndFileTests`).
  - Note: saves keep tile coordinates, so a park saved before this change reloads at the new (correct) scale.

## Observed (2026-10-21, while decoding `base.lnd` for T-071)

`Level` builds the grid with `PlacementGrid.FromLevelSettings( standard, tileSize: 16f, worldCenter: terrain.Centroid )`
— a 95×84 grid of **16-unit** tiles (1520×1344 units) centred on the terrain's vertex centroid (486, 279 on
jungle). The terrain data says otherwise:

- the park fence (`jho_fol*`) spans world x[0,960] × y[0,860];
- the `.lnd` blend map covers exactly that rectangle at 4 pixels per tile → **96×86 tiles of 10 units from
  world (0,0)**, and its scenery holes (river, lake, entrance) land pixel-exactly on their meshes.

So the grid is ~1.6× too coarse and shifted: rides can be placed over the river/lake (seen in-game — the monkey
ride sits on the river), the grid extends well past the fence, and the T-050 "water tiles" (sampled from
nearest terrain vertex heights on the wrong grid) don't match the real water.

## Scope

1. Build the grid from the measured layout: tile size 10, origin (0,0,0); reconcile `Standard.sam`'s 95×84
   with the 96×86 of the `.lnd` map (border tiles?).
2. Derive unbuildable/water tiles from the `.lnd` alpha mask (0 = scenery) instead of vertex heights.
3. Re-check everything sized from `TileSize`: ride/shop footprints and model scaling (T-067 fits stalls to their
   footprint), queue paths, camera framing, minimap projection, autoplace positions, save files (tile coords).

## Acceptance criteria

- Grid tiles line up with the terrain (a ride footprint can't overlap the river/lake), unit-tested against the
  measured jungle layout; autoplace + saves still work.
