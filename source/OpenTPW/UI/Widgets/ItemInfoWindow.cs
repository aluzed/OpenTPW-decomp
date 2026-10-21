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
	private bool devOpened = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) is not ("ride" or "rideit" or "shop" or "toilet" or "queue");

	protected override void OnUpdate()
	{
		var build = BuildMode.Current;
		if ( !devOpened && build != null && Entity.All.OfType<Ride>().FirstOrDefault( r => !r.IsFeature ) is { } first )
		{
			devOpened = true;
			build.SelectRide( first );
			var stall = Shop.Stalls.FirstOrDefault( s => s.Stats is { Ingredient: > 0 } ) ?? Shop.Stalls.FirstOrDefault( s => !s.IsFree );
			if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "shop" && stall != null )
				build.SelectShop( stall );
			if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "toilet" && Shop.Stalls.FirstOrDefault( s => s.Kind == ShopKind.Toilet ) is { } loo )
				build.SelectShop( loo );
			if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "rideit" )
			{
				build.ClearSelection();
				RideItCameraMode.Start( Entity.All.OfType<Ride>().FirstOrDefault( r => r.RiderPosition != null ) ?? first );
			}
			// Queue: reshape the first ride's queue with a turn each way, to look at the bends.
			if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "queue" && first.QueuePath is { } queue )
			{
				build.ToggleQueueTool();
				BuildCameraMode.Focus = first.Position.WithZ( BuildCameraMode.Focus.Z );
				while ( queue.SegmentCount > 2 )
					queue.Backtrack();
				var (x, y) = queue.Cells[^1];
				var (dx, dy) = (x - queue.Cells[0].X, y - queue.Cells[0].Y);
				foreach ( var step in new[] { (-dy, dx), (-dy, dx), (dx, dy), (dx, dy), (dy, -dx), (dy, -dx) } )
				{
					(x, y) = (x + step.Item1, y + step.Item2);
					queue.Extend( x, y );
				}
			}
		}
		IsOpen = (build?.SelectedRide != null && !build.LayingQueue) || build?.SelectedShop != null;
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

		// Right-hand column: previous / next ride (or stall: toilets among toilets), close.
		if ( build.SelectedShop is { } stall )
		{
			bool toilets = stall.Kind == ShopKind.Toilet;
			var stalls = Shop.Stalls.Where( s => (s.Kind == ShopKind.Toilet) == toilets ).ToList();
			int si = stalls.IndexOf( stall );
			if ( RoundButton( Up, "b_arup", hint: toilets ? 84 : 36, enabled: stalls.Count > 1 && si >= 0 ) )
				build.SelectShop( stalls[(si - 1 + stalls.Count) % stalls.Count] );
			if ( RoundButton( Down, "b_ardown", hint: toilets ? 83 : 35, enabled: stalls.Count > 1 && si >= 0 ) )
				build.SelectShop( stalls[(si + 1) % stalls.Count] );
		}
		else
		{
			var rides = Entity.All.OfType<Ride>().Where( r => !r.IsFeature ).ToList();
			int at = build.SelectedRide is { } current ? rides.IndexOf( current ) : -1;
			if ( RoundButton( Up, "b_arup", hint: 21, enabled: rides.Count > 1 && at >= 0 ) )
				build.SelectRide( rides[(at - 1 + rides.Count) % rides.Count] );
			if ( RoundButton( Down, "b_ardown", hint: 20, enabled: rides.Count > 1 && at >= 0 ) )
				build.SelectRide( rides[(at + 1) % rides.Count] );
		}
		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
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
		if ( EditableHeading( ride.Name, 460f, 44f, ride.IsSideshow ? 37 : 3 ) )
			RenameDialog.Open( Localization.Get( UIStrings.RenameItem, "Rename Item" ), Localization.Get( UIStrings.Name, "Name" ), ride.Name,
				name => { ride.Name = name; ride.Renamed = true; } );
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
		if ( ride.IsSideshow )
		{
			// A game: the prize it costs to give away, and the chance of winning it (the original's sideshow boxes).
			SettingBox( 0, $"Cost of prize : $ {ride.SideshowCostOfGoods}", Math.Clamp( ride.SideshowCostOfGoods / 20f, 0f, 1f ),
				() => ride.SideshowCostOfGoods = Math.Max( 0, ride.SideshowCostOfGoods - 1 ), () => ride.SideshowCostOfGoods++ );
			SettingBox( 1, $"Chance of winning : {100 - ride.SideshowChanceOfLoosing} %", (100 - ride.SideshowChanceOfLoosing) / 100f,
				() => ride.SideshowChanceOfLoosing = Math.Min( 100, ride.SideshowChanceOfLoosing + 5 ),
				() => ride.SideshowChanceOfLoosing = Math.Max( 0, ride.SideshowChanceOfLoosing - 5 ) );
		}
		else
		{
			// Fewer seats, shorter waits to fill a cycle (the original's capacity box); up to the upgrade's capacity.
			SettingBox( 0, $"Capacity : {ride.Capacity} / {ride.MaxCapacity}", maxCapacity > 0 ? (float)ride.Capacity / maxCapacity : 1f,
				ride.Capacity > 1 ? () => ride.SeatLimit = ride.Capacity - 1 : null,
				ride.Capacity < ride.MaxCapacity ? () => ride.SeatLimit = ride.Capacity + 1 >= ride.MaxCapacity ? null : ride.Capacity + 1 : null );
			// Longer rides, fewer riders an hour (the original's duration box).
			SettingBox( 1, $"Duration : {ride.RideDuration:0} secs", (ride.DurationScale - 0.5f) / 2.5f,
				() => ride.DurationScale -= 0.25f, () => ride.DurationScale += 0.25f );
		}
		if ( ride.IsSideshow )
			SettingBox( 2, $"Price : $ {ride.TicketPrice:0}", Math.Clamp( ride.TicketPrice / 20f, 0f, 1f ),
				() => ride.TicketPrice = Math.Max( 1f, ride.TicketPrice - 1f ), () => ride.TicketPrice += 1f );
		else
			SettingBox( 2, $"Level : {ride.UpgradeLevel + 1} / {Math.Max( 1, ride.Upgrades.Count )}",
				ride.Upgrades.Count > 0 ? (ride.UpgradeLevel + 1f) / ride.Upgrades.Count : 1f );

		if ( RoundButton( Action( 143f ), "b_delete_login", hint: 15 ) )
			build.SellSelected();
		// Move: pick the ride up and put it down elsewhere, as it is (not a ride with a laid track).
		if ( RoundButton( Action( 217f ), "b_move", hint: 14, enabled: ride.Track == null && ride.TileTrack == null && !ride.IsResearching ) )
			build.BeginMoveSelected();
		if ( RoundButton( Action( 369f ), "b_queue", enabled: ride.QueuePath != null, hint: 9 ) )
			build.ToggleQueueTool();
		if ( RoundButton( Action( 450f ), "b_rideit", hint: 16 ) )
		{
			build.ClearSelection();
			RideItCameraMode.Start( ride );
		}
		// Call a mechanic: the nearest one comes to service the ride (lit while one is on the way).
		bool coming = Staff.MechanicCalledTo( ride );
		if ( RoundButton( Action( 525f ), "b_callmech", hint: 8, selected: coming, enabled: !coming && Entity.All.OfType<Staff>().Any( s => s.Role == StaffRole.Mechanic ) ) )
			Staff.CallMechanic( ride );
		// Upgrade: research the next level, then buy it (the R / U keys).
		bool canResearch = ride.HasNextLevel && !ride.NextResearched && !ride.IsResearching && fin.CanAfford( ride.NextResearchCost );
		bool canUpgrade = ride.NextResearched && fin.CanAfford( ride.NextUpgradeCost );
		if ( RoundButton( Action( 600f ), "b_upgrade", hint: 19, enabled: canResearch || canUpgrade ) )
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
		if ( RoundButton( Action( 672f, 93f ), ride.IsClosed ? "b_doorc" : "b_dooro", hint: ride.IsClosed ? 12 : 13 ) )
			ride.SetClosed( !ride.IsClosed );
	}

	private static void RenderShop( BuildMode build, ParkFinances fin, Shop shop )
	{
		if ( EditableHeading( shop.Name, 460f, 44f, 22 ) )
			RenameDialog.Open( Localization.Get( UIStrings.RenameItem, "Rename Item" ), Localization.Get( UIStrings.Name, "Name" ), shop.Name,
				name => shop.CustomName = name );
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
		bool toilet = shop.Kind == ShopKind.Toilet;
		if ( !shop.IsFree )
			StatRow( Localization.Get( UIStrings.CustomerSatisfaction, "Customer satisfaction" ), null, shop.Satisfaction );
		if ( toilet )
			StatRow( Localization.Get( UIStrings.Cleanliness, "Cleanliness" ), null, 1f - shop.Filth / 100f );
		StatRow( Localization.Get( UIStrings.ScrapValue, "Scrap value" ), $"$ {shop.BuildCost * Ride.SellRefundFraction:0}" );

		if ( !shop.IsFree )
		{
			SettingBox( 0, $"{Localization.Get( UIStrings.QualityOfGoods, "Quality of goods" )} : {shop.Quality:0} %", shop.Quality / 100f,
				() => shop.Quality = Math.Max( 0f, shop.Quality - 10f ), () => shop.Quality = Math.Min( 100f, shop.Quality + 10f ) );
			if ( shop.Stats is { Ingredient: > 0 } st )
				SettingBox( 1, $"Amount of {ShopBuilding.IngredientName( st.Ingredient )} : {shop.IngredientAmount:0} %", shop.IngredientAmount / 100f,
					() => shop.IngredientAmount = Math.Max( 0f, shop.IngredientAmount - 10f ), () => shop.IngredientAmount = Math.Min( 100f, shop.IngredientAmount + 10f ) );
		}
		if ( !shop.IsFree )
			SettingBox( 2, $"{Localization.Get( UIStrings.SalePrice, "Sale price" )} : $ {shop.Price:0}", Math.Clamp( shop.Price / 10f, 0f, 1f ),
				() => shop.Price = Math.Max( 1f, shop.Price - 1f ), () => shop.Price += 1f );

		// A toilet's buttons have their own hints (UIHELPTEXT 77–81), and it can call a cleaner.
		if ( RoundButton( Action( 143f ), "b_delete_login", hint: toilet ? 81 : 33 ) )
			build.SellSelected();
		if ( RoundButton( Action( 217f ), "b_move", hint: toilet ? 80 : 32 ) )
			build.BeginMoveSelected();
		if ( toilet )
		{
			bool coming = Staff.CleanerCalledTo( shop );
			if ( RoundButton( Action( 369f ), "b_callhandy", hint: 77, selected: coming,
				enabled: !coming && Entity.All.OfType<Staff>().Any( s => s.Role == StaffRole.Handyman ) ) )
				Staff.CallCleaner( shop );
		}
		// Apply these settings (price, open / closed) to every stall of the same kind (UIHELPTEXT 536).
		var same = Shop.Stalls.Where( s => s != shop && s.CatalogKey == shop.CatalogKey ).ToList();
		if ( RoundButton( Action( 525f ), "b_applyall", hint: 536, enabled: same.Count > 0 ) )
			foreach ( var s in same )
			{
				s.Price = shop.Price;
				s.IsClosed = shop.IsClosed;
				s.Quality = shop.Quality;
				s.IngredientAmount = shop.IngredientAmount;
			}
		if ( RoundButton( Action( 672f, 93f ), shop.IsClosed ? "b_doorc" : "b_dooro", hint: toilet ? (shop.IsClosed ? 78 : 79) : shop.IsClosed ? 30 : 31 ) )
			shop.IsClosed = !shop.IsClosed;
	}
}
