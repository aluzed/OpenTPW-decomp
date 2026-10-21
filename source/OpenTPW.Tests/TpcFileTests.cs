using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace OpenTPW.Tests;

[TestClass]
public class TpcFileTests
{
	// Builds a minimal fmt-3 .TPC: header + palette + one 33×2 frame whose two scanlines are the exact RLE
	// examples verified in T-034 (skip-transparent markers ≥0xF0 + literal runs), with index 1 = opaque.
	private static byte[] BuildSample()
	{
		var b = new List<byte>();
		void U16( int v ) { b.Add( (byte)(v & 0xFF) ); b.Add( (byte)((v >> 8) & 0xFF) ); }
		void U32( int v ) { U16( v & 0xFFFF ); U16( (v >> 16) & 0xFFFF ); }

		U16( 3 ); U16( 3 ); U32( 1 ); // fmt=3, second u16, frameCount=1

		// 256-entry BGRA palette: index 0 transparent, index 1 = B=10, G=20, R=30, A=255.
		var pal = new byte[1024];
		pal[4] = 10; pal[5] = 20; pal[6] = 30; pal[7] = 255;
		b.AddRange( pal );

		// Two scanlines (width 33): the verified examples from T-034.
		byte[] row1 = { 0xF2, 0x00, 0x05, 1, 1, 1, 1, 1, 0xF2, 0x00 };                  // skip14, lit5, skip14
		byte[] row2 = { 0xFC, 0x00, 0x07, 1, 1, 1, 1, 1, 1, 1,                          // skip4, lit7
						0xFE, 0x00, 0x0A, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,                  // skip2, lit10
						0xF6, 0x00 };                                                    // skip10
		var data = new List<byte> { (byte)row1.Length };
		data.AddRange( row1 );
		data.Add( (byte)row2.Length );
		data.AddRange( row2 );

		// Frame header: dataLen, w, h, pad, pad, hotspotX, hotspotY.
		U32( data.Count ); U16( 33 ); U16( 2 ); U16( 0 ); U16( 0 ); U32( 0 ); U32( 0 );
		b.AddRange( data );
		return b.ToArray();
	}

	[TestMethod]
	public void DecodesSkipTransparentAndLiteralRuns()
	{
		var tpc = new TpcFile( BuildSample() );
		Assert.AreEqual( 1, tpc.FrameCount );
		var f = tpc.Frames[0];
		Assert.AreEqual( 33, f.Width );
		Assert.AreEqual( 2, f.Height );

		bool Opaque( int x, int y ) => f.Rgba[(y * 33 + x) * 4 + 3] == 255;

		// Row 0: skip14 (0-13 transparent) · 5 literals (14-18 opaque) · skip14 (19-32 transparent).
		for ( int x = 0; x < 14; x++ ) Assert.IsFalse( Opaque( x, 0 ), $"r0 x{x} transparent" );
		for ( int x = 14; x < 19; x++ ) Assert.IsTrue( Opaque( x, 0 ), $"r0 x{x} opaque" );
		for ( int x = 19; x < 33; x++ ) Assert.IsFalse( Opaque( x, 0 ), $"r0 x{x} transparent" );

		// The literal colour resolves through the palette (index 1 → R=30, G=20, B=10).
		int o = 14 * 4;
		Assert.AreEqual( 30, f.Rgba[o] );     // R
		Assert.AreEqual( 20, f.Rgba[o + 1] ); // G
		Assert.AreEqual( 10, f.Rgba[o + 2] ); // B

		// Row 1: skip4 · lit7 (4-10) · skip2 (11-12) · lit10 (13-22) · skip10 (23-32).
		for ( int x = 0; x < 4; x++ ) Assert.IsFalse( Opaque( x, 1 ) );
		for ( int x = 4; x < 11; x++ ) Assert.IsTrue( Opaque( x, 1 ) );
		for ( int x = 11; x < 13; x++ ) Assert.IsFalse( Opaque( x, 1 ) );
		for ( int x = 13; x < 23; x++ ) Assert.IsTrue( Opaque( x, 1 ) );
		for ( int x = 23; x < 33; x++ ) Assert.IsFalse( Opaque( x, 1 ) );
	}

	[TestMethod]
	public void ARunRepeatsTheFollowingIndex()
	{
		// One 16-pixel row from the real balloon sprite (SPR_BL frame 0, row 0): FC 00 = 4 transparent,
		// 03 81 BA 58 = 3 literals, FE C5 = index 0xC5 twice, 03 2B 66 14 = 3 literals, FC 00 = 4 transparent.
		var b = new List<byte>();
		void U16( int v ) { b.Add( (byte)(v & 0xFF) ); b.Add( (byte)((v >> 8) & 0xFF) ); }
		void U32( int v ) { U16( v & 0xFFFF ); U16( (v >> 16) & 0xFFFF ); }
		U16( 3 ); U16( 3 ); U32( 1 );
		var pal = new byte[1024];
		for ( int i = 1; i < 256; i++ ) { pal[i * 4] = (byte)i; pal[i * 4 + 3] = 255; } // B = index
		b.AddRange( pal );
		byte[] row = { 0xFC, 0x00, 0x03, 0x81, 0xBA, 0x58, 0xFE, 0xC5, 0x03, 0x2B, 0x66, 0x14, 0xFC, 0x00 };
		U32( row.Length + 1 ); U16( 16 ); U16( 1 ); U16( 0 ); U16( 0 ); U32( 0 ); U32( 0 );
		b.Add( (byte)row.Length );
		b.AddRange( row );

		var f = new TpcFile( b.ToArray() ).Frames[0];
		int?[] expected = { null, null, null, null, 0x81, 0xBA, 0x58, 0xC5, 0xC5, 0x2B, 0x66, 0x14, null, null, null, null };
		for ( int x = 0; x < 16; x++ )
		{
			int o = x * 4;
			if ( expected[x] is { } index )
			{
				Assert.AreEqual( 255, f.Rgba[o + 3], $"x{x} opaque" );
				Assert.AreEqual( index, f.Rgba[o + 2], $"x{x} index" ); // B channel carries the index
			}
			else
				Assert.AreEqual( 0, f.Rgba[o + 3], $"x{x} transparent" );
		}
	}
}
