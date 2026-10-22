namespace OpenTPW;

/// <summary>
/// A ride's name sign. Ride models map their signboard to the shared <c>sign1</c>/<c>sign2</c> textures, which are
/// placeholders — "SIGN1"/"SIGN2" on pink — that the game swaps for the ride's own name at runtime; we drew the
/// placeholder, a pink bar by every ride. (Monkey.sam even declares them: <c>SignTextures[0..1].FileName</c> =
/// sign1.tga / sign2.tga; the other 123 rides/stalls/sideshows follow the same names by convention.) This renders the ride's name (one or two lines, scaled to fit) in the
/// front-end's MENUMED font, dark on a cream board.
/// </summary>
public static class RideSign
{
	public const int Size = 128;

	private static readonly (byte R, byte G, byte B) Board = (238, 224, 184);
	private static readonly (byte R, byte G, byte B) Ink = (70, 38, 16);

	private static FontAtlas? atlas;

	/// <summary>Whether a model material is one of the placeholder sign textures.</summary>
	public static bool IsSignMaterial( string materialName ) =>
		materialName.Trim( '\0', ' ' ) is var n && (n.Equals( "sign1", StringComparison.OrdinalIgnoreCase ) || n.Equals( "sign2", StringComparison.OrdinalIgnoreCase ));

	// The boards made so far, so a repaint can tell them among a material's textures.
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, object> boards = new();

	/// <summary>Whether <paramref name="texture"/> is a name board made by <see cref="For"/>.</summary>
	public static bool IsSignTexture( Texture texture ) => boards.TryGetValue( texture, out _ );

	/// <summary>The sign texture for <paramref name="name"/> (falls back to a blank board without the font).</summary>
	public static Texture For( string name )
	{
		var board = Make( name );
		boards.AddOrUpdate( board, true );
		return board;
	}

	private static Texture Make( string name )
	{
		try
		{
			atlas ??= new FontAtlas( new BF4File( FileSystem.OpenRead( GameLanguage.File( "MENUMED.bf4" ) ) ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[sign] font unavailable: {e.Message}" );
		}
		return new Texture( Render( atlas, name, Size, Size ), Size, Size );
	}

	/// <summary>
	/// The sign's RGBA pixels (row 0 = top): <paramref name="text"/> centred on the board, split onto two lines at the
	/// middle space when that fits better, scaled to fill ~90% of the width / ~70% of the height. Pure.
	/// </summary>
	public static byte[] Render( FontAtlas? font, string text, int width, int height )
	{
		var rgba = new byte[width * height * 4];
		for ( int i = 0; i < rgba.Length; i += 4 )
		{
			rgba[i] = Board.R; rgba[i + 1] = Board.G; rgba[i + 2] = Board.B; rgba[i + 3] = 255;
		}
		if ( font == null || string.IsNullOrWhiteSpace( text ) )
			return rgba;

		var lines = SplitLines( text.Trim(), font );
		int widest = Math.Max( 1, lines.Max( font.Measure ) );
		int lineHeight = Math.Max( 1, font.LineHeight );
		float scale = MathF.Min( width * 0.9f / widest, height * 0.7f / (lines.Length * lineHeight) );
		float top = (height - lines.Length * lineHeight * scale) / 2f;

		for ( int l = 0; l < lines.Length; l++ )
		{
			float penX = (width - font.Measure( lines[l] ) * scale) / 2f;
			float lineTop = top + l * lineHeight * scale;
			foreach ( var c in lines[l] )
			{
				if ( !font.Glyphs.TryGetValue( c, out var g ) )
					continue;
				Blit( font, g, rgba, width, height, penX + g.XBearing * scale, lineTop + g.YBearing * scale, scale );
				penX += g.Advance * scale;
			}
		}
		return rgba;
	}

	// One line if it fits as well as two, else split at the space nearest the middle.
	private static string[] SplitLines( string text, FontAtlas font )
	{
		int space = -1;
		for ( int i = 0; i < text.Length; i++ )
			if ( text[i] == ' ' && (space < 0 || Math.Abs( i - text.Length / 2 ) < Math.Abs( space - text.Length / 2 )) )
				space = i;
		if ( space < 0 )
			return [text];
		string a = text[..space], b = text[(space + 1)..];
		return font.Measure( text ) > 1.6f * Math.Max( font.Measure( a ), font.Measure( b ) ) ? [a, b] : [text];
	}

	// Nearest-neighbour copy of a glyph's coverage (the atlas alpha) as ink over the board.
	private static void Blit( FontAtlas font, FontAtlas.Glyph g, byte[] rgba, int width, int height, float x, float y, float scale )
	{
		int w = (int)MathF.Ceiling( g.Width * scale ), h = (int)MathF.Ceiling( g.Height * scale );
		for ( int dy = 0; dy < h; dy++ )
			for ( int dx = 0; dx < w; dx++ )
			{
				int tx = (int)(x + dx), ty = (int)(y + dy);
				if ( tx < 0 || ty < 0 || tx >= width || ty >= height )
					continue;
				int sx = g.X + Math.Min( g.Width - 1, (int)(dx / scale) ), sy = g.Y + Math.Min( g.Height - 1, (int)(dy / scale) );
				float a = font.Pixels[(sy * font.Width + sx) * 4 + 3] / 255f;
				if ( a <= 0f )
					continue;
				int o = (ty * width + tx) * 4;
				rgba[o] = (byte)(rgba[o] + (Ink.R - rgba[o]) * a);
				rgba[o + 1] = (byte)(rgba[o + 1] + (Ink.G - rgba[o + 1]) * a);
				rgba[o + 2] = (byte)(rgba[o + 2] + (Ink.B - rgba[o + 2]) * a);
			}
	}
}
