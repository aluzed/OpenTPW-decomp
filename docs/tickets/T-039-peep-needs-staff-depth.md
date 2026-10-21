# T-039 — Peep needs & staff depth

- **Priority**: 🟡 Feature
- **Type**: Engine
- **Status**: ✅ Core done (+toilets, +breakdowns) — thirst + drink stalls, **bladder + toilets**, vandalism
  that guards measurably reduce, a balanced long-run economy, peep thoughts/ratings (T-050), and **ride
  breakdown + mechanic repair with spark/zap feedback**. Only a reliability-balance tuning pass remains.
  **2026-10-21: litter bins** — each theme's bin feature (jungle `pelbin`, hallow `bin`, fantasy `bfbin`, space
  `spacbin`: "Litter Bin", 1×1, $100, `UsageInfo.HoldsLitter 1`) is buildable; a visitor within ~3 tiles of one
  bins its rubbish (random litter and the wrappers stalls hand out) instead of dropping it (`Shop.Discard`).
  Vandalism still scatters litter on the ground. Stray (non-wrapper) litter dropped to ~1 piece per 2 minutes of
  wandering (was one per 25 s, which pinned a 250-visitor park at the 300-piece cap).
  Litter now draws as the game's own rubbish sprites (`esprites/Generic/Litter/SPR_LI`: wrapper, cup, can…, at the
  visitors' pixel scale) instead of a brown square.
  **Ride sickness** (`Standard.sam`): each ride adds `excitement ÷ PeepInfo.RideVomitDivisor` (10) to the rider's
  illness; from 60 % of `PeepInfo.VomitCapacity` (100) the visitor thinks "Feeling sick" (THOUGHTS.str 14, green-face
  bubble) and heads for a toilet (it shares the toilet need, like `DecisionVarIllnessWeight`), where it recovers; one
  who reaches the capacity is sick at the ride exit — a vomit puddle (the litter sheet's last two frames, cleaned by
  handymen, souring visitors near it like litter), the `P_EFFECT_GreenPuke` effect and −15 happiness
  (`MediumHappinessChange`). With the authored values it takes ~10–20 rides, so it shows in long stays on intense
  rides (verified by forcing the divisor: 66 visitors sick in two minutes, puddles + effect).
  **Ride throughput (2026-10-21)**: a 250-visitor dev park managed ~1 ride per visitor in 2½ minutes, with lines of
  10–17 beside empty rides. Two causes: (1) a visitor joined a ride's line the moment it *chose* the ride, so one
  still crossing the park from the gate (20–40 tiles) sat at the front of the line and everyone behind it waited at
  the entrance — visitors now walk to the back of the line and join it there; (2) the "visible dev breakdowns" wear
  (every ride broke after 25 s of use, one mechanic couldn't keep up) — wear now follows the level's authored
  `Upgrades[n].WearRate` (5/10 as built → ~250 s of occupied running; 3 and 2 once upgraded). Rides ridden at the
  2-minute mark: 87 → 197. This is the reliability-balance pass the ticket was waiting on.
  **Pranksters** (`Standard.sam`): `PeepInfo.PrankeryLikelihood` (5) visitors in 100 are pranksters; every 10 s one
  lets off a stink bomb with `StinkbombLikelihood` (25) % chance — the `P_EFFECT_YellowStink` effect and −5 happiness
  for every visitor within ~3 tiles — unless a guard is near (counted as deterred). ~9 stink bombs per 2 minutes in
  the 250-visitor dev park.
  **Toilet filth**: each visit dirties a toilet (+1 of 100); a handyman cleans the dirtiest toilet over 30 first (in
  its patrol zone), before litter. Using one over 60 (`Advisor.sam` `FeaturesDirtyToilets*.FilthierThan`) costs −5
  happiness and makes the visitor queasy (+15 illness, +30 over 90 — `RegionFX` "Dirty Toilet": illness), and it no
  longer settles a sick stomach. The advisor complains (TAG_SYSTEM 89–92, with/without cleaners, dirty/filthy) and
  challenge type 24 ("keep toilets N% clean", checked at the deadline) measures the average cleanliness. (Filth isn't
  saved yet.)
- **Related**: [T-034](T-034-peeps.md) (needs, economy, staff — core done).

## Context

Peeps already have happiness / energy / hunger, choose rides by excitement, queue, ride, eat at shops,
turn over, and the park has entertainers, handymen and guards. This ticket deepens the simulation so
each staff role and need has a fuller loop.

## Done

1. **Thirst + drink stalls** — peeps now build `thirst` (a bit faster than hunger) and detour to the
   nearest **drink** stall when parched, satisfying it (more park income). `Shop` gained a `ShopKind`
   (Food/Drink, blue vs green billboard) + `Shop.Nearest(kind,…)`; the build catalog/autoplace gained a
   "drink" stall; a peep picks whichever need (food vs drink) is more urgent relative to its threshold.
2. **Vandalism + guards** — an unhappy peep (happiness ≤ a vandalism threshold, above the give-up
   threshold so there's a window) periodically **vandalises** (scatters litter) *unless a guard is near*,
   which **deters** it. Both outcomes are tallied park-wide (`Peep.VandalismActs` / `VandalismDeterred`).
   Crucially, **guards now patrol toward trouble** (`Staff.DoGuard` heads for the nearest unhappy peep)
   so their deterrence actually lands where vandalism happens — a lone standing guard almost never
   coincides with a vandal (a point guard covers ~0.3% of the park). Staff patrol radius widened so they
   cover the park (guards reach queue backs, handymen reach far litter).
4. **Staff behaviour (partial)** — guards patrol toward unhappy peeps (above); other roles unchanged.
5. **Balance pass** — tuned thirst/hunger rates, the drink price, the vandalism window/rate and the
   patrol radius for a stable long run.
6. **Toilets + bladder need** — peeps build a `bladder` need (and a drink fills it further); when it
   passes its threshold they detour to the nearest **toilet** (a new `ShopKind.Toilet` — a *free* facility,
   no income) and relieve it. The need detour generalised to "most-urgent over-threshold need that has a
   stall" (`Peep.NeedDetour`, covering food/drink/toilet). A desperate peep (full bladder, no reachable
   toilet) loses happiness fast. New "toilet" catalog item (placeable + sellable via the build UI like any
   stall). Verified via `OPENTPW_AUTOPLACE`: `toilet=49` visits accumulate (`Peep.ToiletVisits`), no
   exceptions, economy stable.

Verified via `OPENTPW_AUTOPLACE` + `OPENTPW_ECON_DEBUG` (≈150 s): money held steady and net-positive
(≈10.0k → 10.5k), drink+food revenue grew, and with one patrolling guard **vandalism=41 / deterred=21**
(≈34 % of would-be acts stopped, total acts also down vs ~70 without a patrolling guard), no exceptions.
Build clean, 74/0 tests.

## Remaining

- ~~**Peep thoughts / ride ratings** (item 3)~~ — **done in [T-050](T-050-peep-simulation-depth.md)**:
  `Peep.RateRide` → satisfaction + `RideThought`, the running `Ride.Rating` reputation, and
  rating-weighted ride choice.
- ~~**Ride breakdown + mechanics** (rest of item 4)~~ — **done**: rides have a `Reliability` that wears down
  while carrying riders and `BreakDown()`s at zero (stops boarding + running), a mechanic (`Staff.DoMechanic`)
  heads to the nearest broken ride and `Repair()`s it (repair particle effect), and a broken ride now gives
  **breakdown feedback** — it periodically emits `P_EFFECT_Sparks` + an electrical `ZAP` (RideHD) at the ride
  through the 3D audio bus (`RideEngine.PlayBreakdownEffect`, on a `Ride.PeriodicDue` cadence), so the fault
  is visible + audible + locatable until repaired. Pure cadence helper unit-tested; verified in-game (10 forced
  breakdowns → 65 spark bursts, ZAP resolves, 0 exceptions). A reliability **balance** pass (scale wear by
  upgrade level / age) remains a tuning nicety.

## Acceptance criteria

- Peeps express thirst (visit drink stalls) ✅; unhappy peeps vandalise and guards measurably reduce it
  ✅ (deterred 21 of 62 would-be acts in the verified run); the economy stays balanced over a long run ✅.

## Affected files

`source/OpenTPW/World/Peep.cs`, `Staff.cs`, `Shop.cs`, `Level.cs`.
