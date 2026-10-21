namespace OpenTPW;

/// <summary>
/// The original's staff training budgets (UITEXT 147 "Staff Training Budgets", 148 "Average Skill", 149 "Monthly
/// Budget"): a monthly sum per kind of staff, spent when the month closes on training everyone of that kind who can
/// still improve — shared evenly, each share buying training points at their grade's <c>PoundsPerTrainingPoint</c>
/// (<see cref="StaffGrades"/>). What can't be used (nobody to train, or not enough money in the bank) isn't spent.
/// </summary>
public sealed class StaffTraining
{
	public const float Step = 50f;     // the window's −/+ step
	public const float MaxBudget = 1000f;

	public static StaffTraining? Current { get; set; }

	private readonly Dictionary<StaffRole, float> budgets = new();

	public float BudgetOf( StaffRole role ) => budgets.GetValueOrDefault( role );

	public void SetBudget( StaffRole role, float amount ) => budgets[role] = Math.Clamp( amount, 0f, MaxBudget );

	public IReadOnlyDictionary<StaffRole, float> Budgets => budgets;

	/// <summary>Spend this month's budgets on <paramref name="staff"/> (see <see cref="Plan"/>); returns the total spent.</summary>
	public float Spend( IReadOnlyList<Staff> staff, float available )
	{
		var (points, spent) = Plan( staff.Select( s => (s.Role, s.Grade, s.CanTrain) ).ToList(), available );
		for ( int i = 0; i < staff.Count; i++ )
			if ( points[i] > 0f )
				staff[i].Train( points[i] );
		return spent;
	}

	/// <summary>
	/// The month's training (pure): each role's budget, capped by the <paramref name="available"/> money left, split
	/// evenly between its trainable members, each share bought at that member's grade's cost per point. Returns the
	/// points per member, in order, and the total spent.
	/// </summary>
	public (float[] Points, float Spent) Plan( IReadOnlyList<(StaffRole Role, int Grade, bool CanTrain)> staff, float available )
	{
		var points = new float[staff.Count];
		float spent = 0f;
		foreach ( var role in staff.Where( s => s.CanTrain ).Select( s => s.Role ).Distinct() )
		{
			float budget = MathF.Min( BudgetOf( role ), MathF.Max( 0f, available - spent ) );
			if ( budget <= 0f )
				continue;
			var members = Enumerable.Range( 0, staff.Count ).Where( i => staff[i].Role == role && staff[i].CanTrain ).ToList();
			float share = budget / members.Count;
			foreach ( int i in members )
				points[i] = share / StaffGrades.Current.CostPerTrainingPoint( role, staff[i].Grade );
			spent += budget;
		}
		return (points, spent);
	}

	/// <summary>The average grade of a role's staff, 0–<see cref="StaffGrades.MaxGrade"/> with training as the fraction.</summary>
	public static float AverageSkill( IEnumerable<Staff> staff, StaffRole role )
	{
		var of = staff.Where( s => s.Role == role ).ToList();
		return of.Count == 0 ? 0f : of.Average( s => s.Grade + s.TrainingPoints / StaffGrades.PointsPerGrade );
	}
}
