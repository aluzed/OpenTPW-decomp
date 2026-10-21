namespace OpenTPW;

/// <summary>
/// Clickable manage buttons (T-038): a bottom-left bar that exposes the park-economy and selected-ride
/// actions that were keyboard-only — admission fee ±, take/repay loan, and (when a ride is selected via
/// the Default tool) its ticket price ± and research/upgrade. Each button calls the same
/// <see cref="ParkFinances"/> / <see cref="Ride"/> methods the keyboard shortcuts do. Renders nothing
/// outside a park. Shares mouse/hit-test/button helpers with <see cref="HudPanel"/>.
/// </summary>
internal sealed class ManagePanel : HudPanel
{
	public static ManagePanel? Current { get; private set; }

	// Bottom-left bar in 1280×720 base space. Two rows: park economy (top) + selected ride (bottom).
	private const float EconY = 52f;  // bottom of the economy row buttons
	private const float RideY = 22f;  // bottom of the ride row buttons
	private const float BtnH = 26f;
	private const float TrainingStep = 10f; // training percent bought per click (100 = one grade)

	// The game's own words where UITEXT.str has them ("Price", "Available Loans"), in the selected language.
	private static string PriceLabel => Localization.Get( UIStrings.Price, "Price" ).ToUpperInvariant();
	private static string LoansLabel => Localization.Get( UIStrings.GraphAvailableLoans, "Loans" );

	private static Texture? bg, normal, disabled, warn, research;
	private static Texture Bg => bg ??= Solid( 18, 18, 28, 190 );
	private static Texture Normal => normal ??= Solid( 58, 58, 92, 230 );
	private static Texture Disabled => disabled ??= Solid( 45, 45, 55, 170 );
	private static Texture Warn => warn ??= Solid( 150, 110, 50, 235 );
	private static Texture Research => research ??= Solid( 80, 140, 210, 235 );

	// Progress ≥ 0 renders the button as a progress bar (used for the live research gauge).
	private readonly record struct Btn( Rectangle Rect, string Label, bool Enabled, System.Action Act, bool Warned = false, float Progress = -1f, string? Icon = null );

	public ManagePanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private static Rectangle PanelBounds() => new( 12f, 14f, 460f, 76f );

	public bool ContainsMouse()
		=> BuildMode.Current != null && ParkFinances.Current != null && Contains( PanelBounds(), MouseBase() );

	// Build the live button list (shared by render + click handling so they never drift).
	private static List<Btn> Buttons()
	{
		var list = new List<Btn>();
		if ( ParkFinances.Current is not { } fin )
			return list;

		// Park economy row.
		list.Add( new Btn( new Rectangle( 16f, EconY, 64f, BtnH ), $"FEE", fin.EntryFee > 0,
			() => fin.EntryFee = System.Math.Max( 0f, fin.EntryFee - 1f ), Icon: "b_minus" ) );
		list.Add( new Btn( new Rectangle( 84f, EconY, 64f, BtnH ), $"{fin.EntryFee:0}", true,
			() => fin.EntryFee += 1f, Icon: "b_plus" ) );
		// The bank (T-042): the level's loan offers live in the LoanPanel list (they don't fit as one button each).
		list.Add( new Btn( new Rectangle( 152f, EconY, 160f, BtnH ),
			fin.Debt > 0 ? $"{LoansLabel} (${fin.Debt / 1000f:0.0}k)" : LoansLabel, true,
			() => LoanPanel.Current?.Toggle(), Warned: fin.Debt > 0, Icon: "b_loan" ) );

		// Selected-ride row (only when a ride is selected via the Default tool).
		if ( BuildMode.Current?.SelectedRide is { } ride )
		{
			// Only sideshows charge (the price of a game); rides are free in TPW.
			if ( ride.IsSideshow )
			{
				list.Add( new Btn( new Rectangle( 16f, RideY, 64f, BtnH ), PriceLabel, ride.TicketPrice > 1f,
					() => ride.TicketPrice = System.Math.Max( 1f, ride.TicketPrice - 1f ), Icon: "b_minus" ) );
				list.Add( new Btn( new Rectangle( 84f, RideY, 88f, BtnH ), $"{ride.TicketPrice:0}", true,
					() => ride.TicketPrice += 1f, Icon: "b_plus" ) );
			}
			list.Add( ResearchButton( fin, ride ) );
			list.Add( new Btn( new Rectangle( 350f, RideY, 116f, BtnH ),
				$"SELL ${ride.BuildCost * Ride.SellRefundFraction:0}", true,
				() => BuildMode.Current?.SellSelected(), Warned: true, Icon: "b_delete_login" ) );
		}
		// Selected-shop row: a food/drink stall's price is adjustable (a toilet is free); plus sell.
		else if ( BuildMode.Current?.SelectedShop is { } shop )
		{
			if ( !shop.IsFree )
			{
				list.Add( new Btn( new Rectangle( 16f, RideY, 64f, BtnH ), PriceLabel, shop.Price > 1f,
					() => shop.Price = System.Math.Max( 1f, shop.Price - 1f ), Icon: "b_minus" ) );
				list.Add( new Btn( new Rectangle( 84f, RideY, 88f, BtnH ), $"{shop.Price:0}", true,
					() => shop.Price += 1f, Icon: "b_plus" ) );
			}
			list.Add( new Btn( new Rectangle( 192f, RideY, 230f, BtnH ),
				$"SELL {shop.Name} ${shop.BuildCost * Ride.SellRefundFraction:0}", true,
				() => BuildMode.Current?.SellSelected(), Warned: true, Icon: "b_delete_login" ) );
		}
		// Selected-staff row (T-049): dismiss the staffer + bound their patrol zone; training sits on the economy row.
		else if ( BuildMode.Current?.SelectedStaff is { } staff )
		{
			float cost = staff.TrainingCost( TrainingStep );
			list.Add( new Btn( new Rectangle( 316f, EconY, 140f, BtnH ),
				staff.CanTrain ? $"TRAIN ${cost:0}" : "MAX GRADE", staff.CanTrain && fin.CanAfford( cost ),
				() => { fin.PayBuild( cost ); staff.Train( TrainingStep ); }, Icon: "b_staffcost" ) );
			bool zoned = staff.HasPatrolZone;
			float radius = staff.Zone?.Radius ?? 0f;
			bool moving = BuildMode.Current?.IsMovingStaff == true;
			list.Add( new Btn( new Rectangle( 16f, RideY, 52f, BtnH ), "FIRE", true,
				() => BuildMode.Current?.FireSelectedStaff(), Warned: true, Icon: "b_fire" ) );
			// MOVE picks the staffer up; while armed it reads CLICK and the next tile click drops them (T-043).
			list.Add( new Btn( new Rectangle( 72f, RideY, 70f, BtnH ), moving ? "DROP" : "MOVE", true,
				() => BuildMode.Current?.BeginMoveSelectedStaff(), Icon: "b_move" ) );
			list.Add( new Btn( new Rectangle( 146f, RideY, 48f, BtnH ), "ZONE-", zoned,
				() => BuildMode.Current?.AdjustSelectedStaffZone( -1 ) ) );
			list.Add( new Btn( new Rectangle( 198f, RideY, 116f, BtnH ),
				zoned ? $"ZONE r{radius:0}" : "SET ZONE", true,
				() => BuildMode.Current?.SetSelectedStaffZoneHere() ) );
			list.Add( new Btn( new Rectangle( 318f, RideY, 48f, BtnH ), "ZONE+", true,
				() => BuildMode.Current?.AdjustSelectedStaffZone( +1 ) ) );
			list.Add( new Btn( new Rectangle( 370f, RideY, 84f, BtnH ), "FREE ROAM", zoned,
				() => BuildMode.Current?.ClearSelectedStaffZone() ) );
		}
		// Nothing selected: the park gates (highlighted while closed — visitors only come once they're open).
		else
		{
			list.Add( new Btn( new Rectangle( 16f, RideY, 130f, BtnH ), Level.GatesOpen ? "CLOSE PARK" : "OPEN PARK", true,
				Level.ToggleGates, Warned: !Level.GatesOpen, Icon: "b_open" ) );
		}
		return list;
	}

