using System.Numerics;

namespace OpenTPW;

/// <summary>
/// A ride instance: loads its RSE script + settings + model from the ride's `.wad`, drives the
/// script through a <see cref="RideEngine"/>, and renders the ride's geometry. Slice 1 of the ride
/// engine — the model is static and engine opcodes cover sound (see the plan / docs/tickets/T-007).
/// </summary>
public class Ride : Entity
{
	public RideVM VM { get; private set; }

	/// <summary>The ride's tile footprint (from its <c>.sam</c> Info.Shape) — how many grid tiles it occupies.</summary>
	public RideShape Shape { get; }

	/// <summary>Fractional position within the entrance cell where queueing peeps stand (default centre).</summary>
	public (float X, float Y) EntryStandPos { get; private set; } = (0.5f, 0.5f);

	/// <summary>Fractional position within the exit cell where peeps appear (default centre).</summary>
	public (float X, float Y) ExitAppearPos { get; private set; } = (0.5f, 0.5f);

	/// <summary>One research/upgrade level from the ride's <c>Upgrades[*]</c> table.</summary>
	public readonly record struct UpgradeLevelInfo( int Capacity, float CostResearch, float CostUpgrade, float WearRate = 5f );

	// Rides.sam's per-level defaults for Upgrades[n].WearRate ("out of 10"), for a ride .sam that doesn't set its own:
	// an upgraded ride wears more slowly.
	private static readonly float[] DefaultWearRates = { 5f, 3f, 2f };

	private int maxCapacity = 8;
	private readonly List<UpgradeLevelInfo> upgrades = new();

	/// <summary>How many peeps the ride holds at once — the current upgrade level's capacity
	/// (<c>Upgrades[level].InitCapacity</c>), falling back to <c>UsageInfo.MaxCapacity</c>.</summary>
	public int Capacity => upgrades.Count > 0 ? upgrades[Math.Min( UpgradeLevel, upgrades.Count - 1 )].Capacity : maxCapacity;

	/// <summary>The ride's available upgrade levels (level 0 = as built).</summary>
	public IReadOnlyList<UpgradeLevelInfo> Upgrades => upgrades;

	/// <summary>Current applied upgrade level, and the highest level researched (≥ <see cref="UpgradeLevel"/>).</summary>
	public int UpgradeLevel { get; private set; }
	public int ResearchedLevel { get; private set; }

	private const float ResearchDuration = 12f; // researcher-seconds to unlock the next level
	private bool researching;
	private float researchProgress;

	public bool HasNextLevel => !IsFeature && UpgradeLevel + 1 < upgrades.Count;
	public bool NextResearched => ResearchedLevel > UpgradeLevel;
	public bool IsResearching => researching;
	public float ResearchFraction => researching ? Math.Clamp( researchProgress / ResearchDuration, 0f, 1f ) : 0f;

	/// <summary>True while this ride is the one the research team is actively working on (the queue head, T-044).</summary>
	public bool IsResearchActive => researching && ResearchQueue.Active == this;
	/// <summary>Rides ahead of this one in the research queue (≥1 while waiting; 0 when active/not queued).</summary>
	public int ResearchQueuePosition => Math.Max( 0, ResearchQueue.Position( this ) );
	public float NextResearchCost => HasNextLevel ? upgrades[UpgradeLevel + 1].CostResearch : 0f;
	public float NextUpgradeCost => HasNextLevel ? upgrades[UpgradeLevel + 1].CostUpgrade : 0f;

	/// <summary>Begin researching the next level (caller pays <see cref="NextResearchCost"/>).</summary>
	public void StartResearch()
	{
		if ( HasNextLevel && !NextResearched && !researching )
		{
			researching = true;
			researchProgress = 0f;
			ResearchQueue.Enqueue( this ); // join the park-wide queue; only the head actually advances (T-044)
		}
	}

	/// <summary>Advance research by <paramref name="researchers"/> researcher-seconds.</summary>
	public void TickResearch( float dt, float researchers )
	{
		if ( !researching || researchers <= 0 )
			return;
		researchProgress += dt * researchers;
		if ( researchProgress >= ResearchDuration )
		{
			ResearchedLevel = UpgradeLevel + 1;
			researching = false;
			researchProgress = 0f;
			ParkFinances.Current?.NoteResearchCompleted(); // challenge type 16 (T-054)
			ResearchQueue.Dequeue( this ); // done → the next queued ride becomes the active one
		}
	}

	/// <summary>Apply the researched next level (caller pays <see cref="NextUpgradeCost"/>); bumps capacity.</summary>
	public void ApplyUpgrade()
	{
		if ( NextResearched )
		{
			UpgradeLevel++;
			engine.SpawnParticleEffect( ParticleEmitter.Upgrade ); // Rides.sam UpgradeParticleEffect
		}
	}

	/// <summary>Restore this ride's progression from a save (T-059): the applied + researched upgrade levels,
	/// any in-progress research (fraction of the next level), and the mechanical condition. Values are clamped
	/// to the ride's real upgrade table so a stale/corrupt save can't push it out of range. The caller rebuilds
	/// the park-wide <see cref="ResearchQueue"/> order separately (so it matches the saved queue, not load order).</summary>
	public void RestoreProgress( int upgradeLevel, int researchedLevel, bool isResearching, float researchFraction, float reliability, bool broken )
	{
		int maxLevel = Math.Max( 0, upgrades.Count - 1 );
		UpgradeLevel = Math.Clamp( upgradeLevel, 0, maxLevel );
		ResearchedLevel = Math.Clamp( researchedLevel, UpgradeLevel, maxLevel );

		researching = isResearching && HasNextLevel && !NextResearched;
		researchProgress = researching ? Math.Clamp( researchFraction, 0f, 1f ) * ResearchDuration : 0f;

		Reliability = Math.Clamp( reliability, 0f, 1f );
		IsBroken = broken;
	}

