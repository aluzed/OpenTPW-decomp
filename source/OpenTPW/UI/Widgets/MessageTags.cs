namespace OpenTPW;

/// <summary>
/// The original's message tags (UIHELPTEXT 475: "Message - left-click to open, right-click to delete"): each piece
/// of advice and each event leaves a tag down the right edge — the advisor's speech bubble (<c>tag_a</c>), a challenge
/// trophy (<c>tag_c</c>), a golden ticket (<c>tag_t</c>), an alert (<c>nutag_a</c>), news (<c>nutag_i</c>) — newest on
/// top. Clicking one opens the message in the original's message box; right-clicking it throws it away.
/// </summary>
internal sealed class MessageTags : GameWindow
{
	public static MessageTags? Current { get; private set; }

	public enum Kind { Advice, Challenge, Ticket, Alert, News }

	public readonly record struct Message( Kind Kind, string Title, string Text );

	private static readonly List<Message> messages = new();
	private const int MaxTags = 8;

	/// <summary>Leave a message tag (the same advice twice in a row isn't stacked again).</summary>
	public static void Post( Kind kind, string title, string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) || (messages.Count > 0 && messages[0].Text == text) )
			return;
		messages.Insert( 0, new Message( kind, title, text ) );
		if ( messages.Count > MaxTags )
			messages.RemoveAt( messages.Count - 1 );
	}

	public MessageTags()
	{
		Current = this;
		messages.Clear();
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private Message? open;

	protected override Rectangle Frame => open != null ? Box : Column;
	private static readonly Rectangle Box = At( 270f, 170f, 420f, 247f );
	private static Rectangle Column => new( 1280f - 58f, 720f - 150f - MaxTags * 52f, 50f, MaxTags * 52f );
	private static Rectangle TagRect( int i ) => new( 1280f - 56f, 720f - 150f - (i + 1) * 52f, 44f, 50f );

	private static string Texture( Kind kind ) => kind switch
	{
		Kind.Challenge => "tag_c", Kind.Ticket => "tag_t", Kind.Alert => "nutag_a", Kind.News => "nutag_i", _ => "tag_a",
	};

	public override void Close() => open = null;

	private int summarisedYear;

	// At each new year, a summary of the one just ended (UIHELPTEXT 534, "a year-end summary"): visitors, takings,
	// profit and the park's rating, from the monthly samples.
	private void YearEnd()
	{
		if ( GameClock.Current is not { } clock || ParkFinances.Current is not { } fin )
			return;
		if ( summarisedYear == 0 )
			summarisedYear = clock.Year;
		if ( clock.Year <= summarisedYear )
			return;
		int ended = summarisedYear;
		summarisedYear = clock.Year;
		var year = fin.History.TakeLast( 12 ).ToList();
		if ( year.Count == 0 )
			return;
		int visitorsBefore = fin.History.Count > year.Count ? fin.History[^(year.Count + 1)].Visitors : 0;
		float income = year.Sum( s => s.Income ), expense = year.Sum( s => s.Expense );
		Post( Kind.News, $"End of year {ended}",
			$"Visitors this year: {year[^1].Visitors - visitorsBefore}\nMoney in: $ {income:0}\nMoney out: $ {expense:0}\n" +
			$"Profit: $ {income - expense:0}\nAverage happiness: {Peep.AverageHappiness:0} %" );
		UiSounds.Play( UiSounds.Open );
		Log.Info( $"[park] year {ended} summary: profit {income - expense:0}" );
	}

	protected override void OnUpdate()
	{
		YearEnd();
		IsOpen = open != null;
		if ( RideItCameraMode.Target != null || CamcorderCameraMode.Active )
			return;
		var m = MouseBase();
		for ( int i = 0; i < messages.Count; i++ )
		{
			if ( !Contains( TagRect( i ), m ) )
				continue;
			Hint( 475 );
			if ( Input.MouseLeftPressed )
			{
				open = messages[i];
				UiSounds.Play( UiSounds.Button );
			}
			else if ( Input.MouseRightPressed )
			{
				if ( Equals( open, messages[i] ) )
					open = null;
				messages.RemoveAt( i );
			}
			return;
		}
	}

	/// <summary>True when the cursor is over a tag or the open message (world tools ignore the click).</summary>
	public new bool ContainsMouse()
	{
		var m = MouseBase();
		for ( int i = 0; i < messages.Count; i++ )
			if ( Contains( TagRect( i ), m ) )
				return true;
		return open != null && Contains( Box, m );
	}

	protected override void OnRender()
	{
		if ( RideItCameraMode.Target != null || CamcorderCameraMode.Active || ParkFinances.Current == null )
			return;
		for ( int i = 0; i < messages.Count; i++ )
			if ( UiTexture( Texture( messages[i].Kind ) ) is { } t )
				Fill( TagRect( i ), t );

		if ( open is { } msg )
		{
			var ok = DrawDialog( Box, msg.Title, msg.Text.Split( '\n' ) );
			if ( RoundButton( ok, "b_okay", hint: 1 ) )
				open = null;
		}
	}
}
