using Silk.NET.OpenAL;

namespace OpenTPW;

/// <summary>
/// Game audio (T-031): decodes MPEG (.mp2) tracks with the bundled minimp3 native lib and plays them
/// through OpenAL. Currently drives a single looping background-music source. A missing audio device
/// or native decoder (headless / CI / unbuilt platform) disables playback gracefully instead of
/// throwing. NLayer (used by the ModKit previewer) mis-decoded the game's MPEG-2 Layer II music —
/// dropping ~12% of frames — so the game uses minimp3, which decodes it correctly.
/// </summary>
internal static unsafe class Audio
{
	private static AL al = null!;
	private static ALContext alc = null!;
	private static Device* device;
	private static Context* context;

	private static uint musicSource;
	private static uint musicBuffer;

	// Ambient bed (T-051): a second looping source that plays level ambience under the music. Its gain
	// rides the SFX bus (ambience is environmental sound, not music), scaled by a fixed base so it sits
	// quietly beneath everything else.
	private static uint ambientSource;
	private static uint ambientBuffer;
	private const float AmbientBaseGain = 0.55f;

	// Weather bed (T-056): a third looping source for the rain loop, on top of the ambient bed so the two
	// coexist (rain over the level ambience). Rides the SFX bus like the ambient bed.
	private static uint weatherSource;
	private static uint weatherBuffer;
	private const float WeatherBaseGain = 0.6f;

	// One-shot SFX: a small round-robin pool of sources + a cache of decoded buffers keyed by name
	// (so a sound decodes once and replays cheaply).
	private const int SfxSourceCount = 8;
	private static readonly uint[] sfxSources = new uint[SfxSourceCount];
	private static int sfxNext;
	private static readonly Dictionary<string, uint> sfxBuffers = new();

	// 3D positional bus (T-047): a dedicated round-robin pool whose sources carry a world AL_POSITION and
	// attenuate with distance from the listener (which tracks the camera each frame, UpdateListener). The
	// music / ambient / speech + 2D SFX sources are made head-relative at init, so moving the listener never
	// pans or fades them — only these 3D sources are spatialised. Shares the sfxBuffers decode cache.
	private const int Sfx3dSourceCount = 8;
	private static readonly uint[] sfx3dSources = new uint[Sfx3dSourceCount];
	private static int sfx3dNext;
	private const float RefDistance = 40f;    // world units within which a 3D sound is at full volume
	private const float MaxDistance = 700f;   // past this, no further attenuation
	private const float RolloffFactor = 1f;   // attenuation steepness

	private static bool triedInit;
	private static bool available;

	private static float musicVolume = 0.5f;
	private static float sfxVolume = 0.8f;
	private static float speechVolume = 0.9f;

	/// <summary>Background-music gain in [0,1].</summary>
	public static float MusicVolume
	{
		get => musicVolume;
		set
		{
			musicVolume = Math.Clamp( value, 0f, 1f );
			if ( available )
				al.SetSourceProperty( musicSource, SourceFloat.Gain, musicVolume );
		}
	}

	/// <summary>
	/// Sound-effects gain in [0,1] (applied to each SFX as it plays, and to the ambient bed which rides
	/// this bus).
	/// </summary>
	public static float SfxVolume
	{
		get => sfxVolume;
		set
		{
			sfxVolume = Math.Clamp( value, 0f, 1f );
			if ( available )
			{
				al.SetSourceProperty( ambientSource, SourceFloat.Gain, AmbientBaseGain * sfxVolume );
				al.SetSourceProperty( weatherSource, SourceFloat.Gain, WeatherBaseGain * sfxVolume );
			}
		}
	}

	/// <summary>Speech gain in [0,1] (applied to advisor / character lines played via <see cref="PlaySpeech"/>).</summary>
	public static float SpeechVolume
	{
		get => speechVolume;
		set => speechVolume = Math.Clamp( value, 0f, 1f );
	}

	/// <summary>
	/// Plays a one-shot sound effect from an MPEG (.mp2) byte stream. <paramref name="key"/> caches
	/// the decoded buffer so repeated effects (e.g. UI clicks) decode only once. Uses a round-robin
	/// source pool so overlapping effects don't cut each other off. <paramref name="gain"/> sets the
	/// per-effect volume (0..1); negative uses the global <see cref="SfxVolume"/> (e.g. ride screams
	/// scale gain by their script level — see RideEngine).
	/// </summary>
	public static void PlaySfx( string key, byte[] mpegData, float gain = -1f )
		=> PlayPooled( key, mpegData, gain, sfxVolume );

