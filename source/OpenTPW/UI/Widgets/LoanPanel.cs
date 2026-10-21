namespace OpenTPW;

/// <summary>
/// The bank (T-042), as the original's loans window (the finance window's loan button, or L): the <c>w_park</c>
/// frame with the level's offers (<see cref="ParkFinances.LoadLoanOffers"/>) in a list under the game's own column
/// headings (<c>UITEXT.str</c> "Lender name", "Amount", "Interest rate", "Loan term", "Monthly repayment", "Total
/// payable"), each with a round button to take it out; a second tab lists the loans taken out (original amount,
/// months remaining, remaining balance) to repay them. Headings sort the list; hovering an action shows the balance
/// it would leave (the original's mini-balance).
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

	private static readonly Rectangle List = At( 140f, 128f, 620f, 284f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private const float HeaderTop = 134f, RowTop = 176f, RowHeight = 28f;
	private static readonly float[] Columns = { 150f, 300f, 384f, 460f, 540f, 640f };

	private static Rectangle ActionRect( int row ) => At( 714f, RowTop + row * RowHeight - 5f, 26f, 26f );
	private static Rectangle TabRect( int i ) => At( 150f + i * 56f, 74f, 48f, 48f );
	private static Rectangle ColumnRect( int c ) => At( Columns[c] - 4f, HeaderTop - 6f, (c + 1 < Columns.Length ? Columns[c + 1] : 700f) - Columns[c], 32f );

	// The original's two lists: the offers (UIHELPTEXT 220) and the loans taken out (221).
	private bool outstanding;
	private int sortColumn = -1;
	private bool descending;

	private readonly record struct Row( int Index, ParkFinances.Loan Loan, string[] Cells, float[] Keys );

	private List<Row> Rows( ParkFinances fin )
	{
		var rows = new List<Row>();
		for ( int i = 0; i < fin.Loans.Count; i++ )
		{
			var l = fin.Loans[i];
			if ( l.Bought != outstanding )
				continue;
			if ( outstanding )
			{
				int left = l.Monthly > 0f ? (int)MathF.Ceiling( l.Outstanding / l.Monthly ) : 0;
				rows.Add( new Row( i, l, new[] { l.Name, $"$ {l.Principal:0}", $"{l.Apr:0} %", $"{left}", $"$ {l.Monthly:0}", $"$ {l.Outstanding:0}" },
					new[] { 0f, l.Principal, l.Apr, left, l.Monthly, l.Outstanding } ) );
			}
			else
				rows.Add( new Row( i, l, new[] { l.Name, $"$ {l.Principal:0}", $"{l.Apr:0} %", $"{l.Months}", $"$ {l.TotalRepayable / l.Months:0}", $"$ {l.TotalRepayable:0}" },
					new[] { 0f, l.Principal, l.Apr, l.Months, l.TotalRepayable / l.Months, l.TotalRepayable } ) );
		}
		IEnumerable<Row> sorted = sortColumn switch
		{
			< 0 => rows,
			0 => rows.OrderBy( r => r.Loan.Name, StringComparer.CurrentCultureIgnoreCase ),
			_ => rows.OrderBy( r => r.Keys[sortColumn] ),
		};
		return (sortColumn >= 0 && descending ? sorted.Reverse() : sorted).ToList();
	}

	// The headings' sort hints (UIHELPTEXT 222–231): lender, amount, rate, term, monthly, total — or, for the loans
	// taken out, original amount, term remaining and remaining balance.
	private int SortHint( int c ) => c switch
	{
		0 => 222, 1 => outstanding ? 229 : 223, 2 => 225, 3 => outstanding ? 230 : 224, 4 => 226, _ => outstanding ? 231 : 227,
	};

	protected override void OnRender()
	{
		if ( !IsOpen || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_park" );
		DrawHeading( outstanding ? Localization.Get( UIStrings.GraphOutstandingLoans, "Outstanding Loans" )
			: Localization.Get( UIStrings.GraphAvailableLoans, "Available Loans" ), 470f, 0f, 46f );
		if ( RoundButton( TabRect( 0 ), "b_sloan", !outstanding, hint: 220 ) && outstanding )
			(outstanding, sortColumn) = (false, -1);
		if ( RoundButton( TabRect( 1 ), "b_spayup", outstanding, hint: 221 ) && !outstanding )
			(outstanding, sortColumn) = (true, -1);

		DrawInset( List );
		Fill( At( 146f, HeaderTop - 6f, 608f, 32f ), Header );
		var headings = new[]
		{
			Localization.Get( UIStrings.LenderName, "Lender name" ),
			outstanding ? Localization.Get( UIStrings.OriginalAmount, "Original amount" ) : Localization.Get( UIStrings.Amount, "Amount" ),
			Localization.Get( UIStrings.InterestRate, "Interest rate" ),
			outstanding ? Localization.Get( UIStrings.MonthsRemaining, "Months remaining" ) : Localization.Get( UIStrings.LoanTerm, "Loan term" ),
			Localization.Get( UIStrings.MonthlyRepayment, "Monthly repayment" ),
			outstanding ? Localization.Get( UIStrings.RemainingBalance, "Remaining balance" ) : Localization.Get( UIStrings.TotalPayable, "Total payable" ),
		};
		var m = MouseBase();
		for ( int c = 0; c < headings.Length; c++ )
		{
			var lines = ParkHud.Wrap( Small, headings[c] + (sortColumn == c ? (descending ? " v" : " ^") : ""), (c + 1 < Columns.Length ? Columns[c + 1] : 700f) - Columns[c] - 8f );
			for ( int k = 0; k < lines.Count && k < 2; k++ )
				Text( Small, lines[k], Columns[c], HeaderTop + k * 12f, 11f, HeaderInk );
			if ( Contains( ColumnRect( c ), m ) )
			{
				Hint( SortHint( c ) );
				if ( Input.MouseLeftPressed )
				{
					descending = sortColumn == c ? !descending : c > 0;
					sortColumn = c;
					UiSounds.Play( UiSounds.Button );
				}
			}
		}

		var rows = Rows( fin );
		float? after = null; // the mini-balance: the bank balance once the hovered loan is taken out / repaid
		for ( int r = 0; r < rows.Count; r++ )
		{
			var (i, l, cells, _) = rows[r];
			float top = RowTop + r * RowHeight;
			for ( int c = 0; c < cells.Length; c++ )
				Text( Small, cells[c], Columns[c], top, 11f );
			if ( Contains( ActionRect( r ), m ) )
				after = fin.Money + (l.Bought ? -l.Outstanding : l.Principal);
			// Take it out (the tick), or repay what's owed (the bank's pay-up button) when the balance allows.
			if ( l.Bought )
			{
				if ( RoundButton( ActionRect( r ), "b_spayup", enabled: fin.CanAfford( l.Outstanding ), hint: 232 ) )
					fin.RepayLoan( i );
			}
			else if ( RoundButton( ActionRect( r ), "b_okay", hint: 228 ) )
				fin.TakeLoan( i );
		}
		if ( rows.Count == 0 )
			Text( Small, outstanding ? "You have no loans" : "No more loans on offer", 450f, RowTop, 11f, default, TextAlign.Center );

		Text( Small, after is { } balance ? $"Bank balance $ {fin.Money:0}  >  $ {balance:0}" : $"Bank balance $ {fin.Money:0}",
			750f, 422f, 11f, after is { } b2 && b2 < 0f ? LockedInk : default, TextAlign.Right );

		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}
}
