namespace OpenTPW;

/// <summary>A peep's reaction to a ride it just rode (T-050) — surfaced as its current "thought".</summary>
public enum RideThought
{
	GreatRide,    // exciting + fairly priced
	GoodValue,    // cheap for what you get
	TooExpensive, // overpriced vs its excitement
	Unreliable,   // the ride felt rickety / had broken down
	Mediocre,     // unremarkable
	Rubbish,      // dull
}

/// <summary>Readable player-facing text for a <see cref="RideThought"/> (T-050): a full first-person line and a
/// short HUD tag. Pure, so the wording is unit-tested + decoupled from how it's shown.</summary>
public static class RideThoughtText
{
	public static string For( RideThought t ) => t switch
	{
		RideThought.GreatRide => "What a great ride!",
		RideThought.GoodValue => "Good value for money",
		RideThought.TooExpensive => "That was too expensive!",
		RideThought.Unreliable => "That ride felt unsafe!",
		RideThought.Mediocre => "That was OK, I suppose",
		RideThought.Rubbish => "What a load of rubbish!",
		_ => "",
	};

	/// <summary>A short tag for the compact HUD mood readout.</summary>
	public static string Tag( RideThought t ) => t switch
	{
		RideThought.GreatRide => "Great",
		RideThought.GoodValue => "Value",
		RideThought.TooExpensive => "Pricey",
		RideThought.Unreliable => "Unsafe",
		RideThought.Mediocre => "OK",
		RideThought.Rubbish => "Rubbish",
		_ => "",
	};
}

/// <summary>
/// A park visitor. Renders as an upright camera-facing billboard (a placeholder until the authentic
/// peep sprites — <c>esprites.wad</c>'s <c>.TPC</c>/<c>.FPC</c>, a custom encoded image format — are
/// decoded). It picks rides (weighted by excitement), queues for them, boards, rides, then re-routes,
/// gaining <see cref="happiness"/> from exciting rides and losing it to long waits while
/// <see cref="energy"/> drains over its stay (<c>PeepInfo.ExitLevel</c> ± <c>ExitLevelVar</c> seconds). A tired or
/// fed-up peep heads home; with arrivals running (T-074) it then leaves the park, otherwise it is recycled as a
/// fresh arrival, so the crowd turns over. Always dropped onto the terrain surface; the billboard
/// yaws (about world up) to face the camera.
/// </summary>
public sealed class Peep : ModelEntity
{
	private const float StartHappiness = 50f;  // mood on arrival (0..100)
	private const float MaxEnergy = 100f;       // full on arrival, drains while in the park

	/// <summary>How long a visitor stays, in seconds: <c>PeepInfo.ExitLevel</c> ± <c>PeepInfo.ExitLevelVar</c>
	/// ("starting value for the ExitLevel counter, in SECONDS"). Set from the level's <c>Standard.sam</c>; energy
	/// drains to 0 over that stay (it was a hard-coded 2/s, ~50 s).</summary>
	public static float ExitLevelSeconds { get; set; } = 120f;
	public static float ExitLevelVarSeconds { get; set; } = 60f;

	/// <summary>True while an <see cref="ArrivalScheduler"/> feeds the park (T-074): a departing peep then leaves
	/// for good instead of being recycled into a fresh visitor.</summary>
	public static bool LeaveOnExit { get; set; }
	private const float WaitPenaltyPerSec = 1f; // standing in a line that isn't moving sours the mood
	private const float RideRewardScale = 0.3f; // happiness gained = ride excitement × this
	private const float LeaveHappiness = 10f;   // fed up below this
	// Stray litter besides the wrappers stalls hand out (their authored LitterEffect): ~1 piece per 2 minutes of
	// wandering. It was 0.04 (one per 25 s) before purchases dropped wrappers, which kept a 250-visitor park at the
	// litter cap and "Too messy" on top of the thoughts whatever the handymen and bins did.
	private const float LitterChancePerSec = 0.008f;
	private const float LitterRadius = 18f;          // litter within this sours the mood
	private const float LitterPenaltyPerSec = 0.4f;  // happiness lost per second per nearby litter (capped at 3 → max 1.2/s, so a peep survives the walk to a ride instead of dying in the litter death-spiral)
	private const float HungerPerSec = 2f;           // hunger builds while in the park (0..100)
	private const float HungerThreshold = 55f;        // detour to a food stall at this point
	private const float ThirstPerSec = 2.6f;          // thirst builds a little faster than hunger (0..100)
	private const float ThirstThreshold = 50f;        // detour to a drink stall at this point
	private const float BladderPerSec = 1.8f;         // bladder fills slowly while in the park (0..100)
	private const float BladderThreshold = 60f;        // detour to a toilet at this point
	private const float DrinkBladderBump = 35f;        // a drink fills the bladder noticeably (T-039)
	private const float BurstPenaltyPerSec = 3f;       // happiness lost while desperate with no toilet reachable
	private const float IndoorShelterBias = 1.5f;      // ride-choice bonus for an indoor attraction in bad weather (T-056)
	private const float ImpulseRadius = 40f;           // a gift/balloon/costume shop this close can tempt a passer-by
	private const float ImpulseCheckSeconds = 2f;      // how often a wandering peep considers an impulse buy
	private const float ImpulseChance = 0.06f;         // per check, scaled by mood (a happy peep splurges more)
	private const float VandalHappiness = 28f;        // a peep this unhappy may vandalise (well above LeaveHappiness, so there's a window to act before giving up)
	private const float VandalChancePerSec = 0.14f;   // ~1 act every ~7 s while unhappy and unwatched
	private const float WalkFps = 8f;                // walk-cycle frames per second

