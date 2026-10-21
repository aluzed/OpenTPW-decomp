using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Persistent player settings (T-051). Currently the three audio volume buses — music, SFX and speech —
/// that the options panel exposes as sliders, persisted as JSON so they survive between runs. Loading is
/// fault-tolerant: a missing or corrupt file falls back to the built-in defaults rather than throwing, so
/// a fresh install (or a hand-mangled file) still boots. Values are clamped to [0,1] on load and save.
/// </summary>
internal sealed class GameSettings
{
	public float MusicVolume { get; set; } = 0.5f;
	public float SfxVolume { get; set; } = 0.8f;
	public float SpeechVolume { get; set; } = 0.9f;

	/// <summary>Whether the advisor character appears in a park and gives tips (the original's <c>AdvisorOn</c>);
	/// on by default. Persisted so the player can switch the bug head off and have it stay off.</summary>
	public bool AdvisorEnabled { get; set; } = true;

	/// <summary>Whether the debug overlay (income breakdown, counts, thoughts, FPS) is shown. OFF by default so
	/// the HUD reads as a real interface; toggled in-game with the backtick key (T-065). Persisted.</summary>
	public bool ShowDebug { get; set; } = false;

	/// <summary>Text + voice language (a disc language folder: English, French, German…); OPENTPW_LANGUAGE overrides it.</summary>
	public string Language { get; set; } = "English";

	/// <summary>
	/// Where the settings JSON lives. Defaults to the local <c>./.opentpw</c> cache dir (the same place
	/// <see cref="Game"/> keeps its cache). Settable so tests can point it at a temp file.
	/// </summary>
	public static string FilePath { get; set; } = Path.Combine( ".opentpw", "settings.json" );

	private static GameSettings? current;

	/// <summary>The live settings, loaded from disk on first access.</summary>
	public static GameSettings Current => current ??= Load();

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>Clamp every volume into the valid [0,1] gain range.</summary>
	public void Clamp()
	{
		MusicVolume = Math.Clamp( MusicVolume, 0f, 1f );
		SfxVolume = Math.Clamp( SfxVolume, 0f, 1f );
		SpeechVolume = Math.Clamp( SpeechVolume, 0f, 1f );
	}

	/// <summary>Serialize to indented JSON (clamps first so a persisted file is always valid).</summary>
	public string Serialize()
	{
		Clamp();
		return JsonSerializer.Serialize( this, JsonOptions );
	}

	/// <summary>Parse settings JSON; any error (malformed / truncated / null) yields fresh defaults.</summary>
	public static GameSettings Deserialize( string json )
	{
		try
		{
			var parsed = JsonSerializer.Deserialize<GameSettings>( json );
			if ( parsed == null )
				return new GameSettings();
			parsed.Clamp();
			return parsed;
		}
		catch ( JsonException )
		{
			return new GameSettings();
		}
	}

	/// <summary>Read <see cref="FilePath"/>, or return defaults if it is absent or unreadable.</summary>
	public static GameSettings Load()
	{
		try
		{
			if ( File.Exists( FilePath ) )
				return Deserialize( File.ReadAllText( FilePath ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not read settings ({e.Message}); using defaults." );
		}
		return new GameSettings();
	}

	/// <summary>Persist to <see cref="FilePath"/>, creating the directory if needed. Errors are logged, not thrown.</summary>
	public void Save()
	{
		try
		{
			var dir = Path.GetDirectoryName( FilePath );
			if ( !string.IsNullOrEmpty( dir ) )
				Directory.CreateDirectory( dir );
			File.WriteAllText( FilePath, Serialize() );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Could not save settings ({e.Message})." );
		}
	}
}
