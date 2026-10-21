# T-062 — Level theme selection (jungle / hallow / fantasy / space)

- **Priority**: 🟡 Feature (content)
- **Type**: Engine
- **Status**: ⚠️ Core done — the park theme is now selectable. Theme Park World ships four themed worlds under
  `levels/<name>/` (jungle / hallow / fantasy / space), each with its own terrain, rides, shops, sideshows and
  `Standard.sam`; only jungle was ever loaded (everything hardcoded `levels/jungle/...`). Now `OPENTPW_LEVEL`
  picks the theme (default + fallback `jungle`), routed through the existing `Level.Name`. The **build catalog is
  data-driven**: `LevelTheme.RideNames`/`SideshowNames` enumerate the theme's `rides/`+`sideshow/` WADs (via the
  VFS `GetDirectories`, which surfaces mountable `.wad` archives), so any theme populates without jungle-specific
  names. **Jungle is unchanged** — it keeps its curated, verified ride/sideshow set, so the default park is
  byte-for-byte the same. Non-jungle themes use a generic autoplace (`AutoplaceGeneric`) that drops the first few
  enumerated rides + the shared shops/staff. Terrain, `Standard.sam` and the advisor speech dir are theme-relative.
  **Verified in-game — all four themes load with 0 exceptions**: jungle identical (`autoplace totem=True …
  guard=True`, autotrack 9 segs), **space** (200-mesh terrain; bouncy/creature/hoverbot/mbuggy/moonshot, 5/6
  autoplaced), **hallow** (201-mesh; brainb/bug/coasta, 4/6), **fantasy** (195-mesh; bbugs/b_drip/bigapple/
  candy_c, 5/6) — each loads its own terrain + its own enumerated rides, peeps enter. Unit-tested
  (`LevelThemeTests`: resolve/fallback + jungle stays curated).

## Context

The four themes are full, self-contained parks in the install, but OpenTPW hardcoded jungle everywhere — the
terrain path, the `Standard.sam` path, and especially the build catalog's seven named jungle rides. Switching
themes needed (a) routing the paths through the level name and (b) making the catalog discover a theme's rides
from the data rather than a fixed name list.

## Scope (done)

1. `LevelTheme`: `Resolve` an `OPENTPW_LEVEL` value to a known theme (fallback jungle); `RideNames`/`SideshowNames`
   (curated for jungle, enumerated for others via `FileSystem.GetDirectories`).
2. `Game.cs` picks the theme; `Level` paths (terrain / `Standard.sam`) + `Advisor` speech go theme-relative.
3. `BuildCatalog` builds from `LevelTheme` lists, loading each WAD defensively (a bad archive skips that item).
4. `AutoplaceGeneric` for non-jungle themes; jungle keeps its exact named autoplace.
5. Unit tests + in-game verification of jungle (no regression) and space (loads).

## Remaining (polish)

- ~~A level-reload path~~ — **done**: `Level.RequestReload(theme)` defers a swap to the next frame, then
  `DoReload` unwires the render loop, tears down the world (`Entity.All` + the static collections/tallies:
  `Shop.Stalls`, `Litter.Active`, `parkQueues`, `ResearchQueue`, `Staff.ResetAll`, `Peep.ResetStats`,
  `Ride.ResetStats`, `RootPanel.Instance`) and builds a fresh `Level` for the new theme. **F7 cycles the theme
  in-game** (jungle→hallow→fantasy→space). Verified via an `OPENTPW_THEME_CYCLE` auto-cycle diagnostic: the park
  reloaded jungle→hallow→fantasy live (3 parks, 2 reloads, 0 exceptions), each with its own terrain + catalog
  (the hallow BUILD panel showed brainb/bug/coasta, etc.). A **"Loading the &lt;theme&gt; park…" screen** now
  covers the synchronous load: the reload is two-phase (draw the overlay one frame, do the heavy load the next),
  so the frozen frame reads as a loading screen instead of the old park (verified in-game). It can't present its
  own progress frames because the load runs mid-frame, after the render CommandList has begun (a nested present
  would error). A **click-driven theme picker** (`ThemePanel`, toggled with **F12**) lists the four worlds by
  their **authentic names** — decoded from `THEMENAMES.str` via the existing `BFSTReader`
  (`LevelTheme.DisplayName`: jungle = "Lost Kingdom", hallow = "Halloween World", fantasy = "Wonder Land", space
  = "Space Zone"; falls back to the folder name if the strings are unavailable) — with the active one
  highlighted; clicking one reloads into it. The loading overlay uses the same names. Verified in-game (the panel
  reads "Lost Kingdom (current)" / "Halloween World" / …; `OPENTPW_THEME_PANEL=1` opens it). *Remaining*: ~~a true
  pre-park **lobby** picker~~ (done by T-063: click an island in the front-end lobby; Space too since T-073), the loading overlay drawing under the HUD panels (UI batches per-texture), and ~~the per-reload
  GPU-resource leak~~ — measured 2026-10-21 with `OPENTPW_THEME_CYCLE=1` over 5 reloads (190 s): GPU memory
  oscillates 365–600 MiB and keeps returning to ~365 MiB, process RSS stays 1.0–1.4 GB — no unbounded leak.
- ~~Per-theme queue/path strip textures~~ — **done**: the `jpa_que1`/`jpa_str1` filenames turned out to be
  *shared* across every theme's `queue.wad`/`terrain.wad`, so `LoadPathTexture` just routes the path through the
  active theme. (The `Terrain.cs` flat-plane class with a hardcoded jungle texture is **dead code** — the park
  uses `ParkTerrain`, which loads each theme's own per-mesh textures from its WAD; verified by space rendering
  its own ground.)
- ~~Wider verification of hallow / fantasy~~ — **done**: all four themes launched and populate cleanly
  (0 exceptions); no per-WAD failures observed (a few rides simply didn't fit the generic autoplace spots).

## Affected files

`source/OpenTPW/World/LevelTheme.cs` (new), `Level.cs` (paths + catalog + `AutoplaceGeneric`), `Client/Game.cs`,
`World/Advisor.cs`, `source/OpenTPW.Tests/LevelThemeTests.cs` (new).
