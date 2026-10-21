namespace OpenTPW;

/// <summary>
/// Tracks the level's golden-ticket goals against the live park (T-055): re-evaluates on a schedule, exposes
/// each goal's progress for the HUD, and awards the ticket once (when every set target is met — the level win
/// condition). One <see cref="Current"/> per level. The pure comparison lives in <see cref="GoldenTicket"/>.
/// </summary>
public sealed class GoldenTicketGoals
{
	/// <summary>The active level's goals (HUD reads this); null before a level loads.</summary>
	public static GoldenTicketGoals? Current { get; set; }

	// Happiness above this counts a visitor as "happy" for the AtLeastThisManyHappyPeople goal (approx — the
	// original's exact "happy" threshold isn't decoded yet).
	private const float HappyThreshold = 50f;

	private readonly GoldenTicketTargets targets;

	/// <summary>True once the golden ticket has been awarded (all goals met).</summary>
	public bool Awarded { get; private set; }
	/// <summary>Per-goal progress from the most recent evaluation.</summary>
	public IReadOnlyList<GoldenTicket.GoalProgress> Progress { get; private set; } = new List<GoldenTicket.GoalProgress>();

	public GoldenTicketGoals( GoldenTicketTargets targets ) => this.targets = targets;

	/// <summary>How many goals are currently met / total set.</summary>
	public int Met => Progress.Count( g => g.Met );
	public int Total => Progress.Count;

	/// <summary>Restore the awarded flag from a save (T-059): a level already won stays won across save/load,
	/// so the win banner doesn't reappear/clear on reload. <see cref="Evaluate"/> only ever sets it true, so
	/// restoring it can't un-win a park mid-session.</summary>
	public void RestoreAwarded( bool awarded ) => Awarded = awarded;

	/// <summary>Re-evaluate against the live park (call periodically, e.g. on <see cref="GameClock.OnNewDay"/>);
	/// awards the ticket the first time every set target is met.</summary>
	public void Evaluate()
	{
		var state = Snapshot();
		Progress = GoldenTicket.Evaluate( targets, state );
		if ( !Awarded && GoldenTicket.AllMet( targets, state ) )
		{
			Awarded = true;
			Log.Info( "[ticket] GOLDEN TICKET awarded — every level goal met!" );
			// Celebrate: every ride launches a firework (P_EFFECT_FW1explosion), the award effect T-055 lacked.
			foreach ( var ride in Entity.All.OfType<Ride>() )
				ride.Celebrate();
		}
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
