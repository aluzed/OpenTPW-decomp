namespace OpenTPW;

/// <summary>
/// The clickable build/manage catalog (T-038): a vertical list of buttons down the right side, one per
/// <see cref="BuildMode"/> catalog item, that selects the item to place on click — so every item is
/// mouse-reachable (the old number-key list capped at 1–9). The selected item is highlighted; items the
/// park can't afford are dimmed red. Renders nothing outside build mode (e.g. the plain lobby).
///
/// <para>Shared mouse/hit-test/button helpers live in <see cref="HudPanel"/>. <see cref="BuildMode"/>
/// consults <see cref="HudPanel.PointerOverUi"/> so a click on the panel doesn't also act on the tile
/// behind it.</para>
/// </summary>
internal sealed class BuildPanel : HudPanel
{
	public static BuildPanel? Current { get; private set; }

	// Layout in 1280×720 base space (right-anchored column).
	private const float PanelW = 250f;
	private const float Left = 1280f - PanelW - 12f;
	private const float TopY = 690f;
	private const float RowH = 30f;
	private const float RowGap = 4f;
	private const float FirstRowTop = TopY - 34f;

	private static Texture? bg, normal, selected, unaffordable;
	private static Texture Bg => bg ??= Solid( 18, 18, 28, 190 );
	private static Texture Normal => normal ??= Solid( 58, 58, 92, 225 );
	private static Texture Selected => selected ??= Solid( 80, 165, 90, 240 );
	private static Texture Unaffordable => unaffordable ??= Solid( 95, 48, 48, 215 );

	public BuildPanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	// Rows shrink to keep a long catalog (a theme's rides + sideshows + every stall + staff) on screen.
	private const float BottomMargin = 8f;
	private static float RowStep => RowCount <= 0 ? RowH + RowGap : MathF.Min( RowH + RowGap, (FirstRowTop - BottomMargin) / RowCount );
	private static float RowHeight => RowStep - RowGap;
	private static float RowLabelScale => LabelScale * MathF.Min( 1f, RowHeight / RowH );

	// The button rect (base space) for catalog row i: X,Y = bottom-left.
	private static Rectangle Row( int i )
	{
		var top = FirstRowTop - i * RowStep;
		return new Rectangle( Left, top - RowHeight, PanelW, RowHeight );
	}

	private static int RowCount => BuildMode.Current?.Catalog.Count ?? 0;

	// Whole-panel bounds (title + all rows), used to block world clicks while the cursor is over the UI.
	private static Rectangle PanelBounds()
	{
		var bottom = RowCount > 0 ? Row( RowCount - 1 ).Y - RowGap : TopY;
		return new Rectangle( Left - 6f, bottom, PanelW + 12f, TopY + 24f - bottom );
	}

	/// <summary>True when the window mouse (mapped to base space) is over the panel.</summary>
	public bool ContainsMouse()
		=> BuildMode.Current != null && RowCount > 0 && Contains( PanelBounds(), MouseBase() );

	protected override void OnUpdate()
	{
		if ( BuildMode.Current is not { } build || build.Catalog.Count == 0 || !Input.MouseLeftPressed )
			return;

		var m = MouseBase();
		for ( var i = 0; i < build.Catalog.Count; i++ )
		{
			if ( !Contains( Row( i ), m ) )
				continue;
			build.SelectIndex( i ); // toggles off if already selected
			break;
		}
	}

	protected override void OnRender()
	{
		if ( BuildMode.Current is not { } build || build.Catalog.Count == 0 )
			return;

		var fin = ParkFinances.Current;

		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( PanelBounds(), mat );

		Graphics.DrawText( Font, "BUILD", Left + 8f, TopY + 20f, TextAlign.Left, LabelScale );

		for ( var i = 0; i < build.Catalog.Count; i++ )
		{
			var item = build.Catalog[i];
			bool affordable = fin?.CanAfford( item.Cost ) ?? true;
			var fill = build.Selected == i ? Selected : affordable ? Normal : Unaffordable;
			// Staff show their authentic TPW role name (Cleaner/Scientist/…); the catalog key (item.Name) is
			// unchanged (T-062).
			var label = item.Staff is { } role ? StaffNames.For( role ) : item.Label;
			DrawButton( Row( i ), $"{label}  ${item.Cost:0}", fill, RowLabelScale );
		}
	}
}
