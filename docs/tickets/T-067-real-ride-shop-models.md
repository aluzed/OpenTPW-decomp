# T-067 — Real ride / vehicle / shop models instead of placeholders

- **Priority**: 🟠 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ⚠️ Mostly done — retro-ticket for five untracked commits (`3535f43`, `3b22724`, `7758a10`,
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

- **Toilets** stay on the billboard fallback: `features/toilet.md2` is a thin single-facade panel that reads as
  a flat plate from the top-down camera.

## Affected files

`source/OpenTPW/World/Ride.cs`, `source/OpenTPW/World/Rides/RideCarMesh.cs`,
`source/OpenTPW/World/Rides/RideVehicle.cs`, `source/OpenTPW/World/Build/BuildCameraMode.cs`,
`source/OpenTPW/World/Level.cs`, `source/OpenTPW/World/Shop.cs`, `source/OpenTPW/World/ShopBuilding.cs`.