	/// <summary>Park-wide tallies (T-039): vandalism acts committed, and would-be acts a nearby guard
	/// stopped — so a guard's effect on vandalism is measurable.</summary>
	public static int VandalismActs { get; private set; }
	public static int VandalismDeterred { get; private set; }

	/// <summary>Park-wide count of toilet visits (T-039) — toilets earn no income, so this is the only
	/// signal they're used.</summary>
	public static int ToiletVisits { get; private set; }

	/// <summary>Park-wide tally of ride reactions (T-050): how many riders left with each
	/// <see cref="RideThought"/>, indexed by the enum. Surfaced on the HUD so the player can read the crowd's
	/// mood (the thought was computed but invisible before).</summary>
	private static readonly int[] thoughtTally = new int[6];
	public static IReadOnlyList<int> ThoughtTally => thoughtTally;
	public static int ThoughtTotal { get { int s = 0; foreach ( var n in thoughtTally ) s += n; return s; } }

	/// <summary>Zero the park-wide peep tallies (vandalism / toilet / ride thoughts) for a fresh park — used
	/// when a level is reloaded so a new park doesn't inherit the previous one's stats (T-062).</summary>
	public static void ResetStats()
	{
		VandalismActs = 0;
		VandalismDeterred = 0;
		ToiletVisits = 0;
		Array.Clear( thoughtTally );
	}

	/// <summary>Average visitor happiness (0–100) across the live crowd — the park rating; -1 if empty.</summary>
	public static float AverageHappiness
	{
		get
		{
			float sum = 0f;
			int n = 0;
			foreach ( var e in Entity.All )
				if ( e is Peep p ) { sum += p.happiness; n++; }
			return n == 0 ? -1f : sum / n;
		}
	}

	/// <summary>How many live visitors are thirstier than <paramref name="threshold"/> (0–100) — feeds the
	/// advisor's "visitors thirsty" advice (T-046).</summary>
	public static int CountThirstierThan( float threshold ) => CountNeedAbove( threshold, thirst: true );

	/// <summary>How many live visitors are hungrier than <paramref name="threshold"/> (0–100, T-046).</summary>
	public static int CountHungrierThan( float threshold ) => CountNeedAbove( threshold, thirst: false );

	/// <summary>How many live visitors are happier than <paramref name="threshold"/> (0–100) — feeds the
	/// golden-ticket "this many happy people" goal (T-055).</summary>
	public static int CountHappierThan( float threshold )
	{
		int n = 0;
		foreach ( var e in Entity.All )
			if ( e is Peep p && p.happiness > threshold )
				n++;
		return n;
	}

	private static int CountNeedAbove( float threshold, bool thirst )
	{
		int n = 0;
		foreach ( var e in Entity.All )
			if ( e is Peep p && (thirst ? p.thirst : p.hunger) > threshold )
				n++;
		return n;
	}

	// A small palette of clothing colours so the crowd reads as varied people.
	private static readonly (byte R, byte G, byte B)[] Palette =
	{
		(220, 80, 80), (70, 140, 225), (240, 205, 70), (110, 200, 110), (205, 120, 205), (235, 235, 235)
	};

	private static Model[]? sharedModels;

