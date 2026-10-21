# T-055 — Golden-ticket goals / level objectives

- **Priority**: 🟡 Feature
- **Type**: Engine / UI
- **Status**: ⚠️ Core done — `GoldenTicketLocal.*` targets parse from `Standard.sam`; a pure `GoldenTicket`
  evaluator + `GoldenTicketGoals` manager track the live park (visitors / in-park / happiness / happy-people /
  profit-per-year / **recent-visitors-over-a-months-window**, on `GameClock.OnNewDay`) and award the ticket
  once all set targets are met; the HUD shows `TICKET GOALS n/N` → `GOLDEN TICKET WON!`. Unit-tested, verified
  in-game. **`RecentVisitors` now wired**: `ParkFinances` snapshots the cumulative visitor count per closed
  month (`FinanceSample.Visitors`) and `ParkFinances.RecentVisitors(months)` returns the count admitted over
  the `RecentVisitorMonths` window; `GoldenTicket.Evaluate` scores it as a 6th goal. **Award advisor
  congratulation now wired**: the advisor edge-triggers a one-shot `CongratGoldenTicket` tip the tick the ticket
  is won (via `AdvisorAdvice` + a new readable line) — likewise a `CongratChallengeWon` / `ChallengeFailed`
  reaction for challenges (T-054). **Remaining (polish)**: the
  award **particle effect** (the advisor speaks, but no fireworks yet); Global/Secret tiers aren't in the
  jungle `.sam`.
- **Needs**: [T-053](T-053-ingame-clock.md) (months for the "recent visitors" window). **Related**:
  [T-054](T-054-challenge-system.md) (shares the `SAD_ADV_SCORING` progression cluster), [T-047](T-047-ride-event-3d-sound-particle-pools.md)
  (particle/award effect).

## Context

The golden ticket is TPW's per-level **win condition**: hit a set of park targets (visitors, happiness,
profit, …) to earn the Local → Global → Secret tickets. The level `.sam` defines all the targets; nothing in
OpenTPW reads them.

## What we know (RE recon — strong)

- **Data: `levels/<lvl>/Standard.sam`** — `GoldenTicketLocal.Visitors 100`, `.PeopleInPark 200`,
  `.Happiness 75`, `.AtLeastThisManyHappyPeople 150`, `.ProfitYear 15000`, `.RecentVisitors 350`,
  `.RecentVisitorMonths 6` (and Global/Secret tiers per the binary).
- **Binary (Ghidra, named C++):** `CGoldenTicketControl::TellTheWorld` with Local/Global/Secret variants and
  sub-states `ticket only → ticket and key → ticket, key and park`; award reasons (`big park`, `water length`,
  `gokart excitement`, `coaster height`); hint strings `GoldTicketNearToXPeeps/XHappiness/XProfit`,
  `"You have %d total golden tickets"`. Persisted in `SAD_ADV_SCORING`. Particle `P_EFFECT_GoldenTicket`.
- The advisor already surfaces near-miss hints (`AdvisorAdvice`, T-046) — wire the `GoldTicketNearTo*` ones.

## Scope

1. Parse the `GoldenTicket{Local,Global,Secret}.*` targets from the level `.sam`.
2. A `GoldenTicketGoals` evaluator: track real park state (`Peep.AverageHappiness`, visitor counts,
   `ParkFinances` profit-per-year over the T-053 clock, recent-visitors window) against each tier's targets;
   fire the award when all are met.
3. On award: the `P_EFFECT_GoldenTicket` particle + an advisor congratulation; feed the `GoldTicketNearTo*`
   hints into `AdvisorAdvice`.
4. UI: a goals panel (each target + met/▢) showing progress toward the ticket.
5. Unit-test the pure target-evaluation logic.

## Acceptance criteria

- The level's golden-ticket targets load, the player sees progress, and meeting all of a tier's targets awards
  the ticket (effect + advisor line).

## Affected files (anticipated)

`source/OpenTPW/World/GoldenTicketGoals.cs` (new), `UI/Widgets/GoalsPanel.cs` (new), hooks in `Level`/
`AdvisorAdvice`, `source/OpenTPW.Tests/GoldenTicketTests.cs` (new).

## 2026-10-21 — award effect

Winning the golden ticket now makes every ride launch a firework (`Ride.Celebrate()` → `P_EFFECT_FW1explosion`,
rendered by the new particle sprites, T-066). Not seen in-game yet: a 200 s autoplace run met 4/6 goals (the
15 000/year profit is far off). A visitor counts as "happy" over **66**: the game's own happy band, reversed from its park-happiness gauge (`FUN_004a52a0` shows `FUN_004c9670(0x42)` — the % of visitors over 66 — against `FUN_004c9750(0x21)`, the % under 33). It was an approximate 50.
