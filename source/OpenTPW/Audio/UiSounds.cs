namespace OpenTPW;

/// <summary>
/// The interface's own sounds, from the global banks <c>sfUiHD.sdt</c> and <c>UIHD.sdt</c>: the button press
/// (<c>BUTTON01</c>), a window opening (<c>UIopen1</c>), selling (<c>bomb1</c>), laying a ride or a small item
/// (<c>layride</c>, <c>laysmall</c>), a golden ticket (<c>goldticket</c>), the camcorder (<c>camera1</c>)…
/// Each clip is read once.
/// </summary>
public static class UiSounds
{
	public const string Button = "BUTTON01", Open = "UIopen1", Sell = "bomb1", LayRide = "layride", LaySmall = "laysmall",
		GoldTicket = "goldticket", Camera = "camera1", Error = "blnl_error1";

	private static readonly Dictionary<string, byte[]?> clips = new( StringComparer.OrdinalIgnoreCase );
	private static List<MP2File>? bank;

	public static void Play( string name )
	{
		if ( !clips.TryGetValue( name, out var data ) )
		{
			data = Find( name );
			clips[name] = data;
		}
		if ( data != null )
			Audio.PlaySfx( $"ui_{name}", data );
	}

	private static byte[]? Find( string name )
	{
		if ( bank == null )
		{
			bank = new();
			foreach ( var file in new[] { "sfUiHD.sdt", "UIHD.sdt" } )
			{
				try
				{
					var path = Path.Join( GameDir.GamePath, "data", "global", "sound", file );
					if ( File.Exists( path ) )
						bank.AddRange( new SdtArchive( path ).soundFiles );
				}
				catch ( Exception e ) { Log.Warning( $"[ui] {file}: {e.Message}" ); }
			}
		}
		return bank.FirstOrDefault( f => Path.GetFileNameWithoutExtension( f.Name ).Equals( name, StringComparison.OrdinalIgnoreCase ) )?.SoundData;
	}
}