	// The kid sprites in esprites.wad — each peep gets a random one so the crowd reads as varied.
	private static readonly string[] KidSprites =
		{ "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };

	/// <summary>The directory the kid sprites live in (shared with the ride walk/head markers, T-048).</summary>
	internal const string KidSpriteDir = "esprites/Generic/Kids";

	/// <summary>A kid sprite name picked deterministically by <paramref name="index"/> (wraps), so ride
	/// markers vary their figure by slot/head value while reusing the same crowd art.</summary>
	internal static string KidSpriteName( int index ) =>
		KidSprites[((index % KidSprites.Length) + KidSprites.Length) % KidSprites.Length];

	private SpriteSheet? sheet;
	private SpriteSheet? kidSheet;   // the visitor's own sprite (a costume replaces `sheet` until it leaves)
	private ModelEntity? balloon;    // the balloon it carries once it bought one (Generic/Balloons)

	private readonly ParkTerrain terrain;
	private readonly IReadOnlyList<RideQueue> queues;
	private readonly PathGraph? paths;
	private readonly float speed;
	private readonly Model billboard;

	private readonly Vector3 home; // the park-edge point this peep entered at and heads back to when done
	private readonly float preferredExcitement; // this peep's ride-intensity taste (T-060, PeepTypes[*])

	private RideQueue? route;
	private RideQueue? lastRoute;
	private float rideTimer;
	private bool riding;
	private CoasterTrain? mount; // the coaster train this peep is riding in view (null = ordinary hidden ride)
	private bool leaving;
	private Shop? shopTarget;
	private float happiness = StartHappiness;

	/// <summary>The peep's reaction to the last ride it rode (T-050), or null if it hasn't ridden yet.</summary>
	public RideThought? LastThought { get; private set; }

	private float energy = MaxEnergy;
	private float energyDrainPerSec = MaxEnergy / 120f; // set per visitor from the ExitLevel stay
	private bool amidLitter; // standing among litter (≥ 2 pieces near) — "Too messy"
	private int peepType;

	/// <summary>The visitor's money ("Cash remaining" in the original's visitor window): the type's StartingCash ±
	/// StartingCashVarPc on arrival, spent at the gate, the stalls and the sideshows. Someone who can't afford what
	/// they came for goes home.</summary>
	public float Cash { get; private set; }

	// Spend `price` if the visitor has it; false (and nothing spent) otherwise.
	private bool TrySpend( float price )
	{
		if ( price <= 0f )
			return true;
		if ( Cash < price )
			return false;
		Cash -= price;
		return true;
	}

	// A fresh arrival's purse, minus the admission it pays at the gate.
	private void ArriveWithCash()
	{
		Cash = PeepTypes.CashFor( peepType, Random.Shared.NextDouble() );
		var fee = ParkFinances.Current?.EntryFee ?? 0f;
		Cash = MathF.Max( 0f, Cash - fee );
	}

	/// <summary>What this visitor is thinking (a <see cref="CrowdThoughts"/> / THOUGHTS.str index).</summary>
	public int Thought => CrowdThoughts.For( hunger / HungerThreshold, thirst / ThirstThreshold, bladder / BladderThreshold,
		happiness, amidLitter, route != null && route.PositionOf( this ) >= LongQueue );
	private const int LongQueue = 12; // a place this far back in a line is "Queue too long"

	/// <summary>How many live visitors think each thought (by THOUGHTS.str index).</summary>
	public static Dictionary<int, int> CrowdThoughtTally()
	{
		var tally = new Dictionary<int, int>();
		foreach ( var e in Entity.All )
			if ( e is Peep { riding: false } p )
				tally[p.Thought] = tally.GetValueOrDefault( p.Thought ) + 1;
		return tally;
	}

	// This peep's walking lane: a fixed offset within a tile (±3 of 10 units) applied to intermediate waypoints.
	private readonly Vector3 laneOffset = new( ((float)Random.Shared.NextDouble() - 0.5f) * 6f, ((float)Random.Shared.NextDouble() - 0.5f) * 6f, 0f );
	private float hunger;
	private float thirst;
	private float bladder;

	// Impulse items bought this visit (one of each: a visitor with a balloon doesn't buy another).
	private readonly HashSet<SaleCategory> bought = new();
	private float impulseTimer = ImpulseCheckSeconds * (float)Random.Shared.NextDouble();
	// A snack's wrapper/cup, dropped a little after eating (the stall's LitterEffect is the chance), seconds left.
	private float wrapperTimer = -1f;

	/// <summary>True when the visitor bought a balloon / costume this visit (challenge types 9 and 10).</summary>
	public bool HasBalloon => bought.Contains( SaleCategory.Balloon );
	public bool HasCostume => bought.Contains( SaleCategory.Costume );

	// Sprite animation state (real sprite path only).
	private readonly float spriteHeight;
	private int facing;          // 0..7 camera-relative direction sector → directional walk cycle
	private float walkPhase;     // advances while moving; indexes the walk cycle
	private bool movedThisFrame;
	private Vector3 moveDir = new( 0, -1, 0 ); // last world travel direction (drives the facing each frame)

	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );
	private readonly ModelEntity? shadow; // soft ground shadow under the peep (hidden while riding)

	// Pathfinding state (T-036): the active route's waypoints + the goal it was planned for, so a path is
	// reused until the goal moves to another tile rather than re-planned every frame.
	private List<Vector3>? pathPts;
	private int pathIdx;
	private (int X, int Y)? pathGoalTile;

	private static int recycles; // park-wide count of visitors that left and were replaced (diagnostics)

	public Peep( ParkTerrain terrain, IReadOnlyList<RideQueue> queues, Vector3 spawn, int colorIndex, PathGraph? paths = null )
	{
		this.terrain = terrain;
		this.queues = queues;
		this.paths = paths;
		home = spawn;

		// Ride taste (T-060): a random peep type, whose authored PreferredExcitement steers ride choice toward
		// rides near that intensity (a timid peep favours gentle rides, a thrill-seeker intense ones).
		peepType = Random.Shared.Next( PeepTypes.Count );
		preferredExcitement = PeepTypes.PreferredFor( peepType );

		// Prefer a real decoded peep sprite (per-frame models, directional walk cycles), a random kid for
		// crowd variety; fall back to a flat-colour billboard if it can't load.
		// ~1 tile tall (was 15–18 units, sized for the old 16-unit grid — next to true-scale rides a visitor stood
		// 1.5 tiles tall, T-072).
		spriteHeight = 9.4f + (float)Random.Shared.NextDouble() * 1.9f;
		sheet = kidSheet = SpriteSheet.Load( KidSpriteDir, KidSprites[Random.Shared.Next( KidSprites.Length )] );
		if ( sheet != null )
		{
			billboard = sheet.FrameModel( 0 );
			Model = billboard;
			ApplySprite();
		}
		else
		{
			billboard = SharedModel( colorIndex );
			Model = billboard;
			Scale = new Vector3( 3f, 1f, 5f + (float)Random.Shared.NextDouble() * 2f );
		}
		speed = 8f + (float)Random.Shared.NextDouble() * 7f;
		Position = spawn;
		// ~0.7 of a 10-unit tile across (it was sized for the old 16-unit grid and read as a dark square — T-072).
		shadow = new ModelEntity { Model = ShadowModel(), Scale = new Vector3( spriteHeight * 0.22f, spriteHeight * 0.22f, 1f ) };
		ParkFinances.Current?.TakeEntryFee(); // pays the gate on arrival
		ArriveWithCash();
		happiness = EntryFeeOpinion.ArrivalHappinessNow(); // how the fee compares with what the park offers
		energyDrainPerSec = DrainForStay();
		PickRoute();
		DropToGround();
	}

