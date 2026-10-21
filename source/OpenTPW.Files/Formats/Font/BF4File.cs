using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Reader for Theme Park World bitmap fonts (<c>.BF4</c>, magic "F4FB").
///
/// No published spec exists; the layout below was reverse-engineered from sample fonts
/// and the container structure is verified by invariants (see <c>BF4FileTests</c>):
///
/// <code>
///   0  : 4   magic "F4FB"
///   4  : 1   version
///   5  : 1   (unknown)
///   6  : 2   glyph count (little-endian uint16)
///   8  : n*4 glyph offset table (uint32 byte offset of each glyph block);
///            the table tiles exactly up to the first glyph offset
///   ...: glyph blocks. Each block is:
///          0  : 4   character code (confirmed, e.g. 42 = '*')
///          4  : 4   line height (constant 8)
///          8  : 4   4bpp uncompressed size (= width*height/2; nominal, not the stored size)
///          12 : 4   encoding tag: 2 = 1bpp, 0 = raw 4bpp (antialiased), 1 = compressed 4bpp
///          16 : 2   glyph width  (little-endian uint16)
///          18 : 2   glyph height (little-endian uint16)
///          20 : 1   x bearing
///          21 : 1   y bearing
///          22 : 2   x advance (cursor step)
///          24 : ... pixel data (see <see cref="GlyphEncoding"/>)
/// </code>
///
/// All of the above were confirmed by rendering: a real font's glyph atlas is legible, and laying glyphs
/// out by their advance produces correctly-spaced text ("GAME OVER"). The 1bpp + raw-4bpp paths render
/// clean masks / antialiased coverage; the compressed-4bpp variant (menu/title faces) is a nibble-RLE
/// over 4bpp values, decoded from the original's font decompressor. See docs/tickets/T-025. No published
/// spec exists; reverse-engineered from sample fonts + tp.exe.
/// </summary>
public sealed class BF4File : BaseFormat
{
	private const string Magic = "F4FB";
	private const int HeaderSize = 8;

	/// <summary>How a glyph's pixel data (at block offset 24) is encoded — the block's offset-12 tag.</summary>
	public enum GlyphEncoding
	{
		/// <summary>Raw 4-bit-per-pixel coverage (antialiased): continuous nibbles, high nibble first.</summary>
		Raw4Bpp = 0,
		/// <summary>Nibble-RLE-compressed 4bpp (the big menu/title faces) — decoded by
		/// <see cref="DecodeCompressed4Bpp"/>.</summary>
		Compressed4Bpp = 1,
		/// <summary>1-bit-per-pixel mask: a continuous MSB-first bitstream, width bits per row.</summary>
		OneBpp = 2,
	}

	public readonly struct Glyph
	{
		/// <summary>The character this glyph represents (the block's leading uint32).</summary>
		public int CharCode { get; init; }

		/// <summary>Glyph width in pixels.</summary>
		public int Width { get; init; }

		/// <summary>Glyph height in pixels.</summary>
		public int Height { get; init; }

		/// <summary>Left side bearing (x offset before drawing the bitmap).</summary>
		public int XBearing { get; init; }

		/// <summary>Top side bearing (y offset within the line).</summary>
		public int YBearing { get; init; }

		/// <summary>Cursor advance after this glyph, in pixels (proportional spacing).</summary>
		public int Advance { get; init; }

		/// <summary>
		/// Row-major pixel mask, <see cref="Width"/> × <see cref="Height"/> (<c>true</c> = any coverage).
		/// Empty for zero-size glyphs (e.g. space).
		/// </summary>
		public bool[] Pixels { get; init; }

		/// <summary>
		/// Row-major per-pixel coverage 0..255 (the antialiased alpha). For a 1bpp glyph this is 0/255; for
		/// a raw-4bpp glyph the 0..15 nibble scaled to 0..255. Same length as <see cref="Pixels"/>.
		/// </summary>
		public byte[] Coverage { get; init; }

		/// <summary>How this glyph's pixel data was encoded (the block's offset-12 tag).</summary>
		public GlyphEncoding Encoding { get; init; }

		/// <summary>The raw glyph block (for fields not yet decoded). See T-025.</summary>
		public byte[] Data { get; init; }
	}

	/// <summary>Header version byte (observed value: 2).</summary>
	public int Version { get; private set; }

	public List<Glyph> Glyphs { get; } = new();

	public BF4File( string path ) => ReadFromFile( path );

