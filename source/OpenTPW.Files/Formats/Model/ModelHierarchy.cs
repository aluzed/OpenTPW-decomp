using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The object tree of an animated <c>.MD2</c> (docs/08, T-046 gestures): its meshes and, after the mesh table, its
/// nodes — invisible pivots such as a character's neck or shoulders. Objects are numbered as the animation files
/// address them: the meshes first (0 … <see cref="MeshCount"/>−1), then the nodes, to the header's object count
/// (<c>u16 @ 0x42</c>). Each 160-byte mesh entry and each 0x58-byte node entry starts with a type word and the file
/// offset of its parent (0 for the root); a mesh's local matrix is at <c>+0x10</c> (as <see cref="ModelFile"/> reads
/// it), a node's too. Local matrices are relative to the parent, row-vector (<c>world = local × parent world</c>).
/// </summary>
public sealed class ModelHierarchy
{
	public const int MeshStride = 160, NodeStride = 0x58;

	public int MeshCount { get; }
	public int ObjectCount => Parent.Length;

	/// <summary>Each object's parent (−1 for a root).</summary>
	public int[] Parent { get; }

	/// <summary>Each object's local (bind) matrix, relative to its parent.</summary>
	public Matrix4x4[] Local { get; }

	private ModelHierarchy( int meshCount, int[] parent, Matrix4x4[] local )
	{
		MeshCount = meshCount;
		Parent = parent;
		Local = local;
	}

	/// <summary>Read the tree from a model file's bytes; null when the layout doesn't hold together.</summary>
	public static ModelHierarchy? Read( byte[] d )
	{
		ushort U16( int o ) => o >= 0 && o + 2 <= d.Length ? BitConverter.ToUInt16( d, o ) : (ushort)0;
		uint U32( int o ) => o >= 0 && o + 4 <= d.Length ? BitConverter.ToUInt32( d, o ) : 0u;

		if ( d.Length < 0x9c || U32( 0 ) != 0x1CD15D46 )
			return null;
		int meshes = U16( 0x44 ), objects = U16( 0x42 );
		int meshTable = (int)U32( 0x70 );
		int nodeTable = meshTable + meshes * MeshStride;
		int nodes = objects - meshes;
		if ( meshes <= 0 || nodes < 0 || nodes > 256 || meshTable <= 0 || nodeTable + nodes * NodeStride > d.Length )
			return null;

		// A parent pointer → its object number.
		int IndexOf( uint ptr )
		{
			if ( ptr == 0 )
				return -1;
			long p = ptr;
			if ( p >= meshTable && p < nodeTable && (p - meshTable) % MeshStride == 0 )
				return (int)((p - meshTable) / MeshStride);
			if ( p >= nodeTable && p < nodeTable + nodes * NodeStride && (p - nodeTable) % NodeStride == 0 )
				return meshes + (int)((p - nodeTable) / NodeStride);
			return -1;
		}

		Matrix4x4 MatrixAt( int o )
		{
			var f = new float[16];
			for ( int i = 0; i < 16; i++ )
				f[i] = BitConverter.ToSingle( d, o + i * 4 );
			return new Matrix4x4( f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15] );
		}

		var parent = new int[objects];
		var local = new Matrix4x4[objects];
		for ( int i = 0; i < objects; i++ )
		{
			int entry = i < meshes ? meshTable + i * MeshStride : nodeTable + (i - meshes) * NodeStride;
			parent[i] = IndexOf( U32( entry + 4 ) );
			local[i] = MatrixAt( entry + 0x10 );
		}

		// A cycle would hang the walk up the tree: refuse the file.
		for ( int i = 0; i < objects; i++ )
		{
			int steps = 0;
			for ( int p = parent[i]; p >= 0; p = parent[p] )
				if ( ++steps > objects )
					return null;
		}
		return new ModelHierarchy( meshes, parent, local );
	}
}
