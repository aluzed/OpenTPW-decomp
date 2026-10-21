namespace OpenTPW;

/// <summary>
/// The golden-ticket announcement (T-055): each ticket won shows the game's own words — "YOU'VE WON A GOLDEN
/// TICKET!" and its sentence (TAG_SYSTEM 180–190) — in the original's message box for a few seconds (or until its
/// ✓), while the rides fire their fireworks.
/// </summary>
internal sealed class GoldenTicketPanel : GameWindow
{
	private const float ShowSeconds = 9f;

	public override void Close() => showUntil = Time.Now;

	protected override Rectangle Frame => At( 270f, 170f, 420f, 247f );

	private readonly Queue<(string Headline, string Sentence)> pending = new();

	public GoldenTicketPanel()
	{
		// Dev aid: OPENTPW_UI_OPEN=dialog shows a sample announcement, to look at the message box.
		if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "dialog" )
			pending.Enqueue( ("YOU'VE WON A GOLDEN TICKET!", "Congratulations! You've won a golden ticket for having a high level of visitor happiness.") );
	}
	private (string Headline, string Sentence)? showing;
	private float showUntil;
	private GoldenTicketGoals? subscribed;

	protected override void OnUpdate()
	{
		if ( GoldenTicketGoals.Current != subscribed )
		{
			subscribed = GoldenTicketGoals.Current;
			if ( subscribed != null )
				subscribed.TicketWon += ( headline, sentence ) =>
				{
					pending.Enqueue( (headline, sentence) );
					MessageTags.Post( MessageTags.Kind.Ticket, headline, sentence );
				};
		}
		if ( (showing == null || Time.Now >= showUntil) && pending.Count > 0 )
		{
			showing = pending.Dequeue();
			showUntil = Time.Now + ShowSeconds;
			UiSounds.Play( UiSounds.GoldTicket );
		}
		else if ( showing != null && Time.Now >= showUntil )
			showing = null;
	}

	protected override void OnRender()
	{
		IsOpen = showing != null;
		if ( showing is not { } s )
			return;
		// The original's message box, with the ticket itself beside the words; the ✓ dismisses it.
		var ok = DrawDialog( Frame, s.Headline, new[] { s.Sentence } );
		if ( UiTexture( "gticket" ) is { } ticket )
			Fill( new Rectangle( ok.X - 4f, ok.Y + 60f, 52f, 52f ), ticket );
		if ( RoundButton( ok, "b_okay" ) )
			showUntil = Time.Now;
	}
}
