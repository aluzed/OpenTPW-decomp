using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class LipSyncFileTests
{
	private static byte[] Lip( params uint[] values )
	{
		var data = new byte[(values.Length + 1) * 4];
		for ( var i = 0; i < values.Length; i++ )
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( i * 4, 4 ), values[i] );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( values.Length * 4, 4 ), 0xFFFFFFFF );
		return data;
	}

	[TestMethod]
	public void ReadsKeyframesUntilTerminator()
	{
		var bytes = Lip( 0, 1378095, 4497959, 5268752 );

		using var stream = new MemoryStream( bytes );
		var lip = new LipSyncFile( stream );

		CollectionAssert.AreEqual( new uint[] { 0, 1378095, 4497959, 5268752 }, lip.Keyframes );

		// Timestamps are microseconds: the last keyframe (5268752 µs) is ~5.27 s.
		Assert.AreEqual( TimeSpan.FromMicroseconds( 5268752 ), lip.Duration );
		Assert.AreEqual( 5.268752, lip.Duration.TotalSeconds, 1e-6 );
	}

	[TestMethod]
	public void ResolvesIntervalsAndCyclesVisemesInSync()
	{
		// Boundaries at 0,1,2,3,4,5,6 s — seven keyframes => six intervals.
		var lip = new LipSyncFile( new MemoryStream( Lip( 0, 1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000, 6_000_000 ) ) );

		// The active interval tracks the playback time.
		Assert.AreEqual( -1, lip.IntervalAt( TimeSpan.FromSeconds( -0.5 ) ), "before the first keyframe" );
		Assert.AreEqual( 0, lip.IntervalAt( TimeSpan.FromSeconds( 0.5 ) ) );
		Assert.AreEqual( 2, lip.IntervalAt( TimeSpan.FromSeconds( 2.5 ) ) );
		Assert.AreEqual( -1, lip.IntervalAt( TimeSpan.FromSeconds( 6.0 ) ), "at/after the last keyframe" );

		// Outside the clip there is no mouth; the timestamps toggle it open / closed (FUN_00599880): closed it is
		// "normal", open it shows a viseme re-picked every 100 ms.
		Assert.AreEqual( MouthShape.Closed, lip.ShapeAt( TimeSpan.FromSeconds( 7 ) ) );
		Assert.AreEqual( MouthShape.Normal, lip.ShapeAt( TimeSpan.FromSeconds( 1.5 ) ), "interval 1: closed" );
		Assert.AreEqual( MouthShape.Normal, lip.ShapeAt( TimeSpan.FromSeconds( 5.5 ) ), "interval 5: closed" );
		var open = Enumerable.Range( 0, 10 ).Select( i => lip.ShapeAt( TimeSpan.FromMilliseconds( 2000 + i * 100 + 50 ) ) ).ToList();
		Assert.IsTrue( open.All( s => s is >= MouthShape.Normal and <= MouthShape.Sss ), "open: one of the five visemes" );
		Assert.IsTrue( open.Distinct().Count() > 1, "the open mouth changes viseme" );
		Assert.AreEqual( lip.ShapeAt( TimeSpan.FromMilliseconds( 2010 ) ), lip.ShapeAt( TimeSpan.FromMilliseconds( 2090 ) ), "one pick per 100 ms" );
	}

	[TestMethod]
	public void VisemesMapToTheAdvisorMeshPartNames()
	{
		// RE'd from FUN_0044b2e0: each viseme names a sub-mesh of the advisor model; Closed has none.
		Assert.IsNull( MouthShape.Closed.MeshPartName() );
		Assert.AreEqual( "mouth - normal", MouthShape.Normal.MeshPartName() );
		Assert.AreEqual( "mouth - aah", MouthShape.Aah.MeshPartName() );
		Assert.AreEqual( "mouth - eee", MouthShape.Eee.MeshPartName() );
		Assert.AreEqual( "mouth - ooh", MouthShape.Ooh.MeshPartName() );
		Assert.AreEqual( "mouth - sss", MouthShape.Sss.MeshPartName() );
	}

	// Optional validation against a real .LIP. Set TPW_LIP_SAMPLE.
	[TestMethod]
	public void ParsesRealLipSample()
	{
		var path = Environment.GetEnvironmentVariable( "TPW_LIP_SAMPLE" );
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			Assert.Inconclusive( "Set TPW_LIP_SAMPLE to a Theme Park World .LIP file to run this test." );

		using var stream = File.OpenRead( path );
		var lip = new LipSyncFile( stream );

		Assert.IsTrue( lip.Keyframes.Count > 0, "lip-sync should have keyframes" );
		// Keyframe timestamps must be monotonically non-decreasing.
		for ( var i = 1; i < lip.Keyframes.Count; i++ )
			Assert.IsTrue( lip.Keyframes[i] >= lip.Keyframes[i - 1], $"keyframe {i} not monotonic" );

		// Microsecond unit: a speech clip's duration is a few-to-tens of seconds.
		Assert.IsTrue( lip.Duration > TimeSpan.Zero && lip.Duration < TimeSpan.FromMinutes( 5 ),
			$"duration {lip.Duration} should be a plausible speech length" );
	}
}
