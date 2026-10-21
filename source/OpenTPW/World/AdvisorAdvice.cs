namespace OpenTPW;

/// <summary>A snapshot of the park state the advisor reasons over each consideration tick (T-046): the few
/// figures its tips key off, captured once so the rule evaluation stays pure.</summary>
public readonly record struct ParkSnapshot(
	float Money,
	int MonthsInRed,
	int ThirstyVisitors,
	int HungryVisitors,
	float AverageHappiness,
	bool ResearchAvailable,
	// Edge-triggered milestone flags (the snapshot builder sets these only on the tick the event happens, so the
	// advisor reacts once): the golden ticket was just won (T-055), a challenge was just won / just lost (T-054).
	// Trailing defaults keep existing callers + tests compiling.
	bool GoldenTicketWon = false,
	bool ChallengeWon = false,
	bool ChallengeLost = false,
	// Pricing: the entry fee's opinion (EntryFeeOpinion, 0–100) and whether the fee is above the always-happy
	// minimum; and, per stall group (PriceGroups), how many purchases since the last tick found the price too high /
	// a bargain.
	float TicketSatisfaction = 50f,
	bool FeeAboveMinimum = false,
	int[]? PriceComplaints = null,
	int[]? PriceBargains = null );

/// <summary>
/// Maps the live park state to advisor message candidates — <c>(id, group, score)</c> for the
/// <see cref="AdvisorMessages"/> scheduler — using the scoring parameters decoded from <c>Advisor.sam</c>
/// (<see cref="AdvisorConfig.Param"/>). The advice→group assignment mirrors the original's hardcoded grouping
/// (the file's comment sections: general / tutorial / congrats / research). Pure (unit-tested), so the
/// rules are decoupled from how the snapshot is gathered. See docs/tickets/T-046.
/// </summary>
public static class AdvisorAdvice
{
	// Message group per advice family (Advisor.sam groups: 0 general, 1 tutorial, 3 congrats, 4 research).
	public const int GroupGeneral = 0, GroupTutorial = 1, GroupCongrats = 3, GroupResearch = 4;

	/// <summary>The stall groups the original's price advice speaks about (TAG_SYSTEM.str 96–103), by index.</summary>
	public static readonly string[] PriceGroups = { "Food", "Drinks", "Restaurant", "Shops" };

	/// <summary>The price-advice group of a sale (food stalls, drinks, restaurants, souvenir shops).</summary>
	public static int PriceGroupOf( SaleCategory c ) => c switch
	{
		SaleCategory.Drink => 1,
		SaleCategory.Restaurant => 2,
		SaleCategory.Gift or SaleCategory.Costume or SaleCategory.Balloon => 3,
		_ => 0,
	};

	// Entry-fee advice (Advisor.sam PricingTicketTooExpensive/TooCheap defaults); stall price advice needs this many
	// verdicts in a tick and scores this much per verdict (capped) — the .sam has no stall-pricing entries.
	private const float TicketTooExpensiveUnder = 30f, TicketTooCheapOver = 80f, TicketPricingScore = 150f;
	private const int PriceVerdictsToSpeak = 5;
	private const float PriceScorePerVerdict = 10f, PriceScoreCap = 120f;

	// Default scores when the .sam omits the key (matches the shipped jungle file).
	private const float RedMonthScore = 110f, RedThreeScore = 50f, RedSixScore = 75f;
	private const float ThirstyPerPerson = 1f, HungryPerPerson = 1f, ResearchScore = 40f, CongratScore = 60f;
	private const float CongratHappiness = 75f; // average happiness worth a "well done"
	// Milestone reactions (T-054/T-055): winning the level's golden ticket is the loudest tip; a challenge
	// win/loss is a notable-but-lesser event.
	private const float GoldenTicketScore = 200f, ChallengeWonScore = 90f, ChallengeLostScore = 70f;

