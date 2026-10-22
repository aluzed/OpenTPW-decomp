using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
public class GestureFileTests
{
	// The jungle advisor and his gestures (needs the game: OPENTPW_GAMEPATH, see tools/run-integration-tests.sh).
	private static WadArchive Wad()
	{
		var path = Path.Combine( Environment.GetEnvironmentVariable( "OPENTPW_GAMEPATH" ) ?? "", "Data", "levels", "jungle", "advisor.wad" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"Game data not found ({path}); set OPENTPW_GAMEPATH." );
		return new WadArchive( path );
	}

	[TestMethod]
	public void TheAdvisorIsATreeOfMeshesAndNodes()
	{
		var tree = ModelHierarchy.Read( Wad().GetFile( "Advisor_JUN.MD2" ).GetData() );
		Assert.IsNotNull( tree );
		Assert.AreEqual( 18, tree.MeshCount );
		Assert.AreEqual( 29, tree.ObjectCount );
		Assert.AreEqual( 24, tree.Parent[0] );  // Bug Head under the root node
		Assert.AreEqual( -1, tree.Parent[24] ); // the root
		Assert.AreEqual( 0, tree.Parent[25] );  // the neck under the head
		Assert.AreEqual( 25, tree.Parent[26] ); // the right shoulder under the neck
		Assert.AreEqual( 26, tree.Parent[14] ); // the right hand under its shoulder
	}

	[TestMethod]
	public void GesturesRotateTheTreeFromItsBindPose()
	{
		var wad = Wad();
		var tree = ModelHierarchy.Read( wad.GetFile( "Advisor_JUN.MD2" ).GetData() )!;
		var wave = new GestureFile( wad.GetFile( "Advisor_JUNm1.MD2" ).GetData() );
		Assert.AreEqual( 50f, wave.Length );

		var neck = wave.Tracks.Single( t => t.Target == 25 );
		CollectionAssert.AreEqual( new[] { 0f, 28f, 50f }, neck.Rotation.Select( k => k.Frame ).ToArray() );
		// A key is the object's whole local rotation: the first one is the bind pose.
		var hand = wave.Tracks.Single( t => t.Target == 14 );
		Matrix4x4.Decompose( tree.Local[14], out _, out var bind, out _ );
		Assert.IsTrue( MathF.Abs( Quaternion.Dot( bind, hand.Rotation[0].Value ) ) > 0.999f );
	}

	[TestMethod]
	public void TheAdvisorBlinksByVisibility()
	{
		var gesture = new GestureFile( Wad().GetFile( "Advisor_JUNm2.MD2" ).GetData() );
		var eye = gesture.Tracks.Single( t => t.Target == 1 );
		var shut = gesture.Tracks.Single( t => t.Target == 3 );
		Assert.AreEqual( true, eye.VisibleAt( 10f ) );
		Assert.AreEqual( false, eye.VisibleAt( 62f ) );
		Assert.AreEqual( true, eye.VisibleAt( 70f ) );
		Assert.AreEqual( false, shut.VisibleAt( 10f ) );
		Assert.AreEqual( true, shut.VisibleAt( 62f ) );
		Assert.AreEqual( false, shut.VisibleAt( 70f ) );
	}

	// A minimal gesture in the layout the advisor's files use (no game data needed): one record rotating object 25
	// over three numbered keys (the last tagged 0xFFFF), one record showing object 3 from frame 10 to 20.
	private static byte[] Synthetic()
	{
		var d = new byte[0x400];
		void U16( int o, int v ) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( d.AsSpan( o, 2 ), (ushort)v );
		void U32( int o, uint v ) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( d.AsSpan( o, 4 ), v );
		void F32( int o, float v ) => System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( d.AsSpan( o, 4 ), v );
		U32( 0, 0x1CD15D46 );
		int trailer = 0x100, records = 0x140, keys = 0x200, visibility = 0x300;
		U32( 0x98, (uint)trailer );
		U32( trailer + 0x08, 30 );          // length, frames
		U16( trailer + 0x12, 2 );           // records
		U32( trailer + 0x2c, (uint)records );

		U32( records + 0x04, GestureFile.FlagRotation );
		U16( records + 0x10, 3 );           // rotation keys
		U16( records + 0x14, 25 );          // the object
		U32( records + 0x1c, (uint)keys );
		var rotations = new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, 0.5f ), Quaternion.Identity };
		for ( int k = 0; k < 3; k++ )
		{
			int o = keys + k * 20;
			U32( o, ((k == 2 ? 0xFFFFu : (uint)k) << 16) | (uint)(k * 15) );
			F32( o + 4, rotations[k].X ); F32( o + 8, rotations[k].Y ); F32( o + 12, rotations[k].Z ); F32( o + 16, rotations[k].W );
		}

		int r2 = records + 0x40;
		U32( r2 + 0x04, GestureFile.FlagVisibility );
		U16( r2 + 0x14, 3 );
		U32( r2 + 0x30, (uint)visibility );
		U16( visibility, 10 );              // shown from 10
		U16( visibility + 2, unchecked((ushort)-20) ); // hidden from 20
		return d;
	}

	[TestMethod]
	public void ReadsRotationAndVisibilityTracks()
	{
		var g = new GestureFile( Synthetic() );
		Assert.AreEqual( 30f, g.Length );
		var turn = g.Tracks.Single( t => t.Target == 25 );
		CollectionAssert.AreEqual( new[] { 0f, 15f, 30f }, turn.Rotation.Select( k => k.Frame ).ToArray() );
		var mid = GestureFile.Sample( turn.Rotation, 15f );
		Assert.IsTrue( MathF.Abs( Quaternion.Dot( mid, Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, 0.5f ) ) ) > 0.999f );

		var shown = g.Tracks.Single( t => t.Target == 3 );
		Assert.AreEqual( false, shown.VisibleAt( 5f ) );
		Assert.AreEqual( true, shown.VisibleAt( 15f ) );
		Assert.AreEqual( false, shown.VisibleAt( 25f ) );
	}
}
