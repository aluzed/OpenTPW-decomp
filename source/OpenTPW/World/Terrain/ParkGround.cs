namespace OpenTPW;

/// <summary>
/// The park floor (T-071): the grass/dirt/foliage ground layer from the level's <c>base.lnd</c>, drawn as one flat
/// textured quad under the scenery of <see cref="ParkTerrain"/>. The terrain's <c>base.MD2</c> has no floor, so
/// without this the park interior shows the sky through the ground.
///
/// Placement (measured, see T-071): the blend map covers the park interior from world (0,0) at
/// <see cref="UnitsPerMapPixel"/> world units per map pixel (4 pixels per 10-unit tile) — on jungle 384×344 →
/// 960×860, exactly the extent of the park fence (<c>jho_fol*</c>), with the river/lake/entrance holes landing on
/// their meshes. Map row 0 is the far (max-Y) edge. The ground sits at Z = 0, the level of the fence and paths.
/// </summary>
public sealed class ParkGround
{
	/// <summary>World units per blend-map pixel.</summary>
	public const float UnitsPerMapPixel = 2.5f;

	/// <summary>The <c>.lnd</c> detail level composed: LOD 1, 32 pixels per tile (3072×2752 for a 960×860 park) —
	/// LOD 0's 64 would need a 135 MB texture.</summary>
	public const int DetailLevel = 1;

	/// <summary>World Z of the park floor — the level of the fence feet and paths.</summary>
	public const float SurfaceZ = 0f;

	// Just under the scenery meshes that lie flat at Z = 0 (paths, fence feet), so they win the depth test.
	private const float GroundZ = SurfaceZ - 0.1f;

	public float Width { get; }
	public float Depth { get; }

	private readonly LndImage blendMap;

	/// <summary>
	/// True where the blend map has no ground (alpha below half) — the river, lake and entrance holes the scenery
	/// meshes fill — or outside the map. World XY in, from the map's (0,0) corner.
	/// </summary>
	public bool IsScenery( float x, float y )
	{
		int px = (int)MathF.Floor( x / UnitsPerMapPixel );
		int py = (int)MathF.Floor( (Depth - y) / UnitsPerMapPixel );
		if ( px < 0 || py < 0 || px >= blendMap.Width || py >= blendMap.Height )
			return true;
		return blendMap.At( px, py ) < 128;
	}

	public ParkGround( string terrainPath, ParkTerrain? scenery = null )
	{
		LndFile lnd;
		using ( var stream = FileSystem.OpenRead( $"{terrainPath}/base.lnd" ) )
			lnd = new LndFile( stream );
		blendMap = scenery != null ? FillUncoveredHoles( lnd.BlendMap, scenery.Triangles ) : lnd.BlendMap;
		Width = lnd.BlendMap.Width * UnitsPerMapPixel;
		Depth = lnd.BlendMap.Height * UnitsPerMapPixel;

		var rgba = Compose( lnd.Header, blendMap, lnd.Levels[DetailLevel], out int w, out int h );
		var texture = new Texture( rgba, w, h );
		Log.Info( $"[ground] {terrainPath}/base.lnd: {Width:0}×{Depth:0} units, composed {w}×{h}" );

		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided );
		var textures = new Texture[16];
		textures[0] = texture;
		for ( int i = 1; i < textures.Length; i++ )
			textures[i] = Texture.Missing;
		material.Set( "Color", textures );

