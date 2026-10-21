namespace OpenTPW;

/// <summary>
/// The challenge window (T-054): an offered challenge with ACCEPT / DECLINE (the original's <c>b_okay</c> /
/// <c>b_ignore</c> buttons; Y / N still work), the running one's progress bar and days left, and the result for a few
/// seconds. Its headlines are the game's own: <c>TAG_SYSTEM.str</c> keeps four lines per challenge of
/// <c>Challenges.sam</c> — offer ("Here's a challenge! You could win $"), progress ("Challenge progress: Prize: $"),
/// completed ("You've won $"), failed — from entry 192 + 4 × (challenge − 1).
/// </summary>
internal sealed class ChallengePanel : HudPanel
{
	public static ChallengePanel? Current { get; private set; }

	public ChallengePanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private const int FirstMessage = 192;
	private const float ResultSeconds = 8f;

	private static Texture? bg, track, fill;
	private static Texture Bg => bg ??= Solid( 20, 24, 44, 225 );
	private static Texture Track => track ??= Solid( 50, 50, 62, 230 );
	private static Texture Fill => fill ??= Solid( 230, 190, 60, 240 );

	private static Rectangle Bounds => new( 450f, 545f, 380f, 125f );
	private static Rectangle AcceptRect => new( 466f, 553f, 160f, 30f );
	private static Rectangle DeclineRect => new( 654f, 553f, 160f, 30f );

	private (Challenge Challenge, bool Won)? shownResult;
	private float resultUntil;

	/// <summary>True when the cursor is over the window (so world tools ignore the click).</summary>
	public bool ContainsMouse() => Visible && Contains( Bounds, MouseBase() );

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
		if ( cm.State != ChallengeManager.Phase.Offered || !Input.MouseLeftPressed )
			return;
		var m = MouseBase();
		if ( Contains( AcceptRect, m ) )
			cm.Accept();
		else if ( Contains( DeclineRect, m ) )
			cm.Decline();
	}

	protected override void OnRender()
	{
		if ( ChallengeManager.Current is not { } cm || !Visible )
			return;
		var b = Bounds;
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( b, mat );
		float x = b.X + 14f, top = b.Y + b.Height - 22f;

		if ( cm.State == ChallengeManager.Phase.Offered && cm.Active is { } offer )
		{
			Graphics.DrawText( Font, Headline( offer, 0, "Here's a challenge!" ), x, top, TextAlign.Left, LabelScale );
			Graphics.DrawText( Font, ChallengeMetrics.Describe( offer ), x, top - 24f, TextAlign.Left, 1.0f );
			Graphics.DrawText( Font, PrizeLine( offer, 0, "You could win $" ), x, top - 44f, TextAlign.Left, 1.1f );
			DrawIconButton( AcceptRect, "b_okay", "ACCEPT (Y)", Fill );
			DrawIconButton( DeclineRect, "b_ignore", "DECLINE (N)", Track );
		}
		else if ( cm.State == ChallengeManager.Phase.Active && cm.Active is { } act )
		{
			float target = MathF.Max( 1f, ChallengeMetrics.Target( act ) );
			Graphics.DrawText( Font, Headline( act, 1, "Challenge progress:" ), x, top, TextAlign.Left, LabelScale );
			Graphics.DrawText( Font, ChallengeMetrics.Describe( act ), x, top - 24f, TextAlign.Left, 1.0f );
			Graphics.DrawText( Font, $"{PrizeLine( act, 1, "Prize: $" )}   {cm.DaysLeft} days left", x, top - 44f, TextAlign.Left, 1.1f );
			DrawBar( new Rectangle( x, b.Y + 12f, b.Width - 28f, 16f ), cm.Progress / target, Fill, Track );
			Graphics.DrawText( Font, $"{cm.Progress:0} / {target:0}", b.X + b.Width / 2f, b.Y + 26f, TextAlign.Center, 1.0f );
		}
		else if ( shownResult is { } r )
		{
			Graphics.DrawText( Font, Headline( r.Challenge, r.Won ? 2 : 3, r.Won ? "Challenge completed!" : "Challenge failed..." ),
				x, top, TextAlign.Left, LabelScale );
			Graphics.DrawText( Font, ChallengeMetrics.Describe( r.Challenge ), x, top - 24f, TextAlign.Left, 1.0f );
			if ( r.Won )
				Graphics.DrawText( Font, PrizeLine( r.Challenge, 2, "You've won $" ), x, top - 44f, TextAlign.Left, 1.1f );
		}
	}
}
