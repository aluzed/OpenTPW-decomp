namespace OpenTPW;

/// <summary>
/// A ride's queue path (the ride window's queue button, UIHELPTEXT 9): the cells leading out from the ride's
/// entrance, one map cell per piece of the world's <c>queue.wad</c> — straights, left and right bends, and the
/// torch-lit open end where visitors join. A new ride gets a straight run; the queue tool (an <see cref="ITrackTool"/>)
/// lengthens it a cell at a time in any direction from its open end and takes the last cell back, keeping at least
/// one. Each change rebuilds the pieces and the <see cref="RideQueue"/>'s walk waypoints.
/// </summary>
public sealed class QueuePath : ITrackTool
{
	public const int DefaultLength = 6;

	/// <summary>What a piece laid with the queue tool costs (<c>Costs.QueueCell</c>, 75; the ride's own run is free).</summary>
	public static float CellCost { get; set; } = 75f;
	public const int MaxLength = 40;

	public Ride Owner { get; }
	private readonly PlacementGrid grid;
	private readonly ParkTerrain terrain;
	private readonly (int X, int Y) door;    // the entrance cell, on the ride's footprint
	private readonly (int X, int Y) outward; // away from the footprint edge the entrance is on
	private readonly Vector3 standPoint;     // where a visitor waits to board
	private readonly List<(int X, int Y)> cells = new();
	private readonly List<ModelEntity> pieces = new();

	/// <summary>The ride's queue, whose waypoints follow the path.</summary>
	public RideQueue? Queue { get; set; }

	public IReadOnlyList<(int X, int Y)> Cells => cells;
	public int SegmentCount => cells.Count;
	public bool IsClosed => false;

	private (int X, int Y) Head => cells.Count > 0 ? cells[^1] : door;

	public QueuePath( Ride owner, PlacementGrid grid, ParkTerrain terrain, (int X, int Y) door, (int X, int Y) outward, Vector3 standPoint )
	{
		Owner = owner;
		this.grid = grid;
		this.terrain = terrain;
		this.door = door;
		this.outward = outward;
		this.standPoint = standPoint;
		owner.QueuePath = this;
	}

	/// <summary>Lay the default straight run from the entrance (stops at the first blocked cell).</summary>
	public void LayStraight( int length = DefaultLength )
	{
		for ( int i = 1; i <= length; i++ )
			if ( !Lay( (door.X + outward.X * i, door.Y + outward.Y * i) ) )
				break;
		Rebuild();
	}

	/// <summary>Lay a saved path instead of the current one, as far as it still fits; the default run if none of it does.</summary>
	public void Restore( IEnumerable<(int X, int Y)> saved )
	{
		foreach ( var c in cells )
			Free( c );
		cells.Clear();
		foreach ( var c in saved )
			if ( !Adjacent( c, Head ) || cells.Contains( c ) || !Lay( c ) )
				break;
		if ( cells.Count == 0 )
			LayStraight();
		else
			Rebuild();
	}

	public bool CanExtend( int tx, int ty )
		=> cells.Count < MaxLength && Adjacent( (tx, ty), Head ) && !cells.Contains( (tx, ty) ) && grid.CanPlace( tx, ty, 1, 1 );

	public bool Extend( int tx, int ty )
	{
		if ( !CanExtend( tx, ty ) || !Lay( (tx, ty) ) )
			return false;
		Rebuild();
		return true;
	}

	public void Backtrack()
	{
		if ( cells.Count <= 1 )
			return;
		Free( cells[^1] );
		cells.RemoveAt( cells.Count - 1 );
		Rebuild();
	}

	/// <summary>Remove the pieces (the ride is being sold; its grid cells are freed with it).</summary>
	public void Despawn()
	{
		foreach ( var m in pieces )
		{
			Entity.All.Remove( m );
			Owner.OwnedEntities.Remove( m );
		}
		pieces.Clear();
	}

	/// <summary>The walk order: the open end, along the path, then the entrance stand point.</summary>
	public IReadOnlyList<Vector3> Waypoints()
	{
		var points = cells.AsEnumerable().Reverse().Select( c => OnGround( grid.TileToWorld( c.X, c.Y ) ) ).ToList();
		points.Add( standPoint );
		return points;
	}

	/// <summary>
	/// Which piece goes on cell <paramref name="i"/> of a path (pure): the open end on the last cell, a straight where
	/// the path runs on, a bend where it turns — with the direction to face its +Y. A bend joins its tile's +Y and +X
	/// edges, so a left (counter-clockwise) turn faces +Y back the way in, and a right turn faces it the way out.
	/// </summary>
	public static (string Piece, (int X, int Y) Along) PieceAt( IReadOnlyList<(int X, int Y)> path, (int X, int Y) door, int i )
	{
		var from = i == 0 ? door : path[i - 1];
		var inDir = (path[i].X - from.X, path[i].Y - from.Y);
		if ( i == path.Count - 1 )
			return (QueuePieces.End, (-inDir.Item1, -inDir.Item2)); // its open side faces away from the ride
		var outDir = (path[i + 1].X - path[i].X, path[i + 1].Y - path[i].Y);
		if ( outDir == inDir )
			return (QueuePieces.Straight, inDir);
		int cross = inDir.Item1 * outDir.Item2 - inDir.Item2 * outDir.Item1;
		return (QueuePieces.Bend, cross > 0 ? (-inDir.Item1, -inDir.Item2) : outDir);
	}

	private static bool Adjacent( (int X, int Y) a, (int X, int Y) b ) => Math.Abs( a.X - b.X ) + Math.Abs( a.Y - b.Y ) == 1;

	private Vector3 OnGround( Vector3 w ) => w.WithZ( terrain.SampleHeight( w.X, w.Y ) );

	private bool Lay( (int X, int Y) c )
	{
		if ( !grid.TryPlace( c.X, c.Y, 1, 1 ) )
			return false; // ran off the grid or hit another object
		grid.MarkPath( c.X, c.Y ); // reserved against placement, but visitors may walk it (T-036)
		Owner.QueuePathCells.Add( c ); // freed on sell/demolish (T-041)
		cells.Add( c );
		return true;
	}

	private void Free( (int X, int Y) c )
	{
		grid.Clear( c.X, c.Y, 1, 1 );
		Owner.QueuePathCells.Remove( c );
	}

	private void Rebuild()
	{
		Despawn();
		for ( int i = 0; i < cells.Count; i++ )
		{
			var (piece, along) = PieceAt( cells, door, i );
			var placed = QueuePieces.Place( piece, OnGround( grid.TileToWorld( cells[i].X, cells[i].Y ) ), along, grid.TileSize );
			if ( placed.Count == 0 && piece != QueuePieces.Straight ) // a world without the piece: a straight will do
				placed = QueuePieces.Place( QueuePieces.Straight, OnGround( grid.TileToWorld( cells[i].X, cells[i].Y ) ), along, grid.TileSize );
			pieces.AddRange( placed );
		}
		Owner.OwnedEntities.AddRange( pieces );
		if ( Queue != null )
			Queue.Waypoints = Waypoints();
	}
}
