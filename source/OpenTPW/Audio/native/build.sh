#!/usr/bin/env bash
# Builds the minimp3 wrapper (tpwmp3) into a native lib per platform. Each platform's output goes in
# its own folder and is picked up by OpenTPW.csproj (copied next to the app; loaded via DllImport
# "tpwmp3"). Only the toolchains present are used — missing ones are skipped (build them on the
# native OS or via CI: .github/workflows/native-audio.yml). See docs/tickets/T-031-game-audio.md.
set -e
cd "$(dirname "$0")"
mkdir -p linux-x64 win-x64 osx

# Linux (.so)
if command -v gcc >/dev/null 2>&1; then
	gcc -O2 -fPIC -shared tpwmp3.c -o linux-x64/libtpwmp3.so -lm
	echo "built linux-x64/libtpwmp3.so"
else
	echo "skip linux-x64: gcc not found"
fi

# zig (https://ziglang.org, a self-contained tarball) cross-compiles both the Windows DLL and the macOS dylibs
# from Linux; set ZIG to its binary.
ZIG=${ZIG:-$(command -v zig || true)}
if [ -n "$ZIG" ]; then
	"$ZIG" cc -target x86_64-windows-gnu -O2 -shared tpwmp3.c -o win-x64/tpwmp3.dll
	rm -f win-x64/tpwmp3.lib win-x64/tpwmp3.pdb
	echo "built win-x64/tpwmp3.dll (zig)"
	"$ZIG" cc -target x86_64-macos -O2 -dynamiclib tpwmp3.c -o osx/x64.dylib
	"$ZIG" cc -target aarch64-macos -O2 -dynamiclib tpwmp3.c -o osx/arm64.dylib # ad-hoc signed by zig's linker
	python3 fatmacho.py osx/libtpwmp3.dylib osx/x64.dylib osx/arm64.dylib && rm osx/x64.dylib osx/arm64.dylib
	echo "built osx/libtpwmp3.dylib (zig, universal x86_64 + arm64)"
	exit 0
fi

# Windows (.dll) via mingw-w64 cross-compiler
if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
	x86_64-w64-mingw32-gcc -O2 -shared tpwmp3.c -o win-x64/tpwmp3.dll
	echo "built win-x64/tpwmp3.dll"
else
	echo "skip win-x64: x86_64-w64-mingw32-gcc not found (apt install mingw-w64, or use CI)"
fi

# macOS (.dylib): build natively on macOS (clang) or cross via osxcross (o64-clang).
if command -v o64-clang >/dev/null 2>&1; then
	o64-clang -O2 -dynamiclib tpwmp3.c -o osx/libtpwmp3.dylib && echo "built osx/libtpwmp3.dylib (osxcross)"
elif [ "$(uname)" = "Darwin" ] && command -v clang >/dev/null 2>&1; then
	clang -O2 -dynamiclib tpwmp3.c -o osx/libtpwmp3.dylib && echo "built osx/libtpwmp3.dylib"
else
	echo "skip osx: no macOS toolchain (build on macOS or via CI)"
fi
