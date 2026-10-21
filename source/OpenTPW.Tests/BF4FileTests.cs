using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class BF4FileTests
{
	// One glyph block: char code @0, width @16, height @18, 1bpp bitmap @24.
	private static byte[] GlyphBlock( int charCode, int width, int height, byte[] bitmap )
	{
		var block = new byte[24 + bitmap.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 0, 4 ), (uint)charCode );
			BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 12, 4 ), 2 ); // encoding: 1bpp (OneBpp)
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 16, 2 ), (ushort)width );
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 18, 2 ), (ushort)height );
		bitmap.CopyTo( block, 24 );
		return block;
	}

	// Assembles a BF4 (header + offset table + glyph blocks).
	private static byte[] BuildFont( params byte[][] blocks )
	{
		const int headerSize = 8;
		var tableEnd = headerSize + blocks.Length * 4;
		var total = tableEnd + blocks.Sum( b => b.Length );
		var data = new byte[total];

		Encoding.ASCII.GetBytes( "F4FB" ).CopyTo( data, 0 );
		data[4] = 2; // version
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6, 2 ), (ushort)blocks.Length );

		var offset = tableEnd;
		for ( var i = 0; i < blocks.Length; i++ )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( headerSize + i * 4, 4 ), (uint)offset );
			blocks[i].CopyTo( data, offset );
			offset += blocks[i].Length;
		}

		return data;
	}

	[TestMethod]
	public void BearingsAreSignedBytes()
	{
		// A 'j' starting one pixel left of the pen and an accent one pixel above the line: 0xFF / 0xFE on disk.
		var block = GlyphBlock( 'j', 0, 0, Array.Empty<byte>() );
		block[20] = 0xFF;
		block[21] = 0xFE;
		var font = new BF4File( new MemoryStream( BuildFont( block ) ) );
		Assert.AreEqual( -1, font.Glyphs[0].XBearing );
		Assert.AreEqual( -2, font.Glyphs[0].YBearing );
	}

	[TestMethod]
	public void ParsesHeaderAndGlyphCharCodes()
	{
		var bytes = BuildFont(
			GlyphBlock( '*', 0, 0, Array.Empty<byte>() ),
			GlyphBlock( '1', 0, 0, Array.Empty<byte>() ),
			GlyphBlock( 'A', 0, 0, Array.Empty<byte>() ) );

		using var stream = new MemoryStream( bytes );
		var font = new BF4File( stream );

		Assert.AreEqual( 2, font.Version );
		Assert.AreEqual( 3, font.Glyphs.Count );
		CollectionAssert.AreEqual(
			new[] { (int)'*', '1', 'A' },
			font.Glyphs.Select( g => g.CharCode ).ToArray() );
	}

	[TestMethod]
	public void DecodesGlyphBitmap()
	{
		// An 'L', 4x5: bytes 0x88,0x88,0xF0 = rows (4 bits each, MSB-first):
		// 1000 / 1000 / 1000 / 1000 / 1111  → a left bar with a full bottom row.
		var bytes = BuildFont( GlyphBlock( 'L', 4, 5, new byte[] { 0x88, 0x88, 0xF0 } ) );

		using var stream = new MemoryStream( bytes );
		var font = new BF4File( stream );

		var g = font.Glyphs.Single();
		Assert.AreEqual( (int)'L', g.CharCode );
		Assert.AreEqual( 4, g.Width );
		Assert.AreEqual( 5, g.Height );

		string Row( int r ) => string.Concat(
			Enumerable.Range( 0, g.Width ).Select( c => g.Pixels[r * g.Width + c] ? '#' : '.' ) );

		Assert.AreEqual( "#...", Row( 0 ) );
		Assert.AreEqual( "#...", Row( 3 ) );
		Assert.AreEqual( "####", Row( 4 ) );
	}

	[TestMethod]
	public void DecodesRaw4BppAntialiasedGlyph()
	{
		// A 2×2 raw-4bpp glyph: nibbles F,8 / 4,0 (high nibble first) = bytes 0xF8, 0x40.
		// Coverage = nibble×17 → 255,136 / 68,0.
		var block = new byte[24 + 2];
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 0, 4 ), 'A' );
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 12, 4 ), 0 ); // encoding: Raw4Bpp
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 16, 2 ), 2 ); // width
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 18, 2 ), 2 ); // height
		block[24] = 0xF8;
		block[25] = 0x40;

		var font = new BF4File( new MemoryStream( BuildFont( block ) ) );
		var g = font.Glyphs.Single();

		Assert.AreEqual( BF4File.GlyphEncoding.Raw4Bpp, g.Encoding );
		CollectionAssert.AreEqual( new byte[] { 255, 136, 68, 0 }, g.Coverage );
		// Pixels mark any non-zero coverage.
		CollectionAssert.AreEqual( new[] { true, true, true, false }, g.Pixels );
	}

	[TestMethod]
	public void DecodesCompressed4BppGlyph()
	{
		// A 4×2 compressed-4bpp glyph. Nibble-RLE (high nibble first), 0 = run escape:
		//   0,4,F  -> run of 4 × value F  (row 0 = all opaque)
		//   8       -> literal pixel 8
		//   0,3,0   -> run of 3 × value 0  (rest of row 1 transparent)
		//   0,0     -> terminator
		// Nibbles: 0 4 F 8 0 3 0 0 0  -> bytes 04 F8 03 00 0?  (pad the last nibble)
		var block = new byte[24 + 5];
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 0, 4 ), 'A' );
		BinaryPrimitives.WriteUInt32LittleEndian( block.AsSpan( 12, 4 ), 1 ); // encoding: Compressed4Bpp
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 16, 2 ), 4 ); // width
		BinaryPrimitives.WriteUInt16LittleEndian( block.AsSpan( 18, 2 ), 2 ); // height
		block[24] = 0x04; // nibbles 0,4
		block[25] = 0xF8; // nibbles F,8
		block[26] = 0x03; // nibbles 0,3
		block[27] = 0x00; // nibbles 0,0 (terminator)
		block[28] = 0x00;

		var font = new BF4File( new MemoryStream( BuildFont( block ) ) );
		var g = font.Glyphs.Single();

		Assert.AreEqual( BF4File.GlyphEncoding.Compressed4Bpp, g.Encoding );
		// Row 0: 4× F (255); row 1: 8 (136) then 3× 0.
		CollectionAssert.AreEqual( new byte[] { 255, 255, 255, 255, 136, 0, 0, 0 }, g.Coverage );
	}

	[TestMethod]
	public void RejectsBadMagic()
	{
		var bytes = new byte[16];
		Encoding.ASCII.GetBytes( "XXXX" ).CopyTo( bytes, 0 );

		using var stream = new MemoryStream( bytes );
		Assert.ThrowsExactly<InvalidDataException>( () => new BF4File( stream ) );
	}

	// Optional validation against a real font. Set TPW_FONT_SAMPLE to a .BF4 path to run.
	[TestMethod]
	public void ParsesRealFontSample()
	{
		Log = new();

		var path = Environment.GetEnvironmentVariable( "TPW_FONT_SAMPLE" );
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			Assert.Inconclusive( "Set TPW_FONT_SAMPLE to a Theme Park World .BF4 file to run this test." );

		using var stream = File.OpenRead( path );
		var font = new BF4File( stream );

		Assert.IsTrue( font.Glyphs.Count > 0, "a font should contain glyphs" );
		// Note: real fonts can have several glyphs for one char code (e.g. space), so
		// look up the first match rather than building a dictionary.
		var codes = font.Glyphs.Select( g => g.CharCode ).ToHashSet();
		Assert.IsTrue( codes.Contains( '0' ) && codes.Contains( '1' ),
			"font should contain digit glyphs" );

		var zero = font.Glyphs.First( g => g.CharCode == '0' );
		Assert.IsTrue( zero.Width > 0 && zero.Height > 0, "'0' should have real dimensions" );
		Assert.AreEqual( zero.Width * zero.Height, zero.Pixels.Length );
		Assert.IsTrue( zero.Pixels.Any( p => p ), "'0' bitmap should have set pixels" );

		// Proportional advance metrics (confirmed by text rendering with GAME6.BF4).
		Assert.AreEqual( 8, font.Glyphs.First( g => g.CharCode == 'W' ).Advance, "'W' is the widest" );
		Assert.AreEqual( 4, font.Glyphs.First( g => g.CharCode == 'I' ).Advance, "'I' is narrow" );
		Assert.IsTrue( font.Glyphs.First( g => g.CharCode == ' ' ).Advance > 0, "space advances" );
	}
}
