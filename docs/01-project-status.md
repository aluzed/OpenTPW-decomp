# 01 — Project Status

## Nature of the project

OpenTPW is a **clean-room re-implementation** of *Theme Park World*. It is **not** a
crack or a patch of the original game: it is a new C# engine that:

1. Reads the original game's asset files (textures, models, sounds, scripts…);
2. Renders them through a modern rendering backend (Veldrid);
3. Re-implements the game logic (ride-script VM, terrain, UI…).

You therefore **need a legal copy of the game** to provide the assets — which the
`.7z` in the git-ignored `iso/` folder supplies (see [03](03-disc-compatibility.md)).

## Maturity

> **Snapshot 2026-10-21** — an early but *playable* build on Linux: the original front-end lobby (pick one of the
> four worlds by clicking its island), then a park with its real ground (`base.lnd`, T-071), scenery, sky/fog and
> music per world; build rides/shops/staff from a clickable catalog on a correctly-scaled tile grid (T-072); open
> the gates and visitors arrive in groups sized by the park rating, judge the admission fee, queue, ride, eat,
> drink, use the toilets and leave (T-074, T-060); economy, loans, research/upgrades, challenges, golden-ticket goals,
> weather, day/night, save/load, particle effects. ~60 fps with ~100 visitors, ~35–48 with 250; a park loads in
> ~12 s. **Missing / approximate**: the original 3D-widget UI (we have a text HUD, T-065), the exact arrival /
> fee / ground-blend formulas (data-driven approximations), arrival buses, coaster loops & curved pieces, ride
> lights, player profiles. Tests: 365 passing, 1 inconclusive (no `.TPWS` sample) with the game data
> (`tools/run-integration-tests.sh`). The ticket index (`tickets/README.md`) is the detailed source of truth.
>
> **Later on 2026-10-21**: every theme stall is buildable (fries, ice cream, restaurants, gift/balloon/costume
> shops as impulse buys, litter bins, each theme's toilet) with the authored purchase effects and per-product
> challenge counters (types 1–10); visitors wear costumes, carry balloons and show thought bubbles; peep, staff
> and balloon sprites decode cleanly (the TPC RLE is PackBits — they were noisy before); litter, vomit puddles,
> stink bombs, selection arrows and build/demolish puffs use the game's own sprites/effects; ride throughput
> doubled (visitors join a line at its back; wear follows the authored `WearRate`); more `Standard.sam` rules
> (ride sickness, ride mood by taste, pranksters, stall price opinion, staff pay ratios) and `Advisor.sam` advice
> (pricing, staffing, building, open the park). The in-game calendar now runs at the original's pace, reversed from
> `tp.exe`: a day lasts 5.76 s, Gregorian from 1 January 2000 ([T-076](tickets/T-076-calendar-pace.md)).

- **Stage: boots, runs & plays on Linux** (window + Vulkan render loop) — the core park loop is live.
  Verified this session by running on an AMD Radeon (Vulkan): the park renders with terrain, placed
  rides + shops, animated peeps, staff and a build/manage HUD, and a placed **bumper ride's dodgem cars
  visibly roam + collide in their arena** (see the Update below). Earlier verified on the same GPU after
  fixing the Vulkan `libdl` load ([T-023](tickets/T-023-linux-vulkan-libdl.md)).
  The **lobby renders** — island, advisor model, water, sky, and the purple buttons with text
  labels — behind a "LOADING…" screen during the level load
  ([T-024](tickets/T-024-linux-black-screen.md), resolved). The lobby now runs at **vsync ~60 fps**:
  the per-frame GPU resource churn (a synchronous queue submit per uniform bind, ephemeral resource
  sets per draw) is fixed — resource sets are cached, uniforms recorded on the frame command list,
  and UI draws batched ([T-026](tickets/T-026-render-resource-churn.md)/[T-027](tickets/T-027-ui-draw-batching.md)/[T-028](tickets/T-028-frame-cpu-hygiene.md)).
  The level load is still synchronous but no longer freezes the window — it shows a per-step progress
  bar ([T-030](tickets/T-030-async-level-load.md); fully-threaded load is the remaining optional bit).
  The original engine's render loop was reverse-engineered for comparison
  ([T-029](tickets/T-029-native-render-loop-re.md), [05](05-ghidra-reverse.md)/[07](07-ghidra-render.md)).
- **Upstream** (`OpenTPW/OpenTPW`) is dormant (last real activity early 2025). **This fork**
  is under active development: full Linux portability, CI, and a large reverse-engineering
  push this session (see the Update below).
- **Tests**: **161 passing, 0 failing** (was 7/7 failing) + 18 inconclusive integration tests that
  need a real asset (gated on `TPW_*_SAMPLE`/`OPENTPW_GAMEPATH`). Build: **0 warnings**.

## Architecture (`source/OpenTPW.sln`)

| Project | Role | Notable dependencies |
|---------|------|----------------------|
| **OpenTPW.Common** | Virtual file system, client abstractions | Zio |
| **OpenTPW.Files** | **Format parsers** (the reverse-engineering core) | SharpZipLib, ImageSharp, StbImage |
| **OpenTPW** | The game: rendering, world, UI, ride VM | Veldrid, SDL2, ImGui.NET, NAudio |
| **OpenTPW.ModKit** | Asset editor/viewer (ImGui) | NLayer, OpenAL, System.Drawing.Common |
| **OpenTPW.WadTool** | CLI to list/extract `.wad` (DWFB) archives | — |
| **OpenTPW.Tests** | MSTest tests | — |

All targets are **`net8.0`**.

### Rendering stack
- **Veldrid 4.9**: cross-platform graphics abstraction (Vulkan / D3D11 / Metal / OpenGL).
- **Veldrid.SDL2**: cross-platform windowing.
- **Veldrid.SPIRV**: shader compilation.
- **ImGui.NET**: debug UI and ModKit.
- `ShaderCompiler` cross-compiles shaders to the backend target.

> **Update (2026-06-16):** major progress this session.
> - Full Linux portability + CI; every file format decodes at least partially (no more ❌).
> - **Reverse engineering unblocked via Ghidra.** The disc binaries are SafeDisc-encrypted, so
>   an unprotected no-CD `tp.exe` (abandonware) was imported into Ghidra 12. From it: the `.MD2`
>   loader (version gate), the confirmation that `.MTR` is **not** a runtime format, the TQI
>   codec (float AAN IDCT), and — biggest — the **ride-VM opcode table** (106 opcodes + operand
>   counts) and executor, which let **Batch A (all 43 pure VM opcodes) be implemented & tested**.
>   See [05-ghidra-reverse.md](05-ghidra-reverse.md) and [06-rse-vm-opcodes.md](06-rse-vm-opcodes.md).
> - Catch-all tickets T-008/T-012 were split into focused per-concern tickets (T-015…T-022).

> **Update (2026-06-25): ride engine + node motion + in-game verification.**
> - **Ride VM is 106/106 opcodes** ([T-007](tickets/T-007-vm-opcodes-rse.md)) and the **ride engine**
>   backs all of Batch B: object lifecycle, channel keyframe animation (rotation/scale/translation + vertex
>   morph), lights, particles (decoded `.PLB`), walk/head/limbo, scream, breakdown + a mechanic, and the
>   `COAST`/`TOUR`/`BUMP` car multiplexers. A full **gameplay loop** runs: build/manage mode + catalog,
>   ride/shop placement + rotation + sell/demolish, queues, peeps (real `esprites.wad` sprites, A*
>   pathfinding around footprints + water, needs, ratings/thoughts), staff (entertainer/handyman/guard/
>   researcher/mechanic + patrols), economy (prices, fees, loans, upkeep, finance graph), research/upgrades,
>   and a coaster track editor. See [T-032](tickets/T-032-ride-engine.md) + T-034–T-052.
> - **Ride node positioning, end to end** ([T-048](tickets/T-048-ride-node-geometry-movement.md)): a runtime
>   node→world resolver (`RideNodePositions`) drives EVENT/SPARK/REPAIREFFECT effects, WALKON peeps and
>   ADDHEAD heads at their addressed nodes (`FUN_00556b90`), and the car path traces the authored footprint.
> - **The "rig" was decoded (Ghidra) — and it is *not a file structure*.** Decompiling the node-positioning
>   chain proved there is **no bone/skeleton in the model file**: a node's position is a runtime 4×4 matrix
>   (translation `+0x30`) bound on the fly and driven by the keyframe animation or the **car waypoint sim**.
>   That sim is now **re-implemented** (`CarSim`): the tour/kart **circuit** loop and the bumper **arena**
>   (random waypoints + pairwise collision) — verified **live in the game**. See [docs/08](08-ghidra-animation.md).
> - **Disc tooling**: `tools/ccd-img-to-iso.py` converts the CloneCD `.img` to a plain `.iso` (no extra
>   tools) so `DATA/` can be extracted for `OPENTPW_GAMEPATH` — see [03](03-disc-compatibility.md).

## What works (✅)

- **Linux/cross-platform**: builds + tests on Linux, CI, portable audio (NLayer + OpenAL),
  `OPENTPW_GAMEPATH`, case-insensitive asset resolution. See [04](04-linux-compatibility.md).
- **Virtual file system** over `data/`, mounting `.WAD`/`.SDT` archives; `WadTool` CLI.
- **Decompression**: Refpack (EA/Bullfrog) and LZSS.
- **Formats fully decoded**: `.WCT` textures, `.SAM`, strings (`.BFMU`/`.BFST`),
  `.SDT`/`.MP2` sounds, **`.BF4` fonts** (glyphs + metrics), **`.TQI`/`.TGQ` video**
  (EA container + EA-ADPCM audio + TQI frames), and **`.PLB` particles** (effect names + colour
  ramps). `.MTR` is a tool artifact the game never loads (texture binding lives in the `.MD2`).
- **`.RSE` ride VM**: loader/disassembler restored; **106 / 106 opcodes** — Batch A (43 pure) +
  all of Batch B (engine side-effects), all Ghidra-verified. [T-007](tickets/T-007-vm-opcodes-rse.md).
- **Ride engine + gameplay loop**: ride objects/animation (keyframe rotation/scale/translation + vertex
  morph)/lights/particles/scream, breakdown + mechanic; build/manage mode, ride+shop placement (rotate,
  sell/demolish), queues, peeps (animated sprites, A* pathfinding, needs, ratings), staff + patrols,
  economy (prices/fees/loans/upkeep/finance graph), research/upgrades, coaster track editor, car rides
  (tour/kart **circuit** + bumper **arena** sim). [T-032](tickets/T-032-ride-engine.md) + T-034–T-052.
- **Ride node positioning**: runtime node→world resolver; EVENT/SPARK/REPAIREFFECT effects, WALKON peeps
  and ADDHEAD heads placed at their model nodes. [T-048](tickets/T-048-ride-node-geometry-movement.md).
- **Rendering**: window, real jungle terrain mesh, `.MD2` models (advisor + rides), bitmap fonts
  (1bpp + antialiased), `.PLB`-driven effect proxies, lobby↔in-park HUD; ImGui debug UI.

## What is partial (⚠️)

- **Ride node motion**: the node→world resolver + car waypoint sim run, but the exact node *positions*
  are a footprint-shaped stand-in — Ghidra proved they're **runtime simulation output, not file data**
  (no file skeleton; bound to keyframe/car-sim transforms). Feeding the real per-frame transforms is the
  remaining depth. [T-048](tickets/T-048-ride-node-geometry-movement.md), [docs/08](08-ghidra-animation.md).
- **Many engine visuals are proxies**: lights/particles/effects, WALKON peeps and ADDHEAD heads render as
  emissive marker stand-ins (driven by real data) pending the final art/render path.
- **`.LIP` lip-sync**: keyframe timestamps decoded; **unit confirmed = microseconds**. Mouth-shape
  semantics remain. [T-020](tickets/T-020-lip-mouth-shapes.md).
- **`.MAP`**: audio category catalog (not terrain). Variant (BANK/SFX) + BANK entry names + SFX
  category header + SFX per-sound 20-byte records (sound id + variation count + params) decoded; BANK
  records are serialized object pointers (not data); only the SFX mixing-curve blob stays raw.
  [T-016](tickets/T-016-map-entry-records.md).
- **`.PLB`**: names + colour ramp decoded; the other parameter fields remain. [T-019](tickets/T-019-plb-parameter-fields.md).
- **`.TPWS` saves**: partial reader; no sample on the install disc to verify. [T-017](tickets/T-017-tpws-saves.md).

## What is not started (❌)

- **Multiplayer / online** aspect (the game's servers are long shut down) — out of scope.
- **Functional saving** (`.TPWS`): container read/write round-trips, but the inner `SAD_*` module
  stream is opaque and no real save sample exists to verify. [T-017](tickets/T-017-tpws-saves.md).

## Remaining work (see [tickets/](tickets/))

The formats, the full VM, the ride engine and the core gameplay loop are **done**. What's left is
finishing depth + visual fidelity, most of it renderer- or simulation-bounded:

1. **Exact node motion / authored track shape** — re-implement the per-frame car/keyframe transforms so
   ride nodes (and the car path) use real positions instead of the footprint stand-in
   ([T-048](tickets/T-048-ride-node-geometry-movement.md), [T-033](tickets/T-033-ride-animation-keyframes.md)).
2. **Final art for the proxies** — swap the emissive marker stand-ins (lights, particles, WALKON peeps,
   ADDHEAD heads, dodgems) for the real sprites/meshes; draw the level fully from its textures.
3. **3D-positioned audio** — the node position is resolved + recorded for EVENT/category sounds; spatial
   playback waits on a 3D audio bus ([T-047](tickets/T-047-ride-event-3d-sound-particle-pools.md)).
4. **Format tails** (each its own ticket, several deferred for want of a sample): `.MAP` SFX curve
   [T-016], `.TPWS` [T-017], `.PLB` field labels [T-019], `.LIP` mouth shapes [T-020], exact TQI dequant
   [T-021], mono EA-ADPCM [T-022], antialiased `.BF4` edge faces [T-025].

Done this project: Linux portability + CI (T-001…T-006, T-013, T-014), 0 build warnings (T-009),
VM correctness + the full 106-opcode VM (T-007, T-010, T-011), every format decoded, the ride engine +
gameplay loop (T-032, T-034–T-052), node positioning + the rig RE (T-046–T-048), and the format/VM RE above.
