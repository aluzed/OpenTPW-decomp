using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original's financial information window (the handheld's money button, or F11), in the game's own words
/// (<c>UITEXT.str</c> "Financial Information", "Money in", "Gate takings"…): the <c>w_park</c> frame, the series
/// buttons (bank balance, money in, money out, staff costs) over the 1 / 3 / 12 year graph of the monthly samples,
/// the takings and costs so far, the price of entry with −/+ and the loans button down the right edge.
/// </summary>
internal sealed class FinancePanel : GameWindow
{
	public static FinancePanel? Current { get; private set; }

	public FinancePanel()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "finance"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool wasToggleDown;

	/// <summary>Show / hide the finances (the handheld's money button, or F11).</summary>
	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle Graph = At( 348f, 114f, 420f, 258f );
	private static readonly Rectangle Takings = At( 132f, 236f, 206f, 136f );
	private static readonly Rectangle EntryBox = At( 132f, 388f, 286f, 52f );
	private static readonly Rectangle BalanceBox = At( 440f, 388f, 328f, 52f );
	private static readonly Rectangle LoansButton = At( 784f, 280f, 50f, 50f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );

	private readonly record struct Series( UIStrings Label, string Fallback, byte R, byte G, byte B, Func<ParkFinances.FinanceSample, float> Value );

	private static readonly Series[] AllSeries =
	{
		new( UIStrings.BankBalance, "Bank balance", 250, 235, 40, s => s.Balance ),
		new( UIStrings.MoneyIn, "Money in", 60, 200, 70, s => s.Income ),
		new( UIStrings.MoneyOut, "Money out", 200, 40, 40, s => s.Expense ),
		new( UIStrings.GraphStaffCosts, "Staff costs", 60, 170, 230, s => s.Wages ),
	};

	private readonly bool[] shown = { true, true, true, false };
	private int range = 1;
	private static readonly int[] RangeMonths = { 12, 36, 144 };
	private static readonly string[] RangeLabels = { "1 Year", "3 Years", "12 Years" };

	private static Rectangle SeriesRect( int i ) => At( 132f, 82f + i * 36f, 206f, 28f );
	private static Rectangle RangeRect( int i ) => At( 380f + i * 128f, 82f, 118f, 24f );

	protected override void OnUpdate()
	{
		// F11 toggles the window (edge-detected on the raw key, like the other overlays).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.F11 );
		if ( toggle && !wasToggleDown )
			IsOpen = !IsOpen;
		wasToggleDown = toggle;

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
		DrawHeading( Localization.Get( UIStrings.FinancialInformation, "Financial Information" ), 470f, 0f, 46f );

		for ( int i = 0; i < AllSeries.Length; i++ )
		{
			var s = AllSeries[i];
			var r = SeriesRect( i );
			Fill( new Rectangle( r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f ), SolidOf( 20, 30, 90 ) );
			Fill( r, shown[i] ? SolidOf( s.R, s.G, s.B ) : SolidOf( (byte)(s.R / 3), (byte)(s.G / 3), (byte)(s.B / 3) ) );
			Text( Small, Localization.Get( s.Label, s.Fallback ), 235f, 89f + i * 36f, 11f, s.R + s.G > 300 ? new( 0.1f, 0.1f, 0.1f ) : default, TextAlign.Center );
		}
		for ( int i = 0; i < RangeMonths.Length; i++ )
		{
			var r = RangeRect( i );
			Fill( new Rectangle( r.X - 2f, r.Y - 2f, r.Width + 4f, r.Height + 4f ), SolidOf( 20, 30, 90 ) );
			Fill( r, i == range ? SolidOf( 150, 210, 60 ) : SolidOf( 20, 100, 30 ) );
			Text( Small, RangeLabels[i], 439f + i * 128f, 88f, 11f, default, TextAlign.Center );
		}

		var history = fin.History;
		int months = RangeMonths[range];
		int from = Math.Max( 0, history.Count - months );
		var lines = new List<(float[] Values, Texture Colour)>();
		for ( int s = 0; s < AllSeries.Length; s++ )
			if ( shown[s] )
				lines.Add( (history.Skip( from ).Select( AllSeries[s].Value ).ToArray(), SolidOf( AllSeries[s].R, AllSeries[s].G, AllSeries[s].B )) );
		DrawLineGraph( Graph, months, lines );

		// The takings and costs so far.
		DrawInset( Takings );
		float y = 242f;
		void Row( string label, float value )
		{
			Text( Small, label, 140f, y, 11f );
			Text( Small, $"$ {value:0}", 330f, y, 11f, default, TextAlign.Right );
			y += 21f;
		}
		Row( Localization.Get( UIStrings.GateTakings, "Gate takings" ), fin.EntryRevenue );
		Row( Localization.Get( UIStrings.ShopTakings, "Shop takings" ), fin.FoodRevenue );
		Row( Localization.Get( UIStrings.SideshowTakings, "Sideshow takings" ), fin.SideshowRevenue );
		Row( Localization.Get( UIStrings.GraphStaffCosts, "Staff costs" ), fin.WagesPaid );
		Row( "Upkeep", fin.UpkeepPaid );
		Row( Localization.Get( UIStrings.GraphOutstandingLoans, "Outstanding loans" ), fin.Debt );

		// The price of entry, with the original's −/+ buttons.
		DrawInset( EntryBox );
		Text( Small, Localization.Get( UIStrings.TicketPrice, "Ticket price" ), 142f, 398f, 11f );
		Text( Small, $"$ {fin.EntryFee:0}", 300f, 398f, 11f, default, TextAlign.Right );
		if ( RoundButton( At( 330f, 390f, 30f, 30f ), "b_minus", enabled: fin.EntryFee > 0 ) )
			fin.EntryFee = Math.Max( 0f, fin.EntryFee - 1f );
		if ( RoundButton( At( 368f, 390f, 30f, 30f ), "b_plus" ) )
			fin.EntryFee += 1f;

		DrawInset( BalanceBox );
		Text( Small, Localization.Get( UIStrings.BankBalance, "Bank balance" ), 450f, 396f, 11f );
		Text( Small, $"$ {fin.Money:0}", 760f, 396f, 11f, fin.Money < 0 ? LockedInk : default, TextAlign.Right );
		if ( history.Count > 0 )
		{
			var last = history[^1];
			Text( Small, $"{Localization.Get( UIStrings.MoneyIn, "Money in" )} $ {last.Income:0}   {Localization.Get( UIStrings.MoneyOut, "Money out" )} $ {last.Expense:0}", 450f, 418f, 11f );
		}

		if ( RoundButton( LoansButton, "b_loan" ) )
			LoanPanel.Current?.Toggle();
		if ( RoundButton( Ok, "b_okay" ) )
			IsOpen = false;
	}
}
