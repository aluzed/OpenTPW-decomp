# OpenTPW Documentation — Project Status

> Analysis performed on **2026-06-15**. OpenTPW is an open-source re-implementation
> (C# / .NET 8) of *Theme Park World / Sim Theme Park* (Bullfrog/EA, 1999).
> The project does **not** rewrite the game via disassembly: it **reads the original
> game's assets** and renders them in a new engine (Veldrid).

## Table of contents

| Doc | Content |
|-----|---------|
| [01-project-status.md](01-project-status.md) | Overview, architecture, what works / what's left |
| [02-file-formats.md](02-file-formats.md) | File format details and implementation progress |
| [03-disc-compatibility.md](03-disc-compatibility.md) | Is the provided `.7z` compatible with the project? |
| [04-linux-compatibility.md](04-linux-compatibility.md) | Does OpenTPW run on Linux? (nuanced answer) |
| [05-ghidra-reverse.md](05-ghidra-reverse.md) | Ghidra installed: how it helps here + RE roadmap |
| [06-rse-vm-opcodes.md](06-rse-vm-opcodes.md) | RSE ride-script VM: opcode table + arities |
| [07-ghidra-render.md](07-ghidra-render.md) | Native render loop & frame pacing (DDraw + D3D execute buffers + MMX software) |
| [08-ghidra-animation.md](08-ghidra-animation.md) | Ride animation (vertex keyframes in sibling `.md2` files) & `.sgn` signs, RE'd |
| [10-ui-reference.md](10-ui-reference.md) | The original in-game HUD and windows, measured from captures — the target for the UI rework |
| [Linux.md](Linux.md) | Step-by-step setup guide for the Linux toolchain (.NET, Ghidra, deps) |
| [tickets/](tickets/) | Backlog of actionable tickets derived from build + test runs |

## Quick answers

### 1. Is the `.7z` compatible with this project?
**Yes.** `jeu-02988-theme_park_world-pcwin.7z` is a CloneCD image
(`.ccd`/`.img`/`.sub`) of the **original Theme Park World installation CD** (FR
release, Abandonware-France). The disc contains a `DATA/` folder with exactly the
assets OpenTPW reads: `*.WAD`, `*.SAM`, the `JUNGLE/FANTASY/SPACE/HALLOW` levels, etc.
So it is **the correct asset source**. See [03-disc-compatibility.md](03-disc-compatibility.md).

### 2. Is it Linux-compatible?
**Partially.** Two separate things:
- **The original game (`TP.EXE`)**: **SafeDisc**-protected (`TP.ICD`, `SECDRV.SYS`).
  Irrelevant for OpenTPW — we only need the assets, not to run the game.
- **OpenTPW itself**: cross-platform foundation (.NET 8 + Veldrid + SDL2), so Linux
  *in theory*. It **builds on Linux** (verified), but has Windows-specific locks
  (`\` paths, `NAudio`, `System.Drawing.Common`) that break at runtime.
See [04-linux-compatibility.md](04-linux-compatibility.md).

## Build / test status (verified 2026-06-15, .NET 8.0.422 on Linux)

- **Build**: ✅ `dotnet build OpenTPW.sln` → 6 projects, **0 errors**, 109 warnings.
- **Tests**: ❌ **7/7 failing** — hardcoded paths + dependency on a real game install.
  See [tickets/T-001](tickets/T-001-backslash-paths-linux.md) and
  [tickets/T-002](tickets/T-002-tests-absolute-paths.md).
