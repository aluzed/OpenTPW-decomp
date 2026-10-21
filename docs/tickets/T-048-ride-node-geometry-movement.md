# T-048 — Ride node geometry: authored car paths + walk-node & head-node placement

- **Priority**: 🟡 Feature
- **Type**: Engine / reverse engineering
- **Status**: ✅ Done (tour cars now fly the original's FLY sim; see the end) — was ⚠️ Partial — the **node graph is decoded structurally** (type + id per node, exposed in
  `ModelFile.Nodes`, unit-tested + confirmed on real ride models). Node **world positions** bind to bone
  transforms at runtime (not stored in the node entry), so they're **simulation output, not a decode**.
  A **runtime node→world-position resolver** (`RideNodePositions`) now supplies them — car/seat nodes from
  the live vehicle path, the rest from a deterministic footprint layout — and **EVENT effects +
  REPAIREFFECT/SPARK now spawn at the addressed node** instead of the ride centre (T-047 #1). WALKON /
  ADDHEAD visual placement can use the same resolver but still needs the peep/head render path.
- **Parent**: [T-032](T-032-ride-engine.md) (ride engine — this is the "node geometry" tail).
- **Related**: [T-007](T-007-vm-opcodes-rse.md) (WALKON/ADDHEAD/TOUR/BUMP), [T-047](T-047-ride-event-3d-sound-particle-pools.md).

## Context

Several ride features are modelled as pure VM/engine bookkeeping but lack their **visual placement**
because the ride model's named **node graph** (walk nodes, head nodes, tour/car path nodes, particle
nodes) isn't decoded. Today:

- **Car rides** (TOUR/BUMP scripts) show a generic `RideVehicle` looping a *procedurally generated
  ellipse* around the footprint, not the ride's authored car path.
- **WALKON** peeps and **ADDHEAD** heads update slot tables but aren't placed at the real walk/head nodes.
- **EVENT** effects (T-047) and the car engine want the same node positions (`FUN_00556b90`).

## Scope

1. Decode the ride model's node graph (the type-`0x80` objects probed at spawn — `FUN_005587f0` for
   head-node count `+0x4c`; the analogous walk/path/particle node lists). Map node name/index → local
   transform.
2. Drive `RideVehicle` (and the coaster train where applicable) along the **authored** car/tour path
   instead of the ellipse; place WALKON peeps gliding between real walk nodes and ADDHEAD heads at head
   nodes.
3. Feed the node positions to T-047's 3D effect placement.

## Done (node table decoded — Ghidra + ModelFile)

RE'd the node graph from the MD2 loader + the runtime resolver `FUN_0044b220` / the EVENT positioner
`FUN_00556b90`:

- **File layout**: node **count** = `u16` at header **0x48**, node **table** = `u32` file offset at
  header **0x7c**, **0x14 bytes/node** = `{u32 typeMask, u32 nodeId, u32 extra, u32 ptrA(reloc @+0xc),
  u32 ptrB(reloc @+0x10)}`. Decoded in `ModelFile.ParseNodeTable` → `ModelFile.Nodes` (unit-tested,
  `ModelFileTests.ParsesNodeTable`).
- **Resolution**: the VM finds a node by `(TypeMask & selector) != 0 && NodeId == requested`
  (`FUN_0044b220`; `0x3da1f83` is the all-types mask). So `TypeMask` is a **bitfield of which subsystems
  may address the node** and `selector` is the requesting opcode's node-type. Confirmed on real models:
  `Bird.MD2` (tour ride) = 11 nodes — one `0x131`, nine `0xB1` (ids 1-9, the car/seat ring), one `0x1031`;
  `gokarts` = 3; `coaster1` = 9 incl. four `0x811` + several `0x*400031` (the high car/track bits).
- **Positions** are NOT in the node entry — `ptrA/ptrB` are **null in every shipped model**; at runtime the
  node binds to a **bone transform** and the position is that matrix's translation row (`FUN_00556b90`
  reads `transform+0x30/0x34/0x38`, direction at `+0x20/0x24/0x28`).

## Done (node types labelled) + key finding on positions

