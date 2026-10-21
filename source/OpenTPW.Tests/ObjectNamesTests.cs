using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ObjectNamesTests
{
	private static readonly string[] En = { "", "DinoKarts", "Crazy", "Ape", "Inca", "Totem", "Dino Karts Tunnel" };
	private static readonly string[] Fr = { "", "DinoKarts", "Singe", "Ouf", "Totem", "Inca", "Tunnel des dino-karts" };

	[TestMethod]
	public void ANameIsTranslatedByItsPositionInTheTable()
	{
		Assert.AreEqual( "Singe Ouf", ObjectNames.Localize( "Crazy Ape", En, Fr ) );
		Assert.AreEqual( "Totem Inca", ObjectNames.Localize( "Inca  Totem", En, Fr ), "whitespace-insensitive" );
		Assert.AreEqual( "Tunnel des dino-karts", ObjectNames.Localize( "Dino Karts Tunnel", En, Fr ), "a single entry" );
		Assert.AreEqual( "Totem Inca", ObjectNames.Localize( "<JUMPER> Inca Totem", En, Fr ), "a <TAG> marker is ignored" );
		Assert.AreEqual( "Insecticide", ObjectNames.Localize( "<JUMPER> Insecticide", En, Fr ), "and dropped from an unknown name" );
	}

	[TestMethod]
	public void AnUnknownNameIsKept()
	{
		Assert.AreEqual( "Burger Shop", ObjectNames.Localize( "Burger Shop", En, Fr ) );
	}
}
