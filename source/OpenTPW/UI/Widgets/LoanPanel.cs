namespace OpenTPW;

/// <summary>
/// The bank (T-042), as the original's loans window (the finance window's loan button, or L): the <c>w_park</c>
/// frame with the level's offers (<see cref="ParkFinances.LoadLoanOffers"/>) in a list under the game's own column
/// headings (<c>UITEXT.str</c> "Lender name", "Amount", "Interest rate", "Loan term", "Monthly repayment"), each
/// with a round button to take it out or repay it.
/// </summary>
internal sealed class LoanPanel : GameWindow
{
	public static LoanPanel? Current { get; private set; }

	public LoanPanel()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "loans"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle List = At( 140f, 82f, 620f, 330f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private const float HeaderTop = 88f, RowTop = 132f, RowHeight = 34f;
	private static readonly float[] Columns = { 150f, 318f, 400f, 482f, 612f };

	private static Rectangle ActionRect( int row ) => At( 704f, RowTop + row * RowHeight - 4f, 30f, 30f );

	protected override void OnRender()
	{
		if ( !IsOpen || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_park" );
		DrawHeading( Localization.Get( UIStrings.GraphAvailableLoans, "Available Loans" ), 470f, 0f, 46f );

		DrawInset( List );
		Fill( At( 146f, HeaderTop, 608f, 32f ), Header );
		var headings = new[]
		{
			Localization.Get( UIStrings.LenderName, "Lender name" ), Localization.Get( UIStrings.Amount, "Amount" ),
			Localization.Get( UIStrings.InterestRate, "Interest rate" ), Localization.Get( UIStrings.LoanTerm, "Loan term" ),
			Localization.Get( UIStrings.MonthlyRepayment, "Monthly repayment" ),
		};
		for ( int c = 0; c < headings.Length; c++ )
			Text( Small, headings[c], Columns[c], HeaderTop + 10f, 11f, HeaderInk );

		var loans = fin.Loans;
		for ( int i = 0; i < loans.Count; i++ )
		{
			var l = loans[i];
			float top = RowTop + i * RowHeight;
			var ink = l.Bought ? TitleInk : default;
			Text( Small, l.Name, Columns[0], top, 11f, ink );
			Text( Small, l.Bought ? $"$ {l.Outstanding:0} owed" : $"$ {l.Principal:0}", Columns[1], top, 11f, ink );
			Text( Small, $"{l.Apr:0} %", Columns[2], top, 11f, ink );
			Text( Small, $"{l.Months} months", Columns[3], top, 11f, ink );
			Text( Small, $"$ {(l.Bought ? l.Monthly : l.TotalRepayable / l.Months):0}", Columns[4], top, 11f, ink );
			// Take it out (the tick), or repay what's owed (the bank's pay-up button) when the balance allows.
			if ( l.Bought )
			{
				if ( RoundButton( ActionRect( i ), "b_spayup", enabled: fin.CanAfford( l.Outstanding ) ) )
					fin.RepayLoan( i );
			}
			else if ( RoundButton( ActionRect( i ), "b_okay" ) )
				fin.TakeLoan( i );
		}

		if ( RoundButton( Ok, "b_okay" ) )
			IsOpen = false;
	}
}
