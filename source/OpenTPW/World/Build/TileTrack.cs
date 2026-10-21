namespace OpenTPW;

/// <summary>The track-laying tool's view of a track (a coaster's <see cref="CoasterTrack"/> or a kart / water
/// ride's <see cref="TileTrack"/>), so the build mode drives both alike.</summary>
public interface ITrackTool
{
	Ride Owner { get; }
	int SegmentCount { get; }
	bool IsClosed { get; }
	bool CanExtend( int tx, int ty );
	bool Extend( int tx, int ty );
	void Backtrack();
}

/// <summary>
/// A go-kart or water-ride track (T-045 follow-up): the original builds these piece by piece from the ride's own
/// <c>gk_trck*</c> / <c>wr_trck*</c> meshes — <c>S</c> a straight, <c>B</c> a quarter-turn, <c>X</c> a crossroads — each
/// a 20×20 piece covering 2×2 map cells (their <c>.hmp</c> templates are 2×2). The station carries its own stretch of
/// track along its length (the jungle karts' <c>gk_TRACK01</c>), so the circuit leaves the station's far end and is
/// closed by laying back into its near end. A track may cross itself at right angles: the crossed straight becomes
/// an <c>X</c> piece (challenge 15, "a GoKart track with 4 crossroads"); the pieces are its sections (challenge 21).
/// Each piece costs <c>Costs.KartTrackCell</c> (400) or <c>Costs.WaterTrackCell</c> (500). Once closed, the ride's cars
/// drive the circuit.
/// </summary>
public sealed class TileTrack : ITrackTool
{
	public const int PieceTiles = 2;      // a piece covers 2×2 map cells
	private const float PieceUnits = 20f; // the authored piece size

	private static readonly (int X, int Y)[] Dirs = { (1, 0), (0, 1), (-1, 0), (0, -1) };

	private readonly Ride ride;
	private readonly PlacementGrid grid;
	private readonly ParkTerrain terrain;
	private readonly string archive, prefix;

	// Piece centres in tile units (a piece's 2×2 cells are centre ± 1), with the direction the track entered each.
	private readonly List<(int X, int Y)> centres = new();
	private readonly List<(int X, int Y)> entries = new();
	private readonly (int X, int Y) exitCentre, exitDir;   // inside the station: where the circuit leaves from
	private readonly (int X, int Y) closeCentre, closeDir; // the piece before the station: where it closes
	private readonly List<ModelEntity> meshes = new();

	public Ride Owner => ride;
	public int SegmentCount => centres.Count;
	public bool IsClosed { get; private set; }

	/// <summary>How many crossroads the track has (a centre visited twice).</summary>
	public int Crossings => centres.Count - centres.Distinct().Count();

	/// <summary>What one piece costs (<c>Costs.KartTrackCell</c> 400 / <c>Costs.WaterTrackCell</c> 500).</summary>
	public float PieceCost { get; }

	/// <summary>The directions laid, in order (0 +X, 1 +Y, 2 −X, 3 −Y) — what a save keeps.</summary>
	public IEnumerable<int> Directions => entries.Select( d => Array.IndexOf( Dirs, d ) );

	/// <summary>Replay saved directions (a loaded park: no charge).</summary>
	public void Restore( IEnumerable<int> directions )
	{
		foreach ( var i in directions )
			if ( i is >= 0 and < 4 && !Extend( Dirs[i] ) )
				break;
	}

	/// <summary>The piece prefix of a ride built from track pieces (<c>gk_</c> karts, <c>wr_</c> water rides), or null.</summary>
	public static string? PiecePrefix( Ride ride )
	{
		foreach ( var p in new[] { "gk_", "wr_" } )
			if ( FileSystem.OpenRead( $"{ride.Archive}/{p}trckS.md2" ) is { } s )
			{
				s.Dispose();
				return p;
			}
		return null;
	}

	public TileTrack( Ride ride, string prefix, PlacementGrid grid, ParkTerrain terrain, float pieceCost )
	{
		this.ride = ride;
		this.grid = grid;
		this.terrain = terrain;
		this.prefix = prefix;
		archive = ride.Archive;
		PieceCost = pieceCost;

		// The station's track runs along its unrotated length, centred on its middle rows: the circuit leaves past its
		// far (+X) end and closes into its near (−X) end. Rotated with the station like its footprint (RideShape).
		var unrot = ride.Shape.Rotated( (4 - ride.Rotation) % 4 );
		int w = unrot.Width, h = unrot.Height;
		int row = (h + 1) / 2;                    // the track's centre line, in tiles (between two middle rows)
		exitCentre = Map( (w - 1, row ), w, h );
		exitDir = MapDir( (1, 0) );
		closeCentre = Map( (-1, row), w, h );
		closeDir = MapDir( (1, 0) );

		ride.TileTrack = this;
	}

