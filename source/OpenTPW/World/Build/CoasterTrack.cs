namespace OpenTPW;

/// <summary>
/// A player-built coaster track (T-045): a chain of grid tiles laid from the station's <c>&gt;</c>
/// connector, which can be closed into a circuit at the <c>&lt;</c> entry. The ridden centre-line is a
/// Catmull-Rom spline through the tile centres (so the train glides), and the track is **rendered** as a
/// smooth elevated ribbon (the real <c>Trak_sec</c> texture) following that same spline, on support
/// pylons. 3D curved piece *meshes* from the <c>.hmp</c> templates + peep boarding are slice 3b.
/// </summary>
public sealed class CoasterTrack
{
	private const float TrackHeight = 10f; // track elevation above the ground

	private readonly PlacementGrid grid;
	private readonly ParkTerrain terrain;
	private readonly Ride coaster;
	private readonly Texture trackTex;
	private readonly Material<ObjectUniformBuffer> ribbonMat;
	private readonly List<(int X, int Y)> tiles = new();
	private readonly List<float> rise = new();          // per-tile height offset above the base track height (STACKUP/DOWN)
	private readonly List<ModelEntity> pylons = new(); // one support per laid segment
	private readonly CoasterTrain train;
	private readonly (int X, int Y)? trackInTile; // the station's '<' entry connector, where the loop closes
	private readonly List<(float X, float Y, float U)> crossSection; // authored track profile (coaster.sam)
	private ModelEntity? ribbon;                  // the smooth track surface (regenerated on every change)

	public (int X, int Y) Head => tiles[^1];
	public int SegmentCount => tiles.Count - 1; // the first tile is the station connector anchor

	/// <summary>True once the track has been laid back to the station's entry connector — a full circuit
	/// (the train then runs a continuous loop instead of shuttling).</summary>
	public bool IsClosed { get; private set; }

	/// <summary>Live rider count of the parent coaster — the train carries this many and runs while &gt; 0.</summary>
	public int Riders => coaster.Riders;

	/// <summary>The coaster this track belongs to (the train uses it to start/stop the rider scream).</summary>
	public Ride Coaster => coaster;

	public CoasterTrack( Ride coaster, PlacementGrid grid, ParkTerrain terrain )
	{
		this.grid = grid;
		this.terrain = terrain;
		this.coaster = coaster;

		trackTex = LoadTrackTexture( coaster.Archive );
		// Unlit (not 3d.shader): unlit samples a single Color texture at the vertex UVs, whereas 3d.shader
		// expects a per-vertex-indexed texture *array* this hand-built ribbon mesh doesn't supply.
		ribbonMat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader", MaterialFlags.DoubleSided );
		ribbonMat.Set( "Color", trackTex );

		// Anchor at the station's track-out connector (fall back to track-in / centre).
		var c = coaster.Shape.TrackOut ?? coaster.Shape.TrackIn ?? (coaster.TileW / 2, coaster.TileH / 2);
		tiles.Add( (coaster.TileX + c.X, coaster.TileY + c.Y) );
		rise.Add( 0f ); // the station connector stays at the base track height

		// The entry connector (if any, and distinct from the anchor) is where laying back closes the loop.
		if ( coaster.Shape.TrackIn is { } ti )
		{
			var inTile = (coaster.TileX + ti.X, coaster.TileY + ti.Y);
			if ( inTile != tiles[0] )
				trackInTile = inTile;
		}

		// The authored track cross-section profile (extruded along the centre-line for authentic rail
		// geometry; falls back to a procedural bed+rails if absent). Decoded from coaster.sam.
		crossSection = LoadCrossSection( coaster.Archive );

		// The train hides itself until at least one segment is laid (path has < 2 points).
		train = new CoasterTrain( this, grid.TileSize, coaster.Archive );
		coaster.Train = train; // peeps boarding this coaster ride the train in view (T-045 3b)
		coaster.Track = this;  // so selling the coaster tears the track down too (T-041)
	}