	/// <summary>The ride's duration band (<c>Info.DurationUnit</c>, 0–3 — indexes a seconds table in TP.EXE).</summary>
	public int DurationUnit { get; private set; }

	/// <summary>How long a single ride lasts, in seconds (peeps stay aboard this long). Anchored to the
	/// ride's own running animation length — see the ctor.</summary>
	public float RideDuration { get; private set; } = 5f;

	/// <summary>How exciting the ride is (<c>UsageInfo.ExcitementLevel</c>) — its draw: peeps pick rides
	/// weighted by this, so more exciting rides build longer queues.</summary>
	public int Excitement { get; private set; } = 50;

	// Ride rating (T-050): a running average of how satisfied riders were (0..100), seeded from Excitement.
	// Peeps weight their ride choice by this, and it feeds back into park rating via rider happiness. A
	// well-priced, reliable, exciting ride climbs; an overpriced or unreliable one sinks.
	private float ratingSum;
	private int ratingCount;

	/// <summary>Average rider satisfaction 0..100 (the ride's reputation); = <see cref="Excitement"/>
	/// until anyone has ridden.</summary>
	public float Rating => ratingCount == 0 ? Excitement : ratingSum / ratingCount;

	/// <summary>Record one rider's satisfaction into the running rating (called when a peep finishes).</summary>
	public void RegisterRideExperience( float satisfaction )
	{
		ratingSum += Math.Clamp( satisfaction, 0f, 100f );
		ratingCount++;
		RollUsers();
		usersThisMonth++;
	}

	/// <summary>Riders in the previous in-game month — the original ride window's "Users last month".</summary>
	public int UsersLastMonth
	{
		get
		{
			RollUsers();
			return usersLastMonth;
		}
	}

	private int usersMonth = -1, usersThisMonth, usersLastMonth;

	// Close the monthly tally when the in-game month has moved on (lazily, so no clock subscription to leak).
	private void RollUsers()
	{
		int month = GameClock.Current?.TotalMonths ?? 0;
		if ( month == usersMonth )
			return;
		usersLastMonth = month == usersMonth + 1 ? usersThisMonth : 0;
		usersThisMonth = 0;
		usersMonth = month;
	}

	// Ride age (T-060): the in-game day the ride was built, so a freshly-opened ride draws extra interest
	// (the "new ride" bonus) for its first DecisionVariable1 days, then settles to its steady-state pull.
	private readonly int builtDay;

	/// <summary>How many in-game days this ride has been open.</summary>
	public int AgeDays => Math.Max( 0, (GameClock.Current?.TotalDays ?? builtDay) - builtDay );

	/// <summary>True while the ride is still "new" — younger than the authored new-ride window
	/// (<c>DecisionVariable1</c>), so peeps weight it more heavily (T-060).</summary>
	public bool IsNew => AgeDays < RideChoiceScorer.Weights.NewRideDays;

	/// <summary>The ride's base attraction value (<c>Info.AttractionValue</c>).</summary>
	public int Attraction { get; private set; } = 25;

	// The extra draw of a new ride (Attraction[0..2].NewBonus — 10 / 7 / 4 on most) for each NewAttractionDecayTime
	// days (30) of its life.
	private readonly int[] newBonus = { 10, 7, 4 };
	private int newAttractionDecayDays = 30;

	/// <summary>What this ride adds to the park's pull now: <see cref="Attraction"/> plus its new-ride bonus.</summary>
	public int AttractionNow
	{
		get
		{
			int bucket = AgeDays / Math.Max( 1, newAttractionDecayDays );
			return Attraction + (bucket < newBonus.Length ? newBonus[bucket] : 0);
		}
	}

	/// <summary>The park's attraction points, as the arrival code adds them up (<c>FUN_004c8240</c>): every kind of ride
	/// and sideshow counts once (copies of one ride share its points), at its <see cref="AttractionNow"/> — the
	/// oldest copy's age decides the new-ride bonus. Broken rides and features don't count.</summary>
	public static float ParkAttractionPoints()
		=> Entity.All.OfType<Ride>().Where( r => !r.IsFeature && !r.IsBroken )
			.GroupBy( r => r.Archive, StringComparer.OrdinalIgnoreCase )
			.Sum( g => (float)g.MaxBy( r => r.AgeDays )!.AttractionNow );

	/// <summary>What a peep pays to board, in coins. Rides are free in Theme Park World — the original's ride window
	/// has no price, only shops ("Sale price") and sideshows ("Price of game") charge, and the gate fee is what a
	/// park's rides are worth (<see cref="EntryFeeOpinion"/>) — so this is 0 except for sideshows (player-settable).</summary>
	public float TicketPrice { get; set; } = 0f;

	/// <summary>The ride's running cost per second (its money sink), scaled by how many it seats.</summary>
	public float UpkeepPerSecond => Capacity * 0.1f;

	/// <summary>The footprint this ride occupies on the placement grid (set when placed) — used to
	/// select it by clicking a covered tile.</summary>
	public int TileX { get; set; }
	public int TileY { get; set; }
	public int TileW { get; set; }
	public int TileH { get; set; }

	/// <summary>True if the grid tile (tx,ty) is within this ride's footprint.</summary>
	public bool Covers( int tx, int ty ) => tx >= TileX && tx < TileX + TileW && ty >= TileY && ty < TileY + TileH;

	/// <summary>This ride's boarding queue (set when the queue is created), and its live rider count —
	/// used by the coaster train to carry visible riders and run only while occupied.</summary>
	public RideQueue? Queue { get; internal set; }
	public int Riders => Queue?.Riders ?? 0;

