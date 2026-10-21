namespace OpenTPW;

/// <summary>
/// The end of a bankrupt park (T-042): once <see cref="ParkFinances.Bankrupt"/> (six months in the red) the park's
/// gates close and this panel shows the game's own verdict — <c>TAG_SYSTEM.str</c> 127, "You have been in the red
/// for six months, so the banks have closed your park and declared you bankrupt. / Click the tick button to restart
/// this park." — with the tick button (<c>UITEXT</c> "Restart Park"), which rebuilds the park from scratch.
/// </summary>
internal sealed class BankruptPanel : HudPanel
{
	public static BankruptPanel? Current { get; private set; }

	public BankruptPanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private const int BankruptMessage = 127;
	private const float MaxLineWidth = 560f;

	private static Texture? bg, tick;
	private static Texture Bg => bg ??= Solid( 40, 14, 14, 235 );
	private static Texture Tick => tick ??= Solid( 60, 150, 70, 245 );

	private static Rectangle PanelBounds() => new( 340f, 250f, 600f, 220f );
	private static Rectangle TickBounds() => new( 540f, 266f, 200f, 34f );

	private static bool Showing => ParkFinances.Current is { Bankrupt: true };

	/// <summary>True when the panel is up and the cursor is over it (so world tools ignore the click).</summary>
	public bool ContainsMouse() => Showing && Contains( PanelBounds(), MouseBase() );

	private static string[]? lines;
	private static string[] Lines => lines ??= Wrap( AdvisorTips.GameMessageLines( BankruptMessage )
		?? new[] { "You have been in the red for six months, so the banks have closed your park and declared you bankrupt.",
			"Click the tick button to restart this park." } );

	// Break the message into lines that fit the panel at the body scale.
	private static string[] Wrap( string[] paragraphs )
	{
		var result = new List<string>();
		foreach ( var paragraph in paragraphs )
		{
			var line = "";
			foreach ( var word in paragraph.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var next = line.Length == 0 ? word : $"{line} {word}";
				if ( line.Length > 0 && Font.Atlas.Measure( next ) * LabelScale > MaxLineWidth )
				{
					result.Add( line );
					line = word;
				}
				else
					line = next;
			}
			if ( line.Length > 0 )
				result.Add( line );
		}
		return result.ToArray();
	}

	protected override void OnUpdate()
	{
		if ( !Showing || !Input.MouseLeftPressed || !Contains( TickBounds(), MouseBase() ) )
			return;
		Log.Info( "[park] bankrupt: restarting the park" );
		Level.RequestReload( Level.Current.Name );
	}

	protected override void OnRender()
	{
		if ( !Showing )
			return;

		var bounds = PanelBounds();
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( bounds, mat );

		float y = bounds.Y + bounds.Height - 30f;
		foreach ( var line in Lines )
		{
			Graphics.DrawText( Font, line, bounds.X + 20f, y, TextAlign.Left, LabelScale );
			y -= 24f;
		}
		DrawButton( TickBounds(), Localization.Get( UIStrings.RestartPark, "Restart Park" ), Tick );
	}
}