	/// <summary>The track centre-line as elevated world points (one per laid tile) — the raw control path.</summary>
	public List<Vector3> WorldPath()
	{
		var pts = new List<Vector3>( tiles.Count );
		for ( int i = 0; i < tiles.Count; i++ )
		{
			var (tx, ty) = tiles[i];
			var c = grid.TileToWorld( tx, ty );
			pts.Add( c.WithZ( terrain.SampleHeight( c.X, c.Y ) + TrackHeight + rise[i] ) );
		}
		return pts;
	}

	/// <summary>The ridden centre-line: a Catmull-Rom spline through the control points (closed into a
	/// ring when the circuit is complete). Both the train and the rendered ribbon follow this.</summary>
	public List<Vector3> SmoothedPath()
	{
		var ctrl = WorldPath();
		if ( ctrl.Count < 2 )
			return ctrl;
		var path = Smooth( ctrl, IsClosed, 8 );
		if ( IsClosed )
			path.Add( path[0] ); // close the ring so the last edge wraps back to the start
		return path;
	}

	/// <summary>Can the track be extended onto tile (tx,ty)? (on-grid, 4-adjacent to the head, no overlap,
	/// and not already a closed circuit). Laying onto the station's entry connector is allowed — it closes
	/// the loop.</summary>
	public bool CanExtend( int tx, int ty )
	{
		if ( IsClosed || !grid.InBounds( tx, ty ) || tiles.Contains( (tx, ty) ) )
			return false;
		var (hx, hy) = Head;
		return Math.Abs( tx - hx ) + Math.Abs( ty - hy ) == 1;
	}

	public bool Extend( int tx, int ty )
	{
		if ( !CanExtend( tx, ty ) )
			return false;
		tiles.Add( (tx, ty) );
		rise.Add( rise[^1] ); // a new segment continues at the current head's height (build a slope by raising first)
		if ( (tx, ty) == trackInTile )
		{
			IsClosed = true;  // laid back to the station entry — the circuit is complete
			rise[^1] = 0f;     // the entry connector meets the station at the base height
		}
		SpawnPylon( tx, ty, rise[^1] );
		RebuildRibbon();
		return true;
	}

	/// <summary>Raise (<paramref name="dir"/> &gt; 0) or lower the head segment a step (T-045 3b
	/// STACKUP/STACKDOWN), so the player can build hills. Clamped so the track stays above ground; a closed
	/// circuit's closing tile is fixed at the station height and can't be moved.</summary>
	public bool StackHead( int dir )
	{
		if ( tiles.Count <= 1 || IsClosed )
			return false;
		float step = TrackHeight * 0.5f;
		float next = Math.Clamp( rise[^1] + dir * step, -TrackHeight * 0.6f, TrackHeight * 4f );
		if ( MathF.Abs( next - rise[^1] ) < 1e-3f )
			return false;
		rise[^1] = next;
		if ( pylons.Count > 0 )
			PositionPylon( pylons[^1], Head.X, Head.Y, rise[^1] );
		RebuildRibbon();
		return true;
	}

	/// <summary>Remove the last laid segment (keeps the station anchor).</summary>
	public void Backtrack()
	{
		if ( tiles.Count <= 1 )
			return;
		IsClosed = false; // removing the head reopens a closed circuit (the head was the closing tile)
		tiles.RemoveAt( tiles.Count - 1 );
		rise.RemoveAt( rise.Count - 1 );
		if ( pylons.Count > 0 )
		{
			Entity.All.Remove( pylons[^1] );
			pylons.RemoveAt( pylons.Count - 1 );
		}
		RebuildRibbon();
	}

	/// <summary>The laid tiles (the station anchor at [0], then one per segment) — captured by save/load (T-059).</summary>
	public IReadOnlyList<(int X, int Y)> Tiles => tiles;

	/// <summary>The per-tile height offset above the base track height (STACKUP/DOWN), parallel to <see cref="Tiles"/>.</summary>
	public IReadOnlyList<float> Rises => rise;

