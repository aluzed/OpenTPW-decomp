namespace OpenTPW;

/// <summary>
/// test.shader binds 16 textures (<c>Color0</c>..<c>Color15</c>) and picks one per vertex (<see cref="Vertex.TexIndex"/>);
/// an index outside that range falls back to the shader's solid magenta. TPW meshes can carry more materials than
/// that (a fantasy ride's base plate has 25 floor tiles), so a mesh is drawn as one model per group of at most
/// <see cref="Count"/> materials, each with its own texture set.
/// </summary>
public static class MaterialSlots
{
	/// <summary>Texture slots in test.shader.</summary>
	public const int Count = 16;

	/// <summary>
	/// Splits a mesh by material group: triangles whose (first vertex's) material falls in
	/// [<c>g·Count</c>, <c>(g+1)·Count</c>) go to group <c>g</c>, with vertices re-indexed and <see cref="Vertex.TexIndex"/>
	/// rebased into 0..<see cref="Count"/>-1. A mesh with at most <see cref="Count"/> materials comes back unchanged
	/// as group 0.
	/// </summary>
	public static List<(int Group, Vertex[] Vertices, uint[] Indices)> Split( Vertex[] vertices, uint[] indices )
	{
		if ( vertices.All( v => v.TexIndex < Count ) )
			return [(0, vertices, indices)];

		var groups = new SortedDictionary<int, (List<Vertex> Vertices, List<uint> Indices, Dictionary<uint, uint> Remap)>();
		for ( int t = 0; t + 2 < indices.Length; t += 3 )
		{
			int group = Math.Max( 0, vertices[indices[t]].TexIndex ) / Count;
			if ( !groups.TryGetValue( group, out var g ) )
				groups[group] = g = (new List<Vertex>(), new List<uint>(), new Dictionary<uint, uint>());

			for ( int k = 0; k < 3; k++ )
			{
				var source = indices[t + k];
				if ( !g.Remap.TryGetValue( source, out var mapped ) )
				{
					var v = vertices[source];
					v.TexIndex -= group * Count;
					mapped = (uint)g.Vertices.Count;
					g.Vertices.Add( v );
					g.Remap[source] = mapped;
				}
				g.Indices.Add( mapped );
			}
		}

		return groups.Select( kv => (kv.Key, kv.Value.Vertices.ToArray(), kv.Value.Indices.ToArray()) ).ToList();
	}

	/// <summary>
	/// One <see cref="Model"/> per material group of a mesh (see <see cref="Split"/>). <paramref name="textures"/> holds
	/// one texture per material of the mesh (any count); each group's 16 slots are filled from its range, padded with
	/// <see cref="Texture.Missing"/>. <paramref name="newMaterial"/> makes a fresh material per group.
	/// </summary>
	public static List<Model> BuildModels( Vertex[] vertices, uint[] indices, IReadOnlyList<Texture> textures, Func<Material> newMaterial )
	{
		var models = new List<Model>();
		foreach ( var (group, groupVertices, groupIndices) in Split( vertices, indices ) )
		{
			var slots = new Texture[Count];
			for ( int i = 0; i < Count; i++ )
			{
				int index = group * Count + i;
				slots[i] = index < textures.Count ? textures[index] : Texture.Missing;
			}

			var material = newMaterial();
			material.Set( "Color", slots );
			models.Add( new Model( groupVertices, groupIndices, material ) );
		}
		return models;
	}
}
