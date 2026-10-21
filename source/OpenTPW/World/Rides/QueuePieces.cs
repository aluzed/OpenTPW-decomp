namespace OpenTPW;

/// <summary>
/// The 3D queue-path pieces of a world's <c>queue.wad</c> (T-032 follow-up): <c>questra</c> (a straight run between
/// two railings), <c>queend</c> (the open end where visitors join, flanked by two torches), plus the bends and a dead
/// end (<c>quebnd1</c> and <c>quebnd2</c> are the same bend, joining the tile's +Y and +X edges). Each piece is one 10×10 tile authored corner-anchored with the path running along its +Y, its <c>base</c>
/// mesh being the path surface. The meshes are baked once per world (transform applied, centred on the tile) and
/// placed rotated along a ride's queue.
/// </summary>
public static class QueuePieces
{
	public const string Straight = "questra", End = "queend", Bend = "quebnd1";

	private const float PieceSize = 10f; // the authored tile

	private static readonly Dictionary<string, List<Model>?> cache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Forget the baked pieces (a level reload may switch worlds).</summary>
	public static void Reset() => cache.Clear();

	/// <summary>Place a piece on the tile centred at <paramref name="centre"/>, its +Y turned towards
	/// <paramref name="along"/> (a unit grid direction), scaled to <paramref name="tileSize"/>. Empty when the world
	/// has no such piece (the caller keeps its flat path then).</summary>
	public static List<ModelEntity> Place( string piece, Vector3 centre, (int X, int Y) along, float tileSize )
	{
		var entities = new List<ModelEntity>();
		if ( Load( piece ) is not { Count: > 0 } models )
			return entities;
		float yaw = MathF.Atan2( along.Y, along.X ) - MathF.PI / 2f;
		var rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
		foreach ( var model in models )
			entities.Add( new ModelEntity
			{
				Model = model,
				Position = centre,
				Rotation = rotation,
				Scale = new Vector3( tileSize / PieceSize ),
			} );
		return entities;
	}

	private static List<Model>? Load( string piece )
	{
		var theme = Level.Current?.Name ?? LevelTheme.Default;
		var key = $"{theme}/{piece}";
		if ( cache.TryGetValue( key, out var cached ) )
			return cached;

		List<Model>? models = null;
		var archive = $"levels/{theme}/queue";
		try
		{
			var modelFile = new ModelFile( $"{archive}/{piece}.md2" );
			models = new List<Model>();
			foreach ( var mesh in modelFile.Meshes )
			{
				var textures = mesh.Materials.Select( m => Ride.LoadRideTexture( archive, m.Name ) ).ToList();
				var vertices = new Vertex[mesh.Vertices.Length];
				for ( int i = 0; i < mesh.Vertices.Length; ++i )
				{
					// Bake the mesh transform, swizzle to Z-up, and centre the 10×10 tile on the origin.
					var p = mesh.Vertices[i].Position;
					var w = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( p.X, p.Y, p.Z ), mesh.TransformMatrix );
					vertices[i] = new Vertex
					{
						Position = new Vector3( w.X - PieceSize / 2f, w.Z - PieceSize / 2f, w.Y + 0.1f ),
						Normal = mesh.Normals[i],
						TexCoords = mesh.TexCoords[i],
						TexIndex = (int)mesh.Vertices[i].TextureIndex,
						MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
					};
				}
				models.AddRange( MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
					() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[queue] no '{piece}' piece in {archive}: {e.Message}" );
			models = null;
		}
		cache[key] = models;
		return models;
	}
}
