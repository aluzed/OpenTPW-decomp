using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StaffWageTests
{
	[TestMethod]
	public void WagesFollowTheAuthoredPayRatios()
	{
		float handyman = Staff.WagePerSecond( StaffRole.Handyman );
		Assert.AreEqual( 3f, Staff.WagePerSecond( StaffRole.Mechanic ) / handyman, 1e-4f, "mechanic 30 vs handyman 10" );
		Assert.AreEqual( 3.5f, Staff.WagePerSecond( StaffRole.Researcher ) / handyman, 1e-4f, "researcher 35" );
	}

	[TestMethod]
	public void TheMonthlyWageIsBaseWageTimesThePayMultiplier()
	{
		Assert.AreEqual( 50f, Staff.MonthlyWage( StaffRole.Handyman, 1 ), 1e-4f, "BaseWage 5 × 10" );
		Assert.AreEqual( 420f, Staff.MonthlyWage( StaffRole.Researcher, 4 ), 1e-4f, "BaseWage 12 × 35" );
		// Paid as it accrues over the original's month (T-076).
		Assert.AreEqual( 50f / GameClock.SecondsPerMonth, Staff.WagePerSecond( StaffRole.Handyman, 1 ), 1e-5f );
	}
}
