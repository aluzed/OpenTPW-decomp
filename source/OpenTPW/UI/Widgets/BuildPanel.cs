namespace OpenTPW;

/// <summary>
/// The original's buy window (docs/10-ui-reference.md; the handheld's buy button opens it): the <c>w_big</c> frame,
/// "Buy Ride" / "Buy Shop" / "Buy Sideshow" / "Buy Misc Items" at the top with the balance, a preview box, the
/// item's figures under it (number owned, excitement, safe capacity, reliability as gauges), the round category tabs
/// over a list of names and prices — items not researched yet in red — and the close button down the right edge.
/// Picking an item closes the window so it can be placed, as in the original. Measured on captures of the PC
/// version; the catalog is <see cref="BuildMode"/>'s.
/// </summary>
internal sealed class BuildPanel : GameWindow
{
	public static BuildPanel? Current { get; private set; }

	public BuildPanel()
	{
		Current = this;
		// Dev aid: open on load (OPENTPW_UI_OPEN=buy), to look at the window without clicking.
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "buy";
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	/// <summary>Whether the window is open.</summary>
	public static bool Open => Current?.IsOpen ?? false;

	/// <summary>Open / close the window (picking an item closes it too).</summary>
	public static void Toggle()
	{
		if ( Current is { } w )
			w.IsOpen = !w.IsOpen;
	}

	protected override Rectangle Frame => At( 95f, 18f, 850f, 448f );

	private static BuildCategory tab = BuildCategory.Rides;
	private int scroll;

	// The original's four tabs (ui.wad b_s*), plus staff (the original hires from its own window).
	private static readonly (BuildCategory Category, string Icon, UIStrings Title, string Fallback)[] Tabs =
	{
		(BuildCategory.Rides, "b_srides", UIStrings.BuyRide, "Buy Ride"),
		(BuildCategory.Sideshows, "b_sshows", UIStrings.BuySideshow, "Buy Sideshow"),
		(BuildCategory.Shops, "b_sshops", UIStrings.BuyShop, "Buy Shop"),
		(BuildCategory.Features, "b_sfeature", UIStrings.BuyMiscItems, "Buy Misc Items"),
		(BuildCategory.Staff, "b_shandy", UIStrings.HireCleaners, "Hire Staff"),
	};

	private static Rectangle TabRect( int i ) => At( 597f + i * 50f, 91f, 46f, 46f );
	private static readonly Rectangle Preview = At( 196f, 86f, 189f, 190f );
	private static readonly Rectangle Stats = At( 124f, 291f, 331f, 129f );
	private static readonly Rectangle List = At( 476f, 141f, 371f, 279f );
	private static readonly Rectangle CloseButton = At( 885f, 382f, 42f, 42f );
	private const float HeaderTop = 147f, HeaderHeight = 46f, RowTop = 202f, RowHeight = 14f;
	private const int VisibleRows = 15;

	private static Rectangle RowRect( int row ) => At( 482f, RowTop + row * RowHeight, 359f, RowHeight );

	private static List<int> Items( BuildMode build )
	{
		var list = new List<int>();
		for ( int i = 0; i < build.Catalog.Count; i++ )
			if ( build.Catalog[i].Category == tab )
				list.Add( i );
		return list;
	}

	private int hovered = -1;

	protected override void OnUpdate()
	{
		if ( !IsOpen || BuildMode.Current is not { } build )
			return;
		var m = MouseBase();
		var items = Items( build );
		hovered = -1;
		for ( int row = 0; row < VisibleRows && row + scroll < items.Count; row++ )
			if ( Contains( RowRect( row ), m ) )
				hovered = items[row + scroll];

		// The mouse wheel scrolls a long list.
		if ( Contains( List, m ) && Input.Mouse.Wheel != 0 )
			scroll = Math.Clamp( scroll - Math.Sign( Input.Mouse.Wheel ), 0, Math.Max( 0, items.Count - VisibleRows ) );

		if ( !Input.MouseLeftPressed )
			return;
		for ( int t = 0; t < Tabs.Length; t++ )
			if ( Contains( TabRect( t ), m ) )
			{
				tab = Tabs[t].Category;
				scroll = 0;
				return;
			}
		if ( Contains( CloseButton, m ) )
		{
			IsOpen = false;
			return;
		}
		// Picking an item arms it for placement and gets the window out of the way, as in the original.
		if ( hovered >= 0 && build.IsBuildable( hovered ) && (ParkFinances.Current?.CanAfford( build.Catalog[hovered].Cost ) ?? true) )
		{
			if ( build.Selected != hovered )
				build.SelectIndex( hovered );
			IsOpen = false;
		}
	}

	protected override void OnRender()
	{
		if ( !IsOpen || BuildMode.Current is not { } build )
			return;

		DrawFrame( "w_big" );
		int tabIndex = Array.FindIndex( Tabs, t => t.Category == tab );
		DrawHeading( Localization.Get( Tabs[tabIndex].Title, Tabs[tabIndex].Fallback ), 488f, 828f, 44f );

		// The item under the cursor (else the one armed) is described on the left.
		int shown = hovered >= 0 ? hovered : build.Selected >= 0 && build.Catalog[build.Selected].Category == tab ? build.Selected : -1;
		DrawInset( Preview );
		DrawInset( Stats );
		if ( shown >= 0 )
			DrawDetails( build, shown );

		// Tabs over the list.
		for ( int t = 0; t < Tabs.Length; t++ )
			RoundButton( TabRect( t ), Tabs[t].Icon, Tabs[t].Category == tab );

		DrawInset( List );
		Fill( At( 482f, HeaderTop, 196f, HeaderHeight ), Header );
		Fill( At( 682f, HeaderTop, 98f, HeaderHeight ), Header );
		Fill( At( 784f, HeaderTop, 57f, HeaderHeight ), Header );
		Text( Small, Localization.Get( UIStrings.Name, "Name" ), 580f, HeaderTop + 16f, 13f, HeaderInk, TextAlign.Center );
		Text( Small, Localization.Get( UIStrings.Price, "Price" ), 731f, HeaderTop + 16f, 13f, HeaderInk, TextAlign.Center );

		var fin = ParkFinances.Current;
		var items = Items( build );
		for ( int row = 0; row < VisibleRows && row + scroll < items.Count; row++ )
		{
			int i = items[row + scroll];
			var item = build.Catalog[i];
			float top = RowTop + row * RowHeight;
			if ( i == build.Selected || i == hovered )
				Fill( RowRect( row ), Selected );
			bool locked = !build.IsBuildable( i );
			bool affordable = fin?.CanAfford( item.Cost ) ?? true;
			var ink = locked || !affordable ? LockedInk : default;
			var name = item.Staff is { } role ? StaffNames.For( role ) : item.Label;
			if ( locked )
			{
				float done = ItemResearch.Current?.ProgressOf( item.Name ) ?? 0f;
				name = done > 0f ? $"??? {name} ???  {done * 100f:0}%" : $"??? {name} ???";
			}
			Text( Small, name, 487f, top + 1f, 11f, ink );
			Text( Small, $"$ {item.Cost:0}", 776f, top + 1f, 11f, ink, TextAlign.Right );
		}

		// The right-hand column: close.
		RoundButton( CloseButton, "b_exit" );
	}

	// The left-hand boxes: the item's name in the preview, its figures under it.
	private static void DrawDetails( BuildMode build, int index )
	{
		var item = build.Catalog[index];
		var name = item.Staff is { } role ? StaffNames.For( role ) : item.Label;
		Text( Small, name, 290f, 92f, 13f, default, TextAlign.Center );

		var figures = ItemFigures.For( item );
		float y = 300f;
		void Row( string label, string? value, float? gauge )
		{
			Text( Small, label, 133f, y, 11f );
			if ( value != null )
				Text( Small, value, 338f, y, 11f );
			if ( gauge is { } g )
				DrawGauge( At( 338f, y + 1f, 110f, 9f ), g );
			y += 24f;
		}
		int owned = Entity.All.OfType<Ride>().Count( r => r.Name.Equals( item.Name, StringComparison.OrdinalIgnoreCase ) );
		Row( Localization.Get( UIStrings.NumberOwned, "Number owned" ), owned == 0 ? "None" : owned.ToString(), null );
		if ( figures.Excitement is { } excitement )
			Row( Localization.Get( UIStrings.Excitement, "Excitement" ), null, excitement / 100f );
		if ( figures.Capacity is { } capacity )
			Row( Localization.Get( UIStrings.SafeCapacity, "Safe capacity" ), capacity.ToString(), null );
		if ( figures.Reliability is { } reliability )
			Row( Localization.Get( UIStrings.Reliability, "Reliability" ), null, reliability );
	}
}

/// <summary>An item's figures for the buy window, read from its <c>.sam</c> (cached).</summary>
internal readonly record struct ItemFigures( float? Excitement, int? Capacity, float? Reliability )
{
	private static readonly Dictionary<string, ItemFigures> cache = new( StringComparer.OrdinalIgnoreCase );

	public static ItemFigures For( BuildCatalogItem item )
	{
		if ( item.RidePath == null )
			return default;
		if ( cache.TryGetValue( item.Name, out var f ) )
			return f;
		try
		{
			var sam = new SettingsFile( FileSystem.OpenRead( $"{item.RidePath}/{item.Name}.sam" ) );
			float? F( string key ) => float.TryParse( sam[key], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v ) ? v : null;
			// Wear rate runs ~1 (sturdy) to ~10 (fragile): reliability is its complement.
			f = new ItemFigures( F( "UsageInfo.ExcitementLevel" ), (int?)F( "Upgrades[0].InitCapacity" ),
				F( "Upgrades[0].WearRate" ) is { } wear ? Math.Clamp( 1f - wear / 10f, 0.05f, 1f ) : null );
		}
		catch { f = default; }
		cache[item.Name] = f;
		return f;
	}
}
