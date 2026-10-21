# T-025 — BF4: decode the antialiased (multi-bit) font variant

- **Priority**: 🟢 Low (1bpp fonts work; AA fonts are a visual upgrade)
- **Type**: File format / reverse engineering
- **Status**: ✅ Done — both antialiased variants decode: **raw-4bpp** (`*AA` faces) and **compressed-4bpp**
  (the big menu/title faces). The UI's `PurpleButton`s now use the intended `MENUMED` face (verified
  in-game: "Create New Player" / "Quit Game" render smooth antialiased, not the 1bpp GAME12 stand-in).
- **Blocks**: ~~nicer UI typography~~ (delivered).

## Solved: the encoding is in the glyph block (offset 12)

The glyph block's **offset-12 field is the encoding tag** (not a constant): `2` = 1bpp, `0` = **raw 4bpp**
(antialiased), `1` = **compressed 4bpp**. (Offset 8 is `width*height/2`, the 4bpp *uncompressed* size, not
the stored byte count — which is why the byte-ratio heuristic below was noisy.)

- **Raw 4bpp (tag 0) — done.** Continuous 4-bit coverage nibbles, high nibble first, scaled 0..15→0..255.
  Verified by rendering: `GAME6AA`/`GAME9AA`/`TITLESMALL`/… 'A','B','H' come out as clean antialiased
  glyphs. `BF4File` now exposes `GlyphEncoding` + per-pixel `Coverage`, and `FontAtlas` blits coverage as
  the alpha channel (1bpp glyphs stay 0/255). Covers the `*AA` faces, the `DATE*`/`SESHSMALL`/`TITLESMALL`
  /`GAMEBOLD9/10` faces. Unit-tested (`DecodesRaw4BppAntialiasedGlyph`).
- **Compressed 4bpp (tag 1) — done.** The big menu/title faces (`MENU*`, `TITLE*BIG/MED`, `CASH*`,
  `SESH*`, `MATISSE*`, `GAME12AA`) store fewer bytes than `width*height/2` because the 4bpp pixels are
  **nibble-RLE compressed**. Decoded by `BF4File.DecodeCompressed4Bpp`, unit-tested
  (`DecodesCompressed4BppGlyph`) and verified by rendering real `MENUMED` glyphs ('1','H','E','M' come out
  crisp). The scheme (a stream of 4-bit nibbles, high nibble first; `0` is a run escape):
  - a non-zero nibble `c` → one pixel of coverage `c`;
  - a `0` nibble → run: next nibble = `count` (`0` ends the glyph), next = `value`, emitted `count` times.

  RE'd from tp.exe: the loader `FUN_006b0680` checks the `0x42463446` ("F4FB") magic then constructs the
  font (`FUN_006b0480`, vtable `0x70a158`). The two glyph **blitters** (`FUN_006b0760` RGB555 /
  `FUN_006b0c50` RGB565) alpha-blend raw 4bpp into the framebuffer; the decompressor is the on-demand
  glyph-prep `FUN_006b4aa0`, which reads the encoding flag at block offset 12, allocates a scratch buffer
  sized by block offset 8 (`width*height/2`), and runs the nibble-RLE loop above
  (`FUN_006b54c0`/`54a0` = the nibble reader, `FUN_006b5450` = the nibble writer).

## Problem

`BF4File`/`FontAtlas` decode the **1bpp** glyph variant only: a continuous MSB-first bitmap,
`width` bits per row. Several shipped fonts are **antialiased / multi-bit** and the 1bpp reader
turns their glyph data into noise (garbled text).

Detection rule observed on `Language/English/*.bf4` (compare the glyph's bitmap byte count to the
1bpp expectation `ceil(width*height/8)` for `'A'`):

| Font | 'A' w×h | bitmap bytes | 1bpp expects | ratio | kind |
|------|---------|--------------|--------------|-------|------|
| GAME12 | 10×12 | 16 | 15 | 1.1 | **1bpp ✓** |
| GAME10 | 7×10 | 16 | 9 | 1.8 | multi-bit |
| MENUMED | 18×29 | 152 | 66 | 2.3 | multi-bit |
| MENUSMALL | 15×23 | 128 | 44 | 2.9 | multi-bit |
| GAMEBOLD12 | 11×12 | 56 | 17 | 3.3 | multi-bit |

So `GAME12` (and the other ratio-~1 `GAME*`/`CONSOLE*` fonts) decode cleanly; `MENU*`, `TITLE*`,
the `*AA` variants and the bold faces do not. The current UI therefore uses `GAME12` everywhere
text is drawn (loading screen, `PurpleButton` labels).

## Done

1. ✅ Determined the encoding: 4bpp coverage, raw (tag 0) or **nibble-RLE** (tag 1), tag at block
   offset 12 — cross-checked against the font decompressor `FUN_006b4aa0` in tp.exe.
2. ✅ Coverage is decoded into the `FontAtlas` alpha channel (AA glyphs write a 0..255 gradient; 1bpp
   stays 0/255).
3. ✅ The UI's `PurpleButton` labels now use the intended `MENUMED` menu face (HUD body text stays on the
   compact 1bpp `GAME12`, which suits the dense readout).

## Affected files

`source/OpenTPW.Files/Formats/Font/BF4File.cs` (`DecodeBitmap`),
`source/OpenTPW.Files/Formats/Font/FontAtlas.cs` (alpha blit),
`source/OpenTPW/UI/Widgets/PurpleButton.cs` (font choice).

## 2026-10-21 — bearings are signed

The glyph bearings (block bytes 20/21) are **signed** bytes: across the shipped fonts many glyphs start left of
the pen or above the line (`j` = -1…-11, `$`, the digits of the CASH fonts, accented capitals → 0xFF, 0xFE…). Read
unsigned, such a glyph was drawn ~255 px away — the stray "j" floating over the in-park HUD while "adjust" read
"ad ust". `BF4File` now casts them to `sbyte`; unit-tested (`BearingsAreSignedBytes`), verified on the HUD.
