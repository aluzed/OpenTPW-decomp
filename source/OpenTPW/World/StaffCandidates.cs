namespace OpenTPW;

/// <summary>
/// The people applying for each staff job, as the original's hire window lists them: a few candidates per role,
/// each with a name from the game's own lists (<c>HANDYMAN_NAMES.str</c>, <c>MECHANIC_NAMES.str</c>…), a grade rolled
/// as the level rolls new staff, and the monthly wage that grade earns. Hiring one takes them off the list; the
/// list is refilled each month.
/// </summary>
public static class StaffCandidates
{
	public const int PerRole = 4;

	public readonly record struct Candidate( StaffRole Role, string Name, int Grade )
	{
		public float MonthlyWage => Staff.MonthlyWage( Role, Grade );
	}

	private static readonly Dictionary<StaffRole, List<Candidate>> lists = new();
	private static readonly Dictionary<StaffRole, string[]> names = new();
	private static int filledMonth = -1;

	/// <summary>The candidates for <paramref name="role"/> (refilled each in-game month).</summary>
	public static IReadOnlyList<Candidate> For( StaffRole role )
	{
		int month = GameClock.Current?.TotalMonths ?? 0;
		if ( month != filledMonth )
		{
			lists.Clear();
			filledMonth = month;
		}
		if ( !lists.TryGetValue( role, out var list ) )
			lists[role] = list = Enumerable.Range( 0, PerRole ).Select( _ => Roll( role ) ).ToList();
		return list;
	}

	/// <summary>The candidate taken on (hired): off the list.</summary>
	public static void Hired( Candidate candidate )
	{
		if ( lists.TryGetValue( candidate.Role, out var list ) )
			list.Remove( candidate );
	}

	/// <summary>The candidate chosen in the hire window, waiting to be placed in the park (consumed by the hire).</summary>
	public static Candidate? Pending { get; set; }

	/// <summary>A random name for <paramref name="role"/> from the game's list (the role's own name when none loads).</summary>
	public static string RandomName( StaffRole role )
	{
		if ( !names.TryGetValue( role, out var list ) )
		{
			var file = role switch
			{
				StaffRole.Handyman => "HANDYMAN_NAMES.str", StaffRole.Mechanic => "MECHANIC_NAMES.str",
				StaffRole.Entertainer => "ENTERTAINER_NAMES.str", StaffRole.Guard => "GUARD_NAMES.str",
				_ => "RESEARCHER_NAMES.str",
			};
			try { list = new StringFile( GameLanguage.File( file ) ).Entries.Where( n => !string.IsNullOrWhiteSpace( n ) ).Select( n => n.Trim() ).ToArray(); }
			catch { list = Array.Empty<string>(); }
			names[role] = list;
		}
		return list.Length > 0 ? list[Random.Shared.Next( list.Length )] : StaffNames.For( role );
	}

	private static Candidate Roll( StaffRole role )
		=> new( role, RandomName( role ), StaffGrades.Current.RollGrade( role, Random.Shared ) );
}
