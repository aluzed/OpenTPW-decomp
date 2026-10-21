# T-063 — Restore the original lobby front-end + world select

- **Priority**: 🟠 Medium (first thing the player sees)
- **Type**: Engine / UI
- **Status**: ✅ Core done — the default launch shows the authentic lobby again (themed `LobbyIsland`s over the
  sky, orbit camera, logo, `LobbyLayout` purple buttons), and clicking an island enters that world
  (`LobbyWorldSelect` → `Level.RequestReload`). The dev park is test-only (`OPENTPW_TESTPARK=<theme>`).

## Context

An earlier T-032 commit made `Level` always boot straight into the jungle dev park (`InPark=true`), so the
original front-end never showed. Commits `4482bfd` + `4d7c0ea` restored it and made it functional.

## Scope (done)

1. Default launch builds the menu (`Level.CreateMenu`): Jungle/Hallow/Fantasy/Space islands, each loaded
   best-effort (Space's model throws, as upstream, and is skipped).
2. `LobbyWorldSelect` projects each island to screen, draws its authentic name (`THEMENAMES.str`) and enters
   that world on a click near it.
3. `LobbyCameraMode` framed on the island cluster.
4. `OPENTPW_TESTPARK` (legacy alias `OPENTPW_LEVEL`) + `OPENTPW_AUTOPLACE` jump directly into a park.

## Remaining

- ~~The `PurpleButton`s only play a click sound~~ — Quit works (T-068) and, since 2026-10-21, **player profiles**: an
  empty "Create New Player" button creates `save/users/<slot>Player n` and selects it (marked `*`), an existing
  player's button selects it, and a selected player's save slots live in its folder (`PlayerProfile`,
  `SaveGame.SlotPath`); without a profile, saves stay in `.opentpw/`. Remaining: typing a name (it is "Player n").
- World labels overlap the button column (→ [T-070](T-070-lobby-label-overlap.md)).
- ~~The Space island model fails to load~~ — fixed in T-073 (Space Zone is now offered).

## Affected files

`source/OpenTPW/Client/Game.cs`, `source/OpenTPW/World/Level.cs`,
`source/OpenTPW/UI/Widgets/LobbyWorldSelect.cs`, `source/OpenTPW/World/LobbyCameraMode.cs`,
`source/OpenTPW/UI/Layouts/LobbyLayout.cs`.
