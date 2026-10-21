using System.Numerics;

namespace OpenTPW;

internal static partial class Graphics
{
	/// <summary>
	/// Draws a string with a bitmap <see cref="Font"/>. Each glyph is appended to the shared UI
	/// batch (see <see cref="Graphics"/>.Batch); all glyphs share the font texture, so they merge
	/// into one draw with any adjacent same-texture UI geometry. No per-call heap allocation.
	/// UI-space coordinates; <paramref name="x"/>/<paramref name="y"/> is the pen origin.
	/// </summary>
	public static void DrawText( Font font, string text, float x, float y, TextAlign align = TextAlign.Left, float scale = 1f )
		=> DrawText( font, text, x, y, default, align, scale );

	/// <summary>Draws a string tinted by <paramref name="color"/> (RGB 0–1; zero = the font's own colours).</summary>
	public static void DrawText( Font font, string text, float x, float y, Vector3 color, TextAlign align = TextAlign.Left, float scale = 1f )
	{
		var placed = font.Atlas.Layout( text, x, y, align, scale );
		if ( placed.Count == 0 )
			return;

		var screenMatrix = CreateScreenMatrix( BaseScreenSize );

		var material = Material.UI;
		material.Set( "Color", font.Texture );

		foreach ( var g in placed )
		{
			// Whole pixels: the atlas is point-sampled, so a glyph straddling pixels at a fractional scale lost a row or a
			// column — the dot of "!", the stem of a thin "l".
			var rect = new Rectangle( MathF.Round( g.X ), MathF.Round( g.Y ), MathF.Max( 1f, MathF.Round( g.Width ) ), MathF.Max( 1f, MathF.Round( g.Height ) ) );
			// The UI shader samples (x, 1 - y), so the V we hand it must be 1 - atlasV for the
			// glyph to land on its own atlas rows (otherwise it samples a different row = garble).
			var uv = new Rectangle( g.U0, 1f - g.V1, g.U1 - g.U0, g.V1 - g.V0 );

			_quadVertices[0] = new( new Vector3( rect.TopLeft ) * screenMatrix, color, uv.TopLeft );
			_quadVertices[1] = new( new Vector3( rect.TopRight ) * screenMatrix, color, uv.TopRight );
			_quadVertices[2] = new( new Vector3( rect.BottomLeft ) * screenMatrix, color, uv.BottomLeft );
			_quadVertices[3] = new( new Vector3( rect.BottomRight ) * screenMatrix, color, uv.BottomRight );

			AppendGeometry( material, _quadVertices, _quadIndices );
		}
	}
}
