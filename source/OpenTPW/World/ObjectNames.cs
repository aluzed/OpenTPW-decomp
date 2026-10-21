namespace OpenTPW;

/// <summary>
/// Localized ride / stall names. The <c>.sam</c> files carry the English name (<c>Info.Name "Crazy Ape"</c>); the game's
/// <c>OBJECT_NAMES.str</c> holds every name split over consecutive entries ("Crazy", "Ape" at 18–19), and each language's
/// copy keeps the translation at the same positions (French "Singe", "Ouf"). So a name is localized by finding its
/// entries in the English table and reading the same entries in the current language's.
/// </summary>
public static class ObjectNames
{
	private static string[]? english, localized;

	/// <summary><paramref name="name"/> in the current language (<see cref="GameLanguage"/>), or unchanged.</summary>
	public static string Localize( string name )
	{
		if ( GameLanguage.Name.Equals( "English", StringComparison.OrdinalIgnoreCase ) || string.IsNullOrWhiteSpace( name ) )
			return name;
		try
		{
			english ??= new StringFile( "Language/English/OBJECT_NAMES.str" ).Entries;
			localized ??= new StringFile( GameLanguage.File( "OBJECT_NAMES.str" ) ).Entries;
		}
		catch
		{
			english = localized = Array.Empty<string>();
		}
		return Localize( name, english, localized );
	}

	/// <summary>Pure lookup: the run of 1–3 consecutive <paramref name="from"/> entries spelling <paramref name="name"/>
	/// (case-insensitive, single-spaced), mapped to the same entries of <paramref name="to"/>; the name itself if none.</summary>
	public static string Localize( string name, IReadOnlyList<string> from, IReadOnlyList<string> to )
	{
		var wanted = Normalize( name );
		foreach ( int span in new[] { 2, 1, 3 } )
			for ( int i = 0; i + span <= from.Count && i + span <= to.Count; i++ )
			{
				var run = Normalize( string.Join( " ", Enumerable.Range( i, span ).Select( k => from[k] ) ) );
				if ( run.Length == 0 || !run.Equals( wanted, StringComparison.OrdinalIgnoreCase ) )
					continue;
				var result = Normalize( string.Join( " ", Enumerable.Range( i, span ).Select( k => to[k] ) ) );
				return result.Length > 0 ? result : name;
			}
		return wanted.Length > 0 ? wanted : name;
	}

	// Single-spaced, without the <TAG> markers some entries carry (hallow's "<JUMPER> Insecticide").
	private static string Normalize( string s ) =>
		string.Join( " ", System.Text.RegularExpressions.Regex.Replace( s ?? "", "<[^>]*>", " " )
			.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries ) );
}