- **Node-type selectors RE'd** (each subsystem calls `FUN_0044b220(model, selector, id)`; a node matches
  when `TypeMask & selector != 0`): **0x80 = object/head attach** (`FUN_005587f0` head-mount + the
  TOUR/BUMP car helpers), **0x800 = walk node** (`FUN_00557ab0`; the coaster's `0x811` queue nodes),
  **0x100 = bumper/kart car** (`FUN_0054a040`); other bits (0x400/0x1000/0x20000/0x400000) belong to
  peep/scenery/other subsystems. Exposed as `Node.IsObject/IsWalk/IsCar`, `Node.Matches(selector)`,
  `ModelFile.NodesMatching(selector)` + `ModelFile.NodeSelector` constants (unit-tested). Confirmed: Bird's
  nine `0xB1` nodes (ids 1-9) are object/head attach points (its riders), the coaster's `0x811` are walk.
- **Key finding — node positions are NOT static file data.** In every shipped model the node entries'
  transform pointers (`+0xc/+0x10`) are **null**, there is **no bone table** (`ptr@0x98 = ptr@0xac = 0`,
  `count@0x40 = 0`), and Bird has **1 mesh but 11 nodes** — so positions can't come from the file or the
  meshes. `FUN_00556b90` reads a node's position from a **bone transform bound at runtime** (translation at
  matrix +0x30); that binding is produced by the **skeletal animation + the ride's motion VM**
  (TOUR/BUMP/COAST move the car/seat nodes each frame). So node world positions are **simulation output**,
  not a decode — they fold into T-032's "authored car-physics subsystem" + the T-033 skeleton, not this
  file format.

## Done (this pass — authored car/seat count drives the vehicle)

The node **count** is static file data even though the positions aren't, so the first visible consumer of
the decoded graph landed without needing the motion sim or a renderer: a car ride's `RideVehicle` now shows
**as many riders/cars as the model declares car/seat nodes** (object `0x80` + car `0x100`), instead of a
hardcoded four.

- `Ride.CarNodeCount` = count of the model's object/car nodes, captured at model load.
- `RideVehicle.SeatCountFor(authoredCarNodes)` clamps that to `[1, 12]` (default 4 when the model has no
  node graph); the seat array is sized from it and the riders **trail the lead car along the loop** (a
  train of cars) rather than four markers beside one box.
- Unit-tested (`RideVehicleTests`: count-from-graph via `ParseNodeTable`, clamping/fallback) — so e.g.
  Bird's nine seat nodes → nine riders, go-karts' three → three.

The loop is still the procedural ellipse (the real path needs node *positions*, below).

## Done (this pass — runtime node→world-position resolver + 3D effect placement)

Since node positions are simulation output, not file data, the missing layer was a **runtime resolver**
that supplies a world position per node id each frame — the seam every consumer (effects, sounds, the
vehicle, later WALKON/ADDHEAD) was missing. New `RideNodePositions` (`source/OpenTPW/World/Rides/`):

- **Two regimes.** *Moving* nodes (object `0x80` + car `0x100`) take a **live world position published by
  the `RideVehicle` each frame** — these are real (the car genuinely moves there). *Static* nodes
  (walk/head/particle) take a **deterministic footprint layout** (walk nodes ring the perimeter at ground
  level; other nodes sit on a raised inner ring), worldised by the ride's placement transform (origin +
  90°-step orientation + footprint size — the exact quarter-turn math `Ride.BuildMeshEntities` uses). The
  static layout is an honest engine-side **stand-in** (like the procedural path / the light/particle
  proxies), not decoded geometry — the authored positions don't exist statically.
- **Resolution order**: a published moving position wins; else the static layout (once the ride is placed
  via `Configure`); else unresolved → the caller falls back to the ride body. So a ride with no decoded
  node graph behaves exactly as before.
