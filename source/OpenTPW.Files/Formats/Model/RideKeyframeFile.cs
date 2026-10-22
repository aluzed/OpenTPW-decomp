using System.Numerics;

// The OpenTPW namespace has a custom Vector3 (Common/Utils) registered as a global using alias, so the
// bare name resolves to it. This is geometry math, so Vector3 is fully qualified to System.Numerics
// below. (Quaternion has no custom shadow and resolves to System.Numerics.)
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Parses a Theme Park World ride <b>animation keyframe</b> file — a thin sibling <c>.md2</c> such as
/// <c>monkeym1.md2</c> (see <c>docs/08-ghidra-animation.md</c>, T-033). These files are <i>not</i>
/// standalone meshes (the engine's <see cref="ModelFile"/> rejects them): they share the base model's
/// header but carry time-indexed animation <b>tracks</b> keyed to the base model's surfaces.
///
/// <para>Layout (reverse-engineered from <c>FUN_0046d6d0</c> / <c>FUN_00470b60</c> /
/// <c>FUN_00471860</c>): the dword at <c>0x98</c> points to a per-surface relink trailer — a count at
/// <c>+0x12</c> and an array of <c>0x40</c>-byte records at <c>+0x2c</c>. Each record names a base
/// surface (<c>+0x10</c>), a flags word (<c>+0x04</c>) selecting which tracks run, and file offsets to
/// its translation (<c>+0x18</c>), rotation (<c>+0x1c</c>) and scale (<c>+0x20</c>) tracks. A track is
/// a list of keyframes; each key starts with a <c>0xFFFF</c>-tagged dword whose low <c>u16</c> is the
/// keyframe time, followed by the value (a 4-float <c>(w,x,y,z)</c> quaternion for rotation, a 3-float
/// vec3 for translation/scale). Vertex-morph tracks (flag <c>0x1000</c>) are not yet decoded here.</para>
/// </summary>
public sealed class RideKeyframeFile
{
	private const uint Magic = 0x1CD15D46;

	// Flags in a surface record (record + 0x04) selecting which tracks are present.
	public const uint FlagVertexMorph = 0x1000;
	public const uint FlagRotation = 0x8;
	public const uint FlagScale = 0x80;
	public const uint FlagTranslation = 0x200 | 0x1;

	/// <summary>Animation tracks for one base-model surface (mesh).</summary>
	public sealed class SurfaceAnim
	{
		/// <summary>Index into the base model's surface list (== <see cref="ModelFile.Meshes"/> index for surfaces below the mesh count).</summary>
		public int SurfaceIndex { get; init; }
		public uint Flags { get; init; }

		public List<(float Time, Quaternion Value)> Rotation { get; } = new();
		public List<(float Time, SVector3 Value)> Translation { get; } = new();
		public List<(float Time, SVector3 Value)> Scale { get; } = new();

		/// <summary>Vertex-morph sub-animations (flag <see cref="FlagVertexMorph"/>); each deforms a set of the surface's vertices over keyframes.</summary>
		public List<MorphSub> Morph { get; } = new();

		public bool HasAnimation => Rotation.Count > 0 || Translation.Count > 0 || Scale.Count > 0 || Morph.Count > 0;
	}

	/// <summary>
	/// One vertex-morph sub-animation: a set of the surface's vertices (<see cref="VertexSlots"/>) whose
	/// positions are keyframed (<see cref="Times"/> × <see cref="Frames"/>). Decoded from the morph
	/// block's 0x14-byte sub-entries; the packed 10-bit-per-axis positions are already dequantised here.
	/// </summary>
	public sealed class MorphSub
	{
		public int[] VertexSlots = System.Array.Empty<int>();   // output vertex index per morphed vertex
		public float[] Times = System.Array.Empty<float>();     // ascending keyframe times
		public SVector3[][] Frames = System.Array.Empty<SVector3[]>(); // Frames[frame][i] = position of VertexSlots[i]

