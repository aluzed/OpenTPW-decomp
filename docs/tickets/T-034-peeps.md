# T-034 — Peeps (park visitors)

- **Priority**: 🟡 Feature (the park's life — backs queues, ride usage, excitement/fatigue, income)
- **Type**: Engine / format RE
- **Status**: ✅ Done — full crowd loop: real animated `esprites.wad` sprites (TPC codec fully RE'd,
  **inner RLE decoder implemented to spec + unit-tested**), directional walk cycles, queueing, riding,
  needs (happiness/energy/hunger), economy (gate/tickets/food/upkeep/wages) and staff. Remaining polish
  is split into **[T-035](T-035-peep-sprite-polish.md)–[T-039](T-039-peep-needs-staff-depth.md)** (all done).
- **Related**: [T-032](T-032-ride-engine.md) (ride engine — roadmap "walk/limbo" needed a peep system);
  follow-ups [T-035](T-035-peep-sprite-polish.md), [T-036](T-036-peep-pathfinding.md),
  [T-037](T-037-ride-cycle-sound.md), [T-038](T-038-park-management-ui.md),
  [T-039](T-039-peep-needs-staff-depth.md).

## What exists

Peeps are **sprites** in `esprites.wad`, organised by type (`Generic/Kids`, `Generic/Guards`,
`Generic/Handymen`, `Fantasy/Entertainers`, …). Each is a trio: `.ESP` (descriptor, `ESP_FILE2.00` +
the sprite name), `.TPC` and `.FPC` (the image data — a custom encoded format, header `00 03 00 03`
then `0xAARRGGBB`-style colour runs, same family as the `base.lnd` landscape data). There are also
`2dmap/*sprite.tga` (the minimap blips).

## Done

- `Peep` entity rendered as an **upright camera-facing billboard** (cylindrical yaw to the camera), in
  varied clothing colours, double-sided, always dropped onto the terrain surface (`SampleHeight`).
- **Queue discipline**: each ride exposes a `RideQueue` (ordered waypoints outer end → entrance, the
  exit world point, ride duration, capacity) that owns the **line** of waiting peeps. A peep joins the
  back of the line and stands at the waypoint for its place (front = the entrance, each place back
  steps one waypoint out, clamping at the outer end), so a visible queue forms along the path instead
  of a pile on the entrance. As those ahead board, the line shifts forward and each peep's spot
  advances. Then…
- **Boarding tied to the ride**: only the **front** peep boards, and only once it has reached the
  entrance and a rider slot is free (occupies one of the ride's `Capacity` slots — sourced from the
  ride's `UsageInfo.MaxCapacity` — so the line keeps growing while the ride is full), hides for the
  ride duration, reappears at the **exit** cell, and
  re-routes. **Occupancy drives the ride's animation cycle**: a ride sits idle until its first rider
  boards (`RideQueue.Board()` edge 0→1 → `RideEngine.SetActive(true)`) and idles again when the last
  rider leaves (`Leave()` edge 1→0). Boarding runs the ride's real animation cycle — **Load → Start →
  Main (loop)** — stepping through each one-shot stage (its length from the decoded keyframe track)
  before settling into the run loop; emptying runs **End → Unload → Idle (loop, or rest)**. Stages a
  ride doesn't ship are skipped, so e.g. the monkey plays its full Load/Start/Main/End cycle while the
  totem just toggles Main↔rest. The dev park runs 40 peeps over 3 queues, each at the ride's real capacity.
- **Real ride duration**: a peep stays aboard for one full pass of the ride's running animation
  (`RideEngine.BodyLoopDuration`, decoded from the loop's keyframe track — ~11 s monkey, ~14 s totem,
  ~3 s wateride), falling back to `Info.DurationUnit × 4 s` for rides whose loop has no decoded
  keyframes, instead of a flat 5 s.
- **Excitement-weighted ride choice**: a peep picks its next ride at random *weighted by the ride's
  excitement* (`UsageInfo.ExcitementLevel`, exposed as `Ride.Excitement`), avoiding an immediate
  repeat of the last ride, so more exciting rides draw bigger crowds (verified: over ~45 s the
  wateride/75 was chosen 37×, totem/70 23×, monkey/50 16×). `Ride.Attraction` (`Info.AttractionValue`)
  is also parsed for later use.
- **Needs + visitor turnover**: each peep carries `happiness` (0–100) and `energy`. Riding an exciting
  ride raises happiness (∝ the ride's excitement); standing in a stalled queue slowly lowers it; energy
  drains while walking/queuing (~50 s of activity). A tired (energy ≤ 0) or fed-up (happiness ≤ 10)
  peep abandons its queue, walks back to its entry point, and is **recycled as a fresh arrival** — so
  the crowd turns over without churning entities (verified: ~23 visitors recycled over ~75 s as they
  tired out).
- **Park economy**: `ParkFinances` tracks the bank balance. Each visitor pays a gate **entry fee** on
  arrival (and again when recycled as a fresh arrival), and a **ride ticket** every time it boards
  (`Ride.TicketPrice`, derived from excitement); each ride drains **upkeep** per second
  (`Ride.UpkeepPerSecond`, scaled by capacity). Money in/out is logged (`OPENTPW_ECON_DEBUG`) and the
  books balance (verified: from 10000 → 10609 over ~50 s = +400 gate +383 tickets −174 upkeep). Ride
  prices / entry fee are derived defaults (the original lets the player set them) pending a build/manage UI.
- **Park-stats HUD**: a top-left readout (`ParkStatsPanel`) shows the live balance and flows
  (MONEY / TICKETS / GATE / FOOD / UPKEEP / WAGES) plus the VISITORS and LITTER counts, so the economy
  and crowd are visible on screen rather than only in logs. No-op without a park (harmless in the lobby).
- **Staff (entertainers + handymen)**: `Staff` entities roam the park (role-coloured billboards —
  orange entertainers, blue handymen) and draw a **wage** every second (`ParkFinances.PayWages`, shown
  as WAGES). Entertainers lift the **happiness** of nearby visitors (`Peep.Cheer`); handymen seek out
  the nearest **litter** and pick it up.
- **Litter**: visitors occasionally drop `Litter` (tracked in `Litter.Active`); standing among litter
  sours the mood (so a filthy park drives peeps home unhappy) until a handyman clears it. Shown as
  LITTER in the HUD. Verified: litter accumulates from the crowd but stays bounded as handymen clear it,
  the books reconcile, and the sim runs crash-free.
- **Hunger + shops**: hunger builds while a peep is in the park; past a threshold it leaves the ride
  line and detours to the nearest `Shop` (a green food stall, tracked in `Shop.Stalls`), pays for a
  snack (a **FOOD** income flow) and has its hunger reset before resuming. Verified: FOOD revenue
  accrues as the crowd eats and the books reconcile. (All billboards — peeps, staff, shops — now share
  one `Billboard.Make` helper.)

## Remaining

1. ~~**Decode the sprite format** (`.ESP`/`.TPC`/`.FPC`) and render the real animated peep sprites~~ —
   **done**: peeps/staff render real `.TPC` sprites (`SpriteSheet`, directional walk cycles); the container
   format was RE'd via Ghidra and **the inner RLE decoder is now implemented to the RE'd spec** — each
   scanline expands via skip-transparent markers (`b ≥ 0xF0` → skip `256−b`) + literal runs (`b < 0xF0`),
   replacing the earlier signed-RLE approximation. Unit-tested against the verified T-034 scanline examples
   (`TpcFileTests`); verified in-game (sprites load + render, no corruption). `esprites.wad` holds sprite
   trios under `Generic/*` / `Fantasy/*`.
   **Correction (2026-10-21): the inner RLE is PackBits-style** — a control byte `c ≥ 0x80` repeats the
   *next* byte (a palette index) `256 − c` times, `c < 0x80` is `c` literal indices. The "skip marker" reading
   above only held for runs of index 0 (`F2 00` = 14 × index 0 — the `00` is the run's value, not padding);
   every coloured run (`FE C5` = two pixels of 0xC5) was decoded as a long bogus literal, which is why peeps,
   staff and balloons rendered noisy and streaked. The corrected rule expands **all 6 417 scanlines** of the
   kid and balloon sheets to exactly `width` pixels (the old fit managed ~⅓) and the sprites now render clean.
   - **`.ESP`** = `ESP_FILE2.00` magic (12 bytes) + the referenced texture name (`SPR_KI.TPS`), zero-padded.
   - **`.TPC`/`.FPC`** layout (read primitive `FUN_005c4f60(dst, elemSize, count, stream)` = fread):
     ```
     u16 fmt          // = 3  (the "03 00"); fmt 2 is an alternate path
     u16 _            // = 3
     u32 frameCount   // SPR_KI 175, SPR_GU 260, SPR_HA 105, SPR_FL 130
     if fmt == 3:
       u8 palette[1024]            // 256 entries × 4 bytes RGBA  (index 0 = transparent)
     frame[frameCount]:
       u32 dataLen                 // compressed pixel-data size in bytes
       u16 width, u16 height
       u16 unkA, u16 unkB          // 128/128 on SPR_KI frame 0
       s32 hotspotX, s32 hotspotY  // signed origin (-14, -25); stored as i16 at struct +0x14/+0x16
       u8  data[dataLen]           // palette-indexed pixels, RLE-compressed (dataLen < width*height)
     ```
     (SPR_KI frame 0: dataLen=736, w=33, h=29 → 957 px, so ~1.3× compression.)
   - **Load call chain (RE'd):** `FUN_00541310` (“Loading Sprites from %s”, catalogs files) →
     `FUN_00540d90` (per-file: reads the sprite descriptor's 16-entry anim/direction table) →
     `FUN_00587db0` (reads fmt/`frameCount` + the 1024-byte palette, allocs `frameCount`×{0x60,0x18}
     structs, loops frames; for 16-bit display `DAT_008bd554==2` it remaps the palette through channel
     LUTs via `FUN_005839e0`) → `FUN_00588660` (reads the 20-byte frame header) → `FUN_00564750`
     (`malloc(dataLen)` + read `dataLen` raw bytes = the compressed indices) and `FUN_00588410`
     (surface setup). The surface lifecycle vtable is `0x701168` (`FUN_00564a80…` are ctor/dtor, not
     the codec). Not string-anchored — `.tpc`/`.fpc`/`*.ESP` are all `std::string` boilerplate; the
     `03 00 03 00` magic is never compared (loader just consumes the header).
   - **Per-frame pixel data = `height` RLE scanlines** (confirmed empirically: the command count per
     frame equals `height` for **all 175** SPR_KI frames, and the per-row literal totals trace the
     figure's silhouette). Each scanline is one outer command `count c` followed by `c` bytes of an
     **inner run-length scanline** that expands to exactly `width` pixels:
     - **transparent skip** — a control byte `b ≥ 0xF0` skips `256 − b` transparent pixels (so `0xFE`→2,
       `0xFC`→4, `0xF2`→14, `0xF0`→16). Verified: row 1 `F2 00 05 <5> F2 00` = skip14 + 5 literals +
       skip14 = 33; row 2 `FC 00 07 <7> FE 00 0A <10> F6 00` = 4+7+2+10+10 = 33.
     - **literal run** — a low control byte = pixel count, followed by that many palette-index bytes.
     - **Remaining nuance:** literal *data* bytes can also be ≥0xF0, so distinguishing a skip marker
       from literal data at a command boundary needs the authoritative inner decoder (the per-scanline
       blit/decompress routine) — the one piece not yet pinned. Empirical fit reconstructs ~⅓ of rows
       exactly; the rest hit this ambiguity.
   - **Texture pipeline mapped:** load → `FUN_00564750` stores the **compressed** scanlines at
     `surface+4` → **[decompress → flat base texture]** *(the one function still unlocated)* →
     `FUN_0055e780` generates mip levels (bilinear downsample) → `FUN_0055f780` does the **scaled
     blit** from the flat mip texture (`param_1 + n*0xD8` mip structs; its `0xF0`s are pixel-format
     masks). Sprites reach the screen via the 3D textured-quad rasterizer `FUN_0056e7e0`. So
     `FUN_0055f780`/`FUN_0055e780` operate on the *already-decompressed* flat texture — the RLE
     decompressor is upstream (reads `surface+4`, writes the flat base at `surface+0x20`).
   - **✅ RLE SOLVED** — decompressor is **`FUN_00564790`** (the sprite surface's vtable method, found
     via the lazy-decompress in `FUN_00590d40`). The pixel stream is **per-scanline signed-byte RLE**:
     each row is prefixed by its byte-length (used for row-skipping), then for that row, read a signed
     control byte `c` until `width` pixels are produced:
     - **`c < 0`** → a **run** of `|c|` pixels, all of palette index = the *next* byte (index 0 =
       transparent). Consumes 2 bytes (`c`, index).
     - **`c > 0`** → a **literal** run of `c` pixels = the next `c` palette indices. Consumes `1 + c`.
     - **`c == 0`** → empty (no-op).
     **Verified: 0/6226 rows mis-decode across all 175 SPR_KI frames**, and the rendered frames are
     clean, recognisable peeps (orange shirt, green trousers — see `final_0`). Full container + codec
     now decode end-to-end. (The original converts each palette index → 16-bit colour via a per-mode
     LUT for the display surface; for our renderer we map index → the 1024-byte RGBA palette directly.)
   - **✅ Integrated in-engine:** `OpenTPW.Files/Formats/Image/TpcFile.cs` decodes the container +
     signed-RLE into straight-RGBA frames; `PeepSprite` loads `esprites/Generic/Kids/SPR_KI.TPC`,
     builds a textured `Billboard` (double-sided, depth-write off, alpha-blended, point-filtered), and
     `Peep` renders it (sized to the frame aspect), falling back to the flat-colour billboard if the
     load fails. **Verified in-game: real peep sprites render around the rides.**
   - **✅ Walk-cycle + directional animation:** the `.ESP` carries (after the 12-byte magic + 256-byte
     name field + a 2-byte header) a 16-entry table of `[u16 startFrame][u8 frameCount][u8 flag]`; the
     8 non-empty entries are the **directional walk cycles**. `PeepSprite` builds one textured model per
     frame + parses these segments; `Peep` picks the segment from its 8-sector movement direction and
     cycles its frames at 8 fps while moving (holding the first frame when idle), sized to each frame's
     aspect. **Verified in-game: peeps walk (legs cycle) and face their travel direction.**
   - **✅ Crowd variety + staff sprites:** generalised into a cached `SpriteSheet` (TPC frames + `.ESP`
     anims, keyed by path). Each peep gets one of the **8 kid sprites** (`Generic/Kids/SPR_*`) at random;
     `Staff` use their real sprites (handyman `Generic/Handymen/SPR_HA`, entertainer a random
     `Fantasy/Entertainers/SPR_*`), animated + directional like peeps. All ~10 sheets (~1900 frames)
     load in ~1 s. **Verified in-game: a varied, animated crowd of kids + costumed staff.**
   - **✅ Guards:** added a `StaffRole.Guard` that patrols (wanders + wages) with the real
     `Generic/Guards/SPR_GU` sprite (260 frames), and a thematic effect — visitors within
     `GuardDeterRadius` of a guard don't drop litter (`Staff.GuardNear`). The dev park spawns one guard.
## Follow-ups (split into focused tickets)

The core peep loop is done; the remaining polish/features are tracked separately:

- **[T-035](T-035-peep-sprite-polish.md)** — sprite polish: camera-relative facing, idle/sit/queue
  poses, `.FPC` shadow layer, per-frame hotspot anchoring.
- **[T-036](T-036-peep-pathfinding.md)** — walkable path graph + A* so peeps route over real paths
  instead of straight lines (queue spacing *along* the path is already done).
- **[T-037](T-037-ride-cycle-sound.md)** — per-cycle boarding/unloading SFX (blocked on the `.MAP`
  audio catalog, [T-016](T-016-map-entry-records.md)) + ride duration from the script if encoded.
- **[T-038](T-038-park-management-ui.md)** — build/placement UI, ride prices + entry fee, staff
  hiring, research/upgrades (`Upgrades[*]`/`CostOf*`), finances panel.
- **[T-039](T-039-peep-needs-staff-depth.md)** — thirst/drink stalls, vandalism↔guards, ride ratings,
  staff behaviour, economy balance.
