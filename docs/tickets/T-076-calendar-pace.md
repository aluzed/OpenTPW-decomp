# T-076 — In-game calendar pace

- **Priority**: 🟠 Medium (balance; affects challenges, loans, seasons, advisor timing)
- **Type**: Gameplay / RE
- **Status**: ✅ Done — 2026-10-21

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

## Resolution (2026-10-21)

Reversed from `tp.exe` (Ghidra):

- The calendar object (save fields `mFunnyTimeStart`, `mSessionStart`, `mMonthAtLastUpdate`, `mDayAtLastUpdate`,
  `mFunnySecsPerRealSec`; serializer `FUN_004f7f30`) is built by `FUN_004f7e80`, which sets
  **`mFunnySecsPerRealSec = 15000`** and a start date of **1 January 2000**.
- Every date query (`FUN_004f85a0`, `FUN_004f8690`, `FUN_004f8770`…) computes
  `turn × mFunnySecsPerRealSec / 4 × 10 000 000` — a FILETIME in 100 ns units — and converts it to a SYSTEMTIME, so
  the calendar is Gregorian. `FUN_004f8770` returns `month / 3`: the season.
- The world turn counter (`world + 0x1da70c`, incremented by `FUN_00516380`) runs on every 8th step of the main
  loop's fixed 31 ms step (`FUN_0054e360`, `case 10`), i.e. ≈4 turns per real second — hence the `/ 4`.
- Bankruptcy (`FUN_004d0370`) divides the time in the red by `0x17928F8648000` = 30 days in 100 ns units and
  triggers past 5 — six 30-day months, which `ParkFinances` already models.

So **one in-game day = 86400 / 15000 = 5.76 real seconds**, a 30-day month ≈ 173 s and a year ≈ 35 minutes.

`GameClock` now follows that: game seconds from the 2000-01-01 epoch at 15000× real time, Gregorian
day/month/year, `DayFraction`. Running costs (wages, ride upkeep) are per real second and stay as tuned; what
moves to the authentic pace is everything counted in days/months: loan instalments, bankruptcy (≈17 min in the
red), challenge offers (`DaysUntilFirstChallenge` 540 ≈ 52 min) and windows (30–90 days ≈ 3–9 min), weather
changes (7 days ≈ 40 s), the new-ride window (7 days), golden-ticket years. Saves keep `ClockSeconds` in real
seconds at the original pace.

`OPENTPW_FAST_CLOCK=1` restores the old dev pace (a 30-day month in 8 s); any other number is used as the
speed multiplier. Unit tests: `GameClockTests`.
