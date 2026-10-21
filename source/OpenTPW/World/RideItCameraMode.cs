namespace OpenTPW;

/// <summary>
/// "Ride it" (the ride window's b_rideit): the camera rides along. On a ride with a car or a train it sits a little
/// above the lead car and looks the way it travels; on any other ride it circles close around it at rider height.
/// Escape or the right mouse button goes back to the park view, looking at the ride.
/// </summary>
public sealed class RideItCameraMode : CameraMode
{
	/// <summary>The ride being ridden (set before switching to this mode).</summary>
	public static Ride? Target { get; set; }

	private Vector3 lastLead;
	private Vector3 heading = new( 1, 0, 0 );
	private bool hasLead;

	/// <summary>Ride <paramref name="ride"/>.</summary>
	public static void Start( Ride ride )
	{
		Target = ride;
		Camera.SetCameraMode<RideItCameraMode>();
		Log.Info( $"[ride] riding {ride.Name}" );
	}

	/// <summary>Back to the park view, looking at the ride.</summary>
	public static void Stop()
	{
		if ( Target is { } ride )
			BuildCameraMode.Focus = ride.Position;
		Target = null;
		Camera.SetCameraMode<BuildCameraMode>();
	}

	public override void Update()
	{
		FieldOfView = 70f;
		if ( Target is not { } ride || !Entity.All.Contains( ride ) || Input.Pressed( InputButton.Menu ) || Input.MouseRightPressed )
		{
			Stop();
			return;
		}

		if ( ride.RiderPosition is { } lead )
		{
			// Ride the lead car: look the way it travels (smoothed so the view doesn't jitter between samples).
			if ( hasLead )
			{
				var step = (lead - lastLead).WithZ( 0 );
				if ( step.Length > 0.01f )
					heading = (heading * 0.9f + step.Normal * 0.1f).Normal;
			}
			lastLead = lead;
			hasLead = true;
			Position = lead + Vector3.Up * 6f;
			Rotation = Rotation.LookAt( heading + Vector3.Down * 0.15f );
			return;
		}

		// No car: circle the ride from just outside its footprint, above the neighbours' roofs.
		float a = Time.Now * 0.3f;
		float radius = MathF.Max( ride.Shape.Width, ride.Shape.Height ) * PlacementGrid.WorldTileSize * 0.75f + 25f;
		Position = ride.Position + new Vector3( MathF.Cos( a ) * radius, MathF.Sin( a ) * radius, 38f );
		Rotation = Rotation.LookAt( (ride.Position + Vector3.Up * 12f - Position).Normal );
	}
}
