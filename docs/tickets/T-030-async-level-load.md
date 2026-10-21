# T-030 — Renderer: asynchronous / threaded level load

- **Priority**: 🟠 Medium (the window is frozen ~20-25s during load)
- **Type**: Rendering / UX
- **Status**: ✅ Done — the acceptance is met: the load freeze is resolved (responsive, per-step + per-mesh
  progress with a progress bar; lobby in ~4 s, a park in ~2.5 s). The 60 fps fully-threaded load (To do 1–2) stays
  an optional follow-up, not needed for the acceptance.
- **Follow-up of**: [T-024](T-024-linux-black-screen.md) (promotes its informal follow-up to a ticket).

## Problem

This is the *other* "not responding" cause, distinct from the lobby stutter
([T-026](T-026-render-resource-churn.md)). `Game.Run` constructs `new Level( "jungle" )`
**synchronously on the main thread** (`source/OpenTPW/Client/Game.cs`), which takes ~20-25s
(textures/models). During that window the render loop isn't running, so the window can't pump
events — it sits frozen on the last "LOADING…" frame and the WM marks it "not responding". A
loading screen is presented *before* the load, but it does not animate *during* it.

## Done

- ✅ **Checkpoint progress reporting** (synchronous, no threading). `LoadProgress.Report(status)` is
  called at the major load steps in `Level` (settings, water, sky, each island, interface) **and
  per mesh inside `LobbyIsland`** (the bulk of the load — each island decompresses up to 16 textures
  + uploads a GPU buffer per mesh). Each call pumps events and re-presents the loading screen with
  the current step at the bottom ("Loading island: Hallow… (20/25)"). `Game.Run` wires
  `LoadProgress.OnReport` to `RenderLoadingScreen`. Verified: the status strip changes ~every 0.4 s
  through the whole load, so the window stays responsive — no more freeze / "not responding".
  (`source/OpenTPW/Global/LoadProgress.cs`, `Client/Game.cs`, `World/Level.cs`, `World/Lobby/LobbyIsland.cs`.)
- ✅ **Progress bar.** `LoadProgress` also tracks a `Progress` fraction (`Report(status, fraction)` +
  `BeginPhase`/`ReportSub`, so each island fills its slice of the bar by mesh). The loading screen
  draws a track + fill bar (1×1 colour textures via the UI material) above the status line. Verified
  on-screen: the bar fills as meshes load ("Loading island: Hallow… (18/25)" ≈ 63%).

## To do

1. ☐ Run the level construction off the main thread (Task/worker) while the main thread keeps
   pumping events and re-presenting an (ideally animated) loading screen at 60 fps. Veldrid GPU
   resource creation must happen on the render thread — marshal uploads back, or split asset
   *decode* (CPU, threadable) from GPU *upload* (main thread), draining an upload queue per frame.
   (Until then, a *single* long step — e.g. one island ~several seconds — can still briefly stall
   between checkpoints; add finer per-mesh/texture checkpoints if needed.)
2. ☐ Swap to the level once loading completes; keep `Render.ClearColor`/loading-screen teardown.

## Risks

- Veldrid `GraphicsDevice`/`ResourceFactory` calls are generally not free-threaded — keep GPU
  resource creation on the render thread; only parallelize file reads + CPU decode.

## Acceptance

The window stays responsive (no "not responding") during the level load; the loading screen
animates or at least re-presents; the lobby appears when ready.

## Affected files

`source/OpenTPW/Client/Game.cs`, `source/OpenTPW/Client/Renderer.cs`, `source/OpenTPW/World/Level.cs`,
asset upload paths under `source/OpenTPW/Render/Assets/`.

## 2026-10-21 — load time 19 s → 12 s (jungle dev park)

Profiled with `dotnet-trace` (sampled thread time): the VFS re-listed directories for every path segment on every
read inside a WAD (`ResolveCaseInsensitive` + `FindArchivePath` per archive-prefix probe, ~7 s), and
`Texture.Missing` created a new GPU texture per access (thousands, as `MaterialSlots` pads 16 slots per model).
Both are now memoized/shared. Remaining big items: LZSS texture decode (~2 s), the ground compose (~2 s), WAD
directory parsing (~4 s across ~30 archives) — a threaded load would hide them.

## 2026-10-21 — measured, and the texture decode halved

Measured with a stopwatch on `LoadProgress.Report`: the lobby is up 4.2 s after launch (1.5 s of start-up, the
islands 2.7 s) and a park's own load (terrain, ground, scenery) takes ~2.5 s — the 20–25 s freeze this ticket
started from is long gone; only the dev park's autoplace (a dozen rides built one after another) adds ~7 s.
Half of a park's texture time was the `.wct` decode of textures **already uploaded** (228 of 494 in the dev park:
every ride and stall reloads the shared base-plate / sign textures) — `Texture` now checks its cache before
decoding. And `Texture.PrefetchWct` decodes a batch in parallel (the files are read on the calling thread — the
WAD streams aren't thread-safe — then decoded on the thread pool, then uploaded on the render thread as they are
created): used for the terrain, each ride, and each lobby island (islands 3.35 s → 2.7 s). A fully threaded load
stays optional.
