using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The park map (T-057), in the original's map window (<c>w_map</c>; the handheld's map button, or M): the level's
/// top-down <c>2dmap.tga</c> with a live pin per ride / shop / visitor / litter at its projected world position and a
/// box marking the camera's focus. The layers toggle from the round buttons down the right, and clicking the map
/// pans the camera.
/// Projection is the pure <see cref="MinimapProjection"/> fed the live placement-grid bounds. In-park only.
/// </summary>
internal sealed class MinimapPanel : GameWindow
{
	public static MinimapPanel? Current { get; private set; }

	public MinimapPanel()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "map"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	// The original's map window (w_map), the park's own 2dmap.tga inside it, the layer buttons down the right.
	protected override Rectangle Frame => At( 170f, 34f, 620f, 465f );
	private static Rectangle MapRect() => At( 214f, 76f, 470f, 380f );
	private static readonly Rectangle Ok = At( 712f, 410f, 42f, 42f );

	/// <summary>Show / hide the map (the handheld's map button, or M).</summary>
	public void Toggle() => IsOpen = !IsOpen;
	private bool wasToggleDown;

	// Per-category layer visibility (toggled from the legend).
	private bool showRides = true, showShops = true, showPeeps = true, showLitter = true;

	private static Texture? border, camBox, ridePin, shopPin, peepPin, litterPin, legendBg;
	private static Texture Border => border ??= Solid( 20, 30, 90, 255 );
	private static Texture CamBox => camBox ??= Solid( 255, 235, 90, 255 );
	private static Texture RidePin => ridePin ??= Solid( 90, 170, 255, 255 );   // blue rides
	private static Texture ShopPin => shopPin ??= Solid( 255, 150, 60, 255 );   // orange shops
	private static Texture PeepPin => peepPin ??= Solid( 240, 240, 240, 255 );  // white peeps
	private static Texture LitterPin => litterPin ??= Solid( 150, 110, 60, 255 ); // brown litter
	private static Texture LegendBg => legendBg ??= Solid( 18, 18, 28, 220 );

	private Texture? background;
	private bool backgroundTried;

	// The level's minimap image, loaded once (null if the level ships none).
	private Texture? Background()
	{
		if ( backgroundTried )
			return background;
		backgroundTried = true;
		try
		{
			var name = Level.Current?.Name ?? "jungle";
			using var s = FileSystem.OpenRead( $"/levels/{name}/2dmap.tga" );
			if ( s != null )
				background = new Texture( s );
		}
		catch ( Exception e ) { Log.Warning( $"[minimap] background load failed: {e.Message}" ); }
		return background;
	}

	private MinimapProjection? Projection()
	{
		if ( BuildMode.Current is not { } build )
			return null;
		var grid = build.Grid;
		return new MinimapProjection( grid.Origin.X, grid.Origin.Y, grid.Width * grid.TileSize, grid.Height * grid.TileSize, MapRect() );
	}

	// The layer buttons (rides, shops, visitors, litter) down the right.
	private IEnumerable<(Rectangle Rect, string Icon, bool On, Action Toggle)> Legend()
	{
		yield return (At( 704f, 90f, 50f, 50f ), "b_srides", showRides, () => showRides = !showRides);
		yield return (At( 704f, 150f, 50f, 50f ), "b_sshops", showShops, () => showShops = !showShops);
		yield return (At( 704f, 210f, 50f, 50f ), "b_svisit", showPeeps, () => showPeeps = !showPeeps);
		yield return (At( 704f, 270f, 50f, 50f ), "b_shandy", showLitter, () => showLitter = !showLitter);
	}

	protected override void OnUpdate()
	{
		// M toggles the whole minimap (edge-detected like the other overlays).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.M );
		if ( toggle && !wasToggleDown )
			IsOpen = !IsOpen;
		wasToggleDown = toggle;

		if ( !IsOpen || !Input.MouseLeftPressed )
			return;

		var mouse = MouseBase();

		// A legend click toggles that layer.
		foreach ( var b in Legend() )
			if ( Contains( b.Rect, mouse ) )
			{
				b.Toggle();
				return;
			}

		// A click on the map body pans the build camera to that world point.
		if ( Projection() is { } proj && proj.Contains( mouse ) )
		{
			var (wx, wy) = proj.Unproject( mouse );
			BuildCameraMode.Focus = new Vector3( wx, wy, BuildCameraMode.Focus.Z );
		}
	}

