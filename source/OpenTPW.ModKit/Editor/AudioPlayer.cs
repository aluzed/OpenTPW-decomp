using OpenTPW.Common;
using NLayer;
using Silk.NET.OpenAL;

namespace OpenTPW.ModKit;

/// <summary>
/// Cross-platform audio playback (Windows / Linux / macOS) for the asset previewer.
/// MPEG audio (.mp2) is decoded to PCM with the game's minimp3 decoder (<see cref="MpegDecoder"/> — NLayer, kept as
/// the fallback where the native decoder isn't built, drops ~12% of the game's MPEG-2 Layer II frames) and played
/// through OpenAL, replacing the previous Windows-only NAudio / Media Foundation path. See docs/tickets/T-003, T-031.
/// </summary>
internal static unsafe class AudioPlayer
{
	private static AL al = null!;
	private static ALContext alc = null!;
	private static Device* device;
	private static Context* context;
	private static uint source;
	private static uint currentBuffer;

	private static bool triedInit;
	private static bool available;

	/// <summary>
	/// Decodes an in-memory MPEG (.mp2) stream and plays it once. Decoding errors are
	/// logged; a missing audio device (headless / CI) disables playback gracefully
	/// rather than throwing.
	/// </summary>
	public static void Play( byte[] mpegData )
	{
		if ( !TryDecode( mpegData, out var pcm, out var sampleRate, out var channels ) )
			return;

		if ( pcm.Length == 0 || !EnsureInitialized() )
			return;

		try
		{
			// Stop and recycle the previous buffer before queueing the new one.
			al.SourceStop( source );
			al.SetSourceProperty( source, SourceInteger.Buffer, 0 );
			if ( currentBuffer != 0 )
				al.DeleteBuffer( currentBuffer );

			currentBuffer = al.GenBuffer();
			var format = channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
			fixed ( short* data = pcm )
				al.BufferData( currentBuffer, format, data, pcm.Length * sizeof( short ), sampleRate );

			al.SetSourceProperty( source, SourceInteger.Buffer, (int)currentBuffer );
			al.SourcePlay( source );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Audio playback failed: {e.Message}" );
		}
	}

	private static bool TryDecode( byte[] mpegData, out short[] pcm, out int sampleRate, out int channels )
	{
		pcm = Array.Empty<short>();
		sampleRate = 0;
		channels = 0;

		try
		{
			if ( MpegDecoder.TryDecode( mpegData, out pcm, out sampleRate, out channels ) )
				return true;
		}
		catch ( DllNotFoundException )
		{
			// No native decoder on this platform: fall back to NLayer below.
		}

		try
		{
			using var ms = new MemoryStream( mpegData );
			using var mpeg = new MpegFile( ms );

			sampleRate = mpeg.SampleRate;
			channels = mpeg.Channels;

			// NLayer yields normalized float samples (-1..1); convert to 16-bit PCM.
			var samples = new List<float>();
			var chunk = new float[16384];
			int read;
			while ( ( read = mpeg.ReadSamples( chunk, 0, chunk.Length ) ) > 0 )
			{
				for ( var i = 0; i < read; i++ )
					samples.Add( chunk[i] );
			}

			pcm = new short[samples.Count];
			for ( var i = 0; i < samples.Count; i++ )
				pcm[i] = (short)( Math.Clamp( samples[i], -1f, 1f ) * short.MaxValue );

			return true;
		}
		catch ( Exception e )
		{
			Log.Error( $"Failed to decode audio: {e.Message}" );
			return false;
		}
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
				Log.Warning( "OpenAL: no audio device available; playback disabled." );
				return false;
			}

			context = alc.CreateContext( device, null );
			alc.MakeContextCurrent( context );
			source = al.GenSource();

			available = true;
		}
		catch ( Exception e )
		{
			Log.Warning( $"OpenAL unavailable ({e.Message}); playback disabled." );
			available = false;
		}

		return available;
	}
}