	// Station-local (unrotated) tile point → grid tile point, the RideShape rotation applied to continuous points.
	private (int X, int Y) Map( (int X, int Y) p, int w, int h )
	{
		var (x, y) = ride.Rotation switch
		{
			1 => (h - p.Y, p.X),
			2 => (w - p.X, h - p.Y),
			3 => (p.Y, w - p.X),
			_ => p,
		};
		return (ride.TileX + x, ride.TileY + y);
	}

	private (int X, int Y) MapDir( (int X, int Y) d ) => ride.Rotation switch
	{
		1 => (-d.Y, d.X),
		2 => (-d.X, -d.Y),
		3 => (d.Y, -d.X),
		_ => d,
	};

	private (int X, int Y) HeadCentre => centres.Count > 0 ? centres[^1] : exitCentre;
	private (int X, int Y) HeadDir => entries.Count > 0 ? entries[^1] : exitDir;

	// Which way a click at (tx,ty) extends the head (the piece it falls in), or null.
	private (int X, int Y)? DirectionTo( int tx, int ty )
	{
		foreach ( var d in Dirs )
		{
			var c = (HeadCentre.X + d.X * PieceTiles, HeadCentre.Y + d.Y * PieceTiles);
			if ( tx >= c.Item1 - 1 && tx < c.Item1 + 1 && ty >= c.Item2 - 1 && ty < c.Item2 + 1 )
				return d;
		}
		return null;
	}

	public bool CanExtend( int tx, int ty ) => DirectionTo( tx, ty ) is { } d && CanExtend( d );

	private bool CanExtend( (int X, int Y) d )
	{
		if ( IsClosed )
			return false;
		var head = HeadDir;
		if ( d == (-head.X, -head.Y) )
			return false; // no U-turns
		if ( centres.Count == 0 && d != exitDir )
			return false; // out of the station straight ahead
		if ( centres.Count > 0 && IsCrossing( centres.Count - 1 ) && d != head )
			return false; // straight on through a crossroads
		var c = (HeadCentre.X + d.X * PieceTiles, HeadCentre.Y + d.Y * PieceTiles);
		if ( c == closeCentre )
			return true; // back into the station: the circuit closes
		int i = centres.IndexOf( c );
		if ( i >= 0 )
			return CanCross( i, d );
		return FreeCells( c );
	}

	// A crossing goes straight over an existing straight at right angles (and not over another crossing).
	private bool CanCross( int i, (int X, int Y) d )
	{
		if ( centres.Count( c => c == centres[i] ) > 1 || i == centres.Count - 1 )
			return false;
		var entry = entries[i];
		var exit = i + 1 < entries.Count ? entries[i + 1] : entry;
		return entry == exit && entry.X * d.X + entry.Y * d.Y == 0;
	}

	private bool FreeCells( (int X, int Y) c ) => grid.CanPlace( c.X - 1, c.Y - 1, PieceTiles, PieceTiles );

	public bool Extend( int tx, int ty ) => DirectionTo( tx, ty ) is { } d && Extend( d );

	/// <summary>Lay the next piece one step in <paramref name="d"/> (a unit grid direction).</summary>
	public bool Extend( (int X, int Y) d )
	{
		if ( !CanExtend( d ) )
			return false;
		var c = (HeadCentre.X + d.X * PieceTiles, HeadCentre.Y + d.Y * PieceTiles);
		bool crossing = centres.Contains( c );
		if ( !crossing )
		{
			grid.TryPlace( c.Item1 - 1, c.Item2 - 1, PieceTiles, PieceTiles );
			for ( int y = c.Item2 - 1; y <= c.Item2; y++ )
				for ( int x = c.Item1 - 1; x <= c.Item1; x++ )
					ride.QueuePathCells.Add( (x, y) ); // freed with the ride (T-041)
		}
		centres.Add( c );
		entries.Add( d );
		if ( c == closeCentre )
			IsClosed = true;
		Rebuild();
		return true;
	}

	public void Backtrack()
	{
		if ( centres.Count == 0 )
			return;
		var c = centres[^1];
		centres.RemoveAt( centres.Count - 1 );
		entries.RemoveAt( entries.Count - 1 );
		IsClosed = false;
		if ( !centres.Contains( c ) )
			for ( int y = c.Y - 1; y <= c.Y; y++ )
				for ( int x = c.X - 1; x <= c.X; x++ )
				{
					grid.Clear( x, y, 1, 1 );
					ride.QueuePathCells.Remove( (x, y) );
				}
		Rebuild();
	}

	private bool IsCrossing( int i ) => centres.Count( c => c == centres[i] ) > 1;

