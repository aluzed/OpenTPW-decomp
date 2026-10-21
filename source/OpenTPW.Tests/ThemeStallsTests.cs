using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>Integration: every theme ships a default food/drink stall, a toilet and a litter bin, and its shops
/// decode to the products the challenges count (needs OPENTPW_GAMEPATH).</summary>
[TestClass]
public class ThemeStallsTests
{
	[TestMethod]
	public void EveryThemeHasItsStallsAndFacilities()
	{
		var gp = Environment.GetEnvironmentVariable( "OPENTPW_GAMEPATH" );
		var data = gp == null ? null : Path.Combine( gp, "Data" );
		if ( data == null || !Directory.Exists( data ) )
			Assert.Inconclusive( "Set OPENTPW_GAMEPATH to a Theme Park World install to run this test." );

		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );

		foreach ( var theme in LevelTheme.Known )
		{
			Assert.AreEqual( ShopKind.Food, ShopBuilding.KindOf( ShopBuilding.StatsFor( ShopKind.Food, theme )!.Value.ShopType ), theme );
			Assert.AreEqual( ShopKind.Drink, ShopBuilding.KindOf( ShopBuilding.StatsFor( ShopKind.Drink, theme )!.Value.ShopType ), theme );
			Assert.IsNotNull( ShopBuilding.StatsFor( ShopKind.Toilet, theme ), $"{theme} toilet" );
			Assert.AreEqual( "Litter Bin", ShopBuilding.StatsFor( ShopKind.Bin, theme )?.Name, $"{theme} bin" );

			var products = ShopBuilding.ShopArchives( theme ).Select( a => ShopBuilding.CategoryOf( ShopBuilding.StatsOf( a )!.Value ) ).ToHashSet();
			foreach ( var c in new[] { SaleCategory.Fries, SaleCategory.Burger, SaleCategory.IceCream, SaleCategory.Drink,
				SaleCategory.Restaurant, SaleCategory.Gift, SaleCategory.Costume, SaleCategory.Balloon } )
				Assert.IsTrue( products.Contains( c ), $"{theme} sells {c}" );
		}
	}
}
