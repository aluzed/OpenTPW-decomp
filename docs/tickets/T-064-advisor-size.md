# T-064 — Advisor head: small, recognisable corner overlay

- **Priority**: 🟢 Low (visual)
- **Type**: Engine / rendering
- **Status**: ✅ Done (pragmatic) — `Advisor` group scale 0.28 → 0.15 and offset nudged right/down, so the bug
  head reads as a small upright talking head instead of a skewed blob filling a third of the screen
  (commit `922da6a`). Follows the T-046 visual pass.

## Remaining (optional)

- A pixel-exact, camera-locked overlay would need a dedicated render pass (fixed overlay view/proj — feasible,
  `ModelEntity` reads the static `Camera` matrices). Deferred unless the slight camera lean becomes visible.
- Orientation-matrix experiments (billboard/basis swaps) were reverted: the model's axes are swizzled and
  trial-and-error regressed it.

## Affected files

`source/OpenTPW/World/Advisor.cs`.
