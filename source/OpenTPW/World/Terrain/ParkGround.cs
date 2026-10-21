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

	/// <summary>Ground texture pixels per world unit: a 192-px texture repeats every 64 units (the LOD 0 tile size).</summary>
	public const float TexelsPerUnit = 3f;

	/// <summary>Resolution of the composed ground texture.</summary>
	public const float OutputPixelsPerUnit = 2f;

	// Just under the scenery meshes that lie flat at Z = 0 (paths, fence feet), so they win the depth test.
	private const float GroundZ = -0.1f;

	public float Width { get; }
	public float Depth { get; }

	public ParkGround( string terrainPath )
	{
		LndFile lnd;
		using ( var stream = FileSystem.OpenRead( $"{terrainPath}/base.lnd" ) )
			lnd = new LndFile( stream );
		Width = lnd.BlendMap.Width * UnitsPerMapPixel;
		Depth = lnd.BlendMap.Height * UnitsPerMapPixel;

		int w = (int)(Width * OutputPixelsPerUnit), h = (int)(Depth * OutputPixelsPerUnit);
		var texture = new Texture( Compose( lnd, w, h ), w, h );
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
			Normal = new Vector3( 0, 0, 1 ),
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
	/// Composes the ground into a <paramref name="width"/>×<paramref name="height"/> RGBA image covering the whole
	/// blend map (row 0 = the far edge). Each pixel mixes the dirt, grass and foliage families by the map's R/G/B
	/// weights (bilinear), each family the average of its two LOD 0 textures tiled at <see cref="TexelsPerUnit"/>;
	/// alpha is the map's ground mask, so the scenery holes stay transparent.
	/// The exact original blend (it also has 7 masks, not used here) is still to be reversed.
	/// </summary>
	public static byte[] Compose( LndFile lnd, int width, int height )
	{
		var map = lnd.BlendMap;
		var tex = lnd.Levels[0].Textures;
		float worldW = map.Width * UnitsPerMapPixel, worldH = map.Height * UnitsPerMapPixel;
		var rgba = new byte[width * height * 4];
		Span<float> weight = stackalloc float[4];

		for ( int oy = 0; oy < height; oy++ )
		{
			float wy = (oy + 0.5f) * worldH / height; // world units from the far edge
			float my = wy / UnitsPerMapPixel - 0.5f;

			for ( int ox = 0; ox < width; ox++ )
			{
				float wx = (ox + 0.5f) * worldW / width;
				float mx = wx / UnitsPerMapPixel - 0.5f;
				SampleMap( map, mx, my, weight );

				float sum = weight[1] + weight[2] + weight[3];
				if ( sum <= 0f )
				{
					weight[2] = 1f; // unweighted ground: plain grass
					sum = 1f;
				}

				float r = 0f, g = 0f, b = 0f;
				for ( int family = 0; family < 3; family++ )
				{
					float k = weight[1 + family] / sum * 0.5f;
					if ( k <= 0f )
						continue;
					for ( int variant = 0; variant < 2; variant++ )
					{
						var t = tex[family * 2 + variant];
						int tx = Wrap( (int)(wx * TexelsPerUnit), t.Width );
						int ty = Wrap( (int)(wy * TexelsPerUnit), t.Height );
						r += k * t.At( tx, ty, 1 );
						g += k * t.At( tx, ty, 2 );
						b += k * t.At( tx, ty, 3 );
					}
				}

				int o = (oy * width + ox) * 4;
				rgba[o] = (byte)Math.Clamp( r, 0f, 255f );
				rgba[o + 1] = (byte)Math.Clamp( g, 0f, 255f );
				rgba[o + 2] = (byte)Math.Clamp( b, 0f, 255f );
				rgba[o + 3] = (byte)Math.Clamp( weight[0], 0f, 255f );
			}
		}

		return rgba;
	}

	private static int Wrap( int v, int n ) => ((v % n) + n) % n;

	// Bilinear ARGB sample of the blend map at a continuous pixel position (clamped at the edges).
	private static void SampleMap( LndImage map, float x, float y, Span<float> argb )
	{
		int x0 = (int)MathF.Floor( x ), y0 = (int)MathF.Floor( y );
		float fx = x - x0, fy = y - y0;
		int xa = Math.Clamp( x0, 0, map.Width - 1 ), xb = Math.Clamp( x0 + 1, 0, map.Width - 1 );
		int ya = Math.Clamp( y0, 0, map.Height - 1 ), yb = Math.Clamp( y0 + 1, 0, map.Height - 1 );
		for ( int c = 0; c < 4; c++ )
		{
			float top = map.At( xa, ya, c ) * (1 - fx) + map.At( xb, ya, c ) * fx;
			float bottom = map.At( xa, yb, c ) * (1 - fx) + map.At( xb, yb, c ) * fx;
			argb[c] = top * (1 - fy) + bottom * fy;
		}
	}
}