	// Reliability & breakdown (T-032/T-039): reliability wears down while the ride carries riders; at 0 it
	// breaks down — stops boarding + running until a mechanic repairs it. The rate follows the current level's
	// authored Upgrades[n].WearRate (out of 10: 5 as built, 3 and 2 once upgraded): a 5 ride breaks after ~250 s of
	// occupied running (~2½ in-game years), a fully upgraded one after ~625 s. It was a flat 0.04/s "for visible
	// dev breakdowns" — every ride broke after 25 s of use and one mechanic couldn't keep up, so most rides stood
	// broken beside their lines and a 250-visitor park managed ~1 ride per visitor in 2½ minutes.
	private const float WearPerRatePoint = 0.0008f; // reliability lost per second of use per WearRate point
	internal float WearPerSecond => WearPerRatePoint * (upgrades.Count > 0 ? upgrades[Math.Min( UpgradeLevel, upgrades.Count - 1 )].WearRate : DefaultWearRates[0]);

	/// <summary>Mechanical condition, 1 = perfect, 0 = broken down.</summary>
	public float Reliability { get; private set; } = 1f;

	/// <summary>True while the ride is broken down — it stops boarding + running until repaired.</summary>
	public bool IsBroken { get; private set; }

	/// <summary>Park-wide breakdown / repair tallies (diagnostics + HUD).</summary>
	public static int Breakdowns { get; private set; }
	public static int Repairs { get; private set; }

	/// <summary>Zero the park-wide breakdown/repair tallies for a fresh park (level reload, T-062).</summary>
	public static void ResetStats() { Breakdowns = 0; Repairs = 0; Features.Clear(); }

	// ---- Features (scenery: trees, statues, fountains, cameras, staff rooms — levels/<theme>/features) ----

	/// <summary>The park's placed features (kept so the per-peep security / per-day staff-room checks stay cheap).</summary>
	public static readonly List<Ride> Features = new();

	/// <summary>A feature (scenery placed from the theme's <c>features/</c> folder): nobody rides it, it has no queue,
	/// upkeep or wear and can't be upgraded; it decorates (<see cref="Attraction"/> cheers visitors close by), a
	/// security camera (<c>UsageInfo.ProvidesSecurity</c>) deters vandals like a guard, and a staff room
	/// (<c>UsageInfo.ChillsYouOut</c>) is where tired staff rest.</summary>
	public bool IsFeature { get; private set; }

	/// <summary>The object's <c>Info.Id</c> (1180 = Minecart…), what challenges name rides by (T-054).</summary>
	public int InfoId { get; private set; }

	/// <summary>How many visitors have ridden this ride (challenge "get N peeps onto X").</summary>
	public int TimesRidden { get; internal set; }

	/// <summary>A security camera (<c>UsageInfo.ProvidesSecurity</c>).</summary>
	public bool ProvidesSecurity { get; private set; }

	/// <summary>A staff room (<c>UsageInfo.ChillsYouOut</c>).</summary>
	public bool ChillsYouOut { get; private set; }

	/// <summary>Whether the park has a staff room to rest in.</summary>
	public static bool AnyStaffRoom => Features.Any( f => f.ChillsYouOut );

	/// <summary>True if a security camera covers the point (RegionFX "Security Camera": 5 cells).</summary>
	public static bool CameraNear( Vector3 p, float radius = CameraRadius )
	{
		float r2 = radius * radius;
		foreach ( var f in Features )
		{
			if ( !f.ProvidesSecurity )
				continue;
			float dx = f.Position.X - p.X, dy = f.Position.Y - p.Y;
			if ( dx * dx + dy * dy <= r2 )
				return true;
		}
		return false;
	}

	private const float CameraRadius = 50f;         // RegionFX[4] (Security Camera) radius 5 cells × 10 units
	private const float FeatureCheerRadius = 30f;   // a feature brightens the 3 cells around it
	private const float FeatureCheerPeriod = 1f;    // seconds between a feature's cheer passes
	private float featureCheerTimer;

	// Scenery lifts the mood of visitors near it: Info.AttractionValue (Features.sam 10) ÷ 5 happiness a second
	// (an interpretation: the original's beauty effect isn't reversed).
	private void CheerNearbyVisitors()
	{
		if ( !PeriodicDue( ref featureCheerTimer, Time.Delta, FeatureCheerPeriod ) )
			return;
		float lift = Attraction / 5f * FeatureCheerPeriod;
		float r2 = FeatureCheerRadius * FeatureCheerRadius;
		foreach ( var e in Entity.All )
		{
			if ( e is not Peep peep )
				continue;
			float dx = peep.Position.X - Position.X, dy = peep.Position.Y - Position.Y;
			if ( dx * dx + dy * dy <= r2 )
				peep.Cheer( lift );
		}
	}

	// Breakdown feedback (T-039): while broken, the ride sparks + zaps on this cadence so the player sees +
	// hears the fault (and where it is) instead of the ride just silently stopping.
	private const float BreakdownFxPeriod = 1.8f; // seconds between spark bursts
	private float breakdownFxTimer;

	private void BreakDown()
	{
		if ( IsBroken )
			return;
		IsBroken = true;
		Reliability = 0f;
		Breakdowns++;
		SetActive( false );          // halt the ride animation/cycle
		breakdownFxTimer = 0f;       // spark immediately on the next tick
		Log.Info( $"[ride] {Name} broke down (total {Breakdowns})" );
	}

	/// <summary>Countdown a periodic effect timer: subtract <paramref name="dt"/>, and when it reaches zero
	/// fire (returning true) and reload it to <paramref name="period"/>. Pure (unit-tested).</summary>
	internal static bool PeriodicDue( ref float timer, float dt, float period )
	{
		timer -= dt;
		if ( timer > 0f )
			return false;
		timer = period;
		return true;
	}

	/// <summary>Service a running ride (a mechanic called to it from the ride window): its wear is made good.</summary>
	public void Service()
	{
		if ( IsBroken )
		{
			Repair();
			return;
		}
		Reliability = 1f;
		engine.SpawnParticleEffect( 51 );
		Log.Info( $"[ride] {Name} serviced" );
	}

