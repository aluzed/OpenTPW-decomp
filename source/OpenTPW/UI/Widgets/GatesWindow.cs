namespace OpenTPW;

/// <summary>
/// The park gates (UITEXT 159 "Park Gates", 160 "Ticket Price"; UIHELPTEXT 176 "open/close the park or change the
/// ticket price (G)"): the <c>w_small</c> frame with the ticket price and its −/+ (192), what visitors make of it
/// against the park's estimated price (UITEXT 445, <see cref="EntryFeeOpinion"/>), how many wait outside, and the
/// open / close buttons (193 Ctrl-O, 194 Ctrl-C).
/// </summary>
internal sealed class GatesWindow : GameWindow
{
	public static GatesWindow? Current { get; private set; }

	public GatesWindow()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "gates"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 230f, 160f, 500f, 278f );

	private static readonly Rectangle Figures = At( 262f, 208f, 368f, 150f );
	private static readonly Rectangle Ok = At( 664f, 372f, 40f, 40f );

	protected override void OnRender()
	{
		if ( !IsOpen || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_small" );
		DrawHeading( Localization.Get( UIStrings.ParkGates, "Park Gates" ), 442f, 0f, 190f );
		DrawInset( Figures );

		// The ticket price, with −/+.
		Text( Small, Localization.Get( UIStrings.TicketPrice, "Ticket Price" ), 272f, 222f, 13f, TitleInk );
		Text( Small, $"$ {fin.EntryFee:0}", 520f, 222f, 13f, default, TextAlign.Right );
		if ( RoundButton( At( 540f, 214f, 30f, 30f ), "b_minus", enabled: fin.EntryFee > 0f, hint: 192 ) )
			fin.EntryFee = Math.Max( 0f, fin.EntryFee - 1f );
		if ( RoundButton( At( 578f, 214f, 30f, 30f ), "b_plus", hint: 192 ) )
			fin.EntryFee += 1f;

		// What it's worth: the park's rides' excitement over the divisor, and the visitors' verdict on the price.
		var settings = EntryFeeOpinion.Settings;
		float excitement = Entity.All.OfType<Ride>().Where( r => !r.IsSideshow && !r.IsFeature ).Sum( r => (float)r.Excitement );
		float ideal = EntryFeeOpinion.IdealFee( settings, excitement );
		Text( Small, $"{Localization.Get( UIStrings.EstimatedPrice, "Estimated price : $" ).TrimEnd()} {ideal:0}", 272f, 254f, 11f );
		float opinion = EntryFeeOpinion.ArrivalHappiness( settings, fin.EntryFee, excitement );
		var (verdict, ink) = opinion >= settings.OpinionTooLow ? ("Visitors think it's a bargain", Good)
			: opinion >= settings.OpinionAboutRight ? ("Visitors think it's about right", default)
			: opinion >= settings.OpinionTooHigh ? ("Visitors think it's too expensive", Warn)
			: ("Visitors think it's far too expensive", LockedInk);
		Text( Small, verdict, 272f, 276f, 11f, ink );
		Text( Small, Level.WaitingAtGate > 0 ? $"{Level.WaitingAtGate} waiting at the gates" : "Nobody waiting at the gates", 272f, 298f, 11f );
		Text( Small, Level.GatesOpen ? "The park is open" : "The park is closed", 272f, 330f, 13f, Level.GatesOpen ? Good : LockedInk );

		// Open / close.
		if ( RoundButton( At( 664f, 210f, 40f, 40f ), "b_open", selected: Level.GatesOpen, enabled: !Level.GatesOpen, hint: 193 ) )
			Level.ToggleGates();
		if ( RoundButton( At( 664f, 260f, 40f, 40f ), "b_doorc", enabled: Level.GatesOpen, hint: 194 ) )
			Level.ToggleGates();
		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}

	private static readonly System.Numerics.Vector3 Good = new( 0.6f, 0.95f, 0.55f ), Warn = new( 1f, 0.75f, 0.3f );
}