	/// <summary>
	/// Plays a one-shot speech line (advisor / character) through the SFX source pool, scaled by the
	/// dedicated <see cref="SpeechVolume"/> bus rather than <see cref="SfxVolume"/> so the player can
	/// balance voice against effects independently. Same caching / round-robin behaviour as
	/// <see cref="PlaySfx"/>.
	/// </summary>
	public static void PlaySpeech( string key, byte[] mpegData, float gain = -1f )
		=> PlayPooled( key, mpegData, gain, speechVolume );

	// Shared one-shot playback over the SFX source pool. <paramref name="busVolume"/> is the bus the
	// effect rides (SFX or speech); a negative <paramref name="gain"/> plays at the bus volume, otherwise
	// the per-effect gain scales the bus.
	private static void PlayPooled( string key, byte[] mpegData, float gain, float busVolume )
	{
		if ( !EnsureInitialized() )
			return;

		try
		{
			if ( !TryGetBuffer( key, mpegData, out var buffer ) )
				return;

			var src = sfxSources[sfxNext];
			sfxNext = ( sfxNext + 1 ) % SfxSourceCount;

			al.SourceStop( src );
			al.SetSourceProperty( src, SourceInteger.Buffer, (int)buffer );
			al.SetSourceProperty( src, SourceFloat.Gain, gain < 0f ? busVolume : Math.Clamp( gain, 0f, 1f ) * busVolume );
			al.SourcePlay( src );
		}
		catch ( Exception e )
		{
			Log.Warning( $"SFX playback failed: {e.Message}" );
		}
	}

	// Decode (once, cached by key) an MPEG buffer into an OpenAL buffer handle. Returns false on decode
	// failure / empty PCM. Caller must hold the device (EnsureInitialized).
	private static bool TryGetBuffer( string key, byte[] mpegData, out uint buffer )
	{
		if ( sfxBuffers.TryGetValue( key, out buffer ) )
			return true;

		if ( !TryDecode( mpegData, out var pcm, out var sampleRate, out var channels ) || pcm.Length == 0 )
		{
			buffer = 0;
			return false;
		}

		buffer = al.GenBuffer();
		var fmt = channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
		fixed ( short* data = pcm )
			al.BufferData( buffer, fmt, data, pcm.Length * sizeof( short ), sampleRate );
		sfxBuffers[key] = buffer;
		return true;
	}

	/// <summary>
	/// Plays a one-shot sound effect positioned at a world point (T-047): same decode caching as
	/// <see cref="PlaySfx"/>, but on the dedicated 3D source pool, so it pans + attenuates relative to the
	/// listener (the camera). Rides the <see cref="SfxVolume"/> bus. No device → silent no-op.
	/// </summary>
	public static void PlaySfx3D( string key, byte[] mpegData, Vector3 position, float gain = -1f )
	{
		if ( !EnsureInitialized() )
			return;

		try
		{
			if ( !TryGetBuffer( key, mpegData, out var buffer ) )
				return;

			var src = sfx3dSources[sfx3dNext];
			sfx3dNext = ( sfx3dNext + 1 ) % Sfx3dSourceCount;

			al.SourceStop( src );
			al.SetSourceProperty( src, SourceInteger.Buffer, (int)buffer );
			al.SetSourceProperty( src, SourceFloat.Gain, gain < 0f ? sfxVolume : Math.Clamp( gain, 0f, 1f ) * sfxVolume );
			al.SetSourceProperty( src, SourceVector3.Position, position.X, position.Y, position.Z );
			al.SourcePlay( src );
		}
		catch ( Exception e )
		{
			Log.Warning( $"3D SFX playback failed: {e.Message}" );
		}
	}

	private static float duckUntil;

	/// <summary>Dip the background music by <paramref name="fraction"/> (0–1) for <paramref name="seconds"/> — a ride
	/// script's <c>DIPMUSIC</c>, so a big moment (a splash, a scream) cuts through.</summary>
	public static void DipMusic( float fraction, float seconds )
	{
		if ( !available )
			return;
		duckUntil = Time.Now + MathF.Max( 0.1f, seconds );
		try { al.SetSourceProperty( musicSource, SourceFloat.Gain, musicVolume * (1f - Math.Clamp( fraction, 0f, 1f )) ); }
		catch ( Exception e ) { Log.Warning( $"Music dip failed: {e.Message}" ); }
	}

