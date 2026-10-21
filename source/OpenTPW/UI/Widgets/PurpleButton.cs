using OpenTPW.UI;
using Veldrid;

namespace OpenTPW;

internal class PurpleButton : Panel
{
	// Shared across every button so we only build the atlas/texture once. MENUMED is the game's intended
	// button face — an antialiased (compressed-4bpp) font now decoded (T-025), blitted through FontAtlas
	// as coverage→alpha so the labels render smooth rather than as the 1bpp GAME12 stand-in.
	private static Font? labelFont;
	private static Font LabelFont => labelFont ??= new Font( "Language/English/MENUMED.bf4" );

	// How big the label is drawn relative to its native glyph size (MENUMED is ~29px native).
	private const float LabelScale = 1f;

	private Texture ButtonText1;
	private Texture ButtonText2;

	public string Text { get; set; }

	/// <summary>Invoked when the button is clicked (after the click sound). Null = sound only.</summary>
	public Action? OnClick { get; set; }

	// UI click sound (T-031), loaded once from the global UI sound archive.
	private static byte[]? clickSound;
	private static bool clickSoundTried;
	private static byte[]? ClickSound
	{
		get
		{
			if ( clickSoundTried )
				return clickSound;
			clickSoundTried = true;
			try
			{
				var path = Path.Join( GameDir.GamePath, "data", "global", "sound", "sfUiHD.sdt" );
				if ( File.Exists( path ) )
				{
					var sdt = new SdtArchive( path );
					clickSound = sdt.soundFiles
						.FirstOrDefault( x => x.Name.StartsWith( "textclick", StringComparison.OrdinalIgnoreCase ) )?.SoundData;
				}
			}
			catch ( Exception e ) { Log.Warning( $"UI click sound unavailable: {e.Message}" ); }
			return clickSound;
		}
	}

	public PurpleButton( string text )
	{
		ButtonText1 = new Texture( "ui/textures/button_text1.wct", TextureFlags.PointFilter );
		ButtonText2 = new Texture( "ui/textures/button_text2.wct", TextureFlags.PointFilter );

		Text = Localization.Parse( text );
		Log.Info( $"Parsed '{text}' as '{Text}'" );
	}

	protected override void OnUpdate()
	{
		// Play a click when the cursor is over the button and the left button was just pressed.
		if ( !Input.MouseLeftPressed || !IsCursorOver() )
			return;

		if ( ClickSound != null )
			Audio.PlaySfx( "ui_click", ClickSound );
		OnClick?.Invoke();
	}

	// True when the mouse is over the visible pill. The mouse is in window pixels (Y-down); convert
	// to the UI's Y-up space. (The window matches the UI size, so no extra scaling is needed.)
	private bool IsCursorOver()
	{
		var mx = Input.Mouse.Position.X;
		var my = Screen.Size.Y - Input.Mouse.Position.Y;
		var left = position.X - 550f;
		var right = Math.Min( position.X + 40f, Screen.Size.X );
		var bottom = position.Y - 130f;
		var top = position.Y - 2f;
		return mx >= left && mx <= right && my >= bottom && my <= top;
	}

	protected override void OnRender()
	{
		var material = Material.UI;
		var size = new Vector2( 200, 100 );
		var rect = new Rectangle( position, size );

		var uvs = new Rectangle( 0f, 0f, 1.0f, 0.5f );
		rect.Y -= 130;
		rect.X -= 740;

		//
		// End
		//
		material.Set( "Color", ButtonText1 );
		rect.X += 190;
		Graphics.Quad( rect, uvs.Shift( new Vector2( 0, 0.50f ) ), material );

		//
		// Middle
		//
		material.Set( "Color", ButtonText1 );
		rect.X += 190;
		Graphics.Quad( rect.Shift( new Vector2( 0, 28 ) ), uvs.Shift( new Vector2( 0, 0f ) ), material );

		//
		// Start
		//
		rect.X += 200;
		material.Set( "Color", ButtonText2 );
		Graphics.Quad( rect, uvs.Shift( new Vector2( 0f, 0.50f ) ), material );

		//
		// Label — centred on the button face. The three quads above span roughly
		// [position.X - 550, position.X + 40] horizontally; their centre is at
		// position.X - 255, and the face sits vertically around position.Y - 80.
		//
		if ( !string.IsNullOrEmpty( Text ) )
		{
			// The pill is the union of the three quads above: X spans [position.X - 550,
			// position.X + 40], Y (Y-up) spans [position.Y - 130, position.Y - 2]. Buttons are
			// right-anchored, so the right cap can run past the screen edge — centre the label on
			// the *visible* (screen-clamped) part, else it drifts into the off-screen overhang.
			var visibleLeft = Math.Max( position.X - 550, 0f );
			var visibleRight = Math.Min( position.X + 40, Screen.Size.X );
			var centerX = (visibleLeft + visibleRight) / 2f;
			var centerY = position.Y - 66;

			// Centre on the label's actual ink (caps don't fill the line box, so centring the box
			// leaves text riding high). DrawText's originY is the line top; place it so the ink
			// midpoint lands on centerY: the ink sits (Top+Bottom)/2 * scale below the line top.
			var (inkTop, inkBottom) = LabelFont.Atlas.InkBounds( Text );
			var top = centerY + (inkTop + inkBottom) / 2f * LabelScale;
			Graphics.DrawText( LabelFont, Text, centerX, top, TextAlign.Center, LabelScale );
		}
	}
}
