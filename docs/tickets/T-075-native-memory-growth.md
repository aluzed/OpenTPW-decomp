# T-075 — Slow native memory growth with visitor turnover

- **Priority**: 🟡 Low/Medium (a long session grows; not a crash)
- **Type**: Engine / rendering resources
- **Status**: ☐ To do — measured 2026-10-21

## Observed

Jungle autoplace park with arrivals (T-074), ~250 visitors turning over:
- 10-minute soak: 0 exceptions, clean exit; process RSS 830 MB → 1.19 GB (plateaus at times).
- `dotnet-counters` over 3 minutes: **GC heap stable at ~100 MB**, working set +13 MB/min → the growth is
  **native** (Vulkan/driver/GPU buffers), not managed objects.

## Suspects

Every arriving visitor creates two `ModelEntity`s (sprite + shadow), every particle burst several; entities are
removed from `Entity.All` when they leave/expire, but per-entity GPU state (uniform buffers / resource sets created
on first draw, see `Material.Set( "ObjectUniformBuffer", … )` and the resource-set cache) may never be released.

## Scope

1. Confirm with a counter of live Veldrid resources (or `VK_LAYER` stats) before/after a few thousand arrivals.
2. Give `Entity` removal a dispose path for per-entity GPU state (or pool peep entities instead of re-creating).

## Findings (2026-10-21, later)

- GPU assets are **stable** (`Asset.All` ≈ 6 960 textures/models/materials over 140 s) — nothing is re-created per
  frame or per visitor.
- Entities grew from 550 to 1 440 with the crowd capped at 250: the growth was **litter** (one handyman can't keep
  up with 250 visitors; 200+ pieces in two minutes), and every visitor scans nearby litter each frame. Litter is now
  capped at `Litter.MaxPieces` (300) — past that the park is as messy as it gets; entity count then holds ~1 300.
- The remaining slow working-set growth is unexplained (could be driver-side); keep this open.

## Update (2026-10-21, evening)

After trace logging became opt-in (`OPENTPW_TRACE=1`; the ride VM's COPY opcode alone logged two lines per
execution, thousands per minute), a 6-minute jungle soak with ~250 visitors measured RSS 874 MB → 881 MB between
minutes 2 and 6 — **~1.7 MB/min, down from ~13 MB/min**. Most of the "native" growth was the console logging
path. The small residue is within allocator/driver noise; leave the ticket open at low priority for a longer soak.
