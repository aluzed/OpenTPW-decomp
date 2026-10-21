using System.Runtime.InteropServices;

namespace OpenTPW.Common;

/// <summary>
/// MPEG audio (.mp2 / .mp3) → interleaved 16-bit PCM through the bundled minimp3 wrapper (<c>tpwmp3</c>, built from
/// <c>OpenTPW/Audio/native/tpwmp3.c</c> and copied next to the app). Shared by the game and the ModKit previewer so
/// both decode the game's 22 kHz MPEG-2 Layer II correctly (NLayer drops ~12% of its frames — T-031).
/// </summary>
public static class MpegDecoder
{
	[DllImport( "tpwmp3", CallingConvention = CallingConvention.Cdecl )]
	private static extern int tpw_mp3_decode( byte[] data, int size, out nint outPcm, out int samples, out int channels, out int hz );

	[DllImport( "tpwmp3", CallingConvention = CallingConvention.Cdecl )]
	private static extern void tpw_mp3_free( nint p );

	/// <summary>Decode a whole MPEG buffer. Throws <see cref="DllNotFoundException"/> where the native decoder
	/// hasn't been built; false on a decode failure.</summary>
	public static bool TryDecode( byte[] mpegData, out short[] pcm, out int sampleRate, out int channels )
	{
		pcm = Array.Empty<short>();
		sampleRate = 0;
		channels = 0;

		if ( tpw_mp3_decode( mpegData, mpegData.Length, out var ptr, out var samples, out var ch, out var hz ) != 0
			|| ptr == 0 || samples <= 0 )
			return false;

		try
		{
			pcm = new short[samples];
			Marshal.Copy( ptr, pcm, 0, samples );
		}
		finally
		{
			tpw_mp3_free( ptr );
		}

		sampleRate = hz;
		channels = ch;
		return true;
	}
}
