# T-033 — Ride animation: real MD2 vertex keyframes

- **Priority**: 🟡 Feature (ride engine stage 3 — replaces procedural placeholder with original motion)
- **Type**: Engine / format RE
- **Status**: ✅ Core done — all three keyframe track families (rotation, translation/scale, vertex
  morph) reverse-engineered, parsed and driven from real ride data in `RideEngine`; verified in-game.
  Only polish remains (rate tuning, multi-frame channel merge, LOD).
- **Related**: [T-032](T-032-ride-engine.md) (ride engine), [T-015](T-015-md2-static-variant.md) (MD2
  versions), [08-ghidra-animation.md](../08-ghidra-animation.md) (the RE writeup).

## Problem

A ride's `TRIGANIM`/`LOOPANIM`/`WAITANIM` opcodes select a `ScriptDefs.Animations` channel, but the
engine had nothing to play — it bobbed the model procedurally as a placeholder. The original game's
motion lives in **sibling `.md2` keyframe files** (see below), which the engine never loaded.

## Reverse-engineering done (see docs/08)

- `.sgn` files are **signs** (GDI billboard text on `sign1.tga`/`sign2.tga` surfaces, `FUN_00467a80`),
  **not** animation — the original ticket assumption was wrong and is retired.
- Animation is **vertex-keyframe**, split across files (`FUN_00461f10`): a base `<name>.md2` plus
  per-channel `<name><c>.md2` / `<name><c><n>.md2` keyframe files, `<c>` = first letter of the
  animation name. 12 channels = `ScriptDefs.Animations` slots 0–11; **Main (`m`) is the looping
  motion**. Verified against `monkey`/`totem`/`wateride` WAD contents.
- Frame files are thin and **don't parse as standalone models** — they carry per-frame vertex data
  keyed to the base topology, folded in by `FUN_00470b30`.

## Done (this change)

- `RideEngine` discovers a ride's real channels at load time (probe the WAD for `<base><c>[<n>].md2`),
  maps each `ScriptDefs.Animations` value → channel letter + frame count, and drives
  `TriggerAnim`/`LoopAnim` against that real data (animate only channels the ride ships; log true
  channel + frame count). Placeholder motion retained until the payload decode lands.

## Frame-file binary layout — decoded (see docs/08)

The frame format is now reversed (loader `FUN_0046d6d0`, cross-checked against `monkeym1`/`monkeyc`):

- Header `0x00`–`0x4f` is byte-identical to the base (same magic/version/counts) → inherits base
  topology. The base's offset-table fields `0x50`–`0x7c` are **zero** in a frame (no own tables).
- Dword at **`0x98`** = frame animation pointer (0 in the base). It points to a small (~72 B)
  **per-surface relink trailer** near EOF: a count (`+0x12`) + an offset array (`+0x2c`) into the
  vertex region. The bulk **`0xb8` .. `[0x98]`** is the frame's **vertex-position payload** (verified
  real model-space floats). A frame only carries the surfaces that move, so sizes vary.

## Loader validated + runtime apply decompiled

The runtime apply path is now reversed (see docs/08), so the format is fully known:

- **Keyframes are time-indexed tracks.** `FUN_00470b60` is the interpolator: it reads each entry's
  leading `u16` keyframe time (the `0xFFFF0000|time` dwords), finds the keys bracketing the current
  animation time, and returns a lerp factor. Entry stride by track type: `1→4`, `2→20`, `4→16` B.
- **`FUN_00471860` applies a surface's tracks**, selected by a flags word (`desc[1]`): vertex morph
  (`0x1000`, per-vertex `vec3` lerp into the surface vertex buffer), rotation (`0x8`, quaternion →
  matrix via `FUN_00474490`), scale (`0x80`), translation (`0x200`/`0x1`). `FUN_004679d0` runs it for
  every surface.
- It's a **hybrid**: a surface can morph vertices *and/or* carry T/R/S tracks. `monkeym1` surf 5
  (`m_arm`) carries the rotation-quaternion track (the arms swing). Animating-textures
  (`FUN_00467310`, UV scroll) is a separate system.

Earlier note corrected: the payload is **not** "transform-only" — vertex morph *and* TRS tracks
coexist; only morph surfaces need a dynamic vertex path.

## Done — loader + evaluator + rotation wired

- ✅ **Keyframe loader** (`RideKeyframeFile`, OpenTPW.Files): reads the `0x98` trailer's per-surface
  records and each surface's time-keyed tracks (rotation quaternion stride-20, translation/scale
  vec3 stride-16), bounded by the `0xFFFF` sentinel + monotonic-time rule. Validated against the real
  `monkeym1.MD2` (2 rotation tracks on surface 5 = `m_arm`, full 360° Z-turn, duration 40).
- ✅ **Evaluator**: `SampleRotation` (slerp) / `SampleVector` (lerp) bracket keys by time, clamped to
  the track range. Unit-tested (synthetic frame + the real asset via `TPW_KEYFRAME_SAMPLE`).