	/// <summary>Rebuild a laid track from a save (T-059): replay the saved tiles past the station anchor (which the
	/// constructor already placed), then apply the saved per-tile heights (Extend continues at the head height, so
	/// the STACKUP/DOWN rises are restored here) and the closed-circuit flag. Tiles that no longer extend cleanly
	/// (a corrupt/edited save) are skipped, so a bad save can't throw.</summary>
	public void Restore( IReadOnlyList<(int X, int Y)> savedTiles, IReadOnlyList<float> savedRises, bool closed )
	{
		for ( int i = 1; i < savedTiles.Count; i++ )
			Extend( savedTiles[i].X, savedTiles[i].Y );

		// Apply the saved heights (Extend laid each segment at the head height; STACKUP/DOWN weren't replayed).
		for ( int i = 0; i < rise.Count && i < savedRises.Count; i++ )
		{
			rise[i] = savedRises[i];
			if ( i >= 1 && i - 1 < pylons.Count )
				PositionPylon( pylons[i - 1], tiles[i].X, tiles[i].Y, rise[i] );
		}
		IsClosed = closed;
		RebuildRibbon();
	}

	/// <summary>Remove all laid segments, the ribbon and the train (called when the track is abandoned).</summary>
	public void Despawn()
	{
		foreach ( var p in pylons )
			Entity.All.Remove( p );
		pylons.Clear();
		if ( ribbon != null )
			Entity.All.Remove( ribbon );
		ribbon = null;
		train.Despawn();
		if ( coaster.Train == train )
			coaster.Train = null;
	}

	// A slim grey pillar from the ground up to the (possibly raised) track, under the laid tile's centre.
	private void SpawnPylon( int tx, int ty, float riseVal )
	{
		var pylonMat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		pylonMat.Set( "Color", new Texture( [90, 90, 105, 255], 1, 1 ) );
		var e = new ModelEntity { Model = Primitives.Cube.GenerateModel( pylonMat ) };
		PositionPylon( e, tx, ty, riseVal );
		pylons.Add( e );
	}

	// Size + place a pylon so it spans the ground up to the track top at this tile's height.
	private void PositionPylon( ModelEntity e, int tx, int ty, float riseVal )
	{
		var c = grid.TileToWorld( tx, ty );
		float gz = terrain.SampleHeight( c.X, c.Y );
		float h = MathF.Max( 2f, TrackHeight + riseVal );
		e.Position = c.WithZ( gz + h / 2f );
		e.Scale = new Vector3( 1.5f, 1.5f, h / 2f );
	}

	// Authentic-ish track profile (T-045 slice 3b): a continuous track bed carrying two raised running rails
	// along the ridden centre-line with periodic cross-ties (sleepers), replacing the old flat textured
	// strip — so a built track reads as a real coaster track. The train still runs the centre-line.
	private const float RailGauge = 0.16f;   // ×TileSize: half-distance between the two rails
	private const float RailHalfWidth = 0.03f; // ×TileSize: half-width of a rail strip
	private const float RailRise = 0.05f;      // ×TileSize: rails sit a little above the tie deck
	private const float TieHalfLength = 0.22f; // ×TileSize: cross-tie reach along travel
	private const int TieEvery = 4;            // a cross-tie every N spline points (~2 ties / tile at sub=8)

	// Track banking (T-052): on a curve the swept frame rolls about the travel tangent so the track tilts
	// into the turn — the spline-track realization of the original's per-segment rotation. The bank angle is
	// proportional to each point's heading change (curvature), clamped, then smoothed so it eases through the
	// curve instead of snapping at the control points.
	private const float BankGain = 1.6f;  // heading-change (rad) → roll (rad) multiplier
	private const float MaxBank = 0.6f;   // ≈34° cap on the roll
	private const int BankSmooth = 2;     // ± window (spline points) of the bank moving-average

	// The track's texture, named by its own coaster.sam (T-072 follow-up): it used to be hard-coded to jungle's
	// Trak_sec2, which other themes don't ship — their track drew as an error texture.
	private static Texture LoadTrackTexture( string archive )
	{
		try
		{
			using var s = FileSystem.OpenRead( $"{archive}/coaster.sam" );
			if ( ParseTrackTexture( new SettingsFile( s ) ) is { } name )
				return new Texture( $"{archive}/gtexture/{name}.wct", TextureFlags.Repeat );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[coaster] track texture unavailable for {archive}: {e.Message}" );
		}
		return Texture.Missing;
	}

