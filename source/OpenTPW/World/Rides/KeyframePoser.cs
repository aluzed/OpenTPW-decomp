using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Poses a model built one entity per mesh from keyframes, through its object tree — what <see cref="RideEngine"/>
/// does for a ride's body, for the other animated models (the stalls' <c>c</c> / <c>m</c> / <c>i</c> files). A part
/// is posed as L · B⁻¹ · A (its bind local, out of its hierarchical bind pose, into its hierarchical animated pose)
/// and put where the loader placed it; vertex morphs write their mesh's positions (T-033, T-046).
/// </summary>
public sealed class KeyframePoser
{
	private static readonly Matrix4x4 SwapYZ = new( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );

	private readonly ModelHierarchy tree;
	private readonly IReadOnlyList<ModelEntity> parts;
	private readonly Matrix4x4[] bindWorld;
	private readonly Matrix4x4[] placement;  // per part: out of the loader's Y/Z-swapped local, onto its spot in the park
	private readonly Vertex[][] rest;        // per part: its rest vertices
	private readonly int[][][] vertexOf;     // per part: each mesh position's vertices
	private readonly HashSet<int> morphed = new();
	private readonly Matrix4x4[] world;
	private readonly bool[] done;

	public KeyframePoser( ModelHierarchy tree, IReadOnlyList<ModelEntity> parts, IReadOnlyList<ushort[]> vertexOrders )
	{
		this.tree = tree;
		this.parts = parts;
		world = new Matrix4x4[tree.ObjectCount];
		done = new bool[tree.ObjectCount];
		bindWorld = new Matrix4x4[tree.ObjectCount];
		var bindDone = new bool[tree.ObjectCount];
		for ( int i = 0; i < tree.ObjectCount; i++ )
			WorldOf( i, j => tree.Local[j], bindWorld, bindDone );

		placement = new Matrix4x4[parts.Count];
		for ( int p = 0; p < parts.Count && p < tree.MeshCount; p++ )
		{
			var e = parts[p];
			var baseMatrix = Matrix4x4.CreateScale( e.Scale.GetSystemVector3() ) * Matrix4x4.CreateFromQuaternion( e.Rotation )
				* Matrix4x4.CreateTranslation( e.Position.GetSystemVector3() );
			placement[p] = Matrix4x4.Invert( SwapYZ * tree.Local[p] * SwapYZ, out var restInverse ) ? restInverse * baseMatrix : baseMatrix;
		}
		rest = parts.Select( e => e.Model?.Vertices.ToArray() ?? Array.Empty<Vertex>() ).ToArray();
		vertexOf = vertexOrders.Select( order => Enumerable.Range( 0, order.Length == 0 ? 0 : order.Max() + 1 )
			.Select( position => Enumerable.Range( 0, order.Length ).Where( v => order[v] == position ).ToArray() ).ToArray() ).ToArray();
	}