	protected override void OnUpdate()
	{
		UpdateShadow();

		// On the ride: hidden, waiting out the ride duration, then reappear at the exit, bank the fun, and
		// either head home (tired/fed up) or pick another ride.
		if ( riding )
		{
			rideTimer -= Time.Delta;
			if ( rideTimer <= 0f )
			{
				var (satisfaction, thought) = RateRide( route!.Ride.Excitement, route.Ride.TicketPrice, route.Ride.Reliability );
				LastThought = thought;
				thoughtTally[(int)thought]++; // park-wide mood tally (T-050, surfaced on the HUD)
				route.Ride.RegisterRideExperience( satisfaction );
				// Happiness change is driven by satisfaction (which already folds in value-for-money +
				// reliability), centred on 50 so a great ride lifts the mood and a poor one sours it.
				happiness = Math.Clamp( happiness + (satisfaction - 50f) * RideRewardScale, 0f, 100f );
				riding = false;
				route.Leave();
				mount?.Unboard( this ); // climb off the coaster train (no-op for ordinary rides)
				mount = null;
				Model = billboard;
				Position = route.ExitPoint;
				ResetPath(); // teleported to the exit — replan from here
				if ( WantsToLeave() )
					BeginLeaving();
				else
					PickRoute();
			}
			return;
		}

		// Heading for the exit: walk home (routing around rides), then recycle into a fresh visitor.
		if ( leaving )
		{
			if ( MoveTo( home ) )
			{
				if ( LeaveOnExit )
					LeavePark();
				else
					Recycle();
			}
			FaceCamera();
			ApplySprite();
			return;
		}

		// Detouring to a stall: walk there (routing around rides), use it, satisfy the matching need
		// (food → hunger, drink → thirst but fills the bladder, toilet → bladder, free), then resume riding.
		if ( shopTarget != null )
		{
			if ( MoveTo( shopTarget.Position ) )
			{
				// No money for it: an unmet need sends the visitor home; a souvenir they can't afford is just skipped.
				if ( shopTarget.Kind != ShopKind.Toilet && !TrySpend( shopTarget.Price ) )
				{
					bool need = shopTarget.Kind != ShopKind.Item;
					shopTarget = null;
					if ( need )
					{
						BeginLeaving();
						return;
					}
					ResetPath();
					PickRoute();
					return;
				}
				if ( shopTarget.Kind == ShopKind.Toilet )
				{
					bladder = 0f; // toilets are free facilities — no income
					ToiletVisits++;
				}
				else
					Buy( shopTarget );
				shopTarget = null;
				ResetPath();
				PickRoute();
			}
			FaceCamera();
			ApplySprite();
			return;
		}

		if ( route == null )
		{
			PickRoute();
			return;
		}

		// Being in the park is tiring; a long enough day (or a soured mood) sends the peep home.
		energy -= energyDrainPerSec * Time.Delta;

		// Visitors occasionally drop litter, and standing among litter sours the mood (until a handyman
		// clears it). Both raise the chance the peep heads home unhappy. A nearby guard deters littering.
		if ( Random.Shared.NextDouble() < LitterChancePerSec * Time.Delta && !Staff.GuardNear( Position ) )
			Shop.Discard( Position );
		if ( wrapperTimer >= 0f && (wrapperTimer -= Time.Delta) < 0f )
			Shop.Discard( Position );
		int nearbyLitter = CountNearbyLitter();
		amidLitter = nearbyLitter >= 2;
		if ( nearbyLitter > 0 )
			happiness = Math.Max( 0f, happiness - LitterPenaltyPerSec * nearbyLitter * Time.Delta );

		// A miserable visitor vandalises — scattering litter around it — unless a guard is close enough to
		// stop it (the guard's deterrence is what makes guards worth hiring; both outcomes are tallied).
		if ( happiness <= VandalHappiness && Random.Shared.NextDouble() < VandalChancePerSec * Time.Delta )
		{
			if ( Staff.GuardNear( Position ) )
			{
				VandalismDeterred++;
			}
			else
			{
				Vandalise();
				VandalismActs++;
			}
		}

		// Bad weather sours an exposed peep's mood (T-056): steady discomfort outdoors in rain/snow (worse in a
		// storm). Heading to an indoor stall (a sideshow) counts as seeking shelter, so it's exempt; riding peeps
		// already returned above. The soured mood feeds WantsToLeave below, so crowds thin out in foul weather.
		if ( WeatherSim.Current is { State.IsClear: false } weather && !route.Ride.IsIndoors )
			happiness = Math.Max( 0f, happiness - Weather.ComfortPenaltyPerSec( weather.State ) * Time.Delta );

		if ( WantsToLeave() )
		{
			BeginLeaving();
			return;
		}

		// Needs build over time; when one passes its threshold the peep leaves the ride line and detours to
		// the matching stall (food→hunger, drink→thirst, toilet→bladder), most-urgent first.
		hunger += HungerPerSec * Time.Delta;
		thirst += ThirstPerSec * Time.Delta;
		bladder += BladderPerSec * Time.Delta;
		if ( bladder > 100f )
			bladder = 100f;

		if ( (NeedDetour() ?? ImpulseDetour()) is { } target )
		{
			route?.Dequeue( this );
			route = null;
			ResetPath();
			shopTarget = target;
			return;
		}

		// Desperate (bladder full) with no toilet to reach: the mood sours fast (build a toilet!).
		if ( bladder >= 100f && Shop.Nearest( ShopKind.Toilet, Position ) == null )
			happiness = Math.Max( 0f, happiness - BurstPenaltyPerSec * Time.Delta );

		// Stand at my place in line (front = the entrance, each place back steps one waypoint out), and
		// walk toward it — as those ahead board, the line shifts forward and my target advances.
		int pos = route.PositionOf( this );
		if ( pos < 0 )
		{
			route.Enqueue( this );
			pos = route.PositionOf( this );
		}

		var standPt = route.StandPoint( pos );
		bool atSpot = MoveTo( standPt );
		// The boarding tile sits right next to the ride footprint, which the path graph treats as blocked, so
		// A* stops ~1–2 tiles short and the peep can never land exactly on it (it would orbit the entrance
		// forever, never boarding). Count "close enough at the front" as arrived so the peep actually boards.
		const float BoardReach = 40f; // ~2.5 tiles
		if ( pos == 0 && (Position - standPt).Length <= BoardReach )
			atSpot = true;

		// Only the front peep boards, and only once it has reached the entrance and a slot is free.
		if ( pos == 0 && atSpot && route.HasFreeSlot )
		{
			// A sideshow is pay-to-play with a chance of winning a prize (T-058); a normal ride takes a flat ticket.
			if ( route.Ride.IsSideshow )
			{
				if ( !TrySpend( route.Ride.TicketPrice ) ) // can't pay for a game → give up and go home
				{
					BeginLeaving();
					return;
				}
				var (net, won) = route.Ride.PlaySideshow();
				ParkFinances.Current?.TakeSideshowTakings( net, won );
			}
			else
				ParkFinances.Current?.TakeRideTicket( route.Ride.TicketPrice ); // pays to board
			route.Board( this );
			riding = true;
			rideTimer = route.RideDuration;
			// A coaster carries the peep on its train in view; any other ride (or a full/track-less coaster)
			// hides the peep until it reappears at the exit.
			mount = route.Ride.Train;
			if ( mount == null || !mount.TryBoard( this ) )
			{
				mount = null;
				Model = null;
			}
			return;
		}

		// Waiting in a line that isn't moving (not yet at the front) slowly sours the mood.
		if ( atSpot && pos > 0 )
			happiness = Math.Max( 0f, happiness - WaitPenaltyPerSec * Time.Delta );

		FaceCamera();
		ApplySprite();
	}

