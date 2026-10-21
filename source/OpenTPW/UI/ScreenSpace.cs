using System.Numerics;
using SVector4 = System.Numerics.Vector4;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Placing 3D things on the interface (the advisor, the windows' previews): a point of the 1280×720 base screen
/// (Y up) a given distance in front of the camera, the world size of one screen pixel there, and the screen's axes
/// in the world — all straight from the camera's view and projection matrices.
/// </summary>
internal static class ScreenSpace
{
	/// <summary>The world point under base-screen (<paramref name="x"/>, <paramref name="y"/>) at
	/// <paramref name="distance"/> in front of the camera, and how wide one screen pixel is there.</summary>
	public static bool TryAnchor( float x, float y, float distance, out SVector3 world, out float pixel )
	{
		world = default;
		pixel = 0f;
		var viewProj = Camera.ViewMatrix * Camera.ProjMatrix;
		if ( !Matrix4x4.Invert( viewProj, out var inverse ) )
			return false;
		var fwd = Camera.Rotation.Forward.GetSystemVector3();
		var ahead = SVector4.Transform( new SVector4( Camera.Position.GetSystemVector3() + fwd * distance, 1f ), viewProj );
		float depth = ahead.Z / ahead.W;
		SVector3 Unproject( float px, float py )
		{
			var p = SVector4.Transform( new SVector4( px / 640f - 1f, py / 360f - 1f, depth, 1f ), inverse );
			return new SVector3( p.X, p.Y, p.Z ) / p.W;
		}
		world = Unproject( x, y );
		pixel = SVector3.Distance( world, Unproject( x + 1f, y ) );
		return true;
	}

	/// <summary>The screen's right, up and back-at-the-viewer directions in the world (the view matrix's columns).</summary>
	public static (SVector3 Right, SVector3 Up, SVector3 Back) Axes()
	{
		var v = Camera.ViewMatrix;
		return (SVector3.Normalize( new SVector3( v.M11, v.M21, v.M31 ) ),
			SVector3.Normalize( new SVector3( v.M12, v.M22, v.M32 ) ),
			SVector3.Normalize( new SVector3( v.M13, v.M23, v.M33 ) ));
	}
}