	// The texture most of cross-section 1's edges use (asCrossSectionEdges1[*].usTexture, default 0) →
	// asTextureData[n].pcTextureFilename, without its quotes and extension (the WAD holds it as .wct).
	// The ribbon is drawn with a single texture, so the dominant one stands for the whole profile.
	internal static string? ParseTrackTexture( SettingsFile settings )
	{
		var uses = new Dictionary<int, int>();
		for ( int i = 0; ; i++ )
		{
			var t = settings[$"asCrossSectionEdges1[{i}].usTexture"];
			if ( string.IsNullOrEmpty( t ) )
				break;
			int index = int.TryParse( t, out var v ) ? v : 0;
			uses[index] = uses.GetValueOrDefault( index ) + 1;
		}
		int dominant = uses.Count > 0 ? uses.OrderByDescending( kv => kv.Value ).ThenBy( kv => kv.Key ).First().Key : 0;

		var file = settings[$"asTextureData[{dominant}].pcTextureFilename"];
		if ( string.IsNullOrEmpty( file ) )
			return null;
		return Path.GetFileNameWithoutExtension( file.Trim( '"', ' ' ) );
	}

	// The authored track cross-section: the 2D profile (X across, Y up, U texcoord) the original sweeps
	// along the track centre-line, read from coaster.sam's asCrossSectionPoints1[*] (plain settings text).
	// This is the real rail/channel silhouette (e.g. ±4 wide, rails at ±3, dipping to centre −1.5).
	private static List<(float X, float Y, float U)> LoadCrossSection( string archive )
	{
		try
		{
			using var s = FileSystem.OpenRead( $"{archive}/coaster.sam" );
			if ( s == null )
				return new();
			var pts = ParseCrossSection( new SettingsFile( s ) );
			Log.Info( $"[coaster] loaded {pts.Count} cross-section point(s) from {archive}/coaster.sam" );
			return pts;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[coaster] cross-section load failed: {e.Message}" );
			return new();
		}
	}

	// Pure parse of asCrossSectionPoints1[*] (fX/fY/fU) from a loaded settings file — split out so it can
	// be unit-tested without the VFS. Stops at the first missing index.
	internal static List<(float X, float Y, float U)> ParseCrossSection( SettingsFile settings )
	{
		var pts = new List<(float, float, float)>();
		for ( int i = 0; ; i++ )
		{
			var xs = settings[$"asCrossSectionPoints1[{i}].fX"];
			if ( string.IsNullOrEmpty( xs ) )
				break;
			pts.Add( (Flt( xs ), Flt( settings[$"asCrossSectionPoints1[{i}].fY"] ), Flt( settings[$"asCrossSectionPoints1[{i}].fU"] )) );
		}
		return pts;
	}

	private static float Flt( string? s ) =>
		float.TryParse( s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v ) ? v : 0f;