	// True once a peep is tired out or fed up — time to head for the exit.
	private bool WantsToLeave() => energy <= 0f || happiness <= LeaveHappiness;

	/// <summary>
	/// Rates a ride experience (T-050) → a satisfaction score 0..100 + the peep's thought. Pure so it can
	/// be unit-tested. Satisfaction blends the ride's excitement with value-for-money (its price vs the
	/// "fair" price ≈ excitement/10) and its reliability; the thought names the dominant factor. A free ride
	/// (<paramref name="ticketPrice"/> ≤ 0 — every ride in TPW; only sideshows charge) has no value-for-money term.
	/// </summary>
	public static (float Satisfaction, RideThought Thought) RateRide( int excitement, float ticketPrice, float reliability )
	{
		float fairPrice = MathF.Max( 1f, excitement / 10f );
		// +1 fair → +0 value; each unit cheaper adds, each unit dearer subtracts (scaled by fair price).
		bool free = ticketPrice <= 0f;
		float valueRatio = free ? 1f : ticketPrice / fairPrice;     // 1 = fair, <1 cheap, >1 dear
		float valueDelta = (1f - valueRatio) * 30f;                 // ±; cheap rides feel better (0 when free)
		float reliabilityPenalty = (1f - Math.Clamp( reliability, 0f, 1f )) * 60f;

		float satisfaction = Math.Clamp( excitement + valueDelta - reliabilityPenalty, 0f, 100f );

		RideThought thought =
			reliability < 0.5f ? RideThought.Unreliable :
			valueRatio > 1.5f ? RideThought.TooExpensive :
			!free && valueRatio < 0.6f && satisfaction >= 50f ? RideThought.GoodValue :
			satisfaction >= 70f ? RideThought.GreatRide :
			satisfaction <= 30f ? RideThought.Rubbish :
			RideThought.Mediocre;

		return (satisfaction, thought);
	}

