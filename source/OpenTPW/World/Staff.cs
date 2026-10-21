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
	// Wages: the park-wide level stays at an average of 1.5/s per member of staff, split by Standard.sam's
	// PerTypeStaffConsts[n].PayMultiplier (handyman 10, mechanic 30, entertainer 15, guard 20, researcher 35 — their
	// mean is 22): a mechanic costs three handymen. (The authored BaseWage × multiplier is in an unknown time unit,
	// so only the ratios are taken.)
	private const float AverageWagePerSecond = 1.5f;
	private const float AveragePayMultiplier = 22f;

	/// <summary><c>PerTypeStaffConsts[n].PayMultiplier</c> by role (Standard.sam order: handyman, mechanic, entertainer,
	/// guard, researcher).</summary>
	public static float[] PayMultipliers { get; set; } = { 10f, 30f, 15f, 20f, 35f };

	/// <summary>The wage per second for a role (pure).</summary>
	public static float WagePerSecond( StaffRole role )
	{
		int i = role switch
		{
			StaffRole.Handyman => 0, StaffRole.Mechanic => 1, StaffRole.Entertainer => 2,
			StaffRole.Guard => 3, _ => 4,
		};
		float m = i < PayMultipliers.Length ? PayMultipliers[i] : AveragePayMultiplier;
		return AverageWagePerSecond * m / AveragePayMultiplier;
	}
	private const float CheerRadius = 30f;     // entertainer reach: EntertainerConstsPerGrade[0].ActivationDistance 3 cells × 10 units
	private const float CheerPerSecond = 8f;   // happiness lifted per second for visitors in range
	private const float ReachDistance = 3f;    // how close counts as "arrived" / "picked up"
	private const float WalkFps = 8f;          // walk-cycle frames per second
	private const float GuardDeterRadius = 30f; // visitors within this of a guard don't litter: RegionFX "Guard" radius 3 cells × 10 units

	// One shared billboard model per role (built once), so the crowd reads staff roles by colour.
	private static readonly Dictionary<StaffRole, Model> roleModels = new();

	// Active guards, so peeps can cheaply check whether one is nearby (litter deterrence).
	private static readonly List<Staff> guards = new();

	/// <summary>How many researchers are currently employed (drives ride upgrade research, T-044).</summary>
	public static int ResearcherCount => Entity.All.Count( e => e is Staff s && s.Role == StaffRole.Researcher );

	/// <summary>Drop the park-wide guard registry (T-062 reload): the entities themselves are cleared via
	/// <c>Entity.All</c>, but this static list would otherwise keep stale references across a level reload.</summary>
	public static void ResetAll() => guards.Clear();

	/// <summary>True if a patrolling guard is within <paramref name="radius"/> of the point.</summary>
	public static bool GuardNear( Vector3 p, float radius = GuardDeterRadius )
	{
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
		ParkFinances.Current?.PayWages( WagePerSecond( Role ) * Time.Delta );
		shadow.Position = Position.WithZ( terrain.SampleHeight( Position.X, Position.Y ) + 0.12f );

		switch ( role )
		{
			case StaffRole.Handyman: DoHandyman(); break;
			case StaffRole.Guard: DoGuard(); break;         // patrol toward trouble (unhappy peeps)
			case StaffRole.Mechanic: DoMechanic(); break;   // repair broken-down rides (T-032)
			case StaffRole.Researcher: WanderStep(); break; // research is off-screen (T-044)
			default: DoEntertainer(); break;
		}

		// Cylindrical billboard: yaw about world up so the quad faces the camera.
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );

		ApplySprite();
	}

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
		WanderStep();

		float lift = CheerPerSecond * Time.Delta;
		float r2 = CheerRadius * CheerRadius;
		foreach ( var e in Entity.All )
		{
			if ( e is not Peep peep )
				continue;
			float dx = peep.Position.X - Position.X, dy = peep.Position.Y - Position.Y;
			if ( dx * dx + dy * dy <= r2 )
				peep.Cheer( lift );
		}
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
		if ( trouble != null )
			WalkToward( trouble.Position );
		else
			WanderStep();
	}

	// Head for the nearest broken-down ride and repair it (over RepairTime once there); wander otherwise.
	private const float RepairTime = 4f; // seconds of on-site work to fix a ride
	private Ride? repairTarget;
	private float repairTimer;
	private void DoMechanic()
	{
		if ( repairTarget is { IsBroken: true } )
		{
			if ( WalkToward( repairTarget.Position ) ) // arrived at the ride
			{
				repairTimer -= Time.Delta;
				if ( repairTimer <= 0f )
				{
					repairTarget.Repair();
					repairTarget = null;
				}
			}
			return;
		}

		// No (valid) target: find the nearest broken ride; idle-wander if the park is all running.
		repairTarget = NearestBrokenRide();
		repairTimer = RepairTime;
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
			if ( WalkToward( toiletTarget!.Position ) )
			{
				toiletTarget.Filth = 0f;
				toiletTarget = null;
			}
			return;
		}

		var litter = NearestLitter();
		if ( litter == null )
		{
			WanderStep();
			return;
		}

		if ( WalkToward( litter.Position ) )
			litter.PickUp();
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
