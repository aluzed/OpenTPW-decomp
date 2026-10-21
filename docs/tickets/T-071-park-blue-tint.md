# T-071 — Park terrain looks uniformly blue (missing ground layer + over-strong fog)

- **Priority**: 🟡 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ⚠️ Mostly done — two root causes, neither the night wash:
  1. **The park floor was never rendered.** It is not in `base.MD2` (scenery only: river, lake, entrance, hills,
     fence) but in `terrain.wad/base.lnd`, which nothing read — the blue was the `Sky` cube seen through the
     ground (confirmed: the terrain's first commit `0a964bc` already showed it). `.LND` is now decoded
     (`LndFile`, see docs/02) and drawn by `ParkGround`: a flat quad over the park, its texture composed at load
     from the blend map + the dirt/grass/foliage textures, the map's alpha leaving holes where the scenery
     takes over. Verified in-game on all 4 themes, 0 exceptions; unit-tested (`LndFileTests`).
  2. **Fog too strong**: `test.shader`/`water.shader` fogged to the sky colour at `exp(d·0.01)·0.025` — full at
     ~370 units, while the park camera reaches 700, so zooming out turned the whole terrain cyan (rides use
     another shader, so they stayed visible). Now `exp(d·0.004)·0.025` (full at ~920).

## Observed (2026-10-21)

`OPENTPW_TESTPARK=jungle OPENTPW_AUTOPLACE=1`: after ~60 s the whole jungle park (terrain, paths, rides) reads
as a strong blue/teal wash. The HUD showed `NIGHT` at that moment, so it may just be the T-056 day/night
overlay (`DayNightOverlay`) — or a missing terrain texture.

## Findings (measured)

- Blend map placement: 2.5 world units/pixel from world (0,0), row 0 = max Y → 960×860 on every theme, exactly
  the park fence (`jho_fol*` spans x[-1,961] y[-1,851]); the river/lake/entrance holes overlay their meshes
  (`jgr_bas*`, `jro_*`, `grd_pav*`) pixel-exactly. 4 pixels per tile → **tiles are 10 world units** (→ T-072).
- `test.shader` flips V (`1 - v`), so the ground quad's UVs are written flipped.

## Remaining

- ~~Cyan gaps~~ — fixed in T-072: holes with no mesh over them are filled with ground
  (`ParkGround.FillUncoveredHoles`), and the long "cyan strips" were queue paths fogged by `3d.shader` (full fog
  at 30 units). A few pixel-size slivers remain at the river banks.
- The **exact blend** is approximated (linear weights, each family = the average of its 2 textures); the 7
  masks per LOD (probably height/sharpness maps for a height-based blend) and the 11 header bytes are not
  interpreted. LOD 1/2 are unused.
- The day/night wash was not the cause and is unchanged.

## Original scope (kept for reference)

1. Capture the park at day and at night; if the blue is the night wash, tone it down to something closer to
   the original (and check `ThemeEngine.AmbientLightLevel` handling).
2. Otherwise, find which terrain texture fails to load.

## Acceptance criteria

- The jungle park reads green/brown in daylight, and night is clearly darker without being a flat blue.
