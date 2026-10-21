namespace OpenTPW;

/// <summary>
/// The front-end's player profiles (T-063): one folder per player under the save directory's <c>users/</c>, named with
/// a one-character slot prefix the lobby strips for display (<c>users/0Player 1</c> → "Player 1") — the layout
/// <see cref="LobbyLayout"/> already read. Selecting a profile routes the save slots into its folder.
/// </summary>
public static class PlayerProfile
{
	/// <summary>The selected profile's folder name (with its prefix), or null when none is selected.</summary>
	public static string? Current { get; private set; }

	/// <summary>The selected profile's display name.</summary>
	public static string? DisplayName => Current is { Length: > 1 } c ? c[1..] : null;

	public static void Select( string folder )
	{
		Current = folder;
		Log.Info( $"[profile] selected '{DisplayName}'" );
	}

	/// <summary>Create the profile for lobby slot <paramref name="slot"/> (0-based) as "Player n", select it, and
	/// return its display name.</summary>
	public static string Create( int slot )
	{
		var folder = $"{slot}Player {slot + 1}";
		Directory.CreateDirectory( SaveFileSystem.GetAbsolutePath( $"users/{folder}" ) );
		Select( folder );
		return folder[1..];
	}

	/// <summary>The selected profile's folder on disk, or null when no profile is selected.</summary>
	public static string? FolderPath => Current == null ? null : SaveFileSystem.GetAbsolutePath( $"users/{Current}" );
}
