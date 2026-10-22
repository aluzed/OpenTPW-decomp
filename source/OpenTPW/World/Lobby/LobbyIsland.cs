using System.Numerics;

namespace OpenTPW;

public sealed class LobbyIsland : Entity
{
	public LobbyIsland( Vector3 _position, string themeName )
	{
		Position = _position;

		var modelPrefix = themeName[0..3];
		var modelFile = new ModelFile( $"lobby/terrain/{modelPrefix}_isle.md2" );
		parts.AddRange( Spawn( modelFile, themeName ) );
		LoadAnimation( $"lobby/terrain/{modelPrefix}_isle", modelFile );

		// The park's gates stand on the island, modelled in its frame (Jun_gate …).
		try { Spawn( new ModelFile( $"lobby/terrain/{modelPrefix}_gate.md2" ), themeName ); }
		catch ( Exception e ) { Log.Warning( $"[lobby] {themeName} gates: {e.Message}" ); }
	}

	// One entity per mesh's material group, the loader's Y/Z swap applied, placed on the island; returns each mesh's
	// first (its part for the animation).
	private List<ModelEntity> Spawn( ModelFile modelFile, string themeName )
	{
		var meshParts = new List<ModelEntity>();

		// Loading an island's meshes (each decompresses up to 16 textures + uploads a GPU buffer) is
		// the bulk of the load. Report per mesh so the loading screen pumps events between meshes and
		// the window stays responsive instead of freezing for the whole island. See T-030.
		var meshCount = modelFile.Meshes.Count;
		var meshIndex = 0;

		// Decode the island's textures in parallel up front; each mesh below then only uploads its own (T-030).
		Texture.PrefetchWct( modelFile.Meshes.SelectMany( m => m.Materials ).Select( m => $"lobby/terrain/textures/{m.Name}.wct" ) );

		foreach ( var mesh in modelFile.Meshes )
		{
			LoadProgress.ReportSub( $"Loading island: {themeName}... ({++meshIndex}/{meshCount})", (float)meshIndex / meshCount );

			// A missing texture used to throw and drop the whole island (Space's lobby island never showed).
			var textures = mesh.Materials.Select( m =>
			{
				try { return new Texture( $"lobby/terrain/textures/{m.Name}.wct", TextureFlags.Repeat ); }
				catch { return Texture.Missing; }
			} ).ToList();

			var vertices = new List<Vertex>();
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
			{
				vertices.Add( new Vertex()
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					// Space's island has vertices past its material list (it threw and the island never showed).
					MatFlags = mesh.Vertices[i].TextureIndex < mesh.Materials.Length ? mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags : 0
				} );
			}

			Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );

			var position = new Vector3( pos.X, pos.Z, pos.Y - 2.5f );
			var rotation = new Quaternion( rot.X, rot.Z, rot.Y, -rot.W );
			var scale = new Vector3( scl.X, scl.Z, scl.Y );

			var models = MaterialSlots.BuildModels( [.. vertices], mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) );
			for ( int m = 0; m < models.Count; m++ )
			{
				var entity = new ModelEntity()
				{
					Model = models[m],
					Scale = scale,
					Rotation = rotation,
					Position = position + Position,
				};
				if ( m == 0 )
					meshParts.Add( entity ); // the mesh's part (the rest are extra material groups)
			}
		}
		return meshParts;
	}

	// The island's own animation (its M1, M2 … files: the waterfalls, the windmill), posed through its object tree.
	private readonly List<ModelEntity> parts = new();
	private KeyframePoser? poser;
	private RideKeyframeFile? animation;

	private void LoadAnimation( string stem, ModelFile modelFile )
	{
		static byte[]? Read( string path )
		{
			try
			{
				using var stream = FileSystem.OpenRead( path );
				using var bytes = new MemoryStream();
				stream.CopyTo( bytes );
				return bytes.ToArray();
			}
			catch { return null; }
		}
		if ( parts.Count != modelFile.Meshes.Count || Read( $"{stem}.md2" ) is not { } data || ModelHierarchy.Read( data ) is not { } tree )
			return;
		for ( int n = 1; n <= 9 && Read( $"{stem}M{n}.md2" ) is { } file; n++ )
		{
			var kf = new RideKeyframeFile( file );
			if ( kf.Surfaces.Count == 0 )
				continue;
			if ( animation == null )
				animation = kf;
			else
				animation.AddVariant( kf );
		}
		if ( animation == null || animation.Duration <= 0f )
			return;
		poser = new KeyframePoser( tree, KeyframePoser.PlacementsOf( parts ), parts.Select( e => e.Model ).ToList(),
			modelFile.Meshes.Select( m => m.VertexOrder ).ToList() );
	}

	protected override void OnUpdate()
	{
		if ( poser == null || animation == null )
			return;
		poser.Apply( animation, Time.Now * GestureFile.FramesPerSecond, loop: true );
		poser.PoseEntities( parts );
	}
}
