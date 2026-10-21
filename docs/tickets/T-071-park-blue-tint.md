# T-071 — Park terrain looks uniformly blue

- **Priority**: 🟡 Medium (visual)
- **Type**: Engine / rendering
- **Status**: ☐ To do — investigate

## Observed (2026-10-21)

`OPENTPW_TESTPARK=jungle OPENTPW_AUTOPLACE=1`: after ~60 s the whole jungle park (terrain, paths, rides) reads
as a strong blue/teal wash. The HUD showed `NIGHT` at that moment, so it may just be the T-056 day/night
overlay (`DayNightOverlay`) — or a missing terrain texture.

## Scope

1. Capture the park at day and at night; if the blue is the night wash, tone it down to something closer to
   the original (and check `ThemeEngine.AmbientLightLevel` handling).
2. Otherwise, find which terrain texture fails to load.

## Acceptance criteria

- The jungle park reads green/brown in daylight, and night is clearly darker without being a flat blue.
