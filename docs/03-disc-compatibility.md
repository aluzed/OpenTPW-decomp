# 03 — Is the provided `.7z` compatible with OpenTPW?

**Short answer: YES.** It is the original Theme Park World installation CD, and it
contains exactly the assets OpenTPW reads.

## Archive contents

`jeu-02988-theme_park_world-pcwin.7z` (563 MiB):

```
Theme Park World/
  Theme Park World.ccd   (772 B)            ← CloneCD control file
  Theme Park World.img   (781 MB / 746 MiB) ← raw disc image
  Theme Park World.sub   (31 MB)            ← subchannels
Lisez-Moi.txt            (6 KB)             ← Abandonware-France notice (FR)
```

It is **not an `.iso`** but a **CloneCD image** (`.ccd`/`.img`/`.sub`).
Source: Abandonware-France (game #02988, FR PC release).

## Disc analysis

- **Type**: **data** CD-ROM, single session, 1 data track
  (`.ccd` → `Control=0x04`). Raw 2352-byte sectors (Mode 1, data at offset 16).
- **ISO9660 volume label**: `TPWorld`.
- **Root**: a classic Bullfrog/EA installer — `SETUP.EXE`, `AUTORUN.EXE`,
  `TP.EXE` + `TP.ICD`, language files (`FRENCH`, `GERMAN`, `ENGLISH`, `DANISH`,
  `SWEDISH`), help (`FRHELP.HLP`…), and most importantly the **`DATA/`** folder.

### `DATA/` — the assets OpenTPW needs

```
DATA/
  ESPRITES.WAD (10 MB)  FONTS.WAD     LOBBY.WAD     UI.WAD
  *.SAM (HIGH/MED/LOW/SOUND/ONLINE/CHALLENGE…)
  2DMAP/  ADVISOR/  GENERIC/  GLOBAL/{SOUND,SPEECH}  INIT/{400..1024}
  LANGUAGE/ENGLISH/
  LEVELS/{JUNGLE, FANTASY, HALLOW, SPACE}  + STANDARD.SAM, ONLINE.SAM
  MOVIES/*.TGQ (videos)  PARTICLE/*.PLB  POSTCARD/*.TGA  UI/CURSORS
```

This is **exactly** what the code expects:
- `Client/Game.cs` mounts `GamePath/data/` and registers the `.WAD` handler.
- `World/Terrain/Terrain.cs` reads `levels/jungle/terrain/textures/...wct`.
- `World/Level.cs` reads `/levels/{name}/global.sam`.

→ **The disc is a 100% correct asset source.**

## How to use it

The `DATA/` folder is **directly readable** on the disc (not packed inside a
compressed installer). Two paths:

### Path A — Extract / mount the image (recommended, no Windows)
1. Extract the `.img` from the archive:
   `7z e "iso/jeu-02988-theme_park_world-pcwin.7z" -oiso "Theme Park World/Theme Park World.img"`
2. Convert the raw image (Mode 1, 2352-byte sectors) to a plain 2048-byte `.iso`, then
   copy out `DATA/`:
   ```bash
   python3 tools/ccd-img-to-iso.py "iso/Theme Park World.img" iso/tpw.iso
   7z x iso/tpw.iso Data -oinstall          # install/Data holds the assets
   ```
   (`tools/ccd-img-to-iso.py` is the bundled, no-extra-tools equivalent of
   `ccd2iso`/`bchunk`; `7z`/`fuseiso` can't read the raw `.img` directly.)
3. Point OpenTPW's **`GamePath`** at the folder containing `data/` (here `install/`):
   - `export OPENTPW_GAMEPATH="$PWD/install"` (overrides the setting — see [T-006](tickets/T-006-gamepath-config.md)).
   - ⚠️ On Linux the initial check (`Game.cs`) looks for a **lowercase** `data`, while the
     disc has `Data` — add `ln -s Data install/data` (the runtime VFS is already
     case-insensitive). Mind the hardcoded `\` separators too — see [04](04-linux-compatibility.md).
   - Default (Windows): `C:\Program Files (x86)\Bullfrog\Theme Park World` (`Settings.Designer.cs`).
4. **Language (optional)** — the disc carries one folder per language at its root (`French/`, `German/`, `Danish/`,
   `Swedish/`, `Dutch/`…), each mirroring `data/` with that language's strings, fonts and advisor voice. Extract
   the one you want next to `Data` and select it:
   ```bash
   7z x iso/tpw.iso French/data -oinstall      # → install/French/data (26 MB)
   export OPENTPW_LANGUAGE=French              # or "Language": "French" in .opentpw/settings.json
   ```
   OpenTPW reads it as an overlay over `data/` (`BaseFileSystem.Overlay`, `GameLanguage`): menus, theme/staff/loan
   names, visitor thoughts and the advisor's voice switch language; OpenTPW's own HUD labels stay English.

### Path B — Classic install (Windows or Wine)
Run `SETUP.EXE` (via Wine on Linux). The installer copies `DATA/` and creates the
long/lowercase filenames expected by the original game. **No need for the SafeDisc
protection** since we never launch `TP.EXE`.

## Caveat: case & 8.3 names

On the CD (ISO9660), names are **UPPERCASE 8.3** (`ESPRITES.WAD`, `CHALLE~1.SAM`).
OpenTPW references **lowercase** paths (`levels/jungle/...`). On a case-sensitive
Linux FS, plan for normalization (Wine install, or rename to lowercase after extraction).

## Conclusion

| Question | Answer |
|----------|--------|
| Is it the right game? | ✅ Yes — Theme Park World, label `TPWorld`. |
| Does it contain the required assets? | ✅ Yes — `DATA/` with `.WAD`/`.SAM`/`LEVELS`. |
| Directly usable format? | ⚠️ CloneCD image → extract/mount first. |
| Need to bypass SafeDisc? | ❌ No — we never run `TP.EXE`. |