	private void RebuildRibbon()
	{
		var pts = SmoothedPath();
		if ( pts.Count < 2 )
		{
			if ( ribbon != null ) ribbon.Model = null;
			return;
		}

		float ts = grid.TileSize;
		float gauge = ts * RailGauge, railHalf = ts * RailHalfWidth, rise = ts * RailRise, tieLen = ts * TieHalfLength;

		// A horizontal frame (position + perpendicular-to-travel) at each centre-line point.
		var pos = new Vector3[pts.Count];
		var perp = new Vector3[pts.Count];
		var tan = new Vector3[pts.Count];
		for ( int i = 0; i < pts.Count; i++ )
		{
			var prev = pts[Math.Max( i - 1, 0 )];
			var next = pts[Math.Min( i + 1, pts.Count - 1 )];
			var dir = (next - prev).Normal;
			pos[i] = pts[i];
			perp[i] = new Vector3( -dir.Y, dir.X, 0f ).Normal;
			tan[i] = dir;
		}

		// Bank the frame (T-052): per-point curvature → a roll angle, smoothed, then the (perp, up) axes are
		// rotated about the tangent so the swept profile tilts into the turn. `right`/`up` replace the flat
		// `perp`/world-up below; on a straight they equal them, so a straight track is unchanged.
		var bank = new float[pts.Count];
		for ( int i = 0; i < pts.Count; i++ )
		{
			var tin = (pos[i] - pos[Math.Max( i - 1, 0 )]).Normal;
			var tout = (pos[Math.Min( i + 1, pts.Count - 1 )] - pos[i]).Normal;
			bank[i] = BankAngle( tin, tout, BankGain, MaxBank );
		}
		var right = new Vector3[pts.Count];
		var up = new Vector3[pts.Count];
		for ( int i = 0; i < pts.Count; i++ )
		{
			// Smooth the bank over a small window so it eases through the curve.
			float sum = 0f; int cnt = 0;
			for ( int k = -BankSmooth; k <= BankSmooth; k++ )
			{
				int j = i + k;
				if ( j < 0 || j >= pts.Count ) continue;
				sum += bank[j]; cnt++;
			}
			float theta = cnt > 0 ? sum / cnt : 0f;

			var right0 = perp[i];                              // horizontal, ⟂ travel
			var up0 = Vector3.Cross( tan[i], right0 ).Normal;  // ≈ world-up on the flat, tilts on slopes
			float c = MathF.Cos( theta ), s = MathF.Sin( theta );
			right[i] = (right0 * c + up0 * s).Normal;          // roll the frame about the tangent
			up[i] = (up0 * c - right0 * s).Normal;
		}

		var verts = new List<Vertex>();
		var idx = new List<uint>();

		if ( crossSection.Count >= 2 )
		{
			// Sweep the authored cross-section (coaster.sam) along the centre-line: each profile point is
			// placed at perp·X + up·Y in the track frame, and consecutive profile edges × length segments
			// are stitched into quads → the real rail/channel surface.
			float maxAbsX = 0f;
			foreach ( var p in crossSection )
				maxAbsX = MathF.Max( maxAbsX, MathF.Abs( p.X ) );
			float scale = maxAbsX > 1e-3f ? ts * 0.30f / maxAbsX : 1f; // fit the profile to ~0.6 tile wide
			int m = crossSection.Count;

			float cum = 0f;
			for ( int i = 0; i < pts.Count; i++ )
			{
				if ( i > 0 ) cum += pts[i].Distance( pts[i - 1] );
				float vTex = cum / ts;
				foreach ( var p in crossSection )
				{
					var world = pos[i] + right[i] * (p.X * scale) + up[i] * (p.Y * scale);
					verts.Add( new Vertex { Position = world, Normal = up[i], TexCoords = new Vector2( p.U, vTex ) } );
				}
			}
			for ( int i = 0; i < pts.Count - 1; i++ )
				for ( int j = 0; j < m - 1; j++ )
				{
					uint a = (uint)(i * m + j), b = a + 1, c = (uint)((i + 1) * m + j), d = c + 1;
					idx.Add( a ); idx.Add( b ); idx.Add( c );
					idx.Add( b ); idx.Add( d ); idx.Add( c );
				}
		}
		else
		{
			// Fallback (no authored profile): a procedural bed + two raised rails + periodic cross-ties.
			void AddStrip( float offset, float halfWidth, float lift )
			{
				uint baseIdx = (uint)verts.Count;
				float cum = 0f;
				for ( int i = 0; i < pts.Count; i++ )
				{
					if ( i > 0 ) cum += pts[i].Distance( pts[i - 1] );
					float v = cum / ts;
					var c = pos[i] + right[i] * offset + up[i] * lift;
					verts.Add( new Vertex { Position = c + right[i] * halfWidth, Normal = up[i], TexCoords = new Vector2( 0f, v ) } );
					verts.Add( new Vertex { Position = c - right[i] * halfWidth, Normal = up[i], TexCoords = new Vector2( 1f, v ) } );
				}
				for ( int i = 0; i < pts.Count - 1; i++ )
				{
					uint a = baseIdx + (uint)(2 * i), b = a + 1, c = a + 2, d = a + 3;
					idx.Add( a ); idx.Add( b ); idx.Add( c );
					idx.Add( b ); idx.Add( d ); idx.Add( c );
				}
			}
			float tieReach = gauge + railHalf * 2f;
			AddStrip( 0f, tieReach, 0f );
			AddStrip( -gauge, railHalf, rise );
			AddStrip( +gauge, railHalf, rise );

			float tieLift = rise * 0.5f;
			for ( int i = 0; i < pts.Count; i += TieEvery )
			{
				var step = tan[i] * tieLen;
				var l = pos[i] + right[i] * (-tieReach) + up[i] * tieLift;
				var r = pos[i] + right[i] * tieReach + up[i] * tieLift;
				uint bi = (uint)verts.Count;
				verts.Add( new Vertex { Position = l - step, Normal = Vector3.Up, TexCoords = new Vector2( 0f, 0f ) } );
				verts.Add( new Vertex { Position = r - step, Normal = Vector3.Up, TexCoords = new Vector2( 1f, 0f ) } );
				verts.Add( new Vertex { Position = l + step, Normal = Vector3.Up, TexCoords = new Vector2( 0f, 1f ) } );
				verts.Add( new Vertex { Position = r + step, Normal = Vector3.Up, TexCoords = new Vector2( 1f, 1f ) } );
				idx.Add( bi ); idx.Add( bi + 1 ); idx.Add( bi + 2 );
				idx.Add( bi + 1 ); idx.Add( bi + 3 ); idx.Add( bi + 2 );
			}
		}

		var model = new Model( verts.ToArray(), idx.ToArray(), ribbonMat );
		if ( ribbon == null )
			ribbon = new ModelEntity { Model = model };
		else
			ribbon.Model = model;
	}

