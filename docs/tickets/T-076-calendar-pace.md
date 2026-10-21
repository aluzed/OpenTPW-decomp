# T-076 — In-game calendar pace

- **Priority**: 🟠 Medium (balance; affects challenges, loans, seasons, advisor timing)
- **Type**: Gameplay / RE
- **Status**: ☐ To do — found 2026-10-21

## Observed

`GameClock.SecondsPerMonth` is 8 s ("matches the economy's old month"), so an in-game day lasts ~0.27 s and a year
~1.6 minutes. That pace is a development convenience, and it clashes with the authored data:

- visitors stay `PeepInfo.ExitLevel` 120 ± 60 **seconds** — 15 in-game months at this pace;
- challenges give `TargetTime` 30–90 **days** — 8–24 seconds of play to sell 100 drinks or 150 gifts;
- the advisor's per-day nudges (`RidesBuildRides.AdditionalPerDay` 3, `ShopsBuildShops` 2…) climb in seconds;
- loans repay over `RepaymentPeriodInMonths` 24–48 — 3–6 minutes.

## Scope

1. Find the original's day/month length in `tp.exe` (the calendar advance in the game loop; Ghidra, see docs/05).
2. Retune `SecondsPerMonth` and re-check what was balanced against 8 s: monthly upkeep/wages/loan instalments
   (`ParkFinances`), the weather/season cycle, challenge offers (`DaysUntilFirstChallenge`), golden-ticket windows.
3. Keep a fast-clock switch for tests/dev parks.
