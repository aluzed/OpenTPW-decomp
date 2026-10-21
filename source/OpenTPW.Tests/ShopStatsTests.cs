using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ShopStatsTests
{
	[TestMethod]
	public void StallEconomicsComeFromItsSam()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"Info.Name\t\t\"Burger Shop\"\nUsageInfo.InitPricePerUse\t\t30\nUsageInfo.InitCostOfGoods\t\t20\nUpgrades[0].CostOfUpgrade\t\t\t800\n" ) ) );
		var s = ShopBuilding.ParseStats( sam, "burger" );
		Assert.AreEqual( "Burger Shop", s.Name );
		Assert.AreEqual( 30f, s.Price );
		Assert.AreEqual( 20f, s.CostOfGoods );
		Assert.AreEqual( 800f, s.BuildCost );
	}

	[TestMethod]
	public void ASaleBanksThePriceMinusTheCostOfGoods()
	{
		var fin = new ParkFinances( 100f, 20f );
		fin.TakeFoodSale( 30f, costOfGoods: 20f );
		Assert.AreEqual( 110f, fin.Money, 1e-3f );
		Assert.AreEqual( 30f, fin.FoodRevenue, 1e-3f );
		Assert.AreEqual( 10f, fin.ShopProfit, 1e-3f );
	}
}
