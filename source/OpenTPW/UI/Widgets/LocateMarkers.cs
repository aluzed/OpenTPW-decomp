namespace OpenTPW;

/// <summary>
/// The original's locate buttons (UIHELPTEXT 99 "locate visitors (Ctrl-V)", 128 "locate members of staff (Ctrl-S)"):
/// while on, a bobbing arrow over the head of every visitor or every member of staff, so they can be found in a
/// crowded park. Toggled from the lists or with the keys.
/// </summary>
internal sealed class LocateMarkers : HudPanel
{
	public enum Target { None, Visitors, Staff }

	public static Target Showing { get; private set; }

	/// <summary>Show <paramref name="target"/>, or stop if it is already showing.</summary>
	public static void Toggle( Target target ) => Showing = Showing == target ? Target.None : target;

	public LocateMarkers() => Showing = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "locate" ? Target.Visitors : Target.None; // dev aid

	private const float HeadHeight = 7f, ArrowSize = 22f;

	protected override void OnRender()
	{
		if ( Showing == Target.None || Icon( "b_ardown" ) is not { } arrow )
			return;
		var entities = Showing == Target.Visitors ? Entity.All.OfType<Peep>().Cast<Entity>() : Entity.All.OfType<Staff>();
		float bob = MathF.Sin( Time.Now * 5f ) * 4f;
		var mat = Material.UI;
		mat.Set( "Color", arrow );
		foreach ( var e in entities )
		{
			var head = e.Position + Vector3.Up * HeadHeight;
			if ( !ScreenSpace.TryProject( head.GetSystemVector3(), out float x, out float y ) || x < 0f || x > 1280f || y < 0f || y > 720f )
				continue;
			Graphics.Quad( new Rectangle( x - ArrowSize / 2f, y + bob, ArrowSize, ArrowSize ), mat );
		}
	}
}
