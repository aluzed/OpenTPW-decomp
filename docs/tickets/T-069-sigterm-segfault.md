# T-069 — Segfault when the process is terminated (SIGTERM)

- **Priority**: 🟡 Low/Medium (bug)
- **Type**: Engine / shutdown
- **Status**: ✅ Done — `Game.Run` registers SIGTERM/SIGINT via `PosixSignalRegistration`, cancels the
  runtime's default exit and calls `Renderer.RequestQuit()`, which closes the SDL window on the render thread
  (the window-close path). `kill -TERM` and SIGINT now exit with code 0, no core dump (verified).

## Observed (2026-10-21)

The game runs stably (70 s+ in the lobby, 60 s in a park), and closing the window exits with code 0. But
`kill -TERM <pid>` makes it crash with **SIGSEGV (exit 139, core dumped)** instead of exiting. Likely the
native Vulkan / OpenAL / windowing resources are torn down from a signal context or after the runtime has
started unloading.

## Scope

1. Handle SIGTERM/SIGINT (`PosixSignalRegistration` / `Console.CancelKeyPress`) by requesting the same clean
   shutdown as a window close, instead of letting the runtime exit under the render loop.
2. Check the dispose order (GPU device after its resources, audio context last).

## Acceptance criteria

- `kill -TERM` and Ctrl+C on a running game exit with code 0 and no core dump.

## Follow-up (2026-10-21)

A SIGTERM **during the level load** still crashed (exit 139, or 143 when the default handler won): the handlers
were registered only after the synchronous load, just before the render loop. They are now registered first
thing in `Game.Run`; a signal before the renderer exists is remembered (`quitSignalled`) and the loop closes the
window as soon as it starts. Verified: SIGTERM at 0.5 / 2 / 4 / 6 / 7 / 12 s after launch → exit 0 every time.
