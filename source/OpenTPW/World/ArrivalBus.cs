using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The visitors' bus (T-074). Every world ships <c>features/bus.wad</c>: <c>Bus.MD2</c> (a body + two wheel meshes
/// parented to it). Anchored on the park entrance, the body rests about (-183, -285) away — in the road tunnel
/// behind the entrance forecourt, where the bus waits out of sight. Its <c>bus.RSE</c> cycles arrive
/// (<c>TRIGANIM 5,0</c>, then <c>VAR_STATUS</c> 2: at the stop, offload) → move on (<c>TRIGANIM 5,1</c>, status 4) →
/// leave (<c>TRIGANIM 5,2</c>, <c>KILLOBJ</c>), driven by the game through <c>VAR_TRIGGER</c>. The keyframes only
/// spin the wheels, so the drive follows the forecourt road the terrain names: out of the tunnel to the
/// <c>arrival_roof</c> shelter (offload), past the <c>depart_roof</c> shelter and into the far tunnel. A group of
/// arrivals is released at the arrival stop (see <see cref="Dispatch"/>).
/// </summary>
public sealed class ArrivalBus
{
	private enum Phase { Idle, Arriving, Offloading, Leaving }

	// One mesh part: its model, its offset from the body pivot (the wheels are children of the body) and its
	// authored rotation.
	private readonly record struct Part( ModelEntity Entity, Model Model, Vector3 Offset, System.Numerics.Quaternion BaseRotation );

	private readonly List<Part> parts = new();
	private readonly ParkTerrain terrain;
	private readonly Vector3[] arrivePath; // tunnel → arrival stop
	private readonly Vector3[] leavePath;  // arrival stop → past the departure stop → far tunnel
	private Phase phase = Phase.Idle;
	private Vector3[] path = Array.Empty<Vector3>();
	private float along;                   // distance travelled along the current path
	private float offloadUntil;
	private int passengers;
	private int pending;

	/// <summary>Called with the group size when the bus reaches the arrival stop.</summary>
	public Action<int>? Offload { get; set; }

	/// <summary>Where the group steps off (the arrival shelter).</summary>
	public Vector3 Stop => arrivePath[^1];

	private const float Speed = 60f;         // world units per second (the bus is ~32 long)
	private const float OffloadSeconds = 3f; // bus.RSE: WAIT 1000 after the arrival, then the game's trigger
	private const int Capacity = 40;         // the original's arrival vehicle sizes (mArrivalVehicle_Size1..3) are unknown

	// The forecourt road, relative to the entrance tile (FixedItemInfo.EntrancePos, the same in all four worlds),
	// traced from the terrain's road_* meshes: a U whose left arm climbs out of the tunnel the bus rests in
	// (road_rhs bend02), whose top runs under the arrival_roof then the depart_roof shelters, and whose right arm
	// drops to the far tunnel (road_rhs bend03).
	private static readonly Vector3[] ArriveRoute =
	{
		new( -184, -285, 0 ), new( -150, -215, 0 ), new( -145, -175, 0 ), new( -120, -140, 0 ), new( -80, -130, 0 ),
		new( -48, -130, 0 ),
	};
	private static readonly Vector3[] LeaveRoute =
	{
		new( -48, -130, 0 ), new( 51, -130, 0 ), new( 100, -135, 0 ), new( 140, -175, 0 ), new( 145, -215, 0 ),
		new( 180, -265, 0 ), new( 240, -315, 0 ),
	};

	public bool Busy => phase != Phase.Idle;

	private ArrivalBus( ParkTerrain terrain, Vector3 anchor )
	{
		this.terrain = terrain;
		arrivePath = ArriveRoute.Select( p => anchor + p ).ToArray();
		leavePath = LeaveRoute.Select( p => anchor + p ).ToArray();
	}