	/// <summary>Repair a broken-down ride (a mechanic, see <see cref="Staff"/>): restore it and play the
	/// repair particle effect at the ride.</summary>
	public void Repair()
	{
		if ( !IsBroken )
			return;
		IsBroken = false;
		Reliability = 1f;
		Repairs++;
		engine.SpawnParticleEffect( 51 ); // P_EFFECT_Repair (T-019 .PLB / T-007)
		Log.Info( $"[ride] {Name} repaired (total {Repairs})" );
	}

	/// <summary>The coaster train running this ride's player-laid track, if any (set by
	/// <see cref="CoasterTrack"/>). A boarding peep rides it in view instead of being hidden (T-045 3b).</summary>
	public CoasterTrain? Train { get; internal set; }

	/// <summary>The coaster track laid for this ride, if any (set by <see cref="CoasterTrack"/>) — torn
	/// down with the ride on sell/demolish.</summary>
	public CoasterTrack? Track { get; internal set; }

	/// <summary>A go-kart / water ride's piece-built track (null until laid, and for other rides).</summary>
	public TileTrack? TileTrack { get; internal set; }

	/// <summary>A generic moving car for "car" rides (set by the placement code) — torn down with the ride.</summary>
	public RideVehicle? Vehicle { get; internal set; }

	/// <summary>How many car/seat nodes the ride's authored model declares (object 0x80 + car 0x100 nodes
	/// in its node graph — e.g. Bird's nine per-seat nodes). Drives the visible seat/car count on the
	/// <see cref="RideVehicle"/> instead of a fixed guess (T-048). 0 when the model carries no node graph.</summary>
	public int CarNodeCount { get; private set; }

	/// <summary>Resolves this ride's node ids to world positions (T-048/T-047): car/seat nodes follow the
	/// <see cref="RideVehicle"/> path, other nodes sit at a footprint-derived layout. EVENT effects and
	/// REPAIREFFECT/SPARK spawn at the addressed node via this instead of the ride centre. Built when the
	/// model loads (empty until then); placed by Level.SpawnRideAt.</summary>
	public RideNodePositions NodeField { get; private set; } = new( null );

	/// <summary>True if this is a car ride — its script drives cars via the <c>TOUR</c>/<c>BUMP</c> opcodes
	/// (tour rides, go-karts, water rides, bumpers). Such a ride gets a visible <see cref="RideVehicle"/>.</summary>
	public bool IsCarRide => VM != null && VM.Instructions.Any( i => i.opcode is Opcode.TOUR or Opcode.BUMP );

	/// <summary>True if this is a <b>bumper/dodgem</b> ride — its script drives the cars via the <c>BUMP</c>
	/// opcode. Its <see cref="RideVehicle"/> runs the arena collision sim (<see cref="CarSim"/>) instead of
	/// the tour/kart circuit loop (T-048 / docs/08).</summary>
	public bool IsBumperRide => VM != null && VM.Instructions.Any( i => i.opcode is Opcode.BUMP );

	/// <summary>True if this is a <b>sideshow</b> — a small stall/game (coconut shy, puzzle, etc.) loaded from
	/// the level's <c>sideshow/</c> folder. Peeps "play" it like a quick ride, but it pays out via the takings
	/// model (<see cref="SideshowEconomy"/>) — pay-to-play with a chance of winning a prize — rather than a flat
	/// ride ticket (T-058).</summary>
	public bool IsSideshow { get; private set; }

	/// <summary>Authored sideshow economics (T-058), from the stall's <c>UsageInfo</c> fields; only meaningful
	/// when <see cref="IsSideshow"/>.</summary>
	public int SideshowPrice { get; private set; }
	public int SideshowCostOfGoods { get; private set; }
	public int SideshowChanceOfLoosing { get; private set; }
	/// <summary>A firework over the ride (P_EFFECT_FW1explosion) — the park-wide golden-ticket celebration (T-055).</summary>
	public void Celebrate() => engine.SpawnParticleEffect( 60 );

	private int sideshowWinEffect = 80; // Info.CreateParticleEffect — the burst when a peep wins (T-058)
	private readonly Random sideshowRng = new();

	/// <summary>True if this attraction is under cover (<c>UsageInfo.ISIndoors</c>) — peeps shelter here from
	/// rain/snow, so it draws extra interest in bad weather (T-056/T-058). Most sideshows are indoors.</summary>
	public bool IsIndoors { get; private set; }

	/// <summary>Run one sideshow play (T-058): roll win/lose against the authored odds and return the park's net
	/// takings + whether the peep won. The peep-play path calls this; the pure maths live in
	/// <see cref="SideshowEconomy"/>.</summary>
	public (float Net, bool Won) PlaySideshow()
	{
		// The player's price (TicketPrice, what the visitor paid), not the authored one: a re-priced game used to
		// take the visitor's new price but bank the old one.
		var result = SideshowEconomy.Play( (int)MathF.Round( TicketPrice ), SideshowCostOfGoods, SideshowChanceOfLoosing, sideshowRng.NextDouble() );
		if ( result.Won )
		{
			Log.Info( $"[sideshow] {Name}: a peep won! burst effect {sideshowWinEffect}" );
			engine.SpawnParticleEffect( sideshowWinEffect ); // a celebratory burst over the stall (authored code)
		}
		return result;
	}

	/// <summary>What the player paid to build this ride — used to compute the sell refund (T-041).</summary>
	public float BuildCost { get; set; }

	/// <summary>Fraction of <see cref="BuildCost"/> refunded when the ride is sold/demolished.</summary>
	public const float SellRefundFraction = 0.5f;

	/// <summary>Extra entities this ride spawned outside the engine (entrance/exit markers, queue-path
	/// quads) so they can be removed on sell/demolish. Populated by the placement code.</summary>
	public List<ModelEntity> OwnedEntities { get; } = new();

	/// <summary>The grid cells this ride's queue path reserved — freed on sell/demolish (T-041).</summary>
	public List<(int X, int Y)> QueuePathCells { get; } = new();

