namespace OpenTPW;

/// <summary>
/// Clickable manage buttons (T-038): a bottom bar that exposes the park-economy and selected-staff actions that
/// were keyboard-only — admission fee ±, loans, the gates, and a selected staffer's training / firing / zone. A
/// selected ride or shop has its own window (<see cref="ItemInfoWindow"/>). Each button calls the same
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

	// The bar sits right of the handheld (bottom left) and above the hint strip, until its actions move into the
	// original's windows.
	private const float OffsetX = 228f, OffsetY = 40f;

	// The bar hugs its buttons (a row may be empty).
	private static Rectangle PanelBounds()
	{
		var buttons = Buttons();
		if ( buttons.Count == 0 )
			return new Rectangle( 12f + OffsetX, 14f + OffsetY, 0f, 0f );
		float x0 = buttons.Min( b => b.Rect.X ), y0 = buttons.Min( b => b.Rect.Y );
		float x1 = buttons.Max( b => b.Rect.X + b.Rect.Width ), y1 = buttons.Max( b => b.Rect.Y + b.Rect.Height );
		return new Rectangle( x0 - 4f, y0 - 4f, x1 - x0 + 8f, y1 - y0 + 8f );
	}

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

		// A selected ride or shop is managed in its own window (ItemInfoWindow); the bar keeps the park's controls.
		bool itemWindow = BuildMode.Current?.SelectedRide != null || BuildMode.Current?.SelectedShop != null;
		// Selected-staff row (T-049): dismiss the staffer + bound their patrol zone; training sits on the economy row.
		if ( !itemWindow && BuildMode.Current?.SelectedStaff is { } staff )
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
		else if ( !itemWindow )
		{
			list.Add( new Btn( new Rectangle( 16f, RideY, 130f, BtnH ), Level.GatesOpen ? "CLOSE PARK" : "OPEN PARK", true,
				Level.ToggleGates, Warned: !Level.GatesOpen, Icon: "b_open" ) );
		}
		return list.Select( b => b with { Rect = new Rectangle( b.Rect.X + OffsetX, b.Rect.Y + OffsetY, b.Rect.Width, b.Rect.Height ) } ).ToList();
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
