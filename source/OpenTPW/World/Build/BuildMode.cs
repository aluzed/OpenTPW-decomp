using Veldrid;

namespace OpenTPW;

/// <summary>One placeable catalog entry: a ride (RidePath set), a shop (all null), or a staff member to
/// hire (Staff set). Staff occupy no grid cell; rides/shops reserve their footprint. <paramref name="HmpPath"/>
/// optionally points at an <c>.hmp</c> footprint template, so the piece reserves only its solid tiles
/// (queue paths / fences leave their passable cells walkable) instead of the Width×Height rectangle (T-052).
/// <paramref name="ShopPath"/> names a specific stall archive (gift shop, ice-cream stand…); a shop without one is the
/// theme's default stall of its kind.</summary>
public readonly record struct BuildCatalogItem( string Name, string? RidePath, StaffRole? Staff, int Width, int Height, float Cost, string? HmpPath = null, string? Title = null, string? ShopPath = null )
{
	/// <summary>What the catalog shows: the item's real (localized) name when known, else its key.</summary>
	public string Label => Title ?? Name;

	/// <summary>The build menu tab the item sits in — the original's UI categories (<c>Info.WhichUIType</c>: rides,
	/// shops, sideshows, features) plus staff.</summary>
	public BuildCategory Category => Staff != null ? BuildCategory.Staff
		: RidePath is not { } path ? BuildCategory.Shops
		: path.Contains( "/sideshow/", StringComparison.OrdinalIgnoreCase ) ? BuildCategory.Sideshows
		: path.Contains( "/features/", StringComparison.OrdinalIgnoreCase ) ? BuildCategory.Features
		: BuildCategory.Rides;
}

/// <summary>The build menu's tabs.</summary>
public enum BuildCategory { Rides, Sideshows, Shops, Features, Staff }

/// <summary>
/// Build/manage mode (T-040 foundation + T-041 placement). Each frame it projects the cursor to the
/// <see cref="PlacementGrid"/> tile under it and highlights it. With a catalog item selected (number
/// keys), it shows the item's footprint preview (green = placeable & affordable, red = not) and, on
/// left-click, commits the placement via the supplied callback (which spawns + charges). Esc / 0 leaves
/// the placement tool (Default: click logs the tile — selection hook for later tools).
/// </summary>
public sealed class BuildMode : Entity
{
	public static BuildMode? Current { get; private set; }

	private static readonly Vector3 Offscreen = new( 0, 0, -100000f );
	private static readonly Key[] NumberKeys =
	{
		Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5,
		Key.Number6, Key.Number7, Key.Number8, Key.Number9,
	};

	private readonly PlacementGrid grid;
	private readonly ParkTerrain terrain;

	/// <summary>The park's ground (for cameras that walk on it).</summary>
	public ParkTerrain Terrain => terrain;
	private readonly Func<BuildCatalogItem, int, int, int, bool> commit;
	private readonly Action<Ride> demolish;
	private readonly Action<Shop> demolishShop;
	private readonly ModelEntity highlight;
	private readonly Material<ObjectUniformBuffer> mat;
	private readonly Texture yellow = new( [255, 235, 40, 110], 1, 1 );
	private readonly Texture green = new( [60, 220, 70, 120], 1, 1 );
	private readonly Texture red = new( [230, 50, 40, 120], 1, 1 );
	private readonly HashSet<Key> prevDown = new();

	/// <summary>The build catalog (rides + shops).</summary>
	public IReadOnlyList<BuildCatalogItem> Catalog { get; }

	/// <summary>The park's placement grid — its world bounds drive the minimap projection (T-057).</summary>
	public PlacementGrid Grid => grid;

	/// <summary>Index of the selected catalog item, or -1 when no placement tool is active.</summary>
	public int Selected { get; private set; } = -1;

	/// <summary>The active save slot (1..<see cref="SaveGame.SlotCount"/>); F5 saves it, F9 loads it, F6 cycles it (T-059).</summary>
	public int SaveSlot { get; private set; } = 1;

