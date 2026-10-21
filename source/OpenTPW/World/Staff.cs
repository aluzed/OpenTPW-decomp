namespace OpenTPW;

/// <summary>What a member of staff does as they roam the park.</summary>
public enum StaffRole
{
	/// <summary>Lifts the mood of nearby visitors.</summary>
	Entertainer,
	/// <summary>Seeks out and picks up dropped litter.</summary>
	Handyman,
	/// <summary>Patrols the park; visitors near a guard don't drop litter.</summary>
	Guard,
	/// <summary>Researches ride upgrades (see T-044); for now wanders and draws a wage.</summary>
	Researcher,
	/// <summary>Repairs broken-down rides (T-032).</summary>
	Mechanic,
}

/// <summary>
/// A bounded patrol area a member of staff is confined to (T-049): a world centre + radius, tested in the
/// XY plane (height is irrelevant on the park ground). A pure value so the containment maths is unit-tested
/// without standing up a full <see cref="Staff"/> (which needs terrain + sprite assets).
/// </summary>
public readonly record struct PatrolZone( Vector3 Center, float Radius )
{
	/// <summary>True if <paramref name="p"/> lies within the zone radius (XY distance).</summary>
	public bool Contains( Vector3 p )
	{
		float dx = p.X - Center.X, dy = p.Y - Center.Y;
		return dx * dx + dy * dy <= Radius * Radius;
	}
}

/// <summary>
/// A member of park staff: wanders the park (a camera-facing billboard in a role colour), draws a wage
/// every second, and does its job — an <see cref="StaffRole.Entertainer"/> lifts the mood of nearby
/// visitors (<see cref="Peep.Cheer"/>), a <see cref="StaffRole.Handyman"/> heads for the nearest litter
/// and picks it up (<see cref="Litter"/>). Guards (safety) follow.
/// </summary>
public sealed class Staff : ModelEntity
{
	// Wages: a monthly wage of PerGradeStaffConsts[grade].BaseWage × PerTypeStaffConsts[type].PayMultiplier (Standard.sam:
	// BaseWage 4 / 5 / 6 / 8 / 12, multipliers handyman 10, mechanic 30, entertainer 15, guard 20, researcher 35 — a
	// grade-1 handyman earns 50 a month, a grade-4 researcher 420), paid as it accrues over the original's ~3-minute
	// month (T-076). The staff window's figure is the "Monthly wage" (UITEXT 65).
	private const float AveragePayMultiplier = 22f;

	/// <summary><c>PerTypeStaffConsts[n].PayMultiplier</c> by role (Standard.sam order: handyman, mechanic, entertainer,
	/// guard, researcher).</summary>
	public static float[] PayMultipliers { get; set; } = { 10f, 30f, 15f, 20f, 35f };

	/// <summary>The monthly wage for a role at a grade (pure): <c>BaseWage[grade] × PayMultiplier[role]</c>.</summary>
	public static float MonthlyWage( StaffRole role, int grade )
		=> StaffGrades.Current.BaseWage[Math.Clamp( grade, 0, StaffGrades.MaxGrade )] * PayMultiplier( role );

	/// <summary>The wage per real second for a role at a grade: its <see cref="MonthlyWage"/> over the month.</summary>
	public static float WagePerSecond( StaffRole role, int grade ) => MonthlyWage( role, grade ) / GameClock.SecondsPerMonth;

	/// <summary>The wage per real second for a role at grade 1 (pure).</summary>
	public static float WagePerSecond( StaffRole role ) => WagePerSecond( role, 1 );

	private static float PayMultiplier( StaffRole role )
	{
		int i = role switch
		{
			StaffRole.Handyman => 0, StaffRole.Mechanic => 1, StaffRole.Entertainer => 2,
			StaffRole.Guard => 3, _ => 4,
		};
		return i < PayMultipliers.Length ? PayMultipliers[i] : AveragePayMultiplier;
	}
	private const float CellSize = 10f;        // one map cell (the placement grid's tile) in world units
	private const float ReachDistance = 3f;    // how close counts as "arrived" / "picked up"
	private const float WalkFps = 8f;          // walk-cycle frames per second
	private const float GuardDeterRadius = 30f; // visitors within this of a guard don't litter: RegionFX "Guard" radius 3 cells × 10 units

