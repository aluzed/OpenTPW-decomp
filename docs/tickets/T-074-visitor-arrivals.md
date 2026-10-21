# T-074 — Visitor arrivals (`Arrival.*`) instead of a fixed crowd of 30

- **Priority**: 🟠 Medium — two golden-ticket goals were unreachable with a fixed crowd of 30
  (`GoldenTicketLocal.PeopleInPark 200`, `AtLeastThisManyHappyPeople 150`)
- **Type**: Gameplay / RE
- **Status**: ⚠️ Implemented (data-driven approximation, 2026-10-21) — exact original formula still to reverse.
  - `ArrivalScheduler` (pure, unit-tested): a group every `TimeBetweenArrivals × 0.1 s` (150 → 15 s) of
    `FixedRate + rating / PointsPerVisitor (+ NewParkBonus in year 1)`, at least `MinPeople`, capped at 250 in park.
  - Visitors stay `PeepInfo.ExitLevel ± ExitLevelVar` seconds (the .sam calls it the ExitLevel counter "in
    SECONDS"; energy drained at a hard-coded 2/s, ~50 s) and then **leave for good** (`Peep.LeaveOnExit`) instead
    of being recycled. The opening crowd of 30 stays.
  - Jungle dev park: fills to the 250 cap in ~100 s (new-park bonus), economy turns positive (entry fees + stall
    sales), both golden-ticket crowd goals become reachable.
  - Side fixes found with the bigger crowd: terrain height sampling was 94% of the frame (→ spatial index +
    ground-plane height, commit `perf:`), late arrivals drew over the HUD (UI now renders last), peep/staff
    shadows were dark squares and sprites 1.5 tiles tall (sized for the old 16-unit grid, T-072).

## Observed

The park always holds exactly 30 peeps (`Level` spawns 30; a leaving peep is `Recycle`d at once as a fresh
paying arrival). Attendance doesn't react to park rating, admission price or park age. Found by the scripted
play session (empty park: VISITORS 30).

## Authored data

`levels/Standard.sam` (+ `Easy_Standard`, `Online_Standard`, no comments):
`Arrival.MinPeople 1`, `Arrival.TimeBetweenArrivals 150`, `Arrival.FixedRate 15`, `Arrival.NewParkBonus 20`,
`Arrival.PointsPerVisitor 6` (5 easy, 2 online). Also `PeepInfo.ExitLevel 120 ± ExitLevelVar 60` (seconds a peep
stays) and the entry-fee opinion block (`PeepInfo.MinimumEntryFee`, `*PriceMultiplier`, `OpinionFor*`).

## RE notes (no-CD `tp.exe`, Ghidra 12.1.2 headless)

- `.sam` keys are read through a **descriptor table** at `0x740bec`: 34 entries of 0x3C bytes (56-byte name +
  u32 type; 5/4/1 codes), blocks closed by a type-0 entry naming the block (`StaffPoolInfo`, `Arrival` at
  `0x74136c`). No destination pointers — the parser fills a struct in field order. The only code reference into
  the table is at `0x679ba7`, which Ghidra did not disassemble as a function (needs a manual disassembly).
- Arrivals are **vehicle-based** in the original: `FUN_004cf050` (de)serialises an arrival object
  `{ mArrivalRate @+0, (sub-object @+4), mTargetVehicleCapacity @+8, mPeopleOnBus @+0xc, mOffloading @+0x10,
  mGatesOpen @+0x11 }`, and `FUN_00516c80`/`FUN_005179c0` save `mArrivalVehicle_Size1/2/3` +
  `mCurrentArrivalVehicle` (buses of three sizes). These are serialisers only; the update logic still has to be
  found (search for readers of the arrival object's `+0`/`+0xc` fields).

## Scope

1. Find the arrival update (from the object above) and the meaning of the five keys.
2. Replace the fixed 30 with an arrival scheduler (buses offloading at the gate), departures after
   `ExitLevel ± ExitLevelVar`, entry-fee refusals from the opinion block.
3. Keep the visitor count + admissions feeding `ParkFinances` / golden-ticket goals.

## Entry-fee opinion (2026-10-21)

`EntryFeeOpinion` reads the `PeepInfo` price block: ideal fee = total ride excitement ÷ `ExcitementToCostDivisor`;
below `MinimumEntryFee` or ≤ `Cheap × ideal` → `OpinionForTooLow` (100), ≤ `Average × ideal` → `AboutRight` (50),
below `Expensive × ideal` → `TooHigh` (20), else `MuchTooHigh` (0). The opinion is used as the visitor's **arrival
happiness** — `AboutRight` 50 is exactly the old fixed `StartHappiness`, and a 0 leaves at once (below
`LeaveHappiness`). Interpretation until reversed. So FEE± now matters: a cheap park arrives happy (in the 130 s
dev-park run vandalism fell from 69–351 to 0), an overpriced one turns visitors away. Unit-tested.
