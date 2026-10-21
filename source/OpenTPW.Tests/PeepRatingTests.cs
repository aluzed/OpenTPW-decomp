using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PeepRatingTests
{
	// Ride ratings & thoughts (T-050): Peep.RateRide blends excitement, value-for-money (price vs the
	// "fair" price ~ excitement/10) and reliability into a 0..100 satisfaction + a dominant thought.

	[TestMethod]
	public void ExcitingAndFairlyPricedIsAGreatRide()
	{
		var (sat, thought) = Peep.RateRide( excitement: 80, ticketPrice: 8f, reliability: 1f );
		Assert.AreEqual( 80f, sat, 1f );           // fair price → no value delta
		Assert.AreEqual( RideThought.GreatRide, thought );
	}

	[TestMethod]
	public void OverpricedRideIsResented()
	{
		var (sat, thought) = Peep.RateRide( 80, 20f, 1f ); // fair ~8, charged 20
		Assert.AreEqual( RideThought.TooExpensive, thought );
		Assert.IsTrue( sat < 80f, "an overpriced ride scores below its excitement" );
	}

	[TestMethod]
	public void CheapExcitingRideIsGoodValue()
	{
		var (sat, thought) = Peep.RateRide( 80, 2f, 1f ); // fair ~8, charged 2
		Assert.AreEqual( RideThought.GoodValue, thought );
		Assert.IsTrue( sat >= 80f, "a bargain scores at least its excitement" );
	}

	[TestMethod]
	public void UnreliableRideTanksSatisfaction()
	{
		var (sat, thought) = Peep.RateRide( 80, 8f, reliability: 0.2f );
		Assert.AreEqual( RideThought.Unreliable, thought );
		Assert.IsTrue( sat < 50f, "an unreliable ride is unsatisfying despite the excitement" );
	}

	[TestMethod]
	public void DullRideIsRubbish()
	{
		var (sat, thought) = Peep.RateRide( 20, 2f, 1f );
		Assert.IsTrue( sat <= 30f );
		Assert.AreEqual( RideThought.Rubbish, thought );
	}

	[TestMethod]
	public void AveragePricedAverageRideIsMediocre()
	{
		var (_, thought) = Peep.RateRide( 50, 5f, 1f );
		Assert.AreEqual( RideThought.Mediocre, thought );
	}

	[TestMethod]
	public void CheaperIsAlwaysAtLeastAsSatisfying()
	{
		var dear = Peep.RateRide( 60, 12f, 1f ).Satisfaction;
		var cheap = Peep.RateRide( 60, 4f, 1f ).Satisfaction;
		Assert.IsTrue( cheap >= dear, "lowering the price never lowers satisfaction" );
	}

	[TestMethod]
	public void EveryThoughtHasReadableTextAndAShortTag()
	{
		foreach ( RideThought t in System.Enum.GetValues<RideThought>() )
		{
			Assert.IsFalse( string.IsNullOrWhiteSpace( RideThoughtText.For( t ) ), $"{t} has a full line" );
			Assert.IsFalse( string.IsNullOrWhiteSpace( RideThoughtText.Tag( t ) ), $"{t} has a HUD tag" );
		}
		// A couple of representative wordings (so the mapping can't silently swap).
		StringAssert.Contains( RideThoughtText.For( RideThought.TooExpensive ), "expensive" );
		Assert.AreEqual( "Unsafe", RideThoughtText.Tag( RideThought.Unreliable ) );
		Assert.AreEqual( "Great", RideThoughtText.Tag( RideThought.GreatRide ) );
	}

	[TestMethod]
	public void AFreeRideHasNoValueForMoneyTerm()
	{
		// Rides are free in TPW: satisfaction is the excitement (minus unreliability), never "good value".
		var (sat, thought) = Peep.RateRide( 60, 0f, 1f );
		Assert.AreEqual( 60f, sat, 1e-3f );
		Assert.AreNotEqual( RideThought.GoodValue, thought );
		Assert.AreNotEqual( RideThought.TooExpensive, thought );
	}
}
