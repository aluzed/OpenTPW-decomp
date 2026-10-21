# T-071 — Park terrain looks uniformly blue (missing ground layer + over-strong fog)

- **Priority**: 🟡 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ✅ Done — two root causes, neither the night wash; the ground blend is now the original's:
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
- ~~The exact blend~~ — reversed and implemented (see "Exact blend" below).
- The day/night wash was not the cause and is unchanged.

## Original scope (kept for reference)

1. Capture the park at day and at night; if the blue is the night wash, tone it down to something closer to
   the original (and check `ThemeEngine.AmbientLightLevel` handling).
2. Otherwise, find which terrain texture fails to load.

## Acceptance criteria

- The jungle park reads green/brown in daylight, and night is clearly darker without being a flat blue.

## Per-world fog + backdrop (2026-10-21)

The fog colour was a hard-coded jungle cyan in `test.shader`/`3d.shader`, so zoomed-out Halloween/Space parks drowned
in cyan haze. It is now a uniform (`ObjectUniformBuffer.g_vFogColor`, std140 offset 240 — the old `_padding2` slot)
set from `Fog.Color`: for a park, the average non-white colour of its `sky/sky_cyl.tga` horizon gradient (jungle
(0.34, 0.84, 1.0) — matching the old constant —, hallow navy, space orange); the front-end keeps the default. The
flat `Sky` backdrop takes the same colour. (Note: `sky.tga` is the cloud layer, `sky_rgb.tga` the zenith; a sky
dome isn't worth drawing — at the build camera's 48° pitch and 60° FOV the top of the view stays 18° below the
horizon.) `water.shader` is unchanged (its `Water` entity is unused and has its own UBO struct).

## RE note (2026-10-21, night)

`base.lnd` is loaded by `FUN_004504c0` → `FUN_0056dfe0` → `FUN_0056e550` ("procedural textures"): a tile cache
(`FUN_00572110`: 16-byte ring entries, tile size 16/32/64 picked from the video memory — the three LODs) whose tiles
are generated on demand; the per-pixel blend lives below `FUN_00590a80` (tile init) and wasn't reached. The blend
stays approximated.

## Exact blend (2026-10-21)

The tile generator is `FUN_0055f780` (called from the tile cache `FUN_00572440`); `FUN_0055eaa0` / `FUN_0055ee10` are
its plain-C 3- and 2-family blends, `FUN_00560ff0` loads the file and `FUN_0055f470` maps the 13 images of a level to
their slots. Per pixel of a tile (blend map upsampled bilinearly by `FUN_0055e780`, a map pixel on its corner):

- family colour = `T_b + (T_a − T_b) · ramp_f[(M_f · R) >> 8] >> 8` — dirt T0/T1 by M0, grass T2/T3 by M1, foliage
  T4/T5 by M2: **R is a variation channel**, not a dirt weight;
- grass cover `wG = max(rampG[(M3 · G) >> 8], rampG[(G · M4) >> 8])`, foliage cover `wF` the same with B, M5, M6;
- ground = `lerp(lerp(dirt, grass, wG), foliage, wF)`;
- the 5 ramps are `FUN_0055e710(centre, width)` (0 below `centre − ⌈w/2⌉`, 255 above `centre + ⌈w/2⌉`, linear over
  w + 1) from the **header** pairs: bytes 1–2 dirt, 3–4 grass, 5–6 grass cover, 7–8 foliage, 9–10 foliage cover;
- tile (x, y) reads each image from texel `(TileSize · x) mod (size − TileSize)` — images of different sizes
  (192/212/222/242…) so the repetition never lines up.

`ParkGround.Compose` does exactly this at LOD 1 (32 px per tile, a 3072×2752 texture; LOD 0 would be 135 MB).
Verified in game on jungle, Halloween and space; unit-tested (`LndFileTests`). The original greys out tiles whose
centre isn't fully ground (`0x7f7f7f`) — we keep alpha holes + `FillUncoveredHoles` instead.
