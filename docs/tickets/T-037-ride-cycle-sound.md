# T-037 — Ride boarding/unloading SFX + real ride duration

- **Priority**: 🟡 Feature
- **Type**: Engine / reverse engineering
- **Status**: ✅ Done (2026-10-21) — previously ⚠️ Mostly done — wrong-sound bugs fixed; the **global sound registry** is decoded and
  `EVENT` is wired end to end (types 1-2 = real positioned sounds, types 3-10 = authentic `.PLB` particle
  effects). Ride duration comes from the running-animation length. Remaining: per-pool `.PLB` mapping,
  `COAST`/`BUMP`/`ADDOBJ` residue, and 3D positioning. See "Done" / "Remaining".
- **Related**: [T-034](T-034-peeps.md), [T-016](T-016-map-entry-records.md), [T-031](T-031-game-audio.md).

## Context

The ride animation cycle is occupancy-driven (Load → Start → Main → End → Unload, see T-034) and the
ride **duration** already comes from the ride's own running-animation length. The remaining piece is
**audio per cycle**. The RSE VM already triggers `SPAWNSOUND`/`ADDOBJ` sounds, but the code→asset
mapping is the `.MAP` audio catalog, which isn't decoded — so sounds resolve through an *approximate*
index (e.g. the monkey ride plays `urinal.mp2`). Wiring a per-board cue now would just repeat that
wrong mapping.

## Done

- **[T-016](T-016-map-entry-records.md) landed** — the `.MAP` catalog (BANK bank list + SFX per-sound
  records) is decoded.
- **`SPAWNSOUND` fixed**: its operand is a **string** (RE'd: handler `FUN_005551ab` requires the string
  type tag), naming a sound or — in every jungle ride — the `EventMap.rse` sound-event-map child script.
  We were mis-reading the string offset as an int and playing `RideHD[offset]` (a spurious `arc001` at
  ride start). Now the handler resolves `vm.Strings[…]`; an `.rse` name is recognised as the sound-event
  map (its EVENT bindings await the effects engine) and no clip is played; a plain name plays by name.
- **ADDOBJ "explosion" spam fixed** (user-reported): sound objects were played via `RideHD[param%N]`,
  and `param` is **not** a direct index — `param = -1` (a sentinel) hit `RideHD[1] = Crunch.mp2`, spammed
  ~28×/run (the "explosions"). ADDOBJ sound objects are now registered without playing the approximate
  clip; the real code→asset binding is the ride's EventMap / `.MAP` catalog (deferred). Rider **screams
  remain correct** (KidsHD path, T-032). Verified: 0 `Crunch`/`Backfire`/`arc` clips, screams still fire.
- **Duration**: already taken from the ride's running-animation length (per Context).

## EVENT / EventMap sound architecture (RE'd — Ghidra)

The full chain that plays a ride's *intended* sound, decoded end to end:

1. **`SPAWNSOUND("EventMap.rse")`** (handler `FUN_005551ab`): builds the ride path + the operand string
   and calls `FUN_005587f0` to load the ride's **`EventMap.rse`** as its sound-event-map resource.
2. **`EventMap.rse`** is a tiny per-ride binding table — variables `VAR_EVT0..N` (a **sound id** per
   event slot) and `VAR_PAR0..N` (a param per slot), set by `COPY` literals. E.g. `coaster1`:
   `EVT=[200,216,69,203,203]`, `PAR=[0,0,19,0,22]`; `gokarts`: `EVT=[194,195,203,199,203,…]` (id `203`
   recurs as the "no sound" default).
3. **`EVENT(type, target, code)`** (handler `FUN_00552615` → dispatch `FUN_005573d0`): `switch(type)`
   selects a per-type effect pool (`DAT_00803a20..3c`); `FUN_00556b90` computes the **3D position** from
   the `target` node in the ride's model/node graph; the chosen sound id is spawned through the runtime
   **sound manager** (`FUN_0051bfc0` → virtual call on `DAT_00802bcc`).
4. **sound id → file**: the id indexes a **global sound registry** held by that manager, built at load
   from the banks named in the BANK catalog (`cat_ridesBANK`: `Ride/sfUi/Staff/Kids/xKids`) + the SFX
   catalogs. Verified the ids are global, not per-catalog: `coaster1`'s `200/69/203` are in
   `cat_ridesSFX` but `216` is not; `gokarts`'s `199/203` are, `194/195` are not (they live in other
   banks). The SFX `SoundEntries` (T-016) carry `{soundId, variations, params}` but **no filename**, so
   id→file needs the manager's registry reconstructed.

## Done (global sound registry + correct EVENT sounds)

