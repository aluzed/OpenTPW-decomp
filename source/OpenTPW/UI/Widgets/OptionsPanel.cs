using Veldrid;

namespace OpenTPW;

/// <summary>
/// Settings window (T-051), in the original's small window (<c>w_small</c>): three draggable volume sliders — music,
/// SFX and speech, labelled with the game's own words — that drive the live <see cref="Audio"/> buses and persist to
/// <see cref="GameSettings"/>. Toggled with F10, closed with its tick; works in both the lobby and a park.
/// </summary>
internal sealed class OptionsPanel : GameWindow
{
	public static OptionsPanel? Current { get; private set; }

	public OptionsPanel()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "options"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool wasToggleDown;
	private bool wasDragging;

	protected override Rectangle Frame => At( 230f, 160f, 500f, 278f );

	private static readonly Rectangle Box = At( 262f, 210f, 360f, 190f );
	private static readonly Rectangle Ok = At( 664f, 372f, 40f, 40f );
	private const float TrackX = 280f, TrackW = 230f, FirstRow = 244f, RowStep = 52f;

	private static Rectangle TrackRect( int i ) => At( TrackX, FirstRow + i * RowStep, TrackW, 12f );

	// The three buses, exposed as get/set on the live Audio statics.
	private readonly record struct Row( string Label, Func<float> Get, Action<float> Set );

	private static readonly Row[] Rows =
	{
		new( Localization.Get( UIStrings.MusicVolume, "Music volume:" ), () => Audio.MusicVolume, v => Audio.MusicVolume = v ),
		new( Localization.Get( UIStrings.SoundEffectsVolume, "Sound effects volume:" ), () => Audio.SfxVolume, v => Audio.SfxVolume = v ),
		new( Localization.Get( UIStrings.SpeechVolume, "Speech volume:" ), () => Audio.SpeechVolume, v => Audio.SpeechVolume = v ),
	};

	protected override void OnUpdate()
	{
		// F10 toggles the window (edge-detected on the raw key, like the renderer's debug keys).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.F10 );
		if ( toggle && !wasToggleDown )
			IsOpen = !IsOpen;
		wasToggleDown = toggle;

		if ( !IsOpen )
		{
			wasDragging = false;
			return;
		}

		// Drag (or click) a slider: set the row under the cursor to the cursor's position along the track.
		if ( Input.Mouse.Left )
		{
			var m = MouseBase();
			for ( var i = 0; i < Rows.Length; i++ )
			{
				var track = TrackRect( i );
				// Generous vertical hit zone so a slightly-off drag still grabs the slider.
				var hit = new Rectangle( track.X - 6f, track.Y - 10f, track.Width + 12f, track.Height + 20f );
				if ( !Contains( hit, m ) )
					continue;
				Rows[i].Set( Math.Clamp( (m.X - track.X) / track.Width, 0f, 1f ) );
				wasDragging = true;
				break;
			}
		}
		else if ( wasDragging )
		{
			// Persist once, on release, rather than writing the file every dragged frame.
			var s = GameSettings.Current;
			s.MusicVolume = Audio.MusicVolume;
			s.SfxVolume = Audio.SfxVolume;
			s.SpeechVolume = Audio.SpeechVolume;
			s.Save();
			wasDragging = false;
		}
	}

	protected override void OnRender()
	{
		if ( !IsOpen )
			return;

		DrawFrame( "w_small" );
		DrawHeading( Localization.Get( UIStrings.Options, "Options" ), 442f, 0f, 182f );
		DrawInset( Box );
		for ( var i = 0; i < Rows.Length; i++ )
		{
			var value = Math.Clamp( Rows[i].Get(), 0f, 1f );
			float top = FirstRow + i * RowStep;
			Text( Small, Rows[i].Label, TrackX, top - 18f, 11f );
			var track = TrackRect( i );
			DrawGauge( track, value );
			// The knob, at the fill edge.
			float knob = track.X + track.Width * value;
			Fill( new Rectangle( knob - 4f, track.Y - 5f, 8f, track.Height + 10f ), SolidOf( 20, 30, 90 ) );
			Fill( new Rectangle( knob - 2f, track.Y - 3f, 4f, track.Height + 6f ), SolidOf( 235, 235, 240 ) );
			Text( Small, $"{value * 100f:0} %", TrackX + TrackW + 30f, top, 11f );
		}
		if ( RoundButton( Ok, "b_okay" ) )
			IsOpen = false;
	}
}
