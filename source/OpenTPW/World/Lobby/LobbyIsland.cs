using System.Numerics;

namespace OpenTPW;

public sealed class LobbyIsland : Entity
{
	public LobbyIsland( Vector3 _position, string themeName )
	{
		Position = _position;

		var modelPrefix = themeName[0..3];
		var modelFile = new ModelFile( $"lobby/terrain/{modelPrefix}_isle.md2" );

		// Loading an island's meshes (each decompresses up to 16 textures + uploads a GPU buffer) is
		// the bulk of the load. Report per mesh so the loading screen pumps events between meshes and
		// the window stays responsive instead of freezing for the whole island. See T-030.
		var meshCount = modelFile.Meshes.Count;
		var meshIndex = 0;

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

			foreach ( var model in MaterialSlots.BuildModels( [.. vertices], mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) ) )
				_ = new ModelEntity()
				{
					Model = model,
					Scale = scale,
					Rotation = rotation,
					Position = position + Position,
				};
		}
	}
}
