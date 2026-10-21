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
		["PricingSideshowsTooExpensive"] = 111, // (no "too cheap" sideshow line: the game speaks of prizes instead)
	};

	private static string[]? tagSystem;

	/// <summary>The readable tip for an advice id: the game's own message when mapped, else OpenTPW's English tip,
	/// else a generic line.</summary>
	public static string TextFor( string messageId )
	{
		if ( string.IsNullOrEmpty( messageId ) )
			return "";
		if ( GameText.TryGetValue( messageId, out var index ) && GameMessage( index ) is { } original )
			return original;
		return Tips.TryGetValue( messageId, out var text ) ? text : "The advisor has something to say...";
	}

	// TAG_SYSTEM.str entry `index`, first line, trimmed; null when unavailable.
	private static string? GameMessage( int index )
	{
		if ( tagSystem == null )
		{
			try { tagSystem = new StringFile( GameLanguage.File( "TAG_SYSTEM.str" ) ).Entries; }
			catch { tagSystem = Array.Empty<string>(); }
		}
		if ( index < 0 || index >= tagSystem.Length || string.IsNullOrWhiteSpace( tagSystem[index] ) )
			return null;
		return tagSystem[index].Split( '\n' )[0].Trim();
	}

	/// <summary>Whether a tip id has authored text (vs. the generic fallback) — for tests.</summary>
	public static bool Has( string messageId ) => Tips.ContainsKey( messageId );
}
