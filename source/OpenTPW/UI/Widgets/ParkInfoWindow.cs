using NVector2 = System.Numerics.Vector2;

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

	private static readonly Dictionary<int, Texture> solids = new();

	private static Texture SolidOf( byte r, byte g, byte b )
	{
		int key = (r << 16) | (g << 8) | b;
		if ( !solids.TryGetValue( key, out var t ) )
			solids[key] = t = Solid( r, g, b, 255 );
		return t;
	}

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

		DrawInset( Graph );
		DrawGraph( fin.History );

		// The crowd's top three thoughts, as the original's thought icons.
		DrawInset( ThoughtsBox );
		var top = Peep.CrowdThoughtTally().OrderByDescending( kv => kv.Value ).Take( 3 ).ToList();
		for ( int i = 0; i < top.Count; i++ )
			if ( UiTexture( ThoughtIcon( top[i].Key ) ) is { } icon )
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

		if ( RoundButton( Ok, "b_okay" ) )
			IsOpen = false;
	}

	// The graph: a green grid, then each shown series as a line scaled to its own range.
	private void DrawGraph( IReadOnlyList<ParkFinances.FinanceSample> history )
	{
		var grid = SolidOf( 20, 90, 30 );
		for ( int k = 1; k < 10; k++ )
		{
			Fill( new Rectangle( Graph.X + Graph.Width * k / 10f, Graph.Y, 1f, Graph.Height ), grid );
			Fill( new Rectangle( Graph.X, Graph.Y + Graph.Height * k / 10f, Graph.Width, 1f ), grid );
		}
		int months = RangeMonths[range];
		int from = Math.Max( 0, history.Count - months );
		int count = history.Count - from;
		if ( count < 2 )
		{
			Text( Small, "The graph fills in month by month", 558f, 236f, 11f, default, TextAlign.Center );
			return;
		}
		for ( int s = 0; s < AllSeries.Length; s++ )
		{
			if ( !shown[s] )
				continue;
			var series = AllSeries[s];
			var values = Enumerable.Range( from, count ).Select( i => series.Value( history, i ) ).ToArray();
			float max = MathF.Max( 1f, values.Max() * 1.1f );
			var points = values.Select( ( v, i ) => new NVector2(
				Graph.X + 6f + (Graph.Width - 12f) * i / (months - 1f),
				Graph.Y + 6f + (Graph.Height - 12f) * v / max ) ).ToArray();
			var colour = SolidOf( series.R, series.G, series.B );
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

	// The thought's icon in ui.wad (ti_*), by its index in THOUGHTS.str.
	private static string ThoughtIcon( int thought ) => thought switch
	{
		1 => "ti_hungry", 2 => "ti_thirsty", 3 => "ti_hungthir", 4 => "ti_toilet", 5 => "ti_bad", 6 => "ti_good",
		7 => "ti_litter", 8 => "ti_suhappy", 9 => "ti_happy", 11 => "ti_sad", 12 => "ti_bored", 13 => "ti_angry",
		14 => "ti_sick", 15 => "ti_scared", 16 => "ti_queuebad", 17 => "ti_confused", _ => "ti_normal",
	};
}