	/// <summary>Select catalog item <paramref name="index"/> (or clear with any out-of-range value, e.g. -1).
	/// Clicking the already-selected item toggles it off. Used by the clickable build UI (T-038).</summary>
	public void SelectIndex( int index )
	{
		if ( putDown != null )
			return; // a moved ride must be put down first
		Selected = (index >= 0 && index < Catalog.Count && index != Selected && IsBuildable( index )) ? index : -1;
	}

	/// <summary>Whether catalog item <paramref name="index"/> has been researched (or needed no research).</summary>
	public bool IsBuildable( int index ) => ItemResearch.Current?.IsUnlocked( Catalog[index].Name ) ?? true;

	public (int X, int Y)? HoveredTile { get; private set; }

	/// <summary>The shop currently selected (clicked) in the Default tool — sellable like a ride (T-041).</summary>
	public Shop? SelectedShop { get; private set; }

	/// <summary>The staff member currently selected (clicked) in the Default tool — firable / zonable (T-049).</summary>
	public Staff? SelectedStaff { get; private set; }

	/// <summary>The visitor currently selected (clicked) — their window shows how they're getting on.</summary>
	public Peep? SelectedPeep { get; private set; }

	private const float StaffPickRadius = 18f;   // world distance from the clicked point that counts as "this staffer"
	private const float DefaultZoneRadius = 80f;  // initial patrol-zone radius when first assigned
	private const float ZoneStep = 20f;           // radius change per ZONE-/ZONE+ press

	/// <summary>Dismiss the selected staff member (T-049). No-op if none selected.</summary>
	public void FireSelectedStaff()
	{
		if ( SelectedStaff is { } s )
		{
			s.Fire();
			SelectedStaff = null;
			movingStaff = null;
		}
	}

	/// <summary>Anchor the selected staff member's patrol zone at its current position (T-049).</summary>
	public void SetSelectedStaffZoneHere()
		=> SelectedStaff?.SetPatrolZone( SelectedStaff.Position, SelectedStaff.Zone?.Radius ?? DefaultZoneRadius );

	/// <summary>Grow / shrink the selected staff member's patrol zone radius (assigning one if absent). T-049.</summary>
	public void AdjustSelectedStaffZone( int steps )
	{
		if ( SelectedStaff is not { } s )
			return;
		var center = s.Zone?.Center ?? s.Position;
		var radius = (s.Zone?.Radius ?? DefaultZoneRadius) + steps * ZoneStep;
		s.SetPatrolZone( center, radius );
	}

	/// <summary>Lift the selected staff member's patrol zone (back to free roam). T-049.</summary>
	public void ClearSelectedStaffZone() => SelectedStaff?.ClearPatrolZone();

	// A staffer picked up to be re-placed: the next click on a tile relocates it there (T-043).
	private Staff? movingStaff;

	/// <summary>True while a selected staffer is being moved — the next tile click drops it (T-043).</summary>
	public bool IsMovingStaff => movingStaff != null;

	/// <summary>Pick up the selected staffer to relocate it: the next valid click moves it (T-043). Toggles
	/// off if already moving.</summary>
	public void BeginMoveSelectedStaff() => movingStaff = movingStaff == null ? SelectedStaff : null;

	/// <summary>Drop the selected ride / shop / staffer (the item window's close button).</summary>
	public void ClearSelection()
	{
		SelectedPeep = null;
		SelectedRide = null;
		SelectedShop = null;
		SelectedStaff = null;
	}

	/// <summary>Select <paramref name="ride"/> (the item window's previous / next arrows).</summary>
	public void SelectRide( Ride ride )
	{
		SelectedRide = ride;
		SelectedShop = null;
		SelectedStaff = null;
		SelectedPeep = null;
	}

	/// <summary>Select <paramref name="staff"/> (opens their window).</summary>
	public void SelectStaff( Staff staff )
	{
		SelectedRide = null;
		SelectedShop = null;
		SelectedStaff = staff;
		SelectedPeep = null;
	}

	/// <summary>Select <paramref name="peep"/> (opens their window).</summary>
	public void SelectPeep( Peep peep )
	{
		ClearSelection();
		SelectedPeep = peep;
	}

