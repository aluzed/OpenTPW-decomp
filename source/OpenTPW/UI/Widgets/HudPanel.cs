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
		=> (ParkHud.Current?.ContainsMouse() ?? false) || (BuildPanel.Current?.ContainsMouse() ?? false) || (ItemInfoWindow.Current?.ContainsMouse() ?? false)
			|| (ParkInfoWindow.Current?.ContainsMouse() ?? false) || (ResearchWindow.Current?.ContainsMouse() ?? false)
			|| (StaffWindow.Current?.ContainsMouse() ?? false) || (VisitorWindow.Current?.ContainsMouse() ?? false)
			|| (MessageTags.Current?.ContainsMouse() ?? false) || (ParkListWindow.Current?.ContainsMouse() ?? false) || (TrainingWindow.Current?.ContainsMouse() ?? false)
			|| (OptionsPanel.Current?.ContainsMouse() ?? false) || (FinancePanel.Current?.ContainsMouse() ?? false)
			|| (MinimapPanel.Current?.ContainsMouse() ?? false) || (SavePanel.Current?.ContainsMouse() ?? false)
			|| (ThemePanel.Current?.ContainsMouse() ?? false) || (LoanPanel.Current?.ContainsMouse() ?? false)
			|| (BankruptPanel.Current?.ContainsMouse() ?? false) || (ChallengePanel.Current?.ContainsMouse() ?? false);

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

	// Draw a labelled button (filled rect + left-aligned label). A label too long for the button is drawn smaller
	// so it doesn't spill over its neighbour ("PRICE+ 45" ran into SELL, "FEE+ 20" into LOANS).
	protected static void DrawButton( Rectangle rect, string label, Texture fill, float scale = LabelScale )
	{
		var mat = Material.UI;
		mat.Set( "Color", fill );
		Graphics.Quad( rect, mat );
		int textWidth = Font.Atlas.Measure( label );
		if ( textWidth > 0 )
			scale = MathF.Min( scale, (rect.Width - 12f) / textWidth );
		Graphics.DrawText( Font, label, rect.X + 8f, rect.Y + rect.Height - 7f * scale / LabelScale, TextAlign.Left, scale );
	}

	// The original's button icons (ui.wad stexture/b_*.wct, 51×51 with alpha: b_plus, b_loan, b_srides…; a "d" suffix
	// is the pressed look, "l" lit, "g" greyed). Cached; null when the game files lack one.
	private static readonly Dictionary<string, Texture?> icons = new( StringComparer.OrdinalIgnoreCase );

	protected static Texture? Icon( string name )
	{
		if ( icons.TryGetValue( name, out var t ) )
			return t;
		try { t = new Texture( $"ui/stexture/{name}.wct" ); }
		catch { t = null; }
		icons[name] = t;
		return t;
	}

	// Draw an icon into a square at the left of rect (the icon fills the rect's height).
	protected static void DrawIcon( Rectangle rect, Texture icon )
	{
		var mat = Material.UI;
		mat.Set( "Color", icon );
		Graphics.Quad( new Rectangle( rect.X, rect.Y, rect.Height, rect.Height ), mat );
	}

	// A button with the original's icon before its label (falls back to a plain button without the icon).
	protected static void DrawIconButton( Rectangle rect, string? iconName, string label, Texture fill, float scale = LabelScale )
	{
		if ( iconName == null || Icon( iconName ) is not { } icon )
		{
			DrawButton( rect, label, fill, scale );
			return;
		}
		var mat = Material.UI;
		mat.Set( "Color", fill );
		Graphics.Quad( rect, mat );
		DrawIcon( rect, icon );
		if ( label.Length == 0 )
			return;
		int textWidth = Font.Atlas.Measure( label );
		float room = rect.Width - rect.Height - 10f;
		if ( textWidth > 0 )
			scale = MathF.Min( scale, room / textWidth );
		Graphics.DrawText( Font, label, rect.X + rect.Height + 4f, rect.Y + rect.Height - 7f * scale / LabelScale, TextAlign.Left, scale );
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
