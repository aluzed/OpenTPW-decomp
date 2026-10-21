namespace OpenTPW;

/// <summary>
/// The original's in-game HUD (docs/10-ui-reference.md), from <c>ui.wad</c>'s own art: the balance top left (the
/// <c>$</c> glyph, the big cash digits and the trend arrow), the golden tickets top right, the handheld bottom left
/// (<c>mainpanel.MD2</c> with its beacon, the date LCD, the happiness thermometer and six round buttons) and the hint
/// strip along the bottom, which also carries what the advisor says. Positions are measured on captures of the
/// original at 720 px high: left-anchored pieces keep their x, right-anchored ones keep their distance to the right
/// edge, so the 4:3 layout holds on the 16:9 base screen.
/// </summary>
internal sealed class ParkHud : HudPanel
{
	public static ParkHud? Current { get; private set; }

	public ParkHud() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private static Font? cashFont, dateFont, hintFont;
	private static Font CashFont => cashFont ??= new Font( GameLanguage.File( "CASHBIG.bf4" ) );
	private static Font DateFont => dateFont ??= new Font( GameLanguage.File( "DATESMALL.bf4" ) );
	private static Font HintFont => hintFont ??= new Font( GameLanguage.File( "GAME8.bf4" ) );

	private static readonly System.Numerics.Vector3 LcdInk = new( 0.06f, 0.24f, 0.06f );

	private static Texture? hintBg;
	private static Texture HintBg => hintBg ??= Solid( 10, 10, 14, 150 );

	// Y-down capture coordinates (720 high) → the Y-up base space.
	private static Rectangle FromTop( float x, float top, float w, float h ) => new( x, 720f - top - h, w, h );
	private static Rectangle FromRight( float fromRight, float top, float w, float h ) => FromTop( 1280f - fromRight, top, w, h );

	// The handheld (ui.wad mainpanel.MD2 and friends), measured on the original.
	private static readonly Rectangle Beacon = FromTop( 39f, 398f, 38f, 60f );
	private static readonly Rectangle Body = FromTop( 20f, 477f, 200f, 238f );
	private static readonly Rectangle Lcd = FromTop( 78f, 491f, 106f, 24f );
	private static readonly Rectangle Gauge = FromTop( 34f, 507f, 41f, 131f );
	private const float ButtonSize = 70f; // the b_*.wct disc fills ~¾ of its texture: a ~52-px gold button

	/// <summary>The handheld's six buttons, in the original's order: buy, info, money, research, map, postcard.</summary>
	private static readonly (string Texture, float X, float Y, string Hint, Action? Act)[] Buttons =
	{
		("b_buy", 107f, 543f, "Build rides, shops and scenery", () => BuildPanel.Toggle()),
		("b_info", 163f, 543f, "Park information", () => ParkStatsPanel.ToggleInfo()),
		("b_money", 102f, 600f, "Finances and loans", () => FinancePanel.Current?.Toggle()),
		("b_resrch", 157f, 600f, "Research", null),
		("b_map", 66f, 655f, "Map of the park", () => MinimapPanel.Current?.Toggle()),
		("b_camera", 122f, 655f, "Postcard", null),
	};

	private static Rectangle ButtonRect( int i ) => FromTop( Buttons[i].X - ButtonSize / 2f, Buttons[i].Y - ButtonSize / 2f, ButtonSize, ButtonSize );

	private static readonly Dictionary<string, Texture?> textures = new( StringComparer.OrdinalIgnoreCase );

	private static Texture? Tex( string name )
	{
		if ( textures.TryGetValue( name, out var t ) )
			return t;
		try { t = new Texture( $"ui/textures/{name}.wct" ); }
		catch { t = null; }
		textures[name] = t;
		return t;
	}

	private static void DrawTex( string name, Rectangle rect )
	{
		if ( Tex( name ) is not { } t )
			return;
		var mat = Material.UI;
		mat.Set( "Color", t );
		Graphics.Quad( rect, mat );
	}

	/// <summary>True when the cursor is over the handheld (so world tools ignore the click).</summary>
	public bool ContainsMouse()
	{
		var m = MouseBase();
		return Contains( Body, m ) || Contains( Beacon, m );
	}

	// The balance a few seconds ago, for the trend arrow.
	private float trendFrom, trendAt = float.NegativeInfinity;
	private int trend;
	private int pressed = -1;

	protected override void OnUpdate()
	{
		if ( ParkFinances.Current is { } fin && Time.Now - trendAt > 4f )
		{
			trend = fin.Money > trendFrom + 0.5f ? 1 : fin.Money < trendFrom - 0.5f ? -1 : 0;
			trendFrom = fin.Money;
			trendAt = Time.Now;
		}

		var m = MouseBase();
		if ( Input.MouseLeftPressed )
		{
			pressed = -1;
			for ( int i = 0; i < Buttons.Length; i++ )
				if ( Contains( ButtonRect( i ), m ) && Buttons[i].Act != null )
				{
					pressed = i;
					pressedAt = Time.Now;
					Buttons[i].Act!();
				}
		}
	}

	private float pressedAt;

	protected override void OnRender()
	{
		if ( ParkFinances.Current is not { } fin )
			return;

		DrawCash( fin );
		DrawTickets();
		DrawHandheld();
		DrawHint();
	}

