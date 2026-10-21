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
}
