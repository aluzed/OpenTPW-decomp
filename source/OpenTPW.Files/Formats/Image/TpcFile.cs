using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Decodes a Theme Park World sprite image (<c>.TPC</c> / <c>.FPC</c> from <c>esprites.wad</c>).
/// Format reverse-engineered from the original loader (see docs/tickets/T-034):
/// <code>
///   u16 fmt (=3), u16 (=3), u32 frameCount
///   if fmt==3:  byte palette[1024]            // 256 entries, stored B,G,R,A
///   per frame:  u32 dataLen, u16 w, u16 h, u16, u16, s32 hotspotX, s32 hotspotY
///               byte data[dataLen]            // h RLE scanlines
/// </code>
/// The hotspot is the frame top-left as a signed pixel offset from the sprite's anchor (feet), image X
/// right / Y down — so a pixel at frame-local (px,py) sits at anchor-relative (hotspotX+px, hotspotY+py).
/// <para>Each scanline is one outer command — a byte length, then that many <b>inner</b> bytes — that expands
/// to exactly <c>width</c> pixels, PackBits-style: a control byte <c>c ≥ 0x80</c> repeats the <b>next</b> byte
/// (a palette index) <c>256 − c</c> times (so <c>FE C5</c> = two pixels of index 0xC5, <c>FC 00</c> = four
/// transparent ones); <c>c &lt; 0x80</c> is a literal run of the <c>c</c> palette indices that follow. Index 0 is
/// transparent. Checked against every scanline of the kid and balloon sheets (6 417 rows, all exactly
/// <c>width</c> pixels). Frames are decoded to straight RGBA. See docs/tickets/T-034.</para>
/// </summary>
public sealed class TpcFile
{
	public sealed class Frame
	{
		public int Width { get; init; }
		public int Height { get; init; }
		public int HotspotX { get; init; }
		public int HotspotY { get; init; }
		/// <summary>Straight RGBA, row-major, <c>Width*Height*4</c> bytes (alpha 0 = transparent).</summary>
		public byte[] Rgba { get; init; } = System.Array.Empty<byte>();
	}

	public int FrameCount => Frames.Count;
	public IReadOnlyList<Frame> Frames { get; }

	public TpcFile( Stream stream )
	{
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		Frames = Parse( ms.ToArray() );
	}

	public TpcFile( byte[] data ) => Frames = Parse( data );

	private static List<Frame> Parse( byte[] d )
	{
		var frames = new List<Frame>();
		int p = 0;
		ushort fmt = BinaryPrimitives.ReadUInt16LittleEndian( d.AsSpan( p ) ); p += 2;
		p += 2; // second u16 (also 3)
		uint frameCount = BinaryPrimitives.ReadUInt32LittleEndian( d.AsSpan( p ) ); p += 4;

		// 256-entry BGRA palette (only present for fmt 3).
		byte[]? palette = null;
		if ( fmt == 3 )
		{
			palette = d.AsSpan( p, 1024 ).ToArray();
			p += 1024;
		}

		for ( int i = 0; i < frameCount && p + 20 <= d.Length; i++ )
		{
			uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian( d.AsSpan( p ) );
			ushort w = BinaryPrimitives.ReadUInt16LittleEndian( d.AsSpan( p + 4 ) );
			ushort h = BinaryPrimitives.ReadUInt16LittleEndian( d.AsSpan( p + 6 ) );
			// hotspot = signed top-left offset from the anchor, s32 at frame-header offsets 12/16
			// (the previous u16 @0x14/0x16 read into the pixel data — those values were never used).
			int hotspotX = BinaryPrimitives.ReadInt32LittleEndian( d.AsSpan( p + 12 ) );
			int hotspotY = BinaryPrimitives.ReadInt32LittleEndian( d.AsSpan( p + 16 ) );
			p += 20;
			int pixStart = p;
			p += (int)dataLen;

			frames.Add( new Frame
			{
				Width = w,
				Height = h,
				HotspotX = hotspotX,
				HotspotY = hotspotY,
				Rgba = (w == 0 || h == 0 || palette == null)
					? System.Array.Empty<byte>()
					: DecodeFrame( d, pixStart, (int)dataLen, w, h, palette ),
			} );
		}

		return frames;
	}

	// Decodes one frame's RLE scanlines into straight RGBA via the BGRA palette. Each scanline is one outer
	// command — a byte length, then that many inner bytes — expanding to exactly `width` pixels: a control
	// byte c ≥ 0x80 repeats the next palette index 256−c times; c < 0x80 is a literal run of c indices.
	// (The earlier reading — c ≥ 0xF0 skips transparent pixels without a value byte — only held for runs of
	// index 0, and turned every coloured run into a long bogus literal: the noisy, streaked peep sprites.)
	private static byte[] DecodeFrame( byte[] d, int off, int len, int w, int h, byte[] palette )
	{
		var rgba = new byte[w * h * 4];
		int q = off;
		int end = off + len;

		for ( int row = 0; row < h && q < end; row++ )
		{
			int rowLen = d[q++];                 // outer command: inner bytes for this scanline
			int rowEnd = Math.Min( q + rowLen, end );
			int x = 0;
			while ( x < w && q < rowEnd )
			{
				byte c = d[q++];
				if ( c >= 0x80 )                 // run: the next index, repeated 256 − c times
				{
					if ( q >= rowEnd )
						break;
					byte index = d[q++];
					for ( int j = 0; j < 256 - c && x < w; j++ )
						WritePixel( rgba, (row * w + x++) * 4, index, palette );
				}
				else                             // literal run of c palette indices
				{
					for ( int j = 0; j < c && x < w && q < rowEnd; j++ )
						WritePixel( rgba, (row * w + x++) * 4, d[q++], palette );
				}
			}
			q = rowEnd;                          // resync to the next scanline
		}

		return rgba;
	}

	// Palette index → straight RGBA (palette is B,G,R,A); index 0 is fully transparent.
	private static void WritePixel( byte[] rgba, int o, byte index, byte[] palette )
	{
		if ( index == 0 )
			return; // leave transparent (already zeroed)
		int b = index * 4;
		rgba[o] = palette[b + 2];     // R
		rgba[o + 1] = palette[b + 1]; // G
		rgba[o + 2] = palette[b];     // B
		rgba[o + 3] = 255;            // opaque
	}
}
