namespace OpenTPW;

/// <summary>
/// Staff strikes, per type of staff (reversed from <c>tp.exe</c>, <c>FUN_00508f70</c> / <c>FUN_00509360</c>).
/// Once a month — and only from the 24th month of play — each type is considered: it has a grievance when more than
/// three of them are employed and their average energy ("striking through fatigue") or happiness ("striking through
/// unhappiness") is under 15. A grievance escalates <c>mStrikeLevel</c>: a strike warning, then a strike of one week,
/// two weeks, then a month (and a month every time after that). With the grievance gone the level drops back to 0
/// (the advisor thanks you if it was only a warning). The advisor messages are the game's: <c>StaffUnhappy*</c>
/// (TAG_SYSTEM 38–42, the warning), <c>StaffVeryUnhappy*</c> (43–47, the mid-month reminder), <c>StaffStriking*</c>
/// (48–52), <c>StaffHappier*</c> (53–57, called off) and <c>StrikeEndBad*</c> (58–62, back to work). Pure (unit-tested).
/// </summary>
public sealed class StaffStrikes
{
	/// <summary>The roles in the order the advisor's messages list them (mechanics, cleaners, guards, entertainers,
	/// scientists).</summary>
	public static readonly StaffRole[] Roles =
		{ StaffRole.Mechanic, StaffRole.Handyman, StaffRole.Guard, StaffRole.Entertainer, StaffRole.Researcher };

	/// <summary>The Advisor.sam suffix for a role (<c>StaffStrikingMechanics</c>…).</summary>
	public static string AdvisorName( StaffRole role ) => role switch
	{
		StaffRole.Mechanic => "Mechanics", StaffRole.Handyman => "Handymen", StaffRole.Guard => "Guards",
		StaffRole.Entertainer => "Entertainers", _ => "Researchers",
	};

	public const int FirstMonthConsidered = 24;
	public const float GrievanceLevel = 15f;
	public const int MinStaffForGrievance = 4; // "more than three"

	/// <summary>The active level's strikes (null outside a park).</summary>
	public static StaffStrikes? Current { get; set; }

	private readonly Dictionary<StaffRole, int> level = new();
	private readonly Dictionary<StaffRole, int> strikeDaysLeft = new();
	private readonly List<string> notices = new();

	/// <summary>The strike level of a role: 0 content, 1 warned, 2–4 striking (one week / two weeks / a month).</summary>
	public int Level( StaffRole role ) => level.GetValueOrDefault( role );

	/// <summary>True while a role is out on strike (they stop working and draw no wage).</summary>
	public bool IsStriking( StaffRole role ) => strikeDaysLeft.GetValueOrDefault( role ) > 0;

	/// <summary>The advisor message ids raised since the last call (each fires once).</summary>
	public List<string> TakeNotices()
	{
		var taken = notices.ToList();
		notices.Clear();
		return taken;
	}

	/// <summary>Whether a role's staff have a grievance: more than three of them, tired or unhappy on average.</summary>
	public static bool HasGrievance( int count, float averageEnergy, float averageHappiness )
		=> count >= MinStaffForGrievance && (averageEnergy < GrievanceLevel || averageHappiness < GrievanceLevel);

	/// <summary>The month's consideration for one role (<paramref name="monthsPlayed"/> = calendar months elapsed).</summary>
	public void ConsiderMonth( StaffRole role, int monthsPlayed, bool grievance )
	{
		if ( monthsPlayed < FirstMonthConsidered )
			return;
		int stage = Level( role );
		if ( !grievance )
		{
			if ( stage == 1 )
				notices.Add( $"StaffHappier{AdvisorName( role )}" );
			level[role] = 0;
			return;
		}
		switch ( stage )
		{
			case 0:
				level[role] = 1;
				notices.Add( $"StaffUnhappy{AdvisorName( role )}" );
				break;
			default:
				level[role] = Math.Min( 4, stage + 1 );
				strikeDaysLeft[role] = level[role] switch { 2 => 7, 3 => 14, _ => 30 };
				notices.Add( $"StaffStriking{AdvisorName( role )}" );
				break;
		}
	}

	/// <summary>A day passed: strikes run down (and end with the advisor's warning), and a warned role is reminded
	/// mid-month (<paramref name="dayOfMonth"/> 15) that it strikes at the end of the month.</summary>
	public void OnNewDay( int dayOfMonth )
	{
		foreach ( var role in Roles )
		{
			if ( strikeDaysLeft.GetValueOrDefault( role ) > 0 && --strikeDaysLeft[role] == 0 )
				notices.Add( $"StrikeEndBad{AdvisorName( role )}" );
			if ( dayOfMonth == 15 && Level( role ) == 1 )
				notices.Add( $"StaffVeryUnhappy{AdvisorName( role )}" );
		}
	}
}
