using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class LndFileTests
{
	// Image record: u32 width, u32 height, u32 bytesPerPixel, then the pixels.
	private static void Image( List<byte> d, int w, int h, int bpp, Func<int, byte> pixel )
	{
		d.AddRange( BitConverter.GetBytes( (uint)w ) );
		d.AddRange( BitConverter.GetBytes( (uint)h ) );
		d.AddRange( BitConverter.GetBytes( (uint)bpp ) );
		for ( int i = 0; i < w * h * bpp; i++ )
			d.Add( pixel( i ) );
	}

	// A minimal .LND: header + a 4×2 ARGB blend map + 3 detail levels of 6 ARGB textures and 7 masks each.
	// Texture n (on every level) is a flat colour R = 10n, G = 20 + n, B = 30 + n.
	private static byte[] BuildLnd( Func<int, byte>? map = null, int? truncateBy = null )
	{
		var d = new List<byte> { 3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
		Image( d, 4, 2, 4, map ?? (i => (byte)(i % 4 == 0 ? 255 : i % 4 == 2 ? 255 : 0)) ); // opaque, all grass
		for ( int level = 0; level < LndFile.LevelCount; level++ )
		{
			d.AddRange( BitConverter.GetBytes( (uint)(64 >> level) ) );
			d.Add( (byte)level );
			for ( int t = 0; t < LndFile.TexturesPerLevel; t++ )
			{
				int n = t;
				Image( d, 2, 2, 4, i => (i % 4) switch { 0 => 0, 1 => (byte)(10 * n), 2 => (byte)(20 + n), _ => (byte)(30 + n) } );
			}
			for ( int m = 0; m < LndFile.MasksPerLevel; m++ )
				Image( d, 3, 1, 1, i => (byte)(m + i) );
		}
		var bytes = d.ToArray();
		return truncateBy is int t2 ? bytes[..^t2] : bytes;
	}

	[TestMethod]
	public void ParsesSyntheticLayout()
	{
		var lnd = new LndFile( new MemoryStream( BuildLnd() ) );

		Assert.AreEqual( 3, lnd.Header[0] );
		Assert.AreEqual( 4, lnd.BlendMap.Width );
		Assert.AreEqual( 2, lnd.BlendMap.Height );
		Assert.AreEqual( 4, lnd.BlendMap.BytesPerPixel );
		Assert.AreEqual( 3, lnd.Levels.Count );
		CollectionAssert.AreEqual( new[] { 64, 32, 16 }, lnd.Levels.Select( l => l.TileSize ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, lnd.Levels.Select( l => l.Index ).ToArray() );
		Assert.IsTrue( lnd.Levels.All( l => l.Textures.Count == 6 && l.Masks.Count == 7 ) );
		Assert.AreEqual( 50, lnd.Levels[0].Textures[5].At( 1, 1, 1 ) ); // R of texture 5
		Assert.AreEqual( 1, lnd.Levels[2].Masks[1].At( 0, 0 ) );
	}

	[TestMethod]
	public void RejectsTruncatedOrTrailingData()
	{
		Assert.ThrowsExactly<InvalidDataException>( () => new LndFile( new MemoryStream( BuildLnd( truncateBy: 1 ) ) ) );
		var trailing = BuildLnd().Concat( new byte[] { 0 } ).ToArray();
		Assert.ThrowsExactly<InvalidDataException>( () => new LndFile( new MemoryStream( trailing ) ) );
	}

	[TestMethod]
	public void ComposeMixesFamiliesByWeightAndKeepsTheMask()
	{
		// Left half of the map: opaque, pure grass (G). Right half: alpha 0 (a scenery hole).
		var lnd = new LndFile( new MemoryStream( BuildLnd( i =>
		{
			int px = i / 4 % 4, c = i % 4;
			return c switch { 0 => (byte)(px < 2 ? 255 : 0), 2 => 255, _ => 0 };
		} ) ) );

		var rgba = ParkGround.Compose( lnd, 4, 2 );

		// Grass = average of textures 2 and 3: R = (20 + 30) / 2, G = (22 + 23) / 2, B = (32 + 33) / 2.
		Assert.AreEqual( 25, rgba[0] );
		Assert.AreEqual( 22, rgba[1] );
		Assert.AreEqual( 32, rgba[2] );
		Assert.AreEqual( 255, rgba[3] );
		Assert.AreEqual( 0, rgba[3 * 4 + 3], "the right-most pixel is a scenery hole" );
	}

	[TestMethod]
	public void ComposeFallsBackToGrassWhenUnweighted()
	{
		var lnd = new LndFile( new MemoryStream( BuildLnd( i => (byte)(i % 4 == 0 ? 255 : 0) ) ) );
		var rgba = ParkGround.Compose( lnd, 2, 1 );
		Assert.AreEqual( 25, rgba[0] );
		Assert.AreEqual( 255, rgba[3] );
	}

	// Optional validation against a real terrain.wad/base.lnd. Set TPW_LND_SAMPLE to run.
	[TestMethod]
	public void ParsesRealLndSample()
	{
		var path = Environment.GetEnvironmentVariable( "TPW_LND_SAMPLE" );
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			Assert.Inconclusive( "Set TPW_LND_SAMPLE to a Theme Park World base.lnd (from a level's terrain.wad) to run this test." );

		var lnd = new LndFile( File.OpenRead( path ) );
		Assert.AreEqual( 3, lnd.Header[0] );
		Assert.AreEqual( 384, lnd.BlendMap.Width );
		Assert.AreEqual( 344, lnd.BlendMap.Height );
		CollectionAssert.AreEqual( new[] { 64, 32, 16 }, lnd.Levels.Select( l => l.TileSize ).ToArray() );
		Assert.IsTrue( lnd.Levels.All( l => l.Textures.All( t => t.BytesPerPixel == 4 ) && l.Masks.All( m => m.BytesPerPixel == 1 ) ) );
	}

	[TestMethod]
	public void FillUncoveredHolesKeepsOnlyHolesAMeshCovers()
	{
		// 4×2 map, all holes (alpha 0). One triangle covers the left half of the world (x 0..5 → pixels 0..1).
		var lnd = new LndFile( new MemoryStream( BuildLnd( i => 0 ) ) );
		var tri = (new System.Numerics.Vector2( -1, -1 ), new System.Numerics.Vector2( 5, -1 ), new System.Numerics.Vector2( 5, 99 ));
		var tri2 = (new System.Numerics.Vector2( -1, -1 ), new System.Numerics.Vector2( 5, 99 ), new System.Numerics.Vector2( -1, 99 ));
		var map = ParkGround.FillUncoveredHoles( lnd.BlendMap, new[] { tri, tri2 } );

		Assert.AreEqual( 0, map.At( 0, 0 ), "covered → still a hole" );
		Assert.AreEqual( 0, map.At( 1, 1 ) );
		Assert.AreEqual( 255, map.At( 2, 0 ), "nothing over it → ground again" );
		Assert.AreEqual( 255, map.At( 3, 1 ) );
		Assert.AreEqual( 0, lnd.BlendMap.At( 3, 1 ), "the source map is not modified" );
	}
}