		/// <summary>Interpolate vertex <paramref name="i"/>'s position at <paramref name="time"/> (lerp between bracketing keyframes).</summary>
		public SVector3 Sample( int i, float time )
		{
			if ( Frames.Length == 0 )
				return SVector3.Zero;
			if ( time <= Times[0] )
				return Frames[0][i];
			if ( time >= Times[^1] )
				return Frames[^1][i];

			for ( int f = 0; f < Times.Length - 1; f++ )
			{
				if ( time <= Times[f + 1] )
				{
					float span = Times[f + 1] - Times[f];
					float t = span > 0 ? (time - Times[f]) / span : 0f;
					return SVector3.Lerp( Frames[f][i], Frames[f + 1][i], t );
				}
			}
			return Frames[^1][i];
		}
	}

	/// <summary>The per-surface animation tracks found in the file (only surfaces that actually animate).</summary>
	public IReadOnlyList<SurfaceAnim> Surfaces => surfaces;
	private readonly List<SurfaceAnim> surfaces = new();

	/// <summary>The longest track's last keyframe time — the animation's loop length (0 if none).</summary>
	public float Duration { get; private set; }

	/// <summary>
	/// Appends another keyframe file's surfaces (used to merge a channel's numbered frame files, each of
	/// which animates different surfaces of the ride — see docs/08-ghidra-animation.md).
	/// </summary>
	public void Merge( RideKeyframeFile other )
	{
		surfaces.AddRange( other.surfaces );
		Duration = MathF.Max( Duration, other.Duration );
	}

	public RideKeyframeFile( Stream stream )
	{
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		Parse( ms.ToArray() );
	}

	public RideKeyframeFile( byte[] data ) => Parse( data );

	private void Parse( byte[] d )
	{
		// Bounds-checked little-endian readers — a malformed/short file yields no animation, never throws.
		ushort U16( int o ) => o + 2 <= d.Length ? (ushort)(d[o] | (d[o + 1] << 8)) : (ushort)0;
		uint U32( int o ) => o + 4 <= d.Length ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) : 0u;
		float F32( int o ) => o + 4 <= d.Length ? BitConverter.ToSingle( d, o ) : 0f;

		if ( d.Length < 0x9c || U32( 0 ) != Magic )
			return;

		// The base model has a zero anim pointer (it is the static bind pose); only keyframe files
		// carry one. It points to the per-surface relink trailer.
		int trailer = (int)U32( 0x98 );
		if ( trailer <= 0 || trailer + 0x30 > d.Length )
			return;

		int count = U16( trailer + 0x12 );
		int records = (int)U32( trailer + 0x2c );
		if ( records <= 0 || count <= 0 || count > 4096 )
			return;

		// Tracks are laid out contiguously in the data region, so a track ends where the next one
		// begins. Collect every track/data offset (each record's translation +0x18, rotation +0x1c,
		// scale +0x20, vertex-morph +0x28) plus the trailer, so each track can be bounded by the next
		// offset above it — the marker-and-monotonic-time scan alone would over-read identity tracks
		// into adjacent record data (which can look like a valid key).
		var bounds = new SortedSet<int> { trailer };
		for ( int i = 0; i < count; i++ )
		{
			int rec = records + i * 0x40;
			if ( rec + 0x40 > d.Length )
				break;
			foreach ( var off in new[] { (int)U32( rec + 0x18 ), (int)U32( rec + 0x1c ), (int)U32( rec + 0x20 ), (int)U32( rec + 0x28 ) } )
				if ( off > 0 && off < d.Length )
					bounds.Add( off );
		}

		int NextBound( int off )
		{
			foreach ( var b in bounds )
				if ( b > off )
					return b;
			return d.Length;
		}

