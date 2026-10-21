namespace OpenTPW;

/// <summary>
/// The original's park information window (docs/10-ui-reference.md; the handheld's info button): the <c>w_park</c>
/// frame, the series buttons on the left (people in park, arrival rate, average happiness — the ones OpenTPW
/// records), the 1 / 3 / 12 year tabs over the graph, the crowd's top three thoughts as the original's thought
/// icons, the happiness gauge and the visitor counts. The graph plots <see cref="ParkFinances.History"/>'s
/// monthly samples. Measured on captures of the PC version.
/// </summary>
internal sealed class ParkInfoWindow : GameWindow
{
	public static ParkInfoWindow? Current { get; private set; }

	public ParkInfoWindow()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "info"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle Graph = At( 348f, 114f, 420f, 258f );
	private static readonly Rectangle ThoughtsBox = At( 132f, 300f, 206f, 62f );
	private static readonly Rectangle HappinessBox = At( 132f, 388f, 286f, 52f );
	private static readonly Rectangle CountsBox = At( 440f, 388f, 328f, 52f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private static readonly Rectangle Gates = At( 784f, 280f, 50f, 50f );

	private readonly record struct Series( string Label, byte R, byte G, byte B, Func<IReadOnlyList<ParkFinances.FinanceSample>, int, float> Value );

	private static readonly Series[] AllSeries =
	{
		new( "People in park", 250, 235, 40, ( h, i ) => h[i].PeopleInPark ),
		new( "Arrival rate", 150, 60, 200, ( h, i ) => i == 0 ? 0f : Math.Max( 0, h[i].Visitors - h[i - 1].Visitors ) ),
		new( "Average happiness", 160, 20, 20, ( h, i ) => h[i].Happiness ),
	};

	private readonly bool[] shown = { true, false, true };
	private int range = 1; // 0 = 1 year, 1 = 3 years, 2 = 12 years
	private static readonly int[] RangeMonths = { 12, 36, 144 };
	private static readonly string[] RangeLabels = { "1 Year", "3 Years", "12 Years" };

	private static Rectangle SeriesRect( int i ) => At( 132f, 82f + i * 36f, 206f, 28f );
	private static Rectangle RangeRect( int i ) => At( 380f + i * 128f, 82f, 118f, 24f );

	protected override void OnUpdate()
	{
		if ( !IsOpen || !Input.MouseLeftPressed )
			return;
		var m = MouseBase();
		for ( int i = 0; i < AllSeries.Length; i++ )
			if ( Contains( SeriesRect( i ), m ) )
				shown[i] = !shown[i];
		for ( int i = 0; i < RangeMonths.Length; i++ )
			if ( Contains( RangeRect( i ), m ) )
				range = i;
	}

	protected override void OnRender()
	{
		if ( !IsOpen || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_park" );
		DrawHeading( Localization.Get( UIStrings.ParkInformation, "Park Information" ), 470f, 0f, 46f );

		// Series buttons: the series' own colour, dimmed when hidden.
		for ( int i = 0; i < AllSeries.Length; i++ )
		{
			var s = AllSeries[i];
			var r = SeriesRect( i );
			Fill( new Rectangle( r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f ), SolidOf( 20, 30, 90 ) );
			Fill( r, shown[i] ? SolidOf( s.R, s.G, s.B ) : SolidOf( (byte)(s.R / 3), (byte)(s.G / 3), (byte)(s.B / 3) ) );
			Text( Small, s.Label, 235f, 89f + i * 36f, 11f, s.R + s.G > 300 ? new( 0.1f, 0.1f, 0.1f ) : default, TextAlign.Center );
		}
		for ( int i = 0; i < RangeMonths.Length; i++ )
		{
			var r = RangeRect( i );
			Fill( new Rectangle( r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f ), SolidOf( 20, 30, 90 ) );
			Fill( r, i == range ? SolidOf( 150, 210, 60 ) : SolidOf( 20, 100, 30 ) );
			Text( Small, RangeLabels[i], 439f + i * 128f, 88f, 11f, default, TextAlign.Center );
		}

		DrawGraph( fin.History );

		// The crowd's top three thoughts, as the original's thought icons.
		DrawInset( ThoughtsBox );
		var top = Peep.CrowdThoughtTally().OrderByDescending( kv => kv.Value ).Take( 3 ).ToList();
		for ( int i = 0; i < top.Count; i++ )
			if ( UiTexture( ThoughtIcons.For( top[i].Key ) ) is { } icon )
				Fill( At( 150f + i * 60f, 304f, 52f, 40f ), icon );
		Text( Small, Localization.Get( UIStrings.Top3Thoughts, "Top 3 Thoughts" ), 235f, 348f, 11f, default, TextAlign.Center );

		DrawInset( HappinessBox );
		Text( Small, Localization.Get( UIStrings.AvgHappiness, "Happiness Levels" ), 142f, 392f, 11f );
		DrawGauge( At( 142f, 412f, 266f, 16f ), Peep.AverageHappiness / 100f );

		DrawInset( CountsBox );
		int inPark = Entity.All.Count( e => e is Peep );
		Text( Small, Localization.Get( UIStrings.PeopleInPark, "People in park" ), 450f, 396f, 11f );
		Text( Small, $"{inPark}", 660f, 396f, 11f );
		Text( Small, "Total park visitors", 450f, 418f, 11f );
		Text( Small, $"{fin.VisitorsTotal}", 660f, 418f, 11f );

		// The park gates: open / close them (in the original, a click on the gates themselves).
		if ( RoundButton( Gates, "b_open", selected: !Level.GatesOpen ) )
			Level.ToggleGates();
		Text( Small, Level.GatesOpen ? "Park open" : "Park closed", 810f, 336f, 11f, Level.GatesOpen ? default : LockedInk, TextAlign.Center );
		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}

	private void DrawGraph( IReadOnlyList<ParkFinances.FinanceSample> history )
	{
		int months = RangeMonths[range];
		int from = Math.Max( 0, history.Count - months );
		var lines = new List<(float[] Values, Texture Colour)>();
		for ( int s = 0; s < AllSeries.Length; s++ )
			if ( shown[s] )
				lines.Add( (Enumerable.Range( from, history.Count - from ).Select( i => AllSeries[s].Value( history, i ) ).ToArray(),
					SolidOf( AllSeries[s].R, AllSeries[s].G, AllSeries[s].B )) );
		DrawLineGraph( Graph, months, lines );
	}
}
