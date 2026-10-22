namespace OpenTPW;

/// <summary>
/// Player-facing English text for each advisor advice id (T-046). The original's per-message speech is a
/// runtime-assembled message database (the clip→text binding isn't a liftable table — see T-046's RE note),
/// so OpenTPW carries its own readable tip text keyed by the advice id the rule-engine raises
/// (<see cref="AdvisorAdvice.Evaluate"/>). This turns the talking bug head from decoration into a useful
/// hint: the HUD shows what the advisor is actually telling you.
/// <para>Where the game has the same advice in <c>TAG_SYSTEM.str</c> (the original advisor messages), that text is used
/// instead — so it reads as in the original and follows the selected language (<see cref="GameLanguage"/>); the
/// English tips below remain the fallback (and the welcome line, which has no counterpart).</para>
/// </summary>
public static class AdvisorTips
{
	private static readonly Dictionary<string, string> Tips = new()
	{
		["WelcomeTutorial"] = "Welcome to your park! Place a few rides and shops, then open the gates.",
		["WagesHigh"] = "Staff wages are higher than your income — consider letting some staff go.",
		["RidesNoUpgrades"] = "It's about time you researched some ride upgrades.",
		["Bankrupted"] = "Six months in the red: the banks have closed your park.",
		["InTheRedSixMonths"] = "You've lost money for six months — raise the gate fee or sell off a ride before you go bust!",
		["InTheRedThreeMonths"] = "Three months in the red. Push up your prices or trim staff wages.",
		["InTheRedMonthLeft"] = "Money's tight this month — keep an eye on your wages and upkeep.",
		["VisitorsThirsty"] = "Your visitors are getting thirsty. Build a drink stall!",
		["VisitorsHungry"] = "Your visitors are hungry. Build a food stall!",
		["NewResearchGroupRide"] = "A researched ride upgrade is ready — apply it!",
		["CongratVisitorsHappy"] = "Your visitors are having a wonderful time — keep it up!",
		["CongratGoldenTicket"] = "You've won the Golden Ticket — every park goal is met. Magnificent work!",
		["CongratChallengeWon"] = "Challenge complete! The prize is in the bank — ready for the next one?",
		["ChallengeFailed"] = "That challenge slipped away this time. Don't worry — another will come along.",
		["OpenPark"] = "People want to come in but your park is closed. Open the gates!",
		["StaffHireMechanics1"] = "Your rides are wearing out and you have no mechanics.",
		["StaffHireMechanics2"] = "Rides are breaking down and you have no mechanics — hire some!",
		["StaffHireMechanics3"] = "You need more mechanics for this many rides.",
		["StaffHireHandymen1"] = "The park is getting untidy and you have no cleaners.",
		["StaffHireHandymen2"] = "The park is very untidy — hire some cleaners!",
		["StaffHireGuards2"] = "Visitors are misbehaving and you have no guards.",
		["StaffHireEntertainers1"] = "The park is getting busy — hire some entertainers.",
		["RidesBuildRides"] = "Your park has no rides yet.",
		["ShopsBuildShops"] = "You haven't built any shops yet.",
		["LongQueuesBoredVisitors"] = "Visitors are getting bored waiting in your long queues. Shorten the rides, or place an entertainer nearby.",
		["ShopsPoorFood"] = "Visitors think some of your food is poor. Have you considered increasing the quality?",
		["ShopsPoorGoods"] = "Visitors are complaining about the quality of some of your goods. You could increase it.",
		["ShopsTooFatty"] = "If you reduced the fat content of your burgers, your customers might be happier.",
		["ShopsTooSalty"] = "Visitors think your fries are too salty. Perhaps you should reduce the salt?",
		["ShopsTooIcy"] = "Your drinks aren't quenching the visitors' thirst. If you reduced the ice, that might improve.",
		["ShopsTooSweet"] = "Some people think your ice creams are too sweet. You could reduce the sugar.",
		["ShopsNotSweet"] = "Visitors think your ice creams aren't sweet enough. If you increased the sugar, they might buy more.",
		["SideshowsCheapPrizes"] = "Some of your sideshow prizes are too cheap. Increasing the prizes might keep visitors happy.",
		["SideshowsSlimChances"] = "Visitors think their chances of winning some of your sideshows are too slim.",
		["ParkNowOpen"] = "Your park is now open.",
		["ParkNowClosed"] = "Your park is now closed.",
		["PrankStinkBomb"] = "Some prankster has left a stink bomb in your park. Get it cleaned up before too many people get ill.",
		["StaffMaxHandyman"] = "You have employed the maximum number of cleaners for a park: train them to do better.",
		["StaffZoneMechanics"] = "Your mechanics could be more efficient if you set their patrol areas.",
		["StaffZoneHandymen"] = "Your cleaners could do a better job if you set their patrol areas.",
		["StaffZoneGuards"] = "If you set their patrol areas, your guards would be more efficient.",
		["StaffZoneEntertainers"] = "Your entertainers are covering too wide an area. It would help if you set their patrol areas.",
		["StaffMaxEntertainer"] = "You have employed the maximum number of entertainers for a park: train them to do better.",
		["StaffMaxMechanic"] = "You have employed the maximum number of mechanics for a park: train them to do better.",
		["StaffMaxGuard"] = "You have employed the maximum number of guards for a park: train them to do better.",
		["StaffMaxResearcher"] = "You have employed the maximum number of scientists for a park: train them to do better.",
		["SideshowsBuildSideshows"] = "Your park has no sideshows yet.",
		["FeaturesNoToilets"] = "Your park needs some toilets.",
		["ShopsBuildMoreShops"] = "A park this size should have more shops.",
		["SideshowsBuildMoreSideshows"] = "You haven't got many sideshows for a park this size.",
		["FeaturesDirtyToilets1"] = "Your toilets are getting dirty and you have no cleaners.",
		["FeaturesDirtyToilets2"] = "Your toilets are getting dirty — make sure a cleaner patrols them.",
		["FeaturesDirtyToilets3"] = "Your toilets are filthy — hire some cleaners!",
		["FeaturesDirtyToilets4"] = "Your toilets are so filthy they're making people sick.",
		["WealthHigh"] = "You could spend some of your money to improve your park.",
		["WealthHighRepayLoan"] = "You've plenty of money — why not repay your loan?",
		["MaxHandymen"] = "Do you really need so many cleaners?",
		["MaxEntertainers"] = "You have a lot of entertainers for a park this size.",
		["MaxMechanics"] = "You have too many mechanics for a park this size.",
		["MaxGuards"] = "You don't need so many guards in a park this size.",
		["MaxResearchers"] = "You could manage with fewer scientists.",
		["PricingTicketTooExpensive"] = "Visitors think your ticket price is too high.",
		["PricingTicketTooCheap"] = "Visitors think your ticket price is cheap — you could raise it.",
		["PricingFoodTooExpensive"] = "Your food is too expensive.",
		["PricingFoodTooCheap"] = "Your food is very cheap — you could raise the price.",
		["PricingDrinksTooExpensive"] = "Your drinks are too expensive.",
		["PricingDrinksTooCheap"] = "Your drinks are a bargain — you could raise the price.",
		["PricingRestaurantTooExpensive"] = "Your restaurant is too expensive.",
		["PricingRestaurantTooCheap"] = "Your restaurant is very cheap — you could raise the price.",
		["PricingShopsTooExpensive"] = "Your shops are too expensive.",
		["PricingShopsTooCheap"] = "Your shops are cheap — you could raise the price.",
		["PricingSideshowsTooExpensive"] = "Some of your sideshows are too expensive.",
		["PricingSideshowsTooCheap"] = "Your sideshows are cheap — you could raise the price.",
	};

