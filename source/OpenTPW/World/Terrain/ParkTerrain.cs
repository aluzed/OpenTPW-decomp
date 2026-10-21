using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The real park terrain: the level's <c>base.md2</c> from <c>terrain.wad</c> (the jungle landscape —
/// 272 meshes, the height baked into the geometry). Rendered with the <see cref="LobbyIsland"/>
/// pattern (per-mesh material + <c>.wct</c> textures from the WAD's <c>stexture/</c> + <c>spathtex/</c>
/// folders). Also keeps the world-space vertices so rides can be dropped onto the surface via
/// <see cref="SampleHeight"/>. The placement grid (<see cref="PlacementGrid"/>) maps tiles onto the
/// terrain's XY extent (<see cref="Min"/>/<see cref="Max"/>).
/// </summary>
public sealed class ParkTerrain : Entity
{
	private readonly List<OpenTPW.Vector3> worldVerts = new();

	/// <summary>World-space XY bounds of the terrain (Z is height).</summary>
	public OpenTPW.Vector3 Min { get; private set; } = new( float.MaxValue );
	public OpenTPW.Vector3 Max { get; private set; } = new( float.MinValue );

	/// <summary>World-space XY of every terrain triangle — the top-down footprint of the scenery, used to tell
	/// which <c>base.lnd</c> ground holes a mesh actually fills (T-072).</summary>
	public IReadOnlyList<(System.Numerics.Vector2 A, System.Numerics.Vector2 B, System.Numerics.Vector2 C)> Triangles => triangles;
	private readonly List<(System.Numerics.Vector2, System.Numerics.Vector2, System.Numerics.Vector2)> triangles = new();

	/// <summary>Average vertex position — a robust centre that ignores stray distant scenery meshes.</summary>
	public OpenTPW.Vector3 Centroid { get; private set; }

	public ParkTerrain( string terrainPath )
	{
		var modelFile = new ModelFile( $"{terrainPath}/base.md2" );
		Log.Info( $"[terrain] {terrainPath}/base.md2: {modelFile.Meshes.Count} mesh(es)" );

		// Cache textures by name — the terrain reuses the same ground/path tiles across many meshes,
		// so without this we'd upload hundreds of duplicate GPU textures.
		var texCache = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );

		foreach ( var mesh in modelFile.Meshes )
		{
			// Double-sided: the terrain heightfield's triangle winding isn't uniform after the Y/Z
			// swizzle, so back-face culling would drop much of the ground (verified). See MaterialFlags.
			var textures = mesh.Materials.Select( m => LoadTexture( texCache, terrainPath, m.Name ) ).ToList();

			var vertices = new Vertex[mesh.Vertices.Length];
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
				vertices[i] = new Vertex
				{
					Position = new OpenTPW.Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags
				};

			Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			var mPos = new OpenTPW.Vector3( pos.X, pos.Z, pos.Y );
			var mRot = new Quaternion( rot.X, rot.Z, rot.Y, -rot.W );
			var mScl = new OpenTPW.Vector3( scl.X, scl.Z, scl.Y );

			// One model per group of ≤16 materials (MaterialSlots) — beyond 16 the shader draws solid magenta.
			foreach ( var model in MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided ) ) )
				_ = new ModelEntity { Model = model, Scale = mScl, Rotation = mRot, Position = mPos };

			// Record world-space vertex positions for height sampling + bounds.
			var modelMatrix = Matrix4x4.CreateScale( mScl.GetSystemVector3() )
				* Matrix4x4.CreateFromQuaternion( mRot )
				* Matrix4x4.CreateTranslation( mPos.GetSystemVector3() );
			var meshXY = new System.Numerics.Vector2[vertices.Length];
			for ( int vi = 0; vi < vertices.Length; vi++ )
			{
				var w = System.Numerics.Vector3.Transform( vertices[vi].Position.GetSystemVector3(), modelMatrix );
				meshXY[vi] = new System.Numerics.Vector2( w.X, w.Y );
				worldVerts.Add( new OpenTPW.Vector3( w.X, w.Y, w.Z ) );
				Min = new OpenTPW.Vector3( MathF.Min( Min.X, w.X ), MathF.Min( Min.Y, w.Y ), MathF.Min( Min.Z, w.Z ) );
				Max = new OpenTPW.Vector3( MathF.Max( Max.X, w.X ), MathF.Max( Max.Y, w.Y ), MathF.Max( Max.Z, w.Z ) );
			}
			for ( int t = 0; t + 2 < mesh.Indices.Length; t += 3 )
				triangles.Add( (meshXY[mesh.Indices[t]], meshXY[mesh.Indices[t + 1]], meshXY[mesh.Indices[t + 2]]) );
		}

		if ( worldVerts.Count > 0 )
		{
			var sum = new System.Numerics.Vector3();
			foreach ( var v in worldVerts )
				sum += v.GetSystemVector3();
			sum /= worldVerts.Count;
			Centroid = new OpenTPW.Vector3( sum.X, sum.Y, sum.Z );
		}

		Log.Info( $"[terrain] world bounds X[{Min.X:0},{Max.X:0}] Y[{Min.Y:0},{Max.Y:0}] Z[{Min.Z:0},{Max.Z:0}] centroid({Centroid.X:0},{Centroid.Y:0},{Centroid.Z:0})" );
	}

	// Terrain textures live in the WAD under stexture/ (scenery) or spathtex/ (paths); try both, then
	// fall back to the missing-texture placeholder so geometry still renders.
	private static Texture LoadTexture( Dictionary<string, Texture> cache, string terrainPath, string name )
	{
		name = name.Trim( '\0', ' ' );
		if ( cache.TryGetValue( name, out var cached ) )
			return cached;

		Texture result = Texture.Missing;
		foreach ( var sub in new[] { "stexture", "spathtex" } )
		{
			try { result = new Texture( $"{terrainPath}/{sub}/{name}.wct", TextureFlags.Repeat ); break; }
			catch { /* try next folder */ }
		}

		cache[name] = result;
		return result;
	}

	/// <summary>Terrain height (world Z) at world (<paramref name="x"/>,<paramref name="y"/>) — nearest vertex,
	/// through a spatial index (a linear scan of every vertex was 94% of a crowded park's frame time).</summary>
	public float SampleHeight( float x, float y )
	{
		nearest ??= new NearestVertexIndex( worldVerts.Select( v => (v.X, v.Y) ).ToList() );
		int i = nearest.Nearest( x, y );
		return i >= 0 ? worldVerts[i].Z : 0f;
	}

	private NearestVertexIndex? nearest;
}
