# T-032 — Ride engine: make the VM's engine opcodes do something

- **Priority**: 🟡 Feature (the central unlock — backs Batch B of the VM and real gameplay)
- **Type**: Engine
- **Status**: ⚠️ In progress — the seam + object lifecycle + channel-aware keyframe animation + scream +
  queue→VM boarding bridge + VM tick fix are done, and the engine now backs almost the whole VM:
  **lights**, **particle effects** (decoded `.PLB`), **limbo**, **walk** (slot scheduler) and **heads**
  all landed via T-007 (**106/106 opcodes** — the `COAST`/`TOUR`/`BUMP` car-object multiplexers are in too,
  routed to no-op engine stubs with the faithful queries-return-0 behaviour the original uses when its car
  subsystem isn't live), and **ride breakdown + a mechanic staff role** are in. **Car motion is now
  re-implemented from the RE'd car engine** (`FUN_0054a040`, docs/08): the tour/kart **circuit** loop and the
  bumper **arena** sim (`CarSim` — random waypoints + pairwise collision/bounce), occupancy-driven and
  unit-tested; walk/head node placement landed via T-048. Remaining engine work: the cars follow a
  footprint-shaped stand-in path (the authored node positions are runtime sim output, not file data —
  docs/08), and 3D-positioned EVENT sound.
- **Related**: [T-007](T-007-vm-opcodes-rse.md) (the VM + opcode RE), [T-033](T-033-ride-animation-keyframes.md) (animation keyframes), [05](../05-ghidra-reverse.md)/[07](../07-ghidra-render.md)/[08](../08-ghidra-animation.md).

## Problem

The ride-script VM (`RideVM`) runs, but its **63 "engine" opcodes were silent no-ops** — nothing
spawned objects, played ride sounds, animated, etc. — because there was no engine behind them. A
`Ride : Entity` existed but was never instantiated in-game. So no ride had ever run. The ride engine
is what makes a ride *do* anything, and it backs the remaining VM opcodes (T-007 Batch B) plus
`.PLB` particles, `.LIP` lip-sync and `.MAP` audio actually running.

## Done — slice 1

- ✅ **Engine seam.** `RideVM.Engine` (an `IRideEngine`, `World/Rides/`); default null → engine
  opcodes are guarded no-ops (unit tests stay pure, like the existing `ChildLoader`). New handlers in
  `VM/Handlers/Objects.cs` route `ADDOBJ`/`SPAWNSOUND`/`KILLOBJ` to `vm.Engine?.X` (arities per
  docs/06). Removed the empty duplicate `ADDOBJ` from `Misc.cs` (a duplicate `[OpcodeHandler]` crashes
  the reflection registration). `RideEngineTests` verify routing + the no-engine no-op.
- ✅ **Object registry + sound.** `RideEngine` keeps a `Dictionary<int, RideObject>` (by script
  handle) and plays ride sound objects through the game `Audio` system. (Exact sound-code→asset
  mapping is the `.MAP` catalog — [T-016](T-016-map-entry-records.md); for now it indexes `RideHD.sdt`
  and logs, an audible proof the opcode fired.)
- ✅ **A ride in the scene.** `Ride` loads a real ride from its `.wad` (VFS path omits the `.wad`
  extension), renders its main `.md2` (LobbyIsland pattern), attaches the engine + a VFS `ChildLoader`,
  and runs the VM. `Level` spawns one dev-test ride (jungle `tourride`). Verified: 'Jurassic' loads,
  5 meshes render, the VM runs and `SPAWNSOUND` routes through to audio, no crash.
- ✅ **Incidental fix.** Shaders are now shared by path (`Shader.Load`) and the hot-reload watcher is
  fault-tolerant — the lobby's ~60 per-material `FileSystemWatcher`s were exhausting the OS inotify
  instance limit and crashing ride model loads.

## Done — stage 1 (object lifecycle + procedural animation)

- ✅ **Lifecycle.** `KILLOBJ`/`FADEOBJ` despawn the object (and prune its model from `Entity.All`);
  `SETOBJPARAM` stores params on the `RideObject`.
- ✅ **Animation opcode family** routed to the engine: `TRIGANIM`/`LOOPANIM`/`TRIGANIMSPEED`/
  `FLUSHANIM`/`GETANIM` + the `_CH` (active-child) variants, plus the **`WAIT*` scheduler**
  (`WAITANIM`/`TRIGWAITANIM`/`WAIT4ANIM`) that suspends the script via the VM PC-rewind trick (like
  `WAIT`) — a ride that waits on an animation no longer silently skips the wait. Per-(object, anim)
  checks treat looping anims as never-blocking so a default idle can't hang a `WAITANIM`.
- ✅ **Procedural animation.** `RideEngine` has an object animation state machine + a per-frame
  procedural transform (a model with an active anim bobs); the ride body is registered as a self
  object playing a looping idle so the model is visibly alive. Tested (routing + `WAITANIM` rewind).

## Done — stage 2 (animation system RE'd + channel-aware engine)

- ✅ **Animation reverse-engineered** ([08-ghidra-animation.md](../08-ghidra-animation.md),
  [T-033](T-033-ride-animation-keyframes.md)): ride animation is **vertex keyframes split across
  sibling `.md2` files** (`<base><letter>[<n>].md2`, letter = first letter of the animation name; 12
  channels = `ScriptDefs.Animations`; Main = looping motion). `.sgn` files turned out to be **signs**
  (GDI billboard text), not animation — the original assumption was wrong.
- ✅ **Channel-aware engine.** `Ride` discovers the real channels from the WAD and `RideEngine` maps
  each `ScriptDefs.Animations` value → channel letter + frame count, animates only channels the ride
  ships, scales `WAITANIM` duration by frame count, and loops Main (else Idle) on the body. Verified
  in-game (e.g. `tourride` → `ANIM_Create(c×1)`, `totem` → Create/Main×10/Break×2/Repair). Fixed a
  latent hang: a missing entry in a *mounted* WAD returns a null stream (not an exception), which made
  the frame-probe loop forever.

## To do (roadmap)

1. ✅ **Ride keyframe animation** — rotation, translation/scale and vertex morph all decoded and driven
   from real ride data at the authentic 30 FPS rate, with multi-frame channel merge — [T-033](T-033-ride-animation-keyframes.md).
2. ✅ **Lights** — `ENABLELIGHT`/`DISABLELIGHT`/`SETLIGHT`/`COLOURLIGHT` done (T-007): `RideEngine` keeps
   per-id light state + an emissive colour proxy (our renderer is unlit; real per-pixel lighting is a
   render follow-up).
2a. ✅ **Particle effects** — `REPAIREFFECT`/`SPARK`/`ADDOBJ_EXT`(particle types) done (T-007): the engine
   loads the decoded `.PLB` (T-019), resolves the effect by its `par_lib.h` code, and spawns a colour
   proxy at the ride. `GETCUSTPTCLCODE` RE'd as a stub (returns 0).
3. ✅ **Ride breakdown + mechanic** — rides wear down while carrying riders and **break down** at zero
   reliability (stop boarding via `RideQueue.HasFreeSlot` + halt their cycle); a new **`StaffRole.Mechanic`**
   walks to the nearest broken ride and **repairs** it (restores reliability + plays the `REPAIREFFECT`
   particle). Park-wide `Ride.Breakdowns`/`Repairs` tallies. Verified in-game: a ride broke down and the
   mechanic repaired it (`breakdowns=1 repairs=1`), no exceptions. (Closes a T-039 item too.)
4. ⚠️ **Walk/limbo** — `LIMBO`/`WALKON`/… are implemented as VM-side state (T-007): limbo is a per-VM
   timed queue, walk a slot scheduler (board/alight lifecycle). The **visual** peep glide along walk-node
   world positions still needs the ride's walk-node geometry (not decoded) — peep system is up ([T-034](T-034-peeps.md)).
4a. ⚠️ **Car rides (visible wagon motion)** — tour rides / go-karts / water rides / bumpers (their scripts
   use the `TOUR`/`BUMP` opcodes) now show a **moving car** (`World/Rides/RideVehicle.cs`): a generic wagon
   that loops a path around the footprint carrying the ride's riders (occupancy-driven, like the coaster's
   `CoasterTrain`). `Ride.IsCarRide` (script uses `TOUR`/`BUMP`) drives the spawn; torn down with the ride.
   Verified in-game: `car-test: tourride isCarRide=True vehicle=True`, no exceptions. *Stand-in path:* the
   loop is generated (an ellipse in the footprint), not the ride's authored track — the real car path
   (tour nodes / track geometry) isn't decoded, and the `TOUR`/`BUMP` opcodes that drive the authentic
   car-object engine are now **implemented** (T-007: fixed 2-operand `(sub,arg)` multiplexers) but route to
   no-op engine stubs — the faithful behaviour the original itself uses when its car subsystem isn't live
   (queries return 0, commands no-op), so the ride runs via the boarding bridge + this vehicle. The
   coaster's own cars remain the player-laid-track `CoasterTrain` (T-045). A full authored car-physics
   subsystem + 3D-positioned `EVENT` sound are the remaining engine frontier.
