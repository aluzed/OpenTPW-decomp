# T-049 — Management UI depth (finance graph, staff fire/patrol, per-ride upgrade panel)

- **Priority**: 🟡 Feature
- **Type**: UI / gameplay
- **Status**: ✅ Core done
- **Parents**: [T-042](T-042-economy-controls-loans.md), [T-043](T-043-staff-management.md),
  [T-044](T-044-research-upgrades.md). **Builds on**: [T-041](T-041-ride-shop-placement.md) (HudPanel/UI).

## Context

The economy, staff and research systems are all **core done** and driven from real data, but their
player-facing UI is still keyboard/HUD-text only. This ticket finishes the clickable panels.

## Scope

1. **Finance** (T-042 tail): a clickable economy panel with an income/expense **graph** over time, and
   per-ride price controls (beyond the global admission/price keys).
2. **Staff** (T-043 tail): **fire** a staff member from the UI, and assign **patrol zones** (handymen/
   guards already patrol toward litter/unhappy peeps — let the player bound the area).
3. **Research** (T-044 tail): a **per-ride upgrade/research panel** — pick what to research, see
   `Upgrades[*]` progress and apply capacity/other bumps per ride (the data + effects already exist).

## Acceptance criteria

- The player can, by mouse: read a finance graph + set a ride's price; fire a staffer + set a patrol
  zone; and drive research / apply an upgrade from a panel — each verified in-game.

## What was done

1. **Finance graph** — `ParkFinances` now keeps a rolling per-month history
   (`History`: `(Balance, Income, Expense)` sampled in `Tick`, capped at `MaxHistory = 48`). New
   `FinancePanel` (**F11**) plots the income (green) / expense (red) bars over time with the current
   balance + cumulative totals. History sampling is unit-tested (`ManagementDepthTests`).
2. **Per-ride price** — already clickable in `ManagePanel` (`PRICE-`/`PRICE+` on the selected-ride row,
   from T-042); left as-is.
3. **Staff fire + patrol zones** — `Staff.Fire()` cleanly removes the staffer + its shadow + guard
   registration; a new pure `PatrolZone(center, radius)` bounds wandering **and** job-seeking (a zoned
   guard ignores trouble outside its area; a zoned handyman only clears litter inside it). `BuildMode`
   selects the nearest staffer to a Default-tool click (`SelectedStaff`) and exposes fire / set-zone-here
   / grow-shrink / free-roam; `ManagePanel` shows a **FIRE / ZONE± / SET-ZONE / FREE-ROAM** row and
   `ParkStatsPanel` a status line. `PatrolZone` containment is unit-tested.
4. **Per-ride research/upgrade** — already a clickable gauge/button in `ManagePanel` (`RESEARCH`/`UPGRADE`
   with a live progress bar, from T-044); left as-is.

In-park mouse verification (clicking a staffer, dragging the graph open) is display-blocked like the other
recent UI tickets; the new model logic (`ParkFinances.History`, `PatrolZone`) is unit-tested, and the UI
reuses the verified `HudPanel` click/hit-test path.

## Remaining (nice-to-have)

- A draggable patrol-zone (currently anchored at the staffer's position with ± radius), and a zone
  highlight ring in the world.
- A dedicated per-ride panel window (the controls live on the shared `ManagePanel` bar today).

## Affected files

`source/OpenTPW/World/ParkFinances.cs`, `source/OpenTPW/World/Staff.cs`,
`source/OpenTPW/World/Build/BuildMode.cs`, `source/OpenTPW/UI/Widgets/FinancePanel.cs` (new),
`source/OpenTPW/UI/Widgets/ManagePanel.cs`, `source/OpenTPW/UI/Widgets/ParkStatsPanel.cs`,
`source/OpenTPW/UI/Widgets/HudPanel.cs`, `source/OpenTPW/World/Level.cs`,
`source/OpenTPW.Tests/ManagementDepthTests.cs` (new).

## Update (2026-10-21) — selection arrow

The selected ride / stall / member of staff is marked by the game's own bobbing selection arrow
(`esprites/Generic/Thoughts/SPR_TC` frames 3–6; blue = ride, yellow = stall, green = staff) — `SelectionArrow`.

## Grades, training, energy, morale and strikes (2026-10-21)

From `Standard.sam`'s staff tables (`StaffGrades`, pure + unit-tested) and the strike logic reversed from `tp.exe`
(`StaffStrikes`):

- **Grades 0–4.** A recruit's grade is rolled around `StaffPoolInfo.AvgGradeOf*` (±1), or two above it
  `ChanceToGetGreat*` percent of the time. The wage scales with `PerGradeStaffConsts[g].BaseWage` (relative to grade 1,
  the level the economy was tuned at); the work does too — a mechanic repairs in `WorkDuration` turns (80 → 20, i.e.
  20 s → 5 s at 4 turns/s), an entertainer reaches `ActivationDistance` cells and cheers `HappinessEffectOnCell` a
  second, a guard chases one troublemaker for `WorkDuration` turns, a researcher counts `ResearchAbility / 3`
  grade-1 researchers (`Staff.ResearchPower`), a handyman's broom also sweeps the litter within half its
  `DetectionRange` cells; after a repair / toilet / chase every member of staff idles `IdleDuration` turns.
- **Training.** The selected staffer's **TRAIN $x** button buys 10 % at `*ConstsPerGrade[g].PoundsPerTrainingPoint`;
  100 % raises the grade (grade 4 can't be trained). The HUD shows grade, training, energy and mood.
- **Energy and morale.** A job spends 1.2 energy a second; resting recovers `RecuperationRate` per turn (half that
  while strolling); at 10 a staffer stops to rest until 60. Each day spent tired (< 30) costs
  `HappyHitCosNoRestArea` × 2.5 happiness (there is no staff room to rest in), a rested day gives back
  `HappinessRecuperationRate`. (The rates are an interpretation; the thresholds below are the original's.)
- **Strikes** (`FUN_00508f70`/`FUN_00509360`): from the 24th month, once a month per type — a grievance is more than
  three of that type averaging under 15 energy ("striking through fatigue") or happiness ("through unhappiness").
  It escalates a warning → a one-week strike → two weeks → a month (then a month each time); content staff reset
  it, and a warning called off thanks the player. Strikers draw no wage, stop working (no research either) and
  picket the `FixedItemInfo.StrikeArea*` patch by the bus stop. The advisor speaks the game's own lines
  (TAG_SYSTEM 33–62: `StaffTrain*`, `StaffUnhappy*`, `StaffVeryUnhappy*` mid-month, `StaffStriking*`,
  `StaffHappier*`, `StrikeEndBad*`), with the `Advisor.sam` scores. Not reversed: the handymen's extra litter-ratio
  grievance (`FUN_004c8180/FUN_004c8150` against a constant), the strike walk-out animation.
- Saves keep each staffer's grade, training, energy and mood.