	/// <summary>Load the theme's bus for the entrance at <paramref name="anchor"/>; null when the world has none.</summary>
	public static ArrivalBus? TryCreate( string levelPath, Vector3 anchor, ParkTerrain terrain )
	{
		var archive = $"{levelPath}/features/bus";
		try
		{
			var modelFile = new ModelFile( $"{archive}/bus.md2" );
			if ( modelFile.Meshes.Count == 0 )
				return null;

			var bus = new ArrivalBus( terrain, anchor );
			for ( int index = 0; index < modelFile.Meshes.Count; index++ )
			{
				var mesh = modelFile.Meshes[index];
				var textures = mesh.Materials.Select( m => Ride.LoadRideTexture( archive, m.Name ) ).ToList();
				var vertices = new Vertex[mesh.Vertices.Length];
				for ( int i = 0; i < mesh.Vertices.Length; ++i )
					vertices[i] = new Vertex
					{
						Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
						Normal = mesh.Normals[i],
						TexCoords = mesh.TexCoords[i],
						TexIndex = (int)mesh.Vertices[i].TextureIndex,
						MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
					};
				var models = MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
					() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) );
				Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
				// The body (mesh 0) is the pivot; the wheels' transforms are already relative to it.
				var offset = index == 0 ? Vector3.Zero : new Vector3( pos.X, pos.Z, pos.Y );
				foreach ( var model in models )
				{
					var e = new ModelEntity { Scale = new Vector3( scl.X, scl.Z, scl.Y ) }; // hidden (no model) until it runs
					bus.parts.Add( new Part( e, model, offset, new System.Numerics.Quaternion( rot.X, rot.Z, rot.Y, -rot.W ) ) );
				}
			}
			bus.Place( bus.arrivePath[0], bus.arrivePath[1] - bus.arrivePath[0] );
			Log.Info( $"[bus] {archive}: {bus.parts.Count} part(s), arrival stop at {bus.Stop}" );
			return bus;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[bus] no arrival bus ({archive}): {e.Message}" );
			return null;
		}
	}

	/// <summary>Queue <paramref name="count"/> arrivals; the bus brings them on its next run.</summary>
	public void Dispatch( int count ) => pending += count;

	public void Update( float dt, float now )
	{
		switch ( phase )
		{
			case Phase.Idle:
				if ( pending <= 0 )
					return;
				passengers = Math.Min( pending, Capacity );
				pending -= passengers;
				Start( Phase.Arriving, arrivePath );
				SetVisible( true );
				break;

			case Phase.Arriving:
				if ( Drive( dt ) )
				{
					phase = Phase.Offloading;
					offloadUntil = now + OffloadSeconds;
					Offload?.Invoke( passengers );
					passengers = 0;
				}
				break;

			case Phase.Offloading:
				if ( now >= offloadUntil )
					Start( Phase.Leaving, leavePath );
				break;

			case Phase.Leaving:
				if ( Drive( dt ) )
				{
					phase = Phase.Idle;
					SetVisible( false );
				}
				break;
		}
	}

	private void Start( Phase next, Vector3[] route )
	{
		phase = next;
		path = route;
		along = 0f;
	}

	// Advance along the current path; true once its end is reached.
	private bool Drive( float dt )
	{
		along += Speed * dt;
		float left = along;
		for ( int i = 1; i < path.Length; i++ )
		{
			var seg = path[i] - path[i - 1];
			float len = seg.Length;
			if ( left <= len || i == path.Length - 1 )
			{
				float t = len > 1e-3f ? MathF.Min( 1f, left / len ) : 1f;
				Place( path[i - 1] + seg * t, seg );
				return i == path.Length - 1 && left >= len;
			}
			left -= len;
		}
		return true;
	}

	// Put the body at a road point facing along the direction of travel (the model faces -Y), lifted onto the road.
	private void Place( Vector3 at, Vector3 heading )
	{
		float yaw = heading.Length > 1e-3f ? MathF.Atan2( heading.Y, heading.X ) + MathF.PI / 2f : 0f;
		var q = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
		var ground = at + Vector3.Up * terrain.SampleHeight( at.X, at.Y );
		foreach ( var part in parts )
		{
			var o = System.Numerics.Vector3.Transform( part.Offset.GetSystemVector3(), q );
			part.Entity.Position = ground + new Vector3( o.X, o.Y, o.Z );
			part.Entity.Rotation = q * part.BaseRotation;
		}
	}

	private void SetVisible( bool visible )
	{
		foreach ( var part in parts )
			part.Entity.Model = visible ? part.Model : null;
	}
}
