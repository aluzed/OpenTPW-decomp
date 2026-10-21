using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class FogTests
{
	[TestMethod]
	public void HorizonColorAveragesTheNonWhitePixels()
	{
		// two navy pixels + one white haze pixel → navy
		var rgba = new byte[] { 30, 38, 78, 255, 30, 38, 78, 255, 250, 250, 250, 255 };
		var c = Fog.HorizonColor( rgba )!.Value;
		Assert.AreEqual( 30 / 255f, c.X, 1e-4f );
		Assert.AreEqual( 78 / 255f, c.Z, 1e-4f );
		Assert.AreEqual( 1f, c.W );
	}

	[TestMethod]
	public void AllWhiteHasNoHorizonColor()
	{
		Assert.IsNull( Fog.HorizonColor( new byte[] { 255, 255, 255, 255 } ) );
	}
}