	/// <summary>Tear the ride out of the world: stop its VM, despawn its engine objects + light/particle
	/// proxies, its markers + queue quads, its coaster track/train, and unregister it. Grid cells, the
	/// <see cref="RideQueue"/> and the refund are handled by the caller (it owns the grid + queue list).</summary>
	public void Despawn()
	{
		if ( VM != null )
			VM.IsRunning = false;
		ResearchQueue.Dequeue( this ); // sold/demolished mid-research → drop out of the queue (T-044)
		Features.Remove( this );
		engine.Despawn();
		Track?.Despawn();           // coaster: pylons + ribbon + train (no-op if none)
		TileTrack?.Despawn();       // kart / water track pieces
		Vehicle?.Despawn();         // car rides: the moving wagon (no-op if none)
		foreach ( var e in OwnedEntities )
			Entity.All.Remove( e );
		OwnedEntities.Clear();
		Entity.All.Remove( this );
	}

	/// <summary>Run (true) or idle (false) the ride's animation — driven by occupancy (see RideQueue / Peep).</summary>
	public void SetActive( bool active ) => engine.SetActive( active );

	/// <summary>Begin / end the sustained rider scream — the coaster train raises it while it carries
	/// riders (the VM-driven rides scream from their own scripts instead). See T-045 3b / T-037.</summary>
	public void StartRiderScream() => engine.StartScream( 0, 70 );
	public void StopRiderScream() => engine.StopScream();

	private readonly RideEngine engine = new();

	// The engine places the riders' screams at the moving train / car (T-047).
	private void BindRiderPosition() => engine.RiderPosition = () => Train?.LeadPosition ?? Vehicle?.LeadPosition;

	/// <summary>The ride's WAD path (e.g. <c>levels/jungle/rides/coaster1</c>) — used to load extra
	/// assets like coaster track textures (T-045).</summary>
	public string Archive { get; }

	/// <summary>Placement orientation in 90° clockwise steps (0–3) — rotates the footprint + mesh (T-041).</summary>
	public int Rotation { get; }

