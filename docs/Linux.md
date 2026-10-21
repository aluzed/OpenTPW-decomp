# Linux Setup Guide

Step-by-step setup of the toolchain used to build OpenTPW and reverse-engineer the
original game on Linux. Validated on Ubuntu 24.04 (kernel 6.17) on **2026-06-15**.

The two pieces installed here:
1. **.NET 8 SDK** — to build/run OpenTPW.
2. **Ghidra 12.1.2** (+ JDK 21) — to reverse the original binaries.

Plus optional runtime libraries (SDL2/Vulkan) and disc-extraction tools.

---

## 1. .NET 8 SDK

### Option A — user-local install (no root, what was used here)

```bash
cd /tmp
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
```

Add it to your `PATH` (append to `~/.bashrc` to make it permanent):

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
```

Verify:

```bash
dotnet --version      # → 8.0.422 (or newer 8.0.x)
```

### Option B — system package (needs root)

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

---

## 2. Build & test OpenTPW

### Quick way — the dedicated Linux helper

A `build-linux.sh` script at the repo root wraps the dotnet calls (locates the SDK on
`PATH` or in `~/.dotnet`, sets telemetry opt-out):

```bash
./build-linux.sh            # build the solution (Debug)
./build-linux.sh -c Release # build in Release
./build-linux.sh test       # build + run the test suite
./build-linux.sh run        # build + run the OpenTPW game project
./build-linux.sh clean      # remove bin/ and obj/
```

> If `./build-linux.sh` is blocked by filesystem permissions, run `bash build-linux.sh`.

### Manual way

```bash
cd /var/www/reverse/OpenTPW/source
dotnet build OpenTPW.sln -c Debug      # → 6 projects, 0 errors (109 warnings)
dotnet test OpenTPW.Tests/OpenTPW.Tests.csproj --no-build
```

> A `global.json` pins the SDK to .NET 8 (any installed `8.0.x` works), and
> `source/Directory.Build.props` defines per-OS compilation constants
> (`WINDOWS` / `LINUX` / `OSX`) so platform-specific code can be guarded with
> `#if LINUX` once the Windows locks are addressed (see the tickets).

> Current state (2026-10-21): the build succeeds (0 errors) and `dotnet test` is green
> (0 failed, 282 passed, 19 inconclusive — the inconclusive ones need the game data, see §5).

### Runtime libraries (to actually launch the game window)

Veldrid uses Vulkan or OpenGL via SDL2. Install the runtime libs:

```bash
sudo apt-get install -y libsdl2-2.0-0 libvulkan1 mesa-vulkan-drivers
```

> Audio (OpenAL) needs no system package: `Silk.NET.OpenAL.Soft.Native` bundles
> `libopenal.so` under `runtimes/linux-x64/native/` in the build output (see T-003).

> Note: even with these, the game will not run cleanly until the Windows locks are
> fixed (paths/NAudio/System.Drawing) — see [04-linux-compatibility.md](04-linux-compatibility.md).

---

## 3. Ghidra 12.1.2 (+ JDK 21)

Ghidra 12 requires **JDK 21**.

```bash
# JDK 21 (skip if 'java -version' already shows 21)
sudo apt-get install -y openjdk-21-jdk
java -version            # → openjdk version "21..."
```

Download & extract Ghidra:

```bash
cd /tmp
curl -sL -o ghidra.zip \
  "https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.1.2_build/ghidra_12.1.2_PUBLIC_20260605.zip"
unzip -q ghidra.zip -d "$HOME"      # → ~/ghidra_12.1.2_PUBLIC/
```

> For a newer release, check
> <https://github.com/NationalSecurityAgency/ghidra/releases/latest> and adjust the URL.

Launch:

```bash
~/ghidra_12.1.2_PUBLIC/ghidraRun
```

See [05-ghidra-reverse.md](05-ghidra-reverse.md) for the reverse-engineering workflow.

---

## 4. Disc-image extraction tools (the provided `.7z`)

```bash
sudo apt-get install -y p7zip-full      # provides 7z/7za
# optional, to convert CloneCD .img → .iso:
sudo apt-get install -y ccd2iso          # or 'bchunk'
```

Extract the disc image from the archive:

```bash
cd /var/www/recomp/OpenTPW-decomp
7z e "iso/jeu-02988-theme_park_world-pcwin.7z" -oiso "Theme Park World/Theme Park World.img"
```

Convert and mount (one option):

```bash
ccd2iso "iso/Theme Park World.img" iso/tpw.iso     # CloneCD 2352-byte sectors → 2048 ISO
mkdir -p /tmp/tpw-cd
sudo mount -o loop,ro iso/tpw.iso /tmp/tpw-cd
ls /tmp/tpw-cd/DATA            # the assets OpenTPW needs
```

> Disc images and the no-CD archive live in `iso/` (git-ignored, like the `*.7z`, `*.img`,
> `*.iso`, `*.ccd`, `*.sub` patterns) — never
> commit game data. See [03-disc-compatibility.md](03-disc-compatibility.md).

---

## 5. Point OpenTPW at the game data

OpenTPW expects `<gamePath>/data/`. Set the install location with the
**`OPENTPW_GAMEPATH`** environment variable (it takes precedence over the persisted
Windows default in `source/OpenTPW/Settings.Designer.cs`):

```bash
export OPENTPW_GAMEPATH="$HOME/games/theme-park-world"
./build-linux.sh run
```

The same variable drives the integration tests:

```bash
OPENTPW_GAMEPATH="$HOME/games/theme-park-world" ./build-linux.sh test
```

To also run the per-format sample tests (`TPW_*_SAMPLE`), use the dedicated runner: it
points every sample variable at a real file, extracts the ones that live inside a WAD or
outside `Data/` on the disc into `install/.samples`, then runs the suite
(300 passed / 1 inconclusive — no original `.TPWS` save exists on the disc):

```bash
tools/run-integration-tests.sh
```

On a case-sensitive Linux FS the runtime checks for a lowercase `data/`, while the disc
ships `Data/` — add `ln -s Data install/data` (the asset VFS itself is case-insensitive) —
see [03-disc-compatibility.md](03-disc-compatibility.md).

---

## Summary of installed components

| Component | Version | Location | Purpose |
|-----------|---------|----------|---------|
| .NET SDK | 8.0.422 | `~/.dotnet` | Build/run OpenTPW |
| JDK | 21.0.11 | system | Required by Ghidra |
| Ghidra | 12.1.2 PUBLIC | `~/ghidra_12.1.2_PUBLIC` | Reverse the original binaries |
| 7-Zip | 23.01 | system (`/usr/bin/7z`) | Extract the provided archive |

### Diagnostic output

The game logs to stdout. Verbose trace lines (every ride-script event, animation and boarding cycle) are off by
default; set **`OPENTPW_TRACE=1`** to see them. `OPENTPW_ECON_DEBUG=1` adds a periodic economy line (balance,
income/cost flows, products sold). `OPENTPW_FAST_CLOCK=1` runs the calendar at a dev pace (a month in 8 s instead
of the original's ~3 minutes) to exercise monthly settlement, challenges and seasons quickly.

