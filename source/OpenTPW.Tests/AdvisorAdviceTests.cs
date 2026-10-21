using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class AdvisorAdviceTests
{
	// Real-ish .sam scoring params for the advice rules under test.
	private const string Sam = @"
VisitorsThirsty.ScorePerThirstyPerson	2
VisitorsThirsty.ThirstierThan			80
VisitorsHungry.ScorePerHungryPerson		3
InTheRedSixMonths.Score					75
InTheRedThreeMonths.Score				50
InTheRedMonthLeft.Score					110
NewResearchGroupRide.Score				40
CongratVisitorsHappy.Score				60
";

	private static AdvisorConfig Cfg() => new( new SAMParser( Sam ).Parse() );

	// A healthy, solvent, empty-ish park: no advice.
	private static ParkSnapshot Healthy() =>
		new( Money: 5000f, MonthsInRed: 0, ThirstyVisitors: 0, HungryVisitors: 0, AverageHappiness: 50f, ResearchAvailable: false );

	[TestMethod]
	public void HealthyParkProducesNoAdvice()
	{
		Assert.AreEqual( 0, AdvisorAdvice.Evaluate( Healthy(), Cfg() ).Count );
	}

	[TestMethod]
	public void InTheRedEscalatesWithMonths()
	{
		var c = Cfg();
		string One( ParkSnapshot s ) => AdvisorAdvice.Evaluate( s, c ).Single( a => a.Id.StartsWith( "InTheRed" ) ).Id;

		Assert.AreEqual( "InTheRedMonthLeft", One( Healthy() with { Money = -100f, MonthsInRed = 1 } ) );
		Assert.AreEqual( "InTheRedThreeMonths", One( Healthy() with { Money = -100f, MonthsInRed = 3 } ) );
		Assert.AreEqual( "InTheRedSixMonths", One( Healthy() with { Money = -100f, MonthsInRed = 8 } ) );
	}

	[TestMethod]
	public void NoRedAdviceWhileSolventEvenWithStaleCounter()
	{
		// MonthsInRed only matters while Money < 0.
		var advice = AdvisorAdvice.Evaluate( Healthy() with { Money = 10f, MonthsInRed = 6 }, Cfg() );
		Assert.IsFalse( advice.Any( a => a.Id.StartsWith( "InTheRed" ) ) );
	}

	[TestMethod]
	public void ThirstyAndHungryScalePerVisitorWithSamParams()
	{
		var c = Cfg();
		var advice = AdvisorAdvice.Evaluate( Healthy() with { ThirstyVisitors = 4, HungryVisitors = 2 }, c );

		var thirst = advice.Single( a => a.Id == "VisitorsThirsty" );
		var hunger = advice.Single( a => a.Id == "VisitorsHungry" );
		Assert.AreEqual( 2f * 4, thirst.Score, 1e-4f, "ScorePerThirstyPerson(2) × 4" );
		Assert.AreEqual( 3f * 2, hunger.Score, 1e-4f, "ScorePerHungryPerson(3) × 2" );
		Assert.AreEqual( AdvisorAdvice.GroupGeneral, thirst.Group );
	}

	[TestMethod]
	public void ResearchAndCongratsFireOnState()
	{
		var c = Cfg();
		var research = AdvisorAdvice.Evaluate( Healthy() with { ResearchAvailable = true }, c );
		Assert.IsTrue( research.Any( a => a.Id == "NewResearchGroupRide" && a.Group == AdvisorAdvice.GroupResearch ) );

		var congrats = AdvisorAdvice.Evaluate( Healthy() with { AverageHappiness = 80f }, c );
		var happy = congrats.Single( a => a.Id == "CongratVisitorsHappy" );
		Assert.AreEqual( 60f, happy.Score, 1e-4f );
		Assert.AreEqual( AdvisorAdvice.GroupCongrats, happy.Group );

		// Just below the congrats threshold: nothing.
		Assert.IsFalse( AdvisorAdvice.Evaluate( Healthy() with { AverageHappiness = 74f }, c )
			.Any( a => a.Id == "CongratVisitorsHappy" ) );
	}

	[TestMethod]
	public void MissingSamParamsFallBackToDefaults()
	{
		var empty = new AdvisorConfig( System.Array.Empty<SettingsPair>() );
		var advice = AdvisorAdvice.Evaluate( Healthy() with { Money = -1f, MonthsInRed = 1, ThirstyVisitors = 3 }, empty );
		Assert.AreEqual( 110f, advice.Single( a => a.Id == "InTheRedMonthLeft" ).Score, 1e-4f, "default red score" );
		Assert.AreEqual( 1f * 3, advice.Single( a => a.Id == "VisitorsThirsty" ).Score, 1e-4f, "default 1/thirsty person" );
	}

	[TestMethod]
	public void MilestoneFlagsRaiseOneShotCongratsAndCommiseration()
	{
		var c = Cfg();

		// A healthy park with no milestone flagged → no milestone advice.
		Assert.IsFalse( AdvisorAdvice.Evaluate( Healthy(), c ).Any( a => a.Id.StartsWith( "Congrat" ) || a.Id == "ChallengeFailed" ) );

		var ticket = AdvisorAdvice.Evaluate( Healthy() with { GoldenTicketWon = true }, c )
			.Single( a => a.Id == "CongratGoldenTicket" );
		Assert.AreEqual( AdvisorAdvice.GroupCongrats, ticket.Group );
		Assert.AreEqual( 200f, ticket.Score, 1e-4f, "the golden ticket is the loudest tip" );

		var won = AdvisorAdvice.Evaluate( Healthy() with { ChallengeWon = true }, c );
		Assert.IsTrue( won.Any( a => a.Id == "CongratChallengeWon" && a.Group == AdvisorAdvice.GroupCongrats ) );

		var lost = AdvisorAdvice.Evaluate( Healthy() with { ChallengeLost = true }, c );
		Assert.IsTrue( lost.Any( a => a.Id == "ChallengeFailed" && a.Group == AdvisorAdvice.GroupGeneral ) );
	}

	[TestMethod]
	public void PricingAdviceFollowsTheVerdicts()
	{
		var c = new AdvisorConfig( System.Array.Empty<SettingsPair>() );
		var dear = AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false, TicketSatisfaction: 20f ), c );
		Assert.IsTrue( dear.Any( a => a.Id == "PricingTicketTooExpensive" && a.Score == 150f ) );
		var cheapAtMinimum = AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false, TicketSatisfaction: 100f ), c );
		Assert.IsFalse( cheapAtMinimum.Any( a => a.Id.StartsWith( "PricingTicket" ) ), "the minimum fee is not 'too cheap'" );
		var cheap = AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false, TicketSatisfaction: 100f, FeeAboveMinimum: true ), c );
		Assert.IsTrue( cheap.Any( a => a.Id == "PricingTicketTooCheap" ) );

		var stalls = AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false,
			PriceComplaints: new[] { 6, 0, 0, 2 }, PriceBargains: new[] { 9, 7, 0, 0 } ), c );
		Assert.IsTrue( stalls.Any( a => a.Id == "PricingFoodTooExpensive" ), "complaints win over bargains" );
		Assert.IsFalse( stalls.Any( a => a.Id == "PricingFoodTooCheap" ) );
		Assert.IsTrue( stalls.Any( a => a.Id == "PricingDrinksTooCheap" ) );
		Assert.IsFalse( stalls.Any( a => a.Id.StartsWith( "PricingShops" ) ), "2 complaints aren't enough" );
		Assert.AreEqual( 3, AdvisorAdvice.PriceGroupOf( SaleCategory.Balloon ) );
	}
}
