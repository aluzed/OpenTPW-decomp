namespace OpenTPW;

/// <summary>
/// Tracks the level's golden-ticket goals against the live park (T-055): re-evaluates on a schedule, exposes
/// each goal's progress for the HUD, and — as the original does — awards a golden ticket for **each** goal the first
/// time it's met (TAG_SYSTEM 180–189: "…a golden ticket for getting 1000 visitors", "…for having a high level of
/// visitor happiness"…), plus the global / secret ones the park can earn (<c>GoldenTicketGlobal.*</c>: a really long
/// water ride, a park covered by security cameras). <see cref="Awarded"/> — every level goal met — is the level win.
/// One <see cref="Current"/> per level. The pure comparison lives in <see cref="GoldenTicket"/>.
/// </summary>
public sealed class GoldenTicketGoals
{
	/// <summary>The active level's goals (HUD reads this); null before a level loads.</summary>
	public static GoldenTicketGoals? Current { get; set; }

	// Happiness above this counts a visitor as "happy" for the AtLeastThisManyHappyPeople goal: the game's own
	// happy band — its park-happiness gauge (FUN_004a52a0) shows the share of visitors over 66 (FUN_004c9670( 0x42 ))
	// against the share under 33 (FUN_004c9750( 0x21 )).
	public const float HappyThreshold = 66f;

	private readonly GoldenTicketTargets targets;
	private readonly GoldenTicketGlobals globals;
	private readonly HashSet<string> won = new();

	/// <summary>The golden tickets won so far (goal names) — saved with the park.</summary>
	public IReadOnlyCollection<string> Won => won;

	/// <summary>Raised for each ticket won, with the game's announcement (headline, sentence).</summary>
	public event Action<string, string>? TicketWon;

	/// <summary>True once the golden ticket has been awarded (all goals met).</summary>
	public bool Awarded { get; private set; }
	/// <summary>Per-goal progress from the most recent evaluation.</summary>
	public IReadOnlyList<GoldenTicket.GoalProgress> Progress { get; private set; } = new List<GoldenTicket.GoalProgress>();

	public GoldenTicketGoals( GoldenTicketTargets targets, GoldenTicketGlobals globals = default )
	{
		this.targets = targets;
		this.globals = globals;
	}

	/// <summary>How many goals are currently met / total set.</summary>
	public int Met => Progress.Count( g => g.Met );
	public int Total => Progress.Count;

	/// <summary>Restore the awarded flag from a save (T-059): a level already won stays won across save/load,
	/// so the win banner doesn't reappear/clear on reload. <see cref="Evaluate"/> only ever sets it true, so
	/// restoring it can't un-win a park mid-session.</summary>
	public void RestoreAwarded( bool awarded, IEnumerable<string>? tickets = null )
	{
		Awarded = awarded;
		if ( tickets != null )
			foreach ( var t in tickets )
				won.Add( t );
	}

	/// <summary>Re-evaluate against the live park (call periodically, e.g. on <see cref="GameClock.OnNewDay"/>);
	/// awards the ticket the first time every set target is met.</summary>
	public void Evaluate()
	{
		var state = Snapshot();
		Progress = GoldenTicket.Evaluate( targets, state );

		// A ticket per goal, the first time it's met; then the global / secret ones.
		foreach ( var goal in Progress.Where( g => g.Met ) )
			Award( goal.Name, GoldenTicket.Announcement( goal.Name, goal.Target, targets.RecentVisitorMonths ) );
		if ( globals.WaterLength > 0 && LongestWaterRide() >= globals.WaterLength )
			Award( "Water ride", GoldenTicket.Announcement( "Water ride", globals.WaterLength, 0 ) );
		if ( globals.MinCellsCovered > 0 && CellsCoveredByCameras() >= globals.MinCellsCovered )
			Award( "Cameras", GoldenTicket.Announcement( "Cameras", globals.MinCellsCovered, 0 ) );

		if ( !Awarded && GoldenTicket.AllMet( targets, state ) )
		{
			Awarded = true;
			Log.Info( "[ticket] GOLDEN TICKET awarded — every level goal met!" );
			// Celebrate: every ride launches a firework (P_EFFECT_FW1explosion), the award effect T-055 lacked.
			// (A snapshot: each firework adds particle entities to Entity.All — iterating it live threw
			// "Collection was modified" and killed the game the moment the ticket was won.)
			foreach ( var ride in Entity.All.OfType<Ride>().ToList() )
				ride.Celebrate();
		}
	}

	private void Award( string ticket, (string Headline, string Sentence) text )
	{
		if ( !won.Add( ticket ) )
			return;
		Log.Info( $"[ticket] golden ticket: {text.Sentence}" );
		foreach ( var ride in Entity.All.OfType<Ride>().ToList() ) // a snapshot: the fireworks add particles
			ride.Celebrate();
		TicketWon?.Invoke( text.Headline, text.Sentence );
	}

	// The longest water ride, in map cells of track (GoldenTicketGlobal.WaterLength).
	private static int LongestWaterRide()
		=> Entity.All.OfType<Ride>().Select( r => r.TileTrack is { } t && TileTrack.PiecePrefix( r ) == "wr_" ? t.SegmentCount * TileTrack.PieceTiles : 0 )
			.DefaultIfEmpty( 0 ).Max();

	// How many map cells the security cameras watch (RegionFX "Security Camera": 5 cells round each).
	private static int CellsCoveredByCameras()
	{
		var covered = new HashSet<(int, int)>();
		const int radius = 5;
		foreach ( var cam in Ride.Features.Where( f => f.ProvidesSecurity ) )
			for ( int dy = -radius; dy <= radius; dy++ )
				for ( int dx = -radius; dx <= radius; dx++ )
					if ( dx * dx + dy * dy <= radius * radius )
						covered.Add( (cam.TileX + dx, cam.TileY + dy) );
		return covered.Count;
	}

	private ParkState Snapshot()
	{
		var fin = ParkFinances.Current;
		return new ParkState(
			VisitorsTotal: fin?.VisitorsTotal ?? 0,
			PeopleInPark: Entity.All.Count( e => e is Peep ),
			AverageHappiness: MathF.Max( 0f, Peep.AverageHappiness ),
			HappyPeople: Peep.CountHappierThan( HappyThreshold ),
			ProfitYear: fin?.RecentProfit( GameClock.MonthsPerYear ) ?? 0f,
			RecentVisitors: fin?.RecentVisitors( targets.RecentVisitorMonths ) ?? 0 );
	}
}
