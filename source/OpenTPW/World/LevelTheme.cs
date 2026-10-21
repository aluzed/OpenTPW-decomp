namespace OpenTPW;

/// <summary>
/// The selectable park theme (T-062): Theme Park World ships four themed worlds — <c>jungle</c>, <c>hallow</c>,
/// <c>fantasy</c>, <c>space</c> — each a <c>levels/&lt;name&gt;/</c> folder with its own terrain, rides, shops,
/// sideshows and <c>Standard.sam</c>. The active theme is the <see cref="Level.Name"/>, chosen at startup from
/// <c>OPENTPW_LEVEL</c> (default <c>jungle</c>). This helper resolves/validates a theme name and lists the
/// ride/sideshow WADs a theme provides, so the build catalog is data-driven for any theme rather than
/// hardcoded to jungle's ride names.
/// <para><b>Jungle stays curated.</b> To preserve the verified jungle park exactly, jungle keeps its hand-picked
/// ride/sideshow list (<see cref="CuratedJungleRides"/>); other themes enumerate their <c>rides/</c> +
/// <c>sideshow/</c> WADs.</para>
/// </summary>
public static class LevelTheme
{
	/// <summary>The shipped themes (a <c>levels/&lt;name&gt;</c> folder each).</summary>
	public static readonly string[] Known = { "jungle", "hallow", "fantasy", "space" };

	/// <summary>The default theme when none is selected / a bad name is given.</summary>
	public const string Default = "jungle";

	/// <summary>The jungle park's hand-picked, verified ride + sideshow set (kept stable so the default park is
	/// unchanged by the data-driven path). Other themes enumerate their WADs instead.</summary>
	public static readonly string[] CuratedJungleRides = { "totem", "monkey", "wateride", "coaster1", "gokarts", "tourride", "bumper" };
	public static readonly string[] CuratedJungleSideshows = { "puzzle", "squark", "hyenas", "junspray", "arc2x3" };

	// How many enumerated rides a non-curated theme contributes to the catalog (keeps the BUILD panel sane).
	private const int MaxEnumeratedRides = 8;
	private const int MaxEnumeratedSideshows = 5;

	/// <summary>Resolve a requested theme name to a valid one: a known theme (case-insensitive) is kept, anything
	/// else (null/empty/typo) falls back to <see cref="Default"/> so a bad value never breaks startup.</summary>
	public static string Resolve( string? requested )
	{
		if ( string.IsNullOrWhiteSpace( requested ) )
			return Default;
		foreach ( var k in Known )
			if ( string.Equals( k, requested.Trim(), System.StringComparison.OrdinalIgnoreCase ) )
				return k;
		return Default;
	}

	// The authentic per-world display names decoded from THEMENAMES.str (index-aligned with Known: 0 jungle =
	// "Lost Kingdom", 1 hallow = "Halloween World", 2 fantasy = "Wonder Land", 3 space = "Space Zone"), loaded
	// once. Empty if the file is unavailable → callers fall back to the folder name.
	private static IReadOnlyList<string>? displayNames;

	/// <summary>The authentic world name for a theme folder (e.g. jungle → "Lost Kingdom"), from THEMENAMES.str;
	/// falls back to the folder name if the strings are unavailable or the index is out of range (T-062).</summary>
	public static string DisplayName( string theme )
	{
		displayNames ??= LoadDisplayNames();
		return MapDisplayName( theme, displayNames );
	}

	/// <summary>Pure index-mapping of a theme folder to a display name from <paramref name="names"/> (Known-aligned),
	/// falling back to the folder name — unit-tested without the game file.</summary>
	internal static string MapDisplayName( string theme, IReadOnlyList<string> names )
	{
		int i = System.Array.IndexOf( Known, theme );
		return i >= 0 && i < names.Count && !string.IsNullOrWhiteSpace( names[i] ) ? names[i] : theme;
	}

	private static IReadOnlyList<string> LoadDisplayNames()
	{
		try { return new StringFile( GameLanguage.File( "THEMENAMES.str" ) ).Entries; }
		catch ( System.Exception e ) { Log.Warning( $"[theme] display names unavailable: {e.Message}" ); return System.Array.Empty<string>(); }
	}

	/// <summary>The ride catalog names for a theme: jungle's curated set, else the theme's enumerated ride WADs.</summary>
	public static IReadOnlyList<string> RideNames( string theme )
		=> theme == Default ? CuratedJungleRides : EnumerateWads( $"levels/{theme}/rides", MaxEnumeratedRides );

	/// <summary>The sideshow catalog names for a theme: jungle's curated set, else its enumerated sideshow WADs.</summary>
	public static IReadOnlyList<string> SideshowNames( string theme )
		=> theme == Default ? CuratedJungleSideshows : EnumerateWads( $"levels/{theme}/sideshow", MaxEnumeratedSideshows );

	/// <summary>List the <c>.wad</c> archive names (no path, no extension) under a level subfolder, sorted for a
	/// deterministic catalog, capped at <paramref name="max"/>. The VFS surfaces mountable <c>.wad</c> archives
	/// through <see cref="BaseFileSystem.GetDirectories"/> (extension stripped) — plain <c>.sam</c> files are not
	/// returned. Empty (not an error) if the folder can't be read.</summary>
	private static List<string> EnumerateWads( string relativeDir, int max )
	{
		var names = new List<string>();
		try
		{
			foreach ( var entry in FileSystem.GetDirectories( relativeDir ) )
				names.Add( System.IO.Path.GetFileName( entry ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[theme] couldn't list WADs in '{relativeDir}': {e.Message}" );
		}
		names.Sort( System.StringComparer.OrdinalIgnoreCase );
		if ( names.Count > max )
			names.RemoveRange( max, names.Count - max );
		return names;
	}
}
