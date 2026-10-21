using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ChallengeMetricsTests
{
	private static Challenge OfType( int type, int target = 100, int time = 60 )
		=> new( 1, type, 0, time, target, 0, 0, 0, 1000f, false, true );

	[TestMethod]
	public void SupportsTheDecodedTypes()
	{
		foreach ( var t in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16, 18, 20, 24 } )
			Assert.IsTrue( ChallengeMetrics.IsSupported( t ), $"type {t} is decoded + tracked" );
		// Still-opaque types stay unsupported (skipped by the manager).
		foreach ( var t in new[] { 19, 25 } )
			Assert.IsFalse( ChallengeMetrics.IsSupported( t ), $"type {t} has no counter yet" );
	}

	[TestMethod]
	public void DescribesEachTypeFromTheSamSemantics()
	{
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 1, 200 ) ), "fries" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 2, 30 ) ), "burgers" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 3, 30 ) ), "drinks" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 11, 100 ) ), "new visitors" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 13, 500 ) ), "sideshow" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 20, 200 ) ), "ride" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 12, 500 ) ), "shops" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 16, 2 ) ), "Research" );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 18, 1 ) ), "coaster" );
	}

	[TestMethod]
	public void CurrentReadsTheCounterEachTypeMeasures()
	{
		var saved = ParkFinances.Current;
		try
		{
			var fin = new ParkFinances( 10000f, 5f );
			ParkFinances.Current = fin;

			fin.TakeFoodSale( 3f, category: SaleCategory.Fries ); // a portion of fries
			fin.TakeFoodSale( 3f, drink: true );    // drink
			fin.TakeEntryFee();                     // a visitor
			fin.TakeRideTicket( 4f );               // a ride ridden
			fin.TakeSideshowTakings( 25f, won: false ); // sideshow profit
			fin.NoteCoasterBuilt();                 // a coaster placed
			fin.NoteResearchCompleted();            // a ride research done
			fin.NoteResearchCompleted();

			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 1 ) ), "type 1 → fries sold" );
			Assert.AreEqual( 0f, ChallengeMetrics.Current( OfType( 2 ) ), "type 2 → burgers sold (none)" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 3 ) ), "type 3 → DrinkSold" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 11 ) ), "type 11 → VisitorsTotal" );
			Assert.AreEqual( 25f, ChallengeMetrics.Current( OfType( 13 ) ), "type 13 → SideshowRevenue" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 20 ) ), "type 20 → RidesRidden" );
			Assert.AreEqual( 6f, ChallengeMetrics.Current( OfType( 12 ) ), "type 12 → concession takings (food + drink)" );
			Assert.AreEqual( 2f, ChallengeMetrics.Current( OfType( 16 ) ), "type 16 → ResearchCompleted" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 18 ) ), "type 18 → CoastersBuilt" );
			Assert.AreEqual( 0f, ChallengeMetrics.Current( OfType( 99 ) ), "an unmapped type reads 0" );
		}
		finally { ParkFinances.Current = saved; }
	}

	[TestMethod]
	public void ProductChallengesCountTheirOwnSales()
	{
		var saved = ParkFinances.Current;
		try
		{
			var fin = new ParkFinances( 10000f, 5f );
			ParkFinances.Current = fin;
			foreach ( var c in new[] { SaleCategory.IceCream, SaleCategory.Gift, SaleCategory.Gift, SaleCategory.Balloon,
				SaleCategory.Costume, SaleCategory.Restaurant, SaleCategory.Burger } )
				fin.TakeFoodSale( 10f, category: c );

			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 4 ) ), "type 4 → ice creams" );
			Assert.AreEqual( 2f, ChallengeMetrics.Current( OfType( 5 ) ), "type 5 → gifts" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 6 ) ), "type 6 → balloons" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 7 ) ), "type 7 → costumes" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 8 ) ), "type 8 → restaurant meals" );
			Assert.AreEqual( 1f, ChallengeMetrics.Current( OfType( 2 ) ), "type 2 → burgers" );
			// Souvenirs are neither food nor drink, but still shop takings.
			Assert.AreEqual( 3, fin.FoodSold, "ice cream + restaurant + burger" );
			Assert.AreEqual( 70f, fin.FoodRevenue, 1e-3f );
		}
		finally { ParkFinances.Current = saved; }
	}

	[TestMethod]
	public void ShareOfVisitorsTypesAreLevelsNotGains()
	{
		Assert.IsTrue( ChallengeMetrics.IsLevel( 9 ) && ChallengeMetrics.IsLevel( 10 ) && ChallengeMetrics.IsLevel( 24 ) );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 24, 20, 90 ) ), "toilets 20% clean" );
		Assert.IsFalse( ChallengeMetrics.IsLevel( 6 ) );
		StringAssert.Contains( ChallengeMetrics.Describe( OfType( 9, 15 ) ), "15%" );
	}
}