	public Ride( string rideArchive, Vector3 position, int rotation = 0 )
	{
		Position = position;
		Archive = rideArchive;
		builtDay = GameClock.Current?.TotalDays ?? 0; // ride-age origin for the "new ride" draw (T-060)
		Rotation = ((rotation % 4) + 4) % 4;
		// Sideshows live in the level's sideshow/ folder; detected by path so it holds even if the .sam is absent.
		// Economy defaults match the shared jungle SideShow.sam, overridden below by the stall's own UsageInfo.
		IsSideshow = rideArchive.Contains( "sideshow", StringComparison.OrdinalIgnoreCase );
		IsFeature = rideArchive.Contains( "/features/", StringComparison.OrdinalIgnoreCase );
		if ( IsFeature )
			Features.Add( this );
		(SideshowPrice, SideshowCostOfGoods, SideshowChanceOfLoosing) = (10, 30, 70);
		var rideName = Path.GetFileNameWithoutExtension( rideArchive );
		Shape = RideShape.Load( rideArchive, rideName ).Rotated( Rotation );

		// Script (the VFS resolves the path into the .wad; matching is case-insensitive — T-014).
		VM = new RideVM( FileSystem.OpenRead( $"{rideArchive}/{rideName}.rse" ) );
		VM.Engine = engine;
		engine.Archive = rideArchive; // its own sound bank, if it ships one (EVENT type 10)
		BindRiderPosition();

		// SPAWNCHILD loads sibling child scripts from the same WAD, sharing this ride's engine.
		VM.ChildLoader = name =>
		{
			try
			{
				return new RideVM( FileSystem.OpenRead( $"{rideArchive}/{name}.rse" ) ) { Engine = VM.Engine };
			}
			catch ( Exception e )
			{
				Log.Warning( $"[ride] child script '{name}' not found: {e.Message}" );
				return null;
			}
		};

		try
		{
			var settings = new SettingsFile( FileSystem.OpenRead( $"{rideArchive}/{rideName}.sam" ) );
			var rideTitle = settings.Entries.Where( x => x.Key == "Info.Name" ).Select( x => x.Value ).FirstOrDefault();
			Name = string.IsNullOrEmpty( rideTitle ) ? rideName : ObjectNames.Localize( rideTitle );

			// Sub-tile positions within the entrance/exit cells (where peeps stand / appear), default centre.
			EntryStandPos = (ReadFloat( settings, "UsageInfo.EntryCellStandPosX", 0.5f ), ReadFloat( settings, "UsageInfo.EntryCellStandPosY", 0.5f ));
			ExitAppearPos = (ReadFloat( settings, "UsageInfo.ExitCellAppearPosX", 0.5f ), ReadFloat( settings, "UsageInfo.ExitCellAppearPosY", 0.5f ));
			maxCapacity = Math.Max( 1, ReadInt( settings, "UsageInfo.MaxCapacity", 8 ) );
			DurationUnit = ReadInt( settings, "Info.DurationUnit", 0 );

			// Upgrade table: Upgrades[0] = as built, [1..] = researchable capacity upgrades (T-044).
			for ( int i = 0; ; i++ )
			{
				int cap = ReadInt( settings, $"Upgrades[{i}].InitCapacity", -1 );
				if ( cap < 0 )
					break;
				upgrades.Add( new UpgradeLevelInfo( cap,
					ReadInt( settings, $"Upgrades[{i}].CostOfResearch", 0 ),
					ReadInt( settings, $"Upgrades[{i}].CostOfUpgrade", 0 ),
					ReadFloat( settings, $"Upgrades[{i}].WearRate", DefaultWearRates[Math.Min( i, DefaultWearRates.Length - 1 )] ) ) );
			}
			Excitement = Math.Max( 1, ReadInt( settings, "UsageInfo.ExcitementLevel", 50 ) );
			InfoId = ReadInt( settings, "Info.Id", 0 );
			Attraction = ReadInt( settings, "Info.AttractionValue", IsFeature ? 10 : 25 ); // Features.sam: 10
			for ( int b = 0; b < newBonus.Length; b++ )
				newBonus[b] = ReadInt( settings, $"Attraction[{b}].NewBonus", newBonus[b] );
			newAttractionDecayDays = ReadInt( settings, "Info.NewAttractionDecayTime", newAttractionDecayDays );
			ProvidesSecurity = IsFeature && ReadInt( settings, "UsageInfo.ProvidesSecurity", 0 ) != 0;
			ChillsYouOut = IsFeature && ReadInt( settings, "UsageInfo.ChillsYouOut", 0 ) != 0;
			IsIndoors = ReadInt( settings, "UsageInfo.ISIndoors", 0 ) != 0; // under cover → shelter in bad weather

			// Sideshow takings (T-058): a stall is pay-to-play with a chance of winning a prize; load its
			// authored odds and let the play price drive the ticket/price model.
			if ( IsSideshow )
			{
				SideshowPrice = Math.Max( 1, ReadInt( settings, "UsageInfo.InitPricePerUse", SideshowPrice ) );
				SideshowCostOfGoods = Math.Max( 0, ReadInt( settings, "UsageInfo.InitCostOfGoods", SideshowCostOfGoods ) );
				SideshowChanceOfLoosing = Math.Clamp( ReadInt( settings, "UsageInfo.InitChanceOfLoosing", SideshowChanceOfLoosing ), 0, 100 );
				sideshowWinEffect = ReadInt( settings, "Info.CreateParticleEffect", sideshowWinEffect );
				TicketPrice = SideshowPrice;
			}

			Log.Info( $"[ride] loaded '{Name}' from {rideArchive} (footprint {Shape.Width}x{Shape.Height}, entrance {Shape.Entrance?.ToString() ?? "none"}, exit {Shape.Exit?.ToString() ?? "none"})" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] settings unavailable: {e.Message}" );
		}

		// The in-world model is the exact base name (`<name>.md2`). WADs also ship `P<name>.md2`
		// preview models for the build menu (see docs/08-ghidra-animation.md) — loading by exact base
		// name keeps those out of the park. There is no separate LOD model set (the original has no
		// distance-based model swap).
		LoadModel( $"{rideArchive}/{rideName}.md2", rideArchive );

		// Discover the ride's real animation channels from the WAD (see docs/08-ghidra-animation.md):
		// the original names keyframe files <base><letter>[<n>].md2, letter = first letter of the
		// animation name. We probe each ScriptDefs.Animations channel so the engine animates only what
		// the ride actually ships, and knows each channel's frame count.
		var channels = DiscoverAnimChannels( rideArchive, rideName );
		engine.SetAnimChannels( channels );
		LoadKeyframes( rideArchive, rideName, channels );
		engine.StartBestBodyAnim();

		// Ride duration: one full pass of the ride's running animation is the authoritative, ride-specific
		// length we have (monkey ~11 s, totem ~14 s). Fall back to Info.DurationUnit (a 0–3 band; the exact
		// band→seconds table lives in TP.EXE and isn't RE'd yet — ~4 s/unit matches the decoded loop lengths)
		// then a flat default for rides whose loop has no decoded keyframes.
		RideDuration = engine.BodyLoopDuration is float loop && loop > 0 ? loop
			: DurationUnit > 0 ? DurationUnit * 4f
			: 5f;
		Log.Info( $"[ride] '{Name}' duration {RideDuration:0.0}s (DurationUnit {DurationUnit}), capacity {Capacity}" );

		// Open the ride and tell its script the capacity. A ride starts CLOSED in the VM (VAR_RIDECLOSED
		// = 1); the engine is what opens it. The script's load loop then polls VAR_LETMEON — the "a peep
		// wants on" signal the game sets — to take a rider, run, and (for thrill rides) scream. Wiring
		// our RideQueue → VAR_LETMEON (NotifyBoarding) is what finally drives that authentic run sequence.
		// RE'd from monkey.rse (TEST VAR_RIDECLOSED → TEST VAR_LETMEON → ADD VAR_ONRIDE → STARTSCREAM).
		VM.Variables[(int)RideVariables.VAR_RIDECLOSED] = 0;
		VM.Variables[(int)RideVariables.VAR_CAPACITY] = Capacity;

		VM.IsRunning = true;
	}

	/// <summary>
	/// Tell the ride script a peep is boarding by raising <c>VAR_LETMEON</c> — the per-ride "a peep wants
	/// on" flag its load loop polls (it consumes the flag, increments its on-ride count, and runs the
	/// ride). This bridges our <see cref="RideQueue"/> occupancy to the VM's own rider model so scripted
	/// run sequences (animations, scream, …) actually fire. See T-032.
	/// </summary>
	public void NotifyBoarding()
	{
		if ( VM != null )
			VM.Variables[(int)RideVariables.VAR_LETMEON] = 1;
	}

	// Loads the decoded keyframe tracks for each animation channel and hands them to the engine, so a
	// channel with real animation data plays its tracks (rotation, …) instead of the placeholder bob.
	// A channel's animation lives in its frame file(s) (<base><c>.md2 or <base><c>1.md2 … — see
	// docs/08-ghidra-animation.md). A numbered channel's files each animate different surfaces of the
	// ride (e.g. totem: m1→part 0/11, m2→part 14, …), so all are loaded and their surfaces merged —
	// otherwise only the first file's parts would move. Non-fatal: failures leave that channel on the bob.
	private void LoadKeyframes( string rideArchive, string rideName, Dictionary<int, int> channels )
	{
		foreach ( var (anim, frames) in channels )
		{
			var c = RideEngine.ChannelLetter( (ScriptDefs.Animations)anim );

			RideKeyframeFile? merged = null;
			if ( frames > 1 )
			{
				for ( int n = 1; n <= frames; n++ )
				{
					var kf = TryLoadKeyframe( $"{rideArchive}/{rideName}{c}{n}.md2", anim );
					if ( kf == null || kf.Surfaces.Count == 0 )
						continue;
					if ( merged == null )
						merged = kf;
					else
						merged.Merge( kf );
				}
			}
			else
			{
				merged = TryLoadKeyframe( $"{rideArchive}/{rideName}{c}.md2", anim );
			}

			if ( merged != null && merged.Surfaces.Count > 0 )
			{
				engine.SetChannelKeyframes( anim, merged );
				Log.Info( $"[ride] keyframes for {(ScriptDefs.Animations)anim}: {merged.Surfaces.Count} animated surface(s), duration {merged.Duration}" );
			}
		}
	}

