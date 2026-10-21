using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Settings overlay (T-051): a centred panel with three draggable volume sliders — music, SFX and speech
/// — that drive the live <see cref="Audio"/> buses and persist to <see cref="GameSettings"/>. Toggled with
/// F10. Hidden by default, so it costs nothing until opened, and works in both the lobby and a park.
/// Shares the mouse-mapping / fill helpers with the other <see cref="HudPanel"/>s.
/// </summary>
internal sealed class OptionsPanel : HudPanel
{
	public static OptionsPanel? Current { get; private set; }

	public OptionsPanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool open;
	private bool wasToggleDown;
	private bool wasDragging;

	// Panel + slider geometry in the fixed 1280×720 base space (Y-up, origin bottom-left).
	private const float TrackX = 560f, TrackW = 220f, TrackH = 18f;
	private static readonly float[] RowY = { 420f, 370f, 320f }; // track bottoms: music / sfx / speech

	private static Texture? bg, track, fill, knob;
	private static Texture Bg => bg ??= Solid( 18, 18, 28, 225 );
	private static Texture Track => track ??= Solid( 50, 50, 62, 235 );
	private static Texture Fill => fill ??= Solid( 90, 150, 215, 240 );
	private static Texture Knob => knob ??= Solid( 220, 225, 235, 250 );

	private static Rectangle PanelBounds() => new( 440f, 290f, 400f, 180f );

	// The three buses, exposed as get/set on the live Audio statics.
	private readonly record struct Row( string Label, Func<float> Get, Action<float> Set );

	private static readonly Row[] Rows =
	{
		new( Localization.Get( UIStrings.MusicVolume, "Music volume:" ), () => Audio.MusicVolume, v => Audio.MusicVolume = v ),
		new( Localization.Get( UIStrings.SoundEffectsVolume, "Sound effects volume:" ), () => Audio.SfxVolume, v => Audio.SfxVolume = v ),
		new( Localization.Get( UIStrings.SpeechVolume, "Speech volume:" ), () => Audio.SpeechVolume, v => Audio.SpeechVolume = v ),
	};

	/// <summary>True when the panel is open and the cursor is over it (so world tools ignore the click).</summary>
	public bool ContainsMouse() => open && Contains( PanelBounds(), MouseBase() );

	protected override void OnUpdate()
	{
		// F10 toggles the overlay (edge-detected on the raw key, like the renderer's debug keys).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.F10 );
		if ( toggle && !wasToggleDown )
			open = !open;
		wasToggleDown = toggle;

		if ( !open )
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
				// Generous vertical hit zone so a slightly-off drag still grabs the slider.
				var hit = new Rectangle( TrackX - 6f, RowY[i] - 8f, TrackW + 12f, TrackH + 16f );
				if ( !Contains( hit, m ) )
					continue;

				var fraction = Math.Clamp( (m.X - TrackX) / TrackW, 0f, 1f );
				Rows[i].Set( fraction );
				wasDragging = true;
				break;
			}
		}
		else if ( wasDragging )
		{
			// Persist once, on release, rather than writing the file every dragged frame.
			SyncToSettings();
			GameSettings.Current.Save();
			wasDragging = false;
		}
	}

	private static void SyncToSettings()
	{
		var s = GameSettings.Current;
		s.MusicVolume = Audio.MusicVolume;
		s.SfxVolume = Audio.SfxVolume;
		s.SpeechVolume = Audio.SpeechVolume;
	}

	protected override void OnRender()
	{
		if ( !open )
			return;

		var bounds = PanelBounds();
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( bounds, mat );

		Graphics.DrawText( Font, Localization.Get( UIStrings.Options, "Options" ), bounds.X + 16f, bounds.Y + bounds.Height - 24f, TextAlign.Left, 1.5f );

		for ( var i = 0; i < Rows.Length; i++ )
		{
			var value = Math.Clamp( Rows[i].Get(), 0f, 1f );
			var trackRect = new Rectangle( TrackX, RowY[i], TrackW, TrackH );
			DrawBar( trackRect, value, Fill, Track );

			// Knob at the fill edge.
			var knobX = TrackX + TrackW * value - 3f;
			Graphics.Quad( new Rectangle( knobX, RowY[i] - 3f, 6f, TrackH + 6f ), Mat( Knob ) );

			Graphics.DrawText( Font, Rows[i].Label, bounds.X + 16f, RowY[i] + 1f, TextAlign.Left, LabelScale );
			Graphics.DrawText( Font, $"{value * 100f:0}%", TrackX + TrackW + 12f, RowY[i] + 1f, TextAlign.Left, LabelScale );
		}

		Graphics.DrawText( Font, "drag to set - F10 to close", bounds.X + 16f, bounds.Y + 10f, TextAlign.Left, 1.0f );
	}

	private static Material Mat( Texture t )
	{
		var mat = Material.UI;
		mat.Set( "Color", t );
		return mat;
	}
}