	protected override void OnRender()
	{
		if ( !IsOpen || Projection() is not { } proj )
			return;

		DrawFrame( "w_map" );
		var map = MapRect();
		var mat = Material.UI;

		// Backdrop: the level map image, or a dark fill if none shipped.
		mat.Set( "Color", Background() ?? LegendBg );
		Graphics.Quad( map, mat );
		DrawBorder( map, mat );

		// One pin per entity, by category (when its layer is on).
		if ( showRides )
			foreach ( var r in Entity.All.OfType<Ride>() )
				Pin( mat, proj, r.Position, RidePin, 7f );
		if ( showShops )
			foreach ( var s in Entity.All.OfType<Shop>() )
				Pin( mat, proj, s.Position, ShopPin, 7f );
		if ( showPeeps )
			foreach ( var p in Entity.All.OfType<Peep>() )
				Pin( mat, proj, p.Position, PeepPin, 3f );
		if ( showLitter )
			foreach ( var l in Litter.Active )
				Pin( mat, proj, l.Position, LitterPin, 3f );

		// Camera focus box.
		var c = proj.Project( BuildCameraMode.Focus.X, BuildCameraMode.Focus.Y );
		DrawHollow( new Rectangle( c.X - 14f, c.Y - 14f, 28f, 28f ), map, mat, CamBox );

		// Layer buttons: lit while the layer shows, pressed-in while hidden (clicks are handled in OnUpdate).
		foreach ( var b in Legend() )
			RoundButton( b.Rect, b.Icon, !b.On );
		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}

	// A centred square pin, clamped so it never spills past the map frame.
	private static void Pin( Material mat, MinimapProjection proj, Vector3 world, Texture tex, float size )
	{
		var p = proj.Project( world.X, world.Y );
		var map = MapRect();
		float x = Math.Clamp( p.X - size / 2f, map.X, map.X + map.Width - size );
		float y = Math.Clamp( p.Y - size / 2f, map.Y, map.Y + map.Height - size );
		mat.Set( "Color", tex );
		Graphics.Quad( new Rectangle( x, y, size, size ), mat );
	}

	private static void DrawBorder( Rectangle r, Material mat )
	{
		mat.Set( "Color", Border );
		const float t = 3f;
		Graphics.Quad( new Rectangle( r.X - t, r.Y - t, r.Width + 2 * t, t ), mat );              // bottom
		Graphics.Quad( new Rectangle( r.X - t, r.Y + r.Height, r.Width + 2 * t, t ), mat );        // top
		Graphics.Quad( new Rectangle( r.X - t, r.Y, t, r.Height ), mat );                          // left
		Graphics.Quad( new Rectangle( r.X + r.Width, r.Y, t, r.Height ), mat );                    // right
	}

	// A 1px-edge hollow box (the camera marker), clipped to the map bounds.
	private static void DrawHollow( Rectangle box, Rectangle clip, Material mat, Texture tex )
	{
		float x0 = Math.Max( box.X, clip.X ), y0 = Math.Max( box.Y, clip.Y );
		float x1 = Math.Min( box.X + box.Width, clip.X + clip.Width ), y1 = Math.Min( box.Y + box.Height, clip.Y + clip.Height );
		if ( x1 <= x0 || y1 <= y0 )
			return;
		mat.Set( "Color", tex );
		const float t = 1.5f;
		Graphics.Quad( new Rectangle( x0, y0, x1 - x0, t ), mat );
		Graphics.Quad( new Rectangle( x0, y1 - t, x1 - x0, t ), mat );
		Graphics.Quad( new Rectangle( x0, y0, t, y1 - y0 ), mat );
		Graphics.Quad( new Rectangle( x1 - t, y0, t, y1 - y0 ), mat );
	}
}
