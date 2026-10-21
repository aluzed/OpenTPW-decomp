using StbImageSharp;

namespace OpenTPW;

/// <summary>
/// The distance-fog colour the world shaders blend toward (<c>g_vFogColor</c>). It was a hard-coded jungle cyan in
/// every shader, so zooming out in Halloween World or Space Zone washed the park in cyan. A park now fogs toward its
/// own horizon: the average non-white colour of its <c>sky/sky_cyl.tga</c> (the horizon gradient — jungle cyan, hallow navy,
/// space orange). The front-end keeps the cyan.
/// </summary>
public static class Fog
{
	public static readonly System.Numerics.Vector4 Default = new( 0.301f, 0.84f, 1f, 1f );

	public static System.Numerics.Vector4 Color { get; set; } = Default;

	/// <summary>Fog toward the horizon colour of park <paramref name="level"/>, or the default when it can't be read.</summary>
	public static void UseSkyOf( string level )
	{
		try
		{
			var bytes = FileSystem.ReadAllBytes( $"levels/{level}/sky/sky_cyl.tga" );
			var image = ImageResult.FromMemory( bytes, ColorComponents.RedGreenBlueAlpha );
			Color = HorizonColor( image.Data ) ?? Default;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[fog] {level}: {e.Message}" );
			Color = Default;
		}
	}

	/// <summary>Average colour of the RGBA pixels that aren't near-white haze (null when they all are). Pure.</summary>
	public static System.Numerics.Vector4? HorizonColor( byte[] rgba )
	{
		double r = 0, g = 0, b = 0;
		int n = 0;
		for ( int i = 0; i + 3 < rgba.Length; i += 4 )
		{
			if ( rgba[i] + rgba[i + 1] + rgba[i + 2] >= 3 * 230 )
				continue;
			r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2];
			n++;
		}
		return n == 0 ? null : new System.Numerics.Vector4( (float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255), 1f );
	}
}
