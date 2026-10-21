namespace OpenTPW;

/// <summary>
/// The golden-ticket announcement (T-055): each ticket won shows the game's own banner for a few seconds —
/// "YOU'VE WON A GOLDEN TICKET!" and its sentence (TAG_SYSTEM 180–190) — while the rides fire their fireworks.
/// </summary>
internal sealed class GoldenTicketPanel : HudPanel
{
	private const float ShowSeconds = 9f;

	private static Texture? bg, gold;
	private static Texture Bg => bg ??= Solid( 40, 26, 6, 230 );
	private static Texture Gold => gold ??= Solid( 240, 200, 60, 255 );

	private readonly Queue<(string Headline, string Sentence)> pending = new();
	private (string Headline, string Sentence)? showing;
	private float showUntil;
	private GoldenTicketGoals? subscribed;

	protected override void OnUpdate()
	{
		if ( GoldenTicketGoals.Current != subscribed )
		{
			subscribed = GoldenTicketGoals.Current;
			if ( subscribed != null )
				subscribed.TicketWon += ( headline, sentence ) => pending.Enqueue( (headline, sentence) );
		}
		if ( (showing == null || Time.Now >= showUntil) && pending.Count > 0 )
		{
			showing = pending.Dequeue();
			showUntil = Time.Now + ShowSeconds;
		}
		else if ( showing != null && Time.Now >= showUntil )
			showing = null;
	}

	protected override void OnRender()
	{
		if ( showing is not { } s )
			return;
		var b = new Rectangle( 340f, 330f, 600f, 90f );
		var mat = Material.UI;
		mat.Set( "Color", Gold );
		Graphics.Quad( new Rectangle( b.X - 3f, b.Y - 3f, b.Width + 6f, b.Height + 6f ), mat );
		mat.Set( "Color", Bg );
		Graphics.Quad( b, mat );
		Graphics.DrawText( Font, s.Headline, b.X + b.Width / 2f, b.Y + b.Height - 26f, TextAlign.Center, 1.6f );
		float scale = MathF.Min( 1.2f, (b.Width - 24f) / MathF.Max( 1f, Font.Atlas.Measure( s.Sentence ) ) );
		Graphics.DrawText( Font, s.Sentence, b.X + b.Width / 2f, b.Y + 26f, TextAlign.Center, scale );
	}
}
