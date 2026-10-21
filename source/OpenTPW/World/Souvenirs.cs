namespace OpenTPW;

/// <summary>
/// The sprites for what visitors buy at souvenir shops: the active theme's costume set
/// (<c>esprites/&lt;Theme&gt;/Costumes</c> — jungle tiger SPR_TI, hallow SPR_HK, fantasy SPR_BK, space SPR_SK; a full
/// walk-cycle sheet like a kid's) and the shared balloon (<c>esprites/Generic/Balloons/SPR_BL</c>). Null when the
/// sprite can't load, so the purchase just isn't shown.
/// </summary>
internal static class Souvenirs
{
	private static readonly Dictionary<string, SpriteSheet?> costumes = new();

	/// <summary>The theme folder name in <c>esprites</c> (jungle → Jungle).</summary>
	public static string ThemeDir( string theme ) =>
		string.IsNullOrEmpty( theme ) ? "Jungle" : char.ToUpperInvariant( theme[0] ) + theme[1..].ToLowerInvariant();

	/// <summary>The active theme's costume sheet.</summary>
	public static SpriteSheet? Costume()
	{
		var theme = Level.Current?.Name ?? LevelTheme.Default;
		if ( costumes.TryGetValue( theme, out var cached ) )
			return cached;
		SpriteSheet? sheet = null;
		var dir = $"esprites/{ThemeDir( theme )}/Costumes";
		try
		{
			var tpc = FileSystem.GetFiles( dir ).FirstOrDefault( f => f.EndsWith( ".TPC", StringComparison.OrdinalIgnoreCase ) );
			if ( tpc != null )
				sheet = SpriteSheet.Load( dir, Path.GetFileNameWithoutExtension( tpc ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[souvenir] no costume sprite in {dir}: {e.Message}" );
		}
		costumes[theme] = sheet;
		return sheet;
	}

	/// <summary>The balloon sheet (one frame per balloon colour).</summary>
	public static SpriteSheet? Balloon() => SpriteSheet.Load( "esprites/Generic/Balloons", "SPR_BL" );
}