5. ⚠️ **Scream / coaster** — the **scream family** (`STARTSCREAM`/`STOPSCREAM`/`SINGLESCREAM`/
   `SCREAMLEVEL`) is **done and audible in-game**: routed through `IRideEngine`, `RideEngine` plays a
   real peep scream at the script's level (`Audio.PlaySfx` gained a per-effect volume), and a sustained
   scream re-triggers each ~1.8 s until `STOPSCREAM`. **Screams are peep voices in `KidsHD.sdt`**
   (`sceem*`/`screem*`/`yell*`/`whoop*`), picked by name at random — not `RideHD` indexed by the script
   code (code 0 → `RideHD[0]` = `Backfire.mp2`, which sounded like gunshots/explosions; fixed). Operands RE'd from
   `monkey.rse`: `(soundCode, level 0..100)`, `-1` = default. Two more fixes made it actually fire:
   - **Boarding bridge.** Rides start CLOSED in the VM (`VAR_RIDECLOSED=1`) and the script's load loop
     polls `VAR_LETMEON` ("a peep wants on") then takes a rider (`VAR_ONRIDE++`), runs, and screams
     (RE'd from `monkey.rse`). `Ride` now opens itself (`VAR_RIDECLOSED=0`, sets `VAR_CAPACITY`) and
     `RideQueue.Board` calls `Ride.NotifyBoarding()` → raises `VAR_LETMEON`, bridging our queue to the
     VM's own rider model. No WALKON/LIMBO needed (monkey uses neither).
   - **VM tick fix (the real unlock).** `GameTime` was never advanced, so every `WAIT` armed
     `WaitUntil = 0 + duration` and looped forever — **every ride script hung at its first `WAIT`**. And
     the VM ran only one instruction per 5 Hz tick. `RideVM.Update` now advances `GameTime` by the real
     elapsed ms and runs a bounded per-tick **slice** (`RunSlice`: up to N instructions, yielding at
     `ENDSLICE` or when a `WAIT`/`WAITANIM` rewinds the PC). `ENDSLICE` sets the yield flag (was a
     no-op). Verified in-game: a peep boards `totem` → its script loads, runs, and `SINGLESCREAM` fires
     (audible, repeating each ride cycle), no exceptions.
   - **Engine-opcode routing + log cleanup.** Now that scripts actually run, the live-firing engine
     opcodes were spamming "No handler" (`COAST` ~20k/min from coaster1's load loop, plus `EVENT`,
     `SETREVERB`). Routed `COAST`/`EVENT`/`EVENT_EXT`/`SETREVERB`/`DIPMUSIC` through `IRideEngine`
     (78/106 opcodes now). `CRIT_LOCK`/`CRIT_UNLOCK` are now proper no-ops (single-threaded VM). Also
     dropped the per-instruction `Step`/branch trace logging — with `RunSlice` running many instructions
     per tick it flooded the log (a 48 s run went 35,880 → 149 lines).
   - **EVENT / COAST RE (Ghidra).** Both turned out to be fronts for whole subsystems (decompiled, so
     the future work is scoped):
     - **`EVENT`** (handler `FUN_00552615` → dispatch `FUN_005573d0`) is a `switch(type)` that spawns
       **positioned sounds / particle effects** from per-type effect pools (`DAT_00803a20..3c`), e.g.
       type 1/2 = 3D-positioned sound (the `__ftol` coord conversions), 3–9 = effect-pool spawns. It's
       the particle/effects engine (overlaps `.PLB` + the `.MAP` audio catalog, T-016) — routed for now,
       not dispatched, to avoid spurious sounds.
     - **`COAST`** (handler `FUN_00554a5a`) switches the subcommand onto a coaster-object class
       (`FUN_0043b0e0/1f0/220/270/2f0/330/050/2b0`): 1 load · 2 can-load? · 3 wants-off? · 4–6 set state ·
       8 create/init. Queries 2/3 return a value the next `BRANCH_Z` reads via the flag, so as a stopgap
       **sub 2 clears Zero** (a car is free → the load loop falls through to its `VAR_LETMEON` gate, and
       the queue→`VAR_LETMEON` bridge loads coaster riders like any ride) and **sub 3 sets Zero** (no
       scripted unload yet). Real car loading + motion (and `BUMP`/`TOUR`/`TURBO`) need the coaster-object
       + car/track engine — deferred; coaster1's load loop idles rather than fully terminating until then.
6. ⚠️ **Real park terrain + placement grid** — `PlacementGrid` (tile↔world, footprint, occupancy;
   jungle's 95×84 dims from `Standard.sam`, unit-tested) + `ParkTerrain` rendering the real jungle
   landscape (`terrain.wad`/`base.MD2`, 272 meshes — textured ground, water, paths) with rides placed
   on its surface (height-sampled) via the grid. The terrain needed **double-sided** rendering
   (`MaterialFlags.DoubleSided`) — its heightfield winding isn't uniform after the Y/Z swizzle, so
   back-face culling dropped most of it. Ride **footprints** are now wired: `RideShape` parses a ride's
   `Info.Shape` ASCII grid from its `.sam` (e.g. monkey 4×4, totem 3×4, coaster 2×3; `*`=tile,
   `S`/`2`/`N`=entrance/exit markers), exposed as `Ride.Shape`; the dev park lays rides in a row each
   reserving its real footprint via `PlacementGrid.TryPlace` (no overlap). Unit-tested.
   **Entrance/exit cells** are wired: `RideShape` records the entrance (`S`/`N`/`E`) and exit (`2`)
   tiles; `Ride` reads the `UsageInfo` entry/exit sub-tile stand positions; the dev park drops green
   (entrance) / red (exit) markers at the computed world points on the terrain. Unit-tested.
   Remaining: the `base.lnd`/`*.map` (TP2M attribute) data and a proper terrain heightfield mesh (vs the
   scenery in `base.MD2`); robust grid↔terrain alignment (the bounds have outlier scenery meshes — uses
   the centroid for now); the `Info.Hoarding` perimeter fence + coaster track connectors (`<`/`>`); a
   build/placement UI; and lobby-vs-park scene separation. **Queue paths** are wired: a strip of path
   tiles is laid from each ride's entrance cell (stepping out from the footprint edge it sits on),
   reserved on the grid and rendered on the terrain with the queue path texture (`queue.wad`); the 3D
   queue-fence meshes are placed too since 2026-10-21 (`QueuePieces`: `questra` railings along the run, the
   torch-lit `queend` at the outer end where visitors join; each world's own `queue.wad`).
7. ☐ Sound-code→asset mapping via `.MAP` ([T-016]); fold `EVENT`/`SETREVERB`/`DIPMUSIC` into sound.

## Affected files

`source/OpenTPW/World/Rides/{IRideEngine,RideEngine,RideObject}.cs` (new),
`source/OpenTPW/VM/Handlers/Objects.cs` (new), `source/OpenTPW/VM/RideVM.cs` (Engine),
`source/OpenTPW/VM/Handlers/Misc.cs` (removed dup ADDOBJ), `source/OpenTPW/World/Ride.cs`,
`source/OpenTPW/World/Level.cs` (dev ride), `source/OpenTPW/Render/Assets/Shader.cs`+`Material.cs`
(shared shaders / resilient watcher), `source/OpenTPW.Tests/RideEngineTests.cs`.
