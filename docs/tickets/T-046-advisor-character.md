# T-046 — Render the real advisor character (3D bug-head + visemes + messages)

- **Priority**: 🟡 Feature
- **Type**: Engine / rendering
- **Status**: ✅ Done — functionally verified (model + 5 visemes + lip-sync + speech); the **message
  system (`Advisor.sam`) is parsed + wired** with **per-event triggers** firing tips on real park state, and
  the **on-screen visual pass is done** (the bug head now reads as a clean bottom-right corner overlay,
  screenshot-tuned). Each message now speaks **its own recorded takes** (see "Per-message voice" below).
- **Parent**: [T-020](T-020-lip-mouth-shapes.md) (lip-sync format + wiring done — this is the render tail).
- **Related**: [T-031](T-031-game-audio.md).

## Context

T-020 decoded the `.LIP` lip-sync, RE'd the five visemes → advisor mesh-part names
(`mouth - normal/aah/eee/ooh/sss`, from `FUN_0044b2e0`), and wired a working lip-sync demo
(`AdvisorPanel`, `OPENTPW_ADVISOR_DEMO=1`) that plays a real speech clip and drives a **procedurally
drawn** mouth in sync. What remains is rendering the *real* advisor.

## Scope

1. Load the advisor model from `global/advisor.wad` (`Advisor.MD2` "Bug Head" + the `Advisorm1..m18.MD2`
   face/morph parts) and render it in its proper on-screen position.
2. Show the matching named sub-mesh per viseme (`MouthShape.MeshPartName()`) — hide the others — driving
   it from `LipSyncFile.ShapeAt` (already wired), replacing the demo's procedural mouth.
3. Hook the advisor **message system** (`Advisor/Advisor.sam`: `MessageGroups`, min-time/say-once/
   discard-after-slaps rules) so tips fire on real events (tutorial, golden-ticket, research, congrats)
   with the right speech clip + its `.LIP`.

## Done (so far)

