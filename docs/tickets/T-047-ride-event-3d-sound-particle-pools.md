# T-047 — Ride EVENT 3D sound positioning + particle effect-pool → `.PLB` mapping

- **Priority**: 🟡 Feature
- **Type**: Engine / reverse engineering
- **Status**: ⚠️ Mostly done — the "7 particle pools / per-pool library" model was **wrong** (single
  particle library; the pools are **sound categories** in a unified effect manager), and the EVENT type
  routing is now **split per that corrected RE**: types 3-4 & 10 → particle, 5-9 → category sounds (each
  its own `cat_*BANK.map`). **3D node positioning is wired**, and the **positional audio bus now exists**:
  a camera-tracked OpenAL listener + a world-positioned `PlaySfx3D` pool, so EVENT category sounds play at
  the addressed node and the ride cycle/scream sounds at the ride — verified in-game (the `cat_ridesBANK`
  resolves and plays at the node position). Remaining: the `COAST`/`BUMP`/`ADDOBJ` direct-sound residue.
- **Parent**: [T-037](T-037-ride-cycle-sound.md) (EVENT type switch decoded — this is the residue).
- **Related**: [T-019](T-019-plb-parameter-fields.md), [T-032](T-032-ride-engine.md), [T-048](T-048-ride-node-geometry-movement.md).

## Context

T-037 decoded the whole `EVENT` type switch (`FUN_005573d0`): types 1-2 = positioned sounds (wired to
the global sound registry), types 3-10 = particle effects each selecting one of **7 effect pools**
(`DAT_00803a20..3c`), rendered through the decoded `.PLB` proxy. Two pieces remain.

## Done (Ghidra — the model corrected)

- **There is exactly one particle library.** `data/Particle/Tp2.plb` (header: 105 records, stride 320) +
  `par_lib.h` define the particle effects **`P_EFFECT_*` = 0..100** (plus a tiny `E_EFFECT_*` force set
  0..5). So a code like 76/77/78 = `Destroy2/3/4`, 62 = `LaserFWexplode`, 8 = `FirePuff`, 11 = `Fire`;
  **219/220 are far out of range** → sentinels/invalid, NOT alternate-library effects. T-037's "skip
  unresolved" is therefore correct, and the "per-pool library mapping" premise is dropped.
