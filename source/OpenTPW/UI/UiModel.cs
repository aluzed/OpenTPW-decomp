using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>
/// A widget mesh from <c>ui.wad</c> (<c>mainpanel.MD2</c>, <c>date.MD2</c>…) drawn flat on the HUD. The original builds
/// its in-game interface from these models, seen by a UI camera whose placement lives in <c>tp.exe</c>; we lay each
/// mesh into a screen rectangle measured from captures of the original instead (docs/10-ui-reference.md). The mesh is
/// projected on its two widest axes, so both the upright plates (X/Y) and the flat-authored buttons (X/Z) work.
/// </summary>
internal sealed class UiModel
{
	private sealed class Part
	{
		public required string Name;
		public required NVector2[] Positions; // normalised to the mesh's bounds, 0–1, Y up
		public required NVector2[] Uvs;
		public required List<(Texture Texture, uint[] Indices)> Batches;
	}

	// Which way the widgets face the UI camera along their depth axis.
	private static readonly bool FrontIsHigh = Environment.GetEnvironmentVariable( "OPENTPW_UI_FRONT_LOW" ) != "1";

	private readonly Dictionary<string, Part> parts = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<string, UiModel?> cache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The model <c>ui/{name}.MD2</c>, cached; null when it doesn't load.</summary>
	public static UiModel? Load( string name, float eyeDistance = 0f )
	{
		if ( cache.TryGetValue( name, out var model ) )
			return model;
		try { model = new UiModel( name, eyeDistance ); }
		catch ( Exception e )
		{
			Log.Warning( $"[ui] {name}.MD2: {e.Message}" );
			model = null;
		}
		cache[name] = model;
		return model;
	}

	/// <param name="eyeDistance">When positive, the mesh is seen in perspective from this far in front of its
	/// front face (model units) instead of flat — the handheld's face sits 28 units deep inside its shell, and only
	/// perspective shows the shell around it.</param>
	private UiModel( string name, float eyeDistance )
	{
		ModelFile file;
		using ( var stream = FileSystem.OpenRead( $"ui/{name}.MD2" ) )
			file = new ModelFile( stream );

		foreach ( var mesh in file.Meshes )
		{
			var p = mesh.Vertices.Select( v => v.Position ).ToArray();
			if ( p.Length == 0 )
				continue;
			float Extent( Func<Vector3, float> axis ) => p.Max( axis ) - p.Min( axis );
			// Upright plates span X/Y; buttons and icons are authored lying in X/Z.
			bool upright = Extent( v => v.Y ) >= Extent( v => v.Z );
			Func<Vector3, float> vertical = upright ? v => v.Y : v => v.Z;
			Func<Vector3, float> depth = upright ? v => v.Z : v => v.Y;
			if ( eyeDistance > 0f )
			{
				// Shrink each vertex towards the centre by how far it lies behind the front face.
				float front = p.Max( depth ), cx = (p.Min( v => v.X ) + p.Max( v => v.X )) / 2f, cy = (p.Min( vertical ) + p.Max( vertical )) / 2f;
				var flat = upright;
				p = p.Select( v =>
				{
					float k = eyeDistance / (eyeDistance + front - depth( v ));
					float x = cx + (v.X - cx) * k, y = cy + (vertical( v ) - cy) * k;
					return flat ? new Vector3( x, y, v.Z ) : new Vector3( x, v.Y, y );
				} ).ToArray();
			}
			float x0 = p.Min( v => v.X ), x1 = p.Max( v => v.X ), y0 = p.Min( vertical ), y1 = p.Max( vertical );
			var positions = p.Select( v => new NVector2(
				x1 > x0 ? (v.X - x0) / (x1 - x0) : 0f,
				y1 > y0 ? (vertical( v ) - y0) / (y1 - y0) : 0f ) ).ToArray();

			// No depth buffer in the UI pass: paint the triangles back to front (the depth axis is the one left over),
			// grouping runs that share a texture.
			var textures = mesh.Materials.Select( m => LoadTexture( m.Name ) ).ToArray();
			var triangles = Enumerable.Range( 0, mesh.Indices.Length / 3 )
				.Select( t => (Index: t * 3, Depth: (depth( p[mesh.Indices[t * 3]] ) + depth( p[mesh.Indices[t * 3 + 1]] ) + depth( p[mesh.Indices[t * 3 + 2]] )) / 3f) )
				.OrderBy( t => FrontIsHigh ? t.Depth : -t.Depth ).ToList();
			var batches = new List<(int Material, List<uint> Indices)>();
			foreach ( var (i, _) in triangles )
			{
				int mat = (int)mesh.Vertices[mesh.Indices[i]].TextureIndex;
				if ( batches.Count == 0 || batches[^1].Material != mat )
					batches.Add( (mat, new List<uint>()) );
				batches[^1].Indices.AddRange( [mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]] );
			}

			parts[mesh.Name.Trim( '\0', ' ' )] = new Part
			{
				Name = mesh.Name,
				Positions = positions,
				Uvs = mesh.TexCoords.Select( t => new NVector2( t.X, t.Y ) ).ToArray(),
				Batches = batches.Where( b => b.Material < textures.Length )
					.Select( b => (textures[b.Material], b.Indices.ToArray()) ).ToList(),
			};
		}
	}

	private static Texture LoadTexture( string material )
	{
		try { return new Texture( $"ui/textures/{material.Trim( '\0', ' ' )}.wct" ); }
		catch { return Texture.Missing; }
	}

	public IEnumerable<string> PartNames => parts.Keys;

	/// <summary>Draw mesh <paramref name="part"/> (the first one when null) stretched into <paramref name="rect"/>
	/// (base 1280×720 space, Y up).</summary>
	public void Draw( Rectangle rect, string? part = null )
	{
		var p = part == null ? parts.Values.FirstOrDefault() : parts.GetValueOrDefault( part );
		if ( p == null )
			return;
		var screen = new NVector2[p.Positions.Length];
		for ( int i = 0; i < screen.Length; i++ )
			screen[i] = new NVector2( rect.X + p.Positions[i].X * rect.Width, rect.Y + p.Positions[i].Y * rect.Height );
		var mat = Material.UI;
		foreach ( var (texture, indices) in p.Batches )
		{
			mat.Set( "Color", texture );
			Graphics.Triangles( screen, p.Uvs, indices, mat );
		}
	}
}
