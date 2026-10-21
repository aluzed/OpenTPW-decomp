#!/usr/bin/env python3
"""
ccd-img-to-iso.py — convert a CloneCD raw disc image (.img) to a plain ISO-9660 (.iso).

The Theme Park World assets ship as a CloneCD image (`.ccd`/`.img`/`.sub`, see
docs/03-disc-compatibility.md), whose `.img` stores **raw 2352-byte sectors** (12-byte
sync + 4-byte header + 2048 user data + ECC/EDC), not the 2048-byte user data a
filesystem tool expects. This script deinterleaves the user data out of each sector to
produce a standard `.iso` that `7z`, `fuseiso`, or a loop mount can read — so you can
copy out `DATA/` and point `OPENTPW_GAMEPATH` at it. It is the no-extra-tools
equivalent of `ccd2iso` / `bchunk` (neither of which is always installed).

Usage:
    python3 tools/ccd-img-to-iso.py <input.img> <output.iso>

Example (from the repo's disc archive):
    7z e "iso/jeu-02988-theme_park_world-pcwin.7z" -oiso "Theme Park World/Theme Park World.img"
    python3 tools/ccd-img-to-iso.py "iso/Theme Park World.img" iso/tpw.iso
    7z x iso/tpw.iso Data -oinstall        # install/Data now holds the game assets
    export OPENTPW_GAMEPATH="$PWD/install"   # Game.cs checks for a lowercase 'data':
    ln -s Data install/data                  #   (case-sensitive on Linux)

Supports Mode 1 (user data at offset 16) and Mode 2 Form 1 (offset 24); the mode is
read from each sector's header byte, so mixed tracks convert correctly.
"""

import sys

SECTOR = 2352          # raw CD sector size
USER_DATA = 2048       # ISO-9660 logical block (user data per sector)
SYNC = b"\x00" + b"\xff" * 10 + b"\x00"  # 12-byte raw-sector sync pattern


def data_offset(sector: bytes) -> int:
    """User-data offset within a raw sector, from its mode byte (15)."""
    mode = sector[15]
    if mode == 1:
        return 16           # 12 sync + 3 address + 1 mode
    if mode == 2:
        return 24           # + 8-byte Mode 2 subheader (Form 1)
    raise ValueError(f"unexpected sector mode {mode} (not a Mode 1/2 raw image?)")


def convert(src_path: str, dst_path: str) -> int:
    import os

    size = os.path.getsize(src_path)
    if size % SECTOR != 0:
        sys.exit(f"error: {src_path} size {size} is not a multiple of {SECTOR} "
                 f"— not a raw 2352-byte/sector image.")

    sectors = 0
    with open(src_path, "rb") as src, open(dst_path, "wb") as dst:
        first = src.read(SECTOR)
        if not first.startswith(SYNC):
            sys.exit(f"error: {src_path} does not start with the raw-sector sync "
                     f"pattern — it may already be a 2048-byte .iso (use it directly).")
        src.seek(0)

        while True:
            sector = src.read(SECTOR)
            if len(sector) < SECTOR:
                break
            off = data_offset(sector)
            dst.write(sector[off:off + USER_DATA])
            sectors += 1
            if sectors % 50000 == 0:
                print(f"  ... {sectors} sectors", file=sys.stderr)

    return sectors


def main() -> None:
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, dst = sys.argv[1], sys.argv[2]
    n = convert(src, dst)
    print(f"wrote {dst}: {n} sectors -> {n * USER_DATA} bytes")


if __name__ == "__main__":
    main()