- **Wired into the effect path (closes T-047 #1).** `EVENT(type, node, code)` now passes its `node`
  operand through: particle effects spawn at `NodePosition(node)`, category sounds resolve + record the
  node position (for when the audio bus goes 3D). `REPAIREFFECT`/`SPARK` (op_93/op_105) pass their
  first operand (the node id — `FUN_00556b90`) to a new `SpawnParticleEffect(code, nodeId)` overload, so
  sparks fire at the addressed node (e.g. a moving coaster car) instead of dead-centre.
- **Vehicle publishes.** `RideVehicle` precomputes its car/seat node ids (`CarSeatNodeIds`) and publishes
  each seat's path position every frame, whether or not the seat is occupied (the node exists physically),
  while the visible marker still hides when empty.
- Unit-tested (`RideNodePositionsTests`: layout split walk-vs-inner, car/seat id selection, configured-vs-
  unconfigured resolution, moving-overrides-static, footprint scaling + placement rotation; new
  `RideEngineTests.ParticleOpcodesPassTheirTargetNode` for the REPAIREFFECT/SPARK passthrough).

## Done (this pass — WALKON / ADDHEAD visual placement)

The VM's WALKON/ADDHEAD families kept pure slot tables (`RideVM.Walk`/`RideVM.Heads`); the visual placement
was the gap. The **engine now mirrors those tables into the world each frame** from the node positions —
no VM/handler changes (the engine reads the read-only `WalkSlots`/`HeadSlots` views), so the pure-VM
behaviour + tests are untouched:

- **ADDHEAD** → `RideEngine.SyncHeads(VM.HeadSlots)`: each occupied slot shows a head marker at its
  **head node** (`RideNodePositions.ObjectNodeIds[slot]`, the type-`0x80` mounts), removed when the slot is
  vacated (DELHEAD). The head table is also **sized to the model's head-node count** (`VM.SetHeadCapacity`,
  matching the original probing type-`0x80` at spawn) instead of the fixed-8 stand-in.
- **WALKON** → `RideEngine.SyncWalk(VM.WalkSlots, VM.GameTime)`: each non-free slot shows a peep marker
  **gliding between its two walk nodes**, interpolated by the slot's own start/end clock and facing along
  the travel direction (the original's `atan2`); Arrived/Done peeps pin at the end node; a freed slot drops
  its marker. The interpolation is a pure, unit-tested helper (`RideEngine.WalkSample`).
- The markers are emissive **stand-ins** (no peep/head art — same pattern as the light/particle proxies),
  driven by **real node positions**; swapping in the peep sprite / head mesh is a renderer follow-up.
- Unit-tested (`RideEngineTests.WalkSample*` — midpoint/clamp/end-pin/zero-span + atan2 facing;
  `RideScriptTests.HeadCapacityFollowsHeadNodeCount`/`HeadCapacityIgnoresNonPositive`).

## Done (this pass — footprint-shaped car path) + the hard limit on the authored shape

First, the **finding** (confirmed by a full code+asset sweep, corroborating the T-048 node finding):
**there is no authored car-path data in the ride files.** The original animates a **bone rig** and reads
the car/seat node positions off it each frame (`FUN_00556b90`); that rig isn't decoded, and the only
static path anywhere is the **player-laid coaster** (`CoasterTrack`, tile list + cross-section + Catmull-Rom
spline). So the exact authored track shape is **simulation output** — it needs the motion sim / skeleton
(T-032 car-physics + T-033 bone transforms), not a decode, and tp.exe would have to be re-imported to RE the
car engine the prior pass already showed is engine-less in our state.

What **is** authored and was being ignored: the ride's **footprint** (the `.sam` `Info.Shape` grid,
`RideShape.Cells`) and its **entrance**. So the car loop now **traces the real footprint perimeter and
passes the boarding tile** instead of a generic centred ellipse:

- new `RidePath.FootprintRing` (pure): orders the footprint's perimeter tiles into a ring by angle around
  the centroid, rotated to start at the entrance; degenerate footprints (a thin strip / < 3 perimeter
  tiles) return empty so the caller keeps the ellipse.
- `RidePath.Smooth` is the coaster's closed **Catmull-Rom** curve (reused per the recommendation), so the
  loop is a smooth ride-shaped track; `RideVehicle.BuildFootprintLoop` maps the ring to world (inset inside
  the footprint edge, terrain-sampled) and `BuildEllipseLoop` is the unchanged fallback.
- Net: the car now circulates the **ride's actual shape** (adapts to L-shapes / non-rectangular rides) and
  passes the entrance — a faithful-but-bounded stand-in, honestly **not** the exact authored track.
- Unit-tested (`RidePathTests`: perimeter ring + loop order, entrance anchoring, non-rectangular shape,
  degenerate fallback, Catmull-Rom closure/subdivision).

## Done (this pass — the "rig" decoded: there is no file skeleton)