	// The piece at index i: which mesh, turned how (radians, counter-clockwise about up).
	private (string Piece, float Yaw) PieceAt( int i )
	{
		if ( IsCrossing( i ) )
			return ("X", 0f);
		var entry = entries[i];
		var exit = i == centres.Count - 1 ? (IsClosed ? closeDir : entry) : entries[i + 1];
		if ( exit == entry )
			return ("S", Turns( (0, 1), entry ) * MathF.PI / 2f); // the straight runs along its +Y
		// The quarter-turn joins its +Y and +X edges: turn it until those are the sides it enters and leaves by.
		var sides = new[] { (-entry.X, -entry.Y), exit };
		for ( int k = 0; k < 4; k++ )
		{
			var a = Rotate( (0, 1), k );
			var b = Rotate( (1, 0), k );
			if ( sides.Contains( a ) && sides.Contains( b ) )
				return ("B", k * MathF.PI / 2f);
		}
		return ("S", 0f);
	}

	private static (int X, int Y) Rotate( (int X, int Y) d, int k )
	{
		for ( int i = 0; i < k; i++ )
			d = (-d.Y, d.X);
		return d;
	}

	private static int Turns( (int X, int Y) from, (int X, int Y) to )
	{
		for ( int k = 0; k < 4; k++ )
			if ( Rotate( from, k ) == to )
				return k;
		return 0;
	}

	private void Rebuild()
	{
		foreach ( var m in meshes )
			Entity.All.Remove( m );
		meshes.Clear();

		for ( int i = 0; i < centres.Count; i++ )
		{
			if ( centres.IndexOf( centres[i] ) != i )
				continue; // a crossroads is drawn once (as an X, at its first visit)
			var (piece, yaw) = PieceAt( i );
			if ( Load( piece ) is not { } models )
				continue;
			var world = grid.PointToWorld( centres[i].X, centres[i].Y );
			world = world.WithZ( terrain.SampleHeight( world.X, world.Y ) );
			var rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
			foreach ( var model in models )
				meshes.Add( new ModelEntity { Model = model, Position = world, Rotation = rotation, Scale = new Vector3( grid.TileSize * PieceTiles / PieceUnits ) } );
		}

		// A closed circuit is what the cars drive: through the station, then piece by piece back to it.
		if ( IsClosed && ride.Vehicle is { } vehicle )
			vehicle.SetLoop( CircuitPoints() );
	}

	/// <summary>The circuit as ground points: across the station from its near to its far end, then every piece.</summary>
	public Vector3[] CircuitPoints()
	{
		Vector3 At( (int X, int Y) c )
		{
			var w = grid.PointToWorld( c.X, c.Y );
			return w.WithZ( terrain.SampleHeight( w.X, w.Y ) + 1f );
		}
		var pts = new List<Vector3> { At( (closeCentre.X + closeDir.X * PieceTiles, closeCentre.Y + closeDir.Y * PieceTiles) ), At( exitCentre ) };
		foreach ( var c in centres )
			pts.Add( At( c ) );
		return pts.ToArray();
	}

	/// <summary>Remove the pieces (the ride is being sold; its grid cells are freed with it).</summary>
	public void Despawn()
	{
		foreach ( var m in meshes )
			Entity.All.Remove( m );
		meshes.Clear();
		if ( ride.TileTrack == this )
			ride.TileTrack = null;
	}

	// The piece's meshes (the main surface only: the end caps close an unconnected end), baked centred on the piece.
	private readonly Dictionary<string, List<Model>?> cache = new();

	private List<Model>? Load( string piece )
	{
		if ( cache.TryGetValue( piece, out var cached ) )
			return cached;
		List<Model>? models = null;
		try
		{
			var modelFile = new ModelFile( $"{archive}/{prefix}trck{piece}.md2" );
			if ( modelFile.Meshes.Count > 0 )
			{
				var mesh = modelFile.Meshes[0];
				var textures = mesh.Materials.Select( m => Ride.LoadRideTexture( archive, m.Name ) ).ToList();
				var vertices = new Vertex[mesh.Vertices.Length];
				for ( int i = 0; i < mesh.Vertices.Length; ++i )
				{
					var p = mesh.Vertices[i].Position;
					var w = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( p.X, p.Y, p.Z ), mesh.TransformMatrix );
					vertices[i] = new Vertex
					{
						Position = new Vector3( w.X - PieceUnits / 2f, w.Z - PieceUnits / 2f, w.Y + 0.1f ),
						Normal = mesh.Normals[i],
						TexCoords = mesh.TexCoords[i],
						TexIndex = (int)mesh.Vertices[i].TextureIndex,
						MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
					};
				}
				models = MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
					() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[track] no '{prefix}trck{piece}' piece in {archive}: {e.Message}" );
		}
		cache[piece] = models;
		return models;
	}
}
