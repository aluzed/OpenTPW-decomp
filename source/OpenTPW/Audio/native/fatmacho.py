"""Join single-architecture Mach-O files into a universal ("fat") one, like lipo -create.

Usage: fatmacho.py OUT IN...   (each IN a thin x86_64 or arm64 Mach-O)
"""
import struct
import sys

ALIGN = 14  # 16 KiB slices, as lipo aligns arm64

out, inputs = sys.argv[1], sys.argv[2:]
slices = []
for path in inputs:
    data = open(path, "rb").read()
    cputype, cpusubtype = struct.unpack_from("<ii", data, 4)
    slices.append((cputype, cpusubtype, data))

offset = 1 << ALIGN
header = struct.pack(">II", 0xCAFEBABE, len(slices))
placed = []
for cputype, cpusubtype, data in slices:
    header += struct.pack(">iiIII", cputype, cpusubtype & 0x00FFFFFF, offset, len(data), ALIGN)
    placed.append((offset, data))
    offset = (offset + len(data) + (1 << ALIGN) - 1) & ~((1 << ALIGN) - 1)

blob = bytearray(offset)
blob[: len(header)] = header
for start, data in placed:
    blob[start : start + len(data)] = data
open(out, "wb").write(bytes(blob))
