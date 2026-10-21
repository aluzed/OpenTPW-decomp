# T-060 — Peep AI ride-choice scorer (authored decision weights)

- **Priority**: 🟡 Feature (simulation fidelity)
- **Type**: Engine / RE
- **Status**: ✅ Done (2026-10-21) — previously ⚠️ Core done — the authored `PeepInfo.DecisionVar*` weights load from `levels/Standard.sam`
  (`DecisionWeights`); a pure `RideChoiceScorer` scores each ride by excitement vs distance vs queue length
  (+ a new-ride bonus), and `Peep.PickRoute` now picks a ride **weighted by that score** instead of the
  excitement/rating-only heuristic. **Polish now landed**: (1) **per-peep preferred-excitement** — the authored
  `PeepTypes[*].PreferredExcitement` table loads via `PeepTypes.Load` (read out of the engine's *columnar* `.sam`
  layout, where one key path lists several fields and the value is the first column); each peep takes a random
  type's taste and the scorer's excitement term now rewards how *close* a ride is to that taste (a timid peep
  favours gentle rides, a thrill-seeker intense ones) — `preferredExcitement = -1` keeps the old "more is
  better" behaviour for back-compat. (2) **Ride-age tracking** — `Ride.AgeDays`/`Ride.IsNew` (built-day vs
  `GameClock.TotalDays`, window = `DecisionVariable1`), so the new-ride bonus actually fires for a ride's first
  days instead of `IsNew=false`. 11 unit tests (5 new). The need weights (thirst/hunger/…) steer the
  stall choice instead (`NeedChoiceScorer`), with toilet queues counted as walk (T-039).
- **Related**: [T-034](T-034-peeps.md)/[T-050](T-050-peep-simulation-depth.md) (peep sim — currently uses a
  simpler excitement-weighted choice).

## Context

Peeps currently pick rides by a simple excitement weighting (T-050). The original runs a **weighted-utility
scorer** over several factors (distance, queue, excitement, thirst, …) with authored weights — replacing our
heuristic with it would make crowd flow read like the real game.

## What we know (RE recon)

- **Binary (Ghidra):** the ride-choice scorer logs `"Calculating option score for object with no BOQ"` and
  reads a tunable weight vector: `DecisionVariable1..8`, `DecisionVarDistWeight`, `DecisionVarQueueWeight`,
  `DecisionVarExcitementWeight`, `DecisionVarThirstWeight`. These are `.sam` balance keys (read via the
  generic `BalanceLoader`).
- Supporting peep AI strings: `"A peep became stuck and renavigated"`, `Peep %d collided with Peep %d`,
  needs weights `ScorePerThirsty/Hungry/WaitingPerson`, happiness model
  (`HappinessRecuperationRate`, `HappinessEffectOnCell`, Small/Medium/Big `HappinessChange`).
- Our peeps already expose the needed inputs (position/distance, ride excitement, queue length, thirst/hunger).

## Scope

1. Find + parse the `DecisionVar*` weights from the level/global `.sam` (confirm exact keys + which file).
2. A pure `RideChoiceScorer`: `score(peep, ride) = Σ weight_i · factor_i` (distance, queue wait, excitement,
   thirst, …) matching the RE'd factor set; peeps pick the max-scoring reachable ride.
3. Swap `Peep`'s current excitement-weighted `PickRoute` to use the scorer; keep the avoid-immediate-repeat
   rule.
4. Unit-test the scorer (a closer/shorter-queue/more-exciting ride wins; weights change the ranking).

## Acceptance criteria

- Peeps choose rides via the authored weighted-utility scorer; tuning the weights visibly shifts crowd flow;
  the scorer is unit-tested.

## Affected files (anticipated)

`source/OpenTPW/World/RideChoiceScorer.cs` (new), `Peep.cs` (`PickRoute`), balance-key parsing in `Level`,
`source/OpenTPW.Tests/RideChoiceScorerTests.cs` (new).

## 2026-10-21 — need weights now steer shops

`NeedChoiceScorer` (+ `NeedWeights` from `PeepInfo.DecisionVar{Thirst,Hunger,Toilet}Weight`): every stall serving
a need past its threshold scores `needWeight · urgency − distanceWeight · distance / 200`, best wins — replacing
"nearest stall of the single most urgent need". Unit-tested (`NeedChoiceScorerTests`). A 130 s autoplace session
stays in the same range (shops $408 vs $472, toilet visits 48 vs 59); single runs are noisy (vandalism 29 vs 0
in the previous run), so balance wasn't re-tuned. Illness weight (`DecisionVarIllnessWeight`) has no
illness/first-aid model to drive yet.

## Update (2026-10-21) — the mood a ride gives

A finished ride now lifts the visitor's happiness by the authored match between the ride's excitement and the
visitor's `PreferredExcitement` (`PeepInfo.PerfectRide` +25 within ±5, `GoodRide` +15 within ±15, `OKRide` +5 within
±40, nothing further off — `Peep.RideHappiness`, unit-tested), less up to 15 for a worn ride. It was
`(satisfaction − 50) × 0.3`, which ignored the visitor's taste; satisfaction still drives the ride rating and the
visitor's thought.
