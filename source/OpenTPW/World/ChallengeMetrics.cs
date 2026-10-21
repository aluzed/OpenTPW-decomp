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
	/// <summary>Whether this challenge Type is tracked (so the manager can offer it). 15 (kart crossroads) and 19
	/// (coaster loops) need track features OpenTPW doesn't have yet.</summary>
	public static bool IsSupported( int type ) => type is (>= 1 and <= 13) or 16 or 17 or 18 or 20 or 21 or 24 or 25 or 27
		or 28 or 29 or 30 or 31 or 33;

	/// <summary>The objects (rides, sideshows, features) of the active theme by <c>Info.Id</c> → name, so a challenge
	/// about a specific ride (<c>TargetObj</c> 1180 = Minecart) is only offered where that ride exists and can say
	/// its name. Set by the level.</summary>
	public static IReadOnlyDictionary<int, string> Objects { get; set; } = new Dictionary<int, string>();

	/// <summary>Whether the challenge's ride (if it names one) exists in this theme.</summary>
	public static bool IsAvailable( Challenge c ) => c.TargetObj == 0 || Objects.ContainsKey( c.TargetObj );

	// "Add upgrade to X" (31/33 with an upgrade id in TargetObj2) vs "Build X" (30/31).
	private static bool IsUpgrade( Challenge c ) => c.Type == 33 || c.Type == 31 && c.TargetObj2 != 0;
	private static bool IsBuild( Challenge c ) => c.Type is 18 or 30 || c.Type == 31 && c.TargetObj2 == 0;

	/// <summary>True for a type that measures a level the park must reach (a share of visitors, 9/10) rather than a
	/// count accumulated since the challenge was accepted — its progress is the live value, not a gain.</summary>
	public static bool IsLevel( int type ) => type is 9 or 10 or 21 or 24 or 25 or 27 or 28 or 29;

	/// <summary><see cref="IsLevel(int)"/> for a challenge (an upgrade goal is a level: the ride's upgrade).</summary>
	public static bool IsLevel( Challenge c ) => IsLevel( c.Type ) || IsUpgrade( c );

	/// <summary>"Keep … for N days" (25 all staff, 27 handymen over a happiness): it must hold every day of the
	/// challenge — it's lost the day it slips, won when the time is up.</summary>
	public static bool IsKeep( Challenge c ) => c.Type is 25 or 27;

	/// <summary>The value to reach: <c>TargetVal</c>, but at least one for a "build X" / "add an upgrade" goal (the
	/// file leaves those at 0).</summary>
	public static float Target( Challenge c ) => IsBuild( c ) || IsUpgrade( c ) ? Math.Max( 1, c.TargetVal ) : c.TargetVal;

	/// <summary>The current absolute value of the counter <paramref name="c"/>'s Type measures (the manager
	/// tracks the gain since the challenge was accepted).</summary>
	public static float Current( Challenge c )
	{
		var fin = ParkFinances.Current;
		if ( fin == null )
			return 0f;
		if ( IsUpgrade( c ) )
			return RidesOf( c ).Select( r => (float)r.UpgradeLevel ).DefaultIfEmpty( 0f ).Max();
		if ( IsBuild( c ) )
			return c.TargetObj != 0 ? fin.Built( c.TargetObj ) : fin.CoastersBuilt; // "Build a [Minecart] Coaster"
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
			24 => Shop.ToiletCleanliness(),               // "Keep toilets N% clean" (checked at the deadline)
			11 => fin.VisitorsTotal,   // "Get N new visitors"
			12 => fin.ShopProfit,      // "Make N profit from all shops" (takings minus cost of goods)
			13 => fin.SideshowRevenue, // "Get your sideshows to make N profit"
			16 => fin.ResearchCompleted, // "Research N new rides" (approx: ride research completed)
			// "Get N peeps onto [the Jaw Dropper]": that ride's riders (any ride's when the file names none).
			17 or 20 => c.TargetObj != 0 ? RidesOf( c ).Sum( r => r.TimesRidden ) : fin.RidesRidden,
			21 => RidesOf( c ).Select( r => (float)(r.Track?.SegmentCount ?? 0) ).DefaultIfEmpty( 0f ).Max(), // "with N sections"
			25 => LowestMorale( null ),                // "Keep all staff over N happiness"
			27 => LowestMorale( StaffRole.Handyman ),  // "Keep your Handymen above N happiness"
			28 => RidesOf( c ).Select( r => (float)r.UpgradeLevel ).DefaultIfEmpty( 0f ).Max(), // "Upgrade X to level N+1"
			29 => MechanicSkill(),                     // "Achieve average mechanic skill of N%"
			_ => 0f,
		};
	}

	// The placed rides a challenge names.
	private static IEnumerable<Ride> RidesOf( Challenge c ) => Entity.All.OfType<Ride>().Where( r => r.InfoId == c.TargetObj );

	// The least happy member of staff (of a role), 0 with none — "keep them all over N" needs someone to keep.
	private static float LowestMorale( StaffRole? role )
		=> Entity.All.OfType<Staff>().Where( s => role == null || s.Role == role ).Select( s => s.Morale ).DefaultIfEmpty( 0f ).Min();

	// The mechanics' average skill in percent: grade (and training towards the next) over the top grade.
	private static float MechanicSkill()
		=> Entity.All.OfType<Staff>().Where( s => s.Role == StaffRole.Mechanic )
			.Select( s => (s.Grade + s.TrainingPoints / StaffGrades.PointsPerGrade) / StaffGrades.MaxGrade * 100f ).DefaultIfEmpty( 0f ).Average();

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

	// The challenge's ride by name ("the Minecart"), or a generic word.
	private static string ObjectName( Challenge c, string fallback ) => Objects.TryGetValue( c.TargetObj, out var n ) ? n : fallback;

	/// <summary>A short goal description for the UI.</summary>
	public static string Describe( Challenge c )
	{
		if ( IsUpgrade( c ) )
			return $"Add an upgrade to the {ObjectName( c, "ride" )} in {c.TargetTime} days";
		if ( IsBuild( c ) )
			return $"Build the {ObjectName( c, "coaster" )} in {c.TargetTime} days";
		return c.Type switch
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
			24 => $"Keep your toilets {c.TargetVal}% clean for {c.TargetTime} days",
			11 => $"Attract {c.TargetVal} new visitors in {c.TargetTime} days",
			12 => $"Make {c.TargetVal} profit from shops in {c.TargetTime} days",
			13 => $"Make {c.TargetVal} sideshow profit in {c.TargetTime} days",
			16 => $"Research {c.TargetVal} rides in {c.TargetTime} days",
			17 or 20 => c.TargetObj != 0
				? $"Get {c.TargetVal} peeps onto the {ObjectName( c, "ride" )} in {c.TargetTime} days"
				: $"Get {c.TargetVal} peeps to ride in {c.TargetTime} days",
			21 => $"Build the {ObjectName( c, "track" )} with {c.TargetVal} sections in {c.TargetTime} days",
			25 => $"Keep all staff over {c.TargetVal} happiness for {c.TargetTime} days",
			27 => $"Keep your handymen over {c.TargetVal} happiness for {c.TargetTime} days",
			28 => $"Upgrade the {ObjectName( c, "ride" )} to level {c.TargetVal + 1} in {c.TargetTime} days",
			29 => $"Get your mechanics' average skill to {c.TargetVal}% in {c.TargetTime} days",
			_ => $"Challenge: reach {c.TargetVal} in {c.TargetTime} days",
		};
	}
}