- **The 7 EVENT "pools" are SOUND CATEGORIES, not particle sets.** RE'd from the registrars `FUN_0051eae0`
  / `FUN_0051ec50` (both call `FUN_0051b530("cat_*", …)`): `DAT_00803a20`=`cat_ambient`, `a24`=`cat_kids`,
  `a28`=`cat_rides`, `a2c`=`cat_ui`, `a30`=`cat_staff`, `a34`=`cat_speech`, and a second group `a38`=ambient
  / `a3c`=rides. Mapping these onto the EVENT dispatch (`FUN_005573d0`, T-037): **type 5→rides, 6→kids,
  7→staff, 8→ambient, 9→ui** — i.e. EVENT **types 5-9 are positioned sounds by category**, dispatched
  through the **unified effect manager `DAT_00802bcc`** (the same manager registers categories at vtable+4
  and spawns at +8; it handles *both* positional sound and particles — it was mislabeled "particle
  manager" in T-019).
- **Effects are node-positioned.** `op_93`/`op_105` (REPAIREFFECT/SPARK) resolve a position from a ride
  **node** via `FUN_00556a80`/`FUN_00556b90` (error "Bad particle node in %s") — exactly the node graph
  decoded in [T-048](T-048-ride-node-geometry-movement.md). EVENT's `target` operand is the node id.

## Done (this pass — EVENT routing split)

The premise was solid enough (the pools are unambiguously **sound categories**, and types 5-9 map cleanly
onto them: 5→rides, 6→kids, 7→staff, 8→ambient, 9→ui) to act on without waiting for the display. The old
`ClassifyEvent` routed **all** of EVENT 3-10 to `SpawnParticleEffect`, which mis-played the 5-9 category
sounds as proxies; `RideEngine` now:

- `ClassifyEvent`: types **1-2 & 5-9 → Sound**, **3-4 → Particle**, **10 → Particle** (the custom effect,
  kept on the particle stand-in until its handler is RE'd), else Unknown.
- new `EventSoundCategory(type)`: 1-2 & 5 → `rides`, 6 → `kids`, 7 → `staff`, 8 → `ambient`, 9 → `ui`.
- `PlayEventSound` resolves `code` through that category's bank; the single `cat_ridesBANK.map` registry is
  generalised to a per-category lazy cache (`CategoryBank`, keyed by category, caching the "no catalog"
  case so a headless install is probed once).

Unit-tested (`RideEngineTests.EventTypeClassification` updated to the corrected split, new
`EventSoundCategoryMapping`). The category banks load from the install (disc image, not extracted here), so
the audible result stays display/install-blocked — but the routing decision is now correct and tested.

## Done (this pass — 3D node positioning wired)

The node-position layer T-048 was blocked on now exists as a runtime resolver (`RideNodePositions`), so the
EVENT/effect spawn is wired to it:

- `Event(type, node, code)` passes its **`node` operand** (previously ignored) through: particle effects
  spawn at `NodePosition(node)`; category sounds resolve + record the node position (the mixer is still 2D,
  so it's logged not spatialised — ready for a 3D bus).
- `REPAIREFFECT`/`SPARK` (op_93/op_105) pass their **first operand (the node id — `FUN_00556b90`)** to a new
  `IRideEngine.SpawnParticleEffect(code, nodeId)` overload, so sparks fire at the addressed node (e.g. a
  moving coaster car) rather than the ride centre. The legacy node-less `SpawnParticleEffect(code)` stays
  for the centre case (e.g. the mechanic's repair effect).
- `RideNodePositions` resolves a node to a **live moving position** (car/seat nodes, published by the
  `RideVehicle` each frame) or a **deterministic footprint layout** (static walk/head/particle nodes); an
  unresolved node falls back to the ride body, so scripts with no decoded node graph are unchanged.
- Unit-tested (`RideNodePositionsTests`, `RideEngineTests.ParticleOpcodesPassTheirTargetNode`).

## Done (this pass — the 3D positional audio bus)

The node position was already resolved + recorded; the missing layer was a **spatial mixer**. OpenAL is 3D-
native, so a positional bus dropped in cleanly (`source/OpenTPW/Audio/Audio.cs`):

- **`Audio.PlaySfx3D(key, mpegData, position, gain)`** — a dedicated round-robin source pool whose sources
  carry a world `AL_POSITION` and attenuate with distance (`ReferenceDistance` 40, `MaxDistance` 700,
  `RolloffFactor` 1, inverse-distance-clamped). Shares the existing decode/buffer cache (refactored into
  `TryGetBuffer`).
- **`Audio.UpdateListener()`** — moves the listener (position + forward/up orientation) onto the camera every
  frame, wired in the main loop (`Render.OnUpdate += Audio.UpdateListener`). So positioned sounds pan + fade
  as the view orbits/moves.
- **2D sounds stay 2D**: music, the ambient bed, and the 2D SFX pool are made **head-relative**
  (`SourceRelative = true`, origin) at init, so moving the listener never pans or fades them — only the 3D
  pool is spatialised.
- **Ride sounds now spatialised** through it: EVENT category sounds at the addressed **node** position
  (`PlayEventSound` → the T-048 resolver), and the ride **cycle sound** (`SPAWNSOUND`) + rider **screams** at
  the ride body position (`LightPosition(SelfId)`).
- **Verified in-game** (real jungle assets, `OPENTPW_AUTOPLACE`): `OpenAL audio initialized`, then
  `EVENT t2 cat=rides code=22 node=1@(526, 278, 0) -> nl_creak_5.mp2` (the `cat_ridesBANK.map` resolves the
  code and the sound plays at the node world position via the 3D bus) plus positioned screams
  (`myell4`/`screemboy003`/`whoop002`); 0 listener/3D-SFX warnings, 0 exceptions. Closes remaining #1 and the
  audible-path half of #3 (the split routes correctly and the category bank actually resolves + plays).

## Done (this pass — the BUMP collision sound)

The `BUMP` branch's direct sound trigger (`FUN_00473270` — the dodgem knock) is now wired, riding the new 3D
bus. The arena sim (`CarSim`) already resolves the car-vs-car collisions, so the sound falls out of the
existing physics:

- **`CarSim` exposes impacts**: `ResolveCollisions` now records an `Impact { localPos, closingSpeed }` for
  each pair whose **closing speed** along the contact normal exceeds a threshold (so resting/grazing contact
  doesn't fire) — cleared each `Step`, so they're per-frame. Unit-tested (`HardCollisionRecordsAnImpact`,
  `RestingContactDoesNotBump`, `ImpactsClearBetweenSteps`).
- **`RideVehicle` plays the knock**: after stepping the arena it takes the **strongest** impact that frame,
  maps its local point to world (as the cars are), and plays **`Crunch.mp2`** (the global RideHD clip — the
  knock the original uses; it was only "wrong" when misused as an ADDOBJ object sound) through `PlaySfx3D` at
  that point, **gain scaled by impact strength**, rate-limited (`BumpCooldown` 0.12 s) so a pile-up doesn't
  machine-gun the clip.
- **Verified in-game** (`OPENTPW_BUMPER_DEMO=1`): a temporary diagnostic logged **129 positioned bumps over
  40 s** at real arena coordinates (X≈460–466), gain tracking strength (`speed 4.7→0.41`, `26.9→1.00`),
  debounced to ≤ ~3/s; OpenAL initialised, 0 exceptions. Diagnostic reverted.

## Remaining

1. **`ADDOBJ`** — operand roles now reversed (2026-10-21): the executor (op_8, `0x551f60`) fills the object record
   with the type (op 1, `+0x8`) and the **node** (op 2, `+0x10`), calls `FUN_00557970(vm, record, code = op 3,
   1000, op 4)`, and `FUN_005573d0` makes the effect — types 1–2 a positioned sound at the node (`FUN_00521e60` /
   `FUN_00521930`, the same global ride-sound ids EVENT plays; bit 0x8000 = a ride-local id, remapped by
   `FUN_004145d0`), types 3–10 a particle from one of the engine's effect pools (`FUN_0051bfc0` with
   `DAT_00803a3c / 38 / 28 / 24…`); **op 4 is the handle** later used by `KILLOBJ` / `SETOBJPARAM` (we keyed objects by
   the code before, so those addressed the wrong object). The sound types now play (ride-local ids still skipped);
   the particle types stay undrawn: the shipped scripts' codes (73, 74, 75, 90…) are not `Tp2.plb` entries (read as
   such they loop demolition clouds and message tags), so the pools' libraries remain to be found. The old
   "explosions" came from reading op 2 (the node, −1) as the sound. (`COAST` has no sound
   trigger: its decoded sub-commands are load / can-load / wants-off / state / mode / capacity / worn / init.)
2. A finer pass on the 3-4-vs-5-9 split's *audible* character (e.g. per-category gain/reverb buses). (The scream now
   comes from the moving train / car — `RideEngine.RiderPosition`, the lead car of a coaster train or a car ride's
   vehicle — and from the ride body only when it has none, 2026-10-21.)

## Acceptance criteria

- Out-of-range EVENT particle codes resolve to a real effect (no "unresolved"); effects/sounds play at
  the correct node position rather than the ride centre. *(Particle effects now spawn at the resolved node;
  sound spatialisation awaits a 3D audio bus.)*

## Affected files

`source/OpenTPW/Audio/Audio.cs` (3D bus: `PlaySfx3D`/`UpdateListener`/`TryGetBuffer`/head-relative 2D pool),
`source/OpenTPW/Client/Game.cs` (listener wired into the loop),
`source/OpenTPW/World/Rides/CarSim.cs` (`Impact`/`Impacts`), `RideVehicle.cs` (`PlayBumps` → positioned bump
sound), `source/OpenTPW.Tests/CarSimTests.cs` (impact tests),
`source/OpenTPW/World/Rides/RideEngine.cs` (EVENT/cycle/scream → `PlaySfx3D`), `RideNodePositions.cs` (new),
`IRideEngine.cs`, `source/OpenTPW/VM/Handlers/Particles.cs`, `source/OpenTPW/World/Ride.cs`/`Level.cs`,
`source/OpenTPW.Files/Formats/Particle/ParticleLibraryFile.cs`.

## Update (2026-10-21) — park-level effects

The particle proxy system moved out of `RideEngine` into `ParticleEmitter` (each ride engine owns one; a shared
`ParticleEmitter.World` is ticked by the level). It now plays the build/demolish/upgrade effects every theme's
category `.sam` declares — `Info.CreateParticleEffect` / `DestroyParticleEffect` in `Rides.sam` (79/75),
`SideShow.sam` (80/76), `Shops.sam` (81/77), `Features.sam` (82/78), and `Rides.sam`'s `UpgradeParticleEffect` (89):
`P_EFFECT_Create1..4` / `Destroy1..4` / `Upgrade`. These are continuous emitters (`BurstCount` 0, 4.5–8.5 s) that
the proxy releases at once as a 14-mote cloud, with the authored emission speed capped so it stays in view.

