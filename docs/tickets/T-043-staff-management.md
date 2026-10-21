# T-043 — Staff hiring & management

- **Priority**: 🟡 Feature
- **Type**: Engine / UI
- **Status**: ✅ Done — hire (charge cost) + place-on-tile for all staff types, wages per head, **fire +
  patrol-zone assignment** (T-049), and **pick-up / re-place** (MOVE) all in and verified. (Per-role
  hire-cost config has no source data in the install — see Remaining.) **Authentic role names** now shown in
  the build catalog + HUD (`StaffNames.For`, decoded from `STAFF_TYPES.str`): a Handyman is a **Cleaner** and a
  Researcher a **Scientist** in TPW; the others match. Verified in-game (the catalog reads "Cleaner"/"Scientist").
- **Parent**: [T-038](T-038-park-management-ui.md). **Needs**: [T-040](T-040-build-mode-foundation.md).
  **Related**: [T-039](T-039-peep-needs-staff-depth.md).

## Done

- **Researcher** role added (feeds [T-044](T-044-research-upgrades.md)); `Staff.Role` exposed. Roster:
  Entertainer (cheer), Handyman (litter), Guard (litter deterrence), Researcher (wanders; research is
  off-screen). (2026-10-21: researchers and mechanics do have sprites — `esprites/Generic/Researchers/SPR_RE`,
  `Generic/Mechanics/SPR_FA` / `SPR_OM` — now used instead of flat billboards, and entertainers come from the
  active theme's `esprites/<Theme>/Entertainers` instead of always Fantasy's.)
- **Hire + place**: staff are catalog entries (no grid footprint — mobile); selecting one and clicking
  a tile spawns the staff there and charges its hire cost (`ParkFinances.PayBuild`/`CanAfford`).
- **Wages**: each `Staff` draws `WagePerSecond` (shown as WAGES). The park starts with **no** staff —
  the player hires them.
- **HUD**: STAFF count + the four staff entries (with hire costs) in the build palette.
- **Verified in-game** (`OPENTPW_AUTOPLACE` exercising the commit path): hiring entertainer + handyman +
  researcher → STAFF 3, money debited by the hire costs (800+600+1500), wages then drawn.

## Remaining (follow-up)

- ~~**Fire**~~ + ~~**patrol-zone assignment**~~ — done in [T-049](T-049-management-ui-depth.md)
  (`ManagePanel` FIRE / ZONE±/SET-ZONE/FREE-ROAM when a staffer is selected).
- ~~**Re-place** a placed staffer (pick up + drop elsewhere)~~ — **done**: `ManagePanel` has a **MOVE**
  button on the selected-staff row; it arms a move (`BuildMode.BeginMoveSelectedStaff`, button reads `DROP`)
  and the next tile click relocates the staffer there (`Staff.Relocate` — re-anchors its wander centre,
  teleports onto the ground, and carries its patrol zone along, radius kept). Verified in-game (a staffer +
  its r60 zone relocated +80,+80 with terrain-sampled height, 0 exceptions).
- ~~hire cost/wage from a real `AllStaffConstants` config~~ — **no source data**: there is no such file in
  the install. The level's `Staff.sam` is the *staff-room object* definition (Info.Id 1411 + upgrades/
  particles), not per-role hire costs/wages (verified by dumping the wad), so the fixed catalog costs +
  `WagePerSecond` stand. Nothing to wire.

## Context

Staff (`Staff` — entertainers, handymen→mechanics, guards) are spawned by the hardcoded dev layout.
This ticket lets the player hire, place, and fire them, paying a hiring cost + wages, with the four
real staff types.

## Reference (original)

Staff types are **Entertainers, Mechanics, Guards, Researchers** (`AvgGradeOf*`,
`BeginningNumberOf*`, `MaxResearchers`). Constants live in `AllStaffConstants` (`BaseWage`,
`BaseCostPerStaff`, `ChanceToGetGreat*`). Placement is a mode (`CMMPlaceStaff::OnLeftClick`) that
**picks up / drops** a staff member on a valid cell ("Cannot place staff member here - the cell is of
type %d", `mStaffMemberPickedUp`, "Picking up staff member %d") and patrol areas ("Could not find or
reach a destination in the patrol area"). `CAdvisor::HandleStaffInfo` drives the advisor/info panel.

## Work

1. **Hire**: a staff panel (per type) — hiring debits `BaseCostPerStaff`; spawn the `Staff` with its
   role sprite (already wired). Add **Mechanic** + **Researcher** roles (researcher feeds T-044).
2. **Place / pick up**: enter a place-staff tool (T-040), drop on a valid cell; allow picking an
   existing staff up to re-place them (`mStaffMemberPickedUp`).
3. **Wages**: per-staff `BaseWage` (already drained globally — make it per-head and shown).
4. **Fire**: select a staff member → dismiss (removes it, stops its wage).
5. **Patrol areas** (optional, links to [T-039](T-039-peep-needs-staff-depth.md)): assign a guard/
   handyman a zone.

## Acceptance criteria

- The player hires a guard (cash drops), places it, sees its wage in the finances, and can fire it.

## Affected files

`source/OpenTPW/World/Staff.cs`, `source/OpenTPW/World/Build/*`, `ParkFinances.cs`, `Level.cs`, UI.

## Update (2026-10-21) — wages by role

Each role's wage follows `Standard.sam`'s `PerTypeStaffConsts[n].PayMultiplier` (handyman 10, mechanic 30,
entertainer 15, guard 20, researcher 35) around the previous flat 1.5/s average — a mechanic costs three handymen,
a researcher 3.5. The authored `BaseWage × PayMultiplier` is in a time unit not yet pinned, so only the ratios are
used (`Staff.WagePerSecond`, unit-tested).
