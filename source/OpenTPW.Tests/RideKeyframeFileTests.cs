using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using SVector3 = System.Numerics.Vector3;   // OpenTPW.Vector3 (global alias) shadows the bare name

namespace OpenTPW.Tests;

[TestClass]
public class RideKeyframeFileTests
{
	// Builds a minimal keyframe file in the older layout (docs/08, the jungle monkey's): a header with the magic and
	// a 0x98 anim pointer, a trailer (count + record array), one record (key count +0x10, object +0x14) with a
	// rotation track of (0xFFFF-tagged time, (w,x,y,z) quaternion) keys.
	private static byte[] BuildFrame( int surfaceIndex, (int time, Quaternion q)[] rotKeys )
	{
		var d = new byte[0x400];
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0, 4 ), 0x1CD15D46 ); // magic

		int trailer = 0x100;
		int records = 0x140;
		int rotTrack = 0x200;

		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0x98, 4 ), (uint)trailer );    // anim pointer
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( trailer + 0x12, 2 ), 1 );      // record count
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( trailer + 0x2c, 4 ), (uint)records ); // record array

		// One surface record (0x40 bytes).
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x04, 4 ), RideKeyframeFile.FlagRotation ); // flags
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( records + 0x10, 2 ), (ushort)rotKeys.Length );        // rotation key count
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( records + 0x14, 2 ), (ushort)surfaceIndex );          // the object
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x1c, 4 ), (uint)rotTrack );                // rotation track offset

		// Rotation track: stride 20 = [0xFFFF|time][4 floats w,x,y,z].
		int o = rotTrack;
		foreach ( var (time, q) in rotKeys )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( o, 4 ), 0xFFFF0000u | (uint)time );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 4, 4 ), q.W );  // stored (w, x, y, z)
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 8, 4 ), q.X );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 12, 4 ), q.Y );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 16, 4 ), q.Z );
			o += 20;
		}
		return d;
	}

	// Builds a frame with one surface carrying a rotation track (0xFFFF marker) and a contiguous scale
	// track (0x0000 marker) — exercises the per-track marker + gap-bounding (a scale key time can
	// exceed the next track's start, which must not be misread as extra keys).
	private static byte[] BuildFrameRotAndScale( (int time, Quaternion q)[] rot, (int time, SVector3 s)[] scale )
	{
		var d = new byte[0x600];
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0, 4 ), 0x1CD15D46 );
		int trailer = 0x100, records = 0x140, rotTrack = 0x200, scaleTrack = 0x300;
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0x98, 4 ), (uint)trailer );
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( trailer + 0x12, 2 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( trailer + 0x2c, 4 ), (uint)records );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x04, 4 ), RideKeyframeFile.FlagRotation | RideKeyframeFile.FlagScale );
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( records + 0x10, 2 ), (ushort)rot.Length );
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( records + 0x12, 2 ), (ushort)scale.Length );
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( records + 0x14, 2 ), 5 );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x1c, 4 ), (uint)rotTrack );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x20, 4 ), (uint)scaleTrack );

		int o = rotTrack;
		foreach ( var (time, q) in rot )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( o, 4 ), 0xFFFF0000u | (uint)time );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 4, 4 ), q.W );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 8, 4 ), q.X );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 12, 4 ), q.Y );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 16, 4 ), q.Z );
			o += 20;
		}
		o = scaleTrack;
		foreach ( var (time, s) in scale )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( o, 4 ), (uint)time );   // 0x0000 marker in high u16
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 4, 4 ), s.X );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 8, 4 ), s.Y );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 12, 4 ), s.Z );
			o += 16;
		}
		return d;
	}

	// Builds a frame with one vertex-morph record: a block (bbox offset/scale + 1 sub-entry) whose
	// sub-entry has vc vertices keyframed via 10-bit-packed positions (see docs/08).
	private static byte[] BuildFrameWithMorph( int slot, SVector3 off, SVector3 scl, (int time, (int x, int y, int z))[] keys )
	{
		var d = new byte[0x400];
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0, 4 ), 0x1CD15D46 );
		int trailer = 0x100, records = 0x140, block = 0x180, sub = 0x1c0, idx = 0x200, times = 0x210, packed = 0x220;
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0x98, 4 ), (uint)trailer );
		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( trailer + 0x12, 2 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( trailer + 0x2c, 4 ), (uint)records );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x04, 4 ), RideKeyframeFile.FlagVertexMorph );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( records + 0x28, 4 ), (uint)block );

		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( block + 0x02, 2 ), 1 );            // nsub
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( block + 0x0c, 4 ), (uint)sub );    // substruct
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x14, 4 ), off.X );
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x18, 4 ), off.Y );
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x1c, 4 ), off.Z );
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x20, 4 ), scl.X );
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x24, 4 ), scl.Y );
		BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( block + 0x28, 4 ), scl.Z );

		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( sub + 0x02, 2 ), 1 );              // vertexCount
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( sub + 0x04, 4 ), (uint)idx );      // index ptr
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( sub + 0x08, 4 ), (uint)times );    // times ptr
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( sub + 0x0c, 4 ), (uint)packed );   // packed ptr

		BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( idx, 2 ), (ushort)slot );
		for ( int f = 0; f < keys.Length; f++ )
		{
			BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( times + f * 2, 2 ), (ushort)keys[f].time );
			var (x, y, z) = keys[f].Item2;
			uint p = (uint)((x & 0x3ff) | ((y & 0x3ff) << 10) | ((z & 0x3ff) << 20));
			BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( packed + f * 4, 4 ), p );
		}
		return d;
	}

	[TestMethod]
	public void ParsesVertexMorphAndDequantises()
	{
		// Two keyframes; identity bbox transform so dequant returns the raw signed-10-bit components.
		var keys = new (int, (int, int, int))[] { (0, (10, 20, 30)), (10, (40, -50, 60)) };
		var kf = new RideKeyframeFile( BuildFrameWithMorph( slot: 3, SVector3.Zero, SVector3.One, keys ) );

		var m = kf.Surfaces.Single().Morph.Single();
		Assert.AreEqual( 1, m.VertexSlots.Length );
		Assert.AreEqual( 3, m.VertexSlots[0], "output vertex slot" );
		Assert.AreEqual( 2, m.Times.Length );
		Assert.AreEqual( 10f, kf.Duration );

		// Frame 0 dequantises to the raw components; negative component sign-extends.
		Assert.AreEqual( new SVector3( 10, 20, 30 ), m.Frames[0][0] );
		Assert.AreEqual( new SVector3( 40, -50, 60 ), m.Frames[1][0] );

		// Midpoint lerps.
		var mid = m.Sample( 0, 5f );
		Assert.AreEqual( 25f, mid.X, 1e-3f );
		Assert.AreEqual( -15f, mid.Y, 1e-3f );
		Assert.AreEqual( 45f, mid.Z, 1e-3f );
	}

	[TestMethod]
	public void MorphDequantAppliesBoundingBoxTransform()
	{
		// Non-identity bbox: component * scale + offset.
		var keys = new (int, (int, int, int))[] { (0, (100, 0, -100)) };
		var kf = new RideKeyframeFile( BuildFrameWithMorph( 0, new SVector3( 1, 2, 3 ), new SVector3( 0.5f, 0.5f, 0.5f ), keys ) );
		var p = kf.Surfaces.Single().Morph.Single().Frames[0][0];
		Assert.AreEqual( 100 * 0.5f + 1, p.X, 1e-3f );
		Assert.AreEqual( 0 * 0.5f + 2, p.Y, 1e-3f );
		Assert.AreEqual( -100 * 0.5f + 3, p.Z, 1e-3f );
	}

	[TestMethod]
	public void ParsesScaleTrackAndComposesWithRotation()
	{
		// Like space_bouncy: a "grow-in" scale 0.1 -> 1 alongside a rotation track.
		var rot = new[] { (0, RotZ( 0 )), (10, RotZ( 90 )) };
		var scale = new[] { (0, new SVector3( 0.1f )), (10, SVector3.One ) };
		var kf = new RideKeyframeFile( BuildFrameRotAndScale( rot, scale ) );

		var s = kf.Surfaces.Single();
		Assert.AreEqual( 2, s.Rotation.Count );
		Assert.AreEqual( 2, s.Scale.Count, "scale track (0x0000 marker) should parse" );
		Assert.AreEqual( 0.1f, s.Scale[0].Value.X, 1e-4f );

		// Midpoint scale lerps to ~0.55.
		var mid = RideKeyframeFile.SampleVector( s.Scale, 5f, SVector3.One );
		Assert.AreEqual( 0.55f, mid.X, 1e-3f );
	}

	private static Quaternion RotZ( float deg ) =>
		Quaternion.CreateFromAxisAngle( SVector3.UnitZ, deg * MathF.PI / 180f );

	[TestMethod]
	public void ParsesRotationTrackKeysAndTimes()
	{
		// A full 360° turn about Z, like monkeym1's m_arm (surface 5): t=0..40.
		var keys = new[] { (0, RotZ( 0 )), (10, RotZ( 90 )), (20, RotZ( 180 )), (30, RotZ( 270 )), (40, RotZ( 360 )) };
		var kf = new RideKeyframeFile( BuildFrame( 5, keys ) );

		Assert.AreEqual( 1, kf.Surfaces.Count );
		var s = kf.Surfaces[0];
		Assert.AreEqual( 5, s.SurfaceIndex );
		Assert.AreEqual( 5, s.Rotation.Count, "should read exactly 5 keys (sentinel + monotonic time)" );
		Assert.AreEqual( 40f, kf.Duration );
		Assert.AreEqual( 0f, s.Rotation[0].Time );
		Assert.AreEqual( 20f, s.Rotation[2].Time );
	}

	[TestMethod]
	public void StopsAtTimeWrap()
	{
		// A key count past the track (the next one wraps back to t=0) — only the first track should be read.
		var keys = new[] { (0, RotZ( 0 )), (10, RotZ( 90 )), (0, RotZ( 0 )), (10, RotZ( 90 )) };
		var kf = new RideKeyframeFile( BuildFrame( 5, keys ) );
		Assert.AreEqual( 2, kf.Surfaces[0].Rotation.Count, "must stop when the keyframe time decreases" );
	}

	[TestMethod]
	public void ReadsTheNewerLayoutsNumberedXyzwKeys()
	{
		// The newer files (the advisor's, the Inca god's): keys numbered 0, 1, … with the last tagged 0xFFFF, (x,y,z,w).
		var d = BuildFrame( 7, new[] { (0, RotZ( 0 )), (10, RotZ( 90 )), (20, RotZ( 180 )) } );
		int o = 0x200;
		var keys = new[] { RotZ( 0 ), RotZ( 90 ), RotZ( 180 ) };
		for ( int k = 0; k < keys.Length; k++, o += 20 )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( o, 4 ), ((k == keys.Length - 1 ? 0xFFFFu : (uint)k) << 16) | (uint)(k * 10) );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 4, 4 ), keys[k].X );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 8, 4 ), keys[k].Y );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 12, 4 ), keys[k].Z );
			BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o + 16, 4 ), keys[k].W );
		}
		var s = new RideKeyframeFile( d ).Surfaces.Single();
		Assert.AreEqual( 7, s.SurfaceIndex );
		Assert.AreEqual( 3, s.Rotation.Count );
		AssertQuat( RotZ( 90 ), s.Rotation[1].Value );
	}

	[TestMethod]
	public void TheLengthComesFromTheTrailer()
	{
		var d = BuildFrame( 5, new[] { (0, RotZ( 0 )), (10, RotZ( 90 )) } );
		BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( 0x100 + 0x08, 4 ), 60 );
		Assert.AreEqual( 60f, new RideKeyframeFile( d ).Duration );
	}

	[TestMethod]
	public void SampleRotationInterpolatesAndClamps()
	{
		var keys = new (float, Quaternion)[] { (0f, RotZ( 0 )), (10f, RotZ( 90 )) };

		// Clamp below/above the range.
		AssertQuat( RotZ( 0 ), RideKeyframeFile.SampleRotation( keys, -5f ) );
		AssertQuat( RotZ( 90 ), RideKeyframeFile.SampleRotation( keys, 99f ) );

		// Midpoint slerps to ~45°.
		AssertQuat( RotZ( 45 ), RideKeyframeFile.SampleRotation( keys, 5f ) );
	}

	[TestMethod]
	public void MergeCombinesSurfacesAndMaxesDuration()
	{
		// A channel's numbered frame files each animate different surfaces; merging unions them.
		var a = new RideKeyframeFile( BuildFrame( 5, new[] { (0, RotZ( 0 )), (40, RotZ( 360 )) } ) );
		var b = new RideKeyframeFile( BuildFrame( 9, new[] { (0, RotZ( 0 )), (100, RotZ( 180 )) } ) );
		Assert.AreEqual( 40f, a.Duration );

		a.Merge( b );
		Assert.AreEqual( 2, a.Surfaces.Count, "merged file should hold both surfaces" );
		CollectionAssert.AreEquivalent( new[] { 5, 9 }, a.Surfaces.Select( s => s.SurfaceIndex ).ToArray() );
		Assert.AreEqual( 100f, a.Duration, "duration is the max across merged files" );
	}

	[TestMethod]
	public void FilesMovingTheSameObjectsAreVariants()
	{
		var a = new RideKeyframeFile( BuildFrame( 5, new[] { (0, RotZ( 0 )), (40, RotZ( 90 )) } ) );
		var b = new RideKeyframeFile( BuildFrame( 5, new[] { (0, RotZ( 90 )), (60, RotZ( 0 )) } ) );
		a.AddVariant( b );
		Assert.AreEqual( 2, a.Variants );
		Assert.AreEqual( 60f, a.Duration, "as long as the longest variant" );
		Assert.AreEqual( 0, a.Surfaces[0].Variant );
		Assert.AreEqual( 1, a.Surfaces[1].Variant );
	}

	[TestMethod]
	public void AnObjectsVariantsPlayOnePerPass()
	{
		// Two files moving object 5 (variants) and one moving object 7 (always on).
		var kf = new RideKeyframeFile( BuildFrame( 5, new[] { (0, RotZ( 0 )), (50, RotZ( 90 )) } ) );
		kf.AddVariant( new RideKeyframeFile( BuildFrame( 5, new[] { (0, RotZ( 0 )), (50, RotZ( 180 )) } ) ) );
		kf.AddVariant( new RideKeyframeFile( BuildFrame( 7, new[] { (0, RotZ( 0 )), (50, RotZ( 45 )) } ) ) );
		var (first, second, other) = (kf.Surfaces[0], kf.Surfaces[1], kf.Surfaces[2]);

		Assert.IsTrue( KeyframePoser.Playing( first, kf, 10f, loop: true, out var t ) );
		Assert.AreEqual( 10f, t );
		Assert.IsFalse( KeyframePoser.Playing( second, kf, 10f, loop: true, out _ ) );
		Assert.IsTrue( KeyframePoser.Playing( second, kf, 60f, loop: true, out t ), "the second pass plays the second variant" );
		Assert.AreEqual( 10f, t );
		Assert.IsTrue( KeyframePoser.Playing( other, kf, 60f, loop: true, out _ ), "an object with one track always plays it" );
		Assert.IsTrue( KeyframePoser.Playing( first, kf, 60f, loop: false, out t ), "a one-shot plays the first, held at its end" );
		Assert.AreEqual( 50f, t );
	}

	[TestMethod]
	public void EmptyOrBadDataYieldsNoAnimation()
	{
		Assert.AreEqual( 0, new RideKeyframeFile( new byte[0x20] ).Surfaces.Count );      // too short / no magic
		var noAnim = new byte[0x100];
		BinaryPrimitives.WriteUInt32LittleEndian( noAnim.AsSpan( 0, 4 ), 0x1CD15D46 );     // magic but anim ptr 0 (base model)
		Assert.AreEqual( 0, new RideKeyframeFile( noAnim ).Surfaces.Count );
	}

	// Optional validation against a real keyframe file (e.g. monkeym1.MD2 extracted from monkey.wad).
	// Set TPW_KEYFRAME_SAMPLE to that file to run.
	[TestMethod]
	public void ParsesRealKeyframeSample()
	{
		var path = Environment.GetEnvironmentVariable( "TPW_KEYFRAME_SAMPLE" );
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			Assert.Inconclusive( "Set TPW_KEYFRAME_SAMPLE to a ride keyframe .MD2 (e.g. monkeym1.MD2) to run this test." );

		var kf = new RideKeyframeFile( File.ReadAllBytes( path ) );
		Assert.IsTrue( kf.Surfaces.Count > 0, "real keyframe file should have animated surfaces" );

		foreach ( var s in kf.Surfaces )
		{
			// Each track's times must be strictly increasing, and rotation values must be unit quaternions.
			for ( int i = 1; i < s.Rotation.Count; i++ )
				Assert.IsTrue( s.Rotation[i].Time > s.Rotation[i - 1].Time, "keyframe times strictly increase" );
			foreach ( var (_, q) in s.Rotation )
				Assert.IsTrue( MathF.Abs( q.Length() - 1f ) < 1e-3f, $"rotation key should be a unit quaternion, got |q|={q.Length()}" );
		}
		Assert.IsTrue( kf.Duration > 0, "duration should be positive" );
	}

	private static void AssertQuat( Quaternion expected, Quaternion actual )
	{
		// Quaternions q and -q are the same rotation; compare via dot magnitude.
		float dot = MathF.Abs( Quaternion.Dot( Quaternion.Normalize( expected ), Quaternion.Normalize( actual ) ) );
		Assert.IsTrue( dot > 0.999f, $"expected {expected}, got {actual} (|dot|={dot})" );
	}
}
