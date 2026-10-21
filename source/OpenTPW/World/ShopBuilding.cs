namespace OpenTPW;

/// <summary>
/// Loads a shop's real building model from the active theme's WAD (a food/drink stall under <c>shops/</c>,
/// a toilet under <c>features/</c>) as a set of textured child entities — so a stall reads as a building
/// instead of a flat coloured billboard. TPW ships several stalls per theme under different names; the first
/// candidate that loads is used. Returns false (leaving the caller on its billboard) when none resolve, so a
/// theme without a matching model still renders something.
/// </summary>
internal static class ShopBuilding
{
	// Candidate archive paths per kind for a theme, tried in order. Food/drink stalls live under shops/ and are
	// authored corner-anchored at their own scale, so BuildParts re-centres + fits each to its footprint. Toilets
	// are a "feature" under features/, but the model (jungle toilet.md2, …) is a thin single-facade panel rather
	// than a boxy outhouse — scaled up it reads as a flat plate top-down, worse than the upright billboard — so it
	// stays on the billboard fallback (empty candidate list) pending a fuller toilet model / multi-part load.
	private static IEnumerable<string> Candidates( ShopKind kind, string theme ) => kind switch
	{
		ShopKind.Drink => new[] { "coconut", "drinks", "cola", "ices", "icecream" }.Select( n => $"levels/{theme}/shops/{n}" ),
		ShopKind.Toilet => Enumerable.Empty<string>(),
		_ => new[] { "burger", "fries", "steak", "hotdog", "sburger", "gchips" }.Select( n => $"levels/{theme}/shops/{n}" ),
	};

	/// <summary>Try to build the real shop model centred on <paramref name="origin"/> and scaled to fill a
	/// <paramref name="footprintSize"/>-wide cell, appending its per-mesh entities to <paramref name="parts"/>
	/// (the caller removes them on demolish). True if a model loaded.</summary>
	public static bool TryLoad( ShopKind kind, Vector3 origin, float footprintSize, List<ModelEntity> parts )
	{
		foreach ( var archive in Candidates( kind, Level.Current.Name ) )
		{
			var name = Path.GetFileName( archive );
			try
			{
				var modelFile = new ModelFile( $"{archive}/{name}.md2" );
				if ( modelFile.Meshes.Count == 0 )
					continue;
				BuildParts( modelFile, archive, origin, footprintSize, parts );
				if ( parts.Count > 0 )
				{
					Log.Info( $"[shop] {kind}: building '{name}' ({parts.Count} mesh(es))" );
					return true;
				}
			}
			catch { /* try the next candidate */ }
		}
		return false;
	}

	// One textured ModelEntity per mesh, Y/Z-swizzled (the LobbyIsland / Ride pattern), then re-centred on the
	// model's ground base-plate (mesh 0) and uniformly scaled so that plate fills the stall footprint — TPW models
	// are authored corner-anchored at varying scales, so without this a small model (a toilet) sits in a corner of
	// its cell at a fraction of the size. Textures load through the shared ride loader (stexture/, chroma-keyed).
	private static void BuildParts( ModelFile modelFile, string archive, Vector3 origin, float footprintSize, List<ModelEntity> parts )
	{
		var meshes = modelFile.Meshes;

		// The base-plate (mesh 0) is the model's ground tile = its footprint. Measure its XY span (in swizzled
		// model space: model-X = raw X, model-Y = raw Z) to get the centre to pull to origin and the scale to fit.
		float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f;
		if ( meshes.Count > 0 )
		{
			System.Numerics.Matrix4x4.Decompose( meshes[0].TransformMatrix, out var bscl, out _, out var bpos );
			foreach ( var raw in meshes[0].Vertices )
			{
				float x = bpos.X + bscl.X * raw.Position.X;
				float y = bpos.Z + bscl.Z * raw.Position.Z;
				minx = MathF.Min( minx, x ); maxx = MathF.Max( maxx, x );
				miny = MathF.Min( miny, y ); maxy = MathF.Max( maxy, y );
			}
		}
		float baseSize = MathF.Max( maxx - minx, maxy - miny );
		float fit = baseSize > 1e-2f && footprintSize > 0f ? footprintSize / baseSize : 1f;
		var centre = new Vector3( (minx + maxx) / 2f, (miny + maxy) / 2f, 0f );

		foreach ( var mesh in meshes )
		{
			var textures = mesh.Materials.Select( m => Ride.LoadRideTexture( archive, m.Name ) ).ToList();

			var vertices = new List<Vertex>( mesh.Vertices.Length );
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
				vertices.Add( new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
				} );

			System.Numerics.Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			var spos = new Vector3( pos.X, pos.Z, pos.Y );
			foreach ( var model in MaterialSlots.BuildModels( [.. vertices], mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) ) )
				parts.Add( new ModelEntity
				{
					Model = model,
					Scale = new Vector3( scl.X, scl.Z, scl.Y ) * fit,
					Rotation = new System.Numerics.Quaternion( rot.X, rot.Z, rot.Y, -rot.W ),
					Position = origin + (spos - centre) * fit,
				} );
		}
	}
}
