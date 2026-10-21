namespace OpenTPW;

/// <summary>
/// The original's ride / shop window (docs/10-ui-reference.md), opened by clicking a ride or a shop in the park: the
/// <c>w_park</c> frame with the item's name, a preview box, its figures as gauges, three setting boxes and the row of
/// round action buttons (sell, move, queue, ride it, call a mechanic, upgrade, gate), the previous / next arrows and
/// the ✓ that closes it down the right edge. Actions OpenTPW doesn't simulate yet are shown greyed out, so the
/// layout stays the original's. Measured on captures of the PC version.
/// </summary>
internal sealed class ItemInfoWindow : GameWindow
{
	public static ItemInfoWindow? Current { get; private set; }

	public ItemInfoWindow() => Current = this;

	public override void Close() => BuildMode.Current?.ClearSelection();

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	protected override Rectangle Frame => At( 120f, 20f, 720f, 440f );

	private static readonly Rectangle Preview = At( 165f, 77f, 192f, 191f );
	private static readonly Rectangle Stats = At( 403f, 77f, 328f, 179f );
	private static readonly Rectangle[] Boxes = { At( 152f, 285f, 196f, 72f ), At( 357f, 285f, 194f, 72f ), At( 561f, 285f, 195f, 72f ) };
	private static readonly Rectangle Up = At( 780f, 60f, 38f, 38f ), Down = At( 780f, 100f, 38f, 38f ), Ok = At( 781f, 381f, 42f, 42f );
	// The b_*.wct disc fills ~¾ of its texture: the quad overhangs the measured button by that margin.
	private static Rectangle Action( float x, float w = 66f ) => At( x - 9f, 367f, w + 18f, 82f );

	private static Texture? boxBg, labelBg;
	private static Texture BoxBg => boxBg ??= Solid( 18, 32, 98, 255 );
	private static Texture LabelBg => labelBg ??= Solid( 105, 215, 115, 255 );
	private static readonly System.Numerics.Vector3 LabelInk = new( 0.05f, 0.18f, 0.05f );

