namespace OpenTPW;

/// <summary>Visitors' names, from the game's own list (<c>FEMALE_NAMES.str</c>, the one the language folder ships).</summary>
public static class VisitorNames
{
	private static string[]? names;

	public static string Random()
	{
		if ( names == null )
		{
			try { names = new StringFile( GameLanguage.File( "FEMALE_NAMES.str" ) ).Entries.Where( n => !string.IsNullOrWhiteSpace( n ) ).Select( n => n.Trim() ).ToArray(); }
			catch { names = Array.Empty<string>(); }
		}
		return names.Length > 0 ? names[System.Random.Shared.Next( names.Length )] : "Visitor";
	}
}