	// One shared billboard model per role (built once), so the crowd reads staff roles by colour.
	private static readonly Dictionary<StaffRole, Model> roleModels = new();

	// Active guards, so peeps can cheaply check whether one is nearby (litter deterrence).
	private static readonly List<Staff> guards = new();

	/// <summary>How many researchers are currently employed (drives ride upgrade research, T-044).</summary>
	public static int ResearcherCount => Entity.All.Count( e => e is Staff s && s.Role == StaffRole.Researcher );

	/// <summary>The research speed of the researchers at work, in grade-1 researchers (<c>ResearchAbility</c>; none
	/// while they strike).</summary>
	public static float ResearchPower => StaffStrikes.Current?.IsStriking( StaffRole.Researcher ) == true ? 0f
		: Entity.All.OfType<Staff>().Where( s => s.Role == StaffRole.Researcher ).Sum( s => StaffGrades.Current.ResearchFactor( s.Grade ) );

	// ---- Grade, training, energy and morale (Standard.sam PerGradeStaffConsts / *ConstsPerGrade, see StaffGrades) ----

	/// <summary>The staff member's grade, 0–4 (rolled from the staff pool at hire, raised by training).</summary>
	public int Grade { get; private set; }

	/// <summary>Their name (the game's own name lists; set when hired from a candidate).</summary>
	public string PersonalName { get; set; } = "";

	/// <summary>Training towards the next grade, 0–<see cref="StaffGrades.PointsPerGrade"/> percent.</summary>
	public float TrainingPoints { get; private set; }

	/// <summary>Energy 0–100: spent working, recovered resting (<c>RecuperationRate</c>).</summary>
	public float Energy { get; private set; } = 100f;

	/// <summary>Happiness 0–100: worn down by working tired with nowhere to rest (<c>HappyHitCosNoRestArea</c>),
	/// recovered when rested (<c>HappinessRecuperationRate</c>). Tired or unhappy staff strike (<see cref="StaffStrikes"/>).</summary>
	public float Morale { get; private set; } = StartMorale;

	private const float StartMorale = 60f;
	private const float WorkDrainPerSecond = 1.2f; // energy a member of staff spends per second on a job
	private const float TiredBelow = 30f;          // tired enough that a day at work costs happiness
	private const float RestBelow = 10f, RestUntil = 60f;

	private bool working;   // on a job this frame (walking to it or doing it)
	private bool resting;   // exhausted: standing to recover until RestUntil
	private float idleUntil; // short rest after each job (IdleDuration)

	/// <summary>The cost of <paramref name="points"/> training percent at the current grade (0 at the top grade).</summary>
	public float TrainingCost( float points ) => points * StaffGrades.Current.CostPerTrainingPoint( Role, Grade );

	/// <summary>Whether training can still raise this member of staff.</summary>
	public bool CanTrain => Grade < StaffGrades.MaxGrade && StaffGrades.Current.CostPerTrainingPoint( Role, Grade ) > 0f;

	/// <summary>Add training percent (the caller pays <see cref="TrainingCost"/>); a full hundred raises the grade.</summary>
	public void Train( float points )
	{
		if ( !CanTrain )
			return;
		TrainingPoints += points;
		while ( TrainingPoints >= StaffGrades.PointsPerGrade && Grade < StaffGrades.MaxGrade )
		{
			TrainingPoints -= StaffGrades.PointsPerGrade;
			Grade++;
			Log.Info( $"[staff] {Role} trained to grade {Grade}" );
		}
		if ( Grade >= StaffGrades.MaxGrade )
			TrainingPoints = 0f;
	}

	/// <summary>Restore a saved member of staff's grade, training and condition (T-059).</summary>
	public void RestoreCondition( int grade, float training, float energy, float morale )
	{
		Grade = Math.Clamp( grade, 0, StaffGrades.MaxGrade );
		TrainingPoints = Math.Clamp( training, 0f, StaffGrades.PointsPerGrade );
		Energy = Math.Clamp( energy, 0f, 100f );
		Morale = Math.Clamp( morale, 0f, 100f );
	}