	private static RideKeyframeFile? TryLoadKeyframe( string rel, int anim )
	{
		try
		{
			using var s = FileSystem.OpenRead( rel );
			return s == null ? null : new RideKeyframeFile( s );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] keyframes for {(ScriptDefs.Animations)anim} ({rel}) failed: {e.Message}" );
			return null;
		}
	}

	private static float ReadFloat( SettingsFile settings, string key, float fallback ) =>
		float.TryParse( settings[key], System.Globalization.CultureInfo.InvariantCulture, out var v ) ? v : fallback;

	private static int ReadInt( SettingsFile settings, string key, int fallback ) =>
		int.TryParse( settings[key], out var v ) ? v : fallback;

	// Probes the WAD for each animation channel's keyframe files and returns anim id -> frame count.
	// A channel is a numbered sequence (<base><c>1.md2, <base><c>2.md2, …) or a single frame
	// (<base><c>.md2); a channel with no files is simply absent (no animation for that state).
	private static Dictionary<int, int> DiscoverAnimChannels( string rideArchive, string rideName )
	{
		// A file is present only if OpenRead yields a non-null stream: a missing entry inside a
		// *mounted* WAD returns null (not an exception), so a bare try/catch would treat every
		// missing frame as present and loop forever. See WadArchive.OpenFile.
		bool Exists( string rel )
		{
			try { using var s = FileSystem.OpenRead( rel ); return s != null; }
			catch { return false; }
		}

		var map = new Dictionary<int, int>();
		foreach ( ScriptDefs.Animations anim in Enum.GetValues<ScriptDefs.Animations>() )
		{
			var c = RideEngine.ChannelLetter( anim );
			if ( c == '\0' )
				continue;

			// Numbered sequence first (Main is e.g. <base>m1.md2 … <base>m7.md2).
			int frames = 0;
			while ( Exists( $"{rideArchive}/{rideName}{c}{frames + 1}.md2" ) )
				frames++;

			// Otherwise a single unnumbered frame (<base><c>.md2).
			if ( frames == 0 && Exists( $"{rideArchive}/{rideName}{c}.md2" ) )
				frames = 1;

			if ( frames > 0 )
				map[(int)anim] = frames;
		}

		return map;
	}

	// Loads the ride's main model and spawns a ModelEntity per mesh (the LobbyIsland pattern). Ride
	// textures live in the WAD under textures/; missing ones fall back to Texture.Missing. Any failure
	// is non-fatal — the VM still runs (so the sound/engine proof holds) even if geometry doesn't load.
	private void LoadModel( string md2Path, string rideArchive )
	{
		try
		{
			var parts = BuildMeshEntities( md2Path, rideArchive );
			// Register the meshes as the ride body so the engine can animate them (it starts a looping
			// idle so the model visibly moves and is easy to pick out).
			engine.RegisterBody( parts );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] model load failed ({md2Path}): {e.Message}" );
		}
	}

	// Builds a ModelEntity per mesh of an MD2 at this ride's position (the LobbyIsland pattern). Ride
	// textures live in the WAD under textures/; missing ones fall back to Texture.Missing.
	/// <summary>Load a ride mesh texture by material name, trying the folders a ride WAD actually uses. TPW ride
	/// wads keep their textures under <c>stexture/</c> (the code long looked only in <c>textures/</c>, so most
	/// ride bodies rendered as the magenta missing-texture placeholder); <c>gtexture/</c> holds the coaster
	/// track art. Falls back to <see cref="Texture.Missing"/> when none resolve. Shared with the car meshes.</summary>
	internal static Texture LoadRideTexture( string archive, string materialName )
	{
		foreach ( var folder in new[] { "stexture", "textures", "gtexture" } )
		{
			// PinkChromaKey: TPW ride textures use magenta (255,0,255) as the cutout colour (fences, foliage,
			// decorative holes); keying it to alpha 0 lets the shader discard it instead of drawing solid magenta.
			// Sampler stays AnisotropicRepeat (the Texture default), so tiling still works.
			try { return new Texture( $"{archive}/{folder}/{materialName}.wct", TextureFlags.PinkChromaKey ); }
			catch { /* try the next folder */ }
		}

		// Shared textures (the base-plate floor tiles jfl_*, grass, signs, doorways…) live once per level in
		// levels/<theme>/sharetex.wad, not in each ride's WAD. archive = levels/<theme>/<rides|sideshow|…>/<name>.
		var level = Path.GetDirectoryName( Path.GetDirectoryName( archive ) );
		if ( !string.IsNullOrEmpty( level ) )
		{
			try { return new Texture( $"{level}/sharetex/{materialName}.wct", TextureFlags.PinkChromaKey ); }
			catch { /* not shared either */ }
		}
		return Texture.Missing;
	}

	private List<ModelEntity> BuildMeshEntities( string md2Path, string rideArchive )
	{
		var modelFile = new ModelFile( md2Path );
		// The authored car/seat nodes (object 0x80 + car 0x100) — their count drives how many riders the
		// RideVehicle shows (T-048). Their world positions are runtime sim output, not file data, so only
		// the count is used here.
		CarNodeCount = modelFile.Nodes.Count( n => n.IsObject || n.IsCar );
		// Runtime node→world-position resolver (T-048/T-047): EVENT/SPARK effects + the vehicle read it.
		NodeField = new RideNodePositions( modelFile.Nodes );
		engine.NodeField = NodeField;
		// Size the head-slot table to the model's head-node count (the original probes type-0x80 at spawn).
		VM?.SetHeadCapacity( NodeField.ObjectNodeIds.Count );
		Log.Info( $"[ride] model {md2Path}: {modelFile.Meshes.Count} mesh(es), {modelFile.Nodes.Count} node(s) ({CarNodeCount} car/seat)" );

		// Decode the ride's textures in parallel before the meshes upload them one by one (T-030).
		Texture.PrefetchWct( modelFile.Meshes.SelectMany( m => m.Materials ).Select( m => m.Name )
			.Where( n => !RideSign.IsSignMaterial( n ) ).Distinct()
			.SelectMany( n => new[] { $"{rideArchive}/stexture/{n}.wct", $"{rideArchive}/textures/{n}.wct" } ) );

		var parts = new List<ModelEntity>();
		Texture? sign = null;
		foreach ( var mesh in modelFile.Meshes )
		{
			// One texture per material (any count — see MaterialSlots); LoadRideTexture is shared with the car meshes.
			// The placeholder sign textures (sign1/sign2) get the ride's own name painted on (RideSign).
			var textures = mesh.Materials.Select( m => RideSign.IsSignMaterial( m.Name )
				? (sign ??= RideSign.For( Name ))
				: LoadRideTexture( rideArchive, m.Name ) ).ToList();

			var vertices = new List<Vertex>();
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
			{
				vertices.Add( new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags
				} );
			}

			// A mesh with more than 16 materials (a fantasy ride's base plate has 25 floor tiles) is drawn as several
			// models. The first stays this mesh's animated part — the engine addresses parts by mesh index — and the
			// rest (static floor tiles in practice) are extra entities the ride owns.
			var models = MaterialSlots.BuildModels( [.. vertices], mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) );
			Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );

			// Placement rotation (T-041): spin the whole ride about its footprint centre (= Position) by
			// the orientation yaw — rotate each part's local offset and compose the yaw onto its rotation.
			// 90° CW per step (negative about world +Z, which is up here). RegisterBody captures this as the
			// rest pose, so the engine's animation composes on top of the placed orientation.
			// Ride models are authored corner-anchored: the footprint spans (0,0)..(w,h)·WorldTileSize in model space
			// (totem 3×4 → 30×40), while Position is the footprint centre — so shift by half the upright footprint
			// before rotating (T-072).
			int uprightW = Rotation % 2 == 1 ? Shape.Height : Shape.Width;
			int uprightH = Rotation % 2 == 1 ? Shape.Width : Shape.Height;
			var localOffset = new Vector3( pos.X, pos.Z, pos.Y )
				- new Vector3( uprightW * PlacementGrid.WorldTileSize / 2f, uprightH * PlacementGrid.WorldTileSize / 2f, 0 );
			var partRot = new Quaternion( rot.X, rot.Z, rot.Y, -rot.W );
			if ( Rotation != 0 )
			{
				// Rotate the horizontal offset (XY; Z is up) by -Rotation·90°, exact per quarter-turn.
				(float c, float s) = Rotation switch { 1 => (0f, -1f), 2 => (-1f, 0f), 3 => (0f, 1f), _ => (1f, 0f) };
				localOffset = new Vector3( localOffset.X * c - localOffset.Y * s, localOffset.X * s + localOffset.Y * c, localOffset.Z );
				var yaw = Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, -Rotation * MathF.PI / 2f );
				partRot = yaw * partRot;
			}

			for ( int m = 0; m < models.Count; m++ )
			{
				var entity = new ModelEntity
				{
					Model = models[m],
					Scale = new Vector3( scl.X, scl.Z, scl.Y ),
					Rotation = partRot,
					Position = localOffset + Position,
				};
				if ( m == 0 )
					parts.Add( entity );
				else
					OwnedEntities.Add( entity );
			}
		}
		return parts;
	}

	protected override void OnUpdate()
	{
		// Null-guarded: the Entity base ctor registers this in Entity.All before the ctor body runs,
		// so a ride that failed to load (VM never assigned) would otherwise crash the update loop.
		VM?.Update();
		engine.Update( Time.Now );

		// Mirror the VM's WALKON/ADDHEAD slot tables into the world at the ride's node positions (T-048):
		// gliding walk peeps between walk nodes, decorative heads at head nodes.
		if ( VM != null )
		{
			engine.SyncHeads( VM.HeadSlots );
			engine.SyncWalk( VM.WalkSlots, VM.GameTime );
		}

		// Scenery costs nothing to run and never wears; it brightens the visitors around it.
		if ( IsFeature )
		{
			CheerNearbyVisitors();
			return;
		}

		// Running the ride costs money over time (its upkeep drains the park balance).
		ParkFinances.Current?.PayUpkeep( UpkeepPerSecond * Time.Delta );

		// Carrying riders wears the ride down; at zero reliability it breaks (a mechanic must repair it).
		if ( !IsBroken && Riders > 0 )
		{
			Reliability -= WearPerSecond * Time.Delta;
			if ( Reliability <= 0f )
				BreakDown();
		}

		// While broken, periodically spark + zap at the ride so the fault is visible/audible until repaired.
		if ( IsBroken && PeriodicDue( ref breakdownFxTimer, Time.Delta, BreakdownFxPeriod ) )
			engine.PlayBreakdownEffect();

		// Researchers advance only the queue's active ride (one at a time park-wide), so the team concentrates
		// instead of every researching ride progressing in parallel (T-044).
		if ( ResearchQueue.Active == this )
			// (Half the scientists' time while new items are being researched too.)
			TickResearch( Time.Delta, Staff.ResearchPower * (ItemResearch.Current?.Busy == true ? 0.5f : 1f) );
	}
}
