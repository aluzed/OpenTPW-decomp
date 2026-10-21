namespace OpenTPW;

/// <summary>
/// The sprites drawn over visitors — what they bought at souvenir shops, and their thought bubbles. Souvenirs: the active theme's costume set
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

	/// <summary>The bubble icon for a <see cref="CrowdThoughts"/> thought (<c>esprites/Generic/Thoughts/SPR_TB</c>), or
	/// null for one not worth showing ("Okay") or without the sprite.</summary>
	public static Model? ThoughtBubble( int thought ) =>
		BubbleFrame( thought ) is { } frame && SpriteSheet.Load( "esprites/Generic/Thoughts", "SPR_TB" ) is { } sheet
			? sheet.FrameModel( frame ) : null;

	/// <summary>Which SPR_TB frame draws a thought: 0 smiling, 1 so-so, 2 angry, 3 sad, 5 laughing, 8 burger, 10 drink,
	/// 11 hourglass + thumbs-down, 12 drink + burger, 13 toilet, 14 litter. Pure.</summary>
	public static int? BubbleFrame( int thought ) => thought switch
	{
		CrowdThoughts.Hungry => 8,
		CrowdThoughts.Thirsty => 10,
		CrowdThoughts.HungryAndThirsty => 12,
		CrowdThoughts.NeedToilet => 13,
		CrowdThoughts.TooMessy => 14,
		CrowdThoughts.QueueTooLong => 11,
		CrowdThoughts.SuperHappy => 5,
		CrowdThoughts.Happy => 0,
		CrowdThoughts.Unhappy => 3,
		CrowdThoughts.Angry => 2,
		_ => null,
	};
}