- ✅ **Wired into the engine**: `Ride` loads each channel's keyframe file and hands it to `RideEngine`
  (`SetChannelKeyframes`); when a channel with tracks is the active animation, `RideEngine.Update`
  evaluates each animated surface's rotation, swizzles it to world space and composes it onto the
  part's base rotation (replacing the bob for those surfaces). **Verified in-game**: the `monkey`
  ride's animated surface visibly rotates from the real decoded track.

## Done — translation + scale composition

- ✅ **Key-header marker decoded**: a key's header dword is `(marker << 16) | time`. The high u16 is
  the interpolation type — `0xFFFF` = rotation (quaternion, 4 floats, slerp), `0x0000` = linear vec3
  (translation/scale, 3 floats, lerp). The loader now reads all three track types with the right
  marker.
- ✅ **Robust track bounding**: tracks are contiguous, so each is bounded by the next track offset
  (collected across all records) plus the marker + strictly-increasing-time rule — fixes the
  identity-track over-read that otherwise pulled adjacent record data in as bogus keys.
- ✅ **TRS composition in the engine**: `RideEngine` composes scale (multiply, Y/Z-swizzled onto the
  part's base scale) and translation (additive, swizzled) alongside rotation, **per-track looped**
  (each track wraps over its own last-key time for looping anims, clamps for one-shots) so a short
  rotation track keeps spinning while a full-length identity scale stays a no-op.
- ✅ **Validated**: parse confirmed against real data — `space_bouncy` grow-in (`0.07→1.0`),
  `space_bumper` vertical squash (`1,0.71,1`/`1,0.53,1`). Verified in-game on `space_hoverbot` (Main,
  10 animated surfaces): visible breathing-scale + multi-part rotation. Unit test added for the
  `0x0000` scale-marker parse + lerp eval.

> Finding: **translation tracks are absent/identity across all sampled rides** (4 worlds) — rides
> animate by rotation and scale only. Translation is wired for completeness but is a no-op in practice.

## Vertex-morph track — format decoded (see docs/08), wiring remaining

Reverse-engineered from `FUN_004714a0` (the flag-`0x1000` morph apply). It is common —
**~154 of 595 animated ride files** carry real multi-frame morph. Format:

- One record per frame (`dword[0]`=frame index); `dword[0xa]` → that frame's morph block.
- Vertices are **quantised**: a packed 32-bit int with three **10-bit signed** components, dequantised
  `component = signed10 * scaleAxis + offsetAxis` (per-block bounding-box scale `+0x20/24/28`, offset
  `+0x14/18/1c`). Block `+0xc` → sub-struct with keyframe times (`+0x08`), vertex count (`+0x16`), a
  vertex-index array (`+0x18`), and the packed-int array (`+0x20`); two bracketing keys are blended.
- Result is written into the surface's render vertex buffer → needs a **dynamic vertex buffer**
  (`Model` already supports `Device.UpdateBuffer`, so per-frame CPU dequant + re-upload is feasible).

### Morph sub-entry structure (mapped further — `fantasy/bbugs`)

The morph block's `+0xc` points to an **array of sub-entries** (one per sub-part/"bone"), each `0x14`
bytes:

- `+0x00`: `(flags << 16) | vertexCount` (e.g. `0x20024` = 36 verts; `0x10013` = 19 verts)
- `+0x04`, `+0x08`, `+0x0c`: three file offsets — the keyframe times, the packed-int vertex data, and
  the vertex-index array (exact roles **not yet confirmed**)
- `+0x10`: `0` (runtime current-key field, zeroed in the file; `FUN_004714a0` clears it with a
  5-dword stride == sub-entry size, confirming the `0x14` stride)

Dequantising the packed ints at a sub-entry's data pointer with the block's bbox scale/offset
(`signed10 * scale + offset`) yields **plausible** clustered vertex positions (a small ~1-unit part
around the block offset), so the 10-bit dequant formula is right.

### Done — morph format cracked, validated, and parsed

Dedicated deep-RE pass (`FUN_00470e90` per-sub-entry loop + `FUN_004711d0` blend + `FUN_004721f0`
driver). The sub-entry layout is confirmed (vc `+2`, index `+4`, times `+8`, packed `+0xc`,
keyIdx `+0x10`), the keyframe count = the times-array length (packed size = `frameCount × vc × 4`,
verified exact), and the dequant (10-bit signed × bbox scale + offset) produces coherent per-vertex
clouds. Application is a **global additive blend-shape**: `FUN_004721f0` applies *all* records to one
shared vertex buffer additively, so index slots are **global** vertex indices.

- ✅ **Parser**: `RideKeyframeFile.MorphSub` (per surface, `SurfaceAnim.Morph`) — vertex slots, times,
  and dequantised per-frame positions, with `Sample(i, t)` lerp. Unit-tested (10-bit dequant, bbox
  transform, lerp) and cross-checked against real `BbugsC.MD2` (12 surfaces, coherent keyframes).

### Done — vertex morph wired + working

- ✅ `Model` retains its vertices and exposes `UploadVertices()` (dynamic vertex buffer).
- ✅ `RideEngine.ApplyMorph`: per frame it resets each touched part to its rest pose, samples each
  morph sub-animation (lerp between bracketing keyframes), maps the **global** vertex slot → part +
  local index (via per-part cumulative offsets captured in `RegisterBody`), writes the dequantised
  position (model→world Y/Z swizzle, **absolute**), and re-uploads only the parts that changed.
- ✅ `StartBestBodyAnim`: the body loops the best keyframed channel (Main → Idle → any with data), so a
  ride that only ships e.g. Create (like `bbugs`) still animates.
- ✅ **Verified in-game** on `fantasy/bbugs` (Create, 12 morph surfaces): the creature deforms
  coherently from the real decoded keyframes (no scrambled/exploded verts) — confirming the global
  slot mapping, absolute-position semantics, swizzle, and per-frame upload are all correct.

All three keyframe track families — rotation, translation/scale, and vertex morph — are now decoded
and driven from real ride data. 51 tests pass.

### Remaining (polish)

1. ✅ **Playback rate** — RE'd from `FUN_004735d0`: keyframe times are 30 FPS frames
   (`animTime = elapsed_ms × 0.03 × speed`, the clock is ms, `0.03 ≈ 1/33.33`), so `KeyframeRate = 30`
   units/second. Confirm pivot/compose order on more rides.
2. ✅ **Multi-frame channel merge** — a numbered channel's files each animate a *different* surface
   (e.g. totem Main: m1→part 0/11, m2→14, m3→13, … m10→16). `Ride.LoadKeyframes` now loads all of a
   channel's frame files and `RideKeyframeFile.Merge`s their surfaces, so the whole ride animates (not
   just the first file's parts). Verified in-game: totem Main went from ~3 to 12 animated surfaces.
3. ✅ **LOD / preview models** — investigated: there is **no separate LOD model set** (the `7`-prefix
   "LOD" in an earlier note was a raw-byte regex artifact; proper extraction across all 76 rides finds
   zero `7*.md2`, and `tp.exe` has no LOD strings or path format). The real `P`-prefix files are
   **preview** models (build menu, `PreviewAnimType`) — already excluded in-world since the ride loads
   its exact base name. Corrected in docs/08.
4. Surfaces with two records on the same index (e.g. monkey's two arms): currently last-writer-wins
   per slot; fine visually, revisit if the relink distinguishes them.
5. Per-frame full-buffer re-upload is fine for ride-sized meshes; revisit if a hot path needs it.

## Acceptance

- A ride (e.g. `monkey`) visibly plays its **Main** keyframe loop matching the original, and plays
  one-shot transitions (Create/Start/End) when the script triggers them, with `dotnet test` green.

## 2026-10-22 — the record layout corrected (from the advisor's gestures, T-046)

Decoding the advisor's gestures showed the keyframe records were misread: `+0x10` / `+0x12` are the rotation /
scale **key counts** and the object a record moves is `+0x14`, an index into the model's whole object tree
(meshes, then the nodes after the mesh table — `ModelHierarchy`). The old reader took the key count for the
surface, so e.g. all eight of the Inca god's tracks landed on "surface 9", and the monkey's second arm drove the
first. Rotation keys are the object's **whole local rotation**, not a delta on the bind pose; the files come in two
generations — older (the jungle monkey: every key tagged 0xFFFF, `(w,x,y,z)`) and newer (numbered keys, the last
0xFFFF, `(x,y,z,w)`). The length is the trailer's `+0x08`. Vertex morphs address the **target mesh's positions**
(`ModelFile.Mesh.VertexOrder`), not one buffer across the parts — the monkey's body morph used to deform the base.

`RideEngine` now poses the parts through the tree (`L · B⁻¹ · A`: the loaded look at rest, everything moving with
what it hangs from) and morphs the right mesh. Visible result: the Crazy Ape now pops out of its crate and the crate
bursts. Rides that ship only Create no longer loop their construction as their running motion.

Follow-ups the same night:
- A channel's numbered files (`totemm1..m10`, `monkeym1..`) all play together; an object several of them move takes
  them as **variants**, one per pass (the totem's legs, bodies and heads three each) — not one long sequence, which
  made the totem's run 100 s. The channel lasts as long as its longest file; a ride never runs shorter than its
  `DurationUnit` band (×4 s), since one monkey trick is under 4 s.
- The tag style doesn't tell the quaternion order (the totem tags every key 0xFFFF yet stores x,y,z,w): the engine,
  which has the bind pose, reads each track in the order that makes its first key the object's bind rotation.
- The body's Create plays once, when it is placed: the script's later Create triggers are ignored (the totem kept
  rebuilding itself, its pole sunk while the construction morph ran).
- Breakdowns play the ride's Break channel (held while broken), repairs its Repair channel; after a one-shot the
  body goes back to its run or idle loop. A loaded park's rides and stalls don't replay their construction.
- `KeyframePoser` (the same tree pose and morphs) animates the stalls (`<name>c` build once, then `m` / `i`) and the
  windows' turning previews (`P<name>m`).
