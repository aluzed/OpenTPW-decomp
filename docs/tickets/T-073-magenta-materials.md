# T-073 — Magenta faces on non-jungle rides, coaster track and the Space lobby island

- **Priority**: 🟠 Medium (visual — very visible on hallow/fantasy/space)
- **Type**: Engine / rendering
- **Status**: ✅ Done (2026-10-21)

## Observed

After T-071/T-072 every theme renders, but hallow, fantasy and space rides showed large solid-magenta faces, the
fantasy coaster track was a magenta ribbon, and the Space island never appeared in the lobby.

## Root causes (measured)

1. **More than 16 materials per mesh.** `test.shader` binds 16 textures; a vertex whose material index is ≥ 16
   falls back to the shader's `vec4(1,0,1,1)`. Fantasy's `bigapple` base plate has 25 materials (floor tiles).
   → `MaterialSlots.Split/BuildModels`: one model per group of ≤ 16 materials (rides, terrain, shops, lobby
   islands). In `Ride`, the first group stays the mesh's animated part (the engine addresses parts by mesh
   index); extra groups are owned static entities.
2. **Shared textures not found.** Base-plate floor tiles (`jfl_*`), `grass`, signs and doorways live once per
   level in `levels/<theme>/sharetex.wad`, not in each ride WAD. → `Ride.LoadRideTexture` falls back to it.
3. **Coaster track texture hard-coded** to jungle's `Trak_sec2`. → `CoasterTrack.ParseTrackTexture` reads
   `coaster.sam`: the texture most `asCrossSectionEdges1[*].usTexture` use → `asTextureData[n].pcTextureFilename`
   (jungle now draws `trak_sec3`, the one its own data names; fantasy `b_drip` its red gutter).
4. **Space lobby island**: a material points past the MD2's texture list → `ModelFile` threw → the island was
   skipped. It now leaves that material unnamed (missing texture). A missing lobby texture no longer throws either.

5. **A texture missing from its WAD never failed.** The VFS returns a null stream, which `TextureFile` turns into
   a 1×1 magenta placeholder — drawn magenta, or invisible once chroma-keyed — and no exception, so every
   fallback (next folder, `sharetex`, `Texture.Missing`) was dead code (`ParkTerrain` never reached `spathtex/`).
   `Texture` now throws `FileNotFoundException` for a missing `.wct`; all dynamic callers already catch.

Scans showed no lossy near-magenta texels in fantasy textures, so the exact `(255,0,255)` chroma key is fine.

## Not changed / noted

- `RideCarMesh` (single-`Model` API) and `Advisor` still bind only 16 materials — no observed case.
- `TextureFlags` isn't a bit-flag enum (`Wrap = 3` → `HasFlag(PinkChromaKey)` is true); the texture cache is keyed
  by path only (ignores flags) and scans every asset per load. Latent, untouched.

## Verification

Screenshots of all 4 themes + the lobby (Space Zone island visible and labelled), 0 exceptions; unit tests
`MaterialSlotsTests`, `CoasterTrackTests` (track texture).
