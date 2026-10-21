namespace OpenTPW;

/// <summary>
/// The authentic Theme Park World staff-role names, decoded from <c>Language/English/STAFF_TYPES.str</c> via the
/// existing <see cref="StringFile"/>/<c>BFSTReader</c> (T-062). The file lists singular/plural pairs — this maps
/// each <see cref="StaffRole"/> to its singular form. Two differ from our internal enum names: a
/// <see cref="StaffRole.Handyman"/> is a <b>Cleaner</b> and a <see cref="StaffRole.Researcher"/> is a
/// <b>Scientist</b> in the game. Falls back to the enum name if the strings are unavailable.
/// </summary>
public static class StaffNames
{
	// Index of each role's singular name in STAFF_TYPES.str (pairs: 0/1 Cleaner, 2/3 Mechanic, 4/5 Entertainer,
	// 6/7 Guard, 8/9 Scientist).
	private static int IndexFor( StaffRole role ) => role switch
	{
		StaffRole.Handyman => 0,     // "Cleaner"
		StaffRole.Mechanic => 2,
		StaffRole.Entertainer => 4,
		StaffRole.Guard => 6,
		StaffRole.Researcher => 8,   // "Scientist"
		_ => -1,
	};

	private static IReadOnlyList<string>? names;

	/// <summary>The authentic singular name for a staff role (e.g. Handyman → "Cleaner"); the enum name if the
	/// strings can't be read.</summary>
	public static string For( StaffRole role )
	{
		names ??= Load();
		return Map( role, names );
	}

	/// <summary>Pure role→name mapping over a decoded STAFF_TYPES list, with the enum-name fallback — unit-tested
	/// without the game file.</summary>
	internal static string Map( StaffRole role, IReadOnlyList<string> names )
	{
		int i = IndexFor( role );
		return i >= 0 && i < names.Count && !string.IsNullOrWhiteSpace( names[i] ) ? names[i] : role.ToString();
	}

	private static IReadOnlyList<string> Load()
	{
		try { return new StringFile( GameLanguage.File( "STAFF_TYPES.str" ) ).Entries; }
		catch ( System.Exception e ) { Log.Warning( $"[staff] type names unavailable: {e.Message}" ); return System.Array.Empty<string>(); }
	}
}
