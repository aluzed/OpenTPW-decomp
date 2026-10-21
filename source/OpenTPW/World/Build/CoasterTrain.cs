namespace OpenTPW;

/// <summary>
/// A small train that runs a laid <see cref="CoasterTrack"/> (T-045 slice 3): a few car bodies spaced
/// along the track's smoothed centre-line, advancing by arc length and orienting to the local tangent
/// (shuttling on an open track, looping on a closed circuit). The car is the ride's real
/// <c>CrocCar.MD2</c> mesh when it loads (single mesh, 6 materials), falling back to a procedural box.
/// The train carries **real boarded peeps** — a peep that reaches the front of the coaster's queue
/// climbs onto a seat (<see cref="TryBoard"/>) and rides the train in view until its ride ends (slice
/// 3b), and the ride's rider scream plays while anyone is aboard.
/// </summary>
public sealed class CoasterTrain : Entity
{
	private const int CarCount = 3;
	private const int SeatsPerCar = 2;
	private const float Speed = 16f;   // world units / second along the track
	private const float AnimFps = 8f;  // CrocCar frame cadence
	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );

	private readonly CoasterTrack track;
	private readonly float tileSize;
	private readonly float spacing;     // arc-length gap between cars
	private readonly ModelEntity[] cars;
	private readonly Vector3[] carRight; // each car's side axis, carried through a loop
	private readonly List<Peep> riders = new(); // boarded peeps, seat order (front car first)
	private readonly Model[] frames;      // CrocCar animation frames (base + CrocCarM1..3)
	private float dist;                 // lead car's distance travelled along the shuttle cycle
	private bool screaming;             // true while the train carries ≥1 rider (drives the ride scream)

	/// <summary>How many seats the train has across all cars.</summary>
	public int SeatCount => cars.Length * SeatsPerCar;

	public CoasterTrain( CoasterTrack track, float tileSize, string archive )
	{
		this.track = track;
		this.tileSize = tileSize;
		spacing = tileSize * 0.9f;

		var (loaded, scale) = LoadCarFrames( archive, tileSize );
		frames = loaded;

		cars = new ModelEntity[CarCount];
		carRight = new Vector3[CarCount];
		for ( int i = 0; i < CarCount; i++ )
			cars[i] = new ModelEntity { Model = frames[0], Scale = scale, Position = Offscreen };
	}

	/// <summary>A peep at the front of the queue climbs aboard, taking the next free seat. Returns false
	/// (so the caller hides the peep as on an ordinary ride) when the train is already full.</summary>
	public bool TryBoard( Peep peep )
	{
		if ( riders.Count >= SeatCount || riders.Contains( peep ) )
			return false;
		riders.Add( peep );
		return true;
	}

	/// <summary>A peep's ride ends — it leaves its seat (the train frees up for the next in line).</summary>
	public void Unboard( Peep peep ) => riders.Remove( peep );

	/// <summary>Despawn the train's car entities + end any scream (called when the track is torn down).</summary>
	public void Despawn()
	{
		foreach ( var c in cars )
			Entity.All.Remove( c );
		riders.Clear();
		if ( screaming )
		{
			track.Coaster.StopRiderScream();
			screaming = false;
		}
		Entity.All.Remove( this );
	}

	protected override void OnUpdate()
	{
		// The ridden centre-line is the track's Catmull-Rom spline (already a closed ring when complete),
		// so the cars glide through corners. A closed circuit loops; an open track shuttles (down + back).
		var path = track.SmoothedPath();
		if ( path.Count < 2 )
		{
			foreach ( var c in cars )
				c.Position = Offscreen;
			return;
		}
		bool closed = track.IsClosed;

		var cum = new float[path.Count];
		for ( int i = 1; i < path.Count; i++ )
			cum[i] = cum[i - 1] + path[i].Distance( path[i - 1] );
		float length = cum[^1];
		if ( length < 1e-3f )
			return;

		float period = closed ? length : 2f * length;
		dist = (dist + Speed * Time.Delta) % period;

		// The ride screams while it carries anyone (sustained by the ride engine; T-037 plays KidsHD voices).
		UpdateScream();

		var frame = frames[CurrentFrame()];
		for ( int i = 0; i < cars.Length; i++ )
		{
			cars[i].Model = frame; // advance the CrocCar's chomp animation (all cars in sync)
			float u = (dist - i * spacing) % period;
			if ( u < 0 ) u += period;

			// Open track: reflect the second half so the car retraces it, facing travel direction.
			bool returning = !closed && u > length;
			float d = returning ? period - u : u;

			var (pos, tan) = Sample( path, cum, d );
			if ( returning ) tan = -tan;

			cars[i].Position = pos;
			// The car body is long along its local +Y (forward), +X to its right, +Z up. Its right stays horizontal,
			// except through a loop, where the travel is vertical or runs backwards over the top: there it keeps
			// the side it had, so the car turns over with the track (T-045 loops).
			var right = new Vector3( tan.Y, -tan.X, 0f );
			if ( right.Length > 0.35f && tan.Z * tan.Z < 0.5f && (carRight[i].Length < 0.5f || Vector3.Dot( right.Normal, carRight[i] ) > -0.2f) )
				carRight[i] = right.Normal;
			var carSide = carRight[i].Length < 0.5f ? new Vector3( 1f, 0f, 0f ) : carRight[i];
			var f = tan.Normal;
			var carUp = Vector3.Cross( carSide, f ).Normal;
			carSide = Vector3.Cross( f, carUp ).Normal; // re-orthogonalise
			cars[i].Rotation = System.Numerics.Quaternion.Normalize( System.Numerics.Quaternion.CreateFromRotationMatrix( new System.Numerics.Matrix4x4(
				carSide.X, carSide.Y, carSide.Z, 0f, f.X, f.Y, f.Z, 0f, carUp.X, carUp.Y, carUp.Z, 0f, 0f, 0f, 0f, 1f ) ) );

			// Seat the real boarded peeps this car carries, side by side across its width (perp to travel);
			// each rider sits on the car and keeps facing the camera (the peep itself idles while aboard).
			var side = new Vector3( -tan.Y, tan.X, 0f ).Normal;
			for ( int r = 0; r < SeatsPerCar; r++ )
			{
				int seat = i * SeatsPerCar + r;
				if ( seat >= riders.Count )
					break;
				float off = (r == 0 ? 1f : -1f) * tileSize * 0.12f;
				riders[seat].SeatAt( pos + Vector3.Up * (tileSize * 0.10f) + side * off );
			}
		}
	}

	// Start the ride's scream the moment the first peep boards and stop it once the last one leaves.
	private void UpdateScream()
	{
		bool occupied = riders.Count > 0;
		if ( occupied == screaming )
			return;
		screaming = occupied;
		if ( occupied )
			track.Coaster.StartRiderScream();
		else
			track.Coaster.StopRiderScream();
	}

	// Position + forward tangent at arc-length d along the polyline.
	private static (Vector3 Pos, Vector3 Tangent) Sample( IReadOnlyList<Vector3> path, float[] cum, float d )
	{
		int s = 0;
		while ( s < path.Count - 2 && cum[s + 1] < d )
			s++;

		float segLen = cum[s + 1] - cum[s];
		float f = segLen > 1e-4f ? (d - cum[s]) / segLen : 0f;
		var a = path[s];
		var b = path[s + 1];
		return (a + (b - a) * f, (b - a).Normal);
	}

	// Ping-pong over the animation frames (0 → n-1 → 1) at AnimFps, so the croc chomps as it runs.
	private int CurrentFrame()
	{
		int n = frames.Length;
		if ( n <= 1 )
			return 0;
		int cycle = 2 * (n - 1);
		int step = (int)(Time.Now * AnimFps) % cycle;
		return step < n ? step : cycle - step;
	}

	// The CrocCar mesh + its animation frames (CrocCarM1..3), all at the base frame's scale so they only
	// animate, not pulse. Falls back to a single procedural box frame if nothing loads.
	private static (Model[] Frames, Vector3 Scale) LoadCarFrames( string archive, float tileSize )
	{
		var frames = new List<Model>();
		Vector3 scale = Vector3.One;
		foreach ( var name in new[] { "CrocCar.MD2", "CrocCarM1.MD2", "CrocCarM2.MD2", "CrocCarM3.MD2" } )
		{
			var built = BuildCarMesh( archive, name );
			if ( built == null )
				continue;
			frames.Add( built.Value.Model );
			if ( frames.Count == 1 ) // size the whole train off the base frame
			{
				float halfExtent = built.Value.HalfExtent;
				float fit = halfExtent > 1e-3f ? (tileSize * 1.3f) / (2f * halfExtent) : 1f;
				scale = built.Value.DecompScale * fit;
			}
		}

		if ( frames.Count > 0 )
		{
			Log.Info( $"[coaster] CrocCar loaded ({frames.Count} frame(s))" );
			return (frames.ToArray(), scale);
		}

		Log.Warning( "[coaster] CrocCar load failed; using a placeholder box" );
		var mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		mat.Set( "Color", new Texture( [60, 165, 75, 255], 1, 1 ) );
		// Long axis along local +Y to match the orientation convention (see OnUpdate).
		return ([Primitives.Cube.GenerateModel( mat )], new Vector3( tileSize * 0.22f, tileSize * 0.36f, 1.4f ));
	}

	// One CrocCar frame mesh — the shared ride-car mesh loader (per-material .wct textures, Y/Z swizzle,
	// centroid-centred). Returns null if the mesh won't load.
	private static (Model Model, Vector3 DecompScale, float HalfExtent)? BuildCarMesh( string archive, string name )
		=> RideCarMesh.Build( archive, name );
}
