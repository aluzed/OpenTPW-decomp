using Veldrid;

namespace OpenTPW;

/// <summary>
/// Finance graph overlay (T-049): a centred panel that plots the park's per-month income (green) and
/// expense (red) bars over time from <see cref="ParkFinances.History"/>, with the current balance and
/// cumulative totals. Toggled with F11. Hidden (and free) until opened; only meaningful in a park, so it
/// renders nothing without a live <see cref="ParkFinances"/>.
/// </summary>
internal sealed class FinancePanel : HudPanel
{
	public static FinancePanel? Current { get; private set; }

	public FinancePanel() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool open;
	private bool wasToggleDown;

	/// <summary>Show / hide the finances (the handheld's money button, or F11).</summary>
	public void Toggle() => open = !open;

	private static Texture? bg, axis, income, expense;
	private static Texture Bg => bg ??= Solid( 18, 18, 28, 225 );
	private static Texture Axis => axis ??= Solid( 90, 90, 105, 220 );
	private static Texture Income => income ??= Solid( 80, 185, 90, 240 );
	private static Texture Expense => expense ??= Solid( 205, 70, 60, 240 );

	// Panel + graph geometry in the fixed 1280×720 base space (Y-up, origin bottom-left).
	private static Rectangle PanelBounds() => new( 420f, 220f, 440f, 280f );
	private const float GraphX = 452f, GraphY = 268f, GraphW = 376f, GraphH = 150f;

	/// <summary>True when the panel is open and the cursor is over it (so world tools ignore the click).</summary>
	public bool ContainsMouse() => open && Contains( PanelBounds(), MouseBase() );

	protected override void OnUpdate()
	{
		// F11 toggles the overlay (edge-detected on the raw key, like the other overlays).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.F11 );
		if ( toggle && !wasToggleDown )
			open = !open;
		wasToggleDown = toggle;
	}

	protected override void OnRender()
	{
		if ( !open || ParkFinances.Current is not { } fin )
			return;

		var bounds = PanelBounds();
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( bounds, mat );

		Graphics.DrawText( Font, "PARK FINANCES", bounds.X + 16f, bounds.Y + bounds.Height - 24f, TextAlign.Left, 1.5f );

		var history = fin.History;

		// Bars are scaled against the largest single-month flow so the busiest month fills the graph.
		float peak = 1f;
		foreach ( var s in history )
			peak = MathF.Max( peak, MathF.Max( s.Income, s.Expense ) );

		// Baseline axis.
		mat.Set( "Color", Axis );
		Graphics.Quad( new Rectangle( GraphX, GraphY, GraphW, 1.5f ), mat );

		if ( history.Count > 0 )
		{
			// Each month gets a slot holding a green income bar and a red expense bar side by side.
			float slot = GraphW / history.Count;
			float barW = MathF.Max( 1.5f, slot * 0.4f );
			for ( int i = 0; i < history.Count; i++ )
			{
				var s = history[i];
				float x = GraphX + i * slot;
				float incH = s.Income / peak * GraphH;
				float expH = s.Expense / peak * GraphH;

				mat.Set( "Color", Income );
				Graphics.Quad( new Rectangle( x, GraphY, barW, incH ), mat );
				mat.Set( "Color", Expense );
				Graphics.Quad( new Rectangle( x + barW + 1f, GraphY, barW, expH ), mat );
			}
		}
		else
		{
			Graphics.DrawText( Font, "(collecting data...)", GraphX + 8f, GraphY + GraphH * 0.5f, TextAlign.Left, 1.1f );
		}

		// Legend + readout under the graph.
		Graphics.DrawText( Font, $"BALANCE {fin.Money:0}", bounds.X + 16f, GraphY - 22f, TextAlign.Left, LabelScale );
		Graphics.DrawText( Font, $"IN {fin.RideRevenue + fin.EntryRevenue + fin.FoodRevenue:0} (green)",
			bounds.X + 16f, GraphY - 46f, TextAlign.Left, 1.1f );
		Graphics.DrawText( Font, $"OUT {fin.UpkeepPaid + fin.WagesPaid + fin.BuildSpent:0} (red)",
			bounds.X + 220f, GraphY - 46f, TextAlign.Left, 1.1f );
		Graphics.DrawText( Font, "F11 to close", bounds.X + 16f, bounds.Y + 10f, TextAlign.Left, 1.0f );
	}
}
