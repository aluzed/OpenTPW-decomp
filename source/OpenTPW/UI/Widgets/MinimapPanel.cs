using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The park map (T-057), in the original's map window (<c>w_map</c>; the handheld's map button, or M): the level's
/// top-down <c>2dmap.tga</c> with live pins at their projected world positions and a box marking the camera's focus.
/// The original's layers toggle from the round buttons down the right (UIHELPTEXT 267–292): rides, shops, sideshows,
/// scenery, visitors, cleaners and litter, mechanics and ride reliability, guards, entertainers and queues, and the
/// visitors' happiness, the rides' excitement and the shops' customer satisfaction as red→green pins. Clicking the
/// map pans the camera.
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

	// The layers, toggled from the legend; each button's hint is "show …" (its index) or "hide …" (the next one).
	private enum Layer { Rides, Shops, Sideshows, Scenery, Visitors, Cleaners, Mechanics, Guards, Entertainers, Happiness, Excitement, Satisfaction }
	private static readonly (Layer Layer, string Icon, int Hint)[] Layers =
	{
		(Layer.Rides, "b_srides", 267), (Layer.Shops, "b_sshops", 269), (Layer.Sideshows, "b_sshows", 271),
		(Layer.Scenery, "b_sfeature", 273), (Layer.Visitors, "b_svisit", 277), (Layer.Cleaners, "b_shandy", 279),
		(Layer.Mechanics, "b_smech", 281), (Layer.Guards, "b_sguard", 283), (Layer.Entertainers, "b_senter", 285),
		(Layer.Happiness, "b_shap", 287), (Layer.Excitement, "b_sexcit", 289), (Layer.Satisfaction, "b_ssatis", 291),
	};
	private readonly HashSet<Layer> shown = new() { Layer.Rides, Layer.Shops, Layer.Sideshows, Layer.Visitors };
	private static Rectangle LayerRect( int i ) => At( 700f + i % 2 * 44f, 84f + i / 2 * 46f, 38f, 38f );
	private bool On( Layer l ) => shown.Contains( l );

	private static Texture? border, camBox, ridePin, shopPin, peepPin, litterPin, legendBg;
	private static Texture Border => border ??= Solid( 20, 30, 90, 255 );
	private static Texture CamBox => camBox ??= Solid( 255, 235, 90, 255 );
	private static Texture RidePin => ridePin ??= Solid( 90, 170, 255, 255 );   // blue rides
	private static Texture ShopPin => shopPin ??= Solid( 255, 150, 60, 255 );   // orange shops
	private static Texture PeepPin => peepPin ??= Solid( 240, 240, 240, 255 );  // white peeps
	private static Texture LitterPin => litterPin ??= Solid( 150, 110, 60, 255 ); // brown litter
	private static Texture SideshowPin => SolidOf( 190, 110, 230 ); // purple sideshows
	private static Texture SceneryPin => SolidOf( 70, 160, 70 );    // green scenery
	private static Texture StaffPin( StaffRole role ) => role switch
	{
		StaffRole.Handyman => SolidOf( 255, 230, 60 ), StaffRole.Mechanic => SolidOf( 60, 230, 230 ),
		StaffRole.Guard => SolidOf( 40, 60, 200 ), StaffRole.Entertainer => SolidOf( 255, 110, 200 ), _ => SolidOf( 255, 255, 255 ),
	};

	// A 0–1 rating as the gauges' red → yellow → green, in steps (each colour a cached texture).
	private static Texture Rated( float f )
	{
		f = Math.Clamp( f, 0f, 1f );
		float r = f < 0.5f ? 1f : 2f - 2f * f, g = f < 0.5f ? 2f * f : 1f;
		return SolidOf( (byte)(40 + r * 200), (byte)(40 + g * 180), 40 );
	}
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
		for ( int i = 0; i < Layers.Length; i++ )
			if ( Contains( LayerRect( i ), mouse ) )
			{
				if ( !shown.Remove( Layers[i].Layer ) )
					shown.Add( Layers[i].Layer );
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

		// One pin per entity, by category (when its layer is on). Rides take the colour of the rating layer that is on:
		// reliability (with the mechanics), how short their queue is (with the entertainers), or excitement.
		foreach ( var r in Entity.All.OfType<Ride>() )
		{
			if ( r.IsFeature )
			{
				if ( On( Layer.Scenery ) )
					Pin( mat, proj, r.Position, SceneryPin, 5f );
				continue;
			}
			if ( !On( r.IsSideshow ? Layer.Sideshows : Layer.Rides ) && !On( Layer.Mechanics ) && !On( Layer.Entertainers ) && !On( Layer.Excitement ) )
				continue;
			var tex = On( Layer.Mechanics ) ? Rated( r.Reliability )
				: On( Layer.Entertainers ) ? Rated( 1f - (r.Queue?.LineLength ?? 0) / MathF.Max( 1f, 3f * r.Capacity ) )
				: On( Layer.Excitement ) ? Rated( r.Excitement / 100f )
				: r.IsSideshow ? SideshowPin : RidePin;
			Pin( mat, proj, r.Position, tex, 8f );
		}
		if ( On( Layer.Shops ) || On( Layer.Satisfaction ) )
			foreach ( var s in Shop.Stalls )
				Pin( mat, proj, s.Position, On( Layer.Satisfaction ) ? Rated( s.Satisfaction ) : ShopPin, 7f );
		if ( On( Layer.Cleaners ) )
			foreach ( var l in Litter.Active )
				Pin( mat, proj, l.Position, LitterPin, 3f );
		if ( On( Layer.Visitors ) || On( Layer.Happiness ) )
			foreach ( var p in Entity.All.OfType<Peep>() )
				Pin( mat, proj, p.Position, On( Layer.Happiness ) ? Rated( p.Happiness / 100f ) : PeepPin, 3f );
		foreach ( var st in Entity.All.OfType<Staff>() )
			if ( st.Role switch
			{
				StaffRole.Handyman => On( Layer.Cleaners ), StaffRole.Mechanic => On( Layer.Mechanics ),
				StaffRole.Guard => On( Layer.Guards ), StaffRole.Entertainer => On( Layer.Entertainers ), _ => false,
			} )
				Pin( mat, proj, st.Position, StaffPin( st.Role ), 5f );

		// Camera focus box.
		var c = proj.Project( BuildCameraMode.Focus.X, BuildCameraMode.Focus.Y );
		DrawHollow( new Rectangle( c.X - 14f, c.Y - 14f, 28f, 28f ), map, mat, CamBox );

		// Layer buttons: lit while the layer shows, pressed-in while hidden (clicks are handled in OnUpdate).
		for ( int i = 0; i < Layers.Length; i++ )
		{
			var (layer, icon, hint) = Layers[i];
			RoundButton( LayerRect( i ), icon, !On( layer ), hint: On( layer ) ? hint + 1 : hint );
		}
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
