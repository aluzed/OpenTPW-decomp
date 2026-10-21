using System.Globalization;

namespace OpenTPW;

/// <summary>The level's golden-ticket targets (T-055), parsed from <c>GoldenTicketLocal.*</c> in the level
/// <c>Standard.sam</c>. A target of 0 means "not set" (skipped). <c>RecentVisitors</c> over a
/// <c>RecentVisitorMonths</c> window is now evaluated against <see cref="ParkFinances.RecentVisitors"/>.</summary>
public readonly record struct GoldenTicketTargets(
	int Visitors, int PeopleInPark, float Happiness, int HappyPeople, float ProfitYear,
	int RecentVisitors, int RecentVisitorMonths )
{
	/// <summary>Parse the <c>GoldenTicketLocal.*</c> targets from a level settings file.</summary>
	public static GoldenTicketTargets ParseLocal( SettingsFile sam )
	{
		float G( string key )
			=> float.TryParse( sam[$"GoldenTicketLocal.{key}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : 0f;
		return new GoldenTicketTargets(
			Visitors: (int)G( "Visitors" ),
			PeopleInPark: (int)G( "PeopleInPark" ),
			Happiness: G( "Happiness" ),
			HappyPeople: (int)G( "AtLeastThisManyHappyPeople" ),
			ProfitYear: G( "ProfitYear" ),
			RecentVisitors: (int)G( "RecentVisitors" ),
			RecentVisitorMonths: (int)G( "RecentVisitorMonths" ) );
	}
}

/// <summary>The park-wide golden tickets (<c>GoldenTicketGlobal.*</c> in <c>Standard.sam</c>) OpenTPW can award: a water
/// ride of <c>WaterLength</c> cells (50), and the secret one for <c>MinCellsCovered</c> cells (2000) under security
/// cameras. (<c>CoasterHeight</c>, <c>GokartExcitement</c> and <c>MinCellsOwned</c> need coaster heights, kart
/// excitement and land buying the park doesn't model yet.)</summary>
public readonly record struct GoldenTicketGlobals( int WaterLength, int MinCellsCovered )
{
	public static GoldenTicketGlobals Parse( SettingsFile sam )
	{
		int G( string key ) => int.TryParse( sam[$"GoldenTicketGlobal.{key}"], out var v ) ? v : 0;
		return new GoldenTicketGlobals( G( "WaterLength" ), G( "MinCellsCovered" ) );
	}
}

/// <summary>A snapshot of the park figures the golden-ticket goals measure. <c>RecentVisitors</c> is the count
/// admitted over the level's <c>RecentVisitorMonths</c> window (T-055); it defaults to 0 so callers that don't
/// measure it keep compiling.</summary>
public readonly record struct ParkState(
	int VisitorsTotal, int PeopleInPark, float AverageHappiness, int HappyPeople, float ProfitYear,
	int RecentVisitors = 0 );

/// <summary>
/// Pure golden-ticket goal evaluation (T-055): compares a <see cref="ParkState"/> to the level's
/// <see cref="GoldenTicketTargets"/>, yielding one progress row per <i>set</i> target and whether <b>all</b>
/// are met (the level win condition). Unit-tested; the manager (<see cref="GoldenTicketGoals"/>) gathers the
/// snapshot + fires the award.
/// </summary>
public static class GoldenTicket
{
	public readonly record struct GoalProgress( string Name, float Current, float Target, bool Met );

	/// <summary>One progress row per target that's set (&gt; 0), with whether it's met.</summary>
	public static List<GoalProgress> Evaluate( GoldenTicketTargets t, ParkState s )
	{
		var goals = new List<GoalProgress>();
		void Add( string name, float current, float target )
		{
			if ( target > 0f )
				goals.Add( new GoalProgress( name, current, target, current >= target ) );
		}
		Add( "Visitors", s.VisitorsTotal, t.Visitors );
		Add( "In park", s.PeopleInPark, t.PeopleInPark );
		Add( "Happiness", s.AverageHappiness, t.Happiness );
		Add( "Happy people", s.HappyPeople, t.HappyPeople );
		Add( "Profit/yr", s.ProfitYear, t.ProfitYear );
		Add( "Recent visitors", s.RecentVisitors, t.RecentVisitors );
		return goals;
	}

	// TAG_SYSTEM.str's golden-ticket announcements by goal (180 "…for getting ", 181 "…for having ", 182 happiness,
	// 184 "…for exceeding $", 185 "…for getting " recent visitors, 188 a long water ride, 190 the cameras), and what
	// the game completes each with.
	private static readonly Dictionary<string, (int Message, string Before, string After)> Announcements = new()
	{
		["Visitors"] = (180, "", " visitors."),
		["In park"] = (181, "", " visitors in the park at once."),
		["Happiness"] = (182, "", ""),
		["Happy people"] = (181, "", " happy visitors."),
		["Profit/yr"] = (184, "", " profit in a year."),
		["Recent visitors"] = (185, "", " visitors in {0} months."),
		["Water ride"] = (188, "", ""),
		["Cameras"] = (190, "", ""),
	};

	/// <summary>The game's announcement of a golden ticket for <paramref name="goal"/>: its headline ("YOU'VE WON A
	/// GOLDEN TICKET!") and sentence, completed with the target where the game's line stops at a number.</summary>
	public static (string Headline, string Sentence) Announcement( string goal, float target, int months )
	{
		if ( !Announcements.TryGetValue( goal, out var a ) || AdvisorTips.GameMessageLines( a.Message ) is not { Length: > 0 } lines )
			return ("YOU'VE WON A GOLDEN TICKET!", $"Congratulations! {goal}: {target:0}");
		var sentence = lines.Length > 1 ? lines[^1] : "";
		if ( !sentence.TrimEnd().EndsWith( '.' ) )
			sentence = $"{sentence} {target:0}{string.Format( a.After, months )}".Replace( "$ ", "$" );
		return (lines[0], sentence);
	}

	/// <summary>True when every set target is met (and at least one target exists).</summary>
	public static bool AllMet( GoldenTicketTargets t, ParkState s )
	{
		var goals = Evaluate( t, s );
		return goals.Count > 0 && goals.TrueForAll( g => g.Met );
	}
}
