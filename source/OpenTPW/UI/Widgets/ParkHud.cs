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
	private static readonly Rectangle Beacon = FromTop( 41f, 420f, 38f, 64f );
	private static readonly Rectangle Body = FromTop( 30f, 484f, 182f, 220f );
	private static readonly Rectangle Lcd = FromTop( 78f, 491f, 106f, 24f );
	private static readonly Rectangle Gauge = FromTop( 34f, 507f, 41f, 131f );
	private const float ButtonSize = 70f; // the b_*.wct disc fills ~¾ of its texture: a ~52-px gold button

	/// <summary>The handheld's six buttons, in the original's order: buy, info, money, research, map, postcard.</summary>
	private static readonly (string Texture, float X, float Y, int Hint, Action? Act)[] Buttons =
	{
		("b_buy", 107f, 543f, UiHelp.Buy, () => BuildPanel.Toggle()),
		("b_info", 163f, 543f, UiHelp.Info, () => ParkStatsPanel.ToggleInfo()),
		("b_money", 102f, 600f, UiHelp.Finance, () => FinancePanel.Current?.Toggle()),
		("b_resrch", 157f, 600f, UiHelp.Research, null),
		("b_map", 66f, 655f, UiHelp.Map, () => MinimapPanel.Current?.Toggle()),
		("b_camera", 122f, 655f, UiHelp.Camera, null),
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

	private static void DrawTex( string name, Rectangle rect, System.Numerics.Vector3 tint = default )
	{
		if ( Tex( name ) is not { } t )
			return;
		var mat = Material.UI;
		mat.Set( "Color", t );
		Graphics.Quad( rect, new Rectangle( 0f, 0f, 1f, 1f ), mat, tint );
	}

	// The casing the original shows around mainpanel.MD2 — that mesh is only the green face and a thin rim, the
	// engine's shading made the rest: a rounded mid-blue slab a little larger than the face (filling its bottom-right
	// notch as the grip) and the column of grey studs down its right edge (grip.wct). Measured on the original.
	private static readonly Rectangle Shell = FromTop( 16f, 476f, 210f, 240f );
	private static readonly Rectangle Studs = FromTop( 208f, 552f, 22f, 112f );

	// The casing is the original's mid blue, lighter at the top — a rounded slab built once.
	private static Texture? shellTexture;
	private static Texture ShellTexture => shellTexture ??= RoundedSlab( 64, 128, 20, (40, 70, 185), (22, 40, 130) );

	private static Texture RoundedSlab( int w, int h, int radius, (int R, int G, int B) top, (int R, int G, int B) bottom )
	{
		var px = new byte[w * h * 4];
		for ( int y = 0; y < h; y++ )
			for ( int x = 0; x < w; x++ )
			{
				// Distance outside the rounded corners.
				float cx = Math.Clamp( x + 0.5f, radius, w - radius ), cy = Math.Clamp( y + 0.5f, radius, h - radius );
				float d = MathF.Sqrt( (x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) );
				float t = (float)y / (h - 1);
				int o = (y * w + x) * 4;
				px[o] = (byte)(top.R + (bottom.R - top.R) * t);
				px[o + 1] = (byte)(top.G + (bottom.G - top.G) * t);
				px[o + 2] = (byte)(top.B + (bottom.B - top.B) * t);
				px[o + 3] = d <= radius ? (byte)255 : (byte)0;
			}
		return new Texture( px, w, h );
	}
	private static readonly System.Numerics.Vector3 Recess = new( 0.05f, 0.07f, 0.22f );

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

	// The last purchase, shown under the balance in yellow while it fades ("$ 650").
	private float spentAmount, spentAt = float.NegativeInfinity;
	private ParkFinances? hooked;
	private const float SpentSeconds = 2.5f;
	private static readonly System.Numerics.Vector3 SpentInk = new( 1f, 0.85f, 0.1f );

	protected override void OnUpdate()
	{
		if ( ParkFinances.Current is { } f && f != hooked )
		{
			hooked = f;
			f.Spent += amount => { spentAmount = Time.Now - spentAt < SpentSeconds ? spentAmount + amount : amount; spentAt = Time.Now; };
		}

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
		// The last purchase drops in under the balance and fades (the font can't fade, so it shrinks away).
		float age = Time.Now - spentAt;
		if ( age < SpentSeconds )
		{
			float popScale = 22f / MathF.Max( 1f, CashFont.Atlas.LineHeight ) * (1f - 0.3f * age / SpentSeconds);
			Graphics.DrawText( CashFont, $"$ {spentAmount:0}", 175f, 720f - 132f - age * 6f, SpentInk, TextAlign.Left, popScale );
		}
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
		var mat = Material.UI;
		mat.Set( "Color", ShellTexture );
		Graphics.Quad( Shell, mat );
		// The studs: the right half of grip.wct (two grey studs on blue), stacked.
		if ( Tex( "grip" ) is { } grip )
		{
			mat.Set( "Color", grip );
			const int Repeats = 3;
			float h = Studs.Height / Repeats;
			for ( int i = 0; i < Repeats; i++ )
				Graphics.Quad( new Rectangle( Studs.X, Studs.Y + i * h, Studs.Width, h ), new Rectangle( 0.55f, 0f, 0.45f, 1f ), mat );
		}
		UiModel.Load( "mainpanel" )?.Draw( Body );

		UiModel.Load( "date" )?.Draw( Lcd );
		if ( GameClock.Current is { } clock )
		{
			var d = clock.Date;
			float scale = MathF.Max( 1f, MathF.Round( 17f / MathF.Max( 1f, DateFont.Atlas.LineHeight ) ) ); // whole: point-sampled
			Graphics.DrawText( DateFont, $"{d.Day}.{d.Month}.{d.Year}", Lcd.X + Lcd.Width / 2f, Lcd.Y + Lcd.Height - 4f,
				LcdInk, TextAlign.Center, scale );
		}

		// The thermometer: its rim, the black capsule with the smileys, then the orange gradient filled up to the
		// park's happiness from the bottom.
		// meter.wct holds both capsules side by side: the full (orange, smileys) on the left, the empty (black) on the
		// right. Draw the empty one, then the full one cut at the park's happiness, from the bottom.
		UiModel.Load( "gauge" )?.Draw( Gauge );
		var capsule = new Rectangle( Gauge.X + 7f, Gauge.Y + 7f, Gauge.Width - 14f, Gauge.Height - 14f );
		float happy = Math.Clamp( Peep.AverageHappiness / 100f, 0f, 1f );
		if ( Tex( "meter" ) is { } meter )
		{
			mat.Set( "Color", meter );
			Graphics.Quad( capsule, new Rectangle( 0.5f, 0f, 0.5f, 1f ), mat );
			if ( happy > 0f )
				Graphics.Quad( capsule with { Height = capsule.Height * happy }, new Rectangle( 0f, 0f, 0.5f, happy ), mat );
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
			// Each button sits in a dark recess in the face: its own disc, darkened and a little larger.
			DrawTex( Buttons[i].Texture, new Rectangle( rect.X - 5f, rect.Y - 5f, rect.Width + 10f, rect.Height + 10f ), Recess );
			if ( Contains( rect, m ) && !idle && !down )
				rect = new Rectangle( rect.X - 2f, rect.Y - 2f, rect.Width + 4f, rect.Height + 4f );
			DrawTex( name, rect );
		}
	}

	/// <summary>The line shown in the hint strip, as the original: the HUD control under the cursor, else what the
	/// advisor is saying, else what a click on the park would do.</summary>
	private static string? HintText()
	{
		var m = MouseBase();
		for ( int i = 0; i < Buttons.Length; i++ )
			if ( Contains( ButtonRect( i ), m ) )
				return UiHelp.Text( Buttons[i].Hint, "" ) + (Buttons[i].Act == null ? "  (not available yet)" : "");
		if ( Contains( Gauge, m ) )
			return UiHelp.Text( UiHelp.Happiness, "Happiness of the park visitors" );
		if ( Contains( Lcd, m ) )
			return UiHelp.Text( UiHelp.Date, "Current date" );
		if ( Contains( FromTop( 50f, 30f, 220f, 90f ), m ) )
			return UiHelp.Text( UiHelp.Balance, "Your bank balance" );
		if ( Contains( FromRight( 175f, 74f, 100f, 44f ), m ) )
			return UiHelp.Text( UiHelp.GoldenTickets, "Number of golden tickets you currently have" );
		if ( HudPanel.PointerOverUi() )
			return null;

		if ( Advisor.Current is { } advisor && !string.IsNullOrEmpty( advisor.ClipName ) && advisor.Elapsed <= advisor.ClipLength + 2f )
			return AdvisorTips.TextFor( advisor.ActiveMessage );

		if ( BuildMode.Current is not { } build || build.HoveredTile is not { } tile )
			return null;
		if ( build.Selected >= 0 )
			return $"{build.Catalog[build.Selected].Label}: {UiHelp.Text( UiHelp.BuildItem, "Left-click to build the item" )}";
		var name = Entity.All.OfType<Ride>().FirstOrDefault( r => r.Covers( tile.X, tile.Y ) )?.Name
			?? Shop.Stalls.FirstOrDefault( s => s.Covers( tile.X, tile.Y ) )?.Name;
		return name != null ? $"{name}: {UiHelp.Text( UiHelp.ItemDetails, "Left-click to view/edit this item's details" )}" : null;
	}

	// The translucent strip at the bottom centre; a long line wraps onto more rows and the strip grows with it.
	private static void DrawHint()
	{
		if ( HintText() is not { Length: > 0 } text )
			return;
		const float Width = 560f, Pad = 6f;
		float Scale = MathF.Max( 1f, MathF.Round( 13f / MathF.Max( 1f, HintFont.Atlas.LineHeight ) ) ); // whole: point-sampled
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