	/// <summary>The candidate tips justified by <paramref name="s"/>, scored via <paramref name="c"/>.</summary>
	public static List<(string Id, int Group, float Score)> Evaluate( ParkSnapshot s, AdvisorConfig c )
	{
		var list = new List<(string, int, float)>();

		// Escalating "in the red" warning by how long the park has been losing money (the higher MonthsInRed
		// score wins once the park has been in debt long enough).
		if ( s.Money < 0f )
		{
			if ( s.MonthsInRed >= 6 )
				list.Add( ("InTheRedSixMonths", GroupGeneral, c.Param( "InTheRedSixMonths", "Score" ) ?? RedSixScore) );
			else if ( s.MonthsInRed >= 3 )
				list.Add( ("InTheRedThreeMonths", GroupGeneral, c.Param( "InTheRedThreeMonths", "Score" ) ?? RedThreeScore) );
			else
				list.Add( ("InTheRedMonthLeft", GroupGeneral, c.Param( "InTheRedMonthLeft", "Score" ) ?? RedMonthScore) );
		}

		// Visitors who can't get a drink / food: score per affected visitor (so a thirstier crowd shouts louder).
		if ( s.ThirstyVisitors > 0 )
			list.Add( ("VisitorsThirsty", GroupGeneral,
				(c.Param( "VisitorsThirsty", "ScorePerThirstyPerson" ) ?? ThirstyPerPerson) * s.ThirstyVisitors) );
		if ( s.HungryVisitors > 0 )
			list.Add( ("VisitorsHungry", GroupGeneral,
				(c.Param( "VisitorsHungry", "ScorePerHungryPerson" ) ?? HungryPerPerson) * s.HungryVisitors) );

		// The entry fee, as arriving visitors judge it.
		if ( s.TicketSatisfaction < (c.Param( "PricingTicketTooExpensive", "TicketSatisfactionUnder" ) ?? TicketTooExpensiveUnder) )
			list.Add( ("PricingTicketTooExpensive", GroupGeneral, c.Param( "PricingTicketTooExpensive", "Score" ) ?? TicketPricingScore) );
		else if ( s.FeeAboveMinimum && s.TicketSatisfaction > (c.Param( "PricingTicketTooCheap", "TicketSatisfactionOver" ) ?? TicketTooCheapOver) )
			list.Add( ("PricingTicketTooCheap", GroupGeneral, c.Param( "PricingTicketTooCheap", "Score" ) ?? TicketPricingScore) );

		// Stall prices, as buyers judge them (complaints win over bargains for the same group).
		for ( int g = 0; g < PriceGroups.Length; g++ )
		{
			int complaints = s.PriceComplaints is { } pc && g < pc.Length ? pc[g] : 0;
			int bargains = s.PriceBargains is { } pb && g < pb.Length ? pb[g] : 0;
			if ( complaints >= PriceVerdictsToSpeak )
				list.Add( ($"Pricing{PriceGroups[g]}TooExpensive", GroupGeneral, MathF.Min( PriceScoreCap, complaints * PriceScorePerVerdict )) );
			else if ( bargains >= PriceVerdictsToSpeak )
				list.Add( ($"Pricing{PriceGroups[g]}TooCheap", GroupGeneral, MathF.Min( PriceScoreCap, bargains * PriceScorePerVerdict )) );
		}

		// New research finished and ready to apply.
		if ( s.ResearchAvailable )
			list.Add( ("NewResearchGroupRide", GroupResearch, c.Param( "NewResearchGroupRide", "Score" ) ?? ResearchScore) );

		// Congratulate a happy park.
		if ( s.AverageHappiness >= CongratHappiness )
			list.Add( ("CongratVisitorsHappy", GroupCongrats, c.Param( "CongratVisitorsHappy", "Score" ) ?? CongratScore) );

		// One-shot milestone reactions (the builder edge-triggers these so they speak once).
		if ( s.GoldenTicketWon )
			list.Add( ("CongratGoldenTicket", GroupCongrats, c.Param( "CongratGoldenTicket", "Score" ) ?? GoldenTicketScore) );
		if ( s.ChallengeWon )
			list.Add( ("CongratChallengeWon", GroupCongrats, c.Param( "CongratChallengeWon", "Score" ) ?? ChallengeWonScore) );
		if ( s.ChallengeLost )
			list.Add( ("ChallengeFailed", GroupGeneral, c.Param( "ChallengeFailed", "Score" ) ?? ChallengeLostScore) );

		return list;
	}
}
