using System.Numerics;

using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// A character animation (the advisor's gestures, <c>Advisor_&lt;theme&gt;m1..20.MD2</c>; T-046). It shares the ride
/// keyframe files' outer layout (<see cref="RideKeyframeFile"/>: the trailer at <c>0x98</c>, its record count at
/// <c>+0x12</c>, its 0x40-byte records at <c>+0x2c</c>, the length in frames at <c>+0x08</c>), but its records address
/// the model's whole object tree (<see cref="ModelHierarchy"/>) and carry other tracks:
/// <list type="bullet">
/// <item><c>+0x04</c> flags: <c>0x8</c> rotation, <c>0x80</c> scale, <c>0x20000</c> visibility (<c>0x1</c> translation and
/// <c>0x1000</c> morph are not read: the advisor's translations are all zero);</item>
/// <item><c>+0x10</c> the rotation key count, <c>+0x12</c> the scale key count, <c>+0x14</c> the object;</item>
/// <item><c>+0x1c</c> rotation keys, 20 bytes: <c>(index &lt;&lt; 16 | frame)</c> — the last index <c>0xFFFF</c> — then the
/// object's whole local rotation as <c>(x, y, z, w)</c>;</item>
/// <item><c>+0x20</c> scale keys, 16 bytes: <c>(frame)</c> and the local scale;</item>
/// <item><c>+0x30</c> visibility: signed u16 frames, <c>+f</c> shown from f, <c>−f</c> hidden from f (0: hidden), |f|
/// increasing — the advisor blinks by swapping his eyes for the shut ones.</item>
/// </list>
/// Frames run at 30 a second, as the rides' keys. Pure; a malformed file yields fewer tracks, never throws.
/// </summary>
public sealed class GestureFile
{
	public const uint FlagRotation = 0x8, FlagScale = 0x80, FlagVisibility = 0x20000;
	public const float FramesPerSecond = 30f;

	public sealed class Track
	{
		public int Target { get; init; }
		public List<(float Frame, Quaternion Value)> Rotation { get; } = new();
		public List<(float Frame, SVector3 Value)> Scale { get; } = new();
		/// <summary>Visibility changes, in frame order (empty: the track doesn't touch it).</summary>
		public List<(float Frame, bool Visible)> Visibility { get; } = new();

		/// <summary>Whether the object shows at <paramref name="frame"/> (null: the track leaves it alone).</summary>
		public bool? VisibleAt( float frame )
		{
			if ( Visibility.Count == 0 )
				return null;
			bool visible = !Visibility[0].Visible; // before its first change it is the other way
			foreach ( var (f, v) in Visibility )
			{
				if ( f > frame )
					break;
				visible = v;
			}
			return visible;
		}
	}

	public IReadOnlyList<Track> Tracks => tracks;
	private readonly List<Track> tracks = new();

	/// <summary>The animation's length in frames.</summary>
	public float Length { get; private set; }

	public float Seconds => Length / FramesPerSecond;

	public GestureFile( byte[] d ) => Parse( d );

	private void Parse( byte[] d )
	{
		ushort U16( int o ) => o >= 0 && o + 2 <= d.Length ? BitConverter.ToUInt16( d, o ) : (ushort)0;
		uint U32( int o ) => o >= 0 && o + 4 <= d.Length ? BitConverter.ToUInt32( d, o ) : 0u;
		float F32( int o ) => o >= 0 && o + 4 <= d.Length ? BitConverter.ToSingle( d, o ) : 0f;

		if ( d.Length < 0x9c || U32( 0 ) != 0x1CD15D46 )
			return;
		int trailer = (int)U32( 0x98 );
		if ( trailer <= 0 || trailer + 0x30 > d.Length )
			return;
		Length = U32( trailer + 0x08 );
		int count = U16( trailer + 0x12 );
		int records = (int)U32( trailer + 0x2c );
		if ( count <= 0 || count > 512 || records <= 0 )
			return;

		for ( int i = 0; i < count; i++ )
		{
			int rec = records + i * 0x40;
			if ( rec + 0x40 > d.Length )
				break;
			uint flags = U32( rec + 0x04 );
			var track = new Track { Target = U16( rec + 0x14 ) };

			if ( (flags & FlagRotation) != 0 )
			{
				int keys = U16( rec + 0x10 ), o = (int)U32( rec + 0x1c );
				for ( int k = 0; k < keys && k < 1024 && o > 0 && o + 20 <= d.Length; k++, o += 20 )
				{
					float frame = U32( o ) & 0xFFFF;
					if ( track.Rotation.Count > 0 && frame < track.Rotation[^1].Frame )
						break;
					track.Rotation.Add( (frame, Quaternion.Normalize( new Quaternion( F32( o + 4 ), F32( o + 8 ), F32( o + 12 ), F32( o + 16 ) ) )) );
					if ( (U32( o ) >> 16) == 0xFFFF )
						break; // the last key
				}
			}
			if ( (flags & FlagScale) != 0 )
			{
				int keys = U16( rec + 0x12 ), o = (int)U32( rec + 0x20 );
				for ( int k = 0; k < keys && k < 1024 && o > 0 && o + 16 <= d.Length; k++, o += 16 )
				{
					float frame = U32( o ) & 0xFFFF;
					if ( track.Scale.Count > 0 && frame < track.Scale[^1].Frame )
						break;
					track.Scale.Add( (frame, new SVector3( F32( o + 4 ), F32( o + 8 ), F32( o + 12 ) )) );
				}
			}
			if ( (flags & FlagVisibility) != 0 )
			{
				int o = (int)U32( rec + 0x30 );
				int last = -1;
				for ( int k = 0; k < 64 && o > 0 && o + 2 <= d.Length; k++, o += 2 )
				{
					short v = (short)U16( o );
					int frame = Math.Abs( (int)v );
					if ( frame <= last || frame > Length )
						break;
					track.Visibility.Add( (frame, v > 0) );
					last = frame;
				}
			}
			if ( track.Rotation.Count > 0 || track.Scale.Count > 0 || track.Visibility.Count > 0 )
				tracks.Add( track );
		}
	}

	/// <summary>A rotation track's value at <paramref name="frame"/> (slerp between keys, held at the ends).</summary>
	public static Quaternion Sample( IReadOnlyList<(float Frame, Quaternion Value)> keys, float frame )
	{
		if ( frame <= keys[0].Frame )
			return keys[0].Value;
		for ( int i = 0; i < keys.Count - 1; i++ )
			if ( frame <= keys[i + 1].Frame )
			{
				float span = keys[i + 1].Frame - keys[i].Frame;
				return Quaternion.Slerp( keys[i].Value, keys[i + 1].Value, span > 0f ? (frame - keys[i].Frame) / span : 1f );
			}
		return keys[^1].Value;
	}

	/// <summary>A scale track's value at <paramref name="frame"/> (lerp between keys, held at the ends).</summary>
	public static SVector3 Sample( IReadOnlyList<(float Frame, SVector3 Value)> keys, float frame )
	{
		if ( frame <= keys[0].Frame )
			return keys[0].Value;
		for ( int i = 0; i < keys.Count - 1; i++ )
			if ( frame <= keys[i + 1].Frame )
			{
				float span = keys[i + 1].Frame - keys[i].Frame;
				return SVector3.Lerp( keys[i].Value, keys[i + 1].Value, span > 0f ? (frame - keys[i].Frame) / span : 1f );
			}
		return keys[^1].Value;
	}
}