	// An item being moved (picked up, waiting to be put down): how to put it down at a tile, and back where it was.
	private Func<int, int, int, bool>? putDown;
	private Action? putBack;

	/// <summary>Whether a ride or shop is picked up, waiting to be put down.</summary>
	public bool IsMovingItem => putDown != null;

	/// <summary>Pick up the selected ride or shop to move it (its window's move button); false when it can't be moved.</summary>
	public bool BeginMoveSelected()
	{
		if ( SelectedRide is { } ride )
		{
			int index = Catalog.ToList().FindIndex( c => c.RidePath != null && Path.GetFileName( c.RidePath ) == Path.GetFileName( ride.Archive ) );
			if ( index < 0 || Level.PickUpRide( ride, grid ) is not { } moved )
				return false;
			putDown = ( tx, ty, rot ) => Level.PutDownRide( moved, Catalog[index], grid, terrain, tx, ty, rot );
			putBack = () => Level.PutDownRide( moved, Catalog[index], grid, terrain, moved.FromX, moved.FromY, moved.FromRotation );
			Rotation = ride.Rotation;
			Selected = index;
		}
		else if ( SelectedShop is { } shop )
		{
			int index = Catalog.ToList().FindIndex( c => c.Name == shop.CatalogKey );
			if ( index < 0 || Level.PickUpShop( shop, grid ) is not { } moved )
				return false;
			putDown = ( tx, ty, _ ) => Level.PutDownShop( moved, Catalog[index], grid, terrain, tx, ty );
			putBack = () => Level.PutDownShop( moved, Catalog[index], grid, terrain, moved.FromX, moved.FromY );
			Selected = index;
		}
		else
			return false;
		SelectedRide = null;
		SelectedShop = null;
		return true;
	}

	/// <summary>Sell/demolish the selected ride or shop: refunds part of its cost and tears it out of the
	/// park (T-041). No-op if nothing is selected. Exposed for the manage UI + the Delete shortcut.</summary>
	public void SellSelected()
	{
		if ( SelectedRide != null || SelectedShop != null )
			UiSounds.Play( UiSounds.Sell );
		if ( SelectedRide is { } ride )
		{
			if ( track?.Owner == ride )
				track = null; // the track is owned by the ride; Ride.Despawn tears it down
			demolish( ride );
			SelectedRide = null;
		}
		else if ( SelectedShop is { } shop )
		{
			demolishShop( shop );
			SelectedShop = null;
		}
	}

