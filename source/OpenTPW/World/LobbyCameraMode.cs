namespace OpenTPW;

public class LobbyCameraMode : CameraMode
{
	private float Height => 95f;
	private float Distance => 240f;
	private float Speed => 0.15f;

	// Orbit the centroid of the four island spots (X,Y in 400..600) so the whole world cluster is framed, not
	// just one island — the world-select needs the islands on-screen to click them (T-063).
	private Vector3 Target => new Vector3( 500, 500, 12.5f );

	// The menu buttons own the right side of the screen, so aim this far to the right of the cluster: the islands
	// (and their world names) then sit in the left part of the view instead of sliding under the buttons (T-070).
	private float ScreenShift => 110f;

	public override void Update()
	{
		float x = MathF.Sin( Time.Now * Speed ) * Distance;
		float y = MathF.Cos( Time.Now * Speed ) * Distance;

		Position = new Vector3( Target.X + x, Target.Y + y, Height );

		var forward = new Vector3( Target.X - Position.X, Target.Y - Position.Y, 0 ).Normal;
		var screenRight = Vector3.Cross( forward, Vector3.Up ); // the engine's +Y is screen-left (Vector3.Right = -Y)
		Rotation = Rotation.LookAt( Target - screenRight * ScreenShift - Position );

		FieldOfView = 60;
	}
}
