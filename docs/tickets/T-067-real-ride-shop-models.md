# T-067 — Real ride / vehicle / shop models instead of placeholders

- **Priority**: 🟠 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ✅ Done — retro-ticket for five untracked commits (`3535f43`, `3b22724`, `7758a10`,
  `e987772`, `59c3ad7`):
  - **Ride textures**: `LoadRideTexture` tries `stexture/` → `textures/` → `gtexture/` and loads with
    `PinkChromaKey` so magenta (255,0,255) cutouts are discarded (bodies were solid magenta noise). Build
    camera pulled in closer.
  - **Entrance/exit**: flat ground pads (~0.4 tile) instead of floating 4-unit cubes.
  - **Vehicles**: tour rides use their theme vehicle (jungle Bird, hallow Balloon), go-karts the four
    `gk_*` karts, discovered by enumerating the ride WAD; only a ride without circuit meshes (the dodgem's
    `b_car.MD2`) stays on the arena sim. `OPENTPW_CAR_DEMO` previews them.
  - **Shops**: food/drink stalls load the theme's real stall model from `shops/` instead of colour billboards,
    centred on and scaled to their footprint (models are authored corner-anchored). `OPENTPW_SHOP_DEMO`
    previews them.

## Remaining

- ~~Toilets stay on the billboard~~ — done (2026-10-21): `features/toilet` is a base plate + door + a full booth
  (`J_WC01`, ~8×7×10). The "thin panel" was only the door: the booth's textures live in `sharetex.wad` and a
  texture missing from a WAD loaded as an invisible chroma-keyed placeholder (fixed in T-073).

## Affected files

`source/OpenTPW/World/Ride.cs`, `source/OpenTPW/World/Rides/RideCarMesh.cs`,
`source/OpenTPW/World/Rides/RideVehicle.cs`, `source/OpenTPW/World/Build/BuildCameraMode.cs`,
`source/OpenTPW/World/Level.cs`, `source/OpenTPW/World/Shop.cs`, `source/OpenTPW/World/ShopBuilding.cs`.
