# T-031 — Game audio: play sound in-engine (lobby music)

- **Priority**: 🟡 Feature
- **Type**: Engine wiring / audio
- **Status**: ✅ Done — music per world, ambience, SFX, volume UI and one shared decoder; the Windows/macOS
  `libtpwmp3` binaries are now committed (cross-compiled with zig; not yet run on those OSes).
- **Related**: [T-003](T-003-naudio-not-portable.md) (portable audio stack), [01-project-status](../01-project-status.md).

## Problem

The game played **no sound**. The pieces existed but weren't connected: `OpenTPW.Files` decodes the
audio containers (`SoundFile`/`MP2File`/`SdtArchive` — `.sdt` archives of MPEG `.mp2` tracks), and
the **ModKit** has an OpenAL previewer (`AudioPlayer`, NLayer + Silk.NET.OpenAL — T-003), but the
**game** (`OpenTPW`) had no audio device init and no playback — the "MusicVolume/SoundEffectsVolume"
strings were unwired labels.

## Done

- ✅ **Game audio service** `source/OpenTPW/Audio/Audio.cs`: lazy OpenAL init (graceful when there's
  no device — headless/CI just disables playback), `PlayMusic(mp2Bytes, loop)` (decode → one OpenAL
  buffer, `AL_LOOPING`), `StopMusic`, and a `MusicVolume` gain. OpenAL via Silk.NET (+.Soft.Native).
- ✅ **Lobby music**: `Game.Run` opens `data/global/sound/MusicHD.sdt`, takes the calm track
  (`level4c.mp2`) and plays it looping.
- ✅ **Decoder fix — NLayer → minimp3.** NLayer mis-decoded the MPEG-2 Layer II music (22 kHz):
  it dropped ~42 of 347 frames (~12%), producing a 15.9 s, time-desynced, glitchy stream
  (correlation 0.078 vs a correct ffmpeg decode) — audible as "missing instruments / weird". Switched
  to the public-domain **minimp3** decoder, bundled as a tiny native lib (`Audio/native/tpwmp3.c` +
  `minimp3*.h`, built by `build.sh` → `linux-x64/libtpwmp3.so`, P/Invoked as `tpwmp3`). minimp3's
  decode matches ffmpeg bit-for-bit (correlation **1.000**, full 18.1 s). The SDT payload offset was
  also corrected (`headerSize`, not the field sum — see commit) so the stream starts on an MP2 frame.
  Note: the source remains 22 kHz / 112 kb/s MP2 (the original asset); minimp3 just decodes it
  correctly. The ModKit previewer still uses NLayer and has the same MPEG-2 bug (item 3).
- ✅ **SFX (UI clicks).** `Audio.PlaySfx(key, mp2)` decodes via minimp3, caches the buffer per key,
  and plays on a round-robin pool of 8 non-looping sources. `PurpleButton.OnUpdate` hit-tests the
  cursor against the visible pill (`Input.MouseLeftPressed` rising-edge) and plays the original
  `textclick` (`sfUiHD.sdt`). Verified on-machine.
- ✅ **Volume.** Separate `MusicVolume` (0.5) / `SfxVolume` (0.8) gains; `-`/`+` adjust music volume
  live (edge-detected, logged). Persistent settings UI still TODO (item 2).
- ✅ **Cross-platform build infra.** `Audio/native/build.sh` builds per-OS (gcc/mingw/clang),
  `.github/workflows/native-audio.yml` produces `linux/win/macOS` libs as CI artifacts, and the
  csproj copies the matching lib per OS (Exists-guarded). Linux `.so` is committed; the
  Windows `.dll` / macOS `.dylib` come from CI (this dev box has no Win/Mac toolchain) — until then
  those platforms fall back to silence gracefully.

## To do

1. ✅ **Ambient SFX** — ambience (`AmbientHD.sdt`) now loops under the music on a dedicated source
   (T-051). More UI/event sounds beyond the click remain.
2. ✅ **Volume settings UI** — `MusicVolume`/`SfxVolume`/`SpeechVolume` persist to `GameSettings` and are
   exposed as draggable sliders in the F10 `OptionsPanel` (T-051); per-category mixing done (3 buses).
3. ✅ The ModKit `AudioPlayer` decodes with the same minimp3 path — one shared decoder,
   `OpenTPW.Common.MpegDecoder` (2026-10-21); NLayer stays only as its fallback where the native lib isn't built.
4. ✅ **Track selection** — each park world plays its own `Music/MusicHD.sdt` (`LevelSoundscape`); the lobby keeps
   the global `level4c`.
5. ✅ Windows/macOS `libtpwmp3` binaries committed (2026-10-21): `build.sh` cross-compiles them from Linux with zig
   0.13 (`x86_64-windows-gnu` DLL exporting `tpw_mp3_decode`/`tpw_mp3_free`; a universal x86_64 + arm64 dylib joined
   by `fatmacho.py`, the arm64 slice ad-hoc signed by zig's linker). Checked structurally (PE export table, Mach-O
   slices + code signature); a run on real Windows / macOS hardware is still to be confirmed.

## Affected files

`source/OpenTPW/Audio/Audio.cs` (music + SFX), `source/OpenTPW/Audio/native/*` (minimp3 wrapper +
build script + Linux lib), `source/OpenTPW/Client/Game.cs` (lobby music),
`source/OpenTPW/Client/Renderer.cs` (volume keys), `source/OpenTPW/Global/Input.cs` (mouse edge),
`source/OpenTPW/UI/Widgets/PurpleButton.cs` (click SFX), `source/OpenTPW/OpenTPW.csproj` (packages +
native libs), `.github/workflows/native-audio.yml` (cross-platform native build).
