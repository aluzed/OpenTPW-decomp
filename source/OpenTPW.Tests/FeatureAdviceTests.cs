using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class FeatureAdviceTests
{
	private static System.Collections.Generic.List<string> Ids( ParkFacilities f )
		=> AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false, Facilities: f ), new AdvisorConfig( Array.Empty<SettingsPair>() ) )
			.Select( x => x.Id ).ToList();

	[TestMethod]
	public void NoSceneryNagGrowsByTheDay()
	{
		var early = new ParkFacilities( 1, 1, 1, 1, 3, 0, 0, 2, 0, 1, 0, 50, 0, 0, Days: 10, Features: 0 );
		CollectionAssert.DoesNotContain( Ids( early ), "FeaturesBuildFeatures", "-40 + 10 days is not worth saying yet" );
		CollectionAssert.Contains( Ids( early with { Days = 120 } ), "FeaturesBuildFeatures" );
		CollectionAssert.DoesNotContain( Ids( early with { Days = 120, Features = 3 } ), "FeaturesBuildFeatures" );
	}

	[TestMethod]
	public void OverdoneStallsAndMeanSideshowsDrawComplaints()
	{
		var park = new ParkFacilities( 1, 1, 1, 1, 3, 0, 0, 2, 0, 1, 0, 50, 0, 0, Days: 10, Features: 2 );
		var ids = Ids( park with { Salty = 1, NotSweet = 1, SlimChances = 2 } );
		CollectionAssert.Contains( ids, "ShopsTooSalty" );
		CollectionAssert.Contains( ids, "ShopsNotSweet" );
		CollectionAssert.Contains( ids, "SideshowsSlimChances" );
		CollectionAssert.DoesNotContain( ids, "ShopsPoorFood" );
	}

	[TestMethod]
	public void FullQueuesBoreVisitors()
	{
		var park = new ParkFacilities( 1, 1, 1, 1, 3, 0, 0, 2, 0, 1, 0, 50, 0, 0, Days: 10, Features: 2 );
		CollectionAssert.DoesNotContain( Ids( park ), "LongQueuesBoredVisitors" );
		CollectionAssert.Contains( Ids( park with { FullQueues = 2 } ), "LongQueuesBoredVisitors" );
	}

	[TestMethod]
	public void TroubleWithoutACameraAsksForOne()
	{
		var f = new ParkFacilities( 1, 1, 2, 1, 3, 0, 0, 2, 0, 1, 0, 50, 0, Misbehaving: 6, Days: 10, Features: 2, Cameras: 0 );
		CollectionAssert.Contains( Ids( f ), "StaffGuardBuildCamera" );
		CollectionAssert.DoesNotContain( Ids( f with { Cameras = 1 } ), "StaffGuardBuildCamera" );
		CollectionAssert.DoesNotContain( Ids( f with { Misbehaving = 3 } ), "StaffGuardBuildCamera" );
	}

	[TestMethod]
	public void CatalogItemsSortIntoTheBuildTabs()
	{
		Assert.AreEqual( BuildCategory.Features, new BuildCatalogItem( "camera", "levels/jungle/features/camera", null, 1, 1, 250f ).Category );
		Assert.AreEqual( BuildCategory.Sideshows, new BuildCatalogItem( "hyenas", "levels/jungle/sideshow/hyenas", null, 2, 2, 1750f ).Category );
		Assert.AreEqual( BuildCategory.Rides, new BuildCatalogItem( "totem", "levels/jungle/rides/totem", null, 3, 4, 3250f ).Category );
		Assert.AreEqual( BuildCategory.Shops, new BuildCatalogItem( "drink", null, null, 2, 2, 650f ).Category );
		Assert.AreEqual( BuildCategory.Staff, new BuildCatalogItem( "guard", null, StaffRole.Guard, 1, 1, 1000f ).Category );
	}

	[TestMethod]
	public void LongToiletQueuesAskForMoreToilets()
	{
		var f = new ParkFacilities( 1, 1, 1, 1, 3, 0, 0, 2, 0, Toilets: 1, Litter: 0, Visitors: 80, NeedingToilet: 0, Misbehaving: 0, Days: 10, Features: 2, ToiletQueue: 3f );
		CollectionAssert.Contains( Ids( f ), "FeaturesNotEnoughToilets" );
		CollectionAssert.DoesNotContain( Ids( f with { ToiletQueue = 1f } ), "FeaturesNotEnoughToilets" );
	}

	[TestMethod]
	public void ResearchAdviceFollowsWhatsBuiltAndWhoResearches()
	{
		var f = new ParkFacilities( 1, 1, 1, 1, 3, 0, 0, 2, 0, 1, 0, 50, 0, 0, Days: 10, Features: 2,
			RideKindsAvailable: 4, RideKindsBuilt: 3, RidesToResearch: 6, Researchers: 0 );
		CollectionAssert.Contains( Ids( f ), "StaffHireResearchers1", "75 % built, nobody researching" );
		CollectionAssert.Contains( Ids( f with { RideKindsBuilt = 4 } ), "StaffHireResearchers2", "all built" );
		CollectionAssert.Contains( Ids( f with { RideKindsBuilt = 4, Researchers = 2 } ), "RidesResearchMore" );
		CollectionAssert.DoesNotContain( Ids( f with { RidesToResearch = 0, RideKindsBuilt = 4 } ), "StaffHireResearchers2", "nothing left to research" );
	}
}
