namespace OpenTPW;

/// <summary>
/// The end of a bankrupt park (T-042): once <see cref="ParkFinances.Bankrupt"/> (six months in the red) the park's
/// gates close and this panel shows the game's own verdict — <c>TAG_SYSTEM.str</c> 127, "You have been in the red
/// for six months, so the banks have closed your park and declared you bankrupt. / Click the tick button to restart
/// this park." — with the tick button (<c>UITEXT</c> "Restart Park"), which rebuilds the park from scratch.
/// </summary>
internal sealed class BankruptPanel : GameWindow
{
	public static BankruptPanel? Current { get; private set; }

	public BankruptPanel() => Current = this;

	protected override bool Closable => false;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private const int BankruptMessage = 127;

	protected override Rectangle Frame => At( 250f, 170f, 460f, 270f );

	private static bool Showing => ParkFinances.Current is { Bankrupt: true };

	private static string[] Lines => AdvisorTips.GameMessageLines( BankruptMessage )
		?? new[] { "You have been in the red for six months, so the banks have closed your park and declared you bankrupt.",
			"Click the tick button to restart this park." };

	protected override void OnRender()
	{
		IsOpen = Showing;
		if ( !Showing )
			return;
		// The original's message box; its tick restarts the park.
		var ok = DrawDialog( Frame, Localization.Get( UIStrings.RestartPark, "Restart Park" ), Lines );
		if ( RoundButton( ok, "b_okay" ) )
		{
			Log.Info( "[park] bankrupt: restarting the park" );
			Level.RequestReload( Level.Current.Name );
		}
	}
}