	private void BeginLeaving()
	{
		route?.Dequeue( this );
		route = null;
		ResetPath();
		leaving = true;
	}

	// Energy drain for one visitor's stay: ExitLevel ± ExitLevelVar seconds (at least 10 s).
	private static float DrainForStay()
	{
		float stay = ExitLevelSeconds + ((float)Random.Shared.NextDouble() * 2f - 1f) * ExitLevelVarSeconds;
		return MaxEnergy / MathF.Max( 10f, stay );
	}

	// A departed visitor leaves the park for good (arrivals running, T-074): drop it and its shadow.
	private void LeavePark()
	{
		Entity.All.Remove( this );
		if ( shadow != null )
			Entity.All.Remove( shadow );
		DropSouvenirs();
	}

	// A departing visitor takes its balloon and costume home.
	private void DropSouvenirs()
	{
		if ( balloon != null )
		{
			Entity.All.Remove( balloon );
			balloon = null;
		}
		sheet = kidSheet;
	}

	// Show what the visitor just bought: a costume swaps its sprite for the theme's costume set
	// (esprites/<Theme>/Costumes), a balloon floats from its hand (esprites/Generic/Balloons, drawn with the same
	// hotspot/scale as the kid so it lines up with the arm).
	private void ShowSouvenir( SaleCategory category )
	{
		if ( category == SaleCategory.Costume && Souvenirs.Costume() is { } costume )
			sheet = costume;
		else if ( category == SaleCategory.Balloon && balloon == null && Souvenirs.Balloon() is { } b )
		{
			// SPR_BL holds four colours (blue, green, red, yellow) as pairs of frames: the balloon, then it bursting.
			balloon = new ModelEntity { Model = b.FrameModel( 2 * Random.Shared.Next( Math.Max( 1, b.FrameCount / 2 ) ) ) };
			UpdateBalloon();
		}
	}

	// Keep the balloon on the visitor: same anchor + scale, nudged toward the camera so it draws over the kid.
	private void UpdateBalloon()
	{
		if ( balloon == null )
			return;
		if ( riding || sheet == null )
		{
			balloon.Position = Offscreen;
			return;
		}
		var toCam = (Camera.Position - Position).WithZ( 0 );
		float len = toCam.Length;
		balloon.Position = len > 0f ? Position + toCam * (0.05f / len) : Position;
		balloon.Rotation = Rotation;
		balloon.Scale = Scale;
	}

	// Recycle a departed peep as a fresh arrival, keeping the crowd steady without churning entities.
	private void Recycle()
	{
		recycles++;
		leaving = false;
		shopTarget = null;
		ResetPath();
		happiness = EntryFeeOpinion.ArrivalHappinessNow();
		energy = MaxEnergy;
		energyDrainPerSec = DrainForStay();
		hunger = 0f;
		thirst = 0f;
		bladder = 0f;
		bought.Clear();
		DropSouvenirs();
		wrapperTimer = -1f;
		lastRoute = null;
		ParkFinances.Current?.TakeEntryFee(); // a fresh visitor pays the gate
		ArriveWithCash();
		PickRoute();
		if ( Environment.GetEnvironmentVariable( "OPENTPW_PEEP_DEBUG" ) != null )
			Log.Trace( $"[peep] recycled (total {recycles})" );
	}

	// Walk toward a world point (XY only), dropping onto the terrain. Returns true once within reach.
	// Tracks the movement direction (for the directional sprite) and whether we actually moved.
	private bool WalkToward( Vector3 target )
	{
		var to = new Vector3( target.X - Position.X, target.Y - Position.Y, 0 );
		bool arrived = to.Length < 2.5f;
		movedThisFrame = !arrived;
		if ( !arrived )
		{
			Position += to.Normal * speed * Time.Delta;
			moveDir = to.Normal; // world travel direction; the facing is derived from it per frame
		}
		DropToGround();
		return arrived;
	}

	// Walk toward a goal along a path that routes around rides/shops (T-036), falling back to a straight
	// line when there's no pathfinder or no route. The path is re-planned only when the goal lands in a new
	// tile (so a peep shuffling up a queue or chasing a fixed target doesn't re-run A* every frame).
	// Returns true once the peep reaches the goal.
	private bool MoveTo( Vector3 goal )
	{
		if ( paths == null )
			return WalkToward( goal );

		var goalTile = paths.TileOf( goal );
		if ( pathPts == null || pathGoalTile != goalTile )
		{
			pathPts = paths.FindPath( Position, goal );
			pathGoalTile = goalTile;
			pathIdx = 0;
		}

		if ( pathPts == null || pathPts.Count == 0 )
			return WalkToward( goal ); // unreachable / off-grid — head straight there

		// Intermediate waypoints are tile centres shared by every peep, so a crowd walked in one perfect single file;
		// each peep keeps its own small offset inside the tile (the final point stays exact — a queue spot, a stall).
		bool final = pathIdx >= pathPts.Count - 1;
		var waypoint = final ? pathPts[pathIdx] : pathPts[pathIdx] + laneOffset;
		bool reachedWaypoint = WalkToward( waypoint );
		if ( reachedWaypoint && pathIdx < pathPts.Count - 1 )
			pathIdx++;
		return reachedWaypoint && pathIdx >= pathPts.Count - 1;
	}

