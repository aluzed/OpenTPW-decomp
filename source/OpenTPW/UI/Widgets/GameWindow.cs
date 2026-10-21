using NVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The original's window (docs/10-ui-reference.md): a <c>ui.wad</c> frame model (<c>w_big</c>, <c>w_park</c>…) — a blue
/// bezel round a green wave panel with a column of studs on the right —, the title in yellow at the top, the balance
/// top right, content in black inset boxes with thin blue-green borders, red→green gauges and round buttons down the
/// right-hand column. Coordinates are those of the 4:3 captures (960×720, Y down), centred on the 16:9 base screen.
/// </summary>
internal abstract class GameWindow : HudPanel
{
	protected static readonly SVector3 TitleInk = new( 1f, 0.88f, 0.2f );
	protected static readonly SVector3 LockedInk = new( 0.95f, 0.15f, 0.12f );
	protected static readonly SVector3 HeaderInk = new( 1f, 1f, 0.75f );

	private static Font? small, medium;
	protected static Font Small => small ??= new Font( GameLanguage.File( "GAME8.bf4" ) );
	protected static Font Medium => medium ??= new Font( GameLanguage.File( "GAME12.bf4" ) );

	private static Texture? inset, insetEdge, header, selected, barTrack;
	private static Texture Inset => inset ??= Solid( 0, 0, 0, 255 );
	private static Texture InsetEdge => insetEdge ??= Solid( 24, 54, 140, 255 );
	protected static Texture Header => header ??= Solid( 18, 110, 150, 255 );
	protected static Texture Selected => selected ??= Solid( 18, 110, 150, 255 );
	private static Texture BarTrack => barTrack ??= Solid( 60, 60, 60, 255 );
	private static Texture? gauge;
	// The original's gauges run red → yellow → green.
	private static Texture Gauge => gauge ??= new Texture( Enumerable.Range( 0, 32 ).SelectMany( i =>
	{
		float t = i / 31f;
		byte r = (byte)(t < 0.5f ? 230 : 230 - (t - 0.5f) * 2f * 150f), g = (byte)(t < 0.5f ? 40 + t * 2f * 190f : 230);
		return new byte[] { r, g, 30, 255 };
	} ).ToArray(), 32, 1 );

	/// <summary>A rectangle given in capture coordinates (960×720, Y down) — the 4:3 picture centred on the base screen.</summary>
	protected static Rectangle At( float x, float top, float w, float h ) => new( x + 160f, 720f - top - h, w, h );

	/// <summary>Is the window open.</summary>
	public bool IsOpen { get; protected set; }

	protected abstract Rectangle Frame { get; }

	/// <summary>True when the cursor is over the open window (so world tools ignore the click).</summary>
	public bool ContainsMouse() => IsOpen && Contains( Frame, MouseBase() );

	protected void DrawFrame( string model ) => UiModel.Load( model )?.Draw( Frame );

	protected static void Text( Font font, string text, float x, float top, float height, SVector3 ink = default, TextAlign align = TextAlign.Left )
		=> Graphics.DrawText( font, text, x + 160f, 720f - top, ink, align, Scale( font, height ) );

	protected static float Measure( Font font, string text, float height )
		=> font.Atlas.Measure( text ) * Scale( font, height );

	/// <summary>The whole-number scale closest to <paramref name="height"/> pixels: the bitmap fonts are point-sampled,
	/// and a fractional scale drops rows (the dot of "!") or columns (a thin "l").</summary>
	protected static float Scale( Font font, float height ) => MathF.Max( 1f, MathF.Round( height / MathF.Max( 1f, font.Atlas.LineHeight ) ) );

	/// <summary>A black inset box with the original's thin blue border.</summary>
	protected static void DrawInset( Rectangle r )
	{
		Fill( new Rectangle( r.X - 3f, r.Y - 3f, r.Width + 6f, r.Height + 6f ), InsetEdge );
		Fill( r, Inset );
	}

	protected static void Fill( Rectangle r, Texture t, SVector3 tint = default )
	{
		var mat = Material.UI;
		mat.Set( "Color", t );
		Graphics.Quad( r, new Rectangle( 0f, 0f, 1f, 1f ), mat, tint );
	}

	private static readonly SVector3 Dimmed = new( 0.45f, 0.45f, 0.5f );

	/// <summary>A red→green gauge filled to <paramref name="fraction"/>.</summary>
	protected static void DrawGauge( Rectangle r, float fraction )
	{
		Fill( r, BarTrack );
		fraction = Math.Clamp( fraction, 0f, 1f );
		if ( fraction <= 0f )
			return;
		var mat = Material.UI;
		mat.Set( "Color", Gauge );
		Graphics.Quad( r with { Width = r.Width * fraction }, new Rectangle( 0f, 0f, fraction, 1f ), mat );
	}

	private static readonly Dictionary<string, Texture?> icons = new( StringComparer.OrdinalIgnoreCase );

	protected static Texture? UiTexture( string name )
	{
		if ( icons.TryGetValue( name, out var t ) )
			return t;
		try { t = new Texture( $"ui/textures/{name}.wct" ); }
		catch { t = null; }
		icons[name] = t;
		return t;
	}

