namespace OpenTPW;

/// <summary>
/// Maps a <see cref="Challenge.Type"/> to the live park counter it measures + a human description (T-054).
/// <para><b>Type semantics</b> are taken from <c>Challenges.sam</c>'s own per-challenge comments (EA's authored
/// descriptions), which decode the numeric types: 1 = sell fries, 2 = sell burgers, 3 = sell drinks, 4 = sell ice
/// creams, 5 = sell gifts, 6 = kids with balloons, 7 = sell costumes, 8 = restaurant meals, 9 / 10 = % of kids with
/// balloons / in costumes, 11 = new visitors, 12 = profit from all shops, 13 = sideshow profit, 16 = research rides,
/// 18 = build a coaster, 20 = peeps using a ride. Products are counted per <see cref="SaleCategory"/>
/// (<see cref="ParkFinances.Sold"/>); 6 counts balloons sold and 9/10 the share of all visitors in the park (OpenTPW
/// doesn't tell kids from adults). The
/// "use the Minecart ride" goal is approximated by total <see cref="ParkFinances.RidesRidden"/> (we don't track
/// a specific ride's rider count). Likewise a challenge's <c>TargetObj</c> (which ride: 1180 = Minecart…) isn't
/// mapped yet, so 18 counts any coaster built and 16 any ride-research completed (OpenTPW research upgrades
/// rides rather than unlocking new ones); 12 counts concession takings. The manager only offers
/// <see cref="IsSupported"/> types, so the still-opaque ones (19 loops, 24 toilet
/// cleanliness, staff happiness/skill, specific-ride builds/upgrades…) are parsed but skipped until a counter
/// exists. Adding a type = one case in each switch (+ a counter on <see cref="ParkFinances"/> if needed).</para>
/// </summary>
public static class ChallengeMetrics
{
	/// <summary>Whether this challenge Type is tracked (so the manager can offer it).</summary>
	public static bool IsSupported( int type ) => type is (>= 1 and <= 13) or 16 or 18 or 20;

	/// <summary>True for a type that measures a level the park must reach (a share of visitors, 9/10) rather than a
	/// count accumulated since the challenge was accepted — its progress is the live value, not a gain.</summary>
	public static bool IsLevel( int type ) => type is 9 or 10;

	/// <summary>The current absolute value of the counter <paramref name="c"/>'s Type measures (the manager
	/// tracks the gain since the challenge was accepted).</summary>
	public static float Current( Challenge c )
	{
		var fin = ParkFinances.Current;
		if ( fin == null )
			return 0f;
		return c.Type switch
		{
			1 => fin.Sold( SaleCategory.Fries ),      // "Sell N Fries"
			2 => fin.Sold( SaleCategory.Burger ),     // "Sell N Burgers"
			3 => fin.DrinkSold,                       // "Sell N Drinks"
			4 => fin.Sold( SaleCategory.IceCream ),   // "Sell N Icecreams"
			5 => fin.Sold( SaleCategory.Gift ),       // "Sell N Gifts"
			6 => fin.Sold( SaleCategory.Balloon ),    // "Get N kids with balloons"
			7 => fin.Sold( SaleCategory.Costume ),    // "Sell N Costumes"
			8 => fin.Sold( SaleCategory.Restaurant ), // "Sell N Restaurant meals"
			9 => PercentOfVisitors( p => p.HasBalloon ),  // "Get N% of the kids to have balloons"
			10 => PercentOfVisitors( p => p.HasCostume ), // "Get N% of the kids in costumes"
			11 => fin.VisitorsTotal,   // "Get N new visitors"
			12 => fin.ShopProfit,      // "Make N profit from all shops" (takings minus cost of goods)
			13 => fin.SideshowRevenue, // "Get your sideshows to make N profit"
			16 => fin.ResearchCompleted, // "Research N new rides" (approx: ride research completed)
			18 => fin.CoastersBuilt,   // "Build a [Minecart] Coaster" (approx: any coaster)
			20 => fin.RidesRidden,     // "Get N peeps to use [a] ride" (approx: any ride)
			_ => 0f,
		};
	}

	// The share (0–100) of the visitors in the park matching a predicate — 0 with an empty park.
	private static float PercentOfVisitors( Func<Peep, bool> match )
	{
		int total = 0, matched = 0;
		foreach ( var e in Entity.All )
			if ( e is Peep p )
			{
				total++;
				if ( match( p ) ) matched++;
			}
		return total == 0 ? 0f : 100f * matched / total;
	}

	/// <summary>A short goal description for the UI.</summary>
	public static string Describe( Challenge c ) => c.Type switch
	{
		1 => $"Sell {c.TargetVal} portions of fries in {c.TargetTime} days",
		2 => $"Sell {c.TargetVal} burgers in {c.TargetTime} days",
		3 => $"Sell {c.TargetVal} drinks in {c.TargetTime} days",
		4 => $"Sell {c.TargetVal} ice creams in {c.TargetTime} days",
		5 => $"Sell {c.TargetVal} gifts in {c.TargetTime} days",
		6 => $"Get {c.TargetVal} visitors with balloons in {c.TargetTime} days",
		7 => $"Sell {c.TargetVal} costumes in {c.TargetTime} days",
		8 => $"Sell {c.TargetVal} restaurant meals in {c.TargetTime} days",
		9 => $"Get {c.TargetVal}% of visitors holding balloons in {c.TargetTime} days",
		10 => $"Get {c.TargetVal}% of visitors in costumes in {c.TargetTime} days",
		11 => $"Attract {c.TargetVal} new visitors in {c.TargetTime} days",
		12 => $"Make {c.TargetVal} profit from shops in {c.TargetTime} days",
		13 => $"Make {c.TargetVal} sideshow profit in {c.TargetTime} days",
		16 => $"Research {c.TargetVal} rides in {c.TargetTime} days",
		18 => $"Build a coaster in {c.TargetTime} days",
		20 => $"Get {c.TargetVal} peeps to ride in {c.TargetTime} days",
		_ => $"Challenge: reach {c.TargetVal} in {c.TargetTime} days",
	};
}