- **Resolved the model structure.** `Advisor.MD2` ("Bug Head") loads cleanly through our `ModelFile`
  (25 named sub-meshes) and **contains the five viseme mouths as named meshes** exactly matching the
  RE'd selector — `Mouth - Normal/Aah/Eee/Ooh/Sss` (textures `Mouth1a..e.wct`) — plus `Bug Head`, eyes,
  `ShutEye` (blink), `Body`, antennae, hands, spatula and the context **hats** (Ticket/Kiosk/Bowler/
  Security/Handyman/Research/Fast Food/Hardhat). So it's **mesh-part swapping**, not the
  `Advisorm*.MD2` files (which are a separate sub-format we don't need).
- **`Advisor` world entity** (`source/OpenTPW/World/Advisor.cs`): builds a `ModelEntity` per relevant
  mesh (the bug face + the five mouths), anchors the whole assembly upright in front of / facing the
  camera each frame, shows only the active viseme's `Mouth - *` mesh (hides the other four), and plays a
  real `sp_001.mp2` clip driving the viseme from its `.LIP` via `ShapeAt`. `AdvisorPanel` is now a thin
  HUD label. Enabled by `OPENTPW_ADVISOR_DEMO=1`.
- **Verified (functional, in-game logs):** `Advisor.MD2 loaded: 25 mesh(es)` → `built 11 part(s)` (6 base
  + 5 visemes) → `speaking 'sp_001.mp2', 35 keyframes, 28.6s`, no exceptions.

## Done (this pass — the `Advisor.sam` message system)

The advisor's tip pacing/selection is now decoded + wired, so the **real `Advisor.sam` rules** decide when and
which message the advisor speaks (not an unconditional loop):

- **`AdvisorConfig`** (`source/OpenTPW/World/`): parses `Advisor/Advisor.sam` — the EA text key/value format,
  reusing the existing `SettingsFile`/`SAMParser` (no new parser). Exposes the global pacing
  (`MinTimeAnyMessage`/`MinTimeSameMessage`/`MinScoreForConsideration`), the per-group rules
  (`MessageGroups[N]` → `{MinTimeSameMessage, SayOnlyOnce, DiscardAfterSlaps}`, sparse 0-5+9), and a generic
  `Param(advice, key)` accessor over the ~200 per-advice scoring params for the future score formulas.
  Fault-tolerant: every key has a documented default, so a missing/stripped file still yields a usable config.
- **`AdvisorMessages`** scheduler (pure, time-injected): each tick the game `Submit`s candidate tips
  (id + group + score); `Consider(now)` picks the single best one enforcing the named rules — global min-gap,
  per-group same-message gap, say-once, min-score, and `Slap`/`DiscardAfterSlaps` retirement. Fully unit-tested.
- **Wired into `Advisor`**: it loads the config + builds the scheduler, and the demo now feeds two built-in
  candidates (a say-once `WelcomeTutorial` in tutorial group 1, a repeating `GeneralTip` in group 0) and speaks
  whatever the scheduler returns — so the visible behaviour is governed by the real rules. The id→clip mapping
  is by convention with a fallback to the shipped `sp_001` clip (we don't ship per-message advisor clips).
- **Unit-tested** (`AdvisorMessagesTests`, 10 tests: config globals/groups/advice parse incl. trailing-comment
  stripping + float params + missing-file defaults; highest-score-wins, min-score, global gap, same-message
  gap, say-once, discard-after-slaps, zero-discard). 180 tests pass, 0 new warnings.
- **Verified in-game** (`OPENTPW_ADVISOR_DEMO=1`, real assets): `message config: 7 group(s), minGap 5s,
  minScore 25` (parsed the real file), then `message 'WelcomeTutorial' → speaking 'sp_001.mp2'` — the say-once
  welcome fired first by score and drove real speech + lip-sync; 0 exceptions.

## Done (this pass — per-event triggers fire tips on real park state)

The scheduler now consumes **live park state**, so the advisor speaks on real events instead of a fixed demo
list. New `AdvisorAdvice` (pure rule-engine) + the state sources feeding it:

- **`ParkSnapshot`** + **`AdvisorAdvice.Evaluate(snapshot, config)`** map the park state → scored candidate
  tips, scoring each from the decoded `Advisor.sam` params (`AdvisorConfig.Param`): escalating **in-the-red**
  warnings by `MonthsInRed` (`InTheRedMonthLeft/ThreeMonths/SixMonths`), **thirsty/hungry visitors**
  (`ScorePer*Person` × count, threshold from the `.sam`), **research ready**, and a **happy-park congrats**.
  The advice→group mapping mirrors the original's hardcoded grouping (general/tutorial/congrats/research).
  Pure, fully unit-tested.
- **State sources**: `ParkFinances.MonthsInRed` (consecutive months closed in the red) + `Peep.CountThirstierThan`
  / `CountHungrierThan` aggregates. `Advisor.BuildSnapshot` gathers them each tick (thresholds read from the
  `.sam`) and submits the candidates after the say-once welcome; the scheduler's rules pick what actually speaks.
- **Unit-tested** (`AdvisorAdviceTests`, 6: healthy-park-is-silent, in-the-red escalation, solvent-ignores-stale-
  counter, thirsty/hungry per-visitor scoring, research+congrats firing + below-threshold, missing-param defaults).
  208 tests pass, 0 new warnings.
- **Verified in-game** (`OPENTPW_ADVISOR_DEMO=1` + `OPENTPW_AUTOPLACE`): a temporary diagnostic confirmed the
  snapshot reads the **live evolving state** (`money 1729→1777`, `happy 12→25`, thirsty/hungry resolved each
  frame, 0 candidates while the park is healthy → advisor stays quiet). Forcing a deficit then showed the full
  chain end-to-end: the say-once `WelcomeTutorial` opens, and once it finishes the real `InTheRedThreeMonths`
  tip is elected by the scheduler and spoken. Diagnostic reverted; 0 exceptions.

## Done (this pass — the on-screen visual pass)

Screenshot tooling now works (display + GPU), so the advisor's placement was tuned against real jungle
assets by iterating screenshot → adjust the anchoring constants in `Advisor.cs`:

- **Result**: the bug head sits as a small **bottom-right corner overlay** (`Distance 24`, `RightOffset 8.5`,
  `UpOffset -3.5`, `GroupScale 0.28`) at ~⅓ screen height, fully on-screen (no clipping), facing the camera,
  lip-syncing — down from the initial state where the model filled ~70% of the screen.
- **Body part hidden**: the `body` mesh hung off-axis below the head and cluttered the corner, so `BaseParts`
  now shows just the **head + eyes + antennae** (+ the active viseme mouth) — a clean "talking head". The
  body/hands/spatula/hats stay hidden as before.
- Verified across 8 screenshot iterations (final reviewed, not committed); 0 exceptions, build 0-warning,
  208 tests still pass.

## Remaining

- ~~**Per-message speech clips**~~ — **done**, see "Per-message voice" at the end. Previous notes: map each message id to its own clip + `.LIP` (today they fall back to the
  shipped `sp_001`). **RE finding (Ghidra, no-CD `tp.exe`):** the binding is a *runtime-assembled message
  database*, not a data-file manifest, so there's no quick table to lift:
  - Assets: `data/global/Speech/speechHD.SDT` holds 641 clips `sp_001..sp_641.mp2` (+ per-level
    `LocalSpeech`), indexed by the `cat_speech*.map` banks; `Advisor.sam` has **no** clip field per message and
    `advisor.wad` is model-only — so the mapping is purely in code.
  - Architecture: the advisor singleton array is `DAT_0079039c` (stride `0xb0`). Ctor `FUN_00429ba0` loads
    `Advisor[%s].md2` + inits the message system `FUN_00473e30`, which walks **12 message groups** at
    `advisor+0x1c` (each `{count, ptr, _}` 0xc bytes); every message is an **8-byte entry whose `+4` is the
    clip/presentation handle**, registered via `FUN_00470a00`. Dispatch: `FUN_00429e90` ("Initial advisor
    call") → `FUN_004732a0(advisor, type, msgId, flags, score)` (scheduler/scorer) → `FUN_00472f60` indexes
    `[advisor+(group+2)*0xc+8][msgId].+4` → `FUN_00472d70` drives the talking-head node visibility.
  - The clip handles in those entries are loaded a layer deeper (the message-centre / localized resource),
    which still needs tracing; the exact `sp_NNN` numbers weren't recovered. **Not worth a deep dig** unless
    real per-message voice becomes a priority. **Pragmatic alternative shipped:** the advisor now shows the
    readable **tip text** per advice id (`AdvisorTips`) in a HUD speech panel (one neutral `sp_001` voice
    under it), so the advice is useful to the player without the per-clip audio mapping.
- ~~The advisor still only spawns under `OPENTPW_ADVISOR_DEMO=1`.~~ **Done**: it now spawns by default in a
  park, gated on the persisted `GameSettings.AdvisorEnabled` (default true, the original's `AdvisorOn`); the
  env var still force-spawns it anywhere for testing. Verified in-game (welcome tip + lip-sync, no env var).
- Textures: the `.wct` are loaded from `global/advisor/textures/` via the VFS; verify they resolve (else
  the advisor renders untextured — still geometrically correct).
- Hook the **message system** (`Advisor.sam`: `MessageGroups`, min-time/say-once/discard-after-slaps) so
  tips fire on real events with the right clip, and pick the matching **hat** per advisor role.
- Exact runtime viseme selection (the `.LIP` only marks phoneme boundaries; `ShapeAt` cycles the five) —
  traceable from the speech-update caller if precise per-interval visemes matter.

## Acceptance criteria

- The advisor character renders on-screen and visibly mouths a speech clip with the correct viseme parts.

## Affected files

`source/OpenTPW/World/AdvisorConfig.cs` (`Advisor.sam` parser), `AdvisorMessages.cs` (message scheduler/rules),
`AdvisorAdvice.cs` (new — `ParkSnapshot` + rule-engine), `Advisor.cs` (builds the snapshot + drives speech
through the scheduler), `ParkFinances.cs` (`MonthsInRed`), `Peep.cs` (`CountThirstierThan`/`CountHungrierThan`),
`source/OpenTPW.Tests/AdvisorMessagesTests.cs` + `AdvisorAdviceTests.cs` (new). Earlier passes:
`source/OpenTPW/UI/Widgets/AdvisorPanel.cs`, the model/render pipeline, `ModelFile.cs` (named-part access).

## 2026-10-21 — original message text, still no per-message voice

The advisor now shows the game's own message text where one matches (`TAG_SYSTEM.str`, 332 entries: thirsty/hungry
visitors, money warnings, research, happy visitors, golden ticket, challenge won/failed), in the selected language.
Tested whether the voice clips follow the same numbering: the durations of `global/speech` `sp_NNN` (from the 637
`.LIP` files) don't correlate with the `TAG_SYSTEM` text lengths at any offset −2…+10 (r ≈ 0.1), so the clips aren't
indexed by message — consistent with the runtime-assembled message database noted above.

## Update (2026-10-21) — pricing advice

The advisor now gives the original's pricing advice: `PricingTicketTooExpensive` / `TooCheap` from the arriving
visitors' entry-fee opinion against `Advisor.sam`'s `TicketSatisfactionUnder 30` / `Over 80` (score 150; "too cheap"
only above the always-happy minimum fee), and per stall group — food, drinks, restaurant, souvenir shops — when
buyers' price verdicts (`EntryFeeOpinion.StallPriceMood`) pile up (≥ 5 in a ~30 s fading tally): TAG_SYSTEM.str
94–103 in the selected language. Long messages wrap onto two lines in the advisor panel.

Staffing / building advice with `Advisor.sam`'s own thresholds and scores: `StaffHireMechanics1/2/3` (worn rides
under 25 %, broken rides, more than 4 rides per mechanic), `StaffHireHandymen1/2` (litter 3–25 / ≥ 26 with no
cleaners), `StaffHireGuards2` (misbehaving visitors, no guards), `StaffHireEntertainers1` (≥ 35 visitors, none hired),
`RidesBuildRides` / `ShopsBuildShops` / `SideshowsBuildSideshows` (nothing built; the nudge grows per day from the
.sam's base) and `FeaturesNoToilets` (> 25 visitors needing one) — TAG_SYSTEM.str 11–20 / 67 / 77 / 81 / 86.
Verified: an empty park hears "no rides", "no shops", "no sideshows" in turn; the staffed dev park hears none.

`OpenPark` (TAG_SYSTEM 7, `ScorePerWaitingPerson` 20): with the gates shut and at least one ride built, arrivals
gather at the gate (`Level.WaitingAtGate`, up to 60) and walk in when the gates open; the advisor suggests opening.

Money and staff-count advice: `WealthHigh` (> 200 000, TAG 121), `WealthHighRepayLoan` (money > debt × 1.2, TAG 122),
`MaxHandymen/Entertainers/Mechanics/Guards/Researchers` (> 10 of one kind, TAG 129–133).
`ShopsBuildMoreShops` / `SideshowsBuildMoreSideshows` (fewer than 0.15 shops / 0.2 sideshows per ride while the park
has over 20 000; TAG 78 / 82).
`RidesNoUpgrades` (18 months in without a researched upgrade, TAG 68 — `NewResearchGroupRide` now speaks 70, "upgrades
available that you haven't used", which is what it means), `WagesHigh` (wages over the last 3 months > 2 × income, TAG
128; the monthly finance history now records wages), and `Bankrupted` (TAG 127) after six months in the red.

## Per-message voice (2026-10-21)

The mapping is a **static table in `tp.exe`** after all, reached from the lip-sync path: `FUN_00599050` ("Advisor
says ResponseID %d", formats `\Speech\lips\sp_%03d.lip`) scans 32-byte records at `0x768fb8` until id 9999 —
{response id, …, **clip number**, …, flags (high word 1 = the level's `LocalSpeech`), …, **`TAG_SYSTEM.str` text
index**}. 610 responses: every advisor text has 2–3 recorded takes (e.g. text 9 "Lots of your visitors are thirsty…"
→ `sp_020`–`sp_022`); the only local clip is each level's `sp_001`, its welcome (text 2).

`AdvisorSpeech.ClipsByText` carries the table (text → takes); `Advisor.Speak` maps the advice id to its text
(`AdvisorTips.TextIndexOf`, the existing `GameText` map), picks a take at random and plays it from
`data/global/Speech/speechHD.SDT` with its lip-sync from `lips.wad` (the bank is preloaded in the background while
the welcome plays). Advice the game never voiced keeps the level's `sp_001`. Verified in game: "StaffHireMechanics3"
→ `sp_032`/`sp_033`, "CongratVisitorsHappy" → `sp_405`/`sp_407`, "FeaturesNotEnoughToilets" → `sp_233`/`sp_234`.
