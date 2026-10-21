using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class ItemResearchTests
{
	private static ItemResearch.Item I( string key, BuildCategory c, int group, float cost ) => new( key, c, group, cost, key );

	[TestMethod]
	public void GroupZeroIsAvailableFromTheStart()
	{
		var r = new ItemResearch( new[] { I( "monkey", BuildCategory.Rides, 0, 0 ), I( "gokarts", BuildCategory.Rides, 1, 600 ) } );
		Assert.IsTrue( r.IsUnlocked( "monkey" ) );
		Assert.IsFalse( r.IsUnlocked( "gokarts" ) );
		Assert.IsTrue( r.IsUnlocked( "handyman" ), "not a researchable item" );
	}

	[TestMethod]
	public void EachCategoryResearchesItsCheapestOpenItem()
	{
		var r = new ItemResearch( new[]
		{
			I( "a", BuildCategory.Rides, 0, 0 ), I( "kart", BuildCategory.Rides, 1, 600 ), I( "hyena", BuildCategory.Rides, 1, 450 ),
		}, new Dictionary<BuildCategory, float> { [BuildCategory.Rides] = 100 } );
		Assert.AreEqual( "hyena", r.Target( BuildCategory.Rides )?.Key );
		var done = new List<string>();
		r.Researched += i => done.Add( i.Key );
		r.Tick( 450f / ItemResearch.PointsPerResearcherSecond, 1f ); // one grade-1 researcher, 450 points
		CollectionAssert.AreEqual( new[] { "hyena" }, done );
		Assert.IsTrue( r.IsUnlocked( "hyena" ) );
		Assert.AreEqual( "kart", r.Target( BuildCategory.Rides )?.Key );
	}

	[TestMethod]
	public void LaterGroupsOpenOnceEnoughBelowIsResearched()
	{
		// Group 2 needs 80 % of groups 0–1 researched (ResearchTech[2]).
		var items = new List<ItemResearch.Item> { I( "a", BuildCategory.Rides, 0, 0 ) };
		items.AddRange( Enumerable.Range( 0, 4 ).Select( i => I( $"g1_{i}", BuildCategory.Rides, 1, 100 ) ) );
		items.Add( I( "g2", BuildCategory.Rides, 2, 100 ) );
		var r = new ItemResearch( items, new Dictionary<BuildCategory, float> { [BuildCategory.Rides] = 100 } );
		Assert.AreEqual( 1, r.OpenGroup );
		r.Restore( new[] { "g1_0", "g1_1", "g1_2" } ); // 4 of 5 below = 80 %
		Assert.AreEqual( 2, r.OpenGroup );
	}

	[TestMethod]
	public void TheWorkIsSplitByCategoryEffort()
	{
		var r = new ItemResearch( new[] { I( "ride", BuildCategory.Rides, 1, 1000 ), I( "shop", BuildCategory.Shops, 1, 1000 ) },
			new Dictionary<BuildCategory, float> { [BuildCategory.Rides] = 100, [BuildCategory.Shops] = 25 } );
		r.Tick( 100f / ItemResearch.PointsPerResearcherSecond, 1f ); // 100 points: 80 to rides, 20 to shops
		Assert.AreEqual( 0.08f, r.ProgressOf( "ride" ), 1e-4f );
		Assert.AreEqual( 0.02f, r.ProgressOf( "shop" ), 1e-4f );
	}

	[TestMethod]
	public void ThePlayerCanPickWhatToResearchNext()
	{
		var r = new ItemResearch( new[]
		{
			I( "a", BuildCategory.Rides, 0, 0 ), I( "kart", BuildCategory.Rides, 1, 600 ), I( "hyena", BuildCategory.Rides, 1, 450 ),
		}, new Dictionary<BuildCategory, float> { [BuildCategory.Rides] = 100 } );
		r.Choose( BuildCategory.Rides, +1 );
		Assert.AreEqual( "kart", r.Target( BuildCategory.Rides )?.Key );
		r.Choose( BuildCategory.Rides, +1 ); // wraps round
		Assert.AreEqual( "hyena", r.Target( BuildCategory.Rides )?.Key );
		r.Choose( BuildCategory.Rides, -1 );
		r.Tick( 600f / ItemResearch.PointsPerResearcherSecond, 1f );
		Assert.IsTrue( r.IsUnlocked( "kart" ) );
		Assert.AreEqual( "hyena", r.Target( BuildCategory.Rides )?.Key, "the pick is done: back to the cheapest" );

		var again = new ItemResearch( new[] { I( "a", BuildCategory.Rides, 0, 0 ), I( "kart", BuildCategory.Rides, 1, 600 ), I( "hyena", BuildCategory.Rides, 1, 450 ) } );
		again.RestoreChoices( new Dictionary<string, string> { ["Rides"] = "kart" } );
		Assert.AreEqual( "kart", again.Target( BuildCategory.Rides )?.Key );
	}

	[TestMethod]
	public void HarderWorkIsFasterAndMoreTiring()
	{
		var r = new ItemResearch( System.Array.Empty<ItemResearch.Item>() );
		Assert.AreEqual( 1f, r.RateSpeedFactor );
		r.WorkRate = ItemResearch.MaxRate;
		Assert.IsTrue( r.RateSpeedFactor > 1f && r.RateTiringFactor > r.RateSpeedFactor );
		r.WorkRate = 0;
		Assert.IsTrue( r.RateSpeedFactor < 1f && r.RateTiringFactor < 1f );
	}
}