	/// <summary>A day passed: working tired with no rest area costs happiness, being rested restores it.</summary>
	public void OnNewDay()
	{
		var g = StaffGrades.Current;
		// A staff room (UsageInfo.ChillsYouOut) is the rest area HappyHitCosNoRestArea is about.
		Morale = Energy < TiredBelow && !Ride.AnyStaffRoom
			? MathF.Max( 0f, Morale - g.HappyHitNoRestArea * 2.5f )
			: MathF.Min( 100f, Morale + g.HappinessRecuperationRate[Math.Clamp( Grade, 0, StaffGrades.MaxGrade )] );
	}

	/// <summary>Every member of staff's daily condition update.</summary>
	public static void OnNewDayAll()
	{
		foreach ( var s in Entity.All.OfType<Staff>().ToList() )
			s.OnNewDay();
	}

	/// <summary>The month's strike consideration for every type of staff (from the clock's month boundary).</summary>
	public static void ConsiderStrikes()
	{
		if ( StaffStrikes.Current is not { } strikes || GameClock.Current is not { } clock )
			return;
		var staff = Entity.All.OfType<Staff>().ToList();
		foreach ( var role in StaffStrikes.Roles )
		{
			var of = staff.Where( s => s.Role == role ).ToList();
			bool grievance = of.Count > 0 && StaffStrikes.HasGrievance( of.Count, of.Average( s => s.Energy ), of.Average( s => s.Morale ) );
			strikes.ConsiderMonth( role, clock.TotalMonths, grievance );
		}
	}

	/// <summary>Where strikers gather (<c>FixedItemInfo.StrikeArea*</c>: "I'm going on strike! And I'm going to that
	/// bus stop!"), set by the level.</summary>
	public static (Vector3 Min, Vector3 Max)? StrikeArea { get; set; }
	private Vector3? picket;

	/// <summary>Drop the park-wide guard registry (T-062 reload): the entities themselves are cleared via
	/// <c>Entity.All</c>, but this static list would otherwise keep stale references across a level reload.</summary>
	public static void ResetAll() => guards.Clear();

	/// <summary>True if a patrolling guard is within <paramref name="radius"/> of the point, or a security camera
	/// covers it (<see cref="Ride.CameraNear"/>).</summary>
	public static bool GuardNear( Vector3 p, float radius = GuardDeterRadius )
	{
		if ( Ride.CameraNear( p ) )
			return true;
		float r2 = radius * radius;
		foreach ( var g in guards )
		{
			float dx = g.Position.X - p.X, dy = g.Position.Y - p.Y;
			if ( dx * dx + dy * dy <= r2 )
				return true;
		}
		return false;
	}

	private readonly StaffRole role;
	public StaffRole Role => role;
	private readonly ParkTerrain terrain;
	private Vector3 center; // wander/roam centre — moves when the player relocates this staffer (T-043)
	private readonly float roam;
	private readonly float speed;
	private Vector3 target;

	// Optional patrol zone (T-049): when set, the staff member wanders within it and only does its job
	// (guard deterrence / litter pickup / repairs) inside the zone. Null = free roam over the spawn area.
	private PatrolZone? zone;

	/// <summary>The assigned patrol zone, or null when the staff member roams freely.</summary>
	public PatrolZone? Zone => zone;

	/// <summary>The staff member's wander centre (its anchor, not the jittering live position) — captured by
	/// save/load so a reloaded staffer respawns where the player put them (T-059).</summary>
	public Vector3 WanderCenter => center;

	/// <summary>True when the player has bounded this staff member to a patrol zone.</summary>
	public bool HasPatrolZone => zone != null;

	/// <summary>Confine this staff member to a circular patrol zone and re-target inside it (T-049).</summary>
	public void SetPatrolZone( Vector3 patrolCenter, float radius )
	{
		zone = new PatrolZone( patrolCenter.WithZ( 0 ), MathF.Max( 1f, radius ) );
		target = PickWanderTarget();
	}

	/// <summary>Lift the patrol zone: the staff member returns to roaming the whole park.</summary>
	public void ClearPatrolZone() => zone = null;

