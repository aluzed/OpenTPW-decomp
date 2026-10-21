using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class CoasterTrackTests
{
	// A minimal coaster.sam excerpt: the authored track cross-section (asCrossSectionPoints1[*]), tab-
	// separated like the real files. Mirrors coaster1.wad/coaster.sam's 7-point channel profile.
	private const string Sam =
		"asCrossSectionPoints1[0].fX\t\t-4.0\n" +
		"asCrossSectionPoints1[0].fY\t\t2.5\n" +
		"asCrossSectionPoints1[0].fU\t\t1.0\n" +
		"asCrossSectionPoints1[1].fX\t\t-3.0\n" +
		"asCrossSectionPoints1[1].fY\t\t2.0\n" +
		"asCrossSectionPoints1[1].fU\t\t0.1\n" +
		"asCrossSectionPoints1[2].fX\t\t3.0\n" +
		"asCrossSectionPoints1[2].fY\t\t2.0\n" +
		"asCrossSectionPoints1[2].fU\t\t0.9\n" +
		"asCrossSectionPoints1[3].fX\t\t4.0\n" +
		"asCrossSectionPoints1[3].fY\t\t2.5\n" +
		"asCrossSectionPoints1[3].fU\t\t1.0\n" +
		"asCrossSectionPoints1[4].fX\t\t0.0\n" +
		"asCrossSectionPoints1[4].fY\t\t-1.5\n" +
		"asCrossSectionPoints1[4].fU\t\t0.0\n";

	[TestMethod]
	public void ParsesAllProfilePointsInOrder()
	{
		var settings = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( Sam ) ) );
		var pts = CoasterTrack.ParseCrossSection( settings );

		Assert.AreEqual( 5, pts.Count );
		Assert.AreEqual( -4.0f, pts[0].X, 1e-4f );
		Assert.AreEqual( 2.5f, pts[0].Y, 1e-4f );
		Assert.AreEqual( 1.0f, pts[0].U, 1e-4f );
		// The channel floor: centred (X=0) and the lowest point (Y=-1.5).
		Assert.AreEqual( 0.0f, pts[4].X, 1e-4f );
		Assert.AreEqual( -1.5f, pts[4].Y, 1e-4f );
	}

	[TestMethod]
	public void EmptySettingsYieldsNoPoints()
	{
		var settings = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( "someOtherKey\t1.0\n" ) ) );
		Assert.AreEqual( 0, CoasterTrack.ParseCrossSection( settings ).Count );
	}

	[TestMethod]
	public void BankAngleIsZeroOnAStraight()
	{
		var fwd = new Vector3( 1f, 0f, 0f );
		Assert.AreEqual( 0f, CoasterTrack.BankAngle( fwd, fwd, gain: 1.6f, maxBank: 0.6f ), 1e-5f );
	}

	[TestMethod]
	public void BankAngleSignsOppositeForLeftVsRightTurns()
	{
		// Travelling +X, then turning toward +Y (a left turn) vs toward -Y (a right turn).
		var tin = new Vector3( 1f, 0f, 0f );
		var left = CoasterTrack.BankAngle( tin, new Vector3( 1f, 1f, 0f ).Normal, 1.6f, 0.6f );
		var right = CoasterTrack.BankAngle( tin, new Vector3( 1f, -1f, 0f ).Normal, 1.6f, 0.6f );

		Assert.IsTrue( left > 0f, "left turn banks one way" );
		Assert.IsTrue( right < 0f, "right turn banks the other" );
		Assert.AreEqual( left, -right, 1e-5f, "symmetric turns bank symmetrically" );
	}

	[TestMethod]
	public void BankAngleClampsHardTurns()
	{
		// A near-reversal would roll past the cap; it's clamped to ±maxBank.
		var tin = new Vector3( 1f, 0f, 0f );
		var hardLeft = new Vector3( -1f, 0.2f, 0f ).Normal; // ~157° left
		Assert.AreEqual( 0.6f, CoasterTrack.BankAngle( tin, hardLeft, gain: 1.6f, maxBank: 0.6f ), 1e-5f );
	}

	[TestMethod]
	public void BankAngleScalesWithGain()
	{
		var tin = new Vector3( 1f, 0f, 0f );
		var tout = new Vector3( 1f, 0.3f, 0f ).Normal;
		float small = CoasterTrack.BankAngle( tin, tout, gain: 0.5f, maxBank: 1.5f );
		float big = CoasterTrack.BankAngle( tin, tout, gain: 1.0f, maxBank: 1.5f );
		Assert.AreEqual( 2f * small, big, 1e-5f, "doubling the gain doubles the (unclamped) bank" );
	}

	[TestMethod]
	public void TrackTextureIsTheOneMostCrossSectionEdgesUse()
	{
		const string sam =
			"asTextureData[0].pcTextureFilename\t\"Gutter.tga\"\n" +
			"asTextureData[1].pcTextureFilename\t\"Paint_flow.tga\"\n" +
			"asCrossSectionEdges1[0].usTexture\t1\n" +
			"asCrossSectionEdges1[1].usTexture\t1\n" +
			"asCrossSectionEdges1[2].usTexture\t0\n";
		var settings = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( sam ) ) );
		Assert.AreEqual( "Paint_flow", CoasterTrack.ParseTrackTexture( settings ) );
	}

	[TestMethod]
	public void TrackTextureDefaultsToTheFirstAndIsNullWhenUnnamed()
	{
		var first = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( "asTextureData[0].pcTextureFilename\t\"trak_sec3.tga\"\n" ) ) );
		Assert.AreEqual( "trak_sec3", CoasterTrack.ParseTrackTexture( first ) );
		var none = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( "someOtherKey\t1.0\n" ) ) );
		Assert.IsNull( CoasterTrack.ParseTrackTexture( none ) );
	}
}
