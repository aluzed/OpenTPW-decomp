namespace OpenTPW;

/// <summary>
/// Nearest-vertex lookup in the XY plane over a fixed point set, bucketed in a uniform grid. Answers exactly what a
/// linear scan would (the closest point; on a tie the lowest index), but only visits the cells around the query —
/// <see cref="ParkTerrain.SampleHeight"/> scanned all ~30k terrain vertices per call, twice per peep per frame,
/// which was 94% of a crowded park's frame time.
/// </summary>
public sealed class NearestVertexIndex
{
	private readonly float[] xs, ys;
	private readonly float minX, minY, cellSize;
	private readonly int cols, rows;
	private readonly int[] cellStart; // CSR layout: points of cell c are order[cellStart[c] .. cellStart[c + 1])
	private readonly int[] order;

	public int Count => xs.Length;

	public NearestVertexIndex( IReadOnlyList<(float X, float Y)> points, float cellSize = 16f )
	{
		int n = points.Count;
		xs = new float[n];
		ys = new float[n];
		this.cellSize = cellSize;

		float maxX = float.MinValue, maxY = float.MinValue;
		minX = minY = float.MaxValue;
		for ( int i = 0; i < n; i++ )
		{
			xs[i] = points[i].X;
			ys[i] = points[i].Y;
			minX = MathF.Min( minX, xs[i] ); maxX = MathF.Max( maxX, xs[i] );
			minY = MathF.Min( minY, ys[i] ); maxY = MathF.Max( maxY, ys[i] );
		}
		if ( n == 0 )
			minX = minY = maxX = maxY = 0f;

		cols = Math.Max( 1, (int)((maxX - minX) / cellSize) + 1 );
		rows = Math.Max( 1, (int)((maxY - minY) / cellSize) + 1 );

		var counts = new int[cols * rows + 1];
		var cellOf = new int[n];
		for ( int i = 0; i < n; i++ )
		{
			cellOf[i] = CellIndex( CellX( xs[i] ), CellY( ys[i] ) );
			counts[cellOf[i] + 1]++;
		}
		for ( int c = 0; c < cols * rows; c++ )
			counts[c + 1] += counts[c];
		cellStart = counts;

		order = new int[n];
		var fill = (int[])cellStart.Clone();
		for ( int i = 0; i < n; i++ )
			order[fill[cellOf[i]]++] = i; // ascending i within a cell, so ties resolve like a linear scan
	}

	private int CellX( float x ) => Math.Clamp( (int)MathF.Floor( (x - minX) / cellSize ), 0, cols - 1 );
	private int CellY( float y ) => Math.Clamp( (int)MathF.Floor( (y - minY) / cellSize ), 0, rows - 1 );
	private int CellIndex( int cx, int cy ) => cy * cols + cx;

	/// <summary>Index of the point closest to (<paramref name="x"/>, <paramref name="y"/>), or -1 when empty.</summary>
	public int Nearest( float x, float y )
	{
		if ( xs.Length == 0 )
			return -1;

		// A query outside the grid starts from the clamped border cell; the ring bound below still holds for it.
		int qx = CellX( x ), qy = CellY( y );

		int best = -1;
		float bestDistSq = float.MaxValue;
		int maxRing = Math.Max( cols, rows );
		for ( int r = 0; r <= maxRing; r++ )
		{
			for ( int cy = qy - r; cy <= qy + r; cy++ )
			{
				if ( cy < 0 || cy >= rows )
					continue;
				for ( int cx = qx - r; cx <= qx + r; cx++ )
				{
					if ( cx < 0 || cx >= cols || Math.Max( Math.Abs( cx - qx ), Math.Abs( cy - qy ) ) != r )
						continue;
					int c = CellIndex( cx, cy );
					for ( int k = cellStart[c]; k < cellStart[c + 1]; k++ )
					{
						int i = order[k];
						float dx = xs[i] - x, dy = ys[i] - y;
						float d = dx * dx + dy * dy;
						if ( d < bestDistSq || (d == bestDistSq && i < best) )
						{
							bestDistSq = d;
							best = i;
						}
					}
				}
			}

			// Every point in ring r+1 or further is at least r·cellSize away from the query (inside or outside the
			// grid), so a strictly closer best can't be beaten — and a tie keeps the lower index only if it is in a
			// ring already scanned, hence the strict comparison.
			if ( best >= 0 && bestDistSq < (r * cellSize) * (r * cellSize) )
				break;
		}
		return best;
	}
}
