namespace OpenTPW;

/// <summary>
/// A visitor's window, in the original's style: clicking a visitor in the park shows their name, what they're
/// thinking (the game's thought icon and words), their happiness and needs as gauges, and the money they have
/// left. The ✓ closes it; it closes by itself when they leave the park.
/// </summary>
internal sealed class VisitorWindow : GameWindow
{
	public static VisitorWindow? Current { get; private set; }

	public VisitorWindow() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	protected override Rectangle Frame => At( 230f, 140f, 500f, 278f );

	private static readonly Rectangle Thought = At( 262f, 190f, 140f, 160f );
	private static readonly Rectangle Figures = At( 420f, 190f, 210f, 200f );
	private static readonly Rectangle Ok = At( 664f, 352f, 40f, 40f );

	// Dev aid: OPENTPW_UI_OPEN=visitor selects a visitor once one is in the park.
	private bool devOpened = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) != "visitor";

	protected override void OnUpdate()
	{
		var build = BuildMode.Current;
		if ( !devOpened && build != null && Entity.All.OfType<Peep>().FirstOrDefault() is { } first )
		{
			devOpened = true;
			build.SelectPeep( first );
		}
		// Gone (left the park): close.
		if ( build?.SelectedPeep is { } peep && !Entity.All.Contains( peep ) )
			build.ClearSelection();
		IsOpen = build?.SelectedPeep != null;
	}

	protected override void OnRender()
	{
		if ( !IsOpen || BuildMode.Current is not { SelectedPeep: { } peep } build )
			return;

		DrawFrame( "w_small" );
		DrawHeading( peep.PersonalName, 442f, 0f, 172f );

		DrawInset( Thought );
		int thought = peep.Thought;
		if ( UiTexture( ThoughtIcons.For( thought ) ) is { } icon )
			Fill( At( 292f, 200f, 80f, 64f ), icon );
		var lines = ParkHud.Wrap( Small, CrowdThoughts.Text( thought ), 120f );
		for ( int i = 0; i < lines.Count && i < 4; i++ )
			Text( Small, lines[i], 332f, 280f + i * 15f, 11f, default, TextAlign.Center );

		DrawInset( Figures );
		float y = 198f;
		void Row( string label, float gauge )
		{
			Text( Small, label, 428f, y, 11f );
			DrawGauge( At( 520f, y + 1f, 100f, 10f ), gauge );
			y += 26f;
		}
		Row( "Happiness", peep.Happiness / 100f );
		// Needs: full green when satisfied, red when pressing.
		Row( "Not hungry", 1f - peep.Hunger );
		Row( "Not thirsty", 1f - peep.ThirstLevel );
		Row( "Toilet", 1f - peep.Bladder );
		Row( "Energy", peep.EnergyLevel );
		Text( Small, "Money", 428f, y, 11f );
		Text( Small, $"$ {peep.Cash:0}", 520f, y, 11f );

		if ( RoundButton( Ok, "b_okay" ) )
			build.ClearSelection();
	}
}