	// Advice id → TAG_SYSTEM.str entry with the same meaning (first line only: the challenge entries continue with
	// a prize amount the game fills in).
	private static readonly Dictionary<string, int> GameText = new()
	{
		["InTheRedMonthLeft"] = 123,   // "You have run out of money! … It might be wise to take out a loan."
		["InTheRedThreeMonths"] = 125, // "You have had no money for three months…"
		["InTheRedSixMonths"] = 126,   // "You have had no money for five months now…" (the last warning)
		["Bankrupted"] = 127,          // "You have been in the red for six months, so the banks have closed your park…"
		["VisitorsThirsty"] = 9,
		["VisitorsHungry"] = 10,
		["WagesHigh"] = 128,           // "Staff wages are higher than your income…"
		["RidesNoUpgrades"] = 68,      // "It's about time you researched some ride upgrades. As yet you don't have any."
		["NewResearchGroupRide"] = 70, // "There are ride upgrades available that you haven't yet used." (68 is "you have none yet")
		["CongratVisitorsHappy"] = 161,
		["CongratGoldenTicket"] = 120,
		["CongratChallengeWon"] = 194, // "Challenge completed!"
		["ChallengeFailed"] = 195,     // "Challenge failed..."
		["OpenPark"] = 7,
		["StaffHireMechanics1"] = 11,
		["StaffHireMechanics2"] = 12,
		["StaffHireMechanics3"] = 13,
		["StaffHireHandymen1"] = 14,
		["StaffHireHandymen2"] = 15,
		["StaffHireGuards2"] = 18,
		["StaffHireEntertainers1"] = 20,
		["RidesBuildRides"] = 67,
		["ShopsBuildShops"] = 77,
		["SideshowsBuildSideshows"] = 81,
		["FeaturesNoToilets"] = 86,
		["FeaturesNotEnoughToilets"] = 88, // "A park this size should have more toilets."
		["LongQueuesBoredVisitors"] = 63,  // "Visitors are getting bored waiting in your long queues. …"
		["ShopsPoorFood"] = 104,           // "Visitors think some of your food is poor. …"
		["ShopsPoorGoods"] = 105,          // "Visitors are complaining about the quality of some of your goods. …"
		["ShopsTooFatty"] = 106, ["ShopsTooSalty"] = 107, ["ShopsTooIcy"] = 108, ["ShopsTooSweet"] = 109, ["ShopsNotSweet"] = 110,
		["SideshowsCheapPrizes"] = 112,    // "… some of your sideshow prizes are too cheap. …"
		["SideshowsSlimChances"] = 113,    // "… their chances of winning some of your sideshows are too slim. …"
		["ParkNowOpen"] = 149, ["ParkNowClosed"] = 150, // "Your park is now open." / "… closed."
		["PrankStinkBomb"] = 146,          // "Some prankster has left a stink bomb in your park. …"
		// "You have employed the maximum number of … for a park … you'll have to train them."
		["StaffMaxHandyman"] = 136, ["StaffMaxEntertainer"] = 138, ["StaffMaxMechanic"] = 140, ["StaffMaxGuard"] = 142,
		["StaffMaxResearcher"] = 144,
		["StaffZoneMechanics"] = 24, ["StaffZoneHandymen"] = 26, ["StaffZoneGuards"] = 29, ["StaffZoneEntertainers"] = 31,
		// Research: hire scientists (21: most rides built, 22: all built) / research more rides (66).
		["StaffHireResearchers1"] = 21, ["StaffHireResearchers2"] = 22, ["RidesResearchMore"] = 66,
		["ShopsBuildMoreShops"] = 78,
		["SideshowsBuildMoreSideshows"] = 82,
		["FeaturesDirtyToilets1"] = 89, // "…getting dirty and you have no cleaners…"
		["FeaturesDirtyToilets2"] = 90, // "…getting dirty. Make sure a cleaner is patrolling them."
		["FeaturesDirtyToilets3"] = 91, // "People are complaining that your toilets are dirty. You should hire some cleaners!"
		["FeaturesDirtyToilets4"] = 92, // "…so filthy they are making people sick…"
		["WealthHigh"] = 121,
		["WealthHighRepayLoan"] = 122,
		["MaxHandymen"] = 129,
		["MaxEntertainers"] = 130,
		["MaxMechanics"] = 131,
		["MaxGuards"] = 132,
		["MaxResearchers"] = 133,
		["PricingTicketTooExpensive"] = 94,
		["PricingTicketTooCheap"] = 95,
		["PricingFoodTooExpensive"] = 96,
		["PricingFoodTooCheap"] = 97,
		["PricingDrinksTooCheap"] = 98,
		["PricingDrinksTooExpensive"] = 99,
		["PricingShopsTooExpensive"] = 100,
		["PricingShopsTooCheap"] = 101,
		["PricingRestaurantTooExpensive"] = 102,
		["PricingRestaurantTooCheap"] = 103,
		["PricingSideshowsTooExpensive"] = 111,
		// Features: build some scenery (85), security cameras (28).
		["FeaturesBuildFeatures"] = 85, ["FeaturesPoorSelection"] = 83, ["StaffGuardBuildCamera"] = 28,
		// Staff (T-049): training, then the strike cycle — TAG_SYSTEM lists each in the order mechanics, cleaners,
		// guards, entertainers, scientists (StaffStrikes.Roles).
		["StaffTrainMechanics"] = 33, ["StaffTrainHandymen"] = 34, ["StaffTrainGuards"] = 35,
		["StaffTrainEntertainers"] = 36, ["StaffTrainResearchers"] = 37,
		["StaffUnhappyMechanics"] = 38, ["StaffUnhappyHandymen"] = 39, ["StaffUnhappyGuards"] = 40,
		["StaffUnhappyEntertainers"] = 41, ["StaffUnhappyResearchers"] = 42,
		["StaffVeryUnhappyMechanics"] = 43, ["StaffVeryUnhappyHandymen"] = 44, ["StaffVeryUnhappyGuards"] = 45,
		["StaffVeryUnhappyEntertainers"] = 46, ["StaffVeryUnhappyResearchers"] = 47,
		["StaffStrikingMechanics"] = 48, ["StaffStrikingHandymen"] = 49, ["StaffStrikingGuards"] = 50,
		["StaffStrikingEntertainers"] = 51, ["StaffStrikingResearchers"] = 52,
		["StaffHappierMechanics"] = 53, ["StaffHappierHandymen"] = 54, ["StaffHappierGuards"] = 55,
		["StaffHappierEntertainers"] = 56, ["StaffHappierResearchers"] = 57,
		["StrikeEndBadMechanics"] = 58, ["StrikeEndBadHandymen"] = 59, ["StrikeEndBadGuards"] = 60,
		["StrikeEndBadEntertainers"] = 61, ["StrikeEndBadResearchers"] = 62,
		// Research announcements: their text is made when raised (Advisor.Announce); the voice is the opening line's.
		["ResearchRideResearched"] = 114, ["ResearchFeatureResearched"] = 118, // (no "too cheap" sideshow line: the game speaks of prizes instead)
	};

