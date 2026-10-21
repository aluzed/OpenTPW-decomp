# T-077 — Features: scenery, security cameras, staff rooms

- **Priority**: 🟡 Feature
- **Type**: Gameplay
- **Status**: ✅ Done (2026-10-21) — beauty effect is an interpretation

## What the data has

Every world ships a `features/` folder of `.wad`s, each with its model, script and `.sam` (`Info.Name`,
`Info.Shape`, `Upgrades[0].CostOfUpgrade`), over the theme's `Features.sam` (`Info.WhichUIType 3`, `IsChoosable 0`,
`AttractionValue 10`, `CannotRide 1`, create/destroy particle effects 82 / 78). Some are not placeable: the fixed items
(`bus`, `ferry`, `seaplane`, `gates`, `end`, `lights`, land buying — `WhichUIType 4`) and the ones visitors use
(toilets — `IsChoosable 1` — and litter bins, both stalls already). Two have a game effect:

- **Security camera** (`UsageInfo.ProvidesSecurity 1`): `Standard.sam` `RegionFX[4]` "Security Camera" — radius 5
  cells, security 20 (a guard is radius 3, security 10).
- **Staff room** (`UsageInfo.ChillsYouOut 1`): the rest area of `AllStaffConstants.HappyHitCosNoRestArea`.

## Implementation

- The build menu has **tabs** (rides, sideshows, shops, scenery, staff: `BuildCatalogItem.Category`); the scenery
  tab lists the theme's placeable features (one entry per name — the four "Loudspeaker" looks share one).
- A feature is placed like a ride (model, script animation, footprint, sell refund, its own create/destroy puff) but
  `Ride.IsFeature`: no entrance, queue, riders, upkeep, wear or upgrades; it isn't a reason for visitors to come.
- Cameras deter vandals and litterers within 5 cells (`Staff.GuardNear` → `Ride.CameraNear`).
- With a staff room in the park, tired staff no longer lose happiness for having nowhere to rest (T-049 morale).
- Scenery cheers the visitors within 3 cells by `AttractionValue / 5` happiness a second — an interpretation, the
  original's beauty effect isn't reversed.
- Advisor: `FeaturesBuildFeatures` (TAG_SYSTEM 85, `-40 + 1 per day` with no features) and `StaffGuardBuildCamera`
  (28, trouble over `AcceptableTrouble 4` with guards but no camera). Unit-tested (`FeatureAdviceTests`).
- The dev park (`OPENTPW_AUTOPLACE`) places a camera, a staff room and two small features.

## Not done

Research groups (`Research.Group` 1/2 features are available from the start here), `FeaturesPoorSelection`, the
loudspeakers' music, watering (`RequiresWatering`).