	// Drop the cached path so the next MoveTo re-plans (called when the peep switches goal/mode).
	private void ResetPath()
	{
		pathPts = null;
		pathGoalTile = null;
	}

	// Picks the current sprite frame from the camera-relative facing + walk phase, holding the cycle's
	// first (standing) frame when idle, at a uniform scale (the quad is hotspot-anchored so feet stay
	// planted and frames don't pulse). No-op on the flat-billboard fallback path.
	private void ApplySprite()
	{
		if ( sheet == null )
			return;

		facing = SpriteFacing.Sector( moveDir ); // matches the on-screen travel direction (T-035)
		var anims = sheet.Anims;
		int frame = 0;
		if ( anims.Count > 0 )
		{
			var a = anims[facing % anims.Count];
			walkPhase = movedThisFrame ? walkPhase + WalkFps * Time.Delta : 0f; // idle holds the standing frame
			frame = a.Start + ( a.Count > 0 ? (int)walkPhase % a.Count : 0 );
		}

		Model = sheet.FrameModel( frame );
		float p2w = spriteHeight / sheet.RefHeight; // one px→world factor for the whole sheet (no jitter)
		Scale = new Vector3( p2w, 1f, p2w );
		UpdateBalloon();
	}

	// Keep the soft ground shadow under the peep (hidden while it's riding / off the ground).
	private void UpdateShadow()
	{
		if ( shadow == null )
			return;
		shadow.Position = riding
			? Offscreen
			: Position.WithZ( terrain.SampleHeight( Position.X, Position.Y ) + 0.12f );
		if ( riding )
			UpdateBalloon();
	}

