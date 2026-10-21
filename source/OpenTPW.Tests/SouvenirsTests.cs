using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SouvenirsTests
{
	[TestMethod]
	public void EachThoughtHasItsBubbleIcon()
	{
		Assert.AreEqual( 8, Souvenirs.BubbleFrame( CrowdThoughts.Hungry ), "burger" );
		Assert.AreEqual( 10, Souvenirs.BubbleFrame( CrowdThoughts.Thirsty ), "drink" );
		Assert.AreEqual( 12, Souvenirs.BubbleFrame( CrowdThoughts.HungryAndThirsty ), "drink + burger" );
		Assert.AreEqual( 13, Souvenirs.BubbleFrame( CrowdThoughts.NeedToilet ), "toilet" );
		Assert.AreEqual( 14, Souvenirs.BubbleFrame( CrowdThoughts.TooMessy ), "litter" );
		Assert.AreEqual( 11, Souvenirs.BubbleFrame( CrowdThoughts.QueueTooLong ), "hourglass" );
		Assert.AreEqual( 2, Souvenirs.BubbleFrame( CrowdThoughts.Angry ), "angry face" );
		Assert.IsNull( Souvenirs.BubbleFrame( CrowdThoughts.Okay ), "nothing worth a bubble" );
	}

	[TestMethod]
	public void ThemeFoldersAreCapitalised()
	{
		Assert.AreEqual( "Jungle", Souvenirs.ThemeDir( "jungle" ) );
		Assert.AreEqual( "Hallow", Souvenirs.ThemeDir( "HALLOW" ) );
	}
}
