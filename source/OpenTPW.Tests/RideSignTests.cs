using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace OpenTPW.Tests;

[TestClass]
public class RideSignTests
{
	[TestMethod]
	public void WithoutAFontTheBoardIsBlank()
	{
		var rgba = RideSign.Render( null, "Crazy Ape", 8, 8 );
		Assert.AreEqual( 8 * 8 * 4, rgba.Length );
		Assert.AreEqual( 238, rgba[0] );
		Assert.AreEqual( 255, rgba[3] );
	}

	[TestMethod]
	public void SignMaterialsAreRecognised()
	{
		Assert.IsTrue( RideSign.IsSignMaterial( "sign1" ) );
		Assert.IsTrue( RideSign.IsSignMaterial( "SIGN2\0\0" ) );
		Assert.IsFalse( RideSign.IsSignMaterial( "GK_sign" ) );
	}

	// With a real font (TPW_FONT_SAMPLE): ink lands in the middle of the board, the border stays blank.
	[TestMethod]
	public void RendersTheNameCentred()
	{
		var path = Environment.GetEnvironmentVariable( "TPW_FONT_SAMPLE" );
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			Assert.Inconclusive( "Set TPW_FONT_SAMPLE to a Theme Park World .BF4 file to run this test." );

		var atlas = new FontAtlas( new BF4File( File.OpenRead( path ) ) );
		var rgba = RideSign.Render( atlas, "Crazy Ape", 128, 128 );
		int ink = 0, borderInk = 0;
		for ( int y = 0; y < 128; y++ )
			for ( int x = 0; x < 128; x++ )
			{
				bool dark = rgba[(y * 128 + x) * 4] < 150;
				if ( !dark ) continue;
				if ( x < 4 || y < 4 || x > 123 || y > 123 ) borderInk++; else ink++;
			}
		Assert.IsTrue( ink > 100, $"ink pixels: {ink}" );
		Assert.AreEqual( 0, borderInk );
	}
}