	// A shared flat dark translucent ground decal, so adding peeps/staff allocates nothing extra.
	private static Model? shadowModel;
	internal static Model ShadowModel()
	{
		if ( shadowModel != null )
			return shadowModel;
		var material = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader",
			MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite );
		material.Set( "Color", new Texture( ShadowBlob( 32 ), 32, 32 ) );
		shadowModel = Primitives.Plane.GenerateModel( material );
		return shadowModel;
	}

	/// <summary>A round, soft-edged shadow: black, alpha falling from ~110 at the centre to 0 at the rim (it was a
	/// uniformly dark 1×1 texture, i.e. a square).</summary>
	internal static byte[] ShadowBlob( int size )
	{
		var rgba = new byte[size * size * 4];
		float c = (size - 1) / 2f;
		for ( int y = 0; y < size; y++ )
			for ( int x = 0; x < size; x++ )
			{
				float r = MathF.Sqrt( (x - c) * (x - c) + (y - c) * (y - c) ) / c;
				rgba[(y * size + x) * 4 + 3] = (byte)(r >= 1f ? 0 : 110f * MathF.Pow( 1f - r, 1.5f ));
			}
		return rgba;
	}

	/// <summary>An entertainer lifts this peep's mood (capped at full); a happier peep stays longer.</summary>
	public void Cheer( float amount ) => happiness = Math.Min( 100f, happiness + amount );

	/// <summary>True when this peep is unhappy enough to vandalise — guards patrol toward these so their
	/// deterrence actually lands where the trouble is (T-039). Not while riding (it's off wandering then).</summary>
	public bool Unhappy => !riding && !leaving && happiness <= VandalHappiness;

	/// <summary>Place this peep on a moving coaster seat (T-045 3b): sit at <paramref name="seat"/> and
	/// keep facing the camera. Called by the <see cref="CoasterTrain"/> each frame while the peep is aboard;
	/// the peep's own update skips its walk logic while riding, so the train fully owns its transform.</summary>
	public void SeatAt( Vector3 seat )
	{
		Position = seat;
		FaceCamera();
	}

	// Pay for and consume what a stall sells: the park takes the sale (by product, for the challenges), the
	// stall's authored effects apply (needs relieved, mood/energy lifted), a drink fills the bladder, a snack
	// may leave a wrapper, and a souvenir is kept for the rest of the visit.
	private void Buy( Shop shop )
	{
		var category = shop.Category;
		ParkFinances.Current?.TakeFoodSale( shop.Price, shop.Kind == ShopKind.Drink, shop.CostOfGoods, category );
		if ( shop.Stats is { } stats )
		{
			(hunger, thirst, happiness, energy) = ShopBuilding.ApplyEffects( stats, hunger, thirst, happiness, energy,
				HungerThreshold, ThirstThreshold, MaxEnergy );
			if ( stats.LitterEffect > 0f && Random.Shared.NextDouble() * 100f < stats.LitterEffect )
				wrapperTimer = 4f + 8f * (float)Random.Shared.NextDouble();
		}
		else if ( shop.Kind == ShopKind.Drink )
			thirst = 0f;
		else if ( shop.Kind == ShopKind.Food )
			hunger = 0f;
		if ( shop.Kind == ShopKind.Drink )
			bladder = MathF.Min( 100f, bladder + DrinkBladderBump );
		if ( shop.Kind == ShopKind.Item )
		{
			bought.Add( category );
			ShowSouvenir( category );
		}
	}

	// Now and then a wandering visitor passing a gift / balloon / costume shop goes in — if it hasn't bought
	// that item yet and can afford it; a happier visitor is likelier to splurge. Null when nothing tempts it.
	private Shop? ImpulseDetour()
	{
		if ( (impulseTimer -= Time.Delta) > 0f )
			return null;
		impulseTimer = ImpulseCheckSeconds;
		Shop? best = null;
		float bestD2 = ImpulseRadius * ImpulseRadius;
		foreach ( var s in Shop.Stalls )
		{
			if ( s.Kind != ShopKind.Item || bought.Contains( s.Category ) || Cash < s.Price )
				continue;
			float d2 = (s.Position - Position).LengthSquared;
			if ( d2 < bestD2 )
			{
				bestD2 = d2;
				best = s;
			}
		}
		if ( best == null || Random.Shared.NextDouble() >= ImpulseChance * (happiness / 100f) * 2f )
			return null;
		return best;
	}

	// Pick the stall to detour to for the most urgent over-threshold need that actually has a stall — by
	// urgency (need ÷ its threshold) descending — or null if no need is pressing / no matching stall exists.
	private Shop? NeedDetour()
	{
		// Every stall serving a need that has passed its threshold is a candidate, scored by the authored need
		// weights against the walk (NeedChoiceScorer, T-060).
		var candidates = new List<(ShopKind Kind, float Urgency, float Distance)>();
		var stalls = new List<Shop>();
		foreach ( var (urgency, kind) in new[]
		{
			(bladder / BladderThreshold, ShopKind.Toilet),
			(thirst / ThirstThreshold, ShopKind.Drink),
			(hunger / HungerThreshold, ShopKind.Food),
		} )
		{
			if ( urgency < 1f )
				continue;
			foreach ( var stall in Shop.Stalls )
				if ( stall.Kind == kind )
				{
					candidates.Add( (kind, urgency, (stall.Position - Position).Length) );
					stalls.Add( stall );
				}
		}

		int best = NeedChoiceScorer.Choose( candidates, NeedChoiceScorer.Weights, RideChoiceScorer.Weights.Distance );
		return best >= 0 ? stalls[best] : null;
	}

	// Act of vandalism: scatter a couple of pieces of litter around the peep (a mess a handyman must then
	// clean), venting a little of its frustration so it doesn't immediately do it again.
	private void Vandalise()
	{
		Litter.TryDrop( Position );
		var off = new Vector3( ((float)Random.Shared.NextDouble() - 0.5f) * LitterRadius,
			((float)Random.Shared.NextDouble() - 0.5f) * LitterRadius, 0 );
		Litter.TryDrop( Position + off );
		happiness = Math.Min( 100f, happiness + 6f );
	}

	// Litter within reach, capped so a single filthy spot doesn't drive happiness down instantly.
	private int CountNearbyLitter()
	{
		float r2 = LitterRadius * LitterRadius;
		int count = 0;
		foreach ( var l in Litter.Active )
		{
			float dx = l.Position.X - Position.X, dy = l.Position.Y - Position.Y;
			if ( dx * dx + dy * dy <= r2 && ++count >= 3 )
				break;
		}
		return count;
	}

	private void DropToGround() => Position = Position.WithZ( terrain.SampleHeight( Position.X, Position.Y ) );

	// Cylindrical billboard: yaw about world up so the quad's +Y face points at the camera.
	private void FaceCamera()
	{
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
	}

	// Choose the next ride weighted by its excitement (more exciting rides draw more peeps), avoiding an
	// immediate repeat of the last ride when there's a choice, and join the back of its line.
	private void PickRoute()
	{
		if ( queues.Count == 0 )
		{
			route = null;
			return;
		}

		var candidates = queues.Where( q => q != lastRoute ).ToList();
		if ( candidates.Count == 0 )
			candidates = queues.ToList();

		// Score each ride by the authored weighted-utility scorer — excitement vs distance vs queue length
		// (T-060) — then pick one weighted by score (so a better ride draws more peeps, but not all of them).
		var w = RideChoiceScorer.Weights;
		// In bad weather, indoor attractions (sideshows) gain shelter appeal so peeps seek cover (T-056).
		float indoorBonus = WeatherSim.Current is { } weather && !weather.State.IsClear ? IndoorShelterBias : 0f;
		var scores = candidates
			.Select( q => RideChoiceScorer.Score(
				new RideOption( q.Ride.Excitement, Position.Distance( q.Ride.Position ), q.LineLength, q.Ride.IsNew, q.Ride.IsIndoors ),
				w, indoorBonus, preferredExcitement ) )
			.ToList();
		int idx = RideChoiceScorer.ChooseWeighted( scores, (float)Random.Shared.NextDouble() );
		route = idx >= 0 ? candidates[idx] : candidates[^1];

		lastRoute = route;
		route.Enqueue( this );
	}

	private static Model SharedModel( int colorIndex )
	{
		sharedModels ??= BuildModels();
		return sharedModels[((colorIndex % Palette.Length) + Palette.Length) % Palette.Length];
	}

	// One shared upright billboard per clothing colour — the yaw + per-entity scale do the rest.
	private static Model[] BuildModels()
	{
		var models = new Model[Palette.Length];
		for ( int i = 0; i < Palette.Length; i++ )
		{
			var (r, g, b) = Palette[i];
			models[i] = Billboard.Make( r, g, b );
		}
		return models;
	}
}
