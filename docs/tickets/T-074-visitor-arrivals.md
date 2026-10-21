# T-074 — Visitor arrivals (`Arrival.*`) instead of a fixed crowd of 30

- **Priority**: 🟡 Feature (fidelity — not blocking: golden-ticket visitor goals count admissions, and a
  recycled visitor pays again)
- **Type**: Gameplay / RE
- **Status**: ☐ To do — RE started (2026-10-21)

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
