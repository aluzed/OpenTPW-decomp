namespace OpenTPW;

/// <summary>
/// The bobbing arrow over whatever the player has selected in build/manage mode — the game's own selection arrows
/// (<c>esprites/Generic/Thoughts/SPR_TC</c> frames 3–6: blue, green, red, yellow; their hotspot puts them ~119 px
/// above the anchor, i.e. over a visitor's head). Blue marks a ride, yellow a stall, green a member of staff. Hidden
/// when nothing is selected or the sprite is missing.
/// </summary>
public sealed class SelectionArrow : ModelEntity
{
	private const int BlueFrame = 3, GreenFrame = 4, YellowFrame = 6;
	private const float SpriteScale = 10.4f / 43f; // the visitors' pixel scale (a ~43 px kid is ~10.4 units tall)
	private const float BobHeight = 1.5f, BobSpeed = 4f;
	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );

	protected override void OnUpdate()
	{
		var build = BuildMode.Current;
		var target = build?.SelectedRide is { } ride ? (Position: ride.Position + Vector3.Up * RideLift, Frame: BlueFrame)
			: build?.SelectedShop is { } shop ? (shop.Position + Vector3.Up * StallLift, YellowFrame)
			: build?.SelectedStaff is { } staff ? (staff.Position, GreenFrame)
			: ((Vector3, int)?)null;
		if ( target is not { } t || SpriteSheet.Load( "esprites/Generic/Thoughts", "SPR_TC" ) is not { } sheet )
		{
			Position = Offscreen;
			return;
		}
		Model = sheet.FrameModel( t.Item2 );
		Position = t.Item1 + Vector3.Up * (BobHeight * MathF.Sin( Time.Now * BobSpeed ));
		Scale = new Vector3( SpriteScale, 1f, SpriteScale );
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
	}

	// The arrow's hotspot already floats it a head's height up; rides and stalls are taller than a visitor.
	private const float RideLift = 12f, StallLift = 6f;
}