	/// <summary>A round ui.wad button (b_*.wct; its "d" look when pressed / selected), slightly larger under the cursor.
	/// Returns true when clicked.</summary>
	protected static bool RoundButton( Rectangle r, string texture, bool selected = false, bool enabled = true )
	{
		bool over = Contains( r, MouseBase() );
		var name = selected && UiTexture( texture + "d" ) != null ? texture + "d" : texture;
		var draw = over && enabled ? new Rectangle( r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f ) : r;
		if ( !enabled && UiTexture( texture + "g" ) is { } grey )
			Fill( draw, grey );
		else if ( UiTexture( name ) is { } t )
			Fill( draw, t, enabled ? default : Dimmed );
		else
			UiModel.Load( texture )?.Draw( draw, null, enabled ? default : Dimmed ); // a button with no flat texture (b_upgrade)
		return enabled && over && Input.MouseLeftPressed;
	}

	private static readonly Dictionary<int, Texture> solids = new();

	/// <summary>An opaque colour as a texture (cached).</summary>
	protected static Texture SolidOf( byte r, byte g, byte b )
	{
		int key = (r << 16) | (g << 8) | b;
		if ( !solids.TryGetValue( key, out var t ) )
			solids[key] = t = Solid( r, g, b, 255 );
		return t;
	}

	/// <summary>The original's graph: a black box with a green grid, each series a dotted line scaled to its own
	/// range over <paramref name="months"/> monthly slots (the newest on the right of the data).</summary>
	protected static void DrawLineGraph( Rectangle box, int months, IReadOnlyList<(float[] Values, Texture Colour)> series )
	{
		DrawInset( box );
		var grid = SolidOf( 20, 90, 30 );
		for ( int k = 1; k < 10; k++ )
		{
			Fill( new Rectangle( box.X + box.Width * k / 10f, box.Y, 1f, box.Height ), grid );
			Fill( new Rectangle( box.X, box.Y + box.Height * k / 10f, box.Width, 1f ), grid );
		}
		if ( series.All( s => s.Values.Length < 2 ) )
		{
			Graphics.DrawText( Small, "The graph fills in month by month", box.X + box.Width / 2f, box.Y + box.Height / 2f, default, TextAlign.Center, Scale( Small, 11f ) );
			return;
		}
		foreach ( var (values, colour) in series )
		{
			if ( values.Length < 2 )
				continue;
			float min = MathF.Min( 0f, values.Min() ), max = MathF.Max( 1f, values.Max() * 1.1f );
			var points = values.Select( ( v, i ) => new NVector2(
				box.X + 6f + (box.Width - 12f) * i / MathF.Max( 1f, months - 1f ),
				box.Y + 6f + (box.Height - 12f) * (v - min) / (max - min) ) ).ToArray();
			for ( int i = 1; i < points.Length; i++ )
				Line( points[i - 1], points[i], colour );
			foreach ( var p in points )
				Fill( new Rectangle( p.X - 2f, p.Y - 2f, 4f, 4f ), colour );
		}
	}

	private static readonly uint[] QuadIndices = { 0, 1, 2, 2, 1, 3 };
	private static readonly NVector2[] QuadUvs = { new( 0, 0 ), new( 1, 0 ), new( 0, 1 ), new( 1, 1 ) };

	private static void Line( NVector2 a, NVector2 b, Texture colour )
	{
		var d = b - a;
		if ( d.LengthSquared() < 1e-4f )
			return;
		var n = NVector2.Normalize( new NVector2( -d.Y, d.X ) );
		var mat = Material.UI;
		mat.Set( "Color", colour );
		Graphics.Triangles( new[] { a - n, a + n, b - n, b + n }, QuadUvs, QuadIndices, mat );
	}

	/// <summary>
	/// The original's message box (<c>w_dialog</c>): the frame in <paramref name="r"/>, the title in yellow, the text
	/// wrapped to the panel. Returns the ✓ button's rectangle (on the frame's right-hand column).
	/// </summary>
	protected static Rectangle DrawDialog( Rectangle r, string? title, IEnumerable<string> paragraphs )
	{
		UiModel.Load( "w_dialog" )?.Draw( r );
		// The text sits inside the black screen, clear of the bezel.
		float left = r.X + r.Width * 0.1f, width = r.Width * 0.66f, y = r.Y + r.Height * 0.84f;
		const float LineHeight = 19f;
		float scale = Scale( Medium, 15f );
		if ( title != null )
		{
			Graphics.DrawText( Medium, title, left + width / 2f, y, TitleInk, TextAlign.Center, Scale( Medium, 17f ) );
			y -= 26f;
		}
		foreach ( var paragraph in paragraphs )
		{
			foreach ( var line in ParkHud.Wrap( Medium, paragraph, width / scale ) )
			{
				Graphics.DrawText( Medium, line, left, y, default, TextAlign.Left, scale );
				y -= LineHeight;
			}
			y -= 6f;
		}
		return new Rectangle( r.X + r.Width * 0.84f, r.Y + r.Height * 0.12f, 42f, 42f );
	}

	/// <summary>The window's title (yellow, centred at the top) and the balance at the top right (none when
	/// <paramref name="right"/> is 0).</summary>
	protected static void DrawHeading( string title, float centreX, float right, float top )
	{
		Text( Medium, title, centreX, top, 17f, TitleInk, TextAlign.Center );
		if ( right > 0f && ParkFinances.Current is { } fin )
			Text( Medium, $"Cash  $ {fin.Money:0}", right, top, 17f, default, TextAlign.Right );
	}
}
