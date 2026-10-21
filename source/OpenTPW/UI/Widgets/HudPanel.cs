using System.Numerics;
using OpenTPW.UI;

namespace OpenTPW;

/// <summary>
/// Shared base for the in-park HUD panels (build catalog, manage buttons): mouse mapping into the fixed
/// 1280×720 UI base space, hit-testing, solid-colour fills and a simple button drawer. Concrete panels
/// keep a <c>static Current</c> so <see cref="PointerOverUi"/> can tell <see cref="BuildMode"/> when a
/// click belongs to the UI (and must not also act on the tile behind it). See docs/tickets/T-038.
/// </summary>
internal abstract class HudPanel : Panel
{
	protected const float LabelScale = 1.3f;

	private static Font? font;
	protected static Font Font => font ??= new Font( GameLanguage.File( "GAME12.bf4" ) );

	/// <summary>True when the cursor is over any interactive HUD panel (so world tools ignore the click).</summary>
	public static bool PointerOverUi()
		=> (BuildPanel.Current?.ContainsMouse() ?? false) || (ManagePanel.Current?.ContainsMouse() ?? false)
			|| (OptionsPanel.Current?.ContainsMouse() ?? false) || (FinancePanel.Current?.ContainsMouse() ?? false)
			|| (MinimapPanel.Current?.ContainsMouse() ?? false) || (SavePanel.Current?.ContainsMouse() ?? false)
			|| (ThemePanel.Current?.ContainsMouse() ?? false) || (LoanPanel.Current?.ContainsMouse() ?? false);

	// Window mouse (pixels, Y-down) → base 1280×720 space (Y-up).
	protected static Vector2 MouseBase()
	{
		var w = Screen.Width <= 0 ? 1f : Screen.Width;
		var h = Screen.Height <= 0 ? 1f : Screen.Height;
		return new Vector2(
			Input.Mouse.Position.X / w * 1280f,
			(1f - Input.Mouse.Position.Y / h) * 720f );
	}

	protected static bool Contains( Rectangle r, Vector2 p )
		=> p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

	protected static Texture Solid( byte r, byte g, byte b, byte a ) => new( [r, g, b, a], 1, 1 );

	// Draw a labelled button (filled rect + left-aligned label).
	protected static void DrawButton( Rectangle rect, string label, Texture fill, float scale = LabelScale )
	{
		var mat = Material.UI;
		mat.Set( "Color", fill );
		Graphics.Quad( rect, mat );
		Graphics.DrawText( Font, label, rect.X + 8f, rect.Y + rect.Height - 7f * scale / LabelScale, TextAlign.Left, scale );
	}

	// Draw a horizontal progress/gauge bar: background track + a fill clamped to [0,1] of the width.
	protected static void DrawBar( Rectangle rect, float fraction, Texture fill, Texture track )
	{
		var mat = Material.UI;
		mat.Set( "Color", track );
		Graphics.Quad( rect, mat );

		fraction = Math.Clamp( fraction, 0f, 1f );
		if ( fraction <= 0f )
			return;
		mat.Set( "Color", fill );
		Graphics.Quad( new Rectangle( rect.X, rect.Y, rect.Width * fraction, rect.Height ), mat );
	}
}