tp.exe was re-imported into Ghidra and the node-positioning chain decompiled end to end (write-up:
[docs/08 — "The node rig"](../08-ghidra-animation.md#the-node-rig--how-node-world-positions-are-produced-no-file-skeleton)).
**Definitive result: there is no bone/skeleton structure in the model file, and nothing to decode as a
format.** A node's world position is produced entirely at runtime:

- `FUN_0044b220(model, selector, id)` resolves a node **index** (count `@0x48`, table `@0x7c`, `0x14`/entry,
  match `(mask & selector) && id`) — byte-for-byte OpenTPW's `ModelFile`/`Node.Matches`.
- `FUN_00556b90` reads that node's **runtime 4×4 matrix** (in a per-instance parallel table) and returns its
  **translation row `+0x30`** as the position (`FUN_00435710`) and forward row `+0x20` as facing.
- The matrix is **never loaded from the file** (the file node entry's pointer slots `+0xc/+0x10` are null,
  there is no bone table). It's bound at runtime to an attached object and driven by either the **keyframe
  surface animation** (T-033) or, for cars, the **waypoint sim** (`FUN_0054a040`: a kart loops the model's
  `0x100` car-node list, a bumper randoms it). So node motion is the runtime composition of those two — **not
  a separable rig**, which is why neither the node positions nor the car path are static file data.

This **closes the "decode the rig" thread** (the answer is structural: nothing to decode) and turns three
prior "RE'd" claims into binary-cited facts: head capacity = type-`0x80` node count (`FUN_005587f0`), the
node table layout + resolver match, and the EVENT/walk/head positioning all flowing through `FUN_00556b90`.

## Done (this pass — real per-frame node transforms fed from the keyframe animation)

The last fidelity gap was that **static** nodes (head/object/particle mounts — everything bar the car/seat
nodes the vehicle drives) resolved to a **synthetic footprint ring** that ignored the live animation. The
keyframe animation (T-033) already computes each body part's **real per-frame world transform** every frame
(`RideEngine.ApplyKeyframes` writes `Parts[surface].Entity.Position/Rotation/Scale`), which is exactly what
the original reads off a node's bound surface matrix (`FUN_00556b90`, translation row `+0x30`; docs/08). So
those transforms now **feed the node resolver**:

- **New body regime in `RideNodePositions`.** A third source between the vehicle's moving positions and the
  static layout: `PublishBody(nodeId, world)` / `ClearBody()`, resolved at **priority moving > body >
  static** (`TryResolve`). A head/effect on an animated ride now tracks the **moving mesh** instead of a
  fixed ring; a car/seat node the vehicle owns still wins.
- **Engine publishes each frame.** `RideEngine.PublishBodyNodes` (called from `Update` after the body's
  keyframes apply) binds every non-walk node (`NodeField.BodyNodeIds`) onto a body part and publishes that
  part's **live `Entity.Position`**. Binding prefers the surfaces the active channel actually animates
  (`AnimatedSurfaceIndices` — the original's animated bind targets), falling back to all parts when nothing
  animates. The node→surface *binding* is still a deterministic stand-in (the file's binding pointers are
  null), but the **positions are real per-frame transforms**, not synthetic geometry.
- Walk nodes stay on the ground footprint ring (they genuinely ring the perimeter — peep paths), and a ride
  with no animated body falls back to the static layout exactly as before.
- Unit-tested (`RideNodePositionsTests`: `BodyNodeIdsAreEveryNonWalkNodeOrderedById`,
  `BodyPositionResolvesAboveStaticLayout` + clear-to-fallback, `VehicleMovingPositionWinsOverBodyPosition`,
  `BodyPositionResolvesEvenWhenUnconfigured`). Build 0-warning, 165 tests pass.

## Done (this pass — node facing: the full 4×4, not just the translation)

The prior pass published a node's **position** (matrix translation row `+0x30`); the original also reads its
**forward row `+0x20`** to orient the attached object (`FUN_00556b90` returns both). That's now published too,
completing the runtime transform:

- **`RideNodePositions` carries facing** alongside position in both live regimes (`movingDir`/`bodyDir`),
  resolved at the same priority via `TryResolveFacing` (vehicle tangent > animated part forward > none). A
  position-only publish or the static layout carries no facing (returns false) — callers keep their own.
- **Producers.** `RideEngine.PublishBodyNodes` publishes each body node's forward = its animated part's
  rotated `+X` axis (the surface matrix forward row), so a head turns *with* the keyframe rotation.
  `RideVehicle` publishes the seat's **travel direction** (circuit tangent / bumper heading) — the raw node
  forward, without the `+Y` car-mesh art offset (that's the consumer's concern).
- **Consumer.** `SyncHeads` now **tracks the head node every frame** (it previously set the position only at
  creation, so heads never followed the animation) — position from the node field, **yaw from the resolved
  facing** (`atan2(fwd.Y, fwd.X)` about Z, the engine's facing convention). `HeadNodeId` factored out so the
  position + facing lookups share the slot→node mapping.
- Unit-tested (`RideNodePositionsTests`: `FacingResolvesVehicleTangentOverBodyForward`,
  `FacingIsUnsetWithoutAPublishedDirection`, `ClearDropsBothPositionAndFacing`). 168 tests pass, 0-warning.
- **Verified in-game** (real jungle assets, `OPENTPW_AUTOPLACE`): the animated rides (Inca Totem, Ape Ride,
  coaster, tour ride, bumper) load their node graphs (totem 18 nodes, monkey 24, bumper 13) and
  `PublishBodyNodes` runs every frame across them with **no exceptions / sync failures**. A temporary
  diagnostic confirmed each ride's body node reports a **real per-frame model-space position + facing** (one
  ride's part 0 resolved raised at Z=10 with yaw −90° — i.e. tracking an elevated, rotated mesh part, not the
  flat footprint ring); EVENT effects resolve to live node positions. Diagnostic reverted after the run.

## Done (this pass — art swap: WALKON peeps + ADDHEAD heads now render as real peep sprites)

The last visual stand-in was the emissive **cubes** the walk/head markers drew. They're now **real
animated peep sprites** — the same `esprites.wad` kid art (directional walk cycles) the crowd peeps use —
driven by the existing real node positions, so a ride's walk/head nodes show people instead of boxes:

- **New `RideSpriteMarker`** (`source/OpenTPW/World/Rides/`): wraps a shared `SpriteSheet` (a kid sprite)
  exactly like `Peep` does — a camera-facing billboard showing the **directional walk-cycle frame** for the
  travel/forward direction, advancing the cycle while moving and holding the standing frame when idle. Falls
  back to a flat-colour billboard when the sprite can't decode (headless / missing assets), so the pure
  node-positioning path is unchanged and the engine always has something to draw.
- **`RideEngine.SyncWalk`** now drives a marker per walk slot: it **glides between the two walk nodes**
  (position from the unchanged pure `WalkSample`), the directional frame + walk cycle follow the **travel
  direction**, and it holds the standing frame once Arrived/Done. **`SyncHeads`** stands a marker at each
  head node, facing the node's resolved **forward** (the directional sprite frame), tracking the animated
  body node every frame; the head graphic id (`value`) picks which kid sprite shows, so the figure varies.
- The kid art is shared via `Peep.KidSpriteDir`/`Peep.KidSpriteName(index)` (no duplicated sprite list);
  the markers vary their figure by walk slot / head value.
- **Frame selection is a pure helper** (`RideSpriteMarker.FrameFor`) — picks the directional cycle for the
  facing sector then the looped walk phase, wrapping facing modulo the available cycles (fewer than 8 anims)
  and guarding the no-anim sheet. Unit-tested (`RideEngineTests.FrameForSelectsDirectionalCycleAndLoopsPhase`
  / `FrameForWrapsFacingAndHandlesEmpty`). 170 tests pass, 0 new warnings.
- **Verified in-game** (real jungle assets, `OPENTPW_AUTOPLACE`, 45 s): all 11 rides/staff place; the eight
  `esprites/Generic/Kids` sprite sheets (the marker art) load; `SyncHeads`/`SyncWalk` run every frame across
  every placed ride with **0 sync failures / 0 exceptions** — the sprite marker path executes cleanly.

## Remaining

1. **Exact authored track shape / car-path node positions.** The **car/seat** nodes are driven by the
   re-implemented car sim (`CarSim`: tour/kart **circuit** + bumper **arena**, `FUN_0054a040`), and the
   **body-attached** nodes now ride the real keyframe transforms (position + facing). What stays a stand-in is
   the car **waypoint geometry** (footprint loop / arena rectangle) and the node→surface **binding** (ordered,
   not file-decoded) — both because the authored data is bound at runtime, not stored. `RideVehicle`'s
   `loop`/`Sample` is shape-agnostic, so real waypoint positions drop straight in if a future RE recovers them.
2. ~~**Art swap.** WALKON peeps + ADDHEAD heads as marker proxies.~~ **Done** (this pass): both render as
   real animated peep sprites (`RideSpriteMarker`) at the node positions. A bespoke decorative head *mesh*
   per `ADDHEAD value` (vs. reusing the kid sprite) would need a value→mesh decode that isn't in the files.

## Acceptance criteria

- A tour ride's car follows the ride's authored path; a walking peep (WALKON) glides between the ride's
  real walk nodes; heads (ADDHEAD) appear at head nodes. *(Node-table decode + the runtime resolver +
  node-positioned effects + WALKON/ADDHEAD placement + a footprint-shaped car path done; the "rig" is now
  proven to be **runtime sim, not a file structure** — the exact node motion needs the car/keyframe sim
  re-implemented. WALKON peeps + ADDHEAD heads now render as **real animated peep sprites** at the node
  positions; the only remaining stand-in is the runtime-bound car waypoint geometry.)*

## Affected files

`source/OpenTPW/World/Rides/RideNodePositions.cs` (body regime + facing: `PublishBody`/`ClearBody`/
`BodyNodeIds`/`TryResolveFacing`), `RidePath.cs` (new), `RideVehicle.cs` (publishes seat facing),
`RideSpriteMarker.cs` (new — peep-sprite walk/head markers + pure `FrameFor`),
`RideEngine.cs` (`PublishBodyNodes`/`AnimatedSurfaceIndices`/`HeadNodeId`, `SyncHeads`/`SyncWalk` now drive
peep-sprite markers), `source/OpenTPW/World/Peep.cs` (`KidSpriteDir`/`KidSpriteName` shared sprite picker),
`IRideEngine.cs`, `source/OpenTPW/World/Ride.cs`, `source/OpenTPW/World/Level.cs`,
`source/OpenTPW/VM/RideVM.Walk.cs`/`RideVM.Heads.cs` (head-capacity setter), `source/OpenTPW/VM/Handlers/Particles.cs`,
`source/OpenTPW.Files/Formats/Model/ModelFile.cs` (node-graph access).

## Tour rides fly the original's FLY sim (2026-10-21)

The tour ride (jungle Jurassic Tours' bird, the Halloween balloon) has no track (`Bumper.WhichTrackType 0`): its cars
are flown by `FUN_0055abf0` ("FLY: …" diagnostics), ticked every second 31 ms step (~16/s) from the main loop
`FUN_0054e360`; cars are made by `FUN_0055a620`/`FUN_0055a720` (internal units = world × 300, a tile = 3000). States:
boarding at the station → take-off to a point 20000 ahead, climbing to ≥ 14000 → **tour**: a random walk over a
20000-unit grid (`FUN_0055c350`: straight 3 times in 4 unless the ground ahead is higher than the car + 5000, else
side / other side / straight / back with an ever higher limit; cell centres; flown 5000 over the ground, ≥ 12000) for
300 ticks → holding pattern on the four corners ±25000 around the station (`FUN_0055c270`) until it's free →
approach a point 5000 short → land on a 1:1 glide slope. Steering: turn rate = (bearing error + turn rate) / 2, a
tenth of it per tick; speed = speed × 0.879 + 0.15 × cruise (cruise 300–399 a tick); climb a damped clamped step;
bank ¾ of the turn rate. `TOUR 5` sets ceil(capacity / 9) cars.

`TourFlight` re-implements it (unit-tested, `TourFlightTests`), and `RideVehicle` flies the bird/balloon with it —
one per 9 seats, posed on heading, climb and bank — instead of the footprint loop. Verified in game (car demo): the
bird cycles tour → approach → land → board → take off at ~23 units/s, 40 units up. Not reproduced: the bird's
flap/glide animations (`FUN_0055aa80` anim ids 3–6, 9), car-to-car avoidance, and the station perch point (the ride
centre, 0.6 tile up, stands in for it).
