namespace OpenTPW;

/// <summary>
/// The game's own hover hints (<c>UIHELPTEXT.str</c>, in the selected language): the line the original shows in its
/// hint strip for each control — "Left-click to buy and build attractions or to hire staff", "Left-click to
/// view/edit this item's details"… A '|' in an entry separates two lines.
/// </summary>
public static class UiHelp
{
	// Entries used by the HUD (indices into UIHELPTEXT.str).
	public const int Buy = 469, Info = 470, Finance = 471, Research = 472, Map = 473, Camera = 474,
		Happiness = 477, Date = 478, ItemDetails = 439, BuildItem = 440, ParkGates = 459,
		GoldenTickets = 463, GoldenKeys = 464, Balance = 465, CloseScreen = 1;

	private static string[]? entries;

	/// <summary>Entry <paramref name="index"/> with its lines joined, else <paramref name="fallback"/>.</summary>
	public static string Text( int index, string fallback )
	{
		if ( entries == null )
		{
			try { entries = new StringFile( GameLanguage.File( "UIHELPTEXT.str" ) ).Entries; }
			catch { entries = Array.Empty<string>(); }
		}
		if ( index < 0 || index >= entries.Length || string.IsNullOrWhiteSpace( entries[index] ) )
			return fallback;
		return string.Join( "  ", entries[index].Split( '|', '\n' ).Select( l => l.Trim() ).Where( l => l.Length > 0 ) );
	}
}
