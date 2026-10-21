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

	private static ShopBuilding.Stats Stall( string name, int type, float hunger = 0f, float thirst = 0f, float happy = 0f, float fatigue = 0f )
		=> new( name, 30f, 20f, 800f, type, hunger, thirst, happy, fatigue );

	[TestMethod]
	public void ShopTypeAndNameDecideTheProduct()
	{
		Assert.AreEqual( SaleCategory.Gift, ShopBuilding.CategoryOf( Stall( "Gift Shop", 1 ) ) );
		Assert.AreEqual( SaleCategory.Restaurant, ShopBuilding.CategoryOf( Stall( "Moonrock Cafe", 3 ) ) );
		Assert.AreEqual( SaleCategory.Drink, ShopBuilding.CategoryOf( Stall( "Drinks Shop", 4 ) ) );
		Assert.AreEqual( SaleCategory.Costume, ShopBuilding.CategoryOf( Stall( "Costume Shop", 5 ) ) );
		Assert.AreEqual( SaleCategory.Balloon, ShopBuilding.CategoryOf( Stall( "Balloon Shop", 6 ) ) );
		Assert.AreEqual( SaleCategory.Fries, ShopBuilding.CategoryOf( Stall( "Fries Shop", 2 ) ) );
		Assert.AreEqual( SaleCategory.Burger, ShopBuilding.CategoryOf( Stall( "Burger Shop", 2 ) ) );
		Assert.AreEqual( SaleCategory.IceCream, ShopBuilding.CategoryOf( Stall( "Ice Cream Shop", 2 ) ) );
		Assert.AreEqual( SaleCategory.Snack, ShopBuilding.CategoryOf( Stall( "Hot Dogs", 2 ) ) );
		Assert.AreEqual( ShopKind.Item, ShopBuilding.KindOf( 6 ) );
		Assert.AreEqual( ShopKind.Food, ShopBuilding.KindOf( 3 ) );
		Assert.AreEqual( ShopKind.Drink, ShopBuilding.KindOf( 4 ) );
	}

	[TestMethod]
	public void PurchaseEffectsScaleWithTheAuthoredValues()
	{
		// A burger (25 hunger) sates a visitor at the detour threshold; fries (20) relieve 80 % of it.
		var burger = ShopBuilding.ApplyEffects( Stall( "Burger Shop", 2, hunger: 25f, happy: 5f, fatigue: 5f ), 55f, 10f, 50f, 50f, 55f, 50f, 100f );
		Assert.AreEqual( 0f, burger.Hunger, 1e-3f );
		Assert.AreEqual( 10f, burger.Thirst, 1e-3f );
		Assert.AreEqual( 55f, burger.Happiness, 1e-3f );
		Assert.AreEqual( 55f, burger.Energy, 1e-3f );
		var fries = ShopBuilding.ApplyEffects( Stall( "Fries Shop", 2, hunger: 20f ), 55f, 0f, 50f, 50f, 55f, 50f, 100f );
		Assert.AreEqual( 11f, fries.Hunger, 1e-3f );
		// An ice cream (15 hunger, 5 thirst) takes the edge off both.
		var ice = ShopBuilding.ApplyEffects( Stall( "Ice Cream Shop", 2, hunger: 15f, thirst: 5f ), 55f, 50f, 99f, 100f, 55f, 50f, 100f );
		Assert.AreEqual( 22f, ice.Hunger, 1e-3f );
		Assert.AreEqual( 43.75f, ice.Thirst, 1e-3f );
		Assert.AreEqual( 99f, ice.Happiness, 1e-3f );
		Assert.AreEqual( 100f, ice.Energy, 1e-3f );
	}
}
