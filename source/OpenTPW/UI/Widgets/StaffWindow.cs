namespace OpenTPW;

/// <summary>
/// The staff window (T-049), in the original's style: clicking a cleaner, mechanic, guard, entertainer or
/// scientist opens the <c>w_park</c> frame with their role, portrait and figures — grade, monthly wage, energy,
/// happiness and training as gauges, their patrol — and the round actions: fire, move, set / grow / shrink the
/// patrol zone, free roam, and train (a tenth of a grade a click).
/// </summary>
internal sealed class StaffWindow : GameWindow
{
	public static StaffWindow? Current { get; private set; }

	public StaffWindow() => Current = this;

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	protected override Rectangle Frame => At( 120f, 20f, 720f, 440f );

	private static readonly Rectangle Preview = At( 165f, 77f, 192f, 191f );
	private static readonly Rectangle Stats = At( 403f, 77f, 328f, 179f );
	private static readonly Rectangle Ok = At( 781f, 381f, 42f, 42f );
	private const float TrainingStep = 10f; // training points bought per click (100 = one grade)

	private static Rectangle Action( float x ) => At( x - 9f, 367f, 84f, 82f );

	// Dev aid: OPENTPW_UI_OPEN=staff selects the park's first staffer once, to look at the window without clicking.
	private bool devOpened = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) != "staff";

	protected override void OnUpdate()
	{
		if ( !devOpened && BuildMode.Current is { } build && Entity.All.OfType<Staff>().FirstOrDefault() is { } first )
		{
			devOpened = true;
			build.SelectStaff( first );
		}
		IsOpen = BuildMode.Current?.SelectedStaff != null;
	}

	protected override void OnRender()
	{
		if ( !IsOpen || BuildMode.Current is not { SelectedStaff: { } staff } build || ParkFinances.Current is not { } fin )
			return;

		DrawFrame( "w_park" );
		DrawHeading( string.IsNullOrEmpty( staff.PersonalName ) ? StaffNames.For( staff.Role ) : $"{StaffNames.For( staff.Role )} - {staff.PersonalName}", 460f, 0f, 44f );
		DrawInset( Preview );
		if ( UiTexture( Portrait( staff.Role ) ) is { } face )
			Fill( At( 201f, 113f, 120f, 120f ), face );
		bool striking = StaffStrikes.Current?.IsStriking( staff.Role ) == true;
		if ( striking )
			Text( Small, "On strike!", 261f, 250f, 11f, LockedInk, TextAlign.Center );

		DrawInset( Stats );
		float y = 87f;
		void Row( string label, string? value, float? gauge = null )
		{
			Text( Small, label, 411f, y, 11f );
			if ( value != null )
				Text( Small, value, 616f, y, 11f );
			if ( gauge is { } g )
				DrawGauge( At( 616f, y + 1f, 110f, 10f ), g );
			y += 24f;
		}
		Row( "Grade", $"{staff.Grade + 1} / {StaffGrades.MaxGrade + 1}" );
		Row( "Monthly wage", $"$ {Staff.MonthlyWage( staff.Role, staff.Grade ):0}" );
		Row( "Energy", null, staff.Energy / 100f );
		Row( "Happiness", null, staff.Morale / 100f );
		Row( "Training", null, staff.TrainingPoints / 100f );
		Row( "Patrol", staff.Zone is { } zone ? $"zone, radius {zone.Radius:0}" : "free roam" );

		if ( RoundButton( Action( 143f ), "b_fire" ) )
			build.FireSelectedStaff();
		if ( RoundButton( Action( 217f ), "b_move", selected: build.IsMovingStaff ) )
			build.BeginMoveSelectedStaff();
		if ( RoundButton( Action( 369f ), "b_route", selected: staff.HasPatrolZone ) )
			build.SetSelectedStaffZoneHere();
		if ( RoundButton( At( 446f, 376f, 30f, 30f ), "b_minus", enabled: staff.HasPatrolZone ) )
			build.AdjustSelectedStaffZone( -1 );
		if ( RoundButton( At( 446f, 410f, 30f, 30f ), "b_plus" ) )
			build.AdjustSelectedStaffZone( +1 );
		if ( RoundButton( Action( 490f ), "b_routeX", enabled: staff.HasPatrolZone ) )
			build.ClearSelectedStaffZone();
		float cost = staff.TrainingCost( TrainingStep );
		if ( RoundButton( Action( 600f ), "b_staffcost", enabled: staff.CanTrain && fin.CanAfford( cost ) ) )
		{
			fin.PayBuild( cost );
			staff.Train( TrainingStep );
		}
		Text( Small, staff.CanTrain ? $"$ {cost:0}" : "max", 633f, 358f, 11f, default, TextAlign.Center );

		if ( RoundButton( Ok, "b_okay" ) )
			build.ClearSelection();
	}

	// The role's portrait in ui.wad (the hire window's h_* pictures).
	private static string Portrait( StaffRole role ) => role switch
	{
		StaffRole.Handyman => "h_handy", StaffRole.Mechanic => "h_mech", StaffRole.Guard => "h_guard",
		StaffRole.Researcher => "h_resrcher", _ => "h_enter",
	};
}
