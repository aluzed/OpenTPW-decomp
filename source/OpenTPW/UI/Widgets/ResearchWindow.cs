namespace OpenTPW;

/// <summary>
/// The original's research lab (the handheld's bulb button, docs/10-ui-reference.md): the <c>w_res</c> frame, the
/// scientists' power down the left with the researcher's face under it, and a row per field — rides, sideshows,
/// shops, scenery (<see cref="ItemResearch"/>) and ride upgrades (<see cref="ResearchQueue"/>): the effort slider
/// with −/+, the field's round icon, what is being researched with how far it has got, and the fluid tube that
/// fills as it nears completion. Measured on captures of the PC version.
/// </summary>
internal sealed class ResearchWindow : GameWindow
{
	public static ResearchWindow? Current { get; private set; }

	public ResearchWindow()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "research"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 99f, 33f, 840f, 447f );

	private static readonly Rectangle Power = At( 132f, 90f, 66f, 250f );
	private static readonly Rectangle Face = At( 134f, 364f, 74f, 74f );
	private static readonly Rectangle Ok = At( 884f, 404f, 42f, 42f );
	private const float FirstRow = 88f, RowStep = 76f;

	private static readonly (BuildCategory? Category, string Icon, string Label)[] Fields =
	{
		(BuildCategory.Rides, "b_srides", "Rides"), (BuildCategory.Sideshows, "b_sshows", "Sideshows"),
		(BuildCategory.Shops, "b_sshops", "Shops"), (BuildCategory.Features, "b_sfeature", "Scenery"),
		(null, "b_upgrade", "Ride upgrades"),
	};

	private static Texture? pill, nameBox, tube, fluid;
	private static Texture Pill => pill ??= Solid( 18, 32, 98, 255 );
	private static Texture NameBox => nameBox ??= Solid( 0, 0, 0, 255 );
	private static Texture TubeGlass => tube ??= Solid( 40, 60, 70, 255 );
	private static Texture Fluid => fluid ??= Solid( 150, 230, 140, 255 );
	private static readonly System.Numerics.Vector3 Progress = new( 0.6f, 0.95f, 0.55f );

	protected override void OnRender()
	{
		if ( !IsOpen )
			return;
		var research = ItemResearch.Current;

		DrawFrame( "w_res" );
		DrawHeading( "Research Lab", 505f, 0f, 52f );

		// The scientists' power (grade-weighted researchers), down the left; their face under it.
		DrawInset( Power );
		float power = Staff.ResearchPower;
		Fill( Power with { Height = Power.Height * Math.Clamp( power / 10f, 0f, 1f ) }, SolidOf( 60, 200, 110 ) );
		Text( Small, $"{power:0.#}", 165f, 344f, 11f, default, TextAlign.Center );
		if ( UiTexture( "h_resrcher" ) is { } face )
			Fill( Face, face );

		for ( int i = 0; i < Fields.Length; i++ )
		{
			var (category, icon, label) = Fields[i];
			float top = FirstRow + i * RowStep;

			// Effort: a pill with the share filled, −/+ either side.
			var pillRect = At( 225f, top, 134f, 46f );
			Fill( pillRect, Pill );
			if ( category is { } cat && research != null )
			{
				float effort = research.EffortOf( cat );
				DrawGauge( At( 256f, top + 18f, 72f, 10f ), effort / 100f );
				if ( RoundButton( At( 229f, top + 12f, 22f, 22f ), "b_minus", enabled: effort > 0f ) )
					research.SetEffort( cat, effort - 5f );
				if ( RoundButton( At( 333f, top + 12f, 22f, 22f ), "b_plus", enabled: effort < 100f ) )
					research.SetEffort( cat, effort + 5f );
			}

			RoundButton( At( 372f, top - 4f, 56f, 56f ), icon );

			// What is being researched, and how far.
			var box = At( 440f, top, 270f, 46f );
			Fill( box, NameBox );
			var (name, fraction) = Target( category, research );
			if ( fraction > 0f )
				Fill( box with { Width = box.Width * fraction }, SolidOf( 30, 70, 30 ) );
			Text( Small, label, 450f, top + 6f, 11f, TitleInk );
			Text( Small, name ?? "Nothing to research", 450f, top + 24f, 11f, name == null ? new( 0.6f, 0.6f, 0.6f ) : default );
			if ( name != null )
				Text( Small, $"{fraction * 100f:0} %", 700f, top + 24f, 11f, Progress, TextAlign.Right );

			// The fluid tube fills as the work nears completion.
			var tubeRect = At( 722f, top + 10f, 130f, 26f );
			Fill( tubeRect, TubeGlass );
			Fill( tubeRect with { Width = tubeRect.Width * fraction }, Fluid );
		}

		if ( RoundButton( Ok, "b_okay" ) )
			IsOpen = false;
	}

	// A field's current work: the item a category researches, or the ride whose upgrade is being researched.
	private static (string? Name, float Fraction) Target( BuildCategory? category, ItemResearch? research )
	{
		if ( category is { } cat )
			return research?.Target( cat ) is { } item ? (item.Title, research.ProgressOf( item.Key )) : (null, 0f);
		return ResearchQueue.Active is { } ride ? ($"{ride.Name} (level {ride.UpgradeLevel + 2})", ride.ResearchFraction) : (null, 0f);
	}
}
