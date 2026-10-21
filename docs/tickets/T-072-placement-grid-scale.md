# T-072 — Placement grid has the wrong scale and origin

- **Priority**: 🟠 Medium (correctness: everything placed on the grid is offset from the terrain)
- **Type**: Engine / world
- **Status**: ☐ To do

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