	private static string[]? tagSystem;

	/// <summary>The readable tip for an advice id: the game's own message when mapped, else OpenTPW's English tip,
	/// else a generic line.</summary>
	/// <summary>Texts made when the message was raised (a research announcement naming the ride), by message id.</summary>
	public static Dictionary<string, string> Raised { get; } = new();

	public static string TextFor( string messageId )
	{
		if ( messageId != null && Raised.TryGetValue( messageId, out var raised ) )
			return raised;
		if ( string.IsNullOrEmpty( messageId ) )
			return "";
		if ( GameText.TryGetValue( messageId, out var index ) && GameMessage( index ) is { } original )
			return original;
		return Tips.TryGetValue( messageId, out var text ) ? text : "The advisor has something to say...";
	}

	// TAG_SYSTEM.str entry `index`, first line, trimmed; null when unavailable.
	private static string? GameMessage( int index ) => GameMessageLines( index )?[0];

	/// <summary>Every line of <c>TAG_SYSTEM.str</c> entry <paramref name="index"/>, trimmed (the game separates a
	/// message's paragraphs with line breaks); null when unavailable.</summary>
	public static string[]? GameMessageLines( int index )
	{
		if ( tagSystem == null )
		{
			try { tagSystem = new StringFile( GameLanguage.File( "TAG_SYSTEM.str" ) ).Entries; }
			catch { tagSystem = Array.Empty<string>(); }
		}
		if ( index < 0 || index >= tagSystem.Length || string.IsNullOrWhiteSpace( tagSystem[index] ) )
			return null;
		return tagSystem[index].Split( '\n' ).Select( l => l.Trim() ).Where( l => l.Length > 0 ).ToArray();
	}

	/// <summary>The <c>TAG_SYSTEM.str</c> entry an advice id says, when the game has one (for its voice,
	/// <see cref="AdvisorSpeech"/>).</summary>
	public static int? TextIndexOf( string messageId )
		=> messageId != null && GameText.TryGetValue( messageId, out var index ) ? index : null;

	/// <summary>Whether a tip id has authored text (vs. the generic fallback) — for tests.</summary>
	public static bool Has( string messageId ) => Tips.ContainsKey( messageId );
}