	public BuildMode( PlacementGrid grid, ParkTerrain terrain, IReadOnlyList<BuildCatalogItem> catalog,
		Func<BuildCatalogItem, int, int, int, bool> commit, Action<Ride> demolish, Action<Shop> demolishShop )
	{
		this.grid = grid;
		this.terrain = terrain;
		this.commit = commit;
		this.demolish = demolish;
		this.demolishShop = demolishShop;
		Catalog = catalog;
		Current = this;

		mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader",
			MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite );
		mat.Set( "Color", yellow );
		highlight = new ModelEntity { Model = Primitives.Plane.GenerateModel( mat ), Position = Offscreen };
	}

	protected override void OnUpdate()
	{
		HandleSelectionKeys();

		// While the cursor is over any HUD panel, don't interact with the world (the panel owns the click —
		// otherwise clicking a catalog/manage button would also place/select on the tile behind it). T-038.
		if ( HudPanel.PointerOverUi() )
		{
			highlight.Position = Offscreen;
			return;
		}

		if ( !TryPickGround( out var hit ) )
		{
			HoveredTile = null;
			highlight.Position = Offscreen;
			return;
		}

		var (tx, ty) = grid.WorldToTile( hit );
		HoveredTile = grid.InBounds( tx, ty ) ? (tx, ty) : null;

		if ( track != null )
			UpdateTrack( tx, ty );
		else if ( Selected >= 0 )
			UpdatePlacement( tx, ty );
		else
			UpdateDefault( tx, ty );
	}

	/// <summary>The ride currently selected (clicked) in the Default tool — its price is adjustable.</summary>
	public Ride? SelectedRide { get; private set; }

	private ITrackTool? track;
	/// <summary>Whether the coaster track-laying tool is active, and how many segments are laid (T-045).</summary>
	public bool LayingTrack => track != null;
	public int TrackSegments => track?.SegmentCount ?? 0;
	/// <summary>Whether the track being laid forms a complete circuit (back to the station entry).</summary>
	public bool TrackClosed => track?.IsClosed ?? false;

	// Track-laying tool: highlight the candidate tile (green if it extends the track), lay on click.
	private void UpdateTrack( int tx, int ty )
	{
		if ( HoveredTile == null )
		{
			highlight.Position = Offscreen;
			return;
		}
		bool ok = track!.CanExtend( tx, ty ) && (track is not TileTrack tt || (ParkFinances.Current?.CanAfford( tt.PieceCost ) ?? true));
		mat.Set( "Color", ok ? green : red );
		highlight.Scale = new Vector3( grid.TileSize / 2f );
		var c = grid.TileToWorld( tx, ty );
		highlight.Position = c.WithZ( terrain.SampleHeight( c.X, c.Y ) + 0.3f );
		if ( Input.MouseLeftPressed && ok && track.Extend( tx, ty ) && track is TileTrack laid )
			ParkFinances.Current?.PayBuild( laid.PieceCost ); // Costs.KartTrackCell / WaterTrackCell per piece
	}

	// Number keys pick a catalog item; Esc/0 cancels; economy keys adjust prices/fee/loans (T-042).
	private void HandleSelectionKeys()
	{
		var down = Input.Keyboard.KeysDown;
		bool Hit( Key k ) => down.Contains( k ) && !prevDown.Contains( k );

		for ( int i = 0; i < NumberKeys.Length && i < Catalog.Count; i++ )
			if ( Hit( NumberKeys[i] ) && IsBuildable( i ) && putDown == null )
				Selected = i;
		// Esc puts down the item being placed, else closes the open windows.
		if ( Hit( Key.Escape ) && Selected < 0 && !CamcorderCameraMode.Active && RideItCameraMode.Target == null )
			GameWindow.CloseOpen();
		if ( Hit( Key.Escape ) || Hit( Key.Number0 ) )
		{
			// A ride being moved goes back where it was.
			if ( putBack != null )
			{
				putBack();
				putDown = null;
				putBack = null;
			}
			Selected = -1;
		}

		// R rotates the ride being placed (90° CW per press); only meaningful for a ride placement tool.
		if ( Hit( Key.R ) && Selected >= 0 && Catalog[Selected].RidePath != null )
			Rotation = (Rotation + 1) % 4;

		// Cycle the park theme (F7): tears down + reloads into the next world — jungle→hallow→fantasy→space (T-062).
		if ( Hit( Key.F7 ) ) Level.RequestReload( Level.NextTheme() );

		// Save (F5) / load (F9) the park to the active slot; F6 cycles the slot 1→2→3→1 (T-059).
		if ( Hit( Key.F6 ) ) SaveSlot = SaveSlot % SaveGame.SlotCount + 1;
		if ( Hit( Key.F5 ) ) Level.CaptureSave().WriteToFile( SaveGame.SlotPath( SaveSlot ) );
		if ( Hit( Key.F9 ) && SaveGame.ReadFromFile( SaveGame.SlotPath( SaveSlot ) ) is { } loaded ) Level.ApplySave( loaded );

		var fin = ParkFinances.Current;
		if ( fin != null )
		{
			// Admission fee.
			if ( Hit( Key.BracketLeft ) ) fin.EntryFee = MathF.Max( 0f, fin.EntryFee - 1f );
			if ( Hit( Key.BracketRight ) ) fin.EntryFee += 1f;
			// Challenge accept (Y) / decline (N) while one is offered (T-054).
			if ( ChallengeManager.Current is { State: ChallengeManager.Phase.Offered } ch )
			{
				if ( Hit( Key.Y ) ) ch.Accept();
				if ( Hit( Key.N ) ) ch.Decline();
			}
			// Selected ride: ticket price (, .), research next upgrade (R), apply it (U) — T-042 / T-044.
			// Only in the Default tool (Selected < 0); while placing (Selected ≥ 0), R rotates instead.
			if ( SelectedRide is { } sel && Selected < 0 )
			{
				if ( sel.IsSideshow && Hit( Key.Comma ) ) sel.TicketPrice = MathF.Max( 1f, sel.TicketPrice - 1f );
				if ( sel.IsSideshow && Hit( Key.Period ) ) sel.TicketPrice += 1f;
				if ( Hit( Key.R ) && sel.HasNextLevel && !sel.NextResearched && !sel.IsResearching
					&& fin.CanAfford( sel.NextResearchCost ) )
				{
					fin.PayBuild( sel.NextResearchCost );
					sel.StartResearch();
				}
				if ( Hit( Key.U ) && sel.NextResearched && fin.CanAfford( sel.NextUpgradeCost ) )
				{
					fin.PayBuild( sel.NextUpgradeCost );
					sel.ApplyUpgrade();
				}
				if ( Hit( Key.Delete ) ) SellSelected(); // sell/demolish the selected ride (T-041)
			}
			// Selected shop: snack/drink price (, .) — a toilet stays free (T-041).
			if ( SelectedShop is { IsFree: false } shopSel && Selected < 0 )
			{
				if ( Hit( Key.Comma ) ) shopSel.Price = MathF.Max( 1f, shopSel.Price - 1f );
				if ( Hit( Key.Period ) ) shopSel.Price += 1f;
				if ( Hit( Key.Delete ) ) SellSelected();
			}
			// Loans: L opens the loan offers (LoanPanel); K repays the first loan taken.
			if ( Hit( Key.L ) ) LoanPanel.Current?.Toggle();
			if ( Hit( Key.K ) && fin.Loans.ToList().FindIndex( l => l.Bought ) is var k and >= 0 ) fin.RepayLoan( k );
		}

		// Coaster track tool (T-045): T toggles laying from the selected coaster's connector, B backtracks.
		if ( Hit( Key.T ) )
		{
			if ( track != null )
				track = null;
			else if ( SelectedRide is { } cr && cr.Shape.HasTrack )
				track = cr.Track ?? new CoasterTrack( cr, grid, terrain );
			else if ( SelectedRide is { } kr && (kr.TileTrack ?? Level.NewTileTrack( kr )) is { } tileTrack )
				track = tileTrack; // a go-kart / water ride's piece-built track
		}
		if ( track != null )
		{
			if ( Hit( Key.B ) ) track.Backtrack();
		}
		else if ( Selected < 0 && !CamcorderCameraMode.Active && RideItCameraMode.Target == null )
		{
			// The original's window keys (UIHELPTEXT.str): B buy, H hire, F finance, I park information, R research
			// (R on a selected ride researches its upgrade instead), Space map, G the gates, C the camcorder.
			if ( Hit( Key.B ) ) BuildPanel.Toggle();
			if ( Hit( Key.H ) ) BuildPanel.OpenHire();
			if ( Hit( Key.F ) ) FinancePanel.Current?.Toggle();
			if ( Hit( Key.I ) ) ParkInfoWindow.Current?.Toggle();
			if ( Hit( Key.R ) && SelectedRide == null ) ResearchWindow.Current?.Toggle();
			if ( Hit( Key.Space ) ) MinimapPanel.Current?.Toggle();
			if ( Hit( Key.G ) ) Level.ToggleGates();
			if ( Hit( Key.C ) ) CamcorderCameraMode.Start();
		}
		if ( track is CoasterTrack coasterTrack )
		{
			if ( Hit( Key.PageUp ) ) coasterTrack.StackHead( +1 );   // raise the head segment (build a hill)
			if ( Hit( Key.PageDown ) ) coasterTrack.StackHead( -1 ); // lower it (build a dip)
			// O: a vertical loop on the straight just laid (three level tiles in a row), paid like a build.
			if ( Hit( Key.O ) && coasterTrack.CanLoopAtHead() && ParkFinances.Current is { } loopFin && loopFin.CanAfford( CoasterTrack.LoopCost ) )
			{
				loopFin.PayBuild( CoasterTrack.LoopCost );
				coasterTrack.AddLoopAtHead();
			}
		}

		prevDown.Clear();
		foreach ( var k in down )
			prevDown.Add( k );
	}

	// Default tool: highlight the hovered tile; click selects the ride or shop covering it (for price
	// control / sell). A ride takes precedence if one covers the tile; selecting one clears the other.
	private void UpdateDefault( int tx, int ty )
	{
		if ( HoveredTile == null )
		{
			highlight.Position = Offscreen;
			return;
		}
		mat.Set( "Color", yellow );
		highlight.Scale = new Vector3( grid.TileSize / 2f );
		var c = grid.TileToWorld( tx, ty );
		highlight.Position = c.WithZ( terrain.SampleHeight( c.X, c.Y ) + 0.3f );

		if ( Input.MouseLeftPressed )
		{
			// Re-placing a picked-up staffer: this click drops it on the hovered tile instead of selecting (T-043).
			if ( movingStaff is { } ms )
			{
				ms.Relocate( c.WithZ( 0 ) );
				Log.Info( $"[build] moved {ms.Role} to ({tx},{ty})" );
				movingStaff = null;
				return;
			}

			// Ctrl-click on a ride or stall buys another of it (the original's "Hold CTRL and click to buy another").
			if ( Input.Keyboard.KeysDown.Contains( Key.ControlLeft ) || Input.Keyboard.KeysDown.Contains( Key.ControlRight ) )
			{
				int another = Entity.All.OfType<Ride>().FirstOrDefault( r => r.Covers( tx, ty ) ) is { } likeRide
					? Catalog.ToList().FindIndex( ci => ci.RidePath != null && Path.GetFileName( ci.RidePath ) == Path.GetFileName( likeRide.Archive ) )
					: Shop.Stalls.FirstOrDefault( s => s.Covers( tx, ty ) ) is { } likeShop
						? Catalog.ToList().FindIndex( ci => ci.Name == likeShop.CatalogKey ) : -1;
				if ( another >= 0 && IsBuildable( another ) )
				{
					Selected = another;
					return;
				}
			}

			SelectedRide = Entity.All.OfType<Ride>().FirstOrDefault( r => r.Covers( tx, ty ) );
			SelectedShop = SelectedRide == null ? Shop.Stalls.FirstOrDefault( s => s.Covers( tx, ty ) ) : null;
			// Staff have no footprint, so pick the nearest one to the clicked point (T-049). A ride/shop
			// under the cursor takes precedence.
			SelectedStaff = SelectedRide == null && SelectedShop == null ? NearestStaffTo( c ) : null;
			SelectedPeep = SelectedRide == null && SelectedShop == null && SelectedStaff == null ? NearestPeepTo( c ) : null;
			Log.Info( SelectedRide != null ? $"[build] selected {SelectedRide.Name} (price {SelectedRide.TicketPrice:0})"
				: SelectedShop != null ? $"[build] selected {SelectedShop.Name}"
				: SelectedStaff != null ? $"[build] selected {SelectedStaff.Role}"
				: $"[build] click tile ({tx},{ty})" );
		}
	}

	// Nearest visitor in the park (not riding) to a world point, within the pick radius.
	private static Peep? NearestPeepTo( Vector3 p )
		=> Entity.All.OfType<Peep>().Where( v => (v.Position - p).WithZ( 0 ).LengthSquared <= StaffPickRadius * StaffPickRadius )
			.OrderBy( v => (v.Position - p).WithZ( 0 ).LengthSquared ).FirstOrDefault();

	// Nearest staff member to a world point, within the pick radius (XY) — null if none is close (T-049).
	private static Staff? NearestStaffTo( Vector3 p )
	{
		Staff? best = null;
		float bestD2 = StaffPickRadius * StaffPickRadius;
		foreach ( var e in Entity.All )
		{
			if ( e is not Staff s )
				continue;
			float dx = s.Position.X - p.X, dy = s.Position.Y - p.Y;
			float d2 = dx * dx + dy * dy;
			if ( d2 <= bestD2 )
			{
				bestD2 = d2;
				best = s;
			}
		}
		return best;
	}

	// Placement tool: show the selected item's footprint, green/red by validity, place on click.
	private void UpdatePlacement( int tx, int ty )
	{
		var item = Catalog[Selected];
		if ( HoveredTile == null )
		{
			highlight.Position = Offscreen;
			return;
		}

		// A rotated ride footprint swaps width/height on odd quarter-turns (T-041). Only rides rotate;
		// shops are square and staff have no footprint.
		var (ew, eh) = RotatedFootprint( item );

		// Staff don't reserve a cell (they walk anywhere) — only need to be on-grid; rides/shops must fit.
		bool canPlace = item.Staff != null ? grid.InBounds( tx, ty ) : grid.CanPlace( tx, ty, ew, eh );
		bool affordable = ParkFinances.Current?.CanAfford( item.Cost ) ?? true;
		bool ok = canPlace && affordable;

		mat.Set( "Color", ok ? green : red );
		highlight.Scale = new Vector3( ew * grid.TileSize / 2f, eh * grid.TileSize / 2f, 1f );
		var c = grid.TileToWorld( tx, ty, ew, eh );
		highlight.Position = c.WithZ( terrain.SampleHeight( c.X, c.Y ) + 0.3f );

		if ( putDown != null )
		{
			// Putting a moved item down: free, wherever it fits.
			if ( Input.MouseLeftPressed && canPlace && putDown( tx, ty, Rotation ) )
			{
				Log.Info( $"[build] moved {item.Name} to ({tx},{ty})" );
				putDown = null;
				putBack = null;
				Selected = -1;
			}
			return;
		}
		if ( Input.MouseLeftPressed && ok && commit( item, tx, ty, Rotation ) )
		{
			Log.Info( $"[build] placed {item.Name} at ({tx},{ty}) rot {Rotation} for ${item.Cost:0}" );
			// As the original: one placement per pick, Ctrl held to build more than one.
			if ( !Input.Keyboard.KeysDown.Contains( Key.ControlLeft ) && !Input.Keyboard.KeysDown.Contains( Key.ControlRight ) )
				Selected = -1;
		}
	}

	/// <summary>The current placement rotation in 90° clockwise steps (0–3), for the manage UI hint.</summary>
	public int Rotation { get; private set; }

	// The item's footprint under the current rotation (rides swap W/H on odd turns; others are unchanged).
	private (int W, int H) RotatedFootprint( BuildCatalogItem item )
		=> item.RidePath != null && Rotation % 2 == 1 ? (item.Height, item.Width) : (item.Width, item.Height);

	// Cursor → world ray (from the camera basis + vertical FOV) intersected with the park ground.
	private bool TryPickGround( out Vector3 hit )
	{
		hit = default;
		var pos = Camera.Position;
		var rot = Camera.Rotation;
		var dir = (rot.Forward
			+ rot.Right * ((2f * Input.Mouse.Position.X / Screen.Width - 1f) * MathF.Tan( Camera.FieldOfView.DegreesToRadians() * 0.5f ) * Screen.Aspect)
			+ rot.Up * ((1f - 2f * Input.Mouse.Position.Y / Screen.Height) * MathF.Tan( Camera.FieldOfView.DegreesToRadians() * 0.5f ))).Normal;
		if ( MathF.Abs( dir.Z ) < 1e-4f )
			return false;

		float groundZ = terrain.Centroid.Z;
		float t = (groundZ - pos.Z) / dir.Z;
		if ( t <= 0f ) return false;
		hit = pos + dir * t;
		groundZ = terrain.SampleHeight( hit.X, hit.Y );
		t = (groundZ - pos.Z) / dir.Z;
		if ( t <= 0f ) return false;
		hit = pos + dir * t;
		return true;
	}
}