		// Texture row 0 is the far edge (max Y), like the blend map; test.shader flips V (v' = 1 - v), so the far
		// edge gets v = 1 here.
		Vertex Corner( float x, float y, float u, float v ) => new()
		{
			Position = new Vector3( x, y, GroundZ ),
			Normal = new Vector3( 0, 1, 0 ), // up, in the MD2 (Y-up) convention test.shader expects
			TexCoords = new Vector2( u, v )
		};
		var vertices = new[]
		{
			Corner( 0, Depth, 0, 1 ), Corner( Width, Depth, 1, 1 ),
			Corner( 0, 0, 0, 0 ), Corner( Width, 0, 1, 0 )
		};
		_ = new ModelEntity { Model = new Model( vertices, [0, 2, 1, 1, 2, 3], material ) };
	}

	/// <summary>
	/// Composes the ground into an RGBA image covering the whole blend map (row 0 = the far edge), at
	/// <paramref name="level"/>'s resolution: one <see cref="LndLevel.TileSize"/>-pixel square per 10-unit tile.
	/// This is the original's tile generator (<c>FUN_0055f780</c>, blends <c>FUN_0055eaa0</c>/<c>FUN_0055ee10</c>,
	/// ramps <c>FUN_0055e710</c>, loader <c>FUN_00560ff0</c>):
	/// <list type="bullet">
	/// <item>The blend map is upsampled bilinearly, a map pixel sitting on its top-left corner (<c>FUN_0055e780</c>).</item>
	/// <item>Each family mixes its two textures by <c>ramp_f[(mask_f · R) &gt;&gt; 8]</c>: dirt = T0/T1 by M0,
	/// grass = T2/T3 by M1, foliage = T4/T5 by M2 — R is the "variation" channel.</item>
	/// <item>Grass covers dirt by <c>max(ramp_g[(M3 · G) &gt;&gt; 8], ramp_g[(M4 · G) &gt;&gt; 8])</c>, foliage covers both
	/// by the same with B, M5 and M6 — the masks break the edges into clumps.</item>
	/// <item>The five ramps are the header's (centre, width) byte pairs 1–2 (dirt), 3–4 (grass), 5–6 (grass cover),
	/// 7–8 (foliage), 9–10 (foliage cover); see <see cref="Ramp"/>.</item>
	/// <item>Tile (x, y) reads every image from texel <c>(TileSize · x) mod (size − TileSize)</c>, so the images of
	/// different sizes never repeat together.</item>
	/// </list>
	/// Alpha is the map's ground mask, so the scenery holes stay transparent (the original greys those tiles out).
	/// </summary>
	public static byte[] Compose( LndFile lnd, int level, out int width, out int height ) =>
		Compose( lnd.Header, lnd.BlendMap, lnd.Levels[level], out width, out height );

	/// <inheritdoc cref="Compose(LndFile, int, out int, out int)"/>
	public static byte[] Compose( byte[] header, LndImage map, LndLevel level, out int width, out int height )
	{
		int tile = level.TileSize;
		int tilesX = map.Width / MapPixelsPerTile, tilesY = map.Height / MapPixelsPerTile;
		int w = width = tilesX * tile, h = height = tilesY * tile;
		var rgba = new byte[w * h * 4];

		byte H( int i ) => i < header.Length ? header[i] : (byte)0;
		var dirtRamp = Ramp( H( 1 ), H( 2 ) );
		var grassRamp = Ramp( H( 3 ), H( 4 ) );
		var grassCover = Ramp( H( 5 ), H( 6 ) );
		var foliageRamp = Ramp( H( 7 ), H( 8 ) );
		var foliageCover = Ramp( H( 9 ), H( 10 ) );
		var tex = level.Textures;
		var mask = level.Masks;

		// Rows are independent: compose them in parallel.
		Parallel.For( 0, h, oy =>
		{
			int ty = oy / tile, ly = oy % tile;
			float my = ty * MapPixelsPerTile + ly * (float)MapPixelsPerTile / tile;
			for ( int ox = 0; ox < w; ox++ )
			{
				int tx = ox / tile, lx = ox % tile;
				float mx = tx * MapPixelsPerTile + lx * (float)MapPixelsPerTile / tile;
				int alpha = SampleMap( map, mx, my, 0 );
				int variation = SampleMap( map, mx, my, 1 );
				int grass = SampleMap( map, mx, my, 2 );
				int foliage = SampleMap( map, mx, my, 3 );

				int M( int i ) => Texel( mask[i], tile, tx, ty, lx, ly, 0 );
				int wGrass = Math.Max( grassCover[(M( 3 ) * grass) >> 8], grassCover[(grass * M( 4 )) >> 8] );
				int wFoliage = Math.Max( foliageCover[(foliage * M( 6 )) >> 8], foliageCover[(M( 5 ) * foliage) >> 8] );
				int tDirt = dirtRamp[(M( 0 ) * variation) >> 8];
				int tGrass = grassRamp[(M( 1 ) * variation) >> 8];
				int tFoliage = foliageRamp[(M( 2 ) * variation) >> 8];

				int o = (oy * w + ox) * 4;
				for ( int c = 0; c < 3; c++ )
				{
					int Mix( int a, int b, int t ) => b + (((a - b) * t) >> 8);
					int T( int i ) => Texel( tex[i], tile, tx, ty, lx, ly, 1 + c );
					int dirt = Mix( T( 0 ), T( 1 ), tDirt );
					int grassColour = Mix( T( 2 ), T( 3 ), tGrass );
					int foliageColour = Mix( T( 4 ), T( 5 ), tFoliage );
					int ground = Mix( foliageColour, Mix( grassColour, dirt, wGrass ), wFoliage );
					rgba[o + c] = (byte)Math.Clamp( ground, 0, 255 );
				}
				rgba[o + 3] = (byte)alpha;
			}
		} );

		return rgba;
	}

	/// <summary>Blend-map pixels per 10-unit tile.</summary>
	public const int MapPixelsPerTile = 4;

	/// <summary>
	/// The original's 256-entry ramp (<c>FUN_0055e710</c>): 0 below <c>centre − ⌈width/2⌉</c>, 255 above
	/// <c>centre + ⌈width/2⌉</c>, linear in between (over width + 1 steps).
	/// </summary>
	public static byte[] Ramp( int centre, int width )
	{
		int half = (width + 1) >> 1, lo = centre - half, hi = centre + half;
		var ramp = new byte[256];
		for ( int i = 0; i < 256; i++ )
			ramp[i] = i < lo ? (byte)0 : i > hi ? (byte)255 : (byte)Math.Clamp( 255 * (i - lo) / (width + 1), 0, 255 );
		return ramp;
	}

	// Byte `channel` of `image` for pixel (lx, ly) of tile (tx, ty): each tile starts at (tile · t) mod (size − tile).
	private static int Texel( LndImage image, int tile, int tx, int ty, int lx, int ly, int channel )
	{
		int px = image.Width > tile ? image.Width - tile : image.Width;
		int py = image.Height > tile ? image.Height - tile : image.Height;
		int x = (tile * tx % px + lx) % image.Width;
		int y = (tile * ty % py + ly) % image.Height;
		return image.At( x, y, channel );
	}

	/// <summary>
	/// A copy of <paramref name="map"/> where every ground hole (alpha below half) that no scenery triangle covers
	/// is turned back into ground (alpha 255). Some holes — the thin arms of the jungle river — have no mesh over
	/// them, so drawing them as holes shows the sky through the park (T-072). Triangles are world XY.
	/// </summary>
	public static LndImage FillUncoveredHoles( LndImage map, IEnumerable<(System.Numerics.Vector2 A, System.Numerics.Vector2 B, System.Numerics.Vector2 C)> triangles )
	{
		float depth = map.Height * UnitsPerMapPixel;
		var covered = new bool[map.Width * map.Height];
		foreach ( var (a, b, c) in triangles )
		{
			// To map pixel space (row 0 = max Y).
			System.Numerics.Vector2 P( System.Numerics.Vector2 v ) => new( v.X / UnitsPerMapPixel, (depth - v.Y) / UnitsPerMapPixel );
			var (pa, pb, pc) = (P( a ), P( b ), P( c ));
			int x0 = Math.Max( 0, (int)MathF.Floor( MathF.Min( pa.X, MathF.Min( pb.X, pc.X ) ) ) );
			int x1 = Math.Min( map.Width - 1, (int)MathF.Floor( MathF.Max( pa.X, MathF.Max( pb.X, pc.X ) ) ) );
			int y0 = Math.Max( 0, (int)MathF.Floor( MathF.Min( pa.Y, MathF.Min( pb.Y, pc.Y ) ) ) );
			int y1 = Math.Min( map.Height - 1, (int)MathF.Floor( MathF.Max( pa.Y, MathF.Max( pb.Y, pc.Y ) ) ) );
			for ( int y = y0; y <= y1; y++ )
				for ( int x = x0; x <= x1; x++ )
					if ( !covered[y * map.Width + x] && Inside( new( x + 0.5f, y + 0.5f ), pa, pb, pc ) )
						covered[y * map.Width + x] = true;
		}

		var pixels = (byte[])map.Pixels.Clone();
		for ( int i = 0; i < covered.Length; i++ )
			if ( pixels[i * 4] < 128 && !covered[i] )
				pixels[i * 4] = 255;
		return map with { Pixels = pixels };
	}

	// Point-in-triangle, either winding (edges on the boundary count as inside).
	private static bool Inside( System.Numerics.Vector2 p, System.Numerics.Vector2 a, System.Numerics.Vector2 b, System.Numerics.Vector2 c )
	{
		static float Cross( System.Numerics.Vector2 o, System.Numerics.Vector2 u, System.Numerics.Vector2 v ) =>
			(u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
		float d1 = Cross( a, b, p ), d2 = Cross( b, c, p ), d3 = Cross( c, a, p );
		bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
		return !(neg && pos);
	}

	// Bilinear sample of one blend-map channel, map pixels on integer positions (clamped at the edges).
	private static int SampleMap( LndImage map, float x, float y, int channel )
	{
		int x0 = (int)MathF.Floor( x ), y0 = (int)MathF.Floor( y );
		float fx = x - x0, fy = y - y0;
		int xa = Math.Clamp( x0, 0, map.Width - 1 ), xb = Math.Clamp( x0 + 1, 0, map.Width - 1 );
		int ya = Math.Clamp( y0, 0, map.Height - 1 ), yb = Math.Clamp( y0 + 1, 0, map.Height - 1 );
		float top = map.At( xa, ya, channel ) * (1 - fx) + map.At( xb, ya, channel ) * fx;
		float bottom = map.At( xa, yb, channel ) * (1 - fx) + map.At( xb, yb, channel ) * fx;
		return (int)(top * (1 - fy) + bottom * fy);
	}
}
