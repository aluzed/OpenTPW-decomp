using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StaffTrainingTests
{
	[TestMethod]
	public void BudgetIsSharedBetweenTrainableStaffOfThatRole()
	{
		var t = new StaffTraining();
		t.SetBudget( StaffRole.Handyman, 100f );
		var staff = new[] { (StaffRole.Handyman, 0, true), (StaffRole.Handyman, 1, true), (StaffRole.Handyman, 4, false), (StaffRole.Guard, 0, true) };
		var (points, spent) = t.Plan( staff, 1000f );
		Assert.AreEqual( 100f, spent );
		Assert.AreEqual( 50f / 5f, points[0], 1e-4f ); // grade 0: 5 a point
		Assert.AreEqual( 50f / 8f, points[1], 1e-4f ); // grade 1: 8 a point
		Assert.AreEqual( 0f, points[2] );              // fully trained
		Assert.AreEqual( 0f, points[3] );              // guards have no budget
	}

	[TestMethod]
	public void SpendingIsCappedByTheBank()
	{
		var t = new StaffTraining();
		t.SetBudget( StaffRole.Mechanic, 400f );
		t.SetBudget( StaffRole.Guard, 400f );
		var (_, spent) = t.Plan( new[] { (StaffRole.Mechanic, 0, true), (StaffRole.Guard, 0, true) }, 500f );
		Assert.AreEqual( 500f, spent );
		Assert.AreEqual( 0f, t.Plan( new[] { (StaffRole.Mechanic, 0, true) }, -20f ).Spent );
	}

	[TestMethod]
	public void NobodyToTrainSpendsNothingAndBudgetsAreClamped()
	{
		var t = new StaffTraining();
		t.SetBudget( StaffRole.Researcher, 5000f );
		Assert.AreEqual( StaffTraining.MaxBudget, t.BudgetOf( StaffRole.Researcher ) );
		t.SetBudget( StaffRole.Researcher, -1f );
		Assert.AreEqual( 0f, t.BudgetOf( StaffRole.Researcher ) );
		Assert.AreEqual( 0f, t.Plan( System.Array.Empty<(StaffRole, int, bool)>(), 1000f ).Spent );
	}
}
