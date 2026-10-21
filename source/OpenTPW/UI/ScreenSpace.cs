using System.Numerics;
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
		// From the projection's own scale factors (M11 = 1 / (tan(fov/2) · aspect), M22 = 1 / tan(fov/2)) and the
		// view's axes — no matrix inverse, whose precision at depths near the far plane drifted.
		var proj = Camera.ProjMatrix;
		world = default;
		pixel = 0f;
		if ( proj.M11 <= 0f || proj.M22 <= 0f )
			return false;
		var (right, up, back) = Axes();
		float ndcX = x / 640f - 1f, ndcY = y / 360f - 1f;
		world = Camera.Position.GetSystemVector3() - back * distance + right * (ndcX * distance / proj.M11) + up * (ndcY * distance / proj.M22);
		pixel = 2f * distance / (proj.M22 * 720f);
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
