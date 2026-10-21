namespace OpenTPW;

/// <summary>
/// The in-park RTS camera used in build/manage mode: a fixed downward pitch, player-controlled yaw
/// (arrow keys, in 45° steps), zoom (wheel), and panning (WASD or right-mouse drag). It orbits a ground
/// <see cref="Focus"/> point at <c>distance</c>, looking down at it. Foundation for T-040 — the build
/// tools (T-041+) pick tiles relative to this view.
/// </summary>
public class BuildCameraMode : CameraMode
{
	/// <summary>The ground point the camera looks at; panning moves this.</summary>
	public static Vector3 Focus = new();

	/// <summary>Where <see cref="Focus"/> may go (world XY min/max) — the park plus a margin, so panning can't wander
	/// off into the void. Null = unbounded (set by the level).</summary>
	public static (Vector2 Min, Vector2 Max)? Bounds { get; set; }

	private const float Pitch = 48f;       // degrees looking down (a park view that shows the rides in profile)
	private const float MinDist = 90f, MaxDist = 700f;

	private float yaw = 45f, wishYaw = 45f;
	private float distance = 165f;         // start zoomed in enough to actually see the rides (was 340, too far)
	private Vector2 dragAnchor;
	private bool wasRight;

	public override void Update()
	{
		// Yaw in 45° steps (arrow keys), smoothed.
		if ( Input.Pressed( InputButton.RotateLeft ) )
			wishYaw -= 45f;
		if ( Input.Pressed( InputButton.RotateRight ) )
			wishYaw += 45f;
		yaw = yaw.LerpTo( wishYaw, 10f * Time.Delta );

		// Zoom (wheel).
		distance = (distance - Input.Mouse.Wheel * 25f).Clamp( MinDist, MaxDist );

		// Look direction: down-forward from yaw + fixed pitch.
		float yr = yaw.DegreesToRadians();
		float pr = (-Pitch).DegreesToRadians();
		var dir = new Vector3( MathF.Cos( pr ) * MathF.Cos( yr ), MathF.Cos( pr ) * MathF.Sin( yr ), MathF.Sin( pr ) ).Normal;
		Rotation = Rotation.LookAt( dir );

		// Ground-plane basis for panning.
		var gFwd = new Vector3( dir.X, dir.Y, 0 ).Normal;
		var gRight = new Vector3( gFwd.Y, -gFwd.X, 0 );

		// WASD pans (speed scales with zoom so it feels consistent).
		Focus += (gFwd * Input.Forward + gRight * Input.Right) * distance * 1.2f * Time.Delta;

		// Right-mouse drag pans by the cursor delta mapped to the view extent.
		if ( Input.Mouse.Right && !wasRight )
			dragAnchor = Input.Mouse.Position;
		if ( Input.Mouse.Right )
		{
			var d = Input.Mouse.Position - dragAnchor;
			float scale = distance / Screen.Height;
			Focus += (gRight * -d.X + gFwd * d.Y) * scale;
			dragAnchor = Input.Mouse.Position;
		}
		wasRight = Input.Mouse.Right;

		if ( Bounds is { } b )
			Focus = new Vector3( Math.Clamp( Focus.X, b.Min.X, b.Max.X ), Math.Clamp( Focus.Y, b.Min.Y, b.Max.Y ), Focus.Z );

		Position = Focus - dir * distance;
		FieldOfView = 55f;
	}
}
