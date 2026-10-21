using Veldrid;

namespace OpenTPW;

/// <summary>
/// The camcorder (the handheld's camera button, "Left-click to use camcorder mode"): walk the park at a visitor's eye
/// height — WASD to walk, hold the right mouse button and drag to look, the wheel to walk faster or slower. Esc goes
/// back to the park view where you stand.
/// </summary>
public sealed class CamcorderCameraMode : CameraMode
{
	/// <summary>Whether the camcorder is on.</summary>
	public static bool Active { get; private set; }

	private const float EyeHeight = 7f;
	private float yaw = 45f, pitch = -5f, speed = 25f;
	private bool wasRight;
	private Vector2 lastMouse;

	public static void Start()
	{
		Camera.SetCameraMode<CamcorderCameraMode>();
		Active = true;
		UiSounds.Play( UiSounds.Camera );
		Log.Info( "[camera] camcorder on" );
	}

	public static void Stop()
	{
		Active = false;
		Camera.SetCameraMode<BuildCameraMode>();
	}

	public CamcorderCameraMode()
	{
		Position = BuildCameraMode.Focus;
		FieldOfView = 70f;
	}

	public override void Update()
	{
		if ( Input.Pressed( InputButton.Menu ) || BuildMode.Current is not { } build )
		{
			BuildCameraMode.Focus = Position.WithZ( BuildCameraMode.Focus.Z );
			Stop();
			return;
		}

		if ( Input.Mouse.Right && !wasRight )
			lastMouse = Input.Mouse.Position;
		if ( Input.Mouse.Right )
		{
			var delta = Input.Mouse.Position - lastMouse;
			yaw -= delta.X * 0.2f;
			pitch = (pitch - delta.Y * 0.2f).Clamp( -60f, 60f );
			lastMouse = Input.Mouse.Position;
		}
		wasRight = Input.Mouse.Right;

		float yr = yaw.DegreesToRadians(), pr = pitch.DegreesToRadians();
		Rotation = Rotation.LookAt( new Vector3( MathF.Cos( pr ) * MathF.Cos( yr ), MathF.Cos( pr ) * MathF.Sin( yr ), MathF.Sin( pr ) ) );
		speed = (speed + Input.Mouse.Wheel * 5f).Clamp( 8f, 120f );

		// Walk on the ground: forward is the look direction flattened.
		var forward = new Vector3( MathF.Cos( yr ), MathF.Sin( yr ), 0 );
		var right = new Vector3( forward.Y, -forward.X, 0 );
		var move = forward * Input.Forward + right * Input.Right;
		var at = Position + (move.Length > 0f ? move.Normal * speed * Time.Delta : Vector3.Zero);
		if ( BuildCameraMode.Bounds is { } b )
			at = new Vector3( Math.Clamp( at.X, b.Min.X, b.Max.X ), Math.Clamp( at.Y, b.Min.Y, b.Max.Y ), at.Z );
		Position = at.WithZ( build.Terrain.SampleHeight( at.X, at.Y ) + EyeHeight );
	}
}
