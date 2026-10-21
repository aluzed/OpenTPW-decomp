using SN = System.Numerics;

namespace OpenTPW;

/// <summary>
/// The lobby world-select (T-063): projects each lobby island's world position to the screen, draws its world
/// name over it, and enters that world when the player clicks near it (<see cref="Level.RequestReload"/>). This
/// makes the restored front-end lobby functional — click an island to start playing that world.
/// (System.Numerics is aliased <c>SN</c> because the engine has its own shadowing Vector3/Vector4 types.)
/// </summary>
internal sealed class LobbyWorldSelect : HudPanel
{
	public static LobbyWorldSelect? Current { get; private set; }

	public LobbyWorldSelect() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private const float ClickRadius = 120f; // screen-space (1280×720 base) radius around an island's label

	// Left edge of the lobby's PurpleButton column (1280 - 550). An island orbiting behind the buttons gets no
	// label (it would be drawn over them) and no click (the click belongs to the button on top) — T-070.
	private const float ButtonColumnLeft = 730f;

	// Project a world point to the UI base space (1280×720, Y-up). False if behind the camera, far off-screen, or
	// under the button column.
	private static bool Project( Vector3 world, out float sx, out float sy )
	{
		sx = sy = 0f;
		var clip = SN.Vector4.Transform( new SN.Vector4( world.X, world.Y, world.Z, 1f ), Camera.ViewMatrix * Camera.ProjMatrix );
		if ( clip.W <= 0.001f )
			return false;
		float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
		if ( ndcX < -1.3f || ndcX > 1.3f || ndcY < -1.3f || ndcY > 1.3f )
			return false;
		sx = (ndcX * 0.5f + 0.5f) * 1280f;
		sy = (ndcY * 0.5f + 0.5f) * 720f;
		return sx < ButtonColumnLeft;
	}

	// The island's clickable/label anchor: a little above the island base so the name sits on the island.
	private static Vector3 Anchor( Vector3 islandPos ) => islandPos + new Vector3( 0, 0, 18f );

	protected override void OnUpdate()
	{
		if ( !Input.MouseLeftPressed )
			return;

		var m = MouseBase();
		foreach ( var (theme, pos) in Level.LobbyWorlds )
		{
			if ( !Project( Anchor( pos ), out var sx, out var sy ) )
				continue;
			float dx = m.X - sx, dy = m.Y - sy;
			if ( dx * dx + dy * dy <= ClickRadius * ClickRadius )
			{
				Level.RequestReload( theme ); // enter that world (draws its own loading screen)
				break;
			}
		}
	}

	protected override void OnRender()
	{
		foreach ( var (theme, pos) in Level.LobbyWorlds )
		{
			if ( !Project( Anchor( pos ), out var sx, out var sy ) )
				continue;
			Graphics.DrawText( Font, LevelTheme.DisplayName( theme ), sx, sy, TextAlign.Center, 1.8f );
		}
	}
}