	/// <summary>The signed bank (roll) angle for a track point whose incoming/outgoing travel directions are
	/// <paramref name="tin"/>/<paramref name="tout"/>: the horizontal heading change (curvature) scaled by
	/// <paramref name="gain"/> and clamped to ±<paramref name="maxBank"/>. Zero on a straight; opposite signs
	/// for left vs right turns so the track tilts into the curve. Pure (unit-tested, T-052).</summary>
	internal static float BankAngle( Vector3 tin, Vector3 tout, float gain, float maxBank )
	{
		// Signed turn about the vertical axis, from the horizontal components of the two tangents.
		float cross = tin.X * tout.Y - tin.Y * tout.X; // + = left turn, - = right turn
		float dot = tin.X * tout.X + tin.Y * tout.Y;
		float turn = MathF.Atan2( cross, dot );
		return Math.Clamp( turn * gain, -maxBank, maxBank );
	}

	// Catmull-Rom through the control points → a dense, curved centre-line. For a closed ring the
	// neighbours wrap; for an open track the endpoints are clamped. Each control segment is subdivided
	// into <sub> points (start-inclusive, end-exclusive); the open case appends the final endpoint.
	private static List<Vector3> Smooth( IReadOnlyList<Vector3> p, bool closed, int sub )
	{
		int n = p.Count;
		if ( n < 3 )
			return new List<Vector3>( p ); // too few points to curve — keep it straight

		var outp = new List<Vector3>( (closed ? n : n - 1) * sub + 1 );
		int segs = closed ? n : n - 1;
		for ( int i = 0; i < segs; i++ )
		{
			var p0 = p[closed ? (i - 1 + n) % n : Math.Max( i - 1, 0 )];
			var p1 = p[i % n];
			var p2 = p[(i + 1) % n];
			var p3 = p[closed ? (i + 2) % n : Math.Min( i + 2, n - 1 )];
			for ( int s = 0; s < sub; s++ )
				outp.Add( CatmullRom( p0, p1, p2, p3, (float)s / sub ) );
		}
		if ( !closed )
			outp.Add( p[n - 1] );
		return outp;
	}

	private static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		float t2 = t * t, t3 = t2 * t;
		return 0.5f * (p1 * 2f
			+ (p2 - p0) * t
			+ (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2
			+ (p1 * 3f - p0 - p2 * 3f + p3) * t3);
	}
}
