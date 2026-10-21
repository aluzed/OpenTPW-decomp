# T-044 — Research & ride upgrades

- **Priority**: 🟡 Feature
- **Type**: Engine / UI
- **Status**: ✅ Core done — the full `Upgrades[*]` table is parsed; researchers advance research; an
  applied upgrade raises capacity (and the live queue); the per-ride `ManagePanel` has a clickable
  RESEARCH/UPGRADE button with a live progress gauge. Verified in-game. (Research duration isn't `.sam` data
  — see Remaining; only a global research queue is left, a nicety.)
- **Parent**: [T-038](T-038-park-management-ui.md).
  **Needs**: [T-041](T-041-ride-shop-placement.md) + [T-042](T-042-economy-controls-loans.md);
  **researchers** from [T-043](T-043-staff-management.md).

## Done

- **Full `Upgrades[*]` table parsed** into `Ride.Upgrades` (`InitCapacity` / `CostOfResearch` /
  `CostOfUpgrade` per level); level 0 = as built. `Ride.Capacity` is now the current level's capacity,
  and `RideQueue.Capacity` delegates to it so an upgrade is immediately live for peeps.
- **Research**: `Ride.StartResearch()` + `TickResearch(dt, researchers)` (driven each frame from
  `Staff.ResearcherCount`) accumulates researcher-seconds to unlock the next level; no researchers → no
  progress. `ApplyUpgrade()` raises the level once researched.
- **Controls**: select a ride, `R` pays `CostOfResearch` + starts research, `U` pays `CostOfUpgrade` +
  applies it. HUD shows level / capacity / research % / next cost.
- **Verified in-game** (`OPENTPW_AUTOPLACE`): totem L0 cap 6 → research complete → apply → L1 cap 8
  (3 levels parsed), matching the `.sam`.

## Remaining (follow-up)

- ~~A per-ride research/upgrade **panel** (progress bar)~~ — done: `ManagePanel.ResearchButton` (RESEARCH →
  RESEARCHING n% gauge → UPGRADE → MAX LEVEL).
- ~~source the research **duration** from the `.sam` (`DurationOfUpgrade`)~~ — **no source data**: dumping a
  ride `.sam` shows `Upgrades[*]` carries `CostOfResearch`/`CostOfUpgrade`/`InitCapacity`/
  `QueueWaitTimeConstant` but **no research-duration key**, so the researcher-seconds constant stands.
- ~~**Global research queue** across rides~~ — **done**: a park-wide `ResearchQueue` (a pure FIFO-no-dupes
  `FifoSet<Ride>`) processes **one ride at a time** — `Ride.StartResearch` enqueues, only the queue head
  advances each frame (`OnUpdate` gates `TickResearch` on `ResearchQueue.Active == this`), completion dequeues
  and promotes the next, and a sold/demolished ride drops out (`Despawn`). `ManagePanel` shows `RESEARCHING n%`
  for the active ride and `QUEUED #n` for waiting ones. Reset per level. Replaces every researching ride
  soaking up the whole research team in parallel. Unit-tested (`FifoSetTests`), verified in-game (rides
  complete in FIFO order, queue 3→0). 

Nothing actionable remains — research & upgrades are complete.

## Context

Each ride `.sam` already parses an `Upgrades[*]` table (`InitCapacity`, `RedLineCapacity`,
`CostOfUpgrade`, `CostOfResearch`, `QueueWaitTimeConstant`) — currently unused. This ticket spends
money + researcher time to unlock and apply upgrades that raise a ride's capacity/throughput.

## Reference (original)

`CostOfResearch` / `CostOfUpgrade` / `DurationOfUpgrade`; research is staffed by **Researchers**
(`MaxResearchers`, `AvgGradeOfResearchers`, `mAllResearchCompleted`, `FeaturesResearchMore`). Build/
upgrade gating: "Can only build objects at upgrade level 0 at the moment", "Cannot upgrade this
object", "Found upgrade... modify?". So an object starts at upgrade level 0 and is upgraded in steps.

## Work

1. **Parse the full `Upgrades[*]` table** into `Ride` (currently only `MaxCapacity` is read): per-level
   `InitCapacity`/`RedLineCapacity`/costs/`QueueWaitTimeConstant`.
2. **Research progress**: spend `CostOfResearch` + researcher-time (`DurationOfUpgrade`, scaled by the
   number/grade of researchers) to unlock the next upgrade level.
3. **Apply upgrade**: pay `CostOfUpgrade` to raise the ride to the next level → bump its capacity
   (`Ride.Capacity` → next `InitCapacity`) and queue throughput.
4. **UI**: a research/upgrade panel per ride (current level, cost, progress).

## Acceptance criteria

- With a researcher hired, a ride's next upgrade unlocks after its research duration; paying the
  upgrade cost raises its capacity, and the bigger queue/throughput is observable in-game.

## Affected files

`source/OpenTPW/World/Rides/Ride.cs`, `RideQueue.cs`, `source/OpenTPW/World/ParkFinances.cs`,
`source/OpenTPW/World/Staff.cs` (researchers), UI, `Level.cs`.

## Researching new items (2026-10-21, night)

The original's research **unlocks** rides, sideshows, shops and features — until now everything was buildable from
the start, and research only upgraded rides. `ItemResearch` (pure, unit-tested) follows the data:

- each item's `.sam` gives `Research.Group` (0 = available from the start; 1–4 later) and its research cost
  `Upgrades[0].CostOfResearch` (jungle: hyenas 700, karts 600, coaster 1050, the mine coaster 800…);
- `Standard.sam` gates the groups (`ResearchTech[g].PercentageForThisTech` 0 / 0 / 80 / 85 / 85: group g opens once
  that share of the items below it is researched) and splits the work by category (`ResearchCategories[c].Effort`:
  rides 100, shops 15, sideshows 30, features 15); each category works on its cheapest open item;
- the scientists' rate is OpenTPW's: 1.5 points a second per grade-1 researcher (`Staff.ResearchPower`, so better
  grades research faster), split half-and-half with a ride-upgrade research when one is under way.

The build menu greys out what isn't researched ("(research)", or "researching 34%" for a category's current target)
and won't select or place it. A finished item is announced by the advisor in the game's words (TAG_SYSTEM 114 "Your
scientists have researched the …", 118 "You've researched a new feature: …"; `ResearchRideResearched` 150 …), and the
advisor asks for scientists when most / all the available rides are built (TAG 21 / 22, `StaffHireResearchers1/2`) or
for more research (66, `RidesResearchMore`). Challenge 16 ("Research 2 new rides") counts rides researched; 30
("Research and build the Hocus Pocus") means something now. Saved with the park. The dev park (`OPENTPW_AUTOPLACE`)
has everything researched; `OPENTPW_RESEARCH_DEMO=1` hires four scientists in a fresh park.
