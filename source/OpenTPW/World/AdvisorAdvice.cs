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
	int[]? PriceBargains = null,
	ParkFacilities? Facilities = null );

/// <summary>What the park has built and hired, for the staffing / building advice (T-046).</summary>
public readonly record struct ParkFacilities(
	int Mechanics, int Handymen, int Guards, int Entertainers,
	int Rides, int WornRides, int BrokenRides, int Shops, int Sideshows, int Toilets,
	int Litter, int Visitors, int NeedingToilet, int Misbehaving, int Days, int WaitingAtGate = 0, float WorstToiletFilth = 0f,
	int Researchers = 0, float Money = 0f, float Debt = 0f, bool AnyUpgradeResearched = true,
	float RecentWages = 0f, float RecentIncome = 0f,
	// Staff (T-049): the average grade of each type that is employed, in StaffStrikes.Roles order (NaN = none hired),
	// and the strike announcements (StaffStrikes notices) still to be made.
	float[]? AverageGrades = null, IReadOnlyCollection<string>? StaffNotices = null,
	// Features (scenery) placed, and how many of them are security cameras; -1 = not counted (older callers).
	int Features = -1, int Cameras = 0,
	// The average number of visitors queueing at a toilet (FeaturesNotEnoughToilets).
	float ToiletQueue = 0f,
	// Research (T-044): the kinds of ride the park may build, how many of them it has built, and how many are still
	// to be researched (-1 = not counted).
	int RideKindsAvailable = -1, int RideKindsBuilt = 0, int RidesToResearch = 0,
	// Rides whose queue is full (visitors turned away, bored waiting): LongQueuesBoredVisitors.
	int FullQueues = 0,
	// The shop and sideshow windows' settings gone too far (TAG_SYSTEM 104-113): stalls of poor quality food or goods,
	// too much fat / salt / ice / sugar or too little sugar, prizes too cheap, chances too slim.
	int PoorFood = 0, int PoorGoods = 0, int Fatty = 0, int Salty = 0, int Icy = 0, int TooSweet = 0, int NotSweet = 0,
	int CheapPrizes = 0, int SlimChances = 0,
	// Staff left to roam the whole park, by role, among roles with at least three hired (TAG_SYSTEM 24 / 26 / 29 / 31).
	int UnzonedMechanics = 0, int UnzonedHandymen = 0, int UnzonedGuards = 0, int UnzonedEntertainers = 0,
	// Variety: the kinds of stall and sideshow the park may build and how many of them it has (-1 = not counted).
	int ShopKindsAvailable = -1, int ShopKindsBuilt = 0, int SideshowKindsAvailable = -1, int SideshowKindsBuilt = 0 );

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
	public static readonly string[] PriceGroups = { "Food", "Drinks", "Restaurant", "Shops", "Sideshows" };

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

	// Default scores when the .sam omits the key (matches the shipped jungle file); a strike notice the .sam doesn't
	// score (StaffUnhappy* has 50, StaffStriking* 280…) is still worth saying.
	private const float StrikeNoticeScore = 100f;
	private const float RedMonthScore = 110f, RedThreeScore = 50f, RedSixScore = 75f, BankruptedScore = 100000f;
	private const float ThirstyPerPerson = 1f, HungryPerPerson = 1f, ResearchScore = 40f, CongratScore = 60f;
	private const float CongratHappiness = 75f; // average happiness worth a "well done"
	// Milestone reactions (T-054/T-055): winning the level's golden ticket is the loudest tip; a challenge
	// win/loss is a notable-but-lesser event.
	private const float GoldenTicketScore = 200f, ChallengeWonScore = 90f, ChallengeLostScore = 70f;

	/// <summary>The candidate tips justified by <paramref name="s"/>, scored via <paramref name="c"/>.</summary>
	// How many of a job left without a patrol area are worth the patrol-area advice.
	private const int UnzonedEnough = 3;

	public static List<(string Id, int Group, float Score)> Evaluate( ParkSnapshot s, AdvisorConfig c )
	{
		var list = new List<(string, int, float)>();

		// Escalating "in the red" warning by how long the park has been losing money (the higher MonthsInRed
		// score wins once the park has been in debt long enough).
		if ( s.Money < 0f && s.MonthsInRed >= ParkFinances.BankruptMonths )
			list.Add( ("Bankrupted", GroupGeneral, c.Param( "Bankrupted", "Score" ) ?? BankruptedScore) );
		else if ( s.Money < 0f )
		{
			if ( s.MonthsInRed >= 5 )
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

		if ( s.Facilities is { } f )
			AddFacilityAdvice( list, f, c );

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

	// Staffing / building advice, with Advisor.sam's thresholds and scores (defaults = the shipped file). The ids
	// are the .sam's; their texts are TAG_SYSTEM.str 11–20 / 67 / 77 / 81 / 86 (AdvisorTips).
	private static void AddFacilityAdvice( List<(string, int, float)> list, ParkFacilities f, AdvisorConfig c )
	{
		float P( string id, string key, float fallback ) => c.Param( id, key ) ?? fallback;
		void Add( string id, float score ) { if ( score > 0f ) list.Add( (id, GroupGeneral, score) ); }

		// Mechanics: none while rides wear out / break down; too few for the rides.
		if ( f.Mechanics == 0 && f.BrokenRides > 0 )
			Add( "StaffHireMechanics2", P( "StaffHireMechanics2", "ScorePerBrokenRide", 10f ) * f.BrokenRides );
		else if ( f.Mechanics == 0 && f.WornRides > 0 )
			Add( "StaffHireMechanics1", P( "StaffHireMechanics1", "ScorePerWornRide", 5f ) * f.WornRides );
		else if ( f.Mechanics > 0 )
		{
			float extra = f.Rides - P( "StaffHireMechanics3", "IdealNumberOfRidesPerMechanic", 4f ) * f.Mechanics;
			if ( extra > 0f )
				Add( "StaffHireMechanics3", P( "StaffHireMechanics3", "ScorePerExtraRide", 10f ) * extra );
		}

		// Cleaners: none while litter builds up (a little → a hint, a lot → an order).
		if ( f.Handymen == 0 && f.Litter >= P( "StaffHireHandymen2", "LitterPointCutoffLow", 26f ) )
			Add( "StaffHireHandymen2", P( "StaffHireHandymen2", "ScorePerLitterPoint", 3f ) * f.Litter );
		else if ( f.Handymen == 0 && f.Litter >= P( "StaffHireHandymen1", "LitterPointCutoffLow", 3f ) )
			Add( "StaffHireHandymen1", P( "StaffHireHandymen1", "ScorePerLitterPoint", 3f ) * f.Litter );

		// Guards: none while visitors misbehave. Entertainers: none in a busy park.
		if ( f.Guards == 0 && f.Misbehaving > 0 )
			Add( "StaffHireGuards2", P( "StaffHireGuards2", "Score", 10f ) + P( "StaffHireGuards2", "AdditionalPerMisbehavingKid", 5f ) * f.Misbehaving );
		float busy = P( "StaffHireEntertainers1", "AtLeastThisBusy", 35f );
		if ( f.Entertainers == 0 && f.Visitors >= busy )
			Add( "StaffHireEntertainers1", MathF.Min( FacilityScoreCap, P( "StaffHireEntertainers1", "ScorePerExtraBusyPoint", 3f ) * (f.Visitors - busy + 1) ) );

		// Dirty toilets (FeaturesDirtyToilets1–4): dirty (> 60) / filthy (> 90), with or without cleaners.
		if ( f.Toilets > 0 && f.WorstToiletFilth > P( "FeaturesDirtyToilets1", "FilthierThan", 60f ) )
		{
			bool filthy = f.WorstToiletFilth > P( "FeaturesDirtyToilets3", "FilthierThan", 90f );
			string id = filthy ? (f.Handymen == 0 ? "FeaturesDirtyToilets3" : "FeaturesDirtyToilets4")
				: (f.Handymen == 0 ? "FeaturesDirtyToilets1" : "FeaturesDirtyToilets2");
			Add( id, P( id, "PointsPerFilthPct", filthy ? 2f : 1f ) * f.WorstToiletFilth );
		}

		// Research: most of the available rides built and nobody researching more (StaffHireResearchers1: past Most 70 %,
		// PointsPerExtraPct 5; StaffHireResearchers2 when all are built), or all built with scientists (RidesResearchMore).
		if ( f.RideKindsAvailable > 0 && f.RidesToResearch > 0 )
		{
			float pct = 100f * f.RideKindsBuilt / f.RideKindsAvailable;
			if ( f.Researchers == 0 && f.RideKindsBuilt >= f.RideKindsAvailable )
				Add( "StaffHireResearchers2", P( "StaffHireResearchers2", "Score", 90f ) );
			else if ( f.Researchers == 0 && pct > P( "StaffHireResearchers1", "Most", 70f ) )
				Add( "StaffHireResearchers1", (pct - P( "StaffHireResearchers1", "Most", 70f )) * P( "StaffHireResearchers1", "PointsPerExtraPct", 5f ) );
			else if ( f.Researchers > 0 && f.RideKindsBuilt >= f.RideKindsAvailable )
				Add( "RidesResearchMore", P( "RidesResearchMore", "Score", 100f ) );
		}

		// Toilet queues (FeaturesNotEnoughToilets.AverageQueueLongerThan 2.5): "a park this size should have more".
		if ( f.Toilets > 0 && f.ToiletQueue > P( "FeaturesNotEnoughToilets", "AverageQueueLongerThan", 2.5f ) )
			Add( "FeaturesNotEnoughToilets", P( "FeaturesNotEnoughToilets", "Score", 100f ) );

		// Full queues (LongQueuesBoredVisitors; Advisor.sam gives it no parameters): shorter rides, or an entertainer.
		if ( f.FullQueues > 0 )
			Add( "LongQueuesBoredVisitors", P( "LongQueuesBoredVisitors", "Score", 60f ) + 10f * (f.FullQueues - 1) );

		// A poor selection (<X>PoorSelection.PoorerThan % of the kinds on offer built, with more than MoreXThan of them):
		// "your selection of rides / shops / sideshows isn't so good".
		void Selection( string id, string more, int count, int built, int available, float poorerThan, float moreThan )
		{
			if ( available > 0 && count > P( id, more, moreThan ) && 100f * built / available < P( id, "PoorerThan", poorerThan ) )
				Add( id, P( id, "Score", 40f ) );
		}
		Selection( "RidesPoorSelection", "MoreRidesThan", f.Rides, f.RideKindsBuilt, f.RideKindsAvailable, 30f, 3f );
		Selection( "ShopsPoorSelection", "MoreShopsThan", f.Shops, f.ShopKindsBuilt, f.ShopKindsAvailable, 25f, 2f );
		Selection( "SideshowsPoorSelection", "MoreSideshowsThan", f.Sideshows, f.SideshowKindsBuilt, f.SideshowKindsAvailable, 15f, 2f );

		// Three or more of a job and none given a patrol area: "they'd be more efficient if you set their patrol areas".
		foreach ( var (id, count) in new[] { ("StaffZoneMechanics", f.UnzonedMechanics), ("StaffZoneHandymen", f.UnzonedHandymen),
			("StaffZoneGuards", f.UnzonedGuards), ("StaffZoneEntertainers", f.UnzonedEntertainers) } )
			if ( count >= UnzonedEnough )
				Add( id, P( id, "Score", 40f ) );

		// The shop and sideshow settings (the windows' sliders): what visitors complain about.
		foreach ( var (id, count) in new[] { ("ShopsPoorFood", f.PoorFood), ("ShopsPoorGoods", f.PoorGoods), ("ShopsTooFatty", f.Fatty),
			("ShopsTooSalty", f.Salty), ("ShopsTooIcy", f.Icy), ("ShopsTooSweet", f.TooSweet), ("ShopsNotSweet", f.NotSweet),
			("SideshowsCheapPrizes", f.CheapPrizes), ("SideshowsSlimChances", f.SlimChances) } )
			if ( count > 0 )
				Add( id, P( id, "Score", 50f ) + 10f * (count - 1) );

		// Money to spare: spend it, or repay what's owed (WealthHigh / WealthHighRepayLoan).
		if ( f.Debt > 0f && f.Money > f.Debt * P( "WealthHighRepayLoan", "Margin", 1.2f ) )
			Add( "WealthHighRepayLoan", P( "WealthHighRepayLoan", "Score", 90f ) );
		else if ( f.Money > P( "WealthHigh", "MoreMoneyThan", 200000f ) )
			Add( "WealthHigh", P( "WealthHigh", "Score", 40f ) );

		// Wages swallowing the income (WagesHigh: wages over the last AverageOverMonths > WagesTimesHigherThanIncome ×).
		if ( f.RecentWages > 0f && f.RecentWages > P( "WagesHigh", "WagesTimesHigherThanIncome", 2f ) * f.RecentIncome )
			Add( "WagesHigh", P( "WagesHigh", "Score", 90f ) );

		// Scenery: none at all (FeaturesBuildFeatures: a negative base that grows by the day, like RidesBuildRides).
		if ( f.Features == 0 && f.Rides > 0 )
			Add( "FeaturesBuildFeatures", P( "FeaturesBuildFeatures", "Score", -40f ) + P( "FeaturesBuildFeatures", "AdditionalPerDay", 1f ) * f.Days );
		// Trouble and no camera to help the guards (StaffGuardBuildCamera.AcceptableTrouble 4).
		if ( f.Features >= 0 && f.Cameras == 0 && f.Guards > 0 && f.Misbehaving > P( "StaffGuardBuildCamera", "AcceptableTrouble", 4f ) )
			Add( "StaffGuardBuildCamera", P( "StaffGuardBuildCamera", "Score", 45f ) );

		// Strikes: the game's own announcements (warning, reminder, strike, called off, back to work).
		if ( f.StaffNotices is { } notices )
			foreach ( var id in notices )
				Add( id, P( id, "Score", StrikeNoticeScore ) );

		// Poorly trained staff (StaffTrain*: an average grade worse than WorseThan 1).
		if ( f.AverageGrades is { } grades )
			for ( int i = 0; i < grades.Length && i < StaffStrikes.Roles.Length; i++ )
			{
				string id = $"StaffTrain{StaffStrikes.AdvisorName( StaffStrikes.Roles[i] )}";
				if ( !float.IsNaN( grades[i] ) && grades[i] < P( id, "WorseThan", 1f ) )
					Add( id, P( id, "Score", 30f ) );
			}

		// Too many of one kind of staff (Max*.MoreThan 10).
		foreach ( var (id, hired) in new[] { ("MaxHandymen", f.Handymen), ("MaxEntertainers", f.Entertainers),
			("MaxMechanics", f.Mechanics), ("MaxGuards", f.Guards), ("MaxResearchers", f.Researchers) } )
			if ( hired > P( id, "MoreThan", 10f ) )
				Add( id, P( id, "Score", 80f ) );

		// People want to come in but the gates are shut.
		if ( f.WaitingAtGate > 0 )
			Add( "OpenPark", P( "OpenPark", "ScorePerWaitingPerson", 20f ) * f.WaitingAtGate );

		// Nothing built yet: the nudge grows by the day (the .sam's negative base delays it).
		if ( f.Rides == 0 )
			Add( "RidesBuildRides", P( "RidesBuildRides", "Score", -50f ) + P( "RidesBuildRides", "AdditionalPerDay", 3f ) * f.Days );
		if ( f.Shops == 0 )
			Add( "ShopsBuildShops", P( "ShopsBuildShops", "Score", 50f ) + P( "ShopsBuildShops", "AdditionalPerDay", 2f ) * f.Days );
		if ( f.Sideshows == 0 )
			Add( "SideshowsBuildSideshows", P( "SideshowsBuildSideshows", "Score", -70f ) + P( "SideshowsBuildSideshows", "AdditionalPerDay", 2f ) * f.Days );

		// A rich park short of shops / sideshows for its rides (…BuildMore….*PerRideRatio, WealthMoreThan).
		if ( f.Rides > 0 && f.Shops > 0 && f.Money > P( "ShopsBuildMoreShops", "WealthMoreThan", 20000f )
			&& f.Shops < P( "ShopsBuildMoreShops", "ShopsPerRideRatio", 0.15f ) * f.Rides )
			Add( "ShopsBuildMoreShops", P( "ShopsBuildMoreShops", "Score", 70f ) );
		if ( f.Rides > 0 && f.Sideshows > 0 && f.Money > P( "SideshowsBuildMoreSideshows", "WealthMoreThan", 20000f )
			&& f.Sideshows < P( "SideshowsBuildMoreSideshows", "SideshowsPerRideRatio", 0.2f ) * f.Rides )
			Add( "SideshowsBuildMoreSideshows", P( "SideshowsBuildMoreSideshows", "Score", 80f ) );

		// Well into the game without a single ride upgrade researched (RidesNoUpgrades).
		if ( f.Rides > 0 && !f.AnyUpgradeResearched
			&& f.Days >= P( "RidesNoUpgrades", "MonthsIntoGameForConsideration", 18f ) * GameClock.DaysPerMonth )
			Add( "RidesNoUpgrades", P( "RidesNoUpgrades", "Score", 80f ) );

		// No toilets while enough visitors need one.
		if ( f.Toilets == 0 && f.NeedingToilet > P( "FeaturesNoToilets", "MorePeopleThanThisNeeding", 25f ) )
			Add( "FeaturesNoToilets", P( "FeaturesNoToilets", "Score", 90f ) );
	}

	private const float FacilityScoreCap = 300f;
}