	// "$ 50000" + the trend arrow, top left.
	private void DrawCash( ParkFinances fin )
	{
		DrawTex( fin.Money < 0 ? "Cashno_alpha" : "Cash_alpha", FromTop( 50f, 30f, 64f, 90f ) );
		var text = $"{MathF.Abs( fin.Money ):0}";
		float scale = 46f / MathF.Max( 1f, CashFont.Atlas.LineHeight );
		Graphics.DrawText( CashFont, text, 118f, 720f - 52f, TextAlign.Left, scale );
		float right = 120f + CashFont.Atlas.Measure( text ) * scale + 10f;
		if ( trend != 0 )
			DrawTex( trend > 0 ? "green_up" : "red_down", FromTop( right, 56f, 26f, 42f ) );
	}

	// "n ×" and a golden ticket, top right.
	private static void DrawTickets()
	{
		int won = GoldenTicketGoals.Current?.Won.Count ?? 0;
		Graphics.DrawText( Font, $"{won} ×", 1280f - 132f, 720f - 92f, TextAlign.Right, 1.2f );
		DrawTex( "gticket", FromRight( 125f, 74f, 44f, 44f ) );
	}

	private void DrawHandheld()
	{
		UiModel.Load( "aerialtop" )?.Draw( Beacon );
		UiModel.Load( "mainpanel", eyeDistance: 128f )?.Draw( Body );

		UiModel.Load( "date" )?.Draw( Lcd );
		if ( GameClock.Current is { } clock )
		{
			var d = clock.Date;
			float scale = 15f / MathF.Max( 1f, DateFont.Atlas.LineHeight );
			Graphics.DrawText( DateFont, $"{d.Day}.{d.Month}.{d.Year}", Lcd.X + Lcd.Width / 2f, Lcd.Y + Lcd.Height - 4f,
				LcdInk, TextAlign.Center, scale );
		}

		// The thermometer: its rim, the black capsule with the smileys, then the orange gradient filled up to the
		// park's happiness from the bottom.
		UiModel.Load( "gauge" )?.Draw( Gauge );
		var capsule = new Rectangle( Gauge.X + 7f, Gauge.Y + 7f, Gauge.Width - 14f, Gauge.Height - 14f );
		DrawTex( "meter", capsule );
		float happy = Math.Clamp( Peep.AverageHappiness / 100f, 0f, 1f );
		if ( happy > 0f && Tex( "happygrad" ) is { } grad )
		{
			var mat = Material.UI;
			mat.Set( "Color", grad );
			Graphics.Quad( capsule with { Height = capsule.Height * happy }, new Rectangle( 0f, 0f, 1f, happy ), mat );
		}

		var m = MouseBase();
		for ( int i = 0; i < Buttons.Length; i++ )
		{
			var rect = ButtonRect( i );
			bool down = pressed == i && Time.Now - pressedAt < 0.2f;
			bool idle = Buttons[i].Act == null;
			string name = idle ? Buttons[i].Texture + "g" : down ? Buttons[i].Texture + "d" : Buttons[i].Texture;
			if ( Tex( name ) == null )
				name = Buttons[i].Texture;
			if ( Contains( rect, m ) && !idle && !down )
				rect = new Rectangle( rect.X - 2f, rect.Y - 2f, rect.Width + 4f, rect.Height + 4f );
			DrawTex( name, rect );
		}
	}

	/// <summary>The line shown in the hint strip: the button under the cursor, else what the advisor is saying.</summary>
	private static string? HintText()
	{
		var m = MouseBase();
		for ( int i = 0; i < Buttons.Length; i++ )
			if ( Contains( ButtonRect( i ), m ) )
				return Buttons[i].Act == null ? $"{Buttons[i].Hint} (not available yet)" : Buttons[i].Hint;
		if ( Advisor.Current is { } advisor && !string.IsNullOrEmpty( advisor.ClipName ) && advisor.Elapsed <= advisor.ClipLength + 2f )
			return AdvisorTips.TextFor( advisor.ActiveMessage );
		return null;
	}

	// The translucent strip at the bottom centre; a long line wraps onto more rows and the strip grows with it.
	private static void DrawHint()
	{
		if ( HintText() is not { Length: > 0 } text )
			return;
		const float Width = 560f, Scale = 1.4f, Pad = 6f;
		var lines = Wrap( HintFont, text, (Width - 2f * Pad) / Scale );
		float lineH = HintFont.Atlas.LineHeight * Scale;
		float height = lines.Count * lineH + 2f * Pad;
		var strip = new Rectangle( 640f - Width / 2f, 6f, Width, height );
		var mat = Material.UI;
		mat.Set( "Color", HintBg );
		Graphics.Quad( strip, mat );
		for ( int i = 0; i < lines.Count; i++ )
			Graphics.DrawText( HintFont, lines[i], 640f, strip.Y + strip.Height - Pad - i * lineH, TextAlign.Center, Scale );
	}

	/// <summary>Greedy word wrap of <paramref name="text"/> to <paramref name="maxWidth"/> font pixels.</summary>
	public static List<string> Wrap( Font font, string text, float maxWidth )
	{
		var lines = new List<string>();
		var line = "";
		foreach ( var word in text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var candidate = line.Length == 0 ? word : $"{line} {word}";
			if ( line.Length > 0 && font.Atlas.Measure( candidate ) > maxWidth )
			{
				lines.Add( line );
				line = word;
			}
			else
				line = candidate;
		}
		if ( line.Length > 0 )
			lines.Add( line );
		return lines;
	}
}