	/// <summary>
	/// Moves the 3D-audio listener to the camera each frame (T-047): position + orientation (forward/up), so
	/// positioned effects (<see cref="PlaySfx3D"/>) pan and fade as the view moves. No device → no-op.
	/// </summary>
	public static void UpdateListener()
	{
		if ( !available )
			return;

		// A dipped music track (DIPMUSIC) comes back up once the dip is over.
		if ( duckUntil > 0f && Time.Now >= duckUntil )
		{
			duckUntil = 0f;
			try { al.SetSourceProperty( musicSource, SourceFloat.Gain, musicVolume ); }
			catch ( Exception e ) { Log.Warning( $"Music restore failed: {e.Message}" ); }
		}

		try
		{
			var p = Camera.Position;
			var fwd = Camera.Rotation.Forward;
			var up = Camera.Rotation.Up;
			al.SetListenerProperty( ListenerVector3.Position, p.X, p.Y, p.Z );
			float* orientation = stackalloc float[6] { fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z };
			al.SetListenerProperty( ListenerFloatArray.Orientation, orientation );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Listener update failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Decodes an in-memory MPEG (.mp2) stream and plays it on the music source, replacing whatever
	/// was playing. Loops by default. Decode errors are logged; no device disables playback quietly.
	/// </summary>
	public static void PlayMusic( byte[] mpegData, bool loop = true )
	{
		if ( !TryDecode( mpegData, out var pcm, out var sampleRate, out var channels ) || pcm.Length == 0 )
			return;

		if ( !EnsureInitialized() )
			return;

		try
		{
			al.SourceStop( musicSource );
			al.SetSourceProperty( musicSource, SourceInteger.Buffer, 0 );
			if ( musicBuffer != 0 )
				al.DeleteBuffer( musicBuffer );

			musicBuffer = al.GenBuffer();
			var format = channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
			fixed ( short* data = pcm )
				al.BufferData( musicBuffer, format, data, pcm.Length * sizeof( short ), sampleRate );

			al.SetSourceProperty( musicSource, SourceInteger.Buffer, (int)musicBuffer );
			al.SetSourceProperty( musicSource, SourceBoolean.Looping, loop );
			al.SetSourceProperty( musicSource, SourceFloat.Gain, musicVolume );
			al.SourcePlay( musicSource );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Music playback failed: {e.Message}" );
		}
	}

	/// <summary>Stops the music source (no-op if audio is unavailable).</summary>
	public static void StopMusic()
	{
		if ( !available )
			return;

		try { al.SourceStop( musicSource ); }
		catch ( Exception e ) { Log.Warning( $"StopMusic failed: {e.Message}" ); }
	}

	/// <summary>
	/// Decodes an in-memory MPEG (.mp2) stream and plays it on the looping ambient source (T-051),
	/// replacing any previous ambience. The ambient bed rides the <see cref="SfxVolume"/> bus. Decode
	/// errors / no device disable it quietly, matching the rest of the audio layer.
	/// </summary>
	public static void PlayAmbient( byte[] mpegData, bool loop = true )
	{
		if ( !TryDecode( mpegData, out var pcm, out var sampleRate, out var channels ) || pcm.Length == 0 )
			return;

		if ( !EnsureInitialized() )
			return;

		try
		{
			al.SourceStop( ambientSource );
			al.SetSourceProperty( ambientSource, SourceInteger.Buffer, 0 );
			if ( ambientBuffer != 0 )
				al.DeleteBuffer( ambientBuffer );

			ambientBuffer = al.GenBuffer();
			var format = channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
			fixed ( short* data = pcm )
				al.BufferData( ambientBuffer, format, data, pcm.Length * sizeof( short ), sampleRate );

			al.SetSourceProperty( ambientSource, SourceInteger.Buffer, (int)ambientBuffer );
			al.SetSourceProperty( ambientSource, SourceBoolean.Looping, loop );
			al.SetSourceProperty( ambientSource, SourceFloat.Gain, AmbientBaseGain * sfxVolume );
			al.SourcePlay( ambientSource );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Ambient playback failed: {e.Message}" );
		}
	}

	/// <summary>Stops the ambient source (no-op if audio is unavailable).</summary>
	public static void StopAmbient()
	{
		if ( !available )
			return;

		try { al.SourceStop( ambientSource ); }
		catch ( Exception e ) { Log.Warning( $"StopAmbient failed: {e.Message}" ); }
	}

	/// <summary>Plays a looping weather bed (the rain loop, T-056) on its own source, over the ambient bed.
	/// Replaces any previous weather loop; rides the <see cref="SfxVolume"/> bus. Quiet no-op without a device.</summary>
	public static void PlayWeatherLoop( byte[] mpegData, bool loop = true )
	{
		if ( !TryDecode( mpegData, out var pcm, out var sampleRate, out var channels ) || pcm.Length == 0 )
			return;
		if ( !EnsureInitialized() )
			return;

		try
		{
			al.SourceStop( weatherSource );
			al.SetSourceProperty( weatherSource, SourceInteger.Buffer, 0 );
			if ( weatherBuffer != 0 )
				al.DeleteBuffer( weatherBuffer );

			weatherBuffer = al.GenBuffer();
			var format = channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
			fixed ( short* data = pcm )
				al.BufferData( weatherBuffer, format, data, pcm.Length * sizeof( short ), sampleRate );

			al.SetSourceProperty( weatherSource, SourceInteger.Buffer, (int)weatherBuffer );
			al.SetSourceProperty( weatherSource, SourceBoolean.Looping, loop );
			al.SetSourceProperty( weatherSource, SourceFloat.Gain, WeatherBaseGain * sfxVolume );
			al.SourcePlay( weatherSource );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Weather loop playback failed: {e.Message}" );
		}
	}

	/// <summary>Stops the weather (rain) loop (no-op if audio is unavailable).</summary>
	public static void StopWeatherLoop()
	{
		if ( !available )
			return;

		try { al.SourceStop( weatherSource ); }
		catch ( Exception e ) { Log.Warning( $"StopWeatherLoop failed: {e.Message}" ); }
	}

	private static bool TryDecode( byte[] mpegData, out short[] pcm, out int sampleRate, out int channels )
	{
		pcm = Array.Empty<short>();
		sampleRate = 0;
		channels = 0;

		try
		{
			// minimp3 (shared with the ModKit previewer) decodes MPEG-1/2 Layer I/II/III to interleaved int16 PCM.
			return OpenTPW.Common.MpegDecoder.TryDecode( mpegData, out pcm, out sampleRate, out channels );
		}
		catch ( Exception e )
		{
			// DllNotFoundException on platforms without the native build, or any decode failure.
			Log.Error( $"Failed to decode audio: {e.Message}" );
			return false;
		}
	}

	// Pin a source to the listener (head-relative, at the origin) so the 3D listener never affects it.
	private static void MakeHeadRelative( uint src )
	{
		al.SetSourceProperty( src, SourceBoolean.SourceRelative, true );
		al.SetSourceProperty( src, SourceVector3.Position, 0f, 0f, 0f );
	}

	private static bool EnsureInitialized()
	{
		if ( triedInit )
			return available;

		triedInit = true;

		try
		{
			alc = ALContext.GetApi();
			al = AL.GetApi();

			device = alc.OpenDevice( "" );
			if ( device == null )
			{
				Log.Warning( "OpenAL: no audio device available; music disabled." );
				return false;
			}

			context = alc.CreateContext( device, null );
			alc.MakeContextCurrent( context );
			musicSource = al.GenSource();
			ambientSource = al.GenSource();
			weatherSource = al.GenSource();
			for ( var i = 0; i < SfxSourceCount; i++ )
				sfxSources[i] = al.GenSource();

			// Music / ambient / weather / 2D SFX are head-relative: pinned to the listener at the origin so
			// moving the 3D listener (UpdateListener) never pans or fades them — only the 3D pool below is.
			MakeHeadRelative( musicSource );
			MakeHeadRelative( ambientSource );
			MakeHeadRelative( weatherSource );
			for ( var i = 0; i < SfxSourceCount; i++ )
				MakeHeadRelative( sfxSources[i] );

			// 3D positional pool (T-047): world-positioned, distance-attenuated sources for ride effects/sounds.
			for ( var i = 0; i < Sfx3dSourceCount; i++ )
			{
				var src = sfx3dSources[i] = al.GenSource();
				al.SetSourceProperty( src, SourceBoolean.SourceRelative, false );
				al.SetSourceProperty( src, SourceFloat.ReferenceDistance, RefDistance );
				al.SetSourceProperty( src, SourceFloat.MaxDistance, MaxDistance );
				al.SetSourceProperty( src, SourceFloat.RolloffFactor, RolloffFactor );
			}

			// Adopt the persisted volume buses (T-051) so the first sound already plays at the player's
			// saved levels rather than the hardcoded defaults.
			var settings = GameSettings.Current;
			musicVolume = Math.Clamp( settings.MusicVolume, 0f, 1f );
			sfxVolume = Math.Clamp( settings.SfxVolume, 0f, 1f );
			speechVolume = Math.Clamp( settings.SpeechVolume, 0f, 1f );

			available = true;
			Log.Info( "OpenAL audio initialized." );
		}
		catch ( Exception e )
		{
			Log.Warning( $"OpenAL unavailable ({e.Message}); music disabled." );
			available = false;
		}

		return available;
	}
}