	/// <summary>Move this staff member to <paramref name="pos"/> (T-043): re-anchor its wander centre there,
	/// teleport it onto the ground, re-target, and carry its patrol zone along (keeping the radius) so a zoned
	/// staffer relocates zone-and-all instead of immediately walking back.</summary>
	public void Relocate( Vector3 pos )
	{
		center = pos.WithZ( 0 );
		if ( zone is { } z )
			zone = z with { Center = center };
		Position = pos.WithZ( terrain.SampleHeight( pos.X, pos.Y ) );
		target = PickWanderTarget();
	}

	// The effective wander area: the patrol zone if one is set, else the spawn centre + roam radius.
	private (Vector3 Center, float Radius) WanderArea => zone is { } z ? (z.Center, z.Radius) : (center, roam);

	// Whether a world point is reachable for this staff member (always true when free-roaming).
	private bool InRange( Vector3 p ) => zone is not { } z || z.Contains( p );

	/// <summary>Dismiss this staff member: remove it (and its shadow) from the world and stop its wage.</summary>
	public void Fire()
	{
		if ( role == StaffRole.Guard )
			guards.Remove( this );
		Entity.All.Remove( shadow );
		Entity.All.Remove( this );
	}

	// Sprite animation (real sprite path only); falls back to the flat role billboard if unavailable.
	private readonly SpriteSheet? sheet;
	private readonly Model fallback;
	private readonly float spriteHeight;
	private int facing;
	private float walkPhase;
	private bool movedThisFrame;
	private Vector3 moveDir = new( 0, -1, 0 ); // last world travel direction (drives the camera-relative facing)

	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );
	private readonly ModelEntity shadow; // soft ground shadow under the staff member

	public Staff( StaffRole role, ParkTerrain terrain, Vector3 center, float roam )
	{
		this.role = role;
		this.terrain = terrain;
		this.center = center;
		this.roam = roam;

		if ( role == StaffRole.Guard )
			guards.Add( this );

		Grade = StaffGrades.Current.RollGrade( role, Random.Shared );
		PersonalName = StaffCandidates.RandomName( role );
		Energy = 70f + (float)Random.Shared.NextDouble() * 30f;

		sheet = RoleSprite( role );
		fallback = RoleModel( role );
		spriteHeight = 10f; // ~1 tile (was 16, sized for the old 16-unit grid — T-072)
		Model = sheet != null ? sheet.FrameModel( 0 ) : fallback;
		Scale = new Vector3( 3f, 1f, 7f ); // overridden per-frame on the sprite path
		speed = 10f + (float)Random.Shared.NextDouble() * 4f;
		target = PickWanderTarget();
		Position = target;
		shadow = new ModelEntity { Model = Peep.ShadowModel(), Scale = new Vector3( spriteHeight * 0.25f, spriteHeight * 0.25f, 1f ) };
		DropToGround();
	}

	protected override void OnUpdate()
	{
		shadow.Position = Position.WithZ( terrain.SampleHeight( Position.X, Position.Y ) + 0.12f );
		var grades = StaffGrades.Current;

		if ( StaffStrikes.Current?.IsStriking( role ) == true )
		{
			// On strike: no wage, no work — off to the picket by the bus stop, recovering there.
			picket ??= PicketSpot();
			if ( picket is { } spot )
				WalkToward( spot );
			else
				movedThisFrame = false;
			Energy = MathF.Min( 100f, Energy + grades.EnergyRecoveryPerSecond( Grade ) * Time.Delta );
		}
		else
		{
			picket = null;
			ParkFinances.Current?.PayWages( WagePerSecond( Role, Grade ) * Time.Delta );

			if ( Energy <= RestBelow )
				resting = true;
			if ( resting || Time.Now < idleUntil )
			{
				// Exhausted (or between jobs): stand and recover.
				movedThisFrame = false;
				Energy = MathF.Min( 100f, Energy + grades.EnergyRecoveryPerSecond( Grade ) * Time.Delta );
				if ( Energy >= RestUntil )
					resting = false;
			}
			else
			{
				working = false;
				switch ( role )
				{
					case StaffRole.Handyman: DoHandyman(); break;
					case StaffRole.Guard: DoGuard(); break;         // patrol toward trouble (unhappy peeps)
					case StaffRole.Mechanic: DoMechanic(); break;   // repair broken-down rides (T-032)
					case StaffRole.Researcher: working = true; WanderStep(); break; // research is off-screen (T-044)
					default: DoEntertainer(); break;
				}
				// A job spends energy; strolling between jobs recovers half the resting rate.
				Energy = working
					? MathF.Max( 0f, Energy - WorkDrainPerSecond * Time.Delta )
					: MathF.Min( 100f, Energy + grades.EnergyRecoveryPerSecond( Grade ) * 0.5f * Time.Delta );
			}
		}

		// Cylindrical billboard: yaw about world up so the quad faces the camera.
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );

		ApplySprite();
	}

	// A spot in the strike area, or null when the level has none.
	private static Vector3? PicketSpot()
	{
		if ( StrikeArea is not { } a )
			return null;
		float t = (float)Random.Shared.NextDouble(), u = (float)Random.Shared.NextDouble();
		return new Vector3( a.Min.X + (a.Max.X - a.Min.X) * t, a.Min.Y + (a.Max.Y - a.Min.Y) * u, 0 );
	}

	// A job is done: rest IdleDuration (shorter for better-trained staff) before the next.
	private void FinishJob() => idleUntil = Time.Now + StaffGrades.Current.IdleSeconds( Grade );

	private static SpriteSheet? RoleSprite( StaffRole role ) => role switch
	{
		StaffRole.Handyman => SpriteSheet.Load( "esprites/Generic/Handymen", "SPR_HA" ),
		StaffRole.Guard => SpriteSheet.Load( "esprites/Generic/Guards", "SPR_GU" ),
		StaffRole.Researcher => SpriteSheet.Load( "esprites/Generic/Researchers", "SPR_RE" ),
		StaffRole.Mechanic => SpriteSheet.Load( "esprites/Generic/Mechanics", Random.Shared.Next( 2 ) == 0 ? "SPR_FA" : "SPR_OM" ),
		_ => EntertainerSprite(),
	};

	// One of the active theme's three entertainer costumes (esprites/<Theme>/Entertainers: jungle SPR_DI/EX/…,
	// hallow SPR_DR/FK/…, fantasy SPR_FL/GN/LA, space SPR_MO/SM/…).
	private static SpriteSheet? EntertainerSprite()
	{
		var dir = $"esprites/{Souvenirs.ThemeDir( Level.Current?.Name ?? LevelTheme.Default )}/Entertainers";
		try
		{
			var names = FileSystem.GetFiles( dir ).Where( f => f.EndsWith( ".TPC", StringComparison.OrdinalIgnoreCase ) )
				.Select( Path.GetFileNameWithoutExtension ).ToArray();
			if ( names.Length > 0 )
				return SpriteSheet.Load( dir, names[Random.Shared.Next( names.Length )]! );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[staff] no entertainer sprites in {dir}: {e.Message}" );
		}
		return null;
	}

	// Picks the current sprite frame from the camera-relative facing + walk phase, at a uniform scale
	// (hotspot-anchored quad — feet planted, no per-frame pulsing).
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
			walkPhase = movedThisFrame ? walkPhase + WalkFps * Time.Delta : 0f;
			frame = a.Start + ( a.Count > 0 ? (int)walkPhase % a.Count : 0 );
		}
		Model = sheet.FrameModel( frame );
		float p2w = spriteHeight / sheet.RefHeight;
		Scale = new Vector3( p2w, 1f, p2w );
	}

	// Wander the park and lift the mood of every visitor within reach.
	private void DoEntertainer()
	{
		// Reach and cheer by grade: ActivationDistance cells, HappinessEffectOnCell happiness a second.
		var g = StaffGrades.Current;
		int grade = Math.Clamp( Grade, 0, StaffGrades.MaxGrade );
		float radius = g.EntertainerActivationDistance[grade] * CellSize;
		float lift = g.EntertainerHappinessEffect[grade] * Time.Delta;
		float r2 = radius * radius;
		bool audience = false;
		foreach ( var e in Entity.All )
		{
			if ( e is not Peep peep )
				continue;
			float dx = peep.Position.X - Position.X, dy = peep.Position.Y - Position.Y;
			if ( dx * dx + dy * dy <= r2 )
			{
				peep.Cheer( lift );
				audience = true;
			}
		}
		// Entertaining an audience is the job; with nobody around the entertainer just strolls.
		working = audience;
		if ( !audience )
			WanderStep();
		else
			movedThisFrame = false;
	}

	// Patrol toward the nearest unhappy (would-be vandal) peep so the guard's deterrence lands where the
	// trouble actually is; wander when the crowd is content. This is what makes guards measurably cut
	// vandalism (a lone guard standing still almost never coincides with a vandal). See T-039.
	private const float GuardSeekRange = 600f; // a guard notices trouble most of the way across the park
	private void DoGuard()
	{
		Peep? trouble = null;
		float bestD2 = GuardSeekRange * GuardSeekRange;
		foreach ( var e in Entity.All )
		{
			if ( e is not Peep { Unhappy: true } p )
				continue;
			if ( !InRange( p.Position ) ) // a zoned guard ignores trouble outside its patrol area (T-049)
				continue;
			float dx = p.Position.X - Position.X, dy = p.Position.Y - Position.Y;
			float d2 = dx * dx + dy * dy;
			if ( d2 < bestD2 )
			{
				bestD2 = d2;
				trouble = p;
			}
		}
		// Chase the trouble for WorkDuration (longer for better-trained guards), then give up on it for a while.
		if ( trouble != null && trouble != pursued )
		{
			pursued = trouble;
			pursuitUntil = Time.Now + StaffGrades.Current.PursuitSeconds( Grade );
		}
		if ( trouble != null && Time.Now < pursuitUntil )
		{
			working = true;
			WalkToward( trouble.Position );
		}
		else
		{
			if ( trouble != null && Time.Now >= pursuitUntil )
				FinishJob();
			pursued = null;
			WanderStep();
		}
	}

	private Peep? pursued;
	private float pursuitUntil;

	// Head for the nearest broken-down ride and repair it (over the grade's WorkDuration once there); wander otherwise.
	private Ride? repairTarget;
	private float repairTimer;
	/// <summary>Send the nearest mechanic who isn't on strike to service <paramref name="ride"/> (the ride window's
	/// call-mechanic button); false when there is none.</summary>
	public static bool CallMechanic( Ride ride )
	{
		if ( StaffStrikes.Current?.IsStriking( StaffRole.Mechanic ) == true )
			return false;
		var mechanic = Entity.All.OfType<Staff>().Where( s => s.Role == StaffRole.Mechanic && !s.called )
			.OrderBy( s => (s.Position - ride.Position).WithZ( 0 ).LengthSquared ).FirstOrDefault();
		if ( mechanic == null )
			return false;
		mechanic.repairTarget = ride;
		mechanic.called = true;
		mechanic.repairTimer = StaffGrades.Current.RepairSeconds( mechanic.Grade );
		return true;
	}

	private bool called; // sent to a ride by the player: service it even if it's running

	/// <summary>Whether a mechanic is on the way to <paramref name="ride"/>.</summary>
	public static bool MechanicCalledTo( Ride ride ) => Entity.All.OfType<Staff>().Any( s => s.called && s.repairTarget == ride );

	private void DoMechanic()
	{
		if ( repairTarget is { IsBroken: true } || (called && repairTarget != null && Entity.All.Contains( repairTarget )) )
		{
			working = true;
			if ( WalkToward( repairTarget!.Position ) ) // arrived at the ride
			{
				repairTimer -= Time.Delta;
				if ( repairTimer <= 0f )
				{
					repairTarget.Service();
					repairTarget = null;
					called = false;
					FinishJob();
				}
			}
			return;
		}
		called = false;

		// No (valid) target: find the nearest broken ride; idle-wander if the park is all running.
		repairTarget = NearestBrokenRide();
		repairTimer = StaffGrades.Current.RepairSeconds( Grade );
		if ( repairTarget == null )
			WanderStep();
	}

	private Ride? NearestBrokenRide()
	{
		Ride? best = null;
		float bestD2 = float.MaxValue;
		foreach ( var e in Entity.All )
		{
			if ( e is not Ride { IsBroken: true } r )
				continue;
			float dx = r.Position.X - Position.X, dy = r.Position.Y - Position.Y;
			float d2 = dx * dx + dy * dy;
			if ( d2 < bestD2 )
			{
				bestD2 = d2;
				best = r;
			}
		}
		return best;
	}

	// Head for the nearest litter and pick it up; wander if the park is clean.
	private void DoHandyman()
	{
		// A toilet that needs cleaning comes first (it sickens visitors); otherwise the nearest litter.
		if ( toiletTarget is { NeedsCleaning: true } || (toiletTarget = DirtiestToilet()) != null )
		{
			working = true;
			if ( WalkToward( toiletTarget!.Position ) )
			{
				toiletTarget.Filth = 0f;
				toiletTarget = null;
				FinishJob();
			}
			return;
		}

		var litter = NearestLitter();
		if ( litter == null )
		{
			WanderStep();
			return;
		}

		working = true;
		if ( WalkToward( litter.Position ) )
		{
			// The broom sweeps the litter around the spot too — half the grade's DetectionRange cells.
			float sweep = StaffGrades.Current.HandymanDetectionRange[Math.Clamp( Grade, 0, StaffGrades.MaxGrade )] * CellSize * 0.5f;
			foreach ( var l in Litter.Active.ToList() )
			{
				float dx = l.Position.X - litter.Position.X, dy = l.Position.Y - litter.Position.Y;
				if ( l == litter || dx * dx + dy * dy <= sweep * sweep )
					l.PickUp();
			}
		}
	}

	private Shop? toiletTarget;

	private Shop? DirtiestToilet()
	{
		Shop? best = null;
		foreach ( var s in Shop.Stalls )
			if ( s.NeedsCleaning && InRange( s.Position ) && (best == null || s.Filth > best.Filth) )
				best = s;
		return best;
	}

	private Litter? NearestLitter()
	{
		Litter? best = null;
		float bestD2 = float.MaxValue;
		foreach ( var l in Litter.Active )
		{
			if ( !InRange( l.Position ) ) // a zoned handyman only clears litter inside its patrol area (T-049)
				continue;
			float dx = l.Position.X - Position.X, dy = l.Position.Y - Position.Y;
			float d2 = dx * dx + dy * dy;
			if ( d2 < bestD2 )
			{
				bestD2 = d2;
				best = l;
			}
		}
		return best;
	}

	// Walk toward the current wander target on the ground; pick a new one on arrival.
	private void WanderStep()
	{
		if ( WalkToward( target ) )
			target = PickWanderTarget();
	}

	// Walk toward a world point (XY only), dropping onto the terrain. Returns true once within reach.
	private bool WalkToward( Vector3 to )
	{
		var d = new Vector3( to.X - Position.X, to.Y - Position.Y, 0 );
		bool arrived = d.Length < ReachDistance;
		movedThisFrame = !arrived;
		if ( !arrived )
		{
			Position += d.Normal * speed * Time.Delta;
			moveDir = d.Normal; // world travel direction; the facing is derived from it per frame
		}
		DropToGround();
		return arrived;
	}

	private Vector3 PickWanderTarget()
	{
		var (wc, wr) = WanderArea;
		float a = (float)Random.Shared.NextDouble() * MathF.PI * 2f;
		float d = (float)Random.Shared.NextDouble() * wr;
		return new Vector3( wc.X + MathF.Cos( a ) * d, wc.Y + MathF.Sin( a ) * d, 0 );
	}

	private void DropToGround() => Position = Position.WithZ( terrain.SampleHeight( Position.X, Position.Y ) );

	private static Model RoleModel( StaffRole role )
	{
		if ( roleModels.TryGetValue( role, out var m ) )
			return m;

		// Distinct fallback colours per role: entertainer orange, handyman blue, guard navy, mechanic yellow.
		var (r, g, b) = role switch
		{
			StaffRole.Handyman => ((byte)40, (byte)90, (byte)220),
			StaffRole.Guard => ((byte)30, (byte)30, (byte)70),
			StaffRole.Researcher => ((byte)235, (byte)235, (byte)245), // lab-coat white
			StaffRole.Mechanic => ((byte)230, (byte)200, (byte)40),    // hi-vis yellow
			_ => ((byte)255, (byte)140, (byte)0),
		};
		var model = Billboard.Make( r, g, b );
		roleModels[role] = model;
		return model;
	}
}