		for ( int i = 0; i < count; i++ )
		{
			int rec = records + i * 0x40;
			if ( rec + 0x40 > d.Length )
				break;

			var anim = new SurfaceAnim { SurfaceIndex = U16( rec + 0x10 ), Flags = U32( rec + 0x04 ) };

			// A key's header dword is (marker << 16) | time: the high u16 is the interpolation-type
			// marker — 0xFFFF for a rotation (quaternion) track, 0x0000 for a linear vec3 track
			// (translation / scale). The low u16 is the keyframe time. A track is read until the next
			// track offset, the marker mismatches, or the time stops increasing.
			int transOff = (int)U32( rec + 0x18 ), rotOff = (int)U32( rec + 0x1c ), scaleOff = (int)U32( rec + 0x20 );
			ReadTrack( d, transOff, NextBound( transOff ), 3, 0x0000, U32, F32, ( t, v ) => anim.Translation.Add( (t, new SVector3( v[0], v[1], v[2] )) ) );
			// Rotation quaternion is stored (w, x, y, z); System.Numerics.Quaternion is (x, y, z, w).
			ReadTrack( d, rotOff, NextBound( rotOff ), 4, 0xFFFF, U32, F32, ( t, v ) => anim.Rotation.Add( (t, Quaternion.Normalize( new Quaternion( v[1], v[2], v[3], v[0] ) )) ) );
			ReadTrack( d, scaleOff, NextBound( scaleOff ), 3, 0x0000, U32, F32, ( t, v ) => anim.Scale.Add( (t, new SVector3( v[0], v[1], v[2] )) ) );

			if ( (anim.Flags & FlagVertexMorph) != 0 )
				anim.Morph.AddRange( ReadMorph( d, (int)U32( rec + 0x28 ) ) );

			if ( anim.HasAnimation )
			{
				surfaces.Add( anim );
				foreach ( var k in anim.Rotation ) Duration = MathF.Max( Duration, k.Time );
				foreach ( var k in anim.Translation ) Duration = MathF.Max( Duration, k.Time );
				foreach ( var k in anim.Scale ) Duration = MathF.Max( Duration, k.Time );
				foreach ( var m in anim.Morph ) if ( m.Times.Length > 0 ) Duration = MathF.Max( Duration, m.Times[^1] );
			}
		}
	}

	// Parses a vertex-morph block (see docs/08): a bounding-box scale/offset header and a count of
	// 0x14-byte sub-entries, each describing a set of vertices whose positions are keyframed. A vertex
	// is a packed 32-bit int holding three 10-bit signed components, dequantised as
	// component = signed10 * scaleAxis + offsetAxis. Decoded from FUN_004714a0 / FUN_00470e90.
	public static List<MorphSub> ReadMorph( byte[] d, int block )
	{
		ushort U16( int o ) => o >= 0 && o + 2 <= d.Length ? (ushort)(d[o] | (d[o + 1] << 8)) : (ushort)0;
		uint U32( int o ) => o >= 0 && o + 4 <= d.Length ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) : 0u;
		float F32( int o ) => o >= 0 && o + 4 <= d.Length ? BitConverter.ToSingle( d, o ) : 0f;

		var result = new List<MorphSub>();
		if ( block <= 0 || block + 0x2c > d.Length )
			return result;

		var offset = new SVector3( F32( block + 0x14 ), F32( block + 0x18 ), F32( block + 0x1c ) );
		var scale = new SVector3( F32( block + 0x20 ), F32( block + 0x24 ), F32( block + 0x28 ) );
		int nsub = U16( block + 2 );
		int sub0 = (int)U32( block + 0x0c );
		if ( sub0 <= 0 || nsub <= 0 || nsub > 4096 )
			return result;

		// Dequantise one packed vertex (three 10-bit signed components) with the block's bbox transform.
		SVector3 Dequant( uint p ) => new(
			Sext10( p ) * scale.X + offset.X,
			Sext10( p >> 10 ) * scale.Y + offset.Y,
			Sext10( p >> 20 ) * scale.Z + offset.Z );

		for ( int e = 0; e < nsub; e++ )
		{
			int se = sub0 + e * 0x14;
			if ( se + 0x14 > d.Length )
				break;

			int vc = U16( se + 0x02 );
			int idxPtr = (int)U32( se + 0x04 );
			int timesPtr = (int)U32( se + 0x08 );
			int packPtr = (int)U32( se + 0x0c );
			if ( vc <= 0 || vc > 65536 || idxPtr <= 0 || timesPtr <= 0 || packPtr <= 0 )
				continue;

			// Keyframe times: ascending u16, bounded by where the packed data starts.
			var times = new List<float>();
			for ( int o = timesPtr; o + 2 <= d.Length && o + 2 <= packPtr; o += 2 )
			{
				float t = U16( o );
				if ( times.Count > 0 && t <= times[^1] )
					break;
				times.Add( t );
			}
			if ( times.Count == 0 )
				continue;

			var slots = new int[vc];
			for ( int v = 0; v < vc; v++ )
				slots[v] = U16( idxPtr + v * 2 );

			// Packed positions: frame-contiguous, vc ints per frame.
			var frames = new SVector3[times.Count][];
			for ( int f = 0; f < times.Count; f++ )
			{
				frames[f] = new SVector3[vc];
				for ( int v = 0; v < vc; v++ )
					frames[f][v] = Dequant( U32( packPtr + (f * vc + v) * 4 ) );
			}

			result.Add( new MorphSub { VertexSlots = slots, Times = times.ToArray(), Frames = frames } );
		}
		return result;
	}

	// Sign-extend the low 10 bits of a packed value to a signed int.
	private static int Sext10( uint v )
	{
		int x = (int)(v & 0x3ff);
		return x >= 0x200 ? x - 0x400 : x;
	}

	// Reads keyframes starting at <paramref name="off"/>: each key is a header dword
	// (marker &lt;&lt; 16 | time) followed by <paramref name="floats"/> float components. Keys run
	// while the high u16 matches <paramref name="marker"/> and the time strictly increases (the next
	// track begins, or data ends, otherwise).
	private static void ReadTrack( byte[] d, int off, int end, int floats, uint marker, Func<int, uint> U32, Func<int, float> F32, Action<float, float[]> emit )
	{
		if ( off <= 0 )
			return;

		int stride = 4 + floats * 4;
		float prev = -1f;
		for ( int o = off; o + stride <= d.Length && o + stride <= end; o += stride )
		{
			uint hdr = U32( o );
			if ( (hdr >> 16) != marker )
				break;

			float t = hdr & 0xFFFF;
			if ( t <= prev )
				break;
			prev = t;

			var v = new float[floats];
			for ( int j = 0; j < floats; j++ )
				v[j] = F32( o + 4 + j * 4 );
			emit( t, v );
		}
	}

	/// <summary>Sample a rotation track at <paramref name="time"/> (slerp between bracketing keys; clamped to the track range).</summary>
	public static Quaternion SampleRotation( IReadOnlyList<(float Time, Quaternion Value)> keys, float time )
	{
		if ( keys.Count == 0 )
			return Quaternion.Identity;
		if ( time <= keys[0].Time )
			return keys[0].Value;
		if ( time >= keys[^1].Time )
			return keys[^1].Value;

		for ( int i = 0; i < keys.Count - 1; i++ )
		{
			if ( time <= keys[i + 1].Time )
			{
				float span = keys[i + 1].Time - keys[i].Time;
				float f = span > 0 ? (time - keys[i].Time) / span : 0f;
				return Quaternion.Slerp( keys[i].Value, keys[i + 1].Value, f );
			}
		}
		return keys[^1].Value;
	}

	/// <summary>Sample a vec3 track (translation/scale) at <paramref name="time"/> (lerp; <paramref name="fallback"/> if empty).</summary>
	public static SVector3 SampleVector( IReadOnlyList<(float Time, SVector3 Value)> keys, float time, SVector3 fallback )
	{
		if ( keys.Count == 0 )
			return fallback;
		if ( time <= keys[0].Time )
			return keys[0].Value;
		if ( time >= keys[^1].Time )
			return keys[^1].Value;

		for ( int i = 0; i < keys.Count - 1; i++ )
		{
			if ( time <= keys[i + 1].Time )
			{
				float span = keys[i + 1].Time - keys[i].Time;
				float f = span > 0 ? (time - keys[i].Time) / span : 0f;
				return SVector3.Lerp( keys[i].Value, keys[i + 1].Value, f );
			}
		}
		return keys[^1].Value;
	}
}