	// The research/upgrade button mirrors the R/U keyboard logic + states.
	private static Btn ResearchButton( ParkFinances fin, Ride ride )
	{
		var rect = new Rectangle( 176f, RideY, 170f, BtnH );
		if ( ride.IsResearching )
			return ride.IsResearchActive
				? new Btn( rect, $"RESEARCHING {ride.ResearchFraction * 100f:0}%", false, () => { }, Progress: ride.ResearchFraction )
				: new Btn( rect, $"QUEUED #{ride.ResearchQueuePosition}", false, () => { } ); // waiting behind another ride
		if ( ride.NextResearched )
			return new Btn( rect, $"UPGRADE ${ride.NextUpgradeCost:0}", fin.CanAfford( ride.NextUpgradeCost ),
				() => { fin.PayBuild( ride.NextUpgradeCost ); ride.ApplyUpgrade(); }, Warned: true, Icon: "b_upgrade" );
		if ( ride.HasNextLevel )
			return new Btn( rect, $"RESEARCH ${ride.NextResearchCost:0}", fin.CanAfford( ride.NextResearchCost ),
				() => { fin.PayBuild( ride.NextResearchCost ); ride.StartResearch(); }, Icon: "b_resrch" );
		return new Btn( rect, "MAX LEVEL", false, () => { } );
	}

	protected override void OnUpdate()
	{
		if ( BuildMode.Current == null || ParkFinances.Current == null || !Input.MouseLeftPressed )
			return;

		var m = MouseBase();
		foreach ( var b in Buttons() )
		{
			if ( !b.Enabled || !Contains( b.Rect, m ) )
				continue;
			b.Act();
			break;
		}
	}

	protected override void OnRender()
	{
		if ( BuildMode.Current == null || ParkFinances.Current == null )
			return;

		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( PanelBounds(), mat );

		foreach ( var b in Buttons() )
		{
			if ( b.Progress >= 0f )
			{
				// Live progress gauge (research) — fill + label, not a flat button.
				DrawBar( b.Rect, b.Progress, Research, Disabled );
				Graphics.DrawText( Font, b.Label, b.Rect.X + 8f, b.Rect.Y + b.Rect.Height - 7f, TextAlign.Left, LabelScale );
			}
			else
			{
				DrawIconButton( b.Rect, b.Icon, b.Label, !b.Enabled ? Disabled : b.Warned ? Warn : Normal );
			}
		}
	}
}