	/// <summary>Pose the parts from <paramref name="kf"/> at <paramref name="frame"/> (looped over the channel, or held
	/// at its end).</summary>
	public void Apply( RideKeyframeFile kf, float frame, bool loop )
	{
		FixRotationOrder( tree, kf );
		var tracks = new Dictionary<int, RideKeyframeFile.SurfaceAnim>();
		foreach ( var sa in kf.Surfaces )
			if ( sa.SurfaceIndex < tree.ObjectCount && (sa.Rotation.Count > 0 || sa.Scale.Count > 0) && Playing( sa, kf, frame, loop, out _ ) )
				tracks[sa.SurfaceIndex] = sa;

		Matrix4x4 Local( int i )
		{
			var bind = tree.Local[i];
			if ( !tracks.TryGetValue( i, out var sa ) || !Playing( sa, kf, frame, loop, out var t ) )
				return bind;
			Matrix4x4.Decompose( bind, out var scl, out var rot, out var pos );
			if ( sa.Rotation.Count > 0 )
				rot = RideKeyframeFile.SampleRotation( sa.Rotation, t );
			if ( sa.Scale.Count > 0 )
				scl = RideKeyframeFile.SampleVector( sa.Scale, t, scl );
			return Matrix4x4.CreateScale( scl ) * Matrix4x4.CreateFromQuaternion( rot ) * Matrix4x4.CreateTranslation( pos );
		}

		Array.Clear( done );
		for ( int p = 0; p < parts.Count && p < tree.MeshCount; p++ )
		{
			bool moves = false;
			for ( int a = p; a >= 0 && !moves; a = tree.Parent[a] )
				moves = tracks.ContainsKey( a );
			if ( !moves || !Matrix4x4.Invert( bindWorld[p], out var bindInverse ) )
				continue;
			var final = SwapYZ * (tree.Local[p] * bindInverse * WorldOf( p, Local, world, done )) * SwapYZ * placement[p];
			if ( !Matrix4x4.Decompose( final, out var scale, out var rotation, out var position ) )
				continue;
			parts[p].Position = new Vector3( position.X, position.Y, position.Z );
			parts[p].Rotation = rotation;
			parts[p].Scale = new Vector3( scale.X, scale.Y, scale.Z );
		}

		// Morphs: the mesh's positions written over its rest shape; parts no morph moves any more go back to rest.
		var touched = new HashSet<int>();
		foreach ( var sa in kf.Surfaces )
		{
			int p = sa.SurfaceIndex;
			if ( sa.Morph.Count == 0 || p >= parts.Count || p >= vertexOf.Length || parts[p].Model is not { } model
				|| !Playing( sa, kf, frame, loop, out var t ) )
				continue;
			if ( touched.Add( p ) )
				Array.Copy( rest[p], model.Vertices, Math.Min( rest[p].Length, model.Vertices.Length ) );
			foreach ( var m in sa.Morph )
				for ( int i = 0; i < m.VertexSlots.Length && m.Times.Length > 0; i++ )
				{
					if ( m.VertexSlots[i] >= vertexOf[p].Length )
						continue;
					var q = m.Sample( i, t );
					foreach ( int v in vertexOf[p][m.VertexSlots[i]] )
						if ( v < model.Vertices.Length )
							model.Vertices[v].Position = new Vector3( q.X, q.Z, q.Y );
				}
		}
		foreach ( int p in touched )
			parts[p].Model?.UploadVertices();
		foreach ( int p in morphed.Where( p => !touched.Contains( p ) ).ToList() )
			if ( parts[p].Model is { } model )
			{
				Array.Copy( rest[p], model.Vertices, Math.Min( rest[p].Length, model.Vertices.Length ) );
				model.UploadVertices();
			}
		morphed.Clear();
		morphed.UnionWith( touched );
	}

	private Matrix4x4 WorldOf( int i, Func<int, Matrix4x4> local, Matrix4x4[] into, bool[] known )
	{
		if ( known[i] )
			return into[i];
		int parent = tree.Parent[i];
		// A root mesh keeps its own matrix (as the loader placed it); a root node is the scene's frame, left out.
		var m = parent < 0 ? (i < tree.MeshCount ? local( i ) : Matrix4x4.Identity) : local( i ) * WorldOf( parent, local, into, known );
		into[i] = m;
		known[i] = true;
		return m;
	}

	/// <summary>Whether a track is the one playing for its object at <paramref name="frame"/>, and the time within the
	/// pass. An object several of the channel's files move has that many variants: a looping channel takes them in
	/// turn, one per pass; a one-shot plays the first.</summary>
	public static bool Playing( RideKeyframeFile.SurfaceAnim sa, RideKeyframeFile kf, float frame, bool loop, out float local )
	{
		int pass = loop && kf.Duration > 0f ? (int)(frame / kf.Duration) : 0;
		local = kf.Duration <= 0f ? 0f : loop ? frame % kf.Duration : MathF.Min( frame, kf.Duration );
		var variants = kf.VariantsOf( sa.SurfaceIndex, sa.Morph.Count > 0 && sa.Rotation.Count == 0 && sa.Scale.Count == 0 && sa.Translation.Count == 0 );
		return variants.Count <= 1 || variants[pass % variants.Count] == sa;
	}

	// The tracks whose component order was already settled (each is checked once).
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RideKeyframeFile.SurfaceAnim, object> checkedOrder = new();

	/// <summary>A rotation track's first key is its object's bind rotation: read each track in the component order that
	/// makes it so (the tag style doesn't always tell — the jungle totem tags every key 0xFFFF yet stores x,y,z,w).</summary>
	public static void FixRotationOrder( ModelHierarchy tree, RideKeyframeFile keyframes )
	{
		foreach ( var sa in keyframes.Surfaces )
		{
			if ( sa.Rotation.Count == 0 || sa.SurfaceIndex >= tree.ObjectCount || checkedOrder.TryGetValue( sa, out _ ) )
				continue;
			checkedOrder.AddOrUpdate( sa, true );
			if ( !Matrix4x4.Decompose( tree.Local[sa.SurfaceIndex], out _, out var bind, out _ ) )
				continue;
			var q = sa.Rotation[0].Value;
			var other = sa.WFirst ? new Quaternion( q.W, q.X, q.Y, q.Z ) : new Quaternion( q.Y, q.Z, q.W, q.X );
			if ( MathF.Abs( Quaternion.Dot( bind, other ) ) > MathF.Abs( Quaternion.Dot( bind, q ) ) + 0.01f )
				sa.SwapRotationOrder();
		}
	}
}
