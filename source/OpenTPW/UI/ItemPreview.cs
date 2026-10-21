using System.Numerics;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The turning 3D model in a window's preview box (the buy window, the ride / shop window), as in the original. The
/// item's model (its build-menu <c>P&lt;name&gt;.md2</c> when the wad ships one, else <c>&lt;name&gt;.md2</c>) is drawn
/// after the interface, over a cleared depth buffer, a short way in front of the camera behind the box and fitted
/// into it, seen from a little above and slowly turning. Models are built once per archive.
/// </summary>
internal sealed class ItemPreview
{
	private readonly record struct Part( Model Model, Matrix4x4 Local );

	private readonly List<Part> parts = new();
	private SVector3 centre;
	private float radius = 1f;

	private static readonly Dictionary<string, ItemPreview?> cache = new( StringComparer.OrdinalIgnoreCase );
	private static readonly List<(ItemPreview Preview, Rectangle Box)> queued = new();

	private const float Distance = 40f;   // in front of the camera
	private const float TurnSpeed = 0.6f; // radians a second
	private const float Tilt = -0.6f;     // looking down on it, radians

	/// <summary>The preview of the model in <paramref name="archive"/> named <paramref name="name"/> (cached; null when
	/// it doesn't load).</summary>
	public static ItemPreview? For( string archive, string name )
	{
		var key = $"{archive}/{name}";
		if ( cache.TryGetValue( key, out var preview ) )
			return preview;
		try { preview = new ItemPreview( archive, name ); }
		catch ( Exception e )
		{
			Log.Warning( $"[ui] preview {key}: {e.Message}" );
			preview = null;
		}
		if ( preview != null && preview.parts.Count == 0 )
			preview = null;
		cache[key] = preview;
		return preview;
	}

	private ItemPreview( string archive, string name )
	{
		ModelFile file;
		try { file = new ModelFile( $"{archive}/P{name}.md2" ); }
		catch { file = new ModelFile( $"{archive}/{name}.md2" ); }

		var points = new List<SVector3>();
		foreach ( var mesh in file.Meshes )
		{
			var textures = mesh.Materials.Select( m => RideSign.IsSignMaterial( m.Name ) ? Texture.Missing : Ride.LoadRideTexture( archive, m.Name ) ).ToList();
			var vertices = new Vertex[mesh.Vertices.Length];
			for ( int i = 0; i < vertices.Length; i++ )
				vertices[i] = new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials.Length == 0 ? 0 : mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
				};
			Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			var local = Matrix4x4.CreateScale( scl.X, scl.Z, scl.Y )
				* Matrix4x4.CreateFromQuaternion( new Quaternion( rot.X, rot.Z, rot.Y, -rot.W ) )
				* Matrix4x4.CreateTranslation( pos.X, pos.Z, pos.Y );
			foreach ( var v in vertices )
				points.Add( SVector3.Transform( v.Position.GetSystemVector3(), local ) );
			foreach ( var model in MaterialSlots.BuildModels( vertices, mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided ) ) )
				parts.Add( new Part( model, local ) );
		}
		if ( points.Count == 0 )
			return;
		var min = points.Aggregate( SVector3.Min );
		var max = points.Aggregate( SVector3.Max );
		centre = (min + max) / 2f;
		radius = MathF.Max( 1f, points.Max( p => SVector3.Distance( p, centre ) ) );
	}

	/// <summary>Show the preview in <paramref name="box"/> (base 1280×720 space) this frame.</summary>
	public void Draw( Rectangle box ) => queued.Add( (this, box) );

	/// <summary>Draw the previews queued this frame, over the interface (the level calls this after the UI).</summary>
	public static void RenderQueued()
	{
		if ( queued.Count == 0 )
			return;
		Graphics.FlushBatch();
		Render.CommandList.ClearDepthStencil( 1 );

		var fwd = Camera.Rotation.Forward.GetSystemVector3();
		// Screen → world through the real view-projection: the depth of a point Distance ahead, then each box's
		// centre and a pixel's size at that depth.
		var viewProj = Camera.ViewMatrix * Camera.ProjMatrix;
		if ( !Matrix4x4.Invert( viewProj, out var inverse ) )
			return;
		var ahead = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( Camera.Position.GetSystemVector3() + fwd * Distance, 1f ), viewProj );
		float depth = ahead.Z / ahead.W;
		SVector3 Unproject( float x, float y )
		{
			var p = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( x / 640f - 1f, y / 360f - 1f, depth, 1f ), inverse );
			return new SVector3( p.X, p.Y, p.Z ) / p.W;
		}

		// Face the camera: the model's up (Z) is the screen's up, then lean it towards the viewer and turn it.
		// The screen's axes in the world, straight from the view matrix (its columns): right, up, and back at the viewer.
		var view = Camera.ViewMatrix;
		var modelX = SVector3.Normalize( new SVector3( view.M11, view.M21, view.M31 ) );
		var screenUp = SVector3.Normalize( new SVector3( view.M12, view.M22, view.M32 ) );
		var toViewer = SVector3.Normalize( new SVector3( view.M13, view.M23, view.M33 ) );
		var basis = new Matrix4x4(
			modelX.X, modelX.Y, modelX.Z, 0f,
			toViewer.X, toViewer.Y, toViewer.Z, 0f,
			screenUp.X, screenUp.Y, screenUp.Z, 0f,
			0f, 0f, 0f, 1f );
		var spin = Matrix4x4.CreateRotationZ( Time.Now * TurnSpeed ) * Matrix4x4.CreateRotationX( Tilt );

		foreach ( var (preview, box) in queued )
		{
			var where = Unproject( box.X + box.Width / 2f, box.Y + box.Height / 2f );
			float pixel = SVector3.Distance( where, Unproject( box.X + box.Width / 2f + 1f, box.Y + box.Height / 2f ) );
			// Fit the model's bounding sphere into the box's smaller side.
			float fit = MathF.Min( box.Width, box.Height ) * 0.42f * pixel / preview.radius;
			var world = Matrix4x4.CreateTranslation( -preview.centre ) * spin * Matrix4x4.CreateScale( fit ) * basis * Matrix4x4.CreateTranslation( where );

			foreach ( var part in preview.parts )
			{
				var ubo = new ObjectUniformBuffer
				{
					g_mModel = part.Local * world,
					g_mView = Camera.ViewMatrix,
					g_mProj = Camera.ProjMatrix,
					g_vLightPos = Level.Current?.SunLight?.Position ?? Vector3.Zero,
					g_vLightColor = Level.Current?.SunLight?.Color ?? Vector3.One,
					g_vCameraPos = Camera.Position,
					g_flTime = Time.Now,
					g_vFogColor = Fog.Color,
				};
				part.Model.Material.Set( "ObjectUniformBuffer", ubo );
				part.Model.Draw();
			}
		}
		queued.Clear();
	}
}
