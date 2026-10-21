namespace OpenTPW;

/// <summary>
/// The bank (T-042): a centred overlay listing the level's loan offers (<see cref="ParkFinances.LoadLoanOffers"/>) —
/// lender, amount, APR, period and monthly payment — with a TAKE or REPAY button per offer. Opened with L or the
/// LOANS button; hidden by default. Same layout pattern as <see cref="SavePanel"/>.
/// </summary>
internal sealed class LoanPanel : HudPanel
{
	public static LoanPanel? Current { get; private set; }

	public LoanPanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool open;

	public void Toggle() => open = !open;

	private const float PanelX = 300f, PanelW = 680f, RowH = 40f;
	private const float BtnW = 110f, BtnH = 28f;

	private static int Rows => Math.Max( 1, ParkFinances.Current?.Loans.Count ?? 1 );
	private static float PanelH => 76f + Rows * RowH;
	private static Rectangle PanelBounds() => new( PanelX, 700f - PanelH, PanelW, PanelH );
	private static float RowY( int i ) => 700f - 62f - (i + 1) * RowH + 6f;

	private static Texture? bg, take, repay, dim, row;
	private static Texture Bg => bg ??= Solid( 18, 18, 28, 230 );
	private static Texture TakeBtn => take ??= Solid( 70, 130, 80, 240 );
	private static Texture RepayBtn => repay ??= Solid( 160, 110, 50, 240 );
	private static Texture Dim => dim ??= Solid( 55, 55, 62, 235 );
	private static Texture Row => row ??= Solid( 30, 30, 40, 180 );

	private readonly record struct Btn( Rectangle Rect, string Label, Texture Fill, bool Enabled, Action Act );

	/// <summary>True when the panel is open and the cursor is over it (so world tools ignore the click).</summary>
	public bool ContainsMouse() => open && Contains( PanelBounds(), MouseBase() );

	private static List<Btn> Buttons()
	{
		var list = new List<Btn>();
		if ( ParkFinances.Current is not { } fin )
			return list;
		var loans = fin.Loans;
		for ( int i = 0; i < loans.Count; i++ )
		{
			int idx = i;
			var l = loans[i];
			var rect = new Rectangle( PanelX + PanelW - BtnW - 16f, RowY( i ), BtnW, BtnH );
			list.Add( l.Bought
				? new Btn( rect, "REPAY", RepayBtn, fin.CanAfford( l.Outstanding ), () => fin.RepayLoan( idx ) )
				: new Btn( rect, "TAKE", TakeBtn, true, () => fin.TakeLoan( idx ) ) );
		}
		return list;
	}

	protected override void OnUpdate()
	{
		if ( !open || !Input.MouseLeftPressed )
			return;

		var m = MouseBase();
		foreach ( var b in Buttons() )
		{
			if ( !b.Enabled || !Contains( b.Rect, m ) )
				continue;
			b.Act();
			break;
		}
	}

	protected override void OnRender()
	{
		if ( !open || ParkFinances.Current is not { } fin )
			return;

		var bounds = PanelBounds();
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( bounds, mat );
		Graphics.DrawText( Font, "BANK LOANS", bounds.X + 16f, bounds.Y + bounds.Height - 24f, TextAlign.Left, 1.5f );

		var loans = fin.Loans;
		for ( int i = 0; i < loans.Count; i++ )
		{
			var l = loans[i];
			float y = RowY( i );
			mat.Set( "Color", Row );
			Graphics.Quad( new Rectangle( PanelX + 12f, y - 4f, PanelW - 24f, RowH - 6f ), mat );
			string terms = l.Bought
				? $"{l.Name}  - owe ${l.Outstanding:0}, ${l.Monthly:0}/month"
				: $"{l.Name}  - ${l.Principal:0}, {l.Apr:0}%, {l.Months} mo (${l.TotalRepayable / l.Months:0}/mo)";
			Graphics.DrawText( Font, terms, PanelX + 22f, y + BtnH - 8f, TextAlign.Left, 1.1f );
		}

		foreach ( var b in Buttons() )
			DrawButton( b.Rect, b.Label, b.Enabled ? b.Fill : Dim );

		Graphics.DrawText( Font, "L to close", bounds.X + 16f, bounds.Y + 8f, TextAlign.Left, 1.0f );
	}
}