	// Dev aid: OPENTPW_UI_OPEN=ride selects the park's first ride once, to look at the window without clicking.
	private bool devOpened = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) is not ("ride" or "rideit");

	protected override void OnUpdate()
	{
		var build = BuildMode.Current;
		if ( !devOpened && build != null && Entity.All.OfType<Ride>().FirstOrDefault( r => !r.IsFeature ) is { } first )
		{
			devOpened = true;
			build.SelectRide( first );
			if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "rideit" )
			{
				build.ClearSelection();
				RideItCameraMode.Start( Entity.All.OfType<Ride>().FirstOrDefault( r => r.RiderPosition != null ) ?? first );
			}
		}
		IsOpen = build?.SelectedRide != null || build?.SelectedShop != null;
	}

	protected override void OnRender()
	{
		if ( !IsOpen || BuildMode.Current is not { } build || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_park" );
		DrawInset( Preview );
		DrawInset( Stats );

		if ( build.SelectedRide is { } ride )
			RenderRide( build, fin, ride );
		else if ( build.SelectedShop is { } shop )
			RenderShop( build, fin, shop );

		// Right-hand column: previous / next ride, close.
		var rides = Entity.All.OfType<Ride>().Where( r => !r.IsFeature ).ToList();
		int at = build.SelectedRide is { } current ? rides.IndexOf( current ) : -1;
		if ( RoundButton( Up, "b_arup", enabled: rides.Count > 1 && at >= 0 ) )
			build.SelectRide( rides[(at - 1 + rides.Count) % rides.Count] );
		if ( RoundButton( Down, "b_ardown", enabled: rides.Count > 1 && at >= 0 ) )
			build.SelectRide( rides[(at + 1) % rides.Count] );
		if ( RoundButton( Ok, "b_okay" ) )
			build.ClearSelection();
	}

	private static float statsY;

	private static void StatRow( string label, string? value, float? gauge = null )
	{
		Text( Small, label, 411f, statsY, 11f );
		if ( value != null )
			Text( Small, value, 616f, statsY, 11f );
		if ( gauge is { } g )
			DrawGauge( At( 616f, statsY + 1f, 110f, 10f ), g );
		statsY += 24f;
	}

	// One of the three setting boxes: the green label strip, a value gauge, and −/+ buttons when it can be changed.
	private static void SettingBox( int i, string label, float fraction, Action? less = null, Action? more = null )
	{
		var b = Boxes[i];
		Fill( b, BoxBg );
		DrawGauge( new Rectangle( b.X + 30f, b.Y + 36f, b.Width - 60f, 10f ), fraction );
		Fill( new Rectangle( b.X + 6f, b.Y + 5f, b.Width - 12f, 19f ), LabelBg );
		Graphics.DrawText( Small, label, b.X + b.Width / 2f, b.Y + 20f, LabelInk, TextAlign.Center, Scale( Small, 11f ) );
		if ( less != null && RoundButton( new Rectangle( b.X + 4f, b.Y + 30f, 22f, 22f ), "b_minus" ) )
			less();
		if ( more != null && RoundButton( new Rectangle( b.X + b.Width - 26f, b.Y + 30f, 22f, 22f ), "b_plus" ) )
			more();
	}

	private static void RenderRide( BuildMode build, ParkFinances fin, Ride ride )
	{
		DrawHeading( ride.Name, 460f, 0f, 44f );
		ItemPreview.For( ride.Archive, Path.GetFileName( ride.Archive ) )?.Draw( Preview );
		Text( Small, ride.IsBroken ? "Broken down!" : ride.Riders > 0 ? $"{ride.Riders} riding" : "", 261f, 250f, 11f,
			ride.IsBroken ? LockedInk : default, TextAlign.Center );

		statsY = 87f;
		StatRow( Localization.Get( UIStrings.UsersLastMonth, "Users last month" ), $"{ride.UsersLastMonth}" );
		StatRow( Localization.Get( UIStrings.Age, "Age" ), $"{ride.AgeDays} days" );
		StatRow( Localization.Get( UIStrings.Excitement, "Excitement" ), null, ride.Excitement / 100f );
		StatRow( Localization.Get( UIStrings.Reliability, "Reliability" ), null, ride.Reliability );
		if ( ride.IsResearching )
			StatRow( "Upgrade research", null, ride.ResearchFraction );
		StatRow( Localization.Get( UIStrings.ScrapValue, "Scrap value" ), $"$ {ride.BuildCost * Ride.SellRefundFraction:0}" );

		int maxCapacity = ride.Upgrades.Count > 0 ? ride.Upgrades.Max( u => u.Capacity ) : ride.Capacity;
		SettingBox( 0, $"Capacity : {ride.Capacity}", maxCapacity > 0 ? (float)ride.Capacity / maxCapacity : 1f );
		SettingBox( 1, $"Duration : {ride.RideDuration:0} secs", Math.Clamp( ride.RideDuration / 120f, 0f, 1f ) );
		if ( ride.IsSideshow )
			SettingBox( 2, $"Price : $ {ride.TicketPrice:0}", Math.Clamp( ride.TicketPrice / 20f, 0f, 1f ),
				() => ride.TicketPrice = Math.Max( 1f, ride.TicketPrice - 1f ), () => ride.TicketPrice += 1f );
		else
			SettingBox( 2, $"Level : {ride.UpgradeLevel + 1} / {Math.Max( 1, ride.Upgrades.Count )}",
				ride.Upgrades.Count > 0 ? (ride.UpgradeLevel + 1f) / ride.Upgrades.Count : 1f );

		if ( RoundButton( Action( 143f ), "b_delete_login" ) )
			build.SellSelected();
		// Move: pick the ride up and put it down elsewhere, as it is (not a ride with a laid track).
		if ( RoundButton( Action( 217f ), "b_move", enabled: ride.Track == null && ride.TileTrack == null && !ride.IsResearching ) )
			build.BeginMoveSelected();
		RoundButton( Action( 369f ), "b_queue", enabled: false );
		if ( RoundButton( Action( 450f ), "b_rideit" ) )
		{
			build.ClearSelection();
			RideItCameraMode.Start( ride );
		}
		// Call a mechanic: the nearest one comes to service the ride (lit while one is on the way).
		bool coming = Staff.MechanicCalledTo( ride );
		if ( RoundButton( Action( 525f ), "b_callmech", selected: coming, enabled: !coming && Entity.All.OfType<Staff>().Any( s => s.Role == StaffRole.Mechanic ) ) )
			Staff.CallMechanic( ride );
		// Upgrade: research the next level, then buy it (the R / U keys).
		bool canResearch = ride.HasNextLevel && !ride.NextResearched && !ride.IsResearching && fin.CanAfford( ride.NextResearchCost );
		bool canUpgrade = ride.NextResearched && fin.CanAfford( ride.NextUpgradeCost );
		if ( RoundButton( Action( 600f ), "b_upgrade", enabled: canResearch || canUpgrade ) )
		{
			if ( canUpgrade )
			{
				fin.PayBuild( ride.NextUpgradeCost );
				ride.ApplyUpgrade();
			}
			else
			{
				fin.PayBuild( ride.NextResearchCost );
				ride.StartResearch();
			}
		}
		// The gate: open (green light) / closed.
		if ( RoundButton( Action( 672f, 93f ), ride.IsClosed ? "b_doorc" : "b_dooro" ) )
			ride.SetClosed( !ride.IsClosed );
	}

	private static void RenderShop( BuildMode build, ParkFinances fin, Shop shop )
	{
		DrawHeading( shop.Name, 460f, 0f, 44f );
		if ( shop.Archive is { } archive )
			ItemPreview.For( archive, Path.GetFileName( archive ) )?.Draw( Preview );

		statsY = 87f;
		StatRow( Localization.Get( UIStrings.CustomersLastMonth, "Customers last month" ), $"{shop.CustomersLastMonth}" );
		if ( !shop.IsFree )
		{
			StatRow( Localization.Get( UIStrings.CostOfGoods, "Cost of goods" ), $"$ {shop.CostOfGoods:0}" );
			StatRow( Localization.Get( UIStrings.TakingsLastMonth, "Takings last month" ), $"$ {shop.TakingsLastMonth:0}" );
			StatRow( Localization.Get( UIStrings.ProfitLastMonth, "Profit last month" ), $"$ {shop.ProfitLastMonth:0}" );
		}
		else
			StatRow( "Queueing", $"{shop.Waiting}" );
		if ( shop.Kind == ShopKind.Toilet )
			StatRow( "Cleanliness", null, 1f - shop.Filth / 100f );
		StatRow( Localization.Get( UIStrings.ScrapValue, "Scrap value" ), $"$ {shop.BuildCost * Ride.SellRefundFraction:0}" );

		if ( !shop.IsFree )
			SettingBox( 2, $"{Localization.Get( UIStrings.SalePrice, "Sale price" )} : $ {shop.Price:0}", Math.Clamp( shop.Price / 10f, 0f, 1f ),
				() => shop.Price = Math.Max( 1f, shop.Price - 1f ), () => shop.Price += 1f );

		if ( RoundButton( Action( 143f ), "b_delete_login" ) )
			build.SellSelected();
		if ( RoundButton( Action( 217f ), "b_move" ) )
			build.BeginMoveSelected();
		if ( RoundButton( Action( 672f, 93f ), shop.IsClosed ? "b_doorc" : "b_dooro" ) )
			shop.IsClosed = !shop.IsClosed;
	}
}
