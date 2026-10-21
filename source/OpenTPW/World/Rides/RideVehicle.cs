namespace OpenTPW;

/// <summary>
/// A ride car for the "car" rides — tour rides, go-karts, water rides, bumpers (their scripts use the
/// <c>TOUR</c>/<c>BUMP</c> opcodes). It re-implements the two motion behaviours of the original car engine
/// (Ghidra <c>FUN_0054a040</c>, docs/08), occupancy-driven, giving these rides visible moving cars:
/// <list type="bullet">
///   <item><b>Circuit</b> (tour / kart / water): the cars follow a loop around the footprint in sequence,
///   like the coaster's <see cref="CoasterTrain"/>.</item>
///   <item><b>Arena</b> (bumper / dodgem — the ride's script uses <c>BUMP</c>): the cars roam to random
///   waypoints inside the footprint and bounce off each other and the walls, via <see cref="CarSim"/>.</item>
/// </list>
///
/// <para>The circuit path follows the ride's <b>authored footprint</b> (its <c>.sam</c> shape) and passes
/// the entrance (<see cref="RidePath.FootprintRing"/>), falling back to a generic ellipse for a degenerate
/// footprint. The motion <i>behaviour</i> is faithful to the original; the waypoint <i>positions</i> are a
/// footprint-shaped stand-in, because the real ones are runtime sim output, not file data — the original
/// reads car/seat node positions off matrices bound at runtime (no static path in the assets; docs/08 /
/// T-048). So like the light/particle proxies, the cars move while the ride runs and carry their riders.</para>
/// </summary>
public sealed class RideVehicle : Entity
{
	private const int DefaultSeats = 4;  // when the model declares no car/seat nodes
	private const int MaxSeats = 12;     // cap the visible markers regardless of the authored count
	private const float Speed = 14f;     // world units / second along the loop
	private const float SeatSpacing = 6f; // arc-length gap between trailing riders
	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );

	/// <summary>The visible seat/rider count for a ride with <paramref name="authoredCarNodes"/> car/seat
	/// nodes in its model (T-048): the authored count clamped to [1, <see cref="MaxSeats"/>], or the default
	/// when the model declares none. So a tour ride with nine seat nodes shows nine riders, not a fixed four.</summary>
	public static int SeatCountFor( int authoredCarNodes )
		=> authoredCarNodes <= 0 ? DefaultSeats : Math.Clamp( authoredCarNodes, 1, MaxSeats );

	private readonly Ride ride;
	private readonly int seatCount;
	private Vector3[] loop; // closed ring of ground points
	private float[] cum;    // cumulative arc length
	private float length;
	private readonly ModelEntity car;
	private readonly ModelEntity[] seats;

	/// <summary>Where the lead car (or, for dodgems, the first car) is — null while none is out.</summary>
	public Vector3? LeadPosition => flyers.Count > 0 ? flyers[0].Position
		: car.Position.Z > -1000f ? car.Position
		: seats.Length > 0 && seats[0] != null && seats[0].Position.Z > -1000f ? seats[0].Position : null;
	private readonly IReadOnlyList<int> seatNodeIds; // car/seat node ids this vehicle drives (T-048)
	private float dist;

	// Bumper/dodgem rides run the arena collision sim instead of the circuit loop (docs/08 — the BUMP
	// branch of the original car engine). Tour/kart/water rides keep the loop (`carSim` stays null).
	private readonly CarSim? carSim;
	private readonly ParkTerrain terrain;
	// A tour ride (the jungle bird, the Halloween balloon — no track) flies the original's FLY sim instead.
	private TourFlight? flight;
	private readonly List<ModelEntity> flyers = new();
	private (Model Model, Vector3 Scale) flyerMesh;
	private const int RidersPerFlyer = 9; // TourRide.RSE: TOUR 5, ceil(capacity / 9) cars

	private readonly Model[]? carFrames; // the bumper's real b_car.MD2 mesh (+ b_carm wobble frame), if loaded
	private float carAnimT;
	private const float CarAnimFps = 6f;

	// Dodgem collision sound (T-047): the original BUMP branch plays a knock on each car-vs-car hit
	// (FUN_00473270). The arena sim (CarSim) already resolves those collisions, so a real knock spawns a
	// positioned bump sound through the 3D audio bus, rate-limited so a pile-up doesn't machine-gun the clip.
	private float lastBumpAt = float.NegativeInfinity;
	private const float BumpCooldown = 0.12f; // min seconds between bump sounds
	private static byte[]? bumpClip;
	private static bool bumpClipLoaded;

	public RideVehicle( Ride ride, float tileSize, ParkTerrain terrain )
	{
		this.ride = ride;
		this.terrain = terrain;
		seatCount = SeatCountFor( ride.CarNodeCount );
		// The model's car/seat node ids, one per visible seat — we publish each one's live world position
		// to the ride's node field every frame (T-048), so EVENT/SPARK effects addressed to a seat node
		// follow the moving car instead of the ride centre.
		seatNodeIds = ride.NodeField.CarSeatNodeIds.Take( seatCount ).ToList();

		// Prefer a loop that traces the ride's authored footprint (its .sam shape) and passes the entrance;
		// fall back to a generic ellipse for a degenerate footprint (T-048).
		loop = BuildFootprintLoop( ride, tileSize, terrain ) ?? BuildEllipseLoop( ride, tileSize, terrain );
		cum = new float[loop.Length];
		for ( int i = 1; i < loop.Length; i++ )
			cum[i] = cum[i - 1] + loop[i].Distance( loop[i - 1] );
		length = cum[^1];

		var carMat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		carMat.Set( "Color", new Texture( [200, 60, 60, 255], 1, 1 ) ); // red car
		car = new ModelEntity
		{
			Model = Primitives.Cube.GenerateModel( carMat ),
			Scale = new Vector3( tileSize * 0.18f, tileSize * 0.34f, tileSize * 0.18f ), // long along local +Y
			Position = Offscreen,
		};

		seats = new ModelEntity[seatCount];

		// Prefer the ride's own real vehicle meshes (tour bird/balloon, the coloured go-karts). Their presence
		// means this is a track/circuit ride, so it circulates the loop with those cars — even go-karts, whose
		// script also raises the BUMP opcode (they have a track, not a dodgem arena). Only a ride with no such
		// mesh but the BUMP opcode is a true dodgem (the bumper's b_car.MD2, driven by the arena collision sim);
		// anything else keeps the small rider-marker cubes.
		var vehicles = LoadCircuitCars( ride.Archive, Path.GetFileNameWithoutExtension( ride.Archive ), tileSize, out bool flies );

		if ( flies )
		{
			flyerMesh = vehicles[0];
			var station = ride.Position.WithZ( terrain.SampleHeight( ride.Position.X, ride.Position.Y ) + tileSize * 0.6f );
			flight = new TourFlight( station, ride.Rotation * MathF.PI / 2f, FlyerCount, terrain.SampleHeight );
			car.Position = Offscreen;
			for ( int i = 0; i < seatCount; i++ )
				seats[i] = new ModelEntity { Position = Offscreen }; // the riders sit on the flying cars
		}
		else if ( vehicles.Count > 0 )
		{
			// Circuit ride with real vehicles: go-karts cycle their four colours across the seats; a tour ride
			// uses its single vehicle for every seat + the lead car.
			car.Model = vehicles[0].Model;   // the lead car heads the train with a real mesh, not a red box
			car.Scale = vehicles[0].Scale;
			for ( int i = 0; i < seatCount; i++ )
			{
				var v = vehicles[i % vehicles.Count];
				seats[i] = new ModelEntity { Model = v.Model, Scale = v.Scale, Position = Offscreen };
			}
		}
		else if ( ride.IsBumperRide )
		{
			// A true dodgem arena: roam-and-bump via the collision sim, drawing the real b_car.MD2 (red box if absent).
			carSim = new CarSim( seatCount,
				ride.Shape.Width * tileSize * 0.5f, ride.Shape.Height * tileSize * 0.5f,
				radius: tileSize * 0.3f, speed: Speed );

			var (frames, scale) = LoadDodgemFrames( ride.Archive, tileSize );
			carFrames = frames; // null if b_car.MD2 didn't load → fall back to a red box below
			Model carModel; Vector3 carScale;
			if ( frames != null ) { carModel = frames[0]; carScale = scale; }
			else { carModel = ColourCube( 220, 40, 40 ); carScale = new Vector3( tileSize * 0.22f ); }
			for ( int i = 0; i < seatCount; i++ )
				seats[i] = new ModelEntity { Model = carModel, Scale = carScale, Position = Offscreen };
		}
		else
		{
			// No vehicle mesh resolved → the original small rider-marker cubes.
			var m = ColourCube( 250, 220, 90 ); var sc = new Vector3( tileSize * 0.06f );
			for ( int i = 0; i < seatCount; i++ )
				seats[i] = new ModelEntity { Model = m, Scale = sc, Position = Offscreen };
		}
	}

	// Discovers and loads the real vehicle meshes for a circuit ride (tour / kart / water) from its WAD, sized to
	// the tile. Go-karts return their four coloured karts (cycled across the seats); a tour ride returns its
	// single theme vehicle (jungle Bird / hallow Balloon). We enumerate the WAD root rather than hard-coding a
	// theme's name, so it adapts per level. Empty when none resolve → the caller keeps the marker cubes.
	private static List<(Model Model, Vector3 Scale)> LoadCircuitCars( string archive, string rideName, float tileSize, out bool flies )
	{
		var found = new List<(Model, Vector3)>();
		flies = false;

		string[] files;
		try { files = FileSystem.GetFiles( archive ); }
		catch { return found; }

		var present = files.Select( Path.GetFileName ).Where( n => !string.IsNullOrEmpty( n ) ).ToList();
		string? Find( string baseNoExt ) => present.FirstOrDefault( n =>
			Path.GetFileNameWithoutExtension( n! ).Equals( baseNoExt, StringComparison.OrdinalIgnoreCase ) );

		// Go-karts: the four coloured karts (a small kart fits ~half a tile). Fall back to a tour ride's single
		// vehicle (the bird / balloon), which fills more of its tile.
		var candidates = new List<string>();
		float fit = 0.5f;
		foreach ( var c in new[] { "gk_blue", "gk_green", "gk_orange", "gk_purple" } )
			if ( Find( c ) is { } f ) candidates.Add( f );
		if ( candidates.Count == 0 )
		{
			fit = 0.85f;
			foreach ( var c in new[] { "Bird", "Balloon" } )
				if ( Find( c ) is { } f ) { candidates.Add( f ); break; }
			flies = candidates.Count > 0;
		}

		foreach ( var name in candidates )
		{
			var built = RideCarMesh.Build( archive, name );
			if ( built == null )
				continue;
			float he = built.Value.HalfExtent;
			float s = he > 1e-3f ? (tileSize * fit) / (2f * he) : 1f;
			found.Add( (built.Value.Model, built.Value.DecompScale * s) );
		}

		flies &= found.Count > 0;
		if ( found.Count > 0 )
			Log.Info( $"[ride] {rideName}: loaded {found.Count} vehicle mesh(es) [{string.Join( ",", candidates )}]" );
		return found;
	}

	private static Model ColourCube( byte r, byte g, byte b )
	{
		var mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		mat.Set( "Color", new Texture( [r, g, b, 255], 1, 1 ) );
		return Primitives.Cube.GenerateModel( mat );
	}

	// The bumper's real dodgem car (b_car.MD2) + its motion frame (b_carm.MD2), sized to ~0.9 tile so the
	// cars read at dodgem scale. Null if the mesh doesn't load (caller uses a red box stand-in).
	private static (Model[]? Frames, Vector3 Scale) LoadDodgemFrames( string archive, float tileSize )
	{
		var frames = new List<Model>();
		Vector3 scale = Vector3.One;
		foreach ( var name in new[] { "b_car.MD2", "b_carm.MD2" } )
		{
			var built = RideCarMesh.Build( archive, name );
			if ( built == null )
				continue;
			frames.Add( built.Value.Model );
			if ( frames.Count == 1 )
			{
				float he = built.Value.HalfExtent;
				float fit = he > 1e-3f ? (tileSize * 0.9f) / (2f * he) : 1f;
				scale = built.Value.DecompScale * fit;
			}
		}
		return frames.Count > 0 ? (frames.ToArray(), scale) : (null, scale);
	}

	/// <summary>Despawn the car + rider markers (called when the ride is sold/demolished).</summary>
	/// <summary>Drive a laid circuit instead of the footprint loop (a kart / water ride's <see cref="TileTrack"/>).</summary>
	public void SetLoop( Vector3[] ring )
	{
		if ( ring.Length < 3 || carSim != null )
			return;
		loop = ring[0].Distance( ring[^1] ) < 1e-3f ? ring : [.. ring, ring[0]]; // a closed ring
		cum = new float[loop.Length];
		for ( int i = 1; i < loop.Length; i++ )
			cum[i] = cum[i - 1] + loop[i].Distance( loop[i - 1] );
		length = cum[^1];
	}

	public void Despawn()
	{
		foreach ( var f in flyers )
			Entity.All.Remove( f );
		Entity.All.Remove( car );
		foreach ( var s in seats )
			Entity.All.Remove( s );
		Entity.All.Remove( this );
	}

	protected override void OnUpdate()
	{
		if ( carSim != null )
		{
			UpdateArena();
			return;
		}
		if ( flight != null )
		{
			UpdateFlight();
			return;
		}

		if ( length < 1e-3f )
			return;

		// The car circulates only while the ride is actually running (has riders, not broken down). The car
		// demo (OPENTPW_CAR_DEMO=1) circulates + fills every seat regardless, to preview the vehicles.
		if ( DemoAllCars || (ride.Riders > 0 && !ride.IsBroken) )
			dist = (dist + Speed * Time.Delta) % length;

		var (pos, tan) = Sample( dist );
		car.Position = pos;
		// The box car is long along local +Y, so align +Y with the travel tangent.
		float yaw = MathF.Atan2( tan.Y, tan.X ) - MathF.PI / 2f;
		var rot = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
		car.Rotation = rot;

		// Riders trail the lead car along the loop, one per occupied seat, so the authored seat count reads
		// as a train of cars rather than markers stacked beside one box. Each seat's path position is the
		// car/seat node's live world position — published to the node field (T-048) whether or not it's
		// occupied (the node exists physically), while the visible marker hides when empty.
		int occupied = DemoAllCars ? seatCount : Math.Min( ride.Riders, seatCount );
		for ( int i = 0; i < seatCount; i++ )
		{
			float d = ((dist - (i + 1) * SeatSpacing) % length + length) % length;
			var (sp, st) = Sample( d );
			var seatPos = sp + Vector3.Up * 4f;

			if ( i < seatNodeIds.Count )
				// Publish the seat's world position + travel direction (the node matrix's forward row); the
				// art-orientation offset (the +Y car mesh) is the consumer's business, not the node facing.
				ride.NodeField.PublishMoving( seatNodeIds[i], seatPos, new Vector3( st.X, st.Y, 0f ) );

			if ( i < occupied )
			{
				seats[i].Position = seatPos;
				float syaw = MathF.Atan2( st.Y, st.X ) - MathF.PI / 2f;
				seats[i].Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, syaw );
			}
			else
			{
				seats[i].Position = Offscreen;
			}
		}
	}

	private int FlyerCount => Math.Max( 1, (ride.Capacity + RidersPerFlyer - 1) / RidersPerFlyer );

	// Tour ride: fly the cars (TourFlight) — they take off with riders while the ride runs — and pose each one
	// on its heading, pitched by its climb and banked into its turn. Each car's riders are its seat nodes.
	private void UpdateFlight()
	{
		var sim = flight!;
		sim.SetCarCount( FlyerCount );
		sim.Step( Time.Delta, DemoAllCars || (ride.Riders > 0 && !ride.IsBroken) );

		while ( flyers.Count < sim.Cars.Count )
			flyers.Add( new ModelEntity { Model = flyerMesh.Model, Scale = flyerMesh.Scale } );
		while ( flyers.Count > sim.Cars.Count )
		{
			Entity.All.Remove( flyers[^1] );
			flyers.RemoveAt( flyers.Count - 1 );
		}

		for ( int i = 0; i < sim.Cars.Count; i++ )
		{
			var c = sim.Cars[i];
			// The vehicle meshes run along local +Y: yaw it onto the heading, then pitch (about X) and bank (about Y).
			var q = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, c.Heading - MathF.PI / 2f )
				* System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, c.Pitch )
				* System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitY, c.Bank );
			flyers[i].Position = c.Position;
			flyers[i].Rotation = q;
		}

		for ( int i = 0; i < seatNodeIds.Count; i++ )
		{
			var c = sim.Cars[Math.Min( i / RidersPerFlyer, sim.Cars.Count - 1 )];
			ride.NodeField.PublishMoving( seatNodeIds[i], c.Position, new Vector3( MathF.Cos( c.Heading ), MathF.Sin( c.Heading ), 0f ) );
		}
	}

	// Bumper/dodgem update: advance the arena collision sim and place one car per occupied seat at its
	// local arena position mapped to world + terrain. There's no lead car or circuit loop — the dodgems
	// roam and bump (docs/08, the BUMP branch). Each car's live world position feeds the node field (T-048).
	// Dev visualisation: drive + show every dodgem regardless of occupancy, so the arena sim is visible
	// without waiting for peeps to board (OPENTPW_BUMPER_DEMO=1). Off by default — normal play is occupancy-driven.
	private static readonly bool DemoAllCars =
		Environment.GetEnvironmentVariable( "OPENTPW_BUMPER_DEMO" ) == "1" ||
		Environment.GetEnvironmentVariable( "OPENTPW_CAR_DEMO" ) == "1";

	private void UpdateArena()
	{
		if ( DemoAllCars || (ride.Riders > 0 && !ride.IsBroken) )
		{
			carSim!.Step( Time.Delta );
			PlayBumps();
		}

		car.Position = Offscreen; // no lead car in bumper mode

		// Cycle the dodgem's animation frames (b_car ↔ b_carm) for its wobble.
		carAnimT += Time.Delta;
		int frame = carFrames is { Length: > 1 } ? (int)(carAnimT * CarAnimFps) % carFrames.Length : 0;

		int occupied = DemoAllCars ? seatCount : Math.Min( ride.Riders, seatCount );
		var c = ride.Position;
		for ( int i = 0; i < seatCount; i++ )
		{
			var cs = carSim!.Cars[i];
			float wx = c.X + cs.Pos.X, wy = c.Y + cs.Pos.Y;
			var pos = new Vector3( wx, wy, 0 ).WithZ( terrain.SampleHeight( wx, wy ) + 2f );

			if ( i < seatNodeIds.Count )
				ride.NodeField.PublishMoving( seatNodeIds[i], pos,
					new Vector3( MathF.Cos( cs.Heading ), MathF.Sin( cs.Heading ), 0f ) );

			if ( i < occupied )
			{
				seats[i].Position = pos;
				// The car mesh's long axis is +Y (the ride-car convention), so face it along travel by -90°;
				// the plain marker box has no front, so it uses the raw heading.
				float yaw = carFrames != null ? cs.Heading - MathF.PI / 2f : cs.Heading;
				seats[i].Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
				if ( carFrames != null )
					seats[i].Model = carFrames[frame];
			}
			else
			{
				seats[i].Position = Offscreen;
			}
		}
	}

	// Play a positioned bump sound for the strongest car-vs-car knock this frame (rate-limited). The impact's
	// local-arena point maps to world like the cars do, and the closing speed scales the gain so a hard hit
	// is louder. Routed through the 3D audio bus (T-047), so it pans from where the dodgems actually collide.
	private void PlayBumps()
	{
		var hits = carSim!.Impacts;
		if ( hits.Count == 0 || Time.Now - lastBumpAt < BumpCooldown )
			return;

		var strongest = hits[0];
		for ( int i = 1; i < hits.Count; i++ )
			if ( hits[i].Speed > strongest.Speed )
				strongest = hits[i];

		var clip = BumpClip();
		if ( clip == null )
			return;

		var ctr = ride.Position;
		float wx = ctr.X + strongest.Pos.X, wy = ctr.Y + strongest.Pos.Y;
		var world = new Vector3( wx, wy, 0 ).WithZ( terrain.SampleHeight( wx, wy ) + 2f );
		float gain = Math.Clamp( strongest.Speed / 12f, 0.3f, 1f );
		Audio.PlaySfx3D( "ride_bump_crunch", clip, world, gain );
		lastBumpAt = Time.Now;
	}

	// The dodgem collision clip (Crunch.mp2 from the global RideHD) — loaded once, shared across all bumpers.
	// (Crunch is exactly the knock the original plays on a bump; it was only "wrong" when misused as an
	// ADDOBJ object sound — see RideEngine.SpawnObject.)
	private static byte[]? BumpClip()
	{
		if ( bumpClipLoaded )
			return bumpClip;
		bumpClipLoaded = true;
		try
		{
			var path = Path.Join( GameDir.GamePath, "data", "global", "sound", "RideHD.sdt" );
			if ( File.Exists( path ) )
				bumpClip = new SdtArchive( path ).soundFiles
					.FirstOrDefault( f => f.Name.StartsWith( "Crunch", StringComparison.OrdinalIgnoreCase ) )?.SoundData;
		}
		catch ( Exception e ) { Log.Warning( $"[ride] bump sound load failed: {e.Message}" ); }
		return bumpClip;
	}

	// A closed loop tracing the ride's authored footprint perimeter (its .sam shape), pulled slightly
	// inside the edge and smoothed into a curve; null when the footprint is degenerate (caller uses the
	// ellipse). Uses real authored shape data + the entrance, so the loop adapts to non-rectangular rides
	// and passes the boarding tile. See RidePath.
	private static Vector3[]? BuildFootprintLoop( Ride ride, float tileSize, ParkTerrain terrain )
	{
		var ring = RidePath.FootprintRing( ride.Shape.Cells, ride.Shape.Entrance );
		if ( ring.Count < 3 )
			return null;

		const float Inset = 0.82f; // pull the ring inside the footprint edge so the car reads as "on" the ride
		float halfW = ride.Shape.Width / 2f, halfH = ride.Shape.Height / 2f;
		var ctr = ride.Position;
		var ctrl = new List<Vector3>( ring.Count );
		foreach ( var (tx, ty) in ring )
		{
			float wx = ctr.X + (tx + 0.5f - halfW) * Inset * tileSize;
			float wy = ctr.Y + (ty + 0.5f - halfH) * Inset * tileSize;
			ctrl.Add( new Vector3( wx, wy, 0 ).WithZ( terrain.SampleHeight( wx, wy ) + 2f ) );
		}
		return RidePath.Smooth( ctrl, closed: true, sub: 6 ).ToArray();
	}

	// The original generic fallback: an elliptical ring inside the footprint, sampled onto the terrain.
	private static Vector3[] BuildEllipseLoop( Ride ride, float tileSize, ParkTerrain terrain )
	{
		const int n = 48;
		float rx = MathF.Max( tileSize, ride.TileW * tileSize * 0.32f );
		float ry = MathF.Max( tileSize, ride.TileH * tileSize * 0.32f );
		var c = ride.Position;
		var l = new Vector3[n + 1];
		for ( int i = 0; i <= n; i++ )
		{
			float a = i / (float)n * MathF.Tau;
			var p = new Vector3( c.X + MathF.Cos( a ) * rx, c.Y + MathF.Sin( a ) * ry, 0 );
			l[i] = p.WithZ( terrain.SampleHeight( p.X, p.Y ) + 2f );
		}
		return l;
	}

	// Position + forward tangent at arc-length d along the loop.
	private (Vector3 Pos, Vector3 Tan) Sample( float d )
	{
		int s = 0;
		while ( s < loop.Length - 2 && cum[s + 1] < d )
			s++;
		float seg = cum[s + 1] - cum[s];
		float f = seg > 1e-4f ? (d - cum[s]) / seg : 0f;
		var a = loop[s];
		var b = loop[s + 1];
		return (a + (b - a) * f, (b - a).Normal);
	}
}
