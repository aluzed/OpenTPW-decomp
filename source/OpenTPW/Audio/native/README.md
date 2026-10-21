# tpwmp3 — native MPEG audio decoder (minimp3)

The game decodes `.mp2` music/SFX through [minimp3](https://github.com/lieff/minimp3) (public
domain), wrapped by `tpwmp3.c` into a tiny C ABI (`tpw_mp3_decode` / `tpw_mp3_free`) and loaded via
`DllImport("tpwmp3")`. NLayer mis-decoded the game's MPEG‑2 Layer II audio (dropped ~12% of frames);
minimp3 decodes it correctly — see [T-031](../../../../docs/tickets/T-031-game-audio.md).

## Files

- `tpwmp3.c` — the wrapper.
- `minimp3.h`, `minimp3_ex.h` — the upstream decoder (vendored).
- `build.sh` — builds the lib for whichever toolchains are present.
- `linux-x64/libtpwmp3.so` — committed Linux build.
- `win-x64/tpwmp3.dll`, `osx/libtpwmp3.dylib` — committed; cross-compiled from Linux with zig (the dylib is
  universal x86_64 + arm64, the arm64 slice ad-hoc signed). Not yet run on Windows / macOS.
- `fatmacho.py` — joins thin Mach-O files into a universal one (a `lipo -create` stand-in).

`OpenTPW.csproj` copies the per-OS lib next to the executable; .NET resolves `DllImport("tpwmp3")`
to `libtpwmp3.so` / `tpwmp3.dll` / `libtpwmp3.dylib`. If the lib for the current OS is absent, audio
disables itself gracefully (no crash).

## Building

```bash
./build.sh                        # Linux (gcc); also Windows (mingw-w64) / macOS (clang|osxcross) if present
ZIG=~/zig/zig ./build.sh          # + Windows and macOS, cross-compiled by zig
```

Per-platform commands (also in `.github/workflows/native-audio.yml`):

| Platform | Command |
|----------|---------|
| Linux    | `gcc -O2 -fPIC -shared tpwmp3.c -o linux-x64/libtpwmp3.so -lm` |
| Windows  | `gcc -O2 -shared tpwmp3.c -o win-x64/tpwmp3.dll` (mingw-w64) |
| macOS    | `clang -O2 -dynamiclib tpwmp3.c -o osx/libtpwmp3.dylib` |

The **native-audio** GitHub Actions workflow builds all three on their native runners and uploads
them as artifacts; download and commit them under the matching folder to ship audio on that OS.
