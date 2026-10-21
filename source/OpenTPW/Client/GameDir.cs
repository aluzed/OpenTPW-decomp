namespace OpenTPW;

/// <summary>
/// Performs operations that allow the quick, easy access of Theme Park World game files.
/// </summary>
public static class GameDir
{
	/// <summary>
	/// The resolved Theme Park World install path. The <c>OPENTPW_GAMEPATH</c>
	/// environment variable takes precedence over the persisted
	/// <see cref="Settings.Default.GamePath"/> setting, so the game can be pointed at any
	/// install (Linux / CI / Wine prefix) without editing config. See docs/tickets/T-006.
	/// </summary>
	public static string GamePath
	{
		get
		{
			var env = Environment.GetEnvironmentVariable( "OPENTPW_GAMEPATH" );
			return string.IsNullOrWhiteSpace( env ) ? Settings.Default.GamePath : env;
		}
	}

	/// <summary>The language overlay's data folder (<c>&lt;GamePath&gt;/&lt;Language&gt;/data</c>, case-insensitive), or null.</summary>
	public static string? LanguageDataPath( string language )
	{
		if ( !Directory.Exists( GamePath ) )
			return null;
		var folder = Directory.GetDirectories( GamePath )
			.FirstOrDefault( d => Path.GetFileName( d ).Equals( language, StringComparison.OrdinalIgnoreCase ) );
		if ( folder == null )
			return null;
		var data = Directory.GetDirectories( folder )
			.FirstOrDefault( d => Path.GetFileName( d ).Equals( "data", StringComparison.OrdinalIgnoreCase ) );
		return data;
	}

	/// <summary>
	/// An on-disk game-data file (for code that opens files directly rather than through the VFS): the language
	/// overlay's copy when it has one, else <c>data/</c>'s. Path segments are matched case-insensitively.
	/// </summary>
	public static string DataFile( params string[] parts )
	{
		if ( GameLanguage.Name != "English" && LanguageDataPath( GameLanguage.Name ) is { } overlay
			&& Resolve( overlay, parts ) is { } localized && File.Exists( localized ) )
			return localized;
		return Resolve( Path.Join( GamePath, "data" ), parts ) ?? Path.Join( [GamePath, "data", .. parts] );
	}

	// Walk `parts` under `root`, matching each segment case-insensitively; null if a segment is missing.
	private static string? Resolve( string root, string[] parts )
	{
		var current = root;
		foreach ( var part in parts )
		{
			var exact = Path.Join( current, part );
			if ( File.Exists( exact ) || Directory.Exists( exact ) )
			{
				current = exact;
				continue;
			}
			if ( !Directory.Exists( current ) )
				return null;
			var match = Directory.GetFileSystemEntries( current )
				.FirstOrDefault( e => Path.GetFileName( e ).Equals( part, StringComparison.OrdinalIgnoreCase ) );
			if ( match == null )
				return null;
			current = match;
		}
		return current;
	}

	/// <summary>
	/// Joins <see cref="GamePath"/> to <param name="path">path</param>.
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public static string GetPath( string path )
	{
		// Normalize to the platform's native separator so paths resolve on every OS.
		// See docs/tickets/T-001.
		return Path.Join( GamePath, path )
			.Replace( '/', Path.DirectorySeparatorChar )
			.Replace( '\\', Path.DirectorySeparatorChar );
	}
}
