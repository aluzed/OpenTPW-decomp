namespace OpenTPW;

/// <summary>
/// The original's staff training budgets (UITEXT 147–149), in the <c>w_park</c> frame: a row per kind of staff with
/// their portrait, how many there are, their average skill as a gauge, and the monthly budget with −/+
/// (<see cref="StaffTraining"/>, spent as each month closes). Opened from the staff list.
/// </summary>
internal sealed class TrainingWindow : GameWindow
{
	public static TrainingWindow? Current { get; private set; }

	public TrainingWindow()
	{
		Current = this;
		IsOpen = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "training"; // dev aid
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	public void Toggle() => IsOpen = !IsOpen;

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle List = At( 140f, 82f, 620f, 330f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private const float FirstRow = 124f, RowStep = 56f;

	private static readonly StaffRole[] Roles =
		{ StaffRole.Handyman, StaffRole.Mechanic, StaffRole.Entertainer, StaffRole.Guard, StaffRole.Researcher };

	protected override void OnRender()
	{
		if ( !IsOpen || StaffTraining.Current is not { } training )
			return;

		DrawFrame( "w_park" );
		DrawHeading( Localization.Get( UIStrings.StaffTrainingBudgets, "Staff Training Budgets" ), 470f, 0f, 46f );
		DrawInset( List );
		Text( Small, Localization.Get( UIStrings.AvgSkill, "Average Skill" ), 470f, 92f, 11f, TitleInk, TextAlign.Center );
		Text( Small, Localization.Get( UIStrings.MonthlyBudget, "Monthly Budget" ), 668f, 92f, 11f, TitleInk, TextAlign.Center );

		var staff = Entity.All.OfType<Staff>().ToList();
		for ( int i = 0; i < Roles.Length; i++ )
		{
			var role = Roles[i];
			float top = FirstRow + i * RowStep;
			if ( i > 0 )
				Fill( At( 146f, top - 8f, 608f, 1f ), SolidOf( 24, 54, 140 ) );
			if ( UiTexture( StaffWindow.Portrait( role ) ) is { } face )
				Fill( At( 152f, top, 44f, 44f ), face );
			int count = staff.Count( s => s.Role == role );
			Text( Small, StaffNames.For( role ), 206f, top + 6f, 13f, TitleInk );
			Text( Small, $"{count} employed", 206f, top + 26f, 11f, count == 0 ? new( 0.6f, 0.6f, 0.6f ) : default );

			float skill = StaffTraining.AverageSkill( staff, role );
			DrawGauge( At( 400f, top + 14f, 140f, 12f ), skill / StaffGrades.MaxGrade );
			Text( Small, count == 0 ? "-" : $"{skill + 1f:0.0}", 470f, top + 30f, 11f, default, TextAlign.Center );

			float budget = training.BudgetOf( role );
			if ( RoundButton( At( 596f, top + 8f, 28f, 28f ), "b_minus", enabled: budget > 0f ) )
				training.SetBudget( role, budget - StaffTraining.Step );
			Text( Small, $"$ {budget:0}", 668f, top + 14f, 13f, default, TextAlign.Center );
			if ( RoundButton( At( 712f, top + 8f, 28f, 28f ), "b_plus", enabled: budget < StaffTraining.MaxBudget ) )
				training.SetBudget( role, budget + StaffTraining.Step );
		}

		float total = Roles.Sum( training.BudgetOf );
		Text( Small, $"Total $ {total:0} a month", 740f, 422f, 11f, default, TextAlign.Right );
		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}
}
