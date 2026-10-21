namespace OpenTPW;

/// <summary>
/// The challenge window (T-054), in the original's message box: an offered challenge with its ✓ / ✕ (the original's
/// <c>b_okay</c> / <c>b_ignore</c>; Y / N still work), the running one's progress gauge and days left in a compact
/// box at the top, and the result for a few seconds. Its headlines are the game's own: <c>TAG_SYSTEM.str</c> keeps
/// four lines per challenge of <c>Challenges.sam</c> — offer ("Here's a challenge! You could win $"), progress ("Challenge progress: Prize: $"),
/// completed ("You've won $"), failed — from entry 192 + 4 × (challenge − 1).
/// </summary>
internal sealed class ChallengePanel : GameWindow
{
	public static ChallengePanel? Current { get; private set; }

	public ChallengePanel() => Current = this;

	protected override bool Closable => false; // an offer is answered with its tick or cross

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private const int FirstMessage = 192;
	private const float ResultSeconds = 8f;

	// An offer or a result is the original's message box; a running challenge, a compact one at the top.
	private static readonly Rectangle Box = At( 270f, 170f, 420f, 247f );
	private static readonly Rectangle Tracker = At( 320f, 6f, 320f, 120f );
	private static Rectangle Accept => new( Box.X + Box.Width * 0.84f, Box.Y + Box.Height * 0.12f, 42f, 42f );
	private static Rectangle Decline => new( Box.X + Box.Width * 0.84f, Box.Y + Box.Height * 0.12f + 50f, 42f, 42f );

	protected override Rectangle Frame => ChallengeManager.Current?.State == ChallengeManager.Phase.Active ? Tracker : Box;

	private (Challenge Challenge, bool Won)? shownResult;
	private float resultUntil;

	private bool Visible => ChallengeManager.Current is { } cm
		&& (cm.State != ChallengeManager.Phase.Idle || Time.Now < resultUntil);

	// The game's headline for a challenge: the first line of its offer / progress / completed / failed entry.
	private static string Headline( Challenge c, int kind, string fallback )
		=> AdvisorTips.GameMessageLines( FirstMessage + 4 * (c.Index - 1) + kind ) is { Length: > 0 } lines ? lines[0] : fallback;

	// The entry's money line ("You could win $" / "Prize: $" / "You've won $") completed with the prize.
	private static string PrizeLine( Challenge c, int kind, string fallback )
		=> AdvisorTips.GameMessageLines( FirstMessage + 4 * (c.Index - 1) + kind ) is { Length: > 1 } lines
			? $"{lines[^1]}{c.Prize:0}" : $"{fallback}{c.Prize:0}";

	protected override void OnUpdate()
	{
		if ( ChallengeManager.Current is not { } cm )
			return;
		if ( cm.LastResult is { } r && !Equals( r, shownResult ) )
		{
			shownResult = r;
			resultUntil = Time.Now + ResultSeconds;
		}
		IsOpen = Visible;
	}

	protected override void OnRender()
	{
		if ( ChallengeManager.Current is not { } cm || !Visible )
			return;

		if ( cm.State == ChallengeManager.Phase.Offered && cm.Active is { } offer )
		{
			DrawDialog( Box, Headline( offer, 0, "Here's a challenge!" ), new[] { ChallengeMetrics.Describe( offer ), PrizeLine( offer, 0, "You could win $" ) } );
			if ( RoundButton( Accept, "b_okay" ) )
				cm.Accept();
			else if ( RoundButton( Decline, "b_ignore" ) )
				cm.Decline();
		}
		else if ( cm.State == ChallengeManager.Phase.Active && cm.Active is { } act )
		{
			float target = MathF.Max( 1f, ChallengeMetrics.Target( act ) );
			DrawDialog( Tracker, null, new[] { ChallengeMetrics.Describe( act ), $"{PrizeLine( act, 1, "Prize: $" )}   {cm.DaysLeft} days left" } );
			var bar = new Rectangle( Tracker.X + Tracker.Width * 0.1f, Tracker.Y + 18f, Tracker.Width * 0.64f, 10f );
			DrawGauge( bar, cm.Progress / target );
		}
		else if ( shownResult is { } r )
		{
			var ok = DrawDialog( Box, Headline( r.Challenge, r.Won ? 2 : 3, r.Won ? "Challenge completed!" : "Challenge failed..." ),
				r.Won ? new[] { ChallengeMetrics.Describe( r.Challenge ), PrizeLine( r.Challenge, 2, "You've won $" ) } : new[] { ChallengeMetrics.Describe( r.Challenge ) } );
			if ( RoundButton( ok, "b_okay" ) )
				resultUntil = Time.Now;
		}
	}
}