- **Global sound registry decoded + implemented.** A ride sound id (`VAR_EVT*` / `EVENT` operands) is a
  **global index across the category's banks concatenated** in BANK-catalog order — `cat_ridesBANK` →
  `RideHD (0-69) · sfUiHD (70-89) · StaffHD (90-185) · KidsHD (186-992) · xKidsHD (993-1176)`. Verified:
  id 14 → `RideHD:nl_creak_2`, 69 → `mortar_3`, 104 → `StaffHD:hamer002`, 200 → `KidsHD:fscE001` — all
  plausible ride/peep/staff sounds. New `RideSoundBank` (lazy per-bank load) resolves id → sample;
  `RideSoundBankTests` lock in the mapping against the real banks.
- **`EVENT` plays the ride's real sounds.** Types **1 & 2** are dedicated positioned sounds (RE'd:
  dispatch `FUN_005573d0` → `FUN_00521e60` / `FUN_00521930`); the engine resolves their `code` through
  the registry and plays it (e.g. a coaster's `nl_creak` track creak), debounced so a looped event
  doesn't restart the clip every tick. Verified in-game: `EVENT t2 code=22 -> nl_creak_5.mp2`, 0
  `Backfire`/`Crunch`/`bomb`/`goldticket`.

## ⚠️ Correction (see T-047): the 7 "pools" are SOUND CATEGORIES

Later RE ([T-047](T-047-ride-event-3d-sound-particle-pools.md)) showed the "7 effect pools" below are
actually **sound categories** (`DAT_00803a20`=cat_ambient, `a24`=cat_kids, `a28`=cat_rides, `a2c`=cat_ui,
`a30`=cat_staff, `a38/a3c`=ambient/rides), registered through the **unified effect manager** `DAT_00802bcc`
(handles both positional sound and particles). So EVENT **types 5-9 are positioned sounds by category**,
and only **types 3-4** are particles — the "types 3-10 = particles" claim below is **wrong for 5-9**.
The engine still routes all of 3-10 to particles; splitting it (sounds 5-9 / particles 3-4) needs in-game
verification and is tracked in T-047 (blocked while the renderer is down). Out-of-range codes (219/220)
are sentinels (the skip is correct — only one particle library, `Tp2.plb` 0..104, exists).

## Done (EVENT type switch decoded — pool kinds corrected in T-047)

- **The whole EVENT type switch decoded** (`FUN_005573d0`): types **1-2 are sounds**, types **3-10 are
  particle effects** — each calls the particle spawner `FUN_0051bfc0(0, pool, code, pos)` (the same
  spawner as `REPAIREFFECT`/`SPARK`/`ADDOBJ_EXT`), where the type picks one of **7 effect pools**
  (`DAT_00803a20..3c`) and `code` (the EVENT `p2`) indexes within it; type 10 is a custom-effect handle.
  This corrected an earlier misread: the type-3 codes 76-78 the old note called "wrong sounds" are in
  fact the **`Destroy2`/`Destroy3`/`Destroy4` particle effects** — `code` there is a particle index, never
  a sound id.
- **Wired through the decoded `.PLB` proxy** (T-019): `RideEngine.Event` now classifies the type
  (`ClassifyEvent` — Sound / Particle / Unknown) and routes types 3-10 to `SpawnParticleEffect(code)`,
  debounced per `(type,code)`. A code outside the decoded `Tp2.plb` (a per-ride/other-pool effect we
  don't have, e.g. 219/220) is skipped rather than drawn as a meaningless white proxy.
- Verified in-game (`OPENTPW_AUTOPLACE`): real jungle rides fire EVENT **type 3** + types 1/2 only; the
  type-3 effects render their authentic cues — `Fire`/`Sparks`/`Destroy2-4`/`LaserFWexplode`/`FirePuff` —
  with no exceptions and no wrong-sound clips. Unit-tested by `RideEngineTests.EventTypeClassification`.

## Remaining work

- **Per-type effect-pool → exact `.PLB`-library mapping**: the 7 pools (`DAT_00803a20..3c`) are distinct
  effect sets; we currently index them all into `Tp2.plb` (correct for the common type-3 codes, but
  out-of-range codes like 219/220 belong to another pool/library not yet decoded).
- **`COAST`/`BUMP` + `ADDOBJ` sounds**: the car-object opcodes' internal sound triggers (their scripts
  mostly drive audio via `EVENT`, so this is a smaller residue).
- **3D positioning** (`FUN_00556b90`) — effects currently fire at the ride origin; per-node placement
  needs the ride's particle/sound-node geometry (the same gap as walk/head nodes).

## Acceptance criteria

- Boarding a ride plays its correct sound (not an arbitrary clip); no wrong-sound spam while idle.

## Affected files

`source/OpenTPW/World/Rides/RideEngine.cs`, `Ride.cs`, `RideQueue.cs`,
`source/OpenTPW.Files/Public/MapFile.cs`.

## Closed (2026-10-21, night)

The remaining items resolved: the "per-type effect pools" are sound categories (T-047) — EVENT / ADDOBJ types 3-4
play the level's own ride / ambient banks, 10 the ride's own bank; `COAST` has no sound trigger and `BUMP`'s knock is
wired; effects and the riders' screams are positioned (node / moving car).
