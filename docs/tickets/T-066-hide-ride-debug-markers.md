# T-066 — Hide the ride-engine debug stand-ins

- **Priority**: 🟠 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ✅ Done — the RE-era stand-ins (emissive cubes per enabled ride light, coloured-cube EVENT
  particle proxies, WALKON/ADDHEAD node sprite markers) are gated behind `GameSettings.ShowDebug` (backtick,
  T-065): off by default, so a park shows only real ride meshes, shops and peeps. Underlying VM state is
  unaffected. Commit `7be2765`.

## Remaining

- ~~Real particle rendering~~ — done 2026-10-21: particles are soft camera-facing sprites (a radial-alpha blob,
  one billboard model per `.PLB` colour-ramp stop, cached per effect) that walk the effect's ramp over their life
  (when `CyclesColorRamp`), on the existing .PLB-driven motion (burst count, lifetime, velocity, acceleration) — and
  are shown by default. In an 80 s autoplace run ~85 effects fire (Destroy2/3/4, LaserFWexplode, FirePuff, Create2,
  Repair, Sparks); seen as soft puffs over the rides (hard to judge finely at screenshot scale).
- Real ride **light** rendering (the light stand-ins stay hidden).

## Affected files

`source/OpenTPW/World/Rides/RideEngine.cs`.
