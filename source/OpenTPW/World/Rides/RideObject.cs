namespace OpenTPW;

/// <summary>
/// An object a ride script spawned (or the ride body), tracked by the <see cref="RideEngine"/> so
/// later opcodes (KILLOBJ / SETOBJPARAM / TRIGANIM / WAITANIM) can find it by its script handle.
/// Stage 1: sound key, params, and procedural animation state over its visual parts (real MD2
/// keyframe playback is stage 2-3).
/// </summary>
public sealed class RideObject
{
	public int Id { get; init; }
	public int Slot { get; init; }
	public int Type { get; init; }

	/// <summary>The audio cache key for sound objects (null otherwise).</summary>
	public string? SoundKey { get; set; }

	/// <summary>An effect object's node (where it sits on the ride) and effect code (ADDOBJ's 2nd / 3rd operands).</summary>
	public int Node { get; init; } = -1;
	public int Code { get; init; }

	/// <summary>Script-set parameters (SETOBJPARAM), by parameter index.</summary>
	public Dictionary<int, int> Params { get; } = new();

	/// <summary>The parts a vertex morph changed last frame (put back to rest once no morph moves them).</summary>
	public HashSet<int> Morphed { get; } = new();

	// Animation state (procedural for now). AnimId null = not animating.
	public int? AnimId { get; set; }
	public bool AnimLoop { get; set; }
	public float AnimStart { get; set; }
	public float AnimSpeed { get; set; } = 1f;

	/// <summary>
	/// The object's visual parts and their rest pose (the ride body has several meshes). Part index
	/// == base-model surface index, so a keyframe track for surface N drives <c>Parts[N]</c>. BasePos
	/// / BaseRot are the loaded transform that animation composes onto.
	/// </summary>
	public List<(ModelEntity Entity, Vector3 BasePos, System.Numerics.Quaternion BaseRot, Vector3 BaseScale)> Parts { get; } = new();

	/// <summary>
	/// Vertex-morph support. The morph addresses one combined vertex buffer spanning all parts in
	/// order, so <see cref="PartVertexOffset"/>[p] is part p's global vertex start, and
	/// <see cref="PartBaseVerts"/>[p] is its rest-pose positions (to restore before each frame's morph).
	/// </summary>
	public List<int> PartVertexOffset { get; } = new();
	public List<Vector3[]> PartBaseVerts { get; } = new();
}
