using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

internal static partial class Graphics
{
	private const int MaxVertexCount = ushort.MaxValue;
	private const int MaxIndexCount = ushort.MaxValue;

	private static DeviceBuffer vertexBuffer;
	private static DeviceBuffer indexBuffer;

	static Graphics()
	{
		var vertexStructSize = (uint)Marshal.SizeOf( typeof( Vertex ) );

		vertexBuffer = Device.ResourceFactory.CreateBuffer(
			new BufferDescription( MaxVertexCount * vertexStructSize, BufferUsage.VertexBuffer | BufferUsage.Dynamic )
		);

		var uintSize = (uint)sizeof( uint );

		indexBuffer = Device.ResourceFactory.CreateBuffer(
			new BufferDescription( MaxIndexCount * uintSize, BufferUsage.IndexBuffer | BufferUsage.Dynamic )
		);
	}

	private static Matrix4x4 CreateScreenMatrix( Point2 screenSize )
	{
		var matrix = Matrix4x4.Identity;

		// Scale to fit screen
		matrix *= Matrix4x4.CreateScale( new Vector3( 1f / screenSize.X, 1f / screenSize.Y, 1 ).GetSystemVector3() );

		// Convert from [-0.5f, 0.5f] to [0.0f, 1.0f]
		matrix *= Matrix4x4.CreateScale( 2.0f );
		matrix *= Matrix4x4.CreateTranslation( new Vector3( -1f, -1f, 0 ).GetSystemVector3() );

		return matrix;
	}

	public static void Quad( Rectangle rectangle, Material material )
	{
		Quad( rectangle, new Rectangle( 0, 0, 1, 1 ), material );
	}

	// Reused per-quad scratch so building a quad allocates nothing (was new List<Vertex>, new
	// List<uint> + two ToArray() per quad — T-027). Single-threaded render; consumed immediately by
	// AppendGeometry (which copies it into the batch), so it's safe to reuse for the next quad/glyph.
	private static readonly Vertex[] _quadVertices = new Vertex[4];
	private static readonly uint[] _quadIndices = { 3, 2, 1, 0, 1, 2 };

	public static void Quad( Rectangle rectangle, Rectangle uvs, Material material )
	{
		var screenMatrix = CreateScreenMatrix( BaseScreenSize );

		_quadVertices[0] = new( new Vector3( rectangle.TopLeft ) * screenMatrix, uvs.TopLeft );
		_quadVertices[1] = new( new Vector3( rectangle.TopRight ) * screenMatrix, uvs.TopRight );
		_quadVertices[2] = new( new Vector3( rectangle.BottomLeft ) * screenMatrix, uvs.BottomLeft );
		_quadVertices[3] = new( new Vector3( rectangle.BottomRight ) * screenMatrix, uvs.BottomRight );

		AppendGeometry( material, _quadVertices, _quadIndices );
	}
}

internal static partial class Graphics
{
	/// <summary>Draw textured triangles given in the 1280×720 base space (Y-up) — a ui.wad widget mesh laid flat
	/// on the screen (<see cref="UiModel"/>).</summary>
	public static void Triangles( ReadOnlySpan<System.Numerics.Vector2> positions, ReadOnlySpan<System.Numerics.Vector2> uvs, ReadOnlySpan<uint> indices, Material material )
	{
		var screenMatrix = CreateScreenMatrix( BaseScreenSize );
		var verts = new Vertex[positions.Length];
		for ( int i = 0; i < positions.Length; i++ )
			verts[i] = new( new Vector3( positions[i].X, positions[i].Y, 0f ) * screenMatrix, new Vector2( uvs[i].X, uvs[i].Y ) );
		AppendGeometry( material, verts, indices );
	}
}
