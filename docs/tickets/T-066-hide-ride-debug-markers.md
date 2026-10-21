# T-066 — Hide the ride-engine debug stand-ins

- **Priority**: 🟠 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ✅ Done — the RE-era stand-ins (emissive cubes per enabled ride light, coloured-cube EVENT
  particle proxies, WALKON/ADDHEAD node sprite markers) are gated behind `GameSettings.ShowDebug` (backtick,
  T-065): off by default, so a park shows only real ride meshes, shops and peeps. Underlying VM state is
  unaffected. Commit `7be2765`.

## Remaining

- Real ride light and particle rendering (the stand-ins are hidden, not replaced).

## Affected files

`source/OpenTPW/World/Rides/RideEngine.cs`.