	public BF4File( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		var data = ms.ToArray();

		if ( data.Length < HeaderSize )
			throw new InvalidDataException( "BF4: file is smaller than the header." );

		var magic = Encoding.ASCII.GetString( data, 0, 4 );
		if ( magic != Magic )
			throw new InvalidDataException( $"BF4: bad magic '{magic}', expected '{Magic}'." );

		Version = data[4];
		int glyphCount = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6, 2 ) );

		var tableEnd = HeaderSize + glyphCount * 4;
		if ( tableEnd > data.Length )
			throw new InvalidDataException( "BF4: glyph offset table runs past end of file." );

		var offsets = new int[glyphCount];
		for ( var i = 0; i < glyphCount; i++ )
			offsets[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( HeaderSize + i * 4, 4 ) );

		for ( var i = 0; i < glyphCount; i++ )
		{
			var start = offsets[i];
			var end = i + 1 < glyphCount ? offsets[i + 1] : data.Length;

			// Each block must sit after the table and hold at least its 4-byte char code.
			if ( start < tableEnd || end > data.Length || end < start + 4 )
			{
				Log.Warning( $"BF4: glyph {i} has an out-of-range block ({start}..{end}); skipping." );
				continue;
			}

			var charCode = (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( start, 4 ) );
			var block = data[start..end];

			// Width/height @16/18; bearings @20/21; advance @22; bitmap @24.
			var hasMetrics = block.Length >= 24;
			var width = hasMetrics ? BinaryPrimitives.ReadUInt16LittleEndian( block.AsSpan( 16, 2 ) ) : 0;
			var height = hasMetrics ? BinaryPrimitives.ReadUInt16LittleEndian( block.AsSpan( 18, 2 ) ) : 0;
			// Signed: a glyph that starts left of the pen (j, $, digits in the CASH fonts…) stores -1 as 0xFF — read
			// unsigned it was drawn ~255 px to the right (the stray "j" over the HUD).
			var xBearing = hasMetrics ? (sbyte)block[20] : 0;
			var yBearing = hasMetrics ? (sbyte)block[21] : 0;
			var advance = hasMetrics ? BinaryPrimitives.ReadUInt16LittleEndian( block.AsSpan( 22, 2 ) ) : 0;
			var encoding = hasMetrics
				? (GlyphEncoding)BinaryPrimitives.ReadUInt32LittleEndian( block.AsSpan( 12, 4 ) )
				: GlyphEncoding.OneBpp;
			var coverage = DecodeCoverage( block, width, height, encoding );
			var pixels = new bool[coverage.Length];
			for ( var p = 0; p < coverage.Length; p++ )
				pixels[p] = coverage[p] != 0;

			Glyphs.Add( new Glyph
			{
				CharCode = charCode,
				Width = width,
				Height = height,
				XBearing = xBearing,
				YBearing = yBearing,
				Advance = advance,
				Encoding = encoding,
				Pixels = pixels,
				Coverage = coverage,
				Data = block,
			} );
		}
	}

	/// <summary>
	/// Decodes a glyph's pixel data (block offset 24) to row-major coverage 0..255, per its
	/// <see cref="GlyphEncoding"/>. Missing bytes (short block) read as 0.
	/// <list type="bullet">
	/// <item><see cref="GlyphEncoding.Raw4Bpp"/>: continuous 4-bit coverage nibbles (high nibble first),
	///   scaled 0..15 → 0..255 — verified to render clean antialiased glyphs.</item>
	/// <item><see cref="GlyphEncoding.Compressed4Bpp"/>: a nibble-RLE over the same 4bpp values — decoded
	///   then scaled 0..15 → 0..255 (see <see cref="DecodeCompressed4Bpp"/>).</item>
	/// <item><see cref="GlyphEncoding.OneBpp"/>: a continuous MSB-first 1bpp bitstream → 0/255.</item>
	/// </list>
	/// </summary>
	private static byte[] DecodeCoverage( byte[] block, int width, int height, GlyphEncoding encoding )
	{
		const int bitmapOffset = 24;
		var count = width * height;
		var coverage = new byte[count < 0 ? 0 : count];

		switch ( encoding )
		{
			case GlyphEncoding.Raw4Bpp:
				for ( var i = 0; i < coverage.Length; i++ )
				{
					var byteIndex = bitmapOffset + (i >> 1);
					if ( byteIndex >= block.Length )
						break;
					var nibble = (i & 1) == 0 ? block[byteIndex] >> 4 : block[byteIndex] & 0xF;
					coverage[i] = (byte)(nibble * 17); // 0..15 → 0..255
				}
				break;

			case GlyphEncoding.Compressed4Bpp:
				DecodeCompressed4Bpp( block, bitmapOffset, coverage );
				break;

			default: // OneBpp
				for ( var i = 0; i < coverage.Length; i++ )
				{
					var byteIndex = bitmapOffset + (i >> 3);
					if ( byteIndex >= block.Length )
						break;
					if ( ( (block[byteIndex] >> (7 - (i & 7))) & 1 ) != 0 )
						coverage[i] = 255;
				}
				break;
		}

		return coverage;
	}

	/// <summary>
	/// Decodes the compressed-4bpp glyph stream (the big menu/title faces) into <paramref name="coverage"/>
	/// (0..255). RE'd from the original's font decompressor (<c>FUN_006b4aa0</c> in tp.exe): the pixel data
	/// is a stream of 4-bit nibbles (high nibble of each byte first), run-length coded with <c>0</c> as the
	/// escape:
	/// <list type="bullet">
	/// <item>a non-zero nibble <c>c</c> emits one pixel of coverage <c>c</c>;</item>
	/// <item>a <c>0</c> nibble starts a run: the next nibble is the run <c>count</c> (<c>0</c> = end of
	///   glyph), the one after is the pixel <c>value</c>, emitted <c>count</c> times.</item>
	/// </list>
	/// Each 0..15 value is scaled ×17 to 0..255, matching the raw-4bpp path.
	/// </summary>
	private static void DecodeCompressed4Bpp( byte[] block, int bitmapOffset, byte[] coverage )
	{
		var nibblePos = bitmapOffset * 2;
		var totalNibbles = block.Length * 2;

		int NextNibble()
		{
			if ( nibblePos >= totalNibbles )
				return -1;
			var byteIndex = nibblePos >> 1;
			var v = (nibblePos & 1) == 0 ? block[byteIndex] >> 4 : block[byteIndex] & 0xF;
			nibblePos++;
			return v;
		}

		var p = 0;
		while ( p < coverage.Length )
		{
			var c = NextNibble();
			if ( c < 0 )
				break;
			if ( c != 0 )
			{
				coverage[p++] = (byte)(c * 17);
				continue;
			}

			var count = NextNibble();
			if ( count <= 0 ) // 0 (or end-of-stream) terminates the glyph
				break;
			var value = NextNibble();
			if ( value < 0 )
				break;
			var scaled = (byte)(value * 17);
			for ( var r = 0; r < count && p < coverage.Length; r++ )
				coverage[p++] = scaled;
		}
	}
}
