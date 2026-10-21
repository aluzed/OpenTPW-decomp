# T-054 — Challenge / scenario system

- **Priority**: 🟡 Feature (headline gameplay)
- **Type**: Engine / UI
- **Status**: ⚠️ Core done — `Challenges.sam` parses; a `ChallengeManager` state machine offers/accepts/
  declines, tracks the metric gain vs `TargetVal` over `TargetTime` days (on `GameClock.OnNewDay`), wins/loses,
  pays the prize and chains `FollowupType`; the HUD shows the challenge (Y accept / N decline). Verified
  in-game (load → offer → accept → day countdown → timeout → next). The **advisor now reacts to a win/loss**
  (T-055 milestone tips). **Metric mapping expanded + corrected from `Challenges.sam`'s own EA comments** (the
  authoritative type semantics): 1 = sell fries (food), 2 = sell burgers (food), 3 = drinks, **11 = new
  visitors**, **13 = sideshow profit**, **20 = peeps using a ride** — six types now tracked + offered (was 1/2/3,
  and Type 1 had been mis-mapped to *visitors* — the real visitors challenge is Type 11). Fries+burgers share the
  single `FoodSold` counter and the "use the Minecart ride" goal is approximated by total `RidesRidden` (no
  per-ride counter). Unit-tested (`ChallengeMetricsTests`). **2026-10-21: +3 types** — 12 (profit from
  all shops = concession takings), 16 (research N rides ≈ ride research completed) and 18 (build a coaster ≈ any
  track ride placed); `TargetObj` (the specific ride, e.g. 1180 = Minecart) is not mapped yet. Still-opaque
  types (balloons 6/9, costumes 7/10, loops 19, toilet cleanliness 24, staff happiness/skill, specific-ride
  builds/upgrades) stay skipped until a counter exists. A dedicated clickable panel + advisor-delivered
  offers remain (polish).
- **Needs**: [T-053](T-053-ingame-clock.md) (in-game days). **Related**: [T-055](T-055-golden-ticket-goals.md),
  [T-046](T-046-advisor-character.md) (advisor delivers challenge offers).

## Context

TPW's objective layer is the **challenge engine**: timed goals ("sell 30 drinks in 60 days", "build a Minecart
Coaster then add 3 loops then get 200 riders") offered over the level, with cash prizes and multi-part chains.
OpenTPW implements none of it, though all the data and the tracking infrastructure (money, visitor counts,
ride usage, shop sales) already exist.

## What we know (RE recon — strong)

- **Data: `data/Challenges.sam`** — **35 challenges**, each: `Type` (category: 2=burgers, 3=drinks, 18=build
  ride, …), `FollowupType` (chains the next challenge), `TargetTime` (days, 30–180), `TargetVal` (numeric
  target), `TargetObj` (specific ride/shop id, e.g. 1180=Minecart Coaster), `Prize` (2000–30000),
  `CheckAtEndOnly`, `Independent`. Plus globals `ChallengesInThisLevel`, `DaysUntilFirstChallenge`,
  `DaysAfterCompletedChallenge`, `DaysAfterDeclinedChallenge`.
- **Binary (Ghidra, named C++):** loader logs `"Loading challenges.sam went horribly wrong! Shout at Bjarne!"`;
  state machine `GetCurrentChallenge()`/`EndCurrentChallenge()`, fields `mCurrentChallenge`, `mChallengeOn`,
  `mNextChallengeEventTime`, `mChallengeDeclined/Lost/Offered`; events `"Challenge %d is being offered!"`,
  `"Won/Lost the challenge!!!"`. Persisted in `SAD_ADV_SCORING` (T-059).
- A generic `BalanceLoader` reads the `.sam` key→field tables — one parser yields the struct map.

## Scope

1. Parse `Challenges.sam` into a typed `Challenge` table (reuse `SettingsFile`/`SAMParser`; index `[n].Field`).
2. A `ChallengeManager` (per level): offer timing (`DaysUntilFirstChallenge`, gaps after won/declined via the
   T-053 clock), accept/decline, progress tracking per `Type` against `TargetVal`/`TargetObj`, win/lose
   detection (`CheckAtEndOnly` vs continuous), prize payout (`ParkFinances`), and `FollowupType` chaining.
2. Map each `Type` to the metric it watches (drinks/burgers sold, peeps on a ride, ride built, …) — most map
   onto existing counters (`ParkFinances`, `Shop` sales, `Ride.Riders`, build events).
3. UI: an active-challenge panel (goal text + progress bar + days left) and accept/decline + win/lose
   notifications (route through the advisor, T-046).
4. Unit-test the parser + the pure progress/win-loss logic.

## Acceptance criteria

- Challenges from `Challenges.sam` are offered over time, tracked against real park metrics, paid out on
  success, and chained via `FollowupType`; the player sees + accepts/declines them.

## Affected files (anticipated)

`source/OpenTPW/World/Challenge.cs` + `ChallengeManager.cs` (new), `UI/Widgets/ChallengePanel.cs` (new),
hooks in `ParkFinances`/`Shop`/`Ride`/`Level`, `source/OpenTPW.Tests/ChallengeTests.cs` (new).
