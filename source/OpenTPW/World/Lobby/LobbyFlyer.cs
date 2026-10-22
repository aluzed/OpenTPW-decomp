using SMatrix = System.Numerics.Matrix4x4;
using SQuaternion = System.Numerics.Quaternion;

namespace OpenTPW;

/// <summary>
/// A flyer of the front-end lobby (<c>lobby.wad</c>): a bat or a butterfly (<c>terrain/Bat</c>, <c>Bfly_PINK</c>,
/// <c>Bfly_YELL</c>) circling its island, or the plane (<c>globe/plane</c>) circling them all — bobbing, playing its
/// own animation (<c>m1</c>, <c>plane_anim</c>) through <see cref="KeyframePoser"/>.
/// </summary>
public sealed class LobbyFlyer : Entity
{
	private readonly List<ModelEntity> parts = new();
	private readonly List<SMatrix> placed = new();
	private readonly KeyframePoser? poser;
	private readonly RideKeyframeFile? flap;
	private readonly Vector3 centre;
	private readonly float radius, speed, phase;

	public LobbyFlyer( string model, Vector3 centre, float radius, float speed, float phase, string folder = "terrain", string? animation = null )
	{
		this.centre = centre;
		this.radius = radius;
		this.speed = speed;
		this.phase = phase;

		var path = $"lobby/{folder}/{model}.md2";
		var file = new ModelFile( path );
		foreach ( var mesh in file.Meshes )
		{
			var textures = mesh.Materials.Select( m =>
			{
				try { return new Texture( $"lobby/{folder}/textures/{m.Name}.wct", TextureFlags.Repeat ); }
				catch { return Texture.Missing; }
			} ).ToList();
			var vertices = new Vertex[mesh.Vertices.Length];
			for ( int i = 0; i < vertices.Length; i++ )
				vertices[i] = new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Vertices[i].TextureIndex < mesh.Materials.Length ? mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags : 0,
				};
			SMatrix.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			placed.Add( SMatrix.CreateScale( scl.X, scl.Z, scl.Y ) * SMatrix.CreateFromQuaternion( new SQuaternion( rot.X, rot.Z, rot.Y, -rot.W ) )
				* SMatrix.CreateTranslation( pos.X, pos.Z, pos.Y ) );
			// Double-sided: a wing is a single sheet.
			var models = MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided ) );
			parts.Add( new ModelEntity { Model = models[0] } );
		}

		try
		{
			using var stream = FileSystem.OpenRead( $"lobby/{folder}/{animation ?? model + "m1"}.md2" );
			flap = new RideKeyframeFile( stream );
			using var bytes = new MemoryStream();
			using ( var modelStream = FileSystem.OpenRead( path ) )
				modelStream.CopyTo( bytes );
			if ( ModelHierarchy.Read( bytes.ToArray() ) is { } tree && flap.Surfaces.Count > 0 && flap.Duration > 0f )
				poser = new KeyframePoser( tree, placed, parts.Select( p => p.Model ).ToList(), file.Meshes.Select( m => m.VertexOrder ).ToList() );
		}
		catch { flap = null; } // no wings to flap: it glides
	}

	protected override void OnUpdate()
	{
		float angle = phase + Time.Now * speed;
		var where = centre + new Vector3( MathF.Cos( angle ) * radius, MathF.Sin( angle ) * radius, MathF.Sin( Time.Now * 1.7f + phase ) * 3f );
		// Facing along its circle (the models look along their +Y once Y/Z-swapped).
		var heading = SMatrix.CreateRotationZ( angle + (speed > 0f ? MathF.PI : 0f) ) * SMatrix.CreateTranslation( where.GetSystemVector3() );

		if ( poser != null && flap != null )
			poser.Apply( flap, Time.Now * GestureFile.FramesPerSecond, loop: true );
		for ( int i = 0; i < parts.Count; i++ )
			if ( SMatrix.Decompose( (poser?.Pose[i] ?? placed[i]) * heading, out var scale, out var rotation, out var position ) )
			{
				parts[i].Position = new Vector3( position.X, position.Y, position.Z );
				parts[i].Rotation = rotation;
				parts[i].Scale = new Vector3( scale.X, scale.Y, scale.Z );
			}
	}
}
