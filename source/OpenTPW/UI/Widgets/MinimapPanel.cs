using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Corner minimap (T-057): the level's top-down <c>2dmap.tga</c> as a backdrop with a live icon per ride / shop
/// / peep / litter at its projected world position, plus a box marking the camera's focus. Toggled with M;
/// per-category layers toggle from the legend buttons along the bottom, and clicking the map pans the camera.
/// Projection is the pure <see cref="MinimapProjection"/> fed the live placement-grid bounds. In-park only.
/// </summary>
internal sealed class MinimapPanel : HudPanel
{
	public static MinimapPanel? Current { get; private set; }

	public MinimapPanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	// Upper area of the 1280×720 base space (Y-up), tucked just left of the right-anchored BUILD column
	// (which starts at x≈1012) so the two never overlap. The legend row sits just under the map.
	private const float MapSize = 200f;
	private static Rectangle MapRect() => new( 1006f - MapSize, 720f - MapSize - 16f, MapSize, MapSize );

	private bool open;

	/// <summary>Show / hide the map (the handheld's map button, or M).</summary>
	public void Toggle() => open = !open;
	private bool wasToggleDown;

	// Per-category layer visibility (toggled from the legend).
	private bool showRides = true, showShops = true, showPeeps = true, showLitter = true;

	private static Texture? frame, camBox, ridePin, shopPin, peepPin, litterPin, legendBg, legendOn;
	private static Texture Frame => frame ??= Solid( 230, 230, 240, 230 );
	private static Texture CamBox => camBox ??= Solid( 255, 235, 90, 255 );
	private static Texture RidePin => ridePin ??= Solid( 90, 170, 255, 255 );   // blue rides
	private static Texture ShopPin => shopPin ??= Solid( 255, 150, 60, 255 );   // orange shops
	private static Texture PeepPin => peepPin ??= Solid( 240, 240, 240, 255 );  // white peeps
	private static Texture LitterPin => litterPin ??= Solid( 150, 110, 60, 255 ); // brown litter
	private static Texture LegendBg => legendBg ??= Solid( 18, 18, 28, 220 );
	private static Texture LegendOn => legendOn ??= Solid( 60, 110, 70, 230 );

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

	// Legend buttons (R/S/P/L) laid out under the map; returns each with its toggle action + on-state.
	private IEnumerable<(Rectangle Rect, string Label, bool On, Action Toggle)> Legend()
	{
		var map = MapRect();
		float w = (MapSize - 9f) / 4f, h = 22f, y = map.Y - h - 4f;
		yield return (new Rectangle( map.X + 0 * (w + 3f), y, w, h ), "RIDE", showRides, () => showRides = !showRides);
		yield return (new Rectangle( map.X + 1 * (w + 3f), y, w, h ), "SHOP", showShops, () => showShops = !showShops);
		yield return (new Rectangle( map.X + 2 * (w + 3f), y, w, h ), "PEEP", showPeeps, () => showPeeps = !showPeeps);
		yield return (new Rectangle( map.X + 3 * (w + 3f), y, w, h ), "LITR", showLitter, () => showLitter = !showLitter);
	}

	/// <summary>True when the cursor is over the open minimap (so world tools ignore the click).</summary>
	public bool ContainsMouse()
	{
		if ( !open )
			return false;
		var m = MouseBase();
		if ( Contains( MapRect(), m ) )
			return true;
		foreach ( var b in Legend() )
			if ( Contains( b.Rect, m ) )
				return true;
		return false;
	}

	protected override void OnUpdate()
	{
		// M toggles the whole minimap (edge-detected like the other overlays).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.M );
		if ( toggle && !wasToggleDown )
			open = !open;
		wasToggleDown = toggle;

		if ( !open || !Input.MouseLeftPressed )
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
		if ( !open || Projection() is not { } proj )
			return;

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

		// Legend toggle buttons (lit when their layer is on).
		foreach ( var b in Legend() )
		{
			mat.Set( "Color", b.On ? LegendOn : LegendBg );
			Graphics.Quad( b.Rect, mat );
			Graphics.DrawText( Font, b.Label, b.Rect.X + 4f, b.Rect.Y + b.Rect.Height - 6f, TextAlign.Left, 1f );
		}
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
		mat.Set( "Color", Frame );
		const float t = 2f;
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
