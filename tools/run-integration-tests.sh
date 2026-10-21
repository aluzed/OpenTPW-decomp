#!/usr/bin/env bash
#
# Run the test suite against the real game data, so the integration tests that
# are otherwise Inconclusive (TPW_*_SAMPLE / OPENTPW_GAMEPATH) actually execute.
#
# Expects the extracted game in install/ (see docs/03-disc-compatibility.md).
# Samples that only exist inside a WAD or outside Data/ on the disc are
# extracted once into install/.samples (git-ignored, like install/).
#
# Usage:
#   tools/run-integration-tests.sh [extra dotnet test args]
#
# Overrides: OPENTPW_GAMEPATH (default: <repo>/install), TPW_ISO (default: <repo>/iso/tpw.iso).
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SLN="$REPO_ROOT/source/OpenTPW.sln"

# --- Locate the .NET SDK (PATH first, then a user-local install) ---
if command -v dotnet >/dev/null 2>&1; then
  DOTNET="$(command -v dotnet)"
elif [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
  export DOTNET_ROOT="$HOME/.dotnet"
else
  echo "error: 'dotnet' not found. Install the .NET 8 SDK — see docs/Linux.md." >&2
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

export OPENTPW_GAMEPATH="${OPENTPW_GAMEPATH:-$REPO_ROOT/install}"
DATA="$OPENTPW_GAMEPATH/Data"
[ -d "$DATA" ] || DATA="$OPENTPW_GAMEPATH/data"
if [ ! -d "$DATA" ]; then
  echo "error: no game data under $OPENTPW_GAMEPATH — extract it first (docs/03-disc-compatibility.md)." >&2
  exit 1
fi
TPW_ISO="${TPW_ISO:-$REPO_ROOT/iso/tpw.iso}"
SAMPLES="$OPENTPW_GAMEPATH/.samples"
mkdir -p "$SAMPLES"

echo ">> building"
"$DOTNET" build "$SLN" -nologo -v q

# --- Samples inside WAD archives: extracted with the repo's own wadtool ---
WADTOOL="$REPO_ROOT/source/OpenTPW.WadTool/bin/Debug/net8.0/wadtool"
if [ ! -f "$SAMPLES/monkey/monkeym1.MD2" ]; then
  "$WADTOOL" "$DATA/levels/jungle/rides/monkey.wad" -x "$SAMPLES/monkey" >/dev/null
fi
if [ ! -f "$SAMPLES/terrain/base.lnd" ]; then
  "$WADTOOL" "$DATA/levels/jungle/terrain.wad" -x "$SAMPLES/terrain" >/dev/null
fi

# --- Samples outside Data/ on the disc: .MTR files live in the language folders ---
SEVENZIP="$(command -v 7zz || command -v 7z || true)"
if [ ! -f "$SAMPLES/English/Meshes/English/bankrupt.mtr" ]; then
  if [ -n "$SEVENZIP" ] && [ -f "$TPW_ISO" ]; then
    "$SEVENZIP" x "$TPW_ISO" "English/Meshes/English/bankrupt.mtr" -o"$SAMPLES" -y >/dev/null
  else
    echo "note: no 7z or no $TPW_ISO — the .MTR test will stay Inconclusive." >&2
  fi
fi

# Set a sample only when the file exists, so a missing one stays Inconclusive.
sample() {
  if [ -f "$2" ]; then export "$1=$2"; else echo "note: $1 missing ($2)" >&2; fi
}
sample TPW_FONT_SAMPLE     "$DATA/Language/English/GAME6.bf4"   # the advance asserts are GAME6's metrics
sample TPW_VIDEO_SAMPLE    "$DATA/Movies/bf.tgq"
sample TPW_MODEL_SAMPLE    "$DATA/generic/dynamic/garrow.MD2"
sample TPW_MAP_SAMPLE      "$DATA/global/Speech/cat_speechSFX.map"
sample TPW_PLB_SAMPLE      "$DATA/Particle/Tp2.plb"
sample TPW_LIP_SAMPLE      "$DATA/levels/jungle/Speech/lips/sp_001.LIP"
sample TPW_HMP_WAD         "$DATA/levels/jungle/rides/coaster1.wad"
sample TPW_KEYFRAME_SAMPLE "$SAMPLES/monkey/monkeym1.MD2"
sample TPW_MTR_SAMPLE      "$SAMPLES/English/Meshes/English/bankrupt.mtr"
sample TPW_LND_SAMPLE      "$SAMPLES/terrain/base.lnd"
# TPW_SAVE_SAMPLE: no original .TPWS save ships on the disc (T-017) — stays Inconclusive.

echo ">> testing against $OPENTPW_GAMEPATH"
"$DOTNET" test "$SLN" --no-build "$@"
